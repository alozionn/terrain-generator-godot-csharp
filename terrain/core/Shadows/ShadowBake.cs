using System;
using System.Collections.Generic;
using System.Numerics;
using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.World;

namespace Terrain.Core.Shadows;

/// <summary>An ellipsoid that stands in for part of a model (a canopy lobe, a trunk), in model units.</summary>
/// <param name="Dapple">0..1: how much light breaks through in patches (leaves); 0 for solid parts.</param>
public readonly record struct ShadowProxy(Vector3 Centre, Vector3 Radii, float Density, float Dapple = 0f);

/// <summary>
/// Light reaching the ground from the sun past fixed decor, on a grid over the map: 1 lit, 0 fully shadowed.
/// <see cref="Depth"/> and <see cref="Reach"/> keep the unblurred optical depth and how far up the sun ray it was met,
/// so an area can be re-baked on its own.
/// </summary>
public sealed class ShadowMap
{
    public required int Size { get; init; }
    /// <summary>Metres per texel.</summary>
    public required float Texel { get; init; }
    public required Vector3 ToSun { get; init; }
    public required float[] Depth { get; init; }
    /// <summary>Depth-weighted sum of the distance along the sun ray to where the shadow is cast.</summary>
    public required float[] Reach { get; init; }
    public required float[] Light { get; init; }
    /// <summary>Blur radius in texels for a shadow cast right at the ground.</summary>
    public int BlurRadius { get; init; } = 1;
    /// <summary>Extra blur in metres per metre between caster and ground (penumbra), capped at <see cref="MaxBlur"/> texels.</summary>
    public float Softness { get; init; } = 0.06f;
    public int MaxBlur { get; init; } = 5;

    /// <summary>Light at a position in metres (nearest texel), 1 off the map.</summary>
    public float At(float x, float z)
    {
        int tx = (int)MathF.Floor(x / Texel), tz = (int)MathF.Floor(z / Texel);
        return tx < 0 || tz < 0 || tx >= Size || tz >= Size ? 1f : Light[tz * Size + tx];
    }
}

/// <summary>
/// Bakes decor shadows into a ground map: for each texel, a ray from the ground toward the sun is tested against every
/// proxy whose shadow can reach it. Light falls off with the length of proxy crossed (leaves let some through), broken
/// up by a slow 3D noise where the proxy is leafy (dapples), then blurred: a little for shadows cast close to the
/// ground, more the further they fall (a penumbra), with a summed-area table so wide blurs cost the same as narrow
/// ones. Done per proxy over the box its shadow can fall in. <see cref="Rebake"/> redoes one rectangle, for when a tree
/// is cut or planted.
/// </summary>
public static class ShadowBake
{
    /// <summary>Lowest ground below a proxy that its shadow is followed down to, metres.</summary>
    const float Drop = 12f;
    /// <summary>Size of the dapple noise's cells, metres.</summary>
    const float DappleCell = 0.7f;

    /// <param name="toSun">Direction toward the sun; it must point upward.</param>
    /// <param name="proxies">Proxies per mesh id; meshes without proxies cast nothing.</param>
    public static ShadowMap Bake(WorldData w, FoliageResult foliage, IReadOnlyDictionary<string, ShadowProxy[]> proxies,
        Vector3 toSun, float texel = 0.5f, int blurRadius = 1, float softness = 0.06f)
    {
        int size = Math.Max(1, (int)MathF.Ceiling(w.Size / texel));
        var map = new ShadowMap
        {
            Size = size, Texel = texel, ToSun = Vector3.Normalize(toSun), BlurRadius = blurRadius, Softness = softness,
            Depth = new float[size * size], Reach = new float[size * size], Light = new float[size * size],
        };
        Rebake(map, w, foliage, proxies, 0, 0, size, size);
        return map;
    }

    /// <summary>Re-bakes the texels in [x0, x1) x [z0, z1) from the instances still in <paramref name="foliage"/>.</summary>
    public static void Rebake(ShadowMap map, WorldData w, FoliageResult foliage, IReadOnlyDictionary<string, ShadowProxy[]> proxies,
        int x0, int z0, int x1, int z1)
    {
        int n = map.Size, blur = map.BlurRadius + map.MaxBlur;
        x0 = Math.Clamp(x0, 0, n); z0 = Math.Clamp(z0, 0, n); x1 = Math.Clamp(x1, 0, n); z1 = Math.Clamp(z1, 0, n);
        // the blur reads up to its widest radius around the rectangle, so depth is rebuilt over that much more
        int dx0 = Math.Max(0, x0 - blur), dz0 = Math.Max(0, z0 - blur);
        int dx1 = Math.Min(n, x1 + blur), dz1 = Math.Min(n, z1 + blur);
        for (int z = dz0; z < dz1; z++)
        {
            Array.Clear(map.Depth, z * n + dx0, dx1 - dx0);
            Array.Clear(map.Reach, z * n + dx0, dx1 - dx0);
        }

        if (map.ToSun.Y > 0.02f)
            foreach (var (mesh, list) in foliage.ByMesh)
            {
                if (!proxies.TryGetValue(mesh, out var parts) || parts.Length == 0) continue;
                foreach (var f in list)
                    foreach (var p in parts)
                        Cast(map, w, f, p, dx0, dz0, dx1, dz1);
            }

        // summed-area table of transmitted light over the rebuilt area, then a box blur per texel whose radius grows
        // with how far up the sun ray its shadow was cast
        int sw = dx1 - dx0 + 1, sh = dz1 - dz0 + 1;
        var sat = new double[sw * sh];
        for (int z = 1; z < sh; z++)
        {
            double row = 0;
            for (int x = 1; x < sw; x++)
            {
                row += MathF.Exp(-map.Depth[(dz0 + z - 1) * n + dx0 + x - 1]);
                sat[z * sw + x] = sat[(z - 1) * sw + x] + row;
            }
        }
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
            {
                int i = z * n + x;
                float reach = map.Depth[i] > 1e-4f ? map.Reach[i] / map.Depth[i] : NearbyReach(map, x, z);
                int r = map.BlurRadius + Math.Min(map.MaxBlur, (int)MathF.Round(reach * map.Softness / map.Texel));
                int ax = Math.Max(dx0, x - r) - dx0, bx = Math.Min(dx1 - 1, x + r) - dx0 + 1;
                int az = Math.Max(dz0, z - r) - dz0, bz = Math.Min(dz1 - 1, z + r) - dz0 + 1;
                double sum = sat[bz * sw + bx] - sat[az * sw + bx] - sat[bz * sw + ax] + sat[az * sw + ax];
                map.Light[i] = (float)(sum / ((bx - ax) * (bz - az)));
            }
    }

    /// <summary>
    /// A lit texel beside a shadow takes the shadow's caster distance, so the penumbra blurs both sides of the edge.
    /// </summary>
    static float NearbyReach(ShadowMap map, int x, int z)
    {
        int n = map.Size;
        float reach = 0, weight = 0;
        for (int dz = -3; dz <= 3; dz += 3)
            for (int dx = -3; dx <= 3; dx += 3)
            {
                int xx = x + dx, zz = z + dz;
                if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                int j = zz * n + xx;
                reach += map.Reach[j];
                weight += map.Depth[j];
            }
        return weight > 1e-4f ? reach / weight : 0f;
    }

    static void Cast(ShadowMap map, WorldData w, FoliageInstance f, ShadowProxy p, int x0, int z0, int x1, int z1)
    {
        var l = map.ToSun;
        float cos = MathF.Cos(f.Yaw), sin = MathF.Sin(f.Yaw);
        // rotation about +y as Godot does it: x' = x cos + z sin, z' = -x sin + z cos
        var c = new Vector3(f.X + (p.Centre.X * cos + p.Centre.Z * sin) * f.Scale, f.Y + p.Centre.Y * f.Scale,
                            f.Z + (-p.Centre.X * sin + p.Centre.Z * cos) * f.Scale);
        var r = p.Radii * f.Scale;
        float reach = MathF.Max(r.X, MathF.Max(r.Y, r.Z));

        // the shadow of the bounding sphere on ground between c.y - Drop and c.y + reach
        float slopeX = l.X / l.Y, slopeZ = l.Z / l.Y;
        float hiDrop = Drop + reach, loDrop = -reach;
        float ax = c.X - slopeX * hiDrop, bx = c.X - slopeX * loDrop;
        float az = c.Z - slopeZ * hiDrop, bz = c.Z - slopeZ * loDrop;
        float pad = reach / l.Y;
        int tx0 = Math.Max(x0, (int)MathF.Floor((MathF.Min(ax, bx) - pad) / map.Texel));
        int tx1 = Math.Min(x1, (int)MathF.Ceiling((MathF.Max(ax, bx) + pad) / map.Texel) + 1);
        int tz0 = Math.Max(z0, (int)MathF.Floor((MathF.Min(az, bz) - pad) / map.Texel));
        int tz1 = Math.Min(z1, (int)MathF.Ceiling((MathF.Max(az, bz) + pad) / map.Texel) + 1);

        var inv = new Vector3(1f / r.X, 1f / r.Y, 1f / r.Z);
        var d = l * inv;
        float a = Vector3.Dot(d, d);
        int n = map.Size;
        for (int tz = tz0; tz < tz1; tz++)
            for (int tx = tx0; tx < tx1; tx++)
            {
                float px = (tx + 0.5f) * map.Texel, pz = (tz + 0.5f) * map.Texel;
                var ground = new Vector3(px, w.HeightAt(px, pz), pz);
                var q = (ground - c) * inv;
                float b = Vector3.Dot(q, d), cc = Vector3.Dot(q, q) - 1f;
                float disc = b * b - a * cc;
                if (disc <= 0) continue;
                float s = MathF.Sqrt(disc);
                float t1 = MathF.Max((-b - s) / a, 0f), t2 = (-b + s) / a;
                if (t2 <= 0) continue;
                // t is in metres along the unit sun direction
                float mid = (t1 + t2) * 0.5f;
                float density = p.Density;
                if (p.Dapple > 0)
                {
                    // gaps between leaf clumps, sampled where the ray crosses the middle of the lobe in world space,
                    // so neighbouring texels see neighbouring gaps and the dapples come out as soft spots
                    var at = (ground + l * mid) / DappleCell;
                    density *= MathF.Max(0f, 1f + p.Dapple * (Noise3(at.X, at.Y, at.Z) * 2f - 1f) * 1.4f);
                }
                float depth = density * (t2 - t1);
                map.Depth[tz * n + tx] += depth;
                map.Reach[tz * n + tx] += depth * mid;
            }
    }

    /// <summary>Smooth 3D value noise, 0..1.</summary>
    static float Noise3(float x, float y, float z)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y), iz = (int)MathF.Floor(z);
        float fx = Smooth(x - ix), fy = Smooth(y - iy), fz = Smooth(z - iz);
        float V(int dx, int dy, int dz) => (Seeds.Hash(ix + dx, (iy + dy) * 7919 + (iz + dz) * 104729, 0x2545f491u) >> 8) / 16777216f;
        float x00 = Lerp(V(0, 0, 0), V(1, 0, 0), fx), x10 = Lerp(V(0, 1, 0), V(1, 1, 0), fx);
        float x01 = Lerp(V(0, 0, 1), V(1, 0, 1), fx), x11 = Lerp(V(0, 1, 1), V(1, 1, 1), fx);
        return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
    }

    static float Smooth(float t) => t * t * (3 - 2 * t);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
