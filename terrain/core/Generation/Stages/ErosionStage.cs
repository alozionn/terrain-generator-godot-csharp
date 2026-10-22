using System;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>
/// Erosion as a filter, after runevision's "fast and gorgeous erosion filter" (blog.runevision.com, 2026-03): no water
/// is simulated. Each octave lays Phacelle stripes (cosine/sine waves from a few jittered cells, blended and
/// renormalised) that run down the current flow direction, so they read as gullies and ridges. Per sample:
/// <list type="bullet">
/// <item>A fade target says what the terrain is without gullies here: -1 in valleys, +1 on peaks (from altitude).</item>
/// <item>A mask says how much gully pattern is allowed: 0 on flat ground, 1 on steep slopes (eased).</item>
/// <item>Each octave's value is the fade target blended toward its stripes by the mask, and becomes the next fade
/// target; the mask then shrinks to where this octave's stripes are themselves sloped, so finer gullies sit on the
/// flanks of coarser ones instead of on their crests and troughs (raise <c>detail</c> to let them spread).</item>
/// <item>The flow direction for the next octave adds this octave's gully slopes, using only their sign so the gullies
/// act like extruded triangle waves and the finer ones branch cleanly.</item>
/// </list>
/// The summed offset, times gully weight and strength, is added to the heights. Low ground near the sea is left alone
/// and land is never cut below 0.4 m. Deterministic per seed; a few tens of milliseconds at board size.
/// </summary>
public static class ErosionStage
{
    public static void Run(WorldData w, ErosionDefinition e, uint seed)
    {
        if (e == null || e.Strength <= 0 || e.Octaves <= 0) return;
        int n = w.Samples;
        var h = w.Height;
        var gx = new float[h.Length];
        var gz = new float[h.Length];
        Gradient(w, gx, gz);

        float valley = e.ValleyHeight, peak = e.PeakHeight > valley ? e.PeakHeight : MathF.Max(valley + 1f, Max(h));
        var fade = new float[h.Length];
        var mask = new float[h.Length];
        var offset = new float[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            fade[i] = Math.Clamp((h[i] - valley) / (peak - valley) * 2f - 1f, -1f, 1f);
            float slope = MathF.Sqrt(gx[i] * gx[i] + gz[i] * gz[i]);
            mask[i] = EaseOut((slope - e.SlopeStart) / MathF.Max(1e-3f, e.AssumedSlope - e.SlopeStart));
        }

        var baseline = (float[])fade.Clone();
        float freq = e.Frequency, amp = 1f, total = 0f;
        float scale = e.Strength * e.GullyWeight;
        for (int o = 0; o < e.Octaves; o++)
        {
            uint s = Seeds.Mix(seed + (uint)o * 0x9e3779b9u);
            // the slope one unit of this octave's stripes makes, in metres per metre, for the flow-direction feedback
            float gullySlope = MathF.Tau * freq * amp * scale;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float len = MathF.Sqrt(gx[i] * gx[i] + gz[i] * gz[i]);
                    if (len < 1e-6f) { offset[i] += fade[i] * amp; mask[i] = 0; continue; }
                    // across the flow: stripes vary along this, so they run downhill
                    float ax = -gz[i] / len, az = gx[i] / len;
                    var (c, sn) = Phacelle(x * w.SampleSpacing, z * w.SampleSpacing, ax, az, freq, e.CellScale, s);
                    float g = fade[i] + (c - fade[i]) * mask[i];
                    offset[i] += g * amp;
                    fade[i] = g;
                    mask[i] = PowInv(mask[i], e.Detail) * EaseOut(MathF.Abs(sn));
                    // d/d(across) of cos(phase) is -sin: the gully slope points across the flow, by the sign only
                    float push = -MathF.Sign(sn) * gullySlope;
                    gx[i] += ax * push;
                    gz[i] += az * push;
                }
            total += amp;
            amp *= e.Gain;
            freq *= e.Lacunarity;
        }

        // the raw pattern, relative to the starting fade target so ground with no gullies keeps its height. Crests are
        // only partly raised (they turned 1 m sample spikes into pyramids), and the field is softened once so ridges
        // come out rounded rather than knife-edged.
        var delta = new float[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            float d = (offset[i] - baseline[i] * total) / total;
            delta[i] = d > 0 ? d * e.CrestLift : d;
        }
        delta = Soften(delta, n);
        for (int i = 0; i < h.Length; i++)
        {
            // keep the coast and anything near sea level as the layout made it
            float land = Noise.SmoothStep(valley, valley + 2f, h[i]);
            if (land <= 0) continue;
            float next = h[i] + delta[i] * total * scale * land;
            h[i] = MathF.Max(next, MathF.Min(h[i], 0.4f));
            // the same offset, normalised, is the ridge map the ground painting reads
            w.Ridge[i] = Math.Clamp((offset[i] - baseline[i] * total) / total * 1.5f, -1f, 1f) * land;
        }
    }

    /// <summary>One pass of a 3x3 binomial blur.</summary>
    static float[] Soften(float[] f, int n)
    {
        var o = new float[f.Length];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0, wsum = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        float k = (dx == 0 ? 2 : 1) * (dz == 0 ? 2 : 1);
                        sum += f[zz * n + xx] * k;
                        wsum += k;
                    }
                o[z * n + x] = sum / wsum;
            }
        return o;
    }

    /// <summary>
    /// Phacelle noise: stripes across direction (ax, az) at <paramref name="freq"/> waves per metre. Each of the 4x4
    /// nearest jittered cells contributes a cosine and a sine of its own phase, weighted by a smooth bump; the blended
    /// pair is stretched (x2, clamped to unit length) so blends of out-of-phase waves keep their amplitude without the
    /// spikes of full normalisation. Returns (cos, sin) of the blended phase.
    /// </summary>
    static (float c, float s) Phacelle(float px, float pz, float ax, float az, float freq, float cellScale, uint seed)
    {
        float size = cellScale / freq;
        int cx = (int)MathF.Floor(px / size - 1.5f), cz = (int)MathF.Floor(pz / size - 1.5f);
        float sc = 0, ss = 0, sw = 0, reach2 = 4f * size * size;
        for (int dz = 0; dz < 4; dz++)
            for (int dx = 0; dx < 4; dx++)
            {
                uint hash = Seeds.Hash(cx + dx, cz + dz, seed);
                float ox = (cx + dx + Unit(hash)) * size, oz = (cz + dz + Unit(Seeds.Mix(hash + 1))) * size;
                float rx = px - ox, rz = pz - oz;
                float t = 1f - (rx * rx + rz * rz) / reach2;
                if (t <= 0) continue;
                float wgt = t * t;
                float phase = (rx * ax + rz * az) * freq * MathF.Tau;
                sc += wgt * MathF.Cos(phase);
                ss += wgt * MathF.Sin(phase);
                sw += wgt;
            }
        if (sw <= 0) return (1f, 0f);
        sc /= sw;
        ss /= sw;
        float len = MathF.Sqrt(sc * sc + ss * ss);
        if (len < 1e-6f) return (0f, 0f);
        float k = MathF.Min(1f, 2f * len) / len;
        return (sc * k, ss * k);
    }

    static float EaseOut(float t)
    {
        float v = 1f - Math.Clamp(t, 0f, 1f);
        return 1f - v * v;
    }

    static float PowInv(float t, float p) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), p);

    static float Max(float[] a)
    {
        float m = float.MinValue;
        foreach (float v in a) m = MathF.Max(m, v);
        return m;
    }

    static void Gradient(WorldData w, float[] gx, float[] gz)
    {
        int n = w.Samples;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int xl = Math.Max(0, x - 1), xr = Math.Min(n - 1, x + 1), zu = Math.Max(0, z - 1), zd = Math.Min(n - 1, z + 1);
                gx[z * n + x] = (w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing);
                gz[z * n + x] = (w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing);
            }
    }

    static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
}
