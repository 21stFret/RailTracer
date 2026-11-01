using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailTracer
{
    public enum KillKind { Stealth, Brutal }
    public enum TakedownFor { Any, Guard, Heavy }

    // One paired finisher: the attacker's clip (played by you), the victim's, and how they stand together.
    [Serializable]
    public class Takedown
    {
        public string name;
        public KillKind kind;
        public TakedownFor target;
        public AnimationClip attacker, victim;
        public Vector3 victimOffset = new Vector3(0f, 0f, 0.9f); // where the victim stands, in your space (metres at full size; scaled with the characters)
        public float victimYaw = 180f;                           // the victim's facing relative to yours: 180 = facing you
        public float speed = 1f;                                 // playback speed of both clips
        [Range(0f, 1f)] public float bloodAt = 0.5f;             // point in the clip where the blood flies
        public Vector3 camOffset;                                // kill cam position in the pair's space (zero = low, close, side-on)
    }

    // The takedowns kills choose from (Resources/Takedowns, made by Editor/FinisherSetup.cs and yours to edit).
    [CreateAssetMenu(menuName = "Rail Tracer/Takedown Library")]
    public class TakedownLibrary : ScriptableObject
    {
        public List<Takedown> takedowns = new List<Takedown>();
        [NonSerialized] readonly Dictionary<KillKind, Takedown> last = new Dictionary<KillKind, Takedown>();

        // A random takedown of this kind for a guard or a heavy (entries tagged Any suit both), not the one just used
        // when there's a choice. Null if none have both clips.
        public Takedown Pick(KillKind kind, bool heavy)
        {
            var want = heavy ? TakedownFor.Heavy : TakedownFor.Guard;
            var set = takedowns.FindAll(t => t.kind == kind && t.attacker && t.victim && (t.target == want || t.target == TakedownFor.Any));
            if (set.Count == 0) return null;
            last.TryGetValue(kind, out var prev);
            if (set.Count > 1) set.Remove(prev);
            var pick = set[UnityEngine.Random.Range(0, set.Count)];
            last[kind] = pick;
            return pick;
        }
    }
}
