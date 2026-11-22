using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Rail Tracer > Car Looks: bakes Journey cars' looks (shell, furniture, curtains, lamps, props) into prefabs in
    // Resources/CarLooks/Look_NN, built exactly as the game builds them. Edit a prefab like any other (move, add or
    // delete pieces; rename a "Prop_Ashtray_Cigar" to end " (empty)" to put its cigar out); the game loads it for that
    // car from then on. Delete it to go back to the built look. Needs Play mode (the game builds the car).
    // Gameplay is still Train.cs: moving a seat or rack in the prefab doesn't move the tile that blocks.
    static class CarLookBaker
    {
        const string Folder = "Assets/RailTracer/Resources/" + Game.LookFolder;

        [MenuItem("Rail Tracer/Car Looks/Bake The Midlands Night Train (train 1)")]
        static void BakeTrain1() => Bake(0, Train.TrainCars);
        [MenuItem("Rail Tracer/Car Looks/Bake The Northern Sovereign (train 2)")]
        static void BakeTrain2() => Bake(Train.TrainCars, 2 * Train.TrainCars);
        [MenuItem("Rail Tracer/Car Looks/Bake The Royal Parallel (train 3)")]
        static void BakeTrain3() => Bake(2 * Train.TrainCars, 3 * Train.TrainCars);

        [MenuItem("Rail Tracer/Car Looks/Bake This Car")]
        static void BakeThis()
        {
            var game = PlayingGame();
            if (!game) return;
            if (game.LiveLookCar < 0) { EditorUtility.DisplayDialog("Car Looks", "This car (endless or between cars) has no look of its own.", "OK"); return; }
            Bake(game.LiveLookCar, game.LiveLookCar + 1);
        }

        static Game PlayingGame()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Car Looks", "Enter Play mode first: the game builds the cars to bake.", "OK");
                return null;
            }
            var game = Object.FindAnyObjectByType<Game>();
            if (!game) EditorUtility.DisplayDialog("Car Looks", "No Game in the open scene.", "OK");
            return game;
        }

        static void Bake(int from, int to)
        {
            var game = PlayingGame();
            if (!game) return;
            Directory.CreateDirectory(Folder);
            var existing = new List<string>();
            for (int g = from; g < to; g++)
                if (File.Exists(PathOf(g))) existing.Add(Path.GetFileNameWithoutExtension(PathOf(g)));
            if (existing.Count > 0 && !EditorUtility.DisplayDialog("Car Looks",
                    $"Replace {string.Join(", ", existing)}? Any edits you made to them are lost.", "Replace", "Cancel"))
                return;
            int baked = 0;
            for (int g = from; g < to; g++)
            {
                var look = game.BuildLookForBake(g);
                if (!look) { Debug.LogWarning($"Rail Tracer: car {g} has no kit look to bake."); continue; }
                look.name = Game.LookId(false, false, g);
                // Anything not made from an asset (a white-box stand-in's material, say) would save as missing.
                foreach (var r in look.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m && !AssetDatabase.Contains(m))
                            Debug.LogWarning($"Rail Tracer: {look.name}/{r.name} uses a runtime material ({m.name}); it will be missing in the prefab.", r);
                PrefabUtility.SaveAsPrefabAsset(look, PathOf(g));
                Object.Destroy(look);
                baked++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"Rail Tracer: baked {baked} car look(s) into {Folder}. The game uses them from the next car built.");
        }

        static string PathOf(int g) => $"{Folder}/{Game.LookId(false, false, g)}.prefab";
    }
}
