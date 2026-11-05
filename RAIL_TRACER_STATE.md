# Rail Tracer: current state

The single source of truth for what is actually built. Updated after every handoff and every change Alex asks for.
Last updated: Update 17 (4 Nov 2026).

Numbers are the code defaults in `Assets/RailTracer/Rules.cs` (`Tuning`) and the skill table in
`Assets/RailTracer/Resources/SkillTable.asset`. The Game component in the scene can override any tuning value in the
Inspector; the saved scene matches these.

## Handoff status

| Update | Status |
| --- | --- |
| 1–13 | Done |
| 14 (playtest fixes: sight, cover, kill split, intents, log panel, previews) | Done. Later changed: start-tile reaction fire removed (Alex), label kept as "Hit %" (Alex) |
| 15 (kill polish) | Done, not yet seen in Play mode. Kill cam, lamps, body fear, takedown library |
| 16 (behaviour and readability) | Done, not yet seen in Play mode |
| 16.11 (legend every 10, chip and zone fixes) | Done, not yet seen in Play mode |
| 16.12–16.17 (chips by sight, fixes, hearing 8/10, takedown slides, no kill cam skip, demon aura) | Done |
| 17 (demo structure, saves, menus, new cars, ragdolls, scared face) | Done, not yet seen in Play mode. Rail-grab glass waits for Alex's broken window model |
| Still needed (art) | Rail grab, demon throw and crouched drag-down animations; knife mesh (on hold); unarmed finishers (skipped for now); dining car and baggage hold props (greybox for now); broken window and glass shards; a searching animation; more idles |
| Next | Full audio pass |

## The train

"Red Line" (working title) is shown only as the start screen's title; the product name, repo, folders and namespaces stay RailTracer.

- **Demo:** five fixed cars, played in order, the same every playthrough (`Train.cs`). All 26 x 5, you start at the left door.

| Car | Name | Layout | Enemies |
| --- | --- | --- | --- |
| 1 | Third class | The hand-made carriage | 6, no heavies |
| 2 | Second class | Generated (seed 2002), more open: 60% of seat banks, 1-2 luggage stacks | 7, 1 heavy |
| 3 | Dining car | Hand-made greybox: tables by the windows and a few in the room, a 3-tile bar counter at the far end | 7, 1 heavy |
| 4 | Baggage hold | Hand-made greybox: crate maze, mail sacks, the safe's cage, a few windows | 8, 2 heavies |
| 5 | First class | Generated (seed 5005), the passenger layout | 9, 2 heavies |

- **Map tiles:** '.' floor, '_' floor with plain wall, 'S' seat, 'L' / 'l' luggage, 'W' wall, 'P' start. New: 'T' dining table, 'B' bar, 'M' mail sack (seat rules: no walking through, leap over, half cover crouched); 'X' / 'x' crate (luggage rules: blocks walking, sight and leaps); 'C' the safe's cage (blocks walking, leaps and snatches like a wall, but you can see through it). Chairs sit tucked under the tables and block nothing.
- **Greybox:** tables (cloth white, chairs tucked under), bar, sacks, two-crate stacks and the cage's beams are placeholder blocks the size of the real props; the dining car and baggage hold get bare lamps along both walls every 4 tiles.
- **Every car** starts at legend 0 with cooldowns clear and the lamps warm. You get half your health back (15) at the start of each car, up to 30.
- **Clearing a car:** the win card shows the tickets; hold Space to walk on (you walk out of the far door), or R to ride again. Then the **walk-through car**: the first carriage's seats, nobody in it, no turns, no move costs, no skills. A glowing tile by the far door asks "Board / Not yet" (Space or Enter boards).
- **After car 5:** "Demo complete": Restart from car 1, Try Endless mode, Main menu.
- **Fleeing enemies** jump off the train: broken, gone, never in the next car.
- **Endless mode:** generated cars without end (TrainGen), the same rules; car n has 5 + n enemies (up to 11) and 1 + n/2 heavies (up to 3).
- R restarts the current car from its start (the health you came in with; an endless car keeps its layout). N on a won car's card: straight out (debug).

## Saves

- One slot, `save.json` in Unity's persistent data folder, overwritten automatically: at the start of every player turn (after the enemy turn's results, so reloading never rerolls a shot), when a car or walk-through car starts, and when settings change.
- The run is saved as its car, the car's seed, your health coming in and every command since (`Carriage.Do`: moves, leaps, hops, kills, throws, grabs, crouches, undos, each enemy's turn). Continue replays them onto a fresh copy of the car, which lands exactly where you were at the start of that turn: enemies (position, facing, awareness, terror, panic rolls, has-seen), bodies, blood, your stance, health, legend, cooldowns, held body, the turn number and the log. Checked in RulesCheck against 40 random games.
- Also saved: which demo cars are cleared, the best ticket per car (most killed plus broken, then fewest turns) and the settings.

## Menus

Every menu is over the real carriages: the live (saved) car plus the other demo cars built beside it, empty and without props. Menu train lamps: red for a cleared car, white for the next to play, dark for cars ahead.

- **Start:** "RED LINE" ("Red" #C8102E, "Line" bone) and the menu down the left over a shaded strip, the saved game behind it, loaded and paused. With no save, an empty car 1 and "Board the train". Lines: Continue (car and turn) or Board the train, Journey, Endless, Options, Quit.
- **Camera:** each screen is a zoomed-out view of a real car (Start over your car, Options over the next one along); it glides sideways between them. Journey pulls out side-on to the whole train. Continue (or boarding) dives straight down into the car and play resumes, no loading screen.
- **Journey:** the five cars side-on, names under them (red cleared, white next, grey ahead) with short links between; click one for its name, description and best ticket, and Replay (cleared), Continue or Board (next). Cars ahead can't be played. Replaying a car carries on through the rest of the train.
- **Options:** Kill cam On/Off, kill cam slow motion (1, 0.75, 0.5, 0.25x), window mode (Borderless, Fullscreen, Windowed), resolution, Controls (view only). No audio yet.
- **Pause (Esc):** Resume, Tutorial tips, Kill cam, Restart car, Main menu. Esc backs out of every submenu.

## Turn

- 4 moves and 1 action per turn. Crouched or dragging a body costs 2 moves a tile.
- At the start of your turn, anyone not startled who can see you (a searcher that turned your way) is in combat.
- End the turn by holding Space (0.5 s). Undo (Z) until you act, are seen or move a body.
- Your legend (0–100) grows from kills only: stealth kill +10, rail grab +25, brutal kill +30. It is also a shield: hits drain it before health.
- Thresholds every 10, notched on the bar at 10–90: brutal unlock 10, hop Bloodied 20, rail unlock 30, stealth Bloodied 40, brutal Bloodied 50, rail Bloodied 60, leap Bloodied 70, demon throw 80, dread 90. Above 90 is overspill: still capped at 100 and still a shield, so it protects dread.
- Health 30. A hit does 15.

## Skills

| Key | Skill | Cost | Cooldown | Range | Unlock | Bloodied |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Grab and drag | Free | none | A body on your tile or next to you (not one hung out of a window) | 0 | none |
| 2 | Leap | Needs all 4 moves unspent; uses them all | 3 | 3 tiles, line of sight, clear flight over seats (not luggage or walls) | 0 | 70: double range (6); the stylish flip plays past 3 |
| 3 | Window hop | All moves, once a turn | none | The next booth's window, same side | 0 | 20: costs no moves |
| 4 | Stealth kill | Action | none | The 4 tiles straight next to you | 0 | 40: diagonals too, plus the snatch (2 tiles straight, not past luggage or people) |
| 5 | Brutal kill | Action; uses all moves | 2 (blocked next turn; the box shows 1) | The 8 tiles round you | 10 | 50: refreshes your leap |
| 6 | Rail grab | Action | 2 | The 8 tiles round you; you or they at a window | 30 | 60: bigger terror (×1.5) and medium instead of small for those who only hear it |
| 7 | Demon throw | Action, body held | 3 | 5 tiles, line of sight | 80 | 80 |
| 8 | Dread | Passive | none | none | 90 | Anyone who sees you is at least Shaken |

Kill details:

- **Stealth kill:** only when the victim can't see you. The victim is pulled onto your tile and dies there; you never move and keep your stance. Heard within 1 tile. Witnesses who see your tile: medium terror (25), combat. Who see only where the victim stood: they vanish (small terror 10; a snatch 35) and they search there.
- **Brutal kill:** a standing act. You move onto the victim's tile; crouched before it, you drop back to crouching after. Heard within 8. Witnesses take large terror (45) and go into combat, except anyone who has never seen you, who is startled. No free shots.
- **Rail grab:** the victim hangs out of their window (or yours, if they're not at one) with one leg inside. Blood on every tile within 1 of the window. Heard within 10 (louder than a brutal kill). Witnesses: large terror (45, or 67 bloodied).
- **Demon throw:** large terror (45) for everyone who sees it land; they investigate the body.
- Toughs (heavies) can't be killed until Shaken.

## Enemies: awareness (the chip colour)

| State | Chip | Entered | Left |
| --- | --- | --- | --- |
| Unaware | Black, white outline (ring icon) | Start; a search running out | Any sighting, noise, blood, body, shout or gunfire |
| Searching | White, "?" | A noise, blood, a body, a shout or gunfire; a fighter losing sight of you at its turn | Seeing you (combat); after the 3-turn look-around ("gave up the search") |
| Startled | White, scared face (two wide eyes, open mouth) | Never saw you, then sees a leap landing or a brutal kill | Its next turn: loses it, then searches where it saw you |
| Combat | Red, only while it can see you where you stand, or where the aimed move ends while you preview one (redrawn every frame, so every step) | The moment it sees you (checked on every tile you move through), unless startled | Losing sight of you, at its own turn: it drops to Searching where it last saw you. Its chip turns white "?" the moment you are out of its sight |
| Broken | White flag | Terror 100 | Never (out of play, killable) |

Chip icons (what they'll do if you end your turn where you are, or where the aimed move ends). Crosshair and charge show only on red (combat) chips and berserk heavies; a searcher keeps its "?":

- **Crosshair:** a guard that sees you and will shoot (not one hesitating, startled or on a panic turn).
- **Charge:** a heavy that sees you, or any berserk heavy. Heavies don't shoot: they charge 3 tiles a turn and melee (90%) when next to you.
- **Wild fire:** a panicked guard on a wild fire turn.
- **Scared face on a freeze turn:** a panicked guard freezing (loses the turn, like startled).
- **Armour plate** on toughs: whole while calm (can't be killed), cracked once Shaken. Targeting a kill on an unshaken tough shows "Not yet" over it.
- Terror segments: three (Shaken, Panicked, Broken), white outline on red and black chips, black on white. The eye: has seen you (it can't be startled).

Hover label: name, role, one state line ("Searching · Shaken", "Panicked · Freeze", "Berserk", "Fled", "Begging"), and what they'll do next ("Hit 85%", "Charge", "Investigate: Noise / Blood / Body / Movement", "Hold", "Patrol", "Back to post", "Loses the turn").

### Behaviour

- **Unaware:** guards hold their post and turn every 2 turns (an arrow shows the turn before); patrols walk their route; after a search, guards walk back to their post.
- **An unaware enemy who spots you mid-move** turns red but doesn't use reaction fire that turn. **At the start of its turn, if it still sees you, it attacks.**
- **Searching** does one of three things:
  1. **Go to the spot** (a noise, a body, where it saw you).
  2. **Follow a blood trail** to the newest part it can see right now (the "?" marker moves with it).
  - A "?" on the floor marks where a searcher will go and look; none for looking around in place (shout or gunfire) and none on the last-seen ghost.
  3. **Look around** for 3 turns: at the spot (moving within 2 tiles of it), or where it stands if it heard a shout or gunfire. A floor arrow shows the tile it will stand on and the way it will face next turn.
  Then it gives up and walks back.
- **Shouting:** the first enemy to see you shouts and comes looking itself (it's in combat). Unaware listeners within 6 tiles turn white, face the shout and look around where they stand.
- **Gunfire:** everyone not in combat turns to face it and looks around where they stand (the carriage doesn't pile in).
- **Bodies** stop whoever finds one for that turn. Terror by how they died: stealth 10, brutal or shot 25, rail grab or thrown 45. Log: "Name found Victim's body (brutal)."
- **Blood:** seen like a crouched player on that tile; each enemy reacts once per stain.

### Terror

- Shaken (30+): hit chance 35%; 40% chance each turn to hesitate instead of shooting.
- Panicked (60+), guards: every other turn is a panic turn, starting with the next turn after they panic. On a panic turn they freeze (50%, tunable; lose the turn, no reaction fire that player turn) or fire wildly at a random target in front of them (30% to hit, ally or you). The roll is made at the start of your turn and shown on the chip. Other turns: normal, with the Shaken penalties. No fleeing or charging.
- Panicked (60+), heavies: berserk for good. They charge your real tile every turn (3 tiles), melee when next to you, and never drop to searching.
- Broken (100): one roll: flee to the next carriage (50%, tunable; needs a path to a door, else they beg) or kneel and beg. Both count as broken; killing a broken enemy turns its ticket red. If the level is won while someone is still fleeing, they run out before the end card.
- Terror values: small 10, medium 25, large 45, unseen (a body dragged or someone snatched away by something unseen) 35. A leap landing seen: legend × 40 / 100.

## Sight

- Range: guards 7, heavies 8. Never changes with awareness.
- Guards: a 90° wedge in front (full range), plus two tiles of side vision one row ahead.
- Heavies: the same, plus the first 2 tiles straight out to each side.
- Each standing enemy on the line halves the sight left beyond them (broken ones and bodies don't count).
- Luggage and walls block sight. Seats block it only for something low (you crouched, a body, blood).
- **Half cover (crouched):** a seat on the line to the viewer hides you if it's right next to you (viewer 3 tiles away or less) or within 2 tiles of you (viewer further). Worked out per viewer. Bodies and blood use the same rule.
- **Close look:** a searcher spots you on any tile next to it inside its cone, cover or not (a guard's 3 in front, a heavy's 5).

## Reaction fire

- From searching and combat enemies with their shot unspent this turn (not on a panicked guard's freeze turn).
- Triggers on stepping onto a tile they see you on (not the tile you start on), every tile of a leap's arc and its landing, a hop's landing tile, and standing up. Once per enemy per turn; a shot ends a walk there.
- Shots at a leaping player are 25 points lower (85% becomes 60%).
- Shown on the floor as a faint red tint on every watched tile (Game "Zone Tint", 0.12) and one red dashed line round the outer edge only: seats and the tiles they hide, up to 2 tiles between watched ones in a row or column, count as inside the line but stay untinted. The move preview marks the tile in red and highlights the shooter with its hit chance.

## Hits

- Hit 85% (Shaken 35%), half cover ×0.75, melee 90%, wild fire 30%.

## Animation test

`Assets/RailTracer/AnimationTest.unity`: no train, just an attacker and a victim on a bare floor at full size, replaying one takedown from `Resources/Takedowns` on a loop until you switch, spaced and turned by each entry's Victim Offset / Victim Yaw (edits show on the next play). The game's kill blood fires at each entry's Blood At (of the last clip, for a combo), from the victim's chest, and "BLOOD" flashes on screen; stains clear each loop. Left / right arrows: previous / next; R: replay; Space: pause; right-drag orbits, scroll zooms.

In the game too, finisher blood now comes from the victim's chest bone (where the clip has carried them), not above their tile.

## Bodies

- Ragdolls: the character has ragdoll bones from Unity's Ragdoll Builder (Editor/RagdollSetup, kinematic and collider-off until needed, on Ignore Raycast). After the last clip of every takedown the victim goes limp and falls (during the kill cam's fly-out); other deaths too, except rail grabs (posed over the sill). After Game "Ragdoll Settle" (1.6 s) the body freezes where it fell and slides so its hips sit over its tile; drags and throws move the frozen pose. The takedown viewer ragdolls its victim the same way.
- You too: when the finisher blends back to idle your hips stay where the clip left them, then you slide onto your tile over the same hold.

## Blood

- A kill (and a thrown body's landing) bloodies tiles by the rules: stealth 1, brutal 5 (the tile and its open neighbours), rail grab every open tile within 1 of the window.
- The splash effect comes from the victim's chest (the big gore one for a brutal kill, towards the glass for a rail grab).
- The stains that stay: from each bloodied tile, five straight casts from 1.8 m up (1 m for a crouched stealth kill; a thrown body 1 m), down and out along the grid up to 1.5 m (Game: Blood Cast Standing / Crouched / Reach). A floor or wall gets a stain square-on; a seat gets one at 45 degrees into the cushion fold (down and towards its back); window glass gets one stuck to the sash. The kill tile's floor gets a pool for a brutal kill or a thrown body, spatter otherwise; walls get smears. Drag trails keep a floor stain per tile.

## Camera, HUD and presentation

- Kill cam on brutal kills (as long as the finisher), rail grabs and throws (1.2 s): the camera flies in (0.45 s), holds a beat (0.2 s), plays the shot from a fixed angle, then flies back to your exact view (0.35 s). The angle is low and side-on, trying other angles until no wall, floor, luggage or seat back is in the way (or a takedown's own Cam Offset). Slow motion: Kill Cam Slow Mo (brutal and rail, default 1 = none), Throw Cam Slow Mo (0.5). It can't be skipped; Options and the pause menu: "Kill cam: On/Off" (default On), and its slow motion in Options. The HUD hides during it.
- Demon aura (Resources/Effects/Demon Aura) round your feet: fades in with the same eased legend as the blood on your clothes, from nothing at 10 legend to the prefab's full colours at 100 (Game "Aura From" 0.1, "Aura Full" 1).
- The carriage lamps redden with legend, stepping at 25, 50, 75 and 100 and blending over about a second. Overlays never change.
- Finishers: Brutal Finishers knife clips (Humanoid on the mannequin avatar) in `Resources/Takedowns`, Alex's picks:
  - Stealth, crouched (random): Heart Stab, plus the Stealth Finishers pack: Grab Flip Head Stomp, Karate Chop KO, Neck Break, Grab Flip Stab, Neck Stab (from behind), Spin Stab, Thigh and Neck Stab (start values: 1 m apart, victim facing away; still to tune). Stealth, standing: Shove Neck Slice (quick).
  - Brutal, at random: Duck and Back Stab, or the Slash Combo (mid-section slash, then face slash cross-faded in on the "Finisher2" states: Combo From / Combo Start / Combo Blend on the entry; the victim topples after).
  - Brutal with half cover (a seat) straight out from the victim's tile: always Shove to Table, the pair turned to face that seat.
  - The clips keep their own travel in the pose (imported with root XZ baked from the original position), so a pair lines up through the whole clip once its start offset is right. The exported clips start with both characters on the same spot; each pair has its own starting distance (as set up in Unreal), facing you: Victim Offset Z 1.5 (Shove Neck Slice 1.3), Victim Yaw 180.
  - Each entry can be tagged Guard / Heavy / Any (all Any for now).
- Ghosts: the move-preview ghost and the last-seen ghost are see-through outlines (last-seen fainter). The last-seen ghost marks only where an enemy actually last saw you.
- Keys: 1–8 skills, Shift leap, Ctrl hop, G grab, C crouch, Z undo, V all senses, B blood outlines, L log, H controls, R restart the car, Esc pause (Main menu is in there).
