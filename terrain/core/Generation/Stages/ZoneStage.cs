using System;
using System.Collections.Generic;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Pulls each playable zone into its height window as a gently rolling surface, blended back into the terrain over
/// the zone's blend distance, and marks its cells. Later zones win where zones overlap. A zone that must touch the
/// sea never lifts lower ground around it, so the water next to it stays water (a quay edge).
/// </summary>
public static class ZoneStage
{
    public static void Run(WorldData w, IReadOnlyList<ZoneDefinition> zones, uint seed)
    {
        var ids = new string[zones.Count];
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

            int sx0 = Math.Max(0, (int)MathF.Floor((x0 - blend) / w.SampleSpacing)), sx1 = Math.Min(w.Samples - 1, (int)MathF.Ceiling((x1 + blend) / w.SampleSpacing));
            int sz0 = Math.Max(0, (int)MathF.Floor((z0 - blend) / w.SampleSpacing)), sz1 = Math.Min(w.Samples - 1, (int)MathF.Ceiling((z1 + blend) / w.SampleSpacing));
            for (int z = sz0; z <= sz1; z++)
                for (int x = sx0; x <= sx1; x++)
                {
                    float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                    float dx = MathF.Max(MathF.Max(x0 - px, px - x1), 0f), dz = MathF.Max(MathF.Max(z0 - pz, pz - z1), 0f);
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    float k = dist <= 1e-4f ? 1f : blend <= 0 ? 0f : Noise.SmoothStep(blend, 0f, dist);
                    if (k <= 0) continue;
                    float target = lo + (hi - lo) * (0.5f + 0.5f * roll.Fbm(px * zone.RollFrequency, pz * zone.RollFrequency, 2));
                    int i = w.Index(x, z);
                    if (zone.TouchesSea && dist > 1e-4f && w.Height[i] < target) continue;
                    w.Height[i] += (target - w.Height[i]) * k;
                }

            int cx1 = Math.Min(w.Cells, zone.Origin[0] + zone.Cells[0]), cz1 = Math.Min(w.Cells, zone.Origin[1] + zone.Cells[1]);
            for (int cz = Math.Max(0, zone.Origin[1]); cz < cz1; cz++)
                for (int cx = Math.Max(0, zone.Origin[0]); cx < cx1; cx++)
                    w.CellZone[cz * w.Cells + cx] = (short)zi;
        }
        w.ZoneIds = ids;
    }
}
