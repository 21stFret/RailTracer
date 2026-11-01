"""
RAILTRACER - MODULAR 1930s TRAIN CAR KIT   (Blender 3.x-5.x -> Unity FBX)
Run in Blender (Scripting tab), or headless:  blender -b --python railtracer_train_kit.py
Every FBX goes to EXPORT_DIR (the folder is created if needed).

GRID (all shell modules share one cross-section)
  Car axis = Blender Y (Unity Z). 1 m grid along the car. Floor surface at z = 0.
  Interior 5.0 m wide (inner wall faces x = +-2.5) x 3.0 m to the ceiling. Walls 0.15 m (wood + steel skin).
  Every pivot: car centreline (x = 0), floor level, CENTRE of the piece along the car.
  -> 1 m pieces sit at half-metres (0.5, 1.5 ...), 2 m pieces on whole metres, end caps 0.25 m from the end.
  Wall modules are built for ONE side; rotate 180 deg (Unity Y) to use them on the other side.

SIDE WALLS (paneling, baseboard, dado, brass picture rail, cream frieze, cornice, steel skin, pinstripes)
  Wall_1m_Solid        plain panel
  Wall_1m_Pilaster     panel with a dark pilaster (use between windows)
  Wall_1m_WindowSmall  0.7 x 1.45 m open window
  Wall_2m_Window       1.6 x 1.45 m open window (sill 0.8 m)
  Wall_2m_Door         1.1 x 2.3 m side door, brass tread, exterior grab rails + step
  Window_Sash_Small / Window_Sash_Large   drop-light sashes (glass + wood frame + brass pull) for the two window
                       walls. Same pivot as their wall: place on the wall's transform. Closed fills the opening; open =
                       slide straight down (up to 1.0 m) into the wall cavity below the sill.
FLOORS / ROOFS (each includes ceiling + curved roof)
  Floor_1m, Floor_1m_Plain (no velvet runner)      underframe included
  Roof_1m, Roof_1m_Rib (ceiling rib), Roof_2m_Lamp (brass lamp + roof vent)
ENDS
  EndCap_Door / EndCap_Solid   0.5 m deep end of the car (floor, wall stubs, end wall, roof). End wall faces +Y
                               (rotate 180 for the other end). 0.5 + 13 x 1 m + 0.5 = the original 14 m car.
  Partition_Door               0.1 m cross-wall with a 1.0 x 2.3 m doorway, for compartments (place on a joint)
PROPS
  Seat_Double (2 x 1 x 1 m), LuggageRack_Full_A/B/C + LuggageRack_Empty (1 x 2 x 1 m), as before.
"""
# >>> CONFIG
EXPORT_DIR = r"C:\Users\owner\RailTracer\Assets\RailTracer\Train Assets"
KIT_NAME = "RailTracerTrain"
RACK_SEEDS = (11, 23, 37)   # one luggage-rack variant per seed; change to reroll the contents
TEXTURE_STYLE = globals().get("TEXTURE_STYLE", "pbr")   # "flat" | "toon" | "painted" | "pbr" (semi-realistic)
# Wallpaper above the dado rail: pattern = stripes | damask | fans | trellis | chevron | floral,
# paper = background colour, ink = printed colour (linear RGB), repeats = pattern repeats per metre.
WALLPAPER = globals().get("WALLPAPER", {"pattern": "stripes", "paper": (0.015, 0.015, 0.02),
                                        "ink": (0.62, 0.42, 0.12), "repeats": 5})   # gold on black
TEXTURE_RES = 1024          # pixels per texture tile
# <<< CONFIG

# ---------------------------------------------------------------- palette (names = Unity material names)
(SLOT_WOOD, SLOT_WOODDARK, SLOT_BRASS, SLOT_CREAM,
 SLOT_VELVET, SLOT_LEATHER, SLOT_IRON, SLOT_WALLPAPER, SLOT_GLASS) = set_palette([
    #  name           linear colour        metal rough  texture recipe, metres per repeat
    ("Trn_Wood",     (0.45, 0.24, 0.12), 0.0, 0.6, {"tex": "wood", "tile": 2.0, "planks": 1, "rings": 14, "seams": False}),
    ("Trn_WoodDark", (0.20, 0.10, 0.05), 0.0, 0.6, {"tex": "wood", "tile": 1.0, "planks": 2}),
    ("Trn_Brass",    (0.95, 0.70, 0.25), 1.0, 0.3, {"tex": "brushed", "tile": 0.5}),
    ("Trn_Cream",    (0.90, 0.84, 0.68), 0.0, 0.8, {"tex": "plaster", "tile": 2.0}),
    ("Trn_Velvet",   (0.45, 0.07, 0.10), 0.0, 0.9, {"tex": "velvet", "tile": 0.5}),
    ("Trn_Leather",  (0.40, 0.22, 0.10), 0.0, 0.7, {"tex": "leather", "tile": 0.5}),
    ("Trn_Iron",     (0.18, 0.20, 0.22), 0.6, 0.5, {"tex": "painted_metal", "tile": 2.0}),
    ("Trn_Wallpaper", WALLPAPER["paper"], 0.0, 0.75,
     {"tex": "wallpaper", "tile": 1.0, "pattern": WALLPAPER["pattern"], "ink": WALLPAPER["ink"],
      "repeats": WALLPAPER["repeats"]}),
    # Opaque night glass: dark and glossy, so it reflects the lamps and takes blood decals (URP decals skip transparents).
    ("Trn_Glass",    (0.010, 0.012, 0.016), 0.0, 0.05)])

# ---------------------------------------------------------------- cross-section (shared by every module)
IN_W   = 2.5          # inner wall face
Z_BOT  = -0.25        # underside of floor slab / bottom of walls
CEIL   = 3.0
TOP    = 3.15         # wall tops, ceiling top, roof underside
SILL, WIN_TOP = 0.8, 2.25
DOOR_H = 2.3
DADO_Z = 0.74         # wood / wallpaper split, inside the dado rail (0.72-0.76)
ROOF_PROFILE = [(-2.8, TOP), (2.8, TOP), (2.8, 3.30), (2.2, 3.60), (1.2, 3.78), (0.0, 3.82),
                (-1.2, 3.78), (-2.2, 3.60), (-2.8, 3.30)]

def side_wall(S, y0, y1, windows=(), door=None, pilasters=(), trim_end=None):
    """+x side wall from y0 to y1 (inner face x = 2.5). windows: [(centre, width)], door: (centre, width).
    trim_end: interior trims stop here instead of y1 (end caps: at the end wall's inner face)."""
    ty1 = y1 if trim_end is None else trim_end
    h = TOP - Z_BOT
    ops = [(c, w, SILL - Z_BOT, WIN_TOP - Z_BOT) for c, w in windows]
    if door:
        ops.append((door[0], door[1], -Z_BOT, DOOR_H - Z_BOT))
    # inner skin: wood up to the dado rail (0.74, hidden by the rail), wallpaper above
    lower = [(c_, w_, b_, t_) for c_, w_, b_, t_ in ops if b_ < DADO_Z - Z_BOT]
    upper = [(c_, w_, max(b_ - (DADO_Z - Z_BOT), 0.0), t_ - (DADO_Z - Z_BOT)) for c_, w_, b_, t_ in ops]
    wall(S, 'y', 2.55, y0, y1, Z_BOT, DADO_Z - Z_BOT, 0.10, lower, SLOT_WOOD)
    wall(S, 'y', 2.55, y0, y1, DADO_Z, TOP - DADO_Z, 0.10, upper, SLOT_WALLPAPER)
    wall(S, 'y', 2.625, y0, y1, Z_BOT, h, 0.05, ops, SLOT_IRON)       # outer painted steel skin
    # gaps in the low trims
    sill_gaps = [(c - w / 2 - 0.10, c + w / 2 + 0.10) for c, w in windows]
    door_gap = [(door[0] - door[1] / 2 - 0.08, door[0] + door[1] / 2 + 0.08)] if door else []
    band(S, 'y', IN_W, -1, y0, ty1, 0.0, 0.15, 0.03, SLOT_WOODDARK, gaps=door_gap)                 # baseboard
    band(S, 'y', IN_W, -1, y0, ty1, 0.72, 0.76, 0.045, SLOT_WOODDARK, gaps=sill_gaps + door_gap)  # dado
    for c, w in windows:
        frame_opening(S, 'y', IN_W, -1, c, w, SILL, WIN_TOP, SLOT_WOODDARK,
                      jamb=(0.08, 0.06), head=(0.10, 0.07), sill=(0.08, 0.08), sill_drop=0.08)
        S.rect(2.505, 2.52, c - w / 2, c + w / 2, SILL, SILL + 0.04, SLOT_BRASS)   # brass strip in the reveal
    if door:
        c, w = door
        frame_opening(S, 'y', IN_W, -1, c, w, 0.0, DOOR_H, SLOT_WOODDARK,
                      jamb=(0.08, 0.06), head=(0.12, 0.07), sill=None)
        S.rect(2.5, 2.65, c - w / 2, c + w / 2, 0.0, 0.012, SLOT_BRASS)            # tread plate
        S.rect(2.65, 2.95, c - w / 2, c + w / 2, -0.55, -0.50, SLOT_IRON)          # exterior step
        for s in (-1, 1):                                                          # step hangers
            S.rect(2.65, 2.95, c + s * (w / 2 - 0.03) - 0.03, c + s * (w / 2 - 0.03) + 0.03,
                   -0.50, Z_BOT, SLOT_IRON)
            yr = c + s * (w / 2 + 0.12)                                            # exterior grab rail
            S.cylinder(0.022, 0.15, 2.05, SLOT_BRASS, segs=10, center=(2.74, yr, 0))
            for za in (0.15, 2.02):                                                # rail brackets
                S.rect(2.65, 2.74, yr - 0.015, yr + 0.015, za, za + 0.03, SLOT_BRASS)
    for p in pilasters:
        S.rect(2.46, 2.50, p - 0.08, p + 0.08, 0.15, 2.60, SLOT_WOODDARK)
    S.rect(2.45, 2.50, y0, ty1, 2.60, 2.65, SLOT_BRASS)          # picture rail
    S.rect(2.49, 2.50, y0, ty1, 2.65, 2.85, SLOT_CREAM)          # cream frieze
    S.rect(2.38, 2.50, y0, ty1, 2.85, CEIL, SLOT_WOODDARK)       # cornice
    band(S, 'y', 2.65, 1, y0, y1, 0.50, 0.55, 0.015, SLOT_BRASS, gaps=door_gap)   # exterior pinstripes
    band(S, 'y', 2.65, 1, y0, y1, 2.45, 2.50, 0.015, SLOT_BRASS)

def sash(S, w):
    """Drop-light sash for a window of width w in the +x side wall (wall-module coords). It sits in the wall's
    cavity (x 2.56-2.59, between the 2.50 inner face and the 2.65 outer face) and runs 2 cm into the wall on every
    side, so no face lies on the opening's reveals; lowered, it disappears into the wall below the sill."""
    h = w / 2
    S.rect(2.56, 2.59, -h - 0.02, -h + 0.05, SILL - 0.02, WIN_TOP + 0.02, SLOT_WOODDARK)   # stiles
    S.rect(2.56, 2.59, h - 0.05, h + 0.02, SILL - 0.02, WIN_TOP + 0.02, SLOT_WOODDARK)
    S.rect(2.56, 2.59, -h + 0.05, h - 0.05, SILL - 0.02, SILL + 0.05, SLOT_WOODDARK)       # bottom rail
    S.rect(2.56, 2.59, -h + 0.05, h - 0.05, WIN_TOP - 0.05, WIN_TOP + 0.02, SLOT_WOODDARK) # top rail
    S.rect(2.570, 2.580, -h + 0.03, h - 0.03, SILL + 0.03, WIN_TOP - 0.03, SLOT_GLASS)     # pane, edges in the frame
    S.rect(2.54, 2.565, -0.06, 0.06, WIN_TOP - 0.045, WIN_TOP - 0.01, SLOT_BRASS)          # pull on the top rail

def floor_part(S, y0, y1, runner=True, under=True):
    S.rect(-IN_W, IN_W, y0, y1, Z_BOT, 0.0, SLOT_WOODDARK)
    if runner:
        S.rect(-0.5, 0.5, y0, y1, 0.0, 0.015, SLOT_VELVET)
    if under:
        S.rect(-2.3, 2.3, y0, y1, -0.6, Z_BOT, SLOT_IRON)

def roof_part(S, y0, y1):
    S.rect(-IN_W, IN_W, y0, y1, CEIL, TOP, SLOT_CREAM)
    S.extrude(ROOF_PROFILE, 'y', y0, y1, SLOT_IRON)

def lamp(S, y):
    S.lathe([(0.09, CEIL), (0.09, 2.98), (0.025, 2.97), (0.025, 2.88), (0.22, 2.86), (0.24, 2.83)],
            segs=24, slot=SLOT_BRASS, center=(0, y, 0))
    S.lathe([(0.235, 2.83), (0.20, 2.76), (0.10, 2.72), (0.01, 2.71)],
            segs=24, slot=SLOT_CREAM, center=(0, y, 0), cap_top=False)

def rib(S, y):
    S.rect(-2.38, 2.38, y - 0.06, y + 0.06, 2.92, CEIL, SLOT_WOODDARK)

# ---------------------------------------------------------------- module builders
def module(name, fn):
    def build():
        a = Asset(name, seed=3)
        fn(a.S)
        return a.finish()
    build.__name__ = name
    return build

WALLS = [
    module("Wall_1m_Solid",       lambda S: side_wall(S, -0.5, 0.5)),
    module("Wall_1m_Pilaster",    lambda S: side_wall(S, -0.5, 0.5, pilasters=(0.0,))),
    module("Wall_1m_WindowSmall", lambda S: side_wall(S, -0.5, 0.5, windows=[(0.0, 0.7)])),
    module("Wall_2m_Window",      lambda S: side_wall(S, -1.0, 1.0, windows=[(0.0, 1.6)])),
    module("Wall_2m_Door",        lambda S: side_wall(S, -1.0, 1.0, door=(0.0, 1.1))),
    module("Window_Sash_Small",   lambda S: sash(S, 0.7)),
    module("Window_Sash_Large",   lambda S: sash(S, 1.6)),
]

def _roof_lamp(S):
    roof_part(S, -1.0, 1.0)
    lamp(S, 0.0)
    S.lathe([(0.16, 3.78), (0.16, 3.95), (0.24, 3.97), (0.20, 4.05), (0.01, 4.07)],
            segs=16, slot=SLOT_IRON, center=(0, 0, 0))                     # roof vent

FLOORS_ROOFS = [
    module("Floor_1m",       lambda S: floor_part(S, -0.5, 0.5)),
    module("Floor_1m_Plain", lambda S: floor_part(S, -0.5, 0.5, runner=False)),
    module("Roof_1m",        lambda S: roof_part(S, -0.5, 0.5)),
    module("Roof_1m_Rib",    lambda S: (roof_part(S, -0.5, 0.5), rib(S, 0.0))),
    module("Roof_2m_Lamp",   _roof_lamp),
]

def end_cap(S, door=True):
    """Occupies y -0.25..0.25. End wall (0.15) at y 0.10..0.25, interior y -0.25..0.10."""
    Y0, YI, YO = -0.25, 0.10, 0.25
    floor_part(S, Y0, YI, runner=True, under=False)
    S.rect(-2.3, 2.3, Y0, YO - 0.1, -0.6, Z_BOT, SLOT_IRON)                 # underframe end + buffer beam
    S.rect(-2.6, 2.6, YO - 0.1, YO, -0.62, -0.30, SLOT_IRON)
    side_wall(S, Y0, YO, trim_end=YI)                                      # side stubs, +x ...
    tmp = MB()
    side_wall(tmp, Y0, YO, trim_end=YI)
    S.merge_mirror_x(tmp)                                                  # ... and mirrored to -x
    ops = [(0.0, 1.2, -Z_BOT, DOOR_H - Z_BOT)] if door else []
    h = TOP - Z_BOT
    wall(S, 'x', 0.15, -IN_W, IN_W, Z_BOT, h, 0.10, ops, SLOT_WOOD)
    wall(S, 'x', 0.225, -IN_W, IN_W, Z_BOT, h, 0.05, ops, SLOT_IRON)
    face = YI
    def er(xa, xb, d, za, zb, slot):
        S.rect(xa, xb, face - d, face, za, zb, slot)
    if door:
        for xa, xb in ((-2.47, -0.72), (0.72, 2.47)):
            er(xa, xb, 0.03, 0.0, 0.15, SLOT_WOODDARK)                     # baseboard
        for sg in (1, -1):
            er(*sx(sg, 0.60, 0.72), 0.06, 0.0, DOOR_H, SLOT_WOODDARK)       # door jambs
        er(-0.72, 0.72, 0.06, DOOR_H, DOOR_H + 0.12, SLOT_WOODDARK)        # door head
        er(-0.25, 0.25, 0.02, 2.46, 2.56, SLOT_BRASS)                      # car number plate
        S.rect(-0.6, 0.6, YI, YO, 0.0, 0.012, SLOT_BRASS)                  # tread plate
        panels = ((0.95, 2.25),)
    else:
        er(-2.47, 2.47, 0.03, 0.0, 0.15, SLOT_WOODDARK)
        er(-0.6, 0.6, 0.02, 0.30, 0.90, SLOT_WOODDARK)                    # centre panels
        er(-0.6, 0.6, 0.02, 1.05, 2.40, SLOT_WOODDARK)
        er(-0.25, 0.25, 0.025, 2.46, 2.56, SLOT_BRASS)
        panels = ((0.95, 2.25),)
    for sg in (1, -1):
        for a0, a1 in panels:
            er(*sx(sg, a0, a1), 0.02, 0.30, 0.90, SLOT_WOODDARK)
            er(*sx(sg, a0, a1), 0.02, 1.05, 2.40, SLOT_WOODDARK)
    er(-2.45, 2.45, 0.05, 2.60, 2.65, SLOT_BRASS)                          # picture rail
    er(-2.49, 2.49, 0.01, 2.65, 2.85, SLOT_CREAM)
    er(-2.38, 2.38, 0.12, 2.85, CEIL, SLOT_WOODDARK)                       # cornice
    S.rect(-IN_W, IN_W, Y0, YI, CEIL, TOP, SLOT_CREAM)                     # ceiling
    S.extrude(ROOF_PROFILE, 'y', Y0, YO, SLOT_IRON)                        # roof

ENDS = [
    module("EndCap_Door",  lambda S: end_cap(S, door=True)),
    module("EndCap_Solid", lambda S: end_cap(S, door=False)),
]

def partition(S):
    """Cross-wall at y = 0 (0.1 thick), 1.0 x 2.3 m doorway, trimmed both faces."""
    wall(S, 'x', 0.0, -IN_W, IN_W, 0.0, CEIL, 0.10, [(0.0, 1.0, 0.0, DOOR_H)], SLOT_WOOD)
    for f in (1, -1):
        face = 0.05 * f
        def pr(xa, xb, d, za, zb, slot):
            y0_, y1_ = sorted((face, face + f * d))
            S.rect(xa, xb, y0_, y1_, za, zb, slot)
        for sg in (1, -1):                       # each trim stops at the matching side-wall trim face
            pr(*sx(sg, 0.62, 2.47), 0.03, 0.0, 0.15, SLOT_WOODDARK)
            pr(*sx(sg, 0.62, 2.455), 0.045, 0.72, 0.76, SLOT_WOODDARK)
            pr(*sx(sg, 0.62, 2.45), 0.05, 2.60, 2.65, SLOT_BRASS)
        for sg in (1, -1):
            pr(*sx(sg, 0.50, 0.62), 0.06, 0.0, DOOR_H, SLOT_WOODDARK)
        pr(-0.62, 0.62, 0.07, DOOR_H, DOOR_H + 0.12, SLOT_WOODDARK)
        pr(-2.38, 2.38, 0.10, 2.85, CEIL, SLOT_WOODDARK)
    S.rect(-0.5, 0.5, -0.05, 0.05, 0.0, 0.012, SLOT_BRASS)                 # threshold

PARTS = [module("Partition_Door", partition)]


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

def lamp_wall():
    """Wall sconce. Pivot = mounting point (back face of the back plate, centred), back at +Y, out along -Y.
    Back plate 0.11 wide sits inside the 0.16 m pilaster. Every layer has its own depth / radius."""
    a = Asset("Lamp_Wall", seed=3)
    S = a.S
    S.rect(-0.055, 0.055, -0.020, 0.0, -0.15, 0.15, SLOT_WOODDARK)       # back plate, flat on the pilaster
    S.rect(-0.040, 0.040, -0.032, -0.020, -0.11, 0.11, SLOT_BRASS)       # stepped brass plate (deco)
    S.rect(-0.025, 0.025, -0.040, -0.032, 0.03, 0.09, SLOT_BRASS)        # arm boss
    S.rect(-0.012, 0.012, -0.225, -0.040, 0.040, 0.062, SLOT_BRASS)      # arm out from the wall
    sy = -0.24                                                           # shade axis
    S.lathe([(0.022, 0.075), (0.022, 0.030), (0.036, 0.020), (0.036, 0.0)],
            segs=20, slot=SLOT_BRASS, center=(0, sy, 0))                 # collar the arm runs into
    S.lathe([(0.034, 0.0), (0.060, -0.040), (0.090, -0.100), (0.100, -0.130)],
            segs=20, slot=SLOT_CREAM, center=(0, sy, 0), cap_top=False)  # frosted bell shade (top hidden by collar)
    S.annulus(0.094, 0.108, -0.137, -0.117, SLOT_BRASS, segs=20, center=(0, sy, 0))   # brass rim, proud of the shade
    return a.finish()

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


builders = WALLS + FLOORS_ROOFS + ENDS + PARTS + [seat_double, lamp_wall]
builders += [make_rack(f"LuggageRack_Full_{k}", s) for k, s in zip("ABC", RACK_SEEDS)]
builders += [make_rack("LuggageRack_Empty", 1, full=False)]
run(builders, spacing=8.0)
