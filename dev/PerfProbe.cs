using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Generation;
using Terrain.Core.Ground;
using Terrain.Core.Level;
using Terrain.Core.Shadows;
using Terrain.Core.World;
using Terrain.Surface;
using TokisanGames;

namespace Terrain.Dev;

/// <summary>
/// Isolated ground-rendering cost: same level, same camera, sun with shadows, procedural sky, no decals, SSAO, fog or
/// UI. Measures GPU time with the viewport's render timer (3 s settle, then 3 s average), prints it, quits.
/// godot --path . res://dev/perf_probe.tscn -- --mode MODE [--view x,z,yaw,pitch,dist] [--splits 1|2|4] [--shadowdist m] [--shadowsize px] [--skip mesh,mesh]
/// Modes: none, flat (vertex colours only), ground (our renderer), ground_foliage, t3d, t3d_lite, t3d_foliage.
/// </summary>
public partial class PerfProbe : Node3D
{
    string _mode = "ground";
    int _shadowSplits = 4;
    float _shadowDistance = 600;
    double _clock, _gpu, _cpu, _frames;
    int _samples;
    Rid _viewport;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string Arg(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        _mode = Arg("--mode", "ground");
        _shadowSplits = int.Parse(Arg("--splits", "4"));
        _shadowDistance = float.Parse(Arg("--shadowdist", "600"), System.Globalization.CultureInfo.InvariantCulture);
        var view = Array.ConvertAll(Arg("--view", "94,98,30,38,30").Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

        Environment();
        T Read<T>(string p) where T : new() => LevelJson.Deserialize<T>(FileAccess.GetFileAsString(p));
        var level = Read<LevelDefinition>("res://data/levels/harbour_village.json");
        var layout = Read<LayoutDefinition>($"res://data/layouts/{level.Layout}.json");
        var palette = Read<PaletteDefinition>($"res://data/palettes/{level.Palette}.json");
        var sets = level.Foliage.Select(id => Read<FoliageSetDefinition>($"res://data/foliage/sets/{id}.json")).ToList();
        var world = LevelGenerator.Generate(level, layout, palette, sets).World;
        var textures = GroundTextures.Load(Read<TextureSetDefinition>("res://data/ground/textures.json"));
        var ground = GroundMaps.Build(world, palette, textures.Ids, level.Seed, sets);
        var meshes = Read<FoliageMeshSetDefinition>("res://data/foliage/meshes.json");
        FoliageResult Foliage() => FoliageStage.Run(world, sets, level.Seed,
            world.CellMask(level.Placements.Select(p => (p.Cell[0], p.Cell[1], p.Cells[0], p.Cells[1]))));

        switch (_mode)
        {
            case "none":
                break;
            case "flat":
                AddChild(new MeshInstance3D { Mesh = FlatMesh(world, palette) });
                break;
            case "ground":
                new GroundRenderer(this).Build(world, ground, textures);
                break;
            case "ground_foliage":
                var foliage = Foliage();
                // --skip a,b drops those meshes, to see what each one costs
                foreach (var id in Arg("--skip", "").Split(',', StringSplitOptions.RemoveEmptyEntries)) foliage.ByMesh.Remove(id);
                new GroundRenderer(this).Build(world, ground, textures, foliage);
                var instancer = new FoliageInstancer(this);
                instancer.LoadMeshes(meshes);
                instancer.Build(foliage);
                GD.Print("[Probe] instances: " + string.Join(", ", foliage.ByMesh.OrderByDescending(p => p.Value.Count).Select(p => $"{p.Key} {p.Value.Count}")));
                // the decor shadow bake, as the terrain node does it (same sun as the light below)
                var map = ShadowBake.Bake(world, foliage, instancer.Proxies, new System.Numerics.Vector3(0.5f, 0.707107f, 0.5f));
                var bytes = map.Light.Select(l => (byte)Math.Clamp((int)MathF.Round(l * 255f), 0, 255)).ToArray();
                RenderingServer.GlobalShaderParameterSet("terrain_shadow", ImageTexture.CreateFromImage(Image.CreateFromData(map.Size, map.Size, false, Image.Format.R8, bytes)));
                RenderingServer.GlobalShaderParameterSet("terrain_shadow_rect", new Vector4(0, 0, 1f / (map.Size * map.Texel), 1f / (map.Size * map.Texel)));
                RenderingServer.GlobalShaderParameterSet("terrain_shadow_strength", 1f);
                break;
            case "t3d":
            case "t3d_lite":
            case "t3d_foliage":
                var bridge = new Terrain3DBridge(this);
                bridge.Upload(world, ground, textures, _mode == "t3d_foliage" ? meshes : null, _mode == "t3d_foliage" ? Foliage() : null);
                if (_mode == "t3d_lite")
                {
                    var t = bridge.Node;
                    t.MeshLods = 4; // enough rings for a 200 m board
                    t.MeshSize = 32;
                    t.Material.TextureFiltering = Terrain3DMaterial.TextureFilteringEnum.Linear;
                    for (int i = 0; i < t.Assets.GetTextureCount(); i++) t.Assets.GetTextureAsset(i).DetilingRotation = 0;
                }
                break;
            default:
                GD.PushError($"[Probe] unknown mode {_mode}");
                break;
        }

        var cam = new Camera3D { Fov = 50, Far = 2000 };
        AddChild(cam);
        var focus = new Vector3(view[0], world.HeightAt(view[0], view[1]), view[1]);
        var dir = Basis.FromEuler(new Vector3(-Mathf.DegToRad(view[3]), Mathf.DegToRad(view[2]), 0)) * Vector3.Back;
        cam.Position = focus + dir * view[4];
        cam.LookAt(focus);

        int shadowSize = int.Parse(Arg("--shadowsize", "0"));
        if (shadowSize > 0) RenderingServer.DirectionalShadowAtlasSetSize(shadowSize, true);
        _viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock < 3) return;
        _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport);
        _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport);
        _frames += delta;
        _samples++;
        if (_clock < 6) return;
        GD.Print($"[Probe] {_mode,-14} gpu {_gpu / _samples:F3} ms, render cpu {_cpu / _samples:F3} ms, frame {_frames / _samples * 1000:F2} ms, " +
                 $"{Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6:F2} M prims, " +
                 $"{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} draws");
        SetProcess(false);
        GetTree().Quit();
    }

    void Environment()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        AddChild(new DirectionalLight3D
        {
            Transform = new Transform3D(new Basis(new Vector3(0.707107f, 0, -0.707107f), new Vector3(-0.5f, 0.707107f, -0.5f), new Vector3(0.5f, 0.707107f, 0.5f)), Vector3.Up * 50),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = _shadowDistance,
            DirectionalShadowMode = _shadowSplits == 2 ? DirectionalLight3D.ShadowMode.Parallel2Splits : _shadowSplits == 1 ? DirectionalLight3D.ShadowMode.Orthogonal : DirectionalLight3D.ShadowMode.Parallel4Splits,
        });
    }

    /// <summary>The cheapest possible ground for reference: one mesh, band colours per vertex, no textures.</summary>
    static ArrayMesh FlatMesh(WorldData w, PaletteDefinition palette)
    {
        int n = w.Samples;
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        var bandColors = palette.Bands.OrderBy(b => b.From).Select(b => { var c = LevelJson.ParseColor(b.Color); return new Color(c.X, c.Y, c.Z); }).ToArray();
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = w.Index(x, z);
                verts[i] = new Vector3(x * w.SampleSpacing, w.Height[i], z * w.SampleSpacing);
                int xl = Math.Max(0, x - 1), xr = Math.Min(n - 1, x + 1), zu = Math.Max(0, z - 1), zd = Math.Min(n - 1, z + 1);
                normals[i] = new Vector3(-(w.H(xr, z) - w.H(xl, z)) / ((xr - xl) * w.SampleSpacing), 1, -(w.H(x, zd) - w.H(x, zu)) / ((zd - zu) * w.SampleSpacing)).Normalized();
                colors[i] = bandColors[w.Band[i]];
            }
        var indices = new List<int>((n - 1) * (n - 1) * 6);
        for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                indices.AddRange(new[] { a, b, d, a, d, c });
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f });
        return mesh;
    }
}
