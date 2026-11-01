"""Visible z-fighting detector: axis-aligned rectangles with the same OUTWARD normal on the same
plane that overlap, and are not hidden by a face pointing the other way (solids touching).
Outward is decided per piece (one MB.add call): boxes use their centroid, any other closed piece
(lathes, rings, extrusions) uses a ray-parity inside test, so winding order never matters.
Ground-contact faces (facing down at z<=0) are ignored. Only axis-aligned rectangles are checked."""
import numpy as np

def _inside(p, tris, d=np.array([0.5773, 0.5774, 0.5775])):
    """Ray parity: is point p inside the closed triangle soup `tris` (N,3,3)?"""
    v0, v1, v2 = tris[:, 0], tris[:, 1], tris[:, 2]
    e1, e2 = v1 - v0, v2 - v0
    h = np.cross(d, e2); a = np.einsum('ij,ij->i', e1, h)
    ok = np.abs(a) > 1e-12
    f = np.zeros_like(a); f[ok] = 1.0 / a[ok]
    s = p - v0; u = f * np.einsum('ij,ij->i', s, h)
    q = np.cross(s, e1); v = f * (q @ d); t = f * np.einsum('ij,ij->i', e2, q)
    hit = ok & (u >= 0) & (u <= 1) & (v >= 0) & (u + v <= 1) & (t > 1e-9)
    return int(hit.sum()) % 2 == 1
def find_zfight(V, F, piece, min_area=0.01, tol=2e-3, ground=0.001):
    V = np.asarray(V, float); piece = np.asarray(piece)
    cent, tris, is_box = {}, {}, {}
    for pid in np.unique(piece):
        fids = np.where(piece == pid)[0]
        idx = sorted({i for fi in fids for i in F[fi]})
        cent[pid] = V[idx].mean(0)
        is_box[pid] = len(fids) == 6 and len(idx) == 8
        if not is_box[pid]:
            tris[pid] = np.array([[V[F[fi][0]], V[F[fi][k]], V[F[fi][k + 1]]]
                                  for fi in fids for k in range(1, len(F[fi]) - 1)])
    recs = []
    for fi, f in enumerate(F):
        if len(f) != 4: continue
        P = V[f]
        n = np.cross(P[1] - P[0], P[2] - P[0]); ln = np.linalg.norm(n)
        if ln < 1e-9: continue
        n /= ln
        ax = int(np.argmax(np.abs(n)))
        if abs(n[ax]) < 0.999: continue
        pid = piece[fi]
        if is_box[pid]:
            sgn = 1 if (P.mean(0) - cent[pid])[ax] >= 0 else -1
        else:
            probe = P.mean(0).copy(); probe[ax] += 1e-3
            sgn = -1 if _inside(probe, tris[pid]) else 1
        coord = P[0, ax]
        if ax == 2 and sgn < 0 and coord <= ground: continue
        others = [i for i in range(3) if i != ax]
        Q = P[:, others]
        lo_, hi_ = Q.min(0), Q.max(0)
        if not all(np.all(np.isclose(Q[:, k], lo_[k], atol=1e-4) | np.isclose(Q[:, k], hi_[k], atol=1e-4)) for k in range(2)):
            continue                                  # only true axis-aligned rectangles
        recs.append((ax, sgn, round(coord / tol), lo_, hi_, coord))
    groups, hits = {}, []
    for r in recs:
        groups.setdefault(r[:3], []).append(r)
    for key, lst in groups.items():
        if len(lst) < 2: continue
        lo = np.array([r[3] for r in lst]); hi = np.array([r[4] for r in lst])
        for i in range(len(lst) - 1):
            ov_lo = np.maximum(lo[i], lo[i + 1:]); ov_hi = np.minimum(hi[i], hi[i + 1:])
            wh = np.clip(ov_hi - ov_lo, 0, None); area = wh[:, 0] * wh[:, 1]
            opp = groups.get((key[0], -key[1], key[2]), [])
            for j in np.where(area > min_area)[0]:
                # hidden if a face pointing the other way sits on the same plane over the whole overlap
                gx = np.linspace(ov_lo[j][0], ov_hi[j][0], 6)[1:-1]
                gy = np.linspace(ov_lo[j][1], ov_hi[j][1], 6)[1:-1]
                pts = [(x, y) for x in gx for y in gy]
                if opp and all(any(r[3][0] - 1e-4 <= x <= r[4][0] + 1e-4 and r[3][1] - 1e-4 <= y <= r[4][1] + 1e-4
                                   for r in opp) for x, y in pts):
                    continue
                hits.append((key[0], key[1], lst[i][5], float(area[j]), ov_lo[j], ov_hi[j]))
    return hits
def report(name, V, F, piece, show=6):
    hits = find_zfight(V, F, piece)
    if not hits:
        print(f"  OK   {name}: no z-fighting"); return 0
    print(f"  FAIL {name}: {len(hits)} overlapping coplanar faces, {sum(h[3] for h in hits):.2f} m^2")
    for h in sorted(hits, key=lambda h: -h[3])[:show]:
        ax = "xyz"[h[0]]; o = [c for c in "xyz" if c != ax]
        print(f"       {ax}={h[2]:.3f} ({'+' if h[1] > 0 else '-'}{ax})  {h[3]:.2f} m^2   "
              f"{o[0]} {h[4][0]:.2f}..{h[5][0]:.2f}  {o[1]} {h[4][1]:.2f}..{h[5][1]:.2f}")
    return len(hits)
