using System;
using System.Collections.Generic;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>Checks the finished world against the level's rules. Failures come back as readable issues, never exceptions.</summary>
public static class ConstraintStage
{
    /// <summary>Allowed overshoot of a zone's height window, in metres.</summary>
    public const float WindowTolerance = 0.02f;

    public static List<string> Run(WorldData w, IReadOnlyList<ZoneDefinition> zones)
    {
        var issues = new List<string>();
        for (int zi = 0; zi < zones.Count; zi++)
        {
            var zone = zones[zi];
            int ox = zone.Origin[0], oz = zone.Origin[1];
            if (ox < 0 || oz < 0 || ox + zone.Cells[0] > w.Cells || oz + zone.Cells[1] > w.Cells)
                issues.Add($"zone '{zone.Id}' reaches outside the {w.Cells}x{w.Cells} cell map");

            int outside = 0, owned = 0;
            bool seaFound = false;
            for (int cz = Math.Max(0, oz); cz < Math.Min(w.Cells, oz + zone.Cells[1]); cz++)
                for (int cx = Math.Max(0, ox); cx < Math.Min(w.Cells, ox + zone.Cells[0]); cx++)
                {
                    if (w.CellZone[cz * w.Cells + cx] != zi) continue; // overwritten by a later zone
                    owned++;
                    var (lo, hi) = w.CellRange(cx, cz);
                    if (lo < zone.Height[0] - WindowTolerance || hi > zone.Height[1] + WindowTolerance) outside++;
                    if (zone.TouchesSea && !seaFound) seaFound = BordersSea(w, cx, cz, zi);
                }
            if (owned == 0) issues.Add($"zone '{zone.Id}' has no cells");
            if (outside > 0) issues.Add($"zone '{zone.Id}': {outside} of {owned} cells leave the height window [{zone.Height[0]}, {zone.Height[1]}]");
            if (zone.TouchesSea && owned > 0 && !seaFound) issues.Add($"zone '{zone.Id}' must touch the sea but no cell borders water");
        }
        return issues;
    }

    static bool BordersSea(WorldData w, int cx, int cz, int zone)
    {
        ReadOnlySpan<(int dx, int dz)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        foreach (var (dx, dz) in dirs)
        {
            int nx = cx + dx, nz = cz + dz;
            if (nx < 0 || nz < 0 || nx >= w.Cells || nz >= w.Cells) continue;
            if (w.CellZone[nz * w.Cells + nx] == zone) continue;
            if (w.CellHeight(nx, nz) < 0f) return true;
        }
        return false;
    }
}
