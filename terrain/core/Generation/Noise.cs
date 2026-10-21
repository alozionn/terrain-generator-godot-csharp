using System;

namespace Terrain.Core.Generation;

/// <summary>Seeded 2D gradient noise and fBm. Plain C# so the generator gives the same result in tests and in Godot.</summary>
public readonly struct Noise
{
    readonly uint _seed;

    public Noise(uint seed) => _seed = seed;

    /// <summary>Gradient noise, roughly -1..1.</summary>
    public float Sample(float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float a = Dot(ix, iy, fx, fy), b = Dot(ix + 1, iy, fx - 1, fy);
        float c = Dot(ix, iy + 1, fx, fy - 1), d = Dot(ix + 1, iy + 1, fx - 1, fy - 1);
        float ux = Fade(fx), uy = Fade(fy);
        return 1.4142135f * Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    /// <summary>Fractal sum normalised to roughly -1..1.</summary>
    public float Fbm(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0, amp = 1, norm = 0, f = 1;
        for (int i = 0; i < Math.Max(1, octaves); i++)
        {
            // each octave is offset so the lattices never line up
            sum += amp * Sample(x * f + i * 17.31f, y * f - i * 11.73f);
            norm += amp;
            amp *= gain;
            f *= lacunarity;
        }
        return sum / norm;
    }

    float Dot(int ix, int iy, float dx, float dy)
    {
        float angle = Seeds.Hash(ix, iy, _seed) * (MathF.Tau / 4294967296f);
        return MathF.Cos(angle) * dx + MathF.Sin(angle) * dy;
    }

    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
