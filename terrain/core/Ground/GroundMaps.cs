using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Core.Foliage;
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
    /// <param name="sets">The level's foliage sets: regional ones tint the ground of their regions and lay patches
    /// (dirt, litter) where no band edge or cliff already uses the overlay. Tint and patch strength blend across region
    /// borders over the regions' blend width.</param>
    public static GroundMaps Build(WorldData w, PaletteDefinition palette, IReadOnlyDictionary<string, int> textureIds, uint seed,
        IReadOnlyList<FoliageSetDefinition> sets = null)
    {
        var bands = palette.Bands.OrderBy(b => b.From).ToArray();
        int n = w.Height.Length;
        var maps = new GroundMaps { Base = new byte[n], Overlay = new byte[n], Blend = new float[n], Tint = new Vector3[n] };
        if (bands.Length == 0) return maps;

        byte Id(string tex) => (byte)(textureIds.TryGetValue(tex ?? "", out int id) ? Math.Clamp(id, 0, 31) : 0);
        var biome = BiomeFields(w, sets, textureIds, bands);
        var patchNoise = new Noise(Seeds.Derive(seed, "patches"));
        var bandTex = bands.Select(b => Id(b.Tex)).ToArray();
        var bandTint = bands.Select(b => LevelJson.ParseColor(b.Tint)).ToArray();
        byte cliffTex = Id(palette.Cliff.Tex);
        bool hasCliff = !string.IsNullOrEmpty(palette.Cliff.Tex);
        var cliffTint = LevelJson.ParseColor(palette.Cliff.Tint);
        float blend = MathF.Max(1e-3f, palette.Blend);
        float cosStart = MathF.Cos(palette.Cliff.SlopeStart * MathF.PI / 180f), cosFull = MathF.Cos(palette.Cliff.SlopeFull * MathF.PI / 180f);
        var macro = new Noise(Seeds.Derive(seed, "macro"));
        var ero = palette.Erosion ?? new ErosionPaintDefinition();
        bool hasCrease = !string.IsNullOrEmpty(ero.Crease) && textureIds.ContainsKey(ero.Crease);
        byte creaseTex = Id(ero.Crease);

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
                float c = hasCliff ? Noise.SmoothStep(cosStart, cosFull, NormalY(w, x, z)) : 0f;
                // erosion: bare rock on gully crests, softer ground down the creases
                float ridge = w.Ridge[i], crest = MathF.Max(0f, ridge), crease = MathF.Max(0f, -ridge);
                if (hasCliff && ridge != 0) c = Math.Clamp(c + ero.CrestRock * crest - ero.CreaseSoften * crease, 0f, 1f);

                if (biome != null)
                {
                    int bi = w.BiomeAt(x, z);
                    var mask = bi >= 0 ? biome.Bands[bi] : null;
                    float applies = mask == null ? 0f : (mask[lower] ? 1f - t : 0f) + (mask[upper] ? t : 0f);
                    applies *= 1f - c;
                    tint *= Vector3.Lerp(Vector3.One, biome.Tint[i], applies);
                    float px = x * w.SampleSpacing, pz = z * w.SampleSpacing, f = biome.Frequency[i];
                    float p = biome.Amount[i] * applies
                            * Noise.SmoothStep(biome.Threshold[i], biome.Threshold[i] + 0.25f, patchNoise.Fbm(px * f, pz * f, 3));
                    if (w.IsPlayable(Math.Min(w.Cells - 1, x / w.SamplesPerCell), Math.Min(w.Cells - 1, z / w.SamplesPerCell))) p *= biome.Playable[i];
                    // patches use the overlay only where no band edge holds it
                    if (bi >= 0 && biome.PatchTex[bi] != 255 && t < 0.02f && p > 0.01f && biome.PatchTex[bi] != baseTex)
                    {
                        overTex = biome.PatchTex[bi];
                        t = p;
                        // patch textures carry their own colour; the band tint (green for grass) would muddy them
                        tint = Vector3.Lerp(tint, Vector3.One, p);
                    }
                }

                if (hasCliff)
                {
                    if (c > t)
                    {
                        // keep the band texture as base so the cliff fades into it
                        baseTex = t > 0.5f ? overTex : baseTex;
                        overTex = cliffTex;
                        t = c;
                    }
                    tint = Vector3.Lerp(tint, cliffTint, c);
                }
                if (hasCrease && crease > 0.05f)
                {
                    // scree and soil collect in the creases, taking the overlay unless something shows more there
                    float amount = ero.CreaseAmount * Noise.SmoothStep(0.1f, 0.7f, crease);
                    if (amount > t && creaseTex != baseTex)
                    {
                        overTex = creaseTex;
                        t = amount;
                        tint = Vector3.Lerp(tint, Vector3.One, amount);
                    }
                }
                tint *= 1f + ero.Shade * ridge;
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

    sealed class Biomes
    {
        public bool[][] Bands;
        public byte[] PatchTex;
        public Vector3[] Tint;
        public float[] Amount, Frequency, Threshold, Playable;
    }

    /// <summary>Per-biome band masks and patch textures, and per-sample ground settings blurred across region borders.</summary>
    static Biomes BiomeFields(WorldData w, IReadOnlyList<FoliageSetDefinition> sets, IReadOnlyDictionary<string, int> textureIds, BandDefinition[] bands)
    {
        if (sets == null || w.BiomeIds.Length == 0) return null;
        var defs = w.BiomeIds.Select(id => sets.FirstOrDefault(s => s.Id == id)?.Ground).ToArray();
        if (defs.All(d => d == null)) return null;

        int n = w.Height.Length;
        var f = new Biomes
        {
            Bands = defs.Select(d => d == null ? null : bands.Select(b => d.Bands.Count == 0 || d.Bands.Contains(b.Name)).ToArray()).ToArray(),
            PatchTex = defs.Select(d => d != null && textureIds.TryGetValue(d.Patches ?? "", out int id) ? (byte)Math.Clamp(id, 0, 31) : (byte)255).ToArray(),
            Tint = new Vector3[n], Amount = new float[n], Frequency = new float[n], Threshold = new float[n], Playable = new float[n],
        };
        for (int i = 0; i < n; i++)
        {
            int r = w.Region[i];
            int b = r >= 0 && r < w.RegionBiome.Length ? w.RegionBiome[r] : -1;
            var d = b >= 0 ? defs[b] : null;
            f.Tint[i] = d == null ? Vector3.One : LevelJson.ParseColor(d.Tint);
            f.Amount[i] = d == null || f.PatchTex[b] == 255 ? 0f : d.PatchAmount;
            f.Frequency[i] = d?.PatchFrequency ?? 0.06f;
            f.Threshold[i] = d?.PatchThreshold ?? 1f;
            f.Playable[i] = d?.PlayablePatches ?? 1f;
        }
        int radius = Math.Max(1, (int)MathF.Round(w.RegionBlend / w.SampleSpacing));
        Blur(w.Samples, radius, f.Tint);
        foreach (var field in new[] { f.Amount, f.Frequency, f.Threshold, f.Playable }) Blur(w.Samples, radius, field);
        return f;
    }

    /// <summary>Separable box blur over an n x n field, clamped at the edges.</summary>
    static void Blur(int n, int radius, Vector3[] field)
    {
        var tmp = new Vector3[field.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            var (src, dst) = pass == 0 ? (field, tmp) : (tmp, field);
            int step = pass == 0 ? 1 : n, across = pass == 0 ? n : 1;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    Vector3 sum = Vector3.Zero;
                    for (int k = -radius; k <= radius; k++) sum += src[a * across + Math.Clamp(b + k, 0, n - 1) * step];
                    dst[a * across + b * step] = sum / (2 * radius + 1);
                }
        }
    }

    static void Blur(int n, int radius, float[] field)
    {
        var tmp = new float[field.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            var (src, dst) = pass == 0 ? (field, tmp) : (tmp, field);
            int step = pass == 0 ? 1 : n, across = pass == 0 ? n : 1;
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    float sum = 0;
                    for (int k = -radius; k <= radius; k++) sum += src[a * across + Math.Clamp(b + k, 0, n - 1) * step];
                    dst[a * across + b * step] = sum / (2 * radius + 1);
                }
        }
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
