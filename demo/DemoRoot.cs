using System;
using Godot;

namespace Terrain.Demo;

/// <summary>
/// Demo: orbit camera around the terrain plus a few keys. R reroll seed, P cycle palette, G toggle grid.
/// `-- --shot path.png` saves one frame and quits (for reviewing changes without opening the editor).
/// </summary>
public partial class DemoRoot : Node3D
{
    static readonly string[] Palettes = { "meadow", "dusk_bloom" };

    Terrain _terrain;
    Camera3D _camera;
    Label _info;
    Vector3 _focus;
    float _yaw = 35f, _pitch = 48f, _distance = 150f;
    bool _dragging;
    int _palette;
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
    }

    void OnGenerated()
    {
        if (_terrain.World == null) return;
        _focus = new Vector3(_terrain.World.Size * 0.5f, 0, _terrain.World.Size * 0.5f);
        _distance = _terrain.World.Size * 0.75f;
        _palette = Math.Max(0, Array.IndexOf(Palettes, _terrain.Level.Palette));
        UpdateCamera();
        string issues = _terrain.Issues.Count == 0 ? "no issues" : string.Join("\n", _terrain.Issues);
        _info.Text = $"{_terrain.Level.Id}  seed {_terrain.Level.Seed}  palette {_terrain.Level.Palette}\n{issues}\n" +
                     "R reroll · P palette · G grid · RMB orbit · MMB pan · wheel zoom";
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
