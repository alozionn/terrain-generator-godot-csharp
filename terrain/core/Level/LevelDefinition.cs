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
    /// <summary>Pre-placed buildings and props.</summary>
    public List<PlacementDefinition> Placements { get; set; } = new();
    /// <summary>Optional ocean preset id; empty means no ocean.</summary>
    public string Ocean { get; set; } = "";
    /// <summary>Lighting preset id (data/lighting/*.json): sun, sky and fog. Empty uses "default".</summary>
    public string Lighting { get; set; } = "";
    /// <summary>Temperature and moisture over the map; regional foliage sets pick their regions by them.</summary>
    public ClimateDefinition Climate { get; set; } = new();
    public RegionsDefinition Regions { get; set; } = new();
}

/// <summary>
/// Temperature and moisture, both 0..1 (cold..hot, dry..wet). Temperature: a base, a gradient across the map, cooler
/// with height, plus slow noise. Moisture: a base, wetter near the coast, plus its own noise.
/// </summary>
public sealed class ClimateDefinition
{
    public float Temperature { get; set; } = 0.55f;
    /// <summary>Change from the map's west to east edge (x) and north to south edge (z).</summary>
    public float[] Gradient { get; set; } = { 0f, 0f };
    /// <summary>Temperature lost per metre above sea level.</summary>
    public float Lapse { get; set; } = 0.025f;
    public float Moisture { get; set; } = 0.45f;
    /// <summary>Moisture added at the shoreline, fading out over <see cref="CoastReach"/> metres inland.</summary>
    public float Coast { get; set; } = 0.25f;
    public float CoastReach { get; set; } = 30f;
    /// <summary>Roughly ± how far slow noise moves temperature, 0..1.</summary>
    public float Noise { get; set; } = 0.12f;
    /// <summary>Roughly ± how far slow noise moves moisture, 0..1.</summary>
    public float MoistureNoise { get; set; } = 0.25f;
    /// <summary>Noise cycles per metre (both).</summary>
    public float Frequency { get; set; } = 0.012f;
}

/// <summary>Voronoi regions: the land is split into cells, and each region takes one regional foliage set.</summary>
public sealed class RegionsDefinition
{
    /// <summary>Typical distance between region centres, metres.</summary>
    public float Size { get; set; } = 40f;
    /// <summary>Domain warp in metres, so borders wander instead of running straight.</summary>
    public float Warp { get; set; } = 12f;
    /// <summary>Width in metres over which neighbouring regions' foliage interleaves.</summary>
    public float Blend { get; set; } = 6f;
}

/// <summary>A playable rectangle of cells whose ground is pulled into a height window.</summary>
public sealed class ZoneDefinition
{
    public string Id { get; set; } = "";
    /// <summary>Top-left cell (x, z).</summary>
    public int[] Origin { get; set; } = { 0, 0 };
    /// <summary>Size in cells (x, z).</summary>
    public int[] Cells { get; set; } = { 1, 1 };
    /// <summary>Height window (min, max) in metres: the ground is held inside it, and only cells inside it are buildable.</summary>
    public float[] Height { get; set; } = { 1.0f, 1.4f };
    /// <summary>Metres over which the levelling fades back into the surrounding terrain.</summary>
    public float Blend { get; set; } = 8f;
    /// <summary>0..1: how strongly the ground is levelled (pulled toward its smoothed self, inside the window).</summary>
    public float Level { get; set; } = 1f;
    /// <summary>Metres the terrain is smoothed over before levelling: larger irons out more of its small bumps.</summary>
    public float Smooth { get; set; } = 6f;
    /// <summary>Steepest buildable cell, as rise over run across the cell (0.25 is about 14 degrees).</summary>
    public float MaxSlope { get; set; } = 0.25f;
    /// <summary>Corner radius in cells.</summary>
    public float Corner { get; set; }
    /// <summary>Metres the area's outline wanders in and out, so it doesn't read as a rectangle.</summary>
    public float Ragged { get; set; }
    /// <summary>Constraint: at least one cell must border water. A sea zone never lifts water; its cells stop at the shore.</summary>
    public bool TouchesSea { get; set; }
    /// <summary>Constraint: at least this fraction of the area's cells must come out buildable.</summary>
    public float MinBuildable { get; set; } = 0.5f;
}

/// <summary>
/// A building or prop standing on a footprint of cells. It sits at the lowest ground under the footprint, so its
/// base sinks into any higher ground rather than floating; the terrain is not flattened for it.
/// </summary>
public sealed class PlacementDefinition
{
    public string Id { get; set; } = "";
    /// <summary>res:// scene (.tscn, .glb…).</summary>
    public string Scene { get; set; } = "";
    /// <summary>Top-left cell (x, z).</summary>
    public int[] Cell { get; set; } = { 0, 0 };
    /// <summary>Footprint in cells (x, z), 1–4 per side.</summary>
    public int[] Cells { get; set; } = { 1, 1 };
    /// <summary>Degrees around Y.</summary>
    public float Rotation { get; set; }
    /// <summary>Scale the model uniformly so its widest side fills the footprint; false keeps its authored size.</summary>
    public bool Fit { get; set; } = true;
    /// <summary>Fraction of the footprint the model fills when fitted.</summary>
    public float Fill { get; set; } = 0.9f;
}

/// <summary>The authored silhouette: island falloff, height stamps and noise detail.</summary>
public sealed class LayoutDefinition
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public FalloffDefinition Falloff { get; set; } = new();
    public List<StampDefinition> Stamps { get; set; } = new();
    public DetailDefinition Detail { get; set; } = new();
    /// <summary>Gullies carved down slopes after the detail noise; off unless a strength is given.</summary>
    public ErosionDefinition Erosion { get; set; } = new();
}

/// <summary>Erosion filter settings (see ErosionStage); names follow runevision's article.</summary>
public sealed class ErosionDefinition
{
    /// <summary>Overall height of the effect in metres; 0 switches erosion off.</summary>
    public float Strength { get; set; }
    /// <summary>Share of the strength that goes into gully shapes (the rest sharpens peaks and deepens valleys).</summary>
    public float GullyWeight { get; set; } = 0.5f;
    /// <summary>Coarsest gullies per metre across the slope.</summary>
    public float Frequency { get; set; } = 0.04f;
    public int Octaves { get; set; } = 5;
    /// <summary>Amplitude kept per finer octave.</summary>
    public float Gain { get; set; } = 0.5f;
    /// <summary>Frequency multiplier per octave.</summary>
    public float Lacunarity { get; set; } = 2f;
    /// <summary>How far finer gullies spread from the steepest parts of coarser ones; lower confines them.</summary>
    public float Detail { get; set; } = 1.5f;
    /// <summary>Slope (rise over run) below which there are no gullies, so gentle rolls stay smooth.</summary>
    public float SlopeStart { get; set; } = 0.2f;
    /// <summary>Slope (rise over run) treated as fully steep for the gully mask.</summary>
    public float AssumedSlope { get; set; } = 0.6f;
    /// <summary>0..1: how much gully crests are raised (troughs are always carved in full); low keeps ridges rounded.</summary>
    public float CrestLift { get; set; } = 0.35f;
    /// <summary>Phacelle cell size in wavelengths.</summary>
    public float CellScale { get; set; } = 1f;
    /// <summary>Altitude treated as valley floor (fade target -1); land below it is left alone.</summary>
    public float ValleyHeight { get; set; } = 1f;
    /// <summary>Altitude treated as peak (fade target +1); 0 uses the highest point of the map.</summary>
    public float PeakHeight { get; set; }
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
    /// <summary>How eroded ground is painted from the erosion ridge map.</summary>
    public ErosionPaintDefinition Erosion { get; set; } = new();
}

/// <summary>
/// Painting from the erosion ridge map: crests turn toward the cliff texture (bare rock), creases toward a scree or
/// soil texture, and the tint lightens crests and darkens creases so the gullies read even from far away.
/// </summary>
public sealed class ErosionPaintDefinition
{
    /// <summary>How strongly crests become cliff rock, 0..2.</summary>
    public float CrestRock { get; set; } = 1.2f;
    /// <summary>How strongly creases lose cliff rock, 0..2.</summary>
    public float CreaseSoften { get; set; } = 0.8f;
    /// <summary>Ground texture id that collects in creases (scree, soil); empty for none.</summary>
    public string Crease { get; set; } = "";
    /// <summary>Most the crease texture shows, 0..1.</summary>
    public float CreaseAmount { get; set; } = 0.7f;
    /// <summary>± brightness from the ridge map, 0..1.</summary>
    public float Shade { get; set; } = 0.18f;
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
