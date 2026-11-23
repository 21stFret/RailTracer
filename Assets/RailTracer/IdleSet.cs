using UnityEngine;

namespace RailTracer
{
    // The standing idles enemies pick from, one at random each (Resources/Anim/IdleSet). Filled by Editor/AnimationSetup:
    // relaxed = Synty's standing idle, the Starter Assets idle and any "Idle*.fbx" in RailTracer/Mixamo; armed = the
    // Mixamo "Pistol Idle*.fbx" clips. Game swaps the pick in over the controller's own clip per enemy.
    public class IdleSet : ScriptableObject
    {
        public AnimationClip[] relaxed = new AnimationClip[0];
        public AnimationClip[] armed = new AnimationClip[0];
        // 20.2: heavies carry no gun, so when not unaware they take `heavy` (the Heavy Fighter pack's Heavy@Idle, a fists-up
        // stance) in place of the enemy controller's armed idle (`armedSlot`, its "Idle" state's clip). Empty: no swap.
        public AnimationClip heavy, armedSlot;
    }
}
