// Kit Setup - materials + textures for kits exported by stylized-blender-assets scripts.
// Installed automatically by the kit scripts (any Editor folder works).
//
// Every kit export writes "<Kit>_palette.json" next to its FBX files (and, when TEXTURE_STYLE is not
// "flat", seamless textures in "<folder>/Textures": <Mat>_albedo.png, <Mat>_normal.png, <Mat>_mask.png
// with R = metallic, G = occlusion, A = smoothness). When the palette is imported or changed this script:
//   1. sets the texture import settings (albedo sRGB, normal map type, mask linear, repeat wrap),
//   2. creates/updates the kit's materials in "<folder>/Materials" for the active pipeline
//      (URP Lit, HDRP Lit or Built-in Standard): base map, normal map, metallic/smoothness/occlusion
//      from the mask, tiling (1 UV unit = 1 m, so tiling = 1 / tile metres), colour when untextured,
//   3. re-imports the folder's FBX files so KitModelPostprocessor swaps the materials in by name.
// Materials in "<folder>/Materials" belong to the kit and follow the palette on every export.
// A material with the same name (or name + "_Toon") anywhere else is yours: it is used and never touched.
// Menu: Tools > Kit > Set Up All Kits
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class KitSetup : AssetPostprocessor
{
#pragma warning disable 0649   // fields are filled by JsonUtility
    [Serializable] class PaletteEntry
    {
        public string name; public float[] color; public float metallic; public float roughness; public float tile;
        public string albedo, normal, mask;
    }
    [Serializable] class Palette { public string kit; public string style; public PaletteEntry[] materials; }
#pragma warning restore 0649

    const string Suffix = "_palette.json";
    static readonly List<string> pending = new List<string>();

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var p in imported)
            if (p.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) && !pending.Contains(p))
                pending.Add(p);
        if (pending.Count > 0)
            EditorApplication.delayCall += RunPending;
    }

    static void RunPending()
    {
        if (pending.Count == 0) return;
        var paths = new List<string>(pending);
        pending.Clear();
        foreach (var p in paths) SetUpKit(p);
    }

    [MenuItem("Tools/Kit/Set Up All Kits")]
    static void SetUpAll()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("_palette"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) continue;
            SetUpKit(p);
            n++;
        }
        if (n == 0)
            EditorUtility.DisplayDialog("Kit Setup", "No *_palette.json found under Assets.\n\n" +
                "Run a kit script with its export folder inside this Unity project first.", "OK");
    }

    static void SetUpKit(string palettePath)
    {
        Palette pal;
        try { pal = JsonUtility.FromJson<Palette>(File.ReadAllText(palettePath)); }
        catch (Exception e) { Debug.LogError($"[Kit Setup] Could not read {palettePath}: {e.Message}"); return; }
        if (pal == null || pal.materials == null) return;

        string folder = Path.GetDirectoryName(palettePath).Replace('\\', '/');
        string texFolder = folder + "/Textures";
        string matFolder = folder + "/Materials";
        if (!AssetDatabase.IsValidFolder(matFolder))
            AssetDatabase.CreateFolder(folder, "Materials");

        // 1. texture import settings (only re-imports textures whose settings change)
        foreach (var e in pal.materials)
        {
            ConfigureTexture(texFolder, e.albedo, TextureImporterType.Default, true);
            ConfigureTexture(texFolder, e.normal, TextureImporterType.NormalMap, false);
            ConfigureTexture(texFolder, e.mask, TextureImporterType.Default, false);
        }

        // 2. materials
        Shader shader = PipelineShader();
        int created = 0, updated = 0, userOwned = 0;
        foreach (var e in pal.materials)
        {
            string ownPath = matFolder + "/" + e.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(ownPath);
            if (m == null)
            {
                if (ExistsElsewhere(e.name, matFolder)) { userOwned++; continue; }
                m = new Material(shader) { name = e.name };
                AssetDatabase.CreateAsset(m, ownPath);
                created++;
            }
            else updated++;
            if (m.shader != shader) m.shader = shader;
            Apply(m, e, texFolder);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();

        // 3. re-import this kit's FBX files
        int fbx = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetDirectoryName(p).Replace('\\', '/') != folder) continue;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                fbx++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        if (Type.GetType("KitModelPostprocessor") == null)
            Debug.LogWarning("[Kit Setup] KitModelPostprocessor.cs not found in an Editor folder: materials and " +
                             "colliders will not be hooked up to the FBX files without it.");
        Debug.Log($"[Kit Setup] {pal.kit} ({pal.style}): {created} material(s) created, {updated} updated, " +
                  $"{userOwned} of yours kept, {fbx} FBX re-imported - {folder}, shader {shader.name}.");
    }

    static void ConfigureTexture(string texFolder, string file, TextureImporterType type, bool srgb)
    {
        if (string.IsNullOrEmpty(file)) return;
        string path = texFolder + "/" + file;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool dirty = false;
        if (ti.textureType != type) { ti.textureType = type; dirty = true; }
        if (type != TextureImporterType.NormalMap && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
        if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
        if (dirty) ti.SaveAndReimport();
    }

    static Texture2D Tex(string texFolder, string file) =>
        string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(texFolder + "/" + file);

    static bool ExistsElsewhere(string name, string matFolder)
    {
        foreach (var target in new[] { name, name + "_Toon" })
            foreach (var guid in AssetDatabase.FindAssets(target + " t:Material"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == target &&
                    Path.GetDirectoryName(p).Replace('\\', '/') != matFolder)
                    return true;
            }
        return false;
    }

    static Shader PipelineShader()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        string type = rp == null ? "" : rp.GetType().Name;
        string want = type.Contains("Universal") ? "Universal Render Pipeline/Lit"
                    : type.Contains("HD") ? "HDRP/Lit" : "Standard";
        return Shader.Find(want) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
    }

    static void SetTex(Material m, string prop, Texture2D t)
    {
        if (m.HasProperty(prop)) m.SetTexture(prop, t);
    }

    static void Keyword(Material m, string kw, bool on)
    {
        if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
    }

    static void Apply(Material m, PaletteEntry e, string texFolder)
    {
        var albedo = Tex(texFolder, e.albedo);
        var normal = Tex(texFolder, e.normal);
        var mask = Tex(texFolder, e.mask);
        // Blender colours are linear; Unity colour properties are authored in sRGB.
        var c = new Color(e.color[0], e.color[1], e.color[2], 1f).gamma;
        var tint = albedo != null ? Color.white : c;            // albedo already carries the colour
        float smooth = Mathf.Clamp01(1f - e.roughness);
        float tiling = e.tile > 0f ? 1f / e.tile : 1f;           // UVs are in metres
        var scale = new Vector2(tiling, tiling);

        // colour
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);        // URP / HDRP
        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);                // Built-in
        SetTex(m, "_BaseMap", albedo);                                          // URP
        SetTex(m, "_MainTex", albedo);                                          // Built-in (URP mirrors it)
        SetTex(m, "_BaseColorMap", albedo);                                     // HDRP
        foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
            if (m.HasProperty(p)) m.SetTextureScale(p, scale);

        // normal
        SetTex(m, "_BumpMap", normal);                                          // URP / Built-in
        SetTex(m, "_NormalMap", normal);                                        // HDRP
        if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
        Keyword(m, "_NORMALMAP", normal != null);
        Keyword(m, "_NORMALMAP_TANGENT_SPACE", normal != null);

        // metallic / smoothness / occlusion
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", e.metallic);
        if (mask != null)
        {
            SetTex(m, "_MetallicGlossMap", mask);                               // URP / Built-in: R metal, A smooth
            SetTex(m, "_OcclusionMap", mask);                                   // URP / Built-in read G
            SetTex(m, "_MaskMap", mask);                                        // HDRP: R metal, G AO, A smooth
            if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);  // metallic alpha
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);    // URP: map alpha x this
            if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 1f);
            if (m.HasProperty("_OcclusionStrength")) m.SetFloat("_OcclusionStrength", 1f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        }
        else
        {
            SetTex(m, "_MetallicGlossMap", null); SetTex(m, "_OcclusionMap", null); SetTex(m, "_MaskMap", null);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        }
        Keyword(m, "_METALLICSPECGLOSSMAP", mask != null);                      // URP
        Keyword(m, "_METALLICGLOSSMAP", mask != null);                          // Built-in
        Keyword(m, "_OCCLUSIONMAP", mask != null);
        Keyword(m, "_MASKMAP", mask != null);                                   // HDRP
        Keyword(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", false);
    }
}
