# Terrain blueprint

Level terrain for Mushrook: JSON level → seeded generator (plain C#) → Terrain3D renders it. See [DESIGN.md](DESIGN.md).

- `terrain/core/` – no Godot types: level records + JSON, generator stages, ground maps, foliage placement, review images.
- `terrain/` – the `Terrain` node (`Apply(level)`, `LoadLevel(path)`, `Reroll(seed)`, `SetPalette(id)`, `CellAt`, `CellCentre`).
- `terrain/integration/` – the only code that knows Terrain3D.
- `terrain/surface/` – playable-cell grid decals, procedural foliage stand-ins.
- `data/` – levels, layouts, palettes, ground textures, foliage meshes and sets.
- `demo/` – orbit camera demo (R reroll, P palette, G grid). `-- --shot out.png [--view x,z,yaw,pitch,dist] [--nogrid] [--t3d show_colormap]`.
- `dev/` – headless stage capture.

```bash
dotnet run --project tests/Terrain.Tests
```

```bash
godot --headless --path . res://dev/stage_capture.tscn -- --out C:/temp/terrain --seed 77
```

## Terrain3D

Pinned to **v1.1.0-rc1** (MIT) for its generated C# bindings. The release is missing the `Terrain3DAssetResource`
wrapper; `terrain/integration/Terrain3DAssetResource.cs` supplies it. Delete that file when upgrading if the build
reports a duplicate type.

## Export notes

JSON under `data/` must be in the export filter (`*.json`).
