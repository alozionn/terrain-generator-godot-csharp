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

    /// <summary>Every instance placed by one species (all of its mesh variants).</summary>
    static List<FoliageInstance> Of(FoliageResult foliage, FoliageSetDefinition set, string species)
    {
        var sp = set.Species.First(s => s.Id == species);
        var meshes = sp.Meshes.Count > 0 ? sp.Meshes : [sp.Mesh];
        return meshes.Distinct().SelectMany(m => foliage.ByMesh.TryGetValue(m, out var l) ? l : []).ToList();
    }

    [Test]
    public async Task SameSeedSamePlacement()
    {
        var (_, a, set) = Run();
        var (_, b, _) = Run();
        var oaksA = Of(a, set, "oaks");
        await Assert.That(oaksA.Count).IsGreaterThan(0);
        await Assert.That(oaksA.SequenceEqual(Of(b, set, "oaks"))).IsTrue();
    }

    [Test]
    public async Task VariantsAreAllUsed()
    {
        var (_, foliage, _) = Run();
        foreach (var mesh in new[] { "tree_common_1", "tree_common_3", "tree_common_5" })
            await Assert.That(foliage.ByMesh[mesh].Count).IsGreaterThan(0);
    }

    [Test]
    public async Task TreesStayOffPlayableCells()
    {
        var (w, foliage, set) = Run();
        foreach (var species in new[] { "oaks", "pines", "bushes", "rocks" })
            foreach (var f in Of(foliage, set, species))
                await Assert.That(w.IsPlayable((int)(f.X / w.CellSize), (int)(f.Z / w.CellSize))).IsFalse();
    }

    [Test]
    public async Task GrassMayGrowOnPlayableCells()
    {
        var (w, foliage, set) = Run();
        await Assert.That(Of(foliage, set, "tufts").Any(f => w.IsPlayable((int)(f.X / w.CellSize), (int)(f.Z / w.CellSize)))).IsTrue();
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
        // oaks are placed after rocks, so no oak may stand inside a rock's footprint
        var rocks = Of(foliage, set, "rocks");
        float rockFootprint = set.Species.First(s => s.Id == "rocks").Footprint;
        foreach (var o in Of(foliage, set, "oaks"))
            foreach (var r in rocks)
            {
                float d2 = (o.X - r.X) * (o.X - r.X) + (o.Z - r.Z) * (o.Z - r.Z);
                await Assert.That(d2).IsGreaterThanOrEqualTo(MathF.Pow(rockFootprint * r.Scale, 2) - 1e-3f);
            }
    }
}
