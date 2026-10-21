#nullable disable
using System.Collections.Generic;

namespace Terrain.Core.Level;

/// <summary>One level: seed, map size, which layout and palette it uses, and its playable zones. Lengths in metres, cells in cell units.</summary>
public sealed class LevelDefinition
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public uint Seed { get; set; } = 1;
    public string Layout { get; set; } = "";
    public string Palette { get; set; } = "";
    /// <summary>Map edge length in metres (square map).</summary>
    public float Size { get; set; } = 200f;
    public float SampleSpacing { get; set; } = 1f;
    public float CellSize { get; set; } = 2f;
    public List<ZoneDefinition> Zones { get; set; } = new();
    /// <summary>Foliage set ids (data/foliage/sets/*.json), placed in order.</summary>
    public List<string> Foliage { get; set; } = new();
    /// <summary>Optional ocean preset id; empty means no ocean.</summary>
    public string Ocean { get; set; } = "";
}

/// <summary>A playable rectangle of cells whose ground is pulled into a height window.</summary>
public sealed class ZoneDefinition
{
    public string Id { get; set; } = "";
    /// <summary>Top-left cell (x, z).</summary>
    public int[] Origin { get; set; } = { 0, 0 };
    /// <summary>Size in cells (x, z).</summary>
    public int[] Cells { get; set; } = { 1, 1 };
    /// <summary>Target height window (min, max) in metres.</summary>
    public float[] Height { get; set; } = { 1.0f, 1.4f };
    /// <summary>Metres over which the zone blends back into the surrounding terrain.</summary>
    public float Blend { get; set; } = 8f;
    /// <summary>Frequency of the zone's own gentle roll, in cycles per metre.</summary>
    public float RollFrequency { get; set; } = 0.04f;
    /// <summary>Constraint: at least one cell must border water.</summary>
    public bool TouchesSea { get; set; }
}

/// <summary>The authored silhouette: island falloff, height stamps and noise detail.</summary>
public sealed class LayoutDefinition
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public FalloffDefinition Falloff { get; set; } = new();
    public List<StampDefinition> Stamps { get; set; } = new();
    public DetailDefinition Detail { get; set; } = new();
}

public enum FalloffKind { Island, None }

public sealed class FalloffDefinition
{
    public FalloffKind Kind { get; set; } = FalloffKind.Island;
    /// <summary>Coast distance from the centre, as a fraction of the map size.</summary>
    public float Radius { get; set; } = 0.38f;
    /// <summary>Width of the land-to-sea slope, as a fraction of the map size.</summary>
    public float Edge { get; set; } = 0.08f;
    /// <summary>How much the coastline wobbles, as a fraction of the map size.</summary>
    public float CoastNoise { get; set; } = 0.06f;
    public float LandHeight { get; set; } = 1.2f;
    public float SeaDepth { get; set; } = -6f;
}

public enum StampKind { Mountain, Hill, Plateau, Valley, Cove }

public sealed class StampDefinition
{
    public StampKind Type { get; set; } = StampKind.Hill;
    /// <summary>Centre as a fraction of the map (x, z), 0..1.</summary>
    public float[] At { get; set; } = { 0.5f, 0.5f };
    public float Radius { get; set; } = 20f;
    /// <summary>Peak height (mountain, hill, plateau) or depth (valley, cove) in metres.</summary>
    public float Height { get; set; } = 5f;
}

public sealed class DetailDefinition
{
    public float Amplitude { get; set; } = 1.5f;
    public float Frequency { get; set; } = 0.02f;
    public int Octaves { get; set; } = 4;
    /// <summary>Domain-warp strength in metres; 0 disables warping.</summary>
    public float Warp { get; set; } = 12f;
}

/// <summary>Ground bands by height. Bands are sorted by <see cref="BandDefinition.From"/>; the first applies below everything.</summary>
public sealed class PaletteDefinition
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public List<BandDefinition> Bands { get; set; } = new();
    /// <summary>Metres over which neighbouring bands blend.</summary>
    public float Blend { get; set; } = 0.25f;
    public CliffDefinition Cliff { get; set; } = new();
    /// <summary>Large-scale brightness/hue drift so ground doesn't look flat from far away.</summary>
    public MacroDefinition Macro { get; set; } = new();
}

public sealed class BandDefinition
{
    public string Name { get; set; } = "";
    public float From { get; set; }
    /// <summary>"#rrggbb" flat colour for review images.</summary>
    public string Color { get; set; } = "#808080";
    /// <summary>Ground texture id from the texture set (data/ground/textures.json).</summary>
    public string Tex { get; set; } = "";
    /// <summary>"#rrggbb" multiplied over the texture, so one texture can serve several looks (grass1 vs grass2).</summary>
    public string Tint { get; set; } = "#ffffff";
}

/// <summary>Steep ground switches to the cliff texture, whatever its band.</summary>
public sealed class CliffDefinition
{
    public string Tex { get; set; } = "";
    public string Tint { get; set; } = "#ffffff";
    /// <summary>Slope in degrees where the cliff starts showing, and where it fully takes over.</summary>
    public float SlopeStart { get; set; } = 30f;
    public float SlopeFull { get; set; } = 45f;
}

public sealed class MacroDefinition
{
    /// <summary>± brightness change, 0..1.</summary>
    public float Amount { get; set; } = 0.1f;
    /// <summary>Cycles per metre.</summary>
    public float Frequency { get; set; } = 0.015f;
}

/// <summary>The ground textures available to palettes. Order matters: the index is the texture id on the GPU.</summary>
public sealed class TextureSetDefinition
{
    public int Version { get; set; } = 1;
    public List<GroundTextureDefinition> Textures { get; set; } = new();
}

public sealed class GroundTextureDefinition
{
    public string Id { get; set; } = "";
    /// <summary>RGB albedo with height in alpha.</summary>
    public string AlbedoHeight { get; set; } = "";
    /// <summary>RGB normal (OpenGL) with roughness in alpha.</summary>
    public string NormalRoughness { get; set; } = "";
    /// <summary>Texture repeats per metre.</summary>
    public float UvScale { get; set; } = 0.1f;
    /// <summary>Stand-in when there are no image files yet: noise between two colours.</summary>
    public string[] Procedural { get; set; } = System.Array.Empty<string>();
}
