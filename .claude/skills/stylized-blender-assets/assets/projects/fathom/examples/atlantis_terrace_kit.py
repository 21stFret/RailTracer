"""
ATLANTIS KIT  -  TERRACE BUILDINGS (stackable)   Blender 3.x / 4.x  ->  Unity
Paste into the Scripting tab and press Run (clears the scene). Exports 7 FBX files.

Grid: every piece is 20 x 20 m with its pivot at the bottom center, so pieces snap on a
20 m grid and rotate in 90 degree steps. One terrace LEVEL = 14 m = two 7 m stories.

BASES (2 stories, 14 m, flat roof at exactly 14 m):
  Base_Passage   straight 7 x 6.4 m passage through the middle, side rooms, open upper hall
  Base_Loggia    columned front porch, L-shaped 7 m passage that turns and exits the side,
                 big room, open upper hall
TOPS (sit on any base at +14 m; rotate freely):
  Top_LTerrace   1-story block in one corner, L-shaped terrace = steps to the next level
  Top_Slope      tilted single-slope roof block with a front terrace
  Top_Balcony    block + terrace + balcony cantilevered 3 m out over the street
  Top_Shrine     open columned shrine with a pitched gable roof on a terrace
  Top_Parapet    just a railing with gaps + planters, for a plain flat-roof terrace

Pipeline: Atl_* materials, *_SOLID = visible + collider, *_COL = invisible collider.
In Unity: place a top at the same X/Z as its base, Y + 14 (times your scale).
"""
import bpy, bmesh, os, random
from math import pi, sin, cos, radians
from mathutils import Matrix, Vector

EXPORT_FBX = True
EXPORT_DIR = ""            # "" = AtlantisKit_FBX next to the .blend, or ~/AtlantisKit_FBX

O, T = 10.0, 0.8           # half footprint, outer wall thickness
I = O - T                  # inner face of outer walls
ST = 7.0                   # story height
LV = 14.0                  # level height (2 stories)
SL = 0.6                   # slab thickness

# ======================================================================
# Scene reset + materials
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
MATS = [M_MARBLE, M_DARK, M_GOLD, M_ROCK]          # slots 0..3

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

    def merge(self, other, mat4):
        o = len(self.v)
        self.v += [tuple(mat4 @ Vector(p)) for p in other.v]
        self.f += [[i + o for i in face] for face in other.f]
        self.m += other.m

    def box(self, center, size, slot=0):
        cx, cy, cz = center
        hx, hy, hz = (s / 2 for s in size)
        verts = [(cx + x * hx, cy + y * hy, cz + z * hz) for z in (-1, 1)
                 for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        self.add(verts, [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]], slot)

    def rect(self, x0, x1, y0, y1, z0, z1, slot=0):
        if x1 - x0 > 1e-4 and y1 - y0 > 1e-4 and z1 - z0 > 1e-4:
            self.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), slot)

    def extrude(self, profile, axis, a0, a1, slot=0):
        """Extrude a closed 2D profile. axis 'y': profile is (x, z); axis 'x': profile is (y, z)."""
        n = len(profile)
        if axis == 'y':
            verts = [(p, a, q) for a in (a0, a1) for p, q in profile]
        else:
            verts = [(a, p, q) for a in (a0, a1) for p, q in profile]
        faces = [list(range(n)), list(reversed(range(n, 2 * n)))]
        faces += [[i, (i + 1) % n, n + (i + 1) % n, n + i] for i in range(n)]
        self.add(verts, faces, slot)

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

# ======================================================================
# Kit column (same design as the pillars)
# ======================================================================
def shaft(mb, r, z0, h, r0, r1, flutes=16, segs=40, rings=8):
    verts, faces = [], []
    for i in range(rings + 1):
        t = i / rings
        for j in range(segs):
            a = 2 * pi * j / segs
            rad = r0 + (r1 - r0) * t + 0.015 * sin(pi * t) - 0.028 * (0.5 - 0.5 * cos(flutes * a))
            rad += r.uniform(-0.003, 0.003)
            verts.append((rad * cos(a), rad * sin(a), z0 + h * t))
    for i in range(rings):
        for j in range(segs):
            j1 = (j + 1) % segs
            faces.append([i * segs + j, i * segs + j1, (i + 1) * segs + j1, (i + 1) * segs + j])
    mb.add(verts, faces, 0)

def kit_column(seed):
    r = random.Random(seed)
    mb = MB()
    mb.box((0, 0, 0.10), (1.10, 1.10, 0.20), 1)
    mb.box((0, 0, 0.26), (0.96, 0.96, 0.12), 0)
    mb.lathe([(0.50, 0.32), (0.53, 0.36), (0.51, 0.41), (0.44, 0.44), (0.46, 0.48), (0.42, 0.54)], segs=32)
    shaft(mb, r, 0.54, 2.96, 0.40, 0.35)
    z = 3.50
    mb.lathe([(0.36, z), (0.40, z + 0.05), (0.47, z + 0.13), (0.54, z + 0.20)], segs=32)
    mb.box((0, 0, z + 0.30), (1.15, 1.15, 0.20), 0)
    mb.box((0, 0, z + 0.27), (1.18, 1.18, 0.05), 2)
    return mb

def emblem(mb, center, facing='-y', scale=1.0):
    rot = Matrix.Rotation(radians(90 if facing == '-y' else -90), 3, 'X')
    for r_in, r_out in ((0.0, 0.35), (0.65, 0.9), (1.3, 1.5)):
        prof = [(max(r_in * scale, 0.001), 0), (r_out * scale, 0), (r_out * scale, 0.1), (max(r_in * scale, 0.001), 0.1)]
        mb.lathe(prof, segs=40, slot=2, closed=True, center=center, mat3=rot)

# ======================================================================
# Walls, bands, parapets
# ======================================================================
def wall(mb, axis, c, a0, a1, z0, height, thick, openings=(), slot=0, trim=False):
    """Axis-aligned wall from boxes. openings: (center, width, bottom, top) relative to z0."""
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
                piece(l - 0.25, r_ + 0.25, z0 + b - 0.15, z0 + b + 0.05, 1, 0.3)             # sill
            if t < height:
                piece(l - 0.45, r_ + 0.45, z0 + t - 0.03, z0 + t + 0.4, 1, 0.25)            # lintel
                piece(l - 0.15, r_ + 0.15, z0 + t + 0.4, z0 + t + 0.62, 2, 0.22)            # stepped gold
            piece(l - 0.35, l + 0.03, z0 + b, z0 + t, 1, 0.2)                                # jambs
            piece(r_ - 0.03, r_ + 0.35, z0 + b, z0 + t, 1, 0.2)
        cur = r_
    piece(cur, a1, z0, z0 + height)

def block_walls(mb, x0, x1, y0, y1, z0, h, ops, trim=True, thick=T):
    wall(mb, 'x', y0 + thick / 2, x0, x1, z0, h, thick, ops.get('front', ()), trim=trim)
    wall(mb, 'x', y1 - thick / 2, x0, x1, z0, h, thick, ops.get('back', ()), trim=trim)
    wall(mb, 'y', x0 + thick / 2, y0 + thick, y1 - thick, z0, h, thick, ops.get('left', ()), trim=trim)
    wall(mb, 'y', x1 - thick / 2, y0 + thick, y1 - thick, z0, h, thick, ops.get('right', ()), trim=trim)

def block_band(mb, x0, x1, y0, y1, z0, z1, extra, slot, gaps=None, thick=T, sides='fblr'):
    """A course/frieze wrapping a block's walls, proud by extra/2 each side. gaps: side -> [(center, width)]."""
    gaps = gaps or {}
    g = lambda s: [(c, w, 0, 99) for c, w in gaps.get(s, ())]
    e, h = extra / 2, z1 - z0
    if 'f' in sides: wall(mb, 'x', y0 + thick / 2, x0 - e, x1 + e, z0, h, thick + extra, g('front'), slot)
    if 'b' in sides: wall(mb, 'x', y1 - thick / 2, x0 - e, x1 + e, z0, h, thick + extra, g('back'), slot)
    if 'l' in sides: wall(mb, 'y', x0 + thick / 2, y0 + thick + e, y1 - thick - e, z0, h, thick + extra, g('left'), slot)
    if 'r' in sides: wall(mb, 'y', x1 - thick / 2, y0 + thick + e, y1 - thick - e, z0, h, thick + extra, g('right'), slot)

def floor_gaps(ops):
    """Base-course gaps wherever an opening reaches the floor."""
    return {s: [(c, w + 0.1) for c, w, b, t in lst if b == 0] for s, lst in ops.items()}

def dress_block(mb, x0, x1, y0, y1, z0, h, ops_ground, pilasters=True, base_course=True):
    """Standard Atlantean dressing for a block: base course, gold frieze, cornice, pilasters."""
    if base_course:
        block_band(mb, x0, x1, y0, y1, z0, z0 + 0.7, 0.3, 1, floor_gaps(ops_ground))
    block_band(mb, x0, x1, y0, y1, z0 + h - 1.6, z0 + h - 1.25, 0.16, 2)
    block_band(mb, x0, x1, y0, y1, z0 + h - 0.75, z0 + h - 0.08, 0.8, 1)
    if pilasters:
        for (x, y, sx, sy) in ((x0 + 0.6, y0 - 0.15, 1.2, 0.3), (x1 - 0.6, y0 - 0.15, 1.2, 0.3),
                               (x0 + 0.6, y1 + 0.15, 1.2, 0.3), (x1 - 0.6, y1 + 0.15, 1.2, 0.3),
                               (x0 - 0.15, y0 + 0.6, 0.3, 1.2), (x0 - 0.15, y1 - 0.6, 0.3, 1.2),
                               (x1 + 0.15, y0 + 0.6, 0.3, 1.2), (x1 + 0.15, y1 - 0.6, 0.3, 1.2)):
            mb.box((x, y, z0 + 0.7 + (h - 2.3) / 2), (sx, sy, h - 2.3), 1)

def parapet(mb, axis, c, a0, a1, z0, gaps=(), h=1.1, t=0.5):
    g = [(cc, w, 0, 99) for cc, w in gaps]
    wall(mb, axis, c, a0, a1, z0, h, t, g, 1)
    inset = 0.1 if axis == 'y' else 0.0          # side caps stop short of the front/back caps
    wall(mb, axis, c, a0 + inset, a1 - inset, z0 + h, 0.15, t + 0.2, g, 0)

def slab_with_hole(mb, x0, x1, y0, y1, z0, z1, hole=None, slot=1):
    if not hole:
        mb.rect(x0, x1, y0, y1, z0, z1, slot)
        return
    hx0, hx1, hy0, hy1 = hole
    mb.rect(x0, x1, y0, hy0, z0, z1, slot)
    mb.rect(x0, x1, hy1, y1, z0, z1, slot)
    mb.rect(x0, hx0, hy0, hy1, z0, z1, slot)
    mb.rect(hx1, x1, hy0, hy1, z0, z1, slot)
    for (a, b_, c_, d) in ((hx0, hx1, hy0 - 0.15, hy0), (hx0, hx1, hy1, hy1 + 0.15)):   # gold rim
        mb.rect(a, b_, c_, d, z1, z1 + 0.04, 2)

def floor_with_thresholds(mb, ops, z0=-0.3, z1=0.02):
    """Interior floor plus a threshold slab under every floor-level opening of the outer walls."""
    mb.rect(-I, I, -I, I, z0, z1, 1)
    for side, lst in ops.items():
        for c, w, b, t in lst:
            if b > 0:
                continue
            l, r_ = c - w / 2, c + w / 2
            if side == 'front':   mb.rect(l, r_, -O, -I, z0, z1, 1)
            elif side == 'back':  mb.rect(l, r_, I, O, z0, z1, 1)
            elif side == 'left':  mb.rect(-O, -I, max(l, -I), min(r_, I), z0, z1, 1)
            elif side == 'right': mb.rect(I, O, max(l, -I), min(r_, I), z0, z1, 1)

def planter(mb, x, y, z0=0.0, sx=2.2, sy=1.2):
    mb.rect(x - sx / 2, x + sx / 2, y - sy / 2, y + sy / 2, z0, z0 + 0.9, 1)
    mb.rect(x - sx / 2 + 0.15, x + sx / 2 - 0.15, y - sy / 2 + 0.15, y + sy / 2 - 0.15, z0 + 0.9, z0 + 0.95, 3)

# ======================================================================
# Asset container
# ======================================================================
class Asset:
    def __init__(self, name):
        self.name = name
        self.S, self.C, self.COL = MB(), MB(), MB()
        self.n = 0

    def column(self, x, y, z, height, kh):
        kv = height / 3.92
        self.n += 1
        m = (Matrix.Translation((x, y, z)) @ Matrix.Diagonal((kh, kh, kv, 1.0)))
        self.C.merge(kit_column(500 + self.n * 13 + len(self.name)), m)
        self.COL.rect(x - 0.55 * kh, x + 0.55 * kh, y - 0.55 * kh, y + 0.55 * kh, z, z + 0.32 * kv)
        self.COL.lathe([(0.43 * kh, z + 0.3 * kv), (0.43 * kh, z + height - 0.22 * kv)], segs=12, center=(x, y, 0))
        self.COL.rect(x - 0.575 * kh, x + 0.575 * kh, y - 0.575 * kh, y + 0.575 * kh, z + height - 0.22 * kv, z + height)

    def finish(self):
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
        root = bpy.context.object
        root.name = self.name
        objs = [root, self.S.build("Structure_SOLID", MATS, bevel=0.0, smooth_angle=30)]
        if self.C.v:
            objs.append(self.C.build("Columns", MATS, bevel=0.02))
            col = self.COL.build("Columns_COL", [M_COL], uv=False, smooth_angle=0)
            col.display_type = 'WIRE'
            col.hide_render = True
            objs.append(col)
        for o in objs[1:]:
            o.parent = root
        return root, objs

# ======================================================================
# BASE 1: PASSAGE
# ======================================================================
def base_passage():
    a = Asset("Base_Passage")
    S = a.S
    side_win = [(-6.5, 2.2, 2.0, 4.8), (6.5, 2.2, 2.0, 4.8)]
    ground = {
        'front': [(0, 7.0, 0, 6.4)] + side_win,
        'back':  [(0, 7.0, 0, 6.4)] + side_win,
        'left':  [(0, 3.5, 0, 4.6), (-5.5, 2.2, 2.0, 4.8), (5.5, 2.2, 2.0, 4.8)],
        'right': [(0, 3.5, 0, 4.6), (-5.5, 2.2, 2.0, 4.8), (5.5, 2.2, 2.0, 4.8)],
    }
    upper = {
        'front': [(0, 6.0, 0, 4.7), (-6.5, 2.2, 1.2, 4.2), (6.5, 2.2, 1.2, 4.2)],
        'back':  [(0, 5.0, 0, 4.6), (-6.5, 2.2, 1.2, 4.2), (6.5, 2.2, 1.2, 4.2)],
        'left':  [(0, 5.0, 0, 4.6), (-5.5, 2.2, 1.2, 4.2), (5.5, 2.2, 1.2, 4.2)],
        'right': [(0, 5.0, 0, 4.6), (-5.5, 2.2, 1.2, 4.2), (5.5, 2.2, 1.2, 4.2)],
    }
    block_walls(S, -O, O, -O, O, 0, ST, ground)
    floor_with_thresholds(S, ground)
    block_walls(S, -O, O, -O, O, ST, ST, upper)
    for x in (-3.8, 3.8):                                                 # passage side walls
        wall(S, 'y', x, -I, I, 0, ST - SL, 0.6, [(-4.5, 3.2, 0, 4.4), (4.5, 3.2, 0, 4.4), (0, 2.0, 2.0, 4.2)])
    slab_with_hole(S, -I, I, -I, I, ST - SL, ST, hole=(-2, 2, -2, 2))      # upper floor, hole over passage
    S.rect(-I, I, -I, I, LV - SL, LV, 0)                                   # roof
    block_band(S, -O, O, -O, O, ST - SL + 0.05, ST - 0.05, 0.4, 1)        # string course (band)
    dress_block(S, -O, O, -O, O, 0, LV, ground)
    for y in (-O - 0.15, O + 0.15):                                       # pilasters flanking the passage
        for x in (-4.4, 4.4):
            S.box((x, y, 0.7 + (ST - SL - 0.7) / 2), (1.0, 0.3, ST - SL - 0.7), 1)
    for x in (-4.5, 4.5):                                                 # upper hall columns
        for y in (-4.5, 4.5):
            a.column(x, y, ST, LV - SL - ST, 1.3)
    return a.finish()

# ======================================================================
# BASE 2: LOGGIA (columned porch + L passage)
# ======================================================================
def base_loggia():
    a = Asset("Base_Loggia")
    S = a.S
    LF = -6.0                                                              # recessed ground front line
    PX = -5.7                                                              # passage centre (x), width 7
    PY = 5.7                                                               # passage exit centre (y)
    # ground walls
    wall(S, 'x', LF + T / 2, -I, I, 0, ST - SL, T, [(PX, 7.0, 0, 6.4), (1.8, 2.4, 2.0, 4.8), (6.5, 2.4, 2.0, 4.8)], trim=True)
    wall(S, 'x', O - T / 2, -O, O, 0, ST, T, [(-5.7, 2.4, 2.0, 4.8), (5.0, 2.4, 2.0, 4.8)], trim=True)
    left_ops = [(-7.9, 2.6, 0, 5.2), (0.0, 2.2, 2.0, 4.8), (6.0, 2.2, 2.0, 4.8)]
    right_ops = [(-7.9, 2.6, 0, 5.2), (-2.0, 2.2, 2.0, 4.8), (PY, 7.0, 0, 6.4)]
    floor_with_thresholds(S, {'left': left_ops, 'right': right_ops})
    wall(S, 'y', -O + T / 2, -O + T, O - T, 0, ST, T, left_ops, trim=True)
    wall(S, 'y', O - T / 2, -O + T, O - T, 0, ST, T, right_ops, trim=True)
    # inner walls around the room (front-right), with doors to the passage
    wall(S, 'y', -1.9, LF + T, 1.6, 0, ST - SL, 0.6, [(-1.5, 3.2, 0, 4.4)])
    wall(S, 'x', 1.9, -2.2, I, 0, ST - SL, 0.6, [(4.0, 3.2, 0, 4.4)])
    # porch columns (6.4 m) under the upper floor
    for x in (-7.5, -2.5, 2.5, 7.5):
        a.column(x, -8.4, 0, ST - SL, 1.3)
    S.rect(-I, I, -O + 0.3, LF, 0.0, 0.12, 1)                             # porch floor
    for x in (-O, I):                                                      # ground corner posts
        S.rect(x, x + T, -O, -I, 0, ST, 0)
    # upper floor + upper story (full footprint)
    slab_with_hole(S, -I, I, -I, I, ST - SL, ST, hole=(-8, -4, 4, 8))
    upper = {
        'front': [(-6, 3.0, 1.0, 4.7), (0, 3.0, 1.0, 4.7), (6, 3.0, 1.0, 4.7)],
        'back':  [(0, 5.0, 0, 4.6), (-6.5, 2.2, 1.2, 4.2), (6.5, 2.2, 1.2, 4.2)],
        'left':  [(-5.5, 2.2, 1.2, 4.2), (0, 2.2, 1.2, 4.2), (5.5, 2.2, 1.2, 4.2)],
        'right': [(-4.0, 6.0, 0, 4.7), (4.5, 2.2, 1.2, 4.2)],
    }
    block_walls(S, -O, O, -O, O, ST, ST, upper)
    S.rect(-I, I, -I, I, LV - SL, LV, 0)
    S.rect(-I, I, -O, -I, ST - SL, ST, 0)                                  # porch architrave (wall line only)
    block_band(S, -O, O, -O, O, ST - SL + 0.05, ST - 0.05, 0.4, 1)         # string course (band)
    S.rect(-O - 0.1, O + 0.1, -O - 0.25, -O + 0.3, ST - SL - 0.35, ST - SL, 2)   # gold band over porch
    # dressing: base course on the ground walls (custom because the front is recessed)
    wall(S, 'x', LF + T / 2, -I + 0.15, I - 0.15, 0, 0.7, T + 0.3, [(PX, 7.1, 0, 99)], 1)
    wall(S, 'x', O - T / 2, -O - 0.15, O + 0.15, 0, 0.7, T + 0.3, (), 1)
    wall(S, 'y', -O + T / 2, LF - 0.15, O - T - 0.15, 0, 0.7, T + 0.3, [], 1)
    wall(S, 'y', O - T / 2, LF - 0.15, O - T - 0.15, 0, 0.7, T + 0.3, [(PY, 7.1, 0, 99)], 1)
    dress_block(S, -O, O, -O, O, ST, ST, {}, pilasters=False, base_course=False)
    for (x, y, sx, sy) in ((-O + 0.6, O + 0.15, 1.2, 0.3), (O - 0.6, O + 0.15, 1.2, 0.3),
                           (-O - 0.15, O - 0.6, 0.3, 1.2), (O + 0.15, O - 0.6, 0.3, 1.2)):
        S.box((x, y, 0.7 + (LV - 2.3) / 2), (sx, sy, LV - 2.3), 1)
    return a.finish()

# ======================================================================
# TOPS  (local z = 0 is the base roof)
# ======================================================================
def top_lterrace():
    a = Asset("Top_LTerrace")
    S = a.S
    x0, x1, y0, y1 = -2.0, O, -2.0, O                     # 12 x 12 block in the back-right corner
    ops = {'front': [(4.0, 5.0, 0, 4.7)], 'left': [(4.0, 5.0, 0, 4.7)],
           'back': [(1.0, 2.2, 1.2, 4.2), (7.0, 2.2, 1.2, 4.2)], 'right': [(1.0, 2.2, 1.2, 4.2), (7.0, 2.2, 1.2, 4.2)]}
    block_walls(S, x0, x1, y0, y1, 0, ST, ops)
    S.rect(x0 + T, x1 - T, y0 + T, y1 - T, ST - SL, ST, 0)
    dress_block(S, x0, x1, y0, y1, 0, ST, ops)
    parapet(S, 'x', y0 + 0.25, x0, x1, ST, gaps=[(4.0, 3.0)])
    parapet(S, 'x', y1 - 0.25, x0, x1, ST)
    parapet(S, 'y', x0 + 0.25, y0 + 0.5, y1 - 0.5, ST)
    parapet(S, 'y', x1 - 0.25, y0 + 0.5, y1 - 0.5, ST, gaps=[(4.0, 3.0)])
    # L terrace railing with step-off gaps; wide gap at the back (uphill) edge
    parapet(S, 'x', -O + 0.25, -O, O, 0, gaps=[(-6.0, 3.0), (5.0, 3.0)])
    parapet(S, 'y', -O + 0.25, -O + 0.5, O - 0.5, 0, gaps=[(1.0, 3.0)])
    parapet(S, 'x', O - 0.25, -O, -6.5, 0)                                # back edge of the L: mostly open
    parapet(S, 'y', O - 0.25, -O + 0.5, y0 - 0.3, 0)
    planter(S, -8.3, -8.6)
    planter(S, -8.6, 7.5, sx=1.2, sy=2.2)
    planter(S, 7.5, -8.6)
    return a.finish()

def top_slope():
    a = Asset("Top_Slope")
    S = a.S
    y0, y1 = -2.0, O
    zf, zb = 6.0, 9.5                                     # front / back wall height
    zr = lambda y: zf + (y - y0) * (zb - zf) / (y1 - y0)
    wall(S, 'x', y0 + T / 2, -O, O, 0, zf, T, [(-5.0, 4.0, 0, 4.0), (5.0, 4.0, 0, 4.0)], trim=True)
    wall(S, 'x', y1 - T / 2, -O, O, 0, zb, T, [(-5.0, 2.2, 2.0, 5.0), (5.0, 2.2, 2.0, 5.0)], trim=True)
    for x in (-O + T / 2, O - T / 2):                     # trapezoid side walls
        ya, yb = y0 + T, y1 - T
        S.extrude([(ya, 0), (yb, 0), (yb, zr(yb)), (ya, zr(ya))], 'x', x - T / 2, x + T / 2)
    # sloped roof slab with overhangs + gold ridge trim
    ya, yb = y0 - 1.2, y1 + 0.6
    S.extrude([(ya, zr(ya)), (yb, zr(yb)), (yb, zr(yb) + 0.5), (ya, zr(ya) + 0.5)], 'x', -O - 0.6, O + 0.6, 1)
    S.extrude([(yb - 0.6, zr(yb - 0.6) + 0.5), (yb, zr(yb) + 0.5), (yb, zr(yb) + 0.75), (yb - 0.6, zr(yb - 0.6) + 0.75)],
              'x', -O - 0.55, O + 0.55, 2)
    S.rect(-O - 0.15, O + 0.15, y0 - 0.15, y0 + T + 0.15, 0, 0.7, 1)       # base course (front)
    S.rect(-O - 0.08, O + 0.08, y0 - 0.08, y0 + T + 0.08, zf - 1.3, zf - 0.95, 2)   # gold frieze (front)
    for x in (-O + 0.6, O - 0.6):
        S.box((x, y0 - 0.15, 0.7 + (zf - 1.7) / 2), (1.2, 0.3, zf - 1.7), 1)
    # front terrace
    parapet(S, 'x', -O + 0.25, -O, O, 0, gaps=[(0.0, 4.0)])
    parapet(S, 'y', -O + 0.25, -O + 0.5, y0 - 0.3, 0)
    parapet(S, 'y', O - 0.25, -O + 0.5, y0 - 0.3, 0)
    planter(S, -6.5, -6.0)
    planter(S, 6.5, -6.0)
    return a.finish()

def top_balcony():
    a = Asset("Top_Balcony")
    S = a.S
    x0, x1, y0, y1 = -O, O, -1.0, O
    ops = {'front': [(0, 7.0, 0, 4.7), (-6.5, 3.0, 0, 4.6), (6.5, 3.0, 0, 4.6)],
           'back': [(-5, 2.2, 1.2, 4.2), (5, 2.2, 1.2, 4.2)],
           'left': [(4.5, 2.2, 1.2, 4.2)], 'right': [(4.5, 2.2, 1.2, 4.2)]}
    block_walls(S, x0, x1, y0, y1, 0, ST, ops)
    S.rect(x0 + T, x1 - T, y0 + T, y1 - T, ST - SL, ST, 0)
    dress_block(S, x0, x1, y0, y1, 0, ST, ops)
    parapet(S, 'x', y0 + 0.25, x0, x1, ST, gaps=[(0, 3.0)])
    parapet(S, 'x', y1 - 0.25, x0, x1, ST, gaps=[(0, 4.0)])
    parapet(S, 'y', x0 + 0.25, y0 + 0.5, y1 - 0.5, ST)
    parapet(S, 'y', x1 - 0.25, y0 + 0.5, y1 - 0.5, ST)
    # terrace railing (open where the balcony is)
    parapet(S, 'x', -O + 0.25, -O, -5.0, 0)
    parapet(S, 'x', -O + 0.25, 5.0, O, 0)
    parapet(S, 'y', -O + 0.25, -O + 0.5, y0 - 0.3, 0)
    parapet(S, 'y', O - 0.25, -O + 0.5, y0 - 0.3, 0)
    # cantilevered balcony: slab, corbels, balustrade with turned balusters
    BX, BY = 5.0, -13.0
    S.rect(-BX, BX, BY, -O, -0.6, 0.0, 1)
    S.rect(-BX - 0.1, BX + 0.1, BY - 0.1, BY + 0.2, -0.75, -0.35, 2)
    for x in (-4.2, 0.0, 4.2):
        S.rect(x - 0.35, x + 0.35, BY + 0.4, -O + 0.4, -1.3, -0.6, 1)
        S.rect(x - 0.3, x + 0.3, -O - 1.4, -O + 0.4, -2.2, -1.3, 1)
    bal = [(0.10, 0.0), (0.14, 0.08), (0.09, 0.25), (0.15, 0.55), (0.08, 0.85), (0.11, 0.95)]
    def balusters(p0, p1, n):
        for k in range(n + 1):
            t = k / n
            S.lathe(bal, segs=8, slot=0, center=(p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t, 0.12))
    S.rect(-BX, BX, BY, BY + 0.35, 0.0, 0.12, 1)                           # balustrade plinth + rails
    S.rect(-BX - 0.05, BX + 0.05, BY - 0.05, BY + 0.4, 1.07, 1.25, 0)
    for xa, xb in ((-BX, -BX + 0.35), (BX - 0.35, BX)):
        S.rect(xa, xb, BY + 0.35, -O, 0.0, 0.12, 1)
        S.rect(xa - 0.05, xb + 0.05, BY + 0.4, -O, 1.07, 1.25, 0)
    balusters((-BX + 0.2, BY + 0.18), (BX - 0.2, BY + 0.18), 18)
    balusters((-BX + 0.18, BY + 0.7), (-BX + 0.18, -O - 0.2), 5)
    balusters((BX - 0.18, BY + 0.7), (BX - 0.18, -O - 0.2), 5)
    planter(S, -7.8, -7.5)
    planter(S, 7.8, -7.5)
    return a.finish()

def top_shrine():
    a = Asset("Top_Shrine")
    S = a.S
    PX, PY = 6.5, 8.0
    S.rect(-PX, PX, -PY, PY, 0, 0.6, 1)                                    # podium
    S.rect(-4.0, 4.0, -PY - 1.2, -PY, 0, 0.3, 1)                           # front step
    for x in (-4.8, 4.8):
        for y in (-5.8, 0.0, 5.8):
            a.column(x, y, 0.6, 7.0, 1.45)
    zt = 7.6
    for x in (-4.8, 4.8):                                                  # architraves
        S.rect(x - 0.8, x + 0.8, -6.8, 6.8, zt, zt + 0.9, 0)
        S.rect(x - 0.9, x + 0.9, -6.75, 6.75, zt + 0.45, zt + 0.65, 2)
    for y in (-5.8, 5.8):
        S.rect(-4.0, 4.0, y - 0.8, y + 0.8, zt, zt + 0.85, 0)
    ze, zr_, ex = zt + 0.9, zt + 3.6, 6.6                                   # eave, ridge, half-span
    S.extrude([(-ex, ze - 0.1), (0, zr_), (ex, ze - 0.1), (ex, ze + 0.4), (0, zr_ + 0.5), (-ex, ze + 0.4)],
              'y', -7.6, 7.6, 1)
    for y0_, y1_ in ((-7.0, -6.4), (6.4, 7.0)):                            # pediments
        S.extrude([(-ex + 0.4, ze), (ex - 0.4, ze), (0, zr_ - 0.2)], 'y', y0_, y1_, 0)
    S.rect(-0.3, 0.3, -7.7, 7.7, zr_ + 0.35, zr_ + 0.65, 2)                # gold ridge
    emblem(S, (0, -7.02, ze + 0.9), '-y', 0.6)
    S.rect(-1.6, 1.6, 2.5, 4.5, 0.6, 1.8, 1)                               # altar + orb
    orb = [(0.9 * sin(t), -0.9 * cos(t)) for t in [pi * k / 12 for k in range(1, 12)]]
    S.lathe(orb, segs=24, slot=2, center=(0, 3.5, 2.7))
    for sgn in (-1, 1):                                                    # terrace railing, gaps on all sides
        parapet(S, 'x', sgn * (O - 0.25), -O, O, 0, gaps=[(0, 4.0)])
        parapet(S, 'y', sgn * (O - 0.25), -O + 0.5, O - 0.5, 0, gaps=[(0, 4.0)])
    for x, y in ((-8.3, -8.3), (8.3, -8.3), (-8.3, 8.3), (8.3, 8.3)):
        planter(S, x, y, sx=1.6, sy=1.6)
    return a.finish()

def top_parapet():
    a = Asset("Top_Parapet")
    S = a.S
    for sgn in (-1, 1):
        parapet(S, 'x', sgn * (O - 0.25), -O, O, 0, gaps=[(-5.0, 3.0), (5.0, 3.0)])
        parapet(S, 'y', sgn * (O - 0.25), -O + 0.5, O - 0.5, 0, gaps=[(0, 3.0)])
    for x, y in ((-8.3, -8.3), (8.3, -8.3), (-8.3, 8.3), (8.3, 8.3)):
        planter(S, x, y, sx=1.6, sy=1.6)
    planter(S, 0, 0, sx=4.0, sy=1.2)
    return a.finish()

# ======================================================================
# Build, export, preview (a little terraced street)
# ======================================================================
out = EXPORT_DIR or (os.path.join(os.path.dirname(bpy.data.filepath), "AtlantisKit_FBX")
                     if bpy.data.filepath else os.path.expanduser("~/AtlantisKit_FBX"))
os.makedirs(out, exist_ok=True)
assets = {}
for fn in (base_passage, base_loggia, top_lterrace, top_slope, top_balcony, top_shrine, top_parapet):
    root, objs = fn()
    assets[fn.__name__] = (root, objs)
    if EXPORT_FBX:
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
        print("Exported", root.name)
    for o in objs[1:]:                       # free the short names for the next asset
        o.name = f"{root.name}_{o.name}"
        if o.data is not None:
            o.data.name = o.name

# preview: originals for export stay as-is; linked duplicates arranged as a terraced street
def show(key, x, y, z, rot=0):
    root, objs = assets[key]
    new_root = root.copy()
    new_root.name = f"PREVIEW_{root.name}"
    bpy.context.collection.objects.link(new_root)
    new_root.hide_render = False
    for o in objs[1:]:
        c = o.copy()
        c.name = f"PREVIEW_{o.name}"
        bpy.context.collection.objects.link(c)
        c.parent = new_root
        c.hide_render = "_COL" in o.name
    new_root.location = (x, y, z)
    new_root.rotation_euler = (0, 0, radians(rot))
for root, objs in assets.values():
    for o in objs:
        o.hide_set(True)
        o.hide_render = True
layout = [  # (base, top, x, y, z, rotation)  front row at street level, back row one level up
    ("base_passage", "top_lterrace", -22, 0, 0, 0),
    ("base_loggia",  "top_balcony",    0, 0, 0, 0),
    ("base_passage", "top_slope",     22, 0, 0, 90),
    ("base_loggia",  "top_shrine",   -22, 20, LV, 0),
    ("base_passage", "top_parapet",    0, 20, LV, 0),
    ("base_loggia",  "top_lterrace",  22, 20, LV, 270),
]
for b, t, x, y, z, r in layout:
    show(b, x, y, z, r)
    show(t, x, y, z + LV, r)
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, -40, 0))
bpy.context.object.name = "_PreviewGround"
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 50, LV / 2))
g = bpy.context.object
g.name = "_PreviewTerraceStep"
g.scale = (140, 60, LV)
bpy.ops.object.light_add(type='SUN', location=(0, 0, 60), rotation=(radians(40), 0, radians(30)))
bpy.context.object.data.energy = 3
bpy.ops.object.camera_add(location=(60, -95, 60), rotation=(radians(62), 0, radians(32)))
bpy.context.scene.camera = bpy.context.object
print("Terrace kit built:", ", ".join(r.name for r, _ in assets.values()))
