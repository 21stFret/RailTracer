"""
ATLANTIS KIT  -  COMBAT HOUSE ("Domus")  for Blender 3.x / 4.x  ->  Unity
Paste into the Scripting tab and press Run (clears the scene).

Layout (32 m default, change SIZE below - try 24 / 32 / 40):
  * Stepped marble base, front portico with columns + pediment (Atlantis rings emblem)
  * Open central atrium ringed by columns, with a ring-shaped cover wall + gold orb pedestal
  * 4 side halls open onto the atrium, 4 corner rooms behind doorways
  * One collapsed corner (broken walls, roof gone) + a roof skylight = vertical routes
  * Walkable roof with parapets and gaps
Gameplay sizes: ceilings 8 m, main door 6x6.5 m, other doors 4x5.5 m, windows 2.4x3 m,
cover walls ~1.2 m high. Everything sized for swimming players with weapons.

Exports ONE fbx with a hierarchy:
  House_Atlantis_S32
    House_Structure_SOLID  floors, walls, roof, trims (visible + MeshCollider)
    House_Columns       detailed fluted columns       (render only)
    House_Columns_COL   simple column proxies         (collider, renderer off)
    House_Props_SOLID      cover, altars, rubble      (visible + MeshCollider)
"""
import bpy, bmesh, os, random
from math import pi, sin, cos, radians
from mathutils import Matrix, Vector

# ======================================================================
# Settings
# ======================================================================
SIZE       = 32.0      # footprint in meters (square)
H          = 8.0       # interior clear height
EXPORT_FBX = True
EXPORT_DIR = ""        # "" = AtlantisKit_FBX next to the .blend, or ~/AtlantisKit_FBX

T      = 0.8           # outer wall thickness
FLOOR  = 0.9           # top of the stepped base = interior floor
RT     = 0.8           # roof thickness
MAIN_W, MAIN_H   = 6.0, 6.5
DOOR_W, DOOR_H   = 4.0, 5.5
WIN_W, WIN_B, WIN_T = 2.4, 2.0, 5.0

O  = SIZE / 2                              # outer half-size
A  = round(SIZE * 0.2 * 2) / 2             # atrium column-line half-size (6.5 for 32 m)
AI = A - 1.0                               # roof inner edge (rests on column tops)
WX = (A + O) / 2                           # center of corner rooms
K  = H / 3.92                              # column scale vs. kit pillar
ROOF_Z = FLOOR + H
broken_corner = lambda x, y: x > A + 0.5 and y > A + 0.5
rng = random.Random(42)

# ======================================================================
# Scene reset + materials (names match the Unity toon materials)
# ======================================================================
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for block in (bpy.data.meshes, bpy.data.materials):
    for item in list(block):
        if item.users == 0:
            block.remove(item)

def mat(name, color, metallic=0.0, rough=0.7):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1)
    return m

M_MARBLE = mat("Atl_Marble",     (0.62, 0.70, 0.66))
M_DARK   = mat("Atl_MarbleDark", (0.40, 0.48, 0.45))
M_GOLD   = mat("Atl_Gold",       (0.95, 0.70, 0.25), 1.0, 0.3)
M_ROCK   = mat("Atl_Rock",       (0.42, 0.50, 0.50))
M_COL    = mat("Atl_Collider",   (1.0, 0.0, 1.0))

# ======================================================================
# Mesh builder
# ======================================================================
def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

def shade(obj, angle):
    activate(obj)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=radians(angle))
    except (AttributeError, RuntimeError):
        bpy.ops.object.shade_smooth()
        if hasattr(obj.data, "use_auto_smooth"):
            obj.data.use_auto_smooth = True
            obj.data.auto_smooth_angle = radians(angle)

class MB:
    def __init__(self):
        self.v, self.f, self.m = [], [], []

    def add(self, verts, faces, slot=0):
        o = len(self.v)
        self.v += [tuple(p) for p in verts]
        self.f += [[i + o for i in face] for face in faces]
        self.m += [slot] * len(faces)

    def merge(self, other, mat4=Matrix.Identity(4)):
        o = len(self.v)
        self.v += [tuple(mat4 @ Vector(p)) for p in other.v]
        self.f += [[i + o for i in face] for face in other.f]
        self.m += other.m

    def box(self, center, size, slot=0):
        cx, cy, cz = center
        hx, hy, hz = (s / 2 for s in size)
        verts = [(cx + x * hx, cy + y * hy, cz + z * hz) for z in (-1, 1)
                 for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        self.add(verts, faces, slot)

    def rect(self, x0, x1, y0, y1, z0, z1, slot=0):
        if x1 - x0 > 1e-4 and y1 - y0 > 1e-4 and z1 - z0 > 1e-4:
            self.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), slot)

    def lathe(self, profile, segs=32, slot=0, cap_bottom=True, cap_top=True, closed=False,
              center=(0, 0, 0), mat3=None):
        cx, cy, cz = center
        verts, faces = [], []
        for r, z in profile:
            for j in range(segs):
                a = 2 * pi * j / segs
                p = Vector((r * cos(a), r * sin(a), z))
                if mat3:
                    p = mat3 @ p
                verts.append((p.x + cx, p.y + cy, p.z + cz))
        n = len(profile)
        rows = n if closed else n - 1
        for i in range(rows):
            i1 = (i + 1) % n
            for j in range(segs):
                j1 = (j + 1) % segs
                faces.append([i * segs + j, i * segs + j1, i1 * segs + j1, i1 * segs + j])
        if cap_bottom and not closed:
            faces.append(list(reversed(range(segs))))
        if cap_top and not closed:
            faces.append([(n - 1) * segs + j for j in range(segs)])
        self.add(verts, faces, slot)

    def build(self, name, mats, bevel=0.0, smooth_angle=32, uv=True):
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
        # no merge-by-distance: touching blocks must stay separate closed pieces, otherwise the
        # merged seams become non-manifold and bevel/normals produce slivers and dark lines
        bpy.ops.mesh.normals_make_consistent(inside=False)
        if uv:
            bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.01)
        bpy.ops.object.mode_set(mode='OBJECT')
        if bevel:
            mod = obj.modifiers.new("Bevel", 'BEVEL')
            mod.width = bevel
            mod.segments = 2
            mod.limit_method = 'ANGLE'
            mod.angle_limit = radians(50)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        shade(obj, smooth_angle)
        return obj

# ======================================================================
# Column pieces (same design as the kit pillars, built at kit scale)
# ======================================================================
def shaft(mb, r, z0, h, r0, r1, flutes=16, segs=48, rings=10, jag_top=0.0, jag_bot=0.0, slot=0):
    def break_line(amount):
        if amount <= 0:
            return [0.0] * segs
        phase = r.uniform(0, 2 * pi)
        raw = [-amount * (1 - (0.55 + 0.45 * cos(2 * pi * j / segs - phase))) - r.uniform(0, amount * 0.35)
               for j in range(segs)]
        return [(raw[j - 1] + 2 * raw[j] + raw[(j + 1) % segs]) / 4 for j in range(segs)]
    top, bot = break_line(jag_top), [-x for x in break_line(jag_bot)]
    verts, faces = [], []
    for i in range(rings + 1):
        t = i / rings
        for j in range(segs):
            a = 2 * pi * j / segs
            rad = r0 + (r1 - r0) * t + 0.015 * sin(pi * t) - 0.028 * (0.5 - 0.5 * cos(flutes * a))
            rad += r.uniform(-0.003, 0.003)
            if (i == rings and jag_top) or (i == 0 and jag_bot):
                rad *= r.uniform(0.86, 0.99)
            verts.append((rad * cos(a), rad * sin(a), z0 + bot[j] + (h + top[j] - bot[j]) * t))
    for i in range(rings):
        for j in range(segs):
            j1 = (j + 1) % segs
            faces.append([i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j])
    tc, bc = len(verts), len(verts) + 1
    verts.append((0, 0, z0 + h + sum(top) / segs - jag_top * 0.15))
    verts.append((0, 0, z0 + sum(bot) / segs + jag_bot * 0.15))
    for j in range(segs):
        j1 = (j + 1) % segs
        faces.append([rings * segs + j, rings * segs + j1, tc])
        faces.append([j1, j, bc])
    mb.add(verts, faces, slot)

def kit_column(seed, broken_h=None):
    """Kit-scale column (3.92 m). broken_h = shaft height for a snapped column."""
    r = random.Random(seed)
    mb = MB()
    mb.box((0, 0, 0.10), (1.10, 1.10, 0.20), 1)
    mb.box((0, 0, 0.26), (0.96, 0.96, 0.12), 0)
    mb.lathe([(0.50, 0.32), (0.53, 0.36), (0.51, 0.41), (0.44, 0.44), (0.46, 0.48), (0.42, 0.54)], segs=40)
    if broken_h is None:
        shaft(mb, r, 0.54, 2.96, 0.40, 0.35)
        z = 3.50
        mb.lathe([(0.36, z), (0.40, z + 0.05), (0.47, z + 0.13), (0.54, z + 0.20)], segs=40)
        mb.box((0, 0, z + 0.30), (1.15, 1.15, 0.20), 0)
        mb.box((0, 0, z + 0.27), (1.18, 1.18, 0.05), 2)          # gold ring under the abacus
    else:
        shaft(mb, r, 0.54, broken_h, 0.40, 0.39, jag_top=0.35)
    return mb

def kit_fragment(seed):
    r = random.Random(seed)
    mb = MB()
    shaft(mb, r, 0.0, 1.2, 0.38, 0.37, jag_top=0.22, jag_bot=0.18, rings=8)
    rot = Matrix.Rotation(radians(90), 3, 'Y')
    mb.v = [tuple(rot @ Vector((x, y, z - 0.6)) + Vector((0, 0, 0.36))) for x, y, z in mb.v]
    return mb

def place(mat_scale, x, y, z, yaw=0.0):
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Scale(mat_scale, 4))

def rock(mb, r, center, size, slot=0):
    bm = bmesh.new()
    try:
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    except TypeError:
        bmesh.ops.create_icosphere(bm, subdivisions=2, diameter=1.0)
    verts = []
    for v in bm.verts:
        k = r.uniform(0.8, 1.15)
        p = Vector((v.co.x * size[0] * k, v.co.y * size[1] * k, max(v.co.z * size[2] * k, -size[2] * 0.3)))
        verts.append(p + Vector(center) + Vector((0, 0, size[2] * 0.25)))
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    mb.add(verts, faces, slot)

# ======================================================================
# Walls with openings
# ======================================================================
def wall(mb, axis, c, a0, a1, z0, height, thick, openings=(), slot=0, trim=False,
         broken=None, skip=None):
    """Axis-aligned wall built from boxes. openings: (center, width, bottom, top) along the wall."""
    world = (lambda a: (a, c)) if axis == 'x' else (lambda a: (c, a))

    def piece(b0, b1, za, zb, s=slot, extra=0.0):
        if b1 - b0 < 1e-4 or zb - za < 1e-4:
            return
        m, L, t = (b0 + b1) / 2, b1 - b0, thick + extra
        if axis == 'x':
            mb.box((m, c, (za + zb) / 2), (L, t, zb - za), s)
        else:
            mb.box((c, m, (za + zb) / 2), (t, L, zb - za), s)

    def solid(b0, b1):
        if b1 - b0 < 1e-4:
            return
        if broken is None and skip is None:
            piece(b0, b1, z0, z0 + height)
            return
        n = max(1, int((b1 - b0) / 1.3))
        step = (b1 - b0) / n
        run = None                                   # merge consecutive full-height chunks
        for k in range(n):
            s0, s1 = b0 + k * step, b0 + (k + 1) * step
            p = world((s0 + s1) / 2)
            if skip and skip(*p):
                h = 0
            elif broken and broken(*p):
                h = height * rng.uniform(0.3, 0.8)
            else:
                h = height
            if h == height:
                run = (run[0], s1) if run else (s0, s1)
                continue
            if run:
                piece(run[0], run[1], z0, z0 + height)
                run = None
            if h > 0:
                piece(s0, s1, z0, z0 + h)
        if run:
            piece(run[0], run[1], z0, z0 + height)

    cur = a0
    for oc, w, b, t in sorted(openings):
        l, r_ = oc - w / 2, oc + w / 2
        solid(cur, l)
        p = world(oc)
        gone = (broken and broken(*p)) or (skip and skip(*p))
        if not (skip and skip(*p)):
            if b > 0:
                piece(l, r_, z0, z0 + b)
            if t < height and not gone:
                piece(l, r_, z0 + t, z0 + height)
        if trim and not gone:
            if b > 0:
                piece(l - 0.25, r_ + 0.25, z0 + b - 0.15, z0 + b + 0.05, 1, 0.3)      # sill
            if t < height:
                piece(l - 0.45, r_ + 0.45, z0 + t - 0.03, z0 + t + 0.45, 1, 0.25)     # lintel
            piece(l - 0.35, l + 0.03, z0 + b, z0 + t, 1, 0.2)                          # jambs
            piece(r_ - 0.03, r_ + 0.35, z0 + b, z0 + t, 1, 0.2)
        cur = r_
    solid(cur, a1)

def side_ops(door_w, door_h):
    return [(0.0, door_w, 0.0, door_h), (-WX, WIN_W, WIN_B, WIN_T), (WX, WIN_W, WIN_B, WIN_T)]

# ======================================================================
# STRUCTURE
# ======================================================================
S_MB = MB()      # slots: 0 marble, 1 dark marble, 2 gold

# stepped base + portico platform and steps
for i, grow in enumerate((1.5, 1.0, 0.5)):
    S_MB.rect(-O - grow, O + grow, -O - grow, O + grow, i * 0.3, (i + 1) * 0.3, 1)
S_MB.rect(-11, 11, -O - 7, -O - 0.5, 0, FLOOR, 1)
S_MB.rect(-8, 8, -O - 8, -O - 7, 0, 0.6, 1)
S_MB.rect(-8, 8, -O - 9, -O - 8, 0, 0.3, 1)

# outer walls
wall(S_MB, 'x', -O + T / 2, -O, O, FLOOR, H, T, side_ops(MAIN_W, MAIN_H), trim=True, broken=broken_corner)
wall(S_MB, 'x',  O - T / 2, -O, O, FLOOR, H, T, side_ops(DOOR_W, DOOR_H), trim=True, broken=broken_corner)
wall(S_MB, 'y', -O + T / 2, -O + T, O - T, FLOOR, H, T, side_ops(DOOR_W, DOOR_H), trim=True, broken=broken_corner)
wall(S_MB, 'y',  O - T / 2, -O + T, O - T, FLOOR, H, T, side_ops(DOOR_W, DOOR_H), trim=True, broken=broken_corner)

# partitions: corner rooms, each with a doorway into the neighbouring halls
PW = (A + O - T) / 2
for sx in (-1, 1):
    for sy in (-1, 1):
        lo, hi = sorted((sx * A, sx * (O - T)))
        wall(S_MB, 'x', sy * A, lo, hi, FLOOR, H, 0.6, [(sx * PW, DOOR_W, 0, DOOR_H)], trim=True)
        lo, hi = sorted((sy * A, sy * (O - T)))
        wall(S_MB, 'y', sx * A, lo, hi, FLOOR, H, 0.6, [(sy * PW, DOOR_W, 0, DOOR_H)], trim=True)

# roof slab (open atrium, collapsed corner, skylight over the left hall)
z0, z1 = ROOF_Z, ROOF_Z + RT
S_MB.rect(-O, O, -O, -AI, z0, z1)                                   # front strip
S_MB.rect(-O, A + 1, AI, O, z0, z1)                                 # back strip (minus corner)
S_MB.rect(A + 1, O, AI, A + 1, z0, z1)
S_MB.rect(AI, O, -AI, AI, z0, z1)                                   # right strip
S_MB.rect(-O, -AI, -AI, -1.5, z0, z1)                               # left strip around skylight
S_MB.rect(-O, -AI, 1.5, AI, z0, z1)
S_MB.rect(-O, -O + 3, -1.5, 1.5, z0, z1)
S_MB.rect(-AI - 2, -AI, -1.5, 1.5, z0, z1)
S_MB.rect(-11, 11, -O - 7, -O, z0, z1)                              # portico roof

no_roof = lambda x, y: x > A + 1 and y > A + 1

# gold frieze + dark cornice wrapping the outer walls (skipped where collapsed)
for zf0, zf1, extra, slot in ((ROOF_Z - 1.1, ROOF_Z - 0.65, 0.16, 2),
                              (ROOF_Z - 0.25, z1 + 0.05, 0.7, 1)):
    half = extra / 2
    for sgn in (-1, 1):
        wall(S_MB, 'x', sgn * (O - T / 2), -O - half, O + half, zf0, zf1 - zf0, T + extra, slot=slot, skip=no_roof)
        wall(S_MB, 'y', sgn * (O - T / 2), -O + T + half, O - T - half, zf0 + 0.002, zf1 - zf0, T + extra,
             slot=slot, skip=no_roof)

# exterior pilasters
for sgn in (-1, 1):
    face = sgn * (O + 0.15)
    for a in (-O + 0.6, -MAIN_W / 2 - 1.0, MAIN_W / 2 + 1.0, O - 0.6, -A, A):
        for (x, y, sx, sy) in ((a, face, 1.0, 0.3), (face, a, 0.3, 1.0)):
            if not broken_corner(x, y):
                S_MB.box((x, y, FLOOR + (H - 1.1) / 2), (sx, sy, H - 1.1), 1)

# roof parapets with gaps (outer edge) and a low rail around the atrium opening
pz = z1
gaps = [(-SIZE * 0.28, 2.5, 0, 1.2), (0.0, 3.0, 0, 1.2), (SIZE * 0.28, 2.5, 0, 1.2)]
for sgn in (-1, 1):
    wall(S_MB, 'x', sgn * (O - 0.25), -O, O, pz, 1.2, 0.5, gaps, slot=1, skip=no_roof)
    wall(S_MB, 'y', sgn * (O - 0.25), -O + 0.5, O - 0.5, pz, 1.2, 0.5, gaps, slot=1, skip=no_roof)
    wall(S_MB, 'x', sgn * (AI + 0.25), -AI - 0.5, AI + 0.5, pz, 0.8, 0.5, [(0, 3.0, 0, 0.8)], slot=1)
    wall(S_MB, 'y', sgn * (AI + 0.25), -AI, AI, pz, 0.8, 0.5, [(0, 3.0, 0, 0.8)], slot=1)

# pediment over the portico, with the Atlantis concentric-rings emblem
pw, ph, y0, y1 = 22.0, 3.0, -O - 7, -O
ped = [(-pw / 2, y0, z1), (pw / 2, y0, z1), (0, y0, z1 + ph),
       (-pw / 2, y1, z1), (pw / 2, y1, z1), (0, y1, z1 + ph)]
S_MB.add(ped, [[0, 1, 2], [5, 4, 3], [0, 3, 4, 1], [0, 2, 5, 3], [1, 4, 5, 2]], 0)
emblem_rot = Matrix.Rotation(radians(90), 3, 'X')
for r_in, r_out in ((0.0, 0.22), (0.4, 0.58), (0.85, 1.0), (1.25, 1.38)):
    prof = [(max(r_in, 0.001), 0), (r_out, 0), (r_out, 0.08), (max(r_in, 0.001), 0.08)]
    S_MB.lathe(prof, segs=40, slot=2, closed=True, center=(0, y0 + 0.02, z1 + 1.05), mat3=emblem_rot)

# floor inlay rings in the atrium
for r_in, r_out in ((1.9, 2.1), (A - 1.6, A - 1.35)):
    S_MB.lathe([(r_in, 0), (r_out, 0), (r_out, 0.03), (r_in, 0.03)], segs=64, slot=2,
               closed=True, center=(0, 0, FLOOR - 0.01))

structure = S_MB.build("House_Structure_SOLID", [M_MARBLE, M_DARK, M_GOLD], bevel=0.0, smooth_angle=30)

# ======================================================================
# COLUMNS (+ simple collision proxies)
# ======================================================================
C_MB, COL_MB = MB(), MB()
n_side = max(3, round(2 * A / 4.3) + 1)
ring_pts = set()
for i in range(n_side):
    t = -A + 2 * A * i / (n_side - 1)
    ring_pts |= {(round(t, 3), -A), (round(t, 3), A), (-A, round(t, 3)), (A, round(t, 3))}
columns = [(x, y, None) for x, y in sorted(ring_pts)]
columns = [(x, y, 1.1 if (x, y) == (A, A) else None) for x, y, _ in columns]     # one snapped column
columns += [(x, -O - 4.5, None) for x in (-9, -3.5, 3.5, 9)]                      # portico

for idx, (x, y, broken_h) in enumerate(columns):
    C_MB.merge(kit_column(100 + idx, broken_h), place(K, x, y, FLOOR))
    top = FLOOR + (0.54 + broken_h) * K if broken_h else ROOF_Z
    COL_MB.rect(x - 0.55 * K, x + 0.55 * K, y - 0.55 * K, y + 0.55 * K, FLOOR, FLOOR + 0.54 * K)
    COL_MB.lathe([(0.42 * K, FLOOR), (0.42 * K, top)], segs=10, center=(x, y, 0))

cols = C_MB.build("House_Columns", [M_MARBLE, M_DARK, M_GOLD], bevel=0.02)
cols_col = COL_MB.build("House_Columns_COL", [M_COL], uv=False, smooth_angle=0)

# ======================================================================
# PROPS: cover, altars, fallen columns, rubble
# ======================================================================
P_MB = MB()      # slots: 0 marble, 1 dark marble, 2 gold, 3 rock

# atrium: ring-shaped cover wall with 4 gaps, pedestal + gold orb
def arc_wall(mb, r_in, r_out, z0, z1, a0, a1, segs, slot):
    verts, faces = [], []
    for k in range(segs + 1):
        a = a0 + (a1 - a0) * k / segs
        for r_, z_ in ((r_in, z0), (r_out, z0), (r_out, z1), (r_in, z1)):
            verts.append((r_ * cos(a), r_ * sin(a), z_))
    for k in range(segs):
        for e in range(4):
            e1 = (e + 1) % 4
            faces.append([k * 4 + e, k * 4 + e1, (k + 1) * 4 + e1, (k + 1) * 4 + e])
    faces.append([3, 2, 1, 0])
    faces.append([segs * 4 + i for i in range(4)])
    mb.add(verts, faces, slot)

ring_r = A * 0.55
for q in range(4):
    a0 = radians(18 + q * 90)
    arc_wall(P_MB, ring_r - 0.3, ring_r + 0.3, FLOOR - 0.02, FLOOR + 1.2, a0, a0 + radians(54), 10, 1)
    arc_wall(P_MB, ring_r - 0.35, ring_r + 0.35, FLOOR + 1.2, FLOOR + 1.32, a0, a0 + radians(54), 10, 2)
P_MB.lathe([(1.3, 0), (1.3, 0.3), (1.0, 0.4), (0.8, 1.4), (1.1, 1.6), (1.1, 1.8)],
           segs=32, slot=1, center=(0, 0, FLOOR - 0.01))
orb = [(0.8 * sin(t), -0.8 * cos(t)) for t in [pi * k / 12 for k in range(1, 12)]]
P_MB.lathe(orb, segs=32, slot=2, center=(0, 0, FLOOR + 2.55))

# side halls: low cover walls (~1.2 m) and fallen columns
def low_wall(x, y, length, along='x'):
    sx, sy = (length, 0.7) if along == 'x' else (0.7, length)
    P_MB.box((x, y, FLOOR + 0.55), (sx, sy, 1.1), 1)
    P_MB.box((x, y, FLOOR + 1.15), (sx + 0.15, sy + 0.15, 0.12), 0)

hall = (A + O - T) / 2
low_wall(-4.5, -hall, 3.5)
low_wall(4.5, -hall + 1.5, 3.5)
low_wall(0.0, hall, 5.0)
low_wall(-hall, -3.5, 3.5, 'y')
low_wall(-hall + 1.2, 3.5, 3.5, 'y')
low_wall(hall + 1.0, -4.0, 3.0, 'y')
P_MB.merge(kit_fragment(7), place(K * 0.9, hall - 0.5, 1.5, FLOOR, 75))
P_MB.merge(kit_fragment(8), place(K * 0.9, A - 2.1, A - 1.9, FLOOR, 45))

# corner rooms: altars (cover) - rubble in the collapsed corner instead
for sx in (-1, 1):
    for sy in (-1, 1):
        x, y = sx * WX, sy * WX
        if broken_corner(x, y):
            for k in range(7):
                s = rng.uniform(0.5, 1.2)
                rock(P_MB, rng, (x + rng.uniform(-3, 3), y + rng.uniform(-3, 3), FLOOR),
                     (s * rng.uniform(1.0, 1.6), s, s * 0.8), 3)
        else:
            P_MB.box((x, y, FLOOR + 0.6), (2.8, 1.4, 1.2), 1)
            P_MB.box((x, y, FLOOR + 1.25), (3.0, 1.6, 0.1), 2)

props = P_MB.build("House_Props_SOLID", [M_MARBLE, M_DARK, M_GOLD, M_ROCK], bevel=0.0)

# ======================================================================
# Hierarchy + export
# ======================================================================
bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
root = bpy.context.object
root.name = f"House_Atlantis_S{int(SIZE)}"
for o in (structure, cols, cols_col, props):
    o.parent = root
cols_col.display_type = 'WIRE'
cols_col.hide_render = True

if EXPORT_FBX:
    out = EXPORT_DIR or (os.path.join(os.path.dirname(bpy.data.filepath), "AtlantisKit_FBX")
                         if bpy.data.filepath else os.path.expanduser("~/AtlantisKit_FBX"))
    os.makedirs(out, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in (root, structure, cols, cols_col, props):
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(out, root.name + ".fbx"),
        use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True,
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', add_leaf_bones=False)
    print("Exported", root.name, "to", out)

# preview dressing (not exported)
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
bpy.context.object.name = "_PreviewSand"
bpy.context.object.data.materials.append(mat("_Sand", (0.55, 0.50, 0.36)))
bpy.ops.mesh.primitive_cylinder_add(radius=0.3, depth=1.8, location=(0, -O - 10, 0.9))
bpy.context.object.name = "_ScaleRef_1.8m"
bpy.ops.object.light_add(type='SUN', location=(0, 0, 30), rotation=(radians(40), 0, radians(30)))
bpy.context.object.data.energy = 3
bpy.ops.object.camera_add(location=(28, -52, 26), rotation=(radians(66), 0, radians(28)))
bpy.context.scene.camera = bpy.context.object
bpy.ops.object.select_all(action='DESELECT')
print(f"House built: {SIZE} m footprint, atrium {2 * A} m, {len(columns)} columns.")
