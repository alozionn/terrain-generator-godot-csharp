using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Terrain.Core.Level;

namespace Terrain.Surface;

/// <summary>
/// The ground texture set loaded into memory: one packed albedo+height and normal+roughness image per texture id,
/// all the same size, plus the texture arrays the ground shader samples. Files load through the importer (so exports
/// work); entries with only "procedural" colours become noise stand-ins of the same size.
/// </summary>
internal sealed class GroundTextures
{
    /// <summary>The ground shader blends up to this many textures.</summary>
    public const int MaxTextures = 8;

    readonly List<(string id, Image albedoHeight, Image normalRoughness, float uvScale)> _entries = new();
    readonly Dictionary<string, int> _ids = new();

    public IReadOnlyDictionary<string, int> Ids => _ids;
    public int Count => _entries.Count;
    public Image AlbedoHeight(int i) => _entries[i].albedoHeight;
    public Image NormalRoughness(int i) => _entries[i].normalRoughness;
    public float UvScale(int i) => _entries[i].uvScale;
    public string Id(int i) => _entries[i].id;

    public Texture2DArray AlbedoArray { get; private set; }
    public Texture2DArray NormalArray { get; private set; }
    /// <summary>Repeats per metre, padded to <see cref="MaxTextures"/>.</summary>
    public float[] UvScales { get; private set; } = new float[MaxTextures];

    public static GroundTextures Load(TextureSetDefinition set)
    {
        var t = new GroundTextures();
        if (set.Textures.Count > MaxTextures) GD.PushWarning($"[Terrain] {set.Textures.Count} ground textures; only the first {MaxTextures} are used");

        int size = 0;
        var pending = new List<(GroundTextureDefinition def, Image alb, Image nrm)>();
        foreach (var def in set.Textures.Take(MaxTextures))
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
            t._ids[def.Id] = t._entries.Count;
            t.UvScales[t._entries.Count] = def.UvScale;
            t._entries.Add((def.Id, alb, nrm, def.UvScale));
        }

        if (t._entries.Count > 0)
        {
            t.AlbedoArray = new Texture2DArray();
            t.AlbedoArray.CreateFromImages(new Godot.Collections.Array<Image>(t._entries.Select(e => e.albedoHeight)));
            t.NormalArray = new Texture2DArray();
            t.NormalArray.CreateFromImages(new Godot.Collections.Array<Image>(t._entries.Select(e => e.normalRoughness)));
        }
        return t;
    }

    static Image LoadImage(string path)
    {
        var img = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path)?.GetImage() : null;
        if (img != null && img.IsCompressed()) img.Decompress();
        return img;
    }

    /// <summary>Seamless noise between two colours, its value doubling as height; flat normal, fairly rough.</summary>
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
                // seamless: blend four offset copies of the noise
                float u = (float)x / size, v = (float)y / size;
                float n = (noise.GetNoise2D(x, y) * (1 - u) * (1 - v) + noise.GetNoise2D(x - size, y) * u * (1 - v)
                         + noise.GetNoise2D(x, y - size) * (1 - u) * v + noise.GetNoise2D(x - size, y - size) * u * v) * 0.5f + 0.5f;
                alb.SetPixel(x, y, new Color(a.X + (b.X - a.X) * n, a.Y + (b.Y - a.Y) * n, a.Z + (b.Z - a.Z) * n, n));
            }
        return (alb, nrm);
    }
}
