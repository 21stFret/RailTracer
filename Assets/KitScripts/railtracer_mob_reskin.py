"""Rail Tracer mob reskin (Update 19.59): 1950s British mob on the NYC1950 pack skeletons.

  SK_CH01_TeddyBoy.fbx  (guards)  CH01 body: knee-length single-breasted drape coat (Chr_Suit) with a narrow velvet
      collar, lapels, cuffs and buttons (Chr_Velvet), cream shirt front (Chr_Shirt) and a slim tie (Chr_Velvet),
      drainpipe trousers (CH01's own trousers pulled in below the thigh, Chr_Suit), a quiff (Chr_Hair). The fedora,
      shirt, chain and watch are gone.
  SK_CH02_FlatCap.fbx   (heavies) CH02 unchanged but the fedora: a flat cap (Trn_Felt) in its place.

Both stay ONE skinned mesh on the unchanged 67-bone skeleton (metres, scale 1, -Y forward, Z up), written next to the
pack FBX they come from (the packs are gitignored: these are derived from them, so they stay out of git too).
Garments are weighted from the body (nearest face, interpolated); the coat's skirt blends pelvis and both thighs so it
swings instead of splitting; the cap and quiff are 100% head (they go with the head at Head Off).

Run:  blender -b --factory-startup --python railtracer_mob_reskin.py [-- --render <dir>]
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

# >>> CONFIG
ASSETS = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CH01 = os.path.join(ASSETS, "NYC1950_CH01", "Meshes", "SK_CH01_FullClothes.fbx")
CH02 = os.path.join(ASSETS, "NYC1950_CH02", "Meshes", "SK_CH02_FullClothes.fbx")
OUT01 = os.path.join(ASSETS, "NYC1950_CH01", "Meshes", "SK_CH01_TeddyBoy.fbx")
OUT02 = os.path.join(ASSETS, "NYC1950_CH02", "Meshes", "SK_CH02_FlatCap.fbx")
HEM_Z = 0.47          # coat hem: knee length (knee joint at 0.53)
COAT_EASE = 0.02     # how loose the coat hangs off the body
SHOULDER_X = 0.215    # padded shoulder half-width
N = 40                # verts per coat ring
# <<< CONFIG

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER = argv[argv.index("--render") + 1] if "--render" in argv else None

ARM = ("upperarm", "lowerarm", "hand", "index", "middle", "ring", "pinky", "thumb")
LEG = ("thigh", "calf", "foot", "ball")


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
    bm = bmesh.new(); bm.from_mesh(me.data)
    mats = [m.name for m in me.data.materials]
    kill = [f for f in bm.faces if pred(mats[f.material_index], me.matrix_world @ f.calc_center_median())]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(me.data); bm.free()
    return len(kill)


def dominant(me):
    names = {g.index: g.name for g in me.vertex_groups}
    return [names[max(v.groups, key=lambda g: g.weight).group] if v.groups else "" for v in me.data.vertices]


class Source:
    """The body before anything is removed: BVH trees of its faces (all / not arms / arms) and their weights."""
    def __init__(self, me):
        self.mw = me.matrix_world
        m = me.data
        self.groups = [g.name for g in me.vertex_groups]
        self.co = [self.mw @ v.co for v in m.vertices]
        self.w = [{self.groups[g.group]: g.weight for g in v.groups} for v in m.vertices]
        self.dom = dominant(me)
        self.mat = [m.materials[p.material_index].name for p in m.polygons]
        self.vmat = {}
        for p, mt in zip(m.polygons, self.mat):
            for i in p.vertices:
                self.vmat[i] = mt
        self.polys = [list(p.vertices) for p in m.polygons]
        def tree(keep):
            idx = [i for i, p in enumerate(self.polys) if keep(i)]
            return BVHTree.FromPolygons(self.co, [self.polys[i] for i in idx]), idx
        isarm = lambda i: self.dom[self.polys[i][0]].startswith(ARM)
        self.torso = tree(lambda i: not isarm(i) and self.mat[i] != "Chr_Hair" and self.mat[i] != "Trn_Felt")
        self.arms = tree(isarm)

    def weights(self, p, which):
        t, idx = which
        loc, _, fi, _ = t.find_nearest(p)
        poly = self.polys[idx[fi]]
        ws = poly_3d_calc([self.co[i] for i in poly], loc)
        out = {}
        for vi, a in zip(poly, ws):
            for g, w in self.w[vi].items():
                out[g] = out.get(g, 0.0) + a * w
        return out

    def pts(self, keep):
        return [c for c, d in zip(self.co, self.dom) if keep(d)]


class Part:
    """Garment geometry: verts with their weights, faces with a material name."""
    def __init__(self):
        self.v, self.w, self.f, self.m = [], [], [], []

    def add_v(self, p, w):
        self.v.append(Vector(p)); self.w.append(w); return len(self.v) - 1

    def face(self, idx, mat, want=None):
        """want: the direction the face should look (it is flipped to match), or None to keep the winding."""
        if want is not None:
            p = [self.v[i] for i in idx]
            nrm = (p[1] - p[0]).cross(p[2] - p[0]) + ((p[2] - p[0]).cross(p[3] - p[0]) if len(p) > 3 else Vector())
            if nrm.dot(want) < 0:
                idx = idx[::-1]
        self.f.append(idx); self.m.append(mat)

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

    def disc(self, centre, normal, r, wfun, mat, seg=8, depth=0.004):
        normal = Vector(normal).normalized()
        u = normal.orthogonal().normalized(); v = normal.cross(u)
        base = Vector(centre)
        rim0 = [base + r * (math.cos(t) * u + math.sin(t) * v) for t in (2 * math.pi * i / seg for i in range(seg))]
        rim1 = [p + normal * depth for p in rim0]
        self.loft([rim0, rim1], wfun, mat)
        top = [self.add_v(p, wfun(p)) for p in rim1]
        self.face(top, mat, normal)

    def build(self, me):
        """Joins the part into the body mesh `me` (one skinned mesh), with its weights."""
        if not self.v:
            return
        m = me.data
        inv = me.matrix_world.inverted()
        bm = bmesh.new(); bm.from_mesh(m)
        dl = bm.verts.layers.deform.verify()
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
        for f, mat in zip(self.f, self.m):
            if mat not in slots:
                slots[mat] = mat_slot(me, mat)
            try:
                nf = bm.faces.new([bv[i] for i in f])
                nf.material_index = slots[mat]; nf.smooth = True
            except ValueError:
                pass  # duplicate face
        bm.to_mesh(m); bm.free()


def ext(pts, z, dz=0.035):
    """Half-width, front depth and back depth (about y=yc) of the points in a height slab."""
    s = [p for p in pts if abs(p.z - z) < dz]
    if not s:
        return None
    yc = (min(p.y for p in s) + max(p.y for p in s)) / 2
    return max(abs(p.x) for p in s), yc - min(p.y for p in s), max(p.y for p in s) - yc, yc


def superellipse(a, bf, bb, yc, z, e=2.6, n=N):
    """A ring around (0, yc) at height z; angle 0 = +x, front is -y."""
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        c, s = math.cos(t), math.sin(t)
        b = bf if s < 0 else bb
        r = (abs(c / a) ** e + abs(s / b) ** e) ** (-1 / e)
        out.append(Vector((r * c, yc + r * s, z)))
    return out


def lerp_keys(keys, z):
    """keys: [(z, (a, bf, bb, yc, e)), ...] ascending; linear in z."""
    if z <= keys[0][0]:
        return keys[0][1]
    for (z0, k0), (z1, k1) in zip(keys, keys[1:]):
        if z <= z1:
            t = (z - z0) / (z1 - z0)
            return tuple(x + (y - x) * t for x, y in zip(k0, k1))
    return keys[-1][1]


def front_y(k, x):
    """Front surface y of a coat ring with params k at offset x (|x| < a)."""
    a, bf, bb, yc, e = k
    q = max(0.0, 1 - abs(x / a) ** e)
    return yc - bf * q ** (1 / e)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def mix(*pairs):
    out = {}
    for w, d in pairs:
        for g, x in d.items():
            out[g] = out.get(g, 0.0) + w * x
    return out


# ---------------------------------------------------------------------------------------------- CH01: the Teddy Boy
def teddy_boy():
    arm, me = load(CH01)
    src = Source(me)
    head = {"head": 1.0}

    torso_pts = src.pts(lambda d: d and not d.startswith(ARM) and d not in ("head",))
    hips = ext(torso_pts, 0.93, 0.05)

    # --- drainpipes: CH01's trousers, pulled in towards each leg's centre below the thigh.
    m = me.data
    suit = {i for p in m.polygons if m.materials[p.material_index].name == "Chr_Suit" for i in p.vertices}
    shoe_pts = [src.co[i] for p in m.polygons if m.materials[p.material_index].name == "Chr_Shoes" for i in p.vertices]
    def leg_r(z):  # target radius (narrow, straight; never inside the shoe's collar)
        pts = [(0.80, 1.0), (0.72, 0.090), (0.53, 0.066), (0.30, 0.056), (0.0, 0.054)]
        for (z0, r0), (z1, r1) in zip(pts[1:], pts):
            if z <= z1:
                t = (z - z0) / (z1 - z0) if z1 != z0 else 0
                return r0 + (r1 - r0) * max(0, min(1, t))
        return 1.0
    shoe_r = max(abs(math.hypot(p.x - math.copysign(0.171, p.x), p.y - 0.06)) for p in shoe_pts if 0.10 < p.z < 0.13)
    for side in (1, -1):
        ids = [i for i in suit if src.co[i].x * side > 0.01 and src.co[i].z < 0.85]
        for i in ids:
            p = src.co[i]
            sl = [src.co[j] for j in ids if abs(src.co[j].z - p.z) < 0.02]
            cx = sum(q.x for q in sl) / len(sl); cy = sum(q.y for q in sl) / len(sl)
            d = Vector((p.x - cx, p.y - cy)); r = d.length
            tgt = leg_r(p.z)
            if p.z < 0.16:
                tgt = max(tgt, shoe_r + 0.004)
            if r > tgt:
                nr = tgt + (r - tgt) * 0.15  # soft clamp: keeps a hint of the original folds
                d *= nr / r
                m.vertices[i].co = me.matrix_world.inverted() @ Vector((cx + d.x, cy + d.y, p.z))

    # --- off: fedora, chain, watch, the whole shirt (the coat and shirt front replace it).
    n = delete_faces(me, lambda mat, c: mat in ("Trn_Felt", "Trn_Brass", "Chr_Shirt"))
    print("CH01 removed faces:", n)

    # --- the coat body: rings from hem to neck.
    chest = ext(torso_pts, 1.25)   # below the armpits: above, the shirt's shoulder verts read as torso
    belly = ext(torso_pts, 1.12)
    a0 = max(chest[0], belly[0]) + COAT_EASE
    ah = max(a0, hips[0] + 0.012)                # the skirt has to clear the legs, which stand apart in the bind pose
    bf0 = max(chest[1], belly[1]) + 0.012
    bb0 = max(chest[2], belly[2]) + 0.012
    yc0 = chest[3]
    print('CH01 coat hips/chest/belly', [tuple(round(x, 3) for x in e) for e in (hips, chest, belly)])
    neck = ext([p for p in src.pts(lambda d: d in ("neck_01", "spine_03", "head"))], 1.60, 0.02)
    na = max(neck[0], 0.062) + 0.012
    upper = src.pts(lambda d: d in ("spine_02", "spine_03", "neck_01", "clavicle_l", "clavicle_r"))
    def fit(z, a, e, bf, bb, widen=True):
        """A shoulder ring that never dips inside the body: depth at least the body's (all of it, skin too) + ease."""
        x = ext(upper, z, 0.02)
        if x:
            a = max(a, x[0] + 0.014) if widen else a
            bf = max(bf, yc0 - (x[3] - x[1]) + 0.014)
            bb = max(bb, (x[3] + x[2]) - yc0 + 0.014)
        return (z, (a, bf, bb, yc0, e))
    def back_reach(z):
        x = ext(upper, z, 0.015)
        return max(neck[3] + neck[2] + 0.035, (x[3] + x[2] + 0.016) if x else 0)
    keys = [
        (HEM_Z, (ah + 0.02, bf0 + 0.015, bb0 + 0.02, yc0, 2.4)),  # a slight swing out at the hem
        (0.95, (ah, bf0, bb0, yc0, 2.5)),
        (1.30, (a0, bf0, bb0, yc0, 2.6)),                            # straight drape: no waist
        fit(1.44, SHOULDER_X - 0.012, 2.6, bf0 - 0.005, bb0),
        fit(1.50, SHOULDER_X, 2.6, bf0 - 0.02, bb0 - 0.01),          # padded shoulders, square
        fit(1.545, SHOULDER_X - 0.03, 2.4, bf0 - 0.05, bb0 - 0.035),
        (1.585, (na + 0.05, neck[1] + 0.035, back_reach(1.585) - neck[3], neck[3], 2.2)),   # deep enough at the back for the trapezius
        (1.605, (na, neck[1] + 0.012, neck[2] + 0.012, neck[3], 2.0)),
    ]
    zs = [HEM_Z + i * 0.06 for i in range(int((1.42 - HEM_Z) / 0.06) + 1)] + [1.44, 1.47, 1.50, 1.525, 1.545, 1.565, 1.585, 1.605]
    zs = sorted(set(round(z, 4) for z in zs))

    def coat_w(p):
        w = src.weights(p, src.torso)
        if p.z < 1.02:  # the skirt: pelvis plus both thighs, more thigh lower down, blended across the middle
            t = smooth01(1.02, HEM_Z, p.z)
            left = smooth01(-0.09, 0.09, p.x)
            skirt = {"pelvis": 1 - 0.6 * t, "thigh_l": 0.6 * t * left, "thigh_r": 0.6 * t * (1 - left)}
            k = smooth01(1.02, 0.94, p.z)
            w = mix((1 - k, w), (k, skirt))
        return w

    coat = Part()
    rings = [superellipse(*lerp_keys(keys, z)[:4], z, e=lerp_keys(keys, z)[4]) for z in zs]
    coat.loft(rings, coat_w, "Chr_Suit")
    # inside of the skirt (seen from below in a ragdoll / a fall) and the hem's edge
    inner = [[Vector((p.x * 0.96, yc0 + (p.y - yc0) * 0.96, p.z)) for p in r] for r in rings if r[0].z < 1.0]
    ids = coat.loft(inner, coat_w, "Chr_Suit", out=-1)
    hem_out = list(range(0, N))  # first ring added = hem ring, verts 0..N-1
    for i in range(N):
        j = (i + 1) % N
        coat.face([ids[0][i], ids[0][j], hem_out[j], hem_out[i]], "Chr_Suit", Vector((0, 0, -1)))

    # --- sleeves: shoulder to wrist round the arm bones, velvet cuffs.
    for s in ("l", "r"):
        sh = bone(arm, "upperarm_" + s); el = bone(arm, "lowerarm_" + s); wr = bone(arm, "hand_" + s)
        # what's left under the sleeve once the shirt is gone: the arm's skin
        armp = [c for i, (c, d) in enumerate(zip(src.co, src.dom)) if src.vmat.get(i) == "Chr_Skin" and d.startswith(("upperarm_" + s, "lowerarm_" + s, "clavicle_" + s))]
        def rad(o, dvec, t0):
            near = [p for p in armp if abs((p - o).dot(dvec)) < 0.03]
            if not near:
                return t0
            return max(t0, max(((p - o) - dvec * (p - o).dot(dvec)).length for p in near))
        start = sh + Vector((-0.015 if s == "l" else 0.015, 0, 0.0))
        path = [(start, 0.0), (sh.lerp(el, 0.5), 0.0), (el, 0.0), (el.lerp(wr, 0.55), 0.0), (wr.lerp(el, 0.16), 0.0)]
        rings_s, rads = [], []
        for k, (o, _) in enumerate(path):
            d = ((path[k + 1][0] if k + 1 < len(path) else o) - (path[k - 1][0] if k else o)).normalized()
            r = max(rad(o, d, 0.035) + 0.016, 0.052)
            r = min(max(r, 0.06 if k == 0 else 0.0), 0.075)
            rads.append(r)
            u = d.orthogonal().normalized(); v = d.cross(u)
            rings_s.append([o + r * (math.cos(t) * u + math.sin(t) * v) for t in (2 * math.pi * i / 16 for i in range(16))])
        # wrist end: the order runs shoulder->wrist; faces must face out
        aw = lambda p: src.weights(p, src.arms)
        coat.loft(rings_s, aw, "Chr_Suit")
        # cuff: a velvet band over the last 5 cm, closed at the end
        o2 = path[-1][0]; d = (path[-1][0] - path[-2][0]).normalized()
        u = d.orthogonal().normalized(); v = d.cross(u); r = rads[-1] + 0.005
        o1 = o2 - d * 0.05
        ring = lambda o, rr: [o + rr * (math.cos(t) * u + math.sin(t) * v) for t in (2 * math.pi * i / 16 for i in range(16))]
        coat.loft([ring(o1, r), ring(o2 + d * 0.004, r)], aw, "Chr_Velvet")
        coat.loft([ring(o2 + d * 0.004, r), ring(o2 + d * 0.004, r * 0.72)], aw, "Chr_Velvet", out=d)

    # --- the front: cream shirt V, slim tie, velvet lapels and collar, three buttons.
    TOP, VB = 1.60, 1.31   # V from the neck down to the top button
    def on_front(x, z, off):
        k = lerp_keys(keys, z)
        return Vector((x, front_y(k, x) - off, z))
    tw = lambda p: src.weights(p, src.torso)
    steps = 7
    def vband(x0f, x1f, off, mat, z0=VB, z1=TOP, cols=2):
        """A strip on the coat front between half-widths x0f(z)..x1f(z), both sides mirrored if x0f >= 0."""
        rows = [z0 + (z1 - z0) * i / steps for i in range(steps + 1)]
        for sgn in ((1, -1) if x0f(z0) >= 0 else (1,)):
            grid = [[on_front(sgn * (x0f(z) + (x1f(z) - x0f(z)) * c / cols), z, off) for c in range(cols + 1)] for z in rows]
            gi = [[coat.add_v(p, tw(p)) for p in row] for row in grid]
            for a, b in zip(gi, gi[1:]):
                for c in range(cols):
                    q = [a[c], a[c + 1], b[c + 1], b[c]]
                    coat.face(q, mat, Vector((0, -1, 0)))
    vw = lambda z: 0.012 + 0.06 * (z - VB) / (TOP - VB)
    vband(lambda z: -vw(z), vw, 0.006, "Chr_Shirt", cols=4)                         # shirt front
    vband(lambda z: vw(z), lambda z: vw(z) + 0.026, 0.009, "Chr_Velvet")            # lapels
    tie = lambda z: 0.011 if z < TOP - 0.03 else 0.016                              # slim tie, a small knot
    vband(lambda z: -tie(z), tie, 0.011, "Chr_Velvet", z0=VB + 0.01, z1=TOP + 0.004)
    # collar: a narrow velvet band round the back and sides of the neck, open at the front where it runs into the lapels
    k = keys[-1][1]
    arc = [a for a in (2 * math.pi * i / 32 for i in range(33)) if math.sin(a) > -0.55]
    arc.sort(key=lambda a: (a + math.pi / 2) % (2 * math.pi))   # from the right front, round the back, to the left front
    def cpt(a, grow, z, dz_back):
        c, sn = math.cos(a), math.sin(a)
        b = k[1] if sn < 0 else k[2]
        r = (abs(c / k[0]) ** 2 + abs(sn / b) ** 2) ** -0.5 * grow
        return Vector((r * c, k[3] + r * sn, z + dz_back * max(0.0, sn) - 0.02 * max(0.0, -sn)))
    c0 = [cpt(a, 1.04, 1.588, 0.008) for a in arc]
    c1 = [cpt(a, 1.10, 1.612, 0.012) for a in arc]
    coat.loft([c0, c1], tw, "Chr_Velvet", closed=False)
    for z in (1.29, 1.20, 1.11):
        coat.disc(on_front(0.0, z, 0.006), (0, -1, 0), 0.009, tw, "Chr_Velvet")

    # --- the quiff: a pompadour over the top of the head, rising to a forward roll over the brow (100% head).
    hair = [p for p in src.pts(lambda d: d == "head")]
    top = max(p.z for p in hair)
    crown = [p for p in hair if p.z > top - 0.05]
    y_back = max(p.y for p in crown) - 0.02
    y_front = min(p.y for p in crown) + 0.005
    zb = top - 0.065
    def half_w(y):
        s = [abs(p.x) for p in hair if abs(p.y - y) < 0.012 and p.z > zb]
        return (max(s) if s else 0.06) + 0.006
    q = Part()
    secs = []
    # (t along the head back->front, forward push, base / top as fractions of the height): the last rows curl the roll under
    for t, push, fb, ft in [(0.0, 0, 0, 1), (0.2, 0, 0, 1), (0.4, 0, 0, 1), (0.6, 0, 0, 1), (0.78, 0, 0, 1), (0.92, 0, 0, 1),
                            (1.0, 0.012, 0.2, 0.96), (1.0, 0.026, 0.45, 0.86), (1.0, 0.033, 0.66, 0.74)]:
        y = y_back + (y_front - y_back) * t - push
        w = half_w(min(max(y, y_front), y_back)) * (1 - 0.12 * smooth01(0.6, 1.0, t)) * (1 - 0.4 * fb)
        h = (top - zb) + 0.006 + 0.048 * smooth01(0.25, 0.9, t)
        z0, z1 = zb + fb * h, zb + ft * h
        secs.append([Vector((w * math.cos(a), y, z0 + (z1 - z0) * math.sin(a) ** 0.75))
                     for a in (math.pi * j / 12 for j in range(13))]
                    + [Vector((w * (-1 + 2 * j / 6), y, z0)) for j in range(1, 6)])   # flat underside: closes the roll
    q.loft(secs, lambda p: head, "Chr_Hair")
    c = sum(secs[-1], Vector()) / len(secs[-1])
    for sec, want in ((secs[-1], Vector((0, -1, 0.3))), (secs[0], Vector((0, 1, 0)))):
        c = sum(sec, Vector()) / len(sec)
        ci = q.add_v(c, head); ri = [q.add_v(p, head) for p in sec]
        for j in range(len(ri)):
            q.face([ci, ri[j], ri[(j + 1) % len(ri)]], "Chr_Hair", want)

    coat.build(me); q.build(me)
    print("CH01 coat+trim tris ~%d, quiff ~%d" % (2 * len(coat.f), 2 * len(q.f)))
    finish(arm, me, OUT01)


# --------------------------------------------------------------------------------------------- CH02: the flat cap
def flat_cap():
    arm, me = load(CH02)
    m = me.data
    hat = [me.matrix_world @ m.vertices[i].co for p in m.polygons if m.materials[p.material_index].name == "Trn_Felt" for i in p.vertices]
    hair = [me.matrix_world @ m.vertices[i].co for p in m.polygons if m.materials[p.material_index].name == "Chr_Hair" for i in p.vertices]
    n = delete_faces(me, lambda mat, c: mat == "Trn_Felt")
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


def finish(arm, me, out):
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
    arm.select_set(True); me.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')
    print("EXPORTED", out, "tris", tris, "materials", [m.name for m in me.data.materials])
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
    for name, (yaw, pitch, scale, tgt) in {"front": (0, 8, 2.1, 0.95), "side": (90, 8, 2.1, 0.95), "game": (-25, 50, 2.3, 0.9),
                                           "back": (180, 15, 2.1, 0.95), "head": (-30, 15, 0.55, 1.7)}.items():
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
