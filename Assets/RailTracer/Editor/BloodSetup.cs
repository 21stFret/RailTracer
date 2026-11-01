using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RailTracer
{
    // Wires the RVFX blood pack (URP version, Assets/RVFX/BloodEffectsPack/1_URP) into the game:
    // creates Resources/Blood/BloodKit.asset if missing (never overwritten, so edit it freely) and adds the
    // Decal renderer feature the projected decals need. Runs after every script reload; cheap when nothing's missing.
    static class BloodSetup
    {
        const string KitPath = "Assets/RailTracer/Resources/Blood/BloodKit.asset";
        const string Pack = "Assets/RVFX/BloodEffectsPack/";

        [InitializeOnLoadMethod]
        static void AutoRun() => EditorApplication.delayCall += Run;

        [MenuItem("Rail Tracer/Set Up Blood")]
        static void Run()
        {
            if (!Directory.Exists(Pack + "1_URP")) return;
            AddDecalFeature();
            if (File.Exists(KitPath)) return;
            var kit = ScriptableObject.CreateInstance<BloodKit>();
            kit.splashes = Load("1_URP/Blood/Splash/Blood_Splash_0{0}_URP.prefab", 1, 2, 3, 4, 5);
            kit.goreSplashes = Load("1_URP/Blood/Splash/Blood_Splash_01_WithGut_URP.prefab");
            kit.gutBursts = Load("1_URP/Gut/Effect_Gut_SphericalSpread.prefab").Concat(Load("1_URP/Gut/Effect_Gut_Directional.prefab")).ToArray();
            kit.decals = Load("1_URP/Blood/Decal_Projector/BloodDecal_0{0}_Static_Projected_URP.prefab", 1, 2, 3, 4);
            kit.screens = Load("UIEffect/Prefab/BloodScreen_0{0}.prefab", 1, 2, 3); // 04 is hue-shifted green
            Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
            AssetDatabase.CreateAsset(kit, KitPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Rail Tracer: blood kit created at " + KitPath);
        }

        static GameObject[] Load(string pattern, params int[] variants) =>
            (variants.Length == 0 ? new[] { pattern } : variants.Select(v => string.Format(pattern, v)))
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(Pack + p)).Where(g => g).ToArray();

        // Same steps as URP's "Add Renderer Feature" button, for every renderer in the project.
        static void AddDecalFeature()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!data || data.rendererFeatures.Any(f => f is DecalRendererFeature)) continue;
                var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                feature.name = "Decal";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: added the Decal renderer feature to " + data.name);
            }
        }
    }
}
