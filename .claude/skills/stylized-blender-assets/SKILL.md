---
name: stylized-blender-assets
description: Build stylized, modular, game-ready 3D assets for ANY game as Blender Python scripts that export Unity-ready FBX files (materials and colliders matched by name), verified with a preview render, a z-fighting check and, when possible, a run in real Blender. Use this skill whenever the user asks for 3D models, props, furniture, rooms, vehicles, interiors, buildings, modular kit pieces, environment assets or variations of them for a game or demo, mentions Blender scripts, bpy or FBX, or wants something "in the same style" as earlier assets - even if they don't say "Blender". Also covers project profiles (e.g. the Fathom / Atlantis underwater game) for consistent ongoing sets.
---

# Stylized Blender Assets

Make chunky, clean, toon-friendly game assets from code. Claude writes ONE self-contained Blender
script per asset set; running it builds every mesh and exports one FBX per asset that Unity imports
with materials and colliders already hooked up. The look is generic on purpose: the style guide
carries the quality bar, the palette and motifs carry the setting (1930s train, fantasy market,
sci-fi corridor, Atlantean ruins...).

## The user experience to aim for
Prompt -> a few questions (incl. the export folder) -> assets appear in the Unity folder, with
materials and colliders set up. Everything lives inside the Unity project: kit scripts in
`Assets/KitScripts/`, assets in the export folder, helpers in an Editor folder. Never ask the user to
copy helper scripts by hand.
- **Agent mode** (Claude Code / Cowork on the user's machine - the preferred flow):
  1. Assemble the kit script and save it as `<Project>/Assets/KitScripts/<kit>.py`.
  2. Run the numpy checks (`preview.py`, `joint_check.py`; `pip install numpy pillow` if missing) and
     view the preview PNG. Don't install bpy - the user's own Blender is the real check.
  3. Find Blender (Windows: newest `C:\Program Files\Blender Foundation\Blender *\blender.exe`, else
     Steam `...\steamapps\common\Blender\blender.exe`, macOS `/Applications/Blender.app/Contents/MacOS/Blender`,
     else PATH) and run `blender -b --factory-startup --python "<script>" -- --out "<export folder>"`.
     Pass Windows paths to blender.exe (also from WSL: `/mnt/c/...` -> `C:\...`).
  4. Read the output: `Exported N FBX`, `Unity tools: ...`, no `Traceback`. Fix and re-run on errors.
     Unity picks everything up when it regains focus; KitSetup makes the materials.
  Zero user steps.
- **Chat mode** (claude.ai: Claude cannot write to the user's disk): deliver ONE file, the kit script.
  The first run (`blender -b --python <kit>.py`) installs the Unity helpers; afterwards the user can
  build kit scripts from Unity with Tools > Kit > Build Kit Script... (or switch on "Watch Downloads
  folder" in Tools > Kit > Bridge Settings so a downloaded kit script offers itself).

## Workflow

### 1. Start of a new asset set: settle the essentials
Ask only what's missing (one short round, tappable options when available; make sensible calls for
everything else and state them):
- **Where should the FBX files go?** Ask at the start of every NEW set unless the conversation or a
  project profile already says. It must be a folder inside the Unity project's `Assets/` (that is how
  the script finds the project to install the helpers, and how KitBridge accepts it). It goes into
  `EXPORT_DIR` in the CONFIG block. Save it in the project profile so it is never asked twice.
- **One-off demo or part of a project?** If a project, check `references/projects/README.md` for a
  profile and read it (Fathom -> `references/projects/fathom.md`). Profiles fix palette, grid, naming.
- **Style / setting and exact sizes** if not given, and the player's size/movement if it changes
  dimensions (walking vs swimming vs top-down).
For follow-up requests in the same set, don't re-ask.

### 2. Read before writing
- `references/style_guide.md` - shapes, trim layering, palette, budgets, grids, gameplay sizes (read
  once per conversation).
- `references/pitfalls.md` - before anything with trims, shelves of items, stacked modules or ruins.
- A similar example: `assets/examples/train_interior.py` (room shell with windows/doors + furniture +
  seeded packed shelves), `assets/projects/railtracer/railtracer_train_kit_piece.py` (fully MODULAR
  vehicle kit: walls/floors/roofs/end caps on a 1 m grid + `TrainCarBuilder.cs` layout-string tool),
  `assets/piece_template.py` (minimal), other project examples under `assets/projects/<name>/`.

### 3. Write the piece script
Copy `assets/piece_template.py`. The library `scripts/kit_lib.py` gives:
`MB` mesh builder (`rect`, `box`, `extrude`, `lathe`, `cylinder`, `sphere`, `annulus`, `merge`),
`Asset` (buckets `S` structure+collider, `P` props+collider, `D` render-only detail with `COL` proxy,
`anchors`, `rng`, `state()`, `finish()`), `set_palette`, `wall` (openings), `frame_opening`, `band`,
`block_walls`, `slab_with_hole`, `arc_wall`, `rock`, `runs`, `place`, `place_lying`, `sx` (mirror an
interval), `MB.merge_mirror_x` (mirror geometry with correct normals), `run(builders)`. Read the lib once for exact signatures - it is short and commented.
- CONFIG block at the top (`# >>> CONFIG` ... `# <<< CONFIG`): `EXPORT_DIR`, `KIT_NAME`, seeds,
  user-tweakable sizes. Preview-only toggles use `NAME = globals().get("NAME", default)`.
- `set_palette([...])` with a kit prefix (`Trn_`, `Mkt_`...) or the project palette. For textures,
  give each entry a 5th element `{"tex": recipe, "tile": metres, ...}` and set `TEXTURE_STYLE` in
  CONFIG ("flat" | "toon" | "painted" | "pbr" = semi-realistic). See "Textures" below.
- One builder per exported asset, returning `asset.finish()`; end with `run([...])`.
- Docstring at the top: each asset, exact size, pivot/facing, how it fits with the others.

### 4. Assemble into one file
`python scripts/assemble.py piece.py -o /mnt/user-data/outputs/<kit>.py [--with assets/projects/<p>/<p>_lib.py] [--unity-tool assets/projects/<p>/<Tool>.cs]`
The core Unity helpers in `assets/unity/` are always embedded; the script installs them on export
(updating any existing file that declares the same class in place - never duplicates).

### 5. Check (always, iterate until clean)
1. `python scripts/preview.py <kit>.py -o <kit>_preview.png [--layout layout.json] [--set CUTAWAY=True] [--yaw --pitch]`
   prints tris + **bounds per asset** and the **z-fighting report**: it must end `ALL CLEAN`. Compare
   bounds with the requested sizes (exact). View the PNG: floating parts, clipping, blocked doorways.
   Use `--layout` (`[[name, x, y, z, rot], ...]`) to show pieces combined, e.g. furniture inside a room.
2. **Modular kits** (pieces that snap together): build 2-3 realistic layouts as layout JSON (a small
   Python helper that applies the same placement rules the engine-side builder will use) and run
   `python scripts/joint_check.py <kit>.py --layout layout.json` - it merges the placed pieces and
   checks z-fighting ACROSS joints. Must end `ALL CLEAN`. Also catches furniture placed through walls.
3. Real Blender, when the sandbox has network: `BPY=$(bash scripts/setup_blender.sh)` then
   `$BPY scripts/blender_check.py <kit>.py --render <kit>_render.png [same --layout/--set]`.
   Confirms it runs in actual Blender, every FBX exports, and lists children/materials/bounds; the
   render is a real Blender image to show the user. If setup fails, rely on step 1 and say so.
Details: `references/unity_and_blender_cli.md`.

### 6. Deliver
Agent mode: script in `Assets/KitScripts/`, Blender run, FBX count confirmed in the folder. Chat mode:
present the kit script (+ preview/render images). Then a short summary:
- assets with exact sizes, pivots/facing and how they combine (grid, placements, layout tokens);
- what's parameterised (seeds, sizes, toggles in CONFIG) and tri counts for heavier pieces;
- agent mode: what was exported where (nothing for the user to do); chat mode: the next step in ONE
  line (first kit in the project: `blender -b --python <kit>.py`; later: Tools > Kit > Build Kit Script...);
- materials are created automatically (KitSetup) - list the names in case they want their own shaders;
- previews are real geometry but not the game's toon shader.
For modular kits also ship an editor layout tool (like `TrainCarBuilder.cs`, embedded with
`--unity-tool`) and type-check all C# with `mcs` against `scripts/unity_api_stubs.cs`.

## Textures
`scripts/kit_textures.py` (numpy, runs inside Blender) generates SEAMLESS texture sets per palette
material at export: `<out>/Textures/<Mat>_albedo.png` (sRGB), `_normal.png` (OpenGL/Unity),
`_mask.png` (R metallic, G occlusion, A smoothness). KitSetup wires them into URP/HDRP/Built-in
materials automatically. Every mesh gets world-scale box UVs (1 UV = 1 m), so textures tile identically
on every module and line up across joints; `tile` = metres per repeat (keep it a divisor/multiple of
the module grid).
- Recipes: `wood` (planks, rings, seams=False -> veneer), `velvet`/`fabric`, `brushed`/`brass`,
  `painted_metal`, `plaster`/`paint`, `leather`, `marble`, `stone`, `plain`, and `wallpaper`
  (`pattern` = stripes | damask | fans | trellis | chevron | floral, palette colour = paper,
  `ink` = print colour, `repeats` per tile).
- Styles: toon (soft gradients, light grain), painted (brush strokes, warm lights / cool shadows),
  pbr (semi-realistic fine detail + strong normals). One CONFIG switch; geometry is unchanged.
- To let the user choose: render the same furnished corner per style/colourway with
  `blender_check.py --set TEXTURE_STYLE=... --set 'WALLPAPER={...}'` (one render per command - each
  Cycles render is ~1 min) and/or a numpy swatch sheet from `make_texture_set`.
- Two materials on one wall: split the skin at a trim line (e.g. wood below the dado rail, wallpaper
  above) so the trim hides the join.
- Optional, agent mode with internet + user's OK: CC0 textures (Poly Haven, ambientCG) can replace a
  generated set - same file names in `Textures/`, same palette entry.

## Conventions (the Unity importer depends on them)
- Meters, Z up in Blender, pivot bottom-centre, walkable floors at z = 0 where props drop in.
- Material NAME = Unity material name. Only used materials are attached per mesh. Export also writes
  `<KIT_NAME>_palette.json` (consumed by KitSetup.cs).
- Object suffixes: `*_SOLID` visible + MeshCollider, `*_COL` invisible collider; `Asset.finish()` and
  `run()` produce `<Asset>_Structure_SOLID`, `_Props_SOLID`, `_Detail` + `_Detail_COL`, `_Anchors`.
- Export only via `run()` / `export_fbx()` (settings tuned for Unity, see the reference).
- Everything random is seeded; same script = same assets.

## Files
- `scripts/kit_lib.py` - generic library (pasted into every generated script)
- `scripts/kit_textures.py` - seamless texture recipes + styles (pasted after the lib)
- `scripts/assemble.py` - CONFIG + lib (+ project libs) + piece -> one script
- `scripts/preview.py` (+ `mathutils_shim.py`, `zfight.py`) - numpy render, bounds, z-fight check
- `scripts/joint_check.py` - z-fight check across modules placed by a layout
- `scripts/setup_blender.sh`, `scripts/blender_check.py` - real-Blender run, export check, Cycles render
- `assets/piece_template.py`, `assets/examples/train_interior.py`
- `assets/unity/` - Unity editor helpers embedded in every kit script: `KitBridge.cs` (build a kit
  script from Unity: Tools > Kit; optional Downloads watch), `KitSetup.cs` (materials + textures from the
  palette JSON, re-synced on every export + reimport), `KitModelPostprocessor.cs` (materials by name, `_SOLID`/`_COL` colliders)
- `assets/install_unity_fbx_preset.py`; `scripts/unity_api_stubs.cs` - type-check C# with mcs
- `assets/projects/fathom/` - Fathom project lib + original example scripts
- `references/style_guide.md`, `references/pitfalls.md`, `references/unity_and_blender_cli.md`,
  `references/projects/` (README + profiles)
