using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Core.Generation;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Ground;

/// <summary>
/// Per-sample ground painting: a base and an overlay texture, how much the overlay shows, and a tint.
/// Band edges blend the lower band (base) into the upper (overlay); steep slopes overlay the cliff texture instead,
/// whichever shows more. The renderer decides how to blend; this only says what goes where.
/// </summary>
public sealed class GroundMaps
{
    public required byte[] Base { get; init; }
    public required byte[] Overlay { get; init; }
    /// <summary>0 = only base, 1 = only overlay.</summary>
    public required float[] Blend { get; init; }
    /// <summary>Linear-ish multiplier, 1 = untouched texture.</summary>
    public required Vector3[] Tint { get; init; }

    /// <param name="textureIds">Texture id → GPU index, from the texture set's order.</param>
    public static GroundMaps Build(WorldData w, PaletteDefinition palette, IReadOnlyDictionary<string, int> textureIds, uint seed)
    {
        var bands = palette.Bands.OrderBy(b => b.From).ToArray();
        int n = w.Height.Length;
        var maps = new GroundMaps { Base = new byte[n], Overlay = new byte[n], Blend = new float[n], Tint = new Vector3[n] };
        if (bands.Length == 0) return maps;

        byte Id(string tex) => (byte)(textureIds.TryGetValue(tex ?? "", out int id) ? Math.Clamp(id, 0, 31) : 0);
        var bandTex = bands.Select(b => Id(b.Tex)).ToArray();
        var bandTint = bands.Select(b => LevelJson.ParseColor(b.Tint)).ToArray();
        byte cliffTex = Id(palette.Cliff.Tex);
        bool hasCliff = !string.IsNullOrEmpty(palette.Cliff.Tex);
        var cliffTint = LevelJson.ParseColor(palette.Cliff.Tint);
        float blend = MathF.Max(1e-3f, palette.Blend);
        float cosStart = MathF.Cos(palette.Cliff.SlopeStart * MathF.PI / 180f), cosFull = MathF.Cos(palette.Cliff.SlopeFull * MathF.PI / 180f);
        var macro = new Noise(Seeds.Derive(seed, "macro"));

        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
            {
                int i = w.Index(x, z);
                float h = w.Height[i];
                int b = w.Band[i];

                // blend toward whichever neighbouring band is within reach
                int lower = b, upper = b;
                float t = 0;
                if (b + 1 < bands.Length && h > bands[b + 1].From - blend)
                {
                    upper = b + 1;
                    t = Noise.SmoothStep(bands[upper].From - blend, bands[upper].From + blend, h);
                }
                else if (b > 0 && h < bands[b].From + blend)
                {
                    lower = b - 1;
                    t = Noise.SmoothStep(bands[b].From - blend, bands[b].From + blend, h);
                }
                byte baseTex = bandTex[lower], overTex = bandTex[upper];
                var tint = Vector3.Lerp(bandTint[lower], bandTint[upper], t);

                if (hasCliff)
                {
                    float c = Noise.SmoothStep(cosStart, cosFull, NormalY(w, x, z));
                    if (c > t)
                    {
                        // keep the band texture as base so the cliff fades into it
                        baseTex = t > 0.5f ? overTex : baseTex;
                        overTex = cliffTex;
                        t = c;
                    }
                    tint = Vector3.Lerp(tint, cliffTint, c);
                }
                // Terrain3D's height blend turns black when a texture blends with itself
                if (baseTex == overTex) t = 0;

                float m = 1f + palette.Macro.Amount * macro.Fbm(x * w.SampleSpacing * palette.Macro.Frequency, z * w.SampleSpacing * palette.Macro.Frequency, 3);
                maps.Base[i] = baseTex;
                maps.Overlay[i] = overTex;
                maps.Blend[i] = t;
                maps.Tint[i] = tint * m;
            }
        return maps;
    }

    static float NormalY(WorldData w, int x, int z)
    {
        int xl = Math.Max(0, x - 1), xr = Math.Min(w.Samples - 1, x + 1);
        int zu = Math.Max(0, z - 1), zd = Math.Min(w.Samples - 1, z + 1);
        float dx = (w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing);
        float dz = (w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing);
        return 1f / MathF.Sqrt(dx * dx + dz * dz + 1f);
    }

    /// <summary>
    /// Terrain3D control-map pixel (uint32, MSB first): base id bits 31-27, overlay 26-22, blend 21-14.
    /// Autoshader, hole and navigation bits stay 0.
    /// </summary>
    public static uint EncodeTerrain3D(byte baseId, byte overlayId, float blend) =>
        (uint)(baseId & 0x1F) << 27 | (uint)(overlayId & 0x1F) << 22 | (uint)Math.Clamp((int)MathF.Round(blend * 255f), 0, 255) << 14;
}
