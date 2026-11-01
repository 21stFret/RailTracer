# Project profile: RailTracer (1930s train)

- **Unity export folder:** `C:\Users\owner\RailTracer\Assets\RailTracer\Train Assets`
  (Unity path `Assets/RailTracer/Train Assets`). Put it in `EXPORT_DIR` of every RailTracer script.
- **Workflow:** Claude Code in the project (agent mode): scripts in `Assets/KitScripts/`, Claude runs
  Blender itself. The user wants everything inside the Unity project (no Downloads folder).
- **Engine:** Unity. Helpers are embedded in the kit script and auto-installed (the user had hand-copied
  KitModelPostprocessor/KitSetup/TrainCarBuilder into an Editor folder; they get updated in place).
  Assemble with `--unity-tool assets/projects/railtracer/TrainCarBuilder.cs`.
- **Look:** generic 1930s Pullman-era luxury car (inspired by period steam-train interiors): warm wood
  paneling, dark pilasters, brass rails/plates/lamps, cream ceiling + frieze, burgundy velvet,
  dark painted-steel exterior with brass pinstripes, curved roof with vents.
- **Materials:** semi-realistic textures (`TEXTURE_STYLE = "pbr"`), modest detail. Side walls: wood
  veneer below the dado rail (split at z 0.74, hidden by the rail), WALLPAPER above: gold-on-black
  stripes (paper (0.015,0.015,0.02), ink (0.62,0.42,0.12), 5 repeats/m) - set in CONFIG `WALLPAPER`.
  End walls and partitions stay wood. Recipes: Trn_Wood wood veneer (tile 2, planks 1, rings 14,
  seams False), Trn_WoodDark wood planks 2, Trn_Brass brushed (0.5), Trn_Cream plaster (2),
  Trn_Velvet velvet (0.5), Trn_Leather leather (0.5), Trn_Iron painted_metal (2), Trn_Wallpaper wallpaper (1), Trn_Glass untextured (opaque dark glossy).
- **Palette:** `Trn_Wood (0.45,0.24,0.12)`, `Trn_WoodDark (0.20,0.10,0.05)`, `Trn_Brass (0.95,0.70,0.25) metal`,
  `Trn_Cream (0.90,0.84,0.68)`, `Trn_Velvet (0.45,0.07,0.10)`, `Trn_Leather (0.40,0.22,0.10)`,
  `Trn_Iron (0.18,0.20,0.22) semi-metal`, `Trn_Wallpaper` (paper colour from WALLPAPER).
- **Player:** walking, ~1.8 m. Realistic train sizes chosen by the user (1 m aisle).

## Grid (shared by every shell module) - `assets/projects/railtracer/railtracer_train_kit_piece.py`
Car along Blender Y (Unity Z), 1 m grid. Floor surface z = 0. Interior 5.0 m wide (inner faces x = +-2.5),
3.0 m to the ceiling, walls 0.15 (0.10 wood + 0.05 steel), wall/ceiling top 3.15, roof crown 3.82,
underframe to -0.6. Window sill 0.8, window top 2.25, doors 2.3 tall. Pivot: centreline, floor level,
centre of the piece along the car. Wall modules are +x side; rotate 180 for the other side.
Trim depths: baseboard 0.03 / pilaster 0.04 / dado 0.045 / picture rail 0.05 / jamb 0.06 / head 0.07 /
sill 0.08 / cornice 0.12.

## Catalog
| Asset | Size | Notes |
|---|---|---|
| Wall_1m_Solid / _Pilaster / _WindowSmall | 1 m | small window 0.7 x 1.45 |
| Wall_2m_Window / Wall_2m_Door | 2 m | window 1.6 x 1.45; door 1.1 x 2.3, brass tread, grab rails, step |
| Window_Sash_Small / _Large | 0.7 / 1.6 m | drop-light sash (Trn_Glass night glass, opaque for decals); wall pivot, slides down <= 1.0 m |
| Floor_1m / Floor_1m_Plain | 1 m | velvet runner x +-0.5 / none; underframe |
| Roof_1m / Roof_1m_Rib / Roof_2m_Lamp | 1 / 1 / 2 m | ceiling + roof; rib at centre; lamp + vent |
| EndCap_Door / EndCap_Solid | 0.5 m deep | end wall faces +Y; 1.2 x 2.3 end door |
| Partition_Door | 0.1 m (0.3 with trims) | 1.0 x 2.3 doorway, place on a joint |
| Seat_Double | 2 x 1 x 1 m | back at -Y; in car at x = +-1.45 |
| LuggageRack_Full_A/B/C, _Empty | 1 x 2 x 1 m | open front +Y; seeds 11/23/37 |
| Lamp_Wall | 0.216 x 0.348 x 0.30 | sconce; pivot = mount (back face), out along -Y; on pilasters |
Layout tokens (TrainCarBuilder + `layouts.py` twin): S P w = 1 m, W D = 2 m, | = partition.
Car length = wall length + 1 m. Examples: coach `S W P W P W P W S` (14 m), doors
`S D P W P W P D S`, compartments `S w W w | w W w | w W w`.
