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
    public Color BorderColor { get; set; } = new(1f, 0.95f, 0.75f, 0.8f);

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
    /// Grid lines over the zone's own cells only (rounded corners, and cells a later zone took over, stay clear), with
    /// the border drawn wherever a cell's neighbour is not part of the zone.
    /// </summary>
    ImageTexture GridTexture(WorldData w, int zone, int x0, int z0, int cellsX, int cellsZ)
    {
        bool Owns(int cx, int cz) => cx >= 0 && cz >= 0 && cx < w.Cells && cz < w.Cells && w.CellZone[cz * w.Cells + cx] == zone;
        int px = Math.Clamp(MaxTexture / Math.Max(cellsX, cellsZ), 8, 32);
        int wpx = cellsX * px, hpx = cellsZ * px;
        var img = Image.CreateEmpty(wpx, hpx, false, Image.Format.Rgba8);
        int line = Math.Max(1, px / 16), border = Math.Max(2, px / 6);
        for (int y = 0; y < hpx; y++)
            for (int x = 0; x < wpx; x++)
            {
                int cx = x0 + x / px, cz = z0 + y / px;
                if (!Owns(cx, cz)) continue;
                int fx = x % px, fy = y % px;
                bool edge = (fx < border && !Owns(cx - 1, cz)) || (fx >= px - border && !Owns(cx + 1, cz))
                         || (fy < border && !Owns(cx, cz - 1)) || (fy >= px - border && !Owns(cx, cz + 1));
                bool grid = fx < line || fy < line || fx >= px - line || fy >= px - line;
                if (edge) img.SetPixel(x, y, BorderColor);
                else if (grid) img.SetPixel(x, y, LineColor);
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
