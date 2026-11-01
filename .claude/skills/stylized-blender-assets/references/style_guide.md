# Style guide - stylized, readable, game-ready

The look this skill produces: chunky, clean, toon/cel-friendly geometry built from simple solids,
with a small palette and crisp trim layers. It reads well at game distance, under outline shaders
and flat lighting, and it stays cheap. The same rules work for a 1930s train, a fantasy tavern,
a sci-fi corridor or an Atlantean temple; only the palette, motifs and proportions change.

## Contents
1. Shapes and proportions
2. Trim layering (the thing that makes it look "finished")
3. Palette and materials
4. Detail and triangle budget
5. Modularity and grids
6. Gameplay dimensions
7. Variation with seeds
8. Motif cheat-sheet by setting

## 1. Shapes and proportions
- Build from boxes (`rect`), extrusions (`extrude`), lathes (`lathe`, `cylinder`, `sphere`) and
  `wall` with openings. No booleans, no sculpting.
- Exaggerate slightly: thicker frames, bigger handles, deeper trims than reality. A 2 cm real-world
  moulding becomes 4-8 cm; a suitcase handle is a chunky block. Thin things vanish at game distance.
- Silhouette first: every asset should be recognisable as a flat black shape. Add the one or two
  features that make the silhouette (a roof curve, an awning, a backrest rail) before small detail.
- Round things get modest segment counts: 16-24 for props, 32-48 for hero pieces.

## 2. Trim layering
"Finished" comes from layers proud of a base surface, each at its **own projection depth**:
base surface -> baseboard (3 cm) -> pilaster (4 cm) -> dado (4.5 cm) -> jamb (6 cm) -> head (7 cm)
-> sill (8 cm) -> cornice (12 cm). Picking distinct depths per trim type is what keeps faces from
ever being coplanar (no z-fighting) and gives the outline shader edges to catch.
- Use `frame_opening` for jambs/head/sill around doors and windows, `band` for horizontal runs
  (baseboards, dado rails, cornices, stripes), with gaps where openings or sills interrupt.
- Contrast: trims in a darker or accent material than the surface they sit on.
- Accent metal (brass, gold, chrome) is used sparingly: rails, caps, plates, latches. One accent.

## 3. Palette and materials
- 4-8 materials per kit. Each palette entry is `(name, linear RGB, metallic, roughness)`; the name is
  the engine contract (Unity material asset with the same name). Prefix per kit: `Trn_`, `Mkt_`, `Atl_`.
- Re-use an existing project palette when assets will sit together; new materials = tell the user
  the exact names to create.
- Textures are optional and generated (see SKILL.md "Textures"): give palette entries a recipe and
  pick `TEXTURE_STYLE`. Keep pattern scale readable at game distance (wallpaper stripes ~20 cm,
  planks 25-50 cm) and detail modest - the user may want "not high detail".
- Colour balance: one dominant warm (wood), one accent metal, one saturated fabric, and at least one
  contrasting surface colour (e.g. dark or teal wallpaper) so the space isn't all one hue family.

## 4. Detail and triangle budget
| Asset class | Typical tris |
|---|---|
| Small prop (crate, chair, bag) | 30-300 |
| Furniture with detail (seat, shelf unit) | 150-1,500 |
| Room / vehicle shell with trim | 2k-6k |
| Building module | 5k-20k |
| Hero landmark | 20k-60k |
Lathed details (columns, lamps, wheels) dominate counts; drop `segs` first if a budget is tight.
Report tri counts in the delivery message for anything over ~5k.

## 5. Modularity and grids
- Pivot at bottom-centre; meters; Z up in Blender (exports Y up for Unity).
- Decide the grid before building: furniture on 0.5 or 1 m, rooms/vehicles on their natural rhythm
  (train bays 3 m, building modules 4/5/10/20 m). Write the grid into the docstring.
- Pieces that combine must share exact interface numbers: floor height (put walkable floors at z = 0 so
  props drop in without offsets), door positions, wall thickness, story height.
- Make exact sizes exact. If the user says 2 x 1 x 1 m, the bounding box is exactly that; caps and
  handles stay inside it. Verify bounds before delivering.
- Interior-facing furniture: fit 3-5 cm clear of wall trim so it never clips.

**Modular kits (walls/floors/roofs that snap):**
- One shared cross-section for every module (inner wall face, floor z = 0, ceiling, wall top, roof
  profile). Every trim runs the full module length so it continues seamlessly across joints.
- Pivot convention, stated in the docstring: e.g. centreline x = 0, floor level, centre of the piece
  along the run. Wall modules are built for ONE side and rotated 180 for the other (keep each module
  symmetric along the run so rotation never changes it).
- Unit lengths 1 m and 2 m (+ half-depth end caps) give every total length. Openings + frames must fit
  inside the module (window + jambs + sill overhang < module length).
- End pieces: trims stop at the end wall's inner face (otherwise they poke through and z-fight).
- Cross walls (partitions) stop each trim at the matching side-wall trim face.
- Ship a layout-string builder for the engine and check layouts with `joint_check.py`.

## 6. Gameplay dimensions
Ask what the player is (size, movement mode) if it's not obvious. Defaults for a ~1.8 m character:
| Thing | Walking game | Swimming / flying / big-camera game |
|---|---|---|
| Door | 1.0-1.2 x 2.1-2.3 m | 3-6 m wide |
| Corridor / aisle | 1.2-2 m | 4-7 m |
| Ceiling | 2.6-3.2 m | 6+ m |
| Cover | 1.0-1.2 m | 1.2 m |
| Railing | 1.0-1.1 m | 1.1 m with gaps |
| Stair step | 0.18 x 0.28 m | ramps |
Every room should have 2+ ways in/out where the game allows. Realistic dimensions (a 1 m train aisle)
are fine when the user picks them: just say what it means for gameplay.

## 7. Variation with seeds
- Everything random goes through `Asset(name, seed).rng` (or a seeded `random.Random`), so output is
  repeatable. Expose seeds in the CONFIG block (`RACK_SEEDS = (11, 23, 37)`) to give A/B/C variants.
- Fill containers by packing (left-to-right, rows front/back, stacking with headroom) rather than
  scattering; a "full" shelf should look full.
- Damage/ruin: `Asset.state(p_broken, p_missing)` per element; skip anything that would float.

## 8. Motif cheat-sheet
| Setting | Palette idea | Motifs |
|---|---|---|
| 1930s / Art Deco | dark wood, cream, brass, burgundy velvet | pilasters, brass rails, stepped tops, sunburst |
| Medieval / fantasy | timber, plaster, slate, iron | exposed beams, X-bracing, chunky iron straps, round arches |
| Sci-fi | white panel, dark grey, emissive accent | bevel-cut panels, recessed light strips, hex/chevrons |
| Western | sun-bleached wood, red paint, rust | plank gaps, false fronts, hitching posts |
| Classical / Atlantis | marble, dark marble, gold | fluted columns, pediments, gold friezes (see projects/fathom.md) |
