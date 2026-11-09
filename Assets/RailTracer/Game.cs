using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace RailTracer
{
    // White-box view of one carriage: primitives built at runtime, Unity UI for the HUD (Hud.cs).
    public partial class Game : MonoBehaviour
    {
        public Tuning tuning = new Tuning();
        public SkillTable skillTable; // empty = Resources/SkillTable
        public int seed; // 0 = random each run
        public float enemyStepDelay = 0.4f;
        public bool useTrainKit = true;      // off = the white-box carriage
        public bool cutawayNearWall = true;  // camera-side wall casts shadows but isn't drawn

        [Header("Seat lamps (kit carriage only)")]
        public float lampHeight = 2.1f;   // just above head height
        public float lampInset = 0.3f;    // in from the wall, so the light isn't inside it
        public float wallLampEvery = 4f;  // dining car and baggage hold: a wall lamp about this often along each side (m)
        // A lit table lamp: a spot pointing straight down from the bulb, a soft-edged pool on the table.
        // (Renamed again from tableLampIntensity etc.: an open scene keeps a field's first default, so a new name is
        // what makes a new value take.)
        public float tableLampPower = 0.1f, tableLampReach = 1f, tableLampCone = 179f, tableLampInnerCone = 0f;
        readonly HashSet<Light> tableLamps = new HashSet<Light>(); // lit at their own level (LegendLighting)
        public float sunDim = 0.01f;      // the directional light over the kit carriage: on, no shadows, very dim
        public Color lampColor = new Color(1f, 0.78f, 0.5f);
        // Wall lamps (renamed from lampIntensity / lampRange / lampAngle so these override the scene): the max (legend 0).
        public float wallLampIntensity = 2f, wallLampRange = 4.5f, wallLampAngle = 120f, wallLampInnerAngle = 0f;
        // The legend reddens the carriage: lamp colour and brightness (x wallLampIntensity) at legend 0 (lampColor), 25, 50, 75 and 100,
        // blended over about a second. Lamps and ambient only: every overlay is unlit, so it never changes.
        public Color[] legendLampColors = { new Color(1f, 0.78f, 0.5f), new Color(1f, 0.66f, 0.38f), new Color(1f, 0.42f, 0.26f), new Color(1f, 0.22f, 0.13f), new Color(0.8f, 0.05f, 0.04f) };
        public float[] legendLampLevels = { 1f, 0.8f, 0.65f, 0.5f, 0.45f };
        readonly List<Light> lamps = new List<Light>();
        Color shownLampColor; float shownLampLevel = -1f;
        Color baseAmbient; float baseAmbientIntensity;
        [Range(0f, 90f)] public float lampTilt = 80f; // degrees below horizontal: 90 = straight down
        public bool lampShadows = true;        public string lampModule = "Lamp_Wall"; // placed at each lamp once the train kit has a module by this name

        [Header("Characters")]
        public string[] tintedParts = { "Suit", "Pants" }; // meshes dressed in Resources/Materials/Fabric, tinted per character (Mixamo Ch33 parts)
        public Color conductorSuitColor = new Color(0.66f, 0.56f, 0.42f); // the player's tintedParts: the conductor's beige
        public Color weakSuitColor = new Color(0.035f, 0.035f, 0.04f);     // enemy suits by type: basic gunmen in black
        public Color strongSuitColor = new Color(0.5f, 0.05f, 0.06f);      // tough ones in red, to stand out
        public Color brokenColor = new Color(0.22f, 0.22f, 0.22f);      // anyone broken and begging
        public string[] skinParts = { "Body" };                        // meshes that take the skin tone
        public Color skinColor = new Color(0.78f, 0.58f, 0.45f);        // multiplied over the skin texture
        Material fabric;
        public Color shoesColor = new Color(0.03f, 0.03f, 0.03f);       // everyone (multiplied over the texture)
        public Color hairColor = new Color(0.4f, 0.24f, 0.12f);         // everyone (multiplied over the texture)
        public Color bloodOverlayColor = new Color(0.45f, 0.03f, 0.03f); // multiplied over the player as the legend fills
        public float bloodPatternScale = 6f, bloodSpreadSpeed = 0.5f;
        Material bloodOverlay;
        // Bloody footprints behind you as you walk once your legend reaches Footprints From (looks only). Left and right
        // in turn, Footprint Stride apart, Footprint Gap either side of your line, staying for the rest of the car. Drawn
        // as the blood's own decals (a copy of its material with a shoe-print texture in the blood texture's red,
        // Footprint Color), projected Footprint Depth deep onto whatever is there: floor, aisle runner, a body.
        public int footprintsFrom = 50;
        public float footprintStride = 0.42f, footprintGap = 0.09f, footprintLength = 0.32f, footprintWidth = 0.115f, footprintDepth = 0.5f;
        public Color footprintColor = new Color(0.45f, 0.12f, 0.12f, 0.9f);
        Material[] footprintMats; // left foot, right foot (mirrored)
        float strideLeft;
        bool leftFoot;
        // The kit floor's velvet aisle runner (1 m wide down the car's middle) stands Runner Height proud of the floor:
        // characters walking on it stand on it, eased in over its edges, so their feet don't sink in.
        public float runnerHeight = 0.015f;
        bool onKit; // this car is the kit carriage (the whitebox has no runner)

        Vector3 OnRunner(Vector3 p)
        {
            if (!onKit) return p;
            float d = Mathf.Abs(p.z + (c.H - 1) / 2f); // from the car's centreline
            return p + Vector3.up * runnerHeight * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.38f, d));
        }
        // The demon aura (Resources/Effects/Demon Aura) round your feet: fades in with the same eased legend as the
        // blood on you, from nothing at auraFrom (of the full legend) to the prefab's own colours at auraFull.
        [Range(0f, 1f)] public float auraFrom = 0.1f, auraFull = 1f;
        ParticleSystem[] aura;
        Transform auraHolder;
        ParticleSystem.MinMaxGradient[] auraColors;
        float auraShown = -1f;
        float shownBlood;
        Light sun;

        static readonly Color Floor = new Color(0.45f, 0.45f, 0.45f);
        static readonly Color SightCol = new Color(1f, 0.75f, 0.1f), HearCol = new Color(0.6f, 0.2f, 0.9f), ReactCol = new Color(0.9f, 0.15f, 0.1f), BloodEdge = new Color(1f, 0.12f, 0.08f);
        // The HUD palette on the floor: bone white for you and your targets, blood red for danger.
        static readonly Color Bone = new Color(0.949f, 0.937f, 0.918f), DangerRed = new Color(0.784f, 0.063f, 0.18f);

        Carriage c;
        Camera cam;
        Transform root;
        Renderer[,] tiles;
        Renderer playerBody;
        readonly Dictionary<Enemy, (Renderer body, Renderer nose)> views = new Dictionary<Enemy, (Renderer, Renderer)>();
        SkillId? selected; // hotkeys 1-8; no kill selected = clicking an enemy does nothing
        bool busy, showAllSenses, leaping, hopping, throwing, endShown;
        SkillHud hud;
        string[] Map => car.map; // the car being played (GameFlow: which car, its seed, your health coming in)
        readonly Tutorial tut = new Tutorial();
        bool walkQueued;          // the opening walkthrough has had its chance this level
        Cell? reactTile;          // a reaction-fire tile on the current path preview (the Reaction fire card)
        readonly List<Enemy> taken = new List<Enemy>(); // tickets, in the order taken
        readonly Dictionary<Enemy, RectTransform> chips = new Dictionary<Enemy, RectTransform>();
        int hudSlot = -1;   // skill box under the mouse
        // The last-seen ghost: a see-through you, frozen in the pose you were in when last seen (one at a time).
        Transform ghost;
        Renderer[] ghostRenderers;
        Cell? ghostCell, pendingGhost;
        readonly HashSet<Enemy> gone = new HashSet<Enemy>(); // fled enemies who have reached the door
        LineRenderer ghostBody; // kill preview: where the body lands
        [Header("Debug (Play mode: edit to set the live values)")]
        [Range(0, 100)] public int debugLegend;
        public int debugLeapWait, debugRailWait, debugThrowWait;
        bool debugDirty;
        public bool followCamera = true; // cut to each enemy as it acts
        Vector3? camTarget;
        Material lineMat;
        readonly Dictionary<Enemy, (LineRenderer route, LineRenderer arrow)> marks = new Dictionary<Enemy, (LineRenderer, LineRenderer)>();
        readonly HashSet<Cell> decaled = new HashSet<Cell>(); // stained tiles that already have a blood decal
        public bool bloodOutlines;                              // red outlines on stained tiles too (B toggles; off by default)
        LineRenderer[,] bloodEdges;
        readonly HashSet<Body> flying = new HashSet<Body>();   // thrown bodies still in the air
        readonly HashSet<Animator> armed = new HashSet<Animator>(); // controllers with the "Armed" bool
        readonly Dictionary<Enemy, (float start, float yaw, float next)> idleLook = new Dictionary<Enemy, (float, float, float)>();
        readonly Dictionary<Body, Cell> bodyCell = new Dictionary<Body, Cell>();
        readonly Dictionary<Enemy, Vector3> bodyOffset = new Dictionary<Enemy, Vector3>();
        readonly HashSet<Enemy> posedBodies = new HashSet<Enemy>(); // left as the clip ended (bent over a table): limp once moved
        Cell? parkedOn; // a cover finisher left you off your tile's centre: you stay there until you move
        Vector3 focus; // camera looks at this point on the floor
        float camDistance = 12f;
        public float panSpeed = 1.2f, zoomSpeed = 0.001f; // scroll is ~120 per wheel notch on Windows
        Cell? hover;

        // Mixamo characters, loaded from Resources/Anim. Falls back to capsules if any are missing.
        public float characterScale = 0.6f, unitSpeed = 2.5f; // tiles/sec; raise for snappier turns, lower if feet slide
        // Controllers are generated by Editor/AnimationSetup.cs and are yours to edit.
        public float flipLength = 1.1f; // how long the reveal leap takes to cross the floor
        // The "StylishFlip" (a bloodied leap past the normal range) crosses the floor as its clip does: share of the way
        // by time (s), sampled from the clip's hips (Stylish Flip.fbx, 30 fps); after the last key it has landed and stays.
        public AnimationCurve stylishFlipTravel = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.067f, 0.086f), new Keyframe(0.133f, 0.163f), new Keyframe(0.2f, 0.239f),
            new Keyframe(0.267f, 0.312f), new Keyframe(0.333f, 0.389f), new Keyframe(0.4f, 0.466f), new Keyframe(0.467f, 0.526f),
            new Keyframe(0.533f, 0.575f), new Keyframe(0.6f, 0.627f), new Keyframe(0.667f, 0.679f), new Keyframe(0.733f, 0.737f),
            new Keyframe(0.8f, 0.792f), new Keyframe(0.867f, 0.841f), new Keyframe(0.933f, 0.89f), new Keyframe(1f, 0.926f),
            new Keyframe(1.067f, 0.956f), new Keyframe(1.133f, 0.984f), new Keyframe(1.2f, 1f));
        bool stylishFlip;
        float flipTime;
        readonly HashSet<Enemy> begging = new HashSet<Enemy>(); // broken enemies who have dropped to their knees
        GameObject characterModel;
        // The guards' models (Editor/CharacterPackSetup.cs): Resources/Anim/Guard (NYC1950 CH01, shirt sleeves) and Guard_CH02
        // (CH02, a double-breasted suit). Heavies wear CH02, so their red covers a whole suit; guards alternate between
        // the two. Whichever is missing falls back to the other, then to the Mixamo character.
        GameObject guardModel, suitModel;
        GameObject GuardModel(Enemy e)
        {
            var pick = e.tough || e.id % 2 == 0 ? suitModel ?? guardModel : guardModel ?? suitModel;
            return pick ? pick : characterModel;
        }
        RuntimeAnimatorController enemyController, playerController;
        bool HasModels => characterModel && enemyController && playerController;
        float FigureHeight => playerAnim ? 1.8f * characterScale : 1f; // Mixamo characters are ~1.8 m at scale 1
        Animator playerAnim;
        readonly Dictionary<Enemy, Animator> anims = new Dictionary<Enemy, Animator>();
        readonly Dictionary<Animator, Color> tints = new Dictionary<Animator, Color>();
        readonly Dictionary<Enemy, Vector3> shown = new Dictionary<Enemy, Vector3>();
        // Walking: characters follow the tiles they actually walked, with the stride matched to the speed.
        readonly Queue<Vector3> playerRoute = new Queue<Vector3>();
        readonly Dictionary<Enemy, Queue<Vector3>> routes = new Dictionary<Enemy, Queue<Vector3>>();
        readonly Dictionary<Animator, Vector3> gaits = new Dictionary<Animator, Vector3>(); // walk, run, crouch m/s from the controller
        Vector3 playerShown, playerFacing = Vector3.right, flipFrom;
        float flipT = -1f; // >= 0 while the reveal leap is crossing
        bool wasLeaped, wasHopped;
        // Window hop: each kit sash with its row and the game x it spans; the hop climbs out of one and in the next.
        readonly List<(SlidingWindow sash, int row, float x, float half)> sashes = new List<(SlidingWindow, int, float, float)>();
        bool hopRunning, hopMoving;
        public float endTurnHold = 0.5f; // seconds Space must be held to end the turn
        float spaceHeld = -1f;
        bool endByMouse; // the end-turn hold was started on the HUD button (the mouse held, not Space)
        public float hopClimbTime = 0.45f, hopWalkSpeed = 3f;
        [Tooltip("Playback speed of the Vault clip on a window hop; the climb lasts the clip's length at this speed.")]
        public float hopVaultSpeed = 1.5f;
        // Brutal kill: player and victim play their "Takedown" states facing each other, locked to the tiles.
        public float takedownHold = 0.5f; // the corpse lingers this long after the clip
        // How far onto the victim's tile the player is (0..1) through the clip (0..1). Shape it in the Inspector:
        // the flat start and end are the window where the player stands still.
        public AnimationCurve takedownMove = AnimationCurve.EaseInOut(0.15f, 0f, 0.45f, 1f);
        Enemy takedownVictim;             // dead in the rules, still on screen while this is set
        readonly HashSet<Enemy> corpses = new HashSet<Enemy>(), pairedKills = new HashSet<Enemy>();
        readonly HashSet<Enemy> placed = new HashSet<Enemy>(); // shown alive at least once (SyncEnemyAnim placed them)
        // Quiet kill: the "StealthKill" pair. You stay on your tile in the rules, so you close in by up to
        // quietKillReach of a tile (along quietKillMove, same idea as takedownMove), then step back.
        public AnimationCurve quietKillMove = AnimationCurve.EaseInOut(0.05f, 0f, 0.3f, 1f);
        [Range(0f, 1f)] public float quietKillReach = 0.5f;
        public float quietKillBloodAt = 0.5f;
        public bool quietVictimFacesAway = true; // grabbed from behind; untick if the clip expects them facing you
        public float snatchTime = 0.25f;         // the snatch: the victim is yanked onto your tile this fast, then the stealth kill plays
        TakedownLibrary takedowns;               // Resources/Takedowns: the finishers kills choose from (else the old pair)
        // Kill cam: brutal kills (as long as the finisher), rail grabs and throws cut in close (no skipping: it always plays out).
        public float killCamClose = 1.25f, killCamHeight = 0.45f, sceneCamTime = 1.2f; // renamed from killCamDistance (1.9) to override the scene: tight on the pair
        public float killCamFlyIn = 0.45f, killCamPause = 0.2f, killCamFlyOut = 0.35f; // seconds (real time)
        // Kill cam slow motion (the Options setting): the game slows to this for Hit Slow Time (real seconds) on each hit
        // the kill cam shows (a takedown's Blood At and extra blood points, a rail grab's impact, a thrown body landing).
        [Range(0.1f, 1f)] public float killCamSlowMo = 1f;
        public float hitSlowTime = 0.5f;
        // A heavy's melee: the hit (blood, the red screen, your health going down) waits until it has walked up and its
        // "Swing" clip is this far in (seconds), so it lands with the blow.
        public float swingHitAt = 0.35f;
        bool holdHit;
        // Walking out of a cleared car: the walk clip (not the sprint), a little brisker than its own pace.
        public float walkOutPace = 1.25f;
        bool exiting;
        float WalkOutSpeed
        {
            get
            {
                float walk = playerAnim && gaits.TryGetValue(playerAnim, out var g) && g.x > 0f ? g.x : 1.4f; // the clip's m/s
                return walk * walkOutPace * characterScale;
            }
        }
        float slowUntil = -1f;

        void HitSlowMo()
        {
            if (!camming || killCamSlowMo >= 1f) return;
            Time.timeScale = killCamSlowMo;
            slowUntil = Time.unscaledTime + hitSlowTime;
        }
        bool camming;
        Vector3 savedFocus; float savedDistance; Quaternion savedCamRot;

        // Blood: Resources/Blood/BloodKit (made by Editor/BloodSetup.cs). Stains are parented to the carriage,
        // so they last the whole match and a restart starts the train clean.
        public float takedownBloodAt = 0.45f; // point in the takedown clip (0..1) where the blood flies
        // Kill blood: straight casts from this high above each stained tile (a crouched stealth kill lower), this far out.
        public float bloodCastStanding = 1.8f, bloodCastCrouched = 1f, bloodCastReach = 1.5f;
        BloodKit blood;
        Canvas screenCanvas;
        readonly Dictionary<Enemy, EnemyState> lastState = new Dictionary<Enemy, EnemyState>();
        int lastHits;
        Act? pendingAct; // what the player just did, so the next death knows how it happened (null = gunfire)

        // The aimed move or leap: a line per tile (bone = unseen, red = seen), a see-through you where you'll land, and
        // the enemies who'd fire on it (or on the aimed kill) with their hit chance.
        readonly List<LineRenderer> pathLines = new List<LineRenderer>();
        Transform previewGhost;
        Renderer[] previewGhostRenderers;
        readonly Dictionary<Enemy, int> threats = new Dictionary<Enemy, int>();
        readonly HashSet<Enemy> swingThreats = new HashSet<Enemy>(); // threats that are a heavy's swing, not a shot
        Cell? previewLanding; // the aimed move's end: the chips' crosshairs are for it
        // Reaction fire zone: one red dashed line round every tile a ready gun watches.
        readonly List<LineRenderer> zoneLines = new List<LineRenderer>();
        Material dashMat;
        // "?" floor markers where searchers are going to look, one per spot.
        readonly Dictionary<Cell, GameObject> clueMarks = new Dictionary<Cell, GameObject>();
        // Rail grab: bent over the window sill, head and arms out over the track, legs inside.
        readonly HashSet<Enemy> hung = new HashSet<Enemy>();
        public float railSillHeight = 0.55f;  // where the hips rest (metres)
        public float barShoveLift = 0.3f;     // Shove to Table over the bar: the victim this much higher than over a table
        // The hung body's final pose on the top side's window (row 0), from the tile: position and rotation (the other
        // side: the same turned half round). Set by hand in Play mode.
        public Vector3 railHangPos = new Vector3(0.335f, 1.334f, -0.691f), railHangRot = new Vector3(77.2229f, 180f, 189.13f);
        public float railLean = 80f;          // how far the body tips out (degrees from upright)
        [Header("Shots")]
        public float freeShotPause = 0.55f;   // the camera holds on each free-shooter this long before and after the shot
        public Color muzzleColor = new Color(1f, 0.75f, 0.35f);

        void Start()
        {
            characterModel = Resources.Load<GameObject>("Anim/Character");
            guardModel = Resources.Load<GameObject>("Anim/Guard");
            suitModel = Resources.Load<GameObject>("Anim/Guard_CH02");
            enemyController = Resources.Load<RuntimeAnimatorController>("Anim/Enemy");
            takedowns = Resources.Load<TakedownLibrary>("Takedowns");
            baseAmbient = RenderSettings.ambientLight;
            baseAmbientIntensity = RenderSettings.ambientIntensity;
            playerController = Resources.Load<RuntimeAnimatorController>("Anim/Player");
            if (!HasModels) Debug.LogWarning("Rail Tracer: character or controllers missing from Resources/Anim (run Rail Tracer > Set Up Animations), using capsules.");
            fabric = Resources.Load<Material>("Materials/Fabric");
            if (!fabric) Debug.LogWarning("Rail Tracer: no Resources/Materials/Fabric (made by Editor/FabricSetup.cs); suits keep the model's material.");
            blood = Resources.Load<BloodKit>("Blood/BloodKit");
            if (!blood) Debug.LogWarning("Rail Tracer: no blood kit in Resources/Blood (run Rail Tracer > Set Up Blood).");
            screenCanvas = new GameObject("Blood Screen").AddComponent<Canvas>();
            screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            if (!skillTable) skillTable = Resources.Load<SkillTable>("SkillTable");
            if (skillTable && skillTable.skills.Length == 8) tuning.skills = skillTable.skills;
            else Debug.LogWarning("Rail Tracer: no 8-row SkillTable in Resources; using the built-in skill defaults.");
            hud = new SkillHud();
            hud.Build(tuning.skills, tuning.maxLegend, tuning.moveRange, tuning.shakenAt, tuning.panickedAt, tuning.brokenAt);

            cam = Camera.main;
            if (!cam) { cam = new GameObject("Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.025f, 0.022f, 0.028f); // outside the train: near black
            cam.orthographic = false;
            cam.fieldOfView = 50;
            cam.transform.rotation = Quaternion.Euler(55, 0, 0);
            focus = new Vector3(6, 0, -(Map.Length - 1) / 2f);
            // The kit carriage is lit by its seat lamps; this sun is only for the white-box fallback (see Build).
            sun = System.Array.Find(FindObjectsByType<Light>(), l => l.type == LightType.Directional);
            if (!sun && !FindAnyObjectByType<Light>())
            {
                sun = new GameObject("Light").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
            hud.BuildMenu();
            LoadSave();
            ApplySettings();
            // The start screen over the saved game (loaded and paused), or over an empty car 1.
            bool resumed = false;
            if (save.run != null)
            {
                try { ResumeSave(); resumed = true; }
                catch (System.Exception ex) { Debug.LogWarning("Rail Tracer: the save didn't replay (" + ex.Message + "); starting fresh."); save.run = null; WriteSave(); }
            }
            if (!resumed) BuildEmptyBackdrop();
            OpenMenu();
            cam.transform.SetPositionAndRotation(MenuPose().pos, MenuPose().rot);
            menuCamPos = cam.transform.position;
            menuCamRot = cam.transform.rotation;
        }

        void Build()
        {
            EndKillCam(); // restarted mid-cam
            if (root) Destroy(root.gameObject);
            lamps.Clear();
            tableLamps.Clear();
            shownLampLevel = -1f; // snap to the new legend
            root = new GameObject("Carriage").transform;
            views.Clear();
            anims.Clear();
            corpses.Clear();
            placed.Clear();
            pairedKills.Clear();
            tints.Clear();
            shown.Clear();
            playerRoute.Clear();
            routes.Clear();
            gaits.Clear();
            busy = false;
            flipT = -1f;
            wasLeaped = wasHopped = false;
            marks.Clear();
            sashes.Clear();
            hopRunning = hopMoving = false;
            KeepTurnPrints();
            if (playerAnim) playerAnim.speed = 1f; // a vault cut short by the car ending
            armed.Clear();
            guns.Clear(); // they went with the old car
            idleLook.Clear();
            begging.Clear();
            bodyCell.Clear();
            bodyOffset.Clear();
            posedBodies.Clear();
            parkedOn = null;
            takedownVictim = null;
            selected = null;
            // The car fresh, or a save's turn replayed onto it (the same dice, so nothing rerolls).
            c = Train.Make(car, tuning, rulesSeed, startHp);
            if (replay != null) foreach (var cmd in replay) c.Do(cmd);
            bool resumed = replay != null && replay.Count > 0;
            replay = null;
            if (phase == Phase.Play && !resumed)
            {
                if (car.kind == CarKind.WalkThrough) { if (!demoOver) hud.ShowCard("Between cars", "A quiet carriage.", "Walk on to the far door.", null, 2.5f, true); }
                else hud.ShowCard(CarLabel(), car.name, "Time to collect.", null, 3f, true);
            }
            endShown = false;
            walkQueued = false;
            taken.Clear();
            tut.Clear();
            if (hud.TutorialUp) hud.HideTutorial();
            choice = null;
            hud.ShowPause(false);
            if (hud.LogOpen) hud.HideLog();
            Time.timeScale = 1f;
            lastState.Clear();
            foreach (var e in c.enemies) lastState[e] = e.state;
            lastHits = 0;
            pendingAct = null;

            // Tile colours are transparent overlays just above whatever floor is there.
            tiles = new Renderer[c.W, c.H];
            var overlay = OverlayMaterial();
            for (int x = 0; x < c.W; x++)
                for (int y = 0; y < c.H; y++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(quad.GetComponent<Collider>());
                    quad.transform.SetParent(root);
                    quad.transform.localPosition = new Vector3(x, 0.03f, -y); // above the kit's velvet runner (0.015)
                    quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    quad.transform.localScale = Vector3.one; // the rim shader insets itself
                    tiles[x, y] = quad.GetComponent<Renderer>();
                    tiles[x, y].sharedMaterial = overlay;
                }
            decaled.Clear();
            bloodEdges = new LineRenderer[c.W, c.H];
            flying.Clear();
            foreach (var e in c.enemies)
            {
                var route = Line(Fill(DangerRed, 0.7f), 0.04f);
                if (e.route != null)
                {
                    route.loop = true;
                    route.positionCount = e.route.Count;
                    for (int i = 0; i < e.route.Count; i++) route.SetPosition(i, World(e.route[i]) + Vector3.up * 0.06f);
                }
                marks[e] = (route, Line(Color.white, 0.05f));
            }
            gramophone = null; // the old car's went with it
            bool kitBuilt = onKit = useTrainKit && BuildKitCarriage(Resources.Load<TrainKit>("Kit/TrainKit"), c, Map, car.kind, root, true);
            if (!kitBuilt) BuildWhitebox();
            if (sun)
            {
                sun.enabled = true;
                if (kitBuilt) { sun.intensity = sunDim; sun.shadows = LightShadows.None; } // the lamps light the car
            }
            BuildGlow();
            // Something for flying guts to land on (the tiles themselves have no colliders).
            var floor = new GameObject("Floor").AddComponent<BoxCollider>();
            floor.transform.SetParent(root);
            floor.center = new Vector3((c.W - 1) / 2f, -0.05f, -(c.H - 1) / 2f);
            floor.size = new Vector3(c.W + 2, 0.1f, c.H + 2);

            playerBody = Box(Vector3.zero, new Vector3(0.6f, 0.5f, 0.6f), Color.white, PrimitiveType.Capsule);
            foreach (var e in c.enemies)
                views[e] = (Box(Vector3.zero, new Vector3(0.6f, 0.5f, 0.6f), SuitColor(e), PrimitiveType.Capsule),
                            Box(Vector3.zero, new Vector3(0.15f, 0.15f, 0.3f), Color.black));

            playerShown = new Vector3(c.player.x, 0, -c.player.y);
            focus = playerShown; // a new game or restart starts on you
            camTarget = null;
            foreach (var e in c.enemies) shown[e] = new Vector3(e.pos.x, 0, -e.pos.y);
            pathLines.Clear();
            zoneLines.Clear();
            previewGhost = null;
            previewGhostRenderers = null;
            threats.Clear();
            clueMarks.Clear();
            shotMarks.Clear(); // they went with the old car
            hung.Clear();
            ghostBody = Line(BloodEdge, 0.06f);
            ghostBody.loop = true;
            ghost = null;
            ghostRenderers = null;
            ghostCell = pendingGhost = null;
            gone.Clear();
            // The Inspector's debug sliders follow the new car (Unity reports a change to them as Play mode starts,
            // which would otherwise overwrite a resumed save's legend and cooldowns with the sliders' old values).
            debugDirty = false;
            debugLegend = c.legend;
            debugLeapWait = c.leapWait;
            debugRailWait = c.railWait;
            debugThrowWait = c.throwWait;
            // A resumed save: rail-grab bodies hang out of their windows again, fleers are already gone.
            foreach (var b in c.bodies) if (b.hung) { hung.Add(b.who); Smash(b.pos, false); } // and their windows stay smashed
            foreach (var e in c.enemies) if (e.state == EnemyState.Fled) gone.Add(e);
            if (!HasModels) return;
            playerBody.enabled = false;
            playerAnim = Character(characterModel, new AnimatorOverrideController(playerController)); // its "Finisher" takes each takedown's clip
            playerAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate; // keeps going while hidden outside on a window hop
            Colour(playerAnim, tintedParts, conductorSuitColor);
            AddBloodOverlay(playerAnim);
            var knifeKit = Resources.Load<TrainKit>("Kit/TrainKit");
            knife = HoldKnife(playerAnim, knifeKit ? knifeKit.Get("Prop_Knife") : null, characterScale);
            AddAura(playerAnim.transform);
            playerEyes = AddEyes(playerAnim, tracerEyeColor, true);
            shownBlood = 0f;
            foreach (var e in c.enemies)
            {
                views[e].body.enabled = views[e].nose.enabled = false;
                var a = anims[e] = Character(GuardModel(e), new AnimatorOverrideController(enemyController));
                a.Play(0, 0, Random.value); // out of sync so they don't bob together
                AddEyes(a, e.id % 2 == 0 ? enemyEyeBlue : enemyEyeGreen, false);
                if (a.parameters.Any(p => p.name == "Armed")) armed.Add(a);
                if (!e.tough) { var gun = HoldGun(a); if (gun != null) guns[e] = gun; } // heavies don't shoot
            }
        }

        Animator Character(GameObject model, RuntimeAnimatorController controller)
        {
            var go = Instantiate(model, root);
            var a = go.GetComponent<Animator>();
            if (!a) a = go.AddComponent<Animator>();
            a.runtimeAnimatorController = controller;
            a.applyRootMotion = false; // the game owns position; clips stay on the tile
            Colour(a, new[] { "Shoes" }, shoesColor);
            Colour(a, new[] { "Hair" }, hairColor);
            Colour(a, skinParts, skinColor);
            Lashes(a);
            if (fabric)
                foreach (var r in a.GetComponentsInChildren<Renderer>())
                    if (System.Array.Exists(tintedParts, p => r.name.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0))
                        r.materials = r.sharedMaterials.Select(_ => new Material(fabric)).ToArray();
            return a;
        }

        // Colour multiplied onto the meshes whose names contain one of the parts (Mixamo Ch33_Suit, Ch33_Hair...), or, on
        // a mesh with several materials (the guard model: one mesh, Chr_Suit, Chr_Hair...), onto the materials named so.
        static void Colour(Animator a, string[] parts, Color col)
        {
            bool Named(string n) => System.Array.Exists(parts, p => n.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (var r in a.GetComponentsInChildren<Renderer>())
                if (Named(r.name)) foreach (var m in r.materials) m.color = col;
                else if (r.sharedMaterials.Length > 1 && r.sharedMaterials.Any(m => m && Named(m.name)))
                    foreach (var m in r.materials) if (Named(m.name)) m.color = col;
        }

        Color SuitColor(Enemy e) => e.state == EnemyState.Broken ? brokenColor : e.tough ? strongSuitColor : weakSuitColor;

        // Type colour on the clothing parts only (meshes named in tintedParts); skin etc. keep their textures.
        void Tint(Animator a, Color col)
        {
            if (tints.TryGetValue(a, out var old) && old == col) return;
            tints[a] = col;
            Colour(a, tintedParts, col);
        }

        // The legend shows as blood soaking over the player's own materials (Resources/Shaders/BloodOverlay):
        // one shared overlay material appended to every renderer, so one value drives the whole body.
        void AddBloodOverlay(Animator a)
        {
            var shader = Resources.Load<Shader>("Shaders/BloodOverlay");
            if (!shader) { Debug.LogWarning("Rail Tracer: Resources/Shaders/BloodOverlay missing; no blood on the player."); return; }
            bloodOverlay = new Material(shader);
            bloodOverlay.SetColor("_BloodColor", bloodOverlayColor);
            bloodOverlay.SetFloat("_Scale", bloodPatternScale);
            foreach (var r in a.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Append(bloodOverlay).ToArray();
        }

        // The carriage from the RailTracer train kit (Assets/RailTracer/Train Assets), laid out on the game grid the
        // way TrainCarBuilder does: modules run along the shell's local +Z (walls from z = 0.5, end caps outside),
        // side A on local -X. The shell is turned so local +Z runs along the game's +X: cell (x, y) is at local
        // (y - 2, 0, x + 1). Checked z-fighting-free with the kit's joint_check (partition on a joint, racks clear).
        bool BuildKitCarriage(TrainKit kit, Carriage car, string[] map, CarKind kind, Transform parent, bool live)
        {
            if (!kit || car.H != 5) return false; // the kit's interior is exactly 5 m (rows) wide
            var missing = new[] { "EndCap_Door", "Floor_1m", "Wall_1m_Solid", "Wall_1m_Pilaster", "Wall_1m_WindowSmall",
                "Wall_2m_Window", "Partition_Door", "Seat_Double", "LuggageRack_Full_A", "LuggageRack_Full_B", "LuggageRack_Full_C" }
                .Where(n => !kit.Get(n)).ToArray();
            if (missing.Length > 0) { Debug.LogWarning("Rail Tracer: train kit is missing " + string.Join(", ", missing) + "; using white-box."); return false; }

            char At(int x, int y) => map[y][x];
            bool Part(int x) => Enumerable.Range(0, car.H).All(y => y == car.H / 2 ? At(x, y) != 'W' : At(x, y) == 'W'); // walled off but for the aisle door
            var shell = new GameObject("Train Car").transform;
            shell.SetParent(parent, false);
            shell.localPosition = new Vector3(-1f, 0f, -(car.H - 1) / 2f);
            shell.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GameObject Place(string module, float x, float z, float rotY)
            {
                var go = Instantiate(kit.Get(module), shell);
                go.transform.localPosition = new Vector3(x, 0f, z);
                go.transform.localRotation = Quaternion.Euler(0f, rotY, 0f);
                return go;
            }

            Place("EndCap_Door", 0f, 0.25f, 0f);
            Place("EndCap_Door", 0f, car.W + 0.75f, 180f);
            PlaceWalls(0, 0f, false);
            PlaceWalls(car.H - 1, 180f, cutawayNearWall);
            void PlaceWalls(int row, float rot, bool shadowOnly)
            {
                // Seat columns get pilasters, the partition column a solid wall, every run of bay tiles windows.
                float s = 0.5f;
                int run = 0;
                void Flush()
                {
                    for (; run >= 2; run -= 2, s += 2f) { Panel(Place("Wall_2m_Window", 0f, s + 1f, rot)); Sash("Window_Sash_Large", s + 1f); }
                    if (run == 1) { Panel(Place("Wall_1m_WindowSmall", 0f, s + 0.5f, rot)); Sash("Window_Sash_Small", s + 0.5f); s += 1f; run = 0; }
                }
                void Sash(string module, float z) // the glass in that window, on the wall's own transform
                {
                    if (!kit.Get(module)) return; // older kit export without sashes: open windows
                    var sash = Place(module, 0f, z, rot);
                    if (live) sashes.Add((sash.AddComponent<SlidingWindow>(), row, z - 1f, module.EndsWith("Large") ? 1f : 0.5f)); // shell z = game x + 1
                    Panel(sash);
                }
                void Panel(GameObject wall)
                {
                    if (!shadowOnly) return; // the camera side: still casts shadows, but you can see in
                    foreach (var r in wall.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                for (int x = 0; x < car.W; x++)
                {
                    var at = new Cell(x, row);
                    string solo = At(x, row) == 'S' ? "Wall_1m_Pilaster" : Part(x) || car.BlankWall(at) || car.IsCage(at) ? "Wall_1m_Solid" : null;
                    if (solo == null) { run++; continue; }
                    Flush();
                    Panel(Place(solo, 0f, s + 0.5f, rot));
                    s += 1f;
                }
                Flush();
            }

            // The camera-side wall is drawn only as shadow, so its windows get a pale glass-blue line along the floor
            // at the wall's foot, one per window opening.
            foreach (var (_, row, x, half) in live ? sashes : new List<(SlidingWindow, int, float, float)>())
            {
                if (row != car.H - 1) continue;
                var line = Line(new Color(0.65f, 0.82f, 1f, 0.22f), 0.04f); // faint: a hint, not a give-away
                float z = -(car.H - 1) - 0.42f;
                line.positionCount = 2;
                line.SetPosition(0, new Vector3(x - half + 0.08f, 0.05f, z));
                line.SetPosition(1, new Vector3(x + half - 0.08f, 0.05f, z));
            }

            var seatColumns = Enumerable.Range(0, car.W).Where(x => Enumerable.Range(0, car.H).Any(y => At(x, y) == 'S')).ToList();
            var seats = new List<Transform>();
            string[] racks = { "LuggageRack_Full_A", "LuggageRack_Full_B", "LuggageRack_Full_C" };
            for (int x = 0; x < car.W; x++)
            {
                float z = x + 1f;
                Place("Floor_1m", 0f, z, 0f);
                if (Part(x)) Place("Partition_Door", 0f, z + 0.5f, 0f); // on the joint after its tile
                for (int y = 0; y < car.H; y++)
                {
                    if ("LlW".IndexOf(At(x, y)) < 0) continue; // luggage, and the partition's walled tiles (full racks)
                    // Luggage, and racks filling the partition's tile, set back clear of its trims and the window jambs.
                    float inset = Part(x) ? 0.03f : 0f, setBack = Part(x) ? 0.16f : 0f;
                    bool sideA = y < car.H / 2;
                    Place(racks[(x * 7 + y) % 3], y - 2f + (sideA ? inset : -inset), z - setBack, sideA ? -90f : 90f); // open towards the aisle
                }
                int pair = seatColumns.IndexOf(x);
                if (pair < 0) continue;
                // Seat banks two rows wide, facing each other in pairs of columns (back at the FBX's local +Z).
                float rot = pair % 2 == 0 ? 180f : 0f;
                // A window seat on its own (its aisle place taken by a 1x1 prop): Seat_Single.
                if (At(x, 0) == 'S' && At(x, 1) == 'S') { seats.Add(Place("Seat_Double", -1.45f, z, rot).transform); Lamp(x, true); }
                else if (At(x, 0) == 'S' && kit.Get("Seat_Single")) { seats.Add(Place("Seat_Single", -1.95f, z, rot).transform); Lamp(x, true); }
                if (At(x, car.H - 2) == 'S' && At(x, car.H - 1) == 'S') { seats.Add(Place("Seat_Double", 1.45f, z, rot).transform); Lamp(x, false); }
                else if (At(x, car.H - 1) == 'S' && kit.Get("Seat_Single")) { seats.Add(Place("Seat_Single", 1.95f, z, rot).transform); Lamp(x, false); }
            }
            // The gramophone ('G', one per car), its back (local +Z) to the nearer side wall: against the wall, or beside
            // the window seat it shares a bank with. The live car's plays the music.
            for (int x = 0; x < car.W; x++)
                for (int y = 0; y < car.H; y++)
                    if (At(x, y) == 'G' && kit.Get("Prop_Gramophone"))
                    {
                        var g = Place("Prop_Gramophone", y - 2f, x + 1f, y < car.H / 2 ? -90f : 90f);
                        if (live) SetGramophone(g.transform);
                    }
            if (kind == CarKind.Dining || kind == CarKind.Baggage)
            {
                var bulbs = new List<Transform>();
                Greybox(car, map, parent, kit, bulbs);
                // Wall lamps, all lit, as evenly spaced as the plain wall allows: one about every wallLampEvery metres
                // along each side (the sides staggered by half of that), each on the free plain-wall tile nearest its
                // even spot (none if there's no plain wall within half a step). Table lamps: every other one along the
                // car is lit.
                foreach (bool sideA in new[] { true, false })
                {
                    int row = sideA ? 0 : car.H - 1;
                    var plain = Enumerable.Range(0, car.W).Where(px => car.BlankWall(new Cell(px, row)) || car.IsCage(new Cell(px, row))).ToList();
                    for (float t = sideA ? wallLampEvery / 2f : wallLampEvery; t < car.W; t += wallLampEvery)
                    {
                        var near = plain.Where(px => Mathf.Abs(px - t) <= wallLampEvery / 2f).OrderBy(px => Mathf.Abs(px - t)).ToList();
                        if (near.Count == 0) continue;
                        plain.Remove(near[0]);
                        Lamp(near[0], sideA, true, false);
                    }
                }
                var tables = bulbs.OrderBy(b => parent.InverseTransformPoint(b.position).x).ToList();
                for (int i = 0; i < tables.Count; i += 2) TableLight(tables[i]);
            }
            if (live && kind != CarKind.Dining && kind != CarKind.Baggage) ScatterProps(kit, seats);
            return true;

            // A wall lamp over a seat bank: a warm spotlight just above head height, set in from the wall and aimed
            // at the seat, plus the kit's lamp model if it has one (module name in lampModule).
            // A lit table lamp: a warm point light in its shade (dining car).
            void TableLight(Transform bulb)
            {
                if (!live) return;
                var light = new GameObject("Table Lamp").AddComponent<Light>();
                lamps.Add(light);
                light.transform.SetParent(bulb, false);
                tableLamps.Add(light);
                light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // straight down
                light.type = LightType.Spot;
                light.color = lampColor;
                light.intensity = tableLampPower;
                light.range = tableLampReach;
                light.spotAngle = tableLampCone;
                light.innerSpotAngle = tableLampInnerCone;
                light.shadows = LightShadows.None;
            }

            void Lamp(float x, bool sideA, bool lit = true, bool pilaster = true)
            {
                float wall = sideA ? 0.5f : -(car.H - 1) - 0.5f;     // inner face of that side's wall (world z)
                float inward = sideA ? -1f : 1f;
                var model = kit.Get(lampModule); // every light has its lamp
                // With the kit's Lamp_Wall: mounted on the pilaster face (4 cm proud of the wall), light inside its
                // shade (24 cm out, a little below the mount). Without it: a bare light lampInset from the wall.
                var mount = new Vector3(x, lampHeight, wall + inward * (pilaster ? 0.04f : 0f)); // on a pilaster, or flat wall
                var pos = model ? mount + new Vector3(0f, -0.05f, inward * 0.24f) : new Vector3(x, lampHeight, wall + inward * lampInset);
                if (!live || !lit) { PlaceLampModel(); return; } // the menu train lights itself by progress (MenuTrain)
                var light = new GameObject("Seat Lamp").AddComponent<Light>();
                lamps.Add(light);
                light.transform.SetParent(parent, false);
                light.transform.localPosition = pos;
                light.transform.localRotation = Quaternion.Euler(lampTilt, sideA ? 180f : 0f, 0f); // tipped down, facing into the car
                light.type = LightType.Spot;
                light.color = lampColor;
                light.intensity = wallLampIntensity;
                light.range = wallLampRange;
                light.spotAngle = wallLampAngle;
                light.innerSpotAngle = wallLampInnerAngle;
                light.shadows = lampShadows ? LightShadows.Soft : LightShadows.None;
                PlaceLampModel();

                void PlaceLampModel()
                {
                    if (!model) return;
                    var lamp = Instantiate(model, parent);
                    lamp.transform.localPosition = mount;
                    lamp.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0f, inward)); // facing into the car
                    foreach (var r in lamp.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off; // don't block its own light
                }
            }
        }

        // Decoration from the props kit (Train Assets/Props), the same for every run of the same carriage:
        // hat hooks on the end walls beside the doors, umbrellas in the end corners and against the side walls
        // beside the seats, briefcases standing by the seats, a steamer trunk in an end corner, ashtrays on seat
        // arms, newspapers on seats or arms. None blocks a tile; none goes on a tile someone starts on.
        void ScatterProps(TrainKit kit, List<Transform> seats)
        {
            var rng = new System.Random(Map.Aggregate(17, (h, row) => row.Aggregate(h, (k, ch) => unchecked(k * 31 + ch))));
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int More(int lo, int hiExclusive) => Mathf.RoundToInt(rng.Next(lo, hiExclusive) * propAmount); // a count, scaled
            GameObject Put(string name, Vector3 at, float yaw)
            {
                var model = kit.Get(name);
                if (!model) return null;
                var go = Instantiate(model, root);
                go.transform.position = at;
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                return go;
            }
            // Inner faces: side walls (row 0 on +z, the last row on -z) and end walls (from the end caps).
            float wallA = 0.5f, wallB = -(c.H - 1) - 0.5f, endL = -0.85f, endR = c.W - 0.15f;
            float aisleZ = -(c.H / 2);
            bool Free(Cell cell) => c.InBounds(cell) && !c.Blocked(cell) && cell != c.player && !c.enemies.Exists(e => e.pos == cell);

            // Hat hooks: a row of 3 or 4 on each end wall, on one side of the door, 36 cm apart (its back plate 12 cm
            // behind the pivot). At least one wears a fedora; the rest are empty about half the time (the same model
            // with the hat's materials hidden).
            void Hooks(float x, float yaw)
            {
                float side = rng.Next(2) == 0 ? -1f : 1f;
                int n = rng.Next(3, 5), hatted = rng.Next(n);
                for (int i = 0; i < n; i++)
                {
                    var hook = Put("Prop_HatHook_Fedora", new Vector3(x, 0f, aisleZ + side * (0.95f + i * 0.36f)), yaw);
                    if (hook && i != hatted && rng.NextDouble() < 0.5) HideParts(hook, "Trn_Felt", "Trn_BlackCloth");
                }
            }
            Hooks(endL + 0.12f, -90f);
            Hooks(endR - 0.12f, 90f);

            // End corners: a steamer trunk in one, an umbrella leaning into the end wall in another.
            var corners = new[] { (new Cell(0, 0), true), (new Cell(0, c.H - 1), true), (new Cell(c.W - 1, 0), false), (new Cell(c.W - 1, c.H - 1), false) }
                .Where(k => Free(k.Item1)).OrderBy(_ => rng.Next()).ToList();
            int trunks = More(1, 2), cornerBrollies = More(1, 2);
            for (int i = 0; i < corners.Count; i++)
            {
                var (cell, left) = corners[i];
                if (i % 2 == 0 && trunks-- > 0)
                    Put("Prop_SteamerTrunk", new Vector3(left ? endL + 0.27f : endR - 0.27f, 0f, cell.y == 0 ? wallA - 0.5f : wallB + 0.5f), left ? -90f : 90f); // long side along the end wall
                else if (cornerBrollies-- > 0)
                    Put("Prop_Umbrella", new Vector3(left ? endL + 0.31f : endR - 0.31f, 0f, cell.y == 0 ? wallA - 0.2f : wallB + 0.2f), left ? -90f : 90f); // its back (+Z) to the end wall
            }

            // Side walls: window-row floor beside a seat bank. Umbrellas lean back against the wall; briefcases stand
            // against it, long side along the wall.
            var walls = new List<(Vector3 at, float yaw)>();
            foreach (int row in new[] { 0, c.H - 1 })
                for (int x = 0; x < c.W; x++)
                {
                    if (!Free(new Cell(x, row))) continue;
                    var dirs = new[] { -1, 1 }.Where(d => c.IsSeat(new Cell(x + d, row))).ToList();
                    if (dirs.Count == 0) continue;
                    int d0 = dirs[rng.Next(dirs.Count)];
                    walls.Add((new Vector3(x + d0 * 0.4f, 0f, row == 0 ? wallA : wallB), row == 0 ? 0f : 180f));
                }
            walls = walls.OrderBy(_ => rng.Next()).ToList();
            int w = 0;
            for (int i = 0, n = More(2, 3); i < n && w < walls.Count; i++, w++)
            {
                var (at, yaw) = walls[w];
                Put("Prop_Umbrella", at + new Vector3(0f, 0f, yaw == 0f ? -0.31f : 0.31f), yaw);
            }

            // Seat arms (two per seat, top at 0.66 m) and cushions (top at 0.46 m), in the seat's own space. Doubles: arms
            // at local x +-0.96, two cushions at +-0.465. Singles (Seat_Single, 1 m): arms at +-0.46, one cushion at 0.
            bool Single(Transform t) => t.name.StartsWith("Seat_Single");
            var arms = seats.SelectMany(t => (Single(t) ? new[] { -0.46f, 0.46f } : new[] { -0.96f, 0.96f }).Select(x => (t, x))).OrderBy(_ => rng.Next()).ToList();
            var cushions = seats.SelectMany(t => (Single(t) ? new[] { 0f } : new[] { -0.465f, 0.465f }).Select(x => (t, x))).OrderBy(_ => rng.Next()).ToList();
            int a = 0, s = 0;
            // Briefcases lying flat on seat cushions at an easy angle: turned onto their back (the case's thickness
            // now vertical), lifted by half that thickness, centred on the cushion.
            for (int i = 0, n = More(2, 3); i < n && s < cushions.Count; i++)
            {
                var (t, x) = cushions[s++];
                var rot = Quaternion.Euler(0f, t.eulerAngles.y + Rand(-35f, 35f) + (rng.Next(2) == 0 ? 0f : 180f), 0f) * Quaternion.Euler(-90f, 0f, 0f);
                var centre = t.TransformPoint(new Vector3(x + Rand(-0.1f, 0.1f), 0.46f, -0.05f + Rand(-0.08f, 0.08f)));
                var lengthways = rot * Vector3.up; // the case's height, lying along the seat
                var brief = Put("Prop_Briefcase", centre - lengthways * 0.165f + Vector3.up * 0.07f, 0f);
                if (brief) brief.transform.rotation = rot;
            }
            // And one or two standing on the floor at the aisle end of a seat bank, against its arm, long side along it:
            // only beside the far banks (rows 0-1), since the near ones would hide them from the camera. Not singles:
            // their aisle end is the prop that took the aisle place.
            var far = seats.Where(t => t.position.z > aisleZ && !Single(t)).OrderBy(_ => rng.Next()).Take(More(1, 3));
            foreach (var t in far)
            {
                float side = t.TransformPoint(Vector3.right).z < t.TransformPoint(Vector3.left).z ? 1f : -1f; // the arm nearer the aisle
                Put("Prop_Briefcase", t.TransformPoint(new Vector3(side * 1.08f, 0f, Rand(-0.2f, 0.1f))), t.eulerAngles.y + 90f + Rand(-6f, 6f));
            }
            // Ashtrays near the front end of the arm (the seat back is at the bank's local +Z, the arm ends at -0.5),
            // lying along it; about 6 in 10 have a lit cigar, the rest are empty (the same model with the cigar hidden).
            for (int n = Mathf.Min(arms.Count - 2, More(6, 11)); a < n; a++) // leave a couple of arms for newspapers
            {
                var (t, x) = arms[a];
                var tray = Put("Prop_Ashtray_Cigar", t.TransformPoint(new Vector3(x, 0.66f, -0.36f)), t.eulerAngles.y + (rng.Next(2) == 0 ? 90f : -90f));
                if (!tray) continue;
                MatteBrass(tray);
                if (rng.NextDouble() < 0.6) Smolder(tray);
                else HideCigar(tray);
            }
            for (int n = More(3, 6), i = 0; i < n; i++)
            {
                if (rng.NextDouble() < 0.3 && a < arms.Count)
                {
                    var (t, x) = arms[a++];
                    Put("Prop_Newspaper", t.TransformPoint(new Vector3(x, 0.66f, -0.06f)), t.eulerAngles.y + 90f + Rand(-10f, 10f));
                }
                else if (s < cushions.Count)
                {
                    var (t, x) = cushions[s++];
                    Put("Prop_Newspaper", t.TransformPoint(new Vector3(x + Rand(-0.15f, 0.15f), 0.46f, -0.05f + Rand(-0.1f, 0.1f))), t.eulerAngles.y + Rand(-30f, 30f));
                }
            }
        }

        // A lit cigar: the ember glows (emissive, plus a faint flickering light) and a thin wisp of smoke rises from
        // the ash socket the props kit put on it (Sockets/Anchor_00).
        [Header("Cigars")]
        public Color emberColor = new Color(1f, 0.35f, 0.08f);
        public float emberEmission = 1.5f, emberLightIntensity = 0.1f, emberLightRange = 0.15f;
        public float emberLightHeight = 0.08f; // metres above the ember, so it warms the air over the dish rather than the brass
        public bool cigarSmoke = true;
        readonly List<(Light light, float bright)> embers = new List<(Light, float)>();
        static Texture2D puff;
        static Material puffMaterial;

        void Smolder(GameObject ashtray)
        {
            foreach (var r in ashtray.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (m.name.StartsWith("Trn_Ember"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", emberColor * emberEmission);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
            // The ember is found from the mesh itself (the centre of its Trn_Ember part): the FBX's socket empty
            // doesn't come through in the right place.
            var ember = EmberPoint(ashtray);
            if (ember == null) return;

            // Your tuned effects (Resources/Effects/CigarEmber and CigarSmoke, saved from play mode) sit where you put
            // them on the ashtray; without them, the built-in versions go on the ember.
            var emberFx = Resources.Load<GameObject>("Effects/CigarEmber");
            var light = emberFx ? Instantiate(emberFx, ashtray.transform).GetComponent<Light>() : new GameObject("Ember").AddComponent<Light>();
            if (!emberFx)
            {
                light.transform.SetParent(ashtray.transform, false);
                light.transform.position = ember.Value + Vector3.up * emberLightHeight;
                light.type = LightType.Point;
                light.color = emberColor;
                light.range = emberLightRange;
                light.intensity = emberLightIntensity;
                light.shadows = LightShadows.None;
            }
            if (light) embers.Add((light, light.intensity));

            if (!cigarSmoke) return;
            if (!puff)
            {
                // A soft round puff for the smoke particles.
                puff = new Texture2D(32, 32) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 15.5f;
                        float a = Mathf.Clamp01(1f - d);
                        puff.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                puff.Apply();
                puffMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = puff };
            }
            var smokeFx = Resources.Load<GameObject>("Effects/CigarSmoke");
            if (smokeFx)
            {
                var fx = Instantiate(smokeFx, ashtray.transform).GetComponent<ParticleSystemRenderer>();
                if (fx && !fx.sharedMaterial) fx.sharedMaterial = puffMaterial; // the material was made at runtime, so the prefab lost it
                return;
            }
            var smoke = new GameObject("Smoke").AddComponent<ParticleSystem>();
            smoke.transform.SetParent(ashtray.transform, false); // unscaled, yaw only: up stays up
            smoke.transform.position = ember.Value + Vector3.up * 0.01f;
            smoke.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // the cone emits along +Z: point it up
            var main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.75f, 0.74f, 0.72f, 0.3f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            var emission = smoke.emission;
            emission.rateOverTime = 6f;
            var shape = smoke.shape;
            shape.angle = 4f;
            shape.radius = 0.003f;
            var size = smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 5f)); // spreads as it rises, to ~10 cm
            var fade = smoke.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var noise = smoke.noise;
            noise.enabled = true;
            noise.strength = 0.04f; // a lazy curl
            noise.frequency = 0.6f;
            var r2 = smoke.GetComponent<ParticleSystemRenderer>();
            r2.sharedMaterial = puffMaterial;
            r2.shadowCastingMode = ShadowCastingMode.Off;
            r2.receiveShadows = false;
        }

        // The ashtrays' brass, duller than the train's: its own copy of Trn_Brass with smoothness and normal
        // strength at 0.5, so the seat caps and rails keep their shine.
        [Header("Props")]
        [Range(0.5f, 4f)] public float propAmount = 2f; // multiplies how many of each prop a carriage gets (spots permitting)

        [Header("Ashtrays")]
        public float ashtrayBrassSmoothness = 0.5f, ashtrayBrassNormal = 0.5f;
        Material matteBrass;

        void MatteBrass(GameObject ashtray)
        {
            foreach (var r in ashtray.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (!m || !m.name.StartsWith("Trn_Brass")) return m;
                    if (!matteBrass)
                    {
                        matteBrass = new Material(m) { name = "Trn_Brass (ashtray)" };
                        matteBrass.SetFloat("_Smoothness", ashtrayBrassSmoothness);
                        matteBrass.SetFloat("_BumpScale", ashtrayBrassNormal);
                    }
                    return matteBrass;
                }).ToArray();
        }

        // Reuse a prop without one of its parts: each part is its own materials, so those slots get an invisible
        // material (an empty ashtray: tobacco, band and ember; an empty hook: the fedora's felt and band).
        static Material invisible;

        static void HideCigar(GameObject ashtray) => HideParts(ashtray, "Trn_Tobacco", "Trn_Velvet", "Trn_Ember");

        static void HideParts(GameObject prop, params string[] materials)
        {
            if (!invisible) invisible = Transparent(Color.clear);
            foreach (var r in prop.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m && materials.Any(n => m.name.StartsWith(n)) ? invisible : m).ToArray();
        }

        // The world-space centre of a prop's Trn_Ember part (its submesh bounds), or null if it has none.
        static Vector3? EmberPoint(GameObject prop)
        {
            foreach (var mr in prop.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length && i < mf.sharedMesh.subMeshCount; i++)
                    if (mats[i] && mats[i].name.StartsWith("Trn_Ember"))
                        return mf.transform.TransformPoint(mf.sharedMesh.GetSubMesh(i).bounds.center);
            }
            return null;
        }

        // Ember lights flicker a little, each on its own noise.
        void FlickerEmbers()
        {
            for (int i = embers.Count - 1; i >= 0; i--)
            {
                var (light, bright) = embers[i];
                if (!light) { embers.RemoveAt(i); continue; }
                light.intensity = bright * (0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 3f, i * 7.3f));
            }
        }

        // Fallback when the kit isn't there: grey floor, brown seat blocks, dark luggage and walls.
        void BuildWhitebox()
        {
            Box(new Vector3((c.W - 1) / 2f, -0.05f, -(c.H - 1) / 2f), new Vector3(c.W, 0.1f, c.H), Floor);
            for (int x = 0; x < c.W; x++)
                for (int y = 0; y < c.H; y++)
                {
                    if (c.IsSeat(new Cell(x, y))) Box(new Vector3(x, 0.3f, -y), new Vector3(0.9f, 0.6f, 0.9f), new Color(0.35f, 0.2f, 0.12f), solid: true);
                    if (c.IsSolid(new Cell(x, y)) || c.IsCage(new Cell(x, y))) Box(new Vector3(x, 0.8f, -y), new Vector3(0.95f, 1.6f, 0.95f), new Color(0.2f, 0.2f, 0.25f), solid: true);
                }
            var wall = new Color(0.25f, 0.25f, 0.3f);
            Box(new Vector3((c.W - 1) / 2f, 0.5f, 0.6f), new Vector3(c.W, 1f, 0.15f), wall, solid: true);
            Box(new Vector3((c.W - 1) / 2f, 0.5f, -(c.H - 1) - 0.6f), new Vector3(c.W, 1f, 0.15f), wall, solid: true);
        }

        // A grid marker line (routes, facing arrows, blood outlines), in carriage space.
        LineRenderer Line(Color col, float width)
        {
            if (!lineMat) lineMat = new Material(Shader.Find("Sprites/Default"));
            var lr = new GameObject("Marker").AddComponent<LineRenderer>();
            lr.transform.SetParent(root, false);
            lr.useWorldSpace = false;
            lr.widthMultiplier = width;
            lr.sharedMaterial = lineMat;
            lr.startColor = lr.endColor = col;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            return lr;
        }

        // Tile overlays draw only an inset rim (Resources/Shaders/TileRim), so the carriage shows through.
        static Material OverlayMaterial()
        {
            var rim = Resources.Load<Shader>("Shaders/TileRim");
            return rim ? new Material(rim) { color = Color.clear } : Transparent(Color.clear);
        }

        // The kill preview's ghost: you, see-through.
        static Material GhostMaterial() => Outline(new Color(0.85f, 0.9f, 1f, 0.5f)); // the last-seen ghost: fainter than the preview's

        // A see-through outline of a figure (Resources/Shaders/GhostOutline), or plain see-through without it.
        static Material Outline(Color col)
        {
            var shader = Shader.Find("RailTracer/GhostOutline");
            if (!shader) return Transparent(new Color(col.r, col.g, col.b, col.a * 0.35f));
            var m = new Material(shader);
            m.SetColor("_Color", col);
            return m;
        }

        // Plain see-through colour. Sprites/Default is always in a build (URP's Unlit was stripped from one, so the
        // empty hat hooks fell back to opaque white fedoras).
        static Material Transparent(Color col) => new Material(Shader.Find("Sprites/Default")) { color = col };

        Renderer Box(Vector3 pos, Vector3 scale, Color col, PrimitiveType type = PrimitiveType.Cube, bool solid = false, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!solid) Destroy(go.GetComponent<Collider>()); // solid ones keep theirs for flying guts
            go.transform.SetParent(parent ? parent : root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.material.color = col;
            return r;
        }

        // The first thing you can see under the cursor, if it's a window sash. The cut-away camera-side wall is
        // drawn shadows-only but keeps its colliders, so those are looked through.
        SlidingWindow WindowUnderMouse(Vector2 screen)
        {
            var hits = Physics.RaycastAll(cam.ScreenPointToRay(screen), 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits.OrderBy(h => h.distance))
            {
                var r = hit.collider.GetComponent<Renderer>();
                if (r && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                return hit.collider.GetComponentInParent<SlidingWindow>();
            }
            return null;
        }

        void Update()
        {
            MusicStep();
            if (slowUntil >= 0f && Time.unscaledTime >= slowUntil) { slowUntil = -1f; if (camming) Time.timeScale = 1f; } // a hit's slow motion ends
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            // The menus own the camera and the input; the live car carries on behind them (paused: nobody acts).
            if (phase != Phase.Play)
            {
                if (phase == Phase.Menu) MenuStep(kb, mouse);
                kb = null;
                mouse = null;
            }
            if (phase == Phase.Play && ThanksStep(kb, mouse)) return; // the end-of-demo card
            // Debug: F9 forgets all tutorial progress and restarts the carriage, as on a new save.
            if (kb != null && kb.f9Key.wasPressedThisFrame) { Tutorial.ResetAll(); RestartCar(); }
            // The intro / end card swallows input: any key or click fades a dismissable one; R always restarts. The
            // win card waits for Space held, then you walk out. A tutorial card or a centre panel (pause, board,
            // demo complete) pauses the game: only their own buttons work.
            bool carded = kb != null && hud.CardUp;
            if (carded)
            {
                bool any = kb.anyKey.wasPressedThisFrame || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame));
                if (kb.rKey.wasPressedThisFrame) RestartCar();
                else if (kb.nKey.wasPressedThisFrame && c.Won && !busy) { hud.HideCard(); StartCoroutine(ExitCar()); } // debug: straight out
                else if (endShown && c.Won && !busy)
                {
                    if (!kb.spaceKey.isPressed && !(mouse != null && mouse.leftButton.isPressed)) winHeld = 0f; // Space, or the mouse held down
                    else if ((winHeld += Time.unscaledDeltaTime) >= endTurnHold) { winHeld = -1f; hud.HideCard(); StartCoroutine(ExitCar()); }
                    hud.SetCardHold(winHeld >= 0f ? winHeld / endTurnHold : 1f);
                }
                else if (endShown && c.Lost && mouse != null && mouse.leftButton.wasPressedThisFrame) RestartCar(); // the loss card: a click retries
                else if (any && hud.CardDismissable) hud.HideCard();
                kb = null;
                mouse = null;
            }
            else if (phase == Phase.Play && (TutorialStep(kb, mouse) || ChoiceStep(kb, mouse) || LogStep(kb, mouse)))
            {
                kb = null;
                mouse = null;
            }
            if (kb == null) spaceHeld = -1f;
            if (kb != null)
            {
                if (kb.rKey.wasPressedThisFrame) RestartCar();
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key };
                for (int i = 0; i < digits.Length; i++)
                    if (digits[i].wasPressedThisFrame) Select((SkillId)i);
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (hud.ControlsOpen) hud.CloseControls();
                    else if (selected != null) selected = null;
                    else if (!busy) OpenChoice("pause"); // nothing to cancel: pause
                }
                if (kb.hKey.wasPressedThisFrame) hud.ToggleControls();
                if (kb.lKey.wasPressedThisFrame) { hud.ShowLog(c.log); Time.timeScale = 0f; }
                if (kb.gKey.wasPressedThisFrame && !busy && !walking) c.Do(c.held != null ? "drop" : "grab");
                showAllSenses = kb.vKey.isPressed;
                if (kb.zKey.wasPressedThisFrame) Undo();
                if (kb.cKey.wasPressedThisFrame && !busy) c.Do("crouch");
                if (kb.bKey.wasPressedThisFrame) bloodOutlines = !bloodOutlines;
                if (kb.oKey.wasPressedThisFrame && mouse != null) WindowUnderMouse(mouse.position.ReadValue())?.Toggle();
                // End turn: hold Space (a tap does nothing, so it can't end a turn by accident).
                if (kb.spaceKey.wasPressedThisFrame && !busy && !c.Over && !walking) { spaceHeld = 0f; endByMouse = false; }
                if (spaceHeld >= 0f)
                {
                    bool holding = kb.spaceKey.isPressed || (endByMouse && mouse != null && mouse.leftButton.isPressed);
                    if (!holding || busy || c.Over) spaceHeld = -1f;
                    else if ((spaceHeld += Time.deltaTime) >= endTurnHold) { spaceHeld = -1f; StartCoroutine(EnemyPhase()); }
                }
            }

            // With input held back this only follows the camera target (tutorial pans). Never during a kill cam: it owns
            // the camera (and this would drag it back to your play distance along the shot's direction).
            if (phase == Phase.Play && !camming) MoveCamera(kb, mouse);
            WalkStep();
            if (selected.HasValue && !c.Has(selected.Value)) selected = null; // lost the legend for it
            leaping = !walking && (selected == SkillId.Leap || (kb != null && kb.shiftKey.isPressed));
            hopping = !walking && (selected == SkillId.Hop || (kb != null && kb.ctrlKey.isPressed));
            throwing = selected == SkillId.Throw;
            var screen = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool overHud = mouse != null && hud.Blocks(screen);
            hudSlot = mouse != null ? hud.SlotAt(screen) : -1;
            hover = mouse != null && !overHud ? CellUnderMouse(screen) : null;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && hudSlot >= 0) Select((SkillId)hudSlot);
            // The HUD's key hints as buttons, for playing with the mouse alone.
            switch (hud.RefreshButtons(mouse != null ? screen : new Vector2(-9999f, -9999f), mouse != null && mouse.leftButton.isPressed, mouse != null && mouse.leftButton.wasPressedThisFrame))
            {
                case "end": if (!busy && !c.Over && !walking) { spaceHeld = 0f; endByMouse = true; } break; // hold the button, like Space
                case "crouch": if (!busy) c.Do("crouch"); break;
                case "undo": Undo(); break;
                case "cancel": selected = null; break;
                case "pause": if (!busy) OpenChoice("pause"); break;
                case "controls": hud.ToggleControls(); break;
                case "closecontrols": hud.CloseControls(); break;
                case "log": hud.ShowLog(c.log); Time.timeScale = 0f; break;
            }
            if (!busy && !c.Over && mouse != null && mouse.leftButton.wasPressedThisFrame && hover.HasValue)
            {
                var target = c.EnemyAt(hover.Value);
                var kill = KillMode;
                bool paired = false, used = false;
                if (throwing)
                {
                    var thrown = c.held;
                    used = thrown != null && c.Do($"throw {hover.Value.x} {hover.Value.y}");
                    if (used && RollKillCam() && anims.TryGetValue(thrown.who, out var ta))
                    {
                        var flat = cam.transform.forward;
                        flat.y = 0f;
                        StartCoroutine(SceneKillCam(() => ta.transform.localPosition, World(hover.Value), flat.normalized, false)); // slows as it lands
                    }
                }
                else if (target != null)
                {
                    if (kill.HasValue && c.Do($"act {kill.Value} {target.id}"))
                    {
                        used = true;
                        pendingAct = kill;
                        paired = kill == Act.BrutalKill || kill == Act.QuietKill;
                        if (kill == Act.RailGrab && RollKillCam())
                        {
                            var hungAt = c.bodies[c.bodies.Count - 1].pos;
                            var w = World(hungAt);
                            var outward = hungAt.y == 0 ? Vector3.forward : Vector3.back;
                            StartCoroutine(SceneKillCam(() => w + outward * 0.3f, w, outward, true)); // the impact: as the shot starts
                        }
                    }
                }
                else if (leaping) used = c.Do($"leap {hover.Value.x} {hover.Value.y}");
                else if (hopping)
                {
                    int hp0 = c.hp;
                    used = c.Do($"hop {hover.Value.x} {hover.Value.y}");
                    if (used && c.shotsFired.Count > 0) { hopShots.AddRange(c.shotsFired); c.shotsFired.Clear(); hud.shownHp = hp0; } // fired as you climb back in
                }
                else if (c.Do($"move {hover.Value.x} {hover.Value.y}")) Follow(playerRoute, c.walked);
                if (used) selected = null;
                if (paired) StartCoroutine(PairedKill(target, kill.Value)); // the turn only ends when you end it
            }
            LegendLighting();
            foreach (var o in c.shotsFired) ShotEffect(o);
            c.shotsFired.Clear();
            DetectBlood();
            FlickerEmbers();
            Debugging();
            Sync();
            Tags();
            Tickets();
            hud.SetEndTurn(spaceHeld >= 0f ? spaceHeld / endTurnHold : 0f, !busy && !c.Over && !walking);
            var mode = Mode();
            hud.Refresh(c, selected ?? (leaping ? SkillId.Leap : hopping ? SkillId.Hop : (SkillId?)null), hudSlot, mode?.name, mode?.hint,
                c.log.Skip(Mathf.Max(0, c.log.Count - 8)).ToList());
            // The level is won the moment nobody is left fighting; anyone broken and fleeing still runs out first.
            if (c.Won && !busy && !endShown && c.enemies.Exists(e => e.state == EnemyState.Broken && e.fleeing)) StartCoroutine(FleersOut());
            if (c.Over && !busy && !endShown && !tut.Active && phase == Phase.Play)
            {
                endShown = true;
                winHeld = 0f;
                if (c.Won)
                {
                    RecordClear();
                    hud.ShowCard($"{CarLabel()} cleared", "The assassin washes up", "and steps out as the conductor.", "Hold Space / click to walk on  /  R to ride again", 0f, false, TicketStates());
                    hud.SetCardHold(0f);
                }
                else hud.ShowCard(CarLabel(), "The demon was only", "human after all.", "R or click to retry", 0f, false);
            }
        }

        // ---- Tickets ----

        // Killed (red) or broken (white; fleeing counts as broken), taken ones first in the order taken, then the live.
        int[] TicketStates()
        {
            foreach (var e in c.enemies)
                if (e.state != EnemyState.Active && !taken.Contains(e)) taken.Add(e);
            return taken.Select(e => e.state == EnemyState.Dead ? 1 : 2)
                .Concat(c.enemies.Where(e => e.state == EnemyState.Active).Select(_ => 0)).ToArray();
        }

        void Tickets()
        {
            hud.SetTickets(TicketStates());
            hud.SetTurn(c.freeWalk ? 0 : c.Round, car.targetTurns);
        }

        // ---- Tutorial ----

        // Shows the current card: pans the camera first if it names a spot, then dims all but its targets.
        // Enter or NEXT moves on; Skip tutorial ends them all. True while a card is up or about to be.
        bool TutorialStep(Keyboard kb, Mouse mouse)
        {
            if (!walkQueued && !hud.CardVisible)
            {
                walkQueued = true;
                if (tut.WantsWalkthrough && !walking) tut.QueueWalkthrough(WalkTargets, WalkFocus);
            }
            if (!busy && !tut.Active) Poll();
            var card = tut.Step();
            if (card == null) return false;
            if (!card.shown)
            {
                if (card.panUntil < 0f)
                {
                    var spot = card.focus?.Invoke();
                    if (spot.HasValue) camTarget = spot;
                    card.panUntil = Time.unscaledTime + (spot.HasValue ? 0.4f : 0f);
                }
                if (Time.unscaledTime < card.panUntil) return true;
                card.shown = true;
                hud.ShowTutorial(card.title, card.number, card.text, card.tip);
            }
            hud.SetTutorialTargets(card.targets?.Invoke() ?? new Rect[0]);
            var at = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool closed = false;
            if (click && hud.TutorialSkipHit(at)) { tut.Skip(); closed = true; }
            else if ((kb != null && kb.spaceKey.wasPressedThisFrame) || (click && hud.TutorialNextHit(at)))
            {
                tut.Next();
                closed = true;
            }
            if (closed)
            {
                hud.HideTutorial();
                if (!tut.Active) camTarget = playerShown; // the last card: back to you
            }
            return true;
        }

        System.Func<IList<Rect>> WalkTargets(int i)
        {
            switch (i)
            {
                case 1: return () => new[] { hud.HealthRect };
                case 2: return () => new[] { hud.MovesRect };
                case 3: return () => new[] { hud.SkillRowRect };
                case 4: return () => new[] { hud.LegendRect };
                case 5: return () => Chip(NearestGuard());
                case 6: return () => new[] { hud.TicketsRect };
                default: return null;
            }
        }

        System.Func<Vector3?> WalkFocus(int i) => i == 5 ? () => NearestGuard() is Enemy e ? shown[e] : (Vector3?)null : (System.Func<Vector3?>)null;

        // The nearest stationary guard still in play (else the nearest anyone).
        Enemy NearestGuard()
        {
            var live = c.enemies.Where(e => e.InPlay).OrderBy(e => (e.pos.x - c.player.x) * (e.pos.x - c.player.x) + (e.pos.y - c.player.y) * (e.pos.y - c.player.y));
            return live.FirstOrDefault(e => e.route == null) ?? live.FirstOrDefault();
        }

        IList<Rect> Chip(Enemy e) => e != null && chips.TryGetValue(e, out var rt) && rt && rt.gameObject.activeInHierarchy ? new[] { SkillHud.ScreenRectOf(rt) } : new Rect[0];

        // A floor tile's outline on screen.
        Rect TileRect(Cell cell)
        {
            var w = World(cell);
            var pts = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) }
                .Select(d => cam.WorldToScreenPoint(w + d)).ToArray();
            return Rect.MinMaxRect(pts.Min(p => p.x), pts.Min(p => p.y), pts.Max(p => p.x), pts.Max(p => p.y));
        }

        // First-time cards: queued the first time each event happens, in the order seen. Called once the player's
        // move has resolved, and in the enemy turn after each enemy acts.
        void Poll()
        {
            if (tut.Skipped || c == null) return;
            foreach (var e in c.enemies)
            {
                if (!e.InPlay) continue;
                var who = e;
                System.Func<Vector3?> at = () => shown[who];
                if (e.awareness == Awareness.Searching) tut.Once("searching", () => Chip(who), at);
                if (e.awareness == Awareness.Startled) tut.Once("startled", () => Chip(who), at);
                if (e.awareness == Awareness.Combat) tut.Once("combat", () => Chip(who), at);
                if (e.hasSeen) tut.Once("seen", () => Chip(who).Append(hud.SightRect).ToList(), at);
            }
            if (reactTile is Cell red) tut.Once("reaction", () => new[] { TileRect(red) }, () => World(red));
            if (c.bodies.Count > 0)
            {
                var b = c.bodies[0];
                tut.Once("bodies", () => new[] { TileRect(b.pos), hud.BoxRect(SkillId.Drag) }, () => World(b.pos));
            }
            if (c.enemies.FirstOrDefault(x => x.state == EnemyState.Broken) is Enemy broken)
                tut.Once("broken", () => Chip(broken), () => shown[broken]);
            if (TicketStates().Any(t => t != 0)) tut.Once("ticket", () => new[] { hud.TicketRect(0) }, null);
            // Only the very first unlock and the very first Bloodied get a card.
            var unlocked = Enumerable.Range(0, c.t.skills.Length).Select(i => (SkillId)i)
                .Where(id => c.Def(id).unlock > 0 && c.Has(id)).OrderBy(id => c.Def(id).unlock).ToList();
            if (unlocked.Count > 0) { var id = unlocked[0]; tut.Once("unlock", () => new[] { hud.BoxRect(id) }, null, c.Def(id).name); }
            var bloodied = Enumerable.Range(0, c.t.skills.Length).Select(i => (SkillId)i)
                .Where(id => c.Def(id).bloodied > c.Def(id).unlock && c.Bloodied(id)).OrderBy(id => c.Def(id).bloodied).ToList();
            if (bloodied.Count > 0)
            {
                var id = bloodied[0];
                var up = c.Def(id).upgrade;
                tut.Once("bloodied", () => new[] { hud.BoxRect(id) }, null, c.Def(id).name, up.Length > 0 ? char.ToLower(up[0]) + up.Substring(1) : up);
            }
            if (Mode() != null) tut.Once("targeting", () => new[] { hud.BannerRect }, null);
        }

        // The full log (L): the game is paused while it's open; the wheel or arrow keys scroll it, L or Esc closes it.
        bool LogStep(Keyboard kb, Mouse mouse)
        {
            if (!hud.LogOpen) return false;
            bool clicked = mouse != null && hud.RefreshButtons(mouse.position.ReadValue(), mouse.leftButton.isPressed, mouse.leftButton.wasPressedThisFrame) == "closelog";
            if (clicked || (kb != null && (kb.lKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)))
            {
                hud.HideLog();
                Time.timeScale = 1f;
                return true;
            }
            float scroll = mouse != null ? mouse.scroll.ReadValue().y * 0.25f : 0f;
            if (kb != null) scroll += (kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : kb.downArrowKey.isPressed || kb.sKey.isPressed ? -1f : 0f) * 600f * Time.unscaledDeltaTime
                + (kb.pageUpKey.wasPressedThisFrame ? 300f : kb.pageDownKey.wasPressedThisFrame ? -300f : 0f);
            hud.ScrollLog(scroll);
            return true;
        }

        // The banner while a skill waits for its target (selected, or Shift / Ctrl held). Null when nothing is.
        (string name, string hint)? Mode()
        {
            if (busy || c.Over) return null;
            if (throwing) return ("Demon throw", "Pick a landing tile");
            if (leaping) return ("Leap", "Pick a landing tile");
            if (hopping) return ("Window hop", "Pick a window tile");
            switch (KillMode)
            {
                case Act.QuietKill: return ("Stealth kill", "Pick an enemy next to you" + (c.Bloodied(SkillId.Stealth) ? ", or 2 tiles away to snatch" : ""));
                case Act.RailGrab: return ("Rail grab", "Pick an enemy at a window");
                case Act.BrutalKill: return ("Brutal kill", "Pick an enemy next to you");
                default: return null;
            }
        }

        // Enemy chips over their heads (awareness icon, terror segments, has-seen eye), scaled with the zoom within
        // 0.85x..1.4x. Hovering an enemy adds its label: role, awareness and stage, intent.
        void Tags()
        {
            float k = Mathf.Clamp(12f / camDistance, 0.85f, 1.4f);
            hud.BeginTags();
            hud.legendGain = 0;
            chips.Clear();
            foreach (var e in c.enemies)
            {
                if (e.state != EnemyState.Active && e.state != EnemyState.Broken) continue;
                var sp = cam.WorldToScreenPoint(shown[e] + Vector3.up * (FigureHeight + 0.25f));
                if (sp.z < 0) continue; // behind the camera
                bool broken = e.state == EnemyState.Broken;
                string role = (e.tough ? "Tough " : "") + (e.route != null ? "Patrol" : "Guard");
                bool threat = threats.TryGetValue(e, out int pct); // will fire on what you're aiming
                // What they'll do if you end your turn where you are (or where the aimed move ends).
                var at = previewLanding ?? c.player;
                // Red only while they can see you where you stand now, or where the aimed move ends (anyone not
                // startled who sees you fights), or a berserk heavy. A fighter you've slipped out of sight of shows
                // white "?" at once, as it will drop to searching at its turn.
                bool red = !broken && (e.berserk || (e.InPlay && e.awareness != Awareness.Startled && c.CanSee(e, at)));
                int shownAwareness = broken ? 4 : red ? (int)Awareness.Combat
                    : e.awareness == Awareness.Combat ? (int)Awareness.Searching : (int)e.awareness;
                string icon = broken || e.awareness == Awareness.Startled ? null
                    : c.PanicTurn(e) ? (e.panic == Panic.WildFire ? "wildfire" : "freeze") // a freeze shows the ghost
                    : !red ? null // a white chip keeps its "?": icons are for red chips
                    : c.Swings(e, at) ? "charge" // a heavy that will reach you and swing (3 steps to arrive, 1 to swing)
                    : c.WillShoot(e, at) ? "crosshair" : null;
                int plate = e.tough && !broken ? (c.StageOf(e) >= Stage.Shaken ? 2 : 1) : 0; // cracked once they can be killed
                bool notYet = KillMode.HasValue && plate == 1;
                // Targeting a kill on them: the legend it gives, on the legend bar (its "+30 legend" is in the skill's
                // tooltip; an unshaken tough shows "Not yet" instead).
                if (KillMode.HasValue && hover == e.pos && !busy && !notYet && c.KillOutcome(KillMode.Value, e).HasValue)
                    hud.legendGain = c.LegendGain(KillMode.Value);
                // What they'll do next can hurt you: a shot, a charge or swing, wild fire (the label's line in red).
                var next = c.Decide(e);
                bool danger = next == Intent.Shoot || next == Intent.Charge || next == Intent.Melee || next == Intent.WildFire;
                string shot = threat && swingThreats.Contains(e) ? $"Swing · Hit {pct}%" // a heavy's melee (never halved), not a shot
                    : threat ? $"Hit {pct}%" + (leaping && !(previewLanding.HasValue && c.Swings(e, previewLanding.Value)) ? $" · leap -{Mathf.RoundToInt(100 * c.t.leapHitPenalty)}" : "") : c.IntentText(e);
                chips[e] = hud.AddTag(sp, k, shownAwareness, e.terror, e.hasSeen, hover == e.pos || threat, c.Name(e), role, c.StatusText(e),
                    shot, threat, icon, plate, notYet, danger, e.id);
            }
            hud.EndTags();
        }

        Act? KillMode => selected == SkillId.Stealth ? Act.QuietKill : selected == SkillId.Rail ? Act.RailGrab
            : selected == SkillId.Brutal ? Act.BrutalKill : (Act?)null;

        // Hotkey (or click on its box): grab/drop at once; leap, hop, a kill or the throw is selected until used
        // (again to deselect). Greyed-out skills (locked, or unusable right now) and Dread (passive) do nothing.
        void Select(SkillId s)
        {
            if (c == null || s == SkillId.Dread || walking) return; // the walk-through car: just walking
            if (selected == s) { selected = null; return; }
            if (busy || !c.Usable(s)) return; // greyed out: nothing to do with it right now
            if (s == SkillId.Drag) { c.Do(c.held != null ? "drop" : "grab"); return; }
            selected = s;
        }

        // Inspector debug: shows the live legend and cooldowns; editing one in Play mode sets it.
        void OnValidate() => debugDirty = true;

        void Debugging()
        {
            if (debugDirty)
            {
                debugDirty = false;
                // Only what was actually moved (a change reported for some other field leaves these alone).
                if (debugLegend != c.legend) c.legend = Mathf.Clamp(debugLegend, 0, tuning.maxLegend);
                if (debugLeapWait != c.leapWait) c.leapWait = Mathf.Clamp(debugLeapWait, 0, c.Def(SkillId.Leap).cooldown);
                if (debugRailWait != c.railWait) c.railWait = Mathf.Clamp(debugRailWait, 0, c.Def(SkillId.Rail).cooldown);
                if (debugThrowWait != c.throwWait) c.throwWait = Mathf.Clamp(debugThrowWait, 0, c.Def(SkillId.Throw).cooldown);
            }
            debugLegend = c.legend;
            debugLeapWait = c.leapWait;
            debugRailWait = c.railWait;
            debugThrowWait = c.throwWait;
        }

        // WASD / arrows pan, right-drag pans, scroll zooms.
        void MoveCamera(Keyboard kb, Mouse mouse)
        {
            var pan = Vector2.zero;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) pan.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) pan.x += 1;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pan.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pan.y -= 1;
            }
            if (pan != Vector2.zero || (mouse != null && mouse.rightButton.isPressed)) camTarget = null; // you took the camera
            pan *= panSpeed * camDistance * Time.deltaTime;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed) pan -= mouse.delta.ReadValue() * camDistance * 0.002f;
                camDistance = Mathf.Clamp(camDistance - mouse.scroll.ReadValue().y * zoomSpeed * camDistance, 4f, 30f);
            }
            if (camTarget.HasValue)
            {
                focus = Vector3.Lerp(focus, camTarget.Value, 1f - Mathf.Exp(-6f * Time.deltaTime));
                if (!busy && (focus - camTarget.Value).sqrMagnitude < 0.01f) camTarget = null; // back on you: hand it over
            }
            focus.x = Mathf.Clamp(focus.x + pan.x, 0, Map[0].Length - 1);
            focus.z = Mathf.Clamp(focus.z + pan.y, -(Map.Length - 1), 0);
            cam.transform.position = focus - cam.transform.forward * camDistance;
        }

        // The rules have already resolved the kill; this only plays it.

        // Brutal kill ("Takedown") or quiet kill ("StealthKill"): the rules have already resolved it; this plays the
        // pair, bleeds on cue, then hands over to the enemy turn if the turn is over (a brutal kill always ends it).
        IEnumerator PairedKill(Enemy victim, Act act)
        {
            busy = true;
            bool brutal = act == Act.BrutalKill;
            // Half cover straight out from the victim's tile (the one furthest from you): finishers that use it come first.
            var away = (shown.TryGetValue(victim, out var vs) ? vs : World(victim.pos)) - playerShown;
            Vector3? coverDir = null;
            Cell coverCell = default;
            foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
            {
                var side = new Vector3(d.x, 0f, -d.y);
                var seatCell = new Cell(victim.pos.x + d.x, victim.pos.y + d.y);
                if (c.IsSeat(seatCell) && (coverDir == null || Vector3.Dot(side, away) > Vector3.Dot(coverDir.Value, away)))
                {
                    coverDir = side;
                    coverCell = seatCell;
                }
            }
            // A finisher from the library (guard or heavy set, your stance), played on the "Finisher" states; else the old pair.
            // In front of the victim or behind them (by the way they face); square to it, from the side: both pools.
            var victimFacing = new Vector3(victim.facing.x, 0f, -victim.facing.y);
            float ahead = Vector3.Dot(victimFacing, -new Vector3(away.x, 0f, away.z).normalized);
            TakedownFacing? approach = ahead > 0.2f ? TakedownFacing.Front : ahead < -0.2f ? TakedownFacing.Back : (TakedownFacing?)null;
            var entry = takedowns ? takedowns.Pick(brutal ? KillKind.Brutal : KillKind.Stealth, victim.tough, c.crouched, coverDir.HasValue, approach) : null;
            string state = entry != null ? "Finisher" : brutal ? "Takedown" : "StealthKill";
            int hash = Animator.StringToHash(state);
            if (playerAnim && anims.TryGetValue(victim, out var va) && playerAnim.HasState(0, hash) && va.HasState(0, hash))
            {
                takedownVictim = victim;
                knifeReversed = entry != null && entry.reverseGrip;
                Vector3 from = playerShown, at = shown[victim];
                var dir = at - from;
                dir.y = 0;
                playerFacing = dir.normalized;
                // A cover finisher (shoved over a seat): the pair turns to face the seat and moves towards it.
                bool useCover = entry != null && entry.needsCover && coverDir.HasValue;
                if (useCover && c.gramophone == coverCell) c.Do("needle"); // shoved onto the gramophone: the music stops
                if (useCover)
                {
                    playerFacing = coverDir.Value;
                    at += coverDir.Value * entry.coverShift * characterScale;
                    if (Map[coverCell.y][coverCell.x] == 'B') at += Vector3.up * barShoveLift; // the bar stands taller than a table
                }
                var look = Quaternion.LookRotation(playerFacing);
                // Where the victim stands from you, and which way they face.
                var gap = entry != null ? look * (entry.victimOffset * characterScale) : playerFacing * 0.45f;
                var victimRot = entry != null ? look * Quaternion.Euler(0f, entry.victimYaw, 0f)
                    : Quaternion.LookRotation(!brutal && quietVictimFacesAway ? playerFacing : -playerFacing);
                // Every stealth kill: the rules already put the victim on your tile. Yank them across, then kill them there.
                if (!brutal)
                {
                    var land = from + gap;
                    va.transform.localRotation = victimRot;
                    for (float time = 0f; time < snatchTime; time += Time.deltaTime)
                    {
                        float k = time / snatchTime;
                        va.transform.localPosition = Vector3.Lerp(at, land, k * k);
                        yield return null;
                    }
                    at = land;
                }
                // The transforms stay put while a clip plays (a finisher's own travel is in its pose). A brutal kill: you
                // step in to the victim (the finisher's own spacing), then onto their tile once it's done (the rules
                // moved you there). A stealth kill: you never move (the old clip closes in partway and back).
                var to = brutal ? (entry != null ? at - gap : at) : entry != null ? from : Vector3.Lerp(from, at, quietKillReach);
                var end = brutal ? World(c.player) : from;
                var move = brutal ? takedownMove : quietKillMove;
                float bloodAt = entry != null ? entry.bloodAt : brutal ? takedownBloodAt : quietKillBloodAt;
                va.transform.localPosition = at;
                va.transform.localRotation = victimRot;
                va.transform.localScale = Vector3.one * characterScale;
                playerAnim.speed = va.speed = entry != null ? entry.speed : 1f;
                // A combo: the second clip on the "Finisher2" states, cross-faded in (comboFrom / comboStart / comboBlend).
                int combo = Animator.StringToHash("Finisher2");
                bool chained = entry != null && entry.attacker2 && entry.victim2 && playerAnim.HasState(0, combo) && va.HasState(0, combo);
                if (entry != null)
                {
                    Override(playerAnim, "Finisher Attacker", entry.attacker);
                    Override(va, "Finisher Victim", entry.victim);
                }
                if (chained)
                {
                    Override(playerAnim, "Finisher Attacker 2", entry.attacker2);
                    Override(va, "Finisher Victim 2", entry.victim2);
                }
                // A finisher carries its own moves: step in to its start first, then both stand still while it plays.
                if (entry != null && (to - playerShown).sqrMagnitude > 1e-4f)
                {
                    var start = playerShown;
                    for (float time = 0f; time < 0.2f; time += Time.deltaTime)
                    {
                        playerShown = Vector3.Lerp(start, to, Mathf.SmoothStep(0f, 1f, time / 0.2f));
                        yield return null;
                    }
                    playerShown = to;
                }
                bool cam = RollKillCam(); // every takedown, stealth too (on, and the chance comes up)
                if (cam)
                {
                    // Fly in and hold a beat before the strike; the shot then stays put (no chasing the pair).
                    BeginKillCam();
                    playerShown = to;
                    yield return FlyIn(KillCamPose(to, at, entry != null ? entry.camOffset : Vector3.zero, entry != null ? entry.camLook : Vector3.zero));
                }
                playerAnim.Play(hash, 0, 0f);
                va.Play(hash, 0, 0f);
                yield return null; // let the state start so its length is known
                // Hold the pair in place (a clip's travel is in its pose): the middle of the two hips stays where it
                // started; both of you are moved back by however far it has drifted, so the spacing is kept.
                var victimHips = va.GetBoneTransform(HumanBodyBones.Hips);
                var playerHipsAtStart = playerAnim.GetBoneTransform(HumanBodyBones.Hips);
                bool hold = entry != null && entry.HoldsPlace && victimHips && playerHipsAtStart;
                Vector3 PairMiddle() => (root.InverseTransformPoint(playerHipsAtStart.position) + root.InverseTransformPoint(victimHips.position)) / 2f;
                var heldAt = hold ? PairMiddle() : Vector3.zero;
                float length = playerAnim.GetCurrentAnimatorStateInfo(0).length;
                float length2 = 0f;
                bool second = false, bled = false;
                var struck = new HashSet<int>(); // extra blood points already fired
                // Handing back to idle: once the last clip starts blending out, your hips stay where the finisher left
                // them (the transform follows the pose), then you slide onto the tile the rules gave you.
                var playerHips = playerAnim.GetBoneTransform(HumanBodyBones.Hips);
                Vector3? anchor = null;
                for (float time = 0f; ; time += Time.deltaTime)
                {
                    if (!second)
                    {
                        if (entry == null) playerShown = Vector3.Lerp(from, to, move.Evaluate(Mathf.Min(1f, time / length))); // the old pair glides in
                        if (chained && time / length >= entry.comboFrom)
                        {
                            playerAnim.CrossFadeInFixedTime(combo, entry.comboBlend, 0, entry.comboStart * entry.attacker2.length);
                            va.CrossFadeInFixedTime(combo, entry.comboBlend, 0, entry.comboStart * entry.victim2.length);
                            length2 = entry.attacker2.length * (1f - entry.comboStart) / Mathf.Max(0.01f, playerAnim.speed);
                            second = true;
                            time = 0f;
                        }
                        else if (time >= length) break;
                    }
                    else if (time >= length2) break;
                    float t = second ? time / length2 : time / length;
                    if ((!chained || second) && !bled && t >= bloodAt)
                    {
                        KillBlood(victim, act, playerFacing, Chest(va), entry != null && entry.gore);
                        Soak(va, brutal);
                        if (entry != null && entry.headOff) HeadOff(va);
                        HitSlowMo();
                        bled = true;
                    }
                    if (entry != null && blood)
                    {
                        // More strikes: a splash at each of the entry's extra points over the whole takedown.
                        float whole = entry.Overall(second, t);
                        for (int s = 0; s < entry.extraBlood.Count; s++)
                            if (!struck.Contains(s) && whole >= entry.extraBlood[s])
                            {
                                struck.Add(s);
                                var chestNow = Chest(va) ?? va.transform.localPosition + Vector3.up * FigureHeight * 0.6f;
                                blood.Splash(root, chestNow, playerFacing, brutal || entry.gore);
                                HitSlowMo();
                            }
                    }
                    if (hold && !anchor.HasValue)
                    {
                        var drift = PairMiddle() - heldAt;
                        drift.y = 0f;
                        playerShown -= drift;
                        va.transform.localPosition -= drift;
                    }
                    if (entry != null && playerHips && (anchor.HasValue || ((!chained || second) && t > 0.3f && playerAnim.IsInTransition(0))))
                    {
                        var h = root.InverseTransformPoint(playerHips.position) - playerShown;
                        h.y = 0f;
                        anchor ??= playerShown + h;
                        playerShown = anchor.Value - h;
                    }
                    yield return null;
                }
                if (!bled)
                {
                    KillBlood(victim, act, playerFacing, Chest(va), entry != null && entry.gore);
                    Soak(va, brutal);
                    if (entry != null && entry.headOff) HeadOff(va);
                }
                if (!anchor.HasValue) playerShown = to;
                var slideFrom = playerShown;
                // Every takedown ends with the victim on the floor: limp after the last clip (a ragdoll), falling as
                // the camera flies out. Without the ragdoll bones: the old topple for a finisher that leaves them up.
                var bodyTile = c.bodies.Find(b => b.who == victim)?.pos ?? victim.pos;
                // A cover finisher (Shove to Table) leaves the pair where the clip ends: the victim bent over the seat,
                // you beside it in your stance (the rules restore it). For the rules the body is on its tile as ever.
                bool stayPut = useCover;
                if (stayPut) { end = slideFrom; parkedOn = c.player; posedBodies.Add(victim); }
                bool limp = !stayPut && HasRagdoll(va);
                if (limp) StartCoroutine(Ragdoll(victim, va, bodyTile, takedownHold)); // slides with you, then falls
                else if (!stayPut && entry != null && entry.victimFallsAfter) StartCoroutine(Topple(va.transform, playerFacing)); // still on their feet
                var flyOut = cam ? StartCoroutine(FlyOut()) : null; // the camera flies back while you both slide
                // Hold on the body, easing onto where the rules have you, still facing it. Without the ragdoll the corpse
                // slides with it so its hips end over the centre of the body's tile (the ragdoll does that once it settles).
                var hips = limp || stayPut ? null : va.GetBoneTransform(HumanBodyBones.Hips);
                Vector3 OffCentre()
                {
                    if (!hips) return Vector3.zero;
                    var d = World(bodyTile) - root.InverseTransformPoint(hips.position);
                    return new Vector3(d.x, 0f, d.z);
                }
                for (float time = 0f; time < takedownHold; time += Time.deltaTime)
                {
                    playerShown = Vector3.Lerp(slideFrom, end, Mathf.SmoothStep(0f, 1f, time / takedownHold));
                    va.transform.localPosition += OffCentre() * Mathf.Clamp01(Time.deltaTime / Mathf.Max(0.01f, takedownHold - time));
                    yield return null;
                }
                va.transform.localPosition += OffCentre();
                playerShown = end;
                if (flyOut != null) yield return flyOut;
                playerAnim.speed = va.speed = 1f;
                knifeReversed = false;
                pairedKills.Add(victim);
                takedownVictim = null;
            }
            busy = false;
        }

        Vector3? Chest(Animator a)
        {
            var bone = a.GetBoneTransform(HumanBodyBones.Chest);
            return bone ? root.InverseTransformPoint(bone.position) : (Vector3?)null;
        }

        static void Override(Animator a, string slot, AnimationClip clip)
        {
            if (a.runtimeAnimatorController is AnimatorOverrideController o) o[slot] = clip;
        }

        // ---- Kill cam ----

        // Starts a kill cam: remembers your view exactly and hides the HUD until EndKillCam.
        void BeginKillCam()
        {
            if (camming) return;
            camming = true;
            savedFocus = focus;
            savedDistance = camDistance;
            savedCamRot = cam.transform.rotation;
            camTarget = null;
            hud.SetVisible(false);
        }

        // Where to film a pair (`a` and `b`, root space) from: low and close, side-on (or the takedown's own offset,
        // in the pair's space), trying other angles until nothing solid is between the camera and the pair.
        (Vector3 pos, Vector3 look) KillCamPose(Vector3 a, Vector3 b, Vector3 offset, Vector3 lookShift = default)
        {
            var mid = (a + b) / 2f + Vector3.up * FigureHeight * 0.5f;
            var along = b - a;
            along.y = 0f;
            var pair = Quaternion.LookRotation(along.sqrMagnitude > 1e-6f ? along : Vector3.forward);
            // The takedown's own shot first (set in the animation test): its look point moved by Cam Look, the camera
            // Cam Offset from that, if nothing is in the way.
            if (offset != Vector3.zero)
            {
                // Seat backs don't count; a wall does. Then the same shot (height, distance, look) swung round: mirrored,
                // then 30 degrees at a time either way, the first clear one. Only when none is clear, the defaults.
                var focusAt = mid + pair * lookShift;
                var mirrored = new Vector3(-offset.x, offset.y, offset.z);
                foreach (var yaw in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f })
                    foreach (var o in yaw == 0f ? new[] { offset, mirrored } : new[] { offset })
                    {
                        var pos = focusAt + pair * (Quaternion.Euler(0f, yaw, 0f) * o);
                        if (!CamBlocked(focusAt, pos, false)) return (pos, focusAt);
                    }
            }
            var tries = new List<Vector3>();
            // the side nearer the view you had first, then three-quarter views, the other side, and finally from above
            var side = pair * Vector3.right;
            if (Vector3.Dot(side, savedCamRot * Vector3.back) < 0f) side = -side;
            foreach (var yaw in new[] { 0f, 35f, -35f, 70f, -70f, 180f, 145f, -145f })
                tries.Add(Quaternion.Euler(0f, yaw, 0f) * side * killCamClose + Vector3.up * killCamHeight);
            // Only when every close angle is blocked: the same angles pulled back, then from above.
            foreach (var yaw in new[] { 0f, 35f, -35f, 70f, -70f, 180f })
                tries.Add(Quaternion.Euler(0f, yaw, 0f) * side * killCamClose * 1.6f + Vector3.up * killCamHeight * 1.5f);
            tries.Add(savedCamRot * Vector3.back * killCamClose * 2f + Vector3.up * killCamHeight * 3f);
            foreach (var t in tries)
                if (!CamBlocked(mid, mid + t)) return (mid + t, mid);
            return (mid + tries[tries.Count - 1], mid);
        }

        // Would the shot from `pos` to `mid` look through a wall, the floor, luggage or a seat back? The camera-side
        // wall only casts shadows (cutaway), so looking through it is fine.
        // seats: whether a seat back in the way counts (a takedown's own shot looks down over them).
        bool CamBlocked(Vector3 mid, Vector3 pos, bool seats = true)
        {
            for (float k = 0.15f; k <= 1.001f; k += 0.08f)
            {
                var p = Vector3.Lerp(mid, pos, k);
                if (p.y < 0.2f || p.z > 0.42f || p.x < -0.42f || p.x > c.W - 0.58f) return true; // floor, far wall, end walls
                if (!cutawayNearWall && p.z < -(c.H - 1) - 0.42f) return true;
                var cell = new Cell(Mathf.RoundToInt(p.x), Mathf.RoundToInt(-p.z));
                if (c.IsSolid(cell) && p.y < 2.2f) return true;
                if (seats && c.IsSeat(cell) && p.y < 1.05f) return true;
            }
            return false;
        }

        void SetCam((Vector3 pos, Vector3 look) pose)
        {
            cam.transform.position = root.TransformPoint(pose.pos);
            cam.transform.LookAt(root.TransformPoint(pose.look));
        }

        // Flies the camera in to `pose` (smoothly, on real time, so slow-mo doesn't drag it), then holds a beat.
        IEnumerator FlyIn((Vector3 pos, Vector3 look) pose)
        {
            Vector3 p0 = cam.transform.position;
            var r0 = cam.transform.rotation;
            var p1 = root.TransformPoint(pose.pos);
            var r1 = Quaternion.LookRotation(root.TransformPoint(pose.look) - p1);
            for (float time = 0f; time < killCamFlyIn; time += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, time / killCamFlyIn);
                cam.transform.SetPositionAndRotation(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k));
                yield return null;
            }
            SetCam(pose);
            for (float time = 0f; time < killCamPause; time += Time.unscaledDeltaTime) yield return null;
        }

        // Flies back to exactly the view you had, then ends the cam.
        IEnumerator FlyOut()
        {
            if (!camming) yield break;
            Time.timeScale = 1f;
            Vector3 p0 = cam.transform.position;
            var r0 = cam.transform.rotation;
            var p1 = root.TransformPoint(savedFocus) - savedCamRot * Vector3.forward * savedDistance;
            for (float time = 0f; time < killCamFlyOut; time += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, time / killCamFlyOut);
                cam.transform.SetPositionAndRotation(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, savedCamRot, k));
                yield return null;
            }
            EndKillCam();
        }

        // Back to exactly the view you had, at once (also on a restart mid-cam).
        void EndKillCam()
        {
            if (!camming) return;
            camming = false;
            slowUntil = -1f;
            Time.timeScale = 1f;
            focus = savedFocus;
            camDistance = savedDistance;
            cam.transform.rotation = savedCamRot;
            cam.transform.position = focus - cam.transform.forward * camDistance;
            hud.SetVisible(true);
        }

        // Rail grab and throw (placeholder animations for now): fly in on the spot (the pair lined up `along`), watch
        // `target` for sceneCamTime at `slowMo`, fly back.
        IEnumerator SceneKillCam(System.Func<Vector3> target, Vector3 spot, Vector3 along, bool hitAtStart)
        {
            busy = true;
            BeginKillCam();
            var pose = KillCamPose(spot - along * 0.5f, spot + along * 0.5f, Vector3.zero);
            yield return FlyIn(pose);
            if (hitAtStart) HitSlowMo();
            for (float time = 0f; time < sceneCamTime; time += Time.unscaledDeltaTime)
            {
                SetCam((pose.pos, target() + Vector3.up * FigureHeight * 0.4f));
                yield return null;
            }
            slowUntil = -1f;
            Time.timeScale = 1f;
            yield return FlyOut();
            busy = false;
        }

        // ---- Legend lighting ----

        // Steps at 25, 50, 75 and 100, blended over about a second (snaps on a new carriage).
        void LegendLighting()
        {
            int tier = Mathf.Clamp(c.legend / 25, 0, legendLampColors.Length - 1);
            var col = tier == 0 ? lampColor : legendLampColors[tier]; // legend 0: the lamps as set above
            float level = legendLampLevels[Mathf.Min(tier, legendLampLevels.Length - 1)];
            if (shownLampLevel < 0f) { shownLampColor = col; shownLampLevel = level; }
            float k = 1f - Mathf.Exp(-4f * Time.deltaTime);
            shownLampColor = Color.Lerp(shownLampColor, col, k);
            shownLampLevel = Mathf.Lerp(shownLampLevel, level, k);
            foreach (var l in lamps)
            {
                if (!l) continue;
                l.color = shownLampColor;
                l.intensity = (tableLamps.Contains(l) ? tableLampPower : wallLampIntensity) * shownLampLevel;
            }
            var tint = Color.Lerp(Color.white, shownLampColor, 0.5f);
            RenderSettings.ambientLight = baseAmbient * tint;
            RenderSettings.ambientIntensity = baseAmbientIntensity * shownLampLevel;
        }

        // One shot: the "Shoot" state if the enemy controller has one, a muzzle flash and a tracer to whatever they fired at.
        void ShotEffect(Enemy e)
        {
            if (!shown.ContainsKey(e)) return;
            var face = new Vector3(e.facing.x, 0f, -e.facing.y);
            if (anims.TryGetValue(e, out var a) && a.HasState(0, Animator.StringToHash("Shoot"))) a.Play("Shoot", 0, 0f);
            var hand = a ? a.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var muzzle = guns.TryGetValue(e, out var g) && g.muzzle && g.t.gameObject.activeInHierarchy ? root.InverseTransformPoint(g.muzzle.position)
                : hand ? root.InverseTransformPoint(hand.position) + face * 0.15f : shown[e] + Vector3.up * FigureHeight * 0.7f + face * 0.4f;
            var chest = playerShown + Vector3.up * FigureHeight * 0.6f;
            var end = c.CanSee(e, c.player) ? chest : muzzle + face * 4f;
            var tracer = Line(muzzleColor, 0.03f);
            tracer.positionCount = 2;
            tracer.SetPosition(0, muzzle);
            tracer.SetPosition(1, end);
            var flash = new GameObject("Muzzle Flash").AddComponent<Light>();
            flash.transform.SetParent(root, false);
            flash.transform.localPosition = muzzle;
            flash.type = LightType.Point;
            flash.color = muzzleColor;
            flash.intensity = 6f;
            flash.range = 3f;
            Destroy(flash.gameObject, 0.08f);
            Destroy(tracer.gameObject, 0.1f);
        }

        static Vector3 World(Cell cell) => new Vector3(cell.x, 0, -cell.y);

        // Spots deaths and hits since last frame and bleeds for them. The takedown victim bleeds on cue instead.
        void DetectBlood()
        {
            foreach (var e in c.enemies)
            {
                if (lastState[e] != EnemyState.Dead && e.state == EnemyState.Dead && e != takedownVictim)
                {
                    var dir = World(e.pos) - playerShown;
                    dir.y = 0;
                    if (pendingAct == Act.RailGrab) { hung.Add(e); Smash(e.pos, true); } // through the glass
                    KillBlood(e, pendingAct, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward);
                }
                lastState[e] = e.state;
            }
            pendingAct = null;
            // A heavy's hit waits for its swing; a hop's reaction hits wait (still counted) until its shots play as you
            // climb back in (HopThrough).
            if (hopShots.Count > 0) return;
            if (c.hitsTaken > lastHits && !holdHit) HitBlood();
            lastHits = c.hitsTaken;
        }

        // `how` null = shot by someone else's gun. `dir` points from the killer through the victim. `chestAt`: the victim's
        // chest where a finisher has carried it (root space); else above its tile.
        void KillBlood(Enemy e, Act? how, Vector3 dir, Vector3? chestAt = null, bool gore = false)
        {
            if (!blood) return;
            var at = chestAt.HasValue ? new Vector3(chestAt.Value.x, 0f, chestAt.Value.z) : World(e.pos);
            var chest = chestAt ?? at + Vector3.up * FigureHeight * 0.6f;
            // The splash effect (it fades), the big gore one for a brutal kill; blood on the glass for a rail grab.
            if (how == Act.RailGrab) dir = e.pos.y == 0 ? Vector3.forward : Vector3.back;
            else if (how == null) { var r = Random.insideUnitCircle.normalized; dir = new Vector3(r.x, 0f, r.y); }
            blood.Splash(root, chest, dir, gore || how == Act.BrutalKill);
            // The stains that stay: straight casts from every tile the kill bloodied (a crouched stealth kill casts lower).
            bool low = how == Act.QuietKill && c.crouched;
            Stains(e.pos, low ? bloodCastCrouched : bloodCastStanding, how == Act.BrutalKill ? blood.poolScale : blood.spatterScale);
        }

        // Straight-cast stains (BloodKit.TileSplat) on each tile of the newest stain on `tile`; `centre` sizes the
        // floor stain on that tile itself, the rest get spatter.
        void Stains(Cell tile, float height, float centre)
        {
            var stain = c.stains.LastOrDefault(s => !s.trail && s.tiles.Contains(tile));
            foreach (var t in stain != null ? stain.tiles : new List<Cell> { tile })
            {
                blood.TileSplat(root, World(t), height, bloodCastReach, t == tile ? centre : blood.spatterScale, blood.smearScale);
                decaled.Add(t);
            }
        }

        // A takedown's big stain, as its blood flies: projected down over the victim from soakHeight, deep enough to
        // cover the floor, the body once it has fallen into it, and a table it's over. Sizes x the pool: brutal, stealth.
        public float soakBrutal = 0.7f, soakStealth = 0.35f, soakHeight = 0.3f, soakDepth = 1.2f;
        void Soak(Animator a, bool brutal)
        {
            if (!blood || !a) return;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var at = hips ? root.InverseTransformPoint(hips.position) : a.transform.localPosition;
            blood.Decal(root, new Vector3(at.x, soakHeight, at.z), Vector3.down, blood.poolScale * (brutal ? soakBrutal : soakStealth), soakDepth);
        }

        // The head comes off (a stomp): the head bone shrunk to nothing, a gore burst where it was.
        void HeadOff(Animator a)
        {
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            if (!head) return;
            var at = root.InverseTransformPoint(head.position);
            head.localScale = Vector3.one * 0.001f;
            if (!blood) return;
            blood.Splash(root, at, Vector3.up, true);
            blood.Splash(root, at, playerFacing, true);
            blood.Decal(root, new Vector3(at.x, 0f, at.z), Vector3.down, blood.poolScale);
        }

        // One bloody print each stride walked: a blood decal projected down, toes along the walk, alternate feet.
        // Undo the turn's moves: snaps back rather than walking, and the walk's footprints go with it.
        void Undo()
        {
            if (busy || !c.CanReset) return;
            c.Do("undo");
            playerRoute.Clear();
            playerShown = World(c.player);
            foreach (var p in turnPrints) if (p) Destroy(p);
            turnPrints.Clear();
            strideLeft = strideAtTurnStart;
            leftFoot = leftFootAtTurnStart;
        }

        // Prints laid this turn (Undo removes them) and the stride as the turn began; reset when a turn ends.
        readonly List<GameObject> turnPrints = new List<GameObject>();
        float strideAtTurnStart;
        bool leftFootAtTurnStart;

        void KeepTurnPrints()
        {
            turnPrints.Clear();
            strideAtTurnStart = strideLeft;
            leftFootAtTurnStart = leftFoot;
        }

        void Footprints(Vector3 step)
        {
            strideLeft -= step.magnitude;
            if (strideLeft > 0f || step.sqrMagnitude < 1e-8f) return;
            strideLeft = footprintStride;
            leftFoot = !leftFoot;
            if (!blood || blood.decals == null || blood.decals.Length == 0 || !blood.decals[0]) return;
            if (footprintMats == null)
            {
                var source = blood.decals[0].GetComponentInChildren<UnityEngine.Rendering.Universal.DecalProjector>();
                if (!source || !source.material) return;
                footprintMats = new Material[2];
                for (int i = 0; i < 2; i++)
                {
                    footprintMats[i] = new Material(source.material) { name = i == 0 ? "Footprint L" : "Footprint R" };
                    footprintMats[i].SetTexture("_MainTex", FootprintTexture(i == 1, footprintColor));
                    footprintMats[i].SetTexture("_NormalTex", Texture2D.normalTexture); // flat: the splat's bumps don't fit a print
                }
            }
            var dir = new Vector3(step.x, 0f, step.z).normalized;
            var go = Instantiate(blood.decals[0], root);
            go.name = "Footprint";
            turnPrints.Add(go);
            var projector = go.GetComponentInChildren<UnityEngine.Rendering.Universal.DecalProjector>();
            projector.transform.localPosition = Vector3.zero;
            projector.transform.localRotation = Quaternion.identity;
            projector.transform.localScale = Vector3.one;
            projector.material = footprintMats[leftFoot ? 0 : 1];
            projector.size = new Vector3(footprintWidth, footprintLength, footprintDepth);
            projector.pivot = Vector3.zero; // the box centred on the floor, reaching up over the runner
            projector.startAngleFade = 50f; projector.endAngleFade = 80f; // floors and what lies on them, not up the walls
            go.transform.localScale = Vector3.one;
            go.transform.localPosition = new Vector3(playerShown.x, 0f, playerShown.z) + Vector3.Cross(Vector3.up, dir) * (leftFoot ? -footprintGap : footprintGap);
            go.transform.localRotation = Quaternion.LookRotation(Vector3.down, dir); // projecting down, toes forward
        }

        // A shoe print in the blood decal's red (`col`, varied a little; alpha blotchy): the sole, a gap, the heel.
        // mirror: the other foot.
        static Texture2D FootprintTexture(bool mirror, Color col)
        {
            const int w = 64, h = 192;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(mirror ? 11 : 7);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    if (mirror) u = 1f - u;
                    // A classic shoe print (toes up, v = 1; the inside is +u). The sole: an egg, widest above its middle,
                    // leaning so its waist sits to the inside. The gap: a band tilted up to the inside, sagging a little in
                    // the middle; it cuts the sole's bottom and the heel's top. The heel: rounded below, set to the inside.
                    float sy = (v - 0.58f) / 0.42f;
                    float sole = 1f - (Sq((u - (0.5f - 0.05f * sy)) / (0.47f * (1f + 0.2f * sy))) + sy * sy);
                    float gap = 0.255f + 0.052f * u - 0.015f * Mathf.Sin(Mathf.PI * u);
                    sole = Mathf.Min(sole, (v - (gap + 0.055f)) * 12f);
                    float heel = 1f - (Sq((u - 0.56f) / 0.34f) + Sq((v - 0.12f) / 0.13f));
                    heel = Mathf.Min(heel, (gap - 0.055f - v) * 12f);
                    float k = (float)rng.NextDouble();
                    float a = Mathf.Clamp01(Mathf.Max(sole, heel) * 6f) * (0.55f + 0.45f * k) * col.a;
                    tex.SetPixel(x, y, new Color(col.r * (0.8f + 0.4f * k), col.g * (0.8f + 0.4f * k), col.b * (0.8f + 0.4f * k), a));
                }
            tex.Apply();
            return tex;
        }

        static float Sq(float f) => f * f;

        void HitBlood()
        {
            if (!blood) return;
            var spray = Random.insideUnitCircle.normalized;
            blood.Splash(root, playerShown + Vector3.up * FigureHeight * 0.6f, new Vector3(spray.x, 0, spray.y), false);
            blood.Decal(root, playerShown, Vector3.down, blood.dripScale);
            blood.Screen(screenCanvas.transform);
        }

        IEnumerator EnemyPhase()
        {
            busy = true;
            Poll(); // cards from the player's move first
            while (tut.Active) yield return null;
            c.Do("end");
            KeepTurnPrints(); // the turn's walk is final now
            foreach (var e in c.enemies)
            {
                if (c.Over) break;
                if (!e.InPlay && c.Decide(e) != Intent.Flee) continue; // broken ones only move to flee
                if (followCamera) camTarget = shown[e];
                yield return new WaitForSeconds(enemyStepDelay / EnemyPace);
                bool fleeing = c.Decide(e) == Intent.Flee;
                int logBefore = c.log.Count, hpBefore = c.hp;
                bool melee = e.tough && (c.Decide(e) == Intent.Melee || c.Decide(e) == Intent.Charge);
                holdHit = melee;
                if (melee) hud.shownHp = hpBefore;
                c.Do($"enemy {e.id}");
                if (!routes.TryGetValue(e, out var route)) routes[e] = route = new Queue<Vector3>();
                if (e.walked != null)
                {
                    Follow(route, e.walked);
                    e.walked = null;
                }
                // The camera stays on each enemy until it has finished moving (a fleer all the way out of the door).
                bool walked = route.Count > 0;
                while (route.Count > 0 || (shown[e] - StandAt(e)).sqrMagnitude > 1e-3f)
                {
                    if (followCamera) camTarget = shown[e];
                    yield return null;
                }
                // Its swing, if it made one: the clip, then the hit as it lands.
                if (melee && c.log.Skip(logBefore).Any(line => line.Contains("lunges")))
                {
                    if (anims.TryGetValue(e, out var ea) && ea.HasState(0, Animator.StringToHash("Swing")))
                    {
                        var face = World(c.player) - shown[e];
                        face.y = 0f;
                        if (face.sqrMagnitude > 1e-4f) ea.transform.localRotation = Quaternion.LookRotation(face);
                        ea.CrossFadeInFixedTime("Swing", 0.1f);
                    }
                    for (float time = 0f; time < swingHitAt; time += Time.deltaTime) yield return null;
                }
                holdHit = false;
                hud.shownHp = -1;
                if (fleeing && e.state == EnemyState.Fled) { yield return new WaitForSeconds(0.3f / EnemyPace); gone.Add(e); }
                else if (walked) yield return new WaitForSeconds(0.15f / EnemyPace);
                Poll(); // a card pauses the turn before the next enemy acts
                while (tut.Active) yield return null;
            }
            c.Do("start");
            AutoSave(); // after the turn's results, so reloading never rerolls a shot
            if (followCamera) { yield return new WaitForSeconds(enemyStepDelay / EnemyPace); camTarget = playerShown; }
            busy = false;
        }

        // Broken enemies still fleeing when the level is won run out of the carriage before the end card.
        IEnumerator FleersOut()
        {
            busy = true;
            for (int turn = 0; turn < 12; turn++)
            {
                var fleers = c.enemies.Where(e => e.state == EnemyState.Broken && e.fleeing).ToList();
                if (fleers.Count == 0) break;
                foreach (var e in fleers)
                {
                    if (followCamera) camTarget = shown[e];
                    c.Do($"enemy {e.id}");
                    if (!routes.TryGetValue(e, out var route)) routes[e] = route = new Queue<Vector3>();
                    if (e.walked != null) { Follow(route, e.walked); e.walked = null; }
                    while (route.Count > 0 || (shown[e] - StandAt(e)).sqrMagnitude > 1e-3f)
                    {
                        if (followCamera) camTarget = shown[e];
                        yield return null;
                    }
                    if (e.state == EnemyState.Fled) { yield return new WaitForSeconds(0.3f); gone.Add(e); }
                }
            }
            if (followCamera) camTarget = playerShown;
            busy = false;
        }

        Cell? CellUnderMouse(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen);
            if (!new Plane(Vector3.up, new Vector3(0, 0.25f, 0)).Raycast(ray, out float d)) return null;
            var p = ray.GetPoint(d);
            var cell = new Cell(Mathf.RoundToInt(p.x), Mathf.RoundToInt(-p.z));
            return c.InBounds(cell) ? cell : (Cell?)null;
        }

        void Sync()
        {
            // One layer at a time, never stacked: hovering an enemy (or holding V) shows senses,
            // otherwise you see your movement (or reveal) range and the path preview.
            var hovered = hover.HasValue ? c.EnemyAt(hover.Value) : null;
            bool senses = hovered != null || showAllSenses;
            bool aiming = leaping || hopping || (throwing && c.held != null);
            var kill = KillMode;
            // With a kill selected the grid shows its attack range instead of where you can walk.
            var reach = busy || phase != Phase.Play || senses || aiming || kill.HasValue ? new HashSet<Cell>() : c.Reachable();
            // The move or leap under the mouse: per tile, seen or not, and who fires.
            var preview = busy || c.Over || senses || !hover.HasValue || hopping || throwing ? null
                : leaping ? (c.CanLeapTo(hover.Value) ? c.LeapPreview(hover.Value) : null)
                : c.MovePreview(hover.Value);
            reactTile = preview?.Where(p => p.shooter != null).Select(p => (Cell?)p.tile).FirstOrDefault();
            // Purple = your kill's noise: only when hovering a target you can act on, centred where it dies.
            var outcome = hovered != null && kill.HasValue && !busy ? c.KillOutcome(kill.Value, hovered) : null;
            threats.Clear();
            swingThreats.Clear();
            if (preview != null)
                foreach (var p in preview)
                    if (p.shooter != null) threats[p.shooter] = Mathf.RoundToInt(100 * c.HitChance(p.shooter, p.tile, leaping));
            previewLanding = preview != null && preview.Count > 1 ? preview[preview.Count - 1].tile : (Cell?)null;
            // A heavy that will reach the landing and swing counts like a shot.
            if (previewLanding.HasValue)
                foreach (var e in c.enemies)
                    if (!threats.ContainsKey(e) && c.Swings(e, previewLanding.Value)) // its reaction shot on the way shows instead
                    {
                        threats[e] = Mathf.RoundToInt(100 * c.t.meleeHitChance);
                        swingThreats.Add(e);
                    }
            SyncShotMarks(preview);
            SyncPath(preview);
            SyncPreviewGhost(preview);
            SyncClues();
            bool targeting = outcome.HasValue;
            int noise = targeting ? c.NoiseOf(kill.Value) : 0;
            var dies = targeting ? outcome.Value.body : default;
            SyncGhost(outcome);
            SyncLastKnown();
            for (int x = 0; x < c.W; x++)
                for (int y = 0; y < c.H; y++)
                {
                    var cell = new Cell(x, y);
                    var col = Color.clear; // overlays are see-through: nothing to show = invisible
                    if (senses)
                    {
                        // The whole cone faint (where they'd see you standing), strong where they'd see you as you are
                        // now (crouched behind a seat, say); red where a ready gun covers it.
                        bool Shown(Enemy e) => showAllSenses || e == hovered;
                        bool react = c.enemies.Exists(e => Shown(e) && (c.WillReact(e) || e.awareness == Awareness.Combat) && c.Sees(e, cell, false));
                        if (c.enemies.Exists(e => Shown(e) && c.CanSee(e, cell))) col = Fill(react ? ReactCol : SightCol, 0.55f);
                        else if (c.enemies.Exists(e => Shown(e) && c.Sees(e, cell, false))) col = Fill(react ? ReactCol : SightCol, 0.07f);
                        else if (targeting && noise > 0 && Within(cell, dies, noise)) col = Fill(HearCol, 0.4f);
                    }
                    else if (preview != null && preview.Exists(p => p.shooter != null && p.tile == cell))
                        col = Fill(ReactCol, 0.75f); // the move draws reaction fire here
                    else if (aiming)
                    {
                        if (leaping ? c.CanLeapTo(cell) : hopping ? c.CanHopTo(cell) : c.CanThrowTo(cell))
                            col = Fill(Bone, 0.6f); // one mode at a time and the banner names it: every target looks the same
                    }
                    else if (kill.HasValue && c.InReach(kill.Value, cell))
                        col = Fill(DangerRed, 0.35f); // the selected kill's reach
                    else if (reach.Contains(cell))
                        col = Fill(Bone, 0.18f); // faint: where you can walk
                    // Every stained tile gets a decal, once the kill or landing that stained it has bled for itself.
                    if (c.IsBloody(cell) && !bloodEdges[x, y]) bloodEdges[x, y] = BloodOutline(cell);
                    if (bloodEdges[x, y]) bloodEdges[x, y].enabled = bloodOutlines && phase == Phase.Play;
                    if (this.blood && c.IsBloody(cell) && !decaled.Contains(cell) && !FreshBlood(cell))
                    {
                        decaled.Add(cell);
                        this.blood.Decal(root, World(cell) + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f)), Vector3.down, this.blood.spatterScale);
                    }
                    // The hovered tile: a solid, thicker outline at full strength, warning what you'd step onto:
                    // red = a gun covers it (reaction fire), amber = someone would see you there, else bone white.
                    var m = tiles[x, y].material;
                    bool under = hover == cell;
                    if (under)
                        col = c.enemies.Exists(e => c.WillReact(e) && c.CanSee(e, cell)) ? ReactCol
                            : c.SeenByAnyone(cell) ? SightCol : Bone;
                    m.color = col;
                    m.SetFloat("_Width", under ? 0.15f : 0.1f);
                    m.SetFloat("_Opacity", under ? 1f : 0.5f);
                }

            SyncZone();
            SyncMarks();
            SyncBodies();
            playerBody.transform.localPosition = new Vector3(c.player.x, c.crouched ? 0.3f : 0.5f, -c.player.y);
            playerBody.transform.localScale = new Vector3(0.6f, c.crouched ? 0.3f : 0.5f, 0.6f);
            // More blood-soaked as the legend fills.
            var blood = Color.Lerp(Color.white, new Color(0.55f, 0f, 0f), c.legend / (float)tuning.maxLegend);
            playerBody.material.color = blood;
            if (playerAnim) SyncPlayerAnim();

            foreach (var kv in views)
            {
                var e = kv.Key;
                var (body, nose) = kv.Value;
                bool present = e.state == EnemyState.Active || e.state == EnemyState.Broken || e == takedownVictim
                    || (e.state == EnemyState.Fled && !gone.Contains(e)); // a fleer stays in sight until out of the door
                body.gameObject.SetActive(present);
                nose.gameObject.SetActive(e.InPlay);
                if (anims.TryGetValue(e, out var anim))
                {
                    if (e.state == EnemyState.Dead && e != takedownVictim) { Corpse(e, anim); continue; }
                    SyncEnemyAnim(e, anim, present);
                }
                if (!present) continue;
                bool broken = e.state == EnemyState.Broken;
                var pos = new Vector3(e.pos.x, broken ? 0.25f : 0.5f, -e.pos.y);
                body.transform.localPosition = pos;
                body.transform.localScale = broken ? new Vector3(0.6f, 0.25f, 0.6f) : e.tough ? new Vector3(0.8f, 0.6f, 0.8f) : new Vector3(0.6f, 0.5f, 0.6f);
                body.material.color = SuitColor(e);
                var dir = new Vector3(e.facing.x, 0, -e.facing.y).normalized;
                nose.transform.localPosition = pos + dir * 0.4f + Vector3.up * 0.2f;
                nose.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        // Kill preview: a see-through you where you'll end up, facing the victim, and a red square where the body lands.
        void SyncGhost((Cell player, Cell body)? outcome)
        {
            bool on = outcome.HasValue;
            ghostBody.positionCount = on ? 4 : 0;
            if (!on) return;
            var mid = World(outcome.Value.body) + Vector3.up * 0.06f;
            const float h = 0.42f;
            ghostBody.SetPosition(0, mid + new Vector3(-h, 0, -h));
            ghostBody.SetPosition(1, mid + new Vector3(h, 0, -h));
            ghostBody.SetPosition(2, mid + new Vector3(h, 0, h));
            ghostBody.SetPosition(3, mid + new Vector3(-h, 0, h));
        }

        // The last-seen ghost (Carriage.LastSeen: where you were last seen, while someone who saw you there still hunts).
        // A new sighting waits until you're shown on that tile (the rules run ahead of the animation), then freezes
        // a see-through copy of you in exactly that pose: mid-hop, crouched, mid-kill, dragging. Hidden while you
        // stand on it yourself.
        void SyncLastKnown()
        {
            var seen = c.LastSeen;
            if (!seen.HasValue) ghostCell = pendingGhost = null;
            else if (seen != ghostCell && seen != pendingGhost) pendingGhost = seen;
            if (pendingGhost.HasValue)
            {
                var at = World(pendingGhost.Value);
                var flat = new Vector3(playerShown.x, 0f, playerShown.z);
                bool there = (flat - at).sqrMagnitude < 0.35f * 0.35f;
                // A leap or hop the rules have done but whose animation only starts later this frame isn't settled
                // (the ghost was being frozen on the take-off tile).
                bool starting = (c.leaped && !wasLeaped) || (c.hopped && !wasHopped);
                bool settled = !starting && playerRoute.Count == 0 && flipT < 0f && !hopRunning && takedownVictim == null;
                if (there || settled)
                {
                    SnapshotGhost(at, there);
                    ghostCell = pendingGhost;
                    pendingGhost = null;
                }
            }
            if (ghostRenderers == null) return;
            bool on = ghostCell.HasValue && ghostCell != c.player;
            foreach (var r in ghostRenderers) if (r) r.enabled = on;
        }

        // Your pose, frozen on the sighting tile `at` (exactly where your model is, if it's on that tile now).
        void SnapshotGhost(Vector3 at, bool there)
        {
            if (!ghost) ghost = NewGhost(GhostMaterial(), out ghostRenderers);
            if (playerAnim)
            {
                CopyPose(ghost);
                ghost.position = there ? playerAnim.transform.position : root.TransformPoint(at);
                ghost.rotation = playerAnim.transform.rotation;
                ghost.localScale = playerAnim.transform.localScale;
            }
            else ghost.localPosition = at + Vector3.up * (c.crouched ? 0.3f : 0.5f);
        }

        // A see-through copy of you (a frozen pose, set by CopyPose), or a capsule without the models.
        Transform NewGhost(Material see, out Renderer[] renderers)
        {
            Transform g;
            if (HasModels)
            {
                var a = Character(characterModel, playerController);
                a.enabled = false;
                g = a.transform;
            }
            else g = Box(Vector3.zero, new Vector3(0.6f, 0.5f, 0.6f), Color.white, PrimitiveType.Capsule).transform;
            renderers = g.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                r.sharedMaterials = Enumerable.Repeat(see, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return g;
        }

        void CopyPose(Transform g)
        {
            var from = playerAnim.GetComponentsInChildren<Transform>(true);
            var to = g.GetComponentsInChildren<Transform>(true);
            for (int i = 1; i < from.Length && i < to.Length; i++)
            {
                to[i].localPosition = from[i].localPosition;
                to[i].localRotation = from[i].localRotation;
                to[i].localScale = from[i].localScale;
            }
        }

        // The aimed move or leap as a line along the floor, a segment per tile: bone where nobody will see you, red
        // where someone will.
        void SyncPath(List<(Cell tile, bool seen, Enemy shooter)> preview)
        {
            int n = preview?.Count ?? 0;
            while (pathLines.Count < n) pathLines.Add(Line(Bone, 0.07f));
            for (int i = 0; i < pathLines.Count; i++)
            {
                var lr = pathLines[i];
                lr.enabled = n > 1 && i < n;
                if (!lr.enabled) continue;
                var at = World(preview[i].tile);
                var a = i > 0 ? (World(preview[i - 1].tile) + at) / 2f : at;
                var b = i < n - 1 ? (World(preview[i + 1].tile) + at) / 2f : at;
                lr.positionCount = 2;
                lr.SetPosition(0, a + Vector3.up * 0.08f);
                lr.SetPosition(1, b + Vector3.up * 0.08f);
                lr.startColor = lr.endColor = preview[i].seen ? DangerRed : Bone;
            }
        }

        // The hit chance on each tile of the aimed move or leap that draws a shot (a leap's is lower, and says so).
        readonly List<TMPro.TextMeshPro> shotMarks = new List<TMPro.TextMeshPro>();
        void SyncShotMarks(List<(Cell tile, bool seen, Enemy shooter)> preview)
        {
            int n = 0;
            if (preview != null && phase == Phase.Play)
                foreach (var p in preview)
                {
                    if (p.shooter == null) continue;
                    if (n == shotMarks.Count) shotMarks.Add(FloorText(Bone));
                    var m = shotMarks[n++];
                    m.transform.localPosition = World(p.tile) + Vector3.up * 0.1f;
                    m.text = $"{threats[p.shooter]}%" + (leaping ? $"\n<size=55%>LEAP -{Mathf.RoundToInt(100 * c.t.leapHitPenalty)}</size>" : "");
                }
            for (int i = 0; i < shotMarks.Count; i++) shotMarks[i].gameObject.SetActive(i < n);
        }

        // Flat Anton text lying on the floor.
        TMPro.TextMeshPro FloorText(Color col)
        {
            var q = new GameObject("Floor Text").AddComponent<TMPro.TextMeshPro>();
            q.transform.SetParent(root, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.rectTransform.sizeDelta = Vector2.one;
            var font = Resources.Load<TMPro.TMP_FontAsset>("Fonts/Anton SDF");
            if (font) q.font = font;
            q.fontSize = 3.2f;
            q.alignment = TMPro.TextAlignmentOptions.Center;
            q.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            q.color = col;
            return q;
        }

        // A see-through you at the end of the aimed move or leap, in your pose, facing along the last step.
        void SyncPreviewGhost(List<(Cell tile, bool seen, Enemy shooter)> preview)
        {
            bool on = preview != null && preview.Count > 1;
            if (on && !previewGhost) previewGhost = NewGhost(Outline(Fill(Bone, 0.9f)), out previewGhostRenderers);
            if (previewGhostRenderers != null) foreach (var r in previewGhostRenderers) if (r) r.enabled = on;
            if (!on) return;
            var end = World(preview[preview.Count - 1].tile);
            var dir = end - World(preview[preview.Count - 2].tile);
            if (playerAnim)
            {
                CopyPose(previewGhost);
                previewGhost.localPosition = end;
                previewGhost.localRotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.right);
                previewGhost.localScale = playerAnim.transform.localScale;
            }
            else previewGhost.localPosition = end + Vector3.up * (c.crouched ? 0.3f : 0.5f);
        }

        // "?" on the floor wherever a searcher is going to go and look (a noise, blood, a body, a glimpse of movement),
        // one per spot, gone once nobody is. Never where they saw you (that's the last-seen ghost), and never for one
        // looking around where it stands after a shout or gunfire (its arrow shows that).
        void SyncClues()
        {
            var ghostAt = c.LastSeen;
            var spots = new HashSet<Cell>(c.enemies.Where(e => e.InPlay && e.awareness == Awareness.Searching && e.hunt != Hunt.Look && e.lastKnown.HasValue && e.lastKnown != ghostAt)
                .Select(e => e.lastKnown.Value));
            foreach (var s in spots) if (!clueMarks.ContainsKey(s)) clueMarks[s] = ClueMark(s);
            foreach (var kv in clueMarks) kv.Value.SetActive(spots.Contains(kv.Key) && phase == Phase.Play);
        }

        // A bone "?" lying on the tile.
        GameObject ClueMark(Cell cell)
        {
            var go = new GameObject("Investigate");
            go.transform.SetParent(root, false);
            go.transform.localPosition = World(cell) + Vector3.up * 0.06f;
            var q = new GameObject("?").AddComponent<TMPro.TextMeshPro>();
            q.transform.SetParent(go.transform, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.rectTransform.sizeDelta = Vector2.one;
            var font = Resources.Load<TMPro.TMP_FontAsset>("Fonts/Anton SDF");
            if (font) q.font = font;
            q.fontSize = 5f;
            q.fontStyle = TMPro.FontStyles.Italic;
            q.alignment = TMPro.TextAlignmentOptions.Center;
            q.color = Bone;
            q.text = "?";
            return go;
        }

        // What each enemy will do if you end your turn now: the coming enemy turn played on a copy of the car (its
        // history replayed onto a fresh one, as a save is: the same dice), recording every enemy's walk and the way it
        // ends up facing. So the arrows can't disagree with the turn: another enemy's shot or shout, who gets in whose
        // way, a body found, all play out as they will. Redone whenever a command changes the car.
        // ponytail: a full replay per command; fine at a few hundred commands a car, cache a copy if it ever shows.
        readonly Dictionary<int, (List<Cell> walk, Cell facing)> forecast = new Dictionary<int, (List<Cell>, Cell)>();
        int forecastAt = -1;
        Carriage forecastFor;

        void Forecast()
        {
            if (forecastFor == c && forecastAt == c.history.Count) return;
            forecastFor = c;
            forecastAt = c.history.Count;
            forecast.Clear();
            if (c.Over || c.freeWalk) return;
            var sim = Train.Make(car, tuning, rulesSeed, startHp);
            foreach (var cmd in c.history) sim.Do(cmd);
            sim.Do("end");
            foreach (var e in sim.enemies) // as EnemyPhase plays it
            {
                if (sim.Over) break;
                if (!e.InPlay && sim.Decide(e) != Intent.Flee) continue;
                sim.Do($"enemy {e.id}");
                forecast[e.id] = (e.walked ?? new List<Cell>(), e.facing);
            }
        }

        // Patrol routes (drawn once at Build) and, for whoever will move or turn next turn, the route it will walk and
        // an arrow for the way it will face at the end (from the forecast).
        void SyncMarks()
        {
            bool show = !busy && !c.Over && phase == Phase.Play;
            if (show) Forecast();
            foreach (var kv in marks)
            {
                var e = kv.Key;
                var (route, arrow) = kv.Value;
                route.enabled = e.InPlay && e.route != null && e.awareness == Awareness.Unaware && phase == Phase.Play;
                bool known = forecast.TryGetValue(e.id, out var plan);
                arrow.enabled = show && e.InPlay && known && (plan.walk.Count > 0 || plan.facing != e.facing);
                if (!arrow.enabled) continue;
                var f = plan.facing;
                var dir = new Vector3(f.x, 0f, -f.y);
                var side = new Vector3(dir.z, 0f, -dir.x);
                var lift = Vector3.up * 0.07f;
                var pts = new List<Vector3> { World(e.pos) + lift };
                foreach (var tile in plan.walk) pts.Add(World(tile) + lift);
                var from = pts[pts.Count - 1] + dir * 0.3f;
                var tip = from + dir * 0.45f;
                pts.AddRange(new[] { from, tip, tip - dir * 0.18f + side * 0.14f, tip, tip - dir * 0.18f - side * 0.14f });
                arrow.positionCount = pts.Count;
                arrow.SetPositions(pts.ToArray());
                arrow.startColor = arrow.endColor = Fill(DangerRed, 0.95f);
            }
        }

        // Reaction fire zone: every tile a ready gun (searching or fighting) would see you on as you stand or crouch,
        // tinted faint red, with a fine red dashed line round exactly those tiles (overlaps merged: only the edges between
        // a watched tile and one that isn't).
        public float zoneTint = 0.12f;
        void SyncZone()
        {
            var watched = new HashSet<Cell>();
            if (!busy && !c.Over && phase == Phase.Play) // none behind the menus
                foreach (var e in c.enemies)
                {
                    if (!c.WillReact(e)) continue;
                    for (int x = 0; x < c.W; x++)
                        for (int y = 0; y < c.H; y++)
                        {
                            var cell = new Cell(x, y);
                            if (!c.Blocked(cell) && c.Sees(e, cell, c.crouched)) watched.Add(cell);
                        }
                }
            if (!dashMat)
            {
                var tex = new Texture2D(4, 1) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                tex.SetPixels(new[] { Color.white, Color.white, Color.clear, Color.clear });
                tex.Apply();
                dashMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, mainTextureScale = new Vector2(8f, 1f) }; // 8 dashes a tile
            }
            var tint = Fill(DangerRed, zoneTint);
            for (int x = 0; x < c.W; x++)
                for (int y = 0; y < c.H; y++)
                    tiles[x, y].material.SetColor("_FillColor", watched.Contains(new Cell(x, y)) ? tint : Color.clear);
            int n = 0;
            var sides = new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) };
            // The outline follows the red tiles exactly: every edge of a watched tile that isn't shared with another.
            foreach (var cell in watched)
                foreach (var d in sides)
                {
                    if (watched.Contains(new Cell(cell.x + d.x, cell.y + d.y))) continue;
                    if (n == zoneLines.Count)
                    {
                        var lr = Line(DangerRed, 0.035f);
                        lr.sharedMaterial = dashMat;
                        lr.textureMode = LineTextureMode.Tile;
                        lr.positionCount = 2;
                        zoneLines.Add(lr);
                    }
                    var edge = zoneLines[n++];
                    var mid = World(cell) + Vector3.up * 0.07f;
                    var out_ = new Vector3(d.x, 0f, -d.y) * 0.47f;
                    var along = new Vector3(-out_.z, 0f, out_.x);
                    edge.SetPosition(0, mid + out_ + along);
                    edge.SetPosition(1, mid + out_ - along);
                    edge.enabled = true;
                }
            for (int i = n; i < zoneLines.Count; i++) zoneLines[i].enabled = false;
        }

        // The optional red outline round a stained tile (B), inside the tile rim.
        LineRenderer BloodOutline(Cell cell)
        {
            var lr = Line(BloodEdge, 0.035f);
            lr.loop = true;
            lr.positionCount = 4;
            var mid = World(cell) + Vector3.up * 0.05f;
            const float h = 0.36f;
            lr.SetPosition(0, mid + new Vector3(-h, 0, -h));
            lr.SetPosition(1, mid + new Vector3(h, 0, -h));
            lr.SetPosition(2, mid + new Vector3(h, 0, h));
            lr.SetPosition(3, mid + new Vector3(-h, 0, h));
            lr.enabled = bloodOutlines;
            return lr;
        }

        // A tile still waiting for its own blood: next to a kill being played, or where a thrown body will land.
        bool FreshBlood(Cell cell)
        {
            if (takedownVictim != null && Mathf.Max(Mathf.Abs(cell.x - takedownVictim.pos.x), Mathf.Abs(cell.y - takedownVictim.pos.y)) <= 1) return true;
            if (busy && pendingAct.HasValue) return true;
            foreach (var b in flying)
                if (Mathf.Max(Mathf.Abs(cell.x - b.pos.x), Mathf.Abs(cell.y - b.pos.y)) <= 1) return true;
            return false;
        }

        // Corpses follow their bodies: dragged a tile at a time (smearing the trail), or thrown (a splash where they land).
        void SyncBodies()
        {
            foreach (var b in c.bodies)
            {
                if (!bodyCell.TryGetValue(b, out var was)) { bodyCell[b] = b.pos; continue; }
                if (!anims.TryGetValue(b.who, out var a) || b.who == takedownVictim || !corpses.Contains(b.who)) continue;
                if (!bodyOffset.ContainsKey(b.who)) bodyOffset[b.who] = a.transform.localPosition - World(was);
                if (was != b.pos)
                {
                    bodyCell[b] = b.pos;
                    if (posedBodies.Remove(b.who) && HasRagdoll(a)) StartCoroutine(Ragdoll(b.who, a, b.pos)); // off the table, it falls
                    if (Mathf.Max(Mathf.Abs(b.pos.x - was.x), Mathf.Abs(b.pos.y - was.y)) > 1) flying.Add(b); // thrown
                    else if (blood) { blood.Decal(root, World(was), Vector3.down, blood.dripScale); decaled.Add(was); } // the drag trail
                }
                var t = a.transform;
                var goal = World(b.pos) + bodyOffset[b.who];
                bool inAir = flying.Contains(b);
                var before = t.localPosition;
                t.localPosition = Vector3.MoveTowards(t.localPosition, goal, (inAir ? 10f : unitSpeed * 0.5f) * Time.deltaTime);
                if (inAir && (t.localPosition - goal).sqrMagnitude < 1e-4f)
                {
                    // Landed: the gore splash (a short effect) and the pools and spatter that stay.
                    flying.Remove(b);
                    if (!blood) continue;
                    var land = World(b.pos);
                    var dir = goal - before;
                    dir.y = 0f;
                    dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                    blood.Splash(root, land + Vector3.up * 0.4f, dir, true);
                    HitSlowMo(); // a thrown body's landing is its hit
                    Stains(b.pos, bloodCastCrouched, blood.poolScale);
                }
            }
        }

        // Drives Player.controller: Moving, Crouched (bools), Flip (trigger).
        void SyncPlayerAnim()
        {
            var target = new Vector3(c.player.x, 0, -c.player.y);
            if (c.leaped && !wasLeaped)
            {
                flipFrom = playerShown;
                flipT = 0f;
                // Past the normal leap range (only the bloodied leap gets there): the stylish flip, if the controller has it.
                bool far = (target - flipFrom).magnitude > c.t.leapRange + 0.01f && playerAnim.HasState(0, Animator.StringToHash("StylishFlip"));
                stylishFlip = far;
                flipTime = far ? stylishFlipTravel.keys[stylishFlipTravel.length - 1].time : flipLength;
                if (far) playerAnim.CrossFade("StylishFlip", 0.1f, 0, 0f);
                else playerAnim.SetTrigger("Flip");
            }
            wasLeaped = c.leaped;
            if (c.hopped && !wasHopped) { playerRoute.Clear(); StartCoroutine(HopThrough(playerShown, target, c.player.y)); } // out one window, in the next
            wasHopped = c.hopped;

            var before = playerShown;
            if (flipT >= 0f)
            {
                flipT += Time.deltaTime;
                playerShown = Vector3.Lerp(flipFrom, target, stylishFlip ? stylishFlipTravel.Evaluate(flipT) : flipT / flipTime);
                if (flipT >= flipTime) flipT = -1f;
            }
            bool moving = false;
            // Crouching or dragging costs double, so it looks slower too.
            float speed = exiting ? WalkOutSpeed : c.crouched || c.held != null ? unitSpeed * 0.5f : unitSpeed;
            if (parkedOn != c.player || playerRoute.Count > 0) parkedOn = null;
            if (flipT < 0f && takedownVictim == null && !hopRunning && parkedOn == null) moving = Advance(ref playerShown, playerRoute, target, speed); // else PairedKill() / HopThrough() moves it
            moving |= hopMoving;
            var step = playerShown - before;
            if (step.sqrMagnitude > 1e-6f) playerFacing = step.normalized;
            if (moving && !hopMoving && !c.freeWalk && c.legend >= footprintsFrom) Footprints(step);

            playerAnim.SetBool("Crouched", c.crouched);
            playerAnim.SetBool("Moving", moving);
            Stride(playerAnim, moving ? speed : 0f, c.crouched);
            var t = playerAnim.transform;
            t.localPosition = OnRunner(playerShown);
            t.localRotation = Quaternion.LookRotation(playerFacing);
            t.localScale = Vector3.one * characterScale;
            PoseKnife(knife, knifeOffset, knifeTwist, knifeReversed); // live, so Knife Offset / Twist can be tuned in Play mode
            // Soaks up as the legend fills, washes back when you're hurt; eased so a kill spreads rather than pops.
            // The walk-through car: every legend effect fully off.
            shownBlood = c.freeWalk ? 0f : Mathf.MoveTowards(shownBlood, c.legend / (float)tuning.maxLegend, bloodSpreadSpeed * Time.deltaTime);
            if (bloodOverlay) bloodOverlay.SetFloat("_Amount", shownBlood);
            if (auraHolder)
            {
                var hipsNow = playerAnim.GetBoneTransform(HumanBodyBones.Hips);
                auraHolder.position = hipsNow ? new Vector3(hipsNow.position.x, t.position.y, hipsNow.position.z) : t.position;
                auraHolder.rotation = t.rotation;
            }
            SyncAura(Mathf.InverseLerp(auraFrom, auraFull, shownBlood));
            SyncEyes(playerEyes, Mathf.InverseLerp(auraFrom, auraFull, shownBlood));
        }

        // ---- The knife (Train Assets/Knife, from railtracer_knife.py) in your right hand ----

        // Nudges on top of the grip worked out from the hand: metres in the knife's own space (+Y up the blade), and
        // degrees. Shared with the animation test.
        public Vector3 knifeOffset, knifeTwist;
        internal class Knife { public Transform t; public Vector3 pos; public Quaternion rot; public float unit; }
        Knife knife;
        bool knifeReversed; // a takedown with Reverse Grip is playing

        // Puts the knife in the right hand, gripped from the hand's own bones: the handle across the knuckles (blade
        // out of the thumb side, little finger to index), its edges along the fingers, the grip in the palm a little
        // short of the knuckles. Null without the model or the bones.
        internal static Knife HoldKnife(Animator a, GameObject model, float worldScale)
        {
            if (!a || !model) return null;
            var hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            var index = a.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little = a.GetBoneTransform(HumanBodyBones.RightLittleProximal) ?? a.GetBoneTransform(HumanBodyBones.RightRingProximal);
            var middle = a.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var thumb = a.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (!hand) return null;
            var k = new Knife { t = Instantiate(model, hand, false).transform };
            k.t.name = "Knife";
            float s = Mathf.Max(1e-4f, hand.lossyScale.x);
            k.unit = 1f / s;                                            // metres to hand-local units
            k.t.localScale = Vector3.one * worldScale / s;
            foreach (var col in k.t.GetComponentsInChildren<Collider>()) col.enabled = false;
            if (index && little && middle)
            {
                Vector3 I = hand.InverseTransformPoint(index.position), Lt = hand.InverseTransformPoint(little.position);
                Vector3 M = hand.InverseTransformPoint(middle.position);
                var across = (I - Lt).normalized;                       // up the blade
                var fingers = Vector3.ProjectOnPlane(M, across).normalized; // wrist to knuckles
                var palm = Vector3.Cross(across, fingers).normalized;
                if (thumb && Vector3.Dot(palm, hand.InverseTransformPoint(thumb.position) - M) < 0f) palm = -palm;
                float d = M.magnitude;                                   // the hand's length, wrist to knuckle
                k.pos = fingers * d * 0.95f + palm * d * 0.3f;
                k.rot = Quaternion.LookRotation(palm, across);           // +Y up the blade, the flats facing the palm
            }
            else { k.pos = new Vector3(0f, 0.08f, 0.02f) * k.unit; k.rot = Quaternion.identity; }
            PoseKnife(k, Vector3.zero, Vector3.zero);
            return k;
        }

        // reverse: the other way round in the fist (turned half round about the flats), the blade out of the little-finger side.
        internal static void PoseKnife(Knife k, Vector3 offset, Vector3 twist, bool reverse = false)
        {
            if (k == null || !k.t) return;
            var rot = k.rot * (reverse ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity) * Quaternion.Euler(twist);
            k.t.localRotation = rot;
            k.t.localPosition = k.pos + rot * (offset * k.unit);
        }

        // ---- Eyes ----

        // The Mixamo character's eyes are painted on its skin; its lash and brow cards (Ch33_Eyelashes) use the hair
        // texture and showed as white strips. They're drawn cut out (the texture's alpha) and brown. Each eye gets a
        // small iris disc where the lashes sit, coloured; yours glow red as the legend (the demon) takes form, with a
        // red trail behind them as you move.
        public Color browColor = new Color(0.2f, 0.12f, 0.06f);
        public Color tracerEyeColor = new Color(0.2f, 0.45f, 0.95f), enemyEyeBlue = new Color(0.25f, 0.45f, 0.8f), enemyEyeGreen = new Color(0.25f, 0.55f, 0.3f);
        public Color eyeGlow = new Color(1f, 0.08f, 0.04f);
        public float irisSize = 0.011f, irisDepth = 0.003f, eyeGlowStrength = 4f, eyeTrailTime = 0.35f, eyeTrailWidth = 0.012f;
        public Vector3 irisNudge; // metres: (right, up, forward) from where the lashes put each eye; applied as characters are made
        internal class Eyes { public Material mat; public TrailRenderer[] trails; }
        Eyes playerEyes;

        static void Lashes(Animator a, Color brows)
        {
            foreach (var r in a.GetComponentsInChildren<Renderer>())
            {
                if (r.name.IndexOf("Eyelash", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var mats = r.materials;
                foreach (var m in mats)
                {
                    m.color = brows;
                    if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);   // opaque, cut out
                    if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                    if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = (int)RenderQueue.AlphaTest;
                }
                r.materials = mats;
            }
        }

        void Lashes(Animator a) => Lashes(a, browColor);

        // Iris discs on the head bone, one per eye, found from the lash mesh (split left and right).
        Eyes AddEyes(Animator a, Color iris, bool tracer)
        {
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var lash = a.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name.IndexOf("Eyelash", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (!lash) { Colour(a, new[] { "Chr_Eye" }, iris); return null; } // the guard model has real eyes: colour them
            if (!head) return null;
            var mesh = new Mesh();
            lash.BakeMesh(mesh, true);
            var lt = lash.transform;
            var pts = mesh.vertices.Select(v => head.InverseTransformPoint(lt.position + lt.rotation * v)).ToList();
            Destroy(mesh);
            if (pts.Count < 4) return null;
            var right = head.InverseTransformDirection(a.transform.right).normalized;
            var up = head.InverseTransformDirection(a.transform.up).normalized;
            var fwd = head.InverseTransformDirection(a.transform.forward).normalized;
            var mid = pts.Aggregate(Vector3.zero, (s, p) => s + p) / pts.Count;
            float unit = 1f / Mathf.Max(1e-4f, head.lossyScale.x);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = iris };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            var eyes = new Eyes { mat = mat, trails = new TrailRenderer[2] };
            for (int side = 0; side < 2; side++)
            {
                var half = pts.Where(p => (Vector3.Dot(p - mid, right) > 0f) == (side == 1)).ToList();
                if (half.Count == 0) continue;
                var at = half.Aggregate(Vector3.zero, (s, p) => s + p) / half.Count;
                at += (right * irisNudge.x * (side == 1 ? 1f : -1f) + up * irisNudge.y + fwd * irisNudge.z) * unit;
                var disc = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(disc.GetComponent<Collider>());
                disc.name = side == 1 ? "Iris R" : "Iris L";
                disc.transform.SetParent(head, false);
                disc.transform.localPosition = at;
                disc.transform.localRotation = Quaternion.LookRotation(fwd, up);
                disc.transform.localScale = new Vector3(irisSize, irisSize, irisDepth) * unit;
                var dr = disc.GetComponent<Renderer>();
                dr.sharedMaterial = mat;
                dr.shadowCastingMode = ShadowCastingMode.Off;
                if (!tracer) continue;
                var trail = disc.AddComponent<TrailRenderer>();
                trail.time = eyeTrailTime;
                trail.minVertexDistance = 0.01f;
                trail.widthMultiplier = eyeTrailWidth;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                // Drawn over everything (no depth test, the overlay queue): it would otherwise hide behind the head
                // and only show where it pokes out at the sides.
                var trailMat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
                if (trailMat.HasProperty("unity_GUIZTestMode")) trailMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                trailMat.renderQueue = (int)RenderQueue.Overlay;
                trail.material = trailMat;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                eyes.trails[side] = trail;
            }
            return eyes;
        }

        // k: how far the demon has taken form (the aura's 0-1). Blue at none, glowing red at full, trails as they come.
        void SyncEyes(Eyes eyes, float k)
        {
            if (eyes == null || !eyes.mat) return;
            eyes.mat.color = Color.Lerp(tracerEyeColor, eyeGlow, k);
            eyes.mat.SetColor("_EmissionColor", eyeGlow * (k * eyeGlowStrength));
            foreach (var trail in eyes.trails)
            {
                if (!trail) continue;
                trail.emitting = k > 0.05f && !exiting; // none on the walk out of a cleared car
                if (exiting && trail.positionCount > 0) trail.Clear();
                trail.startColor = new Color(eyeGlow.r, eyeGlow.g, eyeGlow.b, Mathf.Clamp01(k));
                trail.endColor = new Color(eyeGlow.r, eyeGlow.g, eyeGlow.b, 0f);
            }
        }

        // ---- The guards' revolver (Resources/Weapons/Revolver.fbx) in their right hand ----

        // The model's barrel points along its +Z, its top up +Y, the grip hanging down at the back (checked in Blender).
        // It's measured once as it's made (its bounds), so its own units don't matter: scaled to Gun Length, gripped in the
        // fist like the knife (the grip across the knuckles, the barrel on past the fingers), finished in dark steel.
        public float gunLength = 0.33f;                 // metres at full size
        public Vector3 gunOffset, gunTwist;             // nudges, live in Play mode: metres in the gun's space, degrees
        public Color gunSteel = new Color(0.11f, 0.11f, 0.12f), gunCylinder = new Color(0.2f, 0.2f, 0.21f);
        internal class Gun { public Transform t, muzzle; public Vector3 pos, grip; public Quaternion rot; public float unit; }
        readonly Dictionary<Enemy, Gun> guns = new Dictionary<Enemy, Gun>();

        Gun HoldGun(Animator a)
        {
            var model = Resources.Load<GameObject>("Weapons/Revolver");
            var hand = a.GetBoneTransform(HumanBodyBones.RightHand);
            if (!model || !hand) return null;
            // Measure it at the world origin, unturned: its bounds are then its own.
            var go = Instantiate(model);
            go.name = "Revolver";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Destroy(go); return null; }
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.z < Mathf.Max(b.size.x, b.size.y)) Debug.LogWarning("Rail Tracer: the revolver's longest side isn't its Z; its hold may be off (Gun Twist).");
            foreach (var r in rends)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                    { color = r.name.IndexOf("Cylinder", System.StringComparison.OrdinalIgnoreCase) >= 0 ? gunCylinder : gunSteel };
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.85f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.55f);
                r.sharedMaterials = r.sharedMaterials.Select(_ => m).ToArray();
            }
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
            var grip = new Vector3(b.center.x, b.min.y + 0.30f * b.size.y, b.min.z + 0.19f * b.size.z);   // where the fist closes
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(go.transform, false);
            muzzle.localPosition = new Vector3(b.center.x, b.min.y + 0.85f * b.size.y, b.max.z);
            // Into the hand, at Gun Length.
            float hs = Mathf.Max(1e-4f, hand.lossyScale.x), s = gunLength * characterScale / Mathf.Max(1e-4f, b.size.z);
            go.transform.SetParent(hand, false);
            go.transform.localScale = Vector3.one * s / hs;
            var g = new Gun { t = go.transform, muzzle = muzzle, grip = grip * (s / hs), unit = 1f / hs };
            var index = a.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little = a.GetBoneTransform(HumanBodyBones.RightLittleProximal) ?? a.GetBoneTransform(HumanBodyBones.RightRingProximal);
            var middle = a.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var thumb = a.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (index && little && middle)
            {
                Vector3 I = hand.InverseTransformPoint(index.position), Lt = hand.InverseTransformPoint(little.position);
                Vector3 M = hand.InverseTransformPoint(middle.position);
                var across = (I - Lt).normalized;                              // up the grip, towards the trigger
                var fingers = Vector3.ProjectOnPlane(M, across).normalized;    // the barrel's way
                var palm = Vector3.Cross(across, fingers).normalized;
                if (thumb && Vector3.Dot(palm, hand.InverseTransformPoint(thumb.position) - M) < 0f) palm = -palm;
                float d = M.magnitude;
                var gripUp = new Vector3(0f, 0.08f, 0.065f).normalized;        // the grip's rake, bottom to top, in the gun's space
                var barrel = Vector3.ProjectOnPlane(Vector3.forward, gripUp).normalized;
                g.rot = Quaternion.LookRotation(fingers, across) * Quaternion.Inverse(Quaternion.LookRotation(barrel, gripUp));
                g.pos = fingers * d * 0.95f + palm * d * 0.3f;
            }
            else { g.rot = Quaternion.identity; g.pos = new Vector3(0f, 0.08f, 0.02f) * g.unit; }
            PoseGun(g);
            return g;
        }

        void PoseGun(Gun g)
        {
            if (g == null || !g.t) return;
            var rot = g.rot * Quaternion.Euler(gunTwist);
            g.t.localRotation = rot;
            g.t.localPosition = g.pos - rot * g.grip + rot * (gunOffset * g.unit);
        }

        void AddAura(Transform player)
        {
            var prefab = Resources.Load<GameObject>("Effects/Demon Aura");
            if (!prefab) { aura = null; return; }
            // On its own holder beside you, kept under your hips each frame (SyncPlayerAnim): a finisher's travel is in
            // its pose, not your transform, so the aura would otherwise stay behind on your tile.
            auraHolder = new GameObject("Aura").transform;
            auraHolder.SetParent(player.parent, false);
            aura = Instantiate(prefab, auraHolder, false).GetComponentsInChildren<ParticleSystem>();
            auraColors = aura.Select(p => p.main.startColor).ToArray();
            auraShown = -1f;
        }

        // Every system's start colour at `k` of its alpha (new particles only, so it eases in); stopped at nothing.
        void SyncAura(float k)
        {
            if (aura == null || Mathf.Abs(k - auraShown) < 0.005f) return;
            bool fresh = auraShown < 0f;
            auraShown = k;
            // Starting at nothing (a new car): clear what played on awake at full colour, or it lingers.
            if (fresh && k <= 0f) { aura[0].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); return; }
            for (int i = 0; i < aura.Length; i++)
            {
                var main = aura[i].main;
                main.startColor = Faded(auraColors[i], k);
            }
            var top = aura[0];
            if (k <= 0f && top.isEmitting) top.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            else if (k > 0f && !top.isPlaying) top.Play(true);
        }

        static ParticleSystem.MinMaxGradient Faded(ParticleSystem.MinMaxGradient g, float k)
        {
            Color A(Color c) => new Color(c.r, c.g, c.b, c.a * k);
            Gradient G(Gradient src)
            {
                if (src == null) return null;
                var o = new Gradient { mode = src.mode };
                o.SetKeys(src.colorKeys, src.alphaKeys.Select(a => new GradientAlphaKey(a.alpha * k, a.time)).ToArray());
                return o;
            }
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(A(g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(A(g.colorMin), A(g.colorMax));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(G(g.gradientMin), G(g.gradientMax));
                default: return new ParticleSystem.MinMaxGradient(G(g.gradient)) { mode = g.mode };
            }
        }

        // The window hop as it looks: the window slides up, you climb out, walk along the outside of the carriage,
        // the next window slides up, you climb back in; each window shuts behind you. The rules already moved you.
        // The window hop as it looks: the window slides up, you climb out, walk along the outside of the carriage
        // (hidden while you're out there), the next window slides up, you climb back in; each shuts behind you.
        // The rules already moved you.
        readonly List<Enemy> hopShots = new List<Enemy>(); // reaction shots drawn by a hop, held until you're back in

        IEnumerator HopThrough(Vector3 from, Vector3 to, int row)
        {
            hopRunning = true;
            bool wasBusy = busy;
            busy = true;
            var outward = new Vector3(0f, 0f, row == 0 ? 1.1f : -1.1f); // row 0's wall is on +z, the last row's on -z
            yield return Climb(SashNear(from, row), from, from + outward);
            ShowPlayer(false);
            hopMoving = true;
            var outside = to + outward;
            while ((playerShown - outside).sqrMagnitude > 1e-4f)
            {
                var step = Vector3.MoveTowards(playerShown, outside, hopWalkSpeed * Time.deltaTime) - playerShown;
                if (step.sqrMagnitude > 1e-8f) playerFacing = step.normalized;
                playerShown += step;
                yield return null;
            }
            hopMoving = false;
            yield return Climb(SashNear(to, row), outside, to, () => ShowPlayer(true));
            foreach (var o in hopShots) ShotEffect(o); // reaction fire at the landing, now you're on the tile
            if (hopShots.Count > 0) hud.shownHp = -1;
            hopShots.Clear();
            hopRunning = false;
            busy = wasBusy;
        }

        // Open the window (if there's a sash), climb through it in a little hop over the sill, shut it behind you.
        // started: called as the climb begins (after the window is open).
        IEnumerator Climb(SlidingWindow window, Vector3 a, Vector3 b, System.Action started = null)
        {
            if (window && window.broken) window = null; // smashed: straight through the frame
            if (window)
            {
                window.open = true;
                while (window && !window.IsOpen) yield return null;
            }
            started?.Invoke();
            var flat = b - a;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-6f) playerFacing = flat.normalized;
            hopMoving = true;
            // With a vault state ("Vault" or "Vault2", picked at random each climb) the clip does the jump (its height is
            // in the pose) and lasts as long as the climb; without one, a little code hop over the sill.
            float climbTime = hopClimbTime, lift = 0.7f;
            var vaults = playerAnim ? VaultStates.Where(h => playerAnim.HasState(0, h)).ToArray() : new int[0];
            if (vaults.Length > 0)
            {
                int vault = vaults[Random.Range(0, vaults.Length)];
                playerAnim.speed = hopVaultSpeed;
                // The animator can still be on (or leaving) the same vault from the last climb, where a cross-fade to it
                // does nothing: restart it outright.
                bool inVault = playerAnim.GetCurrentAnimatorStateInfo(0).shortNameHash == vault
                    || playerAnim.GetNextAnimatorStateInfo(0).shortNameHash == vault;
                if (inVault) playerAnim.Play(vault, 0, 0f);
                else playerAnim.CrossFade(vault, 0.1f, 0, 0f);
                yield return null; // it starts next frame; then the vault's length is known
                var info = playerAnim.GetNextAnimatorStateInfo(0);
                if (info.shortNameHash != vault) info = playerAnim.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == vault) climbTime = info.length / hopVaultSpeed;
                lift = 0f;
            }
            for (float time = 0f; time < climbTime; time += Time.deltaTime)
            {
                float k = time / climbTime;
                playerShown = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * lift;
                yield return null;
            }
            playerShown = b;
            if (playerAnim) playerAnim.speed = 1f;
            hopMoving = false;
            if (window) window.open = false;
        }

        static readonly int[] VaultStates = { Animator.StringToHash("Vault"), Animator.StringToHash("Vault2") };

        void ShowPlayer(bool on)
        {
            if (playerAnim) foreach (var r in playerAnim.GetComponentsInChildren<Renderer>()) r.enabled = on;
            else playerBody.enabled = on;
        }

        // A rail grab through a shut window: the sash swaps to its broken one (shards left in the frame; anything
        // stuck to the sash, like blood, stays), glass bursts out (burst) and a spill of it lies on the floor under
        // the window. An open window has no glass in the way. Rebuilt the same way for a resumed save.
        public int glassBurst = 45;
        void Smash(Cell at, bool burst)
        {
            var sash = SashNear(World(at), at.y);
            if (!sash || sash.broken || !sash.IsShut) return;
            var kit = Resources.Load<TrainKit>("Kit/TrainKit");
            float half = sashes.Find(s => s.sash == sash).half;
            var model = kit ? kit.Get(half >= 1f ? "Window_Sash_Large_Broken" : "Window_Sash_Small_Broken") : null;
            if (!model) return;
            sash.broken = true;
            var mode = ShadowCastingMode.On;
            foreach (var r in sash.GetComponentsInChildren<Renderer>()) { mode = r.shadowCastingMode; r.enabled = false; }
            foreach (var col in sash.GetComponentsInChildren<Collider>()) col.enabled = false; // no pane for blood to stick to
            var shell = Instantiate(model, sash.transform);
            foreach (var r in shell.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = mode; // the cut-away wall's stay shadows-only
            var outward = at.y == 0 ? Vector3.forward : Vector3.back;
            var spill = kit.Get("Glass_Shards");
            if (spill)
            {
                var floorGlass = Instantiate(spill, root);
                floorGlass.transform.localPosition = World(at) + outward * 0.15f;
                floorGlass.transform.localRotation = Quaternion.LookRotation(outward); // its window side (+Z) to the wall
            }
            var shard = kit.Get("Glass_Shard");
            if (burst && shard) GlassBurst(World(at) + outward * 0.45f + Vector3.up * 1.4f, shard);
        }

        // Glass flying both ways out of the window, falling and skittering on the floor.
        void GlassBurst(Vector3 at, GameObject shard)
        {
            var mf = shard.GetComponentInChildren<MeshFilter>();
            var mr = shard.GetComponentInChildren<MeshRenderer>();
            if (!mf || !mr) return;
            var go = new GameObject("Glass Burst");
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)glassBurst) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.8f, 1.1f, 0.05f);
            shape.randomDirectionAmount = 1f;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = spin.y = spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.dampen = 0.6f;
            collision.bounce = 0.15f;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Mesh;
            rend.mesh = mf.sharedMesh;
            rend.sharedMaterial = mr.sharedMaterial;
            ps.Play();
        }

        // The sash on that side whose window spans x (nearest, if none quite does). Null without the kit.
        SlidingWindow SashNear(Vector3 at, int row)
        {
            SlidingWindow best = null;
            float bestGap = float.MaxValue;
            foreach (var (sash, r, x, half) in sashes)
            {
                float gap = Mathf.Max(0f, Mathf.Abs(at.x - x) - half);
                if (r == row && sash && gap < bestGap) { best = sash; bestGap = gap; }
            }
            return bestGap <= 0.5f ? best : null;
        }

        // Where an enemy's figure stands: its tile, or for one that has fled, out through the end door it reached
        // (Flee Out Past metres beyond it), so it runs out of the carriage before it's gone.
        public float fleeOutPast = 2.5f;
        Vector3 StandAt(Enemy e) => e.state == EnemyState.Fled
            ? World(e.pos) + Vector3.right * (e.pos.x == 0 ? -fleeOutPast : fleeOutPast) : World(e.pos);

        static void Follow(Queue<Vector3> route, List<Cell> walked)
        {
            if (walked == null) return;
            foreach (var cell in walked) route.Enqueue(World(cell));
        }

        // Moves `shown` along `route`, then on to `target`; true while still on the way.
        static bool Advance(ref Vector3 shown, Queue<Vector3> route, Vector3 target, float speed)
        {
            var goal = route.Count > 0 ? route.Peek() : target;
            shown = Vector3.MoveTowards(shown, goal, speed * Time.deltaTime);
            if (route.Count > 0 && (shown - goal).sqrMagnitude < 1e-6f) route.Dequeue();
            return route.Count > 0 || (target - shown).sqrMagnitude > 1e-4f;
        }

        // Feeds the walk/run blend and plays it at the rate that makes the feet match `speed` (tiles/s).
        // Gait speeds come from the controller's WalkSpeed/RunSpeed/CrouchSpeed defaults (Rail Tracer > Set Up Animations).
        void Stride(Animator a, float speed, bool crouched)
        {
            if (!gaits.TryGetValue(a, out var gait))
            {
                var p = a.parameters;
                float Get(string name) => System.Array.Find(p, q => q.name == name)?.defaultFloat ?? 0f;
                gaits[a] = gait = System.Array.Exists(p, q => q.name == "Speed") ? new Vector3(Get("WalkSpeed"), Get("RunSpeed"), Get("CrouchSpeed")) : Vector3.zero;
            }
            if (gait == Vector3.zero) return; // controller not upgraded yet: leave the clips at normal speed
            float v = speed / characterScale; // the character's own m/s
            float natural = crouched ? gait.z : Mathf.Clamp(v, gait.x, gait.y);
            a.SetFloat("Speed", v);
            a.SetFloat("StrideScale", v > 0f && natural > 0f ? v / natural : 1f);
        }

        // Drives Enemy.controller: Moving, Broken (bools).
        void SyncEnemyAnim(Enemy e, Animator a, bool present)
        {
            a.gameObject.SetActive(present);
            if (!present) return;
            placed.Add(e);
            if (e == takedownVictim) return; // PairedKill() placed it; it stays put while it plays
            if (!routes.TryGetValue(e, out var route)) routes[e] = route = new Queue<Vector3>();
            var pos = shown[e];
            var before = pos;
            float pace = unitSpeed * EnemyPace; // the Options enemy turn speed
            bool moving = Advance(ref pos, route, StandAt(e), pace);
            shown[e] = pos;
            a.SetBool("Moving", moving);
            // Broken: down on their knees to beg ("BegStart", then the "Beg" loop), or the old cower without those states.
            bool broken = e.state == EnemyState.Broken && !e.fleeing;
            if (!a.HasState(0, Animator.StringToHash("BegStart"))) a.SetBool("Broken", broken);
            else if (broken && begging.Add(e)) a.CrossFade("BegStart", 0.25f, 0, 0f);
            if (armed.Contains(a)) a.SetBool("Armed", e.awareness != Awareness.Unaware); // holstered only while unaware
            if (guns.TryGetValue(e, out var held))
            {
                held.t.gameObject.SetActive(e.state == EnemyState.Active && e.awareness != Awareness.Unaware); // holstered; dropped when broken
                PoseGun(held);
            }
            Stride(a, moving ? pace : 0f, false);
            var t = a.transform;
            t.localPosition = OnRunner(pos);
            // Face where they're walking; once there, face where the rules say they're looking.
            var step = pos - before;
            t.localRotation = Quaternion.LookRotation(moving && step.sqrMagnitude > 1e-8f ? step : new Vector3(e.facing.x, 0, -e.facing.y));
            if (e.awareness == Awareness.Startled) t.localRotation *= Quaternion.Euler(-14f, 0f, 0f); // recoils
            else if (!moving && e.awareness == Awareness.Unaware) t.localRotation *= Quaternion.Euler(0f, IdleLook(e), 0f);
            t.localScale = Vector3.one * characterScale * (e.tough ? 1.15f : 1f);
            Tint(a, SuitColor(e));
        }

        // Idle reaction: now and then an unaware enemy glances round (yaw in degrees, eased out and back).
        float IdleLook(Enemy e)
        {
            float now = Time.time;
            if (!idleLook.TryGetValue(e, out var look)) idleLook[e] = look = (-10f, 0f, now + Random.Range(2f, 7f));
            if (now >= look.next) idleLook[e] = look = (now, Random.Range(30f, 60f) * (Random.value < 0.5f ? -1f : 1f), now + Random.Range(5f, 10f));
            float k = (now - look.start) / 1.6f;
            return k >= 0f && k <= 1f ? look.yaw * Mathf.Sin(k * Mathf.PI) : 0f;
        }

        // Bodies stay for the whole match. A paired-kill victim already lies where its clip ended (the victim state
        // has no exit); anyone else (shot, rail grab) topples over away from the player and freezes there.
        void Corpse(Enemy e, Animator a)
        {
            if (!corpses.Add(e) || pairedKills.Contains(e)) return;
            if (!placed.Contains(e))
            {
                // Dead before it was ever shown (a resumed save): stood on its body's tile first, as SyncEnemyAnim would.
                var lying = c.bodies.Find(b => b.who == e)?.pos ?? e.pos;
                var t = a.transform;
                t.localPosition = shown[e] = World(lying);
                t.localRotation = Quaternion.LookRotation(new Vector3(e.facing.x, 0, -e.facing.y));
                t.localScale = Vector3.one * characterScale * (e.tough ? 1.15f : 1f);
                Tint(a, SuitColor(e));
            }
            if (hung.Contains(e)) HangOut(e, a);
            else if (HasRagdoll(a)) StartCoroutine(Ragdoll(e, a, c.bodies.Find(b => b.who == e)?.pos ?? e.pos));
            else StartCoroutine(Topple(a.transform, shown[e] - playerShown));
        }

        // Rail grab: hips on the window sill, tipped out so the head and arms hang over the passing track, legs down
        // inside, one hooked higher than the other. Posed by hand (no clip for this).
        void HangOut(Enemy e, Animator a)
        {
            var body = c.bodies.Find(b => b.who == e);
            if (body == null) return;
            var t = a.transform;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var bones = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm }
                .Select(a.GetBoneTransform).ToArray();
            a.enabled = false; // freeze, then pose
            var outward = body.pos.y == 0 ? Vector3.forward : Vector3.back;
            var axis = Vector3.Cross(Vector3.up, outward); // tipping about this leans the head outward
            t.localRotation = Quaternion.LookRotation(outward);
            t.localPosition = World(body.pos) + outward * 0.45f;
            if (hips)
            {
                t.localPosition += Vector3.up * (railSillHeight - root.InverseTransformPoint(hips.position).y);
                t.RotateAround(hips.position, root.TransformDirection(axis), railLean);
                var down = new[] { -railLean - 5f, -railLean + 30f, -railLean + 10f, -railLean + 10f }; // left leg hooked higher
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i]) bones[i].RotateAround(bones[i].position, root.TransformDirection(axis), down[i]);
            }
            else t.localRotation = Quaternion.AngleAxis(railLean, axis) * t.localRotation;
            // Then the root where it was set by hand (the limbs keep the pose above).
            var half = body.pos.y == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            t.localPosition = World(body.pos) + half * railHangPos;
            t.localRotation = half * Quaternion.Euler(railHangRot);
            bodyCell[body] = body.pos; // stays put (SyncBodies)
            bodyOffset[e] = t.localPosition - World(body.pos);
        }

        // Ragdoll bones (added to the character by Editor/RagdollSetup: kinematic, colliders off, on Ignore Raycast so
        // blood casts and clicks pass through). A body goes limp and falls; once it has settled (ragdollSettle) it
        // freezes in that pose and slides so its hips sit over the centre of its tile.
        public float ragdollSettle = 1.6f, ragdollSlide = 0.15f; // the quick slide over its tile before it falls
        internal static bool HasRagdoll(Animator a) => a && a.GetComponentInChildren<Rigidbody>(true);

        internal static void SetLimp(Animator a, bool limp)
        {
            foreach (var rb in a.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = !limp;
                if (limp) rb.linearVelocity = rb.angularVelocity = Vector3.zero;
            }
            foreach (var col in a.GetComponentsInChildren<Collider>(true)) col.enabled = limp; // the character has only the ragdoll's
        }

        // slide: how long the slide over its tile takes (a takedown: the same as yours onto your tile, so you move
        // together); else Ragdoll Slide.
        IEnumerator Ragdoll(Enemy e, Animator a, Cell tile, float slide = -1f)
        {
            if (slide <= 0f) slide = ragdollSlide;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            a.enabled = false; // hold the clip's last pose
            // First a quick slide so the hips are over the body's tile, then it falls there.
            if (hips)
            {
                var d = World(tile) - root.InverseTransformPoint(hips.position);
                d.y = 0f;
                var start = a.transform.localPosition;
                for (float time = 0f; time < slide; time += Time.deltaTime)
                {
                    a.transform.localPosition = start + d * Mathf.SmoothStep(0f, 1f, time / slide);
                    yield return null;
                    if (!a) yield break;
                }
                a.transform.localPosition = start + d;
            }
            // Where the corpse now lies, for SyncBodies (drags and throws keep it): set here, after the slide, so an offset
            // taken mid-slide can't pull it back.
            bodyOffset[e] = a.transform.localPosition - World(c.bodies.Find(b => b.who == e)?.pos ?? tile);
            SetLimp(a, true); // the pose is the physics' now
            for (float time = 0f; time < ragdollSettle; time += Time.deltaTime)
            {
                yield return null;
                if (!a) yield break; // restarted mid-fall
            }
            SetLimp(a, false); // frozen where it fell (the animator stays off)
        }

        IEnumerator Topple(Transform t, Vector3 away)
        {
            t.GetComponent<Animator>().enabled = false; // freeze the pose
            away.y = 0;
            var axis = Vector3.Cross(Vector3.up, away.sqrMagnitude > 1e-4f ? away.normalized : t.forward);
            var start = t.localRotation;
            for (float time = 0f; time < 0.5f; time += Time.deltaTime)
            {
                float k = time / 0.5f;
                t.localRotation = Quaternion.AngleAxis(88f * k * k, axis) * start; // accelerates like a fall
                yield return null;
                if (!t) yield break; // restarted mid-fall
            }
            t.localRotation = Quaternion.AngleAxis(88f, axis) * start;
        }

        static Color Fill(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        // Same square reach as the rules' Hears(): radius 1 = the 8 tiles round it.
        static bool Within(Cell a, Cell b, int radius) =>
            radius > 0 && Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) <= radius;

    }
}
