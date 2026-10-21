using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Ground;
using Terrain.Core.Level;
using Terrain.Core.World;
using Terrain.Integration;
using Terrain.Surface;

namespace Terrain;

/// <summary>
/// The terrain node. Fully described by a <see cref="LevelDefinition"/>: <see cref="Apply"/> one, or
/// <see cref="LoadLevel"/> from JSON. Layouts, palettes and the ground texture set are looked up under <see cref="DataRoot"/>.
/// Generation runs in plain C#; Terrain3D renders the ground, this node adds the playable-cell grid.
/// </summary>
[GlobalClass]
public partial class Terrain : Node3D
{
    [Export(PropertyHint.File, "*.json")] public string LevelPath { get; set; } = "res://data/levels/harbour_village.json";
    [Export(PropertyHint.Dir)] public string DataRoot { get; set; } = "res://data";

    Terrain3DBridge _bridge;
    ZoneGrid _grid;
    Placements _placements;
    bool _showGrid = true;

    public LevelDefinition Level { get; private set; }
    public PaletteDefinition Palette { get; private set; }
    public LayoutDefinition Layout { get; private set; }
    public WorldData World { get; private set; }
    public GroundMaps Ground { get; private set; }
    public FoliageResult Foliage { get; private set; }
    public IReadOnlyList<string> Issues { get; private set; } = Array.Empty<string>();

    /// <summary>The Terrain3D node currently drawing the ground; replaced on every generate.</summary>
    public Node3D Renderer => _bridge?.Node;

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; _grid?.SetVisible(value); }
    }

    /// <summary>Raised after every successful generate.</summary>
    public event Action Generated;

    public override void _Ready()
    {
        if (!string.IsNullOrEmpty(LevelPath)) LoadLevel(LevelPath);
    }

    public void LoadLevel(string path) => Apply(Read<LevelDefinition>(path));

    /// <summary>Generates and redraws everything for this level.</summary>
    public void Apply(LevelDefinition level)
    {
        if (level == null) return;
        var layout = Read<LayoutDefinition>($"{DataRoot}/layouts/{level.Layout}.json");
        var palette = Read<PaletteDefinition>($"{DataRoot}/palettes/{level.Palette}.json");
        if (layout == null || palette == null) return;

        if (_bridge == null)
        {
            _bridge = new Terrain3DBridge(this);
            _bridge.LoadTextures(Read<TextureSetDefinition>($"{DataRoot}/ground/textures.json") ?? new TextureSetDefinition());
            _bridge.LoadMeshes(Read<FoliageMeshSetDefinition>($"{DataRoot}/foliage/meshes.json") ?? new FoliageMeshSetDefinition());
        }
        var sets = new List<FoliageSetDefinition>();
        foreach (var id in level.Foliage)
            if (Read<FoliageSetDefinition>($"{DataRoot}/foliage/sets/{id}.json") is { } set) sets.Add(set);

        var started = Time.GetTicksMsec();
        var result = LevelGenerator.Generate(level, layout, palette);
        Level = level;
        Layout = layout;
        Palette = palette;
        World = result.World;
        Issues = result.Issues;
        Ground = GroundMaps.Build(World, palette, _bridge.TextureIds, level.Seed);
        var occupied = World.CellMask(level.Placements.Select(p => (p.Cell[0], p.Cell[1], p.Cells[0], p.Cells[1])));
        Foliage = FoliageStage.Run(World, sets, level.Seed, occupied);
        var generated = Time.GetTicksMsec();

        _bridge.Upload(World, Ground);
        _bridge.Plant(Foliage);
        _grid ??= new ZoneGrid(this);
        _grid.Build(World, level.Zones);
        _grid.SetVisible(_showGrid);
        _placements ??= new Placements(this);
        _placements.Build(World, level.Placements);

        GD.Print($"[Terrain] '{level.Id}' seed {level.Seed}: {World.Cells}x{World.Cells} cells, {World.Samples}² samples, {Foliage.Count} foliage, " +
                 $"generate {generated - started} ms, upload {Time.GetTicksMsec() - generated} ms");
        foreach (var issue in Issues) GD.PushWarning($"[Terrain] {issue}");
        Generated?.Invoke();
    }

    /// <summary>Same level, new seed.</summary>
    public void Reroll(uint seed)
    {
        if (Level == null) return;
        Level.Seed = seed;
        Apply(Level);
    }

    /// <summary>Same layout and seed, different palette.</summary>
    public void SetPalette(string id)
    {
        if (Level == null) return;
        Level.Palette = id;
        Apply(Level);
    }

    public void SaveLevel(string path)
    {
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        f?.StoreString(LevelJson.Serialize(Level));
    }

    /// <summary>World-space position of a cell's centre, on the ground.</summary>
    public Vector3 CellCentre(int cx, int cz)
    {
        float x = (cx + 0.5f) * World.CellSize, z = (cz + 0.5f) * World.CellSize;
        return ToGlobal(new Vector3(x, World.HeightAt(x, z), z));
    }

    /// <summary>Cell under a world-space point, or (-1, -1) off the map.</summary>
    public Vector2I CellAt(Vector3 global)
    {
        var p = ToLocal(global);
        int cx = (int)Mathf.Floor(p.X / World.CellSize), cz = (int)Mathf.Floor(p.Z / World.CellSize);
        return cx < 0 || cz < 0 || cx >= World.Cells || cz >= World.Cells ? new Vector2I(-1, -1) : new Vector2I(cx, cz);
    }

    internal T Read<T>(string path) where T : class, new()
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushError($"[Terrain] missing file: {path}");
            return null;
        }
        try { return LevelJson.Deserialize<T>(FileAccess.GetFileAsString(path)); }
        catch (Exception e)
        {
            GD.PushError($"[Terrain] {path}: {e.Message}");
            return null;
        }
    }
}
