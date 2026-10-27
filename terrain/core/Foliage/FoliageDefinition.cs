#nullable disable
using System.Collections.Generic;

namespace Terrain.Core.Foliage;

/// <summary>
/// A foliage set (forest, meadow, pond edge…): species placed by seeded rules, never by hand. A level lists the sets
/// it uses; each species places itself where its rules allow.
/// </summary>
public sealed class FoliageSetDefinition
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    /// <summary>Placed in this order; earlier species claim space first.</summary>
    public List<SpeciesDefinition> Species { get; set; } = new();
    /// <summary>
    /// Regional set (a biome): grows only in the Voronoi regions it is chosen for, by climate. Null means the set grows
    /// everywhere its species' rules allow.
    /// </summary>
    public BiomeClimateDefinition Climate { get; set; }
    /// <summary>How a regional set changes the ground under it; null leaves the palette's ground alone.</summary>
    public BiomeGroundDefinition Ground { get; set; }
}

/// <summary>The climate a regional set likes. A region picks among the sets whose windows hold its mean climate.</summary>
public sealed class BiomeClimateDefinition
{
    /// <summary>Temperature window, 0 cold .. 1 hot.</summary>
    public float[] Temperature { get; set; } = { 0f, 1f };
    /// <summary>Moisture window, 0 dry .. 1 wet.</summary>
    public float[] Moisture { get; set; } = { 0f, 1f };
    /// <summary>Relative chance against other sets that fit the same region.</summary>
    public float Weight { get; set; } = 1f;
}

/// <summary>Ground under a regional set: a tint, and patches of another texture (dirt, leaf litter, dry grass).</summary>
public sealed class BiomeGroundDefinition
{
    /// <summary>Bands the ground changes apply to (e.g. the vegetated ones); empty means every band.</summary>
    public List<string> Bands { get; set; } = new();
    /// <summary>"#rrggbb" multiplied over the band tint.</summary>
    public string Tint { get; set; } = "#ffffff";
    /// <summary>Ground texture id for patches; empty for none.</summary>
    public string Patches { get; set; } = "";
    /// <summary>Most the patch texture shows, 0..1.</summary>
    public float PatchAmount { get; set; } = 0.8f;
    /// <summary>Patch noise, cycles per metre.</summary>
    public float PatchFrequency { get; set; } = 0.06f;
    /// <summary>-1..1: noise below this gets no patch; higher means fewer, smaller patches.</summary>
    public float PatchThreshold { get; set; } = 0.15f;
    /// <summary>Patches on playable cells are scaled by this, so the board stays readable.</summary>
    public float PlayablePatches { get; set; } = 0.4f;
}

public sealed class SpeciesDefinition
{
    public string Id { get; set; } = "";
    /// <summary>Mesh id from data/foliage/meshes.json.</summary>
    public string Mesh { get; set; } = "";
    /// <summary>Several mesh ids to pick from per instance (e.g. tree variants); used instead of <see cref="Mesh"/> when set.</summary>
    public List<string> Meshes { get; set; } = new();
    /// <summary>Expected instances per 100 m² where every rule passes.</summary>
    public float Density { get; set; } = 1f;
    /// <summary>Minimum distance between two instances of this species, metres (also the jitter grid size).</summary>
    public float Spacing { get; set; } = 2f;
    /// <summary>Radius this instance keeps clear of species placed later; 0 claims nothing.</summary>
    public float Footprint { get; set; }
    /// <summary>Allowed band names; empty allows every band.</summary>
    public List<string> Bands { get; set; } = new();
    public float[] Height { get; set; } = { 0.3f, 100f };
    public float MaxSlope { get; set; } = 30f;
    /// <summary>Keep off playable cells (trees, rocks); grass and flowers usually allow them.</summary>
    public bool AvoidPlayable { get; set; } = true;
    /// <summary>Extra metres kept clear around playable zones.</summary>
    public float PlayableMargin { get; set; }
    /// <summary>Scale multiplier on playable cells (e.g. shorter grass so the board stays readable).</summary>
    public float PlayableScale { get; set; } = 1f;
    /// <summary>Metres around playable cells over which scale and density ease from the board's values back to normal.</summary>
    public float PlayableFade { get; set; } = 5f;
    /// <summary>Fraction of instances kept on playable cells (e.g. a few tufts instead of a carpet).</summary>
    public float PlayableDensity { get; set; } = 1f;
    public ClusterDefinition Clusters { get; set; } = new();
    public float[] Scale { get; set; } = { 0.8f, 1.2f };
    /// <summary>Random lean in degrees.</summary>
    public float Tilt { get; set; }
    /// <summary>0..1: how far instances lean with the slope they stand on (rocks bedded into a hillside).</summary>
    public float AlignToSlope { get; set; }
    /// <summary>[min, max] metres (at scale 1) an instance is sunk into the ground; empty sinks nothing.</summary>
    public float[] Sink { get; set; } = System.Array.Empty<float>();
    /// <summary>
    /// Allowed range of the erosion ridge map, -1 (gully floor) .. +1 (crest). E.g. [-1, -0.1] keeps boulders to the
    /// gullies, where loose rock collects, and off the crests. Ground that wasn't eroded is 0.
    /// </summary>
    public float[] Ridge { get; set; } = { -1f, 1f };
    /// <summary>± brightness variation per instance, 0..1.</summary>
    public float TintJitter { get; set; } = 0.1f;
    /// <summary>
    /// "#rrggbb" this species paints onto the ground under it, which the ground shader fades in with distance: far away,
    /// where blades thin out, a meadow still reads as a meadow. Empty paints nothing.
    /// </summary>
    public string Cover { get; set; } = "";
    /// <summary>Ground area one instance covers, square metres (times its scale squared).</summary>
    public float CoverArea { get; set; } = 0.5f;
}

/// <summary>Noise mask that groups a species into patches (groves, flower beds). Threshold 0 disables it.</summary>
public sealed class ClusterDefinition
{
    /// <summary>Cycles per metre.</summary>
    public float Frequency { get; set; } = 0.03f;
    /// <summary>-1..1: noise below this gets nothing; higher means fewer, tighter patches.</summary>
    public float Threshold { get; set; } = -1f;
    /// <summary>Noise range over which density fades in above the threshold.</summary>
    public float Softness { get; set; } = 0.2f;
}

/// <summary>Meshes available to species. Order matters: the index is the instancer mesh id.</summary>
public sealed class FoliageMeshSetDefinition
{
    public int Version { get; set; } = 1;
    public List<FoliageMeshDefinition> Meshes { get; set; } = new();
    /// <summary>Colour schemes by id: material name to "#rrggbb", for flat-coloured kits (a mesh picks one by "colors").</summary>
    public Dictionary<string, Dictionary<string, string>> ColorSchemes { get; set; } = new();
}

public sealed class FoliageMeshDefinition
{
    public string Id { get; set; } = "";
    /// <summary>res:// scene, or "procedural:kind": grass, flowers, or a stand-in tree_round, tree_pine, bush, rock.</summary>
    public string Scene { get; set; } = "";
    /// <summary>"#rrggbb" base colour for procedural stand-ins.</summary>
    public string Color { get; set; } = "#7fae4a";
    /// <summary>Distance at which the mesh stops drawing.</summary>
    public float Range { get; set; } = 150f;
    /// <summary>Clumps (procedural:grass, procedural:flowers): blade root colour; "color" is the tip.</summary>
    public string RootColor { get; set; } = "#3a5a1e";
    /// <summary>Flower head colour (procedural:flowers).</summary>
    public string HeadColor { get; set; } = "#f2d14a";
    public int Blades { get; set; } = 22;
    public float[] BladeHeight { get; set; } = { 0.25f, 0.5f };
    public float BladeWidth { get; set; } = 0.05f;
    /// <summary>Clump radius in metres.</summary>
    public float Radius { get; set; } = 0.3f;
    /// <summary>Distance where blades start dropping out of a clump (x0.4); for authored multi-LOD scenes, where LOD0 ends.</summary>
    public float LodDistance { get; set; } = 25f;
    public bool CastShadows { get; set; } = true;
    /// <summary>
    /// Shadow baked into the ground from simple proxies (a squashed sphere for the canopy, a thin one for the trunk)
    /// instead of a realtime shadow: steady, soft and free per frame. Turns realtime shadows off for this mesh.
    /// </summary>
    public bool BakedShadow { get; set; }
    /// <summary>
    /// Optional canopy proxy in model units: [radius, half height, centre height]; empty measures the leaf surfaces.
    /// </summary>
    public float[] Canopy { get; set; } = System.Array.Empty<float>();
    /// <summary>How much light the canopy stops per metre it is crossed, so leaves cast a lighter shadow than rock.</summary>
    public float ShadowDensity { get; set; } = 0.9f;
    /// <summary>Canopy lobes fitted for the baked shadow (more gives a more detailed outline, and a slower bake).</summary>
    public int Lobes { get; set; } = 5;
    /// <summary>0..1: how much the baked shadow breaks up into dapples of light through the leaves.</summary>
    public float Dapple { get; set; } = 0.6f;
    /// <summary>Authored scenes: metres of sway at the top of the model; 0 keeps the imported materials as they are.</summary>
    public float Wind { get; set; }
    /// <summary>Authored scenes: 0..1, how much less the model sways (wood not at all at 1, leaves 40%); for trees.</summary>
    public float Stiffness { get; set; }
    /// <summary>Authored scenes: how much leaf normals bend outward from the canopy (soft, painterly shading), 0..1.</summary>
    public float LeafNormals { get; set; } = 0.35f;
    /// <summary>Authored scenes: thin instances out with camera distance like the grass does (ground cover).</summary>
    public bool Fade { get; set; }
    /// <summary>
    /// Distances where instances start and finish thinning out (default 30% and 90% of <see cref="Range"/>), measured
    /// shorter when looking down. Grass and faded ground cover only.
    /// </summary>
    public float[] Thin { get; set; } = System.Array.Empty<float>();
    /// <summary>Fraction still kept past the end of <see cref="Thin"/>; they last until <see cref="Range"/>.</summary>
    public float ThinMin { get; set; }
    /// <summary>0..1: how much the kept instances grow to cover for the dropped ones.</summary>
    public float Compensate { get; set; }
    /// <summary>
    /// Clumps: distances where blades within a clump start and finish dropping out (default 40% of
    /// <see cref="LodDistance"/> and 30% of <see cref="Range"/>), down to <see cref="BladeMin"/>. Past that a lighter
    /// mesh with only the kept blades takes over.
    /// </summary>
    public float[] BladeThin { get; set; } = System.Array.Empty<float>();
    public float BladeMin { get; set; } = 0.3f;
    /// <summary>Clumps: 0..1, how much the kept blades widen.</summary>
    public float BladeWiden { get; set; } = 0.7f;
    /// <summary>Clumps: fraction of blades that carry a flower head (procedural:flowers).</summary>
    public float Heads { get; set; } = 0.5f;
    /// <summary>Clumps: sit in the baked decor shadow; for authored scenes, ground cover that should too.</summary>
    public bool ReceiveBake { get; set; } = true;
    /// <summary>Authored scenes: ["#root", "#tip"] recolours leaf and grass surfaces by height, keeping only the texture's light and dark.</summary>
    public string[] Recolor { get; set; } = System.Array.Empty<string>();
    /// <summary>Authored scenes: a colour scheme from the mesh set; surfaces whose material it names get that flat colour.</summary>
    public string Colors { get; set; } = "";
}
