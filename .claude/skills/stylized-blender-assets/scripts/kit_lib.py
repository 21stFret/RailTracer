# ======================================================================
# STYLIZED KIT LIB  -  generic Blender building code for game assets
# Blender 3.x / 4.x / 5.x. scripts/assemble.py pastes this between the CONFIG block and the
# piece code, so the user always gets ONE self-contained script.
#
# Run it any of these ways:
#   * Blender UI: Scripting tab > Open > Run Script
#   * blender --python my_kit.py                          (opens Blender with the scene built)
#   * blender --background --python my_kit.py            (headless: just export)
#   * blender -b --python my_kit.py -- --out "D:/Game/Assets/Models/Train" --only Seat_Double
# Script args after "--":  --out DIR   --no-export   --only Name1,Name2
# ======================================================================
import bpy, bmesh, os, sys, random
from math import pi, sin, cos, radians, degrees, atan2, sqrt
from mathutils import Matrix, Vector

# ---------------------------------------------------------------- config (set in the CONFIG block)
EXPORT_DIR = globals().get("EXPORT_DIR", "")          # "" = <KIT_NAME>_FBX next to the .blend
KIT_NAME = globals().get("KIT_NAME", "Kit")
TEXTURE_STYLE = globals().get("TEXTURE_STYLE", "flat")  # "flat" | "toon" | "painted" | "pbr"
TEXTURE_RES = globals().get("TEXTURE_RES", 1024)       # pixels per tile (power of two)
TEX_PATHS = {}                                          # material name -> {"albedo"|"normal"|"mask": path}
PREVIEW = bool(globals().get("__KIT_PREVIEW__", False) or globals().get("__ATLANTIS_PREVIEW__", False))
PREVIEW_REGISTRY = []

def cli_args():
    """Arguments after '--' on the Blender command line."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"out": "", "export": True, "only": None}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--out" and i + 1 < len(argv):
            opts["out"] = argv[i + 1]; i += 1
        elif a == "--no-export":
            opts["export"] = False
        elif a == "--only" and i + 1 < len(argv):
            opts["only"] = set(argv[i + 1].split(",")); i += 1
        i += 1
    return opts

# ---------------------------------------------------------------- palette / materials
# A palette is the material contract with the engine: Blender material NAME == engine material name.
# Each entry: (name, linear RGB, metallic, roughness[, {"tex": recipe, "tile": metres, ...}]).
# Slot index = position in the list. The optional 5th element drives texture generation
# (kit_textures.py recipes: wood, velvet/fabric, brushed/brass, painted_metal, plaster/paint, leather,
# marble, stone, plain). "tile" = how many metres one texture repeat covers (UVs are in metres).
PALETTE = [("Kit_Main", (0.62, 0.62, 0.60), 0.0, 0.7),
           ("Kit_Trim", (0.30, 0.30, 0.32), 0.0, 0.7),
           ("Kit_Accent", (0.95, 0.70, 0.25), 1.0, 0.3),
           ("Kit_Natural", (0.42, 0.50, 0.40), 0.0, 0.8)]
SLOT_MAIN, SLOT_TRIM, SLOT_ACCENT, SLOT_NATURAL = 0, 1, 2, 3

def set_palette(entries):
    """Replace the palette. Returns range(len) so you can unpack slot constants:
    WOOD, BRASS, CLOTH = set_palette([("Trn_Wood", ...), ("Trn_Brass", ...), ("Trn_Cloth", ...)])"""
    global PALETTE
    PALETTE = list(entries)
    return range(len(PALETTE))

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
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    m = bpy.data.materials.new(name)
    if bpy.app.version < (5, 0, 0):          # 5.x: materials always use nodes (setting it is deprecated)
        m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*color, 1)
    tex = TEX_PATHS.get(name)
    if tex:
        spec = next((e[4] for e in PALETTE if e[0] == name and len(e) > 4), {})
        k = 1.0 / spec.get("tile", 1.0)
        coord = nt.nodes.new("ShaderNodeTexCoord"); coord.location = (-900, 0)
        mp = nt.nodes.new("ShaderNodeMapping"); mp.location = (-700, 0)
        mp.inputs["Scale"].default_value = (k, k, 1.0)
        nt.links.new(coord.outputs["UV"], mp.inputs["Vector"])
        def img_node(key, y, noncolor):
            n = nt.nodes.new("ShaderNodeTexImage"); n.location = (-450, y)
            n.image = bpy.data.images.load(tex[key], check_existing=True)
            if noncolor:
                n.image.colorspace_settings.name = "Non-Color"
            nt.links.new(mp.outputs["Vector"], n.inputs["Vector"])
            return n
        alb = img_node("albedo", 300, False)
        nt.links.new(alb.outputs["Color"], b.inputs["Base Color"])
        msk = img_node("mask", 0, True)
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.location = (-200, 0)
        inv.inputs[0].default_value = 1.0
        nt.links.new(msk.outputs["Alpha"], inv.inputs[1])           # roughness = 1 - smoothness
        nt.links.new(inv.outputs["Value"], b.inputs["Roughness"])
        nrm = img_node("normal", -300, True)
        nm = nt.nodes.new("ShaderNodeNormalMap"); nm.location = (-200, -300)
        nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m

def generate_textures(out):
    """Write seamless texture sets for every palette entry into <out>/Textures (TEXTURE_STYLE != flat)."""
    TEX_PATHS.clear()
    if TEXTURE_STYLE == "flat" or "make_texture_set" not in globals():
        return
    import numpy as np
    folder = os.path.join(out, "Textures")
    os.makedirs(folder, exist_ok=True)
    for e in PALETTE:
        maps = make_texture_set(e, TEXTURE_STYLE, n=TEXTURE_RES)
        TEX_PATHS[e[0]] = {}
        for key, arr in maps.items():
            nm = f"{e[0]}_{key}"
            img = bpy.data.images.get(nm) or bpy.data.images.new(nm, TEXTURE_RES, TEXTURE_RES, alpha=True)
            if key != "albedo":
                img.colorspace_settings.name = "Non-Color"
            img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
            path = os.path.join(folder, nm + ".png")
            img.filepath_raw = path
            img.file_format = 'PNG'
            img.save()
            TEX_PATHS[e[0]][key] = path
    print("Textures: %s style, %d materials -> %s" % (TEXTURE_STYLE, len(TEX_PATHS), folder))

def palette_materials():
    return [mat(*e[:4]) for e in PALETTE]

def collider_material():
    return mat("Kit_Collider", (1.0, 0.0, 1.0))

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

    def merge_mirror_x(self, other):
        """Add `other` mirrored across x = 0 (faces reversed so normals stay outward)."""
        o = len(self.v)
        self.v += [(-x, y, z) for x, y, z in other.v]
        self.f += [[i + o for i in reversed(face)] for face in other.f]
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
        """Axis-aligned box from extents (skips degenerate boxes)."""
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
        """Spin (radius, z) points around Z. closed=True joins last->first (rings, annuli).
        mat3 rotates the result before it is moved to center (e.g. Rotation(90,'Y') = lying along X)."""
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

    def cylinder(self, r, z0, z1, slot=0, segs=24, center=(0, 0, 0), mat3=None):
        self.lathe([(r, z0), (r, z1)], segs=segs, slot=slot, center=center, mat3=mat3)

    def annulus(self, r_in, r_out, z0, z1, slot=0, segs=96, center=(0, 0, 0)):
        self.lathe([(max(r_in, 0.001), z0), (r_out, z0), (r_out, z1), (max(r_in, 0.001), z1)],
                   segs=segs, slot=slot, closed=True, center=center)

    def sphere(self, radius, center, slot=0, segs=24, rings=12):
        prof = [(radius * sin(t), -radius * cos(t)) for t in [pi * k / rings for k in range(1, rings)]]
        self.lathe(prof, segs=segs, slot=slot, center=center)

    def build(self, name, mats, bevel=0.0, smooth_angle=32, uv=True):
        """uv=True: world-scale box mapping (1 UV unit = 1 m, projected along each face's main axis),
        so tiling textures line up across modules on a metre grid."""
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.v, [], self.f)
        mesh.update()
        used = sorted(set(self.m))                 # only the slots this mesh uses -> no empty
        remap = {s: i for i, s in enumerate(used)}   # material slots / submeshes in the engine
        for s in used:
            mesh.materials.append(mats[s] if s < len(mats) else mats[-1])
        for poly, s in zip(mesh.polygons, self.m):
            poly.material_index = remap[s]
        if uv:
            uvl = mesh.uv_layers.new(name="UVMap")
            verts = mesh.vertices
            for poly in mesh.polygons:
                nx, ny, nz = (abs(c) for c in poly.normal)
                for li in poly.loop_indices:
                    co = verts[mesh.loops[li].vertex_index].co
                    if nz >= nx and nz >= ny:
                        uvl.data[li].uv = (co.x, co.y)
                    elif nx >= ny:
                        uvl.data[li].uv = (co.y, co.z)
                    else:
                        uvl.data[li].uv = (co.x, co.z)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        activate(obj)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        # NO merge-by-distance: touching blocks must stay separate closed pieces (see pitfalls.md).
        bpy.ops.mesh.normals_make_consistent(inside=False)
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
    """Transform for an upright sub-piece: kh = width scale, kv = height scale."""
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Diagonal((kh, kh, kv, 1.0)))

def place_lying(kh, klen, x, y, z, yaw=0.0):
    """Transform for a sub-piece lying along X: klen scales length, kh thickness."""
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(radians(yaw), 4, 'Z') @
            Matrix.Diagonal((klen, kh, kh, 1.0)))

def sx(s, a, b):
    """Mirror an x interval [a, b] written for the +x side to side s (+1 / -1). Works for y too."""
    return (a, b) if s > 0 else (-b, -a)

# ---------------------------------------------------------------- organic shapes
def rock(mb, r, center, size, slot=SLOT_NATURAL, subdiv=2):
    """Faceted boulder: jittered icosphere, flattened base."""
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

# ---------------------------------------------------------------- architecture
def wall(mb, axis, c, a0, a1, z0, height, thick, openings=(), slot=0):
    """Axis-aligned wall from boxes, with rectangular openings (no booleans).
    axis 'x': runs along X at y = c.  axis 'y': runs along Y at x = c.
    openings: (center, width, bottom, top) with bottom/top RELATIVE to z0."""
    def piece(b0, b1, za, zb):
        if b1 - b0 < 1e-4 or zb - za < 1e-4:
            return
        m, L = (b0 + b1) / 2, b1 - b0
        if axis == 'x':
            mb.box((m, c, (za + zb) / 2), (L, thick, zb - za), slot)
        else:
            mb.box((c, m, (za + zb) / 2), (thick, L, zb - za), slot)
    cur = a0
    for oc, w, b, t in sorted(openings):
        l, r_ = oc - w / 2, oc + w / 2
        piece(cur, l, z0, z0 + height)
        if b > 0:
            piece(l, r_, z0, z0 + min(b, height))
        if t < height:
            piece(l, r_, z0 + t, z0 + height)
        cur = r_
    piece(cur, a1, z0, z0 + height)

def frame_opening(mb, axis, face, inward, center, width, bottom, top, slot,
                  jamb=(0.08, 0.06), head=(0.10, 0.07), sill=(0.08, 0.08), sill_drop=0.08):
    """Trim around an opening on ONE wall face. face = coordinate of that face, inward = +1/-1 the
    direction the trim projects. (width, depth) per part; depths differ on purpose (no z-fighting).
    bottom/top are absolute z. sill=None for doors."""
    def r(a0, a1, d, z0, z1):
        f0, f1 = sorted((face, face + inward * d))
        if axis == 'y':
            mb.rect(f0, f1, a0, a1, z0, z1, slot)
        else:
            mb.rect(a0, a1, f0, f1, z0, z1, slot)
    l, rr = center - width / 2, center + width / 2
    jw, jd = jamb
    hw, hd = head
    r(l - jw, l, jd, bottom, top)
    r(rr, rr + jw, jd, bottom, top)
    r(l - jw, rr + jw, hd, top, top + hw)
    if sill:
        sw, sd = sill
        r(l - jw - 0.02, rr + jw + 0.02, sd, bottom - sill_drop, bottom)

def band(mb, axis, face, inward, a0, a1, z0, z1, depth, slot, gaps=()):
    """Horizontal trim band on one wall face (baseboard, dado, cornice, stripe). gaps: [(a0, a1)]."""
    f0, f1 = sorted((face, face + inward * depth))
    cur = a0
    for g0, g1 in sorted(gaps) + [(a1, a1)]:
        if g0 > cur:
            if axis == 'y':
                mb.rect(f0, f1, cur, min(g0, a1), z0, z1, slot)
            else:
                mb.rect(cur, min(g0, a1), f0, f1, z0, z1, slot)
        cur = max(cur, g1)

def block_walls(mb, x0, x1, y0, y1, z0, h, ops, thick=0.3, slot=0):
    """Four walls of a rectangular block. ops: {'front'|'back'|'left'|'right': [openings]}.
    front/back run along X (positions in X), left/right along Y. Side walls fit between."""
    wall(mb, 'x', y0 + thick / 2, x0, x1, z0, h, thick, ops.get('front', ()), slot)
    wall(mb, 'x', y1 - thick / 2, x0, x1, z0, h, thick, ops.get('back', ()), slot)
    wall(mb, 'y', x0 + thick / 2, y0 + thick, y1 - thick, z0, h, thick, ops.get('left', ()), slot)
    wall(mb, 'y', x1 - thick / 2, y0 + thick, y1 - thick, z0, h, thick, ops.get('right', ()), slot)

def slab_with_hole(mb, x0, x1, y0, y1, z0, z1, hole=None, slot=0, rim_slot=None):
    if not hole:
        mb.rect(x0, x1, y0, y1, z0, z1, slot)
        return
    hx0, hx1, hy0, hy1 = hole
    mb.rect(x0, x1, y0, hy0, z0, z1, slot)
    mb.rect(x0, x1, hy1, y1, z0, z1, slot)
    mb.rect(x0, hx0, hy0, hy1, z0, z1, slot)
    mb.rect(hx1, x1, hy0, hy1, z0, z1, slot)
    if rim_slot is not None:
        for (a, b_, c_, d) in ((hx0, hx1, hy0 - 0.15, hy0), (hx0, hx1, hy1, hy1 + 0.15)):
            mb.rect(a, b_, c_, d, z1, z1 + 0.04, rim_slot)

def runs(states):
    """Index ranges of consecutive 'ok' entries (length >= 2) - e.g. beams between intact posts."""
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
    """One exported FBX (root empty named `name`). Mesh buckets:
      S   -> "Structure_SOLID"  visible + mesh collider (walls, floors, furniture bodies)
      P   -> "Props_SOLID"      visible + mesh collider (loose clutter)
      D   -> "Detail"           visible, NO collider (fine/round detail: columns, lamps, foliage)
      COL -> "Detail_COL"       invisible simple collider proxy for D
    Empties: anchors -> "Anchors/Anchor_##" (grapple points, spawn points, sockets...)."""
    DETAIL_NAME = "Detail"
    ANCHOR_GROUP = "Anchors"

    def __init__(self, name, seed=1):
        self.name = name
        self.rng = random.Random(seed)
        self.S, self.D, self.COL, self.P = MB(), MB(), MB(), MB()
        self.anchors = []
        self.n = 0

    @property
    def C(self):              # legacy alias (Fathom scripts call the detail bucket C)
        return self.D

    def state(self, p_broken, p_missing, protect=False):
        """Seeded damage state: 'ok' | 'broken' | 'missing'."""
        if protect:
            return 'ok'
        r = self.rng.random()
        return 'missing' if r < p_missing else 'broken' if r < p_missing + p_broken else 'ok'

    def finish(self, anchors=False, anchor_group=None):
        if PREVIEW:
            PREVIEW_REGISTRY.append(self)
            return None, []
        mats = palette_materials()
        bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
        root = bpy.context.object
        root.name = self.name
        objs = [root]
        if self.S.v:
            objs.append(self.S.build("Structure_SOLID", mats, bevel=0.0, smooth_angle=30))   # no bevel: blocks abut
        if self.D.v:
            objs.append(self.D.build(self.DETAIL_NAME, mats, bevel=0.02))
            if self.COL.v:
                col = self.COL.build(self.DETAIL_NAME + "_COL", [collider_material()], uv=False, smooth_angle=0)
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
            ar.name = anchor_group or self.ANCHOR_GROUP
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
        return os.path.expanduser(custom or EXPORT_DIR)
    folder = f"{KIT_NAME}_FBX"
    if bpy.data.filepath:
        return os.path.join(os.path.dirname(bpy.data.filepath), folder)
    return os.path.join(os.path.expanduser("~"), folder)

def export_fbx(root, objs, out):
    """Unity-ready FBX: 1 m = 1 unit, Y up, no -90 X rotation, scale 1 on import."""
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

def export_palette(out):
    """<KIT_NAME>_palette.json next to the FBX files: lets the engine-side setup create the materials."""
    import json
    os.makedirs(out, exist_ok=True)
    mats = []
    for e in PALETTE:
        n, c, m, r = e[:4]
        spec = e[4] if len(e) > 4 else {}
        d = {"name": n, "color": list(c), "metallic": m, "roughness": r, "tile": spec.get("tile", 1.0)}
        if n in TEX_PATHS:
            d["albedo"] = n + "_albedo.png"; d["normal"] = n + "_normal.png"; d["mask"] = n + "_mask.png"
        mats.append(d)
    data = {"kit": KIT_NAME, "style": TEXTURE_STYLE, "materials": mats}
    with open(os.path.join(out, f"{KIT_NAME}_palette.json"), "w") as f:
        json.dump(data, f, indent=2)

# ---------------------------------------------------------------- Unity tools auto-install
UNITY_TOOLS = globals().get("UNITY_TOOLS", {})        # {ClassName: C# source}, embedded by assemble.py

def find_unity_project(path):
    """Walk up from the export folder to the folder holding Assets/ + ProjectSettings/."""
    p = os.path.abspath(path)
    while True:
        if os.path.isdir(os.path.join(p, "Assets")) and os.path.isdir(os.path.join(p, "ProjectSettings")):
            return p
        parent = os.path.dirname(p)
        if parent == p:
            return None
        p = parent

def install_unity_tools(out):
    """Install/update the embedded editor scripts. A file already declaring the same class (e.g. one the
    user copied by hand) is updated IN PLACE, so there are never duplicate classes."""
    import re
    if not UNITY_TOOLS:
        return
    proj = find_unity_project(out)
    if not proj:
        print("Unity tools: export folder is not inside a Unity project - not installed")
        return
    assets = os.path.join(proj, "Assets")
    where, legacy = {}, []
    for root, dirs, files in os.walk(assets):
        for fn in files:
            if not fn.endswith(".cs"):
                continue
            fp = os.path.join(root, fn)
            try:
                with open(fp, encoding="utf-8", errors="ignore") as f:
                    txt = f.read()
            except OSError:
                continue
            for cls in UNITY_TOOLS:
                if cls not in where and re.search(r"\b(class|struct)\s+%s\b" % cls, txt):
                    where[cls] = fp
            if re.search(r"\bclass\s+AtlantisModelPostprocessor\b", txt):
                legacy.append(fp)
    changed = []
    for cls, src in UNITY_TOOLS.items():
        path = where.get(cls) or os.path.join(assets, "KitTools", "Editor", cls + ".cs")
        try:
            with open(path, encoding="utf-8") as f:
                if f.read() == src:
                    continue
        except OSError:
            pass
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(src)
        changed.append(os.path.relpath(path, proj))
    print("Unity tools: " + (("updated " + ", ".join(changed)) if changed else "up to date"))
    for fp in legacy:
        print("Unity tools: WARNING old %s also adds colliders - delete it to avoid double colliders" % fp)

def run(builders, export=True, out_dir="", spacing=None):
    """Build + export each asset in turn (one FBX each), then lay them out in a row for viewing."""
    reset_scene()
    opts = cli_args()
    if PREVIEW:
        return [b() for b in builders]
    export = export and opts["export"]
    out = export_dir(opts["out"] or out_dir)
    built = []
    import tempfile
    generate_textures(out if export else os.path.join(tempfile.gettempdir(), KIT_NAME + "_textures"))
    if export:
        export_palette(out)
        install_unity_tools(out)
    for b in builders:
        root, objs = b()
        if export and (not opts["only"] or root.name in opts["only"]):
            export_fbx(root, objs, out)
        for o in objs[1:]:                      # free the short child names for the next asset
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
    n = sum(1 for r, _ in built if not opts["only"] or r.name in opts["only"])
    print(("Exported %d FBX -> %s" % (n, out)) if export else "Export skipped (--no-export)")
    return built
