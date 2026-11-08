using UnityEngine;

namespace RailTracer
{
    // The music: DarkJazz (Resources/Audio/DarkJazz1) on ONE looping AudioSource for the whole game, so it never
    // restarts. It fades in over the menus as a flat (2D) track. Diving into a car with a gramophone, the same source
    // slides over to 3D at the record (spatial blend 0 to 1 over the dive), so the music you hear in the menu carries
    // on from the gramophone at the same moment of the track. A death next to the gramophone (Carriage.musicStopped)
    // winds it down: the pitch, the volume and the record's spin run out together. In a car without one the music
    // fades out while you play and comes back in the menus.
    public partial class Game
    {
        [Header("Music")]
        public float musicVolume = 0.6f;
        public float musicFade = 2.5f;     // seconds to fade in or out
        public float musicWindDown = 1.6f; // seconds for the record to run down when the music's stopped
        public float musicNear = 2f, musicFar = 30f; // the gramophone's 3D falloff (linear), metres
        public float recordRpm = 78f;      // a shellac 78

        AudioSource music;
        bool enabledMusic = true; // false once the track was found missing (warned once)
        float musicLevel;          // the fade, before the wind-down
        Transform gramophone, gramophoneRecord;
        Vector3 recordPivot;       // the record's centre, in the gramophone's space

        // The live car's gramophone model (GameFlow.DiningModels): its Record child spins about the record's centre.
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
                var listener = FindAnyObjectByType<AudioListener>();
                if (!listener) listener = cam.gameObject.AddComponent<AudioListener>();
                var cfg = AudioSettings.GetConfiguration();
                Debug.Log($"Rail Tracer: music {clip.name} ({clip.length:0}s) fading in to {musicVolume}; listener on {listener.name}; " +
                          $"output {AudioSettings.outputSampleRate} Hz, {cfg.speakerMode}, master volume {AudioListener.volume}" +
                          (AudioSettings.outputSampleRate == 0 ? " - NO AUDIO DEVICE" : ""));
                music = new GameObject("Music").AddComponent<AudioSource>();
                music.clip = clip;
                music.loop = true;
                music.playOnAwake = false;
                music.volume = 0f;
                music.spatialBlend = 0f;
                music.dopplerLevel = 0f;
                music.rolloffMode = AudioRolloffMode.Linear;
                music.Play();
            }
            music.minDistance = musicNear;
            music.maxDistance = musicFar;
            float dt = Time.unscaledDeltaTime; // pauses and slow motion don't touch the music
            bool playing = phase != Phase.Menu;
            bool here = gramophone;
            var centre = here ? gramophone.TransformPoint(recordPivot) : Vector3.zero;
            if (here) music.transform.position = centre;
            music.spatialBlend = Mathf.MoveTowards(music.spatialBlend, playing && here ? 1f : 0f, dt / 1.1f); // the dive
            music.pitch = Mathf.MoveTowards(music.pitch, playing && c.musicStopped ? 0f : 1f, dt / musicWindDown);
            musicLevel = Mathf.MoveTowards(musicLevel, playing && !here ? 0f : musicVolume, dt * musicVolume / musicFade);
            music.volume = musicLevel * music.pitch;
            if (gramophoneRecord) gramophoneRecord.RotateAround(centre, gramophone.up, recordRpm * 6f * music.pitch * dt);
        }
    }
}
