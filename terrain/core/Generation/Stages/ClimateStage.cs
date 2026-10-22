using System;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Temperature and moisture per sample, 0..1. Runs on the finished heights: temperature drops with altitude, moisture
/// rises near the coast. Both get slow noise of their own, so neighbouring regions with the same height still differ.
/// </summary>
public static class ClimateStage
{
    public static void Run(WorldData w, ClimateDefinition c, uint seed)
    {
        var tNoise = new Noise(Seeds.Mix(seed ^ 0x1b873593u));
        var mNoise = new Noise(Seeds.Mix(seed ^ 0xcc9e2d51u));
        var coast = SeaDistance(w);
        float gx = c.Gradient is { Length: > 0 } ? c.Gradient[0] : 0f, gz = c.Gradient is { Length: > 1 } ? c.Gradient[1] : 0f;
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
            {
                int i = w.Index(x, z);
                float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                float u = (float)x / (w.Samples - 1) - 0.5f, v = (float)z / (w.Samples - 1) - 0.5f;
                float t = c.Temperature + gx * u + gz * v - c.Lapse * MathF.Max(0f, w.Height[i])
                        + c.Noise * Spread(tNoise.Fbm(px * c.Frequency, pz * c.Frequency, 3));
                float wet = c.CoastReach > 0 ? MathF.Max(0f, 1f - coast[i] * w.SampleSpacing / c.CoastReach) : 0f;
                float m = c.Moisture + c.Coast * wet * wet + c.MoistureNoise * Spread(mNoise.Fbm(px * c.Frequency, pz * c.Frequency, 3));
                w.Temperature[i] = Math.Clamp(t, 0f, 1f);
                w.Moisture[i] = Math.Clamp(m, 0f, 1f);
            }
    }

    /// <summary>fBm rarely strays far from 0; stretch it so the noise amounts mean roughly ± that much.</summary>
    static float Spread(float fbm) => Math.Clamp(fbm * 2.2f, -1f, 1f);

    /// <summary>Approximate distance in samples to the nearest sample below sea level (two-pass chamfer).</summary>
    public static float[] SeaDistance(WorldData w)
    {
        int n = w.Samples;
        var d = new float[n * n];
        const float far = 1e6f, diag = 1.41421356f;
        for (int i = 0; i < d.Length; i++) d[i] = w.Height[i] < 0 ? 0 : far;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                if (x > 0) d[i] = MathF.Min(d[i], d[i - 1] + 1);
                if (z > 0)
                {
                    d[i] = MathF.Min(d[i], d[i - n] + 1);
                    if (x > 0) d[i] = MathF.Min(d[i], d[i - n - 1] + diag);
                    if (x < n - 1) d[i] = MathF.Min(d[i], d[i - n + 1] + diag);
                }
            }
        for (int z = n - 1; z >= 0; z--)
            for (int x = n - 1; x >= 0; x--)
            {
                int i = z * n + x;
                if (x < n - 1) d[i] = MathF.Min(d[i], d[i + 1] + 1);
                if (z < n - 1)
                {
                    d[i] = MathF.Min(d[i], d[i + n] + 1);
                    if (x < n - 1) d[i] = MathF.Min(d[i], d[i + n + 1] + diag);
                    if (x > 0) d[i] = MathF.Min(d[i], d[i + n - 1] + diag);
                }
            }
        return d;
    }
}
