namespace Terrain.Core.Generation;

/// <summary>Per-stage seeds: each stage hashes the world seed with its own name, so rerolling one stage never reshuffles another.</summary>
public static class Seeds
{
    public static uint Derive(uint worldSeed, string stage)
    {
        uint h = 2166136261u; // FNV-1a over the name, then mixed with the world seed
        foreach (char c in stage)
        {
            h ^= c;
            h *= 16777619u;
        }
        return Mix(h ^ Mix(worldSeed));
    }

    public static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x7feb352du;
        h ^= h >> 15;
        h *= 0x846ca68bu;
        h ^= h >> 16;
        return h;
    }

    public static uint Hash(int x, int y, uint seed) =>
        Mix(seed ^ unchecked((uint)x * 0x8da6b343u) ^ unchecked((uint)y * 0xd8163841u));
}
