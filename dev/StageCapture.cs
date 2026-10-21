using System;
using System.Linq;
using Godot;
using Terrain.Core.Debug;
using Terrain.Core.Generation;
using Terrain.Core.Level;

namespace Terrain.Dev;

/// <summary>
/// Headless review: generates a level and writes height, bands and zones images, then quits.
/// godot --headless --path . res://dev/stage_capture.tscn -- --level res://data/levels/x.json --out C:/somewhere [--seed 5] [--palette id]
/// </summary>
public partial class StageCapture : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

        string levelPath = Arg("--level", "res://data/levels/harbour_village.json");
        string outDir = Arg("--out", "user://captures");
        string root = Arg("--data", "res://data");
        var level = LevelJson.Deserialize<LevelDefinition>(FileAccess.GetFileAsString(levelPath));
        if (args.Contains("--seed")) level.Seed = uint.Parse(Arg("--seed", "1"));
        if (args.Contains("--palette")) level.Palette = Arg("--palette", level.Palette);
        var layout = LevelJson.Deserialize<LayoutDefinition>(FileAccess.GetFileAsString($"{root}/layouts/{level.Layout}.json"));
        var palette = LevelJson.Deserialize<PaletteDefinition>(FileAccess.GetFileAsString($"{root}/palettes/{level.Palette}.json"));

        var result = LevelGenerator.Generate(level, layout, palette);
        var w = result.World;
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        int scale = StageImages.DefaultScale(w);
        string name = $"{level.Id}_s{level.Seed}";
        Save(StageImages.Height(w, scale), $"{outDir}/{name}_height.png");
        Save(StageImages.Bands(w, palette, scale), $"{outDir}/{name}_bands_{level.Palette}.png");
        Save(StageImages.Zones(w, palette, scale), $"{outDir}/{name}_zones.png");

        GD.Print($"[StageCapture] {name}: {w.Cells}x{w.Cells} cells, height {w.Height.Min():F2}..{w.Height.Max():F2} m");
        for (int zi = 0; zi < level.Zones.Count; zi++)
        {
            var z = level.Zones[zi];
            int cx = Math.Clamp(z.Origin[0] + z.Cells[0] / 2, 0, w.Cells - 1), cz = Math.Clamp(z.Origin[1] + z.Cells[1] / 2, 0, w.Cells - 1);
            GD.Print($"  zone {z.Id}: centre cell ({cx},{cz}) h {w.CellHeight(cx, cz):F2} band {w.BandNames[w.CellBand(cx, cz)]}");
        }
        GD.Print(result.Issues.Count == 0 ? "  no issues" : "  ISSUES:\n    " + string.Join("\n    ", result.Issues));
        GetTree().Quit();
    }

    static void Save(StageImages.Rgb img, string path)
    {
        var image = Image.CreateFromData(img.Width, img.Height, false, Image.Format.Rgb8, img.Pixels);
        var err = image.SavePng(path);
        if (err != Error.Ok) GD.PushError($"[StageCapture] {path}: {err}");
    }
}
