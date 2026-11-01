using System.IO;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Makes Resources/Materials/Fabric.mat (URP Lit with the woven FabricWeave textures) if it's missing; never
    // overwritten, so tune its tiling, bump and smoothness freely. Game.cs puts it on the suits (tintedParts) and
    // tints it per character. Menu Rail Tracer > Set Up Fabric rebuilds it (delete the .mat first).
    static class FabricSetup
    {
        const string Dir = "Assets/RailTracer/Resources/Materials/";

        [InitializeOnLoadMethod]
        static void Auto() => EditorApplication.delayCall += Build;

        [MenuItem("Rail Tracer/Set Up Fabric")]
        static void Build()
        {
            string path = Dir + "Fabric.mat";
            if (File.Exists(path)) return;
            var normal = (TextureImporter)AssetImporter.GetAtPath(Dir + "FabricWeave_Normal.png");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!normal || !shader) return;
            if (normal.textureType != TextureImporterType.NormalMap) { normal.textureType = TextureImporterType.NormalMap; normal.SaveAndReimport(); }
            var m = new Material(shader) { name = "Fabric" };
            var weave = new Vector2(28f, 28f); // weave repeats across the 0..1 UV square
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "FabricWeave.png"));
            m.SetTextureScale("_BaseMap", weave);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "FabricWeave_Normal.png"));
            m.SetFloat("_BumpScale", 0.8f);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", 0.12f); // cloth: matte with a faint sheen
            m.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            Debug.Log("Rail Tracer: fabric material created at " + path);
        }
    }
}
