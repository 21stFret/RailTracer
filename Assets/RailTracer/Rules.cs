using System;
using System.Collections.Generic;
using System.Linq;

// Pure game rules, no UnityEngine. Game.cs draws it; Editor/RulesCheck.cs tests it.
namespace RailTracer
{
    public enum EnemyState { Active, Dead, Broken, Fled }
    public enum Stage { Calm, Shaken, Panicked, Broken }
    // Unaware: holstered, patrols or guards. Searching: gun out, goes to what set it off, then searches nearby.
    // Startled: loses the next turn. Combat: gun aimed, sees you.
    public enum Awareness { Unaware, Searching, Startled, Combat }
    // How a searcher hunts: Go to the spot, Sweep (look around) the tiles round it, Look around from where it stands
    // (a shout or gunfire it heard), or follow a blood Trail.
    public enum Hunt { Go, Sweep, Look, Trail }
    // What a searcher is investigating (the hover label and the log).
    public enum Clue { Noise, Blood, Body, Movement }
    public enum Panic { Freeze, WildFire }
    public enum Act { QuietKill, BrutalKill, RailGrab }
    // Slot order = hotkey order (1..8).
    public enum SkillId { Drag, Leap, Hop, Stealth, Brutal, Rail, Throw, Dread }

    // One row of the skill table (Resources/SkillTable): legend to unlock, legend to be Bloodied (-1 = never;
    // equal to unlock = unlocks already bloodied), cooldown in turns, and the HUD text.
    [Serializable]
    public class SkillDef
    {
        public string name, shortName, description, upgrade;
        public int unlock, bloodied = -1, cooldown;

        public static SkillDef[] Defaults() => new[]
        {
            new SkillDef { name = "Grab and drag", shortName = "Drag", description = "Grab a body for free. Drag it at half speed." },
            new SkillDef { name = "Leap", shortName = "Leap", bloodied = 70, cooldown = 3, description = "Leap 3 tiles over anything but walls. Startles anyone who has never seen you. 3-turn cooldown.", upgrade = "Double leap range." },
            new SkillDef { name = "Window hop", shortName = "Hop", bloodied = 20, description = "Out one window, in through the next. Unseen outside.", upgrade = "Hop without using your move." },
            new SkillDef { name = "Stealth kill", shortName = "Stealth", bloodied = 40, description = "Kill anyone who can't see you: pulled onto your tile, you stay in cover.", upgrade = "Snatch: yank an enemy 2 tiles away onto your tile, over seats." },
            new SkillDef { name = "Brutal kill", shortName = "Brutal", unlock = 10, bloodied = 50, cooldown = 2, description = "Butcher them where everyone can see. Uses up your moves.", upgrade = "A brutal kill refreshes your leap." },
            new SkillDef { name = "Rail grab", shortName = "Rail", unlock = 30, bloodied = 60, cooldown = 2, description = "Hold an enemy against the passing track.", upgrade = "Bigger terror, more blood." },
            new SkillDef { name = "Demon throw", shortName = "Throw", unlock = 80, bloodied = 80, cooldown = 3, description = "Hurl a held body. Blood and terror where it lands." },
            new SkillDef { name = "Dread", shortName = "Dread", unlock = 90, bloodied = 90, description = "Passive. Anyone who sees you is at least Shaken." },
        };
    }
    public enum Intent { None, Hold, Return, Patrol, Shoot, Hesitate, Investigate, Follow, Charge, Melee, Search, Startled, Freeze, Flee, WildFire }

    // Every tuning knob in one place, editable in the Inspector on the Game component.
    [Serializable]
    public class Tuning
    {
        public int moveRange = 4, enemyMoveRange = 3, patrolMoveRange = 2;
        public int crouchMoveCost = 2; // per tile while crouched or dragging a body
        // Cones: basic gunmen see forward and the two forward diagonals; tough ones also both sides, further.
        // Never changes with awareness.
        public int guardSight = 7, heavySight = 8;
        public int searchNoticeRange = 1; // the close look: a searcher spots you this close inside its cone, cover or not
        // Noise radius in tiles (square, so 1 = the 8 tiles round it); any enemy inside hears it. 0 = silent.
        // Gunfire is louder than all of these: it alerts the whole carriage.
        public int quietNoise = 1, brutalHeard = 8, railHeard = 10, shoutNoise = 6;
        public int shakenAt = 30, panickedAt = 60, brokenAt = 100;
        public int smallTerror = 10, mediumTerror = 25, largeTerror = 45;
        public int bodyFearSmall = 10, bodyFearLarge = 45; // finding a stealth kill / a rail-grab or thrown body (brutal or shot: mediumTerror)
        public int unseenTerror = 35; // a body dragged or an enemy snatched away by something unseen: less than a kill seen
        public float hitChance = 0.85f, shakenHitChance = 0.35f, wildHitChance = 0.3f, meleeHitChance = 0.9f;
        public float halfCoverHitMult = 0.75f;
        public float leapHitPenalty = 0.25f; // off the hit chance of shots at a leaping player
        public int shotDamage = 15, maxHp = 30, maxLegend = 100;
        public int quietLegend = 10, brutalLegend = 30, railLegend = 25; // the legend grows from kills only
        // Skill unlocks, Bloodied points and cooldowns: the SkillTable asset (Game copies it in at Build).
        [NonSerialized] public SkillDef[] skills = SkillDef.Defaults();
        public int leapRange = 3, throwDistance = 5;
        public int leapTerror = 40; // terror per 100 legend for whoever sees you land
        public float railBoost = 1.5f;
        public float shakenHesitate = 0.4f;
        public float panicFreezeChance = 0.5f; // a Panicked guard's panic turn: freeze, else wild fire
        public float brokenFleeChance = 0.5f;  // on breaking: flee to the next carriage, else kneel and beg
        public int searchTurns = 3, searchRadius = 2; // a searcher sweeps the tiles this close to the spot for this long
        public int guardTurnInterval = 2; // a stationary guard turns every this many turns
    }

    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int x, y;
        public Cell(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Cell o) => x == o.x && y == o.y;
        public override bool Equals(object o) => o is Cell c && Equals(c);
        public override int GetHashCode() => x * 397 ^ y;
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => $"({x},{y})";
    }

    public class Enemy
    {
        public int id, faction, terror, searchLeft; // ponytail: faction unused since families were dropped; remove with the Spawn arg if they stay gone
        public bool tough; // wider, longer cone; closes in to melee; can't be killed until Shaken
        public Cell pos, facing; // facing is always one of the four directions
        public Cell post;        // where a guard stands (its spawn tile); it goes back there after a search
        public EnemyState state;
        public Awareness awareness;
        public Hunt hunt;
        public Clue clue;        // what a searcher is investigating
        public string name;      // a 1930s name for the log and the hover label (Carriage.NameEnemies)
        public bool hasSeen;     // ever glimpsed the player this level (the eye icon); never resets
        public Cell? sawYouAt;   // where it last saw you; cleared when it stops hunting (the last-seen ghost)
        public Cell? lastKnown;  // what they're hunting: where they saw, heard or found something
        public List<Cell> route; // patrollers: waypoints walked in a loop; null = stationary guard
        public int routeIndex, lookIndex, guardTimer;
        public List<Cell> looks; // a guard's facings, turned through every few turns
        public Stain trail; public int trailIndex; // the drag trail being followed
        public readonly HashSet<Body> found = new HashSet<Body>();
        public readonly HashSet<Stain> reacted = new HashSet<Stain>();
        // Pre-rolled each player turn so the intent is visible.
        public Panic panic;
        public bool hesitate;
        public bool panicNext; // a Panicked guard: its next turn is a panic turn (they alternate)
        public bool berserk;   // a Panicked heavy: charges you every turn, for good
        public bool fleeing;   // Broken and running for the next carriage (else begging)
        public Cell sweepLook; public double sweepPick; // a sweep's next look and destination, rolled ahead so the arrow is true
        public bool reactionReady; // one reaction shot per player turn
        public int spottedRound = -1; // the round its "spotted you" was last logged
        public List<Cell> walked;  // tiles of the last move, in order, for the view to follow
        public bool InPlay => state == EnemyState.Active;
    }

    // How the body was made: finding it frightens by how gory it is (Carriage.BodyFear).
    public enum BodyKind { Stealth, Brutal, Rail, Thrown, Shot }

    public class Body { public Enemy who; public Cell pos; public bool hung; public BodyKind kind; } // hung: out of a window (rail grab), can't be grabbed

    // Blood on the floor: one per kill or throw, or one growing trail per drag (tiles oldest to newest).
    public class Stain { public List<Cell> tiles = new List<Cell>(); public bool trail; }

    public class Carriage
    {
        static readonly Cell[] Dirs = { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) };

        public readonly Tuning t;
        public readonly int W, H;
        readonly bool[,] seat, solid, blankWall, cage, wall; // blankWall: a window-row tile with plain wall behind it, no window
        // The walk-through car: no enemies, no turns, no move costs (it's never won or lost).
        public bool freeWalk;
        readonly Random rng;

        public Cell player;
        public int hp, legend;
        public int hitsTaken; // for effects: goes up on every hit, shielded or not
        public List<Cell> walked; // tiles of the last move, in order, for the view to follow
        public int movesLeft; // spent per tile walked, double while crouched or dragging
        public bool acted, turnOver, leaped, hopped, crouched;
        public int leapWait, railWait, throwWait, brutalWait; // cooldowns: turns until usable again
        public Body held;   // body being dragged
        Stain dragTrail;
        public readonly List<Body> bodies = new List<Body>();
        public readonly List<Stain> stains = new List<Stain>();
        bool spotted, noUndo; // someone noticed you, or you moved a body, this turn
        public bool UndoLocked => acted || spotted || noUndo; // the HUD's padlock
        Cell turnStart;
        int leapWaitAtStart;
        public readonly List<Enemy> enemies = new List<Enemy>();
        public readonly List<string> log = new List<string>();
        public readonly List<Enemy> shotsFired = new List<Enemy>(); // every shot at you, in order, for the view to play (it clears them)
        int round;                                                  // player turns started, so "spotted you" logs once a round

        // Map: '.' floor, 'S' seat (half cover), 'L' luggage / 'W' wall (full cover), 'P' player start.
        // Edge rows are windows, middle-row ends are doors; on the edge rows '_' (floor) and 'l' (luggage) have a
        // plain wall behind them instead of a window. Dining car and baggage hold: 'T' table, 'O' round table,
        // 'D' dining cart, 'B' bar, 'M' mail sack (seat rules); 'X' crate ('x' with plain wall behind; luggage rules); 'C' the safe's cage (blocks
        // walking and leaps like a wall, but you can see through it).
        public Carriage(string[] map, Tuning t, int seed)
        {
            this.t = t;
            H = map.Length;
            W = map[0].Length;
            seat = new bool[W, H];
            solid = new bool[W, H];
            blankWall = new bool[W, H];
            cage = new bool[W, H];
            wall = new bool[W, H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    char ch = map[y][x];
                    seat[x, y] = "STBMOD".IndexOf(ch) >= 0;
                    solid[x, y] = "LlWXx".IndexOf(ch) >= 0;
                    blankWall[x, y] = "_lxB".IndexOf(ch) >= 0; // a bar stands against plain wall
                    cage[x, y] = ch == 'C';
                    wall[x, y] = ch == 'W';
                    if (ch == 'P') player = new Cell(x, y);
                }
            // Partitions: a column walled off but for one doorway (the train draws the wall on the joint after it).
            for (int x = 0; x < W; x++)
            {
                int walls = 0, door = -1;
                for (int y = 0; y < H; y++) { if (wall[x, y]) walls++; else door = y; }
                if (H >= 3 && walls == H - 1) partitions.Add((x, door));
            }
            hp = t.maxHp;
            rng = new Random(seed);
            StartPlayerTurn();
        }

        // route: patrol waypoints (walked in a loop). Without one the enemy guards, turning between its facing and
        // `looks` (default: its facing and the opposite way).
        public Enemy Spawn(int x, int y, int fx, int fy, int faction, bool tough = false, List<Cell> route = null, List<Cell> looks = null)
        {
            var facing = Cardinal(fx, fy, new Cell(1, 0));
            var e = new Enemy
            {
                id = enemies.Count + 1, faction = faction, tough = tough, pos = new Cell(x, y), post = new Cell(x, y), facing = facing,
                reactionReady = true, route = route != null && route.Count > 0 ? route : null,
                looks = looks ?? new List<Cell> { facing, new Cell(-facing.x, -facing.y) },
            };
            enemies.Add(e);
            return e;
        }

        public string Name(Enemy e) => e.name ?? $"{(e.tough ? "Brute" : "Gunman")} {e.id}";

        // Mob families, hired guns and drifters you'd meet on a 1930s express.
        public static readonly string[] NamePool =
        {
            "Mazio Fratelli", "Sal Bruno", "Carmine Lucchese", "Vito Rizzo", "Enzo Morello", "Dom Gallo",
            "Frankie Doyle", "Mickey Flanagan", "Tommy Rourke", "Eddie Malone", "Jack Kearney", "Pat Dunne",
            "Lou Kaminsky", "Benny Roth", "Abe Greenbaum", "Hank Bauer", "Otto Klein", "Karl Weber",
            "Ivan Sokolov", "Yuri Petrov", "Gus Petrakis", "Ray Callahan", "Joe Mercer", "Wes Pruitt",
        };

        // A name each from the pool, no repeats in the carriage (numbered fallbacks past 24).
        public void NameEnemies()
        {
            var pool = NamePool.OrderBy(_ => rng.Next()).ToList();
            for (int i = 0; i < enemies.Count; i++) enemies[i].name = i < pool.Count ? pool[i] : null;
        }

        // The last place anyone saw you, while someone who saw you there is still hunting (the last-seen ghost).
        public Cell? LastSeen
        {
            get
            {
                var hunters = enemies.Where(e => e.InPlay && e.awareness != Awareness.Unaware && e.sawYouAt.HasValue).ToList();
                return hunters.Count > 0 && lastSeen.HasValue && hunters.Exists(e => e.sawYouAt == lastSeen) ? lastSeen : null;
            }
        }
        Cell? lastSeen;
        void Log(string s) => log.Add(s);

        // ---- Geometry ----

        public bool InBounds(Cell c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H;
        public bool IsSeat(Cell c) => InBounds(c) && seat[c.x, c.y];
        public bool IsSolid(Cell c) => InBounds(c) && solid[c.x, c.y];
        public bool IsCage(Cell c) => InBounds(c) && cage[c.x, c.y];
        bool Tall(Cell c) => IsSolid(c) || IsCage(c); // stops snatches (the cage you can still see through)
        bool StopsLeap(Cell c) => InBounds(c) && (wall[c.x, c.y] || cage[c.x, c.y]); // a leap flies over everything else
        public bool Blocked(Cell c) => !InBounds(c) || seat[c.x, c.y] || solid[c.x, c.y] || cage[c.x, c.y];
        public bool IsWindow(Cell c) => !Blocked(c) && (c.y == 0 || c.y == H - 1) && !blankWall[c.x, c.y];
        public bool BlankWall(Cell c) => InBounds(c) && blankWall[c.x, c.y];
        static int Dist2(Cell a, Cell b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
        static int Cheb(Cell a, Cell b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));
        static int Manhattan(Cell a, Cell b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
        static bool Adjacent(Cell a, Cell b) => Manhattan(a, b) == 1;
        static Cell Mid(Cell a, Cell b) => new Cell((a.x + b.x) / 2, (a.y + b.y) / 2);

        int Sight(Enemy e) => e.tough ? t.heavySight : t.guardSight;

        public Enemy EnemyAt(Cell c) => enemies.Find(e => e.pos == c && (e.state == EnemyState.Active || e.state == EnemyState.Broken));
        public Body BodyAt(Cell c) => bodies.Find(b => b.pos == c);
        bool Occupied(Cell c) => c == player || EnemyAt(c) != null;
        public bool IsBloody(Cell c) => stains.Exists(s => s.tiles.Contains(c));

        readonly List<(int x, int door)> partitions = new List<(int, int)>();
        const double DoorHalf = 0.45; // half the partition doorway's width (1 m), less its frame

        // Bresenham. Full cover always blocks; seats only when told to (something low). Units never block. A line
        // through a partition must pass its doorway: where it crosses the wall's plane (on the joint after the
        // partition's tiles) it has to be within the opening, so from a steep angle you see barely a tile through.
        public bool LineOfSight(Cell a, Cell b, bool seatsBlock)
        {
            foreach (var (px, door) in partitions)
            {
                double plane = px + 0.5;
                if ((a.x - plane) * (b.x - plane) >= 0) continue; // both on one side
                double k = (plane - a.x) / (b.x - a.x), yc = a.y + k * (b.y - a.y);
                if (Math.Abs(yc - door) > DoorHalf) return false;
            }
            int dx = Math.Abs(b.x - a.x), dy = -Math.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx + dy;
            int x = a.x, y = a.y;
            while (true)
            {
                if (x == b.x && y == b.y) return true;
                if ((x != a.x || y != a.y) && (solid[x, y] || (seatsBlock && seat[x, y]))) return false;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        // Tiles on the straight line from a to b, a excluded.
        static List<Cell> Line(Cell a, Cell b)
        {
            var tiles = new List<Cell>();
            int dx = Math.Abs(b.x - a.x), dy = -Math.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx + dy;
            int x = a.x, y = a.y;
            while (x != b.x || y != b.y)
            {
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
                tiles.Add(new Cell(x, y));
            }
            return tiles;
        }

        // Inside the enemy's cone (ignoring walls): forward and the forward diagonals at full range, plus close
        // peripheral vision (one forward, two to the side); heavies also see the first 2 tiles straight out to each side.
        public bool InCone(Enemy e, Cell c)
        {
            int dx = c.x - e.pos.x, dy = c.y - e.pos.y;
            if (dx == 0 && dy == 0) return true;
            int range = Sight(e);
            if (dx * dx + dy * dy > range * range) return false;
            int fwd = dx * e.facing.x + dy * e.facing.y, side = Math.Abs(dx * e.facing.y - dy * e.facing.x);
            return (fwd > 0 && (side <= fwd || (fwd == 1 && side == 2))) || (e.tough && fwd == 0 && side <= 2);
        }

        // `low` = on the floor or crouched: hidden by half cover on the viewer's side (Covered), except from a
        // searcher's close look (any tile next to it inside its cone: a guard's 3 in front, a heavy's 5 with the sides).
        public bool Sees(Enemy e, Cell c, bool low)
        {
            if (!e.InPlay || !InCone(e, c)) return false;
            if (e.awareness == Awareness.Searching && Cheb(e.pos, c) <= t.searchNoticeRange) return true;
            if (!LineOfSight(e.pos, c, false) || Dist2(e.pos, c) > SightThrough(e, c) * SightThrough(e, c)) return false;
            return !(low && Covered(c, e.pos));
        }

        // Half cover by distance: a seat on the line towards the viewer, right next to `c` if the viewer is 3 tiles
        // away or less, within 2 tiles of it if further (either way the straight line is drawn, as they can differ).
        public bool Covered(Cell c, Cell from)
        {
            int d = Cheb(c, from);
            if (d <= 1) return false;
            int reach = d <= 3 ? 1 : 2;
            var to = Line(c, from);
            var back = Line(from, c);
            for (int i = 0; i < reach && i < d - 1; i++)
                if (IsSeat(to[i]) || IsSeat(back[back.Count - 2 - i])) return true;
            return false;
        }

        // Crowds give cover: each standing enemy on the line halves the sight left beyond them (whole tiles, rounded
        // down). Bodies and kneeling Broken enemies don't count. E.g. 8 tiles of sight, someone 2 away: 2 + 6/2 = 5.
        public double SightThrough(Enemy e, Cell c)
        {
            double range = Sight(e);
            foreach (var tile in Line(e.pos, c))
            {
                if (tile == c) break;
                if (!enemies.Exists(o => o != e && o.InPlay && o.pos == tile)) continue;
                double d = Math.Sqrt(Dist2(e.pos, tile));
                if (d > range) break;
                range = d + Math.Floor((range - d) / 2);
            }
            return range;
        }

        // You (or something at your height) on that tile, in your current stance.
        public bool CanSee(Enemy e, Cell c) => Sees(e, c, crouched);
        public bool SeenByAnyone(Cell c) => enemies.Exists(e => CanSee(e, c));

        // A noise made at `origin` with this radius reaches the enemy (through seats and walls).
        public bool Hears(Enemy e, Cell origin, int radius) => e.InPlay && radius > 0 && Cheb(e.pos, origin) <= radius;
        public int NoiseOf(Act a) => a == Act.QuietKill ? t.quietNoise : a == Act.BrutalKill ? t.brutalHeard : t.railHeard;

        // Half cover: a seat (or anything solid) beside the target on the shooter's side.
        public bool HalfCovered(Cell target, Cell from)
        {
            int sx = Math.Sign(from.x - target.x), sy = Math.Sign(from.y - target.y);
            return (sx != 0 && InBounds(new Cell(target.x + sx, target.y)) && Blocked(new Cell(target.x + sx, target.y)))
                || (sy != 0 && InBounds(new Cell(target.x, target.y + sy)) && Blocked(new Cell(target.x, target.y + sy)));
        }

        Dictionary<Cell, Cell> Bfs(Cell from, int maxSteps, Func<Cell, bool> passable)
        {
            var parent = new Dictionary<Cell, Cell> { [from] = from };
            var dist = new Dictionary<Cell, int> { [from] = 0 };
            var q = new Queue<Cell>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (dist[c] == maxSteps) continue;
                foreach (var d in Dirs)
                {
                    var n = new Cell(c.x + d.x, c.y + d.y);
                    if (!InBounds(n) || parent.ContainsKey(n) || !passable(n)) continue;
                    parent[n] = c;
                    dist[n] = dist[c] + 1;
                    q.Enqueue(n);
                }
            }
            return parent;
        }

        // Steps from `from` toward `goal`, stopping short if the goal is occupied. Null if unreachable.
        List<Cell> PathTo(Cell from, Cell goal)
        {
            if (from == goal) return new List<Cell>();
            var parent = Bfs(from, 99, n => n == goal || (!Blocked(n) && !Occupied(n)));
            if (!parent.ContainsKey(goal)) return null;
            var path = new List<Cell>();
            for (var c = goal; c != from; c = parent[c]) path.Add(c);
            path.Reverse();
            if (Occupied(goal)) path.RemoveAt(path.Count - 1);
            return path;
        }

        Cell? StepToward(Enemy e, Cell goal)
        {
            var p = PathTo(e.pos, goal);
            return p != null && p.Count > 0 ? p[0] : (Cell?)null;
        }

        static Cell Cardinal(int dx, int dy, Cell keep)
        {
            if (dx == 0 && dy == 0) return keep;
            if (Math.Abs(dx) == Math.Abs(dy) && (keep.x == Math.Sign(dx) && keep.y == 0 || keep.y == Math.Sign(dy) && keep.x == 0)) return keep;
            return Math.Abs(dx) >= Math.Abs(dy) ? new Cell(Math.Sign(dx), 0) : new Cell(0, Math.Sign(dy));
        }

        static void Face(Enemy e, Cell c) => e.facing = Cardinal(c.x - e.pos.x, c.y - e.pos.y, e.facing);

        // Walks up to `steps` tiles, asking `next` for each one (null = stop), noticing things on the way:
        // seeing you or finding a body ends the move there.
        void Walk(Enemy e, int steps, Func<Cell?> next)
        {
            e.walked = new List<Cell>();
            for (int i = 0; i < steps; i++)
            {
                var n = next();
                if (n == null || Occupied(n.Value)) break;
                Face(e, n.Value);
                e.pos = n.Value;
                e.walked.Add(n.Value);
                if (e.awareness != Awareness.Combat && CanSee(e, player)) { Fight(e); break; }
                if (e.awareness != Awareness.Combat && Notice(e)) break;
            }
        }

        // ---- Terror and awareness ----

        public Stage StageOf(Enemy e) =>
            e.state == EnemyState.Broken ? Stage.Broken
            : e.terror >= t.panickedAt ? Stage.Panicked
            : e.terror >= t.shakenAt ? Stage.Shaken
            : Stage.Calm;

        void AddTerror(Enemy e, int amount)
        {
            if (!e.InPlay) return;
            var before = StageOf(e);
            e.terror = Math.Min(t.brokenAt, e.terror + amount);
            if (e.terror >= t.brokenAt) { e.state = EnemyState.Broken; e.lastKnown = null; }
            var after = StageOf(e);
            if (after == before) return;
            if (after == Stage.Broken)
            {
                // One roll: run for the next carriage (if there's a way) or kneel and beg.
                e.fleeing = rng.NextDouble() < t.brokenFleeChance && DoorPath(e) != null;
                Log(e.fleeing ? $"{Name(e)} broke: drops the gun and runs." : $"{Name(e)} broke: drops the gun and begs.");
                return;
            }
            Log($"{Name(e)} is {after}.");
            if (after != Stage.Panicked) return;
            if (!e.tough) { e.panicNext = true; return; } // the next turn is a panic turn
            e.berserk = true;
            e.awareness = Awareness.Combat;
            Log($"{Name(e)} goes berserk!");
        }

        // Gun out and hunting `at`. Never calms a fighter or wakes the startled early.
        void Search(Enemy e, Cell at, Hunt how, Clue clue)
        {
            if (e.awareness == Awareness.Combat || e.awareness == Awareness.Startled) return;
            Searching(e, clue);
            e.lastKnown = at;
            e.hunt = how;
            e.searchLeft = t.searchTurns;
            e.trail = null;
        }

        // Now searching, for this reason: logged whenever that changes.
        void Searching(Enemy e, Clue clue)
        {
            if (e.awareness == Awareness.Searching && e.clue == clue) return;
            e.awareness = Awareness.Searching;
            e.clue = clue;
            Log($"{Name(e)} is searching: {clue.ToString().ToLower()}.");
        }

        // Any sighting of you: remembered for good, and with Dread at least Shaken. Logged once a round, unless
        // they're already fighting you (they had you in sight).
        void SawPlayer(Enemy e, Cell at)
        {
            if (e.awareness != Awareness.Combat && e.spottedRound != round) Log($"{Name(e)} spotted you.");
            e.spottedRound = round;
            e.hasSeen = true;
            e.lastKnown = at;
            e.sawYouAt = lastSeen = at;
            if (Has(SkillId.Dread) && e.terror < t.shakenAt) AddTerror(e, t.shakenAt - e.terror);
        }

        // Anyone who sees you is in combat (the chip turns red), the moment they see you; `at` = where (default: where
        // you are). The startled stay startled until their own turn. The first sighting also shouts.
        void Fight(Enemy e, Cell? at = null)
        {
            bool fresh = e.awareness == Awareness.Unaware;
            SawPlayer(e, at ?? player);
            if (e.awareness == Awareness.Startled || e.awareness == Awareness.Combat) return;
            e.awareness = Awareness.Combat;
            Log($"{Name(e)} is fighting you!");
            if (fresh) Shout(e, at ?? player);
        }

        // Someone who has never seen you, seeing you burst into view (leap landing, brutal kill): loses their next turn.
        void Startle(Enemy e)
        {
            SawPlayer(e, player);
            if (!e.InPlay) return;
            e.awareness = Awareness.Startled;
            Log($"{Name(e)} is startled!");
        }

        // The shouter is the one who comes looking (it's fighting you). Unaware listeners within the shout's radius
        // turn to face it and look around from where they stand. ponytail: no cascade (listeners don't shout on).
        void Shout(Enemy spotter, Cell seenAt)
        {
            bool heard = false;
            foreach (var o in enemies)
            {
                if (o == spotter || !o.InPlay || o.awareness != Awareness.Unaware || !Hears(o, spotter.pos, t.shoutNoise)) continue;
                Look(o, spotter.pos, Clue.Movement);
                heard = true;
            }
            if (heard) Log($"{Name(spotter)} shouts!");
        }

        // Gunfire: everyone not already fighting turns towards it and looks around where they stand (the carriage
        // doesn't pile in).
        void AlertAll()
        {
            foreach (var e in enemies)
                if (e.InPlay) Look(e, player, Clue.Noise);
        }

        // Searching from where they stand: face `at`, look around for searchTurns, then stand down.
        void Look(Enemy e, Cell at, Clue clue)
        {
            Search(e, at, Hunt.Look, clue);
            if (e.awareness == Awareness.Searching && e.hunt == Hunt.Look) Face(e, at);
        }

        // What `e` notices besides you: a body (stops them for the turn: true) or blood (they go to it).
        bool Notice(Enemy e)
        {
            foreach (var b in bodies)
            {
                if (e.found.Contains(b) || !Sees(e, b.pos, true)) continue;
                e.found.Add(b);
                Log($"{Name(e)} found {Name(b.who)}'s body ({b.kind.ToString().ToLower()}).");
                AddTerror(e, BodyFear(b));
                if (!e.InPlay) return true;
                if (e.awareness != Awareness.Combat) Searching(e, Clue.Body);
                if (e.awareness == Awareness.Searching) { e.lastKnown = b.pos; e.hunt = Hunt.Sweep; e.searchLeft = t.searchTurns; e.trail = null; }
                return true;
            }
            if (e.awareness == Awareness.Combat) return false; // fighters ignore blood
            foreach (var s in stains)
            {
                if (e.reacted.Contains(s)) continue;
                int best = -1;
                for (int i = 0; i < s.tiles.Count; i++)
                    if (Sees(e, s.tiles[i], true) && (best < 0 || Dist2(e.pos, s.tiles[i]) < Dist2(e.pos, s.tiles[best]))) best = i;
                if (best < 0) continue;
                e.reacted.Add(s);
                if (s.trail) FollowTrail(e, s, best, Clue.Blood);
                else Search(e, s.tiles[best], Hunt.Go, Clue.Blood);
            }
            return false;
        }

        public int BodyFear(Body b) =>
            b.kind == BodyKind.Stealth ? t.bodyFearSmall : b.kind == BodyKind.Rail || b.kind == BodyKind.Thrown ? t.bodyFearLarge : t.mediumTerror;

        void FollowTrail(Enemy e, Stain s, int from, Clue clue)
        {
            Search(e, s.tiles[from], Hunt.Trail, clue);
            if (e.awareness != Awareness.Searching) return;
            e.trail = s;
            e.trailIndex = from;
        }

        // ---- Skills and the legend ----

        public SkillDef Def(SkillId s) => t.skills[(int)s];
        public bool Has(SkillId s) => legend >= Def(s).unlock;
        public bool Bloodied(SkillId s) => Def(s).bloodied >= 0 && legend >= Def(s).bloodied;
        public int Wait(SkillId s) => s == SkillId.Leap ? leapWait : s == SkillId.Rail ? railWait : s == SkillId.Throw ? throwWait : s == SkillId.Brutal ? brutalWait : 0;

        // Why an unlocked skill can't be used right now, in a couple of words for the HUD (null if it can be).
        public string WhyNot(SkillId s)
        {
            if (!Has(s) || Usable(s)) return null;
            if (s == SkillId.Hop)
            {
                if (hopped) return "Hopped already";
                if (held != null) return "Drop the body";
                if (!IsWindow(player)) return "Not at a window";
                if (!Bloodied(SkillId.Hop) && movesLeft < t.moveRange) return "Out of moves";
                return "No target";
            }
            if (s == SkillId.Leap)
            {
                if (held != null) return "Drop the body";
                if (movesLeft < t.moveRange) return "Out of moves";
                return "No target";
            }
            var kill = s == SkillId.Stealth ? Act.QuietKill : s == SkillId.Rail ? Act.RailGrab : s == SkillId.Brutal ? Act.BrutalKill : (Act?)null;
            if (kill == null && s != SkillId.Throw) return "No target";
            if (acted || turnOver) return "No action";
            if (kill == null) return "No target";
            if (held != null) return "Drop the body";
            var why = enemies.Select(e => CantAct(kill.Value, e)).ToList();
            if (why.Contains("Too tough: shake them first.")) return "Shake them first";
            if (why.Contains("They can see you.")) return "They can see you";
            if (why.Contains("Rail grab needs you or them at a window.")) return "Needs a window";
            return "No target";
        }

        // Unlocked and usable right now (somewhere to land, someone to kill, a body to grab...). Dread is passive: always.
        public bool Usable(SkillId s)
        {
            if (!Has(s)) return false;
            bool AnyTile(Func<Cell, bool> ok)
            {
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                        if (ok(new Cell(x, y))) return true;
                return false;
            }
            bool AnyTarget(Act a) => enemies.Exists(e => CantAct(a, e) == null);
            switch (s)
            {
                case SkillId.Drag: return CanMove && (held != null || CanGrab);
                case SkillId.Leap: return AnyTile(CanLeapTo);
                case SkillId.Hop: return AnyTile(CanHopTo);
                case SkillId.Stealth: return AnyTarget(Act.QuietKill);
                case SkillId.Rail: return AnyTarget(Act.RailGrab);
                case SkillId.Brutal: return AnyTarget(Act.BrutalKill);
                case SkillId.Throw: return AnyTile(CanThrowTo);
                default: return true;
            }
        }

        // The legend a kill gives (before the cap).
        public int LegendGain(Act a) => a == Act.QuietKill ? t.quietLegend : a == Act.RailGrab ? t.railLegend : t.brutalLegend;

        void GainLegend(int n)
        {
            int before = legend;
            legend = Math.Min(t.maxLegend, legend + n);
            foreach (var s in t.skills)
            {
                if (s.unlock > 0 && before < s.unlock && legend >= s.unlock) Log($"The legend grows: {s.name} unlocked.");
                if (s.bloodied > s.unlock && before < s.bloodied && legend >= s.bloodied) Log($"The legend grows: {s.name} bloodied. {s.upgrade}");
            }
        }

        // The legend is a shield: it soaks hits before human health, and only damage drains it.
        void Damage(int dmg)
        {
            hitsTaken++;
            int absorbed = Math.Min(legend, dmg);
            legend -= absorbed;
            hp = Math.Max(0, hp - (dmg - absorbed));
            Log($"Hit for {dmg}: legend -{absorbed}, health -{dmg - absorbed}.");
            if (hp == 0) Log("The Rail Tracer falls.");
        }

        // ---- Player movement ----

        int MoveCost => crouched || held != null ? t.crouchMoveCost : 1;
        bool CanMove => !turnOver && !Over;

        public HashSet<Cell> Reachable()
        {
            var r = new HashSet<Cell>();
            if (!CanMove) return r;
            foreach (var c in Bfs(player, movesLeft / MoveCost, PlayerPassable).Keys)
                if (c != player) r.Add(c);
            return r;
        }

        bool PlayerPassable(Cell n) => !Occupied(n) && !Blocked(n);

        // The shortest route there, and of the equally short ones the safest: fewest tiles under reaction fire,
        // then fewest tiles anyone sees.
        public List<Cell> PathFor(Cell c)
        {
            if (!Reachable().Contains(c)) return null;
            var watching = enemies.FindAll(WillReact);
            var dist = new Dictionary<Cell, int> { [player] = 0 };
            var danger = new Dictionary<Cell, int> { [player] = 0 };
            var parent = new Dictionary<Cell, Cell>();
            var q = new Queue<Cell>();
            q.Enqueue(player);
            int steps = movesLeft / MoveCost;
            while (q.Count > 0) // BFS layer by layer, keeping the least dangerous way into each tile
            {
                var at = q.Dequeue();
                if (dist[at] == steps) continue;
                foreach (var d in Dirs)
                {
                    var n = new Cell(at.x + d.x, at.y + d.y);
                    if (!InBounds(n) || !PlayerPassable(n)) continue;
                    int risk = danger[at] + Danger(n, watching);
                    if (!dist.ContainsKey(n)) { dist[n] = dist[at] + 1; danger[n] = risk; parent[n] = at; q.Enqueue(n); }
                    else if (dist[n] == dist[at] + 1 && risk < danger[n]) { danger[n] = risk; parent[n] = at; }
                }
            }
            var path = new List<Cell>();
            for (var p = c; p != player; p = parent[p]) path.Add(p);
            path.Reverse();
            return path;
        }

        // How bad it is to be seen on this tile as you are now: reaction fire counts far more than a glimpse.
        int Danger(Cell tile, List<Enemy> watching)
        {
            int risk = 0;
            foreach (var e in enemies)
                if (Sees(e, tile, crouched)) risk += watching.Contains(e) ? 100 : 1;
            return risk;
        }

        // The preview of a move or leap over `tiles` (the first is where you set off): per tile, whether anyone will
        // see you there and who (if anyone) fires on it. Each of `shooters` fires once, on the first tile it sees.
        public List<(Cell tile, bool seen, Enemy shooter)> Preview(List<Cell> tiles, bool low, List<Enemy> shooters)
        {
            var left = new List<Enemy>(shooters);
            var res = new List<(Cell, bool, Enemy)>();
            foreach (var tile in tiles)
            {
                var o = left.Find(e => Sees(e, tile, low));
                if (o != null) left.Remove(o);
                res.Add((tile, enemies.Exists(e => Sees(e, tile, low)), o));
            }
            return res;
        }

        // A walk's preview along PathFor(c), starting with your own tile (never shot at: you're leaving it).
        public List<(Cell tile, bool seen, Enemy shooter)> MovePreview(Cell c)
        {
            var path = PathFor(c);
            if (path == null) return null;
            var res = Preview(path, crouched, enemies.FindAll(e => WillReact(e) && e.reactionReady));
            res.Insert(0, (player, enemies.Exists(e => Sees(e, player, crouched)), null));
            return res;
        }

        // A leap's preview: your tile, then the arc and landing at head height.
        public List<(Cell tile, bool seen, Enemy shooter)> LeapPreview(Cell c)
        {
            var arc = LeapArc(c);
            var res = Preview(arc, false, LeapShooters(arc));
            res.Insert(0, (player, enemies.Exists(e => Sees(e, player, crouched)), null));
            return res;
        }

        // Overwatch: gun out (searching or fighting), not broken, shot unspent, and not about to freeze in panic.
        public bool WillReact(Enemy e) =>
            e.InPlay && e.reactionReady && (e.awareness == Awareness.Searching || e.awareness == Awareness.Combat)
            && !(PanicTurn(e) && e.panic == Panic.Freeze);

        // You are now on `tile` (seen as you stand or crouch): whoever sees it is in combat; `watching` enemies (those
        // already searching or fighting when you set off) fire.
        // True if you got shot at.
        bool StepInto(Cell tile, List<Enemy> watching, bool low)
        {
            bool interrupted = false;
            foreach (var e in enemies)
            {
                if (!Sees(e, tile, low)) continue;
                Fight(e, tile);
                spotted = true;
                if (Over || !watching.Contains(e) || !e.reactionReady) continue;
                e.reactionReady = false;
                Shoot(e, "reaction fire");
                interrupted = true;
            }
            return interrupted;
        }

        public bool Move(Cell c)
        {
            var path = PathFor(c);
            if (path == null) return false;
            var watching = enemies.FindAll(WillReact); // only enemies already aware when you set off
            walked = new List<Cell>();
            // Every tile walked counts; reaction fire stops you where it happens.
            foreach (var tile in path)
            {
                var left = player;
                player = tile;
                walked.Add(tile);
                movesLeft -= MoveCost;
                bool shot = StepInto(tile, watching, crouched);
                if (held != null) DragTo(left);
                if (shot) { Log("Your move is cut short."); break; }
            }
            foreach (var e in enemies)
                if (e.InPlay && e.lastKnown.HasValue && path.Contains(e.lastKnown.Value)) Face(e, e.lastKnown.Value);
            if (freeWalk) movesLeft = 999;
            return true;
        }

        public void ToggleCrouch()
        {
            if (!CanMove) return;
            crouched = !crouched;
            if (crouched) return;
            StepInto(player, enemies.FindAll(WillReact), false); // standing up over the seats
            foreach (var e in enemies)
                if (e.InPlay && e.lastKnown == player) Face(e, player);
        }

        // Leap: traversal or closing in, over anything but walls and the cage. Costs your whole move; travels at head height, so every tile
        // of the arc is seen as if you were standing, whatever your stance.
        public bool CanLeapTo(Cell c) =>
            CanMove && leapWait == 0 && held == null && movesLeft == t.moveRange && !Blocked(c) && !Occupied(c)
            && Dist2(player, c) <= LeapRange * LeapRange && Line(player, c).TrueForAll(n => n == c || !StopsLeap(n)) && ClearFlight(player, c);

        // Bloodied (70): twice as far.
        public int LeapRange => Bloodied(SkillId.Leap) ? t.leapRange * 2 : t.leapRange;

        // The leap flies straight from tile centre to tile centre, a body's width wide: over seats, luggage and
        // crates, but not through or across the corner of a wall or the cage.
        bool ClearFlight(Cell a, Cell b)
        {
            const double r = 0.12; // half the body width, in tiles
            int n = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(Dist2(a, b)) / 0.1));
            for (int i = 1; i < n; i++)
            {
                double k = (double)i / n, x = a.x + (b.x - a.x) * k, y = a.y + (b.y - a.y) * k;
                foreach (var dx in new[] { -r, r })
                    foreach (var dy in new[] { -r, r })
                        if (StopsLeap(new Cell((int)Math.Floor(x + dx + 0.5), (int)Math.Floor(y + dy + 0.5)))) return false;
            }
            return true;
        }

        // The tiles a leap to `c` crosses at head height: the arc and the landing (not the take-off: the shot as you
        // go would miss). The two straight lines
        // between the tiles can differ by a tile: it takes the one fewer eyes are on.
        public List<Cell> LeapArc(Cell c)
        {
            var watching = enemies.FindAll(WillReact);
            var arc = Line(player, c);
            var back = Line(c, player);
            back.Reverse();
            back.RemoveAt(0);
            back.Add(c);
            int Seen(List<Cell> a)
            {
                int risk = 0;
                foreach (var tile in a)
                    foreach (var e in enemies)
                        if (Sees(e, tile, false)) risk += watching.Contains(e) ? 100 : 1;
                return risk;
            }
            if (Seen(back) < Seen(arc)) arc = back;
            return arc;
        }

        // Who fires on a leap along `arc`: ready guns that see any of it, except anyone the landing startles.
        public List<Enemy> LeapShooters(List<Cell> arc)
        {
            var land = arc[arc.Count - 1];
            return enemies.FindAll(e => WillReact(e) && e.reactionReady && !(!e.hasSeen && Sees(e, land, false))
                && arc.Exists(tile => Sees(e, tile, false)));
        }

        public bool Leap(Cell c)
        {
            if (!CanLeapTo(c)) return false;
            var arc = LeapArc(c);
            var shooters = LeapShooters(arc);
            player = c;
            leaped = true;
            movesLeft = 0;
            leapWait = Def(SkillId.Leap).cooldown;
            Log("You leap.");
            foreach (var e in enemies)
            {
                bool landing = Sees(e, c, false);
                int last = arc.FindLastIndex(tile => Sees(e, tile, false)); // the last of you they saw
                if (last < 0) continue;
                spotted = true;
                bool fresh = !e.hasSeen; // only someone who's never seen you can be startled
                if (landing && fresh) Startle(e);
                else Fight(e, arc[last]);
                if (landing && e.InPlay) AddTerror(e, legend * t.leapTerror / 100);
            }
            // Watchers who weren't startled get their reaction shot at the landing (crossing their sight counts),
            // harder to land on a leaping target.
            foreach (var e in shooters)
                if (!Over && e.InPlay && e.awareness != Awareness.Startled)
                {
                    e.reactionReady = false;
                    Shoot(e, "reaction fire", true);
                }
            return true;
        }

        // Window hop: out of a booth's window, in through the next booth's on the same side. Unseen outside; the
        // landing is seen like any step. Costs your whole move until the legend upgrade. Bodies don't fit.
        public bool CanHopTo(Cell c)
        {
            if (!CanMove || hopped || held != null || !IsWindow(player) || !IsWindow(c) || c.y != player.y || Occupied(c)) return false; // once a turn
            if (!Bloodied(SkillId.Hop) && movesLeft < t.moveRange) return false;
            var mine = Booth(player);
            int dir = Math.Sign(c.x - player.x);
            var next = Booth(new Cell((dir > 0 ? mine.x1 : mine.x0) + dir, player.y), dir);
            return next.HasValue && c.x >= next.Value.x0 && c.x <= next.Value.x1;
        }

        // The run of window tiles containing (or, searching `dir`, first found from) `c` in its row.
        (int x0, int x1) Booth(Cell c) => Booth(c, 0).Value;
        (int x0, int x1)? Booth(Cell c, int dir)
        {
            int x = c.x;
            while (dir != 0 && x >= 0 && x < W && !IsWindow(new Cell(x, c.y))) x += dir;
            if (x < 0 || x >= W || !IsWindow(new Cell(x, c.y))) return null;
            int x0 = x, x1 = x;
            while (IsWindow(new Cell(x0 - 1, c.y))) x0--;
            while (IsWindow(new Cell(x1 + 1, c.y))) x1++;
            return (x0, x1);
        }

        public bool Hop(Cell c)
        {
            if (!CanHopTo(c)) return false;
            player = c;
            hopped = true;
            if (!Bloodied(SkillId.Hop)) movesLeft = 0;
            Log("You slip out of the window and in through the next.");
            StepInto(c, enemies.FindAll(WillReact), crouched);
            return true;
        }

        // ---- Bodies ----

        public bool CanGrab => CanMove && held == null && bodies.Exists(b => !b.hung && Cheb(b.pos, player) <= 1);

        public bool Grab()
        {
            if (!CanGrab) return false;
            held = bodies.Find(b => !b.hung && b.pos == player) ?? bodies.Find(b => !b.hung && Cheb(b.pos, player) == 1);
            dragTrail = new Stain { trail = true };
            dragTrail.tiles.Add(held.pos);
            stains.Add(dragTrail);
            noUndo = true;
            Log($"You grab {Name(held.who)}.");
            return true;
        }

        public void Drop()
        {
            if (held == null) return;
            Log($"You drop {Name(held.who)}.");
            held = null;
            dragTrail = null;
        }

        // The body follows like a snake: into the tile you just left, smearing the trail. Anyone who sees it move
        // without seeing you gets the big fright and follows it.
        void DragTo(Cell tile)
        {
            var from = held.pos;
            held.pos = tile;
            if (dragTrail.tiles[dragTrail.tiles.Count - 1] != tile) dragTrail.tiles.Add(tile);
            foreach (var e in enemies)
            {
                // Seeing it slide, into view or out of it (dragged out of sight still counts).
                if (e.reacted.Contains(dragTrail) || !(Sees(e, tile, true) || Sees(e, from, true)) || CanSee(e, player)) continue;
                Log($"{Name(e)} sees {Name(held.who)}'s body slide away!");
                AddTerror(e, t.unseenTerror);
                e.found.Add(held);
                e.reacted.Add(dragTrail);
                FollowTrail(e, dragTrail, dragTrail.tiles.Count - 1, Clue.Body);
            }
        }

        // Demon throw: a held body flies a few tiles. Blood where it lands; terror for all who see it land.
        public bool CanThrowTo(Cell c) =>
            CanMove && !acted && held != null && Has(SkillId.Throw) && throwWait == 0 && !Blocked(c) && c != player
            && Dist2(player, c) <= t.throwDistance * t.throwDistance && LineOfSight(player, c, false);

        public bool Throw(Cell c)
        {
            if (!CanThrowTo(c)) return false;
            var b = held;
            Drop();
            b.pos = c;
            b.kind = BodyKind.Thrown;
            acted = true;
            throwWait = Def(SkillId.Throw).cooldown;
            Log($"You hurl {Name(b.who)} across the carriage!");
            Bleed(c, true);
            foreach (var e in enemies)
            {
                if (!Sees(e, c, false)) continue;
                AddTerror(e, t.largeTerror);
                e.found.Add(b);
                Search(e, c, Hunt.Go, Clue.Body);
            }
            return true;
        }

        void Bleed(Cell c, bool wide)
        {
            var s = new Stain();
            s.tiles.Add(c);
            if (wide)
                foreach (var d in Dirs)
                {
                    var n = new Cell(c.x + d.x, c.y + d.y);
                    if (!Blocked(n)) s.tiles.Add(n);
                }
            stains.Add(s);
        }

        // ---- Turn ----

        // Undo is free until you act, are noticed, or move a body.
        public bool CanReset => !acted && !turnOver && !spotted && !noUndo && (movesLeft < t.moveRange || leaped || hopped);

        public void ResetTurn()
        {
            if (!CanReset) return;
            player = turnStart;
            movesLeft = t.moveRange;
            leaped = hopped = false;
            leapWait = leapWaitAtStart;
        }

        // ---- Kills ----

        public string CantAct(Act a, Enemy e)
        {
            if (Over) return "The fight is over."; // the level ends the moment nobody is left fighting
            if (acted || turnOver) return "Action already used.";
            if (e == null || (e.state != EnemyState.Active && e.state != EnemyState.Broken)) return "No target.";
            if (held != null) return "Drop the body first.";
            bool reach = Next(a, e.pos);
            if (!reach && a == Act.QuietKill && IsSnatch(e))
            {
                if (!Bloodied(SkillId.Stealth)) return $"Snatch unlocks at {Def(SkillId.Stealth).bloodied} legend.";
                var mid = Mid(player, e.pos);
                if (Tall(mid)) return "Full cover blocks the snatch.";
                if (EnemyAt(mid) != null) return "Someone is in the way.";
                reach = true;
            }
            if (!reach) return "Target must be next to you.";
            if (e.tough && e.InPlay && StageOf(e) < Stage.Shaken) return "Too tough: shake them first.";
            switch (a)
            {
                case Act.QuietKill:
                    if (e.InPlay && CanSee(e, player)) return "They can see you.";
                    break;
                case Act.BrutalKill:
                    if (!Has(SkillId.Brutal)) return $"Brutal kill unlocks at {Def(SkillId.Brutal).unlock} legend.";
                    if (brutalWait > 0) return $"Brutal kill ready in {brutalWait} turn(s).";
                    break;
                case Act.RailGrab:
                    if (!Has(SkillId.Rail)) return $"Rail grab unlocks at {Def(SkillId.Rail).unlock} legend.";
                    if (railWait > 0) return $"Rail grab ready in {railWait} turn(s).";
                    if (!IsWindow(e.pos) && !IsWindow(player)) return "Rail grab needs you or them at a window.";
                    break;
            }
            return null;
        }

        // Next to you: any of the 8 tiles round you, except that the stealth kill only reaches the diagonals once
        // it's bloodied (along with the snatch, 2 tiles straight out along a row or column).
        bool Next(Act a, Cell c)
        {
            bool diagonals = a != Act.QuietKill || Bloodied(SkillId.Stealth);
            return (diagonals ? Cheb(player, c) == 1 : Adjacent(player, c)) && LineOfSight(player, c, false);
        }

        // The tiles this kill can reach from where you stand (the grid's attack range).
        public bool InReach(Act a, Cell c) =>
            InBounds(c) && (Next(a, c) || (a == Act.QuietKill && Bloodied(SkillId.Stealth) && Manhattan(player, c) == 2
                && (player.x == c.x || player.y == c.y) && !Tall(Mid(player, c))));

        // Two tiles straight out along a row or column: the snatch's range.
        bool IsSnatch(Enemy e) => Manhattan(player, e.pos) == 2 && (player.x == e.pos.x || player.y == e.pos.y);

        // The window the rail grab hangs them out of: theirs if they're at one, else yours.
        Cell RailWindow(Enemy e) => IsWindow(e.pos) ? e.pos : player;

        // Where you and the body end up if this kill goes ahead (null if it can't): every stealth kill pulls the
        // victim onto your tile, the rail grab hangs them out of the window, the brutal kill carries you onto theirs.
        public (Cell player, Cell body)? KillOutcome(Act a, Enemy e)
        {
            if (CantAct(a, e) != null) return null;
            if (a == Act.BrutalKill) return (e.pos, e.pos);
            if (a == Act.RailGrab) return (player, RailWindow(e));
            return (player, player);
        }


        public bool Perform(Act a, Enemy e)
        {
            var why = CantAct(a, e);
            if (why != null) { Log(why); return false; }
            acted = true;
            var from = e.pos;
            bool snatch = a == Act.QuietKill && IsSnatch(e);
            if (a == Act.QuietKill) { if (snatch) Log($"You yank {Name(e)} across the seats."); e.pos = player; }
            if (a == Act.RailGrab) e.pos = RailWindow(e);
            var at = e.pos;
            var body = Kill(e, a);
            switch (a)
            {
                case Act.QuietKill:
                {
                    // Pulled onto your tile and killed there: you never move, so only your own tile decides whether
                    // you're seen. Anyone who sees just where they stood sees them vanish (a snatch: or the tile
                    // they were pulled through).
                    var mid = Mid(player, from);
                    Bleed(at, false);
                    foreach (var o in enemies)
                    {
                        if (!o.InPlay) continue;
                        if (CanSee(o, player))
                        {
                            Log($"{Name(o)} sees you kill {Name(e)}!");
                            AddTerror(o, t.mediumTerror);
                            o.found.Add(body);
                            if (o.InPlay) Fight(o);
                        }
                        else if (Sees(o, from, false) || (snatch && Sees(o, mid, false)))
                        {
                            Log($"{Name(o)} sees {Name(e)} vanish.");
                            AddTerror(o, snatch ? t.unseenTerror : t.smallTerror);
                            Search(o, from, Hunt.Go, Clue.Movement);
                        }
                        else if (Hears(o, at, t.quietNoise))
                        {
                            AddTerror(o, t.smallTerror);
                            Search(o, at, Hunt.Go, Clue.Noise);
                        }
                    }
                    GainLegend(t.quietLegend);
                    break;
                }
                case Act.RailGrab:
                {
                    // Hung out of the window, one leg hooked inside: blood on every tile within 1 of it.
                    bool boost = Bloodied(SkillId.Rail);
                    railWait = Def(SkillId.Rail).cooldown;
                    body.hung = true;
                    var s = new Stain();
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var n = new Cell(at.x + dx, at.y + dy);
                            if (!Blocked(n)) s.tiles.Add(n);
                        }
                    stains.Add(s);
                    Witness(at, body, boost ? (int)(t.largeTerror * t.railBoost) : t.largeTerror, boost ? t.mediumTerror : t.smallTerror, t.railHeard, false);
                    GainLegend(t.railLegend);
                    break;
                }
                case Act.BrutalKill:
                {
                    // A standing act, loud: rooted for the rest of the turn, and a cooldown. Crouched before it, you
                    // drop back down after.
                    bool wasCrouched = crouched;
                    crouched = false;
                    player = at; // the takedown carries you onto the victim's tile
                    Bleed(at, true);
                    Witness(at, body, t.largeTerror, t.smallTerror, t.brutalHeard, true);
                    if (Bloodied(SkillId.Brutal)) leapWait = 0; // judged at the legend you did it with
                    GainLegend(t.brutalLegend);
                    movesLeft = 0; // you still end the turn yourself
                    brutalWait = Def(SkillId.Brutal).cooldown;
                    crouched = wasCrouched;
                    break;
                }
            }
            return true;
        }

        // A loud kill at `at`: whoever sees it fights (or, `startles`, is startled if they've never seen you),
        // whoever only hears it comes looking.
        void Witness(Cell at, Body body, int seen, int heard, int noise, bool startles)
        {
            foreach (var o in enemies)
            {
                if (!o.InPlay) continue;
                if (Sees(o, at, false))
                {
                    bool fresh = !o.hasSeen;
                    AddTerror(o, seen);
                    o.found.Add(body);
                    if (!o.InPlay) continue;
                    if (startles && fresh) Startle(o);
                    else Fight(o);
                }
                else if (Hears(o, at, noise)) { AddTerror(o, heard); Search(o, at, Hunt.Go, Clue.Noise); }
            }
        }

        Body Kill(Enemy e, Act a)
        {
            bool begging = e.state == EnemyState.Broken;
            e.state = EnemyState.Dead;
            Log(begging ? $"You execute the begging {Name(e)}."
                : a == Act.QuietKill ? $"You silently kill {Name(e)}."
                : a == Act.RailGrab ? $"You hold {Name(e)} against the passing rails."
                : $"You butcher {Name(e)}.");
            var b = new Body { who = e, pos = e.pos, kind = a == Act.QuietKill ? BodyKind.Stealth : a == Act.RailGrab ? BodyKind.Rail : BodyKind.Brutal };
            bodies.Add(b);
            return b;
        }

        public void EndPlayerTurn() => turnOver = true;

        // ---- Commands: everything that changes the carriage, as text, so a save can replay it ----

        // Every command, in order (a refused one can still write to the log). A fresh carriage with the same map, spawns, seed and health
        // that runs them all ends up exactly here (the dice included), which is how a save is restored.
        public readonly List<string> history = new List<string>();

        public Enemy EnemyById(int id) => enemies.Find(e => e.id == id);

        // "move x y", "leap x y", "hop x y", "throw x y", "act <Act> <enemy id>", "grab", "drop", "crouch", "undo",
        // "end" (your turn), "enemy <id>" (its turn), "start" (your next turn). False if it did nothing.
        public bool Do(string cmd)
        {
            var w = cmd.Split(' ');
            Cell At() => new Cell(int.Parse(w[1]), int.Parse(w[2]));
            bool ok = true;
            switch (w[0])
            {
                case "move": ok = Move(At()); break;
                case "leap": ok = Leap(At()); break;
                case "hop": ok = Hop(At()); break;
                case "throw": ok = Throw(At()); break;
                case "act": ok = Perform((Act)Enum.Parse(typeof(Act), w[1]), EnemyById(int.Parse(w[2]))); break;
                case "grab": ok = Grab(); break;
                case "drop": ok = held != null; Drop(); break;
                case "crouch": ToggleCrouch(); break;
                case "undo": ok = CanReset; ResetTurn(); break;
                case "end": EndPlayerTurn(); break;
                case "enemy": EnemyAct(EnemyById(int.Parse(w[1]))); break;
                case "start": StartPlayerTurn(); break;
                default: throw new ArgumentException("Unknown command: " + cmd);
            }
            history.Add(cmd);
            return ok;
        }

        public void StartPlayerTurn()
        {
            acted = turnOver = leaped = hopped = spotted = noUndo = false;
            round++;
            movesLeft = freeWalk ? 999 : t.moveRange;
            turnStart = player;
            if (leapWait > 0) leapWait--;
            if (railWait > 0) railWait--;
            if (throwWait > 0) throwWait--;
            if (brutalWait > 0) brutalWait--;
            leapWaitAtStart = leapWait;
            foreach (var e in enemies)
            {
                e.panic = rng.NextDouble() < t.panicFreezeChance ? Panic.Freeze : Panic.WildFire;
                e.hesitate = rng.NextDouble() < t.shakenHesitate;
                e.reactionReady = true;
                e.sweepLook = Dirs[rng.Next(4)];
                e.sweepPick = rng.NextDouble();
            }
            // Anyone who ended the enemy turn looking at you (a searcher that turned your way) is fighting you now.
            foreach (var e in enemies)
                if (e.InPlay && e.awareness != Awareness.Combat && e.awareness != Awareness.Startled && CanSee(e, player)) Fight(e);
        }

        public bool Won => !freeWalk && enemies.TrueForAll(e => e.state != EnemyState.Active);
        public int Round => round; // player turns started (1 on the first)
        public bool Lost => hp <= 0;
        public bool Over => Won || Lost;

        // ---- Enemies ----

        // Shots at a leaping player lose leapHitPenalty (percentage points).
        public float HitChance(Enemy e, Cell target, bool leaping = false) =>
            Math.Max(0f, (StageOf(e) >= Stage.Shaken ? t.shakenHitChance : t.hitChance) * (HalfCovered(target, e.pos) ? t.halfCoverHitMult : 1f)
                - (leaping ? t.leapHitPenalty : 0f));

        // A Panicked guard's panic turn (every other turn, from the one after it panics): it freezes or fires wildly,
        // as rolled at the start of your turn.
        public bool PanicTurn(Enemy e) => e.InPlay && !e.tough && StageOf(e) == Stage.Panicked && e.panicNext;

        // Will they shoot you if you end your turn on `at` (crouched as now)? Seeing it, gun out, not hesitating,
        // startled or panicking. Heavies don't shoot (they charge): see Charges.
        public bool WillShoot(Enemy e, Cell at) =>
            e.InPlay && !e.tough && e.awareness != Awareness.Startled && !PanicTurn(e) && Sees(e, at, crouched)
            && !(StageOf(e) >= Stage.Shaken && e.hesitate);

        // A heavy that will come for you: berserk, or seeing `at`.
        public bool Charges(Enemy e, Cell at) =>
            e.InPlay && e.tough && e.awareness != Awareness.Startled && (e.berserk || Sees(e, at, crouched));

        // A charging heavy that gets beside `at` within its move swings the same turn: 3 walkable steps to
        // arrive, 1 to swing (a heavy that would need all 4 to get there only closes in).
        public bool Swings(Enemy e, Cell at)
        {
            if (!Charges(e, at)) return false;
            if (Adjacent(e.pos, at)) return true;
            var reach = Bfs(e.pos, t.enemyMoveRange, n => n != at && !Blocked(n) && (n == player || EnemyAt(n) == null));
            return reach.Keys.Any(n => Adjacent(n, at));
        }

        void Swing(Enemy e)
        {
            Face(e, player);
            if (rng.NextDouble() < t.meleeHitChance) { Log($"{Name(e)} lunges: hit!"); Damage(t.shotDamage); }
            else Log($"{Name(e)} lunges: miss.");
        }

        public Intent Decide(Enemy e)
        {
            if (e.state == EnemyState.Broken) return e.fleeing ? Intent.Flee : Intent.None;
            if (!e.InPlay) return Intent.None;
            if (e.awareness == Awareness.Startled) return Intent.Startled;
            if (e.berserk) return Adjacent(e.pos, player) ? Intent.Melee : Intent.Charge; // a panicked heavy: for good
            if (PanicTurn(e)) return e.panic == Panic.Freeze ? Intent.Freeze : Intent.WildFire;
            if (CanSee(e, player))
            {
                if (e.tough) return Adjacent(e.pos, player) ? Intent.Melee : Intent.Charge; // heavies close in to melee
                return StageOf(e) >= Stage.Shaken && e.hesitate ? Intent.Hesitate : Intent.Shoot;
            }
            // Out of sight: a fighter drops to searching where it saw you at its turn (EnemyAct), and goes there.
            if (e.awareness == Awareness.Combat) return Intent.Investigate;
            if (e.awareness == Awareness.Searching && e.lastKnown.HasValue)
                switch (e.hunt)
                {
                    case Hunt.Go: return Intent.Investigate;
                    case Hunt.Trail: return e.trail != null ? Intent.Follow : Intent.Investigate;
                    default: return Intent.Search; // looking around: at the spot (Sweep) or where they stand (Look)
                }
            // Unaware again (a search ran out): patrollers pick their route back up, guards walk back to their post.
            if (e.awareness == Awareness.Unaware && e.route == null && e.pos != e.post) return Intent.Return;
            return e.route != null && e.awareness == Awareness.Unaware ? Intent.Patrol : Intent.Hold;
        }

        // The chip label's one state line: awareness and nerve, or the single behaviour that overrides them.
        public string StatusText(Enemy e)
        {
            if (e.state == EnemyState.Fled || (e.state == EnemyState.Broken && e.fleeing)) return "Fled";
            if (e.state == EnemyState.Broken) return "Begging";
            if (e.berserk) return "Berserk";
            var stage = StageOf(e);
            if (stage == Stage.Panicked && !e.tough) return e.panicNext ? $"Panicked · {(e.panic == Panic.Freeze ? "Freeze" : "Wild fire")}" : "Panicked";
            var aware = e.awareness == Awareness.Combat ? "In combat" : e.awareness.ToString();
            return stage == Stage.Calm ? aware : $"{aware} · {stage}";
        }

        // What they'll do next and why: one thing only. Searchers say what they're investigating.
        public string IntentText(Enemy e)
        {
            switch (Decide(e))
            {
                case Intent.Shoot: return $"Hit {Math.Round(100 * HitChance(e, player))}%";
                case Intent.Hesitate: return "Hesitates";
                case Intent.Charge: return (e.berserk ? "Charge!" : "Charge") + (Swings(e, player) ? $" · Hit {Math.Round(100 * t.meleeHitChance)}%" : "");
                case Intent.Melee: return $"Melee: hit {Math.Round(100 * t.meleeHitChance)}%";
                case Intent.Investigate: case Intent.Follow: case Intent.Search:
                    return $"Investigate: {(e.awareness == Awareness.Combat ? Clue.Movement : e.clue)}";
                case Intent.Startled: return "Startled: loses turn";
                case Intent.Patrol: return "Patrol";
                case Intent.Hold: return "Hold";
                case Intent.Return: return "Back to post";
                case Intent.Freeze: return "Frozen with fear: loses the turn";
                case Intent.WildFire: return "Fires at anything in front";
                case Intent.Flee: return "Runs for the door";
                default: return e.state == EnemyState.Broken ? "Begging" : "";
            }
        }

        // The arrow: where the enemy will face after its next turn, shown only when it will turn or move that way
        // (null otherwise); a look-around draws it on the tile it will stand on (PlannedTile). Every act below turns
        // as NextFacing says; it's recomputed live, so a startle, freeze or panic clears it at once.
        public Cell? PlannedTurn(Enemy e)
        {
            if (!e.InPlay) return null;
            var intent = Decide(e);
            if (intent == Intent.Startled || intent == Intent.Freeze || intent == Intent.WildFire) return null;
            var f = NextFacing(e);
            bool moves = intent == Intent.Patrol || intent == Intent.Return || intent == Intent.Investigate || intent == Intent.Charge
                || intent == Intent.Follow || intent == Intent.Search;
            return f != e.facing || moves ? f : (Cell?)null;
        }

        // Where a looking-around searcher will stand after its next turn (null for everyone else).
        public Cell? PlannedTile(Enemy e)
        {
            if (Decide(e) != Intent.Search) return null;
            return e.hunt == Hunt.Look ? e.pos : SweepTarget(e) ?? e.pos;
        }

        // The tiles a looking-around searcher will walk next turn to reach PlannedTile, in order (empty if it stays put).
        public List<Cell> PlannedRoute(Enemy e)
        {
            if (Decide(e) != Intent.Search || e.hunt != Hunt.Sweep || !(SweepTarget(e) is Cell to)) return new List<Cell>();
            return (PathTo(e.pos, to) ?? new List<Cell>()).Take(t.enemyMoveRange).ToList();
        }

        // The tile a sweep walks to: one within searchRadius of the spot, picked by the roll made at your turn's start.
        Cell? SweepTarget(Enemy e)
        {
            var centre = e.lastKnown.Value;
            var options = new List<Cell>();
            foreach (var c in Bfs(e.pos, t.enemyMoveRange, n => !Blocked(n) && !Occupied(n)).Keys)
                if (Cheb(c, centre) <= t.searchRadius && c != e.pos) options.Add(c);
            return options.Count > 0 ? options[(int)(e.sweepPick * options.Count)] : (Cell?)null;
        }

        // The facing a walk toward `goal` ends with: along the last of up to `steps` tiles of its path.
        Cell WalkFacing(Enemy e, Cell goal, int steps)
        {
            var p = PathTo(e.pos, goal);
            if (p == null || p.Count == 0) return e.facing;
            int n = Math.Min(p.Count, steps);
            var last = p[n - 1];
            var prev = n > 1 ? p[n - 2] : e.pos;
            return Cardinal(last.x - prev.x, last.y - prev.y, e.facing);
        }

        // Where the enemy will face after its next move (for the grid arrow).
        public Cell NextFacing(Enemy e)
        {
            if (!e.InPlay) return e.facing;
            switch (Decide(e))
            {
                case Intent.Patrol:
                {
                    var goal = e.route[e.routeIndex];
                    if (goal == e.pos) goal = e.route[(e.routeIndex + 1) % e.route.Count];
                    var p = PathTo(e.pos, goal);
                    var last = p != null && p.Count > 0 ? p[Math.Min(p.Count, t.patrolMoveRange) - 1] : e.pos;
                    var prev = p != null && p.Count > 1 ? p[Math.Min(p.Count, t.patrolMoveRange) - 2] : e.pos;
                    return last == prev ? e.facing : Cardinal(last.x - prev.x, last.y - prev.y, e.facing);
                }
                case Intent.Hold:
                    return e.awareness == Awareness.Unaware && e.guardTimer + 1 >= t.guardTurnInterval ? e.looks[(e.lookIndex + 1) % e.looks.Count] : e.facing;
                case Intent.Search:
                    return e.sweepLook;
                case Intent.Return:
                {
                    var p = PathTo(e.pos, e.post);
                    // Arriving this turn: it turns back to its watch; otherwise it faces along its last step.
                    return p != null && p.Count <= t.patrolMoveRange ? e.looks[e.lookIndex] : WalkFacing(e, e.post, t.patrolMoveRange);
                }
                case Intent.Investigate: case Intent.Follow:
                    return WalkFacing(e, e.lastKnown ?? player, t.enemyMoveRange);
                case Intent.Charge:
                    return WalkFacing(e, e.berserk ? player : e.lastKnown ?? player, t.enemyMoveRange);
                case Intent.Startled: case Intent.Freeze: case Intent.WildFire:
                    return e.facing;
                case Intent.Shoot: case Intent.Melee: case Intent.Hesitate:
                    return Cardinal(player.x - e.pos.x, player.y - e.pos.y, e.facing);
                default:
                    return e.facing;
            }
        }

        void Shoot(Enemy o, string what, bool leaping = false)
        {
            shotsFired.Add(o);
            if (o.InPlay && o.awareness != Awareness.Combat && CanSee(o, player)) Fight(o); // whoever fires on you has you in sight
            if (rng.NextDouble() < HitChance(o, player, leaping)) { Log($"{Name(o)} {what}: hit!"); Damage(t.shotDamage); }
            else Log($"{Name(o)} {what}: miss.");
            AlertAll();
        }

        void StandDown(Enemy e)
        {
            e.lastKnown = null;
            e.sawYouAt = null;
            e.trail = null;
            e.awareness = Awareness.Unaware;
            Log($"{Name(e)} gave up the search.");
        }

        // Reached the spot: sweep the tiles round it for a few turns.
        void StartSweep(Enemy e, Cell centre)
        {
            e.hunt = Hunt.Sweep;
            e.lastKnown = centre;
            e.searchLeft = t.searchTurns;
            e.trail = null;
        }

        // A trail follower heads for the newest part of the trail it can see right now (its "?" moves with it).
        void RetargetTrail(Enemy e)
        {
            var s = e.trail;
            if (s == null) return;
            for (int i = s.tiles.Count - 1; i > e.trailIndex; i--)
                if (Sees(e, s.tiles[i], true)) { e.trailIndex = i; break; }
            e.lastKnown = s.tiles[e.trailIndex];
        }

        // The nearest door it can reach (null: cornered).
        List<Cell> DoorPath(Enemy e)
        {
            List<Cell> best = null;
            foreach (var door in new[] { new Cell(0, H / 2), new Cell(W - 1, H / 2) })
            {
                var p = PathTo(e.pos, door);
                if (p != null && (e.pos == door || (p.Count > 0 && p[p.Count - 1] == door)) && (best == null || p.Count < best.Count)) best = p;
            }
            return best;
        }

        public void EnemyAct(Enemy e)
        {
            e.walked = null;
            if (e.state == EnemyState.Broken && e.fleeing)
            {
                // Broken and running: for the next carriage, or begging if the way is blocked.
                var path = DoorPath(e);
                if (path == null) { e.fleeing = false; Log($"{Name(e)} is cornered and begs."); return; }
                int i = 0;
                Walk(e, t.enemyMoveRange, () => i < path.Count ? path[i++] : (Cell?)null);
                if (e.pos.x == 0 || e.pos.x == W - 1) { e.state = EnemyState.Fled; Log($"{Name(e)} flees to the next carriage."); }
                return;
            }
            if (!e.InPlay) return;
            if (e.awareness == Awareness.Startled)
            {
                Searching(e, Clue.Movement); // then hunts where they saw you
                e.hunt = Hunt.Go;
                e.searchLeft = t.searchTurns;
                Log($"{Name(e)} is startled and loses the turn.");
                return;
            }
            bool panicTurn = PanicTurn(e);
            if (!e.berserk)
            {
                if (CanSee(e, player)) Fight(e);
                else if (e.awareness == Awareness.Combat)
                {
                    e.awareness = Awareness.Unaware; // out of sight: search where they last saw you
                    Search(e, e.lastKnown ?? player, Hunt.Go, Clue.Movement);
                }
                else if (Notice(e)) { NextPanicTurn(e, panicTurn); return; } // found a body: frozen to the spot this turn
            }
            if (!e.InPlay) return;
            if (e.trail != null && e.hunt == Hunt.Trail) RetargetTrail(e);
            switch (Decide(e))
            {
                case Intent.Hesitate:
                    Face(e, player);
                    Log($"{Name(e)} hesitates.");
                    break;
                case Intent.Shoot:
                    Face(e, player);
                    Shoot(e, "fires");
                    break;
                case Intent.Melee:
                    Swing(e);
                    break;
                case Intent.Charge:
                    // Berserk: straight for your real tile. Otherwise for where it saw you. Arriving beside you
                    // (still seeing you), it swings at once; that ends its turn.
                    Walk(e, t.enemyMoveRange, () => StepToward(e, e.berserk ? player : e.lastKnown ?? player));
                    if (!Over && e.InPlay && Adjacent(e.pos, player) && (e.berserk || CanSee(e, player))) Swing(e);
                    break;
                case Intent.Investigate:
                {
                    var goal = e.lastKnown ?? player;
                    Walk(e, t.enemyMoveRange, () => e.awareness == Awareness.Searching ? StepToward(e, e.lastKnown ?? goal) : null);
                    if (e.awareness != Awareness.Searching || e.hunt == Hunt.Sweep) break; // saw you or found something
                    var at = e.lastKnown ?? goal;
                    if (Cheb(e.pos, at) <= 1 || StepToward(e, at) == null)
                    {
                        Face(e, at);
                        StartSweep(e, at);
                        Log($"{Name(e)} reaches the spot and searches.");
                    }
                    break;
                }
                case Intent.Follow:
                {
                    var s = e.trail;
                    var goal = e.lastKnown.Value;
                    Walk(e, t.enemyMoveRange, () => e.trail == s && e.awareness == Awareness.Searching ? StepToward(e, goal) : null);
                    if (e.trail != s || e.awareness != Awareness.Searching) break;
                    var before = e.lastKnown;
                    RetargetTrail(e);
                    // Where it was heading, with nothing newer in sight: the trail goes cold here (or ends at the body).
                    if (e.lastKnown == before && (e.pos == goal || StepToward(e, goal) == null))
                    {
                        Notice(e);
                        if (e.hunt == Hunt.Trail) StartSweep(e, e.pos);
                    }
                    break;
                }
                case Intent.Search:
                {
                    if (e.hunt == Hunt.Sweep)
                    {
                        var to = SweepTarget(e);
                        if (to.HasValue)
                        {
                            var path = PathTo(e.pos, to.Value);
                            int i = 0;
                            Walk(e, t.enemyMoveRange, () => path != null && i < path.Count ? path[i++] : (Cell?)null);
                        }
                    }
                    if (e.awareness != Awareness.Searching || (e.hunt != Hunt.Sweep && e.hunt != Hunt.Look)) break;
                    e.facing = e.sweepLook; // look around (rolled at the start of your turn, so the arrow showed it)
                    Notice(e);
                    if (e.awareness == Awareness.Searching && --e.searchLeft <= 0) StandDown(e);
                    break;
                }
                case Intent.Patrol:
                {
                    Walk(e, t.patrolMoveRange, () =>
                    {
                        if (e.awareness != Awareness.Unaware) return null;
                        if (e.pos == e.route[e.routeIndex]) e.routeIndex = (e.routeIndex + 1) % e.route.Count;
                        return StepToward(e, e.route[e.routeIndex]);
                    });
                    break;
                }
                case Intent.Return:
                    Walk(e, t.patrolMoveRange, () => e.awareness != Awareness.Unaware || e.pos == e.post ? (Cell?)null : StepToward(e, e.post));
                    if (e.awareness == Awareness.Unaware && e.pos == e.post)
                    {
                        e.facing = e.looks[e.lookIndex]; // back on watch the way it was
                        e.guardTimer = 0;
                        Notice(e);
                    }
                    break;
                case Intent.Hold:
                    if (e.awareness != Awareness.Unaware || ++e.guardTimer < t.guardTurnInterval) break;
                    e.guardTimer = 0;
                    e.lookIndex = (e.lookIndex + 1) % e.looks.Count;
                    e.facing = e.looks[e.lookIndex];
                    Notice(e); // a fresh view
                    break;
                case Intent.WildFire:
                {
                    var targets = new List<Cell> { player };
                    foreach (var o in enemies) if (o != e && o.InPlay) targets.Add(o.pos);
                    targets.RemoveAll(c => !InCone(e, c) || Dist2(e.pos, c) > Sight(e) * Sight(e) || !LineOfSight(e.pos, c, false)); // only what's in front of them
                    if (targets.Count == 0) { Log($"{Name(e)} fires wildly at nothing."); AlertAll(); break; }
                    shotsFired.Add(e);
                    var hit = targets[rng.Next(targets.Count)];
                    if (rng.NextDouble() >= t.wildHitChance * (HalfCovered(hit, e.pos) ? t.halfCoverHitMult : 1f)) Log($"{Name(e)} fires wildly and misses.");
                    else if (hit == player) { Log($"{Name(e)} fires wildly and hits you!"); Damage(t.shotDamage); }
                    else
                    {
                        var v = EnemyAt(hit);
                        v.state = EnemyState.Dead;
                        bodies.Add(new Body { who = v, pos = v.pos, kind = BodyKind.Shot });
                        Bleed(v.pos, false);
                        Log($"{Name(e)} fires wildly and kills {Name(v)}!");
                    }
                    AlertAll();
                    break;
                }
                case Intent.Freeze:
                    Log($"{Name(e)} freezes in terror.");
                    break;
            }
            NextPanicTurn(e, panicTurn);
        }

        // Panicked guards alternate: a panic turn, then a normal one.
        void NextPanicTurn(Enemy e, bool wasPanicTurn)
        {
            if (e.InPlay && !e.tough && StageOf(e) == Stage.Panicked) e.panicNext = !wasPanicTurn;
        }
    }
}
