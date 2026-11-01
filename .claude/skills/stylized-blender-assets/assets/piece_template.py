"""
<KIT TITLE>   (Blender 3.x-5.x -> Unity FBX)
Run in Blender (Scripting tab, or `blender --python this.py`). Clears the scene, exports one FBX per asset.

<One line per asset: name, exact size (w x h x l), pivot/facing, what it is for in gameplay.>
"""
# kit_lib (MB, Asset, wall, frame_opening, band, block_walls, slab_with_hole, rock, set_palette, sx,
# run, ...) is pasted above this by scripts/assemble.py. Do not import it.

# >>> CONFIG
EXPORT_DIR = r""          # asked at the start of the set, e.g. r"C:/Projects/Game/Assets/Models/Market"
KIT_NAME = "Market"       # default export folder = <KIT_NAME>_FBX next to the .blend
SEED = 7                  # reroll variation
# <<< CONFIG

# ---------------------------------------------------------------- palette (names = Unity material names)
WOOD, WOOD_DARK, CLOTH, METAL = set_palette([
    ("Mkt_Wood",     (0.45, 0.26, 0.13), 0.0, 0.7),
    ("Mkt_WoodDark", (0.22, 0.12, 0.06), 0.0, 0.7),
    ("Mkt_Cloth",    (0.70, 0.20, 0.15), 0.0, 0.9),
    ("Mkt_Metal",    (0.55, 0.55, 0.58), 1.0, 0.4)])

def crate():
    """Example: 1 x 1 x 1 m crate, pivot bottom-centre, frame boards proud 1 cm (own depth)."""
    a = Asset("Crate_1m", seed=SEED)
    S = a.S
    S.rect(-0.5, 0.5, -0.5, 0.5, 0.0, 1.0, WOOD)
    for z0 in (0.004, 0.9):                                   # bands: start 4 mm up (no coplanar bottoms)
        S.rect(-0.51, 0.51, -0.51, 0.51, z0, z0 + 0.096, WOOD_DARK)
    return a.finish()

def market_stall():
    """Example: 3 x 2.6 x 2 m stall, counter at the front (+Y), cloth awning."""
    a = Asset("Stall_Market", seed=SEED)
    S, r = a.S, a.rng
    for x in (-1.45, 1.45):
        for y in (-0.95, 0.95):
            S.rect(x - 0.05, x + 0.05, y - 0.05, y + 0.05, 0.0, 2.4, WOOD_DARK)
    S.rect(-1.4, 1.4, 0.55, 0.9, 0.0, 1.0, WOOD)                      # counter
    S.rect(-1.45, 1.45, 0.5, 1.0, 1.0, 1.05, WOOD_DARK)               # counter top (overhangs 5 cm)
    S.extrude([(-1.6, 2.4), (1.6, 2.4), (0.0, 2.6)], 'y', -1.1, 1.1, CLOTH)   # awning
    return a.finish()

run([crate, market_stall], spacing=6.0)
