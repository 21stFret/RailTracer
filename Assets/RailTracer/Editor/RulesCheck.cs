using System;
using System.Collections.Generic;
using System.Linq;

namespace RailTracer
{
    // Rules self-check. In Unity: menu Rail Tracer > Run Rules Check.
    public static class RulesCheck
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Rail Tracer/Run Rules Check")]
        static void Menu() { Run(); UnityEngine.Debug.Log("Rail Tracer rules check passed."); }
#endif

        // Legend at which a skill unlocks / is Bloodied, from the table.
        static int Unlock(Tuning t, SkillId s) => t.skills[(int)s].unlock;
        static int Blood(Tuning t, SkillId s) => t.skills[(int)s].bloodied;
        static int Cool(Tuning t, SkillId s) => t.skills[(int)s].cooldown;

        static void Check(bool ok, string what) { if (!ok) throw new Exception("Rules check failed: " + what); }

        static readonly string[] Map = { "P....", ".S...", "....." };

        static void EnemyTurn(Carriage c)
        {
            c.EndPlayerTurn();
            foreach (var e in c.enemies) if (!c.Over) c.EnemyAct(e);
            c.StartPlayerTurn();
        }

        static Body PlaceBody(Carriage c, int x, int y)
        {
            var dead = c.Spawn(x, y, 1, 0, 0);
            dead.state = EnemyState.Dead;
            var b = new Body { who = dead, pos = new Cell(x, y) };
            c.bodies.Add(b);
            return b;
        }

        public static void Run()
        {
            var t = new Tuning { hitChance = 1f, shakenHitChance = 1f, leapHitPenalty = 0f }; // sure shots, so hits can be checked

            // Cones: basic = forward and the forward diagonals; tough = also both sides, and further.
            var c = new Carriage(new[] { "P........", ".........", ".........", "........." }, t, 1);
            var basic = c.Spawn(4, 3, 0, -1, 0);
            var tough = c.Spawn(4, 3, 0, -1, 0, tough: true);
            Check(c.InCone(basic, new Cell(4, 1)) && c.InCone(basic, new Cell(6, 1)) && !c.InCone(basic, new Cell(7, 2)) && !c.InCone(basic, new Cell(5, 3)), "basic cone is the forward 90 degrees");
            Check(c.InCone(basic, new Cell(6, 2)) && c.InCone(basic, new Cell(2, 2)), "plus close peripheral: one forward, two to the side");
            Check(!c.InCone(basic, new Cell(5, 3)) && c.InCone(tough, new Cell(5, 3)) && !c.InCone(tough, new Cell(4, 4)), "tough cone takes in both sides, not behind");
            c = new Carriage(new[] { "P.........", ".........." }, t, 1);
            basic = c.Spawn(9, 0, -1, 0, 0);
            tough = c.Spawn(9, 1, -1, 0, 0, tough: true);
            Check(c.CanSee(basic, new Cell(2, 0)) && !c.CanSee(basic, new Cell(1, 0)), "guards see 7 tiles");
            Check(c.CanSee(tough, new Cell(1, 1)) && !c.CanSee(tough, new Cell(0, 1)), "heavies see 8");
            basic.awareness = tough.awareness = Awareness.Searching;
            Check(!c.CanSee(basic, new Cell(1, 0)) && !c.CanSee(tough, new Cell(0, 1)), "searching doesn't see further");
            basic.awareness = tough.awareness = Awareness.Combat;
            Check(!c.CanSee(basic, new Cell(1, 0)) && !c.CanSee(tough, new Cell(0, 1)), "nor does combat");

            // Half cover by distance: from 3 tiles or less it must be right next to you; from further, within 2.
            c = new Carriage(new[] { ".PS...." }, t, 1);
            var e = c.Spawn(5, 0, -1, 0, 0);
            c.crouched = true;
            Check(!c.CanSee(e, c.player), "crouched right behind the seat: hidden");
            c.player = new Cell(0, 0);
            Check(!c.CanSee(e, c.player), "2 tiles behind it, viewer 5 away: hidden");
            e.pos = new Cell(3, 0);
            Check(c.CanSee(e, c.player), "2 tiles behind it, viewer 3 away: seen");

            // Senses: seats hide you only when crouched; full cover always; hearing ignores both.
            c = new Carriage(Map, t, 1);
            e = c.Spawn(3, 1, -1, 0, 0);
            Check(c.CanSee(e, new Cell(0, 1)), "standing is visible over seats");
            c.crouched = true;
            Check(!c.CanSee(e, new Cell(0, 1)), "crouched behind a seat is hidden");
            c.crouched = false;
            Check(c.Hears(e, new Cell(0, 0), 3) && !c.Hears(e, new Cell(0, 0), 2) && !c.Hears(e, new Cell(0, 0), 0), "noise radius, through seats; 0 is silent");
            Check(!c.CanSee(e, new Cell(4, 1)), "can't see behind");
            // Peripheral over a seat: seen standing, hidden crouched (the first booth's guard and the tile at (1,1)).
            var pc = new Carriage(new[] { "..S...", "..S...", "......" }, t, 1);
            var pe = pc.Spawn(3, 0, 0, 1, 0);
            Check(pc.CanSee(pe, new Cell(1, 1)), "peripheral: standing seen over the seat");
            pc.crouched = true;
            Check(!pc.CanSee(pe, new Cell(1, 1)), "peripheral: crouched hidden by the seat");
            pc.crouched = false;
            Check(!pc.CanSee(pe, new Cell(1, 0)), "still blind straight to the side");
            Check(!c.Reachable().Contains(new Cell(1, 1)), "can't stand on seats");
            c = new Carriage(new[] { "P.L.." }, t, 1);
            e = c.Spawn(4, 0, -1, 0, 0);
            Check(!c.CanSee(e, new Cell(0, 0)) && c.CanSee(e, new Cell(3, 0)), "luggage blocks sight");

            // The close look: a searcher spots you on any tile next to it inside its cone, crouched or not (a guard's
            // 3 in front; a heavy's cone takes in the sides too). Behind them you're fine; from 2 away a seat hides you.
            c = new Carriage(new[] { ".PS...", "......" }, t, 1);
            e = c.Spawn(3, 0, -1, 0, 0);
            c.crouched = true;
            Check(!c.CanSee(e, c.player), "unaware: hidden behind the seat");
            e.awareness = Awareness.Searching;
            Check(!c.CanSee(e, c.player), "searcher 2 away: still hidden behind the seat");
            e.pos = new Cell(2, 1);
            e.facing = new Cell(1, 0);
            Check(!c.CanSee(e, c.player), "searcher beside you, looking away: you're fine");
            e.facing = new Cell(0, -1);
            Check(c.CanSee(e, c.player), "searcher with you on its forward diagonal: noticed");
            e.pos = new Cell(1, 1);
            e.facing = new Cell(1, 0);
            Check(!c.CanSee(e, c.player), "guard: right beside it, not in its cone");
            e.tough = true;
            Check(c.CanSee(e, c.player), "heavy: its cone takes in the side");
            e.tough = false;

            // Movement points, crouch cost and undo (bystander looking away).
            c = new Carriage(Map, t, 1);
            c.Spawn(4, 2, 1, 0, 0);
            Check(c.Move(new Cell(0, 1)) && c.movesLeft == t.moveRange - 1, "moving spends one point per tile");
            Check(c.Move(new Cell(0, 2)) && c.movesLeft == t.moveRange - 2, "can keep moving");
            c.ResetTurn();
            Check(c.player == new Cell(0, 0) && c.movesLeft == t.moveRange, "reset undoes moves");
            c.ToggleCrouch();
            Check(c.Move(new Cell(0, 1)) && c.movesLeft == t.moveRange - t.crouchMoveCost, "crouching halves movement");

            // Every tile of the path is checked: walking through a cone puts them in combat, even if you end out of sight.
            c = new Carriage(new[] { "P....", "....." }, t, 1);
            e = c.Spawn(1, 1, 0, -1, 0);
            Check(c.Move(new Cell(4, 0)), "walk past");
            Check(e.awareness == Awareness.Combat && e.lastKnown == new Cell(3, 0) && e.sawYouAt == new Cell(3, 0) && e.hasSeen, "seen in passing: combat, remembering the last tile seen");
            Check(!c.CanReset, "spotted locks undo");
            Check(c.hp == t.maxHp, "unaware enemies don't shoot");

            // Of two equally short routes, the one out of the gunman's sight is taken.
            c = new Carriage(new[] { "P....", "..LL." }, t, 1);
            e = c.Spawn(4, 0, -1, 0, 0);
            e.awareness = Awareness.Searching;
            e.lastKnown = new Cell(4, 0);
            var safe = c.PathFor(new Cell(1, 1));
            Check(safe.Count == 2 && safe[0] == new Cell(0, 1), "safe route chosen over one under reaction fire");
            Check(c.Move(new Cell(1, 1)) && c.hp == t.maxHp, "and walked without being shot (leaving its sight draws nothing)");

            // A fresh sighting shouts: the shouter comes looking; listeners turn to it and look around in place; no cascade.
            c = new Carriage(new[] { "P..L......", "...L......" }, t, 1); // luggage keeps the listeners from seeing you
            var spotter = c.Spawn(2, 1, 0, -1, 0);
            var near = c.Spawn(4, 1, 1, 0, 0);
            var farther = c.Spawn(6, 1, 1, 0, 1);
            var outOfEarshot = c.Spawn(9, 1, 1, 0, 1); // hears `farther`, not the spotter
            c.Move(new Cell(1, 0));
            Check(spotter.awareness == Awareness.Combat, "the shouter: in combat, it comes looking");
            Check(near.awareness == Awareness.Searching && near.hunt == Hunt.Look && near.pos == new Cell(4, 1) && near.facing == new Cell(-1, 0), "listeners turn to the shout and look around where they stand");
            Check(farther.awareness == Awareness.Searching && farther.hunt == Hunt.Look, "every listener in earshot");
            Check(outOfEarshot.awareness == Awareness.Unaware, "no cascade");
            Check(!near.hasSeen && spotter.hasSeen, "hearing a shout isn't seeing you");

            // Reaction fire: a searcher shoots the first tile you enter in its cone, once per turn.
            c = new Carriage(new[] { "P....", "....." }, t, 1);
            e = c.Spawn(3, 1, 0, -1, 0);
            e.awareness = Awareness.Searching;
            e.lastKnown = new Cell(3, 0);
            Check(c.WillReact(e), "reaction ready");
            c.Move(new Cell(4, 0));
            Check(c.player == new Cell(1, 0) && c.hp == t.maxHp - t.shotDamage, "reaction interrupts move");
            Check(e.awareness == Awareness.Combat && c.Decide(e) == Intent.Shoot, "whoever fires a reaction shot is in combat, not still searching");
            c.Move(new Cell(4, 0));
            Check(c.player == new Cell(4, 0) && c.hp == t.maxHp - t.shotDamage, "one reaction per turn");
            Check(!c.WillReact(c.Spawn(0, 1, 1, 0, 0)), "unaware: no reaction fire");
            // Stepping off a watched tile onto a hidden one draws nothing: the shot would miss as you go.
            c = new Carriage(new[] { ".....", ".L...", "....." }, t, 1);
            e = c.Spawn(4, 0, -1, 0, 0);
            e.awareness = Awareness.Searching;
            e.lastKnown = new Cell(0, 0);
            c.player = new Cell(0, 0);
            Check(c.Sees(e, c.player, false) && !c.Sees(e, new Cell(0, 1), false), "set-up: watched tile, hidden one beside it");
            var preview = c.MovePreview(new Cell(0, 1));
            Check(preview.Count == 2 && preview[0].shooter == null && preview[0].seen && !preview[1].seen && preview[1].shooter == null, "preview: no shot marked");
            c.Move(new Cell(0, 1));
            Check(c.player == new Cell(0, 1) && c.hp == t.maxHp, "stepping off a watched tile: no shot");

            // Gunfire: allies don't block the shot, and the whole carriage learns where you are.
            c = new Carriage(Map, t, 1);
            var shooter = c.Spawn(4, 0, -1, 0, 0);
            c.Spawn(2, 0, 1, 0, 0); // ally in the line of fire
            var bystander = c.Spawn(4, 2, 1, 0, 1);
            c.EnemyAct(shooter);
            Check(shooter.awareness == Awareness.Combat && c.hp == t.maxHp - t.shotDamage, "sees you: combat, shoots past allies");
            Check(bystander.awareness == Awareness.Searching && bystander.lastKnown == new Cell(0, 0), "gunfire alerts everyone");
            Check(c.HalfCovered(new Cell(0, 1), new Cell(4, 1)) && c.HitChance(shooter, new Cell(0, 1)) == t.halfCoverHitMult * t.hitChance, "seat is half cover");

            // Patrols face the way they walk; guards turn every few turns.
            c = new Carriage(new[] { "P.........", ".........." }, t, 1);
            var patroller = c.Spawn(3, 1, 1, 0, 0, route: new List<Cell> { new Cell(3, 1), new Cell(8, 1) });
            var guard = c.Spawn(9, 0, 0, 1, 0);
            EnemyTurn(c);
            Check(patroller.pos == new Cell(5, 1) && patroller.facing == new Cell(1, 0), "patroller walks its route, facing forward");
            Check(c.NextFacing(guard) == new Cell(0, -1), "guard about to turn");
            Check(c.IntentText(guard) == "Hold", "a stationary guard holds");
            EnemyTurn(c);
            Check(guard.facing == new Cell(0, -1), "guard turned after 2 turns");

            // Stealth kill: only when the victim can't see you; pulled onto your tile; heard by adjacent tiles only.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            var victim = c.Spawn(1, 0, -1, 0, 0);
            Check(c.CantAct(Act.QuietKill, victim) != null, "can't stealth kill someone who sees you");
            victim.facing = new Cell(1, 0);
            var adjacent = c.Spawn(1, 1, 1, 0, 1);
            var far = c.Spawn(5, 1, 1, 0, 1);
            Check(c.Perform(Act.QuietKill, victim), "stealth kill from behind");
            Check(c.BodyAt(c.player) != null && c.player == new Cell(0, 0), "pulled onto your tile; you don't move");
            Check(adjacent.awareness == Awareness.Searching && adjacent.lastKnown == new Cell(0, 0) && adjacent.terror == t.smallTerror
                  && adjacent.clue == Clue.Noise && c.IntentText(adjacent) == "Investigate: Noise", "next to you hears it: investigates the noise");
            Check(far.awareness == Awareness.Unaware && far.terror == 0, "two tiles away hears nothing");
            Check(c.bodies.Count == 1 && c.stains.Count == 1 && c.stains[0].tiles.Count == 1 && c.IsBloody(c.player), "stealth kill bloodies just your tile");
            Check(c.legend == t.quietLegend && c.movesLeft == t.moveRange && !c.Perform(Act.QuietKill, far), "legend, action spent");

            // Standing stealth kill seen over the seats: the witness fights.
            c = new Carriage(new[] { "P.S...", "......" }, t, 1);
            victim = c.Spawn(1, 0, -1, 0, 0);
            victim.facing = new Cell(0, 1);
            var witness = c.Spawn(5, 0, -1, 0, 0);
            Check(c.Perform(Act.QuietKill, victim), "standing stealth kill");
            Check(witness.awareness == Awareness.Combat && witness.terror == t.mediumTerror && witness.hasSeen, "witness sees you do it");

            // Brutal kill locked at 0, unlocked by the first stealth kill (legend 10); rail grab at 30.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            victim = c.Spawn(1, 0, 1, 0, 0);
            var second = c.Spawn(0, 1, 0, 1, 0);
            Check(c.CantAct(Act.RailGrab, victim) != null && c.CantAct(Act.BrutalKill, victim) != null, "rail and brutal locked at 0");
            c.Perform(Act.QuietKill, victim);
            c.StartPlayerTurn();
            Check(c.legend == Unlock(t, SkillId.Brutal) && c.CantAct(Act.BrutalKill, second) == null, "brutal kill unlocked after the first stealth kill");
            Check(c.CantAct(Act.RailGrab, second) != null, "rail grab still locked at 10");
            c.legend = Unlock(t, SkillId.Rail);
            Check(c.CantAct(Act.RailGrab, second) == null, "rail grab unlocked at 30");
            c.ToggleCrouch();
            Check(c.CantAct(Act.BrutalKill, second) == null, "brutal from a crouch is allowed");
            c.ToggleCrouch();

            // Brutal kill: roots you, whole carriage hears it, witnesses fight (no free shots); one victim only; 2-turn cooldown.
            c = new Carriage(new[] { "P.......", "......L." }, t, 1); // luggage hides the kill from the listener, who turns to the shout
            victim = c.Spawn(1, 0, 1, 0, 0);
            second = c.Spawn(3, 1, 0, 1, 0); // close by but not beside you (a searcher beside you would notice you and fire too)
            witness = c.Spawn(5, 0, -1, 0, 0);
            witness.hasSeen = true; // seen you before: gun out, no startle
            var listener = c.Spawn(7, 1, 1, 0, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            c.ToggleCrouch();
            c.Perform(Act.BrutalKill, victim);
            Check(c.crouched, "crouched before it: back to crouching after");
            Check(second.InPlay, "no double kill");
            Check(!c.turnOver && c.movesLeft == 0 && c.Reachable().Count == 0 && c.player == new Cell(1, 0), "brutal uses up your moves (you end the turn), onto the victim's tile");
            Check(witness.awareness == Awareness.Combat && witness.terror == t.largeTerror, "witness: large terror, combat");
            Check(listener.awareness == Awareness.Searching && listener.terror == t.smallTerror, "heard across the carriage");
            Check(c.stains[0].tiles.Count == 4, "brutal bloodies the tile and its open neighbours");
            Check(c.hp == t.maxHp && c.legend == Unlock(t, SkillId.Brutal) + t.brutalGain && c.shotsFired.Count == 0, "no free shots");
            Check(c.brutalWait == Cool(t, SkillId.Brutal) && Cool(t, SkillId.Brutal) == 2, "cooldown 2");
            c.EndPlayerTurn();
            c.StartPlayerTurn();
            Check(c.Wait(SkillId.Brutal) == 1 && c.CantAct(Act.BrutalKill, second) != null, "blocked next turn, the box shows 1");
            c.StartPlayerTurn();
            Check(c.brutalWait == 0, "ready the turn after");

            // Blood: seen like a crouched player on that tile; each enemy reacts once per stain.
            c = new Carriage(new[] { "........", "........" }, t, 1);
            c.player = new Cell(7, 0);                      // behind the seer and 2 away from it (beside a searcher, it'd notice you)
            var blood = new Stain();
            blood.tiles.Add(new Cell(3, 1));
            c.stains.Add(blood);
            var seer = c.Spawn(5, 1, -1, 0, 0);
            c.EnemyAct(seer);
            Check(seer.reacted.Contains(blood) && seer.awareness == Awareness.Searching, "blood seen: searching to the stain");

            // Rail grab: unlocks at 10, at a window, 2-turn cooldown.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            victim = c.Spawn(1, 0, 1, 0, 0);
            c.legend = Unlock(t, SkillId.Rail);
            Check(c.KillOutcome(Act.RailGrab, victim) == (c.player, victim.pos), "rail preview: hung out of their window");
            Check(c.Perform(Act.RailGrab, victim) && c.railWait == Cool(t, SkillId.Rail), "rail grab, then cooldown");
            Check(c.bodies[0].hung && c.bodies[0].pos == new Cell(1, 0) && !c.CanGrab, "hung out of the window: can't be grabbed");
            Check(new[] { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1) }.All(c.IsBloody) && !c.IsBloody(new Cell(3, 0)),
                  "blood on every tile within 1 of the window");
            c.StartPlayerTurn();
            Check(c.CantAct(Act.RailGrab, c.Spawn(0, 1, 0, 1, 0)) != null, "still cooling down");

            // Leap: whole move, 3-turn cooldown; a brutal kill at 60 refreshes it.
            c = new Carriage(new[] { "P.S..", "....." }, t, 1);
            c.Spawn(4, 1, 1, 0, 0); // bystander looking away (an empty carriage counts as won)
            Check(c.Leap(new Cell(3, 0)) && c.movesLeft == 0 && c.leapWait == Cool(t, SkillId.Leap), "leap over a seat: whole move");
            c.StartPlayerTurn(); c.StartPlayerTurn();
            Check(!c.CanLeapTo(new Cell(0, 0)), "cooling down");
            c.StartPlayerTurn();
            Check(c.CanLeapTo(new Cell(0, 0)), "ready after 3 turns");
            c.Move(new Cell(4, 0));
            Check(!c.CanLeapTo(new Cell(3, 1)), "not after walking");
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            c.leapWait = 2;
            c.legend = Blood(t, SkillId.Brutal) - 1;
            c.Perform(Act.BrutalKill, c.Spawn(1, 0, 1, 0, 0));
            Check(c.leapWait == 2, "brutal kill below 60 leaves the leap cooling");
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            c.leapWait = 2;
            c.legend = Blood(t, SkillId.Brutal);
            c.Perform(Act.BrutalKill, c.Spawn(1, 0, 1, 0, 0));
            Check(c.leapWait == 0, "brutal kill at 60 refreshes the leap");

            // Window hop: out one booth's window and in at the next on the same side; whole move until 25.
            c = new Carriage(new[] { "..S..S..", "........" }, t, 1);
            c.Spawn(7, 1, 1, 0, 0);
            c.player = new Cell(1, 0);
            Check(c.CanHopTo(new Cell(3, 0)) && c.CanHopTo(new Cell(4, 0)) && !c.CanHopTo(new Cell(6, 0)) && !c.CanHopTo(new Cell(1, 1)), "only into the next booth, same side");
            Check(c.Hop(new Cell(3, 0)) && c.movesLeft == 0, "hop costs the move");
            c.StartPlayerTurn();
            c.legend = Blood(t, SkillId.Hop);
            Check(c.Hop(new Cell(6, 0)) && c.movesLeft == t.moveRange, "free at 20");
            PlaceBody(c, 7, 0);
            c.Grab();
            Check(!c.CanHopTo(new Cell(3, 0)), "bodies don't go through windows");

            // Demon throw at 80: blood where it lands, big terror for those who see it.
            c = new Carriage(new[] { "P.......", "........" }, t, 1);
            PlaceBody(c, 1, 0);
            var watcher = c.Spawn(7, 0, -1, 0, 0);
            c.Grab();
            c.legend = Unlock(t, SkillId.Throw) - 1;
            Check(!c.CanThrowTo(new Cell(3, 0)), "throw needs 80 legend");
            c.legend = Unlock(t, SkillId.Throw);
            Check(c.Throw(new Cell(3, 0)) && c.held == null && c.BodyAt(new Cell(3, 0)) != null, "thrown");
            Check(watcher.terror == t.largeTerror && c.IsBloody(new Cell(3, 0)) && c.IsBloody(new Cell(4, 0)), "terror and blood where it lands");
            Check(watcher.clue == Clue.Body, "a body landing: they investigate the body");
            c = new Carriage(new[] { "P.......", "........" }, t, 1);
            PlaceBody(c, 1, 0);
            c.Spawn(7, 1, 1, 0, 0);
            c.Grab();
            c.legend = Unlock(t, SkillId.Throw);
            Check(c.CanThrowTo(new Cell(5, 0)) && !c.CanThrowTo(new Cell(6, 0)), "throw reaches 5 tiles");
            c.acted = true;
            Check(c.WhyNot(SkillId.Throw) == "No action" && c.WhyNot(SkillId.Brutal) == "No action", "action spent: no action");

            // Dread breaks a watcher; broken enemies are out of play and can be executed.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            e = c.Spawn(1, 0, -1, 0, 0);
            e.terror = t.brokenAt - 1;
            e.state = EnemyState.Broken;
            Check(c.Decide(e) == Intent.None && c.Won && c.CantAct(Act.QuietKill, e) != null, "broken counts as cleared: the level ends at once");
            c.Spawn(5, 1, 1, 0, 0); // someone still fighting: the broken can be killed meanwhile
            c.legend = Unlock(t, SkillId.Rail);
            Check(c.CantAct(Act.RailGrab, e) == null && c.Perform(Act.RailGrab, e) && e.state == EnemyState.Dead, "rail grab executes at window");

            // Kill reach: the stealth kill reaches diagonals only once bloodied (35); the others always do.
            c = new Carriage(new[] { "P.....", "......", "......" }, t, 1);
            victim = c.Spawn(1, 1, 1, 0, 0);
            c.legend = Unlock(t, SkillId.Brutal);
            Check(c.CantAct(Act.QuietKill, victim) != null && c.CantAct(Act.BrutalKill, victim) == null, "no diagonal stealth kill until bloodied; brutal reaches it");
            c.legend = Blood(t, SkillId.Stealth);
            Check(c.Perform(Act.QuietKill, victim), "stealth kill on a diagonal once bloodied");
            c = new Carriage(new[] { "P.....", "......", "......" }, t, 1);
            Check(c.CantAct(Act.QuietKill, c.Spawn(2, 1, 1, 0, 0)) != null, "a knight's move is out of reach");

            // Snatch reach: exactly 2 straight out, not through full cover or past someone, only once Bloodied.
            c = new Carriage(new[] { "P.L..", "....." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            Check(c.CantAct(Act.QuietKill, c.Spawn(2, 1, 1, 0, 0)) != null, "no snatch from (2,1)");
            Check(c.CantAct(Act.QuietKill, c.Spawn(3, 0, 1, 0, 0)) != null, "no snatch from 3 away");
            c = new Carriage(new[] { "PL...", "....." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            Check(c.CantAct(Act.QuietKill, c.Spawn(2, 0, 1, 0, 0)) != null, "snatch blocked by luggage between");
            c = new Carriage(new[] { "P....", ".....", "....." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            c.Spawn(0, 1, 0, 1, 1); // in the way, facing away
            Check(c.CantAct(Act.QuietKill, c.Spawn(0, 2, 0, 1, 0)) != null, "snatch blocked by someone between");
            c = new Carriage(new[] { "P.S..", "....." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth) - 1;
            Check(c.CantAct(Act.QuietKill, c.Spawn(2, 0, 1, 0, 0)) != null, "no snatch below 35");
            c.legend = Blood(t, SkillId.Stealth);
            Check(c.KillOutcome(Act.QuietKill, c.enemies[0]) == (c.player, c.player), "snatch over a seat: body lands on your tile");

            // Preview: brutal steps you onto the victim's tile; the stealth kill leaves you put.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            victim = c.Spawn(1, 0, 1, 0, 0);
            Check(c.KillOutcome(Act.BrutalKill, victim) == (victim.pos, victim.pos), "brutal preview: onto the victim's tile");
            Check(c.KillOutcome(Act.QuietKill, victim) == (c.player, c.player), "stealth preview: you stay, the body lands on your tile");

            // The table matches the handoff (slot order, unlocks, Bloodied points, cooldowns).
            int[] unlocks = { 0, 0, 0, 0, 10, 30, 80, 90 }, bloodied = { -1, 70, 20, 40, 50, 60, 80, 90 }, cools = { 0, 3, 0, 0, 2, 2, 3, 0 };
            for (int i = 0; i < 8; i++)
                Check(t.skills[i].unlock == unlocks[i] && t.skills[i].bloodied == bloodied[i] && t.skills[i].cooldown == cools[i], "skill table row " + (i + 1));

            // Blank walls: floor with no window behind it isn't a window (no hop, no rail grab).
            c = new Carriage(new[] { "P._.", "....", "...." }, t, 1);
            Check(c.IsWindow(new Cell(1, 0)) && !c.IsWindow(new Cell(2, 0)) && c.BlankWall(new Cell(2, 0)), "a blank wall has no window");

            // The leap flies over luggage and crates (Update 18), but can't land on them; walls still stop it.
            c = new Carriage(new[] { "PLX..", ".....", "....." }, t, 1);
            c.Spawn(4, 2, 1, 0, 0); // bystander looking away (an empty carriage counts as won)
            Check(c.CanLeapTo(new Cell(3, 0)) && c.CanLeapTo(new Cell(2, 1)) && !c.CanLeapTo(new Cell(1, 0)), "leap over luggage and a crate, not onto them");
            Check(c.CanLeapTo(new Cell(0, 2)) && c.CanLeapTo(new Cell(1, 2)), "a clear flight is fine");
            c = new Carriage(new[] { ".W..", "P.W.", "...." }, t, 1);
            c.Spawn(3, 2, 1, 0, 0);
            Check(c.CanLeapTo(new Cell(1, 2)), "leap set-up is live");
            Check(!c.CanLeapTo(new Cell(3, 1)) && !c.CanLeapTo(new Cell(2, 0)), "no leap through an internal wall");

            // A search runs out: a guard walks back to its post and faces the way it did; a patroller rejoins its route.
            c = new Carriage(new[] { "P........", ".........", "........." }, t, 1);
            var guard2 = c.Spawn(6, 0, 0, 1, 0);
            guard2.pos = new Cell(4, 2);
            guard2.facing = new Cell(1, 0); // looking away from you
            Check(c.Decide(guard2) == Intent.Return && c.IntentText(guard2) == "Back to post", "unaware away from its post: going back");
            for (int turn = 0; turn < 3 && guard2.pos != guard2.post; turn++) EnemyTurn(c);
            Check(guard2.pos == new Cell(6, 0) && guard2.facing == new Cell(0, 1) && c.Decide(guard2) == Intent.Hold, "back at its post, facing as it was");
            e = c.Spawn(8, 2, 1, 0, 0);
            e.awareness = Awareness.Searching;
            e.hunt = Hunt.Sweep;
            e.lastKnown = e.pos;
            e.searchLeft = 1;
            e.facing = new Cell(1, 0);
            EnemyTurn(c);
            Check(e.awareness == Awareness.Unaware && c.Decide(e) == Intent.Return, "search over: heads back");

            // Brutal kill from a crouch stands you up.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            victim = c.Spawn(1, 0, 1, 0, 0);
            c.Spawn(5, 1, 1, 0, 0);
            c.ToggleCrouch();
            Check(c.Perform(Act.BrutalKill, victim) && c.crouched, "brutal kill from a crouch: back down after");

            // Why a kill can't be used: the HUD says so.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            var tough3 = c.Spawn(1, 0, 1, 0, 0, tough: true);
            Check(c.WhyNot(SkillId.Brutal) == "Shake them first", "tough and calm: shake them first");
            tough3.terror = t.shakenAt;
            Check(c.WhyNot(SkillId.Brutal) == null, "shaken: ready");

            // Crowd cover: each standing enemy on the line halves the sight beyond them (rounded down).
            c = new Carriage(new[] { "P.........", ".........." }, t, 1);
            var looker = c.Spawn(0, 1, 1, 0, 0);            // basic: 7 tiles
            Check(c.CanSee(looker, new Cell(7, 1)) && !c.CanSee(looker, new Cell(8, 1)), "7 tiles down a clear row");
            var crowd = c.Spawn(2, 1, 1, 0, 1);              // 2 away, looking away: 2 + 5/2 = 4
            Check(c.CanSee(looker, new Cell(4, 1)) && !c.CanSee(looker, new Cell(5, 1)), "one in the way halves the rest: sees 4");
            crowd.state = EnemyState.Broken;
            Check(c.CanSee(looker, new Cell(6, 1)), "a kneeling Broken enemy doesn't block");
            Check(c.SightThrough(new Enemy { pos = new Cell(0, 0), tough = true, state = EnemyState.Active }, new Cell(9, 0)) == 8, "tough sees 8");

            // Names: from the pool, no repeats; the log uses them.
            c = new Carriage(new[] { "P........." }, t, 1);
            for (int i = 0; i < 10; i++) c.Spawn(i, 0, 1, 0, 0);
            c.NameEnemies();
            Check(c.enemies.All(x => Carriage.NamePool.Contains(x.name)) && c.enemies.Select(x => x.name).Distinct().Count() == 10, "ten different 1930s names");

            // The last-seen ghost: where they saw you, while one who saw you there still hunts.
            c = new Carriage(new[] { "P....", "....." }, t, 1);
            e = c.Spawn(2, 1, 0, -1, 0);
            Check(c.LastSeen == null, "no ghost before anyone sees you");
            c.Move(new Cell(4, 0));
            Check(c.LastSeen == new Cell(4, 0), "ghost where he last saw you (his close side view reaches the end tile)");
            e.state = EnemyState.Dead;
            Check(c.LastSeen == null, "he's dead: no ghost");

            // Turn arrows: a guard's arrow shows only the turn before it turns; an alert guard holding doesn't show one.
            c = new Carriage(new[] { "P.........", ".........." }, t, 1);
            var g2 = c.Spawn(9, 0, 0, 1, 0);
            Check(c.PlannedTurn(g2) == null, "guard: no arrow while it holds");
            EnemyTurn(c);
            Check(c.PlannedTurn(g2) == new Cell(0, -1), "the turn before it turns: arrow");
            var plan = c.PlannedTurn(g2);
            EnemyTurn(c);
            Check(g2.facing == plan, "and it turns that way");
            g2.awareness = Awareness.Startled;
            Check(c.PlannedTurn(g2) == null, "startled: arrow gone");

            // Rail grab: you or they must be at a window.
            c = new Carriage(new[] { "P.....", "......", "......" }, t, 1);
            c.legend = Unlock(t, SkillId.Rail);
            victim = c.Spawn(1, 1, 1, 0, 0);                 // not at a window, but you are
            Check(c.CantAct(Act.RailGrab, victim) == null, "rail grab from your window");
            c.player = new Cell(0, 1);
            Check(c.CantAct(Act.RailGrab, victim) != null && c.WhyNot(SkillId.Rail) == "Needs a window", "neither at a window: no");

            // Window hop: once a turn, even when it no longer costs the move.
            c = new Carriage(new[] { "..S..S..S..", "..........." }, t, 1);
            c.Spawn(10, 1, 1, 0, 0);
            c.player = new Cell(1, 0);
            c.legend = Blood(t, SkillId.Hop);
            Check(c.Hop(new Cell(3, 0)) && !c.CanHopTo(new Cell(6, 0)) && c.WhyNot(SkillId.Hop) == "Hopped already", "one hop a turn");
            c = new Carriage(new[] { "..S..S..", "........" }, t, 1);
            c.Spawn(7, 1, 1, 0, 0);
            c.player = new Cell(1, 0);
            c.Move(new Cell(0, 0));
            Check(c.WhyNot(SkillId.Hop) == "Out of moves", "moved, not bloodied: out of moves");

            // Stealth kill from the front: the HUD says why.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            c.Spawn(1, 0, -1, 0, 0);
            Check(c.WhyNot(SkillId.Stealth) == "They can see you", "stealth from the front: they can see you");

            // Leaping across a watcher's sight draws its reaction shot, even when the landing is out of its view.
            c = new Carriage(new[] { "PL.....", ".......", ".......", "......." }, t, 1);
            var watcher2 = c.Spawn(2, 0, -1, 0, 0);
            watcher2.awareness = Awareness.Searching;
            watcher2.lastKnown = watcher2.pos;
            watcher2.hasSeen = true;
            Check(!c.Sees(watcher2, new Cell(0, 3), false) && c.Sees(watcher2, new Cell(0, 1), false), "set-up: sees the arc, not the landing");
            Check(c.Leap(new Cell(0, 3)) && c.hp < t.maxHp, "reaction fire on a leap across its sight");

            // Dragging a body out of a watcher's sight still spooks it (it saw the body start to slide).
            c = new Carriage(new[] { "......", "L.....", "......", "......" }, t, 1);
            c.player = new Cell(3, 0);
            var body2 = PlaceBody(c, 4, 0);
            e = c.Spawn(1, 3, 1, 0, 0);
            e.found.Add(body2);
            Check(c.Sees(e, new Cell(4, 0), true) && !c.Sees(e, new Cell(3, 0), true), "set-up: sees where it was, not where it goes");
            c.Grab();
            c.Move(new Cell(2, 0));
            Check(e.terror == t.unseenTerror, "a body dragged out of sight: spooked");

            // Leap bloodied at 75: twice the range.
            c = new Carriage(new[] { "P.......", "........" }, t, 1);
            c.Spawn(7, 1, 1, 0, 0);
            c.legend = Blood(t, SkillId.Leap) - 1;
            Check(c.CanLeapTo(new Cell(3, 0)) && !c.CanLeapTo(new Cell(6, 0)), "leap reaches 3 tiles");
            c.legend = Blood(t, SkillId.Leap);
            Check(c.CanLeapTo(new Cell(6, 0)) && !c.CanLeapTo(new Cell(7, 0)), "bloodied: 6 tiles");

            // A stealth kill seen only where the victim stood: they vanish (pulled onto your tile, out of sight).
            c = new Carriage(new[] { "P......", ".......", "......." }, t, 1);
            victim = c.Spawn(1, 0, 1, 0, 0);
            var seesBody = c.Spawn(3, 2, 0, -1, 1);          // looking up: the victim's tile is in its cone, yours just outside
            c.ToggleCrouch();
            c.player = new Cell(0, 0);
            Check(!c.CanSee(seesBody, c.player) && c.Sees(seesBody, new Cell(1, 0), true), "set-up: sees the victim's tile, not you");
            Check(c.Perform(Act.QuietKill, victim), "crouched stealth kill in the open");
            Check(seesBody.awareness == Awareness.Searching && seesBody.lastKnown == new Cell(1, 0) && seesBody.found.Count == 0 && seesBody.terror == t.smallTerror
                  && seesBody.clue == Clue.Movement, "saw them vanish: searches where they stood");

            Update14(t);
            BodyFear(t);
            Update16(t);

            Generated(t);
            Update17(t);
            Scenarios(t);
        }

        // The demo train, the safe's cage, free walking, and a save replayed from its command history.
        static void Update17(Tuning t)
        {
            // Every car as before Update 17 (from 18.38): about nine, two heavies (generated cars: 8 to 10).
            for (int i = 0; i < Train.DemoCars; i++)
            {
                var car = Train.Demo(i);
                var c = Train.Make(car, t, 1, t.maxHp);
                Check(c.W == 26 && c.H == 5 && c.player == new Cell(0, 2), $"car {i + 1}: 26 x 5, you start at the door");
                Check(c.enemies.Count >= 8 && c.enemies.Count <= 10 && c.enemies.Count(e => e.tough) == 2, $"car {i + 1}: 8 to 10 enemies, 2 heavies");
                Check(c.enemies.All(e => !c.Blocked(e.pos)) && c.enemies.Select(e => e.pos).Distinct().Count() == c.enemies.Count, $"car {i + 1}: everyone on free floor, one each");
                // Every free tile reachable from the start (the far door too).
                var seen = new HashSet<Cell> { c.player };
                var todo = new Queue<Cell>(seen);
                while (todo.Count > 0)
                {
                    var at = todo.Dequeue();
                    foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                    {
                        var n = new Cell(at.x + d.x, at.y + d.y);
                        if (c.InBounds(n) && !c.Blocked(n) && seen.Add(n)) todo.Enqueue(n);
                    }
                }
                for (int x = 0; x < c.W; x++)
                    for (int y = 0; y < c.H; y++)
                        Check(c.Blocked(new Cell(x, y)) || seen.Contains(new Cell(x, y)), $"car {i + 1}: tile {x},{y} reachable");
                Check(Enumerable.Range(0, c.W).Any(x => c.IsWindow(new Cell(x, 0))) && Enumerable.Range(0, c.W).Any(x => c.IsWindow(new Cell(x, 4))), $"car {i + 1}: windows on both sides");
            }

            // Dining tables are half cover (hide you crouched, can be leapt); crates block sight; the cage blocks
            // walking and leaps but not sight.
            {
                var c = new Carriage(new[] { "P.T...", "......", "......" }, t, 1);
                Check(c.IsSeat(new Cell(2, 0)) && c.Blocked(new Cell(2, 0)) && c.LineOfSight(new Cell(0, 0), new Cell(5, 0), false), "a table: seat rules");
                c = new Carriage(new[] { "P.X...", "......" }, t, 1);
                Check(!c.LineOfSight(new Cell(0, 0), new Cell(5, 0), false) && c.Blocked(new Cell(2, 0)), "a crate blocks sight and walking");
                c = new Carriage(new[] { "P.C...", "......" }, t, 1);
                var g = c.Spawn(4, 0, -1, 0, 0);
                Check(c.Blocked(new Cell(2, 0)) && c.CanSee(g, c.player) && !c.CanLeapTo(new Cell(3, 0)), "the cage: seen through, not walked or leapt through");
            }

            // Sight through a partition only through its doorway: straight down the aisle, not at a steep slant.
            {
                var c = new Carriage(new[] { "...W...", "...W...", "P......", "...W...", "...W..." }, t, 1);
                Check(c.LineOfSight(new Cell(0, 2), new Cell(6, 2), false) && c.LineOfSight(new Cell(0, 1), new Cell(6, 3), false), "a partition: seen through its doorway");
                Check(!c.LineOfSight(new Cell(2, 1), new Cell(4, 3), false) && !c.LineOfSight(new Cell(1, 2), new Cell(6, 3), false), "a partition: not at a slant past its frame");
            }

            // Round tables and the dining cart: seat rules.
            {
                var c = new Carriage(new[] { ".POD..", "..OO..", "......" }, t, 1);
                c.Spawn(5, 2, 1, 0, 0); // bystander looking away
                Check(c.IsSeat(new Cell(2, 0)) && c.IsSeat(new Cell(3, 0)) && c.Blocked(new Cell(3, 0)) && c.CanLeapTo(new Cell(4, 0)), "round table and cart: seat rules, leapt over");
            }

            // Dining car: tables (a 'T' group or a round table) at least a tile apart, diagonals too; everyone on floor.
            {
                var car = Train.Demo(2);
                var groups = new Dictionary<Cell, int>();
                int id = 0;
                for (int y = 0; y < car.map.Length; y++)
                    for (int x = 0; x < car.map[0].Length; x++)
                    {
                        char ch = car.map[y][x];
                        if ((ch != 'T' && ch != 'O') || groups.ContainsKey(new Cell(x, y))) continue;
                        var q = new Queue<Cell>(new[] { new Cell(x, y) });
                        groups[new Cell(x, y)] = ++id;
                        while (q.Count > 0)
                        {
                            var p = q.Dequeue();
                            foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                            {
                                var n = new Cell(p.x + d.x, p.y + d.y);
                                if (n.x < 0 || n.y < 0 || n.y >= car.map.Length || n.x >= car.map[0].Length || car.map[n.y][n.x] != ch || groups.ContainsKey(n)) continue;
                                groups[n] = id;
                                q.Enqueue(n);
                            }
                        }
                    }
                bool spaced = groups.All(a => groups.All(b => a.Value == b.Value || Math.Max(Math.Abs(a.Key.x - b.Key.x), Math.Abs(a.Key.y - b.Key.y)) >= 2));
                Check(spaced && id >= 8, "dining car: tables a tile apart");
                var dc = Train.Make(car, t, 1, 30);
                Check(dc.enemies.Count == 9 && dc.enemies.Count(e => e.tough) == 2 && dc.enemies.All(e => !dc.Blocked(e.pos)), "dining car: 9 on the floor, 2 heavies");
                var bc = Train.Make(Train.Demo(3), t, 1, 30);
                var fc = Train.Make(Train.Demo(0), t, 1, 30);
                Check(bc.enemies.Count == 9 && bc.enemies.All(e => !bc.Blocked(e.pos)) && fc.enemies.Count == 9 && fc.enemies.Count(e => e.tough) == 2, "baggage hold and third class: 9 each");
            }

            // A charging heavy that gets beside you within its 3 moves swings at once; one that needs 4 only closes in.
            {
                var ht = new Tuning { meleeHitChance = 1f };
                var c = new Carriage(new[] { "P.....", "......" }, ht, 1);
                var h = c.Spawn(4, 0, -1, 0, 0, tough: true);
                h.awareness = Awareness.Combat;
                h.lastKnown = c.player;
                Check(c.Swings(h, c.player) && c.IntentText(h).Contains("Hit 100%"), "heavy 3 away: will swing");
                c.Do("enemy 1");
                Check(h.pos == new Cell(1, 0) && c.hp == ht.maxHp - ht.shotDamage, "heavy arrives and swings");
                c = new Carriage(new[] { "P.....", "......" }, ht, 1);
                h = c.Spawn(5, 0, -1, 0, 0, tough: true);
                h.awareness = Awareness.Combat;
                h.lastKnown = c.player;
                Check(!c.Swings(h, c.player) && c.IntentText(h) == "Charge", "heavy 4 away: closes in only");
                c.Do("enemy 1");
                Check(h.pos == new Cell(2, 0) && c.hp == ht.maxHp, "heavy closes in, no swing");
            }

            // The walk-through car: nobody, never won, walk as far as you like.
            {
                var c = Train.Make(Train.WalkThrough(), t, 1, 20);
                Check(c.enemies.Count == 0 && !c.Over && c.hp == 20, "walk-through: empty, not over, your health kept");
                Check(c.Do("move 20 2") && c.player == new Cell(20, 2) && c.Do("move 25 2"), "walk-through: no move costs");
            }

            // A save: the same car, seed and health, replaying the history, lands exactly where the game was (the dice
            // included), whatever was done.
            for (int seed = 1; seed <= 40; seed++)
            {
                var car = Train.Demo(seed % Train.DemoCars);
                var tt = new Tuning();
                var live = Train.Make(car, tt, seed, 25);
                var rng = new Random(seed);
                for (int turn = 0; turn < 12 && !live.Over; turn++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var to = new Cell(rng.Next(live.W), rng.Next(live.H));
                        var near = live.enemies.Where(e => e.state == EnemyState.Active || e.state == EnemyState.Broken).OrderBy(e => Math.Abs(e.pos.x - live.player.x) + Math.Abs(e.pos.y - live.player.y)).FirstOrDefault();
                        switch (rng.Next(6))
                        {
                            case 0: live.Do($"move {to.x} {to.y}"); break;
                            case 1: live.Do($"leap {to.x} {to.y}"); break;
                            case 2: live.Do("crouch"); break;
                            case 3: if (near != null) live.Do($"act {(Act)rng.Next(3)} {near.id}"); break;
                            case 4: live.Do(rng.Next(2) == 0 ? "grab" : "drop"); break;
                            default: live.Do("undo"); break;
                        }
                    }
                    live.Do("end");
                    foreach (var e in live.enemies) if (!live.Over && (e.InPlay || live.Decide(e) == Intent.Flee)) live.Do($"enemy {e.id}");
                    live.Do("start");
                }
                var back = Train.Make(car, tt, seed, 25);
                foreach (var cmd in live.history) back.Do(cmd);
                Check(back.log.SequenceEqual(live.log) && back.player == live.player && back.hp == live.hp && back.legend == live.legend
                      && back.enemies.Select(e => (e.pos, e.state, e.terror, e.awareness, e.facing)).SequenceEqual(live.enemies.Select(e => (e.pos, e.state, e.terror, e.awareness, e.facing)))
                      && back.bodies.Count == live.bodies.Count && back.stains.Count == live.stains.Count && back.Round == live.Round, $"save replay, seed {seed}");
            }
        }

        // New trains: the right shape for the kit, every floor tile reachable, passengers on the floor and apart.
        static void Generated(Tuning t)
        {
            for (int seed = 1; seed <= 300; seed++)
            {
                var (map, spawns, patrols) = TrainGen.Make(new Random(seed));
                Check(map.Length == 5 && map.All(r => r.Length == TrainGen.W), "train: 26 x 5");
                Check(map[2].Skip(1).All(ch => ch == '.') && map[2][0] == 'P', "train: the aisle is clear, you start at its end");
                var seen = new HashSet<(int, int)> { (0, 2) };
                var todo = new Queue<(int x, int y)>(seen);
                while (todo.Count > 0)
                {
                    var (x, y) = todo.Dequeue();
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < TrainGen.W && ny < 5 && (map[ny][nx] == '.' || map[ny][nx] == '_') && seen.Add((nx, ny))) todo.Enqueue((nx, ny));
                    }
                }
                Check(map.SelectMany((r, y) => r.Select((ch, x) => (ch, x, y))).All(p => (p.ch != '.' && p.ch != '_') || seen.Contains((p.x, p.y))), "train: every floor tile reachable");
                // No luggage between two seat columns that face each other (seat columns 1-2, 3-4... by the kit).
                var cols = Enumerable.Range(0, TrainGen.W).Where(x => map.Any(r => r[x] == 'S')).ToList();
                for (int k = 0; k + 1 < cols.Count; k += 2)
                    for (int x = cols[k] + 1; x < cols[k + 1]; x++)
                        Check(map.All(r => r[x] != 'L' && r[x] != 'l'), "train: no luggage between facing seats");
                Check(spawns.Length >= 8 && spawns.Length <= 10 && spawns.Count(s => s[5] == 1) == 2, "train: 8 to 10 passengers, two tough");
                Check(spawns.All(s => map[s[1]][s[0]] == '.' || map[s[1]][s[0]] == '_') && spawns.Select(s => (s[0], s[1])).Distinct().Count() == spawns.Length, "train: passengers on free floor, one each");
                Check(patrols.Values.All(r => r.All(cell => cell.y == 2 && map[2][cell.x] == '.')), "train: patrols walk the aisle");
                var c = new Carriage(map, t, seed);
                for (int i = 0; i < spawns.Length; i++)
                {
                    var s = spawns[i];
                    c.Spawn(s[0], s[1], s[2], s[3], s[4], s[5] == 1, patrols.TryGetValue(i, out var r) ? r.ToList() : null);
                }
                for (int turn = 0; turn < 4 && !c.Over; turn++) EnemyTurn(c); // they can act in it
            }
        }

        // The handoff's "scenarios to check after building", one by one.
        static void Scenarios(Tuning t)
        {
            // 1. Crouched stealth kill behind seats: the watcher sees the victim vanish, goes Searching to that spot,
            //    and doesn't see you until within 2 tiles.
            var c = new Carriage(new[] { "PS.....", "......." }, t, 1);
            var victim = c.Spawn(0, 1, 0, 1, 0);
            var watcher = c.Spawn(6, 0, -1, 0, 0);
            c.ToggleCrouch();
            Check(c.Perform(Act.QuietKill, victim), "1: crouched stealth kill");
            Check(watcher.awareness == Awareness.Searching && watcher.hunt == Hunt.Go && watcher.lastKnown == new Cell(0, 1)
                  && watcher.terror == t.smallTerror && !watcher.hasSeen, "1: watcher sees the victim vanish, not you");
            for (int turn = 0; turn < 6 && watcher.awareness != Awareness.Combat; turn++)
            {
                EnemyTurn(c);
                if (watcher.awareness == Awareness.Combat)
                    Check(Math.Max(Math.Abs(watcher.pos.x - c.player.x), Math.Abs(watcher.pos.y - c.player.y)) <= 2, "1: only spotted within 2 tiles");
            }

            // 2. Leap over a booth past an unaware enemy: they glimpse it and go Searching to the landing tile.
            c = new Carriage(new[] { ".......", ".SS....", "..L....", "......." }, t, 1);
            c.player = new Cell(0, 1);
            var e = c.Spawn(1, 3, 0, -1, 0);
            Check(c.Leap(new Cell(3, 1)), "2: leap over the booth");
            Check(e.awareness == Awareness.Combat && e.lastKnown == new Cell(1, 1) && c.LastSeen == new Cell(1, 1) && e.terror == 0,
                  "2: sees only the start of the arc: combat, remembering where it saw you (not the landing)");
            // ...and someone who sees the landing, never having seen you, is startled and loses the turn.
            c = new Carriage(new[] { "P.S....", "......." }, t, 1);
            c.legend = 50;
            e = c.Spawn(6, 0, -1, 0, 0);
            Check(c.Leap(new Cell(3, 0)) && e.awareness == Awareness.Startled && e.terror == 50 * t.leapTerror / 100, "2b: startled by the landing");
            c.EndPlayerTurn();
            c.EnemyAct(e);
            Check(c.hp == t.maxHp && e.awareness == Awareness.Searching && e.lastKnown == new Cell(3, 0), "2b: loses the turn, then hunts the landing");

            // 3. Leap in front of an enemy who has already seen you: no startle (but still the legend's terror).
            c = new Carriage(new[] { "P.S....", "......." }, t, 1);
            c.legend = 50;
            e = c.Spawn(6, 0, -1, 0, 0);
            e.hasSeen = true;
            c.Leap(new Cell(3, 0));
            Check(e.awareness != Awareness.Startled && e.terror == 50 * t.leapTerror / 100, "3: no startle for someone who's seen you");

            // 4. Kill out of sight and hearing; a patroller finds the body and stops for the rest of its turn.
            c = new Carriage(new[] { "P...........", "............" }, t, 1);
            victim = c.Spawn(1, 0, 1, 0, 0);
            var patroller = c.Spawn(8, 1, -1, 0, 1, route: new List<Cell> { new Cell(8, 1), new Cell(2, 1) });
            c.Perform(Act.QuietKill, victim);
            Check(patroller.awareness == Awareness.Unaware && patroller.terror == 0, "4: unseen and unheard: nothing");
            c.player = new Cell(11, 0); // gone, behind it
            c.EndPlayerTurn();
            c.EnemyAct(patroller);
            Check(patroller.walked.Count == 2 && patroller.pos == new Cell(6, 1), "4: stops where it finds the body");
            Check(patroller.awareness == Awareness.Searching && patroller.hunt == Hunt.Sweep && patroller.terror == t.bodyFearSmall, "4: a stealth kill's body: small terror, searching nearby");
            Check(c.log.Contains($"{c.Name(patroller)} found {c.Name(victim)}'s body (stealth)."), "4: the log names the tier");

            // 5. Drag the body away out of the enemy's sight: it sees the body move, takes the big spike, follows the trail.
            c = new Carriage(new[] { "......S.", "..LL....", "........", "........" }, t, 1);
            c.player = new Cell(3, 0);
            var body = PlaceBody(c, 4, 0);
            e = c.Spawn(5, 3, 0, -1, 0);
            e.found.Add(body);
            Check(c.Grab(), "5: grab");
            Check(c.Move(new Cell(2, 0)) && body.pos == new Cell(3, 0) && c.movesLeft == t.moveRange - t.crouchMoveCost, "5: body follows into the tile you left, half speed");
            Check(e.terror == t.unseenTerror && e.hunt == Hunt.Trail && e.trail != null, "5: sees the body move, not you: big terror, follows");
            c.Move(new Cell(1, 0));
            Check(body.pos == new Cell(2, 0) && e.terror == t.unseenTerror && !c.CanReset, "5: one spike per drag; no undo");
            c.Drop();
            EnemyTurn(c);
            Check(e.awareness == Awareness.Searching && e.pos.y < 3, "5: following the trail");

            // 6. Window hop out of that booth: the enemy arrives to find only the body.
            Check(c.Hop(new Cell(7, 0)), "6: hop to the next booth");
            Check(e.awareness == Awareness.Searching, "6: not seen hopping");
            for (int turn = 0; turn < 3 && e.pos != body.pos; turn++) EnemyTurn(c);
            Check(e.pos == body.pos && e.awareness == Awareness.Searching && e.hunt == Hunt.Sweep, "6: reaches the body and searches; you're gone");

            // 7. A tough enemy can't be taken down while calm, only once Shaken.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            var tough = c.Spawn(1, 0, 1, 0, 0, tough: true);
            c.legend = Unlock(t, SkillId.Brutal);
            Check(c.CantAct(Act.QuietKill, tough) != null && c.CantAct(Act.BrutalKill, tough) != null, "7: calm tough enemy can't be killed");
            tough.terror = t.shakenAt;
            Check(c.CantAct(Act.QuietKill, tough) == null, "7: shaken: now you can");

            // 8. With Dread (100), stepping into a tough enemy's view shakes it at once.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            tough = c.Spawn(5, 1, -1, 0, 0, tough: true);
            c.legend = Unlock(t, SkillId.Dread);
            c.Move(new Cell(1, 0));
            Check(c.StageOf(tough) >= Stage.Shaken && c.CantAct(Act.QuietKill, tough) != "Too tough: shake them first.", "8: dread shakes on sight");

            // Updates 9-10, check 4: crouched behind a seat, snatch an enemy 2 tiles away who faces elsewhere. You
            // don't move; the body lands on your tile. A second enemy sees the victim yanked through the seat tile but
            // not you: big (unseen) terror, searching toward your tile.
            c = new Carriage(new[] { ".PS....", ".S.....", "......." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            victim = c.Spawn(3, 0, 1, 0, 0);
            var second = c.Spawn(2, 2, 0, -1, 1);
            c.ToggleCrouch();
            Check(!c.CanSee(second, c.player) && c.KillOutcome(Act.QuietKill, victim) == (c.player, c.player), "9-10/4: preview: body onto your tile");
            Check(c.Perform(Act.QuietKill, victim) && c.player == new Cell(1, 0) && c.BodyAt(c.player) != null && c.IsBloody(c.player), "9-10/4: snatched onto your tile");
            Check(second.awareness == Awareness.Searching && second.lastKnown == new Cell(3, 0) && second.terror == t.unseenTerror && !second.hasSeen,
                  "9-10/4: witness of the yank: big terror, searches where they vanished");
            Check(t.unseenTerror < t.largeTerror, "a kill seen is worse than one unseen");
            // Standing, the same witness sees you do it: combat.
            c = new Carriage(new[] { ".PS....", ".S.....", "......." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            victim = c.Spawn(3, 0, 1, 0, 0);
            second = c.Spawn(2, 2, 0, -1, 1);
            Check(c.Perform(Act.QuietKill, victim) && second.awareness == Awareness.Combat && second.hasSeen, "9-10/4b: seen snatching: combat");
            // Check 5: a luggage stack between: no snatch offered.
            c = new Carriage(new[] { ".PL....", "......." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            Check(c.KillOutcome(Act.QuietKill, c.Spawn(3, 0, 1, 0, 0)) == null, "9-10/5: luggage blocks the snatch");
        }

        // Update 16: awareness, searching, panic, sight and leap shots.
        static void Update16(Tuning t)
        {
            // A searcher that turned to face you during its turn: red at the start of yours.
            {
                var sc = new Carriage(new[] { "P....", "....." }, t, 1);
                var looker = sc.Spawn(3, 0, -1, 0, 0);
                looker.awareness = Awareness.Searching;
                sc.StartPlayerTurn();
                Check(looker.awareness == Awareness.Combat, "turn start: a searcher looking at you is fighting you");
            }

            // An unaware guard who spots you mid-move turns red but doesn't fire; at its turn, still seeing you, it shoots.
            var c = new Carriage(new[] { "P....", "....." }, t, 1);
            var g = c.Spawn(3, 1, 0, -1, 0);
            c.Move(new Cell(2, 0));
            Check(g.awareness == Awareness.Combat && c.hp == t.maxHp && c.player == new Cell(2, 0), "spotted mid-move: red, no reaction shot, the move carries on");
            Check(c.WillShoot(g, c.player) && !c.Charges(g, c.player), "crosshair: it will shoot if you end here");
            c.EndPlayerTurn();
            c.EnemyAct(g);
            Check(c.hp == t.maxHp - t.shotDamage, "still seen at its turn: it shoots");

            // Out of sight: still red until its turn, then searching where it last saw you.
            c = new Carriage(new[] { "P.....", "..LL..", "......" }, t, 1);
            g = c.Spawn(5, 0, -1, 0, 0);
            c.Move(new Cell(1, 0));
            Check(g.awareness == Awareness.Combat, "seen: red");
            c.EndPlayerTurn(); c.StartPlayerTurn();
            c.player = new Cell(1, 2); // gone behind the luggage
            Check(g.awareness == Awareness.Combat && !c.WillShoot(g, c.player), "out of its sight: still red, no crosshair");
            Check(c.Decide(g) == Intent.Investigate && c.IntentText(g) == "Investigate: Movement", "its turn: it will search where it saw you");

            // Heavies don't shoot: they charge.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            var h = c.Spawn(4, 0, -1, 0, 0, tough: true);
            Check(c.Charges(h, c.player) && !c.WillShoot(h, c.player), "a heavy shows the charge, not the crosshair");
            // ...and see only 2 tiles straight out to the sides.
            c = new Carriage(new[] { "......", "......", "......", "......", "......", "......", "......" }, t, 1);
            h = c.Spawn(0, 3, 1, 0, 0, tough: true);
            Check(c.InCone(h, new Cell(0, 1)) && !c.InCone(h, new Cell(0, 0)) && c.InCone(h, new Cell(5, 3)), "heavy: 2 to the side, full range ahead");

            // A blood trail: the searcher heads for the newest part it can see, and its spot moves with it.
            c = new Carriage(new[] { "......L.", "......L.", "......L." }, t, 1);
            c.player = new Cell(7, 2); // walled off
            var trail = new Stain { trail = true };
            for (int x = 0; x < 6; x++) trail.tiles.Add(new Cell(x, 0));
            c.stains.Add(trail);
            var s = c.Spawn(0, 2, 0, -1, 0);
            c.EndPlayerTurn();
            c.EnemyAct(s);
            Check(s.hunt == Hunt.Trail && s.lastKnown.HasValue && s.lastKnown.Value.x > 0, "follows the trail to the newest part it sees");
            Check(c.IntentText(s) == "Investigate: Blood", "investigating blood");

            // Looking around: the arrow's tile and facing are where it goes and faces.
            c = new Carriage(new[] { "P.........", "..........", ".........." }, t, 1);
            s = c.Spawn(6, 2, 1, 0, 0);
            s.awareness = Awareness.Searching;
            s.hunt = Hunt.Sweep;
            s.lastKnown = new Cell(6, 1);
            s.searchLeft = 3;
            var tile = c.PlannedTile(s);
            var face = c.NextFacing(s);
            c.EndPlayerTurn();
            c.EnemyAct(s);
            Check(tile.HasValue && s.pos == tile.Value && s.facing == face, "look-around: stands and faces where the arrow said");

            // A Panicked guard alternates panic turns and normal ones; reaction fire only off on a freeze turn.
            c = new Carriage(new[] { "P.......", "........" }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            var victim = c.Spawn(1, 0, 1, 0, 0);
            g = c.Spawn(6, 1, -1, 0, 0);
            g.hasSeen = true;
            g.terror = 20;
            h = c.Spawn(7, 0, -1, 0, 0, tough: true);
            h.hasSeen = true;
            h.terror = 20;
            c.Perform(Act.BrutalKill, victim);
            Check(c.StageOf(g) == Stage.Panicked && g.panicNext && c.StatusText(g).StartsWith("Panicked"), "panicked: its next turn is a panic turn");
            Check(h.berserk && c.StatusText(h) == "Berserk" && c.Decide(h) == Intent.Charge, "a panicked heavy goes berserk");
            g.panic = Panic.Freeze;
            Check(!c.WillReact(g) && c.Decide(g) == Intent.Freeze, "freeze turn: no reaction fire");
            c.EndPlayerTurn();
            c.EnemyAct(g);
            Check(!g.panicNext && c.Decide(g) != Intent.Freeze && c.Decide(g) != Intent.WildFire, "then a normal turn");
            c.EnemyAct(g);
            Check(g.panicNext, "then a panic turn again");
            c.player = new Cell(0, 1);
            c.EnemyAct(h);
            Check(h.awareness == Awareness.Combat && h.berserk, "berserk never drops to searching");

            // Broken: one roll, flee (if there's a way) or beg.
            var flee = new Tuning { brokenFleeChance = 1f };
            c = new Carriage(new[] { "........", "........", "........" }, flee, 1);
            c.player = new Cell(7, 0);
            c.legend = Unlock(flee, SkillId.Brutal);
            victim = c.Spawn(6, 0, 1, 0, 0);
            var b = c.Spawn(3, 1, 1, 0, 0);
            b.hasSeen = true;
            b.terror = 60;
            c.Spawn(0, 2, 0, -1, 0); // someone left fighting, so the level goes on
            c.Perform(Act.BrutalKill, victim);
            Check(b.state == EnemyState.Broken && b.fleeing && c.StatusText(b) == "Fled", "broken: runs");
            c.EndPlayerTurn();
            c.EnemyAct(b);
            Check(b.pos.x < 3, "runs for the nearer door");
            var beg = new Tuning { brokenFleeChance = 0f };
            c = new Carriage(new[] { "P.......", "........", "........" }, beg, 1);
            c.legend = Unlock(beg, SkillId.Brutal);
            victim = c.Spawn(1, 0, 1, 0, 0);
            b = c.Spawn(4, 0, -1, 0, 0);
            b.hasSeen = true;
            b.terror = 60;
            c.Spawn(7, 2, 1, 0, 0);
            c.Perform(Act.BrutalKill, victim);
            Check(b.state == EnemyState.Broken && !b.fleeing && c.StatusText(b) == "Begging", "broken: begs");

            // Shots at a leaping player are 25 points harder.
            var real = new Tuning();
            c = new Carriage(new[] { "P....", "....." }, real, 1);
            g = c.Spawn(4, 0, -1, 0, 0);
            Check(Math.Abs(c.HitChance(g, new Cell(1, 0), true) - (c.HitChance(g, new Cell(1, 0)) - 0.25f)) < 1e-4f, "leap shots: 25 points lower");
            Check(real.hitChance == 0.85f && real.meleeHitChance == 0.9f && real.halfCoverHitMult == 0.75f, "the scene's tuning is the default");
        }

        // Update 15: body fear by how the body was made.
        static void BodyFear(Tuning t)
        {
            var c = new Carriage(new[] { "P......", "......." }, t, 1);
            c.legend = Unlock(t, SkillId.Rail);
            var v = c.Spawn(1, 0, 1, 0, 0);
            c.Perform(Act.RailGrab, v);
            Check(c.bodies[0].kind == BodyKind.Rail && c.BodyFear(c.bodies[0]) == t.bodyFearLarge, "rail grab body: large");
            c = new Carriage(new[] { "P......", "......." }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            c.Perform(Act.BrutalKill, c.Spawn(1, 0, 1, 0, 0));
            Check(c.bodies[0].kind == BodyKind.Brutal && c.BodyFear(c.bodies[0]) == t.mediumTerror, "brutal body: medium");
            c = new Carriage(new[] { "P......", "......." }, t, 1);
            c.Perform(Act.QuietKill, c.Spawn(1, 0, 1, 0, 0));
            var b = c.bodies[0];
            Check(b.kind == BodyKind.Stealth && c.BodyFear(b) == t.bodyFearSmall, "stealth body: small");
            c.Spawn(6, 1, 1, 0, 0);
            c.Grab();
            c.Move(new Cell(1, 0));
            Check(b.kind == BodyKind.Stealth, "dragging keeps the kind");
            c.legend = Unlock(t, SkillId.Throw);
            c.StartPlayerTurn();
            c.Throw(new Cell(4, 1));
            Check(b.kind == BodyKind.Thrown && c.BodyFear(b) == t.bodyFearLarge, "thrown body: large");
        }

        // Update 14's post-build checks that the rules can answer (the rest are in Play mode).
        static void Update14(Tuning t)
        {
            // 3. Stealth through facing: crouched behind a seat, an unaware guard 2 away facing you can't see you:
            //    the snatch is allowed and pulls him onto your tile.
            var c = new Carriage(new[] { "PS.....", "......." }, t, 1);
            c.legend = Blood(t, SkillId.Stealth);
            var guard = c.Spawn(2, 0, -1, 0, 0);
            c.ToggleCrouch();
            Check(!c.CanSee(guard, c.player) && c.CantAct(Act.QuietKill, guard) == null, "14/3: facing you but can't see you: allowed");
            Check(c.Perform(Act.QuietKill, guard) && c.BodyAt(c.player) != null && c.player == new Cell(0, 0) && c.crouched, "14/3: pulled onto your tile, you stay crouched");

            // 4. Close look: a searcher steps next to you, facing you: seen, seat or not.
            c = new Carriage(new[] { "PS.....", "......." }, t, 1);
            var searcher = c.Spawn(1, 1, 0, -1, 0);
            c.ToggleCrouch();
            searcher.awareness = Awareness.Searching;
            searcher.facing = new Cell(-1, 0);
            Check(c.CanSee(searcher, c.player), "14/4: close look sees you");

            // 5. Clean kill split, standing: a witness who sees your tile sees a kill and fights; one who sees only
            //    the victim's tile sees a vanish and searches.
            c = new Carriage(new[] { "P......", "L......", "......." }, t, 1);
            var victim = c.Spawn(1, 0, 1, 0, 0);
            var seesYou = c.Spawn(6, 0, -1, 0, 0);
            var seesVictim = c.Spawn(1, 2, 0, -1, 1);
            Check(c.CanSee(seesYou, c.player) && !c.CanSee(seesVictim, c.player) && c.Sees(seesVictim, victim.pos, false), "14/5: set-up");
            Check(c.Perform(Act.QuietKill, victim), "14/5: standing stealth kill");
            Check(seesYou.awareness == Awareness.Combat && c.log.Contains($"{c.Name(seesYou)} is fighting you!"), "14/5: sees you: a kill, combat, logged");
            Check(seesVictim.awareness == Awareness.Searching && !seesVictim.hasSeen && c.log.Contains($"{c.Name(seesVictim)} is searching: movement."), "14/5: sees only the victim: vanish");

            // Brutal kill: a witness who never saw you is startled (no free shot), and stays startled.
            c = new Carriage(new[] { "P.......", "........" }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            victim = c.Spawn(1, 0, 1, 0, 0);
            var fresh = c.Spawn(6, 0, -1, 0, 0);
            c.Perform(Act.BrutalKill, victim);
            Check(fresh.awareness == Awareness.Startled, "never seen you: startled by the brutal kill");
            // A guard startled by a leap stays startled through a brutal kill he sees.
            c = new Carriage(new[] { "P.S.....", "........" }, t, 1);
            c.legend = Unlock(t, SkillId.Brutal);
            var g = c.Spawn(7, 0, -1, 0, 0);
            victim = c.Spawn(4, 1, 1, 0, 0);
            Check(c.Leap(new Cell(3, 0)) && g.awareness == Awareness.Startled, "startled by the leap");
            c.Perform(Act.BrutalKill, victim);
            Check(g.awareness == Awareness.Startled && c.hp == t.maxHp, "still startled");

            // 8. A guard hears a noise: "Investigate: Noise", and the log names him.
            c = new Carriage(new[] { "P......", "......." }, t, 1);
            c.NameEnemies();
            victim = c.Spawn(1, 0, 1, 0, 0);
            var hearer = c.Spawn(1, 1, 1, 0, 1);
            c.NameEnemies();
            c.Perform(Act.QuietKill, victim);
            Check(c.IntentText(hearer) == "Investigate: Noise" && c.log.Contains($"{hearer.name} is searching: noise."), "14/8: investigating the noise, logged by name");

            // 9. Spotted, then into combat: both logged.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            var spotter = c.Spawn(3, 1, 0, -1, 0);
            c.Move(new Cell(2, 0));
            Check(c.log.Contains($"{c.Name(spotter)} spotted you."), "14/9: spotted logged");
            c.EndPlayerTurn();
            c.EnemyAct(spotter);
            Check(spotter.awareness == Awareness.Combat && c.log.Contains($"{c.Name(spotter)} is fighting you!"), "14/9: combat logged");

            // 11. Leap preview: across a watched tile, the shot is marked where he sees you.
            c = new Carriage(new[] { "PL.....", ".......", ".......", "......." }, t, 1);
            var w = c.Spawn(2, 0, -1, 0, 0);
            w.awareness = Awareness.Searching;
            w.lastKnown = w.pos;
            w.hasSeen = true;
            var lp = c.LeapPreview(new Cell(0, 3));
            Check(lp[0].tile == c.player && lp[lp.Count - 1].tile == new Cell(0, 3) && lp.Exists(p => p.shooter == w && p.seen) && !lp[lp.Count - 1].seen,
                  "14/11: leap preview marks the shooter on the arc");

            // 13. The last enemy broken: the level is won at once.
            c = new Carriage(new[] { "P.....", "......" }, t, 1);
            var last = c.Spawn(4, 1, 1, 0, 0);
            last.terror = t.brokenAt;
            last.state = EnemyState.Broken;
            Check(c.Won, "14/13: all broken: won");
        }
    }
}
