using System.Collections.Generic;
using UnityEngine;

namespace RailTracer
{
    // A car's look: everything you see of it but the people (the shell, seats, racks, curtains, lamps and their lights,
    // dining furniture, props). Built from the kit into one "Car Look" container, or loaded from its baked prefab,
    // Resources/CarLooks/Look_NN (NN = the Journey car, 00-14), made with Rail Tracer > Car Looks and edited by hand.
    // The gameplay tiles always come from Train.cs: a prefab only changes what you see.
    public partial class Game
    {
        public const string LookFolder = "CarLooks";
        const string Empty = " (empty)"; // a prop that starts empty: an ashtray without its cigar, a hook without its hat

        // The prefab name for a car, or null (the walk-through car and endless cars are always built).
        public static string LookId(bool walk, bool endlessCar, int carIndex) =>
            walk || endlessCar || carIndex < 0 || carIndex >= Train.Cars ? null : $"Look_{carIndex:00}";

        // The Journey car being played (-1: endless or the walk-through car), for Bake This Car.
        public int LiveLookCar => walking || endless ? -1 : carIndex;

        // For Editor/CarLookBaker (Play mode): Journey car g's look built fresh from the kit, at the origin, not part of
        // the game. Everything in it uses asset materials, so it saves as a prefab as it is.
        public GameObject BuildLookForBake(int g)
        {
            var kit = Resources.Load<TrainKit>("Kit/TrainKit");
            var def = Train.Demo(g);
            var look = new GameObject("Car Look").transform;
            if (BuildKitCarriage(kit, Train.Make(def, tuning, 1, MaxHpCarry), def.map, def.kind, look, true, Train.TrainOf(g), def.name))
                return look.gameObject;
            Destroy(look.gameObject);
            return null;
        }

        // What a look needs at runtime, found by name, so a built car and a baked (and hand-edited) one behave the same:
        // the train's own wallpaper and curtains, see-through bar glasses, each wall's wood grain shifted; and in the
        // live car the sliding sashes, the gramophone, the lamps' lights, lit cigars, empty ashtrays and hooks.
        // baggage: wooden boarding (Trn_WallPanel) where the wallpaper goes.
        void WakeLook(Transform look, TrainKit kit, int train, int rows, bool live, bool baggage = false)
        {
            TrainLook(kit, look, train, baggage);
            foreach (var t in look.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                // The brass luggage racks over the booth seats (train 3's booth coaches): no shadows, so the seats and
                // lamps below aren't striped by them; they still receive light and shadow (20.2).
                if (n.StartsWith("Rack_Brass"))
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (n.StartsWith("Wall_")) Wood(t.gameObject);
                if (n.StartsWith("Prop_Ashtray_Cigar"))
                {
                    MatteBrass(t.gameObject);
                    if (n.EndsWith(Empty)) HideCigar(t.gameObject);
                    else if (live) Smolder(t.gameObject);
                }
                if (n.StartsWith("Prop_HatHook") && n.EndsWith(Empty)) HideParts(t.gameObject, "Trn_Felt", "Trn_BlackCloth");
                if (!live) continue;
                if (n.StartsWith("Window_Sash_"))
                {
                    var sash = t.GetComponent<SlidingWindow>() ?? t.gameObject.AddComponent<SlidingWindow>();
                    int row = Mathf.Abs(Mathf.DeltaAngle(t.localEulerAngles.y, 180f)) < 1f ? rows - 1 : 0; // turned round: the far wall
                    sashes.Add((sash, row, t.localPosition.z - 1f, n.Contains("Large") ? 1f : 0.5f)); // shell z = game x + 1
                }
                if (n.StartsWith("Prop_Gramophone")) SetGramophone(t);
                if (t.GetComponent<Light>() is Light light)
                {
                    if (n == "Seat Lamp") lamps.Add(light);
                    else if (n == "Table Lamp") { lamps.Add(light); tableLamps.Add(light); }
                    else if (n == "Picture Light") { lamps.Add(light); pictureLights.Add(light); } // reddens with the rest (19.65)
                }
            }
            if (!live) return;
            // The camera-side wall is drawn only as shadow, so its windows get a pale glass-blue line along the floor
            // at the wall's foot, one per window opening.
            foreach (var (_, row, x, half) in sashes)
            {
                if (row != rows - 1) continue;
                var line = Line(new Color(0.65f, 0.82f, 1f, 0.22f), 0.04f); // faint: a hint, not a give-away
                float z = -(rows - 1) - 0.42f;
                line.positionCount = 2;
                line.SetPosition(0, new Vector3(x - half + 0.08f, 0.05f, z));
                line.SetPosition(1, new Vector3(x + half - 0.08f, 0.05f, z));
            }
        }

        // The wood below the dado: each wall module's grain shifted at random, so the 1 and 2 m modules don't all show
        // the same stretch of the texture (the wainscot's stiles hide the joins).
        static void Wood(GameObject wall)
        {
            foreach (var r in wall.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || mats[i].name != "Trn_Wood" || !mats[i].HasProperty("_BaseMap_ST")) continue;
                    var st = mats[i].GetVector("_BaseMap_ST");
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block, i);
                    block.SetVector("_BaseMap_ST", new Vector4(st.x, st.y, Random.value, Random.value));
                    r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
