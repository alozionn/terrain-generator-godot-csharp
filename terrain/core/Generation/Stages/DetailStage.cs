using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Domain-warped fBm over everything. On land the detail only lifts (0..amplitude), so it never punches ponds into
/// the authored shape; out at sea it is a gentle ±half amplitude so the seabed isn't flat.
/// </summary>
public static class DetailStage
{
    public static void Run(WorldData w, DetailDefinition d, uint seed)
    {
        if (d.Amplitude == 0) return;
        var noise = new Noise(seed);
        var warp = new Noise(Seeds.Mix(seed + 1));
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
            {
                float px = x * w.SampleSpacing * d.Frequency, pz = z * w.SampleSpacing * d.Frequency;
                if (d.Warp != 0)
                {
                    float wf = d.Warp * d.Frequency;
                    px += wf * warp.Fbm(px * 0.5f, pz * 0.5f, 2);
                    pz += wf * warp.Fbm(px * 0.5f + 5.2f, pz * 0.5f + 1.3f, 2);
                }
                float n = noise.Fbm(px, pz, d.Octaves);
                int i = w.Index(x, z);
                float land = w.LandMask[i];
                w.Height[i] += d.Amplitude * (0.5f * n * (1 - land) + (0.5f + 0.5f * n) * land);
            }
    }
}
