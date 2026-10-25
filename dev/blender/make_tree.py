# Round-crowned tree for the foliage instancer, generated in Blender (headless):
#   blender -b --factory-startup --python dev/blender/make_tree.py -- --out assets/trees/round_tree_1.gltf --seed 1
#
# The canopy is a few overlapping lumps: an opaque core (no see-through holes) wrapped in leaf-clump cards that break
# up the silhouette. Every canopy vertex gets a soft normal, half the lump it sits on and half one ellipsoid around the
# whole crown, so a crown lights as a few big clumps with one lit side and one shade side instead of card noise.
# Materials are named for FoliageMeshes: "Bark" sways, "Leaves*" flutter; the cards are exported as an alpha mask.
import bpy, bmesh, json, math, os, random, sys
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default):
    return args[args.index(name) + 1] if name in args else default
out = os.path.abspath(arg("--out", "assets/trees/round_tree_1.gltf"))
rng = random.Random(int(arg("--seed", "1")))

bpy.ops.wm.read_factory_settings(use_empty=True)

# --- leaf clump texture: pointed leaves scattered in a disc, darker toward the middle ---
def leaf_texture(size=512):
    rgba = np.zeros((size, size, 4), np.float32)
    yy, xx = np.mgrid[0:size, 0:size] / size
    for _ in range(110):
        a, r = rng.uniform(0, math.tau), 0.42 * rng.random() ** 0.7
        cx, cy = 0.5 + r * math.cos(a), 0.5 + r * math.sin(a)
        ang = a + rng.uniform(-0.6, 0.6)  # leaves point outward from the clump
        length, width = rng.uniform(0.07, 0.11), rng.uniform(0.028, 0.04)
        u = (xx - cx) * math.cos(ang) + (yy - cy) * math.sin(ang)
        v = -(xx - cx) * math.sin(ang) + (yy - cy) * math.cos(ang)
        t = np.clip(u / length, -1, 1)
        inside = (np.abs(u) < length) & (np.abs(v) < width * np.sqrt(1 - t * t) * (1 - 0.35 * t))
        shade = rng.uniform(0.8, 1.15) * (0.85 + 0.25 * r / 0.42)
        vein = np.clip(np.abs(v) / width * 10, 0.82, 1)  # a faint midrib
        for c, base in enumerate((0.36, 0.56, 0.22)):  # sRGB green
            rgba[..., c] = np.where(inside, np.clip(base * shade * vein, 0, 1), rgba[..., c])
        rgba[..., 3] = np.where(inside, 1, rgba[..., 3])
    img = bpy.data.images.new("leaf_clump", size, size, alpha=True)
    img.pixels.foreach_set(rgba[::-1].ravel())  # Blender stores rows bottom-up
    path = os.path.join(os.path.dirname(out), "leaf_clump.png")
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img

def material(name, color, image=None):
    m = bpy.data.materials.new(name)
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = 0.9
    if image:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        m.node_tree.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        m.use_backface_culling = False
    return m

# --- trunk: a skinned branch skeleton, slightly crooked ---
def trunk_mesh():
    j = lambda s: rng.uniform(-s, s)
    pts = [((0, 0, 0), 0.34), ((j(0.1), j(0.1), 1.3), 0.25), ((j(0.2), j(0.2), 2.5), 0.2), ((j(0.2), j(0.2), 3.6), 0.13)]
    edges = [(0, 1), (1, 2), (2, 3)]
    for k in range(3):  # branches from the fork into the crown
        a = k * math.tau / 3 + j(0.5)
        pts.append(((math.cos(a) * 1.1, math.sin(a) * 1.1, 3.5 + j(0.3)), 0.08))
        edges.append((2, len(pts) - 1))
    me = bpy.data.meshes.new("skeleton")
    me.from_pydata([p for p, _ in pts], edges, [])
    ob = bpy.data.objects.new("skeleton", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.modifiers.new("skin", "SKIN")
    for i, (_, r) in enumerate(pts):
        me.skin_vertices[0].data[i].radius = (r, r)
    me.skin_vertices[0].data[0].use_root = True
    ob.modifiers.new("smooth", "SUBSURF").levels = 1
    eval_ob = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return bpy.data.meshes.new_from_object(eval_ob)

# --- canopy lumps ---
crown_c, crown_r = Vector((0, 0, 4.7)), Vector((2.6, 2.6, 2.2))
lumps = [(Vector((0, 0, 4.9)), 1.7), (Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 5.9)), 1.15)]
for k in range(5):
    a = k * math.tau / 5 + rng.uniform(-0.35, 0.35)
    d = rng.uniform(1.15, 1.45)
    lumps.append((Vector((math.cos(a) * d, math.sin(a) * d, rng.uniform(3.9, 4.6))), rng.uniform(1.0, 1.35)))

def soft_normal(p, lump_c):
    crown = Vector(((p - crown_c).x / crown_r.x ** 2, (p - crown_c).y / crown_r.y ** 2, (p - crown_c).z / crown_r.z ** 2))
    return ((p - lump_c).normalized() + crown.normalized()).normalized()

def inside_other(p, own):
    return any(i != own and (p - c).length < r * 0.97 for i, (c, r) in enumerate(lumps))

bm = bmesh.new()
trunk = trunk_mesh()
bm.from_mesh(trunk)
for f in bm.faces:
    f.material_index = 0
normals = [v.normal.copy() for v in bm.verts]
n_trunk = len(bm.verts)

# opaque core, a little inside the cards
for i, (c, r) in enumerate(lumps):
    ret = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r * 0.88)
    for v in ret["verts"]:
        v.co += c
        normals.append(soft_normal(v.co, c))
    for f in {f for v in ret["verts"] for f in v.link_faces}:
        f.material_index = 1
        f.smooth = True

# leaf-clump cards facing out of each lump, skipping spots buried in a neighbour
uv = bm.loops.layers.uv.verify()
for i, (c, r) in enumerate(lumps):
    count = int(9 * r * r)
    for k in range(count):
        z = 1 - 2 * (k + 0.5) / count
        a = k * 2.39996 + rng.uniform(-0.3, 0.3)
        n = Vector((math.cos(a) * math.sqrt(1 - z * z), math.sin(a) * math.sqrt(1 - z * z), z))
        p = c + n * r * rng.uniform(0.92, 1.0)
        if inside_other(p, i):
            continue
        n_card = (n + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), rng.uniform(-0.2, 0.4)))).normalized()
        t = n_card.cross(Vector((0, 0, 1)) if abs(n_card.z) < 0.9 else Vector((1, 0, 0))).normalized()
        roll = rng.uniform(0, math.tau)
        t = t * math.cos(roll) + n_card.cross(t) * math.sin(roll)
        b = n_card.cross(t)
        s = rng.uniform(0.55, 0.75)
        corners = [p + (t * x + b * y) * s for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        verts = [bm.verts.new(q) for q in corners]
        normals.extend(soft_normal(p, c) for _ in verts)
        f = bm.faces.new(verts)
        f.material_index = 2
        f.smooth = True
        for loop, (u, v) in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
            loop[uv].uv = (u, v)

me = bpy.data.meshes.new("RoundTree")
bm.to_mesh(me)
bm.free()
me.shade_smooth()
me.normals_split_custom_set_from_vertices([tuple(n) for n in normals])
ob = bpy.data.objects.new("RoundTree", me)
bpy.context.scene.collection.objects.link(ob)
bpy.data.objects.remove(bpy.data.objects["skeleton"])

os.makedirs(os.path.dirname(out), exist_ok=True)
leaves = leaf_texture()
for m in (material("Bark", (0.16, 0.11, 0.08)), material("Leaves_Core", (0.075, 0.17, 0.045)),
          material("Leaves", (1, 1, 1), leaves)):
    me.materials.append(m)

bpy.ops.export_scene.gltf(filepath=out, export_format="GLTF_SEPARATE", export_yup=True, export_normals=True,
                          export_materials="EXPORT", export_image_format="AUTO")

# the exporter writes the cards as alpha blend; the instancer wants a cut-out
with open(out) as fh:
    g = json.load(fh)
for m in g["materials"]:
    if m["name"] == "Leaves":
        m["alphaMode"], m["alphaCutoff"], m["doubleSided"] = "MASK", 0.5, True
with open(out, "w") as fh:
    json.dump(g, fh, indent=1)
tris = sum(len(p.vertices) - 2 for p in me.polygons)
print(f"[make_tree] {out}: {len(lumps)} lumps, {tris} triangles, {len(me.vertices)} vertices ({n_trunk} trunk)")
