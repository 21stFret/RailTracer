using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Keeps Resources/Kit/TrainKit.asset listing every FBX in the train kit's export folder, so a new kit export
    // (e.g. more modules) is picked up by the game without any wiring. Runs after script reloads and FBX imports.
    class TrainKitSetup : AssetPostprocessor
    {
        const string KitPath = "Assets/RailTracer/Resources/Kit/TrainKit.asset";
        const string Folder = "Assets/RailTracer/Train Assets";

        [InitializeOnLoadMethod]
        static void AutoRun() => EditorApplication.delayCall += Refresh;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Any(p => p.StartsWith(Folder) && p.EndsWith(".fbx")))
                EditorApplication.delayCall += Refresh;
        }

        [MenuItem("Rail Tracer/Refresh Train Kit")]
        static void Refresh()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return;
            var modules = AssetDatabase.FindAssets("t:Model", new[] { Folder })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m).OrderBy(m => m.name).ToArray();
            var kit = AssetDatabase.LoadAssetAtPath<TrainKit>(KitPath);
            if (kit && kit.modules != null && kit.modules.SequenceEqual(modules)) return;
            if (!kit)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<TrainKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            kit.modules = modules;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log($"Rail Tracer: train kit lists {modules.Length} modules.");
        }
    }
}
