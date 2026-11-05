using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailTracer
{
    public enum KillKind { Stealth, Brutal }
    public enum TakedownFor { Any, Guard, Heavy }
    public enum TakedownStance { Any, Crouched, Standing } // your stance when you strike (stealth kills keep it)

    // One paired finisher: the attacker's clip (played by you), the victim's, and how they stand together.
    [Serializable]
    public class Takedown
    {
        public string name;
        public KillKind kind;
        public TakedownFor target;
        public TakedownStance stance;
        public AnimationClip attacker, victim;
        public AnimationClip attacker2, victim2;                 // a follow-up played straight after (a combo); empty = none
        [Range(0f, 1f)] public float comboFrom = 0.85f;          // combo: the point in the first clip where the second blends in
        [Range(0f, 1f)] public float comboStart = 0.1f;          // combo: where in the second clip it starts (skips its wind-up)
        public float comboBlend = 0.25f;                         // combo: cross-fade length (seconds)
        public bool victimFallsAfter;                            // the last victim clip ends standing: topple them after it
        public bool needsCover;                                  // only with half cover beside the victim, and then always chosen
        public float coverShift = 0.45f;                         // the pair moves this far towards that cover (metres at full size)
        public Vector3 victimOffset = new Vector3(0f, 0f, 1.5f); // where the victim stands, in your space (metres at full size; scaled with the characters). The pack's pairs: 1.5 m apart, facing
        public float victimYaw = 180f;                           // the victim's facing relative to yours: 180 = facing you
        public float speed = 1f;                                 // playback speed of both clips
        [Range(0f, 1f)] public float bloodAt = 0.5f;             // point in the clip where the blood flies (and the stains land)
        [Tooltip("More strikes: extra splashes at these points, 0-1 over the WHOLE takedown (both clips of a combo). " +
                 "Blood At still lays the stains. Set them in the animation test scene (N adds one at the current frame).")]
        public List<float> extraBlood = new List<float>();
        [Tooltip("The head comes off at Blood At: a gore burst where it was (a stomp, say).")]
        public bool headOff;
        [Tooltip("Hold the knife the other way round for this one: the blade out of the little-finger side.")]
        public bool reverseGrip;
        [Tooltip("Let the pair travel as the clips do. Off (the default): the game holds the middle of the pair where it " +
                 "started, so they don't wander off their tiles (their spacing is kept). Cover finishers always travel.")]
        public bool keepTravel;

        // Whether the pair is held in place while this plays.
        public bool HoldsPlace => !keepTravel && !needsCover;

        // How far through the whole takedown (0-1, both clips of a combo) a point in its first or second clip is.
        public float Overall(bool second, float k)
        {
            float l1 = attacker ? attacker.length : 1f;
            bool chained = attacker2 && victim2;
            if (!chained) return k;
            float l2 = attacker2.length * (1f - comboStart);
            float total = comboFrom * l1 + l2;
            return total <= 0f ? k : (second ? comboFrom * l1 + k * l2 : k * l1) / total;
        }
        public Vector3 camOffset;                                // kill cam position in the pair's space (zero = low, close, side-on)
        public Vector3 camLook;                                  // with a Cam Offset: the point it looks at (and the camera with it) moved this far from the middle of the pair, in the pair's space
    }

    // The takedowns kills choose from (Resources/Takedowns, made by Editor/FinisherSetup.cs and yours to edit).
    [CreateAssetMenu(menuName = "Rail Tracer/Takedown Library")]
    public class TakedownLibrary : ScriptableObject
    {
        public List<Takedown> takedowns = new List<Takedown>();
        [NonSerialized] readonly Dictionary<KillKind, Takedown> last = new Dictionary<KillKind, Takedown>();

        // A takedown of this kind for a guard or a heavy (entries tagged Any suit both) and your stance. With half cover
        // beside the victim, one that uses it if there is one; otherwise at random, not the one just used when there's a
        // choice. Null if none fit.
        public Takedown Pick(KillKind kind, bool heavy, bool crouched, bool cover)
        {
            var want = heavy ? TakedownFor.Heavy : TakedownFor.Guard;
            var stance = crouched ? TakedownStance.Crouched : TakedownStance.Standing;
            bool Fits(Takedown t, bool anyStance) => t.kind == kind && t.attacker && t.victim && (t.target == want || t.target == TakedownFor.Any)
                && (anyStance || t.stance == stance || t.stance == TakedownStance.Any) && (!t.needsCover || cover);
            var set = takedowns.FindAll(t => Fits(t, false));
            if (set.Count == 0) set = takedowns.FindAll(t => Fits(t, true)); // nothing for this stance: any of its kind
            if (set.Exists(t => t.needsCover)) set.RemoveAll(t => !t.needsCover);
            if (set.Count == 0) return null;
            last.TryGetValue(kind, out var prev);
            if (set.Count > 1) set.Remove(prev);
            var pick = set[UnityEngine.Random.Range(0, set.Count)];
            last[kind] = pick;
            return pick;
        }
    }
}
