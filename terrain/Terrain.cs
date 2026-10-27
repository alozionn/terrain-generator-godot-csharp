using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Ground;
using Terrain.Core.Level;
using Terrain.Core.Shadows;
using Terrain.Core.World;
using Terrain.Surface;

namespace Terrain;

/// <summary>
/// The terrain node. Fully described by a <see cref="LevelDefinition"/>: <see cref="Apply"/> one, or
/// <see cref="LoadLevel"/> from JSON. Layouts, palettes and the ground texture set are looked up under <see cref="DataRoot"/>.
/// Generation runs in plain C#; this node draws the ground, foliage, the playable-cell grid and placed buildings,
/// bakes the decor shadows for the level's lighting preset, and points <see cref="Sun"/> and <see cref="Environment"/>.
/// </summary>
[GlobalClass]
public partial class Terrain : Node3D
{
    [Export(PropertyHint.File, "*.json")] public string LevelPath { get; set; } = "res://data/levels/harbour_village.json";
    [Export(PropertyHint.Dir)] public string DataRoot { get; set; } = "res://data";

    /// <summary>Optional: the sun the level's lighting preset points and colours.</summary>
    [Export] public DirectionalLight3D Sun { get; set; }
    /// <summary>Optional: the environment whose sky, ambient light and fog the preset sets.</summary>
    [Export] public WorldEnvironment Environment { get; set; }

    GroundTextures _textures;
    readonly Dictionary<string, GroundTextures> _textureSets = new();
    Image _shadowImage;
    ImageTexture _shadowTexture;
    GroundRenderer _ground;
    FoliageInstancer _foliage;
    ZoneGrid _grid;
    Placements _placements;
    bool _showGrid = true;

    public LevelDefinition Level { get; private set; }
    public PaletteDefinition Palette { get; private set; }
    public LayoutDefinition Layout { get; private set; }
    public WorldData World { get; private set; }
    public GroundMaps Ground { get; private set; }
    public FoliageResult Foliage { get; private set; }
    public LightingDefinition Lighting { get; private set; }
    /// <summary>Baked decor shadow on the ground (1 lit, 0 shadowed).</summary>
    public ShadowMap Shadows { get; private set; }
    public IReadOnlyList<string> Issues { get; private set; } = Array.Empty<string>();

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

        string textureSet = string.IsNullOrEmpty(palette.Textures) ? "textures" : palette.Textures;
        if (!_textureSets.TryGetValue(textureSet, out _textures))
            _textureSets[textureSet] = _textures =
                GroundTextures.Load(Read<TextureSetDefinition>($"{DataRoot}/ground/{textureSet}.json") ?? new TextureSetDefinition());
        if (_ground == null)
        {
            _ground = new GroundRenderer(this);
            _foliage = new FoliageInstancer(this);
            _foliage.LoadMeshes(Read<FoliageMeshSetDefinition>($"{DataRoot}/foliage/meshes.json") ?? new FoliageMeshSetDefinition());
        }
        var sets = new List<FoliageSetDefinition>();
        foreach (var id in level.Foliage)
            if (Read<FoliageSetDefinition>($"{DataRoot}/foliage/sets/{id}.json") is { } set) sets.Add(set);
        var lighting = Read<LightingDefinition>($"{DataRoot}/lighting/{(string.IsNullOrEmpty(level.Lighting) ? "default" : level.Lighting)}.json") ?? new LightingDefinition();

        var started = Time.GetTicksMsec();
        var result = LevelGenerator.Generate(level, layout, palette, sets);
        Level = level;
        Layout = layout;
        Palette = palette;
        Lighting = lighting;
        World = result.World;
        Issues = result.Issues;
        Ground = GroundMaps.Build(World, palette, _textures.Ids, level.Seed, sets);
        var occupied = World.CellMask(level.Placements.Select(p => (p.Cell[0], p.Cell[1], p.Cells[0], p.Cells[1])));
        Foliage = FoliageStage.Run(World, sets, level.Seed, occupied);
        var generated = Time.GetTicksMsec();
        BakeShadows();
        var baked = Time.GetTicksMsec();

        _ground.Build(World, Ground, _textures, Foliage);
        _foliage.Build(Foliage);
        _grid ??= new ZoneGrid(this);
        _grid.Build(World, level.Zones);
        _grid.SetVisible(_showGrid);
        _placements ??= new Placements(this);
        _placements.Build(World, level.Placements);
        ApplyLighting();

        GD.Print($"[Terrain] '{level.Id}' seed {level.Seed}: {World.Cells}x{World.Cells} cells, {World.Samples}² samples, {Foliage.Count} foliage, " +
                 $"{World.RegionBiome.Count(b => b >= 0)} regions in {World.BiomeIds.Length} biomes, " +
                 $"generate {generated - started} ms, shadow bake {baked - generated} ms, upload {Time.GetTicksMsec() - baked} ms");
        foreach (var issue in Issues) GD.PushWarning($"[Terrain] {issue}");
        Generated?.Invoke();
    }

    /// <summary>Same level and seed, another lighting preset (re-bakes the decor shadows for the new sun).</summary>
    public void SetLighting(string id)
    {
        if (Level == null || Read<LightingDefinition>($"{DataRoot}/lighting/{id}.json") is not { } lighting) return;
        Level.Lighting = id;
        Lighting = lighting;
        var started = Time.GetTicksMsec();
        BakeShadows();
        ApplyLighting();
        GD.Print($"[Terrain] lighting '{id}', shadow bake {Time.GetTicksMsec() - started} ms");
    }

    /// <summary>
    /// Re-bakes the decor shadow over a rectangle in local metres, after instances were removed from or added to
    /// <see cref="Foliage"/> (a tree cut or planted). Only that rectangle's texels are recomputed and uploaded.
    /// </summary>
    public void RebakeShadows(Rect2 area)
    {
        if (Shadows == null) return;
        float t = Shadows.Texel;
        int x0 = (int)Mathf.Floor(area.Position.X / t), z0 = (int)Mathf.Floor(area.Position.Y / t);
        int x1 = (int)Mathf.Ceil(area.End.X / t), z1 = (int)Mathf.Ceil(area.End.Y / t);
        ShadowBake.Rebake(Shadows, World, Foliage, _foliage.Proxies, x0, z0, x1, z1);
        _shadowImage.SetData(Shadows.Size, Shadows.Size, false, Image.Format.R8, ShadowBytes(Shadows));
        _shadowTexture.Update(_shadowImage);
    }

    void BakeShadows()
    {
        var sun = Lighting.Sun.ToSun();
        Shadows = ShadowBake.Bake(World, Foliage, _foliage.Proxies, sun, MathF.Max(0.1f, Lighting.Bake.Texel), Math.Max(0, Lighting.Bake.Blur), Lighting.Bake.Softness);
        _shadowImage = Image.CreateFromData(Shadows.Size, Shadows.Size, false, Image.Format.R8, ShadowBytes(Shadows));
        if (_shadowTexture == null || _shadowTexture.GetWidth() != Shadows.Size) _shadowTexture = ImageTexture.CreateFromImage(_shadowImage);
        else _shadowTexture.Update(_shadowImage);
        float extent = Shadows.Size * Shadows.Texel;
        RenderingServer.GlobalShaderParameterSet("terrain_shadow", _shadowTexture);
        RenderingServer.GlobalShaderParameterSet("terrain_shadow_rect", new Vector4(GlobalPosition.X, GlobalPosition.Z, 1f / extent, 1f / extent));
        RenderingServer.GlobalShaderParameterSet("terrain_shadow_strength", Math.Clamp(Lighting.Bake.Strength, 0f, 1f));
    }

    static byte[] ShadowBytes(ShadowMap map)
    {
        var bytes = new byte[map.Light.Length];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Math.Clamp((int)MathF.Round(map.Light[i] * 255f), 0, 255);
        return bytes;
    }

    /// <summary>Points <see cref="Sun"/> and sets its colour, and the sky and fog of <see cref="Environment"/>, from the preset.</summary>
    void ApplyLighting()
    {
        var l = Lighting;
        if (Sun != null)
        {
            var to = l.Sun.ToSun();
            Sun.Basis = Basis.LookingAt(-new Vector3(to.X, to.Y, to.Z), MathF.Abs(to.Y) > 0.99f ? Vector3.Forward : Vector3.Up);
            Sun.LightColor = ToColor(l.Sun.Color);
            Sun.LightEnergy = l.Sun.Energy;
            Sun.LightAngularDistance = l.Sun.AngularSize;
        }
        if (Environment?.Environment is { } env)
        {
            env.AmbientLightEnergy = l.Sky.AmbientEnergy;
            env.FogLightColor = ToColor(l.Fog.Color);
            env.FogDensity = l.Fog.Density;
            env.Sky ??= new Sky { RadianceSize = Sky.RadianceSizeEnum.Size256 };
            if (!string.IsNullOrEmpty(l.Sky.Panorama) && ResourceLoader.Exists(l.Sky.Panorama))
            {
                env.Sky.SkyMaterial = new PanoramaSkyMaterial { Panorama = GD.Load<Texture2D>(l.Sky.Panorama), EnergyMultiplier = l.Sky.Energy };
                // turn the sky so its painted sun lies along the light: painted azimuth a shows at a - 180 - rotation,
                // and our sun yaw y points along (sin y, cos y), which is azimuth 180 - y
                env.SkyRotation = new Vector3(0, Mathf.DegToRad(l.Sky.SunAzimuth + l.Sun.Yaw - 360f), 0);
            }
            else
            {
                if (env.Sky.SkyMaterial is not ProceduralSkyMaterial) env.Sky.SkyMaterial = new ProceduralSkyMaterial();
                var sky = (ProceduralSkyMaterial)env.Sky.SkyMaterial;
                sky.SkyTopColor = ToColor(l.Sky.Top);
                sky.SkyHorizonColor = ToColor(l.Sky.Horizon);
                sky.GroundHorizonColor = ToColor(l.Sky.Horizon);
                sky.EnergyMultiplier = l.Sky.Energy;
                env.SkyRotation = Vector3.Zero;
            }
        }
    }

    static Color ToColor(string hex)
    {
        var c = LevelJson.ParseColor(hex);
        return new Color(c.X, c.Y, c.Z);
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

    /// <summary>The node placed for a level placement (by its id), or null.</summary>
    public Node3D PlacedNode(string id) => _placements?.Node(id);

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
