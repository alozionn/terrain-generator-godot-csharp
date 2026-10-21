using Terrain.Core.Generation;
using Terrain.Core.Ground;
using Terrain.Core.Level;

namespace Terrain.Tests;

public class GroundTests
{
    static GroundMaps Build(out Terrain.Core.World.WorldData world)
    {
        string Read(string p) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", p));
        var level = LevelJson.Deserialize<LevelDefinition>(Read("levels/harbour_village.json"));
        var layout = LevelJson.Deserialize<LayoutDefinition>(Read($"layouts/{level.Layout}.json"));
        var palette = LevelJson.Deserialize<PaletteDefinition>(Read($"palettes/{level.Palette}.json"));
        var set = LevelJson.Deserialize<TextureSetDefinition>(Read("ground/textures.json"));
        world = LevelGenerator.Generate(level, layout, palette).World;
        var ids = set.Textures.Select((t, i) => (t.Id, i)).ToDictionary(p => p.Id, p => p.i);
        return GroundMaps.Build(world, palette, ids, level.Seed);
    }

    [Test]
    public async Task ATextureNeverBlendsWithItself()
    {
        var g = Build(out _);
        int bad = 0;
        for (int i = 0; i < g.Blend.Length; i++)
            if (g.Base[i] == g.Overlay[i] && g.Blend[i] > 0) bad++;
        await Assert.That(bad).IsEqualTo(0);
    }

    [Test]
    public async Task SteepGroundGetsTheCliffTexture()
    {
        var g = Build(out var w);
        // the mountain stamps make plenty of steep samples; at least some must show rock as overlay at full blend
        await Assert.That(Enumerable.Range(0, g.Blend.Length).Any(i => g.Overlay[i] == 1 && g.Blend[i] > 0.99f)).IsTrue();
        await Assert.That(g.Base.Length).IsEqualTo(w.Samples * w.Samples);
    }

    [Test]
    public async Task ControlEncodingMatchesTerrain3DLayout()
    {
        uint px = GroundMaps.EncodeTerrain3D(3, 5, 1f);
        await Assert.That(px >> 27).IsEqualTo(3u);
        await Assert.That((px >> 22) & 0x1F).IsEqualTo(5u);
        await Assert.That((px >> 14) & 0xFF).IsEqualTo(255u);
        await Assert.That(px & 0x3FFF).IsEqualTo(0u); // no hole, nav or autoshader bits
    }
}
