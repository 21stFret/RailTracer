"""Rail Tracer mob reskin (Update 19.59): 1950s British mob on the NYC1950 pack skeletons.

  SK_CH01_TeddyBoy.fbx  (guards)  CH01's head, hands, legs and shoes in CH02's tailoring, restyled: CH02's shirt with
      its tie made slim and dark (Chr_Velvet); CH02's jacket opened up single-breasted (the double-breasted fronts
      pulled apart, the lapels narrowed with them, the front buttons swapped for three on one edge), lengthened to
      mid-thigh with a little swing, velvet collar, lapels and cuffs (Chr_Velvet), lined so the open fronts aren't
      see-through; drainpipe trousers (CH01's own pulled in below the thigh, Chr_Suit); two quiffs as separate meshes
      Hair_QuiffA / Hair_QuiffB (CH01's own hair cards swept up and forward; the game shows one per guard).
      Clean-shaven (CH01's beard and moustache off). The fedora, chain, watch and CH01's shirt above the chest are gone (its lower front shows in the opening).
  SK_CH02_FlatCap.fbx   (heavies) CH02 unchanged but the fedora: a flat cap (Trn_Felt) in its place.

Both stay ONE skinned mesh on the unchanged 67-bone skeleton (metres, scale 1, -Y forward, Z up); CH01 and CH02 share
that skeleton exactly, so CH02's clothes keep their own weights and UVs on CH01. Written next to the pack FBXs they
come from (gitignored with the packs). The lengthened skirt blends pelvis and both thighs so it swings instead of
splitting; the clothes are pushed out wherever CH01's body would poke through; the cap and quiff are 100% head (they
go with the head at Head Off).

Run:  blender -b --factory-startup --python railtracer_mob_reskin.py [-- --render <dir>]
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

# >>> CONFIG
ASSETS = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CH01 = os.path.join(ASSETS, "NYC1950_CH01", "Meshes", "SK_CH01_FullClothes.fbx")
CH02 = os.path.join(ASSETS, "NYC1950_CH02", "Meshes", "SK_CH02_FullClothes.fbx")
OUT01 = os.path.join(ASSETS, "NYC1950_CH01", "Meshes", "SK_CH01_TeddyBoy.fbx")
OUT02 = os.path.join(ASSETS, "NYC1950_CH02", "Meshes", "SK_CH02_FlatCap.fbx")
HEM = 0.70         # jacket hem: mid-thigh (CH02's jacket ends at 0.89)
STRETCH_FROM = 1.0 # the jacket below this height is stretched down to HEM (the pocket flaps sit above it)
GAP = 0.055        # half the opening between the fronts, below the lapels
TIE_W = 0.55       # the tie's width, as a share of CH02's
# <<< CONFIG

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    me = next(o for o in bpy.data.objects if o.type == 'MESH')
    return arm, me


def bone(arm, name):
    return arm.matrix_world @ arm.data.bones[name].head_local


def mat_slot(me, name):
    for i, m in enumerate(me.data.materials):
        if m.name == name:
            return i
    me.data.materials.append(bpy.data.materials.get(name) or bpy.data.materials.new(name))
    return len(me.data.materials) - 1


def delete_faces(me, pred):
    """pred(material name, world centre, polygon index) -> True to delete."""
    bm = bmesh.new(); bm.from_mesh(me.data)
    mats = [m.name for m in me.data.materials]
    kill = [f for f in bm.faces if pred(mats[f.material_index], me.matrix_world @ f.calc_center_median(), f.index)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me.data); bm.free()
    return len(kill)


def dominant(me):
    names = {g.index: g.name for g in me.vertex_groups}
    return [names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in me.data.vertices]


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def mix(*pairs):
    out = {}
    for w, d in pairs:
        for g, x in d.items():
            out[g] = out.get(g, 0.0) + w * x
    return out


class Part:
    """Geometry to add to a body mesh: verts with weights, faces with a material name and (optionally) UVs."""
    def __init__(self):
        self.v, self.w, self.f, self.m, self.uv, self.isl = [], [], [], [], [], []

    def add_v(self, p, w):
        self.v.append(Vector(p)); self.w.append(w); return len(self.v) - 1

    def face(self, idx, mat, want=None, uv=None, isl=None):
        """want: the direction the face should look (it is flipped to match), or None to keep the winding."""
        if want is not None:
            p = [self.v[i] for i in idx]
            nrm = (p[1] - p[0]).cross(p[2] - p[0]) + ((p[2] - p[0]).cross(p[3] - p[0]) if len(p) > 3 else Vector())
            if nrm.dot(want) < 0:
                idx = idx[::-1]; uv = uv[::-1] if uv else uv
        self.f.append(list(idx)); self.m.append(mat); self.uv.append(uv); self.isl.append(isl)

    def loft(self, rings, wfun, mat, closed=True, out=1):
        """rings: list of point lists (same length), quads between consecutive rings. out=1 faces away from each
        ring's centre, -1 towards it, or a Vector for a fixed direction."""
        ids = [[self.add_v(p, wfun(p)) for p in r] for r in rings]
        n = len(rings[0])
        for ra, a, b in zip(rings, ids, ids[1:]):
            c0 = sum(ra, Vector()) / n
            for i in range(n if closed else n - 1):
                j = (i + 1) % n
                q = [a[i], a[j], b[j], b[i]]
                fc = sum((self.v[k] for k in q), Vector()) / 4
                want = out if isinstance(out, Vector) else (fc - c0) * out
                self.face(q, mat, want)
        return ids

    def disc(self, centre, normal, r, wfun, mat, seg=10, depth=0.004):
        normal = Vector(normal).normalized()
        u = normal.orthogonal().normalized(); v = normal.cross(u)
        base = Vector(centre)
        rim0 = [base + r * (math.cos(t) * u + math.sin(t) * v) for t in (2 * math.pi * i / seg for i in range(seg))]
        rim1 = [p + normal * depth for p in rim0]
        self.loft([rim0, rim1], wfun, mat)
        top = [self.add_v(p, wfun(p)) for p in rim1]
        self.face(top, mat, normal)

    def normals(self):
        """Per-vertex normals (area-weighted face normals)."""
        n = [Vector() for _ in self.v]
        for f in self.f:
            p = [self.v[i] for i in f]
            fn = Vector()
            for k in range(1, len(p) - 1):
                fn += (p[k] - p[0]).cross(p[k + 1] - p[0])
            for i in f:
                n[i] += fn
        return [x.normalized() if x.length > 1e-12 else Vector((0, 0, 1)) for x in n]

    def tree(self, faces=None):
        fs = self.f if faces is None else [self.f[i] for i in faces]
        return BVHTree.FromPolygons(self.v, fs)

    def build(self, me):
        """Joins the part into the body mesh `me` (one skinned mesh), with its weights and UVs."""
        if not self.v:
            return
        m = me.data
        inv = me.matrix_world.inverted()
        bm = bmesh.new(); bm.from_mesh(m)
        dl = bm.verts.layers.deform.verify()
        uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new("DiffuseUV")
        gid = {g.name: g.index for g in me.vertex_groups}
        bv = []
        for p, w in zip(self.v, self.w):
            nv = bm.verts.new(inv @ p)
            tot = sum(w.values()) or 1.0
            for g, x in w.items():
                if x <= 1e-4:
                    continue
                if g not in gid:
                    gid[g] = me.vertex_groups.new(name=g).index
                nv[dl][gid[g]] = x / tot
            bv.append(nv)
        slots = {}
        for f, mat, uv in zip(self.f, self.m, self.uv):
            if mat not in slots:
                slots[mat] = mat_slot(me, mat)
            try:
                nf = bm.faces.new([bv[i] for i in f])
            except ValueError:
                continue  # duplicate face
            nf.material_index = slots[mat]; nf.smooth = True
            if uv:
                for loop, c in zip(nf.loops, uv):
                    loop[uvl].uv = c
        bm.to_mesh(m); bm.free()


def grab(path, keep):
    """The faces of a pack mesh that keep(island) takes, with their weights and UVs. An island is a connected run of
    faces of one material: dict(n, mat, lo, hi, mean) over its face centres (world)."""
    arm, me = load(path)
    m = me.data; mw = me.matrix_world
    names = [g.name for g in me.vertex_groups]
    mats = [x.name for x in m.materials]
    bm = bmesh.new(); bm.from_mesh(m); bm.faces.ensure_lookup_table()
    isl = [-1] * len(bm.faces); info = []
    for f in bm.faces:
        if isl[f.index] >= 0:
            continue
        k = len(info); st = [f]; isl[f.index] = k; comp = []
        while st:
            g = st.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if isl[h.index] < 0 and h.material_index == f.material_index:
                        isl[h.index] = k; st.append(h)
        cs = [mw @ g.calc_center_median() for g in comp]
        info.append(dict(id=k, n=len(comp), mat=mats[f.material_index], mean=sum(cs, Vector()) / len(cs),
                         lo=Vector([min(c[i] for c in cs) for i in range(3)]), hi=Vector([max(c[i] for c in cs) for i in range(3)])))
    bm.free()
    uvd = m.uv_layers.active.data
    part = Part(); vmap = {}
    for p in m.polygons:
        inf = info[isl[p.index]]
        if not keep(inf):
            continue
        ids = []
        for vi in p.vertices:
            if vi not in vmap:
                v = m.vertices[vi]
                vmap[vi] = part.add_v(mw @ v.co, {names[g.group]: g.weight for g in v.groups})
            ids.append(vmap[vi])
        part.face(ids, inf["mat"], uv=[tuple(uvd[l].uv) for l in p.loop_indices], isl=inf)
    return part


def push_out(part, tree, gap, idx, reach=0.025, iters=4):
    """Moves part verts (indices idx) out along the surface normal of `tree` until each is at least `gap` outside it;
    each push spreads to its neighbours within `reach` (fading), so the cloth bulges rather than dimples."""
    kd = KDTree(len(idx))
    for j, i in enumerate(idx):
        kd.insert(part.v[i], j)
    kd.balance()
    moved = 0
    for _ in range(iters):
        need = {}
        for i in idx:
            loc, nrm, _, _ = tree.find_nearest(part.v[i], 0.08)
            if loc is None:
                continue
            d = (part.v[i] - loc).dot(nrm)
            if d < gap:
                need[i] = nrm * (gap - d)
        if not need:
            break
        moved += len(need)
        add = {}
        for i, dv in need.items():
            for _, j, dd in kd.find_range(part.v[i], reach):
                k = idx[j]; cand = dv * (1 - dd / reach) if k != i else dv
                if k not in add or cand.length > add[k].length:
                    add[k] = cand
        for k, dv in add.items():
            part.v[k] = part.v[k] + dv
    return moved


def skirt_weights(p, w):
    """Below the waist: pelvis plus both thighs, more thigh lower down, blended across the middle so the skirt swings."""
    if p.z >= 1.02:
        return w
    t = smooth01(1.02, HEM, p.z)
    left = smooth01(-0.09, 0.09, p.x)
    skirt = {"pelvis": 1 - 0.6 * t, "thigh_l": 0.6 * t * left, "thigh_r": 0.6 * t * (1 - left)}
    k = smooth01(1.02, 0.94, p.z)
    return mix((1 - k, w), (k, skirt))


def velvet_cuffs(me, arm, depth=0.06):
    """The last `depth` of each jacket sleeve in velvet, split by a clean cut square to the forearm (picking whole
    faces leaves a sawtooth of long sleeve triangles that flickers between the two dark materials)."""
    bm = bmesh.new(); bm.from_mesh(me.data)
    mw = me.matrix_world; inv = mw.inverted()
    suit, velvet = mat_slot(me, "Chr_Suit"), mat_slot(me, "Chr_Velvet")
    for s in ("l", "r"):
        hand = bone(arm, "hand_" + s); elbow = bone(arm, "lowerarm_" + s)
        ax = (hand - elbow).normalized()
        sleeve = [f for f in bm.faces if f.material_index in (suit, velvet) and ((mw @ f.calc_center_median()) - hand).length < 0.2
                  and (mw @ f.calc_center_median() - elbow).dot(ax) > 0]
        end = max((mw @ v.co - elbow).dot(ax) for f in sleeve for v in f.verts)
        cut = elbow + ax * (end - depth)
        geom = list({e for f in sleeve for e in f.edges}) + sleeve + list({v for f in sleeve for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=inv @ cut, plane_no=(inv.to_3x3() @ ax).normalized(), dist=1e-5)
        for f in bm.faces:
            if f.material_index in (suit, velvet):
                c = mw @ f.calc_center_median()
                if (c - hand).length < 0.2 and (c - elbow).dot(ax) > 0:
                    f.material_index = velvet if (c - cut).dot(ax) > 0 else suit
    bm.to_mesh(me.data); bm.free()


# ---------------------------------------------------------------------------------------------- CH01: the Teddy Boy
def is_button(i):
    return i["n"] <= 24 and (i["hi"] - i["lo"]).length < 0.035


def is_pocket_square(i):
    return i["lo"].z > 1.435 and i["hi"].z < 1.49 and abs(i["mean"].x) > 0.1 and 28 <= i["n"] <= 46 and i["hi"].z - i["lo"].z > 0.025


def is_brow(c):
    return c.y < -0.075 and 1.745 < c.z < 1.80


def is_beard(c):
    return c.z < 1.725 and c.y < 0.03 and not (abs(c.x) > 0.062 and c.z > 1.69)   # sideburns stay


def subset(part, faces):
    """A new Part with just these faces (and their verts)."""
    out = Part(); vm = {}
    for k in faces:
        ids = []
        for v in part.f[k]:
            if v not in vm:
                vm[v] = out.add_v(part.v[v], part.w[v])
            ids.append(vm[v])
        out.face(ids, part.m[k], uv=part.uv[k], isl=part.isl[k])
    return out


# (lift, forward roll, extra width, sideways sweep) at the front of the crown, fading to nothing at the back
QUIFF_STYLES = {"A": (0.030, 0.024, 0.06, 0.0),      # a neat pompadour, rolled forward
                "B": (0.017, 0.012, 0.12, -0.024)}   # lower and fuller, swept over to one side


def make_quiffs():
    """Two quiff meshes (Hair_QuiffA / Hair_QuiffB) from CH01's scalp hair: its strand cards bent by a smooth field
    (lift and forward roll growing towards the front of the crown, nothing at the back or the sides below the temples,
    so it blends into the existing hair), over a core: the scalp under the hair, lifted with them, in hair colour, so
    the skin doesn't show between the lifted cards. The game shows one per guard."""
    hair = grab(CH01, lambda i: i["mat"] == "Chr_Hair" and not is_brow(i["mean"]) and not is_beard(i["mean"]))
    skin = grab(CH01, lambda i: i["mat"] == "Chr_Skin")
    head = {"head": 1.0}
    hair.w = [dict(head) for _ in hair.v]
    top = max(p.z for p in hair.v)
    cap = [p for p in hair.v if p.z > top - 0.03]
    yf, yb = min(p.y for p in cap), max(p.y for p in cap)
    yc = (yf + yb) / 2
    kd = KDTree(len(hair.v))
    for k, p in enumerate(hair.v):
        kd.insert(p, k)
    kd.balance()
    scalp = [k for k, f in enumerate(skin.f) for c in [sum((skin.v[v] for v in f), Vector()) / len(f)]
             if c.z > 1.74 and c.y > yf + 0.01 and kd.find(c)[2] < 0.012]
    core = subset(skin, scalp)
    nrm = core.normals()
    core.v = [p + n * 0.002 for p, n in zip(core.v, nrm)]
    core.w = [dict(head) for _ in core.v]
    core.m = ["Chr_Hair"] * len(core.f)
    out = {}
    for name, (H, F, W, S) in QUIFF_STYLES.items():
        def bend(p):
            h = smooth01(top - 0.085, top - 0.015, p.z)          # the top of the head only
            t = smooth01(yc + 0.045, yf + 0.004, p.y)            # 0 behind the crown -> 1 at the front hairline
            return Vector((p.x * (1 + W * h * t) + S * h * t * t, p.y - F * h * t * t, p.z + H * h * t))
        q = Part()
        for src in (hair, core):
            n = len(q.v)
            q.v += [bend(p) for p in src.v]; q.w += [dict(w) for w in src.w]
            for f, mt, uv in zip(src.f, src.m, src.uv):
                q.face([v + n for v in f], mt, uv=uv)
        out["Hair_Quiff" + name] = q
        print("CH01 quiff %s tris %d" % (name, sum(len(f) - 2 for f in q.f)))
    return out


def teddy_boy():
    # --- CH02's jacket (not its trousers, front / cuff buttons or pocket square) and shirt with tie
    jacket = grab(CH02, lambda i: i["mat"] == "Chr_Suit" and i["hi"].z > 1.09 and not is_button(i) and not is_pocket_square(i))
    shirt = grab(CH02, lambda i: i["mat"] == "Chr_Shirt")
    quiffs = make_quiffs()   # CH01's own strand cards swept up and forward (two styles), over a hair-coloured core

    arm, me = load(CH01)
    m = me.data; mw = me.matrix_world
    co = [mw @ v.co for v in m.vertices]
    dom = dominant(me)
    head = {"head": 1.0}
    hands = {s: bone(arm, "hand_" + s) for s in ("l", "r")}

    # --- drainpipes: CH01's trousers, pulled in towards each leg's centre below the thigh.
    suit = {i for p in m.polygons if m.materials[p.material_index].name == "Chr_Suit" for i in p.vertices}
    shoe_pts = [co[i] for p in m.polygons if m.materials[p.material_index].name == "Chr_Shoes" for i in p.vertices]
    def leg_r(z):  # target radius (narrow, straight; never inside the shoe's collar)
        pts = [(0.80, 1.0), (0.72, 0.090), (0.53, 0.066), (0.30, 0.056), (0.0, 0.054)]
        for (z0, r0), (z1, r1) in zip(pts[1:], pts):
            if z <= z1:
                t = (z - z0) / (z1 - z0) if z1 != z0 else 0
                return r0 + (r1 - r0) * max(0, min(1, t))
        return 1.0
    shoe_r = max(abs(math.hypot(p.x - math.copysign(0.171, p.x), p.y - 0.06)) for p in shoe_pts if 0.10 < p.z < 0.13)
    for side in (1, -1):
        ids = [i for i in suit if co[i].x * side > 0.01 and co[i].z < 0.85]
        for i in ids:
            p = co[i]
            sl = [co[j] for j in ids if abs(co[j].z - p.z) < 0.02]
            cx = sum(q.x for q in sl) / len(sl); cy = sum(q.y for q in sl) / len(sl)
            d = Vector((p.x - cx, p.y - cy)); r = d.length
            tgt = leg_r(p.z)
            if p.z < 0.16:
                tgt = max(tgt, shoe_r + 0.004)
            if r > tgt:
                d *= (tgt + (r - tgt) * 0.15) / r  # soft clamp: keeps a hint of the original folds
                m.vertices[i].co = mw.inverted() @ Vector((cx + d.x, cy + d.y, p.z))

    # --- off: fedora, chain, watch, CH01's shirt and vest, and the skin they covered (CH02's shirt covers it now;
    # the wrists, neck and head stay).
    polys = [list(p.vertices) for p in m.polygons]
    def covered(mat, c, fi):
        if mat in ("Trn_Felt", "Trn_Brass"):
            return True
        if mat == "Chr_Shirt":   # CH02's shirt stops under the chest (its jacket hid the rest): CH01's fills the opening below
            return c.z > 1.40 or c.y > -0.03 or dom[polys[fi][0]].startswith(("upperarm", "lowerarm", "clavicle", "head"))
        if mat == "Chr_Hair":     # only the brows stay on the body: the scalp hair goes to the quiffs, the beard goes
            return not is_brow(c)
        if mat != "Chr_Skin" or c.z > 1.585:
            return False
        d = dom[polys[fi][0]]
        if d.startswith(("spine", "clavicle", "upperarm", "pelvis")):
            return True
        if d.startswith("lowerarm"):
            return (c - hands[d[-1]]).length > 0.07
        return False
    print("CH01 removed faces:", delete_faces(me, covered))

    body_faces = [list(p.vertices) for p in m.polygons if m.materials[p.material_index].name in ("Chr_Skin", "Chr_Suit", "Chr_Shoes", "Chr_Shirt")]
    body = BVHTree.FromPolygons([mw @ v.co for v in m.vertices], body_faces)

    # --- the shirt: tie slim and dark
    tie_isl = {id(i) for i in shirt.isl if i and abs(i["mean"].x) < 0.025 and i["lo"].y < -0.09 and i["hi"].z - i["lo"].z > 0.12 and i["lo"].z > 1.1}
    tie_v = {v for f, i in zip(shirt.f, shirt.isl) if id(i) in tie_isl for v in f}
    for v in tie_v:
        p = shirt.v[v]; shirt.v[v] = Vector((p.x * TIE_W, p.y, p.z))
    for k, i in enumerate(shirt.isl):
        if id(i) in tie_isl:
            shirt.m[k] = "Chr_Velvet"
    print("CH01 tie islands", len(tie_isl), "verts", len(tie_v))
    print("CH01 shirt pushed", push_out(shirt, body, 0.004, [i for i in range(len(shirt.v)) if i not in tie_v]))

    # --- the jacket: which bits are which
    J = jacket
    panels = [i for i in {id(x): x for x in J.isl}.values() if i["n"] > 200 and i["mean"].y < 0.05 and abs(i["mean"].x) < 0.25]
    sleeves = {id(i) for i in J.isl if i["n"] > 200 and abs(i["mean"].x) > 0.3}
    collar = {id(i) for i in J.isl if i["lo"].z >= 1.555}
    lapels = {id(i) for i in J.isl if i["lo"].z >= 1.2 and i["hi"].z <= 1.58 and i["hi"].z - i["lo"].z > 0.1 and i["mean"].y < -0.03 and i["n"] < 90}
    print("CH01 jacket panels %d sleeves %d collar %d lapels %d" % (len(panels), len(sleeves), len(collar), len(lapels)))
    visl = [None] * len(J.v)
    for f, i in zip(J.f, J.isl):
        for v in f:
            visl[v] = i
    side_of = {}
    pv = [(v, 1 if i["mean"].x > 0 else -1) for v in range(len(J.v)) for i in [visl[v]] if any(i is p for p in panels)]
    kd = KDTree(len(pv))
    for j, (v, s) in enumerate(pv):
        kd.insert(J.v[v], j)
    kd.balance()
    for v in range(len(J.v)):
        i = visl[v]
        if id(i) in sleeves or id(i) in collar or J.v[v].y > 0.0:
            continue   # only the fronts open
        # lapels go with their own front; everything else with the panel it lies on (its own, for panel verts)
        side_of[v] = (1 if i["mean"].x > 0 else -1) if id(i) in lapels else pv[kd.find(J.v[v])[1]][1]
    # faces joining the two fronts (a strip running across the middle) would bridge the opening: cut them
    keep = [k for k, f in enumerate(J.f) if len({side_of.get(v, 0) for v in f} - {0}) < 2]
    print("CH01 faces cut across the opening:", len(J.f) - len(keep))
    J.f, J.m, J.uv, J.isl = [J.f[k] for k in keep], [J.m[k] for k in keep], [J.uv[k] for k in keep], [J.isl[k] for k in keep]

    # --- open it up: each front pulled away from the middle so its edge sits GAP off centre (below the lapels, where
    # the double-breasted fronts overlapped); the squeeze fades out towards the side seams and round to the back.
    U0 = 0.165
    zgrid = [0.86 + 0.02 * k for k in range(42)]
    edge = {}
    for s in (1, -1):
        e = []
        for z in zgrid:
            us = [s * J.v[v].x for v, sv in side_of.items() if sv == s and J.v[v].y < -0.02 and abs(J.v[v].z - z) < 0.02]
            e.append(min(us) if us else None)
        for k in range(len(e)):
            if e[k] is None:
                e[k] = next((x for x in e[k:] if x is not None), None) or next(x for x in reversed(e[:k]) if x is not None)
        for _ in range(2):
            e = [(e[max(k - 1, 0)] + e[k] + e[min(k + 1, len(e) - 1)]) / 3 for k in range(len(e))]
        edge[s] = e
    def edge_at(s, z):
        t = min(max((z - zgrid[0]) / 0.02, 0), len(zgrid) - 1.001)
        k = int(t); return edge[s][k] + (edge[s][k + 1] - edge[s][k]) * (t - k)
    for v, s in side_of.items():
        p = J.v[v]; g = smooth01(0.03, -0.05, p.y)
        u = s * p.x; ue = edge_at(s, p.z)
        if g == 0 or ue >= GAP or u >= U0:
            continue
        u2 = U0 - (U0 - u) * (U0 - GAP) / (U0 - ue)
        J.v[v] = Vector((s * (u + (u2 - u) * g), p.y, p.z))
    print("CH01 front edges (+x, -x) at the waist:", round(edge_at(1, 1.1), 3), round(edge_at(-1, 1.1), 3))

    # --- longer: everything below STRETCH_FROM stretched down to HEM, swinging out a little; skirt weights
    lo = min(p.z for p in J.v)
    K = (STRETCH_FROM - HEM) / (STRETCH_FROM - lo)
    for v, p in enumerate(J.v):
        if p.z < STRETCH_FROM and id(visl[v]) not in sleeves:
            z = STRETCH_FROM - (STRETCH_FROM - p.z) * K
            t = smooth01(STRETCH_FROM, HEM, z)
            J.v[v] = Vector((p.x * (1 + 0.07 * t), 0.012 + (p.y - 0.012) * (1 + 0.07 * t), z))
            J.w[v] = skirt_weights(J.v[v], J.w[v])

    # --- velvet: collar and lapels (the cuffs are cut cleanly after the build: see velvet_cuffs)
    for k, i in enumerate(J.isl):
        if id(i) in collar or id(i) in lapels:
            J.m[k] = "Chr_Velvet"

    # --- fit over CH01's body and the shirt
    under = Part(); under.v = list(shirt.v); under.f = list(shirt.f)
    off = len(under.v)
    under.v += [mw @ v.co for v in m.vertices]; under.f += [[a + off for a in f] for f in body_faces]
    print("CH01 jacket pushed", push_out(J, under.tree(), 0.005, list(range(len(J.v)))))

    # --- lining: the inside of the fronts and the skirt, so the open coat isn't see-through
    nrm = J.normals()
    line_faces = [k for k, (f, i) in enumerate(zip(J.f, J.isl)) if id(i) not in sleeves and id(i) not in collar
                  and (any(i is p for p in panels) or any(J.v[v].z < 1.05 for v in f))]
    remap = {}
    for k in line_faces:
        for v in J.f[k]:
            if v not in remap:
                remap[v] = J.add_v(J.v[v] - nrm[v] * 0.003, J.w[v])
    for k in line_faces:
        J.face([remap[v] for v in reversed(J.f[k])], "Chr_Suit", uv=list(reversed(J.uv[k])) if J.uv[k] else None)

    # --- three buttons down the right front (the wearer's right, -x), just in from its edge
    jt = J.tree()
    btn = Part()
    for z in (1.27, 1.17, 1.07):
        x = -(GAP + 0.02)
        hit, n, _, _ = jt.ray_cast(Vector((x, -0.6, z)), Vector((0, 1, 0)))
        if hit:
            btn.disc(hit - Vector((0, 0.001, 0)), (0, -1, 0), 0.008, lambda p: skirt_weights(p, {"spine_01": 1.0}), "Chr_Velvet")

    for part in (shirt, J, btn):
        part.build(me)
    velvet_cuffs(me, arm)
    print("CH01 shirt tris ~%d, jacket ~%d" % tuple(sum(len(f) - 2 for f in p.f) for p in (shirt, J)))
    finish(arm, me, OUT01, quiffs)


# --------------------------------------------------------------------------------------------- CH02: the flat cap
def flat_cap():
    arm, me = load(CH02)
    m = me.data
    hat = [me.matrix_world @ m.vertices[i].co for p in m.polygons if m.materials[p.material_index].name == "Trn_Felt" for i in p.vertices]
    hair = [me.matrix_world @ m.vertices[i].co for p in m.polygons if m.materials[p.material_index].name == "Chr_Hair" for i in p.vertices]
    n = delete_faces(me, lambda mat, c, fi: mat == "Trn_Felt")
    print("CH02 removed fedora faces:", n)
    band = min(p.z for p in hat) + 0.024   # a flat cap sits higher than the fedora's band
    skull = [p for p in hair if p.z > band]
    cx = (min(p.x for p in skull) + max(p.x for p in skull)) / 2
    cy = (min(p.y for p in skull) + max(p.y for p in skull)) / 2
    rx = max(abs(p.x - cx) for p in skull) + 0.012
    ry = max(abs(p.y - cy) for p in skull) + 0.012
    top = max(p.z for p in hair)
    head = {"head": 1.0}
    cap = Part()
    S = 28
    zt = top + 0.006                                # crown top at the back; it slopes down to the front
    def ztop(y):
        return zt - 0.32 * max(0.0, cy - y)
    def at(a, grow, z_of, fwd_push=0.0):
        c, s = math.cos(a), math.sin(a)
        fwd = max(0.0, -s)
        x = cx + rx * grow * c
        y = cy + ry * grow * s - fwd_push * fwd
        return Vector((x, y, z_of(x, y, s)))
    def band_z(x, y, s):                            # brim line: low on the brow, snug and higher at the back
        return band - 0.004 + 0.012 * max(0.0, s) - 0.008 * max(0.0, -s)
    angles = [2 * math.pi * i / S for i in range(S)]
    rings = [
        [at(a, 1.0, band_z) for a in angles],
        [at(a, 1.07, lambda x, y, s: band_z(x, y, s) + 0.5 * (ztop(y) - band_z(x, y, s)), 0.025) for a in angles],
        [at(a, 1.12, lambda x, y, s: ztop(y) - 0.012, 0.05) for a in angles],   # the soft roll at the crown's edge
        [at(a, 0.98, lambda x, y, s: ztop(y), 0.045) for a in angles],
        [at(a, 0.6, lambda x, y, s: ztop(y) + 0.004, 0.015) for a in angles],
    ]
    cap.loft(rings, lambda p: head, "Trn_Felt")
    tip = Vector((cx, cy, ztop(cy) + 0.005)); ti = cap.add_v(tip, head); last = [cap.add_v(p, head) for p in rings[-1]]
    for i in range(S):
        cap.face([last[i], last[(i + 1) % S], ti], "Trn_Felt", Vector((0, 0, 1)))
    # inner band so the open bottom isn't see-through from below
    inner = [Vector((cx + (p.x - cx) * 0.94, cy + (p.y - cy) * 0.94, p.z + 0.012)) for p in rings[0]]
    cap.loft([rings[0], inner], lambda p: head, "Trn_Felt", out=-1)
    # peak: a short stiff brim tucked under the crown's front, angled a little down
    front = [p for p in rings[0] if p.y < cy - 0.3 * ry]
    front.sort(key=lambda p: p.x)
    outer = []
    for p in front:
        d = Vector((p.x - cx, p.y - cy, 0)).normalized()
        depth = 0.058 * max(0.0, 1 - ((p.x - cx) / (rx * 1.02)) ** 2) ** 0.5
        outer.append(p + d * depth + Vector((0, 0, -0.012 * depth / 0.058)))
    th = Vector((0, 0, 0.006))
    rows = [[p + th for p in front], [p + th for p in outer], outer, front]
    ids = [[cap.add_v(p, head) for p in r] for r in rows]
    for a, b in zip(ids, ids[1:] + ids[:1]):
        for i in range(len(front) - 1):
            q4 = [a[i], a[i + 1], b[i + 1], b[i]]
            col = sum((cap.v[r[k]] for r in ids for k in (i, i + 1)), Vector()) / 8
            cap.face(q4, "Trn_Felt", sum((cap.v[k] for k in q4), Vector()) / 4 - col)
    cap.build(me)
    print("CH02 cap tris ~%d" % (2 * len(cap.f)))
    finish(arm, me, OUT02)


def finish(arm, me, out, extra=None):
    """Exports the armature, the body mesh and any extra meshes ({name: Part}, skinned to the same armature)."""
    objs = [me]
    for name, part in (extra or {}).items():
        ob = bpy.data.objects.new(name, bpy.data.meshes.new(name))
        bpy.context.scene.collection.objects.link(ob)
        ob.parent = arm; ob.matrix_world = me.matrix_world.copy()
        ob.modifiers.new("Armature", 'ARMATURE').object = arm
        part.build(ob)
        objs.append(ob)
    m = me.data
    used = sorted({p.material_index for p in m.polygons})   # drop the slots nothing wears now (fedora, chain...)
    remap = {o: n for n, o in enumerate(used)}
    idx = [remap[p.material_index] for p in m.polygons]
    keep = [m.materials[i] for i in used]
    m.materials.clear()
    for mt in keep:
        m.materials.append(mt)
    for p, i in zip(m.polygons, idx):
        p.material_index = i
    me.data.validate()
    me.data.update()
    tris = sum(len(p.vertices) - 2 for p in me.data.polygons)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for ob in objs:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')
    print("EXPORTED", out, "tris", tris, "materials", [m.name for m in me.data.materials], "meshes", [o.name for o in objs])
    if RENDER:
        render(me, os.path.join(RENDER, os.path.splitext(os.path.basename(out))[0]))


def render(me, base):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items} else 'BLENDER_WORKBENCH'
    scn.render.resolution_x, scn.render.resolution_y = 640, 900
    cols = {"Chr_Suit": (0.04, 0.04, 0.05), "Chr_Velvet": (0.012, 0.012, 0.016), "Chr_Shirt": (0.7, 0.66, 0.55),
            "Chr_Skin": (0.6, 0.36, 0.24), "Chr_Hair": (0.05, 0.025, 0.01), "Chr_Shoes": (0.01, 0.01, 0.01),
            "Trn_Felt": (0.12, 0.1, 0.08), "Chr_Eye": (0.02, 0.02, 0.02), "Chr_Glasses": (0, 0, 0), "Chr_Mouth": (0.3, 0.1, 0.1),
            "Trn_Brass": (0.6, 0.45, 0.1)}
    if "Chr_Suit" in cols and "CH02" in me.name:
        cols["Chr_Suit"] = (0.35, 0.02, 0.02)
    for m in me.data.materials:
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*cols.get(m.name, (0.5, 0.5, 0.5)), 1)
            bsdf.inputs["Roughness"].default_value = 0.8
    w = bpy.data.worlds.new("w"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.55, 0.6, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN')); scn.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); scn.camera = cam
    cam.data.type = 'ORTHO'
    quiffs = sorted((o for o in bpy.data.objects if o.name.startswith("Hair_Quiff")), key=lambda o: o.name)
    for o in quiffs[1:]:   # the other styles: head shots only
        quiffs[0].hide_render = True; o.hide_render = False
        for name, (yaw, pitch) in {"head": (-30, 15), "headside": (90, 5), "headback": (160, 10)}.items():
            cam.data.ortho_scale = 0.55
            y, p = math.radians(yaw), math.radians(pitch)
            d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
            cam.location = Vector((0, 0, 1.7)) + d * 6
            cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
            scn.render.filepath = base + "_" + name + "_" + o.name[-1] + ".png"
            bpy.ops.render.render(write_still=True)
        o.hide_render = True; quiffs[0].hide_render = False
    for name, (yaw, pitch, scale, tgt) in {"front": (0, 8, 2.1, 0.95), "side": (90, 8, 2.1, 0.95), "game": (-25, 50, 2.3, 0.9),
                                           "back": (180, 15, 2.1, 0.95), "head": (-30, 15, 0.55, 1.7), "headside": (90, 5, 0.55, 1.7),
                                           "headback": (160, 10, 0.55, 1.7), "chest": (-20, 12, 1.0, 1.3)}.items():
        cam.data.ortho_scale = scale
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        cam.location = Vector((0, 0, tgt)) + d * 6
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = base + "_" + name + ".png"
        bpy.ops.render.render(write_still=True)
    # a long stride (legs far apart) from the side and the game camera: the skirt should swing, not split
    from mathutils import Matrix
    arm = me.parent
    for bn, ang in (("thigh_l", -40), ("thigh_r", 30)):
        pb = arm.pose.bones[bn]; h = arm.matrix_world @ pb.bone.head_local
        pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(ang), 4, 'X') @ Matrix.Translation(-h) @ pb.matrix
        bpy.context.view_layer.update()
    for name, (yaw, pitch) in {"stride_side": (90, 8), "stride_game": (-25, 50), "stride_front": (0, 8)}.items():
        cam.data.ortho_scale = 2.2
        y, p = math.radians(yaw), math.radians(pitch)
        d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
        cam.location = Vector((0, 0, 0.95)) + d * 6
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = base + "_" + name + ".png"
        bpy.ops.render.render(write_still=True)


def check(out, original):
    """Re-import the export: same bones and rest pose as the original, every vertex weighted."""
    _, me = load(out); arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    rest = {b.name: (b.matrix_local.copy(), b.head_local.copy()) for b in arm.data.bones}
    unweighted = sum(1 for v in me.data.vertices if not v.groups)
    load(original); arm0 = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    worst = max(max(abs(a - b) for ra, rb in zip(rest[b0.name][0], b0.matrix_local) for a, b in zip(ra, rb)) for b0 in arm0.data.bones)
    print("CHECK %s bones=%d/%d max rest diff=%.6f unweighted verts=%d" % (os.path.basename(out), len(rest), len(arm0.data.bones), worst, unweighted))


flat_cap()
teddy_boy()
check(OUT02, CH02)
check(OUT01, CH01)
