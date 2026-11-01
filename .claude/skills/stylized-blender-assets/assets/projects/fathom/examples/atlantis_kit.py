"""
ATLANTIS MODULAR KIT  -  starter set for Unity
Blender 3.x / 4.x. Paste into the Scripting tab and press Run (clears the scene).

Builds:  Pillar_Intact, Pillar_Broken_Tall, Pillar_Broken_Short, Pillar_Stump,
         Pillar_Fragment (fallen), Rubble_Pile, Chest (with hinged Lid + gold),
         Coins_Scatter, Kelp_Tall, Seaweed_Clump
Every asset: pivot at base-center, real-world meters, UVs, game-friendly poly counts.
Seaweed has vertex color R/G/B = 0 at root -> 1 at tip, for sway shaders.
Chest_Lid's pivot sits on the hinge: rotate its local X in Unity to open it.
"""
import bpy, bmesh, os, random
from math import pi, sin, cos, radians
from mathutils import Matrix, Vector

# ----------------------------------------------------------------------
# Settings
# ----------------------------------------------------------------------
EXPORT_FBX = True                       # write one .fbx per asset
EXPORT_DIR = ""                         # "" = folder next to the .blend, or ~/AtlantisKit_FBX
SPACING    = 2.6                        # gap between assets in the preview row

# ----------------------------------------------------------------------
# Scene reset
# ----------------------------------------------------------------------
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for block in (bpy.data.meshes, bpy.data.materials):
    for item in list(block):
        if item.users == 0:
            block.remove(item)

# ----------------------------------------------------------------------
# Materials (simple Principled so they survive FBX -> Unity)
# ----------------------------------------------------------------------
def mat(name, color, metallic=0.0, rough=0.6, double_sided=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1)
    m.use_backface_culling = not double_sided
    return m

M_MARBLE = mat("Atl_Marble",    (0.62, 0.70, 0.66), 0.0, 0.75)
M_MARBLE2= mat("Atl_MarbleDark",(0.40, 0.48, 0.45), 0.0, 0.8)
M_GOLD   = mat("Atl_Gold",      (0.95, 0.70, 0.25), 1.0, 0.3)
M_WOOD   = mat("Atl_Wood",      (0.18, 0.11, 0.06), 0.0, 0.8)
M_IRON   = mat("Atl_Iron",      (0.15, 0.17, 0.16), 1.0, 0.65)
M_KELP   = mat("Atl_Kelp",      (0.10, 0.30, 0.08), 0.0, 0.5)
M_WEED   = mat("Atl_Seaweed",   (0.22, 0.38, 0.12), 0.0, 0.55)

# ----------------------------------------------------------------------
# Mesh builder
# ----------------------------------------------------------------------
class MB:
    """Accumulates verts/faces (with a material slot per face) into one mesh."""
    def __init__(self):
        self.v, self.f, self.m, self.uv = [], [], [], None

    def add(self, verts, faces, slot=0):
        o = len(self.v)
        self.v += [tuple(p) for p in verts]
        self.f += [[i + o for i in face] for face in faces]
        self.m += [slot] * len(faces)

    def box(self, center, size, slot=0, mat3=None):
        cx, cy, cz = center
        hx, hy, hz = (s / 2 for s in size)
        pts = [Vector((x * hx, y * hy, z * hz)) for z in (-1, 1) for (x, y) in
               ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        if mat3:
            pts = [mat3 @ p for p in pts]
        verts = [(p.x + cx, p.y + cy, p.z + cz) for p in pts]
        faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4],
                 [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        self.add(verts, faces, slot)

    def lathe(self, profile, segs=32, slot=0, cap_bottom=True, cap_top=True,
              center=(0, 0, 0), scale=(1, 1, 1), mat3=None):
        """Spin (radius, z) points around Z."""
        cx, cy, cz = center
        verts, faces = [], []
        for r, z in profile:
            for j in range(segs):
                a = 2 * pi * j / segs
                p = Vector((r * cos(a) * scale[0], r * sin(a) * scale[1], z * scale[2]))
                if mat3:
                    p = mat3 @ p
                verts.append((p.x + cx, p.y + cy, p.z + cz))
        n = len(profile)
        for i in range(n - 1):
            for j in range(segs):
                j1 = (j + 1) % segs
                faces.append([i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j])
        if cap_bottom:
            faces.append(list(reversed(range(segs))))
        if cap_top:
            faces.append([(n - 1) * segs + j for j in range(segs)])
        self.add(verts, faces, slot)

    def build(self, name, mats, bevel=0.0, smooth_angle=32, recalc=True, auto_uv=True,
              merge=True):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.v, [], self.f)
        mesh.update()
        for m in mats:
            mesh.materials.append(m)
        for poly, s in zip(mesh.polygons, self.m):
            poly.material_index = s
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        activate(obj)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        if merge:
            bpy.ops.mesh.remove_doubles(threshold=0.0001)
        if recalc:
            bpy.ops.mesh.normals_make_consistent(inside=False)
        if auto_uv:
            bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.02)
        bpy.ops.object.mode_set(mode='OBJECT')
        if self.uv:
            write_uv_and_colors(obj, self.uv)
        if bevel:
            mod = obj.modifiers.new("Bevel", 'BEVEL')
            mod.width = bevel
            mod.segments = 2
            mod.limit_method = 'ANGLE'
            mod.angle_limit = radians(50)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        shade(obj, smooth_angle)
        return obj

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

def write_uv_and_colors(obj, per_vert):
    """per_vert: list of (u, v, sway) for each vertex -> UV map + vertex colors."""
    me = obj.data
    uv = me.uv_layers.new(name="UVMap") if not me.uv_layers else me.uv_layers[0]
    try:
        col = me.color_attributes.new(name="Sway", type='BYTE_COLOR', domain='CORNER')
    except AttributeError:
        col = me.vertex_colors.new(name="Sway")
    for loop in me.loops:
        u, v, s = per_vert[loop.vertex_index]
        uv.data[loop.index].uv = (u, v)
        col.data[loop.index].color = (s, s, s, 1.0)

# ----------------------------------------------------------------------
# Column pieces
# ----------------------------------------------------------------------
def shaft(mb, rng, z0, h, r0, r1, slot=0, flutes=16, segs=64, rings=12,
          jag_top=0.0, jag_bot=0.0, flute_depth=0.028):
    """Fluted Doric-style shaft with entasis. jag_* > 0 makes a broken end."""
    def break_line(amount):
        if amount <= 0:
            return [0.0] * segs
        phase = rng.uniform(0, 2 * pi)
        raw = []
        for j in range(segs):
            a = 2 * pi * j / segs
            slope = 0.55 + 0.45 * cos(a - phase)               # one high side
            raw.append(-amount * (1 - slope) - rng.uniform(0, amount * 0.35))
        # light smoothing so it reads as stone fractures, not noise
        return [(raw[j - 1] + 2 * raw[j] + raw[(j + 1) % segs]) / 4 for j in range(segs)]

    top = break_line(jag_top)
    bot = [-x for x in break_line(jag_bot)]
    verts, faces = [], []
    for i in range(rings + 1):
        t = i / rings
        for j in range(segs):
            a = 2 * pi * j / segs
            r = r0 + (r1 - r0) * t + 0.015 * sin(pi * t)       # entasis bulge
            r -= flute_depth * (0.5 - 0.5 * cos(flutes * a))
            r += rng.uniform(-0.004, 0.004)                     # weathering
            if (i == rings and jag_top) or (i == 0 and jag_bot):
                r *= rng.uniform(0.86, 0.99)                    # chipped rim
            z = z0 + bot[j] + (h + top[j] - bot[j]) * t
            verts.append((r * cos(a), r * sin(a), z))
    for i in range(rings):
        for j in range(segs):
            j1 = (j + 1) % segs
            faces.append([i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j])
    # caps (fan to a slightly sunken center so broken faces aren't flat)
    tc = len(verts)
    verts.append((0, 0, z0 + h + sum(top) / segs - jag_top * 0.15))
    bc = len(verts)
    verts.append((0, 0, z0 + sum(bot) / segs + jag_bot * 0.15))
    for j in range(segs):
        j1 = (j + 1) % segs
        faces.append([rings * segs + j, rings * segs + j1, tc])
        faces.append([j1, j, bc])
    mb.add(verts, faces, slot)

def plinth(mb):
    mb.box((0, 0, 0.10), (1.10, 1.10, 0.20), slot=1)
    mb.box((0, 0, 0.26), (0.96, 0.96, 0.12), slot=0)
    mb.lathe([(0.50, 0.32), (0.53, 0.36), (0.51, 0.41), (0.44, 0.44),
              (0.46, 0.48), (0.42, 0.54)], segs=48)

def capital(mb, z):
    mb.lathe([(0.36, z), (0.40, z + 0.05), (0.47, z + 0.13), (0.54, z + 0.20)], segs=48)
    mb.box((0, 0, z + 0.30), (1.15, 1.15, 0.20), slot=0)

def make_pillar(name, shaft_h, jag=0.0, seed=1, with_capital=False):
    rng = random.Random(seed)
    mb = MB()
    plinth(mb)
    shaft(mb, rng, 0.54, shaft_h, 0.40, 0.35 if with_capital else 0.40 - 0.05 * shaft_h / 2.96,
          jag_top=jag)
    if with_capital:
        capital(mb, 0.54 + shaft_h)
    return mb.build(name, [M_MARBLE, M_MARBLE2], bevel=0.012)

def make_fragment(name, seed=7):
    """A broken drum lying on its side, both ends shattered."""
    rng = random.Random(seed)
    mb = MB()
    shaft(mb, rng, 0.0, 1.2, 0.38, 0.37, jag_top=0.22, jag_bot=0.18, rings=8)
    rot = Matrix.Rotation(radians(90), 3, 'Y') @ Matrix.Rotation(radians(8), 3, 'X')
    mb.v = [tuple(rot @ Vector((x, y, z - 0.6)) + Vector((0, 0, 0.36))) for x, y, z in mb.v]
    return mb.build(name, [M_MARBLE], bevel=0.01)

def rock_verts(rng, radius):
    bm = bmesh.new()
    try:
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=radius)
    except TypeError:
        bmesh.ops.create_icosphere(bm, subdivisions=2, diameter=radius)
    verts = [v.co.copy() for v in bm.verts]
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    sx, sy, sz = rng.uniform(0.8, 1.4), rng.uniform(0.7, 1.2), rng.uniform(0.5, 0.9)
    out = []
    for co in verts:
        k = rng.uniform(0.82, 1.12)
        out.append(Vector((co.x * sx * k, co.y * sy * k, co.z * sz * k)))
    return out, faces

def make_rubble(name, seed=11):
    rng = random.Random(seed)
    mb = MB()
    for i in range(7):
        r = rng.uniform(0.12, 0.32)
        verts, faces = rock_verts(rng, r)
        rot = Matrix.Rotation(rng.uniform(0, 2 * pi), 3, 'Z')
        ang = rng.uniform(0, 2 * pi)
        d = rng.uniform(0, 0.55)
        off = Vector((cos(ang) * d, sin(ang) * d, r * 0.35))
        mb.add([rot @ v + off for v in verts], faces, slot=i % 2)
    return mb.build(name, [M_MARBLE, M_MARBLE2], smooth_angle=20)

# ----------------------------------------------------------------------
# Treasure chest  (body root, lid child pivoting on hinge, gold child)
# ----------------------------------------------------------------------
CH_W, CH_D, CH_H = 0.90, 0.55, 0.42           # width (X), depth (Y), body height

def make_chest(name="Chest"):
    # body
    mb = MB()
    mb.box((0, 0, CH_H / 2), (CH_W, CH_D, CH_H), slot=0)
    for x in (-0.31, 0.31):                                         # iron bands
        mb.box((x, 0, CH_H / 2), (0.06, CH_D + 0.02, CH_H + 0.01), slot=1)
    for sx in (-1, 1):
        for sy in (-1, 1):                                          # corner caps
            mb.box((sx * (CH_W / 2 - 0.02), sy * (CH_D / 2 - 0.02), CH_H / 2),
                   (0.07, 0.07, CH_H + 0.012), slot=1)
            mb.box((sx * 0.36, sy * 0.19, 0.02), (0.1, 0.1, 0.04), slot=1)   # feet
    mb.box((0, -CH_D / 2 - 0.012, CH_H - 0.07), (0.13, 0.025, 0.14), slot=1)  # lock plate
    for k in range(4):                                              # plank seams
        z = 0.085 + k * 0.085
        mb.box((0, 0, z), (CH_W - 0.12, CH_D + 0.004, 0.006), slot=2)
    body = mb.build(name, [M_WOOD, M_IRON, M_IRON], bevel=0.006)

    # lid: half-cylinder along X, built around the hinge (back top edge)
    hinge = Vector((0, CH_D / 2, CH_H))
    r = CH_D / 2
    mb = MB()
    def half_drum(radius, x0, x1, slot, steps=14):
        verts = []
        for x in (x0, x1):
            for k in range(steps + 1):
                a = pi * k / steps
                verts.append((x, -cos(a) * radius, CH_H + sin(a) * radius))
        n = steps + 1
        faces = [[k, k + 1, n + k + 1, n + k] for k in range(steps)]
        faces.append([steps, 0, n, n + steps])                 # flat underside
        faces.append(list(range(n)))                           # end caps
        faces.append(list(reversed(range(n, 2 * n))))
        mb.add([(vx, vy - hinge.y, vz - hinge.z) for vx, vy, vz in verts], faces, slot)
    half_drum(r, -CH_W / 2, CH_W / 2, 0)
    for x in (-0.31, 0.31):
        half_drum(r + 0.012, x - 0.03, x + 0.03, 1)
    for sx in (-1, 1):
        half_drum(r + 0.012, sx * CH_W / 2 - 0.04 * sx - 0.02, sx * CH_W / 2 - 0.04 * sx + 0.02, 1)
    mb.box((0, -r - 0.012 - hinge.y, -0.03), (0.07, 0.022, 0.12), slot=1)  # hasp
    lid = mb.build("Chest_Lid", [M_WOOD, M_IRON], bevel=0.005)
    lid.location = hinge
    lid.parent = body

    # gold inside (visible when lid opens)
    rng = random.Random(3)
    mb = MB()
    mb.lathe([(0.40, 0), (0.36, 0.05), (0.26, 0.10), (0.12, 0.13), (0.02, 0.14)],
             segs=32, cap_top=True, scale=(1.0, 0.6, 1.0), center=(0, 0, CH_H - 0.01))
    for _ in range(26):
        x, y = rng.uniform(-0.34, 0.34), rng.uniform(-0.18, 0.18)
        h = 0.13 * max(0.0, 1 - ((x / 0.4) ** 2 + (y / 0.24) ** 2)) ** 0.5
        coin(mb, rng, (x, y, CH_H + h + 0.004), tilt=25)
    gold = mb.build("Chest_Gold", [M_GOLD], recalc=True)
    gold.parent = body
    return body

def coin(mb, rng, pos, tilt=40, radius=0.026):
    rot = (Matrix.Rotation(rng.uniform(0, 2 * pi), 3, 'Z') @
           Matrix.Rotation(radians(rng.uniform(-tilt, tilt)), 3, 'X'))
    mb.lathe([(radius, -0.003), (radius, 0.003)], segs=12, center=pos, mat3=rot)

def make_coin_scatter(name="Coins_Scatter", seed=5):
    rng = random.Random(seed)
    mb = MB()
    for _ in range(40):
        a, d = rng.uniform(0, 2 * pi), rng.uniform(0, 0.6) ** 1.5
        coin(mb, rng, (cos(a) * d, sin(a) * d, 0.004 + rng.uniform(0, 0.01)), tilt=15)
    mb.lathe([(0.12, 0), (0.09, 0.04), (0.03, 0.06), (0.01, 0.062)], segs=24)  # mini pile
    return mb.build(name, [M_GOLD])

# ----------------------------------------------------------------------
# Seaweed  (double-sided ribbons, sway gradient in vertex color)
# ----------------------------------------------------------------------
def blade(mb, rng, base, height, width, curl, segs=18):
    yaw = rng.uniform(0, 2 * pi)
    lean = rng.uniform(0.05, 0.25) * height * curl
    freq, phase = rng.uniform(1.5, 3.0), rng.uniform(0, 2 * pi)
    twist = rng.uniform(-1.2, 1.2)
    side = Vector((cos(yaw), sin(yaw), 0))
    out = Vector((-sin(yaw), cos(yaw), 0))
    front, data = [], []
    for i in range(segs + 1):
        t = i / segs
        wave = sin(t * freq * pi + phase) * 0.08 * height * t
        center = Vector(base) + out * (lean * t * t + wave) + Vector((0, 0, height * t))
        w = width * (0.25 + 0.75 * sin(pi * min(t * 1.15, 1.0)) ** 0.6) * (1 - 0.7 * t ** 3)
        tw = Matrix.Rotation(twist * t, 3, 'Z')
        s = tw @ side
        for k, sgn in enumerate((-1, 1)):
            front.append(center + s * (w / 2) * sgn)
            data.append((k, t, t))
    n = len(front)
    faces = [[2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2] for i in range(segs)]
    mb.add(front, faces)
    mb.add(front, [[f[0], f[3], f[2], f[1]] for f in faces])   # back side
    mb.uv += data + data

def make_seaweed(name, seed, blades, h_range, w_range, curl, spread, mat_):
    rng = random.Random(seed)
    mb = MB()
    mb.uv = []
    for _ in range(blades):
        a, d = rng.uniform(0, 2 * pi), rng.uniform(0, spread)
        blade(mb, rng, (cos(a) * d, sin(a) * d, 0), rng.uniform(*h_range),
              rng.uniform(*w_range), curl)
    return mb.build(name, [mat_], smooth_angle=60, recalc=False, auto_uv=False, merge=False)

# ----------------------------------------------------------------------
# Build the kit
# ----------------------------------------------------------------------
assets = [
    make_pillar("Pillar_Intact",       2.96, 0.0,  seed=1, with_capital=True),
    make_pillar("Pillar_Broken_Tall",  2.30, 0.45, seed=2),
    make_pillar("Pillar_Broken_Short", 1.10, 0.35, seed=3),
    make_pillar("Pillar_Stump",        0.30, 0.18, seed=4),
    make_fragment("Pillar_Fragment"),
    make_rubble("Rubble_Pile"),
    make_chest("Chest"),
    make_coin_scatter(),
    make_seaweed("Kelp_Tall",     21, 6,  (2.2, 3.8), (0.10, 0.16), 0.6, 0.18, M_KELP),
    make_seaweed("Seaweed_Clump", 22, 12, (0.4, 1.0), (0.04, 0.07), 1.6, 0.12, M_WEED),
]

# ----------------------------------------------------------------------
# Export (each asset at the origin), then lay out a preview row
# ----------------------------------------------------------------------
def select_tree(obj):
    obj.select_set(True)
    for c in obj.children:
        select_tree(c)

if EXPORT_FBX:
    out = EXPORT_DIR or (os.path.join(os.path.dirname(bpy.data.filepath), "AtlantisKit_FBX")
                         if bpy.data.filepath else os.path.expanduser("~/AtlantisKit_FBX"))
    os.makedirs(out, exist_ok=True)
    for a in assets:
        bpy.ops.object.select_all(action='DESELECT')
        select_tree(a)
        bpy.context.view_layer.objects.active = a
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(out, a.name + ".fbx"),
            use_selection=True, object_types={'MESH', 'EMPTY'},
            apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True,
            axis_forward='-Z', axis_up='Y', bake_space_transform=True,
            mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False)
    print("Exported", len(assets), "FBX files to", out)

for i, a in enumerate(assets):
    a.location.x = (i - (len(assets) - 1) / 2) * SPACING

# preview dressing (not exported): sand floor, light, murky water world, camera
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
floor = bpy.context.object
floor.name = "_PreviewSand"
floor.data.materials.append(mat("_Sand", (0.55, 0.50, 0.36), 0.0, 1.0))

world = bpy.context.scene.world or bpy.data.worlds.new("World")
bpy.context.scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get("Background")
if bg:
    bg.inputs["Color"].default_value = (0.01, 0.08, 0.10, 1)
    bg.inputs["Strength"].default_value = 1.0

bpy.ops.object.light_add(type='SUN', location=(0, 0, 10), rotation=(radians(25), 0, radians(20)))
bpy.context.object.data.energy = 3
bpy.context.object.data.color = (0.6, 0.9, 1.0)

bpy.ops.object.camera_add(location=(0, -17, 5.5), rotation=(radians(78), 0, 0))
bpy.context.scene.camera = bpy.context.object
bpy.ops.object.select_all(action='DESELECT')
print("Atlantis kit built:", ", ".join(a.name for a in assets))
