"""Rail Tracer: the player's model (the conductor), from the Renderpeople Eric download, in our flat look.

rp_eric_rigged_001_u3d.fbx comes in centimetres under a 0.01 armature scale (Unity's Ragdoll Builder makes millimetre
colliders in that space), as a photo scan on one 8k texture, with a stray cube, camera and light. This re-exports the
armature and the one skinned mesh in metres at scale 1, with every face given one of our flat Chr_ materials (as the
guards have) instead of the scan:

  head    Chr_Hair above the brows where the scan is dark, Chr_Skin elsewhere (clean-shaven: the stubble goes)
  eyes    Chr_EyeWhite, the dark middle Chr_Eye
  hands   Chr_Skin;  feet Chr_Shoes
  clothes Chr_Shirt where the scan is light (shirt), Chr_Suit where dark (waistcoat, trousers, tie, belt) and the
          waistcoat's back (grey satin in the scan)

Each face's colour is the scan at its UV centre (from a blurred 1k copy); labels are then smoothed by majority over
neighbouring faces so fold shadows don't speckle. Bone names, rest pose and weights are unchanged.

  in:  Assets/RailTracer/Characters/37-rp_eric_rigged_001_fbx/rp_eric_rigged_001_u3d.fbx (+ tex/..._dif.jpg)
  out: Assets/RailTracer/Characters/37-rp_eric_rigged_001_fbx/SK_Conductor.fbx (next to its source; gitignored with it)

Run:  blender -b --factory-startup --python railtracer_conductor.py [-- --render <dir>]
"""
import bpy, bmesh, math, os, sys
import numpy as np
from collections import Counter
from mathutils import Vector

DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "RailTracer", "Characters", "37-rp_eric_rigged_001_fbx"))
SRC = os.path.join(DIR, "rp_eric_rigged_001_u3d.fbx")
TEX = os.path.join(DIR, "tex", "rp_eric_rigged_001_dif.jpg")
OUT = os.path.join(DIR, "SK_Conductor.fbx")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None
MATS = ["Chr_Skin", "Chr_Hair", "Chr_Shirt", "Chr_Suit", "Chr_Shoes", "Chr_Eye", "Chr_EyeWhite"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm)
for o in list(bpy.data.objects):
    if o not in (arm, me):
        bpy.data.objects.remove(o)

# metres at scale 1: apply the armature's 0.01 to its bones and the mesh
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); me.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

# --- the scan, small and blurred, as a numpy array
img = bpy.data.images.load(TEX)
img.scale(1024, 1024)
px = np.empty(1024 * 1024 * 4, dtype=np.float32); img.pixels.foreach_get(px)
px = px.reshape(1024, 1024, 4)[:, :, :3]
k = np.ones(5) / 5
for ax in (0, 1):
    px = np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), ax, px)

m = me.data; mw = me.matrix_world
groups = {g.index: g.name for g in me.vertex_groups}
dom = [groups[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in m.vertices]
uv = m.uv_layers.active.data
eye_z = (mw @ arm.data.bones["eye_l"].head_local).z


def colour(p):
    u = sum(uv[l].uv[0] for l in p.loop_indices) / p.loop_total
    v = sum(uv[l].uv[1] for l in p.loop_indices) / p.loop_total
    x = min(1023, max(0, int((u % 1) * 1024))); y = min(1023, max(0, int((v % 1) * 1024)))
    return px[y, x]


def region(p):
    c = Counter(dom[i] for i in p.vertices).most_common(1)[0][0]
    if c.startswith(("eye_", "eyelid")):
        return "eye"
    if c.startswith(("head", "jaw", "mouth", "eyebrow", "neck")):
        return "head"
    if c.startswith(("hand", "thumb", "index", "middle", "ring", "pinky")):
        return "hand"
    if c.startswith(("foot", "ball", "toe")):
        return "foot"
    return "body"


labels, regions = [], []
for p in m.polygons:
    r, g, b = colour(p)
    lum = 0.3 * r + 0.59 * g + 0.11 * b
    sat = max(r, g, b) - min(r, g, b)
    reg = region(p); c = mw @ p.center
    if reg == "eye":
        lab = "Chr_Eye" if lum < 0.18 else "Chr_EyeWhite"
    elif reg == "head":
        if lum > 0.6 and sat < 0.1 and c.z < eye_z - 0.08:
            lab = "Chr_Shirt"                                    # the collar (weighted to the neck)
        elif c.z > eye_z - 0.005 and lum < 0.22 and sat < 0.16:
            lab = "Chr_Hair"                                     # hair and brows: dark, not skin-coloured
        else:
            lab = "Chr_Skin"
    elif reg == "hand":
        lab = "Chr_Skin" if lum > 0.3 else "Chr_Suit"           # cuffs that dip onto the wrist stay with the clothes
        if lum > 0.7 and sat < 0.12:
            lab = "Chr_Shirt"
    elif reg == "foot":
        lab = "Chr_Shoes" if lum < 0.4 else "Chr_Suit"
    else:
        torso = Counter(dom[i] for i in p.vertices).most_common(1)[0][0].startswith(("spine", "hip", "pelvis"))
        if torso and c.y > -0.02 and c.z < 1.53:
            lab = "Chr_Suit"                                     # the waistcoat's back (grey satin in the scan)
        else:
            lab = "Chr_Shirt" if lum > 0.55 else "Chr_Suit"
    labels.append(lab); regions.append(reg)

# --- smooth: each face takes the majority of itself and its edge neighbours (same region), a few passes
bm = bmesh.new(); bm.from_mesh(m); bm.faces.ensure_lookup_table()
nbr = [[g.index for e in f.edges for g in e.link_faces if g.index != f.index] for f in bm.faces]
bm.free()
for _ in range(3):
    new = list(labels)
    for i, ns in enumerate(nbr):
        if labels[i] == "Chr_Eye":
            continue   # small details: keep
        votes = Counter([labels[i]] + [labels[j] for j in ns if regions[j] == regions[i]])
        new[i] = votes.most_common(1)[0][0]
    labels = new

m.materials.clear()
for name in MATS:
    m.materials.append(bpy.data.materials.new(name))
for p, lab in zip(m.polygons, labels):
    p.material_index = MATS.index(lab)
print("CONDUCTOR faces by material", dict(Counter(labels)))
height = max((mw @ v.co).z for v in m.vertices)
print("CONDUCTOR scale", tuple(round(x, 3) for x in arm.scale), "height %.3f" % height, "bones", len(arm.data.bones))

used = sorted({p.material_index for p in m.polygons})
keep = [m.materials[i] for i in used]; remap = {o: n for n, o in enumerate(used)}
idx = [remap[p.material_index] for p in m.polygons]
m.materials.clear()
for mt in keep:
    m.materials.append(mt)
for p, i in zip(m.polygons, idx):
    p.material_index = i

bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')

if RENDER:
    cols = {"Chr_Suit": (0.04, 0.04, 0.05), "Chr_Shirt": (0.7, 0.66, 0.55), "Chr_Skin": (0.6, 0.36, 0.24),
            "Chr_Hair": (0.05, 0.025, 0.01), "Chr_Shoes": (0.01, 0.01, 0.01), "Chr_Eye": (0.02, 0.02, 0.02),
            "Chr_EyeWhite": (0.75, 0.73, 0.7), "Chr_Mouth": (0.35, 0.1, 0.1)}
    scn = bpy.context.scene; scn.render.engine = 'BLENDER_EEVEE'; scn.render.resolution_x, scn.render.resolution_y = 640, 900
    for mt in m.materials:
        mt.use_nodes = True
        mt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*cols.get(mt.name, (0.5, 0.5, 0.5)), 1)
        mt.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
    w = bpy.data.worlds.new("w"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.55, 0.6, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scn.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    cam.data.type = 'ORTHO'
    for name, (yaw, pitch, scale, tgt) in {"front": (0, 8, 2.1, 0.95), "back": (180, 10, 2.1, 0.95),
                                           "game": (-25, 50, 2.3, 0.9), "head": (-25, 10, 0.45, 1.68)}.items():
        cam.data.ortho_scale = scale
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        cam.location = Vector((0, 0, tgt)) + d * 6
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(RENDER, "SK_Conductor_" + name + ".png")
        bpy.ops.render.render(write_still=True)

# check: re-import, scale 1, every vertex weighted
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=OUT)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
me = next(o for o in bpy.data.objects if o.type == 'MESH')
print("CHECK SK_Conductor scale", tuple(round(x, 3) for x in arm.scale), "height %.3f" % max((me.matrix_world @ v.co).z for v in me.data.vertices),
      "unweighted", sum(1 for v in me.data.vertices if not v.groups), "materials", [mt.name for mt in me.data.materials],
      "eye bones", [b for b in ("eye_l", "eye_r") if b in arm.data.bones])
