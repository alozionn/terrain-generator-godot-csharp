using Terrain.Core.Generation;
using Terrain.Core.Generation.Stages;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Tests;

public class ErosionTests
{
    /// <summary>A 120 m map: flat land at 1 m on the west half, a 1:2 slope rising east on the other.</summary>
    static WorldData Slope()
    {
        var w = new WorldData(121, 1f, 2);
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
                w.Height[w.Index(x, z)] = x < 60 ? 1f : 1f + (x - 60) * 0.5f;
        return w;
    }

    static readonly ErosionDefinition Gullies = new() { Strength = 4f, Frequency = 0.08f, Octaves = 3 };

    [Test]
    public async Task CarvesSlopesAndLeavesFlatGroundAlone()
    {
        var w = Slope();
        var before = (float[])w.Height.Clone();
        ErosionStage.Run(w, Gullies, 7);
        // flat ground (away from where the slope starts) is untouched
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < 55; x++)
                await Assert.That(w.H(x, z)).IsEqualTo(before[w.Index(x, z)]);
        // the slope gets gullies and crests across it, not a uniform shift
        var row = Enumerable.Range(0, w.Samples).Select(z => before[w.Index(90, z)] - w.H(90, z)).ToArray();
        await Assert.That(row.Max() - row.Min()).IsGreaterThan(0.5f);
    }

    [Test]
    public async Task GulliesRunDownhill()
    {
        var w = Slope();
        var before = (float[])w.Height.Clone();
        ErosionStage.Run(w, new ErosionDefinition { Strength = 4f, Frequency = 0.08f, Octaves = 1 }, 7);
        float Cut(int x, int z) => before[w.Index(x, z)] - w.H(x, z);
        // along the fall line (x) the cut changes slowly; across it (z) it swings between gully and crest
        double along = 0, across = 0;
        for (int z = 10; z < 110; z++)
            for (int x = 75; x < 110; x++)
            {
                along += MathF.Abs(Cut(x + 1, z) - Cut(x, z));
                across += MathF.Abs(Cut(x, z + 1) - Cut(x, z));
            }
        await Assert.That(across).IsGreaterThan(along * 1.5);
    }

    [Test]
    public async Task NeverDigsLandIntoTheSea()
    {
        var w = new WorldData(101, 1f, 2);
        for (int z = 0; z < w.Samples; z++)
            for (int x = 0; x < w.Samples; x++)
                w.Height[w.Index(x, z)] = 0.5f + x * 0.4f; // a steep coastal slope from just above sea level
        ErosionStage.Run(w, new ErosionDefinition { Strength = 6f, Frequency = 0.1f, Octaves = 3, SlopeStart = 0.05f }, 3);
        await Assert.That(w.Height.All(h => h >= 0.4f - 1e-4f)).IsTrue();
    }

    [Test]
    public async Task OffByDefaultAndStablePerSeed()
    {
        var w = Slope();
        var before = (float[])w.Height.Clone();
        ErosionStage.Run(w, new ErosionDefinition(), 1);
        await Assert.That(w.Height.SequenceEqual(before)).IsTrue();
        var a = Slope();
        var b = Slope();
        ErosionStage.Run(a, Gullies, 11);
        ErosionStage.Run(b, Gullies, 11);
        await Assert.That(a.Height.SequenceEqual(b.Height)).IsTrue();
    }
}
