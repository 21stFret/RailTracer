# Project profiles

A profile records the decisions that must stay consistent across every asset in one game, so a new
request ("another prop for X") matches what exists without re-asking. Generic demos don't need one.
Create a profile when the user says an asset set belongs to an ongoing project, or after the 2nd set
for the same game. Offer the user the file so they can keep it with the skill.

## What a profile contains (keep it to one page)
- **Name / prefix / export folder**: e.g. Fathom, `Atl_`, `C:/Projects/Fathom/Assets/Atlantis/FBX`
- **Engine specifics**: object naming overrides (`Asset.DETAIL_NAME`, `Asset.ANCHOR_GROUP`), anything the
  game code expects (sockets, anchors, layers)
- **Player + gameplay numbers**: size, movement mode, door/ceiling/cover sizes, grid, story height
- **Palette**: the exact material list with colours
- **Visual language**: motifs, trims, what makes it "this game"
- **Catalog**: what already exists, sizes, which script built it
- **Project lib** (optional): `assets/projects/<name>/<name>_lib.py` with signature helpers, assembled
  with `--with`. It runs after kit_lib, so it can call `set_palette`, set `Asset.*` class attributes,
  override helpers and add `Asset` methods.

## Index
| Project | Profile | Project lib |
|---|---|---|
| Fathom (underwater Atlantis, swimming + grapple combat) | `fathom.md` | `assets/projects/fathom/fathom_lib.py` |
| RailTracer (1930s train interiors) | `railtracer.md` | `assets/projects/railtracer/` (piece + builder + layouts) |
