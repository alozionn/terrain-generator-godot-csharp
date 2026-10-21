using System;
using System.Runtime.InteropServices;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Ground;
using Terrain.Core.World;
using Terrain.Surface;
using TokisanGames;

namespace Terrain.Dev;

/// <summary>
/// Dev only: renders a generated world through Terrain3D (v1.1.0-rc1) so it can be compared against our own ground
/// renderer, e.g. to see what Terrain3D buys at larger scales. Not used by the game; excluded from Ship builds.
/// </summary>
internal sealed class Terrain3DBridge
{
    readonly Node3D _parent;

    public Terrain3DBridge(Node3D parent) => _parent = parent;

    public Terrain3D Node { get; private set; }

    /// <summary>Creates a Terrain3D node with the ground textures, imports height, control and colour maps, and optionally foliage.</summary>
    public void Upload(WorldData w, GroundMaps ground, GroundTextures textures, FoliageMeshSetDefinition meshes = null, FoliageResult foliage = null)
    {
        Node?.QueueFree();
        Node = Terrain3D.Instantiate();
        Node.Name = "Terrain3D";
        Node.VertexSpacing = w.SampleSpacing;
        Node.RegionSize = RegionSizeFor(w.Samples);
        Node.CollisionMode = Terrain3DCollision.CollisionMode.DynamicGame;
        Node.FreeEditorTextures = false; // assets live in memory, not in a saved file
        _parent.AddChild(Node);

        var material = Node.Material;
        material.WorldBackground = Terrain3DMaterial.WorldBackgroundEnum.None;
        material.AutoShaderEnabled = false; // every sample is painted from GroundMaps
        material.SetShaderParam("blend_sharpness", 0.87f);
        for (int i = 0; i < textures.Count; i++)
        {
            var asset = Terrain3DTextureAsset.Instantiate();
            asset.Name = textures.Id(i);
            asset.AlbedoTexture = ImageTexture.CreateFromImage(textures.AlbedoHeight(i));
            asset.NormalTexture = ImageTexture.CreateFromImage(textures.NormalRoughness(i));
            asset.UvScale = textures.UvScale(i);
            asset.DetilingRotation = 0.2;
            Node.Assets.SetTextureAsset(i, asset);
        }

        var images = new Godot.Collections.Array { HeightImage(w), ControlImage(w, ground), ColorImage(w, ground) };
        Node.Data.ImportImages(images, Vector3.Zero, 0, 1);

        if (meshes != null && foliage != null) Plant(meshes, foliage);
    }

    void Plant(FoliageMeshSetDefinition meshes, FoliageResult foliage)
    {
        for (int id = 0; id < meshes.Meshes.Count; id++)
        {
            var def = meshes.Meshes[id];
            var lods = FoliageMeshes.Lods(def);
            var ends = FoliageMeshes.LodEnds(def, lods.Count);
            var root = new Node3D { Name = def.Id };
            for (int i = 0; i < lods.Count; i++)
            {
                var mi = new MeshInstance3D { Name = $"LOD{i}", Mesh = lods[i] };
                root.AddChild(mi);
                mi.Owner = root;
            }
            var scene = new PackedScene();
            scene.Pack(root);
            root.Free();

            var asset = Terrain3DMeshAsset.Instantiate();
            asset.Name = def.Id;
            asset.SceneFile = scene;
            for (int i = 0; i < ends.Length; i++) asset.SetLodRange(i, ends[i]);
            asset.CastShadows = def.CastShadows ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off;
            asset.VisibilityLayers = FoliageInstancer.FoliageLayer;
            Node.Assets.SetMeshAsset(id, asset);

            if (!foliage.ByMesh.TryGetValue(def.Id, out var list) || list.Count == 0) continue;
            var transforms = new Godot.Collections.Array();
            var colors = new Color[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                var basis = new Basis(Vector3.Up, f.Yaw) * Basis.FromEuler(new Vector3(f.TiltX, 0, f.TiltZ));
                transforms.Add(new Transform3D(basis.Scaled(Vector3.One * f.Scale), new Vector3(f.X, f.Y, f.Z)));
                colors[i] = new Color(f.Tint, f.Tint, f.Tint);
            }
            Node.Instancer.AddTransforms(id, transforms, colors, false);
        }
        Node.Instancer.UpdateMmis(-1, default, true);
    }

    static Terrain3D.RegionSizeEnum RegionSizeFor(int samples) => samples switch
    {
        <= 256 => Terrain3D.RegionSizeEnum.Size256,
        <= 512 => Terrain3D.RegionSizeEnum.Size512,
        _ => Terrain3D.RegionSizeEnum.Size1024,
    };

    // Images cover whole regions. Terrain3D fills the rest of a region with height 0 and texture 0, which showed as
    // flat ground z-fighting with the sea at y = 0, so the edge samples are repeated out to the region border instead.

    static int Padded(WorldData w) => (int)RegionSizeFor(w.Samples) * (int)Math.Ceiling(w.Samples / (double)(int)RegionSizeFor(w.Samples));

    static int Source(WorldData w, int i, int size) => w.Index(Math.Min(i % size, w.Samples - 1), Math.Min(i / size, w.Samples - 1));

    static Image HeightImage(WorldData w)
    {
        int n = Padded(w);
        var h = new float[n * n];
        for (int i = 0; i < h.Length; i++) h[i] = w.Height[Source(w, i, n)];
        return Image.CreateFromData(n, n, false, Image.Format.Rf, MemoryMarshal.AsBytes(h.AsSpan()).ToArray());
    }

    /// <summary>Control pixels are raw uint32 bit patterns stored in an RF image (Terrain3D reads them as uints).</summary>
    static Image ControlImage(WorldData w, GroundMaps g)
    {
        int n = Padded(w);
        var bits = new uint[n * n];
        for (int i = 0; i < bits.Length; i++)
        {
            int s = Source(w, i, n);
            bits[i] = GroundMaps.EncodeTerrain3D(g.Base[s], g.Overlay[s], g.Blend[s]);
        }
        return Image.CreateFromData(n, n, false, Image.Format.Rf, MemoryMarshal.AsBytes(bits.AsSpan()).ToArray());
    }

    /// <summary>RGB tints the albedo; alpha is the roughness modifier, where 0.5 leaves it unchanged.</summary>
    static Image ColorImage(WorldData w, GroundMaps g)
    {
        int n = Padded(w);
        var px = new byte[n * n * 4];
        for (int i = 0; i < n * n; i++)
        {
            var t = g.Tint[Source(w, i, n)];
            px[i * 4] = (byte)(Math.Clamp(t.X, 0f, 1f) * 255f);
            px[i * 4 + 1] = (byte)(Math.Clamp(t.Y, 0f, 1f) * 255f);
            px[i * 4 + 2] = (byte)(Math.Clamp(t.Z, 0f, 1f) * 255f);
            px[i * 4 + 3] = 128;
        }
        return Image.CreateFromData(n, n, false, Image.Format.Rgba8, px);
    }
}
