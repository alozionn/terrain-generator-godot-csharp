# Tech_terrain: design

The terrain generator for Mushrook. It makes the board a level is played on: a smooth heightmap with playable zones, ground bands, and later resources and foliage. It is built the way Tech_ocean is: a core in plain C# with no Godot types, a Godot node that draws it, and parameter sets for the UI.

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

The level picks a layout, a palette, a lighting preset and its foliage sets by id.

**Level** (`data/levels/*.json`): seed, map size, cell size, the layout, palette and lighting ids, the playable zones, its foliage sets, and its climate (temperature, moisture) and region size.

**Layout** (`data/layouts/*.json`): the silhouette, like a Path of Exile zone. It's the same shape every time, with seeded detail on top.
- `falloff`: island shape (or `none` for land to the edges).
- `stamps`: mountain, hill, plateau, valley, cove, at normalised positions with a radius and height.
- `detail`: noise amplitude and frequency layered over everything.
- `erosion`: the erosion filter (strength, gully weight, frequency, octaves, detail, slope range, valley height); off when absent.

**Palette** (`data/palettes/*.json`): which ground band each height range uses, its colour now, and its texture later. Switching palettes changes the look without touching the layout.

**Lighting** (`data/lighting/*.json`): sun direction, colour, energy and angular size, sky colours and ambient energy, fog, and the decor shadow bake (texel size, blur, strength). See "Lighting and shadows".

**Foliage sets** (`data/foliage/sets/*.json`): species and their rules. A set with a `climate` is a biome and grows only in the regions it is chosen for; a set without one grows everywhere. See "Climate, regions and biomes".

### Playable zones

"Playable" is not tied to grass, and it is part of the terrain rather than cut into it. A zone is an **area** (a rectangle of cells with rounded `corner`s and an outline that wanders by `ragged` metres) plus a **height window**. The area gently levels the ground under it: heights are pulled toward a copy of the terrain smoothed over `smooth` metres, held inside the window, with the pull (`level`, 0..1) fading out over `blend` metres, so the land keeps its own gentle rolls. Then every cell in the area qualifies on its own ground: it is buildable when it lies in the window and is no steeper than `maxSlope` (rise over run across the cell), and only the largest connected patch is kept. The board's outline therefore follows the land. A level gets an issue when fewer than `minBuildable` of the area's cells qualify. A zone's levelling never disturbs another zone's fully levelled ground, so zones can sit side by side (the board and its port). Slanted buildings on slopes are a later idea: zones with a higher `maxSlope` and buildings that fit a slope.

```jsonc
"zones": [
  { "id": "village", "origin": [35, 35], "cells": [30, 30], "height": [1.0, 1.4], "corner": 6, "ragged": 5, "maxSlope": 0.25 },
  { "id": "port",    "origin": [76, 64], "cells": [10, 8],  "height": [0.1, 0.3], "touchesSea": true, "minBuildable": 0.2 }
]
```

- A level may have one zone or many. How separate zones connect (ramps, paths or nothing) is undecided.
- Buildings sit at a height fitted to the ground under their footprint. The ground is not flattened per building.
- Which band a building may stand on is a building rule (`allowedTerrain` by band name), not a zone rule.
- `touchesSea` is a constraint: at least one cell must border water. Generation reports it when it fails. A sea zone keeps the natural coastline: it never lifts water into land, and only its all-land cells are playable, so its edge follows the shore. Port buildings are meant to stand on those cells and reach out over the water beyond them (as Age of Empires ports do).

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
Shape (falloff) → Stamps → Detail → Erosion → Zones → Bands → Climate → Regions → Biomes → [Resources] → Foliage → Shadow bake
```

Bracketed stages come later.

- Each stage gets its own seed, derived from the world seed and the stage's name (`Seeds.Derive(seed, "detail")`). Rerolling or tuning one stage leaves the others untouched.
- After generating, constraints are checked (zone height windows, `touchesSea`, and later resource counts). Failures are reported as issues, not exceptions.
- Every stage can output an image (height, bands, zones, temperature, moisture, biomes) for review, like the ocean captures.
- Saves store the seed, the level id and the player's edits, never the generated world.
- **Erosion** (`ErosionStage`): a filter, not a water simulation, after runevision's "fast and gorgeous erosion filter" (blog.runevision.com, March 2026; the exact shader wasn't reachable, so details are reconstructed from the article). Each octave lays Phacelle stripes (cosine and sine waves from 4x4 jittered cells, blended, then stretched x2 and clamped to unit length) that run down the current flow direction. A fade target (-1 valley .. +1 peak, from altitude) says what the ground is without gullies; a mask (0 below `slopeStart`, eased to 1 at `assumedSlope`) says how much gully pattern is allowed. Each octave blends the fade target toward its stripes by the mask and becomes the next fade target; the mask then narrows to where this octave's stripes are themselves sloped (`detail` widens it), and the flow direction gains this octave's gully slopes by sign only, so finer gullies branch cleanly. The summed offset, minus the starting fade target so untouched ground keeps its height, times gully weight and strength, is added below Zones (crests only partly raised, `crestLift`, and the result softened once, so ridges stay rounded instead of turning 1 m samples into spikes); land below `valleyHeight` and the coast are left alone, and land is never cut below 0.4 m. The same offset, normalised, is the **ridge map** (`WorldData.Ridge`, +1 crest, -1 crease) that the ground painting and rock placement read; zones clear it where they flatten.
- **Resources (iron, wood…):** things sitting on the ground, placed with per-resource rules. They are not height bands.

## Climate, regions and biomes

One foliage set for a whole island looked the same everywhere. Now the island is split into regions, and each region grows the set its climate suits.

- **Climate** (`ClimateStage`, on the finished heights): temperature and moisture per sample, 0..1. Temperature is the level's base plus a gradient across the map, minus a lapse per metre of height (mountains are cold), plus slow noise. Moisture is a base plus a bonus near the coast (a distance transform from the sea) plus its own noise. All of it is in the level, so a cold level or a dry one is data.
- **Regions** (`RegionStage`): Voronoi cells, one jittered centre per `regions.size` square, looked up through a domain warp so borders wander. Small stranded pieces of a region (land only, so a region joined across water counts as two) are handed to the region around them. Each region records the mean climate of its land.
- **Biomes** (`BiomeStage`): a set with a `climate` window (temperature and moisture ranges, and a weight) is a biome. A region picks among the biomes whose windows hold its climate, weighted toward those it sits in the middle of, with its own roll, so neighbours with the same climate can still differ; when none fits, the nearest window wins. Sample sets: meadow (tall grass drifts, flower beds, lone oaks), forest (oak groves, ferns, mushrooms, litter), pinewood (cold slopes, pines, stones, wispy grass), heath (warm and dry: golden grass, twisted and dead trees).
- **Borders:** a biome's species look their region up from a randomly nudged position (up to `regions.blend` metres), so neighbouring biomes interleave instead of meeting on a line.
- **Ground:** a biome's `ground` tints the bands it lists and lays patches of its own texture (dirt, leaf litter, dry grass) by noise where no band edge or cliff already uses the overlay slot; fewer on the board. Tint and patch strength are blurred across borders.
- **Global sets** (no climate) grow everywhere their rules allow, as before. Rocks and stones live in one, placed first so every biome flows around them.

## Rendering

Our own renderer (`terrain/surface/`): at board scale (200–500 m) it costs about half of what Terrain3D did (GPU about 0.30 ms over an empty sky with cover and the baked shadow, against 0.53–0.73 ms; see `dev/PerfProbe.cs`). Terrain3D stays in the repo as a dev-only comparison (`dev/terrain3d/`, excluded from Ship builds) for when a much larger world might need its clipmap.

- **Ground mesh** (`GroundRenderer`): 64×64-quad chunks, one vertex per height sample, no LOD. Collision is one `HeightMapShape3D`.
- **Ground painting** (`GroundMaps`, plain C#): per sample a base and an overlay texture plus a blend amount. Band edges blend the lower band into the upper; steep ground overlays the palette's cliff texture. The erosion ridge map (palette `erosion`) pushes crests toward the cliff texture, lays a scree texture down the creases and lightens crests and darkens creases, which is where most of the eroded look comes from. A texture never blends with itself (Terrain3D rendered that black).
- **Ground shader** (`ground.gdshader`): up to eight textures splatted by per-vertex weights (two RGBA8 attributes), blended by their height maps; only textures present at a pixel are sampled, usually one or two. Steep ground samples each texture from all three axes (triplanar), so cliffs keep their detail instead of stretching; flat ground stays a single top-down sample. A third attribute carries the colour grass paints under itself; it fades in with distance (a little up close, most of the way far out), so where blades thin out a meadow still reads as one. A custom `light()` applies the baked decor shadow to the sun only.
- **Tint:** per vertex, the band tint (so one grass texture serves "grass1" and "grass2") times the biome tint, times a large-scale brightness drift.
- **Ground textures:** `data/ground/textures.json`, packed as albedo + height and normal + roughness. Files load through the importer so exports work; entries with `procedural` colours are noise stand-ins until real textures land (sand and seabed). Biome patches and scree are ambientCG textures (Ground103, ScatteredLeaves009, Ground109), packed with `dev/pack_texture.tscn`.
- **Playable grid:** one decal per zone, drawn over the zone's own buildable cells only, its lines fading out over the last three cells toward the edge (no border line), projected onto render layer 1 only. Foliage is on layer 2, so it never gets grid lines.
- POM can be an option later, but at a board-game camera distance and under dense grass it would barely show.

## Foliage

- **Sets** (`data/foliage/sets/*.json`, listed by the level): species with rules for band, height range, maximum slope, playable cells (avoid, with a margin, or allow, as grass does), cluster noise for groves and flower beds, density and spacing, scale, tilt and tint jitter. Regional sets are biomes (see "Climate, regions and biomes").
- **Placement** (`FoliageStage`, plain C#): a jittered grid per species, each species seeded from the world seed and its own name. Species placed earlier claim footprints that later ones avoid: rocks, then trees, then bushes, then ground cover.
- **Meshes** (`data/foliage/meshes.json`): mostly Quaternius Stylized Nature MegaKit models (free version, CC0, `assets/nature/`). A mesh with `wind` gets `foliage.gdshader`: the imported texture, colour, cut-out and normal map, plus sway with height, leaf flutter, soft canopy normals, optional `recolor` (palette colours by height, used for the pack grass, the bushes and the twisted trees' leaves) and optional `fade` (distance thinning, below). Species pick from several `meshes` per instance. `procedural:` grass and flowers are generated blade clumps with `grass.gdshader`: root-to-tip gradient, rolling wind gusts, sky-bent normals lit the same on both sides, light through the blades from behind.
- **Two grass layers.** Tall grass (`grass_tall`, `grass_dry`) and flower beds are what the board camera sees: they hold out to about 200 m. Short grass (`grass_short`) is close-up detail: it thins out by 30 m and grows back in as the camera comes down, so detail arrives as you get closer instead of leaving.
- **Distance** (`foliage_common.gdshaderinc`, shared by both shaders): instances thin out rather than shrink, each at its own random cut-off, so the edge is a soft circle around the camera. Between `thin[0]` and `thin[1]` the kept fraction falls to `thinMin`; the survivors grow (`compensate`) so the ground stays as covered, and the last ones fade just before `range`. Distance is measured shorter when looking down (`top_down`, 0.6): from above the camera sees between blades, at a grazing angle they overlap. Within a clump, blades drop out between `bladeThin[0]` and `[1]` down to `bladeMin` and the survivors widen; past that the instancer swaps in a light mesh holding exactly those blades, so the switch never shows.
- **Ground cover colour:** species with a `cover` colour paint it onto the ground under them (`FoliageResult.CoverColor` / `CoverAmount`, softened over a few samples). The ground shader fades it in with distance, so a meadow far away is still a meadow after most of its blades are gone.
- **Board:** `playableDensity` and `playableScale` keep only a few small tufts on playable cells. A per-sample distance field to the nearest playable cell plus a slow wobble drives every edge rule: board values ease back to normal over `playableFade` metres along a wandering, ragged edge, and species that avoid the board (trees, rocks) never come within half their `playableMargin`, then thin in over `playableFade`. A meadow or a wood thins toward the board instead of stopping on a straight line.
- **Rocks:** `ridge` limits a species to part of the erosion ridge map (mountain boulders and scree only in the gullies, where loose rock collects, never perched on crests), `sink` buries instances by a random depth, and `alignToSlope` leans them with the ground. The Rock_Medium models are about 3 m across, so they stay at a fraction of their size on mountains.
- **Wind on trees:** the pack also cuts out its bark, so wood is told from leaves by material name first. `stiffness` (0.9 for trees, with gentler `wind`) keeps trunks nearly still, lets leaves sway a little more than the wood they hang on, and slows the sway to about half the speed of grass (heavy things move slowly); leaf flutter is a slow 2–3 Hz drift, not a buzz. Back faces of leaf cards keep the outward canopy normal (Godot flips it on double-sided surfaces, which turned the far side of canopies black); bushes and ground cover keep their full sway.
- **Rendering** (`FoliageInstancer`): per mesh, per 32 m chunk (64 m for long-range meshes other than grass), one `MultiMeshInstance3D` per LOD with Godot visibility ranges. Chunks are frustum-culled by Godot; only rocks, dead trees and buildings cast realtime shadows, so the shadow pass sees no foliage cards.
- **Assets:** Quaternius Stylized Nature MegaKit; textures from Poly Haven, 3dtextures.me and freestylized (check its licence). The target look is painterly, anime, a bit Warcraft.

## Lighting and shadows

**The problem.** Tree shadows flickered with the camera still: hundreds of alpha-cut leaf cards, each a shadow texel or two, fluttering in the wind, so texels flipped on and off every frame. Smaller shadow texels made the sparkle finer, not stable. Leaf cards in the shadow pass were also the most expensive part of it (about 0.5 ms with 4 cascades).

**Lighting presets per level** (`data/lighting/*.json`, built). Sun direction and colour, angular size (soft realtime shadows), the sky (a procedural gradient, or a panorama turned so its painted sun lines up with the light), ambient energy, fog, and the bake settings live in data, named by the level like a palette. Sun angle plus palette is the main mood and art-direction tool: a few angles and colours make a new-feeling level (`default` is late morning, `golden` a low warm afternoon sun with long shadows). The terrain node points its `Sun` and sets its `Environment` from the preset; `SetLighting(id)` switches and re-bakes. The sun is fixed while a level is played.

**Split by what can change during play:**

- **Fixed decor (trees, bushes): baked** (built). `ShadowBake`, plain C#, after foliage placement. Each mesh with `bakedShadow` gets proxies: a few canopy lobes (`lobes`, k-means clusters of its leaf vertices, each a squashed sphere sized to its leaves' spread; or one `canopy` given in data) and a thin one for the trunk. For each proxy, every texel its shadow can reach is tested with a ray from the ground toward the sun; light falls off with the length of canopy crossed (`shadowDensity`, so leaves let some through), broken up by a slow 3D noise for dapples of light (`dapple`), then blurred: a texel for shadows cast close to the ground, more the higher the canopy (`softness`, a penumbra), via a summed-area table. The map is 0.5 m per texel, in its own R8 texture behind global shader uniforms (`terrain_shadow.gdshaderinc`). Ground, grass and ground cover apply it to the sun only, in their `light()` functions; trees themselves get a little canopy darkening instead of self-shadowing. Zero per-frame cost, zero flicker. About 80 ms for a board-sized level at 45°, about 0.5 s for a sun 15° up (longer shadow boxes per lobe).
- **Anything solid: realtime.** Buildings, rocks, dead trees and the ground itself (hills shadow what is behind them). Solid, opaque, not swaying, so they don't flicker. No leaf cards are in the shadow pass any more.
- **A sun that moves during play** (time of day, a sunrise level): not built. Canopy proxy meshes would cast realtime shadows instead of the bake. Same soft look, one switch per level.

**Gameplay foliage and cleanup.** Cutting a tree must remove its shadow, and planting or building must add one.

- The baked shadow is its **own texture**, not vertex colours, so a change uploads one small texture instead of rebuilding mesh chunks.
- Each tree's shadow covers a known box (its proxy projected along the sun, about 10–20 m). `Terrain.RebakeShadows(area)` re-bakes just that rectangle from the instances still in `Foliage` (built): remove or add the instance, then re-bake its box. Terrain edits can re-bake their area the same way.
- **Stable foliage IDs** (not built), derived from species and scatter grid slot, so they are identical every time the level regenerates. Gameplay uses them to pick, remove and hide instances; saves store changes ("tree 312 removed") on top of the seed.
- A per-species **`interactive`** flag (not built: harvestable trees, wood, later ore) gets IDs, pick shapes and removal; plain decor skips all of it.

## Performance budget

The goal is a terrain-plus-foliage cost well under 1 ms of GPU at 1080p, with water still to come. Lessons so far:

- A simple scene sank about 2 ms through work nobody could see: four shadow cascades of alpha-cut leaf cards, a 4096 shadow map, SSAO, per-chunk LOD swaps, foliage on every cell. Measure first (`dev/PerfProbe.cs` prints GPU time per mode), and judge by GPU time, not fps: at over 1,000 fps the engine loop and present take a large share of the frame.
- Current: ground about 0.30 ms GPU; ground plus foliage 0.62 ms at the board camera and 0.94 ms looking over meadow and forest; full demo frame about 1.2 ms at the board camera (SSAO about 0.5 ms of that). Trees are most of the foliage cost (about 0.4 ms of leaf-card overdraw, see `--skip` on the probe); grass, with tens of thousands of clumps, is cheap.
- **Tripwire:** if adding the ocean drops the full scene to around 200–300 fps, the foliage system gets rebuilt rather than tuned. Likely direction: a camera-following GPU grass field driven by a density map (grass cost no longer scales with map size or instance lists), baked decor shadows as above, opaque or alpha-to-coverage leaves, and far fewer instances overall.
