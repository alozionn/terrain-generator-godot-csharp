using System;
using System.Linq;
using System.Numerics;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Debug;

/// <summary>Top-down review images of a generated world (x right, z down), as raw RGB8. The Godot side saves them as PNG.</summary>
public static class StageImages
{
    public readonly record struct Rgb(int Width, int Height, byte[] Pixels);

    /// <summary>Pixels per sample so the image lands around 1024 px.</summary>
    public static int DefaultScale(WorldData w) => Math.Max(1, 1024 / w.Samples);

    /// <summary>Grey height ramp over land, blue under sea level.</summary>
    public static Rgb Height(WorldData w, int scale)
    {
        float max = MathF.Max(1f, w.Height.Max()), min = MathF.Min(-1f, w.Height.Min());
        return Draw(w, scale, (x, z) =>
        {
            float h = w.H(x, z);
            if (h < 0) { float t = h / min; return new Vector3(0.15f, 0.35f, 0.75f) * (1f - 0.7f * t); }
            float g = 0.15f + 0.85f * h / max;
            return new Vector3(g);
        });
    }

    /// <summary>Palette colours with hillshade.</summary>
    public static Rgb Bands(WorldData w, PaletteDefinition palette, int scale)
    {
        var colors = palette.Bands.OrderBy(b => b.From).Select(b => LevelJson.ParseColor(b.Color)).ToArray();
        return Draw(w, scale, (x, z) => colors.Length == 0 ? new Vector3(0.5f) : colors[w.Band[w.Index(x, z)]] * Shade(w, x, z));
    }

    /// <summary>Bands with decor dimmed, playable cells outlined, and each zone tinted its own colour.</summary>
    public static Rgb Zones(WorldData w, PaletteDefinition palette, int scale)
    {
        var bands = Bands(w, palette, scale);
        var px = bands.Pixels;
        int cellPx = w.SamplesPerCell * scale;
        for (int y = 0; y < bands.Height; y++)
            for (int x = 0; x < bands.Width; x++)
            {
                int cx = Math.Min(w.Cells - 1, x / cellPx), cz = Math.Min(w.Cells - 1, y / cellPx);
                int zone = x / cellPx < w.Cells && y / cellPx < w.Cells ? w.CellZone[cz * w.Cells + cx] : -1;
                int o = (y * bands.Width + x) * 3;
                Vector3 c = new(px[o] / 255f, px[o + 1] / 255f, px[o + 2] / 255f);
                if (zone < 0) c *= 0.55f;
                else
                {
                    c = Vector3.Lerp(c, ZoneTint(zone), 0.18f);
                    if (cellPx >= 3 && (x % cellPx == 0 || y % cellPx == 0)) c *= 0.6f;
                }
                Put(px, o, c);
            }
        return bands;
    }

    /// <summary>Grey hillshade with a low light from the north-west, so landforms (and erosion gullies) stand out.</summary>
    public static Rgb Hillshade(WorldData w, int scale) =>
        Draw(w, scale, (x, z) => w.H(x, z) < 0 ? Sea : new Vector3(MathF.Pow(Shade(w, x, z), 2.2f)));

    /// <summary>Temperature over land, blue (cold) to red (hot), with hillshade.</summary>
    public static Rgb Temperature(WorldData w, int scale) =>
        Draw(w, scale, (x, z) => w.H(x, z) < 0 ? Sea : Vector3.Lerp(new Vector3(0.2f, 0.4f, 0.95f), new Vector3(0.95f, 0.3f, 0.15f), w.Temperature[w.Index(x, z)]) * Shade(w, x, z));

    /// <summary>Moisture over land, sand (dry) to teal (wet), with hillshade.</summary>
    public static Rgb Moisture(WorldData w, int scale) =>
        Draw(w, scale, (x, z) => w.H(x, z) < 0 ? Sea : Vector3.Lerp(new Vector3(0.9f, 0.8f, 0.5f), new Vector3(0.1f, 0.55f, 0.55f), w.Moisture[w.Index(x, z)]) * Shade(w, x, z));

    /// <summary>One colour per biome over land, region borders darkened, hillshade.</summary>
    public static Rgb Biomes(WorldData w, int scale) =>
        Draw(w, scale, (x, z) =>
        {
            if (w.H(x, z) < 0) return Sea;
            int b = w.BiomeAt(x, z);
            var c = b < 0 ? new Vector3(0.5f) : BiomeColor(w.BiomeIds[b]);
            int r = w.Region[w.Index(x, z)];
            bool border = (x + 1 < w.Samples && w.Region[w.Index(x + 1, z)] != r) || (z + 1 < w.Samples && w.Region[w.Index(x, z + 1)] != r);
            return c * Shade(w, x, z) * (border ? 0.6f : 1f);
        });

    static readonly Vector3 Sea = new(0.15f, 0.3f, 0.55f);

    /// <summary>A stable, saturated colour per biome id.</summary>
    public static Vector3 BiomeColor(string id)
    {
        uint h = 2166136261u;
        foreach (char ch in id) { h ^= ch; h *= 16777619u; }
        float hue = (h % 360) / 60f;
        float k(float n) { float v = (n + hue) % 6; return 1f - 0.6f * MathF.Max(0f, MathF.Min(MathF.Min(v, 4 - v), 1f)); }
        return new Vector3(k(5), k(3), k(1));
    }

    static Vector3 ZoneTint(int zone) => (zone % 4) switch
    {
        0 => new Vector3(1f, 0.9f, 0.3f),
        1 => new Vector3(0.3f, 0.9f, 1f),
        2 => new Vector3(1f, 0.4f, 0.8f),
        _ => new Vector3(0.6f, 1f, 0.4f),
    };

    /// <summary>Simple Lambert from the north-west, exaggerated so gentle zones still read.</summary>
    static float Shade(WorldData w, int x, int z)
    {
        int xl = Math.Max(0, x - 1), xr = Math.Min(w.Samples - 1, x + 1);
        int zu = Math.Max(0, z - 1), zd = Math.Min(w.Samples - 1, z + 1);
        float dx = (w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing);
        float dz = (w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing);
        var n = Vector3.Normalize(new Vector3(-dx * 2f, 1f, -dz * 2f));
        var l = Vector3.Normalize(new Vector3(-1f, 1.4f, -1f));
        return 0.55f + 0.45f * MathF.Max(0f, Vector3.Dot(n, l)) / 0.84f;
    }

    static Rgb Draw(WorldData w, int scale, Func<int, int, Vector3> color)
    {
        int size = w.Samples * scale;
        var px = new byte[size * size * 3];
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
            {
                var c = color(x, z);
                for (int sy = 0; sy < scale; sy++)
                    for (int sx = 0; sx < scale; sx++)
                        Put(px, ((z * scale + sy) * size + x * scale + sx) * 3, c);
            }
        return new Rgb(size, size, px);
    }

    static void Put(byte[] px, int o, Vector3 c)
    {
        px[o] = (byte)(Math.Clamp(c.X, 0f, 1f) * 255f + 0.5f);
        px[o + 1] = (byte)(Math.Clamp(c.Y, 0f, 1f) * 255f + 0.5f);
        px[o + 2] = (byte)(Math.Clamp(c.Z, 0f, 1f) * 255f + 0.5f);
    }
}
