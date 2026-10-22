using System;
using System.Collections.Generic;
using System.Numerics;

namespace Terrain.Core.Shadows;

/// <summary>
/// Fits shadow proxies to a tree's leaf vertices: a handful of lobes (k-means clusters), each an ellipsoid sized to the
/// spread of its leaves, so the baked shadow has the canopy's lumpy outline instead of one round blob.
/// </summary>
public static class CanopyFit
{
    /// <param name="leaves">Leaf vertex positions in model units.</param>
    /// <param name="lobes">How many clusters; fewer for small models.</param>
    public static ShadowProxy[] Lobes(IReadOnlyList<Vector3> leaves, int lobes, float density, float dapple)
    {
        if (leaves.Count == 0 || lobes <= 0) return Array.Empty<ShadowProxy>();
        lobes = Math.Min(lobes, leaves.Count);

        // seed the centres spread through the canopy (deterministic: every n-th vertex by height order)
        var sorted = new List<Vector3>(leaves);
        sorted.Sort((a, b) => a.Y.CompareTo(b.Y) != 0 ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var centres = new Vector3[lobes];
        for (int k = 0; k < lobes; k++) centres[k] = sorted[(int)((k + 0.5f) / lobes * sorted.Count)];

        var owner = new int[leaves.Count];
        for (int iteration = 0; iteration < 12; iteration++)
        {
            for (int i = 0; i < leaves.Count; i++)
            {
                float best = float.MaxValue;
                for (int k = 0; k < lobes; k++)
                {
                    float d = Vector3.DistanceSquared(leaves[i], centres[k]);
                    if (d < best) { best = d; owner[i] = k; }
                }
            }
            var sum = new Vector3[lobes];
            var count = new int[lobes];
            for (int i = 0; i < leaves.Count; i++) { sum[owner[i]] += leaves[i]; count[owner[i]]++; }
            for (int k = 0; k < lobes; k++)
                if (count[k] > 0) centres[k] = sum[k] / count[k];
        }

        var result = new List<ShadowProxy>();
        var radial = new float[lobes];
        var vertical = new float[lobes];
        var members = new int[lobes];
        for (int i = 0; i < leaves.Count; i++)
        {
            var d = leaves[i] - centres[owner[i]];
            radial[owner[i]] += d.X * d.X + d.Z * d.Z;
            vertical[owner[i]] += d.Y * d.Y;
            members[owner[i]]++;
        }
        for (int k = 0; k < lobes; k++)
        {
            if (members[k] < 3) continue;
            // leaf cards fill a lobe's outer shell: for a shell, spread = radius / sqrt(3) per axis
            float r = 1.7f * MathF.Sqrt(radial[k] / members[k] / 2f), h = 1.7f * MathF.Sqrt(vertical[k] / members[k]);
            result.Add(new ShadowProxy(centres[k], new Vector3(MathF.Max(r, 0.05f), MathF.Max(h, 0.05f), MathF.Max(r, 0.05f)), density, dapple));
        }
        return result.ToArray();
    }
}
