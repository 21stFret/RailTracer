# Project profile: Fathom (underwater Atlantis)

Unity game: players swim in 3D with weapons and a grappling hook through Atlantean ruins.

## How to build Fathom pieces
- Assemble with the project lib:
  `python scripts/assemble.py piece.py -o /mnt/user-data/outputs/atlantis_<name>.py --with assets/projects/fathom/fathom_lib.py`
- `fathom_lib.py` sets the `Atl_*` palette (`SLOT_MARBLE, SLOT_DARK, SLOT_GOLD, SLOT_ROCK`), the grid
  constants (`GRID 20, STORY 7, LEVEL 14, SLAB 0.6, WALL_T 0.8`), `Asset.DETAIL_NAME = "Columns"`,
  `Asset.ANCHOR_GROUP = "GrappleAnchors"`, default folder `AtlantisKit_FBX`, and adds: `kit_column`,
  `shaft`, `kit_lying_shaft`, `pediment`, `emblem`, `orb`, Atlantean `wall(..., trim=True)`,
  `block_walls`, `block_band`, `dress_block`, `parapet`, `slab_with_hole` (gold rim),
  `floor_with_thresholds`, `planter`, and `Asset.column()/.fallen()/.drum()`.
- Always use `Asset.column()` for columns: it builds the smooth `Columns_COL` proxy (the grapple hook
  snags on fluted meshes otherwise). Pass `anchors=True` to `finish()` for grapple-heavy pieces.
- `examples/atlantis_story_kit.py` is in lib+piece form: its PIECE section re-assembles on the new core
  with identical output. The other examples are older self-contained scripts: read them for
  proportions/ideas, don't re-assemble them.
- Ask the user once for the Unity export folder (e.g. `.../Assets/Atlantis/FBX`) and remember it.

## Design guidance (Fathom)
- Gameplay first: generous openings (>= 4 m doors, 6-7 m passages), ceilings >= 6.4 m, waist-high cover
  (~1.2 m), multiple routes in and out of every space, vertical routes (slab holes, skylights,
  collapsed roofs, railing gaps).
- Grapple playgrounds: columns 3-6 m apart, flat tops as landings, broken columns at varied heights,
  beams only between intact neighbours (`runs()`), high anchor points (spires, rings, gables).
- Ruins with control: seeded `Asset(name, seed)`; expose seeds/probabilities.
- Modularity: stacked pieces share footprints and exact heights; tops have no floor of their own.
- Budget: a detailed column is ~1.2k tris. Report totals for big pieces.

All pieces: meters, pivot at bottom-center, Z up in Blender (Y up in Unity), materials `Atl_*`.
Full original scripts are in `assets/projects/fathom/examples/` — read the relevant one before making a
variation of an existing piece (e.g. a new temple → `atlantis_temples.py`).

## Props & nature — `atlantis_kit.py`, `atlantis_rocks.py`
| Asset | Size | Notes |
|---|---|---|
| Pillar_Intact | 1.15 m abacus, 3.92 m tall | THE reference column (`kit_column`). Fluted Doric shaft w/ entasis, gold ring under abacus |
| Pillar_Broken_Tall / _Short / _Stump | 2.3 / 1.1 / 0.3 m shafts | jagged chipped tops |
| Pillar_Fragment | 1.2 m drum lying down | both ends broken |
| Rubble_Pile | ~1.5 m cluster | icosphere chunks |
| Chest | 0.9 x 0.55 x 0.42 body | `Chest_Lid` child pivots on hinge (rotate local X), `Chest_Gold` child |
| Coins_Scatter | ~1.2 m | |
| Kelp_Tall / Seaweed_Clump | 2.2–3.8 m / 0.4–1 m | double-sided ribbons, vertex color R/G/B 0 root → 1 tip (sway mask), own UVs, `Atl_Kelp` / `Atl_Seaweed` |
| Rock_Boulder_A/B, _Large, Rock_Slab, Rock_Spire, Rock_Cluster, Rock_Arch | 1–4.4 m (arch span 4.4 m) | decimated faceted chunks, optional strata, `Atl_Rock` |

## Buildings
| Asset | Footprint | Script | Key gameplay numbers |
|---|---|---|---|
| House_Atlantis_S32 | 32 x 32 (SIZE param 24/32/40) | atlantis_house.py | 8 m ceilings, main door 6x6.5, doors 4x5.5, windows 2.4x3, cover 1.2 m, open atrium, collapsed corner, skylight, walkable roof |
| Temple_Peristyle | 44 x 72 | atlantis_temples.py | 34 x 14 m columns (kh 2.2), inner hall columns 10 m, beams only between intact columns, back pediment |
| Temple_Rings | 60 m round | atlantis_temples.py | outer ring 20 x 16 m columns @ r25, moat, island ring 12 x 10 m, 30 m spire + floating gold ring @ 21 m |
| Base_Passage / Base_Loggia | 20 x 20, 14 m | atlantis_terrace_kit.py | 2 stories of 7 m, passages 7 x 6.4 m, roof exactly at 14 m |
| Top_LTerrace / _Slope / _Balcony / _Shrine / _Parapet | 20 x 20, sit at +14 m | atlantis_terrace_kit.py | rotate 90°, railings with step-off gaps, balcony cantilevers 3 m |

## Story modules (CURRENT house system) — `atlantis_story_kit.py`
The user's Unity house builder randomly stacks these. Each is 20 x 20 m and exactly 7 m (one story).
| Asset | Role | Notes |
|---|---|---|
| Story_Ground_Passage | ground (y=0) | 7 x 6.4 m through-passage, side rooms, roof holes on diagonal A |
| Story_Ground_Loggia | ground (y=0) | porch columns, L passage, big room, roof holes on diagonal B |
| Story_Upper_Hall / _Balcony / _Gallery | any level ≥ 1 | no own floor; balcony cantilevers 3 m at floor level; gallery = open colonnade front |
| Roof_Flat / Roof_Slope / Roof_Shrine | on top of any story | railing gaps; slope = attic 2.9→6.5 m; shrine fits inside x ±4.3 |
**Hole contract:** every story roof slab has two 4 x 4 m holes in corner zones centred (±6.5, ±6.5).
All upper stories and roofs keep all four zones free (0.06 m above floor up to their own slab), so
any stack in any 90° rotation keeps vertical routes open. New upper/roof modules MUST respect this
(verify with a zone check before delivering). The older Base_*/Top_* terrace pieces are superseded.

## Gameplay dimensions (keep consistent)
- Player ≈ 1.8–2 m, swimming in 3D, has weapons and a **grappling hook**.
- Story = **7 m**, terrace level = **14 m** (2 stories). Building grid = **20 m**. Modules stack per story.
- Passages/doors: main ≥ 6 m wide, normal ≥ 3.5–4 m wide x 4.5–5.5 m tall, through-passages 7 x 6.4 m.
- Ceilings ≥ 6.4 m. Cover walls ≈ 1.1–1.2 m. Railings 1.1 m with 3–5 m gaps.
- Grapple: column tops are flat 2.5–2.8 m landings, gaps 3–6 m between columns, broken columns = perches
  at random heights, beams only between intact columns (gaps to swing through). Use `_COL` cylinders.
- Every space needs ≥ 2 routes, plus vertical routes (holes in slabs, skylights, collapsed roof).

## Visual language
- Marble walls (`Atl_Marble`), dark marble trims/floors (`Atl_MarbleDark`), gold accents (`Atl_Gold`).
- Signature details: fluted columns, stepped gold lintels over openings, gold frieze band below a dark
  cornice, dark base course, corner pilasters, triangular pediments, **Atlantis concentric-rings emblem**,
  gold orbs on pedestals, concentric ring floors/walls.
- Ruin flavour via seeded randomness: broken/missing columns, collapsed wall chunks, fallen drums, rubble.
- Chunky, readable shapes for the cel shader; small bevels (0.02–0.04) so outlines/bands catch edges.
