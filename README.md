# Terrain blueprint

Level terrain for Mushrook: JSON level → seeded generator (plain C#) → our own chunked ground mesh and foliage instancing. See [DESIGN.md](DESIGN.md) for the design and the reasoning behind it.

## Where things are

- `terrain/core/` – no Godot types: level records + JSON, generator stages, ground maps, foliage placement, review images. Tested by `tests/`.
- `terrain/Terrain.cs` – the `Terrain` node: `Apply(level)`, `LoadLevel(path)`, `Reroll(seed)`, `SetPalette(id)`, `CellAt`, `CellCentre`, `ShowGrid`.
- `terrain/surface/` – ground renderer and shader, ground textures, foliage instancer, foliage meshes and shaders (`grass.gdshader` for generated blades, `foliage.gdshader` for pack models), zone grid decals, placed buildings.
- `data/` – levels, layouts, palettes, ground texture set, foliage meshes and sets. Everything a level is made of.
- `assets/` – ground textures, Mushrook's shelter, the nature pack.
- `demo/` – orbit camera demo: R reroll, P palette, G grid, RMB orbit, MMB pan, wheel zoom, F3 cycles the FPS readout.
- `dev/` – headless stage capture, renderer perf probe, Terrain3D comparison bridge. Not in Ship builds.

## Running

```bash
dotnet run --project tests/Terrain.Tests
```

```bash
godot --headless --path . res://dev/stage_capture.tscn -- --out C:/temp/terrain --seed 77
```

```bash
godot --path . -- --shot C:/temp/shot.png --view 94,98,30,38,30
```

Demo flags: `--shot out.png` (settles 3 s, averages frame time over 3 s, saves, quits), `--view x,z,yaw,pitch,distance`, `--level path`, `--nogrid`, `--nossao`, `--noshadow`, `--nofog`, `--nodebugmenu`.

```bash
godot --path . res://dev/perf_probe.tscn -- --mode ground_foliage --view 94,98,30,38,30 --splits 2 --shadowdist 250
```

Probe modes: `none`, `flat`, `ground`, `ground_foliage`, `t3d`, `t3d_lite`, `t3d_foliage`. GPU time only; no SSAO, decals or UI.

## Where it stands (2026-10-22)

- Generator: island shape, stamps, detail, playable zones with height windows and blends, bands, rule checks (zone windows, touches-sea, zones and placements on the map).
- Ground: own renderer, up to 8 height-blended textures per palette, cliffs by slope, tints, macro variation. About 0.25–0.28 ms GPU over an empty sky at 1080p (Terrain3D was 0.53–0.73 ms).
- Foliage: rule-based seeded scatter in sets (rocks, trees, bushes, plants, grass, flowers, clover, pebbles), mesh variants, MegaKit models with wind, grass thinning in a circle around the camera.
- Board: zone grid decals, pre-placed buildings fitted to their footprint.
- Frame: 1.18–1.46 ms for the full demo at 1080p (SSAO about 0.5 ms; sun 2 cascades over 250 m, 2048 shadow map; bushes and ground cover cast no shadows).

## Next steps

Suggested order; each one stands on its own.

1. **Foliage look at board distance.** Pack grass reads as sparse specks from the 30 m camera. Try the generated blade grass for dense meadows plus pack tufts for detail (data only, `data/foliage/`), and tune scales and densities.
2. **Frame cost toward 1 ms.** Decide SSAO (about 0.5 ms on its own), try Vulkan vs D3D12 and the Mobile renderer, profile per pass (Visual Profiler, RenderDoc), smaller leaf textures, blob shadows for tree canopies.
3. **Temperature map + Voronoi regions** that choose which foliage set grows where (forest, meadow, pond edge), instead of one set for the whole island.
4. **Layout pass.** Move the mountain that blocks the board view, raise the island base so decor isn't all grass, add more stamps and a second layout. Consider a small painted image as a layout input.
5. **Paths and a "nothing grows here" mask.** Painted onto the ground (dirt texture) and into foliage rules; the pack's RockPath models can line them.
6. **Erosion stage** (runevision's mask-based filter) after Detail.
7. **Ocean.** Bring the ocean blueprint in at y = 0 instead of the placeholder plane; optionally hand it a water-depth texture so waves calm and foam rises in the shallows.
8. **Gameplay hook-up.** Connect Mushrook's grid and occupancy to `WorldData` / `CellAt`, building rules by band name, zones connecting (ramps, paths), packed vs scattered building spots once gameplay says which.
9. **Bigger maps.** 500 m levels: check generation time, ground chunk LOD if needed, and re-run the perf probe (Terrain3D comparison included).

## Terrain3D (dev only)

Kept for comparisons, not used by the game, left out of Ship builds. Pinned to **v1.1.0-rc1** (MIT) for its generated C# bindings. Only the Windows x64 libraries are committed; for other platforms, copy `addons/terrain_3d/bin/` from the release zip. The release is missing the `Terrain3DAssetResource` wrapper; `dev/terrain3d/Terrain3DAssetResource.cs` supplies it. Delete that file when upgrading if the build reports a duplicate type.

## Third-party assets

- `assets/nature/`: Quaternius Stylized Nature MegaKit, free Standard version, CC0 (`assets/nature/LICENSE.txt`). 28 of its 68 models are used so far.
- `assets/textures/ground/`: ambientCG Ground037 and Rock023, CC0.
- `assets/buildings/`: Mushrook's shelter model.
- `addons/debug_menu/`: Godot Debug Menu by Hugo Locurcio and contributors, MIT.
- `addons/terrain_3d/`: Terrain3D, MIT (dev only).

## Export notes

JSON under `data/` must be in the export filter (`*.json`).
