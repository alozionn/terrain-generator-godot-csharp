using System;
using Godot;
using OceanNode = Ocean.Ocean;
using WaterSample = Ocean.WaterSample;

namespace Terrain.Demo;

/// <summary>
/// Rides its parent (a moored hull) on the water: samples the ocean under bow, stern and both sides, fits heave, pitch
/// and roll, and follows each with its own damped spring. Kinematic, no rigid body; the parent keeps its place and
/// heading. Sampling across the whole hull means a long boat ignores chop shorter than itself and rides the swell.
/// </summary>
public partial class Floater : Node
{
    public OceanNode Water { get; set; }
    /// <summary>Hull axis (world xz, unit) and size in metres.</summary>
    public Vector3 Axis { get; set; } = Vector3.Right;
    public float Length { get; set; } = 8f;
    public float Beam { get; set; } = 3f;

    // a heavy hull: heave follows firmly, pitch a little softer, roll swings slow and lightly damped
    const float HeaveFrequency = 2.2f, PitchFrequency = 1.6f, RollFrequency = 0.9f; // rad/s
    const float HeaveDamping = 1f, PitchDamping = 0.8f, RollDamping = 0.35f;         // 1 = critical

    Node3D _hull;
    Transform3D _rest;
    Vector3 _pose, _rate; // heave (m), pitch, roll (rad) and how fast they change
    readonly Vector3[] _points = new Vector3[6];
    readonly WaterSample[] _water = new WaterSample[6];

    public override void _Ready()
    {
        _hull = GetParent<Node3D>();
        _rest = _hull.Transform;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Water == null || _hull == null) return;
        float dt = (float)delta;
        var target = WaterPose();
        Spring(ref _pose.X, ref _rate.X, target.X, HeaveFrequency, HeaveDamping, dt);
        Spring(ref _pose.Y, ref _rate.Y, target.Y, PitchFrequency, PitchDamping, dt);
        Spring(ref _pose.Z, ref _rate.Z, target.Z, RollFrequency, RollDamping, dt);

        var side = Vector3.Up.Cross(Axis).Normalized();
        var tilt = new Basis(side, _pose.Y) * new Basis(Axis, _pose.Z);
        var origin = _hull.GetParent<Node3D>().GlobalTransform;
        var rest = origin * _rest;
        _hull.GlobalTransform = new Transform3D(tilt * rest.Basis, rest.Origin + Vector3.Up * _pose.X);
    }

    /// <summary>Heave, pitch and roll of the water under the hull: mean height, bow-to-stern and side-to-side slopes.</summary>
    Vector3 WaterPose()
    {
        var rest = _hull.GetParent<Node3D>().GlobalTransform * _rest;
        var c = new Vector3(rest.Origin.X, 0, rest.Origin.Z);
        var side = Vector3.Up.Cross(Axis).Normalized();
        float l = Length * 0.45f, b = Beam * 0.45f;
        _points[0] = c + Axis * l; _points[1] = c - Axis * l;                 // bow, stern
        _points[2] = c + side * b + Axis * l * 0.4f; _points[3] = c - side * b + Axis * l * 0.4f;
        _points[4] = c + side * b - Axis * l * 0.4f; _points[5] = c - side * b - Axis * l * 0.4f;
        Water.SampleWater(_points, _water);

        float sea = Water.GlobalPosition.Y, mean = 0;
        foreach (var w in _water) mean += w.Height - sea;
        mean /= _water.Length;
        float bowRise = (_water[0].Height - _water[1].Height) / (2 * l);
        float sideRise = ((_water[2].Height + _water[4].Height) - (_water[3].Height + _water[5].Height)) / (4 * b);
        // pitch turns about the side axis (bow up = negative), roll about the hull axis
        return new Vector3(mean, -MathF.Atan(bowRise), MathF.Atan(sideRise));
    }

    static void Spring(ref float x, ref float v, float target, float w, float zeta, float dt)
    {
        v += (w * w * (target - x) - 2f * zeta * w * v) * dt;
        x += v * dt;
    }
}
