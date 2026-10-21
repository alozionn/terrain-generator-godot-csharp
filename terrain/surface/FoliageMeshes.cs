using System;
using System.Collections.Generic;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Level;

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

    /// <summary>Distance where each LOD stops drawing: LOD i ends at LodDistance x 3^i, the last at Range. Grass clumps have a single LOD.</summary>
    public static float[] LodEnds(FoliageMeshDefinition def, int count)
    {
        var ends = new float[count];
        for (int i = 0; i < count; i++)
            ends[i] = i == count - 1 ? def.Range : MathF.Min(def.Range, def.LodDistance * MathF.Pow(3, i));
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
                var material = GrassMaterial(def, flowers);
                // one mesh only: the shader thins blades and clumps by each clump's own distance, a smooth circle
                // around the camera; swapping meshes per chunk showed as squares of different density
                lods.Add(Clump(def, def.Blades, 1f, flowers, material));
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
            bool leaf = cutout || name.Contains("lea") || name.Contains("grass") || name.Contains("flower");
            var m = new ShaderMaterial { Shader = _foliageShader };
            if (src != null)
            {
                m.SetShaderParameter("albedo_texture", src.AlbedoTexture);
                m.SetShaderParameter("albedo_color", src.AlbedoColor);
                m.SetShaderParameter("alpha_cut", cutout ? MathF.Max(0.05f, src.AlphaScissorThreshold) : 0f);
                m.SetShaderParameter("use_normal_map", src.NormalEnabled && src.NormalTexture != null);
                m.SetShaderParameter("normal_texture", src.NormalTexture);
            }
            m.SetShaderParameter("sway", def.Wind);
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
            if (def.Fade)
            {
                m.SetShaderParameter("fade_start", def.Range * 0.3f);
                m.SetShaderParameter("fade_end", def.Range * 0.9f);
            }
            mesh.SurfaceSetMaterial(s, m);
        }
        return mesh;
    }

    /// <summary>
    /// A clump of curved, tapering blades (three segments each). UV.y is height along the blade, UV.x marks flower
    /// heads (2), vertex colour gives each blade its own brightness. Blades lean outward from the clump centre.
    /// </summary>
    static ArrayMesh Clump(FoliageMeshDefinition def, int blades, float widthScale, bool flowers, Material material)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        uint seed = Seeds.Derive(1, def.Id + blades);
        float Rand(int i, int k) => (Seeds.Hash(i, k, seed) >> 8) / 16777216f;

        for (int b = 0; b < blades; b++)
        {
            float r = def.Radius * MathF.Sqrt(Rand(b, 0)), a = Rand(b, 1) * MathF.Tau;
            var basePos = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
            float h = Mathf.Lerp(def.BladeHeight[0], def.BladeHeight[^1], Rand(b, 2));
            float facing = Rand(b, 3) * MathF.Tau;
            var side = new Vector3(MathF.Cos(facing), 0, MathF.Sin(facing));
            // lean away from the centre, plus some randomness
            var lean = (new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * 0.6f + new Vector3(Rand(b, 4) - 0.5f, 0, Rand(b, 5) - 0.5f)).Normalized();
            float curve = 0.45f + 0.4f * Rand(b, 6); // strong lean so blades still read from a high camera
            float w = def.BladeWidth * widthScale * (0.85f + 0.4f * Rand(b, 7));
            float brightness = 0.9f + 0.18f * Rand(b, 8);
            var shade = new Color(brightness, brightness, brightness, Rand(b, 9)); // alpha: the blade's distance cut-off
            var normal = lean.Cross(side).Normalized();
            if (normal.Y < 0) normal = -normal;

            Vector3 At(float t) => basePos + Vector3.Up * h * t + lean * h * curve * t * t;
            float Width(float t) => w * MathF.Pow(1 - t, 0.7f);

            const int segs = 3;
            for (int s = 0; s < segs; s++)
            {
                float t0 = (float)s / segs, t1 = (float)(s + 1) / segs;
                Vector3 l0 = At(t0) - side * Width(t0), r0 = At(t0) + side * Width(t0);
                Vector3 l1 = At(t1) - side * Width(t1), r1 = At(t1) + side * Width(t1);
                if (s == segs - 1)
                    Tri(st, (l0, t0), (r0, t0), (At(1), 1f), normal, shade, 0);
                else
                {
                    Tri(st, (l0, t0), (r0, t0), (r1, t1), normal, shade, 0);
                    Tri(st, (l0, t0), (r1, t1), (l1, t1), normal, shade, 0);
                }
            }

            // flowers: every other blade carries a small five-petal head at its tip
            if (flowers && b % 2 == 0)
            {
                var tip = At(1f);
                float pr = 0.05f * widthScale + 0.03f;
                for (int p = 0; p < 5; p++)
                {
                    float pa = p * MathF.Tau / 5 + facing, pb = pa + MathF.Tau / 10;
                    var e0 = tip + new Vector3(MathF.Cos(pa), 0.15f, MathF.Sin(pa)) * pr;
                    var e1 = tip + new Vector3(MathF.Cos(pb), 0.15f, MathF.Sin(pb)) * pr * 0.6f;
                    var e2 = tip + new Vector3(MathF.Cos(pa - MathF.Tau / 10), 0.15f, MathF.Sin(pa - MathF.Tau / 10)) * pr * 0.6f;
                    Tri(st, (tip, 1f), (e2, 1f), (e0, 1f), Vector3.Up, shade, 2);
                    Tri(st, (tip, 1f), (e0, 1f), (e1, 1f), Vector3.Up, shade, 2);
                }
            }
        }
        st.SetMaterial(material);
        return st.Commit();
    }

    static void Tri(SurfaceTool st, (Vector3 p, float t) a, (Vector3 p, float t) b, (Vector3 p, float t) c, Vector3 n, Color shade, float mark)
    {
        foreach (var (p, t) in new[] { a, b, c })
        {
            st.SetNormal(n);
            st.SetColor(shade);
            st.SetUV(new Vector2(mark, t));
            st.AddVertex(p);
        }
    }

    static ShaderMaterial GrassMaterial(FoliageMeshDefinition def, bool flowers)
    {
        _grassShader ??= GD.Load<Shader>("res://terrain/surface/grass.gdshader");
        var m = new ShaderMaterial { Shader = _grassShader };
        m.SetShaderParameter("root_color", ToColor(def.RootColor));
        m.SetShaderParameter("tip_color", ToColor(def.Color));
        m.SetShaderParameter("head_color", ToColor(def.HeadColor));
        // thinned out before the instancer culls the cell, so the grass edge never shows as squares
        m.SetShaderParameter("fade_start", def.Range * 0.3f);
        m.SetShaderParameter("fade_end", def.Range * 0.9f);
        m.SetShaderParameter("blade_thin_start", def.LodDistance * 0.4f);
        m.SetShaderParameter("blade_thin_end", def.Range * 0.3f);
        return m;
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
