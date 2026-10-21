using System;
using System.Collections.Generic;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>Authored height stamps. Raising stamps add, valleys subtract, coves carve down to their depth and turn the area into sea.</summary>
public static class StampStage
{
    public static void Run(WorldData w, IReadOnlyList<StampDefinition> stamps, uint seed)
    {
        var noise = new Noise(seed);
        foreach (var s in stamps)
        {
            if (s.Radius <= 0 || s.At is not { Length: 2 }) continue;
            float cx = s.At[0] * w.Size, cz = s.At[1] * w.Size;
            float reach = s.Radius * 1.3f; // room for the wobble
            int x0 = Math.Max(0, (int)((cx - reach) / w.SampleSpacing)), x1 = Math.Min(w.Samples - 1, (int)((cx + reach) / w.SampleSpacing) + 1);
            int z0 = Math.Max(0, (int)((cz - reach) / w.SampleSpacing)), z1 = Math.Min(w.Samples - 1, (int)((cz + reach) / w.SampleSpacing) + 1);
            float wobbleFreq = 3f / s.Radius;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                    float r = MathF.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz)) / s.Radius;
                    r += 0.18f * noise.Fbm(px * wobbleFreq, pz * wobbleFreq, 2); // organic outline, not a perfect circle
                    if (r >= 1f) continue;
                    int i = w.Index(x, z);
                    switch (s.Type)
                    {
                        case StampKind.Mountain:
                            w.Height[i] += s.Height * MathF.Pow(Noise.SmoothStep(1f, 0f, r), 1.6f);
                            break;
                        case StampKind.Hill:
                            w.Height[i] += s.Height * Noise.SmoothStep(1f, 0f, r);
                            break;
                        case StampKind.Plateau:
                            w.Height[i] += s.Height * Noise.SmoothStep(1f, 0.6f, r);
                            break;
                        case StampKind.Valley:
                            w.Height[i] -= s.Height * Noise.SmoothStep(1f, 0f, r);
                            break;
                        case StampKind.Cove:
                            float k = Noise.SmoothStep(1f, 0.35f, r);
                            w.Height[i] += (MathF.Min(w.Height[i], -s.Height) - w.Height[i]) * k;
                            w.LandMask[i] *= 1f - k;
                            break;
                    }
                }
        }
    }
}
