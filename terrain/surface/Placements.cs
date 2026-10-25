using System.Collections.Generic;
using Godot;
using Terrain.Core.Level;
using Terrain.Core.World;

namespace Terrain.Surface;

/// <summary>
/// Puts pre-placed buildings and props on their cell footprints: centred, rotated, optionally scaled to fill the
/// footprint, and standing at the lowest ground under it so the base sinks into higher ground instead of floating.
/// </summary>
internal sealed class Placements
{
    readonly Node3D _parent;
    readonly List<Node3D> _nodes = new();
    readonly Dictionary<string, Node3D> _byId = new();

    public Placements(Node3D parent) => _parent = parent;

    public Node3D Node(string id) => _byId.GetValueOrDefault(id);

    public void Build(WorldData w, IReadOnlyList<PlacementDefinition> placements)
    {
        foreach (var n in _nodes) n.QueueFree();
        _nodes.Clear();
        _byId.Clear();

        foreach (var p in placements)
        {
            if (!ResourceLoader.Exists(p.Scene))
            {
                GD.PushError($"[Terrain] placement '{p.Id}': missing scene {p.Scene}");
                continue;
            }
            var node = GD.Load<PackedScene>(p.Scene).Instantiate<Node3D>();
            node.Name = string.IsNullOrEmpty(p.Id) ? node.Name : p.Id;
            var bounds = Bounds(node, Transform3D.Identity);

            // footprint in metres; rotated by 90 or 270 the model's x and z swap
            var foot = new Vector2(p.Cells[0], p.Cells[1]) * w.CellSize;
            bool quarter = Mathf.PosMod(Mathf.RoundToInt(p.Rotation / 90f), 2) == 1;
            float modelX = quarter ? bounds.Size.Z : bounds.Size.X, modelZ = quarter ? bounds.Size.X : bounds.Size.Z;
            float scale = p.Fit && modelX > 0 && modelZ > 0 ? p.Fill * Mathf.Min(foot.X / modelX, foot.Y / modelZ) : 1f;

            var (low, high) = w.FootprintRange(p.Cell[0], p.Cell[1], p.Cells[0], p.Cells[1]);
            var basis = new Basis(Vector3.Up, Mathf.DegToRad(p.Rotation)).Scaled(Vector3.One * scale);
            var centre = new Vector3((p.Cell[0] + p.Cells[0] * 0.5f) * w.CellSize, low, (p.Cell[1] + p.Cells[1] * 0.5f) * w.CellSize);
            // move the model's bounds centre over the footprint centre and its bottom onto the ground (or its origin to
            // the given elevation)
            if (p.Elevation is { } y) centre.Y = y;
            var offset = basis * new Vector3(bounds.GetCenter().X, p.Elevation.HasValue ? 0 : bounds.Position.Y, bounds.GetCenter().Z);
            node.Transform = new Transform3D(basis, centre - offset);
            _parent.AddChild(node);
            _nodes.Add(node);
            if (!string.IsNullOrEmpty(p.Id)) _byId[p.Id] = node;

            GD.Print($"[Terrain] placed '{node.Name}': model {bounds.Size.X:F2} x {bounds.Size.Y:F2} x {bounds.Size.Z:F2} m, " +
                     $"scale {scale:F2}, footprint {foot.X:F0} x {foot.Y:F0} m, ground {low:F2}..{high:F2} m");
        }
    }

    /// <summary>Combined mesh bounds in the root's space (the node is not in the tree yet).</summary>
    static Aabb Bounds(Node node, Transform3D xf)
    {
        Aabb? total = null;
        foreach (var child in node.GetChildren())
        {
            var childXf = child is Node3D n3 ? xf * n3.Transform : xf;
            if (child is VisualInstance3D vi)
            {
                var box = childXf * vi.GetAabb();
                total = total is { } t ? t.Merge(box) : box;
            }
            var inner = Bounds(child, childXf);
            if (inner.Size != Vector3.Zero) total = total is { } t2 ? t2.Merge(inner) : inner;
        }
        return total ?? new Aabb();
    }
}
