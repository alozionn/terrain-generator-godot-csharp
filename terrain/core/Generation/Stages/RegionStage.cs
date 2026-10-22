using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Splits the map into Voronoi regions: one centre per <see cref="RegionsDefinition.Size"/> square, jittered, and
/// sample positions domain-warped before the nearest-centre lookup so borders wander. Records each region's mean
/// land climate, which is what regional foliage sets are chosen by.
/// </summary>
public static class RegionStage
{
    public static void Run(WorldData w, RegionsDefinition r, uint seed)
    {
        float size = MathF.Max(4f, r.Size);
        int n = Math.Max(1, (int)MathF.Ceiling(w.Size / size));
        if (n * n > short.MaxValue) throw new ArgumentException("regions.size is too small for this map");
        var centres = new (float x, float z)[n * n];
        for (int gz = 0; gz < n; gz++)
            for (int gx = 0; gx < n; gx++)
            {
                uint h = Seeds.Hash(gx, gz, seed);
                // keep centres off the square's edges so a 3x3 search always finds the nearest
                centres[gz * n + gx] = ((gx + 0.15f + 0.7f * Unit(h)) * size, (gz + 0.15f + 0.7f * Unit(Seeds.Mix(h + 1))) * size);
            }

        var warpX = new Noise(Seeds.Mix(seed ^ 0x68e31da4u));
        var warpZ = new Noise(Seeds.Mix(seed ^ 0xb5297a4du));
        float warpFreq = 1.5f / size;
        var sumT = new double[centres.Length];
        var sumM = new double[centres.Length];
        var count = new int[centres.Length];
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
            {
                float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                float qx = px + r.Warp * warpX.Fbm(px * warpFreq, pz * warpFreq, 2);
                float qz = pz + r.Warp * warpZ.Fbm(px * warpFreq, pz * warpFreq, 2);
                int cx = Math.Clamp((int)MathF.Floor(qx / size), 0, n - 1), cz = Math.Clamp((int)MathF.Floor(qz / size), 0, n - 1);
                int best = 0;
                float bestD = float.MaxValue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int gx = cx + dx, gz = cz + dz;
                        if (gx < 0 || gz < 0 || gx >= n || gz >= n) continue;
                        var c = centres[gz * n + gx];
                        float d = (c.x - qx) * (c.x - qx) + (c.z - qz) * (c.z - qz);
                        if (d < bestD) { bestD = d; best = gz * n + gx; }
                    }
                int i = w.Index(x, z);
                w.Region[i] = (short)best;
                if (w.Height[i] > 0)
                {
                    sumT[best] += w.Temperature[i];
                    sumM[best] += w.Moisture[i];
                    count[best]++;
                }
            }

        // the warp can strand small islands of a region inside a neighbour; hand them to the region around them
        MergeIslands(w, (int)(size * size / (w.SampleSpacing * w.SampleSpacing) / 8), sumT, sumM, count);

        w.RegionCentres = centres;
        w.RegionBlend = MathF.Max(0f, r.Blend);
        var climate = new (float, float)[centres.Length];
        for (int i = 0; i < centres.Length; i++)
            climate[i] = count[i] == 0 ? (float.NaN, float.NaN) : ((float)(sumT[i] / count[i]), (float)(sumM[i] / count[i]));
        w.RegionClimate = climate;
        w.RegionBiome = new short[centres.Length];
        Array.Fill(w.RegionBiome, (short)-1);
    }


    /// <summary>
    /// Connected pieces of a region smaller than <paramref name="minSamples"/> join the neighbouring region they touch
    /// most, so every biome patch is one piece. Keeps the per-region climate sums in step.
    /// </summary>
    static void MergeIslands(WorldData w, int minSamples, double[] sumT, double[] sumM, int[] count)
    {
        int n = w.Samples;
        var seen = new bool[n * n];
        var piece = new List<int>();
        var queue = new Queue<int>();
        var touching = new Dictionary<short, int>();
        for (int start = 0; start < seen.Length; start++)
        {
            if (seen[start]) continue;
            short region = w.Region[start];
            piece.Clear();
            touching.Clear();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                piece.Add(i);
                int x = i % n, z = i / n;
                foreach (int j in new[] { x > 0 ? i - 1 : -1, x < n - 1 ? i + 1 : -1, z > 0 ? i - n : -1, z < n - 1 ? i + n : -1 })
                {
                    // land and sea pieces are separate: a region joined only across water is two pieces
                    if (j < 0 || (w.Height[j] > 0) != (w.Height[i] > 0)) continue;
                    if (w.Region[j] != region) touching[w.Region[j]] = touching.GetValueOrDefault(w.Region[j]) + 1;
                    else if (!seen[j]) { seen[j] = true; queue.Enqueue(j); }
                }
            }
            if (piece.Count >= minSamples || touching.Count == 0) continue;
            short into = touching.MaxBy(p => p.Value).Key;
            foreach (int i in piece)
            {
                w.Region[i] = into;
                if (w.Height[i] <= 0) continue;
                sumT[region] -= w.Temperature[i]; sumM[region] -= w.Moisture[i]; count[region]--;
                sumT[into] += w.Temperature[i]; sumM[into] += w.Moisture[i]; count[into]++;
            }
        }
    }

    static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
}
