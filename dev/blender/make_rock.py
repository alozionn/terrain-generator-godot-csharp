# Sea stack: a tall rock standing in the water, generated in Blender (headless):
#   blender -b --factory-startup --python dev/blender/make_rock.py -- --out assets/props/sea_stack.glb --height 14 --seed 3
#
# One lump: a sphere stretched into a leaning, narrowing column, roughened with noise and decimated into big flat facets;
# faces that look up get moss. The origin is at sea level (place it with "elevation": 0) and the base reaches 5 m below
# so it stands in the water from any angle.
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, noise

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def arg(name, default):
    return args[args.index(name) + 1] if name in args else default
out = os.path.abspath(arg("--out", "assets/props/sea_stack.glb"))
height = float(arg("--height", "14"))
seed = int(arg("--seed", "3"))
rng = random.Random(seed)

bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, color):
    m = bpy.data.materials.new(name)
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = 0.95
    return m

base, radius = -5.0, 3.6
half = (height - base) / 2
lean = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)).normalized() * rng.uniform(0.8, 1.4)
offset = Vector((rng.uniform(0, 100), rng.uniform(0, 100), rng.uniform(0, 100)))

bm = bmesh.new()
bmesh.ops.create_icosphere(bm, subdivisions=4, radius=1.0)
for v in bm.verts:
    t = (v.co.z + 1) / 2                                   # 0 at the foot, 1 at the top
    width = radius * (1.15 - 0.6 * t ** 1.3)               # narrows upward
    p = Vector((v.co.x * width, v.co.y * width, base + t * (height - base) * 1.0))
    p += lean * t * t                                      # leans more toward the top
    n = Vector((v.co.x, v.co.y, v.co.z * 0.3)).normalized()
    p += n * (0.9 * noise.noise(p * 0.18 + offset) + 0.35 * noise.noise(p * 0.5 + offset))  # bulges and ledges
    v.co = p
me = bpy.data.meshes.new("SeaStack")
bm.to_mesh(me)
bm.free()
ob = bpy.data.objects.new("SeaStack", me)
bpy.context.scene.collection.objects.link(ob)
dec = ob.modifiers.new("facets", "DECIMATE")
dec.ratio = 0.15
eval_ob = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
low = bpy.data.meshes.new_from_object(eval_ob)
ob.modifiers.clear()
ob.data = low

bm = bmesh.new()
bm.from_mesh(low)
bm.normal_update()
for f in bm.faces:
    f.smooth = False
    f.material_index = 1 if f.normal.z > 0.55 and f.calc_center_median().z > 1.0 else 0
bm.to_mesh(low)
bm.free()
low.materials.append(material("Rock", (0.13, 0.12, 0.11)))
low.materials.append(material("Moss", (0.08, 0.15, 0.035)))

os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True)
print(f"[make_rock] {out}: {len(low.polygons)} faces, {height} m above the water")
