using System;
using System.Collections.Generic;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Pulls each playable zone into its height window as a gently rolling surface, blended back into the terrain over
/// the zone's blend distance, and marks its cells. Later zones win where zones overlap. A zone that must touch the
/// sea never lifts lower ground around it, so the water next to it stays water (a quay edge). Zones can have rounded
/// corners (cells outside the rounded outline are not playable), and the outer edge of the blend wanders with noise.
/// </summary>
public static class ZoneStage
{
    public static void Run(WorldData w, IReadOnlyList<ZoneDefinition> zones, uint seed)
    {
        var ids = new string[zones.Count];
        // samples some zone pulled fully into its window: other zones' blends leave them alone
        var core = new bool[w.Height.Length];
        for (int zi = 0; zi < zones.Count; zi++)
        {
            var zone = zones[zi];
            ids[zi] = zone.Id;
            var roll = new Noise(Seeds.Derive(seed, "zone:" + zone.Id));
            float cell = w.CellSize;
            float x0 = zone.Origin[0] * cell, z0 = zone.Origin[1] * cell;
            float x1 = (zone.Origin[0] + zone.Cells[0]) * cell, z1 = (zone.Origin[1] + zone.Cells[1]) * cell;
            float lo = zone.Height[0], hi = zone.Height[1];
            float blend = MathF.Max(0f, zone.Blend);
            float corner = Math.Clamp(zone.Corner * cell, 0f, MathF.Min(x1 - x0, z1 - z0) * 0.5f);
            float cxm = (x0 + x1) * 0.5f, czm = (z0 + z1) * 0.5f, hx = (x1 - x0) * 0.5f, hz = (z1 - z0) * 0.5f;
            // signed distance to the zone's rounded rectangle: <= 0 inside
            float Distance(float px, float pz)
            {
                float qx = MathF.Abs(px - cxm) - (hx - corner), qz = MathF.Abs(pz - czm) - (hz - corner);
                float ox = MathF.Max(qx, 0f), oz = MathF.Max(qz, 0f);
                return MathF.Sqrt(ox * ox + oz * oz) + MathF.Min(MathF.Max(qx, qz), 0f) - corner;
            }
            var edge = new Noise(Seeds.Derive(seed, "zone-edge:" + zone.Id));

            int sx0 = Math.Max(0, (int)MathF.Floor((x0 - blend * 1.4f) / w.SampleSpacing)), sx1 = Math.Min(w.Samples - 1, (int)MathF.Ceiling((x1 + blend * 1.4f) / w.SampleSpacing));
            int sz0 = Math.Max(0, (int)MathF.Floor((z0 - blend * 1.4f) / w.SampleSpacing)), sz1 = Math.Min(w.Samples - 1, (int)MathF.Ceiling((z1 + blend * 1.4f) / w.SampleSpacing));
            for (int z = sz0; z <= sz1; z++)
                for (int x = sx0; x <= sx1; x++)
                {
                    float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                    // flattened out to half a cell diagonal past the outline, so every sample of a playable cell is in
                    float dist = MathF.Max(0f, Distance(px, pz) - (corner > 0 ? cell * 0.71f : 0f));
                    // the blend's outer edge wanders, so the flattened ground doesn't trace the zone's outline
                    if (dist > 0) dist = MathF.Max(1e-3f, dist + blend * 0.35f * edge.Fbm(px * 0.05f, pz * 0.05f, 2) * 2f);
                    float k = dist <= 1e-4f ? 1f : blend <= 0 ? 0f : Noise.SmoothStep(blend, 0f, dist);
                    if (k <= 0) continue;
                    float target = lo + (hi - lo) * (0.5f + 0.5f * roll.Fbm(px * zone.RollFrequency, pz * zone.RollFrequency, 2));
                    int i = w.Index(x, z);
                    // a sea zone never lifts water: outside it keeps the quay edge, inside the coast keeps its natural line
                    if (zone.TouchesSea && (w.Height[i] < 0f || (dist > 1e-4f && w.Height[i] < target))) continue;
                    if (k < 1f && core[i]) continue;
                    if (k >= 1f) core[i] = true;
                    w.Height[i] += (target - w.Height[i]) * k;
                    w.Ridge[i] *= 1f - k; // flattened ground has no gullies to paint
                }

            // a cell is playable when its centre is inside the rounded outline (and, for a sea zone, it is all land:
            // buildings there may reach out over the water, but their cells stand on the shore)
            int cx1 = Math.Min(w.Cells, zone.Origin[0] + zone.Cells[0]), cz1 = Math.Min(w.Cells, zone.Origin[1] + zone.Cells[1]);
            for (int cz = Math.Max(0, zone.Origin[1]); cz < cz1; cz++)
                for (int cx = Math.Max(0, zone.Origin[0]); cx < cx1; cx++)
                    if (Distance((cx + 0.5f) * cell, (cz + 0.5f) * cell) <= 0f && (!zone.TouchesSea || w.CellRange(cx, cz).min >= 0f))
                        w.CellZone[cz * w.Cells + cx] = (short)zi;
        }
        w.ZoneIds = ids;
    }
}
