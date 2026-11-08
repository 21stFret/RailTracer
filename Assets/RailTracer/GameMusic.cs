using UnityEngine;

namespace RailTracer
{
    // The music: DarkJazz (Resources/Audio/DarkJazz1) on ONE looping AudioSource for the whole game, so it never
    // restarts. It fades in over the menus as a flat (2D) track at full level. Diving into a car with a gramophone, the
    // same source slides over to 3D at the record (spatial blend 0 to 1 over the dive), so the music carries on from
    // the gramophone at the same moment of the track, and changes character on the way: much quieter (musicInCar of
    // the menu level, even right beside it), its top end taken off (a low-pass filter, the gramophone's tinny sound),
    // and heard from where it stands, softer towards the far end of the carriage but never gone (a custom falloff).
    // The listener (Ears) is the camera in the menus and in play stands at head height over what the camera follows
    // (you), turned like the camera so left and right match the screen.
    // A victim shoved onto the gramophone (Carriage.musicStopped) winds it down: the pitch, the volume and the record's
    // spin run out together. In a car without one the music fades out while you play and comes back in the menus.
    public partial class Game
    {
        [Header("Music")]
        public float musicVolume = 0.6f;
        public float musicFade = 2.5f;     // seconds to fade in or out
        public float musicWindDown = 1.6f; // seconds for the record to run down when the music's stopped
        [Range(0f, 1f)] public float musicInCar = 0.25f; // the gramophone's level (right beside it) as a share of the menu's
        public float musicFar = 30f;       // metres: the falloff curve runs over this (a car is 26 m)
        [Range(0f, 1f)] public float musicFarLevel = 0.25f; // what's left of it at musicFar
        public float musicMuffle = 3200f;  // Hz: the gramophone's low-pass cutoff (the menu: unfiltered)
        public float earsHeight = 1.1f;    // the listener above the floor in play (a figure's head at characterScale 0.6)
        public float recordRpm = 78f;      // a shellac 78

        AudioSource music;
        AudioLowPassFilter muffle;
        Transform ears;
        bool enabledMusic = true; // false once the track was found missing (warned once)
        float musicLevel;          // the fade, before the wind-down
        Transform gramophone, gramophoneRecord;
        Vector3 recordPivot;       // the record's centre, in the gramophone's space

        // The live car's gramophone model: its Record child spins about the record's centre.
        void SetGramophone(Transform g)
        {
            gramophone = g;
            gramophoneRecord = g.Find("Record");
            var r = gramophoneRecord ? gramophoneRecord.GetComponent<Renderer>() : null;
            recordPivot = r ? g.InverseTransformPoint(r.bounds.center) : new Vector3(0f, 0.82f, -0.02f);
        }

        void MusicStep()
        {
            if (!music)
            {
                if (!enabledMusic) return;
                var clip = Resources.Load<AudioClip>("Audio/DarkJazz1");
                if (!cam) return;
                if (!clip) { Debug.LogWarning("Rail Tracer: no Resources/Audio/DarkJazz1; no music."); enabledMusic = false; return; }
                ears = new GameObject("Ears").AddComponent<AudioListener>().transform;
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.transform != ears) l.enabled = false; // one listener
                var cfg = AudioSettings.GetConfiguration();
                Debug.Log($"Rail Tracer: music {clip.name} ({clip.length:0}s) fading in to {musicVolume}; " +
                          $"output {AudioSettings.outputSampleRate} Hz, {cfg.speakerMode}, master volume {AudioListener.volume}" +
                          (AudioSettings.outputSampleRate == 0 ? " - NO AUDIO DEVICE" : ""));
                music = new GameObject("Music").AddComponent<AudioSource>();
                music.clip = clip;
                music.loop = true;
                music.playOnAwake = false;
                music.volume = 0f;
                music.spatialBlend = 0f;
                music.dopplerLevel = 0f;
                music.rolloffMode = AudioRolloffMode.Custom;
                muffle = music.gameObject.AddComponent<AudioLowPassFilter>();
                // Falloff over musicFar: full within the first 1.5 m, easing down to musicFarLevel at the far end.
                music.maxDistance = musicFar;
                music.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(1.5f / musicFar, 1f), new Keyframe(0.5f, Mathf.Lerp(1f, musicFarLevel, 0.6f)), new Keyframe(1f, musicFarLevel)));
                music.Play();
            }

            float dt = Time.unscaledDeltaTime; // pauses and slow motion don't touch the music
            bool playing = phase != Phase.Menu;
            bool here = gramophone;
            // The ears: the camera in the menus; in play, at head height over what the camera follows, turned like it.
            if (playing && root) ears.SetPositionAndRotation(root.TransformPoint(focus) + Vector3.up * earsHeight, Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f));
            else ears.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);

            var centre = here ? gramophone.TransformPoint(recordPivot) : Vector3.zero;
            if (here) music.transform.position = centre;
            music.spatialBlend = Mathf.MoveTowards(music.spatialBlend, playing && here ? 1f : 0f, dt / 1.1f); // the dive
            muffle.cutoffFrequency = 22000f * Mathf.Pow(musicMuffle / 22000f, music.spatialBlend);       // the gramophone's tone with it
            music.pitch = Mathf.MoveTowards(music.pitch, playing && c.musicStopped ? 0f : 1f, dt / musicWindDown);
            float target = !playing ? musicVolume : here ? musicVolume * musicInCar : 0f;
            musicLevel = Mathf.MoveTowards(musicLevel, target, dt * musicVolume / musicFade);
            music.volume = musicLevel * music.pitch;
            if (gramophoneRecord) gramophoneRecord.RotateAround(centre, gramophone.up, recordRpm * 6f * music.pitch * dt);
        }
    }
}
