using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // The NYC1950_CH01 character pack (Assets/NYC1950_CH01/Meshes, Unreal-style static meshes) in our materials instead
    // of its 4k textures. Its materials are names only, and MI_CH01_Clothes01 / 02 are atlases shared by several parts,
    // so each part is dressed by its file (hat, shirt...), not its material name:
    //   hat Trn_Felt (the props kit's fedora felt), necklace and watch Trn_Brass, shirt and vest Chr_Shirt and trousers
    //   Chr_Suit (Fabric.mat copies: our weave, tinted), shoes Chr_Shoes, skin Chr_Skin, beard, brows and hair Chr_Hair,
    //   eyes Chr_Eye, mouth Chr_Mouth (flat colours, the game's own tones). The UCX_ collision hulls are dropped.
    // The Chr_ materials are made once in Characters/Materials (never overwritten: tune them freely) and the pack is
    // re-imported to pick them up. Runs before KitModelPostprocessor, so that finds every material by name.
    class CharacterPackMaterials : AssetPostprocessor
    {
        const string Pack = "Assets/NYC1950_CH01/";
        const string Dir = "Assets/RailTracer/Characters/Materials/";

        public override int GetPostprocessOrder() => -10;

        [InitializeOnLoadMethod]
        static void Auto() => EditorApplication.delayCall += Make;

        [MenuItem("Rail Tracer/Set Up Character Pack Materials")]
        static void Make()
        {
            if (!AssetDatabase.IsValidFolder(Pack.TrimEnd('/'))) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var fabric = AssetDatabase.LoadAssetAtPath<Material>("Assets/RailTracer/Resources/Materials/Fabric.mat");
            if (!lit || !fabric) return;
            Directory.CreateDirectory(Dir);
            bool made = false;
            void Mat(string name, Material from, Color c, float smooth)
            {
                string path = Dir + name + ".mat";
                if (File.Exists(path)) return;
                var m = new Material(from) { name = name };
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                AssetDatabase.CreateAsset(m, path);
                made = true;
            }
            var flat = new Material(lit);
            Mat("Chr_Skin", flat, new Color(0.78f, 0.58f, 0.45f), 0.35f);   // Game skinColor
            Mat("Chr_Hair", flat, new Color(0.16f, 0.1f, 0.05f), 0.3f);     // Game hairColor, as it reads over the Mixamo texture
            Mat("Chr_Eye", flat, new Color(0.08f, 0.05f, 0.03f), 0.85f);
            Mat("Chr_Mouth", flat, new Color(0.45f, 0.18f, 0.17f), 0.5f);
            Mat("Chr_Shoes", flat, new Color(0.03f, 0.03f, 0.03f), 0.55f);  // Game shoesColor, polished
            Mat("Chr_Shirt", fabric, new Color(0.85f, 0.82f, 0.74f), 0.12f);
            Mat("Chr_Suit", fabric, new Color(0.15f, 0.15f, 0.17f), 0.12f);
            Object.DestroyImmediate(flat);
            if (!made) return;
            AssetDatabase.SaveAssets();
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { Pack.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            Debug.Log("Rail Tracer: character pack materials made in " + Dir);
        }

        // Which of our materials a part wears: by file first (the shared atlases), then by material name (the body).
        static string For(string file, string material)
        {
            if (file.Contains("Hat")) return "Trn_Felt";
            if (file.Contains("Necklace") || file.Contains("Watch")) return "Trn_Brass";
            if (file.Contains("Shirt") || file.Contains("Underwear")) return "Chr_Shirt";
            if (file.Contains("Pants")) return "Chr_Suit";
            if (file.Contains("Shoes")) return "Chr_Shoes";
            if (file.Contains("Eye")) return "Chr_Eye";
            if (file.Contains("Mouth")) return "Chr_Mouth";
            return material.Contains("Hair") ? "Chr_Hair" : "Chr_Skin"; // the body: head, body, beard and brows, hair
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(Pack)) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("UCX_")).ToList())
                Object.DestroyImmediate(t.gameObject, true); // Unreal collision hulls
            string file = Path.GetFileNameWithoutExtension(assetPath);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(m => Find(For(file, m ? m.name : "")) ?? m).ToArray();
        }

        Material Find(string name)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(path) != name) continue;
                context.DependsOnSourceAsset(path); // re-import if it moves or changes
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }
    }
}
