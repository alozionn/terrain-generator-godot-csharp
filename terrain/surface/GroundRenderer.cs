using System;
using System.Collections.Generic;
using Godot;
using Terrain.Core.Ground;
using Terrain.Core.World;

namespace Terrain.Surface;

/// <summary>
/// Draws the ground as square chunk meshes (one sample per vertex, so they cull separately and stay small), shaded by
/// ground.gdshader, and owns the height-map collision. At board scale (200–500 m) no LOD is needed.
/// </summary>
internal sealed class GroundRenderer
{
    readonly Node3D _parent;
    readonly List<MeshInstance3D> _chunks = new();
    StaticBody3D _body;
    ShaderMaterial _material;

    public GroundRenderer(Node3D parent) => _parent = parent;

    /// <summary>Quads per chunk side.</summary>
    public int ChunkQuads { get; set; } = 64;

    public void Build(WorldData w, GroundMaps g, GroundTextures textures)
    {
        foreach (var c in _chunks) c.QueueFree();
        _chunks.Clear();

        _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://terrain/surface/ground.gdshader") };
        _material.SetShaderParameter("albedo_height", textures.AlbedoArray);
        _material.SetShaderParameter("normal_rough", textures.NormalArray);
        _material.SetShaderParameter("uv_scale", textures.UvScales);
        _material.SetShaderParameter("texture_count", textures.Count);

        var weights = Weights(w, g);
        for (int z0 = 0; z0 < w.Samples - 1; z0 += ChunkQuads)
            for (int x0 = 0; x0 < w.Samples - 1; x0 += ChunkQuads)
            {
                int nx = Math.Min(ChunkQuads, w.Samples - 1 - x0), nz = Math.Min(ChunkQuads, w.Samples - 1 - z0);
                var chunk = new MeshInstance3D
                {
                    Name = $"Ground_{x0 / ChunkQuads}_{z0 / ChunkQuads}",
                    Mesh = Chunk(w, g, weights, x0, z0, nx, nz),
                    MaterialOverride = _material,
                    Layers = 1, // zone-grid decals project onto layer 1 only
                };
                _parent.AddChild(chunk);
                _chunks.Add(chunk);
            }

        _body?.QueueFree();
        // HeightMapShape3D is centred and one unit per sample; scale the shape, never the body
        _body = new StaticBody3D { Name = "GroundCollision", Position = new Vector3(w.Size * 0.5f, 0, w.Size * 0.5f) };
        _body.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = w.Samples, MapDepth = w.Samples, MapData = w.Height },
            Scale = new Vector3(w.SampleSpacing, 1, w.SampleSpacing),
        });
        _parent.AddChild(_body);
    }

    /// <summary>Per-sample weights for the eight texture slots, as bytes (two RGBA8 custom attributes per vertex).</summary>
    static byte[] Weights(WorldData w, GroundMaps g)
    {
        var weights = new byte[w.Height.Length * GroundTextures.MaxTextures];
        for (int i = 0; i < w.Height.Length; i++)
        {
            float blend = g.Blend[i];
            Add(weights, i, g.Base[i], 1f - blend);
            Add(weights, i, g.Overlay[i], blend);
        }
        return weights;

        static void Add(byte[] into, int sample, byte texture, float amount)
        {
            if (texture >= GroundTextures.MaxTextures || amount <= 0) return;
            int o = sample * GroundTextures.MaxTextures + texture;
            into[o] = (byte)Math.Min(255, into[o] + (int)MathF.Round(amount * 255f));
        }
    }

    static ArrayMesh Chunk(WorldData w, GroundMaps g, byte[] weights, int x0, int z0, int nx, int nz)
    {
        int vx = nx + 1, vz = nz + 1, n = vx * vz;
        var verts = new Vector3[n];
        var normals = new Vector3[n];
        var tangents = new float[n * 4];
        var colors = new Color[n];
        var custom0 = new byte[n * 4];
        var custom1 = new byte[n * 4];
        for (int z = 0; z < vz; z++)
            for (int x = 0; x < vx; x++)
            {
                int sx = x0 + x, sz = z0 + z, s = w.Index(sx, sz), v = z * vx + x;
                verts[v] = new Vector3(sx * w.SampleSpacing, w.Height[s], sz * w.SampleSpacing);
                int xl = Math.Max(0, sx - 1), xr = Math.Min(w.Samples - 1, sx + 1), zu = Math.Max(0, sz - 1), zd = Math.Min(w.Samples - 1, sz + 1);
                float dx = (w.H(xr, sz) - w.H(xl, sz)) / ((xr - xl) * w.SampleSpacing);
                float dz = (w.H(sx, zd) - w.H(sx, zu)) / ((zd - zu) * w.SampleSpacing);
                normals[v] = new Vector3(-dx, 1, -dz).Normalized();
                // tangent follows +x over the slope; w = -1 makes the binormal point +z, matching uv = world xz
                var t = new Vector3(1, dx, 0).Normalized();
                tangents[v * 4] = t.X; tangents[v * 4 + 1] = t.Y; tangents[v * 4 + 2] = t.Z; tangents[v * 4 + 3] = -1;
                var tint = g.Tint[s];
                colors[v] = new Color(tint.X, tint.Y, tint.Z).SrgbToLinear();
                Array.Copy(weights, s * 8, custom0, v * 4, 4);
                Array.Copy(weights, s * 8 + 4, custom1, v * 4, 4);
            }

        var indices = new int[nx * nz * 6];
        int k = 0;
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int a = z * vx + x, b = a + 1, c = a + vx, d = c + 1;
                // split each quad along the diagonal that follows the terrain better
                if (MathF.Abs(verts[a].Y - verts[d].Y) <= MathF.Abs(verts[b].Y - verts[c].Y))
                { indices[k++] = a; indices[k++] = b; indices[k++] = d; indices[k++] = a; indices[k++] = d; indices[k++] = c; }
                else
                { indices[k++] = a; indices[k++] = b; indices[k++] = c; indices[k++] = b; indices[k++] = d; indices[k++] = c; }
            }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Tangent] = tangents;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Custom0] = custom0;
        arrays[(int)Mesh.ArrayType.Custom1] = custom1;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var format = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                   | (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
        return mesh;
    }
}
