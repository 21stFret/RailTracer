using UnityEngine;

namespace RailTracer
{
    // A kit window sash (Window_Sash_Small / _Large) that slides up into the wall cavity above the window head,
    // pushed by the pull on its bottom rail. Placed on its wall's transform, so closed = local y 0. Anything
    // parented to it (blood on the glass) rides along.
    public class SlidingWindow : MonoBehaviour
    {
        public bool open;
        [Range(0f, 0.87f)] public float openRise = 0.87f; // metres it rises when open: all the way, its top just under the wall top
        public float speed = 2.5f;                        // metres per second

        float rise;

        public bool IsOpen => Mathf.Approximately(rise, openRise);
        public bool IsShut => rise <= 0f;
        public void Toggle() => open = !open;

        void Update()
        {
            float target = open ? openRise : 0f;
            if (Mathf.Approximately(rise, target)) return;
            rise = Mathf.MoveTowards(rise, target, speed * Time.deltaTime);
            var p = transform.localPosition;
            transform.localPosition = new Vector3(p.x, rise, p.z);
        }
    }
}
