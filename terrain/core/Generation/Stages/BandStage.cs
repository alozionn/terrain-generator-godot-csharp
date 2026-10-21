using System.Linq;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>Assigns each sample the highest palette band whose start lies at or below its height.</summary>
public static class BandStage
{
    public static void Run(WorldData w, PaletteDefinition palette)
    {
        var bands = palette.Bands.OrderBy(b => b.From).ToArray();
        w.BandNames = bands.Select(b => b.Name).ToArray();
        for (int i = 0; i < w.Height.Length; i++)
            w.Band[i] = Lookup(bands, w.Height[i]);
    }

    public static byte Lookup(BandDefinition[] sortedBands, float h)
    {
        byte band = 0;
        for (int b = 1; b < sortedBands.Length && b < 256; b++)
            if (h >= sortedBands[b].From) band = (byte)b;
        return band;
    }
}
