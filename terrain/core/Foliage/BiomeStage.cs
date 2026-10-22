using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Core.Generation;
using Terrain.Core.World;

namespace Terrain.Core.Foliage;

/// <summary>
/// Chooses a regional foliage set (biome) for every Voronoi region from the region's mean climate. Sets whose
/// climate windows hold it compete by weight, favouring the ones it sits closest to the middle of; when none fits, the
/// nearest window wins. Each region rolls with its own seed, so neighbours with the same climate can still differ.
/// </summary>
public static class BiomeStage
{
    public static void Run(WorldData w, IReadOnlyList<FoliageSetDefinition> sets, uint seed)
    {
        var biomes = sets.Where(s => s.Climate != null).ToArray();
        w.BiomeIds = biomes.Select(s => s.Id).ToArray();
        w.RegionBiome = new short[w.RegionClimate.Length];
        Array.Fill(w.RegionBiome, (short)-1);
        if (biomes.Length == 0) return;

        for (int r = 0; r < w.RegionClimate.Length; r++)
        {
            var (t, m) = w.RegionClimate[r];
            if (float.IsNaN(t)) continue;
            w.RegionBiome[r] = (short)Pick(biomes, t, m, Seeds.Hash(r, 0, seed));
        }
    }

    public static int Pick(FoliageSetDefinition[] biomes, float t, float m, uint roll)
    {
        float total = 0;
        var weights = new float[biomes.Length];
        int nearest = 0;
        float nearestD = float.MaxValue;
        for (int b = 0; b < biomes.Length; b++)
        {
            var c = biomes[b].Climate;
            var (dt, ct) = Window(c.Temperature, t);
            var (dm, cm) = Window(c.Moisture, m);
            float outside = MathF.Sqrt(dt * dt + dm * dm);
            if (outside < nearestD) { nearestD = outside; nearest = b; }
            if (outside > 0) continue;
            // 1 in the middle of both windows, 0.25 at their edges
            weights[b] = MathF.Max(0f, c.Weight) * (0.25f + 0.75f * ct * cm);
            total += weights[b];
        }
        if (total <= 0) return nearest;
        float pick = (roll >> 8) * (1f / 16777216f) * total;
        for (int b = 0; b < biomes.Length; b++)
        {
            pick -= weights[b];
            if (pick < 0 && weights[b] > 0) return b;
        }
        return Array.FindLastIndex(weights, x => x > 0);
    }

    /// <summary>Distance outside a [lo, hi] window (0 inside), and how central the value is inside it (1 middle, 0 edge).</summary>
    static (float outside, float centrality) Window(float[] window, float v)
    {
        float lo = window is { Length: > 0 } ? window[0] : 0f, hi = window is { Length: > 1 } ? window[1] : 1f;
        if (v < lo) return (lo - v, 0f);
        if (v > hi) return (v - hi, 0f);
        float half = MathF.Max(1e-4f, (hi - lo) * 0.5f);
        return (0f, 1f - MathF.Abs(v - (lo + hi) * 0.5f) / half);
    }
}
