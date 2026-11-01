"""
1930s PASSENGER TRAIN INTERIOR KIT   (Blender 3.x-5.x -> Unity FBX)
Run in Blender (Scripting tab, or `blender --python this.py`). Clears the scene, exports one FBX per asset.

Interior floor surface is at z = 0 in every asset, so seats/racks drop straight in at y = 0 in Unity.
Car axis: length along Y (Blender) = Unity Z. Cars chain every 14 m end-to-end (doors line up).

TrainCar_Passenger   14 m long (outer), 5.0 x 3.0 m clear interior, end doors 1.2 x 2.3 m.
                     4 window bays per side (1.8 x 1.45 m open windows, centres y = +-1.5, +-4.5)
                     on a 3 m booth rhythm: seat, 1 m legroom, seat. Pilasters, ceiling ribs at y = 0, +-3, +-6.
                     Wood paneling, brass rails, cream ceiling, 4 lamps over a velvet aisle runner.
Seat_Double          2.0 w x 1.0 h x 1.0 l bench for two. Back at -Y, sitter faces +Y. Symmetric,
                     so rotate 180 for the facing seat. In the car: x = +-1.45 (5 cm off the wall).
LuggageRack_Full_A/B/C  1.0 w x 2.0 h x 1.0 l shelf unit, open front (+Y), seeded suitcases, hatboxes,
                     crates, parcels, duffels and trunks. Change RACK_SEEDS to reroll.
LuggageRack_Empty    the same frame with nothing in it.
"""
# >>> CONFIG
EXPORT_DIR = r""          # e.g. r"C:/Projects/MyGame/Assets/Models/Train"  ("" = Train_FBX next to the .blend)
KIT_NAME = "Train"
RACK_SEEDS = (11, 23, 37) # one luggage-rack variant per seed; change to reroll the contents
CUTAWAY = globals().get("CUTAWAY", False)   # preview only: hides roof, ceiling and the -x wall
# <<< CONFIG

# ---------------------------------------------------------------- palette (names = Unity material names)
(SLOT_WOOD, SLOT_WOODDARK, SLOT_BRASS, SLOT_CREAM,
 SLOT_VELVET, SLOT_LEATHER, SLOT_IRON) = set_palette([
    ("Trn_Wood",     (0.45, 0.24, 0.12), 0.0, 0.6),
    ("Trn_WoodDark", (0.20, 0.10, 0.05), 0.0, 0.6),
    ("Trn_Brass",    (0.95, 0.70, 0.25), 1.0, 0.3),
    ("Trn_Cream",    (0.90, 0.84, 0.68), 0.0, 0.8),
    ("Trn_Velvet",   (0.45, 0.07, 0.10), 0.0, 0.9),
    ("Trn_Leather",  (0.40, 0.22, 0.10), 0.0, 0.7),
    ("Trn_Iron",     (0.18, 0.20, 0.22), 0.6, 0.5)])

# ---------------------------------------------------------------- car dimensions
CAR_L   = 14.0
HALF_L  = CAR_L / 2           # 7.0  outer end faces
IN_W    = 2.5                 # inner wall face (x)
IN_L    = 6.85                # inner end-wall face (y)
CEIL    = 3.0
TOP     = 3.15                # wall tops / ceiling top / roof underside
BAYS    = (-4.5, -1.5, 1.5, 4.5)
WIN_W, SILL, WIN_TOP = 1.8, 0.8, 2.25
DOOR_W, DOOR_H = 1.2, 2.3
POSTS   = (-6.0, -3.0, 0.0, 3.0, 6.0)


def train_car():
    a = Asset("TrainCar_Passenger", seed=5)
    S = a.S
    z0, h = -0.25, TOP + 0.25
    win = [(c, WIN_W, SILL - z0, WIN_TOP - z0) for c in BAYS]
    door = [(0.0, DOOR_W, -z0, DOOR_H - z0)]

    # --- shell: inner wood skin + outer painted-steel skin
    for s in (1, -1):
        if CUTAWAY and s < 0:
            continue
        wall(S, 'y', s * 2.55, -HALF_L, HALF_L, z0, h, 0.10, win, SLOT_WOOD)
        wall(S, 'y', s * 2.625, -HALF_L, HALF_L, z0, h, 0.05, win, SLOT_IRON)
    for s in (1, -1):
        wall(S, 'x', s * 6.90, -IN_W, IN_W, z0, h, 0.10, door, SLOT_WOOD)
        wall(S, 'x', s * 6.975, -IN_W, IN_W, z0, h, 0.05, door, SLOT_IRON)

    # --- floor, runner, underframe
    S.rect(-IN_W, IN_W, -IN_L, IN_L, z0, 0.0, SLOT_WOODDARK)
    S.rect(-0.5, 0.5, -IN_L, IN_L, 0.0, 0.015, SLOT_VELVET)
    S.rect(-2.3, 2.3, -6.6, 6.6, -0.6, z0, SLOT_IRON)

    # --- side-wall trim (each trim type has its own projection depth -> no coplanar faces)
    for s in (1, -1):
        if CUTAWAY and s < 0:
            continue
        X = lambda a_, b_: sx(s, a_, b_)
        S.rect(*X(2.47, 2.50), -IN_L, IN_L, 0.0, 0.15, SLOT_WOODDARK)          # baseboard
        cur = -IN_L                                                            # dado rail, broken by sills
        for c in BAYS:
            S.rect(*X(2.455, 2.50), cur, c - 1.0, 0.72, 0.76, SLOT_WOODDARK)
            cur = c + 1.0
        S.rect(*X(2.455, 2.50), cur, IN_L, 0.72, 0.76, SLOT_WOODDARK)
        for c in BAYS:                                                         # window frames
            S.rect(*X(2.42, 2.50), c - 1.0, c + 1.0, 0.72, SILL, SLOT_WOODDARK)          # sill
            S.rect(*X(2.44, 2.50), c - 0.98, c - 0.90, SILL, WIN_TOP, SLOT_WOODDARK)     # jambs
            S.rect(*X(2.44, 2.50), c + 0.90, c + 0.98, SILL, WIN_TOP, SLOT_WOODDARK)
            S.rect(*X(2.43, 2.50), c - 0.98, c + 0.98, WIN_TOP, WIN_TOP + 0.10, SLOT_WOODDARK)  # head
            S.rect(*X(2.505, 2.52), c - 0.9, c + 0.9, SILL, SILL + 0.04, SLOT_BRASS)  # brass sill strip in the reveal
        for p in POSTS:                                                        # pilasters
            S.rect(*X(2.46, 2.50), p - 0.08, p + 0.08, 0.15, 2.60, SLOT_WOODDARK)
        S.rect(*X(2.45, 2.50), -IN_L, IN_L, 2.60, 2.65, SLOT_BRASS)            # picture rail
        S.rect(*X(2.49, 2.50), -IN_L, IN_L, 2.65, 2.85, SLOT_CREAM)            # cream frieze
        S.rect(*X(2.38, 2.50), -IN_L, IN_L, 2.85, CEIL, SLOT_WOODDARK)         # cornice
        # exterior: brass pinstripes
        S.rect(*X(2.65, 2.665), -HALF_L, HALF_L, 0.50, 0.55, SLOT_BRASS)
        S.rect(*X(2.65, 2.665), -HALF_L, HALF_L, 2.45, 2.50, SLOT_BRASS)

    # --- end-wall trim
    for s in (1, -1):
        Y = lambda a_, b_: sx(s, a_, b_)
        for xa, xb in ((-2.47, -0.72), (0.72, 2.47)):
            S.rect(xa, xb, *Y(6.82, IN_L), 0.0, 0.15, SLOT_WOODDARK)          # baseboard
        for sg in (1, -1):
            S.rect(*sx(sg, 0.60, 0.72), *Y(6.79, IN_L), 0.0, DOOR_H, SLOT_WOODDARK)   # door jambs
            S.rect(*sx(sg, 0.95, 2.25), *Y(6.83, IN_L), 0.30, 0.90, SLOT_WOODDARK)     # raised panels
            S.rect(*sx(sg, 0.95, 2.25), *Y(6.83, IN_L), 1.05, 2.40, SLOT_WOODDARK)
        S.rect(-0.72, 0.72, *Y(6.79, IN_L), DOOR_H, DOOR_H + 0.12, SLOT_WOODDARK)     # door head
        S.rect(-0.25, 0.25, *Y(6.83, IN_L), 2.46, 2.56, SLOT_BRASS)                   # car number plate
        S.rect(-2.45, 2.45, *Y(6.80, IN_L), 2.60, 2.65, SLOT_BRASS)                   # picture rail
        S.rect(-2.49, 2.49, *Y(6.84, IN_L), 2.65, 2.85, SLOT_CREAM)
        S.rect(-2.38, 2.38, *Y(6.73, IN_L), 2.85, CEIL, SLOT_WOODDARK)                # cornice
        S.rect(-0.6, 0.6, *Y(IN_L, 7.0), 0.0, 0.012, SLOT_BRASS)                      # brass tread plate

    if not CUTAWAY:
        # --- ceiling, ribs, lamps
        S.rect(-IN_W, IN_W, -IN_L, IN_L, CEIL, TOP, SLOT_CREAM)
        for p in POSTS:
            S.rect(-2.38, 2.38, p - 0.06, p + 0.06, 2.92, CEIL, SLOT_WOODDARK)
        for c in BAYS:
            S.lathe([(0.09, CEIL), (0.09, 2.98), (0.025, 2.97), (0.025, 2.88), (0.22, 2.86), (0.24, 2.83)],
                    segs=24, slot=SLOT_BRASS, center=(0, c, 0))
            S.lathe([(0.235, 2.83), (0.20, 2.76), (0.10, 2.72), (0.01, 2.71)],
                    segs=24, slot=SLOT_CREAM, center=(0, c, 0), cap_top=False)
        # --- roof (curved, painted steel) + vents
        S.extrude([(-2.8, TOP), (2.8, TOP), (2.8, 3.30), (2.2, 3.60), (1.2, 3.78), (0.0, 3.82),
                   (-1.2, 3.78), (-2.2, 3.60), (-2.8, 3.30)], 'y', -HALF_L, HALF_L, SLOT_IRON)
        for c in BAYS:
            S.lathe([(0.16, 3.78), (0.16, 3.95), (0.24, 3.97), (0.20, 4.05), (0.01, 4.07)],
                    segs=16, slot=SLOT_IRON, center=(0, c, 0))
    return a.finish()

# ---------------------------------------------------------------- seat
def seat_double():
    a = Asset("Seat_Double", seed=2)
    S = a.S
    S.rect(-1.0, 1.0, -0.48, -0.38, 0.0, 0.94, SLOT_WOOD)            # back frame
    S.rect(-1.0, 1.0, -0.50, -0.36, 0.94, 1.0, SLOT_BRASS)           # brass top rail
    S.rect(-0.85, 0.85, -0.50, -0.48, 0.15, 0.80, SLOT_WOODDARK)     # rear panel (seen from behind)
    S.rect(-0.92, 0.92, -0.38, 0.35, 0.0, 0.30, SLOT_WOODDARK)       # base / kick
    for sg in (1, -1):
        S.rect(*sx(sg, 0.92, 1.0), -0.38, 0.50, 0.0, 0.62, SLOT_WOOD)          # armrests
        S.rect(*sx(sg, 0.915, 1.0), -0.38, 0.50, 0.62, 0.66, SLOT_BRASS)     # brass caps
        S.rect(*sx(sg, 0.01, 0.92), -0.38, 0.48, 0.30, 0.46, SLOT_VELVET)      # seat cushions
        S.rect(*sx(sg, 0.02, 0.90), -0.38, -0.26, 0.50, 0.92, SLOT_VELVET)     # back cushions
        S.rect(*sx(sg, 0.10, 0.82), -0.26, -0.24, 0.62, 0.86, SLOT_VELVET)     # tufted panel
    return a.finish()

# ---------------------------------------------------------------- luggage rack
SHELVES = (0.08, 0.70, 1.32, 1.92)    # shelf bottoms (0.04 thick)

def rack_frame(S):
    for x in (-0.47, 0.47):
        for y in (-0.47, 0.47):
            S.rect(x - 0.03, x + 0.03, y - 0.03, y + 0.03, 0.0, 2.0, SLOT_WOODDARK)
    for z in SHELVES:
        S.rect(-0.47, 0.47, -0.47, 0.47, z, z + 0.04, SLOT_WOOD)
    for i, z in enumerate(SHELVES):
        t = z + 0.04
        if i < 3:
            S.rect(-0.44, 0.44, 0.44, 0.47, t, t + 0.04, SLOT_WOODDARK)        # front lip
            rz = (t + 0.17, t + 0.195)
        else:
            rz = (1.975, 2.0)                                                   # top gallery
        S.rect(-0.44, 0.44, -0.4825, -0.4575, *rz, SLOT_BRASS)
        for sg in (1, -1):
            S.rect(*sx(sg, 0.4575, 0.4825), -0.44, 0.44, *rz, SLOT_BRASS)
    S.rect(-0.44, 0.44, 0.4575, 0.4825, 1.975, 2.0, SLOT_BRASS)            # front top rail

def it_case(S, r, x0, x1, y0, y1, z0, hz):
    """Suitcase lying flat: body, trim band, brass latches, handle on the front."""
    body = r.choice((SLOT_LEATHER, SLOT_LEATHER, SLOT_VELVET, SLOT_CREAM))
    z1 = z0 + hz
    S.rect(x0, x1, y0, y1, z0, z1, body)
    zm = (z0 + z1) / 2
    S.rect(x0 - 0.006, x1 + 0.006, y0 - 0.006, y1 + 0.006, zm - 0.012, zm + 0.012, SLOT_WOODDARK)
    xc = (x0 + x1) / 2
    for lx in (x0 + 0.06, x1 - 0.06):
        S.rect(lx - 0.02, lx + 0.02, y1 + 0.006, y1 + 0.016, zm + 0.012, zm + 0.04, SLOT_BRASS)
    S.rect(xc - 0.06, xc + 0.06, y1, y1 + 0.03, zm - 0.04, zm - 0.015, SLOT_WOODDARK)
    return z1

def it_upright(S, r, x0, x1, y0, y1, z0, hz):
    """Suitcase standing on its edge, handle on top."""
    body = r.choice((SLOT_LEATHER, SLOT_VELVET, SLOT_CREAM))
    z1 = z0 + hz
    S.rect(x0, x1, y0, y1, z0, z1, body)
    S.rect(x0 - 0.006, x1 + 0.006, y0 + 0.04, y0 + 0.07, z0 + 0.004, z1 + 0.006, SLOT_WOODDARK)
    S.rect(x0 - 0.006, x1 + 0.006, y1 - 0.07, y1 - 0.04, z0 + 0.004, z1 + 0.006, SLOT_WOODDARK)
    yc, xc = (y0 + y1) / 2, (x0 + x1) / 2
    S.rect(xc - 0.02, xc + 0.02, yc - 0.07, yc + 0.07, z1, z1 + 0.035, SLOT_BRASS)
    return z1

def it_crate(S, r, x0, x1, y0, y1, z0, hz):
    z1 = z0 + hz
    S.rect(x0, x1, y0, y1, z0, z1, SLOT_WOOD)
    for za in (z0 + 0.004, z1 - 0.05):
        S.rect(x0 - 0.01, x1 + 0.01, y0 - 0.01, y1 + 0.01, za, za + 0.046, SLOT_WOODDARK)
    for xa in (x0 - 0.008, x1 - 0.042):
        S.rect(xa, xa + 0.05, y1, y1 + 0.008, z0 + 0.05, z1 - 0.05, SLOT_WOODDARK)
    return z1

def it_parcel(S, r, x0, x1, y0, y1, z0, hz):
    z1 = z0 + hz
    S.rect(x0, x1, y0, y1, z0, z1, SLOT_CREAM)
    xc, yc = (x0 + x1) / 2, (y0 + y1) / 2
    S.rect(xc - 0.008, xc + 0.008, y0 - 0.004, y1 + 0.004, z0 + 0.004, z1 + 0.003, SLOT_LEATHER)
    S.rect(x0 - 0.004, x1 + 0.004, yc - 0.008, yc + 0.008, z0 + 0.004, z1 + 0.005, SLOT_LEATHER)
    return z1

def it_trunk(S, r, x0, x1, y0, y1, z0, hz):
    z1 = z0 + hz
    S.rect(x0, x1, y0, y1, z0, z1, r.choice((SLOT_WOODDARK, SLOT_LEATHER)))
    for xa in (x0 + 0.08, x1 - 0.11):
        S.rect(xa, xa + 0.03, y0 - 0.006, y1 + 0.006, z0 + 0.004, z1 + 0.006, SLOT_BRASS)
    S.rect(x0 - 0.006, x1 + 0.006, y0 - 0.008, y1 + 0.008, z1 - 0.05, z1 - 0.03, SLOT_BRASS)
    xc = (x0 + x1) / 2
    S.rect(xc - 0.04, xc + 0.04, y1 + 0.008, y1 + 0.02, z1 - 0.12, z1 - 0.05, SLOT_BRASS)   # lock
    return z1

def it_hatbox(S, r, x0, x1, y0, y1, z0, hz):
    rad = min(x1 - x0, y1 - y0) / 2
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    body = r.choice((SLOT_CREAM, SLOT_VELVET, SLOT_CREAM))
    lid = SLOT_WOODDARK if body == SLOT_CREAM else SLOT_CREAM
    S.lathe([(rad, z0), (rad, z0 + hz - 0.05)], segs=24, slot=body, center=(cx, cy, 0))
    S.lathe([(rad + 0.012, z0 + hz - 0.06), (rad + 0.012, z0 + hz), (0.01, z0 + hz)],
            segs=24, slot=lid, center=(cx, cy, 0), cap_bottom=True, cap_top=False)
    return z0 + hz

def it_duffel(S, r, x0, x1, y0, y1, z0, hz):
    rad = min(hz, y1 - y0) / 2
    L = x1 - x0
    prof = [(0.01, -L / 2), (rad * 0.6, -L / 2), (rad * 0.92, -L / 2 + rad * 0.35), (rad, -L / 2 + rad * 0.8),
            (rad, L / 2 - rad * 0.8), (rad * 0.92, L / 2 - rad * 0.35), (rad * 0.6, L / 2), (0.01, L / 2)]
    S.lathe(prof, segs=16, slot=r.choice((SLOT_VELVET, SLOT_LEATHER, SLOT_CREAM)),
            center=((x0 + x1) / 2, (y0 + y1) / 2, z0 + rad), mat3=Matrix.Rotation(radians(90), 3, 'Y'),
            cap_bottom=False, cap_top=False)
    return z0 + 2 * rad

# (function, width range, depth range, height range, allowed shelves)
ITEMS = [
    (it_case,    (0.40, 0.62), (0.34, 0.55), (0.14, 0.20), (0, 1, 2)),
    (it_upright, (0.14, 0.22), (0.40, 0.60), (0.32, 0.46), (0, 1, 2)),
    (it_crate,   (0.26, 0.40), (0.26, 0.45), (0.24, 0.40), (0, 1)),
    (it_parcel,  (0.16, 0.30), (0.16, 0.34), (0.10, 0.22), (0, 1, 2)),
    (it_trunk,   (0.55, 0.80), (0.42, 0.55), (0.34, 0.46), (0,)),
    (it_hatbox,  (0.28, 0.40), (0.28, 0.40), (0.18, 0.28), (1, 2)),
    (it_duffel,  (0.40, 0.60), (0.24, 0.32), (0.24, 0.32), (1, 2)),
]
STACKABLE = (it_case, it_trunk, it_crate, it_parcel)

FRONT_ROW = (it_parcel, it_hatbox, it_upright, it_crate, it_parcel)

def stack_on(S, r, x0, x1, y0, y1, top, room_top):
    w, d = x1 - x0, y1 - y0
    sw, sd = w * r.uniform(0.5, 0.85), d * r.uniform(0.55, 0.9)
    sh = room_top - top - 0.03
    if sh > 0.08 and sw > 0.12 and sd > 0.12:
        ox = x0 + r.uniform(0.01, w - sw - 0.01)
        oy = y0 + r.uniform(0.01, d - sd - 0.01)
        g = r.choice((it_parcel, it_case, it_parcel, it_crate))
        return g(S, r, ox, ox + sw, oy, oy + sd, top, min(sh, r.uniform(0.08, 0.2)))
    return top

def fill_shelf(S, r, shelf):
    """Pack a shelf left to right. Shallow items go in a back row with a second item in front."""
    z0 = SHELVES[shelf] + 0.04
    room_top = z0 + 0.54
    cur, end = -0.43, 0.43
    tries = 0
    while tries < 40 and end - cur > 0.12:
        tries += 1
        fn, wr, dr, hr, ok = r.choice(ITEMS)
        if shelf not in ok:
            continue
        w = min(r.uniform(*wr), end - cur)
        if w < wr[0] * 0.8:
            continue
        d = w if fn is it_hatbox else r.uniform(*dr)
        if d > 0.86:
            continue
        hz = min(r.uniform(*hr), 0.5)
        x0, x1 = cur, cur + w
        two_rows = d <= 0.44
        y0 = -0.43 if two_rows else -0.43 + r.uniform(0, 0.86 - d)
        top = fn(S, r, x0, x1, y0, y0 + d, z0, hz)
        if fn in STACKABLE and r.random() < 0.8:
            top = stack_on(S, r, x0, x1, y0, y0 + d, top, room_top)
        if two_rows:                                   # second item in front of it
            fy0 = y0 + d + 0.03
            for _ in range(8):
                g = r.choice(FRONT_ROW)
                gw = [i for i in ITEMS if i[0] is g][0]
                fw = min(r.uniform(*gw[1]), w)
                fd = fw if g is it_hatbox else min(r.uniform(*gw[2]), 0.43 - fy0)
                if fw < 0.12 or fd < 0.12 or fy0 + fd > 0.43 or (g is it_hatbox and fd > 0.43 - fy0):
                    continue
                fx0 = x0 + r.uniform(0, w - fw)
                ft = g(S, r, fx0, fx0 + fw, fy0, fy0 + fd, z0, min(r.uniform(*gw[3]), 0.42))
                if g in STACKABLE and r.random() < 0.5:
                    stack_on(S, r, fx0, fx0 + fw, fy0, fy0 + fd, ft, room_top)
                break
        cur = x1 + r.uniform(0.015, 0.035)

def make_rack(name, seed, full=True):
    def build():
        a = Asset(name, seed=seed)
        rack_frame(a.S)
        if full:
            for shelf in (0, 1, 2):
                fill_shelf(a.P, a.rng, shelf)
        return a.finish()
    build.__name__ = name
    return build

builders = [train_car, seat_double]
builders += [make_rack(f"LuggageRack_Full_{k}", s) for k, s in zip("ABC", RACK_SEEDS)]
builders += [make_rack("LuggageRack_Empty", 1, full=False)]
run(builders, spacing=10.0)
