# Rail Tracer — Design Notes

Companion to `RAIL_TRACER_STATE.md`. The state file says **what is built** (rules and numbers). This doc says **why**, **what's still open** and **where everything lives**. When they disagree on a rule or number, the state file wins.

Last updated: 4 Nov 2026, after Update 16.

## How we work

- **Alex** prototypes in Unity 6 (URP), models in Blender and builds with Claude Code. Design chats with Claude are design only: questions, decisions, mockups, handoffs.
- **Loop:** playtest notes → sort into bugs, design changes and explain-first items → settle open questions with Alex (one at a time) → a numbered handoff doc for Claude Code → Claude Code reads everything and asks all its questions before building → it builds, commits one commit per handoff and updates the state file → Alex syncs the project.
- **Before designing against the build**, check the synced state file. Don't trust memory of old handoffs.
- **Visual changes:** say which element you mean. Claude has misread vague references before.
- **Skills:** game-design-companion (questioning, triage, handoffs) and game-ui-companion (mockups, colour meaning, UI specs). Blender assets use stylized-blender-assets with the RailTracer profile.

## The game

Turn-based grid tactics on a 1930s train, in the style of Fights in Tight Spaces and Hard West. You're the conductor, secretly an ex-circus-acrobat assassin (inspired by Claire Stanfield in Baccano!). Fragile body, feared as a legend. The core fantasy is the train slowly realising what's on board.

Key signal from playtesting: the game is genuinely fun. Alex wanted to keep playing another carriage.

## Decisions and why

- **Legend from kills only, and it doubles as your shield.** Being feared protects you; playing timid starves you.
- **Notches every 10, 10–90, one change each.** Above 90 is overspill: a buffer that protects dread. This replaced crowded notches at 10/25/35/50/60/75/85/100, where 25 carried two changes.
- **"Bloodied" not "Upgraded".** Throw and dread are red-only, going from grey straight to red.
- **Three awareness colours: black unaware, white searching, red combat.** These match the tickets and skill boxes. The chip colour says what an enemy knows; the icon says what it will do if you end your turn now. Every state needs a visible icon (an empty slot looked broken; that's why the frozen icon exists).
- **Two threat indicators:** the crosshair on the chip is the end-of-turn shot; the dashed red floor zone is reaction fire while moving. Dashed means "hasn't happened yet" everywhere.
- **Predictable enemies, not random ones.** Searching is only go to the spot, follow blood, or look around. Panic changes state once (guards alternate panic turns; heavies go berserk for good; broken enemies roll once to beg or flee). Rolls that change every turn felt confusing, not varied.
- **Gore matters.** Body terror by kill type (stealth small, brutal medium, rail and thrown large) makes gory bodies deliberate fear traps.
- **Kill cam only on loud kills** (brutal, rail, throw): short, skippable, with a toggle. Stealth kills stay quick.
- **Guard vs heavy takedowns differ in animation only**, with the heavy's about 0.5 s longer. No rule difference.
- **Red legend lighting** affects the world only. Overlays must never change, since red carries meaning.

## Open and parked

**Bugs reported, check they're fixed:** a combat chip showing white with a crosshair; a magenta (missing material) circle under a chip; stray reaction-zone lines across booths (fix: outer edge only, plus a faint red tint inside).

**Design to come back to:**

- **Tutorial rework.** Research how good tactics games teach systems, then restructure the script. Add cards for things like dragging a body out of sight for fear. Wait until Update 16 has been playtested. Note for the Startled card: brutal kills startle too.
- **Fanned skill row.** The selected skill grows large and the others fan aside like cards. Build as its own artboard beside the current row in the HUD mockup.
- **Stealth kill vanish terror.** Witnesses who see only the victim's tile get 10, but "snatched by something unseen" is 35 elsewhere. Proposal: make the vanish 35.
- **Bloodless takedowns.** Proposal: drop Karate Chop KO and Neck Break, or make every stealth kill bleed the same. Random bloodless kills break predictability. Clean kills could be a future Bloodied upgrade.
- **Heavy takedowns.** All takedowns are tagged Any; tag a few longer ones Heavy.

**Art still needed:** rail grab, demon throw and crouched drag-down animations; Broken begging loop; red player aura (Alex sourcing); knife mesh (on hold); takedown spacing and kill cam angles to tune.

**Business:** buy your own licences for Brutal Finishers and Stealth Finishers before anything ships or is shared (both currently from a friend's copy, for testing only). Steam fee $100 when ready.

**Playtest watch list:** snatch booth-camping (snatch now at 40); whether the brutal cooldown tames the brutal + leap combo.

**Other:** test the design companion skill in a fresh chat with the parked Abe's Oddysee swarm-platformer idea.

## Where things live

**Repo:** github.com/21stFret/RailTracer (private). `RAIL_TRACER_STATE.md` in the root. Asset Store packs are gitignored and listed in README.md.

**Mockups:**

- [Rail Tracer HUD](https://claude.ai/artifact/LhFPCYribNkKgU6GcLbQVT): main HUD (Update 16 chips, floor overlays, notches every 10), tutorial card, ticket options and ticket styles.

**Docs:**

- [Design brief, Updates 1–10](https://claude.ai/artifact/WjPLALJMm2B43nEQ8qs8Vd)
- [Tutorial script](https://claude.ai/artifact/UcuacrB3t2VbsJGcReggTR)
- Handoffs: [4–7](https://claude.ai/artifact/G8idkKX52xMGHxhqm1EBbk), [9+10](https://claude.ai/artifact/Wsbms1w6wVGXsgKbdLkhJD), [11](https://claude.ai/artifact/MZaNTj4baLWBBXMftGb9qi), [12](https://claude.ai/artifact/PCJbDNRqbHB5hiviq7ChvQ), [13](https://claude.ai/artifact/8VCGAD8zbZUQk6uxCoSPBA), [14](https://claude.ai/artifact/Xet5aSAQ29rTpqgprT2ghg), [15](https://claude.ai/artifact/6ffoYBPYNnDxsGU6HrV5cx), [16 revised](https://claude.ai/artifact/2RAH6JRFzwfB4j2yQFwXy4)

**Blender props:** six decoration-only props (briefcase, ashtray with cigar, umbrella, hat hook with fedora, newspaper, steamer trunk), exported to `Assets/RailTracer/Train Assets/Props`.

**Style:** black #0B0809, bone white #F2EFEA, blood red #C8102E, bright red #E0202F, locked grey #3A3536. Fonts Anton and Barlow Condensed. Everything slanted 10°.
