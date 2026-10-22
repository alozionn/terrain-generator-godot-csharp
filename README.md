# Terrain blueprint

Level terrain for Mushrook: JSON level → seeded generator (plain C#) → our own chunked ground mesh and foliage instancing. See [DESIGN.md](DESIGN.md) for the design and the reasoning behind it.

## Where things are

- `terrain/core/` – no Godot types: level records + JSON, generator stages (climate, regions and erosion included), biome choice, ground maps, foliage placement, the decor shadow bake, review images. Tested by `tests/`.
- `terrain/Terrain.cs` – the `Terrain` node: `Apply(level)`, `LoadLevel(path)`, `Reroll(seed)`, `SetPalette(id)`, `SetLighting(id)`, `RebakeShadows(area)`, `CellAt`, `CellCentre`, `ShowGrid`. Point its `Sun` and `Environment` at the scene's light and environment and the level's lighting preset drives them.
- `terrain/surface/` – ground renderer and shader, ground textures, foliage instancer, foliage meshes and shaders (`grass.gdshader` for generated blades, `foliage.gdshader` for pack models, both on `foliage_common.gdshaderinc`; `terrain_shadow.gdshaderinc` is the baked shadow lookup), zone grid decals, placed buildings.
- `data/` – levels, layouts, palettes, lighting presets, ground texture set, foliage meshes and sets (biomes). Everything a level is made of.
- `assets/` – ground textures, Mushrook's shelter, the nature pack.
- `demo/` – orbit camera demo: R reroll, P palette, L lighting, G grid, RMB orbit, MMB pan, wheel zoom, F3 cycles the FPS readout.
- `dev/` – headless stage capture, renderer perf probe, Terrain3D comparison bridge. Not in Ship builds.

## Running

```bash
dotnet run --project tests/Terrain.Tests
```

```bash
godot --headless --path . res://dev/stage_capture.tscn -- --out C:/temp/terrain --seed 77
```

Writes height, bands, zones, temperature, moisture and biome images, and prints how many regions each biome got.

```bash
godot --path . -- --shot C:/temp/shot.png --view 94,98,30,38,30
```

Demo flags: `--shot out.png` (settles 3 s, averages frame time over 3 s, saves, quits), `--view x,z,yaw,pitch,distance`, `--level path`, `--lighting id`, `--nogrid`, `--nossao`, `--noshadow`, `--nofog`, `--nodebugmenu`.

```bash
godot --path . res://dev/perf_probe.tscn -- --mode ground_foliage --view 94,98,30,38,30 --splits 2 --shadowdist 250
```

Probe modes: `none`, `flat`, `ground`, `ground_foliage`, `t3d`, `t3d_lite`, `t3d_foliage`. GPU time only; no SSAO, decals or UI. `ground_foliage` prints instance counts per mesh; `--skip mesh,mesh` leaves meshes out to see what each costs.

## Where it stands (2026-10-22)

- Generator: island shape, stamps, detail, erosion (runevision-style filter: gullies, crests and a ridge map), playable zones (rounded corners, wandering blend edges) with height windows, bands, climate (temperature and moisture), Voronoi regions, a biome per region, rule checks (zone windows, touches-sea, zones and placements on the map).
- Biomes: regional foliage sets (meadow, forest, pinewood, heath) chosen per region by climate, interleaving across borders; each tints its ground and lays patches of its own texture (dirt, leaf litter, dry grass). A global set (rocks, stones) grows everywhere.
- Ground: own renderer, up to 8 height-blended textures per palette, cliffs by slope and erosion crests (projected from all three axes on steep ground), scree in erosion creases, tints, macro variation, grass cover colour that fades in with distance. About 0.30 ms GPU at 1080p over an empty sky (Terrain3D was 0.53–0.73 ms).
- Foliage: generated blade grass in two layers (tall grass that holds to about 200 m, short grass that comes in as the camera gets close), flower beds, pack models with wind. Thinning keeps a share of instances far out and grows the survivors so coverage holds; thinning is measured shorter when looking down. Grass eases out toward the board instead of stopping on its edge.
- Lighting: presets per level (`default` procedural sky, `golden` and `pastel` CC0 sky panoramas turned to match the sun) on an ACES environment with sky reflections and aerial fog. Trees and bushes cast a shadow baked into the ground from a few canopy lobes each, dappled, softer the higher the canopy (about 80 ms per bake, 0.5 s for a low sun), instead of flickering realtime leaf shadows; grass and ground cover sit in it. Tree trunks hold still in the wind; leaves sway.
- Board: zone grid decals that follow the playable cells, pre-placed buildings fitted to their footprint. Trees, rocks and grass ease out toward the board along a wandering edge. Mountain rocks keep to the gullies, half buried and leaning with the slope.
- GPU, ground plus foliage: 0.64–0.69 ms at the board and mountain cameras (was 0.75–0.82 ms before baked shadows), 0.94 ms looking over meadow and forest. Trees are most of it (about 0.4 ms of leaf cards); grass is cheap. Full demo frame about 1.2 ms at the board camera.

## Next steps

Suggested order; each one stands on its own.

1. **Tree cost.** Leaf-card overdraw is the biggest foliage cost now. Try LODs or impostors for distant trees, fewer and larger leaf cards, or opaque canopy shells far out.
2. **Frame cost toward 1 ms** (see DESIGN.md, "Performance budget"). Decide SSAO (about 0.5 ms on its own), try Vulkan vs D3D12 and the Mobile renderer, profile per pass (Visual Profiler, RenderDoc).
3. **Grass on the board.** From the board camera playable cells are nearly bare (tall grass is cut to 6%, short grass is gone by 30 m). Likely a low lawn layer for playable cells that lasts to board distance. Also tune biome colours and densities by eye.
4. **Foliage IDs and interactive trees** (DESIGN.md, "Gameplay foliage and cleanup"): stable IDs from species and grid slot, an `interactive` flag, pick shapes, saves storing changes. `Terrain.RebakeShadows` already re-bakes the area around a cut or planted tree.
5. **Layout pass.** Move the mountain that blocks the board view, raise the island base so decor isn't all grass, add more stamps and a second layout. Consider a small painted image as a layout input.
6. **Paths and a "nothing grows here" mask.** Painted onto the ground (dirt texture) and into foliage rules; the pack's RockPath models can line them.
7. **Ocean.** Watch the frame: if it drops to around 200–300 fps, rebuild foliage (DESIGN.md, "Performance budget"). Bring the ocean blueprint in at y = 0 instead of the placeholder plane; optionally hand it a water-depth texture so waves calm and foam rises in the shallows.
8. **Gameplay hook-up.** Connect Mushrook's grid and occupancy to `WorldData` / `CellAt`, building rules by band name, zones connecting (ramps, paths), packed vs scattered building spots once gameplay says which.
9. **Erosion detail.** Run the erosion filter again at 25 cm into a normal texture for the ground shader, so the finest gullies show without more vertices; bake the low-sun shadows faster.
10. **Bigger maps.** 500 m levels: check generation and bake time, ground chunk LOD if needed, and re-run the perf probe (Terrain3D comparison included).

## Terrain3D (dev only)

Kept for comparisons, not used by the game, left out of Ship builds. Pinned to **v1.1.0-rc1** (MIT) for its generated C# bindings. Only the Windows x64 libraries are committed; for other platforms, copy `addons/terrain_3d/bin/` from the release zip. The release is missing the `Terrain3DAssetResource` wrapper; `dev/terrain3d/Terrain3DAssetResource.cs` supplies it. Delete that file when upgrading if the build reports a duplicate type.

## Third-party assets

- `assets/nature/`: Quaternius Stylized Nature MegaKit, free Standard version, CC0 (`assets/nature/LICENSE.txt`). 36 of its 68 models are used so far.
- `assets/textures/ground/`: ambientCG Ground037, Rock023, Ground103, ScatteredLeaves009 and Ground109, CC0 (packed with `dev/pack_texture.tscn`).
- `assets/skies/`: Poly Haven "evening road 01" (CC0) and Screaming Brain Studios "Cloudy Skyboxes" 07 and 03 (CC0), copied from the ocean blueprint; see `assets/skies/README.md`.
- `assets/buildings/`: Mushrook's shelter model.
- `addons/debug_menu/`: Godot Debug Menu by Hugo Locurcio and contributors, MIT.
- `addons/terrain_3d/`: Terrain3D, MIT (dev only).

## Export notes

JSON under `data/` must be in the export filter (`*.json`).
