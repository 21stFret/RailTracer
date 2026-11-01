# ======================================================================
# FATHOM / ATLANTIS PROJECT LIB  -  sits on top of kit_lib.py
# assemble with:  python scripts/assemble.py piece.py -o out.py --with assets/projects/fathom/fathom_lib.py
# Adds: the Atl_* palette + slot names, Fathom grid constants, the kit column (with collider proxy),
# Atlantean wall trim (wall(..., trim=True)), dress_block, parapets, emblem, orbs, pediments.
# Fully compatible with the original atlantis-blender-kit piece scripts.
# ======================================================================

# ---------------------------------------------------------------- palette + constants
SLOT_MARBLE, SLOT_DARK, SLOT_GOLD, SLOT_ROCK = set_palette([
    ("Atl_Marble",     (0.62, 0.70, 0.66), 0.0, 0.7),
    ("Atl_MarbleDark", (0.40, 0.48, 0.45), 0.0, 0.7),
    ("Atl_Gold",       (0.95, 0.70, 0.25), 1.0, 0.3),
    ("Atl_Rock",       (0.42, 0.50, 0.50), 0.0, 0.7)])
if KIT_NAME == "Kit":
    KIT_NAME = "AtlantisKit"          # default export folder AtlantisKit_FBX, as before
Asset.DETAIL_NAME = "Columns"          # Unity prefabs expect <Asset>_Columns / _Columns_COL
Asset.ANCHOR_GROUP = "GrappleAnchors"   # and <Asset>_GrappleAnchors/<Asset>_Anchor_##

GRID   = 20.0      # building footprint (m); pieces snap on this grid, rotate in 90 deg steps
LEVEL  = 14.0      # one terrace level = 2 stories
STORY  = 7.0
SLAB   = 0.6
WALL_T = 0.8
KIT_COLUMN_H = 3.92   # height of the reference column at scale 1

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

def _asset_column(self, x, y, z, height, kh, state='ok', yaw=0.0, anchor=True):
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

def _asset_fallen(self, x, y, yaw, kh, klen, length_kit=2.96, z=0.0):
    self.n += 1
    self.P.merge(kit_lying_shaft(2000 + self.n, length_kit, rings=10), place_lying(kh, klen, x, y, z, yaw))

def _asset_drum(self, x, y, kh, z=0.0):
    self.n += 1
    self.P.merge(kit_lying_shaft(3000 + self.n, 1.0, rings=4), place_lying(kh, kh, x, y, z, self.rng.uniform(0, 360)))

Asset.column = _asset_column
Asset.fallen = _asset_fallen
Asset.drum = _asset_drum
