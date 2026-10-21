using System;
using System.Collections.Generic;
using Godot;
using Terrain.Core.Foliage;

namespace Terrain.Surface;

/// <summary>
/// Draws placed foliage: per mesh, per square chunk, one MultiMeshInstance3D for each LOD, each limited to its
/// distance band with Godot's visibility ranges. Chunks keep culling coarse but cheap; the grass shader's own
/// per-blade and per-clump thinning hides where a chunk switches.
/// </summary>
internal sealed class FoliageInstancer
{
    /// <summary>Render layer bit for foliage (layer 2), so decals limited to layer 1 skip it.</summary>
    public const uint FoliageLayer = 2;

    readonly Node3D _parent;
    readonly Dictionary<string, (FoliageMeshDefinition def, List<Mesh> lods, float[] ends)> _meshes = new();
    Node3D _root;

    public FoliageInstancer(Node3D parent) => _parent = parent;

    /// <summary>Chunk side in metres for short-range ground cover; long-range meshes (trees, rocks) use <see cref="FarChunkSize"/>.</summary>
    public float ChunkSize { get; set; } = 32f;
    /// <summary>Chunk side for meshes drawn beyond <see cref="FarRange"/>: they cull rarely, so bigger chunks mean fewer draw calls.</summary>
    public float FarChunkSize { get; set; } = 64f;
    public float FarRange { get; set; } = 200f;

    public int MultiMeshCount { get; private set; }

    public void LoadMeshes(FoliageMeshSetDefinition set)
    {
        _meshes.Clear();
        foreach (var def in set.Meshes)
        {
            var lods = FoliageMeshes.Lods(def);
            if (lods.Count > 0) _meshes[def.Id] = (def, lods, FoliageMeshes.LodEnds(def, lods.Count));
        }
    }

    public void Build(FoliageResult foliage)
    {
        _root?.QueueFree();
        _root = new Node3D { Name = "Foliage" };
        _parent.AddChild(_root);
        MultiMeshCount = 0;

        foreach (var (meshId, list) in foliage.ByMesh)
        {
            if (list.Count == 0) continue;
            if (!_meshes.TryGetValue(meshId, out var mesh))
            {
                GD.PushError($"[Terrain] foliage mesh '{meshId}' is not in meshes.json");
                continue;
            }

            float chunk = mesh.def.Range >= FarRange ? FarChunkSize : ChunkSize;
            var chunks = new Dictionary<(int, int), List<FoliageInstance>>();
            foreach (var f in list)
            {
                var key = ((int)MathF.Floor(f.X / chunk), (int)MathF.Floor(f.Z / chunk));
                if (!chunks.TryGetValue(key, out var c)) chunks[key] = c = new List<FoliageInstance>();
                c.Add(f);
            }

            var shadows = mesh.def.CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            foreach (var instances in chunks.Values)
                for (int lod = 0; lod < mesh.lods.Count; lod++)
                {
                    var mm = new MultiMesh
                    {
                        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                        UseColors = true,
                        Mesh = mesh.lods[lod],
                        InstanceCount = instances.Count,
                    };
                    for (int i = 0; i < instances.Count; i++)
                    {
                        var f = instances[i];
                        var basis = new Basis(Vector3.Up, f.Yaw) * Basis.FromEuler(new Vector3(f.TiltX, 0, f.TiltZ));
                        mm.SetInstanceTransform(i, new Transform3D(basis.Scaled(Vector3.One * f.Scale), new Vector3(f.X, f.Y, f.Z)));
                        mm.SetInstanceColor(i, new Color(f.Tint, f.Tint, f.Tint));
                    }
                    _root.AddChild(new MultiMeshInstance3D
                    {
                        Multimesh = mm,
                        Layers = FoliageLayer,
                        CastShadow = shadows,
                        VisibilityRangeBegin = lod == 0 ? 0 : mesh.ends[lod - 1],
                        // the last LOD is padded by half a chunk diagonal: the range is measured to the chunk's centre,
                        // and must not cut instances that are still within reach of the camera
                        VisibilityRangeEnd = mesh.ends[lod] + (lod == mesh.lods.Count - 1 ? chunk * 0.71f : 0f),
                    });
                    MultiMeshCount++;
                }
        }
    }
}
