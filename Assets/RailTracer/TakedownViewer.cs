using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RailTracer
{
    // The animation test scene: one takedown from Resources/Takedowns at a time, replaying on a loop, attacker and victim
    // on a bare floor at full size, spaced and turned by the entry's Victim Offset / Victim Yaw (read fresh each play,
    // so edits to the asset show on the next loop). Left / right arrows: previous / next. R: replay now. Space: pause.
    // Right-drag orbits, scroll zooms.
    public class TakedownViewer : MonoBehaviour
    {
        public float holdAfter = 1f;   // the end pose stays this long
        public float resetPause = 0.4f; // back to idle this long before the next one
        public float bloodCastStanding = 1.8f, bloodCastCrouched = 1f, bloodCastReach = 1.5f; // as on the Game component
        TakedownLibrary library;
        BloodKit blood;      // the game's blood (Resources/Blood/BloodKit), fired at each takedown's Blood At
        Transform bloodRoot; // this loop's stains, cleared on the next
        float bloodFlash = -9f;
        Animator attacker, victim;
        Camera cam;
        int index;
        bool jump; // skip to `index` now
        float yaw = 90f, pitch = 12f, distance = 4.5f;
        string label = "";

        void Start()
        {
            library = Resources.Load<TakedownLibrary>("Takedowns");
            blood = Resources.Load<BloodKit>("Blood/BloodKit");
            bloodRoot = new GameObject("Blood").transform;
            var model = Resources.Load<GameObject>("Anim/Character");
            var playerController = Resources.Load<RuntimeAnimatorController>("Anim/Player");
            var enemyController = Resources.Load<RuntimeAnimatorController>("Anim/Enemy");
            if (!library || !model || !playerController || !enemyController)
            {
                label = "Missing Resources/Takedowns or Resources/Anim (Character, Player, Enemy).";
                return;
            }
            cam = Camera.main;
            if (!cam) { cam = new GameObject("Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            if (!FindAnyObjectByType<Light>())
            {
                var sun = new GameObject("Light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 0.8f;
            floor.GetComponent<Renderer>().material.color = new Color(0.3f, 0.3f, 0.32f);
            attacker = Spawn(model, playerController, "Attacker");
            victim = Spawn(model, enemyController, "Victim");
            StartCoroutine(Loop());
        }

        // Back on their feet after the ragdoll: bones kinematic again, the animator posing them.
        static void Stand(Animator a)
        {
            if (Game.HasRagdoll(a)) Game.SetLimp(a, false);
            a.enabled = true;
        }

        static Animator Spawn(GameObject model, RuntimeAnimatorController controller, string name)
        {
            var go = Instantiate(model);
            go.name = name;
            var a = go.GetComponent<Animator>();
            if (!a) a = go.AddComponent<Animator>();
            a.runtimeAnimatorController = new AnimatorOverrideController(controller);
            a.applyRootMotion = false;
            return a;
        }

        IEnumerator Loop()
        {
            while (true)
            {
                var list = library.takedowns;
                if (list.Count == 0) { label = "The takedown library is empty."; yield return null; continue; }
                index = (index % list.Count + list.Count) % list.Count;
                var t = list[index];
                jump = false;
                if (t.attacker && t.victim) yield return Play(t);
                else { label = $"{index + 1}/{list.Count}  {t.name}: missing a clip, skipped"; yield return new WaitForSeconds(0.5f); }
            }
        }

        // One takedown, as the game plays it: the victim placed from the attacker, both clips together, a combo's second
        // clip cross-faded in, the blood at Blood At (of the last clip), then the end pose held.
        IEnumerator Play(Takedown t)
        {
            foreach (Transform stain in bloodRoot) Destroy(stain.gameObject);
            Stand(victim);
            attacker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            victim.transform.SetPositionAndRotation(t.victimOffset, Quaternion.Euler(0f, t.victimYaw, 0f));
            Override(attacker, "Finisher Attacker", t.attacker);
            Override(victim, "Finisher Victim", t.victim);
            int finisher = Animator.StringToHash("Finisher"), combo = Animator.StringToHash("Finisher2");
            bool chained = t.attacker2 && t.victim2 && attacker.HasState(0, combo) && victim.HasState(0, combo);
            if (chained)
            {
                Override(attacker, "Finisher Attacker 2", t.attacker2);
                Override(victim, "Finisher Victim 2", t.victim2);
            }
            attacker.speed = victim.speed = t.speed;
            attacker.Play(finisher, 0, 0f);
            victim.Play(finisher, 0, 0f);
            label = $"{index + 1}/{library.takedowns.Count}  {t.name}   offset {t.victimOffset}  yaw {t.victimYaw}  blood at {t.bloodAt:0.00}" + (chained ? "  (combo: of the second clip)" : "");
            float length = t.attacker.length / Mathf.Max(0.01f, t.speed);
            float lastFrom = chained ? -1f : 0f; // when the clip the blood belongs to started
            bool bled = false;
            for (float time = 0f; time < length; time += Time.deltaTime)
            {
                if (jump) yield break;
                if (chained && time / length >= t.comboFrom)
                {
                    attacker.CrossFadeInFixedTime(combo, t.comboBlend, 0, t.comboStart * t.attacker2.length);
                    victim.CrossFadeInFixedTime(combo, t.comboBlend, 0, t.comboStart * t.victim2.length);
                    lastFrom = time;
                    length = time + t.attacker2.length * (1f - t.comboStart) / Mathf.Max(0.01f, t.speed);
                    chained = false;
                }
                if (!bled && lastFrom >= 0f && (time - lastFrom) / (length - lastFrom) >= t.bloodAt) { Bleed(t); bled = true; }
                yield return null;
            }
            if (!bled) Bleed(t);
            // As in the game: the victim goes limp after the last clip (the ragdoll), so every takedown ends on the floor.
            if (Game.HasRagdoll(victim)) { victim.enabled = false; Game.SetLimp(victim, true); }
            for (float time = 0f; time < Mathf.Max(holdAfter, 1.6f) && !jump; time += Time.deltaTime) yield return null;
            Stand(victim);
            attacker.speed = victim.speed = 1f;
            attacker.Play("Idle", 0, 0f);
            victim.Play("Idle", 0, 0f);
            for (float time = 0f; time < resetPause && !jump; time += Time.deltaTime) yield return null;
        }

        // The game's kill blood (Game.KillBlood): the splash from the victim's chest, then straight-cast stains.
        void Bleed(Takedown t)
        {
            bloodFlash = Time.time;
            if (!blood) return;
            var bone = victim.GetBoneTransform(HumanBodyBones.Chest);
            var chest = bone ? bone.position : victim.transform.position + Vector3.up * 1.1f;
            var floor = new Vector3(chest.x, 0f, chest.z);
            var dir = victim.transform.position - attacker.transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            bool brutal = t.kind == KillKind.Brutal;
            blood.Splash(bloodRoot, chest, dir, brutal);
            // As in the game: the victim's tile (1 m grid), and for a brutal kill the four next to it, cast from stance height.
            var tile = new Vector3(Mathf.Round(floor.x), 0f, Mathf.Round(floor.z));
            float height = t.stance == TakedownStance.Crouched ? bloodCastCrouched : bloodCastStanding;
            blood.TileSplat(bloodRoot, tile, height, bloodCastReach, brutal ? blood.poolScale : blood.spatterScale, blood.smearScale);
            if (brutal)
                foreach (var d in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                    blood.TileSplat(bloodRoot, tile + d, height, bloodCastReach, blood.spatterScale, blood.smearScale);
        }

        static void Override(Animator a, string slot, AnimationClip clip)
        {
            if (a.runtimeAnimatorController is AnimatorOverrideController o) o[slot] = clip;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) { index++; jump = true; }
                if (kb.leftArrowKey.wasPressedThisFrame) { index--; jump = true; }
                if (kb.rKey.wasPressedThisFrame) jump = true;
                if (kb.spaceKey.wasPressedThisFrame) Time.timeScale = Time.timeScale > 0f ? 0f : 1f;
            }
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    yaw += d.x * 0.3f;
                    pitch = Mathf.Clamp(pitch - d.y * 0.3f, -10f, 80f);
                }
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.002f * distance, 1.5f, 12f);
            }
            if (!cam || library == null) return;
            // Orbit the middle of the pair at chest height.
            var t = library.takedowns.Count > 0 ? library.takedowns[(index % library.takedowns.Count + library.takedowns.Count) % library.takedowns.Count] : null;
            var pivot = (t != null ? t.victimOffset / 2f : Vector3.zero) + Vector3.up * 1f;
            cam.transform.position = pivot + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
            cam.transform.LookAt(pivot);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(16, 12, 1200, 24), label);
            GUI.Label(new Rect(16, 34, 1200, 24), "Left / Right: previous / next   R: replay   Space: pause   Right-drag: orbit   Scroll: zoom");
            if (Time.time - bloodFlash < 0.6f) GUI.Label(new Rect(16, 56, 300, 24), "BLOOD");
        }
    }
}
