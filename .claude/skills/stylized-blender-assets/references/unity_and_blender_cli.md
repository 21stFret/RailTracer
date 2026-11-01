# Unity pipeline and running Blender

## Contents
1. The Unity contract
2. One-time Unity setup
3. How the user runs a script (UI, CLI, Claude Code)
4. Real-Blender check on Claude's side
5. Later: Unreal

## 1. The Unity contract
| Blender side | Unity side |
|---|---|
| Material name `Trn_Wood` | material asset named `Trn_Wood` (or `Trn_Wood_Toon`) is swapped in |
| Object `<Asset>_Structure_SOLID`, `_Props_SOLID` | visible mesh + MeshCollider, static |
| Object `<Asset>_Detail_COL` (or `_Columns_COL`) | invisible MeshCollider (renderer removed) |
| Object `<Asset>_Detail` | visible, no collider (round/fine detail; the `_COL` proxy collides instead) |
| Empties `<Asset>_Anchors/<Asset>_Anchor_##` | transforms for grapple points, sockets, spawns |
| Root empty = file name | prefab root |
FBX export settings (fixed in `export_fbx`): selection only, MESH+EMPTY, FBX_SCALE_ALL, apply unit
scale, forward -Z, up Y, bake space transform (no -90 X rotation), smoothing FACE, no leaf bones,
no animation. 1 Blender meter = 1 Unity unit at scale 1. A Blender point (x, y, z) lands in Unity at
(-x, z, -y) (a 180 deg turn, not a mirror): a model's Blender front (-Y) faces Unity +Z. Engine-side
placement tools must use this mapping (see TrainCarBuilder.cs) and offer a "flip" toggle as a safety net.

## 2. Unity helpers (installed automatically - never ask the user to copy them)
`assemble.py` embeds every `assets/unity/*.cs` (+ `--unity-tool` extras) in the kit script. On export
`install_unity_tools()` walks up from the export folder to the Unity project (folder with `Assets/` and
`ProjectSettings/`), and writes each tool: if a .cs anywhere in Assets already declares that class it is
updated in place, otherwise it goes to `Assets/KitTools/Editor/`. Identical files are left alone. It
warns about an old `AtlantisModelPostprocessor.cs` (double colliders).
- **KitBridge.cs**: build kits from inside Unity. Tools > Kit > Build Kit Script... (file picker,
  opens in `Assets/KitScripts/`), Rebuild Last Kit, Bridge Settings... A build moves/copies the script
  into `Assets/KitScripts/` (strips " (1)"), runs `blender -b --factory-startup --python <script> -- --out
  <dir>` in the background (progress bar, cancel = kill), logs output (errors -> full log to paste to
  Claude), then `AssetDatabase.Refresh()` and pings the folder. `<dir>` = the script's EXPORT_DIR if it is
  inside this project, else `Assets/KitExports/<KIT_NAME>`. Blender: EditorPrefs path, else Program
  Files/Blender Foundation/* (newest), Steam, /Applications, /usr/bin, /snap/bin, PATH; asks once.
  Optional (OFF by default): watch the Downloads folder for kit scripts made in Claude chat. It never
  auto-builds from `Assets/KitScripts/` (Claude Code builds there itself - no double builds).
- **KitSetup.cs**: on import of `<KIT_NAME>_palette.json` (fields per material: name, color (linear),
  metallic, roughness, tile, albedo/normal/mask file names when textured; plus kit + style):
  sets texture import settings (albedo sRGB, normal = Normal Map type, mask linear, repeat, mipmaps),
  creates/updates materials in `<folder>/Materials` for the active pipeline - URP Lit: `_BaseMap`
  (+ white `_BaseColor` when textured), `_BumpMap` + `_NORMALMAP`, `_MetallicGlossMap` +
  `_METALLICSPECGLOSSMAP` (smoothness = mask alpha, `_Smoothness` 1), `_OcclusionMap` (mask G) +
  `_OCCLUSIONMAP`, tiling = 1 / tile on `_BaseMap`; Built-in Standard and HDRP Lit (`_MaskMap`) equivalents.
  Kit-owned materials (in that Materials folder) re-sync on every export; a same-named material
  elsewhere (or `_Toon`) is the user's and is never touched. Then force-reimports that folder's FBX
  files. Menu: Tools > Kit > Set Up All Kits.
- **KitModelPostprocessor.cs**: material swap by name, `_SOLID` / `_COL` colliders, static flags.
Type-check C# before delivering (`apt-get install -y mono-mcs`):
`mcs -langversion:latest -target:library -out:/tmp/t.dll scripts/unity_api_stubs.cs assets/unity/*.cs <extra>.cs -r:System.Core.dll`
(extend the stubs when a script uses new Unity API).

## 3. How the user runs a script
Explain the relevant option(s) in the delivery message, with their real export path filled in.
- **Blender UI:** Scripting tab -> Open -> Run Script (or paste and Run).
- **Open Blender with the scene built:** `blender --python kit.py`
- **Headless export only:** `blender -b --python kit.py`
- **Override the folder / export a subset without editing the file:**
  `blender -b --python kit.py -- --out "C:/Proj/Assets/Models/Train" --only Seat_Double,LuggageRack_Empty`
  `--no-export` builds the scene without writing files.
- Windows path to Blender: `"C:\Program Files\Blender Foundation\Blender <ver>\blender.exe"` (or add
  that folder to PATH). macOS: `/Applications/Blender.app/Contents/MacOS/Blender`.
- **Claude Code / Cowork on the user's machine** can run these commands directly: write the script
  into Assets/KitScripts, run it headless into the Unity folder, read the console output, iterate.
- **From Unity (after the first run):** Tools > Kit > Build Kit Script... (or the optional Downloads watch).
- Export folder precedence: `--out` > `EXPORT_DIR` in CONFIG > `<KIT_NAME>_FBX` next to the saved
  .blend > `~/<KIT_NAME>_FBX` (unsaved file). The folder is created automatically.

## 4. Real-Blender check on Claude's side
When the sandbox has network access to PyPI/GitHub:
```
BPY=$(bash scripts/setup_blender.sh)          # ~1-2 min first time, cached in ~/.kit_bpy
$BPY scripts/blender_check.py out.py --render out_render.png [--layout layout.json] [--set CUTAWAY=True] [--yaw 30 --pitch 30]
```
It runs the script in actual Blender (bpy 5.x), exports to a temp folder, lists every FBX, per-asset
triangles / child objects / materials, and renders with Cycles CPU (~1 min at 32 samples) using
an auto-framed orthographic camera and the real palette colours. A traceback or missing FBX = fix it.
If setup fails (no network), fall back to `preview.py` only and say so.
The renders are plain lit Blender renders, not the game's toon shader.

## 5. Later: Unreal
FBX also imports into Unreal, but its conventions differ: centimetres (export scale), collision
from `UCX_<mesh>` objects that must each be CONVEX (so collision = many boxes, not one mesh), and
material slots matched on import. Not built yet - if the user asks, add an engine option to
`export_fbx` and per-box `UCX_` collision generation from the `MB.rect` calls.
