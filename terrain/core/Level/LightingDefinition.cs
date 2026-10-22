#nullable disable
using System;
using System.Numerics;

namespace Terrain.Core.Level;

/// <summary>
/// A lighting preset (data/lighting/*.json): sun, sky, fog and the decor shadow bake. Levels name one, like a palette;
/// sun angle plus palette is the main mood tool. The sun stays put while a level is played.
/// </summary>
public sealed class LightingDefinition
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public SunDefinition Sun { get; set; } = new();
    public SkyDefinition Sky { get; set; } = new();
    public FogDefinition Fog { get; set; } = new();
    public ShadowBakeDefinition Bake { get; set; } = new();
}

public sealed class SunDefinition
{
    /// <summary>Compass direction the sun shines from, degrees: 0 = from +z (south edge), 90 = from +x (east edge).</summary>
    public float Yaw { get; set; } = 45f;
    /// <summary>Degrees above the horizon.</summary>
    public float Elevation { get; set; } = 45f;
    public string Color { get; set; } = "#fff4e0";
    public float Energy { get; set; } = 1.2f;
    /// <summary>Apparent size in degrees: larger gives softer realtime shadows that harden near the caster.</summary>
    public float AngularSize { get; set; } = 1f;

    /// <summary>Unit vector from the ground toward the sun.</summary>
    public Vector3 ToSun()
    {
        float yaw = Yaw * MathF.PI / 180f, el = Math.Clamp(Elevation, 1f, 90f) * MathF.PI / 180f;
        return new Vector3(MathF.Cos(el) * MathF.Sin(yaw), MathF.Sin(el), MathF.Cos(el) * MathF.Cos(yaw));
    }
}

public sealed class SkyDefinition
{
    /// <summary>res:// panorama image; empty uses a procedural sky from <see cref="Top"/> and <see cref="Horizon"/>.</summary>
    public string Panorama { get; set; } = "";
    /// <summary>
    /// Panorama only: azimuth of the sun painted in the image, degrees (u = (atan2(x, -z) + pi) / 2pi). The sky is turned
    /// so the painted sun sits where <see cref="SunDefinition.Yaw"/> puts the light; match the elevation by hand.
    /// </summary>
    public float SunAzimuth { get; set; }
    /// <summary>Brightness of the sky itself (and of the light it sheds).</summary>
    public float Energy { get; set; } = 1f;
    public string Top { get; set; } = "#528ad9";
    public string Horizon { get; set; } = "#b8d1eb";
    /// <summary>Sky light on everything in shade.</summary>
    public float AmbientEnergy { get; set; } = 1f;
}

public sealed class FogDefinition
{
    public string Color { get; set; } = "#b3cce6";
    public float Density { get; set; } = 0.0003f;
}

public sealed class ShadowBakeDefinition
{
    /// <summary>Metres per shadow texel.</summary>
    public float Texel { get; set; } = 0.5f;
    /// <summary>Blur radius in texels.</summary>
    public int Blur { get; set; } = 1;
    /// <summary>Extra blur in metres per metre between caster and ground: soft edges far from the canopy, crisper near it.</summary>
    public float Softness { get; set; } = 0.06f;
    /// <summary>0..1: how dark the baked shadow gets.</summary>
    public float Strength { get; set; } = 1f;
}
