using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace RailTracer
{
    // Builds the HUD's TextMeshPro fonts from Fonts/*.ttf into Resources/Fonts (Hud.cs loads them by name):
    // "Anton SDF" for numbers and titles, "BarlowCondensed SDF" (Medium) with the Bold face in its weight table
    // for everything else. Runs by itself whenever they're missing; menu Rail Tracer > Set Up Fonts rebuilds them.
    static class FontSetup
    {
        const string Src = "Assets/RailTracer/Fonts/", Dst = "Assets/RailTracer/Resources/Fonts/";

        [InitializeOnLoadMethod]
        static void Auto() => EditorApplication.delayCall += () =>
        {
            if (!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dst + "Anton SDF.asset") || !AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dst + "BarlowCondensed SDF.asset"))
                Build();
            if (!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Dst + "Kalam SDF.asset")) BuildSketch();
        };

        // The sketch HUD's hand-lettered Kalam (Update 19): "Kalam SDF" (Regular) with "Kalam-Bold SDF" as its Bold.
        [MenuItem("Rail Tracer/Set Up Sketch Font")]
        static void BuildSketch()
        {
            var bold = Make("Kalam-Bold", "Kalam-Bold SDF");
            var regular = Make("Kalam-Regular", "Kalam SDF");
            if (!bold || !regular) return;
            regular.fontWeightTable[7].regularTypeface = bold;
            EditorUtility.SetDirty(regular);
            AssetDatabase.SaveAssets();
            Debug.Log("Rail Tracer: Kalam fonts built in " + Dst);
        }

        [MenuItem("Rail Tracer/Set Up Fonts")]
        static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/RailTracer/Resources/Fonts")) AssetDatabase.CreateFolder("Assets/RailTracer/Resources", "Fonts");
            var anton = Make("Anton-Regular", "Anton SDF");
            var bold = Make("BarlowCondensed-Bold", "BarlowCondensed-Bold SDF");
            var body = Make("BarlowCondensed-Medium", "BarlowCondensed SDF");
            if (!anton || !bold || !body) return;
            body.fontWeightTable[7].regularTypeface = bold; // FontStyles.Bold uses the real Bold face
            EditorUtility.SetDirty(body);
            AssetDatabase.SaveAssets();
            Debug.Log("Rail Tracer: HUD fonts built in " + Dst);
        }

        // HUD icons (Resources/UI/Icons/*.png): crisp UI sprites. Fixes any imported before this script existed.
        const string Icons = "Assets/RailTracer/Resources/UI/Icons/";

        class IconImport : AssetPostprocessor
        {
            void OnPreprocessTexture()
            {
                if (!assetPath.StartsWith(Icons)) return;
                var t = (TextureImporter)assetImporter;
                t.textureType = TextureImporterType.Sprite;
                t.spriteImportMode = SpriteImportMode.Single;
                t.mipmapEnabled = false;
                t.alphaIsTransparency = true;
                t.textureCompression = TextureImporterCompression.Uncompressed;
            }
        }

        [InitializeOnLoadMethod]
        static void FixIcons() => EditorApplication.delayCall += () =>
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Icons.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter t && t.textureType != TextureImporterType.Sprite) t.SaveAndReimport();
            }
        };

        static TMP_FontAsset Make(string file, string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(Src + file + ".ttf");
            if (!font) { Debug.LogWarning($"Rail Tracer: {Src}{file}.ttf missing; the HUD keeps TextMeshPro's default font."); return null; }
            string path = Dst + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            // Dynamic, so anything outside the pre-baked ASCII still renders.
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
            fa.name = name;
            fa.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");
            AssetDatabase.CreateAsset(fa, path);
            fa.atlasTexture.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            return fa;
        }
    }
}
