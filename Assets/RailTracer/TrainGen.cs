using System;
using System.Collections.Generic;
using System.Linq;

namespace RailTracer
{
    // A new carriage and its passengers, laid out the way the hand-made one is (and the train kit expects):
    // 26 x 5, windows on rows 0 and 4, the aisle on row 2 kept clear, seat banks two rows deep every third column,
    // one partition wall with a door in the aisle, a few luggage stacks on the window rows (never in a booth
    // between two seats facing each other), and some stretches of outer wall with no windows. No UnityEngine, so
    // Editor/RulesCheck.cs can test it.
    public static class TrainGen
    {
        public const int W = 26, H = 5, Aisle = 2;

        // spawns: x, y, facing x, facing y, (unused), tough. patrols: waypoints by spawn index.
        public static (string[] map, int[][] spawns, Dictionary<int, Cell[]> patrols) Make(Random rng)
        {
            var g = new char[W, H];
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) g[x, y] = '.';

            int part = rng.Next(11, 16);
            for (int y = 0; y < H; y++) if (y != Aisle) g[part, y] = 'W';
            void Banks(int from, int to)
            {
                for (int x = from; x <= to; x += 3)
                    foreach (int top in new[] { 0, Aisle + 1 })
                        if (rng.NextDouble() < 0.85) g[x, top] = g[x, top + 1] = 'S';
            }
            Banks(2, part - 2);
            Banks(part + 1, W - 3);

            // Luggage on the window rows only, so every booth tile still reaches the aisle, and never between two
            // seats facing each other. The kit turns every other seat column round, so columns 1 and 2, 3 and 4...
            // (counting seat columns) face each other across a booth; 2 and 3 sit back to back.
            var seatCols = Enumerable.Range(0, W).Where(x => Enumerable.Range(0, H).Any(y => g[x, y] == 'S')).ToList();
            var booths = new HashSet<int>();
            for (int k = 0; k + 1 < seatCols.Count; k += 2)
                for (int x = seatCols[k] + 1; x < seatCols[k + 1]; x++) booths.Add(x);
            var spots = Tiles(g, (x, y) => (y == 0 || y == H - 1) && x >= 4 && !booths.Contains(x)).OrderBy(_ => rng.Next()).Take(rng.Next(3, 6));
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
            int want = rng.Next(8, 11);
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
            foreach (int i in Enumerable.Range(0, spawns.Count).OrderBy(_ => rng.Next()).Take(2)) spawns[i][5] = 1; // two tough ones

            var map = new string[H];
            for (int y = 0; y < H; y++)
                map[y] = new string(Enumerable.Range(0, W).Select(x => x == 0 && y == Aisle ? 'P' : g[x, y]).ToArray());
            return (map, spawns.ToArray(), patrols);
        }

        static IEnumerable<(int x, int y)> Tiles(char[,] g, Func<int, int, bool> ok)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (g[x, y] == '.' && ok(x, y)) yield return (x, y);
        }
    }
}
