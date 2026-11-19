using UnityEngine;

namespace RailTracer
{
    // The train kit's FBX modules (Assets/RailTracer/Train Assets), listed so the game can load them at runtime.
    // Editor/TrainKitSetup.cs keeps it in sync whenever the kit is re-exported.
    public class TrainKit : ScriptableObject
    {
        public GameObject[] modules;
        public Material[] materials; // every kit material (the per-train curtains and wallpapers are swapped in by name)

        public GameObject Get(string name) => modules == null ? null : System.Array.Find(modules, m => m && m.name == name);
        public Material Mat(string name) => materials == null ? null : System.Array.Find(materials, m => m && m.name == name);
    }
}
