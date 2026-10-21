using Terrain.Core.Generation;
using Terrain.Core.Generation.Stages;
using Terrain.Core.Level;

namespace Terrain.Tests;

public class GeneratorTests
{
    static T Load<T>(string path) where T : new() =>
        LevelJson.Deserialize<T>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", path)));

    static (LevelDefinition level, LayoutDefinition layout, PaletteDefinition palette) Sample()
    {
        var level = Load<LevelDefinition>("levels/harbour_village.json");
        return (level, Load<LayoutDefinition>($"layouts/{level.Layout}.json"), Load<PaletteDefinition>($"palettes/{level.Palette}.json"));
    }

    [Test]
    public async Task SampleLevelGeneratesWithoutIssues()
    {
        var (level, layout, palette) = Sample();
        var result = LevelGenerator.Generate(level, layout, palette);
        await Assert.That(string.Join("\n", result.Issues)).IsEqualTo("");
        await Assert.That(result.World.Cells).IsEqualTo(100);
        await Assert.That(result.World.Samples).IsEqualTo(201);
    }

    [Test]
    public async Task SameSeedSameWorld()
    {
        var (level, layout, palette) = Sample();
        var a = LevelGenerator.Generate(level, layout, palette).World;
        var b = LevelGenerator.Generate(level, layout, palette).World;
        await Assert.That(a.Height.SequenceEqual(b.Height)).IsTrue();
    }

    [Test]
    public async Task DifferentSeedDifferentWorld()
    {
        var (level, layout, palette) = Sample();
        var a = LevelGenerator.Generate(level, layout, palette).World;
        level.Seed++;
        var b = LevelGenerator.Generate(level, layout, palette).World;
        await Assert.That(a.Height.SequenceEqual(b.Height)).IsFalse();
    }

    [Test]
    public async Task StageSeedsAreIndependentAndStable()
    {
        await Assert.That(Seeds.Derive(1234, "detail")).IsNotEqualTo(Seeds.Derive(1234, "shape"));
        await Assert.That(Seeds.Derive(1234, "detail")).IsEqualTo(Seeds.Derive(1234, "detail"));
        await Assert.That(Seeds.Derive(1234, "detail")).IsNotEqualTo(Seeds.Derive(1235, "detail"));
    }

    [Test]
    public async Task ZoneCellsStayInTheirWindow()
    {
        var (level, layout, palette) = Sample();
        var w = LevelGenerator.Generate(level, layout, palette).World;
        var village = level.Zones[0];
        for (int cz = village.Origin[1]; cz < village.Origin[1] + village.Cells[1]; cz++)
            for (int cx = village.Origin[0]; cx < village.Origin[0] + village.Cells[0]; cx++)
            {
                var (lo, hi) = w.CellRange(cx, cz);
                await Assert.That(lo).IsGreaterThanOrEqualTo(village.Height[0] - 1e-4f);
                await Assert.That(hi).IsLessThanOrEqualTo(village.Height[1] + 1e-4f);
                await Assert.That(w.IsPlayable(cx, cz)).IsTrue();
            }
        await Assert.That(w.IsPlayable(0, 0)).IsFalse();
    }

    [Test]
    public async Task PortInlandIsReported()
    {
        var (level, layout, palette) = Sample();
        level.Zones = [new ZoneDefinition { Id = "dry_port", Origin = [45, 45], Cells = [4, 4], Height = [0.1f, 0.3f], TouchesSea = true }];
        var issues = LevelGenerator.Generate(level, layout, palette).Issues;
        await Assert.That(issues.Any(i => i.Contains("dry_port") && i.Contains("sea"))).IsTrue();
    }

    [Test]
    public async Task ZoneOutsideMapIsReported()
    {
        var (level, layout, palette) = Sample();
        level.Zones = [new ZoneDefinition { Id = "edge", Origin = [95, 95], Cells = [10, 10] }];
        var issues = LevelGenerator.Generate(level, layout, palette).Issues;
        await Assert.That(issues.Any(i => i.Contains("edge") && i.Contains("outside"))).IsTrue();
    }

    [Test]
    public async Task BandLookupUsesStartHeights()
    {
        var bands = new[] { new BandDefinition { From = -100 }, new BandDefinition { From = -1 }, new BandDefinition { From = 0.3f } };
        await Assert.That(BandStage.Lookup(bands, -5f)).IsEqualTo((byte)0);
        await Assert.That(BandStage.Lookup(bands, -1f)).IsEqualTo((byte)1);
        await Assert.That(BandStage.Lookup(bands, 0.29f)).IsEqualTo((byte)1);
        await Assert.That(BandStage.Lookup(bands, 5f)).IsEqualTo((byte)2);
    }

    [Test]
    public async Task MissingJsonFieldsKeepDefaults()
    {
        var level = LevelJson.Deserialize<LevelDefinition>("""{ "seed": 7, "layout": "a", "palette": "b", "zones": [ { "id": "z" } ] }""");
        await Assert.That(level.Seed).IsEqualTo(7u);
        await Assert.That(level.CellSize).IsEqualTo(2f);
        await Assert.That(level.Zones[0].Height[1]).IsEqualTo(1.4f);
        await Assert.That(LevelJson.Check(level).Length).IsEqualTo(0);
    }

    [Test]
    public async Task CellSizeMustBeWholeSamples()
    {
        var level = new LevelDefinition { Layout = "a", Palette = "b", CellSize = 2.5f, SampleSpacing = 1f };
        await Assert.That(LevelJson.Check(level).Any(i => i.Contains("whole number"))).IsTrue();
    }
}
