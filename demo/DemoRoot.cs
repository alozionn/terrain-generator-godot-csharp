using System;
using Godot;

namespace Terrain.Demo;

/// <summary>
/// Demo: orbit camera around the terrain plus a few keys. R reroll seed, P cycle palette, L cycle lighting, G toggle grid.
/// `-- --shot path.png` saves one frame and quits (for reviewing changes without opening the editor).
/// </summary>
public partial class DemoRoot : Node3D
{
    static readonly string[] Palettes = { "meadow", "meadow_soft", "meadow_flat", "dusk_bloom" };
    static readonly string[] Lightings = { "default", "golden", "pastel" };

    // starting view: orbit angles in degrees, distance as a fraction of the map size
    [Export] public float Yaw { get; set; } = 35f;
    [Export] public float Pitch { get; set; } = 48f;
    [Export] public float Zoom { get; set; } = 0.75f;
    [Export] public bool Grid { get; set; } = true;

    Terrain _terrain;
    Camera3D _camera;
    Label _info;
    Vector3 _focus;
    float _yaw = 35f, _pitch = 48f, _distance = 150f;
    bool _dragging;
    int _palette, _lighting;
    // shot mode: settle for 3 s, then average frame time over the next 3 s, save, quit
    double _shotClock = -1, _shotMeasured;
    int _shotSamples;
    string _shotPath;

    public override void _Ready()
    {
        _terrain = GetNode<Terrain>("Terrain");
        _camera = GetNode<Camera3D>("Camera");
        _info = GetNode<Label>("Ui/Info");
        _terrain.Generated += OnGenerated;
        (_yaw, _pitch) = (Yaw, Pitch);
        _terrain.ShowGrid = Grid;
        GetNodeOrNull("/root/DebugMenu")?.Set("style", 1); // compact FPS readout (F3 cycles)
        OnGenerated();

        var args = OS.GetCmdlineUserArgs();
        int i = Array.IndexOf(args, "--shot");
        if (i >= 0 && i + 1 < args.Length)
        {
            _shotPath = args[i + 1];
            _shotClock = 0;
        }
        // perf probes: --nossao, --noshadow, --nofog, --nodebugmenu
        var env = GetNode<WorldEnvironment>("WorldEnvironment").Environment;
        if (Array.IndexOf(args, "--nossao") >= 0) env.SsaoEnabled = false;
        if (Array.IndexOf(args, "--nofog") >= 0) env.FogEnabled = false;
        if (Array.IndexOf(args, "--noshadow") >= 0) GetNode<DirectionalLight3D>("Sun").ShadowEnabled = false;
        if (Array.IndexOf(args, "--nodebugmenu") >= 0) GetNodeOrNull<CanvasLayer>("/root/DebugMenu")?.Hide();
        // --level path loads another level (e.g. a perf variant)
        int lv = Array.IndexOf(args, "--level");
        if (lv >= 0 && lv + 1 < args.Length) _terrain.LoadLevel(args[lv + 1]);
        // --palette id switches the palette
        int pa = Array.IndexOf(args, "--palette");
        if (pa >= 0 && pa + 1 < args.Length) _terrain.SetPalette(args[pa + 1]);
        // --view x,z,yaw,pitch,distance frames a shot; --nogrid hides the cell grid
        int v = Array.IndexOf(args, "--view");
        if (v >= 0 && v + 1 < args.Length)
        {
            var p = Array.ConvertAll(args[v + 1].Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
            _focus = new Vector3(p[0], _terrain.World?.HeightAt(p[0], p[1]) ?? 0, p[1]);
            (_yaw, _pitch, _distance) = (p[2], p[3], p[4]);
            UpdateCamera();
        }
        if (Array.IndexOf(args, "--nogrid") >= 0) _terrain.ShowGrid = false;
        // --lighting id switches the lighting preset
        int li = Array.IndexOf(args, "--lighting");
        if (li >= 0 && li + 1 < args.Length) _terrain.SetLighting(args[li + 1]);
    }

    void OnGenerated()
    {
        if (_terrain.World == null) return;
        _focus = new Vector3(_terrain.World.Size * 0.5f, 0, _terrain.World.Size * 0.5f);
        _distance = _terrain.World.Size * Zoom;
        _palette = Math.Max(0, Array.IndexOf(Palettes, _terrain.Level.Palette));
        UpdateCamera();
        // the vendored ocean calms its waves, adds surf and tints the shallows from the ground under it
        var ocean = GetNodeOrNull<global::Ocean.Ocean>("Ocean");
        var w = _terrain.World;
        if (ocean != null)
        {
            ocean.SetSeaFloor(global::Ocean.Core.Shore.SeaFloor.FromHeights(w.Height, w.Samples, w.Samples, w.SampleSpacing,
                System.Numerics.Vector2.Zero, seaLevel: ocean.GlobalPosition.Y));
            ocean.ClearBodies();
            foreach (var body in WaterBodies()) ocean.AddBody(body);
            AddFloaters(ocean);
        }
        string issues = _terrain.Issues.Count == 0 ? "no issues" : string.Join("\n", _terrain.Issues);
        _info.Text = $"{_terrain.Level.Id}  seed {_terrain.Level.Seed}  palette {_terrain.Level.Palette}\n{issues}\n" +
                     "R reroll · P palette · L lighting · G grid · RMB orbit · MMB pan · wheel zoom";
    }

    /// <summary>The placements' waterline footprints, moved with their placed models, as the ocean's bodies.</summary>
    System.Collections.Generic.IEnumerable<global::Ocean.Core.Bodies.WaterBody> WaterBodies()
    {
        foreach (var p in _terrain.Level.Placements)
        {
            var node = _terrain.PlacedNode(p.Id);
            if (node == null) continue;
            var xf = node.GlobalTransform;
            float scale = xf.Basis.Scale.X, yaw = node.GlobalRotation.Y;
            foreach (var f in p.Water)
            {
                var c = xf * new Vector3(f.At[0], 0, f.At[1]);
                var xz = new System.Numerics.Vector2(c.X, c.Z);
                float heading = yaw + Mathf.DegToRad(f.Heading);
                var body = f.Shape switch
                {
                    "box" => global::Ocean.Core.Bodies.WaterBody.Box(xz, heading, f.Length * scale, f.Beam * scale, f.Top),
                    "hull" => global::Ocean.Core.Bodies.WaterBody.Hull(xz, heading, f.Length * scale, f.Beam * scale, f.Draft, f.Top),
                    _ => global::Ocean.Core.Bodies.WaterBody.Circle(xz, f.Radius * scale, f.Top),
                };
                if (f.Shape != "hull") body.Skirt = f.Skirt;
                body.Reach = f.Reach;
                yield return body;
            }
        }
    }

    /// <summary>Placements with a hull footprint ride the water (a fresh floater on each newly placed model).</summary>
    void AddFloaters(global::Ocean.Ocean ocean)
    {
        foreach (var p in _terrain.Level.Placements)
        {
            var node = _terrain.PlacedNode(p.Id);
            var hull = p.Water.Find(f => f.Shape == "hull");
            if (node == null || hull == null) continue;
            float scale = node.GlobalTransform.Basis.Scale.X, heading = node.GlobalRotation.Y + Mathf.DegToRad(hull.Heading);
            node.AddChild(new Floater
            {
                Water = ocean, Axis = new Vector3(Mathf.Cos(heading), 0, -Mathf.Sin(heading)),
                Length = hull.Length * scale, Beam = hull.Beam * scale,
            });
        }
    }

    public override void _Process(double delta)
    {
        var pan = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        if (pan != Vector2.Zero)
        {
            var basis = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_yaw), 0));
            _focus += basis * new Vector3(pan.X, 0, pan.Y) * _distance * (float)delta;
            UpdateCamera();
        }

        if (_shotClock >= 0)
        {
            _shotClock += delta;
            if (_shotClock > 3) { _shotMeasured += delta; _shotSamples++; }
            if (_shotClock > 6)
            {
                GetViewport().GetTexture().GetImage().SavePng(_shotPath);
                double ms = _shotMeasured / _shotSamples * 1000;
                GD.Print($"[Demo] {1000 / ms:F0} fps ({ms:F2} ms), {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6:F2} M primitives, {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} draw calls");
                GetTree().Quit();
                _shotClock = -1;
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } k:
                if (k.Keycode == Key.R) _terrain.Reroll((uint)GD.Randi());
                else if (k.Keycode == Key.P) _terrain.SetPalette(Palettes[(_palette + 1) % Palettes.Length]);
                else if (k.Keycode == Key.G) _terrain.ShowGrid = !_terrain.ShowGrid;
                else if (k.Keycode == Key.L) _terrain.SetLighting(Lightings[_lighting = (_lighting + 1) % Lightings.Length]);
                break;
            case InputEventMouseButton mb:
                if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle) _dragging = mb.Pressed;
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(8f, _distance * 0.9f);
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(2000f, _distance * 1.1f);
                UpdateCamera();
                break;
            case InputEventMouseMotion mm when _dragging:
                if (Input.IsMouseButtonPressed(MouseButton.Middle))
                {
                    var basis = Basis.FromEuler(new Vector3(0, Mathf.DegToRad(_yaw), 0));
                    _focus += basis * new Vector3(-mm.Relative.X, 0, -mm.Relative.Y) * _distance * 0.0015f;
                }
                else
                {
                    _yaw -= mm.Relative.X * 0.3f;
                    _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.3f, 10f, 89f);
                }
                UpdateCamera();
                break;
        }
    }

    void UpdateCamera()
    {
        var dir = Basis.FromEuler(new Vector3(-Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0)) * Vector3.Back;
        _camera.Position = _focus + dir * _distance;
        _camera.LookAt(_focus);
        _camera.Far = Mathf.Max(1000f, _distance * 6f);
    }
}
