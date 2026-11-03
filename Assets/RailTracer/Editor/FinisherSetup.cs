using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Brutal Finishers (knife set): every clip as Humanoid on the Unreal mannequin's avatar, starting after the two
    // bind-pose frames, with all their travel kept in the pose (a pair only lines up if both keep their moves). Then
    // Resources/Takedowns, the library the kills pick
    // from, if it doesn't exist yet (edit it freely; delete it to regenerate). Runs after each script reload and
    // whenever a clip in the pack imports.
    static class FinisherSetup
    {
        const string Pack = "Assets/Animations/Game/Brutal_Finishers_Hand_and_Knife/Animations/Paired_Knife/";
        const string StealthPack = "Assets/Animations/Game/StealthFinishers_KnifeAndHand/Animations/"; // Stealth Finishers - Knife and Hand
        const string Mannequin = "Assets/Animations/SK_Mannequin.FBX";
        const string Library = "Assets/RailTracer/Resources/Takedowns.asset";
        const int FirstFrame = 2; // frames 0-1 are the bind pose

        [InitializeOnLoadMethod]
        static void AutoRun() => EditorApplication.delayCall += Run;

        class OnImport : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Any(p => p.StartsWith(Pack) || p.StartsWith(StealthPack) || p == Mannequin)) EditorApplication.delayCall += Run;
            }
        }

        [MenuItem("Rail Tracer/Set Up Finishers")]
        static void Run()
        {
            if (!Directory.Exists(Pack) || !File.Exists(Mannequin)) return;
            var avatar = MannequinAvatar();
            if (!avatar) return;
            foreach (var pack in new[] { Pack, StealthPack })
                if (Directory.Exists(pack))
                    foreach (var path in Directory.GetFiles(pack, "*.FBX").Select(p => p.Replace('\\', '/'))) Humanoid(path, avatar);
            if (!File.Exists(Library)) BuildLibrary();
        }

        static Avatar MannequinAvatar()
        {
            if (!(AssetImporter.GetAtPath(Mannequin) is ModelImporter mi)) return null;
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Mannequin).OfType<Avatar>().FirstOrDefault();
            if (!avatar || !avatar.isHuman) { Debug.LogWarning("Rail Tracer: the mannequin didn't make a Humanoid avatar; finishers not set up."); return null; }
            return avatar;
        }

        static void Humanoid(string path, Avatar avatar)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return;
            bool dirty = mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || mi.sourceAvatar != avatar;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = avatar;
            var clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                dirty |= c.firstFrame < FirstFrame || c.loopTime || !c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ || !c.keepOriginalPositionXZ;
                c.firstFrame = Mathf.Max(c.firstFrame, FirstFrame);
                c.loopTime = false;
                c.lockRootRotation = c.lockRootHeightY = true; // turns and drops stay in the pose
                c.keepOriginalOrientation = c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = c.keepOriginalPositionXZ = true; // travel baked into the pose, from the clip's own origin
            }
            if (!dirty) return;
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        static AnimationClip Clip(string stem) =>
            AssetDatabase.LoadAllAssetsAtPath(Pack + "AnimSeq_Paired_Knife_" + stem + ".FBX").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        // Alex's picks: stealth by stance, brutal at random, the table shove whenever half cover is beside the victim.
        static void BuildLibrary()
        {
            var lib = ScriptableObject.CreateInstance<TakedownLibrary>();
            void Add(string name, KillKind kind, string stem, TakedownStance stance = TakedownStance.Any, string combo = null, bool cover = false)
            {
                var a = Clip(stem + "_Att");
                var v = Clip(stem + "_Vic");
                if (!a || !v) return;
                lib.takedowns.Add(new Takedown
                {
                    name = name, kind = kind, stance = stance, attacker = a, victim = v, needsCover = cover,
                    attacker2 = combo != null ? Clip(combo + "_Att") : null, victim2 = combo != null ? Clip(combo + "_Vic") : null,
                    victimFallsAfter = combo != null, // the slash combo leaves them standing
                });
            }
            Add("Heart Stab (crouched)", KillKind.Stealth, "Stab_Heart", TakedownStance.Crouched);
            Add("Shove Neck Slice (standing)", KillKind.Stealth, "Shove_NeckSlice_Quick", TakedownStance.Standing);
            Add("Duck and Back Stab", KillKind.Brutal, "DuckNBackStab");
            Add("Slash Combo", KillKind.Brutal, "Slash_MidSection", combo: "Slash_Face");
            Add("Shove to Table (half cover)", KillKind.Brutal, "Shove_toTable_Stab_Quick", cover: true);
            if (lib.takedowns.Count == 0) return; // clips not imported yet: try again next run
            AssetDatabase.CreateAsset(lib, Library);
            AssetDatabase.SaveAssets();
            Debug.Log($"Rail Tracer: takedown library made with {lib.takedowns.Count} finishers ({Library}).");
        }
    }
}
