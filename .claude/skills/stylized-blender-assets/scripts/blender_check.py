"""
Run an assembled kit script in REAL Blender (the `bpy` module), verify the FBX export, and optionally
render a preview with Cycles (CPU). Run it with a Python that has bpy (see setup_blender.sh):

    $BPY scripts/blender_check.py <kit>.py [--render out.png] [--layout layout.json|'[...]']
          [--yaw 30 --pitch 30] [--set CUTAWAY=True] [--samples 32] [--size 1400x860]

Prints: Blender version, every exported FBX with size, per-asset object/triangle counts, material
names, and any traceback. --layout uses the same [name, x, y, z, rot_deg] format as preview.py
(assets are instanced with their real materials). Without it, the script's own row layout is used.
Render-only helpers (colliders) are hidden. Exit code 1 on any failure.
"""
import argparse, ast, json, os, runpy, sys, tempfile, traceback
from math import radians, sin, cos

ap = argparse.ArgumentParser()
ap.add_argument("script")
ap.add_argument("--render", default="")
ap.add_argument("--layout", default="")
ap.add_argument("--yaw", type=float, default=30)
ap.add_argument("--pitch", type=float, default=30)
ap.add_argument("--set", action="append", default=[])
ap.add_argument("--samples", type=int, default=32)
ap.add_argument("--size", default="1400x860")
a = ap.parse_args()

import bpy
from mathutils import Vector
print("Blender", bpy.app.version_string)

init = {}
for kv in a.set:
    k, v = kv.split("=", 1)
    try: init[k] = ast.literal_eval(v)
    except Exception: init[k] = v
out_dir = tempfile.mkdtemp(prefix="kit_fbx_")
sys.argv = [sys.argv[0], "--", "--out", out_dir]           # the kit script reads args after "--"
try:
    runpy.run_path(a.script, init_globals=init, run_name="__main__")
except Exception:
    traceback.print_exc()
    print("FAIL: script raised in Blender")
    sys.exit(1)

fbx = sorted(f for f in os.listdir(out_dir) if f.endswith(".fbx"))
pal = [f for f in os.listdir(out_dir) if f.endswith("_palette.json")]
print("palette:", ", ".join(pal) if pal else "MISSING")
print(f"FBX export ({out_dir}): {len(fbx)} files")
for f in fbx:
    print(f"  {f:40s} {os.path.getsize(os.path.join(out_dir, f)) / 1024:8.1f} KB")
roots = {o.name: o for o in bpy.data.objects if o.parent is None and o.type == 'EMPTY'}
bad = 0
for name, root in roots.items():
    tris, mats, kids, pts = 0, set(), [], []
    for ch in root.children_recursive:
        kids.append(ch.name.replace(name + "_", ""))
        if ch.type == 'MESH':
            ch.data.calc_loop_triangles()
            tris += len(ch.data.loop_triangles)
            mats |= {m.name for m in ch.data.materials if m}
            pts += [ch.matrix_world @ v.co - root.matrix_world.translation for v in ch.data.vertices]
    if name + ".fbx" not in fbx:
        print(f"  !! {name} has no FBX"); bad += 1
    print(f"  {name}: {tris} tris | children {', '.join(kids)} | materials {', '.join(sorted(mats))}")
    if pts:
        lo_ = [min(p[i] for p in pts) for i in range(3)]; hi_ = [max(p[i] for p in pts) for i in range(3)]
        print(f"      bounds x {lo_[0]:.3f}..{hi_[0]:.3f}  y {lo_[1]:.3f}..{hi_[1]:.3f}  z {lo_[2]:.3f}..{hi_[2]:.3f}")
if not fbx:
    print("FAIL: nothing exported"); sys.exit(1)

if a.render:
    sc = bpy.context.scene
    if a.layout:
        lay = open(a.layout).read() if os.path.exists(a.layout) else a.layout
        for name, x, y, z, rot in json.loads(lay):
            src = roots[name]
            inst = bpy.data.objects.new(name + "_inst", None)
            sc.collection.objects.link(inst)
            for ch in src.children:
                if ch.type != 'MESH':
                    continue
                c = ch.copy(); sc.collection.objects.link(c)
                c.parent = inst; c.matrix_parent_inverse.identity(); c.location = (0, 0, 0)
                c["_inst"] = True
            inst.location = (x, y, z); inst.rotation_euler = (0, 0, radians(rot))
        for root in roots.values():                       # hide the originals
            for ch in root.children_recursive:
                ch.hide_render = True
    for o in bpy.data.objects:
        if o.name.endswith("_COL"):
            o.hide_render = True
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in bpy.data.objects
           if o.type == 'MESH' and not o.hide_render for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center = (lo + hi) / 2
    yaw, pitch = radians(a.yaw), radians(a.pitch)
    fwd = Vector((cos(pitch) * sin(yaw), cos(pitch) * cos(yaw), -sin(pitch)))
    right = Vector((cos(yaw), -sin(yaw), 0)); up = right.cross(fwd)
    corners = [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]
    w = max((c - center).dot(right) for c in corners) * 2
    h = max((c - center).dot(up) for c in corners) * 2
    W, H = (int(v) for v in a.size.split("x"))
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(w, h * W / H) * 1.08
    cam.data.clip_end = 2000
    cam.location = center - fwd * (max(hi - lo) * 3 + 10)
    cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    for o in [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(o)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    sc.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.data.angle = radians(3)
    sun.rotation_euler = (radians(40), 0, radians(a.yaw + 35))
    world = bpy.data.worlds.new("PreviewWorld"); sc.world = world
    if bpy.app.version < (5, 0, 0): world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.42, 0.50, 0.55, 1); bg.inputs[1].default_value = 0.9
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'; sc.cycles.samples = a.samples
    try: sc.cycles.use_denoising = True
    except Exception: pass
    sc.view_settings.view_transform = 'Standard'          # true palette colours, no filmic wash-out
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.filepath = os.path.abspath(a.render)
    bpy.ops.render.render(write_still=True)
    print("rendered", sc.render.filepath)
print("FAIL" if bad else "OK")
sys.exit(1 if bad else 0)
