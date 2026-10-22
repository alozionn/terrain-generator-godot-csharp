using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Terrain.Core.Generation;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Foliage;

/// <summary>One placed instance. Tilt is applied in the world frame after the yaw (so it can follow the slope).</summary>
public readonly record struct FoliageInstance(float X, float Y, float Z, float Yaw, float TiltX, float TiltZ, float Scale, float Tint);

public sealed class FoliageResult
{
    /// <summary>Instances per mesh id (as named in the species).</summary>
    public Dictionary<string, List<FoliageInstance>> ByMesh { get; } = new();
    public int Count => ByMesh.Values.Sum(l => l.Count);
    /// <summary>Per sample: the colour species with a <see cref="SpeciesDefinition.Cover"/> paint onto the ground (linear 0..1).</summary>
    public Vector3[] CoverColor { get; init; } = Array.Empty<Vector3>();
    /// <summary>Per sample: how much of the ground that colour covers, 0..1.</summary>
    public float[] CoverAmount { get; init; } = Array.Empty<float>();
}

/// <summary>
/// Places every species of every set on a jittered grid (one candidate per spacing cell, so neighbours keep roughly
/// the spacing apart), filtered by band, height, slope, playable cells, cluster noise and the footprints of species
/// placed before it. Each species has its own seed, so retuning one leaves the others where they were.
/// A regional set (with a climate) only grows in the regions it was chosen for; near a border each candidate looks
/// its region up from a randomly nudged position, so neighbouring biomes interleave instead of meeting on a line.
/// </summary>
public static class FoliageStage
{
    /// <param name="occupied">Cells nothing may grow on (building footprints), indexed cz * Cells + cx; null for none.</param>
    public static FoliageResult Run(WorldData w, IReadOnlyList<FoliageSetDefinition> sets, uint seed, bool[] occupied = null)
    {
        var result = new FoliageResult { CoverColor = new Vector3[w.Height.Length], CoverAmount = new float[w.Height.Length] };
        var claimed = new Footprints();
        var playable = new PlayableField(w);
        var edgeNoise = new Noise(Seeds.Derive(seed, "playable-edge"));
        var bandIndex = w.BandNames.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
        float sampleArea = w.SampleSpacing * w.SampleSpacing;

        foreach (var set in sets)
            foreach (var sp in set.Species)
            {
                if (sp.Spacing <= 0 || sp.Density <= 0) continue;
                int biome = set.Climate == null ? -1 : Array.IndexOf(w.BiomeIds, set.Id);
                if (set.Climate != null && (biome < 0 || Array.IndexOf(w.RegionBiome, (short)biome) < 0)) continue;
                bool hasCover = !string.IsNullOrEmpty(sp.Cover);
                var cover = hasCover ? Vector3.Clamp(LevelJson.ParseColor(sp.Cover), Vector3.Zero, Vector3.One) : Vector3.Zero;
                // cover colours are written in sRGB; the ground mixes them in linear
                cover = new Vector3(MathF.Pow(cover.X, 2.2f), MathF.Pow(cover.Y, 2.2f), MathF.Pow(cover.Z, 2.2f));
                uint s = Seeds.Derive(seed, $"foliage:{set.Id}:{sp.Id}");
                var cluster = new Noise(Seeds.Mix(s ^ 0x5bd1e995u));
                var allowed = sp.Bands.Count == 0 ? null : sp.Bands.Where(bandIndex.ContainsKey).Select(b => (byte)bandIndex[b]).ToHashSet();
                float cosMax = MathF.Cos(sp.MaxSlope * MathF.PI / 180f);
                float p = Math.Min(1f, sp.Density / 100f * sp.Spacing * sp.Spacing);
                var variants = sp.Meshes.Count > 0 ? sp.Meshes : new List<string> { sp.Mesh };
                var lists = variants.Select(m => result.ByMesh.TryGetValue(m, out var l) ? l : result.ByMesh[m] = new List<FoliageInstance>()).ToArray();

                int n = (int)MathF.Ceiling(w.Size / sp.Spacing);
                for (int gz = 0; gz < n; gz++)
                    for (int gx = 0; gx < n; gx++)
                    {
                        uint h = Seeds.Hash(gx, gz, s);
                        if (Unit(h) >= p) continue;
                        float x = (gx + Unit(Seeds.Mix(h + 1))) * sp.Spacing, z = (gz + Unit(Seeds.Mix(h + 2))) * sp.Spacing;
                        if (x >= w.Size || z >= w.Size) continue;
                        if (biome >= 0)
                        {
                            float jx = (Unit(Seeds.Mix(h + 11)) * 2 - 1) * w.RegionBlend, jz = (Unit(Seeds.Mix(h + 12)) * 2 - 1) * w.RegionBlend;
                            if (w.BiomeAt(x + jx, z + jz) != biome) continue;
                        }

                        if (sp.Clusters.Threshold > -1f)
                        {
                            float c = cluster.Fbm(x * sp.Clusters.Frequency, z * sp.Clusters.Frequency, 3);
                            float keep = Noise.SmoothStep(sp.Clusters.Threshold, sp.Clusters.Threshold + MathF.Max(1e-3f, sp.Clusters.Softness), c);
                            if (Unit(Seeds.Mix(h + 3)) >= keep) continue;
                        }

                        int sx = Math.Min(w.Samples - 1, (int)MathF.Round(x / w.SampleSpacing)), sz = Math.Min(w.Samples - 1, (int)MathF.Round(z / w.SampleSpacing));
                        if (allowed != null && !allowed.Contains(w.Band[w.Index(sx, sz)])) continue;
                        float y = w.HeightAt(x, z);
                        if (y < sp.Height[0] || y > sp.Height[1]) continue;
                        if (NormalY(w, sx, sz) < cosMax) continue;
                        float ridge = w.Ridge[w.Index(sx, sz)];
                        if (ridge < sp.Ridge[0] || ridge > sp.Ridge[^1]) continue;
                        // the board's surroundings: distance to the nearest playable cell, and a slow wobble so every
                        // edge rule below follows a wandering line rather than the board's straight one
                        float boardDistance = playable.At(x, z);
                        float wobble = boardDistance < 40f ? Math.Clamp(edgeNoise.Fbm(x * 0.06f, z * 0.06f, 2) * 2.2f, -1f, 1f) : 0f;
                        if (sp.AvoidPlayable)
                        {
                            // never on the board or within half the margin; beyond that, thinning in over playableFade
                            if (boardDistance < MathF.Max(0.01f, sp.PlayableMargin * 0.5f)) continue;
                            float fadeIn = MathF.Max(1e-3f, sp.PlayableFade);
                            float keep = Noise.SmoothStep(sp.PlayableMargin, sp.PlayableMargin + fadeIn, boardDistance + wobble * fadeIn * 0.6f);
                            if (Unit(Seeds.Mix(h + 14)) >= keep) continue;
                        }
                        if (claimed.Blocked(x, z)) continue;
                        if (occupied != null && occupied[Math.Min(w.Cells - 1, (int)(z / w.CellSize)) * w.Cells + Math.Min(w.Cells - 1, (int)(x / w.CellSize))]) continue;

                        float scale = Lerp(sp.Scale[0], sp.Scale[^1], Unit(Seeds.Mix(h + 4)));
                        if (sp.PlayableScale != 1f || sp.PlayableDensity < 1f)
                        {
                            // full board values on playable cells, easing back to normal over playableFade metres around
                            // them along a wandering, ragged edge, so a meadow thins out toward the board instead of
                            // stopping on a line
                            float fade = MathF.Max(1e-3f, sp.PlayableFade);
                            float d = boardDistance + wobble * fade * 0.6f + (Unit(Seeds.Mix(h + 13)) - 0.5f) * fade * 0.5f;
                            float t = boardDistance <= 0 || d <= 0 ? 0f : Noise.SmoothStep(0f, fade, d);
                            if (t < 1f)
                            {
                                if (Unit(Seeds.Mix(h + 9)) >= Lerp(sp.PlayableDensity, 1f, t)) continue;
                                scale *= Lerp(sp.PlayableScale, 1f, t);
                            }
                        }
                        float tilt = sp.Tilt * MathF.PI / 180f;
                        float tiltX = (Unit(Seeds.Mix(h + 6)) * 2 - 1) * tilt, tiltZ = (Unit(Seeds.Mix(h + 7)) * 2 - 1) * tilt;
                        if (sp.AlignToSlope > 0)
                        {
                            // lean with the ground: rotate up toward the surface normal (world frame, see FoliageInstance)
                            var (slopeX, slopeZ) = Gradient(w, sx, sz);
                            tiltX = Lerp(tiltX, MathF.Atan(-slopeZ), sp.AlignToSlope);
                            tiltZ = Lerp(tiltZ, MathF.Atan(slopeX), sp.AlignToSlope);
                        }
                        // sunk into the ground, so rocks look bedded in rather than set down on top
                        if (sp.Sink.Length > 0) y -= Lerp(sp.Sink[0], sp.Sink[^1], Unit(Seeds.Mix(h + 15))) * scale;
                        lists[(int)(Seeds.Mix(h + 10) % (uint)lists.Length)].Add(new FoliageInstance(x, y, z,
                            Unit(Seeds.Mix(h + 5)) * MathF.Tau,
                            tiltX, tiltZ,
                            scale,
                            1f + (Unit(Seeds.Mix(h + 8)) * 2 - 1) * sp.TintJitter));
                        if (sp.Footprint > 0) claimed.Add(x, z, sp.Footprint * scale);
                        if (hasCover)
                        {
                            int ci = w.Index(sx, sz);
                            float a = sp.CoverArea * scale * scale / sampleArea;
                            result.CoverColor[ci] += cover * a;
                            result.CoverAmount[ci] += a;
                        }
                    }
            }
        FinishCover(w, result);
        return result;
    }

    /// <summary>Turns summed cover into a mean colour and a 0..1 amount, softened over neighbouring samples.</summary>
    static void FinishCover(WorldData w, FoliageResult r)
    {
        int n = w.Samples;
        var color = new Vector3[r.CoverColor.Length];
        var amount = new float[r.CoverAmount.Length];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                Vector3 c = Vector3.Zero;
                float a = 0, weight = 0;
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                        float k = 3 - MathF.Max(MathF.Abs(dx), MathF.Abs(dz));
                        int j = zz * n + xx;
                        c += r.CoverColor[j] * k;
                        a += r.CoverAmount[j] * k;
                        weight += k;
                    }
                int i = z * n + x;
                color[i] = a > 1e-5f ? c / a : Vector3.Zero;
                amount[i] = Math.Clamp(a / weight, 0f, 1f);
            }
        Array.Copy(color, r.CoverColor, color.Length);
        Array.Copy(amount, r.CoverAmount, amount.Length);
    }

    /// <summary>Distance in metres to the nearest playable cell, per sample (two-pass chamfer), looked up bilinearly.</summary>
    sealed class PlayableField
    {
        readonly WorldData _w;
        readonly float[] _d;

        public PlayableField(WorldData w)
        {
            _w = w;
            int n = w.Samples;
            _d = new float[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int cx = Math.Min(w.Cells - 1, x / w.SamplesPerCell), cz = Math.Min(w.Cells - 1, z / w.SamplesPerCell);
                    _d[z * n + x] = w.IsPlayable(cx, cz) ? 0f : 1e6f;
                }
            const float diag = 1.41421356f;
            for (int pass = 0; pass < 2; pass++)
            {
                int step = pass == 0 ? 1 : -1, start = pass == 0 ? 0 : n - 1;
                for (int zi = 0, z = start; zi < n; zi++, z += step)
                    for (int xi = 0, x = start; xi < n; xi++, x += step)
                    {
                        int i = z * n + x, px = x - step, pz = z - step, qx = x + step;
                        bool okX = px >= 0 && px < n, okZ = pz >= 0 && pz < n;
                        if (okX) _d[i] = MathF.Min(_d[i], _d[z * n + px] + 1);
                        if (okZ) _d[i] = MathF.Min(_d[i], _d[pz * n + x] + 1);
                        if (okX && okZ) _d[i] = MathF.Min(_d[i], _d[pz * n + px] + diag);
                        if (okZ && qx >= 0 && qx < n) _d[i] = MathF.Min(_d[i], _d[pz * n + qx] + diag);
                    }
            }
            for (int i = 0; i < _d.Length; i++) _d[i] *= w.SampleSpacing;
        }

        public float At(float xm, float zm)
        {
            int n = _w.Samples;
            float fx = Math.Clamp(xm / _w.SampleSpacing, 0, n - 1.001f), fz = Math.Clamp(zm / _w.SampleSpacing, 0, n - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            float a = _d[z * n + x], b = _d[z * n + x + 1], c = _d[(z + 1) * n + x], d = _d[(z + 1) * n + x + 1];
            // a point on a playable cell is on the board even next to a non-playable sample
            int cx = Math.Min(_w.Cells - 1, (int)(xm / _w.CellSize)), cz = Math.Min(_w.Cells - 1, (int)(zm / _w.CellSize));
            if (cx >= 0 && cz >= 0 && _w.IsPlayable(cx, cz)) return 0f;
            return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
        }
    }

    static (float gx, float gz) Gradient(WorldData w, int x, int z)
    {
        int xl = Math.Max(0, x - 1), xr = Math.Min(w.Samples - 1, x + 1);
        int zu = Math.Max(0, z - 1), zd = Math.Min(w.Samples - 1, z + 1);
        return ((w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing), (w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing));
    }

    static float NormalY(WorldData w, int x, int z)
    {
        int xl = Math.Max(0, x - 1), xr = Math.Min(w.Samples - 1, x + 1);
        int zu = Math.Max(0, z - 1), zd = Math.Min(w.Samples - 1, z + 1);
        float dx = (w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing);
        float dz = (w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing);
        return 1f / MathF.Sqrt(dx * dx + dz * dz + 1f);
    }

    static float Unit(uint h) => (h >> 8) * (1f / 16777216f);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Circles claimed by earlier species, bucketed on a coarse grid.</summary>
    sealed class Footprints
    {
        const float Bucket = 4f;
        readonly Dictionary<(int, int), List<(float x, float z, float r)>> _cells = new();
        float _maxR;

        public void Add(float x, float z, float r)
        {
            var key = ((int)MathF.Floor(x / Bucket), (int)MathF.Floor(z / Bucket));
            if (!_cells.TryGetValue(key, out var l)) _cells[key] = l = new();
            l.Add((x, z, r));
            _maxR = MathF.Max(_maxR, r);
        }

        public bool Blocked(float x, float z)
        {
            if (_cells.Count == 0) return false;
            int reach = (int)MathF.Ceiling(_maxR / Bucket);
            int bx = (int)MathF.Floor(x / Bucket), bz = (int)MathF.Floor(z / Bucket);
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (_cells.TryGetValue((bx + dx, bz + dz), out var l))
                        foreach (var (cx, cz, r) in l)
                            if ((cx - x) * (cx - x) + (cz - z) * (cz - z) < r * r) return true;
            return false;
        }
    }
}
