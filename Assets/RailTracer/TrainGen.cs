using System;
using System.Collections.Generic;
using System.Linq;

namespace RailTracer
{
    // A new carriage and its passengers, laid out the way the hand-made one is (and the train kit expects):
    // 26 x 5, windows on rows 0 and 4, the aisle on row 2 kept clear, seat banks two rows deep in facing booths,
    // one partition wall with a door in the aisle, a few luggage stacks on the window rows (never in a booth
    // between two seats facing each other), some stretches of outer wall with no windows, and a gramophone (in a seat
    // bank's aisle place or alone against the side wall). No UnityEngine, so
    // Editor/RulesCheck.cs can test it.
    public static class TrainGen
    {
        public const int W = 26, H = 5, Aisle = 2;

        // spawns: x, y, facing x, facing y, (unused), tough. patrols: waypoints by spawn index. Banks come in booths
        // (19.35): `banks` is the chance each booth place is filled, minBanks counts single banks.
        // enemies: how many (0 = 8 to 10 at random), toughs of them heavies; banks: the chance each seat bank is
        // there; minBanks: at least this many banks (missing ones added at random, so cars that already have enough are
        // unchanged); luggage: how many stacks (min, max exclusive); tables: the chance each booth side gets a table.
        // The defaults are the original generator.
        public static (string[] map, int[][] spawns, Dictionary<int, Cell[]> patrols) Make(Random rng, int enemies = 0, int toughs = 2,
            double banks = 0.85, int luggageMin = 3, int luggageMax = 6, int minBanks = 0, double tables = 0)
        {
            var g = new char[W, H];
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) g[x, y] = '.';

            int part = rng.Next(11, 16);
            for (int y = 0; y < H; y++) if (y != Aisle) g[part, y] = 'W';
            // Seat banks come as booths: two banks three columns apart facing each other across two free tiles, on one
            // side of the aisle; each booth place is all there or all missing. A column left over at a section's end
            // stays open floor, so the kit's every-other-seat-column pairing always matches the booths (no bank facing
            // an empty floor, none paired with one across the car).
            var empty = new List<(int x, int top)>(); // booth places left empty
            int built = 0;
            void Booth(int x, int top) { g[x, top] = g[x, top + 1] = g[x + 3, top] = g[x + 3, top + 1] = 'S'; built += 2; }
            void Banks(int from, int to)
            {
                for (int x = from; x + 3 <= to; x += 6)
                    foreach (int top in new[] { 0, Aisle + 1 })
                        if (rng.NextDouble() < banks) Booth(x, top);
                        else empty.Add((x, top));
            }
            Banks(2, part - 2);
            Banks(part + 1, W - 3);
            for (; built < minBanks && empty.Count > 0;)
            {
                int i = rng.Next(empty.Count);
                var (x, top) = empty[i];
                empty.RemoveAt(i);
                Booth(x, top);
            }

            // Luggage on the window rows only, so every booth tile still reaches the aisle, and never between two
            // seats facing each other. The kit turns every other seat column round, so columns 1 and 2, 3 and 4...
            // (counting seat columns) face each other across a booth; 2 and 3 sit back to back.
            var seatCols = Enumerable.Range(0, W).Where(x => Enumerable.Range(0, H).Any(y => g[x, y] == 'S')).ToList();
            var booths = new HashSet<int>();
            for (int k = 0; k + 1 < seatCols.Count; k += 2)
                for (int x = seatCols[k] + 1; x < seatCols[k + 1]; x++) booths.Add(x);
            var spots = Tiles(g, (x, y) => (y == 0 || y == H - 1) && x >= 4 && !booths.Contains(x)).OrderBy(_ => rng.Next()).Take(rng.Next(luggageMin, luggageMax));
            foreach (var (x, y) in spots) g[x, y] = 'L';

            // Some stretches of outer wall have no windows: each run of window-row tiles between seats and walls is
            // blank about a quarter of the time ('_' floor, 'l' luggage).
            foreach (int y in new[] { 0, H - 1 })
                for (int x = 0; x < W;)
                {
                    if (g[x, y] == 'S' || g[x, y] == 'W') { x++; continue; }
                    int end = x;
                    while (end < W && g[end, y] != 'S' && g[end, y] != 'W') end++;
                    if (rng.NextDouble() < 0.25)
                        for (int i = x; i < end; i++) g[i, y] = g[i, y] == 'L' ? 'l' : '_';
                    x = end;
                }

            // Passengers: 8 to 10, none near the start or each other. Two patrol stretches of the aisle.
            var placed = new List<(int x, int y)>();
            var spawns = new List<int[]>();
            var patrols = new Dictionary<int, Cell[]>();
            bool Clear(int x, int y) => (g[x, y] == '.' || g[x, y] == '_') && x >= 5 && placed.All(p => Math.Max(Math.Abs(p.x - x), Math.Abs(p.y - y)) >= 2);
            for (int i = 0; i < 2; i++)
            {
                for (int tries = 0; tries < 50; tries++)
                {
                    int span = rng.Next(2, 6), x0 = rng.Next(5, W - span);
                    if (!Enumerable.Range(x0, span + 1).All(x => Clear(x, Aisle))) continue;
                    patrols[spawns.Count] = new[] { new Cell(x0, Aisle), new Cell(x0 + span, Aisle) };
                    spawns.Add(new[] { x0, Aisle, 1, 0, 0, 0 });
                    for (int x = x0; x <= x0 + span; x++) placed.Add((x, Aisle)); // keep the beat clear
                    break;
                }
            }
            int want = enemies > 0 ? enemies : rng.Next(8, 11);
            foreach (var (x, y) in Tiles(g, (x, y) => true).OrderBy(_ => rng.Next()))
            {
                if (spawns.Count >= want) break;
                if (!Clear(x, y)) continue;
                // Booth guards mostly watch the aisle; anyone else faces up or down the carriage.
                bool booth = y != Aisle;
                var (fx, fy) = booth && rng.NextDouble() < 0.7 ? (0, y < Aisle ? 1 : -1) : (rng.Next(2) == 0 ? -1 : 1, 0);
                spawns.Add(new[] { x, y, fx, fy, 0, 0 });
                placed.Add((x, y));
            }
            foreach (int i in Enumerable.Range(0, spawns.Count).OrderBy(_ => rng.Next()).Take(toughs)) spawns[i][5] = 1; // the tough ones

            // The gramophone, one per car, never in the aisle: half the time the aisle place of a seat bank (the window
            // place keeps a single seat), otherwise on its own against the side wall, on a window-row floor tile (plain
            // wall goes behind it) clear of the booths, the start and anyone placed. Whichever kind there is, if only one.
            var inBank = Enumerable.Range(0, W).SelectMany(x => new[] { (x, y: 1, win: 0), (x, y: H - 2, win: H - 1) })
                .Where(s => g[s.x, s.y] == 'S' && g[s.x, s.win] == 'S').Select(s => (s.x, s.y)).ToList();
            var byWall = Enumerable.Range(4, W - 4).SelectMany(x => new[] { (x, y: 0), (x, y: H - 1) })
                .Where(t => (g[t.x, t.y] == '.' || g[t.x, t.y] == '_') && !booths.Contains(t.x) && !placed.Contains(t)).ToList();
            var spotsG = inBank.Count > 0 && (byWall.Count == 0 || rng.Next(2) == 0) ? inBank : byWall;
            if (spotsG.Count > 0) { var (gx, gy) = spotsG[rng.Next(spotsG.Count)]; g[gx, gy] = 'G'; }

            // Booth tables ('t', 19.42; Second and First class): a pedestal table on a window-row floor tile between a
            // booth's two facing banks, clear of anyone placed; seat rules (half cover). Drawn last, so the rest of the car
            // comes out the same as without them (and cars without tables are unchanged).
            if (tables > 0)
                for (int k = 0; k + 1 < seatCols.Count; k += 2)
                    foreach (int y in new[] { 0, H - 1 })
                    {
                        int x0 = seatCols[k], x1 = seatCols[k + 1];
                        if (x1 - x0 != 3 || g[x0, y] != 'S' || g[x1, y] != 'S' || rng.NextDouble() >= tables) continue;
                        var free = new[] { x0 + 1, x0 + 2 }.Where(x => g[x, y] == '.' && !placed.Contains((x, y))).ToList();
                        if (free.Count > 0) g[free[rng.Next(free.Count)], y] = 't';
                    }

            var map = new string[H];
            for (int y = 0; y < H; y++)
                map[y] = new string(Enumerable.Range(0, W).Select(x => x == 0 && y == Aisle ? 'P' : g[x, y]).ToArray());
            return (map, spawns.ToArray(), patrols);
        }

        // The booth coach (19.48): booths down the far side (rows 0-3), the aisle down the near side (row 4). A booth is two
        // seat banks (rows 0-1) facing each other across 1 to 3 free columns, between dividers ('V' on rows 0-3: walls,
        // the aisle open, so the rules treat each as a partition with its doorway on the aisle); back-to-back booths share
        // one ('w': a booth front, see below). The two columns at each end stay open floor (any column left over joins the far end); the gramophone
        // stands in the far end's window corner. Passengers as in Make, the patrols on the aisle.
        public static (string[] map, int[][] spawns, Dictionary<int, Cell[]> patrols) MakeBooths(Random rng, int enemies, int toughs)
        {
            const int Aisle = H - 1;
            var g = new char[W, H];
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) g[x, y] = '.';
            void Divider(int c) { for (int y = 0; y < Aisle; y++) g[c, y] = 'V'; }
            int at = 2;
            Divider(at);
            for (;;)
            {
                int gap = Math.Min(rng.Next(1, 4), W - 3 - at - 3); // seat, gap, seat, divider; the last divider at W - 3 at most
                if (gap < 1) break;
                g[at + 1, 0] = g[at + 1, 1] = g[at + gap + 2, 0] = g[at + gap + 2, 1] = 'S';
                at += gap + 3;
                Divider(at);
            }
            g[W - 1, 0] = 'G';
            // Booth fronts (20.2, Alex's layout C): each booth, and the stretch past the last divider, is walled off along
            // its aisle edge ('w' on the row next to the aisle) from its left divider, all but its last 2 tiles: half
            // closed, the doorway at the far end.
            for (int x = 0; x < W; x++)
            {
                if (g[x, 0] != 'V') continue;
                int next = Enumerable.Range(x + 1, W - x - 1).FirstOrDefault(n => g[n, 0] == 'V');
                int width = (next > 0 ? next : W) - x - 1;
                for (int k = 1; k <= width - 2; k++) g[x + k, Aisle - 1] = 'w';
            }

            var placed = new List<(int x, int y)>();
            var spawns = new List<int[]>();
            var patrols = new Dictionary<int, Cell[]>();
            bool Clear(int x, int y) => g[x, y] == '.' && x >= 5 && placed.All(p => Math.Max(Math.Abs(p.x - x), Math.Abs(p.y - y)) >= 2);
            for (int i = 0; i < 2; i++)
                for (int tries = 0; tries < 50; tries++)
                {
                    int span = rng.Next(2, 6), x0 = rng.Next(5, W - span);
                    if (!Enumerable.Range(x0, span + 1).All(x => Clear(x, Aisle))) continue;
                    patrols[spawns.Count] = new[] { new Cell(x0, Aisle), new Cell(x0 + span, Aisle) };
                    spawns.Add(new[] { x0, Aisle, 1, 0, 0, 0 });
                    for (int x = x0; x <= x0 + span; x++) placed.Add((x, Aisle));
                    break;
                }
            foreach (var (x, y) in Tiles(g, (x, y) => true).OrderBy(_ => rng.Next()))
            {
                if (spawns.Count >= enemies) break;
                if (!Clear(x, y)) continue;
                bool booth = y != Aisle; // booth guards mostly watch the aisle
                var (fx, fy) = booth && rng.NextDouble() < 0.7 ? (0, 1) : (rng.Next(2) == 0 ? -1 : 1, 0);
                spawns.Add(new[] { x, y, fx, fy, 0, 0 });
                placed.Add((x, y));
            }
            foreach (int i in Enumerable.Range(0, spawns.Count).OrderBy(_ => rng.Next()).Take(toughs)) spawns[i][5] = 1;

            var map = new string[H];
            for (int y = 0; y < H; y++)
                map[y] = new string(Enumerable.Range(0, W).Select(x => x == 0 && y == H / 2 ? 'P' : g[x, y]).ToArray());
            return (map, spawns.ToArray(), patrols);
        }

        // Which way a seat bank in column x faces, as the view turns it (the FBX's back at local +Z): 180 = facing +x (up the
        // car), 0 = facing -x. Columns pair up along the whole car, so a bank whose partner is across a partition would
        // face the partition's wall: it faces away from it instead (20.3, the Guards' coach).
        public static float SeatRot(string[] m, int x)
        {
            int h = m.Length, w = m[0].Length;
            bool Seat(int c) => Enumerable.Range(0, h).Any(y => m[y][c] == 'S');
            bool Partition(int c) => Enumerable.Range(0, h).All(y => y == h / 2 ? m[y][c] != 'W' : m[y][c] == 'W');
            var cols = Enumerable.Range(0, w).Where(Seat).ToList();
            int i = cols.IndexOf(x);
            if (i < 0) return 0f;
            int partner = i % 2 == 0 ? (i + 1 < cols.Count ? cols[i + 1] : -1) : cols[i - 1];
            if (partner >= 0 && Enumerable.Range(Math.Min(x, partner) + 1, Math.Abs(partner - x) - 1).Any(Partition))
                return partner > x ? 0f : 180f; // its partner beyond a partition: face away from it
            return i % 2 == 0 ? 180f : 0f;
        }

        static IEnumerable<(int x, int y)> Tiles(char[,] g, Func<int, int, bool> ok)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (g[x, y] == '.' && ok(x, y)) yield return (x, y);
        }
    }
}
