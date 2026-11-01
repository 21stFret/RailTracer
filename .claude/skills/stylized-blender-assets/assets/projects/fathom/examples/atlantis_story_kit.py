"""
ATLANTIS KIT  -  STORY-BY-STORY HOUSE MODULES  (Blender 3.x / 4.x -> Unity)
Paste into Blender's Scripting tab and press Run. Clears the scene, exports one FBX per module
to AtlantisKit_FBX (next to the .blend, or ~/AtlantisKit_FBX).

Every module is 20 x 20 m, pivot at bottom-center, and exactly ONE STORY = 7 m tall, so a house
builder can stack them:  ground story at y=0, upper stories at y = 7, 14, 21 ..., a roof on top.
Modules rotate freely in 90 degree steps.

GROUND STORIES (y = 0 only)
  Story_Ground_Passage  7 x 6.4 m through-passage, side rooms, 2 roof holes
  Story_Ground_Loggia   columned porch, L-shaped passage turning out the side, big room, 2 roof holes
UPPER STORIES (any level >= 1; no floor of their own - the story below is the floor)
  Story_Upper_Hall      open hall, 4 columns, big openings on every side, 2 roof holes
  Story_Upper_Balcony   hall opening onto a balcony cantilevered 3 m over the street, 2 roof holes
  Story_Upper_Gallery   open colonnaded front (grapple + sightlines), 2 roof holes
ROOFS (on top of any story)
  Roof_Flat             walkable roof, railing with step-off gaps, planters
  Roof_Slope            tilted roof over an open-fronted attic (2.9 m front, 6.5 m back)
  Roof_Shrine           mini temple: 6 columns, pitched gable roof, emblem, orb altar

HOLE CONTRACT (why any stack/rotation works): every story's roof slab has two 4 x 4 m holes in
diagonal corner zones centred at (+-6.5, +-6.5). Every upper story and roof keeps ALL FOUR corner
zones free of walls, columns and props, so the holes always open into usable space.
"""
# Generated: Atlantis lib + piece. Paste into Blender's Scripting tab and press Run.
# FBX files export automatically - set EXPORT_DIR below to send them straight into Unity.

# ======================================================================
# ATLANTIS LIB  -  shared building code for the Fathom / Atlantis kit
# Blender 3.x / 4.x. This file is pasted ABOVE a piece script by
# scripts/assemble.py so the user gets one self-contained script to run.
# ======================================================================
import bpy, bmesh, os, random
from math import pi, sin, cos, radians, degrees, atan2, sqrt
from mathutils import Matrix, Vector

# ---------------------------------------------------------------- EXPORT FOLDER (edit me)
# Every run exports one FBX per asset automatically. Leave "" for AtlantisKit_FBX next to the .blend
# (or ~/AtlantisKit_FBX if unsaved), or point it straight into the Unity project so it auto-imports, e.g.
#   EXPORT_DIR = r"C:/Projects/Fathom/Assets/Atlantis/FBX"
EXPORT_DIR = ""

PREVIEW = bool(globals().get("__ATLANTIS_PREVIEW__", False))   # set by scripts/preview.py
PREVIEW_REGISTRY = []

# ---------------------------------------------------------------- grid / gameplay constants
GRID   = 20.0      # building footprint (m); pieces snap on this grid, rotate in 90 deg steps
LEVEL  = 14.0      # one terrace level = 2 stories
STORY  = 7.0
SLAB   = 0.6
WALL_T = 0.8
KIT_COLUMN_H = 3.92   # height of the reference column at scale 1

# ---------------------------------------------------------------- scene + materials
def reset_scene():
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

# Material NAMES are the contract with Unity (the postprocessor swaps by name).
SLOT_MARBLE, SLOT_DARK, SLOT_GOLD, SLOT_ROCK = 0, 1, 2, 3
def kit_materials():
    return [mat("Atl_Marble", (0.62, 0.70, 0.66)),
            mat("Atl_MarbleDark", (0.40, 0.48, 0.45)),
            mat("Atl_Gold", (0.95, 0.70, 0.25), 1.0, 0.3),
            mat("Atl_Rock", (0.42, 0.50, 0.50))]
def collider_material():
    return mat("Atl_Collider", (1.0, 0.0, 1.0))

# ---------------------------------------------------------------- blender helpers
def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

def shade(obj, angle):
    activate(obj)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=radians(angle))        # 4.1+
    except (AttributeError, RuntimeError):
        bpy.ops.object.shade_smooth()
        if hasattr(obj.data, "use_auto_smooth"):                         # <= 4.0
            obj.data.use_auto_smooth = True
            obj.data.auto_smooth_angle = radians(angle)

# ---------------------------------------------------------------- mesh builder
class MB:
    """Accumulates verts/faces (one material slot per face) into a single mesh."""
    def __init__(self):
        self.v, self.f, self.m = [], [], []
        self.pid, self.npieces = [], 0      # piece id per face (used by the z-fighting check)

    def add(self, verts, faces, slot=0):
        o = len(self.v)
        self.v += [tuple(p) for p in verts]
        self.f += [[i + o for i in face] for face in faces]
        self.m += [slot] * len(faces)
        self.pid += [self.npieces] * len(faces)
        self.npieces += 1

    def merge(self, other, mat4):
        o = len(self.v)
        self.v += [tuple(mat4 @ Vector(p)) for p in other.v]
        self.f += [[i + o for i in face] for face in other.f]
        self.m += other.m
        self.pid += [self.npieces + p for p in other.pid]
        self.npieces += other.npieces

    def box(self, center, size, slot=0, rot=None):
        hx, hy, hz = (s / 2 for s in size)
        pts = [Vector((x * hx, y * hy, z * hz)) for z in (-1, 1)
               for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        if rot is not None:
            pts = [rot @ p for p in pts]
        c = Vector(center)
        self.add([p + c for p in pts], [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4],
                                       [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]], slot)

    def rect(self, x0, x1, y0, y1, z0, z1, slot=0):
        if x1 - x0 > 1e-4 and y1 - y0 > 1e-4 and z1 - z0 > 1e-4:
            self.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), slot)

    def extrude(self, profile, axis, a0, a1, slot=0):
        """Closed 2D profile extruded along an axis. axis 'y': (x, z) points; axis 'x': (y, z) points."""
        n = len(profile)
        verts = ([(p, a, q) for a in (a0, a1) for p, q in profile] if axis == 'y'
                 else [(a, p, q) for a in (a0, a1) for p, q in profile])
        faces = [list(range(n)), list(reversed(range(n, 2 * n)))]
        faces += [[i, (i + 1) % n, n + (i + 1) % n, n + i] for i in range(n)]
        self.add(verts, faces, slot)

    def lathe(self, profile, segs=32, slot=0, cap_bottom=True, cap_top=True, closed=False,
              center=(0, 0, 0), mat3=None):
        """Spin (radius, z) points around Z. closed=True joins last->first (rings, annuli)."""
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
        for i in range(n if closed else n - 1):
            i1 = (i + 1) % n
            for j in range(segs):
                j1 = (j + 1) % segs
                faces.append([i * segs + j, i * segs + j1, i1 * segs + j1, i1 * segs + j])
        if not closed:
            if cap_bottom:
                faces.append(list(reversed(range(segs))))
            if cap_top:
                faces.append([(n - 1) * segs + j for j in range(segs)])
        self.add(verts, faces, slot)

    def annulus(self, r_in, r_out, z0, z1, slot=0, segs=96, center=(0, 0, 0)):
        self.lathe([(max(r_in, 0.001), z0), (r_out, z0), (r_out, z1), (max(r_in, 0.001), z1)],
                   segs=segs, slot=slot, closed=True, center=center)

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
        # NO merge-by-distance: touching blocks must stay separate closed pieces. Merged seams turn
        # non-manifold and then bevel/normal recalculation creates slivers + dark seam lines in Unity.
        bpy.ops.mesh.normals_make_consistent(inside=False)
        if uv:
            bpy.ops.uv.smart_project(angle_limit=radians(66), island_margin=0.005)
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

def place(kh, kv, x, y, z, yaw=0.0):
    """Transform for an upright kit piece: kh = width scale, kv = height scale."""
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Diagonal((kh, kh, kv, 1.0)))

def place_lying(kh, klen, x, y, z, yaw=0.0):
    """Transform for a piece lying along X: klen scales length, kh thickness."""
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Diagonal((klen, kh, kh, 1.0)))

# ---------------------------------------------------------------- signature pieces
def shaft(mb, r, z0, h, r0, r1, flutes=16, segs=48, rings=10, jag_top=0.0, jag_bot=0.0, slot=0):
    """Fluted column shaft with entasis. jag_* > 0 gives a broken, chipped end."""
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
    """THE Atlantis column at scale 1 (3.92 m): plinth, base moulding, fluted shaft, capital,
    abacus with gold ring. broken_h = shaft height of a snapped column (0.12 = stump)."""
    r = random.Random(seed)
    mb = MB()
    mb.box((0, 0, 0.10), (1.10, 1.10, 0.20), SLOT_DARK)
    mb.box((0, 0, 0.26), (0.96, 0.96, 0.12), SLOT_MARBLE)
    mb.lathe([(0.50, 0.32), (0.53, 0.36), (0.51, 0.41), (0.44, 0.44), (0.46, 0.48), (0.42, 0.54)], segs=40)
    if broken_h is None:
        shaft(mb, r, 0.54, 2.96, 0.40, 0.35)
        z = 3.50
        mb.lathe([(0.36, z), (0.40, z + 0.05), (0.47, z + 0.13), (0.54, z + 0.20)], segs=40)
        mb.box((0, 0, z + 0.30), (1.15, 1.15, 0.20), SLOT_MARBLE)
        mb.box((0, 0, z + 0.27), (1.18, 1.18, 0.05), SLOT_GOLD)
    else:
        shaft(mb, r, 0.54, broken_h, 0.40, 0.39, jag_top=0.35)
    return mb

def kit_lying_shaft(seed, length=1.2, rings=8):
    """Column piece lying along X, both ends broken, resting on z=0 (scale 1)."""
    r = random.Random(seed)
    mb = MB()
    shaft(mb, r, 0.0, length, 0.39, 0.37, jag_top=0.25, jag_bot=0.2, rings=rings)
    rot = Matrix.Rotation(radians(90), 3, 'Y')
    mb.v = [tuple(rot @ Vector((x, y, z - length / 2)) + Vector((0, 0, 0.39))) for x, y, z in mb.v]
    return mb

def rock(mb, r, center, size, slot=SLOT_ROCK, subdiv=2):
    bm = bmesh.new()
    try:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    except TypeError:
        bmesh.ops.create_icosphere(bm, subdivisions=subdiv, diameter=1.0)
    verts = []
    for v in bm.verts:
        k = r.uniform(0.8, 1.15)
        p = Vector((v.co.x * size[0] * k, v.co.y * size[1] * k, max(v.co.z * size[2] * k, -size[2] * 0.3)))
        verts.append(p + Vector(center) + Vector((0, 0, size[2] * 0.25)))
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    mb.add(verts, faces, slot)

def arc_wall(mb, r_in, r_out, z0, z1, a0, a1, slot=0, seg_len=1.5):
    """Curved wall/beam segment between angles a0..a1 (radians)."""
    segs = max(2, int(abs(a1 - a0) * r_out / seg_len))
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

def pediment(mb, x_half, y0, y1, z0, h, slot=SLOT_MARBLE):
    """Triangular gable block; triangle in XZ, extruded y0..y1."""
    mb.extrude([(-x_half, z0), (x_half, z0), (0, z0 + h)], 'y', y0, y1, slot)

def emblem(mb, center, facing='-y', scale=1.0):
    """Atlantis concentric rings (gold), flat against a face pointing -y or +y."""
    rot = Matrix.Rotation(radians(90 if facing == '-y' else -90), 3, 'X')
    for r_in, r_out in ((0.0, 0.35), (0.65, 0.9), (1.3, 1.5)):
        prof = [(max(r_in * scale, 0.001), 0), (r_out * scale, 0), (r_out * scale, 0.1), (max(r_in * scale, 0.001), 0.1)]
        mb.lathe(prof, segs=40, slot=SLOT_GOLD, closed=True, center=center, mat3=rot)

def orb(mb, radius, center, slot=SLOT_GOLD, segs=32):
    prof = [(radius * sin(t), -radius * cos(t)) for t in [pi * k / 14 for k in range(1, 14)]]
    mb.lathe(prof, segs=segs, slot=slot, center=center)

# ---------------------------------------------------------------- architecture
def wall(mb, axis, c, a0, a1, z0, height, thick, openings=(), slot=0, trim=False):
    """Axis-aligned wall from boxes. openings: (center, width, bottom, top), bottom/top relative to z0.
    trim=True adds sill, jambs, lintel and the stepped gold lintel."""
    def piece(b0, b1, za, zb, s=slot, extra=0.0):
        if b1 - b0 < 1e-4 or zb - za < 1e-4:
            return
        m, L, t = (b0 + b1) / 2, b1 - b0, thick + extra
        if axis == 'x':
            mb.box((m, c, (za + zb) / 2), (L, t, zb - za), s)
        else:
            mb.box((c, m, (za + zb) / 2), (t, L, zb - za), s)
    cur = a0
    for oc, w, b, t in sorted(openings):
        l, r_ = oc - w / 2, oc + w / 2
        piece(cur, l, z0, z0 + height)
        if b > 0:
            piece(l, r_, z0, z0 + b)
        if t < height:
            piece(l, r_, z0 + t, z0 + height)
        if trim:
            if b > 0:
                piece(l - 0.25, r_ + 0.25, z0 + b - 0.15, z0 + b + 0.05, SLOT_DARK, 0.3)
            if t < height:
                piece(l - 0.45, r_ + 0.45, z0 + t - 0.03, z0 + t + 0.4, SLOT_DARK, 0.25)
                piece(l - 0.15, r_ + 0.15, z0 + t + 0.4, z0 + t + 0.62, SLOT_GOLD, 0.22)   # != frieze depth
            piece(l - 0.35, l + 0.03, z0 + b, z0 + t, SLOT_DARK, 0.2)
            piece(r_ - 0.03, r_ + 0.35, z0 + b, z0 + t, SLOT_DARK, 0.2)
        cur = r_
    piece(cur, a1, z0, z0 + height)

def block_walls(mb, x0, x1, y0, y1, z0, h, ops, trim=True, thick=WALL_T):
    """Four walls of a rectangular block. ops: {'front'|'back'|'left'|'right': [openings]}.
    front/back openings are positioned along X, left/right along Y."""
    wall(mb, 'x', y0 + thick / 2, x0, x1, z0, h, thick, ops.get('front', ()), trim=trim)
    wall(mb, 'x', y1 - thick / 2, x0, x1, z0, h, thick, ops.get('back', ()), trim=trim)
    wall(mb, 'y', x0 + thick / 2, y0 + thick, y1 - thick, z0, h, thick, ops.get('left', ()), trim=trim)
    wall(mb, 'y', x1 - thick / 2, y0 + thick, y1 - thick, z0, h, thick, ops.get('right', ()), trim=trim)

def block_band(mb, x0, x1, y0, y1, z0, z1, extra, slot, gaps=None, thick=WALL_T):
    """Course/frieze wrapping a block's walls (proud by extra/2). Side bands are trimmed so
    corners never overlap with coplanar faces. gaps: side -> [(center, width)]."""
    gaps = gaps or {}
    g = lambda s: [(c, w, 0, 99) for c, w in gaps.get(s, ())]
    e, h = extra / 2, z1 - z0
    wall(mb, 'x', y0 + thick / 2, x0 - e, x1 + e, z0, h, thick + extra, g('front'), slot)
    wall(mb, 'x', y1 - thick / 2, x0 - e, x1 + e, z0, h, thick + extra, g('back'), slot)
    wall(mb, 'y', x0 + thick / 2, y0 + thick + e, y1 - thick - e, z0, h, thick + extra, g('left'), slot)
    wall(mb, 'y', x1 - thick / 2, y0 + thick + e, y1 - thick - e, z0, h, thick + extra, g('right'), slot)

def floor_gaps(ops):
    return {s: [(c, w + 0.1) for c, w, b, t in lst if b == 0] for s, lst in ops.items()}

def dress_block(mb, x0, x1, y0, y1, z0, h, ops_ground=None, pilasters=True, base_course=True):
    """Standard Atlantean dressing: dark base course, gold frieze, dark cornice (kept below the
    roof top so walkable roofs never z-fight), corner pilasters."""
    if base_course:
        block_band(mb, x0, x1, y0, y1, z0, z0 + 0.7, 0.3, SLOT_DARK, floor_gaps(ops_ground or {}))
    block_band(mb, x0, x1, y0, y1, z0 + h - 1.6, z0 + h - 1.25, 0.16, SLOT_GOLD)
    block_band(mb, x0, x1, y0, y1, z0 + h - 0.75, z0 + h - 0.08, 0.8, SLOT_DARK)
    if pilasters:
        for (x, y, sx, sy) in ((x0 + 0.6, y0 - 0.15, 1.2, 0.3), (x1 - 0.6, y0 - 0.15, 1.2, 0.3),
                               (x0 + 0.6, y1 + 0.15, 1.2, 0.3), (x1 - 0.6, y1 + 0.15, 1.2, 0.3),
                               (x0 - 0.15, y0 + 0.6, 0.3, 1.2), (x0 - 0.15, y1 - 0.6, 0.3, 1.2),
                               (x1 + 0.15, y0 + 0.6, 0.3, 1.2), (x1 + 0.15, y1 - 0.6, 0.3, 1.2)):
            mb.box((x, y, z0 + 0.7 + (h - 2.3) / 2), (sx, sy, h - 2.3), SLOT_DARK)

def parapet(mb, axis, c, a0, a1, z0, gaps=(), h=1.1, t=0.5):
    """Low railing wall with a capping strip. gaps: [(center, width)] step-off points.
    Convention: 'x' railings run full length, 'y' railings fit between them (their caps are inset
    0.1 so corner caps don't overlap). End railings 0.3 m short of a block's corner pilasters."""
    g = [(cc, w, 0, 99) for cc, w in gaps]
    wall(mb, axis, c, a0, a1, z0, h, t, g, SLOT_DARK)
    inset = 0.1 if axis == 'y' else 0.0
    wall(mb, axis, c, a0 + inset, a1 - inset, z0 + h, 0.15, t + 0.2, g, SLOT_MARBLE)

def slab_with_hole(mb, x0, x1, y0, y1, z0, z1, hole=None, slot=SLOT_DARK):
    if not hole:
        mb.rect(x0, x1, y0, y1, z0, z1, slot)
        return
    hx0, hx1, hy0, hy1 = hole
    mb.rect(x0, x1, y0, hy0, z0, z1, slot)
    mb.rect(x0, x1, hy1, y1, z0, z1, slot)
    mb.rect(x0, hx0, hy0, hy1, z0, z1, slot)
    mb.rect(hx1, x1, hy0, hy1, z0, z1, slot)
    for (a, b_, c_, d) in ((hx0, hx1, hy0 - 0.15, hy0), (hx0, hx1, hy1, hy1 + 0.15)):
        mb.rect(a, b_, c_, d, z1, z1 + 0.04, SLOT_GOLD)

def floor_with_thresholds(mb, half, wall_t, ops, z0=-0.3, z1=0.02):
    """Interior floor of a square block (half-size `half`) + thresholds under floor-level openings.
    Never run a floor out to the outer wall faces (coplanar with the walls)."""
    inner = half - wall_t
    mb.rect(-inner, inner, -inner, inner, z0, z1, SLOT_DARK)
    for side, lst in ops.items():
        for c, w, b, t in lst:
            if b > 0:
                continue
            l, r_ = max(c - w / 2, -inner), min(c + w / 2, inner)
            if side == 'front':   mb.rect(l, r_, -half, -inner, z0, z1, SLOT_DARK)
            elif side == 'back':  mb.rect(l, r_, inner, half, z0, z1, SLOT_DARK)
            elif side == 'left':  mb.rect(-half, -inner, l, r_, z0, z1, SLOT_DARK)
            elif side == 'right': mb.rect(inner, half, l, r_, z0, z1, SLOT_DARK)

def planter(mb, x, y, z0=0.0, sx=2.2, sy=1.2):
    mb.rect(x - sx / 2, x + sx / 2, y - sy / 2, y + sy / 2, z0, z0 + 0.9, SLOT_DARK)
    mb.rect(x - sx / 2 + 0.15, x + sx / 2 - 0.15, y - sy / 2 + 0.15, y + sy / 2 - 0.15, z0 + 0.9, z0 + 0.95, SLOT_ROCK)

def runs(states):
    """Index ranges of consecutive 'ok' entries (length >= 2) - for beams between intact columns."""
    out, start = [], None
    for i, s in enumerate(list(states) + ['x']):
        if s == 'ok' and start is None:
            start = i
        elif s != 'ok' and start is not None:
            if i - start >= 2:
                out.append((start, i - 1))
            start = None
    return out

# ---------------------------------------------------------------- asset container
class Asset:
    """One exported FBX. S = structure (visible + collider), C = detailed columns (render only),
    COL = simple collider proxies, P = props (visible + collider), anchors = grapple points."""
    def __init__(self, name, seed=1):
        self.name = name
        self.rng = random.Random(seed)
        self.S, self.C, self.COL, self.P = MB(), MB(), MB(), MB()
        self.anchors = []
        self.n = 0

    def state(self, p_broken, p_missing, protect=False):
        if protect:
            return 'ok'
        r = self.rng.random()
        return 'missing' if r < p_missing else 'broken' if r < p_missing + p_broken else 'ok'

    def column(self, x, y, z, height, kh, state='ok', yaw=0.0, anchor=True):
        """Kit column scaled to `height` (intact height) and width factor kh. Adds the collider proxy."""
        kv = height / KIT_COLUMN_H
        self.n += 1
        bh = None if state == 'ok' else (0.12 if state == 'missing' else self.rng.uniform(0.5, 2.4))
        self.C.merge(kit_column(1000 + self.n * 7 + len(self.name), bh), place(kh, kv, x, y, z, yaw))
        top = z + (KIT_COLUMN_H if state == 'ok' else 0.54 + bh) * kv
        self.COL.rect(x - 0.55 * kh, x + 0.55 * kh, y - 0.55 * kh, y + 0.55 * kh, z, z + 0.32 * kv)
        self.COL.lathe([(0.43 * kh, z + 0.3 * kv), (0.43 * kh, top - (0.22 * kv if state == 'ok' else 0))],
                       segs=12, center=(x, y, 0))
        if state == 'ok':
            self.COL.rect(x - 0.575 * kh, x + 0.575 * kh, y - 0.575 * kh, y + 0.575 * kh, top - 0.22 * kv, top)
        if anchor and state != 'missing':
            self.anchors.append((x, y, top))
        return top

    def fallen(self, x, y, yaw, kh, klen, length_kit=2.96, z=0.0):
        self.n += 1
        self.P.merge(kit_lying_shaft(2000 + self.n, length_kit, rings=10), place_lying(kh, klen, x, y, z, yaw))

    def drum(self, x, y, kh, z=0.0):
        self.n += 1
        self.P.merge(kit_lying_shaft(3000 + self.n, 1.0, rings=4), place_lying(kh, kh, x, y, z, self.rng.uniform(0, 360)))

    def finish(self, anchors=False):
        if PREVIEW:
            PREVIEW_REGISTRY.append(self)
            return None, []
        mats = kit_materials()
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
        root = bpy.context.object
        root.name = self.name
        objs = [root]
        # Short child names (Unity shows "Base_X > Structure_SOLID"). run() renames them after
        # export so the next asset can reuse the same short names.
        if self.S.v:
            objs.append(self.S.build("Structure_SOLID", mats, bevel=0.0, smooth_angle=30))   # no bevel: blocks abut
        if self.C.v:
            objs.append(self.C.build("Columns", mats, bevel=0.02))
            col = self.COL.build("Columns_COL", [collider_material()], uv=False, smooth_angle=0)
            col.display_type = 'WIRE'
            col.hide_render = True
            objs.append(col)
        if self.P.v:
            objs.append(self.P.build("Props_SOLID", mats, bevel=0.0))
        for o in objs[1:]:
            o.parent = root
        if anchors and self.anchors:
            bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
            ar = bpy.context.object
            ar.name = "GrappleAnchors"
            ar.parent = root
            objs.append(ar)
            for i, (x, y, z) in enumerate(self.anchors):
                bpy.ops.object.empty_add(type='SPHERE', radius=0.5, location=(x, y, z))
                e = bpy.context.object
                e.name = f"Anchor_{i:02d}"
                e.parent = ar
                objs.append(e)
        return root, objs

# ---------------------------------------------------------------- export + run
def export_dir(custom=""):
    if custom or EXPORT_DIR:
        return custom or EXPORT_DIR
    if bpy.data.filepath:
        return os.path.join(os.path.dirname(bpy.data.filepath), "AtlantisKit_FBX")
    return os.path.expanduser("~/AtlantisKit_FBX")

def export_fbx(root, objs, out):
    """Unity-ready FBX: 1 m = 1 unit, Y up, no -90 X rotation."""
    os.makedirs(out, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(out, root.name + ".fbx"),
        use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_scale_options='FBX_SCALE_ALL', apply_unit_scale=True,
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False)

def run(builders, export=True, out_dir="", spacing=None):
    """Build + export each asset in turn (one FBX each), then lay them out in a row for viewing."""
    reset_scene()
    if PREVIEW:
        return [b() for b in builders]
    out = export_dir(out_dir)
    built = []
    for b in builders:
        root, objs = b()
        if export:
            export_fbx(root, objs, out)
            print("Exported", root.name, "->", out)
        for o in objs[1:]:                      # free the short names for the next asset
            o.name = f"{root.name}_{o.name}"
            if o.data is not None:
                o.data.name = o.name
        built.append((root, objs))
    x = 0.0
    for i, (root, objs) in enumerate(built):
        if i:
            x += spacing or 30.0
        root.location.x = x
    bpy.ops.object.light_add(type='SUN', location=(0, 0, 50), rotation=(radians(40), 0, radians(30)))
    bpy.context.object.data.energy = 3
    print("Built:", ", ".join(r.name for r, _ in built))
    return built


# ======================================================================
# PIECE
# ======================================================================


O, T = GRID / 2, WALL_T            # 10, 0.8
I = O - T                          # 9.2 inner face of the outer walls
ST, SL = STORY, SLAB               # 7.0, 0.6
HZ, HH = 6.5, 2.0                  # hole-zone centre offset and half size (4 x 4 m holes)
DIAG_A = [(-HZ, HZ), (HZ, -HZ)]
DIAG_B = [(-HZ, HZ), (HZ, HZ)]

def slab_holes(S, z0, z1, centres, slot=SLOT_DARK):
    """Roof/floor slab over the interior (-I..I) with square holes + gold rims round each hole."""
    holes = [(cx - HH, cx + HH, cy - HH, cy + HH) for cx, cy in centres]
    ys = sorted({-I, I} | {y for h in holes for y in (h[2], h[3])})
    for ya, yb in zip(ys, ys[1:]):
        cur = -I
        for hx0, hx1, hy0, hy1 in sorted(h for h in holes if h[2] < yb and h[3] > ya):
            S.rect(cur, hx0, ya, yb, z0, z1, slot)
            cur = hx1
        S.rect(cur, I, ya, yb, z0, z1, slot)
    for hx0, hx1, hy0, hy1 in holes:
        S.rect(hx0 - 0.15, hx1 + 0.15, hy0 - 0.15, hy0, z1, z1 + 0.04, SLOT_GOLD)
        S.rect(hx0 - 0.15, hx1 + 0.15, hy1, hy1 + 0.15, z1, z1 + 0.04, SLOT_GOLD)
        S.rect(hx0 - 0.15, hx0, hy0, hy1, z1, z1 + 0.04, SLOT_GOLD)
        S.rect(hx1, hx1 + 0.15, hy0, hy1, z1, z1 + 0.04, SLOT_GOLD)

def story_dressing(S, ops, base=True):
    """Ground: base course. Upper: string course at the floor line. Both: gold frieze + cornice."""
    if base:
        block_band(S, -O, O, -O, O, 0, 0.7, 0.3, SLOT_DARK, floor_gaps(ops))
    else:
        block_band(S, -O, O, -O, O, 0.05, 0.55, 0.4, SLOT_DARK, floor_gaps(ops))
    dress_block(S, -O, O, -O, O, 0, ST, base_course=False)

def front_porch_trim(S):
    """For open fronts (loggia / gallery): architrave on the wall line + gold fascia under the cornice."""
    S.rect(-I, I, -O, -I, ST - SL, ST, SLOT_MARBLE)
    S.rect(-I, I, -O - 0.08, -O + 0.3, ST - 1.0, ST - 0.75, SLOT_GOLD)
    for x in (-O, I):                                                 # corner posts
        S.rect(x, x + T, -O, -I, 0, ST, SLOT_MARBLE)

# ======================================================================
# GROUND STORIES
# ======================================================================
def story_ground_passage():
    a = Asset("Story_Ground_Passage")
    S = a.S
    win = [(-6.5, 2.2, 2.0, 4.7), (6.5, 2.2, 2.0, 4.7)]
    ops = {
        'front': [(0, 7.0, 0, ST - SL)] + win,
        'back':  [(0, 7.0, 0, ST - SL)] + win,
        'left':  [(0, 3.5, 0, 4.6), (-5.5, 2.2, 2.0, 4.7), (5.5, 2.2, 2.0, 4.7)],
        'right': [(0, 3.5, 0, 4.6), (-5.5, 2.2, 2.0, 4.7), (5.5, 2.2, 2.0, 4.7)],
    }
    block_walls(S, -O, O, -O, O, 0, ST, ops)
    floor_with_thresholds(S, O, T, ops)
    for x in (-3.8, 3.8):                                             # passage side walls
        wall(S, 'y', x, -I, I, 0, ST - SL, 0.6, [(-4.5, 3.2, 0, 4.4), (4.5, 3.2, 0, 4.4), (0, 2.0, 2.0, 4.2)])
    slab_holes(S, ST - SL, ST, DIAG_A)                                # holes over the side rooms
    story_dressing(S, ops, base=True)
    for y in (-O - 0.15, O + 0.15):                                   # pilasters flanking the passage
        for x in (-4.4, 4.4):
            S.box((x, y, 0.7 + (ST - 2.3) / 2), (1.0, 0.3, ST - 2.3), SLOT_DARK)
    return a.finish()

def story_ground_loggia():
    a = Asset("Story_Ground_Loggia")
    S = a.S
    LF, PX, PY = -6.0, -5.7, 5.7          # recessed front line, passage centre (x), exit centre (y)
    front_ops = [(PX, 7.0, 0, ST - SL), (1.8, 2.4, 2.0, 4.7), (6.5, 2.4, 2.0, 4.7)]
    left_ops = [(-7.9, 2.6, 0, 4.7), (0.0, 2.2, 2.0, 4.7), (6.0, 2.2, 2.0, 4.7)]
    right_ops = [(-7.9, 2.6, 0, 4.7), (-2.0, 2.2, 2.0, 4.7), (PY, 7.0, 0, ST - SL)]
    back_ops = [(-5.7, 2.4, 2.0, 4.7), (5.0, 2.4, 2.0, 4.7)]
    wall(S, 'x', LF + T / 2, -I, I, 0, ST - SL, T, front_ops, trim=True)
    wall(S, 'x', O - T / 2, -O, O, 0, ST, T, back_ops, trim=True)
    wall(S, 'y', -O + T / 2, -O + T, O - T, 0, ST, T, left_ops, trim=True)
    wall(S, 'y', O - T / 2, -O + T, O - T, 0, ST, T, right_ops, trim=True)
    floor_with_thresholds(S, O, T, {'left': left_ops, 'right': right_ops})
    S.rect(-I, I, -O + 0.3, LF, 0.0, 0.12, SLOT_DARK)                  # porch floor
    wall(S, 'y', -1.9, LF + T, 1.6, 0, ST - SL, 0.6, [(-1.5, 3.2, 0, 4.4)])     # room walls
    wall(S, 'x', 1.9, -2.2, I, 0, ST - SL, 0.6, [(4.0, 3.2, 0, 4.4)])
    for x in (-7.5, -2.5, 2.5, 7.5):                                   # porch columns
        a.column(x, -8.4, 0, ST - SL, 1.3, anchor=False)
    slab_holes(S, ST - SL, ST, DIAG_B)                                 # both holes over the passage
    front_porch_trim(S)
    # dressing (front is open, so bands are placed per side)
    wall(S, 'x', LF + T / 2, -I + 0.15, I - 0.15, 0, 0.7, T + 0.3, [(PX, 7.1, 0, 99)], SLOT_DARK)
    wall(S, 'x', O - T / 2, -O - 0.15, O + 0.15, 0, 0.7, T + 0.3, (), SLOT_DARK)
    wall(S, 'y', -O + T / 2, LF - 0.15, O - T - 0.15, 0, 0.7, T + 0.3, (), SLOT_DARK)
    wall(S, 'y', O - T / 2, LF - 0.15, O - T - 0.15, 0, 0.7, T + 0.3, [(PY, 7.1, 0, 99)], SLOT_DARK)
    wall(S, 'x', O - T / 2, -O - 0.08, O + 0.08, ST - 1.6, 0.35, T + 0.16, (), SLOT_GOLD)          # frieze
    for xs in (-1, 1):
        wall(S, 'y', xs * (O - T / 2), -O - 0.08, O - T - 0.08, ST - 1.6, 0.35, T + 0.16, (), SLOT_GOLD)
    block_band(S, -O, O, -O, O, ST - 0.75, ST - 0.08, 0.8, SLOT_DARK)                            # cornice
    for (x, y, sx, sy) in ((-O + 0.6, O + 0.15, 1.2, 0.3), (O - 0.6, O + 0.15, 1.2, 0.3),
                           (-O - 0.15, O - 0.6, 0.3, 1.2), (O + 0.15, O - 0.6, 0.3, 1.2)):
        S.box((x, y, 0.7 + (ST - 2.3) / 2), (sx, sy, ST - 2.3), SLOT_DARK)
    return a.finish()

# ======================================================================
# UPPER STORIES
# ======================================================================
UPPER_SIDE = [(0, 5.0, 0, 4.6), (-5.5, 2.2, 1.2, 4.2), (5.5, 2.2, 1.2, 4.2)]

def upper_shell(S, ops):
    block_walls(S, -O, O, -O, O, 0, ST, ops)
    slab_holes(S, ST - SL, ST, DIAG_A)
    story_dressing(S, ops, base=False)

def story_upper_hall():
    a = Asset("Story_Upper_Hall")
    ops = {'front': [(0, 6.0, 0, 4.7), (-6.5, 2.2, 1.2, 4.2), (6.5, 2.2, 1.2, 4.2)],
           'back':  [(0, 5.0, 0, 4.6), (-6.5, 2.2, 1.2, 4.2), (6.5, 2.2, 1.2, 4.2)],
           'left': UPPER_SIDE, 'right': UPPER_SIDE}
    upper_shell(a.S, ops)
    for x in (-3.0, 3.0):                                              # clear of the corner hole zones
        for y in (-3.0, 3.0):
            a.column(x, y, 0, ST - SL, 1.3)
    return a.finish(anchors=True)

def story_upper_balcony():
    a = Asset("Story_Upper_Balcony")
    S = a.S
    ops = {'front': [(0, 7.0, 0, 4.7), (-6.5, 3.0, 0, 4.6), (6.5, 3.0, 0, 4.6)],
           'back':  [(-5, 2.2, 1.2, 4.2), (5, 2.2, 1.2, 4.2)],
           'left': UPPER_SIDE, 'right': UPPER_SIDE}
    upper_shell(S, ops)
    BX, BY = 5.0, -13.0                                                # balcony at this story's floor level
    S.rect(-BX, BX, BY, -O, -0.6, 0.0, SLOT_DARK)
    S.rect(-BX - 0.1, BX + 0.1, BY - 0.1, BY + 0.2, -0.75, -0.35, SLOT_GOLD)
    for x in (-4.2, 0.0, 4.2):                                         # corbels on the story below
        S.rect(x - 0.35, x + 0.35, BY + 0.4, -O + 0.4, -1.3, -0.6, SLOT_DARK)
        S.rect(x - 0.3, x + 0.3, -O - 1.4, -O + 0.4, -2.2, -1.3, SLOT_DARK)
    S.rect(-BX, BX, BY, BY + 0.35, 0.0, 0.12, SLOT_DARK)               # balustrade
    S.rect(-BX - 0.05, BX + 0.05, BY - 0.05, BY + 0.4, 1.07, 1.25, SLOT_MARBLE)
    for xa, xb in ((-BX, -BX + 0.35), (BX - 0.35, BX)):
        S.rect(xa, xb, BY + 0.35, -O - 0.25, 0.0, 0.12, SLOT_DARK)
        S.rect(xa - 0.05, xb + 0.05, BY + 0.4, -O - 0.25, 1.07, 1.25, SLOT_MARBLE)
    bal = [(0.10, 0.0), (0.14, 0.08), (0.09, 0.25), (0.15, 0.55), (0.08, 0.85), (0.11, 0.95)]
    def balusters(p0, p1, n):
        for k in range(n + 1):
            t = k / n
            S.lathe(bal, segs=8, center=(p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t, 0.12))
    balusters((-BX + 0.2, BY + 0.18), (BX - 0.2, BY + 0.18), 18)
    balusters((-BX + 0.18, BY + 0.7), (-BX + 0.18, -O - 0.5), 5)
    balusters((BX - 0.18, BY + 0.7), (BX - 0.18, -O - 0.5), 5)
    return a.finish()

def story_upper_gallery():
    a = Asset("Story_Upper_Gallery")
    S = a.S
    back = [(0, 5.0, 0, 4.6), (-6.5, 2.2, 1.2, 4.2), (6.5, 2.2, 1.2, 4.2)]
    wall(S, 'x', O - T / 2, -O, O, 0, ST, T, back, trim=True)
    wall(S, 'y', -O + T / 2, -O + T, O - T, 0, ST, T, UPPER_SIDE, trim=True)
    wall(S, 'y', O - T / 2, -O + T, O - T, 0, ST, T, UPPER_SIDE, trim=True)
    slab_holes(S, ST - SL, ST, DIAG_A)
    front_porch_trim(S)
    for x in (-6.0, -2.0, 2.0, 6.0):                                   # open colonnade front
        a.column(x, -9.4, 0, ST - SL, 1.3)
    # dressing per side (front is open)
    wall(S, 'x', O - T / 2, -O - 0.2, O + 0.2, 0.05, 0.5, T + 0.4, [(0, 5.1, 0, 99)], SLOT_DARK)
    for xs in (-1, 1):
        wall(S, 'y', xs * (O - T / 2), -O - 0.2, O - T - 0.2, 0.05, 0.5, T + 0.4, [(0, 5.1, 0, 99)], SLOT_DARK)
    wall(S, 'x', O - T / 2, -O - 0.08, O + 0.08, ST - 1.6, 0.35, T + 0.16, (), SLOT_GOLD)
    for xs in (-1, 1):
        wall(S, 'y', xs * (O - T / 2), -O - 0.08, O - T - 0.08, ST - 1.6, 0.35, T + 0.16, (), SLOT_GOLD)
    block_band(S, -O, O, -O, O, ST - 0.75, ST - 0.08, 0.8, SLOT_DARK)
    for (x, y, sx, sy) in ((-O + 0.6, O + 0.15, 1.2, 0.3), (O - 0.6, O + 0.15, 1.2, 0.3),
                           (-O - 0.15, O - 0.6, 0.3, 1.2), (O + 0.15, O - 0.6, 0.3, 1.2)):
        S.box((x, y, 0.7 + (ST - 2.3) / 2), (sx, sy, ST - 2.3), SLOT_DARK)
    return a.finish(anchors=True)

# ======================================================================
# ROOFS
# ======================================================================
def edge_railing(S, gaps_x=((-5.0, 3.0), (5.0, 3.0)), gaps_y=((0, 3.0),)):
    for sgn in (-1, 1):
        parapet(S, 'x', sgn * (O - 0.25), -O, O, 0, gaps=list(gaps_x))
        parapet(S, 'y', sgn * (O - 0.25), -O + 0.5, O - 0.5, 0, gaps=list(gaps_y))

def roof_flat():
    a = Asset("Roof_Flat")
    S = a.S
    edge_railing(S)
    planter(S, 0, -8.4, sx=4.0, sy=1.2)          # planters sit between the corner hole zones
    planter(S, 0, 8.4, sx=4.0, sy=1.2)
    planter(S, -8.4, -2.5, sx=1.2, sy=2.2)
    planter(S, 8.4, 2.5, sx=1.2, sy=2.2)
    return a.finish()

def roof_slope():
    a = Asset("Roof_Slope")
    S = a.S
    zf, zb = 3.5, 6.5                                                  # roof underside at front / back wall
    zr = lambda y: zf + (y + O) * (zb - zf) / (2 * O)
    wall(S, 'x', O - T / 2, -O, O, 0, zb, T, [(-5.0, 2.2, 1.8, 4.6), (5.0, 2.2, 1.8, 4.6)], trim=True)
    for x in (-O + T / 2, O - T / 2):                                   # trapezoid side walls
        ya, yb = -O, O - T
        S.extrude([(ya, 0), (yb, 0), (yb, zr(yb)), (ya, zr(ya))], 'x', x - T / 2, x + T / 2)
    S.rect(-I, I, -O, -O + T, zf - 0.6, zf, SLOT_MARBLE)                 # front beam
    S.rect(-O - 0.08, O + 0.08, -O - 0.08, -O + T + 0.08, zf - 0.45, zf - 0.25, SLOT_GOLD)
    for x in (-6.0, -2.0, 2.0, 6.0):                                   # short columns under the beam
        a.column(x, -O + T / 2, 0, zf - 0.6, 1.0)
    ya, yb = -O - 1.2, O + 0.6                                          # sloped slab with overhangs
    S.extrude([(ya, zr(ya)), (yb, zr(yb)), (yb, zr(yb) + 0.5), (ya, zr(ya) + 0.5)], 'x', -O - 0.6, O + 0.6, SLOT_DARK)
    S.extrude([(yb - 0.6, zr(yb - 0.6) + 0.5), (yb, zr(yb) + 0.5), (yb, zr(yb) + 0.75), (yb - 0.6, zr(yb - 0.6) + 0.75)],
              'x', -O - 0.55, O + 0.55, SLOT_GOLD)                      # gold ridge trim
    S.rect(-O - 0.15, O + 0.15, O - T - 0.15, O + 0.15, 0, 0.5, SLOT_DARK)   # base course (back)
    return a.finish(anchors=True)

def roof_shrine():
    a = Asset("Roof_Shrine")
    S = a.S
    PX, PY = 3.4, 5.0
    S.rect(-PX, PX, -PY, PY, 0, 0.5, SLOT_DARK)                          # podium (inside the zones' gap)
    S.rect(-2.2, 2.2, -PY - 0.9, -PY, 0, 0.25, SLOT_DARK)
    cx, cys, H = 2.6, (-3.6, 0.0, 3.6), 5.5
    for x in (-cx, cx):
        for y in cys:
            a.column(x, y, 0.5, H, 1.1)
    zt = 0.5 + H
    for x in (-cx, cx):                                                 # architraves
        S.rect(x - 0.65, x + 0.65, -4.4, 4.4, zt, zt + 0.7, SLOT_MARBLE)
        S.rect(x - 0.73, x + 0.73, -4.35, 4.35, zt + 0.35, zt + 0.5, SLOT_GOLD)
    for y in (cys[0], cys[-1]):
        S.rect(-cx + 0.65, cx - 0.65, y - 0.65, y + 0.65, zt, zt + 0.65, SLOT_MARBLE)
    ze, zr_, ex = zt + 0.7, zt + 2.7, 4.0                                # eave, ridge, half span (< 4.5 zone)
    S.extrude([(-ex, ze - 0.1), (0, zr_), (ex, ze - 0.1), (ex, ze + 0.35), (0, zr_ + 0.45), (-ex, ze + 0.35)],
              'y', -5.0, 5.0, SLOT_DARK)
    for y0, y1 in ((-4.6, -4.1), (4.1, 4.6)):
        pediment(S, ex - 0.35, y0, y1, ze, zr_ - ze - 0.15)
    S.rect(-0.25, 0.25, -5.1, 5.1, zr_ + 0.3, zr_ + 0.55, SLOT_GOLD)
    emblem(S, (0, -4.62, ze + 0.7), '-y', 0.45)
    S.rect(-1.2, 1.2, 1.4, 2.8, 0.5, 1.5, SLOT_DARK)                      # altar + orb
    orb(S, 0.7, (0, 2.1, 2.25))
    a.anchors.append((0, 0, zr_ + 0.55))
    edge_railing(S, gaps_x=((0, 4.0),), gaps_y=((0, 4.0),))
    planter(S, 0, -8.4, sx=3.0, sy=1.2)
    planter(S, 0, 8.4, sx=3.0, sy=1.2)
    return a.finish(anchors=True)

run([story_ground_passage, story_ground_loggia, story_upper_hall, story_upper_balcony,
     story_upper_gallery, roof_flat, roof_slope, roof_shrine])
