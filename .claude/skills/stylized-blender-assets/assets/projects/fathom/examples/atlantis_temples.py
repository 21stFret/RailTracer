"""
ATLANTIS KIT  -  TEMPLES (grapple playgrounds)   Blender 3.x / 4.x  ->  Unity
Paste into the Scripting tab and press Run (clears the scene). Exports 2 FBX files.

Temple_Peristyle  ~43 x 71 m   Long Greek-style temple: 34 columns (14 m) around a roofless
                               inner hall with its own columns, partial architraves, back
                               pediment, collapsed side wall, fallen columns everywhere.
Temple_Rings      ~60 m round  Atlantean ring temple: outer ring of 16 m columns, a moat,
                               raised island with an inner ring, and a 30 m central spire
                               with a floating gold ring - the highest grapple point.

Grapple design: column tops are 2.5-2.8 m flat landings, gaps 3-6 m between columns,
architrave beams only survive between intact columns (so broken/missing columns create
gaps to swing through), broken columns give mid-height perches at varied heights.

Follows the import pipeline:  materials Atl_*,  *_SOLID = visible + collider,
*_COL = invisible collider (smooth cylinders on columns so the hook doesn't snag).
Bonus: an empty per grapple point under *_GrappleAnchors (tops of intact columns,
spire, ring) in case the game wants explicit anchor targets.
"""
import bpy, bmesh, os, random
from math import pi, sin, cos, radians, atan2, degrees, sqrt
from mathutils import Matrix, Vector

EXPORT_FBX = True
EXPORT_DIR = ""            # "" = AtlantisKit_FBX next to the .blend, or ~/AtlantisKit_FBX

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

    def box(self, center, size, slot=0, rot=None):
        hx, hy, hz = (s / 2 for s in size)
        pts = [Vector((x * hx, y * hy, z * hz)) for z in (-1, 1)
               for (x, y) in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        if rot is not None:
            pts = [rot @ p for p in pts]
        c = Vector(center)
        faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        self.add([p + c for p in pts], faces, slot)

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

    def annulus(self, r_in, r_out, z0, z1, slot=0, segs=96):
        self.lathe([(max(r_in, 0.001), z0), (r_out, z0), (r_out, z1), (max(r_in, 0.001), z1)],
                   segs=segs, slot=slot, closed=True)

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

def place(kh, kv, x, y, z, yaw=0.0):
    """Non-uniform scale: kh = width scale, kv = height scale (kit column is 3.92 m)."""
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Diagonal((kh, kh, kv, 1.0)))

def place_lying(kh, klen, x, y, z, yaw=0.0):
    """For pieces lying along X: klen scales the length, kh the thickness."""
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Diagonal((klen, kh, kh, 1.0)))

# ======================================================================
# Column pieces (same design as the kit pillars)
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
        mb.box((0, 0, z + 0.27), (1.18, 1.18, 0.05), 2)
    else:
        shaft(mb, r, 0.54, broken_h, 0.40, 0.39, jag_top=0.35)
    return mb

def kit_lying_shaft(seed, length=1.2, rings=8):
    """A column piece lying along X, both ends broken, resting on z=0 (kit scale)."""
    r = random.Random(seed)
    mb = MB()
    shaft(mb, r, 0.0, length, 0.39, 0.37, jag_top=0.25, jag_bot=0.2, rings=rings)
    rot = Matrix.Rotation(radians(90), 3, 'Y')
    mb.v = [tuple(rot @ Vector((x, y, z - length / 2)) + Vector((0, 0, 0.39))) for x, y, z in mb.v]
    return mb

def rock(mb, r, center, size, slot=3):
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

def arc_wall(mb, r_in, r_out, z0, z1, a0, a1, slot=0, seg_len=1.5):
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

def prism_tri(mb, x_half, y0, y1, z0, h, slot=0):
    """Triangular pediment block, ridge along Y."""
    v = [(-x_half, y0, z0), (x_half, y0, z0), (0, y0, z0 + h),
         (-x_half, y1, z0), (x_half, y1, z0), (0, y1, z0 + h)]
    mb.add(v, [[0, 1, 2], [5, 4, 3], [0, 3, 4, 1], [0, 2, 5, 3], [1, 4, 5, 2]], slot)

def emblem(mb, center, facing='-y'):
    """Atlantis concentric rings, flat, facing -y or +y."""
    rot = Matrix.Rotation(radians(90 if facing == '-y' else -90), 3, 'X')
    for r_in, r_out in ((0.0, 0.35), (0.65, 0.9), (1.3, 1.5), (1.9, 2.1)):
        prof = [(max(r_in, 0.001), 0), (r_out, 0), (r_out, 0.12), (max(r_in, 0.001), 0.12)]
        mb.lathe(prof, segs=48, slot=2, closed=True, center=center, mat3=rot)

# ======================================================================
# Shared: columns + damage + anchors
# ======================================================================
class Site:
    def __init__(self, name, seed):
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

    def column(self, x, y, z, kh, kv, state, yaw=0.0):
        self.n += 1
        bh = None if state == 'ok' else (0.12 if state == 'missing' else self.rng.uniform(0.5, 2.4))
        self.C.merge(kit_column(1000 + self.n * 7, bh), place(kh, kv, x, y, z, yaw))
        top_kit = 3.92 if state == 'ok' else 0.54 + bh
        top = z + top_kit * kv
        self.COL.rect(x - 0.55 * kh, x + 0.55 * kh, y - 0.55 * kh, y + 0.55 * kh, z, z + 0.32 * kv)
        self.COL.lathe([(0.43 * kh, z + 0.3 * kv), (0.43 * kh, top - (0.22 * kv if state == 'ok' else 0))],
                       segs=12, center=(x, y, 0))
        if state == 'ok':
            self.COL.rect(x - 0.575 * kh, x + 0.575 * kh, y - 0.575 * kh, y + 0.575 * kh, top - 0.22 * kv, top)
        if state != 'missing':
            self.anchors.append((x, y, top))
        return top

    def fallen(self, x, y, yaw, kh, klen, length_kit=2.96, z=0.0):
        self.n += 1
        self.P.merge(kit_lying_shaft(2000 + self.n, length_kit, rings=10), place_lying(kh, klen, x, y, z, yaw))

    def drum(self, x, y, kh, z=0.0):
        self.n += 1
        self.P.merge(kit_lying_shaft(3000 + self.n, 1.0, rings=4),
                     place_lying(kh, kh, x, y, z, self.rng.uniform(0, 360)))

    def finish(self):
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
        root = bpy.context.object
        root.name = self.name
        objs = [
            self.S.build(f"{self.name}_Structure_SOLID", MATS, bevel=0.0, smooth_angle=30),
            self.C.build(f"{self.name}_Columns", MATS, bevel=0.03),
            self.COL.build(f"{self.name}_Columns_COL", [M_COL], uv=False, smooth_angle=0),
            self.P.build(f"{self.name}_Props_SOLID", MATS, bevel=0.0),
        ]
        objs[2].display_type = 'WIRE'
        objs[2].hide_render = True
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
        anchors_root = bpy.context.object
        anchors_root.name = f"{self.name}_GrappleAnchors"
        objs.append(anchors_root)
        for i, (x, y, z) in enumerate(self.anchors):
            bpy.ops.object.empty_add(type='SPHERE', radius=0.5, location=(x, y, z))
            e = bpy.context.object
            e.name = f"{self.name}_Anchor_{i:02d}"
            e.parent = anchors_root
            objs.append(e)
        for o in objs:
            if o.parent is None:
                o.parent = root
        return root, [root] + objs

def runs(states):
    """Index ranges of consecutive intact columns (length >= 2)."""
    out, start = [], None
    for i, s in enumerate(states + ['x']):
        if s == 'ok' and start is None:
            start = i
        elif s != 'ok' and start is not None:
            if i - start >= 2:
                out.append((start, i - 1))
            start = None
    return out

# ======================================================================
# TEMPLE 1: PERISTYLE
# ======================================================================
def build_peristyle():
    s = Site("Temple_Peristyle", 11)
    S, P, rng = s.S, s.P, s.rng
    FL = 1.8
    KH, KV = 2.2, 14.0 / 3.92                     # 14 m columns, ~1.8 m thick
    AB = 1.15 * KH                                 # abacus width
    XC, YC = 17.0, 31.0
    xs = [-XC + 2 * XC * i / 6 for i in range(7)]
    ys = [-YC + 2 * YC * i / 11 for i in range(12)]

    # stepped platform
    for i, g in enumerate((3.0, 2.25, 1.5)):
        S.rect(-XC - g - 2, XC + g + 2, -YC - g - 2, YC + g + 2, i * 0.6, (i + 1) * 0.6, 1)

    # perimeter colonnade; back row + corners protected (back pediment needs support)
    def st(x, y):
        corner = abs(x) == XC and abs(y) == YC
        return s.state(0.24, 0.08, protect=corner or y == YC)
    rows = {
        'front': [(x, -YC) for x in xs], 'back': [(x, YC) for x in xs],
        'left': [(-XC, y) for y in ys], 'right': [(XC, y) for y in ys],
    }
    states = {}
    for key, pts in rows.items():
        for p in pts:
            if p not in states:
                states[p] = st(*p)
    tops = {}
    for p, sname in states.items():
        tops[p] = s.column(p[0], p[1], FL, KH, KV, sname)
        if sname == 'broken' and rng.random() < 0.7:            # drums fall outward
            d = Vector((p[0], p[1])).normalized() * rng.uniform(3.5, 6.0)
            s.drum(p[0] + d.x + rng.uniform(-1, 1), p[1] + d.y + rng.uniform(-1, 1), KH * 0.95)

    # architraves only between intact neighbours
    zb = FL + 14.0
    w = AB - 0.1
    def beam_x(x0, x1, y, z, w, h, trim=True):
        S.rect(x0, x1, y - w / 2, y + w / 2, z, z + h, 0)
        if trim:
            S.rect(x0 + 0.01, x1 - 0.01, y - w / 2 - 0.1, y + w / 2 + 0.1, z + h * 0.5, z + h * 0.75, 2)
            S.rect(x0 - 0.2, x1 + 0.2, y - w / 2 - 0.4, y + w / 2 + 0.4, z + h, z + h + 0.5, 1)
    def beam_y(y0, y1, x, z, w, h, trim=True):
        S.rect(x - w / 2, x + w / 2, y0, y1, z, z + h, 0)
        if trim:
            S.rect(x - w / 2 - 0.1, x + w / 2 + 0.1, y0 + 0.01, y1 - 0.01, z + h * 0.5, z + h * 0.75, 2)
            S.rect(x - w / 2 - 0.4, x + w / 2 + 0.4, y0, y1, z + h, z + h + 0.5, 1)
    for key in ('front', 'back'):
        pts = rows[key]
        for a, b in runs([states[p] for p in pts]):
            beam_x(pts[a][0] - AB / 2, pts[b][0] + AB / 2, pts[a][1], zb, w, 1.6)
            s.anchors.append(((pts[a][0] + pts[b][0]) / 2, pts[a][1], zb + 2.1))
    for key in ('left', 'right'):
        pts = rows[key]
        for a, b in runs([states[p] for p in pts]):
            y0, y1 = pts[a][1] - AB / 2, pts[b][1] + AB / 2
            if abs(pts[a][1]) == YC:
                y0 = pts[a][1] + w / 2 + 0.4             # don't overlap the front/back beams
            if abs(pts[b][1]) == YC:
                y1 = pts[b][1] - w / 2 - 0.4
            beam_y(y0, y1, pts[a][0], zb, w, 1.6)

    # back pediment with emblem
    prism_tri(S, XC + AB / 2 + 0.6, YC - w / 2 - 0.4, YC + w / 2 + 0.4, zb + 2.1, 5.5)
    emblem(S, (0, YC + w / 2 + 0.38, zb + 2.1 + 2.4), facing='+y')
    s.anchors.append((0, YC, zb + 2.1 + 5.5))

    # inner hall (cella) - roofless, east wall partly collapsed
    T, CH = 1.2, 11.0
    CX, CY = 10.0, 21.0
    collapsed = lambda x, y: x > 0 and -6 < y < 12
    def wall(axis, c, a0, a1, ops):
        world = (lambda a: (a, c)) if axis == 'x' else (lambda a: (c, a))
        def piece(b0, b1, za, zb_, slot=0, extra=0.0):
            if b1 - b0 < 1e-4 or zb_ - za < 1e-4:
                return
            m, L = (b0 + b1) / 2, b1 - b0
            size = (L, T + extra, zb_ - za) if axis == 'x' else (T + extra, L, zb_ - za)
            S.box((m, c, (za + zb_) / 2) if axis == 'x' else (c, m, (za + zb_) / 2), size, slot)
        def solid(b0, b1):
            n = max(1, int((b1 - b0) / 1.5))
            step = (b1 - b0) / n
            for k in range(n):
                s0 = b0 + k * step
                h = CH * rng.uniform(0.25, 0.75) if collapsed(*world(s0 + step / 2)) else CH
                piece(s0, s0 + step, FL, FL + h)
                if h == CH:
                    piece(s0, s0 + step, FL + CH - 1.5, FL + CH - 1.1, 2, 0.16)      # gold band
                    piece(s0, s0 + step, FL + CH, FL + CH + 0.4, 1, 0.5)                # cap
        cur = a0
        for oc, ow, b, t in sorted(ops):
            l, r_ = oc - ow / 2, oc + ow / 2
            solid(cur, l)
            gone = collapsed(*world(oc))
            if b > 0:
                piece(l, r_, FL, FL + b)
            if t < CH and not gone:
                piece(l, r_, FL + t, FL + CH)
                piece(l - 0.6, r_ + 0.6, FL + t - 0.03, FL + t + 0.6, 1, 0.3)       # lintel
            if not gone:
                piece(l - 0.5, l + 0.03, FL + b, FL + t, 1, 0.25)                   # jambs
                piece(r_ - 0.03, r_ + 0.5, FL + b, FL + t, 1, 0.25)
            cur = r_
        solid(cur, a1)
    win = [(yy, 3.0, 5.0, 8.5) for yy in (-12, 0, 12)]
    wall('x', -CY + T / 2, -CX, CX, [(0, 7.0, 0, 9.5)])
    wall('x',  CY - T / 2, -CX, CX, [(0, 5.0, 0, 8.0)])
    wall('y', -CX + T / 2, -CY + T, CY - T, win)
    wall('y',  CX - T / 2, -CY + T, CY - T, win)
    S.rect(-CX, CX, 14, CY, FL + CH, FL + CH + 0.8)                     # roof over statue alcove
    for r_in, r_out in ((3.0, 3.3), (6.0, 6.3)):                       # floor rings
        S.lathe([(r_in, 0), (r_out, 0), (r_out, 0.03), (r_in, 0.03)], segs=64, slot=2,
                closed=True, center=(0, -2, FL - 0.01))

    # statue alcove: plinth + gold orb
    S.rect(-3.5, 3.5, 15, 19.5, FL, FL + 3.0, 1)
    orb = [(2.2 * sin(t), -2.2 * cos(t)) for t in [pi * k / 16 for k in range(1, 16)]]
    S.lathe(orb, segs=40, slot=2, center=(0, 17.25, FL + 3.0 + 2.15))

    # inner hall columns (10 m) - a grapple "forest" inside
    KHi, KVi = 1.6, 10.0 / 3.92
    for x in (-5.5, 5.5):
        col_ys = [-14, -7, 0, 7, 14]
        sts = [s.state(0.25, 0.1, protect=(y == 14)) for y in col_ys]
        for y, sname in zip(col_ys, sts):
            s.column(x, y, FL, KHi, KVi, sname)
        for a, b in runs(sts):
            beam_y(col_ys[a] - 0.9, col_ys[b] + 0.9, x, FL + 10.0, 1.7, 1.1, trim=False)

    # porch columns in front of the hall door, full architrave
    for x in (-7.5, -2.5, 2.5, 7.5):
        s.column(x, -25.5, FL, KH, KV, 'ok')
    beam_x(-7.5 - AB / 2, 7.5 + AB / 2, -25.5, zb, w, 1.6)

    # fallen columns + rubble
    s.fallen(-13.2, 8.0, 90, KH, KV, z=FL)                    # between colonnade and hall
    s.fallen(1.5, -6.0, 30, KH, KV, z=FL)                     # across the hall floor
    s.fallen(9.0, -YC - 6.5, 8, KH, KV)                       # tumbled down the front steps
    for k in range(9):
        sz = rng.uniform(0.6, 1.5)
        rock(P, rng, (rng.uniform(10.5, 14.5), rng.uniform(-6, 12), FL), (sz * 1.3, sz, sz * 0.8))
    for k in range(3):                                         # fallen front pediment blocks
        rot = Matrix.Rotation(radians(rng.uniform(-30, 30)), 3, 'Z') @ Matrix.Rotation(radians(rng.uniform(10, 25)), 3, 'X')
        P.box((rng.uniform(-12, -2), -YC - rng.uniform(5, 9), 0.9), (rng.uniform(4, 6), 2.2, 1.8), 0, rot)

    return s.finish()

# ======================================================================
# TEMPLE 2: RINGS
# ======================================================================
def build_rings():
    s = Site("Temple_Rings", 23)
    S, P, rng = s.S, s.P, s.rng

    # concentric platforms: outer deck, moat, island, dais
    S.annulus(28.5, 30.0, 0, 0.6, 1)
    S.annulus(27.0, 28.5, 0, 1.2, 1)
    S.annulus(21.0, 27.0, 0, 1.8, 0)
    S.annulus(17.0, 21.0, 0, 0.4, 1)                     # moat floor (sunken ring)
    S.annulus(15.5, 17.0, 0, 2.4, 1)
    S.annulus(0.0, 15.5, 0, 3.0, 0)
    S.annulus(5.5, 7.0, 0, 3.6, 1)
    S.annulus(0.0, 5.5, 0, 4.2, 0)
    for r_in in (14.2, 20.4):                           # gold edge rings
        S.annulus(r_in, r_in + 0.3, 0, (3.0 if r_in < 15 else 0.4) + 0.03, 2)
    for k in range(4):                                  # bridges over the moat
        a = radians(45 + 90 * k)
        S.box((19.0 * cos(a), 19.0 * sin(a), 1.2), (7.5, 5.0, 2.4), 1, Matrix.Rotation(a, 3, 'Z'))

    def ring(radius, count, base_z, kh, kv, p_broken, p_missing, beam_w, beam_h, trim=True, offset=0.0):
        angs = [2 * pi * i / count + offset for i in range(count)]
        sts = [s.state(p_broken, p_missing) for _ in angs]
        for i in range(0, count, 5):                    # keep some long intact runs
            sts[i] = 'ok'
            sts[(i + 1) % count] = 'ok'
        top = base_z + 3.92 * kv
        for a, sname in zip(angs, sts):
            s.column(radius * cos(a), radius * sin(a), base_z, kh, kv, sname, degrees(a))
            if sname == 'broken' and rng.random() < 0.6:
                d = rng.uniform(2.0, 2.8)
                rr = radius - d if radius > 15 else radius + d      # stay on the deck / island
                s.drum(rr * cos(a + 0.05), rr * sin(a + 0.05), kh * 0.95, z=base_z)
        half = (1.15 * kh / 2) / radius
        # wrap-around runs: rotate so we start at a broken column
        start = next((i for i, x in enumerate(sts) if x != 'ok'), None)
        order = list(range(count)) if start is None else [(start + i) % count for i in range(count)]
        for a_i, b_i in runs([sts[i] for i in order]):
            a0 = angs[order[a_i]] - half
            a1 = angs[order[b_i]] + half
            if a1 < a0:
                a1 += 2 * pi
            arc_wall(S, radius - beam_w / 2, radius + beam_w / 2, top, top + beam_h, a0, a1, 0)
            if trim:
                arc_wall(S, radius - beam_w / 2 - 0.1, radius + beam_w / 2 + 0.1, top + beam_h * 0.5,
                         top + beam_h * 0.75, a0 + 0.001, a1 - 0.001, 2)
                arc_wall(S, radius - beam_w / 2 - 0.4, radius + beam_w / 2 + 0.4, top + beam_h,
                         top + beam_h + 0.5, a0 - 0.01, a1 + 0.01, 1)
            mid = (a0 + a1) / 2
            s.anchors.append((radius * cos(mid), radius * sin(mid), top + beam_h + 0.5))
        return angs, sts

    # outer ring: 20 x 16 m columns; inner ring: 12 x 10 m columns on the island
    outer_a, outer_s = ring(25.0, 20, 1.8, 2.4, 16.0 / 3.92, 0.25, 0.1, 2.6, 1.8)
    ring(11.5, 12, 3.0, 1.8, 10.0 / 3.92, 0.2, 0.05, 2.0, 1.2, offset=pi / 12)

    # central spire with gold capstone and a floating gold ring held by spokes
    z0, z1 = 4.2, 30.0
    b0, b1 = 1.6, 0.8
    v = [(-b0, -b0, z0), (b0, -b0, z0), (b0, b0, z0), (-b0, b0, z0),
         (-b1, -b1, z1), (b1, -b1, z1), (b1, b1, z1), (-b1, b1, z1)]
    S.add(v, [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]], 0)
    S.add([(-1.1, -1.1, z1), (1.1, -1.1, z1), (1.1, 1.1, z1), (-1.1, 1.1, z1), (0, 0, z1 + 2.8)],
          [[0, 3, 2, 1], [0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4]], 2)
    S.rect(-1.3, 1.3, -1.3, 1.3, z1 - 0.4, z1, 1)
    zr = 21.0
    S.lathe([(4.6, 0), (5.3, 0), (5.3, 0.6), (4.6, 0.6)], segs=64, slot=2, closed=True, center=(0, 0, zr))
    for k in range(4):
        a = radians(45 + 90 * k)
        S.box((2.9 * cos(a), 2.9 * sin(a), zr + 0.3), (3.6, 0.4, 0.4), 1, Matrix.Rotation(a, 3, 'Z'))
        s.anchors.append((4.95 * cos(a), 4.95 * sin(a), zr + 0.6))
    s.anchors.append((0, 0, z1 + 2.8))

    # altars on the island (cover)
    for k in range(4):
        a = radians(90 * k)
        S.box((8.5 * cos(a), 8.5 * sin(a), 3.6), (2.8, 1.4, 1.2), 1, Matrix.Rotation(a + pi / 2, 3, 'Z'))
        S.box((8.5 * cos(a), 8.5 * sin(a), 4.25), (3.0, 1.6, 0.1), 2, Matrix.Rotation(a + pi / 2, 3, 'Z'))

    # fallen outer columns lying on the deck, one spanning the moat, rubble in the moat
    placed = 0
    for a, sname in zip(outer_a, outer_s):
        if sname != 'ok' and placed < 2:
            s.fallen(22.4 * cos(a + 0.18), 22.4 * sin(a + 0.18), degrees(a + 0.18) + 90, 2.2, 16.0 / 3.92, z=1.8)
            placed += 1
    am = radians(200)
    s.fallen(19.0 * cos(am), 19.0 * sin(am), 200, 2.0, 3.0, z=1.4)
    for k in range(10):
        a = rng.uniform(0, 2 * pi)
        sz = rng.uniform(0.5, 1.3)
        rock(P, rng, (19 * cos(a), 19 * sin(a), 0.4), (sz * 1.4, sz, sz * 0.7))

    return s.finish()

# ======================================================================
# Build, export, preview
# ======================================================================
temples = [build_peristyle(), build_rings()]

if EXPORT_FBX:
    out = EXPORT_DIR or (os.path.join(os.path.dirname(bpy.data.filepath), "AtlantisKit_FBX")
                         if bpy.data.filepath else os.path.expanduser("~/AtlantisKit_FBX"))
    os.makedirs(out, exist_ok=True)
    for root, objs in temples:
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
        print("Exported", root.name, "to", out)

temples[0][0].location.x = -50
temples[1][0].location.x = 45
bpy.ops.mesh.primitive_plane_add(size=300, location=(0, 0, 0))
bpy.context.object.name = "_PreviewSand"
bpy.context.object.data.materials.append(mat("_Sand", (0.55, 0.50, 0.36)))
bpy.ops.object.light_add(type='SUN', location=(0, 0, 40), rotation=(radians(40), 0, radians(30)))
bpy.context.object.data.energy = 3
bpy.ops.object.camera_add(location=(0, -120, 60), rotation=(radians(64), 0, 0))
bpy.context.scene.camera = bpy.context.object
bpy.ops.object.select_all(action='DESELECT')
for root, objs in temples:
    print(root.name, "anchors:", len([o for o in objs if "_Anchor_" in o.name]))
