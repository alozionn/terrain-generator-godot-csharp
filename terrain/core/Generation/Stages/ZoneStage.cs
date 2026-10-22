using System;
using System.Collections.Generic;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Playable zones are part of the terrain, not cut into it. A zone is an area (a rectangle of cells with rounded corners
/// and a ragged, noise-wandered outline) that gently levels the ground under it: heights are pulled toward a smoothed
/// copy of the terrain, held inside the zone's height window, and the pull fades out over the blend distance. The land
/// keeps its own gentle rolls. Then each cell in the area qualifies on its own ground: it is buildable when it lies in
/// the height window and is no steeper than <see cref="ZoneDefinition.MaxSlope"/>, and only the largest connected patch
/// of such cells is kept, so the board's outline follows the land. Later zones win where zones overlap, and a zone's
/// levelling never disturbs another zone's fully levelled ground. A zone that must touch the sea never lifts water, so
/// it keeps the natural coastline.
/// </summary>
public static class ZoneStage
{
    public static void Run(WorldData w, IReadOnlyList<ZoneDefinition> zones, uint seed)
    {
        var ids = new string[zones.Count];
        // samples some zone levelled fully: other zones' blends leave them alone
        var core = new bool[w.Height.Length];
        for (int zi = 0; zi < zones.Count; zi++)
        {
            var zone = zones[zi];
            ids[zi] = zone.Id;
            float cell = w.CellSize;
            float x0 = zone.Origin[0] * cell, z0 = zone.Origin[1] * cell;
            float x1 = (zone.Origin[0] + zone.Cells[0]) * cell, z1 = (zone.Origin[1] + zone.Cells[1]) * cell;
            float lo = zone.Height[0], hi = zone.Height[1];
            float blend = MathF.Max(0f, zone.Blend);
            float corner = Math.Clamp(zone.Corner * cell, 0f, MathF.Min(x1 - x0, z1 - z0) * 0.5f);
            float cxm = (x0 + x1) * 0.5f, czm = (z0 + z1) * 0.5f, hx = (x1 - x0) * 0.5f, hz = (z1 - z0) * 0.5f;
            var edge = new Noise(Seeds.Derive(seed, "zone-edge:" + zone.Id));
            // signed distance to the zone's outline, <= 0 inside: a rounded rectangle whose edge wanders by up to `ragged` metres
            float Outline(float px, float pz)
            {
                float qx = MathF.Abs(px - cxm) - (hx - corner), qz = MathF.Abs(pz - czm) - (hz - corner);
                float ox = MathF.Max(qx, 0f), oz = MathF.Max(qz, 0f);
                float d = MathF.Sqrt(ox * ox + oz * oz) + MathF.Min(MathF.Max(qx, qz), 0f) - corner;
                return d + zone.Ragged * Math.Clamp(edge.Fbm(px * 0.04f, pz * 0.04f, 3) * 2f, -1f, 1f);
            }

            int reach = (int)MathF.Ceiling((blend + zone.Ragged + zone.Smooth) / w.SampleSpacing) + 1;
            int sx0 = Math.Max(0, (int)MathF.Floor(x0 / w.SampleSpacing) - reach), sx1 = Math.Min(w.Samples - 1, (int)MathF.Ceiling(x1 / w.SampleSpacing) + reach);
            int sz0 = Math.Max(0, (int)MathF.Floor(z0 / w.SampleSpacing) - reach), sz1 = Math.Min(w.Samples - 1, (int)MathF.Ceiling(z1 / w.SampleSpacing) + reach);
            var smooth = Smoothed(w, sx0, sz0, sx1, sz1, Math.Max(0, (int)MathF.Round(zone.Smooth / w.SampleSpacing)));
            int sw = sx1 - sx0 + 1;
            for (int z = sz0; z <= sz1; z++)
                for (int x = sx0; x <= sx1; x++)
                {
                    float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                    // levelled out to a cell diagonal past the outline, so every sample of a qualifying cell is in
                    float dist = MathF.Max(0f, Outline(px, pz) - cell * 0.71f);
                    float k = dist <= 1e-4f ? 1f : blend <= 0 ? 0f : Noise.SmoothStep(blend, 0f, dist);
                    k *= zone.Level;
                    if (k <= 0) continue;
                    int i = w.Index(x, z);
                    float target = Math.Clamp(smooth[(z - sz0) * sw + x - sx0], lo, hi);
                    // a sea zone never lifts water: the coast keeps its natural line
                    if (zone.TouchesSea && (w.Height[i] < 0f || (dist > 1e-4f && w.Height[i] < target))) continue;
                    if (k < 1f && core[i]) continue;
                    if (k >= 1f) core[i] = true;
                    w.Height[i] += (target - w.Height[i]) * k;
                    w.Ridge[i] *= 1f - k; // levelled ground has no gullies to paint
                }

            // qualify cells on their own ground, then keep the largest connected patch
            int ccx0 = Math.Max(0, zone.Origin[0]), ccz0 = Math.Max(0, zone.Origin[1]);
            int ccx1 = Math.Min(w.Cells, zone.Origin[0] + zone.Cells[0]), ccz1 = Math.Min(w.Cells, zone.Origin[1] + zone.Cells[1]);
            var good = new HashSet<(int, int)>();
            for (int cz = ccz0; cz < ccz1; cz++)
                for (int cx = ccx0; cx < ccx1; cx++)
                {
                    if (Outline((cx + 0.5f) * cell, (cz + 0.5f) * cell) > 0f) continue;
                    var (min, max) = w.CellRange(cx, cz);
                    if (min < lo - ConstraintStage.WindowTolerance || max > hi + ConstraintStage.WindowTolerance) continue;
                    if ((max - min) / cell > zone.MaxSlope) continue;
                    if (zone.TouchesSea && min < 0f) continue;
                    good.Add((cx, cz));
                }
            foreach (var (cx, cz) in Largest(good))
                w.CellZone[cz * w.Cells + cx] = (short)zi;
        }
        w.ZoneIds = ids;
    }

    /// <summary>Heights over a sample rectangle, box-blurred by <paramref name="radius"/> samples (separable).</summary>
    static float[] Smoothed(WorldData w, int x0, int z0, int x1, int z1, int radius)
    {
        int sw = x1 - x0 + 1, sh = z1 - z0 + 1;
        var a = new float[sw * sh];
        for (int z = 0; z < sh; z++)
            for (int x = 0; x < sw; x++)
                a[z * sw + x] = w.H(x0 + x, z0 + z);
        if (radius == 0) return a;
        var b = new float[a.Length];
        for (int z = 0; z < sh; z++)
            for (int x = 0; x < sw; x++)
            {
                float s = 0;
                for (int k = -radius; k <= radius; k++) s += w.H(Math.Clamp(x0 + x + k, 0, w.Samples - 1), z0 + z);
                b[z * sw + x] = s / (2 * radius + 1);
            }
        for (int z = 0; z < sh; z++)
            for (int x = 0; x < sw; x++)
            {
                float s = 0;
                for (int k = -radius; k <= radius; k++) s += b[Math.Clamp(z + k, 0, sh - 1) * sw + x];
                a[z * sw + x] = s / (2 * radius + 1);
            }
        return a;
    }

    /// <summary>The largest 4-connected group of cells.</summary>
    static List<(int, int)> Largest(HashSet<(int x, int z)> cells)
    {
        var best = new List<(int, int)>();
        var seen = new HashSet<(int, int)>();
        foreach (var start in cells)
        {
            if (!seen.Add(start)) continue;
            var group = new List<(int, int)>();
            var queue = new Queue<(int x, int z)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                group.Add(c);
                foreach (var n in new[] { (c.x + 1, c.z), (c.x - 1, c.z), (c.x, c.z + 1), (c.x, c.z - 1) })
                    if (cells.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
            if (group.Count > best.Count) best = group;
        }
        return best;
    }
}
