using System.Collections.Generic;
using UnityEngine;

namespace RailTracer
{
    // Sound effects: every clip in Resources/Audio/Sfx, picked at random by name ("KnifeSlice" plays KnifeSlice or KnifeSlice2),
    // in 3D where it happens (root space). Master volume covers them (AudioListener.volume).
    public partial class Game
    {
        [Header("Sound effects")]
        [Range(0f, 1f)] public float sfxVolume = 0.8f;

        AudioClip[] sfxClips;
        readonly List<AudioClip> sfxPick = new List<AudioClip>();

        void Sfx(string name, Vector3 at)
        {
            sfxClips ??= Resources.LoadAll<AudioClip>("Audio/Sfx");
            sfxPick.Clear();
            foreach (var clip in sfxClips) if (clip.name.StartsWith(name)) sfxPick.Add(clip);
            if (sfxPick.Count == 0 || !root) return;
            AudioSource.PlayClipAtPoint(sfxPick[Random.Range(0, sfxPick.Count)], root.TransformPoint(at), sfxVolume);
        }

        // A blow landing: the knife (a stomp for a brutal kill) and the blood together.
        void Strike(Vector3 at, bool brutal)
        {
            Sfx(brutal ? "HeadStomp" : "KnifeSlice", at);
            Sfx("BloodStab", at);
        }
    }
}
