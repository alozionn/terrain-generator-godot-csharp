using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Level;

namespace Terrain.Tests;

public class FoliageTests
{
    static string Read(string p) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", p));

    static (Terrain.Core.World.WorldData world, FoliageResult foliage, FoliageSetDefinition set) Run(uint? seed = null)
    {
        var level = LevelJson.Deserialize<LevelDefinition>(Read("levels/harbour_village.json"));
        if (seed is { } s) level.Seed = s;
        var layout = LevelJson.Deserialize<LayoutDefinition>(Read($"layouts/{level.Layout}.json"));
        var palette = LevelJson.Deserialize<PaletteDefinition>(Read($"palettes/{level.Palette}.json"));
        var set = LevelJson.Deserialize<FoliageSetDefinition>(Read("foliage/sets/temperate.json"));
        var world = LevelGenerator.Generate(level, layout, palette).World;
        return (world, FoliageStage.Run(world, [set], level.Seed), set);
    }

    [Test]
    public async Task SameSeedSamePlacement()
    {
        var a = Run().foliage.ByMesh["tree_round"];
        var b = Run().foliage.ByMesh["tree_round"];
        await Assert.That(a.Count).IsGreaterThan(0);
        await Assert.That(a.SequenceEqual(b)).IsTrue();
    }

    [Test]
    public async Task TreesStayOffPlayableCells()
    {
        var (w, foliage, _) = Run();
        foreach (var mesh in new[] { "tree_round", "tree_pine", "bush", "rock" })
            foreach (var f in foliage.ByMesh[mesh])
                await Assert.That(w.IsPlayable((int)(f.X / w.CellSize), (int)(f.Z / w.CellSize))).IsFalse();
    }

    [Test]
    public async Task GrassMayGrowOnPlayableCells()
    {
        var (w, foliage, _) = Run();
        await Assert.That(foliage.ByMesh["grass"].Any(f => w.IsPlayable((int)(f.X / w.CellSize), (int)(f.Z / w.CellSize)))).IsTrue();
    }

    [Test]
    public async Task NothingGrowsUnderwater()
    {
        var (_, foliage, _) = Run();
        await Assert.That(foliage.ByMesh.Values.SelectMany(l => l).All(f => f.Y >= 0.2f - 1e-3f)).IsTrue();
    }

    [Test]
    public async Task FootprintsKeepTreesApart()
    {
        var (_, foliage, set) = Run();
        var oaks = foliage.ByMesh["tree_round"];
        // oaks are placed after rocks, so no oak may stand inside a rock's footprint
        var rocks = foliage.ByMesh["rock"];
        float rockFootprint = set.Species.First(s => s.Id == "rocks").Footprint;
        foreach (var o in oaks)
            foreach (var r in rocks)
            {
                float d2 = (o.X - r.X) * (o.X - r.X) + (o.Z - r.Z) * (o.Z - r.Z);
                await Assert.That(d2).IsGreaterThanOrEqualTo(MathF.Pow(rockFootprint * r.Scale, 2) - 1e-3f);
            }
    }
}
