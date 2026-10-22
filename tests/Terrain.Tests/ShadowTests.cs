using System.Numerics;
using Terrain.Core.Foliage;
using Terrain.Core.Shadows;
using Terrain.Core.World;

namespace Terrain.Tests;

public class ShadowTests
{
    /// <summary>A flat 100 m world with one "tree": a 2 m ball of leaves whose centre is 5 m up, at (50, 50).</summary>
    static (WorldData w, FoliageResult foliage, Dictionary<string, ShadowProxy[]> proxies) OneTree()
    {
        var w = new WorldData(101, 1f, 2);
        var foliage = new FoliageResult();
        foliage.ByMesh["tree"] = [new FoliageInstance(50, 0, 50, 0, 0, 0, 1, 1)];
        var proxies = new Dictionary<string, ShadowProxy[]> { ["tree"] = [new ShadowProxy(new Vector3(0, 5, 0), new Vector3(2, 2, 2), 2f)] };
        return (w, foliage, proxies);
    }

    [Test]
    public async Task ShadowFallsAwayFromTheSun()
    {
        var (w, foliage, proxies) = OneTree();
        // sun 45° up in the +x direction: the shadow lands 5 m toward -x
        var map = ShadowBake.Bake(w, foliage, proxies, new Vector3(1, 1, 0), texel: 0.5f, blurRadius: 0);
        await Assert.That(map.At(45, 50)).IsLessThan(0.2f);
        await Assert.That(map.At(55, 50)).IsEqualTo(1f);
        await Assert.That(map.At(45, 56)).IsEqualTo(1f);
        await Assert.That(map.At(20, 20)).IsEqualTo(1f);
    }

    [Test]
    public async Task ShadowIsSoftAtItsEdge()
    {
        var (w, foliage, proxies) = OneTree();
        var map = ShadowBake.Bake(w, foliage, proxies, new Vector3(0, 1, 0), texel: 0.5f, blurRadius: 1);
        // straight overhead: darkest in the middle, lighter toward the rim, where less canopy is crossed
        float centre = map.At(50, 50), rim = map.At(51.7f, 50);
        await Assert.That(centre).IsLessThan(rim);
        await Assert.That(rim).IsLessThan(1f);
    }

    [Test]
    public async Task CuttingATreeAndRebakingClearsItsShadow()
    {
        var (w, foliage, proxies) = OneTree();
        var map = ShadowBake.Bake(w, foliage, proxies, new Vector3(1, 1, 0));
        await Assert.That(map.At(45, 50)).IsLessThan(0.5f);
        foliage.ByMesh["tree"].Clear();
        ShadowBake.Rebake(map, w, foliage, proxies, 70, 80, 110, 120);
        await Assert.That(map.At(45, 50)).IsEqualTo(1f);
    }

    [Test]
    public async Task NoProxiesNoShadow()
    {
        var (w, foliage, _) = OneTree();
        var map = ShadowBake.Bake(w, foliage, new Dictionary<string, ShadowProxy[]>(), new Vector3(1, 1, 0));
        await Assert.That(map.Light.All(l => l == 1f)).IsTrue();
    }

    [Test]
    public async Task CanopyLobesFollowTheLeaves()
    {
        // two clumps of leaves, one each side of the trunk
        var leaves = new List<Vector3>();
        for (int i = 0; i < 200; i++)
        {
            float a = i * 0.7f, r = 0.3f + (i % 7) * 0.1f;
            leaves.Add(new Vector3(-2 + MathF.Cos(a) * r, 5 + MathF.Sin(a * 1.3f) * r, MathF.Sin(a) * r));
            leaves.Add(new Vector3(2 + MathF.Cos(a) * r, 4 + MathF.Sin(a * 1.3f) * r, MathF.Sin(a) * r));
        }
        var lobes = CanopyFit.Lobes(leaves, 2, 1f, 0.5f);
        await Assert.That(lobes.Length).IsEqualTo(2);
        var xs = lobes.Select(l => l.Centre.X).OrderBy(x => x).ToArray();
        await Assert.That(xs[0]).IsLessThan(-1.5f);
        await Assert.That(xs[1]).IsGreaterThan(1.5f);
        await Assert.That(lobes.All(l => l.Radii.X < 1.5f && l.Dapple == 0.5f)).IsTrue();
    }

    [Test]
    public async Task HighCanopiesCastSofterShadows()
    {
        float EdgeWidth(float height)
        {
            var w = new WorldData(101, 1f, 2);
            var foliage = new FoliageResult();
            foliage.ByMesh["tree"] = [new FoliageInstance(50, 0, 50, 0, 0, 0, 1, 1)];
            var proxies = new Dictionary<string, ShadowProxy[]> { ["tree"] = [new ShadowProxy(new Vector3(0, height, 0), new Vector3(2, 1, 2), 4f)] };
            var map = ShadowBake.Bake(w, foliage, proxies, new Vector3(0, 1, 0), texel: 0.25f, blurRadius: 0, softness: 0.1f);
            // texels in the half-lit band along a line through the shadow
            int band = 0;
            for (float x = 45; x < 55; x += 0.25f)
                if (map.At(x, 50) is > 0.15f and < 0.85f) band++;
            return band;
        }
        await Assert.That(EdgeWidth(15f)).IsGreaterThan(EdgeWidth(2f));
    }
}
