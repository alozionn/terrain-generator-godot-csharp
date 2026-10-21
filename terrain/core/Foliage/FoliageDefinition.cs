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
    /// <summary>Distance where the clump switches to its lighter LOD (about a third of the blades, wider).</summary>
    public float LodDistance { get; set; } = 25f;
    public bool CastShadows { get; set; } = true;
}
