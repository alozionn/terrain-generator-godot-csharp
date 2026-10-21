# Terrain blueprint

Level terrain for Mushrook: JSON level → seeded generator (plain C#) → our own chunked ground mesh and foliage instancing. See [DESIGN.md](DESIGN.md).

- `terrain/core/` – no Godot types: level records + JSON, generator stages, ground maps, foliage placement, review images.
- `terrain/` – the `Terrain` node (`Apply(level)`, `LoadLevel(path)`, `Reroll(seed)`, `SetPalette(id)`, `CellAt`, `CellCentre`).
- `terrain/surface/` – ground renderer and shader, ground textures, foliage instancing and meshes, zone grid, placements.
- `data/` – levels, layouts, palettes, ground textures, foliage meshes and sets.
- `demo/` – orbit camera demo (R reroll, P palette, G grid). `-- --shot out.png [--view x,z,yaw,pitch,dist] [--nogrid] [--nossao] [--noshadow] [--level path]`.
- `dev/` – headless stage capture, renderer perf probe, Terrain3D comparison bridge.

```bash
dotnet run --project tests/Terrain.Tests
```

```bash
godot --headless --path . res://dev/stage_capture.tscn -- --out C:/temp/terrain --seed 77
```

## Terrain3D (dev only)

Kept for comparisons (`res://dev/perf_probe.tscn -- --mode none|flat|ground|ground_foliage|t3d|t3d_lite|t3d_foliage`), not used by
the game, and left out of Ship builds. Pinned to **v1.1.0-rc1** (MIT) for its generated C# bindings. The release is missing
the `Terrain3DAssetResource` wrapper; `dev/terrain3d/Terrain3DAssetResource.cs` supplies it. Delete that file when upgrading if the build
reports a duplicate type.

## Third-party assets

- `assets/nature/`: Quaternius Stylized Nature MegaKit, free Standard version, CC0 (`assets/nature/LICENSE.txt`).
- `assets/textures/ground/`: ambientCG Ground037 and Rock023, CC0.
- `addons/debug_menu/`: Godot Debug Menu by Hugo Locurcio and contributors, MIT.
- `addons/terrain_3d/`: Terrain3D, MIT (dev only).

## Export notes

JSON under `data/` must be in the export filter (`*.json`).
