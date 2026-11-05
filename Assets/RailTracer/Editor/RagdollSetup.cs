using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RailTracer
{
    // Ragdoll bones on Resources/Anim/Character, made by Unity's own Ragdoll Builder (driven from code: its bones
    // auto-assigned from the Humanoid avatar). Every rigidbody is left kinematic and every collider off, on the
    // Ignore Raycast layer, so living characters are untouched; Game goes limp at a death (Game.Ragdoll). Runs
    // after each script reload until the prefab has them; delete the bones' Rigidbodies to rebuild.
    static class RagdollSetup
    {
        const string Prefab = "Assets/RailTracer/Resources/Anim/Character.prefab";
        const float Mass = 70f;

        [InitializeOnLoadMethod]
        static void AutoRun() => EditorApplication.delayCall += () => Run(false);

        [MenuItem("Rail Tracer/Set Up Ragdoll")]
        static void Menu() => Run(true);

        static void Run(bool rebuild)
        {
            if (!System.IO.File.Exists(Prefab)) return;
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var animator = root.GetComponent<Animator>();
                if (!animator || !animator.isHuman) { Debug.LogWarning("Rail Tracer: the character isn't Humanoid; no ragdoll."); return; }
                if (!rebuild && root.GetComponentInChildren<Rigidbody>(true)) return; // already done
                var type = typeof(Editor).Assembly.GetType("UnityEditor.RagdollBuilder")
                           ?? System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.RagdollBuilder")).FirstOrDefault(t => t != null);
                if (type == null) { Debug.LogWarning("Rail Tracer: this Unity has no RagdollBuilder; no ragdoll."); return; }
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var builder = typeof(ScriptableObject).IsAssignableFrom(type) ? ScriptableObject.CreateInstance(type) : System.Activator.CreateInstance(type, true);
                void Set(string field, object value) => type.GetField(field, any)?.SetValue(builder, value);
                void Call(string method) => type.GetMethod(method, any, null, System.Type.EmptyTypes, null)?.Invoke(builder, null);
                Set("animator", animator);
                Call("AutoAssignTransforms");
                // Older builders: no auto-assign, so the bones straight from the avatar.
                Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
                if (type.GetField("pelvis", any)?.GetValue(builder) == null)
                {
                    Set("pelvis", B(HumanBodyBones.Hips));
                    Set("leftHips", B(HumanBodyBones.LeftUpperLeg)); Set("leftKnee", B(HumanBodyBones.LeftLowerLeg)); Set("leftFoot", B(HumanBodyBones.LeftFoot));
                    Set("rightHips", B(HumanBodyBones.RightUpperLeg)); Set("rightKnee", B(HumanBodyBones.RightLowerLeg)); Set("rightFoot", B(HumanBodyBones.RightFoot));
                    Set("leftArm", B(HumanBodyBones.LeftUpperArm)); Set("leftElbow", B(HumanBodyBones.LeftLowerArm));
                    Set("rightArm", B(HumanBodyBones.RightUpperArm)); Set("rightElbow", B(HumanBodyBones.RightLowerArm));
                    Set("middleSpine", B(HumanBodyBones.Spine)); Set("head", B(HumanBodyBones.Head));
                }
                Set("totalMass", Mass);
                Call("OnWizardUpdate"); // checks and prepares the bones
                Call("OnWizardCreate"); // capsules, boxes, bodies, joints, masses
                if (builder is Object o) Object.DestroyImmediate(o);

                int n = 0;
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.isKinematic = true;
                    rb.interpolation = RigidbodyInterpolation.None;
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    rb.gameObject.layer = 2; // Ignore Raycast: blood casts, clicks and the camera check pass through
                    n++;
                }
                foreach (var col in root.GetComponentsInChildren<Collider>(true)) { col.enabled = false; col.gameObject.layer = 2; }
                if (n == 0) { Debug.LogWarning("Rail Tracer: the Ragdoll Builder made no bones."); return; }
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                Debug.Log($"Rail Tracer: ragdoll on the character ({n} bones).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
