using System;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation.Stages;

/// <summary>Base land/sea shape: an island falloff with a wobbling coastline, or flat land to the edges.</summary>
public static class ShapeStage
{
    public static void Run(WorldData w, FalloffDefinition f, uint seed)
    {
        var noise = new Noise(seed);
        float size = w.Size, half = size * 0.5f;
        float coastFreq = 4f / size; // a handful of bays and headlands around the coast
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
            {
                int i = w.Index(x, z);
                float land = 1f, bed = f.SeaDepth;
                if (f.Kind == FalloffKind.Island)
                {
                    float px = x * w.SampleSpacing, pz = z * w.SampleSpacing;
                    float d = MathF.Sqrt((px - half) * (px - half) + (pz - half) * (pz - half)) / size;
                    d += f.CoastNoise * noise.Fbm(px * coastFreq, pz * coastFreq, 3);
                    land = Noise.SmoothStep(f.Radius + f.Edge * 0.5f, f.Radius - f.Edge * 0.5f, d);
                    // past the coast the shelf drops off following the coastline, and is open sea at every map border
                    float border = MathF.Max(MathF.Abs(px - half), MathF.Abs(pz - half)) / size;
                    float coast = f.Radius + f.Edge * 0.5f, shelfEnd = f.Shelf > 0 ? MathF.Min(0.5f, coast + f.Shelf) : 0.5f;
                    float drop = MathF.Max(Noise.SmoothStep(coast, shelfEnd, d), Noise.SmoothStep(0.46f, 0.5f, border));
                    bed += (f.OpenSeaDepth - f.SeaDepth) * drop;
                }
                w.LandMask[i] = land;
                w.Height[i] = bed + (f.LandHeight - bed) * land;
            }
    }
}
