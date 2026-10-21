# Terrain blueprint: design

The terrain generator for Mushrook. It makes the board a level is played on: a smooth heightmap with playable zones, ground bands, and later resources and foliage. It is built the way the ocean blueprint is: a core in plain C# with no Godot types, a Godot node that draws it, and parameter sets for the UI.

## The game this serves

Mushrook is a builder card game. A level hands the player a board and a deck. The goal is usually to build a specific arrangement of buildings, sometimes with specific qualities. Everything here serves that:

- The **playable** part of the board must be readable, buildable and predictable.
- The rest of the map is **decor**: it can be as dramatic as the layout wants.
- Levels are authored. The seed fills in detail and variation, never the rules.

## Units and contracts

- Metres everywhere. Sea level is y = 0, and the ocean simply covers everything below it. Heights can go below 0 so the seabed continues under the water.
- The heightmap is a grid of float samples, `sampleSpacing` metres apart (default 1 m).
- A playable cell is `cellSize` metres square (default 2 m, i.e. 2 × 2 samples). Buildings cover 1–4 cells.
- Map size and playable size are set per level (for example 30 × 30 cells on a 500 m map, or 100 × 100 cells on 200 m).

## Data: what a level is made of

Three kinds of JSON file. The level picks a layout and a palette by id.

**Level** (`data/levels/*.json`): seed, map size, cell size, the layout and palette ids, and the playable zones.

**Layout** (`data/layouts/*.json`): the silhouette, like a Path of Exile zone. It's the same shape every time, with seeded detail on top.
- `falloff`: island shape (or `none` for land to the edges).
- `stamps`: mountain, hill, plateau, valley, cove, at normalised positions with a radius and height.
- `detail`: noise amplitude and frequency layered over everything.

**Palette** (`data/palettes/*.json`): which ground band each height range uses, its colour now, and its texture later. Switching palettes changes the look without touching the layout.

### Playable zones

"Playable" is not tied to grass. A zone is a rectangle of cells plus a **target height window**. The generator pulls the ground inside the zone into that window as a gently rolling surface, then blends it back into the surrounding terrain over `blend` metres.

```jsonc
"zones": [
  { "id": "village", "origin": [35, 35], "cells": [30, 30], "height": [1.0, 1.4] },
  { "id": "port",    "origin": [40, 88], "cells": [20, 6],  "height": [0.1, 0.3], "touchesSea": true },
  { "id": "outpost", "origin": [72, 18], "cells": [6, 6],   "height": [6.0, 6.4] }
]
```

- A level may have one zone or many. How separate zones connect (ramps, paths or nothing) is undecided.
- Buildings sit at a height fitted to the ground under their footprint. The ground is not flattened per building.
- Which band a building may stand on is a building rule (`allowedTerrain` by band name), not a zone rule.
- `touchesSea` is a constraint: at least one cell must border water. Generation reports it when it fails.

### Default bands (to be tuned in the engine)

| band | from (m) | notes |
|---|---|---|
| seabed | −∞ | under the ocean |
| shore | −1.0 | ports go here; a port cell probably wants 0.1–0.3 with a pier model reaching out |
| sand | 0.3 | |
| grass | 1.0 | the typical playable band (1.0–1.4) |
| rock / hill / mountain | higher | per palette |

The palette is the **only** place these numbers live. Mushrook currently has three different waterlines (`TerrainRange`, the shader bands, and the remembered −1), and this replaces all of them.

## Generator

A pipeline of pure stages. Each takes the world data and fills in one more layer:

```
Shape (falloff) → Stamps → Detail → [Erosion] → Zones → Bands → [Resources] → [Foliage rules]
```

Bracketed stages come later.

- Each stage gets its own seed, derived from the world seed and the stage's name (`Seeds.Derive(seed, "detail")`). Rerolling or tuning one stage leaves the others untouched.
- After generating, constraints are checked (zone height windows, `touchesSea`, and later resource counts). Failures are reported as issues, not exceptions.
- Every stage can output an image (height, bands, zones) for review, like the ocean captures.
- Saves store the seed, the level id and the player's edits, never the generated world.
- **Erosion:** runevision's mask-based erosion filter, applied as a stage after Detail.
- **Resources (iron, wood…):** things sitting on the ground, placed with per-resource rules. They are not height bands.

## Rendering: Terrain3D

[Terrain3D](https://github.com/TokisanGames/Terrain3D) (v1.1.0-rc1) draws the ground: clipmap mesh with LOD, collision, a 32-slot texture array with height blending, and the foliage instancer. We hand it three maps and a list of instance transforms; only `terrain/integration/` knows it exists.

- **Height map:** `WorldData.Height` as is.
- **Control map:** per sample, a base and an overlay texture plus a blend amount (`GroundMaps`, plain C#). Band edges blend the lower band into the upper, and steep ground overlays the palette's cliff texture. A texture is never blended with itself, because Terrain3D renders that black.
- **Colour map:** the band tint (so one grass texture serves "grass1" and "grass2") times a large-scale brightness drift.
- **Ground textures:** `data/ground/textures.json`, packed as albedo + height and normal + roughness. Files load through the importer so exports work; entries with `procedural` colours are noise stand-ins until real textures land (sand and seabed for now).
- **Playable grid:** one decal per zone, projected onto render layer 1 only. Foliage is on layer 2, so it never gets grid lines.
- POM can be an option later, but at a board-game camera distance and under dense grass it would barely show.

## Foliage

- **Sets** (`data/foliage/sets/*.json`, listed by the level): species with rules for band, height range, maximum slope, playable cells (avoid, with a margin, or allow, as grass does), cluster noise for groves and flower beds, density and spacing, scale, tilt and tint jitter.
- **Placement** (`FoliageStage`, plain C#): a jittered grid per species, each species seeded from the world seed and its own name. Species placed earlier claim footprints that later ones avoid: rocks, then trees, then bushes, then ground cover.
- **Meshes** (`data/foliage/meshes.json`): a res:// scene, or `procedural:`. Grass and flowers are generated blade clumps (three LODs) with `terrain/surface/grass.gdshader`: root-to-tip gradient, rolling wind gusts, sky-bent normals lit the same on both sides. Trees, bushes and rocks are single-surface stand-ins until real models land.
- **Distance:** grass thins rather than shrinks. Each blade drops out at its own random distance (down to 30% of a clump), and each clump at its own distance beyond that, so the grass edge is a soft circle around the camera and never shows the instancer's cells.
- **Grass scatter:** dense meadow patches from cluster noise plus sparse tufts in between; `playableDensity` and `playableScale` keep only a few tufts on the board.
- **Rendering:** Terrain3D's instancer (LOD, visibility range, shadows).
- **Next:** temperature map plus Voronoi regions to choose sets per area; real tree and rock models.
- **Assets:** Quaternius Stylized Nature MegaKit; textures from Poly Haven, 3dtextures.me and freestylized (check its licence). The target look is painterly, anime, a bit Warcraft.

## Not yet

- Assembling levels from multiple authored scenes.
- SQLite. A repository will produce the same plain records, so it slots in behind the JSON loader.
- Wave function collapse tilesets, rivers and ponds, a water-depth texture for the ocean.
