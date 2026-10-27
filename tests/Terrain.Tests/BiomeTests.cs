using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Ground;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Tests;

public class BiomeTests
{
    static string Read(string p) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", p));

    /// <summary>The sample level with its own foliage sets (regional and global).</summary>
    static (WorldData world, LevelDefinition level, List<FoliageSetDefinition> sets, PaletteDefinition palette) Generate(uint? seed = null)
    {
        var level = LevelJson.Deserialize<LevelDefinition>(Read("levels/harbour_village.json"));
        if (seed is { } s) level.Seed = s;
        var layout = LevelJson.Deserialize<LayoutDefinition>(Read($"layouts/{level.Layout}.json"));
        var palette = LevelJson.Deserialize<PaletteDefinition>(Read($"palettes/{level.Palette}.json"));
        var sets = level.Foliage.Select(id => LevelJson.Deserialize<FoliageSetDefinition>(Read($"foliage/sets/{id}.json"))).ToList();
        return (LevelGenerator.Generate(level, layout, palette, sets).World, level, sets, palette);
    }

    [Test]
    public async Task ClimateIsStableAndInRange()
    {
        var (a, _, _, _) = Generate();
        var (b, _, _, _) = Generate();
        await Assert.That(a.Temperature.SequenceEqual(b.Temperature)).IsTrue();
        await Assert.That(a.Moisture.SequenceEqual(b.Moisture)).IsTrue();
        await Assert.That(a.Temperature.All(t => t is >= 0 and <= 1) && a.Moisture.All(m => m is >= 0 and <= 1)).IsTrue();
    }

    [Test]
    public async Task MountainsAreColderThanLowlands()
    {
        var (w, _, _, _) = Generate();
        var low = Enumerable.Range(0, w.Height.Length).Where(i => w.Height[i] is > 0.5f and < 2f).Average(i => w.Temperature[i]);
        var high = Enumerable.Range(0, w.Height.Length).Where(i => w.Height[i] > 9f).Average(i => w.Temperature[i]);
        await Assert.That(high).IsLessThan(low - 0.15f);
    }

    [Test]
    public async Task EveryLandRegionGetsABiome()
    {
        var (w, _, sets, _) = Generate();
        await Assert.That(w.BiomeIds.Length).IsEqualTo(sets.Count(s => s.Climate != null));
        for (int r = 0; r < w.RegionClimate.Length; r++)
            await Assert.That(float.IsNaN(w.RegionClimate[r].temperature) || w.RegionBiome[r] >= 0).IsTrue();
    }

    [Test]
    public async Task SeveralBiomesShowUp()
    {
        foreach (uint seed in new uint[] { 1234, 77, 5 })
        {
            var (w, _, _, _) = Generate(seed);
            await Assert.That(w.RegionBiome.Where(b => b >= 0).Distinct().Count()).IsGreaterThanOrEqualTo(3);
        }
    }

    [Test]
    public async Task PickFollowsTheClimateWindows()
    {
        FoliageSetDefinition Set(float t0, float t1, float m0, float m1) =>
            new() { Climate = new BiomeClimateDefinition { Temperature = [t0, t1], Moisture = [m0, m1] } };
        var biomes = new[] { Set(0, 0.4f, 0, 1), Set(0.4f, 1, 0, 0.5f), Set(0.4f, 1, 0.5f, 1) };
        for (uint roll = 0; roll < 50; roll++)
        {
            await Assert.That(BiomeStage.Pick(biomes, 0.2f, 0.7f, Seeds.Mix(roll))).IsEqualTo(0);
            await Assert.That(BiomeStage.Pick(biomes, 0.7f, 0.2f, Seeds.Mix(roll))).IsEqualTo(1);
            await Assert.That(BiomeStage.Pick(biomes, 0.7f, 0.8f, Seeds.Mix(roll))).IsEqualTo(2);
        }
        // nothing fits: the nearest window wins
        await Assert.That(BiomeStage.Pick([Set(0, 0.2f, 0, 1), Set(0.8f, 1, 0, 1)], 0.3f, 0.5f, 1)).IsEqualTo(0);
    }

    [Test]
    public async Task RegionalSpeciesStayInTheirBiome()
    {
        var (w, level, sets, _) = Generate();
        var foliage = FoliageStage.Run(w, sets, level.Seed);
        var forest = sets.First(s => s.Id == "kenney_forest");
        int biome = Array.IndexOf(w.BiomeIds, "kenney_forest");
        float reach = w.RegionBlend + w.SampleSpacing;
        // mushrooms and violets grow only in the forest set: each must stand in forest, or within the border blend of it
        var meshes = forest.Species.Where(s => s.Id is "violets" or "mushrooms").SelectMany(s => s.Meshes).Distinct().ToList();
        int count = 0;
        foreach (var mesh in meshes)
            foreach (var f in foliage.ByMesh.TryGetValue(mesh, out var l) ? l : [])
            {
                count++;
                bool near = new[] { (0f, 0f), (reach, 0f), (-reach, 0f), (0f, reach), (0f, -reach), (reach, reach), (-reach, -reach), (reach, -reach), (-reach, reach) }
                    .Any(o => w.BiomeAt(f.X + o.Item1, f.Z + o.Item2) == biome);
                await Assert.That(near).IsTrue();
            }
        await Assert.That(count).IsGreaterThan(0);
    }

    [Test]
    public async Task GrassPaintsCoverUnderItself()
    {
        var (w, level, sets, _) = Generate();
        var foliage = FoliageStage.Run(w, sets, level.Seed);
        var tall = foliage.ByMesh["grass_tall"];
        await Assert.That(tall.Count).IsGreaterThan(100);
        float under = tall.Take(200).Average(f => foliage.CoverAmount[w.Index((int)MathF.Round(f.X / w.SampleSpacing), (int)MathF.Round(f.Z / w.SampleSpacing))]);
        await Assert.That(under).IsGreaterThan(0.2f);
        // the sea gets none
        await Assert.That(Enumerable.Range(0, w.Height.Length).Where(i => w.Height[i] < -2f).All(i => foliage.CoverAmount[i] == 0)).IsTrue();
    }

    [Test]
    public async Task GrassThinsTowardTheBoardInsteadOfStoppingOnALine()
    {
        var (w, level, sets, _) = Generate();
        var tall = FoliageStage.Run(w, sets, level.Seed).ByMesh["grass_tall"];
        var village = level.Zones[0];
        float x0 = village.Origin[0] * w.CellSize, x1 = (village.Origin[0] + village.Cells[0]) * w.CellSize;
        float z0 = village.Origin[1] * w.CellSize, z1 = (village.Origin[1] + village.Cells[1]) * w.CellSize;
        float Outside(FoliageInstance f) => MathF.Max(MathF.Max(x0 - f.X, f.X - x1), MathF.Max(z0 - f.Z, f.Z - z1));
        // clumps just outside the board are fewer and smaller than those well away from it
        var edge = tall.Where(f => Outside(f) is > 0 and < 3).ToList();
        var away = tall.Where(f => Outside(f) is > 12 and < 30).ToList();
        await Assert.That(edge.Count).IsGreaterThan(0);
        await Assert.That(edge.Average(f => f.Scale)).IsLessThan(away.Average(f => f.Scale) * 0.9f);
    }

    [Test]
    public async Task BiomesTintTheGround()
    {
        var (w, level, sets, palette) = Generate();
        var textures = LevelJson.Deserialize<TextureSetDefinition>(Read("ground/textures.json"));
        var ids = textures.Textures.Select((t, i) => (t.Id, i)).ToDictionary(p => p.Id, p => p.i);
        var plain = GroundMaps.Build(w, palette, ids, level.Seed);
        var tinted = GroundMaps.Build(w, palette, ids, level.Seed, sets);
        int changed = Enumerable.Range(0, w.Height.Length).Count(i => plain.Tint[i] != tinted.Tint[i]);
        await Assert.That(changed).IsGreaterThan(w.Height.Length / 10);
        // litter, dirt or dry-grass patches show up somewhere
        var patches = new[] { "dirt", "litter", "dry" }.Select(t => (byte)ids[t]).ToHashSet();
        await Assert.That(Enumerable.Range(0, w.Height.Length).Any(i => patches.Contains(tinted.Overlay[i]) && tinted.Blend[i] > 0.3f)).IsTrue();
        // and a texture still never blends with itself
        await Assert.That(Enumerable.Range(0, w.Height.Length).Any(i => tinted.Base[i] == tinted.Overlay[i] && tinted.Blend[i] > 0)).IsFalse();
    }
}
