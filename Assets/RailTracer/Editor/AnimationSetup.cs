using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RailTracer
{
    // Builds the character setup in Resources/Anim: the Mixamo character, animated with the Synty base
    // locomotion pack (in-place Humanoid clips) plus two Mixamo clips Synty lacks (pistol idle, flip). Creates only what's missing, never overwrites,
    // so edit the results freely; delete one and run "Rail Tracer > Set Up Animations" to regenerate it.
    static class AnimationSetup
    {
        const string Dir = "Assets/RailTracer/Resources/Anim/";
        const string Mixamo = "Assets/RailTracer/Mixamo/";
        const string Synty = "Assets/Synty/AnimationBaseLocomotion/";
        const string Masc = Synty + "Animations/Polygon/Masculine/";

        // Runs after every script reload; it only touches what's missing or misconfigured, so it's cheap and safe.
        [InitializeOnLoadMethod]
        static void AutoRun() => EditorApplication.delayCall += Run;

        // A clip dropped into RailTracer/Mixamo (e.g. "Pistol Shoot.fbx") is wired up as soon as it imports.
        class OnImport : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Any(p => p.StartsWith(Mixamo) && p.EndsWith(".fbx"))) EditorApplication.delayCall += Run;
            }
        }

        [MenuItem("Rail Tracer/Set Up Animations")]
        static void Run()
        {
            if (!File.Exists(Dir + "Character.prefab") || !File.Exists(Dir + "Player.controller") || !File.Exists(Dir + "Enemy.controller"))
                Generate();
            // Keep the idle's import right even after the controllers exist, and pin its feet with Foot IK.
            MixamoClip(Mixamo + "Pistol Idle.fbx", true, inPlace: false);
            if (FootIK(Dir + "Enemy.controller", "Idle"))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: Foot IK on for the enemy idle.");
            }
            // Paired kill clips, added as states to the existing controllers (keeping your edits):
            // "Takedown" = brutal kill, "StealthKill" = quiet kill.
            bool added = false;
            void Pair(string state, string attackerPath, string victimPath)
            {
                var attacker = MixamoClip(attackerPath, false);
                var victim = MixamoClip(victimPath, false);
                if (attacker) added |= AddTakedown(Dir + "Player.controller", attacker, state, "Idle", "CrouchIdle");
                if (victim) added |= AddTakedown(Dir + "Enemy.controller", victim, state);
            }
            Pair("Takedown", Dir + "Double Leg Takedown - Attacker.fbx", Dir + "Double Leg Takedown - Victim.fbx");
            Pair("StealthKill", Mixamo + "Stealth Assassination.fbx", Mixamo + "Stealth Assassination_Victim.fbx");
            if (added)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: paired kill states added to the controllers.");
            }
            // A "Finisher" state on both controllers, on a placeholder clip the game overrides with each takedown's
            // own (Resources/Takedowns): you return to your idle after it, the victim stays down.
            if (AddTakedown(Dir + "Player.controller", Placeholder("Finisher Attacker"), "Finisher", "Idle", "CrouchIdle")
                | AddTakedown(Dir + "Enemy.controller", Placeholder("Finisher Victim"), "Finisher")
                | AddTakedown(Dir + "Player.controller", Placeholder("Finisher Attacker 2"), "Finisher2", "Idle", "CrouchIdle") // a combo's second clip
                | AddTakedown(Dir + "Enemy.controller", Placeholder("Finisher Victim 2"), "Finisher2"))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: \"Finisher\" states added.");
            }
            // The bloodied leap past the normal range plays "StylishFlip" (Game picks it over "Flip").
            var stylish = MixamoClip(Mixamo + "Stylish Flip.fbx", false);
            if (stylish && AddTakedown(Dir + "Player.controller", stylish, "StylishFlip", "Idle", "CrouchIdle"))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: player \"StylishFlip\" state added.");
            }
            // Broken enemies kneel ("BegStart", Praying) then beg ("Beg", PrayingLoop); Game plays it instead of the cower.
            var pray = MixamoClip(Mixamo + "Praying.fbx", false);
            var prayLoop = MixamoClip(Mixamo + "PrayingLoop.fbx", true);
            if (pray && prayLoop && AddBeg(Dir + "Enemy.controller", pray, prayLoop))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: enemy begging states added.");
            }
            // Enemies fire with a "Shoot" state once a Mixamo "Pistol Shoot.fbx" is in RailTracer/Mixamo.
            var shoot = MixamoClip(Mixamo + "Pistol Shoot.fbx", false);
            if (shoot && AddShoot(Dir + "Enemy.controller", shoot))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: enemy \"Shoot\" state added.");
            }
            if (UpgradeLocomotion(Dir + "Player.controller", "Walk") | UpgradeLocomotion(Dir + "Enemy.controller", "Move"))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: walk states now blend walk/run by speed, with stride matched to movement.");
            }
            if (AddRelaxedIdle(Dir + "Enemy.controller"))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("Rail Tracer: enemies now stand relaxed (gun holstered) until \"Armed\" is set.");
            }
        }

        // Unaware enemies stand relaxed: a "Relaxed" state on Synty's standing idle, swapped with the pistol "Idle"
        // by the "Armed" bool (Game.cs sets it whenever the enemy isn't Unaware). Once, keeping your edits.
        static bool AddRelaxedIdle(string path)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var clip = Clip(Masc + "Idle/A_Idle_Standing_Masc.fbx");
            if (!ac || !clip || ac.parameters.Any(p => p.name == "Armed")) return false;
            var sm = ac.layers[0].stateMachine;
            var idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle");
            var move = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Move");
            if (!idle) return false;
            ac.AddParameter("Armed", AnimatorControllerParameterType.Bool);
            var relaxed = State(sm, "Relaxed", clip);
            relaxed.iKOnFeet = true;
            Link(idle, relaxed, ("Armed", false));
            Link(relaxed, idle, ("Armed", true));
            if (move) Link(relaxed, move, ("Moving", true));
            sm.defaultState = relaxed;
            EditorUtility.SetDirty(ac);
            return true;
        }

        // Feet that match the ground: the walk state becomes a walk->run blend on "Speed" (the character's own
        // m/s, i.e. world speed / scale), and walk + crouch-walk play at "StrideScale". The clips' real speeds,
        // measured from Synty's root-motion twins, are stored as the WalkSpeed / RunSpeed / CrouchSpeed defaults,
        // which Game.cs reads to work out StrideScale. Edit those defaults in the controller to fine-tune.
        static bool UpgradeLocomotion(string path, string walkState)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!ac || ac.parameters.Any(p => p.name == "Speed")) return false;
            var sm = ac.layers[0].stateMachine;
            var walkS = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == walkState);
            var walk = Clip(Masc + "Locomotion/Walk/A_Walk_F_Masc.fbx");
            var run = Clip(Masc + "Locomotion/Run/A_Run_F_Masc.fbx");
            if (!walkS || !walk || !run) return false;
            float Speed(string twin, float fallback)
            {
                var clip = Clip(Masc + twin);
                float v = clip ? new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude : 0f;
                return v > 0.1f ? v : fallback;
            }
            float walkSpeed = Speed("Locomotion/Walk/A_Walk_F_RootMotion_Masc.fbx", 1.4f);
            float runSpeed = Mathf.Max(walkSpeed + 0.5f, Speed("Locomotion/Run/A_Run_F_RootMotion_Masc.fbx", 3.5f));
            float crouchSpeed = Speed("Locomotion/Crouch/A_Crouch_FwdStrafeF_RootMotion_Masc.fbx", 1f);
            void Param(string name, float value) => ac.AddParameter(new AnimatorControllerParameter
                { name = name, type = AnimatorControllerParameterType.Float, defaultFloat = value });
            Param("Speed", 0f);
            Param("StrideScale", 1f);
            Param("WalkSpeed", walkSpeed);
            Param("RunSpeed", runSpeed);
            Param("CrouchSpeed", crouchSpeed);

            var tree = new BlendTree { name = "Walk/Run", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ac);
            tree.AddChild(walk, walkSpeed);
            tree.AddChild(run, runSpeed);
            walkS.motion = tree;
            walkS.speedParameterActive = true;
            walkS.speedParameter = "StrideScale";
            var crouch = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "CrouchWalk");
            if (crouch)
            {
                crouch.speedParameterActive = true;
                crouch.speedParameter = "StrideScale";
            }
            EditorUtility.SetDirty(ac);
            return true;
        }

        static void Generate()
        {
            var idle = Clip(Masc + "Idle/A_Idle_Standing_Masc.fbx");
            var crouchIdle = Clip(Masc + "Idle/A_Idle_Crouching_Masc.fbx");
            var walk = Clip(Masc + "Locomotion/Walk/A_Walk_F_Masc.fbx");
            var crouchWalk = Clip(Masc + "Locomotion/Crouch/A_Crouch_FwdStrafeF_Masc.fbx");
            var toCrouch = Clip(Masc + "Transitions/Stand_ToCrouch/A_Stand_ToCrouch_Masc.fbx");
            var toStand = Clip(Masc + "Transitions/Crouch_ToStand/A_Crouch_ToStand_Masc.fbx");
            var pistolIdle = MixamoClip(Mixamo + "Pistol Idle.fbx", true, inPlace: false);
            var flip = MixamoClip(Mixamo + "Front Twist Flip.fbx", false);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Mixamo + "Pistol Idle.fbx"); // the Mixamo character mesh
            if (new Object[] { idle, crouchIdle, walk, crouchWalk, toCrouch, toStand, pistolIdle, flip, model }.Any(o => !o))
            {
                Debug.LogWarning("Rail Tracer: animation setup is missing Synty or Mixamo assets; nothing generated.");
                return;
            }
            Directory.CreateDirectory(Dir);
            if (!File.Exists(Dir + "Character.prefab")) BuildCharacter(model);
            if (!File.Exists(Dir + "Enemy.controller")) BuildEnemy(pistolIdle, walk, crouchIdle);
            if (!File.Exists(Dir + "Player.controller")) BuildPlayer(idle, walk, crouchIdle, crouchWalk, toCrouch, toStand, flip);
            AssetDatabase.SaveAssets();
            Debug.Log("Rail Tracer: animations set up in " + Dir);
        }

        static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        // Mixamo FBX as Humanoid. Root XZ is based on the centre of mass and not baked, so the travel becomes
        // root motion: discarded while Apply Root Motion is off (in place), or moving the character when the game
        // turns it on (the takedown). "Original" would leave it in the pose: Mixamo has no root bone.
        // inPlace: false for clips that stand still (idles). Their gentle hip sway must stay in the pose exactly as
        //   authored (XZ baked, based on the original); discarding it as centre-of-mass motion makes the feet slide.
        static AnimationClip MixamoClip(string path, bool loop, bool inPlace = true)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) return null;
            bool dirty = mi.animationType != ModelImporterAnimationType.Human;
            if (dirty)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.autoGenerateAvatarMappingIfUnspecified = true;
            }
            var clips = mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations; // keep any trimming
            foreach (var c in clips)
            {
                dirty |= c.loopTime != loop || !c.lockRootRotation || !c.lockRootHeightY
                    || c.lockRootPositionXZ == inPlace || c.keepOriginalPositionXZ == inPlace;
                c.loopTime = loop;
                c.lockRootRotation = true;   // bake rotation into pose
                c.lockRootHeightY = true;    // bake height into pose (flips still leave the ground)
                c.lockRootPositionXZ = !inPlace;
                c.keepOriginalOrientation = c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = !inPlace;
            }
            if (dirty)
            {
                mi.clipAnimations = clips;
                mi.SaveAndReimport();
            }
            return Clip(path);
        }

        // Character prefab from a Humanoid model; Synty clips retarget onto it through the avatar.
        static void BuildCharacter(GameObject model)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var animator = go.GetComponent<Animator>();
            if (!animator) animator = go.AddComponent<Animator>();
            animator.applyRootMotion = false; // the game owns position
            PrefabUtility.SaveAsPrefabAsset(go, Dir + "Character.prefab");
            Object.DestroyImmediate(go);
        }

        // Params: Moving, Broken.
        static void BuildEnemy(AnimationClip idle, AnimationClip walk, AnimationClip cower)
        {
            var ac = AnimatorController.CreateAnimatorControllerAtPath(Dir + "Enemy.controller");
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Broken", AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            var idleState = State(sm, "Idle", idle);
            var move = State(sm, "Move", walk);
            var cowerState = State(sm, "Cower", cower);
            sm.defaultState = idleState;
            Link(idleState, move, ("Moving", true));
            Link(move, idleState, ("Moving", false));
            var t = sm.AddAnyStateTransition(cowerState);
            t.AddCondition(AnimatorConditionMode.If, 0, "Broken");
            t.canTransitionToSelf = false;
            t.duration = 0.25f;
        }

        // Params: Moving, Crouched, Flip (trigger).
        static void BuildPlayer(AnimationClip idle, AnimationClip walk, AnimationClip crouchIdle, AnimationClip crouchWalk,
            AnimationClip toCrouch, AnimationClip toStand, AnimationClip flip)
        {
            var ac = AnimatorController.CreateAnimatorControllerAtPath(Dir + "Player.controller");
            ac.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Crouched", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Flip", AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;
            var stand = State(sm, "Idle", idle);
            var move = State(sm, "Walk", walk);
            var crouch = State(sm, "CrouchIdle", crouchIdle);
            var crouchMove = State(sm, "CrouchWalk", crouchWalk);
            var down = State(sm, "StandToCrouch", toCrouch);
            var up = State(sm, "CrouchToStand", toStand);
            var flipState = State(sm, "Flip", flip);
            sm.defaultState = stand;

            Link(stand, move, ("Moving", true));
            Link(move, stand, ("Moving", false));
            Link(crouch, crouchMove, ("Moving", true));
            Link(crouchMove, crouch, ("Moving", false));
            Link(move, crouchMove, ("Crouched", true));
            Link(crouchMove, move, ("Crouched", false));
            Link(stand, down, ("Crouched", true));
            Link(crouch, up, ("Crouched", false));
            Finish(down, crouch);
            Finish(up, stand);

            var toFlip = sm.AddAnyStateTransition(flipState);
            toFlip.AddCondition(AnimatorConditionMode.If, 0, "Flip");
            toFlip.canTransitionToSelf = false;
            toFlip.duration = 0.1f;
            Finish(flipState, crouch, ("Crouched", true));
            Finish(flipState, stand, ("Crouched", false));
        }

        // Turns on Foot IK for a state (feet held where the clip puts them, despite retargeting). Once, if it's off.
        static bool FootIK(string path, string stateName)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var state = ac ? ac.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName) : null;
            if (!state || state.iKOnFeet) return false;
            state.iKOnFeet = true;
            EditorUtility.SetDirty(ac);
            return true;
        }

        // Adds a paired-kill state (played directly by Game.cs) if the controller doesn't have one by that name yet.
        // It returns to whichever of `exits` exist once the clip ends; with none it holds the last frame.
        static bool AddTakedown(string path, AnimationClip clip, string stateName, params string[] exits)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!ac) return false;
            var sm = ac.layers[0].stateMachine;
            if (sm.states.Any(s => s.state.name == stateName)) return false;
            var takedown = State(sm, stateName, clip);
            foreach (var exit in exits)
            {
                var to = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == exit);
                if (to) Finish(takedown, to, ("Crouched", exit.StartsWith("Crouch")));
            }
            EditorUtility.SetDirty(ac);
            return true;
        }

        // An empty clip in Resources/Anim for a state the game fills at runtime (AnimatorOverrideController).
        static AnimationClip Placeholder(string name)
        {
            string path = Dir + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip) return clip;
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // Down on their knees, then a begging loop. Nothing leads in: Game cross-fades to "BegStart" when they break.
        static bool AddBeg(string path, AnimationClip down, AnimationClip loop)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!ac) return false;
            var sm = ac.layers[0].stateMachine;
            if (sm.states.Any(s => s.state.name == "BegStart")) return false;
            Finish(State(sm, "BegStart", down), State(sm, "Beg", loop));
            EditorUtility.SetDirty(ac);
            return true;
        }

        // A one-shot "Shoot" state that returns to the armed idle.
        static bool AddShoot(string path, AnimationClip clip)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!ac) return false;
            var sm = ac.layers[0].stateMachine;
            var idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle");
            if (!idle || sm.states.Any(s => s.state.name == "Shoot")) return false;
            Finish(State(sm, "Shoot", clip), idle);
            EditorUtility.SetDirty(ac);
            return true;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion)
        {
            var s = sm.AddState(name);
            s.motion = motion;
            return s;
        }

        static void Link(AnimatorState from, AnimatorState to, (string param, bool value) when)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = 0.15f;
            t.AddCondition(when.value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, when.param);
        }

        // Leave `from` near the end of its clip (optionally only when a condition holds).
        static void Finish(AnimatorState from, AnimatorState to, (string param, bool value)? when = null)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 0.85f;
            t.duration = 0.15f;
            if (when.HasValue) t.AddCondition(when.Value.value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, when.Value.param);
        }
    }
}
