using System;
using System.Collections.Generic;
using System.Linq;

namespace RailTracer
{
    public enum CarKind { Passenger, Dining, Baggage, WalkThrough }

    // One car of the train: its layout (Carriage map), passengers (x, y, facing x, facing y, unused, tough) and
    // patrols (waypoints by spawn index), and what the menus say about it. No UnityEngine, so RulesCheck can test it.
    public class CarDef
    {
        public string name, blurb;
        public CarKind kind;
        public string[] map;
        public int[][] spawns;
        public Dictionary<int, Cell[]> patrols;
    }

    // The demo: five fixed cars in order, the same every playthrough, and a quiet walk-through car between them.
    // Endless mode: generated cars (TrainGen), getting busier.
    public static class Train
    {
        public const int DemoCars = 5;

        // The hand-made first carriage.
        static readonly string[] FirstMap =
        {
            "..S..S..S..S.WS..SL.S..SL.",
            "..S..S..S..S.WS..S..S..S..",
            "P.........................",
            "..S..S..S..S.WS..S..S..S..",
            "..S..S.LS..S.WS..S.LS..S..",
        };

        // Dining car: tables by the windows and a few out in the room, straight, L-shaped ('T') and round ('O',
        // 2x2), each a tile clear of the next; a dining cart ('D') in the aisle; an L-shaped bar at the far end.
        // Long sightlines, half cover only. Plain wall ('_') between some of the window tables and behind the bar.
        static readonly string[] DiningMap =
        {
            "..TT...OO___TT...____BBBB.",
            ".......OO...T...T....B....",
            "P.........D...............",
            "...T..T.......OO......T...",
            "..TT_____TT...OO..TT..____",
        };

        // Baggage hold: crates and luggage stacked into a maze of full cover, mail sacks for half cover, the safe's
        // cage you can see through, and only a few windows ('_' and 'x' have plain wall behind them).
        static readonly string[] BaggageMap =
        {
            "__xx___..._xx__CCC__.._xx_",
            "..XX..M...MXX..CCC.M...XX.",
            "P...........X.............",
            "..M..XX...M.....XX..M..X..",
            "___xx__..._____xx___..__x_",
        };

        // The walk-through car: the first carriage's seats with nobody in them.
        static readonly string[] WalkMap = FirstMap.Select(r => r.Replace('L', '.')).ToArray();

        static CarDef First() => new CarDef
        {
            name = "Third class", blurb = "Cramped booths and nowhere to stretch your legs.", kind = CarKind.Passenger, map = FirstMap,
            spawns = new[]
            {
                new[] { 3, 0, 0, 1, 0, 0 }, new[] { 6, 2, 1, 0, 0, 0 }, new[] { 9, 3, 0, -1, 1, 0 },
                new[] { 10, 2, -1, 0, 1, 0 }, new[] { 12, 0, 0, 1, 0, 0 }, new[] { 15, 4, 0, -1, 1, 0 },
            },
            patrols = new Dictionary<int, Cell[]> { [3] = new[] { new Cell(10, 2), new Cell(12, 2) } },
        };

        static CarDef Generated(string name, string blurb, int seed, int enemies, int toughs, double banks = 0.85, int luggageMin = 3, int luggageMax = 6)
        {
            var (map, spawns, patrols) = TrainGen.Make(new Random(seed), enemies, toughs, banks, luggageMin, luggageMax);
            return new CarDef { name = name, blurb = blurb, kind = CarKind.Passenger, map = map, spawns = spawns, patrols = patrols };
        }

        static CarDef Dining() => new CarDef
        {
            name = "Dining car", blurb = "Open tables and long sightlines. Half cover only: made for loud kills and leaps.", kind = CarKind.Dining, map = DiningMap,
            spawns = new[]
            {
                new[] { 5, 1, 0, 1, 0, 0 }, new[] { 8, 3, 1, 0, 0, 0 }, new[] { 12, 2, 1, 0, 0, 0 }, new[] { 14, 0, 0, 1, 0, 0 },
                new[] { 17, 4, 0, -1, 0, 0 }, new[] { 20, 2, -1, 0, 0, 1 }, new[] { 23, 1, -1, 0, 0, 0 },
            },
            patrols = new Dictionary<int, Cell[]> { [2] = new[] { new Cell(12, 2), new Cell(17, 2) } },
        };

        static CarDef Baggage() => new CarDef
        {
            name = "Baggage hold", blurb = "A maze of crates that blocks sight completely. Good for hiding bodies; deadly up close.", kind = CarKind.Baggage, map = BaggageMap,
            spawns = new[]
            {
                new[] { 5, 1, 0, 1, 0, 0 }, new[] { 8, 2, 1, 0, 0, 0 }, new[] { 9, 4, 0, -1, 0, 0 }, new[] { 14, 3, 0, -1, 0, 1 },
                new[] { 16, 2, 1, 0, 0, 0 }, new[] { 19, 0, 0, 1, 0, 0 }, new[] { 22, 3, -1, 0, 0, 1 }, new[] { 24, 2, -1, 0, 0, 0 },
            },
            patrols = new Dictionary<int, Cell[]> { [4] = new[] { new Cell(16, 2), new Cell(21, 2) } },
        };

        // Car `i` (0-based) of the demo (made once; treat as read-only).
        static readonly CarDef[] demo = new CarDef[DemoCars];
        public static CarDef Demo(int i) => demo[i] ??= MakeDemo(i);

        static CarDef MakeDemo(int i)
        {
            switch (i)
            {
                case 0: return First();
                case 1: return Generated("Second class", "The same booths, a little more room between them.", 2002, 7, 1, banks: 0.6, luggageMin: 1, luggageMax: 3);
                case 2: return Dining();
                case 3: return Baggage();
                default: return Generated("First class", "The last car. Whoever's left is waiting here.", 5005, 9, 2);
            }
        }

        // Endless car `n` (1-based) from `seed`: a car more than the last, up to 11; a heavy more every other car, up to 3.
        public static CarDef Endless(int n, int seed) =>
            Generated($"Endless car {n}", "Another car, another crowd.", seed, Math.Min(5 + n, 11), Math.Min(1 + n / 2, 3));

        public static CarDef WalkThrough() => new CarDef
        {
            name = "Between cars", blurb = "Nobody here.", kind = CarKind.WalkThrough, map = WalkMap,
            spawns = new int[0][], patrols = new Dictionary<int, Cell[]>(),
        };

        // A fresh carriage for the car: its map and passengers, with your health carried in (a walk-through car
        // has nobody in it and costs nothing to walk).
        public static Carriage Make(CarDef car, Tuning t, int seed, int hp)
        {
            var c = new Carriage(car.map, t, seed);
            c.freeWalk = car.kind == CarKind.WalkThrough;
            for (int i = 0; i < car.spawns.Length; i++)
            {
                var s = car.spawns[i];
                c.Spawn(s[0], s[1], s[2], s[3], s[4], s[5] == 1, car.patrols.TryGetValue(i, out var route) ? route.ToList() : null);
            }
            c.NameEnemies();
            c.hp = hp;
            if (c.freeWalk) c.StartPlayerTurn(); // the free moves
            return c;
        }
    }
}
