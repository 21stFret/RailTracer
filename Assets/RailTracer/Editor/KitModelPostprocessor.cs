// Put in any Editor folder (e.g. Assets/Editor/). Runs automatically whenever an FBX is imported or
// re-exported. ONE copy per project - if an older AtlantisModelPostprocessor.cs exists, delete it,
// otherwise colliders get added twice.
//
// Contract with the Blender kit scripts (stylized-blender-assets):
//   * Material names: Blender material "Trn_Wood" -> Unity material asset named "Trn_Wood"
//     (or "Trn_Wood_Toon"). Any name works; Blender ".001" suffixes are ignored.
//     Set MaterialPrefixes to limit matching to your kit prefixes (empty = match every name).
//   * Object names:   "..._COL"   -> invisible MeshCollider (renderer removed, materials ignored)
//                     "..._SOLID" -> visible mesh + MeshCollider
//   * Static flag on collider objects (they are level geometry).
// Missing materials are reported once per import in the Console so nothing fails silently.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class KitModelPostprocessor : AssetPostprocessor
{
    static readonly string[] MaterialPrefixes = { };            // e.g. { "Atl_", "Trn_" }
    static readonly string[] MaterialSuffixes = { "", "_Toon" };
    const string ColliderOnlySuffix = "_COL";
    const string SolidSuffix = "_SOLID";
    static readonly Regex BlenderDuplicate = new Regex(@"\.\d{3}$");

    void OnPostprocessModel(GameObject root)
    {
        var missing = new HashSet<string>();
        var cache = new Dictionary<string, Material>();

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string name = BlenderDuplicate.Replace(t.name, "");
            bool colOnly = name.EndsWith(ColliderOnlySuffix);
            bool solid = name.EndsWith(SolidSuffix);

            // ---- colliders by object-name suffix
            if (colOnly || solid)
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    t.gameObject.isStatic = true;
                }
                if (colOnly)
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr != null) Object.DestroyImmediate(mr, true);
                    continue;                                       // no materials to swap
                }
            }

            // ---- materials: swap embedded FBX materials for the project's real ones
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string mname = BlenderDuplicate.Replace(mats[i].name, "");
                if (MaterialPrefixes.Length > 0 && !MaterialPrefixes.Any(p => mname.StartsWith(p))) continue;
                if (!cache.TryGetValue(mname, out var found))
                    cache[mname] = found = FindMaterial(mname);
                if (found != null) { mats[i] = found; changed = true; }
                else missing.Add(mname);
            }
            if (changed) r.sharedMaterials = mats;
        }

        if (missing.Count > 0)
            Debug.LogWarning($"[Kit import] {assetPath}: no Unity material found for " +
                             $"{string.Join(", ", missing)}. Create material assets with those exact names.");
    }

    Material FindMaterial(string name)
    {
        foreach (var suffix in MaterialSuffixes)
        {
            string target = name + suffix;
            foreach (var guid in AssetDatabase.FindAssets($"{target} t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != target) continue;
                context.DependsOnSourceAsset(path);       // re-import the model if this material moves/changes
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null) return m;
            }
        }
        return null;
    }
}
