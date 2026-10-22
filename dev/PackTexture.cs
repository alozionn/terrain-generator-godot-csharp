using System;
using Godot;

namespace Terrain.Dev;

/// <summary>
/// Packs a PBR texture set (ambientCG layout) into the two images the ground shader reads: RGB albedo + height in
/// alpha, and RGB normal (OpenGL) + roughness in alpha. Then quits.
/// godot --headless --path . res://dev/pack_texture.tscn -- --color c.png --height d.png --normal n.png --roughness r.png --out res://assets/textures/ground/name
/// </summary>
public partial class PackTexture : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }

        Image Load(string path)
        {
            var img = Image.LoadFromFile(path);
            if (img == null) GD.PushError($"[Pack] cannot read {path}");
            else img.Convert(Image.Format.Rgba8);
            return img;
        }
        var color = Load(Arg("--color"));
        var height = Load(Arg("--height"));
        var normal = Load(Arg("--normal"));
        var rough = Load(Arg("--roughness"));
        string output = Arg("--out");
        if (color == null || height == null || normal == null || rough == null || output == "")
        {
            GetTree().Quit(1);
            return;
        }

        int w = color.GetWidth(), h = color.GetHeight();
        foreach (var img in new[] { height, normal, rough })
            if (img.GetWidth() != w || img.GetHeight() != h) img.Resize(w, h, Image.Interpolation.Bilinear);
        var albedoHeight = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var normalRough = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = color.GetPixel(x, y);
                albedoHeight.SetPixel(x, y, new Color(c.R, c.G, c.B, height.GetPixel(x, y).R));
                var n = normal.GetPixel(x, y);
                normalRough.SetPixel(x, y, new Color(n.R, n.G, n.B, rough.GetPixel(x, y).R));
            }
        string dir = ProjectSettings.GlobalizePath(output);
        albedoHeight.SavePng(dir + "_alb_ht.png");
        normalRough.SavePng(dir + "_nrm_rgh.png");
        GD.Print($"[Pack] {output}: {w}x{h}");
        GetTree().Quit();
    }
}
