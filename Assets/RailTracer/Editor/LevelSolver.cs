using System;
using System.Collections.Generic;
using System.Linq;

namespace RailTracer
{
    // Can a car be cleared, and in how few turns? A beam search over whole player turns with the real rules: each turn it
    // tries every plan (stand or crouch; walk to any reachable tile, or leap anywhere legal; then any kill in reach), plays
    // the enemy turn exactly as the game does, and keeps the most promising lines (enemies down, health, closing in).
    // States are copied by replaying their command history onto a fresh car (as saves are), so nothing drifts from the
    // rules. The dice are the car's seed: a result is for that seed. Not exhaustive: the turn count it finds is an upper
    // bound (a cleverer line can only be quicker). Doesn't try throws, drags, window hops or opening windows.
    //   Unity: Rail Tracer > Solve Demo Cars (results in the Console).   Harness: dotnet run -- solve
    public static class LevelSolver
    {
        public class Result
        {
            public bool won;
            public int turns, hp, killed, broken, tried;
            public List<string> line = new List<string>(); // your commands, turn by turn
            public string Summary(string name) => won
                ? $"{name}: cleared in {turns} turns, {hp} health left, {killed} killed / {broken} broken ({tried} lines tried)"
                : $"{name}: not cleared within the search ({tried} lines tried)";
        }

        public static Result Solve(CarDef car, Tuning t, int seed, int hp, int beam = 10, int maxTurns = 40)
        {
            var nodes = new List<List<string>> { new List<string>() };
            int tried = 0;
            for (int turn = 1; turn <= maxTurns && nodes.Count > 0; turn++)
            {
                var next = new Dictionary<string, (List<string> hist, double score)>();
                foreach (var hist in nodes)
                {
                    var basis = Load(car, t, seed, hp, hist);
                    foreach (var plan in Plans(car, t, seed, hp, hist, basis))
                    {
                        var c = Load(car, t, seed, hp, hist);
                        foreach (var cmd in plan) c.Do(cmd);
                        tried++;
                        if (!c.Won && !c.Lost) EnemyTurn(c);
                        if (c.Lost) continue;
                        if (c.Won) return Finish(c, tried);
                        var sig = Signature(c);
                        double score = Score(c);
                        if (!next.TryGetValue(sig, out var have) || score > have.score) next[sig] = (new List<string>(c.history), score);
                    }
                }
                nodes = next.Values.OrderByDescending(n => n.score).Take(beam).Select(n => n.hist).ToList();
            }
            return new Result { tried = tried };
        }

        static Carriage Load(CarDef car, Tuning t, int seed, int hp, List<string> hist)
        {
            var c = Train.Make(car, t, seed, hp);
            foreach (var cmd in hist) c.Do(cmd);
            return c;
        }

        // The enemy turn as the game plays it (Game.EnemyPhase): end, each enemy in turn (broken ones only to flee), start.
        static void EnemyTurn(Carriage c)
        {
            c.Do("end");
            foreach (var e in c.enemies)
            {
                if (c.Over) break;
                if (!e.InPlay && c.Decide(e) != Intent.Flee) continue;
                c.Do($"enemy {e.id}");
            }
            if (!c.Over) c.Do("start");
        }

        // Every plan for this turn: [crouch] then a move (or a leap, or staying put), then any kill on someone within
        // 2 tiles of where you end up (a kill that isn't possible there just fails, as in the game).
        static IEnumerable<List<string>> Plans(CarDef car, Tuning t, int seed, int hp, List<string> hist, Carriage basis)
        {
            foreach (bool toggle in new[] { false, true })
            {
                var c = toggle ? Load(car, t, seed, hp, hist) : basis;
                if (toggle) c.Do("crouch");
                var pre = toggle ? new List<string> { "crouch" } : new List<string>();
                var ends = new List<(Cell at, string cmd)> { (c.player, null) };
                ends.AddRange(c.Reachable().Where(x => x != c.player).Select(x => (x, $"move {x.x} {x.y}")));
                if (!toggle)
                    for (int x = 0; x < c.W; x++)
                        for (int y = 0; y < c.H; y++)
                            if (c.CanLeapTo(new Cell(x, y))) ends.Add((new Cell(x, y), $"leap {x} {y}"));
                foreach (var (at, cmd) in ends)
                {
                    var plan = new List<string>(pre);
                    if (cmd != null) plan.Add(cmd);
                    yield return plan;
                    foreach (var e in c.enemies)
                    {
                        if (!e.InPlay && e.state != EnemyState.Broken) continue;
                        if (Math.Max(Math.Abs(e.pos.x - at.x), Math.Abs(e.pos.y - at.y)) > 2) continue;
                        foreach (var act in new[] { Act.QuietKill, Act.BrutalKill, Act.RailGrab })
                        {
                            var skill = act == Act.QuietKill ? SkillId.Stealth : act == Act.RailGrab ? SkillId.Rail : SkillId.Brutal;
                            if (!c.Has(skill)) continue;
                            yield return new List<string>(plan) { $"act {act} {e.id}" };
                        }
                    }
                }
            }
        }

        // How promising a line is: enemies still fighting cost the most, then health, legend, how close you are to the
        // nearest of them, and how many are on to you.
        static double Score(Carriage c)
        {
            var active = c.enemies.Where(e => e.state == EnemyState.Active).ToList();
            double near = active.Count == 0 ? 0 : active.Min(e => Math.Abs(e.pos.x - c.player.x) + Math.Abs(e.pos.y - c.player.y));
            int fighting = active.Count(e => e.awareness == Awareness.Combat);
            return -active.Count * 1000 + c.hp * 12 + c.legend * 2 - near * 15 - fighting * 20;
        }

        static string Signature(Carriage c) =>
            $"{c.player.x},{c.player.y},{c.crouched},{c.hp},{c.legend}|" +
            string.Join(";", c.enemies.Select(e => $"{e.pos.x},{e.pos.y},{(int)e.state},{(int)e.awareness}"));

        static Result Finish(Carriage c, int tried)
        {
            var r = new Result
            {
                won = true, turns = c.Round, hp = c.hp, tried = tried,
                killed = c.enemies.Count(e => e.state == EnemyState.Dead),
                broken = c.enemies.Count(e => e.state == EnemyState.Broken || e.state == EnemyState.Fled),
            };
            // Your commands, grouped by turn (the enemies' and the turn markers left out).
            var turn = new List<string>();
            int n = 1;
            foreach (var cmd in c.history)
            {
                if (cmd == "start") { r.line.Add($"Turn {n++}: {(turn.Count > 0 ? string.Join("; ", turn) : "wait")}"); turn.Clear(); }
                else if (cmd != "end" && !cmd.StartsWith("enemy ")) turn.Add(cmd);
            }
            r.line.Add($"Turn {n}: {string.Join("; ", turn)}");
            return r;
        }

        // Every demo car with your full health and the given seed; one line each, plus the winning line.
        public static List<string> SolveDemo(Tuning t, int seed = 1)
        {
            var report = new List<string>();
            for (int i = 0; i < Train.DemoCars; i++)
            {
                var car = Train.Demo(i);
                var r = Solve(car, t, seed, t.maxHp);
                report.Add(r.Summary($"Car {i + 1} {car.name}"));
                foreach (var line in r.line) report.Add("    " + line);
            }
            return report;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Rail Tracer/Solve Demo Cars")]
        static void Menu()
        {
            var t = new Tuning();
            var table = UnityEngine.Resources.Load<SkillTable>("SkillTable");
            if (table && table.skills.Length == 8) t.skills = table.skills;
            var game = UnityEngine.Object.FindAnyObjectByType<Game>();
            if (game != null) { t = game.tuning; if (table && table.skills.Length == 8) t.skills = table.skills; }
            UnityEditor.EditorUtility.DisplayProgressBar("Rail Tracer", "Solving the demo cars (this can take a minute)...", 0.5f);
            try { UnityEngine.Debug.Log("Rail Tracer, level solver (seed 1):\n" + string.Join("\n", SolveDemo(t))); }
            finally { UnityEditor.EditorUtility.ClearProgressBar(); }
        }
#endif
    }
}
