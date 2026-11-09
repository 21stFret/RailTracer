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
        public int targetTurns; // the HUD's target: the level solver's best (Rail Tracer > Solve Journey Cars, seed 1); 0 = none
        public CarKind kind;
        public string[] map;
        public int[][] spawns;
        public Dictionary<int, Cell[]> patrols;
    }

    // Journey: three trains of five fixed cars, the same every playthrough, unlocked one car after the next (car 5 of
    // a train opens the next train), and a quiet walk-through car between cars. Train 2 is mostly dining cars, train 3
    // mostly baggage holds; each busier than the last. Endless mode: generated cars (TrainGen), getting busier.
    public static class Train
    {
        public const int TrainCars = 5, Trains = 3, Cars = TrainCars * Trains; // a Journey car's index runs 0 to Cars - 1
        public static readonly string[] TrainNames = { "The Red Line", "The Supper Express", "The Night Freight" };
        public static int TrainOf(int car) => car / TrainCars;

        // The hand-made first carriage. The gramophone ('G') takes the aisle place of a seat bank (a single seat beside it).
        static readonly string[] FirstMap =
        {
            "..S..S..S..S.WS..SL.S..SL.",
            "..S..S..G..S.WS..S..S..S..",
            "P.........................",
            "..S..S..S..S.WS..S..S..S..",
            "..S..S.LS..S.WS..S.LS..S..",
        };

        // Dining car: tables by the windows and a few out in the room, straight, L-shaped ('T') and round ('O',
        // 2x2), each a tile clear of the next; a dining cart ('D') in the aisle; an L-shaped bar at the far end.
        // Long sightlines, half cover only. Plain wall ('_') between some of the window tables and behind the bar.
        // The bar stands in the room (row 1), its short leg back to the wall, the bartender behind it. A gramophone
        // ('G', half cover) against the wall just short of the bar plays the music.
        static readonly string[] DiningMap =
        {
            "..TT...OO___TT...__G_B___.",
            ".......OO...T...T....BBBB.",
            "P.........D...............",
            "...T..T.......OO......T...",
            "..TT_____TT...OO..TT..____",
        };

        // Baggage hold: crates and luggage stacked into a maze of full cover, mail sacks for half cover, the safe's
        // cage you can see through, and only a few windows ('_' and 'x' have plain wall behind them).
        static readonly string[] BaggageMap =
        {
            "__xx_G_..._xx__CCC__.._xx_",
            "..XX..M...MXX..CCC.M...XX.",
            "P...........X.............",
            "..M..XX...M.....XX..M..X..",
            "___x___..._____xx_._..__x_",
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
                new[] { 18, 2, -1, 0, 0, 0 }, new[] { 21, 1, 0, 1, 1, 0 },
            },
            patrols = new Dictionary<int, Cell[]> { [3] = new[] { new Cell(10, 2), new Cell(12, 2) }, [6] = new[] { new Cell(18, 2), new Cell(23, 2) } },
        };

        // Endless cars always have at least this many seat banks (of about 16 places), for cover.
        public const int EndlessMinBanks = 8;

        static CarDef Generated(string name, string blurb, int seed, int enemies, int toughs, double banks = 0.85, int luggageMin = 3, int luggageMax = 6, int minBanks = 0)
        {
            var (map, spawns, patrols) = TrainGen.Make(new Random(seed), enemies, toughs, banks, luggageMin, luggageMax, minBanks);
            return new CarDef { name = name, blurb = blurb, kind = CarKind.Passenger, map = map, spawns = spawns, patrols = patrols };
        }

        static CarDef Dining() => new CarDef
        {
            name = "Dining car", blurb = "Open tables and long sightlines. Half cover only: made for loud kills and leaps.", kind = CarKind.Dining, map = DiningMap,
            spawns = new[]
            {
                new[] { 5, 1, 0, 1, 0, 0 }, new[] { 8, 3, 1, 0, 0, 0 }, new[] { 12, 2, 1, 0, 0, 0 }, new[] { 14, 0, 0, 1, 0, 0 },
                new[] { 17, 4, 0, -1, 0, 0 }, new[] { 20, 2, -1, 0, 0, 1 }, new[] { 23, 0, 0, 1, 0, 0 },
                new[] { 16, 3, -1, 0, 0, 0 }, new[] { 11, 4, 0, -1, 0, 0 },
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
                new[] { 11, 3, -1, 0, 0, 0 },
            },
            patrols = new Dictionary<int, Cell[]> { [4] = new[] { new Cell(16, 2), new Cell(21, 2) } },
        };

        // Car `i` (0-based, all three trains) of the Journey (made once; treat as read-only).
        static readonly CarDef[] demo = new CarDef[Cars];
        public static CarDef Demo(int i) => demo[i] ??= MakeDemo(i);

        // The target turns per Journey car: the level solver's results (train 1: 18.40, trains 2 and 3: 19.21; seed 1).
        // Rerun it after changing a car.
        static readonly int[] Targets = { 11, 7, 11, 9, 24, 10, 9, 9, 12, 10, 9, 8, 10, 11, 14 };

        static CarDef MakeDemo(int i)
        {
            var car = i switch
            {
                0 => First(),
                1 => Generated("Second class", "The same booths, a little more room between them.", 2002, 8, 1, banks: 0.6, luggageMin: 1, luggageMax: 3), // 7 guards, 1 heavy
                2 => Dining(),
                3 => Baggage(),
                4 => Generated("First class", "The last car. Whoever's left is waiting here.", 5005, 11, 3), // 8 guards, 3 heavies
                5 => Hand("Pullman car", "Plush booths and a long walk to the far door. The minder waits by the partition.", CarKind.Passenger, PullmanMap, Pullman),
                6 => Hand("Restaurant car", "Supper is served: tables down both sides and waiters working the aisle.", CarKind.Dining, RestaurantMap, Restaurant),
                7 => Hand("Smoking car", "Every booth taken, the partition right by the door. Plenty of cover, plenty of eyes.", CarKind.Passenger, SmokingMap, Smoking),
                8 => Hand("Lounge car", "A bar in the middle of the car and a crowd round it. Two carts in the aisle.", CarKind.Dining, LoungeMap, Lounge),
                9 => Hand("Grand saloon", "The last car of the Express. Silver service, and every guest armed.", CarKind.Dining, SaloonMap, Saloon),
                10 => Hand("Night coach", "Late passengers and the men watching them. A wall halfway down hides the far end.", CarKind.Passenger, NightMap, Night),
                11 => Hand("Mail van", "Sacks of the night's post and the guards who sort it. Short sightlines, deadly corners.", CarKind.Baggage, MailMap, Mail),
                12 => Hand("Guards' coach", "Off-duty guards in every booth, all of them armed. Take them one booth at a time.", CarKind.Passenger, GuardsMap, Guards),
                13 => Hand("Bullion van", "The gold rides here behind its cage, and so do the men paid to keep it.", CarKind.Baggage, BullionMap, Bullion),
                _ => Hand("Guard's van", "The end of the line. Everyone left on the train is in here.", CarKind.Baggage, VanMap, Van),
            };
            car.targetTurns = Targets[i];
            return car;
        }

        // A hand-made car. spawns: x, y, facing x, facing y, tough (1 = heavy); patrols: spawn index, then its waypoints
        // as x, y pairs (the first is where it starts).
        static CarDef Hand(string name, string blurb, CarKind kind, string[] map, (int[][] spawns, int[][] patrols) people) => new CarDef
        {
            name = name, blurb = blurb, kind = kind, map = map,
            spawns = people.spawns.Select(s => new[] { s[0], s[1], s[2], s[3], 0, s[4] }).ToArray(),
            patrols = people.patrols.ToDictionary(p => p[0], p => Enumerable.Range(0, (p.Length - 1) / 2).Select(k => new Cell(p[1 + 2 * k], p[2 + 2 * k])).ToArray()),
        };

        // ---- Train 2, the Supper Express: more dining, no baggage. ----

        // Pullman car: the partition far down (column 19), a bank missing on each side for open floor, the gramophone
        // in a bank's aisle place.
        static readonly string[] PullmanMap =
        {
            "..S..S.L...S__S..S.WS..SL.",
            "..S..S.....S..S..S.WS..S..",
            "P.........................",
            "..S.....S..S..G..S.WS..S..",
            "..S___.LS..S.LS..S.WS..S..",
        };
        static readonly (int[][], int[][]) Pullman = (new[] // 9 guards, 1 heavy
        {
            new[] { 9, 2, 1, 0, 0 }, new[] { 6, 1, 0, 1, 0 }, new[] { 9, 0, 0, 1, 0 }, new[] { 12, 3, -1, 0, 0 }, new[] { 15, 1, 0, 1, 0 },
            new[] { 16, 4, 0, -1, 0 }, new[] { 18, 2, -1, 0, 1 }, new[] { 21, 3, 0, -1, 0 }, new[] { 22, 0, 0, 1, 0 }, new[] { 22, 2, 1, 0, 0 },
        }, new[] { new[] { 0, 9, 2, 13, 2 }, new[] { 9, 22, 2, 25, 2 } });

        // Restaurant car: tables of every shape down both sides (no bar), the gramophone between two window tables,
        // a dining cart in the aisle.
        static readonly string[] RestaurantMap =
        {
            "..TT..TT.__OO...TT_G_.TT..",
            ".......T...OO.........T...",
            "P.............D...........",
            "...T...OO...T......OO...T.",
            "..TT._.OO.____.TT..OO._.T.",
        };
        static readonly (int[][], int[][]) Restaurant = (new[] // 8 guards, 2 heavies
        {
            new[] { 10, 2, 1, 0, 0 }, new[] { 5, 1, 0, 1, 0 }, new[] { 9, 1, -1, 0, 0 }, new[] { 13, 3, 0, -1, 0 }, new[] { 17, 1, 0, 1, 0 },
            new[] { 18, 3, -1, 0, 1 }, new[] { 21, 2, -1, 0, 0 }, new[] { 23, 4, 0, -1, 0 }, new[] { 25, 1, -1, 0, 1 }, new[] { 10, 4, 0, -1, 0 },
        }, new[] { new[] { 0, 10, 2, 13, 2 }, new[] { 6, 21, 2, 16, 2 } });

        // Smoking car: the partition two banks in, then six close-packed banks; luggage in the gaps between them.
        static readonly string[] SmokingMap =
        {
            "..S..S.WS..SL....S__S..S.L",
            "..S..S.WS..S.....G..S..S..",
            "P.........................",
            "..S..S.WS..S..S..S.....S..",
            "..S..SLWS..S__S..S.L...S..",
        };
        static readonly (int[][], int[][]) Smoking = (new[] // 8 guards, 2 heavies
        {
            new[] { 9, 2, 1, 0, 0 }, new[] { 6, 1, 0, 1, 0 }, new[] { 10, 0, 0, 1, 0 }, new[] { 9, 4, 0, -1, 0 }, new[] { 14, 1, 0, 1, 0 },
            new[] { 16, 3, -1, 0, 1 }, new[] { 19, 1, 0, 1, 0 }, new[] { 22, 4, 0, -1, 0 }, new[] { 20, 3, -1, 0, 0 }, new[] { 24, 2, -1, 0, 1 },
        }, new[] { new[] { 0, 9, 2, 15, 2 }, new[] { 9, 24, 2, 19, 2 } });

        // Lounge car: the L-shaped bar in the middle of the car (its short leg back to the wall, the bartender behind
        // it), the gramophone just past it, two dining carts in the aisle.
        static readonly string[] LoungeMap =
        {
            "..TT_.OO._..B____G_.TT..T.",
            "......OO....BBBB.....T..T.",
            "P.........D........D......",
            "...T....OO.......T..OO....",
            "...TT._.OO.__TT._...OO._T.",
        };
        static readonly (int[][], int[][]) Lounge = (new[] // 8 guards, 2 heavies
        {
            new[] { 14, 0, 0, 1, 0 }, new[] { 5, 1, 0, 1, 0 }, new[] { 11, 2, 1, 0, 0 }, new[] { 9, 1, 0, 1, 0 }, new[] { 6, 3, 0, -1, 0 },
            new[] { 11, 3, 1, 0, 0 }, new[] { 16, 1, 1, 0, 1 }, new[] { 18, 4, 0, -1, 0 }, new[] { 22, 2, -1, 0, 1 }, new[] { 23, 0, 0, 1, 0 },
        }, new[] { new[] { 2, 11, 2, 18, 2 } });

        // Grand saloon: round tables, the bar at the far end on the near side (mirrored), a cart mid-aisle.
        static readonly string[] SaloonMap =
        {
            "..TT..OO._.TT..OO._G_.TT..",
            "......OO.......OO.........",
            "P...........D.............",
            "....OO...T...OO......BBBB.",
            "....OO._.TT..OO._TT..B___.",
        };
        static readonly (int[][], int[][]) Saloon = (new[] // 8 guards, 3 heavies
        {
            new[] { 23, 4, 0, -1, 0 }, new[] { 8, 1, 0, 1, 0 }, new[] { 11, 2, -1, 0, 0 }, new[] { 14, 1, 0, 1, 0 }, new[] { 7, 3, 0, -1, 0 },
            new[] { 11, 4, 0, -1, 0 }, new[] { 17, 1, -1, 0, 1 }, new[] { 16, 3, 1, 0, 0 }, new[] { 20, 2, -1, 0, 1 }, new[] { 21, 1, 0, 1, 0 },
            new[] { 25, 2, -1, 0, 1 },
        }, new[] { new[] { 2, 11, 2, 7, 2 }, new[] { 8, 20, 2, 14, 2 } });

        // ---- Train 3, the Night Freight: more baggage, no dining. ----

        // Night coach: the partition halfway (column 16), banks missing here and there.
        static readonly string[] NightMap =
        {
            "..S....LS..S.LS.WS..S..S__",
            "..S.....S..S..S.WS..G..S..",
            "P.........................",
            "..S..S..S.....S.WS.....S..",
            "..S..SL.S.___.S.WS.L...S..",
        };
        static readonly (int[][], int[][]) Night = (new[] // 9 guards, 2 heavies
        {
            new[] { 6, 2, 1, 0, 0 }, new[] { 5, 0, 0, 1, 0 }, new[] { 10, 1, 0, 1, 0 }, new[] { 11, 3, 0, -1, 0 }, new[] { 13, 1, 1, 0, 0 },
            new[] { 15, 3, -1, 0, 1 }, new[] { 18, 1, 0, 1, 0 }, new[] { 20, 3, 0, -1, 0 }, new[] { 22, 0, 0, 1, 0 }, new[] { 21, 4, 0, -1, 0 },
            new[] { 24, 2, -1, 0, 1 },
        }, new[] { new[] { 0, 6, 2, 12, 2 }, new[] { 10, 24, 2, 18, 2 } });

        // Mail van: mail sacks everywhere (half cover), a few crates, short runs of window.
        static readonly string[] MailMap =
        {
            "__MG_xx_..._MM__x_..__M_x_",
            ".....XX.M.....X.X..M....X.",
            "P.........................",
            "...M...XX..M...X...XX.M...",
            "___.._xxx_M__..x_M_xx__.__",
        };
        static readonly (int[][], int[][]) Mail = (new[] // 8 guards, 3 heavies
        {
            new[] { 7, 2, 1, 0, 0 }, new[] { 7, 1, 0, 1, 0 }, new[] { 9, 0, 0, 1, 0 }, new[] { 5, 3, 0, -1, 0 }, new[] { 10, 3, 1, 0, 1 },
            new[] { 13, 1, 0, 1, 0 }, new[] { 15, 0, 0, 1, 0 }, new[] { 17, 3, -1, 0, 0 }, new[] { 18, 1, 0, 1, 1 }, new[] { 21, 2, -1, 0, 0 },
            new[] { 23, 3, -1, 0, 1 },
        }, new[] { new[] { 0, 7, 2, 13, 2 }, new[] { 9, 21, 2, 16, 2 } });

        // Guards' coach: the partition early (column 10), every bank but two filled, luggage in the gaps.
        static readonly string[] GuardsMap =
        {
            "..S..S.LS.WSL.S..___S..SL.",
            "..S..S..S.WS..S.....S..G..",
            "P.........................",
            "..S..S....WS..S..S..S..S..",
            "..S..SL___WS..S..S.LS..S__",
        };
        static readonly (int[][], int[][]) Guards = (new[] // 9 guards, 3 heavies
        {
            new[] { 5, 2, 1, 0, 0 }, new[] { 7, 1, 0, 1, 0 }, new[] { 8, 3, 0, -1, 0 }, new[] { 9, 0, 0, 1, 1 }, new[] { 12, 1, 0, 1, 0 },
            new[] { 13, 4, 0, -1, 0 }, new[] { 17, 1, 0, 1, 0 }, new[] { 16, 3, 0, -1, 0 }, new[] { 19, 1, -1, 0, 1 }, new[] { 21, 3, 0, -1, 0 },
            new[] { 22, 0, 0, 1, 0 }, new[] { 25, 2, -1, 0, 1 },
        }, new[] { new[] { 0, 5, 2, 9, 2 }, new[] { 11, 25, 2, 18, 2 } });

        // Bullion van: the safe's cage against the far wall, crates and sacks in a loose maze round it.
        static readonly string[] BullionMap =
        {
            "_x__..xx_G__..._x__CCC_x__",
            "...M..XX...M..X.X..CCC.X..",
            "P.........................",
            "....XX...M..XX...M...X..M.",
            "__..xx_M__..xx__..._xx____",
        };
        static readonly (int[][], int[][]) Bullion = (new[] // 8 guards, 4 heavies
        {
            new[] { 8, 2, 1, 0, 0 }, new[] { 5, 1, 0, 1, 0 }, new[] { 8, 0, 0, 1, 0 }, new[] { 7, 3, 0, -1, 0 }, new[] { 10, 4, 0, -1, 1 },
            new[] { 13, 1, 0, 1, 0 }, new[] { 15, 3, 1, 0, 0 }, new[] { 18, 1, 1, 0, 1 }, new[] { 22, 1, -1, 0, 0 }, new[] { 19, 3, 0, -1, 0 },
            new[] { 23, 3, -1, 0, 1 }, new[] { 24, 0, 0, 1, 1 },
        }, new[] { new[] { 0, 8, 2, 15, 2 } });

        // Guard's van: crates and sacks staggered down both sides, the gramophone between two crate stacks.
        static readonly string[] VanMap =
        {
            "__xx_.._M_x__.._xx_G_.._x_",
            "..X...M...X...M..X...M..X.",
            "P.........................",
            "....M..XX...M...X...M.XX..",
            "_..__x_xx_.._M__x_..__xx__",
        };
        static readonly (int[][], int[][]) Van = (new[] // 9 guards, 4 heavies
        {
            new[] { 6, 2, 1, 0, 0 }, new[] { 5, 0, 0, 1, 0 }, new[] { 8, 1, 0, 1, 0 }, new[] { 6, 3, 0, -1, 0 }, new[] { 11, 0, 0, 1, 1 },
            new[] { 10, 3, 1, 0, 0 }, new[] { 13, 1, 0, 1, 0 }, new[] { 15, 3, -1, 0, 1 }, new[] { 16, 1, 1, 0, 0 }, new[] { 18, 3, 0, -1, 0 },
            new[] { 20, 1, 0, 1, 1 }, new[] { 23, 1, -1, 0, 0 }, new[] { 24, 2, -1, 0, 1 },
        }, new[] { new[] { 0, 6, 2, 12, 2 }, new[] { 12, 24, 2, 17, 2 } });

        // Endless car `n` (1-based) from `seed`: 7 to 9 guards and 0 to 2 heavies, rolled from the seed (the same each replay).
        public static CarDef Endless(int n, int seed)
        {
            var roll = new Random(seed ^ 0x5eed);
            int heavies = roll.Next(0, 3), guards = roll.Next(7, 10);
            return Generated($"Endless car {n}", "Another car, another crowd.", seed, guards + heavies, heavies, minBanks: EndlessMinBanks);
        }

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
