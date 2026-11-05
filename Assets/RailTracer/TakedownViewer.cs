using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RailTracer
{
    // The animation test scene: one takedown from Resources/Takedowns at a time, replaying on a loop, attacker and victim
    // on a bare floor at full size, spaced and turned by the entry's Victim Offset / Victim Yaw (read fresh each play,
    // so edits to the asset show on the next loop). For tuning each takedown's timing:
    //   Left / Right: previous / next     R: replay     Space: pause     Up / Down: playback speed (1, 0.5, 0.25, 0.1)
    //   . / , : one frame forward / back (pauses)     click the timeline: jump there
    //   B: Blood At = now     N / X: add / remove an extra blood point here (all saved to the asset)     K: kill cam view (the entry's Cam Offset) / orbit     Right-drag: orbit, scroll: zoom
    // The clips are stepped by hand (the animators are evaluated by this script), so pausing, slow motion and seeking
    // are exact. As in the game: the blood fires at Blood At of the last clip, the victim slides over its tile and goes
    // limp after the last clip, and a cover finisher (Shove to Table) leaves the pair as the clip ends, at a stand-in table.
    public class TakedownViewer : MonoBehaviour
    {
        public float holdAfter = 1.6f;  // the end pose (the fall) stays this long
        public float resetPause = 0.4f; // back to idle this long before the next one
        public float bloodCastStanding = 1.8f, bloodCastCrouched = 1f, bloodCastReach = 1.5f; // as on the Game component
        public float killCamClose = 1.25f, killCamHeight = 0.55f, takedownHold = 0.5f;        // as on the Game component (the body's slide takes Takedown Hold)
        const float Frame = 1f / 30f;
        static readonly float[] Rates = { 1f, 0.5f, 0.25f, 0.1f };

        TakedownLibrary library;
        BloodKit blood;      // the game's blood (Resources/Blood/BloodKit)
        Transform bloodRoot; // this loop's stains, cleared on the next
        GameObject coverTable;
        float bloodFlash = -9f;
        Animator attacker, victim;
        Camera cam;
        int index, rate;
        bool jump, paused, killCam;
        int stepFrames;      // frames to step while paused (+ forward)
        float seekTo = -1f;  // replay and run straight to this time (s)
        float yaw = 90f, pitch = 12f, distance = 4.5f;
        string label = "", note = "";
        // The playing takedown's timeline (s, at its own speed): now, the whole length, where the combo blends in, where it bleeds.
        float clock, total, comboAt = -1f, bloodTime, lastFrom;
        Rect bar;

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
            cam.fieldOfView = 50f;
            if (!FindAnyObjectByType<Light>())
            {
                var sun = new GameObject("Light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = Vector3.one * 0.8f;
            floor.GetComponent<Renderer>().material.color = new Color(0.3f, 0.3f, 0.32f);
            // A 1 m grid on the floor, so the tiles the game uses can be seen.
            for (int i = -4; i <= 4; i++)
            {
                Line(new Vector3(i - 0.5f, 0.005f, -4.5f), new Vector3(i - 0.5f, 0.005f, 4.5f));
                Line(new Vector3(-4.5f, 0.005f, i - 0.5f), new Vector3(4.5f, 0.005f, i - 0.5f));
            }
            coverTable = GameObject.CreatePrimitive(PrimitiveType.Cube);
            coverTable.name = "Cover (stand-in table)";
            coverTable.transform.localScale = new Vector3(0.9f, 0.76f, 0.9f);
            coverTable.GetComponent<Renderer>().material.color = new Color(0.86f, 0.83f, 0.76f);
            coverTable.SetActive(false);
            attacker = Spawn(model, playerController, "Attacker");
            victim = Spawn(model, enemyController, "Victim");
            StartCoroutine(Loop());
        }

        static void Line(Vector3 a, Vector3 b)
        {
            var lr = new GameObject("Grid").AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = new Color(1f, 1f, 1f, 0.12f);
            lr.widthMultiplier = 0.01f;
            lr.SetPositions(new[] { a, b });
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

        Takedown Current => library && library.takedowns.Count > 0
            ? library.takedowns[(index % library.takedowns.Count + library.takedowns.Count) % library.takedowns.Count] : null;

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

        // How far time moves this frame: 0 paused (unless stepping), else real time x the playback speed. A seek runs
        // the whole way in one frame.
        float Advance()
        {
            if (seekTo >= 0f) return Mathf.Max(0f, Mathf.Min(Frame, seekTo - clock));
            if (paused)
            {
                if (stepFrames <= 0) return 0f;
                stepFrames--;
                return Frame;
            }
            return Time.deltaTime * Rates[rate];
        }

        // One takedown, as the game plays it: the victim placed from the attacker, both clips together, a combo's second
        // clip cross-faded in, the blood at Blood At (of the last clip), then the slide and the fall (or, for a cover
        // finisher, the end pose held).
        IEnumerator Play(Takedown t)
        {
            foreach (Transform stain in bloodRoot) Destroy(stain.gameObject);
            Stand(attacker);
            Stand(victim);
            attacker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            victim.transform.SetPositionAndRotation(t.victimOffset, Quaternion.Euler(0f, t.victimYaw, 0f));
            // The game turns the pair to face the seat and moves the victim coverShift towards it: the seat stands a
            // tile on from where the victim started.
            coverTable.SetActive(t.needsCover);
            if (t.needsCover) coverTable.transform.position = new Vector3(t.victimOffset.x, 0.38f, t.victimOffset.z + 1f - t.coverShift);
            Override(attacker, "Finisher Attacker", t.attacker);
            Override(victim, "Finisher Victim", t.victim);
            int finisher = Animator.StringToHash("Finisher"), combo = Animator.StringToHash("Finisher2");
            bool chained = t.attacker2 && t.victim2 && attacker.HasState(0, combo) && victim.HasState(0, combo);
            if (chained)
            {
                Override(attacker, "Finisher Attacker 2", t.attacker2);
                Override(victim, "Finisher Victim 2", t.victim2);
            }
            float speed = Mathf.Max(0.01f, t.speed);
            float length1 = t.attacker.length / speed;
            comboAt = chained ? t.comboFrom * length1 : -1f;
            total = chained ? comboAt + t.attacker2.length * (1f - t.comboStart) / speed : length1;
            lastFrom = chained ? comboAt : 0f;
            bloodTime = lastFrom + t.bloodAt * (total - lastFrom);
            // Driven by hand from here: the animators only move when this evaluates them.
            attacker.enabled = victim.enabled = false;
            attacker.speed = victim.speed = 1f;
            attacker.Play(finisher, 0, 0f);
            victim.Play(finisher, 0, 0f);
            attacker.Update(0f);
            victim.Update(0f);
            clock = 0f;
            bool comboStarted = false, bled = false;
            var struck = new System.Collections.Generic.HashSet<int>();
            while (clock < total)
            {
                if (jump) yield break; // a seek restarts the takedown and runs on to seekTo
                float dt = Advance();
                if (dt > 0f)
                {
                    if (chained && !comboStarted && clock + dt >= comboAt)
                    {
                        float before = comboAt - clock; // run up to the blend point, start the blend there
                        attacker.Update(before * speed);
                        victim.Update(before * speed);
                        attacker.CrossFadeInFixedTime(combo, t.comboBlend, 0, t.comboStart * t.attacker2.length);
                        victim.CrossFadeInFixedTime(combo, t.comboBlend, 0, t.comboStart * t.victim2.length);
                        attacker.Update((dt - before) * speed);
                        victim.Update((dt - before) * speed);
                        comboStarted = true;
                    }
                    else
                    {
                        attacker.Update(dt * speed);
                        victim.Update(dt * speed);
                    }
                    clock += dt;
                    if (!bled && clock >= bloodTime) { Bleed(t); bled = true; }
                    for (int s = 0; s < t.extraBlood.Count; s++)
                        if (!struck.Contains(s) && clock >= t.extraBlood[s] * total) { struck.Add(s); Strike(t); }
                }
                if (seekTo >= 0f && clock >= seekTo - 1e-4f) { seekTo = -1f; paused = true; }
                if (seekTo >= 0f) continue; // a seek runs on within the frame
                Label(t, chained);
                yield return null;
            }
            if (!bled) Bleed(t);
            Label(t, chained);
            // As in the game: a cover finisher keeps its end pose; anyone else slides so the hips are over their tile
            // (stealth: pulled onto yours) and goes limp.
            if (!t.needsCover && Game.HasRagdoll(victim))
            {
                var hips = victim.GetBoneTransform(HumanBodyBones.Hips);
                var tile = t.kind == KillKind.Stealth ? Vector3.zero : new Vector3(t.victimOffset.x, 0f, t.victimOffset.z);
                if (hips)
                {
                    var d = tile - hips.position;
                    d.y = 0f;
                    var start = victim.transform.position;
                    for (float time = 0f; time < takedownHold && !jump; time += Time.deltaTime * Rates[rate])
                    {
                        victim.transform.position = start + d * Mathf.SmoothStep(0f, 1f, time / takedownHold);
                        yield return null;
                    }
                    victim.transform.position = start + d;
                }
                Game.SetLimp(victim, true);
            }
            for (float time = 0f; (time < holdAfter || paused) && !jump; time += Time.deltaTime * Rates[rate]) yield return null;
            Stand(victim);
            attacker.enabled = true;
            attacker.Play("Idle", 0, 0f);
            victim.Play("Idle", 0, 0f);
            for (float time = 0f; time < resetPause && !jump; time += Time.deltaTime) yield return null;
        }

        void Label(Takedown t, bool chained)
        {
            label = $"{index + 1}/{library.takedowns.Count}  {t.name}  ({t.kind}, {t.stance}{(t.needsCover ? ", cover" : "")})   " +
                    $"offset {t.victimOffset}  yaw {t.victimYaw}  speed {t.speed:0.##}  blood at {t.bloodAt:0.00}" +
                    (chained ? $"  combo from {t.comboFrom:0.00} start {t.comboStart:0.00} blend {t.comboBlend:0.00}  (blood: of the second clip)" : "") +
                    $"   cam offset {t.camOffset}";
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

        // An extra strike: just the splash (Blood At lays the stains).
        void Strike(Takedown t)
        {
            bloodFlash = Time.time;
            if (!blood) return;
            var bone = victim.GetBoneTransform(HumanBodyBones.Chest);
            var chest = bone ? bone.position : victim.transform.position + Vector3.up * 1.1f;
            var dir = victim.transform.position - attacker.transform.position;
            dir.y = 0f;
            blood.Splash(bloodRoot, chest, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, t.kind == KillKind.Brutal);
        }

        // N: an extra blood point at the current frame; X: remove the one nearest it. Saved to the asset.
        void EditStrikes(bool add)
        {
            var t = Current;
            if (t == null || total <= 0f) return;
            float now = Mathf.Clamp01(clock / total);
            if (add) t.extraBlood.Add(now);
            else
            {
                if (t.extraBlood.Count == 0) { note = "No extra blood to remove."; return; }
                int near = 0;
                for (int i = 1; i < t.extraBlood.Count; i++)
                    if (Mathf.Abs(t.extraBlood[i] - now) < Mathf.Abs(t.extraBlood[near] - now)) near = i;
                t.extraBlood.RemoveAt(near);
            }
            t.extraBlood.Sort();
            Save();
            note = $"{t.name}: extra blood at {string.Join(", ", t.extraBlood.ConvertAll(v => v.ToString("0.000")))} (of the whole takedown), saved.";
        }

        void Save()
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(library);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
        }

        static void Override(Animator a, string slot, AnimationClip clip)
        {
            if (a.runtimeAnimatorController is AnimatorOverrideController o) o[slot] = clip;
        }

        // Replay this takedown and run straight to `time` (s), then pause there.
        void Seek(float time)
        {
            seekTo = Mathf.Clamp(time, 0f, Mathf.Max(0f, total - 1e-3f));
            jump = true;
        }

        void SetBloodHere()
        {
            var t = Current;
            if (t == null || total <= lastFrom) return;
            if (clock < lastFrom) { note = "Blood At is for the combo's second clip: move past the blend first."; return; }
            t.bloodAt = Mathf.Clamp01((clock - lastFrom) / (total - lastFrom));
            bloodTime = clock;
            Save();
            note = $"{t.name}: Blood At = {t.bloodAt:0.000}, saved to Resources/Takedowns.";
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) { index++; jump = true; seekTo = -1f; note = ""; }
                if (kb.leftArrowKey.wasPressedThisFrame) { index--; jump = true; seekTo = -1f; note = ""; }
                if (kb.rKey.wasPressedThisFrame) { jump = true; seekTo = -1f; }
                if (kb.spaceKey.wasPressedThisFrame) paused = !paused;
                if (kb.upArrowKey.wasPressedThisFrame) rate = Mathf.Max(0, rate - 1);
                if (kb.downArrowKey.wasPressedThisFrame) rate = Mathf.Min(Rates.Length - 1, rate + 1);
                if (kb.periodKey.wasPressedThisFrame) { paused = true; stepFrames++; }
                if (kb.commaKey.wasPressedThisFrame) { paused = true; Seek(clock - Frame); }
                if (kb.bKey.wasPressedThisFrame) SetBloodHere();
                if (kb.nKey.wasPressedThisFrame) EditStrikes(true);
                if (kb.xKey.wasPressedThisFrame) EditStrikes(false);
                if (kb.kKey.wasPressedThisFrame) killCam = !killCam;
            }
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame && total > 0f)
                {
                    var p = mouse.position.ReadValue();
                    var gui = new Vector2(p.x, Screen.height - p.y);
                    if (bar.Contains(gui)) { paused = true; Seek((gui.x - bar.x) / bar.width * total); }
                }
                if (mouse.rightButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    yaw += d.x * 0.3f;
                    pitch = Mathf.Clamp(pitch - d.y * 0.3f, -10f, 80f);
                    killCam = false;
                }
                distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.002f * distance, 1.5f, 12f);
            }
            if (!cam || library == null) return;
            var t = Current;
            if (killCam && t != null)
            {
                // The game's kill cam pose (Game.KillCamPose): the entry's Cam Offset in the pair's space, or low and
                // side-on, close in, looking at the middle of the pair at half figure height.
                var a = attacker.transform.position;
                var b = t.victimOffset;
                var mid = (a + b) / 2f + Vector3.up * 0.9f;
                var along = b - a;
                along.y = 0f;
                var pair = Quaternion.LookRotation(along.sqrMagnitude > 1e-6f ? along : Vector3.forward);
                var off = t.camOffset != Vector3.zero ? pair * t.camOffset : pair * Vector3.right * killCamClose + Vector3.up * killCamHeight;
                cam.transform.position = mid + off;
                cam.transform.LookAt(mid);
                return;
            }
            // Orbit the middle of the pair at chest height.
            var pivot = (t != null ? t.victimOffset / 2f : Vector3.zero) + Vector3.up * 1f;
            cam.transform.position = pivot + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
            cam.transform.LookAt(pivot);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(16, 12, 1600, 24), label);
            GUI.Label(new Rect(16, 34, 1600, 24),
                "Left / Right: previous / next   R: replay   Space: pause   Up / Down: speed   . / , : frame forward / back   " +
                "click the timeline: jump   B: Blood At = now   N / X: add / remove an extra blood here (all saved)   K: kill cam view   Right-drag: orbit   Scroll: zoom");
            GUI.Label(new Rect(16, 56, 1600, 24),
                $"{(paused ? "PAUSED" : "playing")} at {Rates[rate]:0.##}x   {clock:0.000} / {total:0.000} s" +
                (total > lastFrom && clock >= lastFrom ? $"   last clip {(clock - lastFrom) / (total - lastFrom):0.000}" : "") +
                (killCam ? "   kill cam view" : "") + "   (other values: select Resources/Takedowns; edits show on the next loop)");
            if (!string.IsNullOrEmpty(note)) GUI.Label(new Rect(16, 78, 1600, 24), note);
            if (Time.time - bloodFlash < 0.6f) GUI.Label(new Rect(16, 100, 300, 24), "BLOOD");

            // The timeline: where it is, where the combo blends in (yellow), where it bleeds (red). Click to jump.
            bar = new Rect(16, Screen.height - 44, Screen.width - 32, 18);
            if (total <= 0f) return;
            Fill(bar, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(clock / total), bar.height), new Color(0.95f, 0.94f, 0.92f, 0.35f));
            if (comboAt >= 0f) Fill(new Rect(bar.x + bar.width * comboAt / total - 1, bar.y - 4, 3, bar.height + 8), new Color(1f, 0.8f, 0.2f));
            Fill(new Rect(bar.x + bar.width * bloodTime / total - 1, bar.y - 6, 3, bar.height + 12), new Color(0.85f, 0.06f, 0.15f));
            var t = Current;
            if (t != null)
                foreach (var e in t.extraBlood)
                    Fill(new Rect(bar.x + bar.width * Mathf.Clamp01(e) - 1, bar.y - 3, 2, bar.height + 6), new Color(1f, 0.45f, 0.5f));
            Fill(new Rect(bar.x + bar.width * Mathf.Clamp01(clock / total) - 1, bar.y - 2, 2, bar.height + 4), Color.white);
            GUI.Label(new Rect(bar.x, bar.y - 22, 400, 20), "red: blood   pink: extra blood   yellow: combo blend   white: now");
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
