"""
Render a flat-shaded cel-style preview PNG AND run a z-fighting check of an assembled kit script WITHOUT Blender.
Stubs bpy/bmesh, runs the geometry code, z-buffers the meshes. Use it to sanity-check every
piece before handing it over (catches overlaps, floating parts, wrong proportions).

    python preview.py atlantis_<name>.py -o preview.png [--yaw 30 --pitch 28 --title "..."]
    [--layout '[["Base_Passage",0,0,0,0],["Top_Balcony",0,0,14,0]]']   # name, x, y, z, rot

Without --layout the assets are placed in a row. Orange circles mark grapple anchors.
Needs numpy + Pillow only.
"""
import argparse, json, math, os, sys, types
from unittest.mock import MagicMock
import numpy as np
from PIL import Image, ImageDraw, ImageFont

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import mathutils_shim as mathutils
sys.modules["mathutils"] = mathutils
sys.modules["bpy"] = MagicMock()

# --- real icosphere for bmesh.ops.create_icosphere (rocks)
class _V:
    def __init__(s, co, i): s.co, s.index = co, i
class _F:
    def __init__(s, vs): s.verts = vs
class _BM:
    def __init__(s): s.verts, s.faces = [], []
    def free(s): pass
def _ico(bm, subdivisions=2, radius=1.0, **k):
    t = (1 + 5 ** 0.5) / 2
    P = [np.array(p, float) for p in [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t),
                                      (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]]
    P = [p / np.linalg.norm(p) for p in P]
    Fs = [(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),
          (3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)]
    for _ in range(subdivisions):
        cache, NF = {}, []
        def mid(a, b):
            key = (min(a, b), max(a, b))
            if key not in cache:
                m = P[a] + P[b]; P.append(m / np.linalg.norm(m)); cache[key] = len(P) - 1
            return cache[key]
        for a, b, c in Fs:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            NF += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        Fs = NF
    bm.verts = [_V(mathutils.Vector(p * radius), i) for i, p in enumerate(P)]
    bm.faces = [_F([bm.verts[i] for i in f]) for f in Fs]
bm_mod = types.ModuleType("bmesh"); bm_mod.new = _BM; bm_mod.ops = types.SimpleNamespace(create_icosphere=_ico)
sys.modules["bmesh"] = bm_mod

ap = argparse.ArgumentParser()
ap.add_argument("script"); ap.add_argument("-o", "--out", required=True)
ap.add_argument("--yaw", type=float, default=30); ap.add_argument("--pitch", type=float, default=28)
ap.add_argument("--title", default=""); ap.add_argument("--layout", default="")
ap.add_argument("--width", type=int, default=1400); ap.add_argument("--height", type=int, default=860)
ap.add_argument("--no-ground", action="store_true")
ap.add_argument("--set", action="append", default=[], help="NAME=python_literal, set before the script runs")
a = ap.parse_args()

import ast
g = {"__name__": "__kit_preview__", "__KIT_PREVIEW__": True, "__ATLANTIS_PREVIEW__": True}
for kv in a.set:
    k, v = kv.split("=", 1)
    try: g[k] = ast.literal_eval(v)
    except Exception: g[k] = v
exec(compile(open(a.script).read(), a.script, "exec"), g)
assets = g["PREVIEW_REGISTRY"]
if not assets:
    sys.exit("No assets registered - does the script call run([...]) / Asset.finish()?")
by_name = {x.name: x for x in assets}

def tris_of(asset, x, y, z, rot):
    c, s = math.cos(math.radians(rot)), math.sin(math.radians(rot))
    R = np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]]); off = np.array([x, y, z])
    out = []
    for mb in (asset.S, asset.C, asset.P):
        if not mb.v: continue
        V = np.array([list(v) for v in mb.v]) @ R.T + off
        for f, m in zip(mb.f, mb.m):
            for k in range(1, len(f) - 1):
                out.append((V[f[0]], V[f[k]], V[f[k + 1]], m))
    anchors = [tuple(np.array(p) @ R.T + off) for p in asset.anchors]
    return out, anchors

lay = a.layout
if lay and os.path.exists(lay): lay = open(lay).read()
placements = json.loads(lay) if lay else None
if not placements:
    placements, x = [], 0.0
    for asset in assets:
        V = np.array([list(v) for mb in (asset.S, asset.C, asset.P) for v in mb.v])
        w = V[:, 0].max() - V[:, 0].min()
        placements.append([asset.name, x + w / 2 - (V[:, 0].max() + V[:, 0].min()) / 2 + 0, 0, 0, 0])
        x += w + 8
    shift = x / 2
    for p in placements: p[1] -= shift - 4
tris, anchors, labels = [], [], []
for name, x, y, z, rot in placements:
    t, an = tris_of(by_name[name], x, y, z, rot); tris += t; anchors += an
    if not a.layout: labels.append((x, y - 2, 0, name))
A = np.array([[t[0], t[1], t[2]] for t in tris]); M = np.array([t[3] for t in tris])
lo, hi = A.reshape(-1, 3).min(0), A.reshape(-1, 3).max(0)
if not a.no_ground:
    pad = 12
    gx0, gx1, gy0, gy1 = lo[0] - pad, hi[0] + pad, lo[1] - pad, hi[1] + pad
    G = [np.array(p, float) for p in [(gx0, gy0, -0.02), (gx1, gy0, -0.02), (gx1, gy1, -0.02), (gx0, gy1, -0.02)]]
    A = np.concatenate([A, np.array([[G[0], G[1], G[2]], [G[0], G[2], G[3]]])]); M = np.append(M, [9, 9])

def _srgb(c): return int(255 * (12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055))
COLS = {i: tuple(_srgb(min(max(ch, 0.0), 1.0)) for ch in e[1]) for i, e in enumerate(g.get("PALETTE", []))}
COLS[9] = (196, 178, 136)
SHADOW = np.array([70, 125, 140]); L = np.array([-0.45, -0.6, 0.66]); L /= np.linalg.norm(L)
W, H = a.width, a.height
az, p = math.radians(a.yaw), math.radians(a.pitch)
fwd = np.array([math.cos(p) * math.sin(az), math.cos(p) * math.cos(az), -math.sin(p)])
right = np.array([math.cos(az), -math.sin(az), 0.0]); up = np.cross(right, fwd)
SX, SY, D = A @ right, -(A @ up), A @ fwd
fit = M != 9
mn = np.array([SX[fit].min(), SY[fit].min()]); mx = np.array([SX[fit].max(), SY[fit].max()])
sc = min((W - 80) / max(mx[0] - mn[0], 1e-6), (H - 150) / max(mx[1] - mn[1], 1e-6))
ox, oy = W / 2 - (mn[0] + mx[0]) / 2 * sc, H / 2 + 35 - (mn[1] + mx[1]) / 2 * sc
PX, PY = SX * sc + ox, SY * sc + oy
nrm = np.cross(A[:, 1] - A[:, 0], A[:, 2] - A[:, 0]); ln = np.linalg.norm(nrm, axis=1); ok = ln > 1e-9
nrm[ok] /= ln[ok, None]
zbuf = np.full((H, W), np.inf); img = np.zeros((H, W, 3))
tt = np.linspace(0, 1, H)[:, None, None]
img[:] = (1 - tt) * np.array([40, 120, 135]) + tt * np.array([10, 40, 55])
for i in np.where(ok)[0]:
    xs, ys, ds = PX[i], PY[i], D[i]
    x0, x1 = max(int(xs.min()), 0), min(int(xs.max()) + 1, W - 1)
    y0, y1 = max(int(ys.min()), 0), min(int(ys.max()) + 1, H - 1)
    if x1 < x0 or y1 < y0: continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
    (ax_, bx, cx), (ay, by, cy) = xs, ys
    den = (by - cy) * (ax_ - cx) + (cx - bx) * (ay - cy)
    if abs(den) < 1e-9: continue
    w0 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / den
    w1 = ((cy - ay) * (gx - cx) + (ax_ - cx) * (gy - cy)) / den
    w2 = 1 - w0 - w1
    inside = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
    if not inside.any(): continue
    dep = w0 * ds[0] + w1 * ds[1] + w2 * ds[2]
    zb = zbuf[y0:y1 + 1, x0:x1 + 1]; upd = inside & (dep < zb)
    if not upd.any(): continue
    lam = abs(nrm[i] @ L); band = 1.0 if lam > 0.62 else 0.72 if lam > 0.3 else 0.5
    zb[upd] = dep[upd]
    img[y0:y1 + 1, x0:x1 + 1][upd] = np.array(COLS.get(int(M[i]), (200, 200, 200)), float) * band + SHADOW * (1 - band) * 0.55
fin = np.isfinite(zbuf)
dn = (zbuf - zbuf[fin].min()) / (zbuf[fin].max() - zbuf[fin].min() + 1e-9)
f = np.clip(dn, 0, 1)[..., None] * 0.35
img[fin] = img[fin] * (1 - f[fin]) + np.array([30, 95, 110]) * f[fin]
zz = np.where(fin, zbuf, 1e4)
edge = (np.abs(np.diff(zz, axis=0, prepend=zz[:1])) > 0.5) | (np.abs(np.diff(zz, axis=1, prepend=zz[:, :1])) > 0.5)
img[edge] *= 0.35
im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)); d = ImageDraw.Draw(im)
def font(bold, size):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",):
        if os.path.exists(path): return ImageFont.truetype(path, size)
    return ImageFont.load_default()
f1, f2 = font(True, 22), font(False, 15)
proj = lambda q: (np.array(q) @ right * sc + ox, -(np.array(q) @ up) * sc + oy)
for q in anchors:
    X, Y = proj(q); d.ellipse([X - 4, Y - 4, X + 4, Y + 4], outline=(255, 110, 50), width=2)
for (x, y, z, txt) in labels:
    X, Y = proj((x, y, z)); bb = d.textbbox((0, 0), txt, font=f2); w = bb[2] - bb[0]
    d.rectangle([X - w / 2 - 5, Y - 11, X + w / 2 + 5, Y + 11], fill=(15, 45, 55)); d.text((X - w / 2, Y - 9), txt, font=f2, fill=(255, 225, 150))
d.text((24, 16), a.title or ", ".join(x.name for x in assets), font=f1, fill=(235, 245, 245))
size = hi - lo
d.text((24, 46), f"Preview from the script's geometry (not the toon shader).  Extent {size[0]:.0f} x {size[1]:.0f} x {size[2]:.0f} m.  Orange = grapple anchors.",
       font=f2, fill=(205, 228, 228))
im.save(a.out)
for asset in assets:
    V = np.array([list(v) for mb in (asset.S, asset.C, asset.P) for v in mb.v])
    lo_, hi_ = V.min(0), V.max(0)
    print(f"{asset.name}: {sum(len(f) - 2 for mb in (asset.S, asset.C, asset.P) for f in mb.f)} tris, "
          f"{len(asset.anchors)} anchors, bounds x {lo_[0]:.3f}..{hi_[0]:.3f}  y {lo_[1]:.3f}..{hi_[1]:.3f}  "
          f"z {lo_[2]:.3f}..{hi_[2]:.3f}  (size {hi_[0]-lo_[0]:.3f} x {hi_[1]-lo_[1]:.3f} x {hi_[2]-lo_[2]:.3f})")
print("saved", a.out)

# ---- z-fighting check (visible coplanar overlaps inside each exported mesh)
from zfight import report
print("Z-fighting check:")
bad = 0
for asset in assets:
    for label, mb in (("structure", asset.S), ("columns", asset.C), ("props", asset.P)):
        if mb.v:
            bad += report(f"{asset.name} {label}", np.array([list(v) for v in mb.v]), mb.f, mb.pid)
print("ALL CLEAN" if bad == 0 else f"{bad} problem face pairs - fix before delivering (see references/pitfalls.md)")
