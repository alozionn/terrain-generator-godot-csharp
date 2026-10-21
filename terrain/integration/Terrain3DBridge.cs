using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Ground;
using Terrain.Core.Level;
using Terrain.Core.World;
using Terrain.Surface;
using TokisanGames;

namespace Terrain.Integration;

/// <summary>
/// The only place that knows Terrain3D. Turns <see cref="WorldData"/> + <see cref="GroundMaps"/> into Terrain3D
/// height, control and colour maps, and owns the ground texture and mesh assets.
/// </summary>
internal sealed class Terrain3DBridge
{
    readonly Node3D _parent;
    readonly List<Terrain3DTextureAsset> _textures = new();
    readonly Dictionary<string, int> _textureIds = new();
    readonly List<Terrain3DMeshAsset> _meshes = new();
    readonly Dictionary<string, int> _meshIds = new();

    /// <summary>Render layer bit for foliage (layer 2), so decals limited to layer 1 skip it.</summary>
    public const uint FoliageLayer = 2;

    public Terrain3DBridge(Node3D parent) => _parent = parent;

    public Terrain3D Node { get; private set; }
    public IReadOnlyDictionary<string, int> TextureIds => _textureIds;

    /// <summary>Builds texture assets from the set. Image files are loaded raw so procedural stand-ins can match their size and format.</summary>
    public void LoadTextures(TextureSetDefinition set)
    {
        _textures.Clear();
        _textureIds.Clear();
        int size = 0;
        var pending = new List<(GroundTextureDefinition def, Image alb, Image nrm)>();
        foreach (var def in set.Textures)
        {
            Image alb = null, nrm = null;
            if (!string.IsNullOrEmpty(def.AlbedoHeight))
            {
                alb = LoadImage(def.AlbedoHeight);
                nrm = LoadImage(def.NormalRoughness);
                if (alb == null || nrm == null) GD.PushError($"[Terrain] could not load texture '{def.Id}'");
                else size = Math.Max(size, alb.GetWidth());
            }
            pending.Add((def, alb, nrm));
        }
        if (size == 0) size = 512;

        foreach (var (def, a, n) in pending)
        {
            Image alb = a, nrm = n;
            if (alb == null || nrm == null) (alb, nrm) = Procedural(def, size);
            foreach (var img in new[] { alb, nrm })
            {
                img.Convert(Image.Format.Rgba8);
                if (img.GetWidth() != size) img.Resize(size, size, Image.Interpolation.Bilinear);
                img.GenerateMipmaps();
            }
            var asset = Terrain3DTextureAsset.Instantiate();
            asset.Name = def.Id;
            asset.AlbedoTexture = ImageTexture.CreateFromImage(alb);
            asset.NormalTexture = ImageTexture.CreateFromImage(nrm);
            asset.UvScale = def.UvScale;
            asset.DetilingRotation = 0.2; // hides repeats
            _textureIds[def.Id] = _textures.Count;
            _textures.Add(asset);
        }
    }

    /// <summary>Builds instancer mesh assets; the list order is the mesh id.</summary>
    public void LoadMeshes(FoliageMeshSetDefinition set)
    {
        _meshes.Clear();
        _meshIds.Clear();
        foreach (var def in set.Meshes)
        {
            var asset = Terrain3DMeshAsset.Instantiate();
            asset.Name = def.Id;
            asset.SceneFile = FoliageMeshes.Scene(def);
            asset.Lod0Range = def.Range;
            asset.CastShadows = def.CastShadows ? RenderingServer.ShadowCastingSetting.On : RenderingServer.ShadowCastingSetting.Off;
            asset.VisibilityLayers = FoliageLayer; // keeps the zone-grid decals off foliage
            _meshIds[def.Id] = _meshes.Count;
            _meshes.Add(asset);
        }
    }

    /// <summary>Hands every placed instance to the Terrain3D instancer. Call after <see cref="Upload"/>.</summary>
    public void Plant(FoliageResult foliage)
    {
        var instancer = Node.Instancer;
        foreach (var (mesh, list) in foliage.ByMesh)
        {
            if (list.Count == 0) continue;
            if (!_meshIds.TryGetValue(mesh, out int id))
            {
                GD.PushError($"[Terrain] foliage mesh '{mesh}' is not in meshes.json");
                continue;
            }
            var transforms = new Godot.Collections.Array();
            var colors = new Color[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                var basis = new Basis(Vector3.Up, f.Yaw) * Basis.FromEuler(new Vector3(f.TiltX, 0, f.TiltZ));
                transforms.Add(new Transform3D(basis.Scaled(Vector3.One * f.Scale), new Vector3(f.X, f.Y, f.Z)));
                colors[i] = new Color(f.Tint, f.Tint, f.Tint);
            }
            instancer.AddTransforms(id, transforms, colors, false);
        }
        instancer.UpdateMmis(-1, default, true);
    }

    /// <summary>Recreates the Terrain3D node and imports the maps. Recreating keeps old regions from lingering when the map size changes.</summary>
    public void Upload(WorldData w, GroundMaps ground)
    {
        Node?.QueueFree();
        Node = Terrain3D.Instantiate();
        Node.Name = "Terrain3D";
        Node.VertexSpacing = w.SampleSpacing;
        Node.RegionSize = RegionSizeFor(w.Samples);
        Node.CollisionMode = Terrain3DCollision.CollisionMode.DynamicGame;
        Node.FreeEditorTextures = false; // our assets live in memory, not in a saved file
        _parent.AddChild(Node);

        var material = Node.Material;
        material.WorldBackground = Terrain3DMaterial.WorldBackgroundEnum.None;
        material.AutoShaderEnabled = false; // we paint every sample ourselves
        material.SetShaderParam("blend_sharpness", 0.87f);
        for (int i = 0; i < _textures.Count; i++) Node.Assets.SetTextureAsset(i, _textures[i]);
        for (int i = 0; i < _meshes.Count; i++) Node.Assets.SetMeshAsset(i, _meshes[i]);

        var images = new Godot.Collections.Array { HeightImage(w), ControlImage(w, ground), ColorImage(w, ground) };
        Node.Data.ImportImages(images, Vector3.Zero, 0, 1);
    }

    /// <summary>Loads through the import system (works in exports) and returns raw pixels.</summary>
    static Image LoadImage(string path)
    {
        var img = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path)?.GetImage() : null;
        if (img != null && img.IsCompressed()) img.Decompress();
        return img;
    }

    static Terrain3D.RegionSizeEnum RegionSizeFor(int samples) => samples switch
    {
        <= 256 => Terrain3D.RegionSizeEnum.Size256,
        <= 512 => Terrain3D.RegionSizeEnum.Size512,
        _ => Terrain3D.RegionSizeEnum.Size1024,
    };

    static Image HeightImage(WorldData w) =>
        Image.CreateFromData(w.Samples, w.Samples, false, Image.Format.Rf, MemoryMarshal.AsBytes(w.Height.AsSpan()).ToArray());

    /// <summary>Control pixels are raw uint32 bit patterns stored in an RF image (Terrain3D reads them as uints).</summary>
    static Image ControlImage(WorldData w, GroundMaps g)
    {
        var bits = new uint[w.Height.Length];
        for (int i = 0; i < bits.Length; i++) bits[i] = GroundMaps.EncodeTerrain3D(g.Base[i], g.Overlay[i], g.Blend[i]);
        return Image.CreateFromData(w.Samples, w.Samples, false, Image.Format.Rf, MemoryMarshal.AsBytes(bits.AsSpan()).ToArray());
    }

    /// <summary>RGB tints the albedo; alpha is the roughness modifier, where 0.5 leaves it unchanged.</summary>
    static Image ColorImage(WorldData w, GroundMaps g)
    {
        var px = new byte[w.Height.Length * 4];
        for (int i = 0; i < g.Tint.Length; i++)
        {
            var t = g.Tint[i];
            px[i * 4] = (byte)(Math.Clamp(t.X, 0f, 1f) * 255f);
            px[i * 4 + 1] = (byte)(Math.Clamp(t.Y, 0f, 1f) * 255f);
            px[i * 4 + 2] = (byte)(Math.Clamp(t.Z, 0f, 1f) * 255f);
            px[i * 4 + 3] = 128;
        }
        return Image.CreateFromData(w.Samples, w.Samples, false, Image.Format.Rgba8, px);
    }

    /// <summary>Noise between two colours, its value doubling as height; flat normal, fairly rough.</summary>
    static (Image alb, Image nrm) Procedural(GroundTextureDefinition def, int size)
    {
        var a = LevelJson.ParseColor(def.Procedural.Length > 0 ? def.Procedural[0] : "#808080");
        var b = LevelJson.ParseColor(def.Procedural.Length > 1 ? def.Procedural[1] : def.Procedural.Length > 0 ? def.Procedural[0] : "#606060");
        var noise = new FastNoiseLite { Seed = def.Id.GetHashCode(), Frequency = 8f / size, FractalOctaves = 5 };
        var alb = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var nrm = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        nrm.Fill(new Color(0.5f, 0.5f, 1f, 0.85f));
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // seamless: sample a torus-wrapped domain by blending the four offset copies
                float u = (float)x / size, v = (float)y / size;
                float n = (noise.GetNoise2D(x, y) * (1 - u) * (1 - v) + noise.GetNoise2D(x - size, y) * u * (1 - v)
                         + noise.GetNoise2D(x, y - size) * (1 - u) * v + noise.GetNoise2D(x - size, y - size) * u * v) * 0.5f + 0.5f;
                var c = new System.Numerics.Vector3(a.X, a.Y, a.Z) * (1 - n) + new System.Numerics.Vector3(b.X, b.Y, b.Z) * n;
                alb.SetPixel(x, y, new Color(c.X, c.Y, c.Z, n));
            }
        return (alb, nrm);
    }
}
