"""
ATLANTIS KIT  -  ROCKS add-on (Blender 3.x / 4.x)
Paste into the Scripting tab and press Run (clears the scene, exports rocks only).

Builds: Rock_Boulder_A, Rock_Boulder_B, Rock_Boulder_Large, Rock_Slab, Rock_Spire,
        Rock_Cluster, Rock_Arch
Chunky faceted shapes (reads well with the cel shader), some with sediment layers.
Pivot at base-center, slightly sunk so they sit into the sand. Material: Atl_Rock.
Exports to the same AtlantisKit_FBX folder as the main kit.
"""
import bpy, bmesh, os, random
from math import pi, sin, cos, radians
from mathutils import Vector, noise

EXPORT_FBX = True
EXPORT_DIR = ""            # "" = folder next to the .blend, or ~/AtlantisKit_FBX
SPACING = 4.5

# ---------------------------------------------------------------- scene reset
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for block in (bpy.data.meshes, bpy.data.materials):
    for item in list(block):
        if item.users == 0:
            block.remove(item)

def mat(name, color, rough=0.85):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1)
    return m

M_ROCK = mat("Atl_Rock", (0.42, 0.50, 0.50))

# ---------------------------------------------------------------- helpers
def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

def shade(obj, angle):
    activate(obj)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=radians(angle))      # 4.1+
    except (AttributeError, RuntimeError):
        bpy.ops.object.shade_smooth()
        if hasattr(obj.data, "use_auto_smooth"):
            obj.data.use_auto_smooth = True
            obj.data.auto_smooth_angle = radians(angle)

def seed_offset(seed):
    r = random.Random(seed)
    return Vector((r.uniform(-500, 500), r.uniform(-500, 500), r.uniform(-500, 500)))

def rock_data(seed, size, subdiv=3, rough=0.35, detail=0.12, strata=0.0, step=0.18,
              taper=0.0, cut=0.55, sink=0.04):
    """Noise-displaced icosphere -> (verts, faces). Flat underside, optional layers/taper."""
    off = seed_offset(seed)
    bm = bmesh.new()
    try:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    except TypeError:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, diameter=1.0)
    for v in bm.verts:
        d = v.co.normalized()
        r = 1 + rough * noise.noise(d * 1.1 + off) + detail * noise.noise(d * 3.5 + off * 1.3)
        co = d * r
        co = Vector((co.x * size[0], co.y * size[1], co.z * size[2]))
        if taper:
            t = min(max((co.z / size[2] + 1) / 2, 0), 1)
            k = 1 - taper * t
            co.x *= k
            co.y *= k
        if strata:
            zq = round(co.z / step) * step
            co.z += (zq - co.z) * strata
        co.z = max(co.z, -size[2] * cut)
        v.co = co
    zmin = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= zmin + sink * size[2]
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces

def arch_data(seed, R=2.2, r0=0.62, rings=30, segs=18):
    """Rock arch: a noisy tube swept along a half circle, feet flattened into the sand."""
    off = seed_offset(seed)
    verts, faces = [], []
    for i in range(rings + 1):
        a = -0.18 + (pi + 0.36) * i / rings
        c = Vector((R * cos(a), 0, R * sin(a) * 1.05))
        n1, n2 = Vector((cos(a), 0, sin(a))), Vector((0, 1, 0))
        r = r0 * (1 - 0.28 * max(0.0, sin(a)))              # thinner at the top
        for j in range(segs):
            b = 2 * pi * j / segs
            dv = n1 * cos(b) + n2 * sin(b) * 1.35
            p = c + dv * r
            disp = 0.35 * r * noise.noise(p * 0.9 + off) + 0.12 * r * noise.noise(p * 2.6 + off)
            p = p + dv.normalized() * disp
            p.z = max(p.z, -0.08)
            verts.append(p)
    for i in range(rings):
        for j in range(segs):
            j1 = (j + 1) % segs
            faces.append([i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j])
    faces.append(list(reversed(range(segs))))
    faces.append([rings * segs + j for j in range(segs)])
    return verts, faces

def build(name, parts, decimate=0.3, smooth_angle=28):
    verts, faces = [], []
    for (pv, pf), offset in parts:
        o = len(verts)
        verts += [tuple(p + Vector(offset)) for p in pv]
        faces += [[i + o for i in f] for f in pf]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    mesh.materials.append(M_ROCK)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=0.0005)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    if decimate < 1:                                    # chunky planar facets
        mod = obj.modifiers.new("Decimate", 'DECIMATE')
        mod.ratio = decimate
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    shade(obj, smooth_angle)
    return obj

# ---------------------------------------------------------------- build
def cluster(seed, count=6):
    rng = random.Random(seed)
    parts = []
    for k in range(count):
        s = rng.uniform(0.15, 0.42)
        size = (s * rng.uniform(0.9, 1.4), s * rng.uniform(0.8, 1.2), s * rng.uniform(0.6, 1.0))
        a, d = rng.uniform(0, 2 * pi), rng.uniform(0, 0.8) * (k > 0)
        parts.append((rock_data(seed * 10 + k, size, subdiv=2, strata=rng.choice((0, 0.5)), step=0.08),
                      (cos(a) * d, sin(a) * d, 0)))
    return parts

ZERO = (0, 0, 0)
assets = [
    build("Rock_Boulder_A",     [(rock_data(1, (0.80, 0.70, 0.60)), ZERO)], 0.30),
    build("Rock_Boulder_B",     [(rock_data(2, (0.60, 0.90, 0.75), strata=0.5, step=0.15), ZERO)], 0.30),
    build("Rock_Boulder_Large", [(rock_data(3, (2.0, 1.6, 1.4), subdiv=4, rough=0.3,
                                            strata=0.35, step=0.3), ZERO)], 0.15),
    build("Rock_Slab",          [(rock_data(4, (1.5, 1.1, 0.32), rough=0.2, detail=0.08, cut=0.3), ZERO)], 0.35),
    build("Rock_Spire",         [(rock_data(5, (0.8, 0.7, 2.8), subdiv=4, taper=0.55,
                                            strata=0.7, step=0.35), ZERO)], 0.20),
    build("Rock_Cluster",       cluster(6), 0.35),
    build("Rock_Arch",          [(arch_data(7), ZERO)], 0.40),
]

# ---------------------------------------------------------------- export + preview
if EXPORT_FBX:
    out = EXPORT_DIR or (os.path.join(os.path.dirname(bpy.data.filepath), "AtlantisKit_FBX")
                         if bpy.data.filepath else os.path.expanduser("~/AtlantisKit_FBX"))
    os.makedirs(out, exist_ok=True)
    for a in assets:
        activate(a)
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(out, a.name + ".fbx"),
            use_selection=True, object_types={'MESH'},
            apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True,
            axis_forward='-Z', axis_up='Y', bake_space_transform=True,
            mesh_smooth_type='FACE', add_leaf_bones=False)
    print("Exported", len(assets), "rock FBX files to", out)

for i, a in enumerate(assets):
    a.location.x = (i - (len(assets) - 1) / 2) * SPACING

bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
floor = bpy.context.object
floor.name = "_PreviewSand"
floor.data.materials.append(mat("_Sand", (0.55, 0.50, 0.36), 1.0))
bpy.ops.object.light_add(type='SUN', location=(0, 0, 10), rotation=(radians(35), 0, radians(25)))
bpy.context.object.data.energy = 3
bpy.ops.object.camera_add(location=(0, -24, 7), rotation=(radians(76), 0, 0))
bpy.context.scene.camera = bpy.context.object
bpy.ops.object.select_all(action='DESELECT')
print("Rocks built:", ", ".join(a.name for a in assets))
