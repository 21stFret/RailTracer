using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace RailTracer
{
    // Steam achievements. The ids are the API names set up in Steamworks (App Admin > Stats & Achievements).
    // Without Steam running (the editor with no steam_appid.txt, a build started outside Steam) it does nothing.
    public static class Achievements
    {
        public const string Train1 = "TRAIN_1", Train2 = "TRAIN_2", Train3 = "TRAIN_3", // train 3: the end of the game
            Untouched = "UNTOUCHED", BreakThree = "BREAK_THREE", Unseen = "UNSEEN",
            Endless10 = "ENDLESS_10", Bloodied = "BLOODIED";

        static bool ready;
        static readonly HashSet<string> sent = new HashSet<string>(); // asked this session: cheap to call every frame

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            sent.Clear();
            try { ready = SteamAPI.Init(); }
            catch (System.Exception ex) { ready = false; Debug.LogWarning("Rail Tracer: Steam isn't available (" + ex.Message + ")."); }
            if (ready) Application.quitting += () => { ready = false; SteamAPI.Shutdown(); };
        }

        // Once a frame (Game.Update).
        public static void Tick() { if (ready) SteamAPI.RunCallbacks(); }

        public static void Unlock(string id)
        {
            if (!ready || !sent.Add(id)) return;
            if (SteamUserStats.GetAchievement(id, out bool had) && had) return;
            if (SteamUserStats.SetAchievement(id)) SteamUserStats.StoreStats();
            else Debug.LogWarning("Rail Tracer: Steam doesn't know the achievement " + id + ".");
        }
    }
}
