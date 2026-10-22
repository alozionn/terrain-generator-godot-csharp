using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Level;
using Terrain.Core.Shadows;

namespace Terrain.Surface;

/// <summary>
/// The LOD meshes for each foliage mesh: the MeshInstance3D meshes of a res:// scene in order, or generated ones for
/// "procedural:*". Multi-part stand-ins are one surface coloured per vertex; grass clumps get three LODs.
/// </summary>
internal static class FoliageMeshes
{
    static readonly Color Bark = new(0.42f, 0.29f, 0.2f);
    static Shader _grassShader;
    static Shader _foliageShader;

    /// <summary>Thinning distance scale when looking straight down (the shaders' top_down).</summary>
    public const float TopDown = 0.6f;

    public static bool IsClump(FoliageMeshDefinition def) => def.Scene is "procedural:grass" or "procedural:flowers";

    /// <summary>Where instances thin out, and where the last ones fade (true distance).</summary>
    public static (Vector2 thin, Vector2 fade) Thinning(FoliageMeshDefinition def)
    {
        var thin = def.Thin.Length >= 2 ? new Vector2(def.Thin[0], def.Thin[1]) : new Vector2(def.Range * 0.3f, def.Range * 0.9f);
        var fade = def.ThinMin > 0 ? new Vector2(MathF.Max(thin.Y, def.Range * 0.8f), def.Range * 0.97f) : new Vector2(1e6f, 2e6f);
        return (thin, fade);
    }

    public static Vector2 BladeThinning(FoliageMeshDefinition def) =>
        def.BladeThin.Length >= 2 ? new Vector2(def.BladeThin[0], def.BladeThin[1]) : new Vector2(def.LodDistance * 0.4f, def.Range * 0.3f);

    /// <summary>
    /// Distance where each LOD stops drawing: LOD i ends at LodDistance x 3^i, the last at Range. Clumps switch to
    /// their light mesh once blade thinning is complete even when looking straight down, plus the reach of a chunk
    /// (visibility ranges are measured to its centre), so the switch never shows.
    /// </summary>
    public static float[] LodEnds(FoliageMeshDefinition def, int count, float chunkReach = 23f)
    {
        var ends = new float[count];
        for (int i = 0; i < count; i++)
            ends[i] = i == count - 1 ? def.Range : MathF.Min(def.Range, def.LodDistance * MathF.Pow(3, i));
        if (IsClump(def) && count == 2) ends[0] = MathF.Min(def.Range, BladeThinning(def).Y / TopDown + chunkReach);
        return ends;
    }

    public static List<Mesh> Lods(FoliageMeshDefinition def)
    {
        if (!def.Scene.StartsWith("procedural:")) return SceneMeshes(def);

        var color = ToColor(def.Color);
        var lods = new List<Mesh>();
        switch (def.Scene["procedural:".Length..])
        {
            case "grass":
            case "flowers":
                bool flowers = def.Scene.EndsWith("flowers");
                var material = GrassMaterial(def);
                // the shader thins blades and clumps by each clump's own distance, a smooth circle around the camera;
                // the light mesh holds exactly the blades still drawn once thinning is done, so swapping never shows
                lods.Add(Clump(def, flowers, material, far: false));
                if (BladeThinning(def).Y / TopDown < def.Range) lods.Add(Clump(def, flowers, material, far: true));
                break;
            case "tree_round":
                lods.Add(Parts(
                    (new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = 3f, RadialSegments = 7 }, Bark, new Vector3(0, 1.5f, 0)),
                    (new SphereMesh { Radius = 1.7f, Height = 3f, RadialSegments = 10, Rings = 6 }, color, new Vector3(0, 3.8f, 0)),
                    (new SphereMesh { Radius = 1.1f, Height = 2f, RadialSegments = 8, Rings = 5 }, color.Lightened(0.1f), new Vector3(0.7f, 4.6f, 0.3f))));
                break;
            case "tree_pine":
                lods.Add(Parts(
                    (new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.22f, Height = 2f, RadialSegments = 6 }, Bark, new Vector3(0, 1f, 0)),
                    (new CylinderMesh { TopRadius = 0f, BottomRadius = 1.5f, Height = 2.6f, RadialSegments = 8 }, color, new Vector3(0, 2.6f, 0)),
                    (new CylinderMesh { TopRadius = 0f, BottomRadius = 1.1f, Height = 2.2f, RadialSegments = 8 }, color.Lightened(0.08f), new Vector3(0, 3.9f, 0))));
                break;
            case "bush":
                lods.Add(Parts((new SphereMesh { Radius = 0.8f, Height = 1.1f, RadialSegments = 9, Rings = 5 }, color, new Vector3(0, 0.4f, 0))));
                break;
            case "rock":
                var rock = new SphereMesh { Radius = 0.8f, Height = 1.1f, RadialSegments = 6, Rings = 3 };
                lods.Add(Merge((rock, color, new Transform3D(Basis.Identity.Scaled(new Vector3(1.2f, 0.7f, 1f)), new Vector3(0, 0.15f, 0)))));
                break;
            default:
                GD.PushError($"[Terrain] unknown procedural mesh '{def.Scene}'");
                break;
        }

        return lods;
    }

    static List<Mesh> SceneMeshes(FoliageMeshDefinition def)
    {
        var lods = new List<Mesh>();
        if (!ResourceLoader.Exists(def.Scene))
        {
            GD.PushError($"[Terrain] missing foliage scene {def.Scene}");
            return lods;
        }
        var root = GD.Load<PackedScene>(def.Scene).Instantiate();
        void Collect(Node n)
        {
            if (n is MeshInstance3D { Mesh: { } m }) lods.Add(def.Wind > 0 || def.Fade ? WithWind(def, m) : m);
            foreach (var c in n.GetChildren()) Collect(c);
        }
        Collect(root);
        root.Free();
        return lods;
    }

    /// <summary>
    /// Shadow proxies for a mesh with <see cref="FoliageMeshDefinition.BakedShadow"/>: a squashed sphere for the
    /// canopy, measured from the leaf surfaces' bounds (or given as "canopy"), and a thin one for the trunk when the
    /// model has bark. Empty for meshes that keep realtime shadows.
    /// </summary>
    public static ShadowProxy[] Proxies(FoliageMeshDefinition def)
    {
        if (!def.BakedShadow) return Array.Empty<ShadowProxy>();
        var proxies = new List<ShadowProxy>();
        var leaves = new List<Vector3>();
        float barkHeight = 0;
        if (!def.Scene.StartsWith("procedural:") && ResourceLoader.Exists(def.Scene))
        {
            var root = GD.Load<PackedScene>(def.Scene).Instantiate();
            void Collect(Node n)
            {
                if (n is MeshInstance3D { Mesh: { } m })
                    for (int s = 0; s < m.GetSurfaceCount(); s++)
                    {
                        var verts = m.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        if (IsLeaf(m.SurfaceGetMaterial(s) as BaseMaterial3D)) leaves.AddRange(verts);
                        else foreach (var v in verts) barkHeight = MathF.Max(barkHeight, v.Y);
                    }
                foreach (var c in n.GetChildren()) Collect(c);
            }
            Collect(root);
            root.Free();
        }

        float density = def.ShadowDensity;
        if (def.Canopy.Length >= 3)
            proxies.Add(new ShadowProxy(new System.Numerics.Vector3(0, def.Canopy[2], 0), new System.Numerics.Vector3(def.Canopy[0], def.Canopy[1], def.Canopy[0]), density));
        else if (leaves.Count > 0)
            // fitted to where the leaves are, not their bounds: a few lobes give the shadow the canopy's outline
            proxies.AddRange(CanopyFit.Lobes(leaves.ConvertAll(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)), def.Lobes, density, def.Dapple));
        if (barkHeight > 0 && proxies.Count > 0)
        {
            // trunk: from the ground up to the lowest lobe's middle, as thick as a few percent of the tree is tall
            float top = proxies.Min(p => p.Centre.Y);
            float radius = MathF.Max(0.12f, barkHeight * 0.03f);
            if (top > 0.4f) proxies.Add(new ShadowProxy(new System.Numerics.Vector3(0, top * 0.5f, 0), new System.Numerics.Vector3(radius, top * 0.5f, radius), 6f));
        }
        return proxies.ToArray();
    }

    static bool IsLeaf(BaseMaterial3D src)
    {
        string name = (src?.ResourceName ?? "").ToLowerInvariant();
        bool cutout = src?.Transparency is BaseMaterial3D.TransparencyEnum.AlphaScissor or BaseMaterial3D.TransparencyEnum.Alpha;
        // the pack cuts out its bark too (alpha mask on Bark_*), so wood is recognised by name before the cut-out test
        if (name.Contains("bark") || name.Contains("trunk") || name.Contains("wood")) return false;
        return cutout || name.Contains("lea") || name.Contains("grass") || name.Contains("flower");
    }

    /// <summary>
    /// A copy of an imported mesh whose surfaces use foliage.gdshader, carrying over each material's texture, colour,
    /// alpha cut-out and normal map. Leaf surfaces (cut-out, or named leaf, leaves, grass or flower) also flutter and get
    /// soft canopy normals; bark only sways.
    /// </summary>
    static Mesh WithWind(FoliageMeshDefinition def, Mesh source)
    {
        _foliageShader ??= GD.Load<Shader>("res://terrain/surface/foliage.gdshader");
        var mesh = (Mesh)source.Duplicate();
        float height = MathF.Max(0.1f, source.GetAabb().End.Y);
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var src = source.SurfaceGetMaterial(s) as BaseMaterial3D;
            string name = (src?.ResourceName ?? "").ToLowerInvariant();
            bool cutout = src?.Transparency is BaseMaterial3D.TransparencyEnum.AlphaScissor or BaseMaterial3D.TransparencyEnum.Alpha;
            bool leaf = IsLeaf(src);
            var m = new ShaderMaterial { Shader = _foliageShader };
            if (src != null)
            {
                m.SetShaderParameter("albedo_texture", src.AlbedoTexture);
                m.SetShaderParameter("albedo_color", src.AlbedoColor);
                m.SetShaderParameter("alpha_cut", cutout ? MathF.Max(0.05f, src.AlphaScissorThreshold) : 0f);
                m.SetShaderParameter("use_normal_map", src.NormalEnabled && src.NormalTexture != null);
                m.SetShaderParameter("normal_texture", src.NormalTexture);
            }
            // stiff models (trees) keep the trunk nearly still; leaves give a little more than the wood they hang on
            m.SetShaderParameter("sway", def.Wind * (1f - def.Stiffness * (leaf ? 0.6f : 1f)));
            m.SetShaderParameter("model_height", height);
            m.SetShaderParameter("flutter", leaf ? def.Wind * 0.12f : 0f);
            m.SetShaderParameter("leaf_normals", leaf ? def.LeafNormals : 0f);
            m.SetShaderParameter("canopy_height", height * 0.6f);
            if (def.Recolor.Length == 2 && (name.Contains("lea") || name.Contains("grass")))
            {
                m.SetShaderParameter("recolor", true);
                m.SetShaderParameter("root_color", ToColor(def.Recolor[0]));
                m.SetShaderParameter("tip_color", ToColor(def.Recolor[1]));
            }
            if (def.Fade) SetThinning(m, def);
            // things that cast a baked shadow stand above it; ground cover sits in it
            m.SetShaderParameter("receive_bake", def.ReceiveBake && !def.BakedShadow);
            // without realtime shadows a canopy would be lit evenly through; darken its inside and underside instead
            m.SetShaderParameter("canopy_ao", def.BakedShadow && leaf ? 0.4f : 0f);
            mesh.SurfaceSetMaterial(s, m);
        }
        return mesh;
    }

    /// <summary>
    /// A clump of curved, tapering blades. UV.y is height along the blade, UV.x marks flower heads (2), UV2 is the
    /// vertex's sideways offset from the blade's centre line (so the shader can widen blades), vertex colour gives each
    /// blade its own brightness and, in alpha, its distance cut-off. Blades lean outward from the clump centre.
    /// The far mesh keeps only the blades that survive thinning, with two segments instead of three.
    /// </summary>
    static ArrayMesh Clump(FoliageMeshDefinition def, bool flowers, Material material, bool far)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        uint seed = Seeds.Derive(1, def.Id + def.Blades);
        float Rand(int i, int k) => (Seeds.Hash(i, k, seed) >> 8) / 16777216f;
        int segs = far ? 2 : 3;

        for (int b = 0; b < def.Blades; b++)
        {
            float cutoff = Rand(b, 9);
            if (far && cutoff >= def.BladeMin) continue;
            float r = def.Radius * MathF.Sqrt(Rand(b, 0)), a = Rand(b, 1) * MathF.Tau;
            var basePos = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            float h = Mathf.Lerp(def.BladeHeight[0], def.BladeHeight[^1], Rand(b, 2));
            float facing = Rand(b, 3) * MathF.Tau;
            var side = new Vector3(MathF.Cos(facing), 0, MathF.Sin(facing));
            // lean away from the centre, plus some randomness
            var lean = (new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.6f + new Vector3(Rand(b, 4) - 0.5f, 0, Rand(b, 5) - 0.5f)).Normalized();
            float curve = 0.45f + 0.4f * Rand(b, 6); // strong lean so blades still read from a high camera
            float w = def.BladeWidth * (0.85f + 0.4f * Rand(b, 7));
            float brightness = 0.9f + 0.18f * Rand(b, 8);
            var shade = new Color(brightness, brightness, brightness, cutoff);
            var normal = lean.Cross(side).Normalized();
            if (normal.Y < 0) normal = -normal;

            Vector3 At(float t) => basePos + Vector3.Up * h * t + lean * h * curve * t * t;
            float Width(float t) => w * MathF.Pow(1 - t, 0.7f);
            Vertex Edge(float t, float sign)
            {
                var o = side * Width(t) * sign;
                return new Vertex(At(t) + o, t, new Vector2(o.X, o.Z));
            }

            for (int s = 0; s < segs; s++)
            {
                float t0 = (float)s / segs, t1 = (float)(s + 1) / segs;
                if (s == segs - 1)
                    Tri(st, Edge(t0, -1), Edge(t0, 1), new Vertex(At(1), 1f, Vector2.Zero), normal, shade, 0);
                else
                {
                    Tri(st, Edge(t0, -1), Edge(t0, 1), Edge(t1, 1), normal, shade, 0);
                    Tri(st, Edge(t0, -1), Edge(t1, 1), Edge(t1, -1), normal, shade, 0);
                }
            }

            // flowers: some blades carry a small five-petal head at their tip
            if (flowers && Rand(b, 10) < def.Heads)
            {
                var tip = At(1f);
                float pr = 0.08f;
                for (int p = 0; p < 5; p++)
                {
                    float pa = p * MathF.Tau / 5 + facing, pb = pa + MathF.Tau / 10;
                    var e0 = new Vector3(MathF.Cos(pa), 0.15f, MathF.Sin(pa)) * pr;
                    var e1 = new Vector3(MathF.Cos(pb), 0.15f, MathF.Sin(pb)) * pr * 0.6f;
                    var e2 = new Vector3(MathF.Cos(pa - MathF.Tau / 10), 0.15f, MathF.Sin(pa - MathF.Tau / 10)) * pr * 0.6f;
                    // petals widen with the blades, so heads stay visible as blades thin out
                    Vertex Petal(Vector3 e) => new(tip + e, 1f, new Vector2(e.X, e.Z));
                    var centre = new Vertex(tip, 1f, Vector2.Zero);
                    Tri(st, centre, Petal(e2), Petal(e0), Vector3.Up, shade, 2);
                    Tri(st, centre, Petal(e0), Petal(e1), Vector3.Up, shade, 2);
                }
            }
        }
        st.SetMaterial(material);
        return st.Commit();
    }

    readonly record struct Vertex(Vector3 P, float T, Vector2 Offset);

    static void Tri(SurfaceTool st, Vertex a, Vertex b, Vertex c, Vector3 n, Color shade, float mark)
    {
        foreach (var v in new[] { a, b, c })
        {
            st.SetNormal(n);
            st.SetColor(shade);
            st.SetUV(new Vector2(mark, v.T));
            st.SetUV2(v.Offset);
            st.AddVertex(v.P);
        }
    }

    static ShaderMaterial GrassMaterial(FoliageMeshDefinition def)
    {
        _grassShader ??= GD.Load<Shader>("res://terrain/surface/grass.gdshader");
        var m = new ShaderMaterial { Shader = _grassShader };
        m.SetShaderParameter("root_color", ToColor(def.RootColor));
        m.SetShaderParameter("tip_color", ToColor(def.Color));
        m.SetShaderParameter("head_color", ToColor(def.HeadColor));
        SetThinning(m, def);
        m.SetShaderParameter("blade_thin", BladeThinning(def));
        m.SetShaderParameter("blade_min", def.BladeMin);
        m.SetShaderParameter("blade_widen", def.BladeWiden);
        return m;
    }

    /// <summary>Distance thinning shared by grass.gdshader and foliage.gdshader (foliage_common.gdshaderinc).</summary>
    static void SetThinning(ShaderMaterial m, FoliageMeshDefinition def)
    {
        var (thin, fade) = Thinning(def);
        m.SetShaderParameter("thin", thin);
        m.SetShaderParameter("thin_min", def.ThinMin);
        m.SetShaderParameter("fade", fade);
        m.SetShaderParameter("compensate", def.Compensate);
        m.SetShaderParameter("top_down", TopDown);
    }

    static ArrayMesh Parts(params (PrimitiveMesh shape, Color color, Vector3 position)[] parts)
    {
        var placed = new (PrimitiveMesh, Color, Transform3D)[parts.Length];
        for (int i = 0; i < parts.Length; i++) placed[i] = (parts[i].shape, parts[i].color, new Transform3D(Basis.Identity, parts[i].position));
        return Merge(placed);
    }

    /// <summary>All parts in one surface, coloured per vertex, so a stand-in costs one draw call per pass instead of one per part.</summary>
    static ArrayMesh Merge(params (PrimitiveMesh shape, Color color, Transform3D xf)[] parts)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (shape, color, xf) in parts)
        {
            var arrays = shape.GetMeshArrays();
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            foreach (int i in indices)
            {
                st.SetColor(color);
                st.SetNormal((xf.Basis * normals[i]).Normalized());
                st.AddVertex(xf * verts[i]);
            }
        }
        st.Index();
        st.SetMaterial(new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.9f });
        return st.Commit();
    }

    static Color ToColor(string hex)
    {
        var c = LevelJson.ParseColor(hex);
        return new Color(c.X, c.Y, c.Z);
    }
}
