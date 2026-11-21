# Reskinning rigged characters (clothes and hair on an existing skeleton)

Use this when the job is a new outfit, garment or hairstyle on a character that already has a skinned mesh and a
skeleton the game's animations, ragdoll and takedowns rely on. Worked example (Rail Tracer, Update 19.59):
`assets/projects/railtracer/railtracer_mob_reskin.py` (Teddy Boy guards: CH02's jacket restyled onto CH01, a quiff
from CH01's own hair; flat-cap heavies).

## The rule that made it work: restyle real geometry, don't model from scratch

Character packs are far more detailed than our stylized props. A garment built from code (lofted rings, superellipse
torsos, hand-placed lapels) looks blocky beside them, sleeves collapse at the elbow, and its weights are guesses.
What worked instead:

- **Borrow the closest real garment** from another character on the SAME skeleton (check: import both, compare every
  bone head; identical to ~1e-6 m means faces carry over with their own weights and UVs untouched).
- **Restyle it with smooth deformations and cuts**: open a front, lengthen a hem, narrow a tie, recolour pieces.
- **For hair, bend the character's own hair** (strand cards) with a smooth field instead of adding a blob: it keeps
  the hair's texture and blends into the back with no hard edge.

Ask the user for a reference photo of the target look before starting; compare renders against it.

## Workflow (all in one Blender script, run headless, re-runnable)

1. **Read the pack meshes by island.** `grab(path, keep)`: flood-fill connected faces of one material into islands
   with stats (face count, bbox, mean); `keep(island)` picks what to take, with weights and UVs. Render the islands in
   distinct colours once (front + side) to learn which is which: panels, back, sleeves, lapels, collar, pocket
   flaps, buttons, hem facings. Classify by stats (size, height range, side), not by index.
2. **Body first, then layers.** Load the target body; delete what the new clothes replace and the skin they cover
   (keep wrists, neck, head); everything else stays.
3. **Restyle the borrowed garment:**
   - *Open a double-breasted front*: give each front vertex a side (+x/-x) from the nearest big front panel (KD-tree);
     lapels go by their island's mean side; then per height slab remap `u = side*x` so the front edge lands at the
     opening half-width, fading to nothing at the side seams and round the back. **Cut any face whose verts got
     different sides** (strips that ran across the middle bridge the opening otherwise). Only label front verts: the
     back seam has mixed sides too.
   - *Lengthen*: stretch everything below a line above the pockets down to the new hem (`z' = z0 - (z0 - z) * k`),
     with a little flare; reweight the skirt (pelvis + both thighs, more thigh lower down, 50/50 across the middle)
     so it swings instead of splitting.
   - *Recolour pieces*: whole islands for collar and lapels. For a band across a piece (cuffs), **bisect the mesh
     with a plane** (`bmesh.ops.bisect_plane`; it interpolates weights and UVs) and colour by side: picking whole
     faces leaves a sawtooth of long triangles that flickers between two dark materials in game.
   - *Remove* buttons / pocket squares by island stats; add new small parts (buttons) by ray-casting onto the result.
4. **Fit**: `push_out` every garment vertex to at least 4-5 mm outside what is under it (body, then shirt), along the
   nearest surface normal, spreading each push to neighbours within ~2.5 cm so cloth bulges rather than dimples.
5. **Fill what the donor hid.** A donor shirt often stops where its jacket covered it: probe the new opening with
   ray casts from the front (material + depth per grid point) and keep the target's own shirt front there.
6. **Line open garments**: duplicate the front panels and skirt faces, reversed and 3 mm in, so open fronts and hems
   aren't see-through from inside.
7. **Hair**: take the scalp hair islands (not brows/beard: classify by island mean, and check the brow test doesn't
   also catch the beard), bend with `lift / forward roll / width / sideways sweep` that grow towards the front of the
   crown (`smoothstep` in height and front-back) and are zero at the back. Under it, a core: the scalp skin faces under
   the hair, offset 2 mm, bent the same way, in the hair material, so skin doesn't show between lifted cards. Style
   variants = different field values; export each as its own mesh (`Hair_<Style>`) skinned to the same armature and
   let the game enable one per character.
8. **Export** the armature + body mesh + extra meshes; re-import and compare every bone's rest matrix with the
   original and count unweighted verts (must be 0).

## Checks (renders, not guesses)

- Front, side, back, chest close-up, head 3/4 + side + back per hair style, cuff close-ups, game-camera angle.
- A posed render (legs in a long stride) for anything below the hips.
- Poke-through: ray from each skin face outward; nothing below the collar should escape the garment.
- Debug colours (bright magenta for the trim material) make cut lines and seams readable in close-ups.

## Pitfalls met

- Loading another FBX (`grab`) resets the scene: read every donor before loading the target body.
- Name extra meshes so the game's colour-by-name code matches them (here "Hair_..." takes the hair colour).
- Unity imports in worker processes: a material created in the same refresh may not be found on the first import;
  reimport the model once the material exists.
- Unity must be closed for `-batchmode` checks; if it's open, it rebuilds prefabs itself on focus.
