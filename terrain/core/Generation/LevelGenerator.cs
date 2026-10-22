#nullable disable
using System;
using System.Collections.Generic;
using Terrain.Core.Foliage;
using Terrain.Core.Generation.Stages;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Core.Generation;

public sealed class GenerationResult
{
    public WorldData World { get; init; }
    /// <summary>Rule failures; empty when the level came out as authored.</summary>
    public IReadOnlyList<string> Issues { get; init; }
}

/// <summary>
/// Runs the stages in order: Shape → Stamps → Detail → Erosion → Zones → Bands → Climate → Regions → Biomes →
/// Constraints. Every stage has its own seed derived from the level seed, so tuning or rerolling one never changes another.
/// </summary>
public static class LevelGenerator
{
    /// <param name="foliageSets">The level's foliage sets; regional ones (with a climate) are assigned to regions here.</param>
    public static GenerationResult Generate(LevelDefinition level, LayoutDefinition layout, PaletteDefinition palette,
        IReadOnlyList<FoliageSetDefinition> foliageSets = null)
    {
        var problems = LevelJson.Check(level);
        if (problems.Length > 0) throw new ArgumentException("Level is not usable: " + string.Join("; ", problems));

        int samplesPerCell = (int)MathF.Round(level.CellSize / level.SampleSpacing);
        // round the map to whole cells so every sample belongs to a cell
        int cells = Math.Max(1, (int)MathF.Round(level.Size / level.CellSize));
        var world = new WorldData(cells * samplesPerCell + 1, level.SampleSpacing, samplesPerCell);

        ShapeStage.Run(world, layout.Falloff, Seeds.Derive(level.Seed, "shape"));
        StampStage.Run(world, layout.Stamps, Seeds.Derive(level.Seed, "stamps"));
        DetailStage.Run(world, layout.Detail, Seeds.Derive(level.Seed, "detail"));
        ErosionStage.Run(world, layout.Erosion, Seeds.Derive(level.Seed, "erosion"));
        ZoneStage.Run(world, level.Zones, Seeds.Derive(level.Seed, "zones"));
        BandStage.Run(world, palette);
        ClimateStage.Run(world, level.Climate, Seeds.Derive(level.Seed, "climate"));
        RegionStage.Run(world, level.Regions, Seeds.Derive(level.Seed, "regions"));
        BiomeStage.Run(world, foliageSets ?? Array.Empty<FoliageSetDefinition>(), Seeds.Derive(level.Seed, "biomes"));
        var issues = ConstraintStage.Run(world, level.Zones, level.Placements);

        return new GenerationResult { World = world, Issues = issues };
    }
}
