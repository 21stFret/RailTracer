using UnityEngine;

namespace RailTracer
{
    // The train kit's FBX modules (Assets/RailTracer/Train Assets), listed so the game can load them at runtime.
    // Editor/TrainKitSetup.cs keeps it in sync whenever the kit is re-exported.
    public class TrainKit : ScriptableObject
    {
        public GameObject[] modules;

        public GameObject Get(string name) => modules == null ? null : System.Array.Find(modules, m => m && m.name == name);
    }
}
