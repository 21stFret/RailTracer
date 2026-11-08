using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // The NYC1950 character packs (Assets/NYC1950_CH01 and _CH02) in our materials instead of their 4k textures, and
    // the guards' models.
    //   SK_CH0#_FullClothes.fbx: the rigged ones (Unreal mannequin skeleton), exported from Alex's Unreal projects and
    //     split in Blender so every part already has one of our material names (the packs' clothes are atlases shared
    //     by several parts). Imported Humanoid; Resources/Anim/Guard.prefab (CH01: shirt sleeves, chain, watch) and
    //     Guard_CH02.prefab (CH02: double-breasted suit, tie, sunglasses, ring) are made from them, with ragdolls.
    //   SM_CH01_*.FBX: CH01's static parts in a T-pose; each dressed by its file (hat, shirt...), not its material name.
    // Ours: hats Trn_Felt (the props kit's fedora felt), chain, watch and ring Trn_Brass, shirts and vest Chr_Shirt,
    // trousers and jacket Chr_Suit (Fabric.mat copies: our weave, tinted), shoes Chr_Shoes, glasses Chr_Glasses, skin
    // Chr_Skin, beards, brows and hair Chr_Hair, eyes Chr_Eye, mouths Chr_Mouth (flat, the game's own tones). The static
    // parts' UCX_ collision hulls are dropped.
    // The Chr_ materials and the prefabs are made once (never overwritten: tune them freely; delete to rebuild).
    // Runs before KitModelPostprocessor, so that finds every material by name.
    class CharacterPackSetup : AssetPostprocessor
    {
        const string Packs = "Assets/NYC1950_";
        const string Dir = "Assets/RailTracer/Characters/Materials/";
        static readonly (string model, string prefab)[] Rigged =
        {
            ("Assets/NYC1950_CH01/Meshes/SK_CH01_FullClothes.fbx", "Assets/RailTracer/Resources/Anim/Guard.prefab"),
            ("Assets/NYC1950_CH02/Meshes/SK_CH02_FullClothes.fbx", "Assets/RailTracer/Resources/Anim/Guard_CH02.prefab"),
        };

        public override int GetPostprocessOrder() => -10;

        [InitializeOnLoadMethod]
        static void Auto() => EditorApplication.delayCall += Make;

        [MenuItem("Rail Tracer/Set Up Character Pack")]
        static void Make()
        {
            var packs = new[] { "Assets/NYC1950_CH01", "Assets/NYC1950_CH02" }.Where(AssetDatabase.IsValidFolder).ToArray();
            if (packs.Length == 0) return;
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
            Mat("Chr_Glasses", flat, new Color(0.02f, 0.02f, 0.025f), 0.92f); // dark glasses, frames and lenses
            Mat("Chr_Shirt", fabric, new Color(0.85f, 0.82f, 0.74f), 0.12f);
            Mat("Chr_Suit", fabric, new Color(0.15f, 0.15f, 0.17f), 0.12f);
            Object.DestroyImmediate(flat);
            if (made)
            {
                AssetDatabase.SaveAssets();
                foreach (var g in AssetDatabase.FindAssets("t:Model", packs))
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
                Debug.Log("Rail Tracer: character pack materials made in " + Dir);
            }
            foreach (var (model, prefab) in Rigged) MakeGuard(model, prefab);
        }

        // A guard prefab from a rigged model (the game owns position: no root motion), with a ragdoll.
        static void MakeGuard(string Rigged, string Guard)
        {
            if (File.Exists(Guard)) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Rigged);
            var animator = model ? model.GetComponent<Animator>() : null;
            if (!animator || !animator.avatar || !animator.avatar.isHuman) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.GetComponent<Animator>().applyRootMotion = false;
            PrefabUtility.SaveAsPrefabAsset(go, Guard);
            Object.DestroyImmediate(go);
            RagdollSetup.Run(Guard, false);
            Debug.Log("Rail Tracer: guard model made at " + Guard);
        }

        // Batch check: unity -batchmode -projectPath . -executeMethod RailTracer.CharacterPackSetup.Check -quit
        static void Check()
        {
            EditorApplication.ExecuteMenuItem("Tools/Kit/Set Up All Kits"); // its import-time pass is deferred, which batch mode never runs
            Debug.Log("CH01 CHECK kit materials: " + string.Join(", ", new[] { "Trn_Shellac", "Trn_Label", "Trn_Felt" }
                .Select(n => n + (AssetDatabase.FindAssets(n + " t:Material").Length > 0 ? " ok" : " MISSING"))));
            Make();
            foreach (var (Rigged, Guard) in CharacterPackSetup.Rigged) Check(Rigged, Guard);
        }

        static void Check(string Rigged, string Guard)
        {
            Debug.Log("CH01 CHECK ---- " + Path.GetFileName(Rigged));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Rigged);
            var a = model ? model.GetComponent<Animator>() : null;
            Debug.Log($"CH01 CHECK model={(bool)model} avatar={(a && a.avatar ? a.avatar.name : "none")} human={(a && a.avatar && a.avatar.isHuman)} valid={(a && a.avatar && a.avatar.isValid)}");
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(Guard);
            if (!g) { Debug.Log("CH01 CHECK no guard prefab"); return; }
            var ga = g.GetComponent<Animator>();
            var tmp = Object.Instantiate(g);
            var ta = tmp.GetComponent<Animator>();
            foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm,
                         HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightThumbProximal, HumanBodyBones.LeftFoot, HumanBodyBones.RightToes })
                Debug.Log($"CH01 CHECK bone {b} = {(ta.GetBoneTransform(b) ? ta.GetBoneTransform(b).name : "MISSING")}");
            foreach (var r in tmp.GetComponentsInChildren<Renderer>())
                Debug.Log($"CH01 CHECK renderer {r.name}: {string.Join(", ", r.sharedMaterials.Select(m => m ? m.name : "null"))} bounds {r.bounds.size}");
            Debug.Log($"CH01 CHECK ragdoll bodies={tmp.GetComponentsInChildren<Rigidbody>(true).Length} rootMotion={ga.applyRootMotion}");
            Object.DestroyImmediate(tmp);
        }

        void OnPreprocessModel()
        {
            if (!Rigged.Any(r => r.model == assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.autoGenerateAvatarMappingIfUnspecified = true;
            mi.importAnimation = false;
        }

        // Which of our materials a part wears: already ours (the rigged model), else by file first (the shared
        // atlases), then by material name (the static body).
        static string For(string file, string material)
        {
            var name = material.Split('.')[0];
            if (name.StartsWith("Chr_") || name.StartsWith("Trn_")) return name;
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
            if (!assetPath.StartsWith(Packs)) return;
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
