# Pitfalls learned the hard way

**Always read the z-fighting report from `preview.py` and fix every FAIL before delivering.**
Z-fighting = two visible faces on the same plane, same direction, overlapping. In the engine they
flicker. The check works per exported mesh (Structure_SOLID, Props_SOLID, Detail separately).

## Z-fighting rules
- **Bands, not slabs.** Courses, friezes, cornices and stripes wrap the walls (`band`), they are never
  full-footprint boxes at slab height.
- **Floors run to the inner wall faces**, never out to the outer faces.
- **Walls under a slab stop at the slab's underside** (or the slab sits between the walls).
- **Every trim type has its own projection depth** (see style_guide.md section 2).
- **Things that share a bottom** (an item and the strap around it, a crate and its bands): start the
  band 3-5 mm above the item's bottom. Two downward faces at the same z overlap otherwise.
- **Crossing straps** (parcel string): give the second strap a slightly higher top (+2 mm).
- **Corners:** side runs go full length, the perpendicular runs fit between them (or stop at the
  other's face). A 3 mm inset is enough.
- **Overhanging caps** on posts/rails: make them a few mm bigger in both axes or keep them exactly inside;
  equal-size flush is fine only when the shared face is fully hidden.
- Opposite-facing faces that touch (a seat resting on a floor, a shelf in a post) are fine: hidden.

## Geometry / Blender rules
- **Never merge-by-distance + bevel on block-built geometry.** Touching boxes stitched into
  non-manifold seams make the Bevel modifier grow slivers and dark seam lines. The lib does no merge
  and no bevel on `Structure_SOLID`/`Props_SOLID`. Only `Detail` (pieces that never share edges)
  gets a 2 cm bevel. Softer edges on architecture = explicit chamfer geometry on one piece.
- **Booleans** are slow and fragile for kits: build walls from boxes around openings (`wall()`).
- **Floating parts:** anything resting on something removed (ruins, cutaways, random fills) must be
  skipped too. The preview shows it.
- **Exact sizes:** after building, check the asset bounding box (the real-Blender check prints the
  scene; or compute min/max of `asset.S.v`). Caps, rails and handles must stay inside the spec size.
- **Clearance against walls:** furniture placed next to trimmed walls needs to clear the deepest trim
  at that height (sills, pilasters), or it clips.
- **Double-sided foliage:** don't merge (front/back verts share positions) and don't recalc normals.

## Blender version drift (3.x -> 5.x)
- `shade_smooth_by_angle` (4.1+) vs `use_auto_smooth` (<= 4.0): use `shade()`.
- `bmesh.ops.create_icosphere(radius=)` vs `diameter=`: try/except (in `rock()`).
- `Material.use_nodes` / `World.use_nodes`: deprecated in 5.0 (always on). Only set it below 5.0.
- Principled input names "Base Color", "Metallic", "Roughness" are stable across 3.x-5.x.

## Preview shim limits
`preview.py` runs the geometry without Blender through a numpy mathutils shim: only Matrix
(Identity, Translation, Rotation, Diagonal, Scale, @) and Vector (+ - * /, normalized, x/y/z, length,
copy) exist. Stick to those in piece code. bmesh: only `create_icosphere`.

## Delivery
- **The user runs ONE file.** Always deliver the assembled, self-contained script.
- Put anything the user might tweak (EXPORT_DIR, seeds, sizes, toggles) in the CONFIG block.
- Preview-only toggles (cutaways) read `globals().get("NAME", default)` so `--set NAME=...` can flip them
  without changing the delivered file.

## Textures / UVs
- UVs are world-scale box projections in object space (1 UV = 1 m). Textures line up across modules
  only if module lengths are whole multiples of the material's `tile` (1 m grid + tile 0.5/1/2 m: OK).
- Lathed/round parts get projection seams where the dominant axis flips - keep busy patterns
  (wallpaper, planks) off round parts; use plain/brushed/fabric recipes there.
- Wood grain follows u: on walls along the run, on floors across X. Choose `planks`/`seams` per use
  (veneer panels: `planks: 1, seams: False`).
- `TEXTURE_STYLE = "flat"` writes no textures (palette colours only) - the default in kit_lib.
- Renders with textures: one Cycles render per shell command (~1 min each at 32 samples), never loop
  several in one command (5 min tool limit; background jobs are killed when the command ends).
