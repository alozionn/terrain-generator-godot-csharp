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
    /// <summary>Fraction of instances kept on playable cells (e.g. a few tufts instead of a carpet).</summary>
    public float PlayableDensity { get; set; } = 1f;
    public ClusterDefinition Clusters { get; set; } = new();
    public float[] Scale { get; set; } = { 0.8f, 1.2f };
    /// <summary>Random lean in degrees.</summary>
    public float Tilt { get; set; }
    /// <summary>± brightness variation per instance, 0..1.</summary>
    public float TintJitter { get; set; } = 0.1f;
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
    /// <summary>Authored scenes: metres of sway at the top of the model; 0 keeps the imported materials as they are.</summary>
    public float Wind { get; set; }
    /// <summary>Authored scenes: how much leaf normals bend outward from the canopy (soft, painterly shading), 0..1.</summary>
    public float LeafNormals { get; set; } = 0.6f;
    /// <summary>Authored scenes: thin instances out with camera distance like the grass does (ground cover).</summary>
    public bool Fade { get; set; }
    /// <summary>Authored scenes: ["#root", "#tip"] recolours leaf and grass surfaces by height, keeping only the texture's light and dark.</summary>
    public string[] Recolor { get; set; } = System.Array.Empty<string>();
}
