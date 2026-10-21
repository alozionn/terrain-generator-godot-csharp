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
    /// <summary>res:// scene, or "procedural:kind" for a built-in stand-in (tree_round, tree_pine, bush, rock, card).</summary>
    public string Scene { get; set; } = "";
    /// <summary>"#rrggbb" base colour for procedural stand-ins.</summary>
    public string Color { get; set; } = "#7fae4a";
    /// <summary>Card size in metres (procedural:card).</summary>
    public float[] Size { get; set; } = { 0.6f, 0.5f };
    /// <summary>Distance at which the mesh stops drawing.</summary>
    public float Range { get; set; } = 150f;
    public bool CastShadows { get; set; } = true;
}
