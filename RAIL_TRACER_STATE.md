# Rail Tracer: current state

The single source of truth for what is actually built. Updated after every handoff and every change Alex asks for.
Last updated: Update 16 (1 Nov 2026).

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
| Still needed (art) | Rail grab, demon throw and crouched drag-down animations; knife mesh (on hold); unarmed finishers (skipped for now) |

## Turn

- 4 moves and 1 action per turn. Crouched or dragging a body costs 2 moves a tile.
- End the turn by holding Space (0.5 s). Undo (Z) until you act, are seen or move a body.
- Your legend (0–100) grows from kills only: stealth kill +10, rail grab +25, brutal kill +30. It is also a shield: hits drain it before health.
- Health 30. A hit does 15.

## Skills

| Key | Skill | Cost | Cooldown | Range | Unlock | Bloodied |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Grab and drag | Free | none | A body on your tile or next to you (not one hung out of a window) | 0 | none |
| 2 | Leap | Needs all 4 moves unspent; uses them all | 3 | 3 tiles, line of sight, clear flight over seats (not luggage or walls) | 0 | 75: double range (6); the stylish flip plays past 3 |
| 3 | Window hop | All moves, once a turn | none | The next booth's window, same side | 0 | 25: costs no moves |
| 4 | Stealth kill | Action | none | The 4 tiles straight next to you | 0 | 35: diagonals too, plus the snatch (2 tiles straight, not past luggage or people) |
| 5 | Brutal kill | Action; uses all moves | 2 (blocked next turn; the box shows 1) | The 8 tiles round you | 10 | 50: refreshes your leap |
| 6 | Rail grab | Action | 2 | The 8 tiles round you; you or they at a window | 25 | 60: bigger terror (×1.5) and medium instead of small for those who only hear it |
| 7 | Demon throw | Action, body held | 3 | 5 tiles, line of sight | 85 | 85 |
| 8 | Dread | Passive | none | none | 100 | Anyone who sees you is at least Shaken |

Kill details:

- **Stealth kill:** only when the victim can't see you. The victim is pulled onto your tile and dies there; you never move and keep your stance. Heard within 1 tile. Witnesses who see your tile: medium terror (25), combat. Who see only where the victim stood: they vanish (small terror 10; a snatch 35) and they search there.
- **Brutal kill:** a standing act. You move onto the victim's tile; crouched before it, you drop back to crouching after. The whole carriage hears it. Witnesses take large terror (45) and go into combat, except anyone who has never seen you, who is startled. No free shots.
- **Rail grab:** the victim hangs out of their window (or yours, if they're not at one) with one leg inside. Blood on every tile within 1 of the window. Heard within 8. Witnesses: large terror (45, or 67 bloodied).
- **Demon throw:** large terror (45) for everyone who sees it land; they investigate the body.
- Toughs (heavies) can't be killed until Shaken.

## Enemies: awareness (the chip colour)

| State | Chip | Entered | Left |
| --- | --- | --- | --- |
| Unaware | Black, white outline (ring icon) | Start; a search running out | Any sighting, noise, blood, body, shout or gunfire |
| Searching | White, "?" | A noise, blood, a body, a shout or gunfire; a fighter losing sight of you at its turn | Seeing you (combat); after the 3-turn look-around ("gave up the search") |
| Startled | White, "!" | Never saw you, then sees a leap landing or a brutal kill | Its next turn: loses it, then searches where it saw you |
| Combat | Red | The moment it sees you (checked on every tile you move through), unless startled | Losing sight of you, at its own turn: it drops to Searching where it last saw you |
| Broken | White flag | Terror 100 | Never (out of play, killable) |

Chip icons (what they'll do if you end your turn where you are, or where the aimed move ends):

- **Crosshair:** a guard that sees you and will shoot (not one hesitating, startled or on a panic turn).
- **Charge:** a heavy that sees you, or any berserk heavy. Heavies don't shoot: they charge 3 tiles a turn and melee (90%) when next to you.
- **Wild fire:** a panicked guard on a wild fire turn.
- **Armour plate** on toughs: whole while calm (can't be killed), cracked once Shaken. Targeting a kill on an unshaken tough shows "Not yet" over it.
- Terror segments: three (Shaken, Panicked, Broken), white outline on red and black chips, black on white. The eye: has seen you (it can't be startled).

Hover label: name, role, one state line ("Searching · Shaken", "Panicked · Freeze", "Berserk", "Fled", "Begging"), and what they'll do next ("Hit 85%", "Charge", "Investigate: Noise / Blood / Body / Movement", "Hold", "Patrol", "Back to post", "Loses the turn").

### Behaviour

- **Unaware:** guards hold their post and turn every 2 turns (an arrow shows the turn before); patrols walk their route; after a search, guards walk back to their post.
- **An unaware enemy who spots you mid-move** turns red but doesn't use reaction fire that turn. **At the start of its turn, if it still sees you, it attacks.**
- **Searching** does one of three things:
  1. **Go to the spot** (a noise, a body, where it saw you).
  2. **Follow a blood trail** to the newest part it can see right now (the "?" marker moves with it).
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
- Shown on the floor as one red dashed line round the whole watched area. The move preview marks the tile in red and highlights the shooter with its hit chance.

## Hits

- Hit 85% (Shaken 35%), half cover ×0.75, melee 90%, wild fire 30%.

## Animation test

`Assets/RailTracer/AnimationTest.unity`: no train, just an attacker and a victim on a bare floor at full size, replaying one takedown from `Resources/Takedowns` on a loop until you switch, spaced and turned by each entry's Victim Offset / Victim Yaw (edits show on the next play). Left / right arrows: previous / next; R: replay; Space: pause; right-drag orbits, scroll zooms.

## Camera, HUD and presentation

- Kill cam on brutal kills (as long as the finisher), rail grabs and throws (1.2 s): the camera flies in (0.45 s), holds a beat (0.2 s), plays the shot from a fixed angle, then flies back to your exact view (0.35 s). The angle is low and side-on, trying other angles until no wall, floor, luggage or seat back is in the way (or a takedown's own Cam Offset). Slow motion: Kill Cam Slow Mo (brutal and rail, default 1 = none), Throw Cam Slow Mo (0.5). Any input skips it; Pause menu "Kill cam: On/Off" (default On). The HUD hides during it.
- The carriage lamps redden with legend, stepping at 25, 50, 75 and 100 and blending over about a second. Overlays never change.
- Finishers: Brutal Finishers knife clips (Humanoid on the mannequin avatar) in `Resources/Takedowns`, Alex's picks:
  - Stealth, crouched: Heart Stab. Stealth, standing: Shove Neck Slice (quick).
  - Brutal, at random: Duck and Back Stab, or the Slash Combo (mid-section slash, then face slash cross-faded in on the "Finisher2" states: Combo From / Combo Start / Combo Blend on the entry; the victim topples after).
  - Brutal with half cover (a seat) straight out from the victim's tile: always Shove to Table, the pair turned to face that seat.
  - The clips keep their own travel in the pose (imported with root XZ baked from the original position), so a pair lines up through the whole clip once its start offset is right. The exported clips start with both characters on the same spot; each pair has its own starting distance (as set up in Unreal), facing you: Victim Offset Z 1.5 (Shove Neck Slice 1.3), Victim Yaw 180.
  - Each entry can be tagged Guard / Heavy / Any (all Any for now).
- Ghosts: the move-preview ghost and the last-seen ghost are see-through outlines (last-seen fainter). The last-seen ghost marks only where an enemy actually last saw you.
- Keys: 1–8 skills, Shift leap, Ctrl hop, G grab, C crouch, Z undo, V all senses, B blood outlines, L log, H controls, R restart, N new train (after a run), Esc pause.
