using System;
using System.Collections.Generic;
using Godot;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Surface;

/// <summary>
/// Draws the playable cell grid as one decal per zone, projected straight down onto whatever renders the ground.
/// Decals only touch render layer 1, so foliage and buildings on other layers stay clean.
/// </summary>
internal sealed class ZoneGrid
{
    const int MaxTexture = 4096;

    readonly Node3D _parent;
    readonly List<Decal> _decals = new();

    public ZoneGrid(Node3D parent) => _parent = parent;

    public Color LineColor { get; set; } = new(0.08f, 0.1f, 0.04f, 0.45f);
    /// <summary>Cells over which the grid fades in from the edge of the buildable area.</summary>
    public float FadeCells { get; set; } = 3f;

    public void Build(WorldData w, IReadOnlyList<ZoneDefinition> zones)
    {
        foreach (var d in _decals) d.QueueFree();
        _decals.Clear();

        for (int zi = 0; zi < zones.Count; zi++)
        {
            var zone = zones[zi];
            int x0 = Math.Clamp(zone.Origin[0], 0, w.Cells), z0 = Math.Clamp(zone.Origin[1], 0, w.Cells);
            int x1 = Math.Clamp(zone.Origin[0] + zone.Cells[0], 0, w.Cells), z1 = Math.Clamp(zone.Origin[1] + zone.Cells[1], 0, w.Cells);
            int cx = x1 - x0, cz = z1 - z0;
            if (cx <= 0 || cz <= 0) continue;

            float lo = zone.Height[0], hi = zone.Height[1];
            float depth = hi - lo + 4f; // reach a little above and below the window
            var decal = new Decal
            {
                Name = $"Grid_{zone.Id}",
                Size = new Vector3(cx * w.CellSize, depth, cz * w.CellSize),
                Position = new Vector3((x0 + cx * 0.5f) * w.CellSize, (lo + hi) * 0.5f, (z0 + cz * 0.5f) * w.CellSize),
                TextureAlbedo = GridTexture(w, zi, x0, z0, cx, cz),
                UpperFade = 0.05f,
                LowerFade = 0.05f,
                NormalFade = 0.5f,
                CullMask = 1,
            };
            _parent.AddChild(decal);
            _decals.Add(decal);
        }
    }

    public void SetVisible(bool visible)
    {
        foreach (var d in _decals) d.Visible = visible;
    }

    /// <summary>
    /// Grid lines over the zone's own cells only, fading out over <see cref="FadeCells"/> cells toward the edge of the
    /// buildable area instead of ending on a border line, so the board reads as part of the ground.
    /// </summary>
    ImageTexture GridTexture(WorldData w, int zone, int x0, int z0, int cellsX, int cellsZ)
    {
        bool Owns(int cx, int cz) => cx >= 0 && cz >= 0 && cx < w.Cells && cz < w.Cells && w.CellZone[cz * w.Cells + cx] == zone;
        int px = Math.Clamp(MaxTexture / Math.Max(cellsX, cellsZ), 8, 32);
        int wpx = cellsX * px, hpx = cellsZ * px;
        // distance in pixels to the nearest pixel outside the zone (two-pass chamfer)
        var d = new float[wpx * hpx];
        for (int y = 0; y < hpx; y++)
            for (int x = 0; x < wpx; x++)
                d[y * wpx + x] = Owns(x0 + x / px, z0 + y / px) ? 1e6f : 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            int step = pass == 0 ? 1 : -1;
            for (int yi = 0, y = pass == 0 ? 0 : hpx - 1; yi < hpx; yi++, y += step)
                for (int xi = 0, x = pass == 0 ? 0 : wpx - 1; xi < wpx; xi++, x += step)
                {
                    int i = y * wpx + x;
                    if (d[i] == 0) continue;
                    int px0 = x - step, py0 = y - step;
                    // the texture's own border counts as outside
                    float best = MathF.Min(MathF.Min(x + 1, wpx - x), MathF.Min(y + 1, hpx - y));
                    if (px0 >= 0 && px0 < wpx) best = MathF.Min(best, d[y * wpx + px0] + 1);
                    if (py0 >= 0 && py0 < hpx) best = MathF.Min(best, d[py0 * wpx + x] + 1);
                    d[i] = MathF.Min(d[i], best);
                }
        }
        var img = Image.CreateEmpty(wpx, hpx, false, Image.Format.Rgba8);
        int line = Math.Max(1, px / 16);
        float fade = MathF.Max(1f, FadeCells * px);
        for (int y = 0; y < hpx; y++)
            for (int x = 0; x < wpx; x++)
            {
                float a = d[y * wpx + x];
                if (a <= 0) continue;
                int fx = x % px, fy = y % px;
                if (!(fx < line || fy < line || fx >= px - line || fy >= px - line)) continue;
                float t = Mathf.SmoothStep(0f, fade, a);
                img.SetPixel(x, y, new Color(LineColor.R, LineColor.G, LineColor.B, LineColor.A * t));
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
