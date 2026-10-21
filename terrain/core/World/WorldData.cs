#nullable disable
using System;
using System.Collections.Generic;

namespace Terrain.Core.World;

/// <summary>
/// The generated world: a square grid of height samples plus per-sample and per-cell layers. This is the hub the
/// generator fills, the terrain node draws and gameplay reads. Samples are row-major, index = z * Samples + x;
/// sample (x, z) sits at (x * SampleSpacing, z * SampleSpacing) metres.
/// </summary>
public sealed class WorldData
{
    public WorldData(int samples, float sampleSpacing, int samplesPerCell)
    {
        if (samples < 2 || samplesPerCell < 1) throw new ArgumentOutOfRangeException(nameof(samples));
        Samples = samples;
        SampleSpacing = sampleSpacing;
        SamplesPerCell = samplesPerCell;
        Cells = (samples - 1) / samplesPerCell;
        Height = new float[samples * samples];
        LandMask = new float[samples * samples];
        Band = new byte[samples * samples];
        CellZone = new short[Cells * Cells];
        Array.Fill(CellZone, (short)-1);
    }

    /// <summary>Samples per side.</summary>
    public int Samples { get; }
    public float SampleSpacing { get; }
    public int SamplesPerCell { get; }
    public float CellSize => SamplesPerCell * SampleSpacing;
    /// <summary>Whole cells per side.</summary>
    public int Cells { get; }
    public float Size => (Samples - 1) * SampleSpacing;

    /// <summary>Height in metres; y = 0 is sea level.</summary>
    public float[] Height { get; }
    /// <summary>0 = open sea, 1 = island interior. Written by the shape stage, read by later stages.</summary>
    public float[] LandMask { get; }
    /// <summary>Palette band index per sample.</summary>
    public byte[] Band { get; }
    /// <summary>Zone index per cell, -1 outside every zone (decor).</summary>
    public short[] CellZone { get; }
    public string[] ZoneIds { get; set; } = Array.Empty<string>();
    public string[] BandNames { get; set; } = Array.Empty<string>();

    public int Index(int x, int z) => z * Samples + x;
    public float H(int x, int z) => Height[z * Samples + x];

    public bool IsPlayable(int cx, int cz) => CellZone[cz * Cells + cx] >= 0;

    /// <summary>Mean height of every sample on and inside the cell's border.</summary>
    public float CellHeight(int cx, int cz)
    {
        int x0 = cx * SamplesPerCell, z0 = cz * SamplesPerCell;
        float sum = 0;
        for (int z = z0; z <= z0 + SamplesPerCell; z++)
            for (int x = x0; x <= x0 + SamplesPerCell; x++)
                sum += Height[z * Samples + x];
        return sum / ((SamplesPerCell + 1) * (SamplesPerCell + 1));
    }

    /// <summary>Lowest and highest sample on the cell, which is what a building footprint has to sit on.</summary>
    public (float min, float max) CellRange(int cx, int cz)
    {
        int x0 = cx * SamplesPerCell, z0 = cz * SamplesPerCell;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int z = z0; z <= z0 + SamplesPerCell; z++)
            for (int x = x0; x <= x0 + SamplesPerCell; x++)
            {
                float h = Height[z * Samples + x];
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
        return (lo, hi);
    }

    /// <summary>Per-cell flags for the given footprints (x, z, sizeX, sizeZ), clipped to the map.</summary>
    public bool[] CellMask(IEnumerable<(int x, int z, int sx, int sz)> footprints)
    {
        var mask = new bool[Cells * Cells];
        foreach (var (x0, z0, sx, sz) in footprints)
            for (int z = Math.Max(0, z0); z < Math.Min(Cells, z0 + sz); z++)
                for (int x = Math.Max(0, x0); x < Math.Min(Cells, x0 + sx); x++)
                    mask[z * Cells + x] = true;
        return mask;
    }

    /// <summary>Lowest and highest ground under a footprint of cells (clipped to the map).</summary>
    public (float min, float max) FootprintRange(int cx, int cz, int sizeX, int sizeZ)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int z = Math.Max(0, cz); z < Math.Min(Cells, cz + sizeZ); z++)
            for (int x = Math.Max(0, cx); x < Math.Min(Cells, cx + sizeX); x++)
            {
                var (a, b) = CellRange(x, z);
                lo = MathF.Min(lo, a);
                hi = MathF.Max(hi, b);
            }
        return (lo, hi);
    }

    /// <summary>Band of the cell centre sample (cells span an even number of samples, so the centre may fall between them).</summary>
    public byte CellBand(int cx, int cz)
    {
        int x = Math.Min(cx * SamplesPerCell + SamplesPerCell / 2, Samples - 1);
        int z = Math.Min(cz * SamplesPerCell + SamplesPerCell / 2, Samples - 1);
        return Band[z * Samples + x];
    }

    /// <summary>Bilinear height at a position in metres, clamped to the map.</summary>
    public float HeightAt(float xm, float zm)
    {
        float fx = Math.Clamp(xm / SampleSpacing, 0, Samples - 1.001f);
        float fz = Math.Clamp(zm / SampleSpacing, 0, Samples - 1.001f);
        int x = (int)fx, z = (int)fz;
        float tx = fx - x, tz = fz - z;
        float a = H(x, z), b = H(x + 1, z), c = H(x, z + 1), d = H(x + 1, z + 1);
        return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
    }
}
