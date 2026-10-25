# Wooden pontoon (jetty) for placing on the water, generated in Blender (headless):
#   blender -b --factory-startup --python dev/blender/make_pontoon.py -- --out assets/props/pontoon.glb --length 12
#
# Runs along +x from the origin, which sits at sea level (place it with "elevation": 0). Deck planks a little uneven,
# two stringers under them, posts down into the water, a mooring post at the far end.
import bpy, bmesh, math, os, random, sys
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default):
    return args[args.index(name) + 1] if name in args else default
out = os.path.abspath(arg("--out", "assets/props/pontoon.glb"))
length, width, deck = float(arg("--length", "12")), 2.4, 0.6
rng = random.Random(int(arg("--seed", "1")))

bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, color):
    m = bpy.data.materials.new(name)
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = 0.85
    return m

mats = [material("Plank_A", (0.36, 0.22, 0.12)), material("Plank_B", (0.30, 0.18, 0.10)),
        material("Plank_C", (0.42, 0.27, 0.15)), material("Post", (0.17, 0.11, 0.07))]

bm = bmesh.new()
def box(centre, size, mat, rot_x=0.0, rot_z=0.0, bevel=0.0):
    ret = bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Translation(centre) @ Matrix.Rotation(rot_z, 4, "Z") @ Matrix.Rotation(rot_x, 4, "X") @ Matrix.Diagonal((*size, 1))
    bmesh.ops.transform(bm, matrix=m, verts=ret["verts"])
    for f in {f for v in ret["verts"] for f in v.link_faces}:
        f.material_index = mat

def post(x, y, top):
    bottom = -3.0
    ret = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.13, radius2=0.12, depth=top - bottom)
    bmesh.ops.translate(bm, vec=Vector((x, y, (top + bottom) / 2)), verts=ret["verts"])
    for f in {f for v in ret["verts"] for f in v.link_faces}:
        f.material_index = 3
        f.smooth = True

# planks across the deck, each a slightly different length, tilt and tone
plank, gap, x = 0.26, 0.05, 0.0
while x + plank <= length:
    w = width + rng.uniform(-0.12, 0.12)
    box(Vector((x + plank / 2, rng.uniform(-0.05, 0.05), deck + rng.uniform(-0.015, 0.015))), (plank, w, 0.07),
        rng.randrange(3), rot_x=rng.uniform(-0.02, 0.02), rot_z=rng.uniform(-0.03, 0.03))
    x += plank + gap
# stringers and posts
for y in (-width * 0.38, width * 0.38):
    box(Vector((length / 2, y, deck - 0.12)), (length, 0.16, 0.16), 3)
    for i in range(int(length // 2.4) + 1):
        post(min(0.2 + i * 2.4, length - 0.2), y + math.copysign(0.12, y), deck - 0.05)
post(length - 0.25, -width * 0.5 - 0.1, deck + 0.7)  # mooring post

me = bpy.data.meshes.new("Pontoon")
bm.to_mesh(me)
bm.free()
for m in mats:
    me.materials.append(m)
ob = bpy.data.objects.new("Pontoon", me)
bpy.context.scene.collection.objects.link(ob)
os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True)
print(f"[make_pontoon] {out}: {len(me.polygons)} faces, {length} m")
