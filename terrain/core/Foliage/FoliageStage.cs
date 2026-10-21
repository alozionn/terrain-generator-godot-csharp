using System;
using System.Collections.Generic;
using System.Linq;
using Terrain.Core.Generation;
using Terrain.Core.World;

namespace Terrain.Core.Foliage;

public readonly record struct FoliageInstance(float X, float Y, float Z, float Yaw, float TiltX, float TiltZ, float Scale, float Tint);

public sealed class FoliageResult
{
    /// <summary>Instances per mesh id (as named in the species).</summary>
    public Dictionary<string, List<FoliageInstance>> ByMesh { get; } = new();
    public int Count => ByMesh.Values.Sum(l => l.Count);
}

/// <summary>
/// Places every species of every set on a jittered grid (one candidate per spacing cell, so neighbours keep roughly
/// the spacing apart), filtered by band, height, slope, playable cells, cluster noise and the footprints of species
/// placed before it. Each species has its own seed, so retuning one leaves the others where they were.
/// </summary>
public static class FoliageStage
{
    /// <param name="occupied">Cells nothing may grow on (building footprints), indexed cz * Cells + cx; null for none.</param>
    public static FoliageResult Run(WorldData w, IReadOnlyList<FoliageSetDefinition> sets, uint seed, bool[] occupied = null)
    {
        var result = new FoliageResult();
        var claimed = new Footprints();
        var bandIndex = w.BandNames.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);

        foreach (var set in sets)
            foreach (var sp in set.Species)
            {
                if (sp.Spacing <= 0 || sp.Density <= 0) continue;
                uint s = Seeds.Derive(seed, $"foliage:{set.Id}:{sp.Id}");
                var cluster = new Noise(Seeds.Mix(s ^ 0x5bd1e995u));
                var allowed = sp.Bands.Count == 0 ? null : sp.Bands.Where(bandIndex.ContainsKey).Select(b => (byte)bandIndex[b]).ToHashSet();
                float cosMax = MathF.Cos(sp.MaxSlope * MathF.PI / 180f);
                float p = Math.Min(1f, sp.Density / 100f * sp.Spacing * sp.Spacing);
                if (!result.ByMesh.TryGetValue(sp.Mesh, out var list)) result.ByMesh[sp.Mesh] = list = new List<FoliageInstance>();

                int n = (int)MathF.Ceiling(w.Size / sp.Spacing);
                for (int gz = 0; gz < n; gz++)
                    for (int gx = 0; gx < n; gx++)
                    {
                        uint h = Seeds.Hash(gx, gz, s);
                        if (Unit(h) >= p) continue;
                        float x = (gx + Unit(Seeds.Mix(h + 1))) * sp.Spacing, z = (gz + Unit(Seeds.Mix(h + 2))) * sp.Spacing;
                        if (x >= w.Size || z >= w.Size) continue;

                        if (sp.Clusters.Threshold > -1f)
                        {
                            float c = cluster.Fbm(x * sp.Clusters.Frequency, z * sp.Clusters.Frequency, 3);
                            float keep = Noise.SmoothStep(sp.Clusters.Threshold, sp.Clusters.Threshold + MathF.Max(1e-3f, sp.Clusters.Softness), c);
                            if (Unit(Seeds.Mix(h + 3)) >= keep) continue;
                        }

                        int sx = Math.Min(w.Samples - 1, (int)MathF.Round(x / w.SampleSpacing)), sz = Math.Min(w.Samples - 1, (int)MathF.Round(z / w.SampleSpacing));
                        if (allowed != null && !allowed.Contains(w.Band[w.Index(sx, sz)])) continue;
                        float y = w.HeightAt(x, z);
                        if (y < sp.Height[0] || y > sp.Height[1]) continue;
                        if (NormalY(w, sx, sz) < cosMax) continue;
                        if (sp.AvoidPlayable && NearPlayable(w, x, z, sp.PlayableMargin)) continue;
                        if (claimed.Blocked(x, z)) continue;
                        if (occupied != null && occupied[Math.Min(w.Cells - 1, (int)(z / w.CellSize)) * w.Cells + Math.Min(w.Cells - 1, (int)(x / w.CellSize))]) continue;

                        float scale = Lerp(sp.Scale[0], sp.Scale[^1], Unit(Seeds.Mix(h + 4)));
                        if (sp.PlayableScale != 1f && NearPlayable(w, x, z, 0f)) scale *= sp.PlayableScale;
                        float tilt = sp.Tilt * MathF.PI / 180f;
                        list.Add(new FoliageInstance(x, y, z,
                            Unit(Seeds.Mix(h + 5)) * MathF.Tau,
                            (Unit(Seeds.Mix(h + 6)) * 2 - 1) * tilt, (Unit(Seeds.Mix(h + 7)) * 2 - 1) * tilt,
                            scale,
                            1f + (Unit(Seeds.Mix(h + 8)) * 2 - 1) * sp.TintJitter));
                        if (sp.Footprint > 0) claimed.Add(x, z, sp.Footprint * scale);
                    }
            }
        return result;
    }

    static bool NearPlayable(WorldData w, float x, float z, float margin)
    {
        int c0x = (int)MathF.Floor((x - margin) / w.CellSize), c1x = (int)MathF.Floor((x + margin) / w.CellSize);
        int c0z = (int)MathF.Floor((z - margin) / w.CellSize), c1z = (int)MathF.Floor((z + margin) / w.CellSize);
        for (int cz = Math.Max(0, c0z); cz <= Math.Min(w.Cells - 1, c1z); cz++)
            for (int cx = Math.Max(0, c0x); cx <= Math.Min(w.Cells - 1, c1x); cx++)
                if (w.IsPlayable(cx, cz)) return true;
        return false;
    }

    static float NormalY(WorldData w, int x, int z)
    {
        int xl = Math.Max(0, x - 1), xr = Math.Min(w.Samples - 1, x + 1);
        int zu = Math.Max(0, z - 1), zd = Math.Min(w.Samples - 1, z + 1);
        float dx = (w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing);
        float dz = (w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing);
        return 1f / MathF.Sqrt(dx * dx + dz * dz + 1f);
    }

    static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Circles claimed by earlier species, bucketed on a coarse grid.</summary>
    sealed class Footprints
    {
        const float Bucket = 4f;
        readonly Dictionary<(int, int), List<(float x, float z, float r)>> _cells = new();
        float _maxR;

        public void Add(float x, float z, float r)
        {
            var key = ((int)MathF.Floor(x / Bucket), (int)MathF.Floor(z / Bucket));
            if (!_cells.TryGetValue(key, out var l)) _cells[key] = l = new();
            l.Add((x, z, r));
            _maxR = MathF.Max(_maxR, r);
        }

        public bool Blocked(float x, float z)
        {
            if (_cells.Count == 0) return false;
            int reach = (int)MathF.Ceiling(_maxR / Bucket);
            int bx = (int)MathF.Floor(x / Bucket), bz = (int)MathF.Floor(z / Bucket);
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (_cells.TryGetValue((bx + dx, bz + dz), out var l))
                        foreach (var (cx, cz, r) in l)
                            if ((cx - x) * (cx - x) + (cz - z) * (cz - z) < r * r) return true;
            return false;
        }
    }
}
