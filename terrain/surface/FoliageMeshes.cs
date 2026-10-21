using Godot;
using Terrain.Core.Foliage;
using Terrain.Core.Level;

namespace Terrain.Surface;

/// <summary>
/// Builds a scene for each foliage mesh: a res:// scene as authored, or a simple stand-in shape for "procedural:*".
/// Stand-ins are one MeshInstance3D with a surface per part, because Terrain3D reads several mesh instances in a
/// scene as LOD levels. Their materials take the per-instance colour as an albedo multiplier (tint jitter).
/// </summary>
internal static class FoliageMeshes
{
    static readonly Color Bark = new(0.42f, 0.29f, 0.2f);

    public static PackedScene Scene(FoliageMeshDefinition def)
    {
        if (!def.Scene.StartsWith("procedural:")) return GD.Load<PackedScene>(def.Scene);

        var c = LevelJson.ParseColor(def.Color);
        var color = new Color(c.X, c.Y, c.Z);
        var mesh = new ArrayMesh();
        switch (def.Scene["procedural:".Length..])
        {
            case "tree_round":
                Part(mesh, new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.3f, Height = 3f, RadialSegments = 7 }, Bark, new Vector3(0, 1.5f, 0));
                Part(mesh, new SphereMesh { Radius = 1.7f, Height = 3f, RadialSegments = 10, Rings = 6 }, color, new Vector3(0, 3.8f, 0));
                Part(mesh, new SphereMesh { Radius = 1.1f, Height = 2f, RadialSegments = 8, Rings = 5 }, color.Lightened(0.1f), new Vector3(0.7f, 4.6f, 0.3f));
                break;
            case "tree_pine":
                Part(mesh, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.22f, Height = 2f, RadialSegments = 6 }, Bark, new Vector3(0, 1f, 0));
                Part(mesh, new CylinderMesh { TopRadius = 0f, BottomRadius = 1.5f, Height = 2.6f, RadialSegments = 8 }, color, new Vector3(0, 2.6f, 0));
                Part(mesh, new CylinderMesh { TopRadius = 0f, BottomRadius = 1.1f, Height = 2.2f, RadialSegments = 8 }, color.Lightened(0.08f), new Vector3(0, 3.9f, 0));
                break;
            case "bush":
                Part(mesh, new SphereMesh { Radius = 0.8f, Height = 1.1f, RadialSegments = 9, Rings = 5 }, color, new Vector3(0, 0.4f, 0));
                break;
            case "rock":
                Part(mesh, new SphereMesh { Radius = 0.8f, Height = 1.1f, RadialSegments = 6, Rings = 3 }, color, new Vector3(0, 0.15f, 0), new Vector3(1.2f, 0.7f, 1f));
                break;
            case "card":
                // two crossed quads standing on the ground, visible from both sides
                var size = new Vector2(def.Size[0], def.Size[^1]);
                for (int i = 0; i < 2; i++)
                    Part(mesh, new QuadMesh { Size = size }, color, new Vector3(0, size.Y * 0.5f, 0), Vector3.One, i * 90f, cull: false);
                break;
            default:
                GD.PushError($"[Terrain] unknown procedural mesh '{def.Scene}'");
                break;
        }

        var root = new Node3D { Name = def.Id };
        var mi = new MeshInstance3D { Name = "LOD0", Mesh = mesh };
        root.AddChild(mi);
        mi.Owner = root;
        var scene = new PackedScene();
        scene.Pack(root);
        root.Free();
        return scene;
    }

    static void Part(ArrayMesh into, PrimitiveMesh shape, Color color, Vector3 position, Vector3? scale = null, float yawDeg = 0, bool cull = true)
    {
        var st = new SurfaceTool();
        var xf = new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(yawDeg), 0)).Scaled(scale ?? Vector3.One), position);
        st.AppendFrom(shape, 0, xf);
        st.SetMaterial(new StandardMaterial3D
        {
            AlbedoColor = color,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.9f,
            CullMode = cull ? BaseMaterial3D.CullModeEnum.Back : BaseMaterial3D.CullModeEnum.Disabled,
        });
        st.Commit(into);
    }
}
