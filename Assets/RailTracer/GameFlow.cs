using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RailTracer
{
    // The save file: one slot, overwritten automatically, plus progress and settings.
    [System.Serializable]
    public class SaveData
    {
        public RunSave run;                                  // null: nothing to continue
        public bool hasRun;                                  // JsonUtility never writes null, so this says whether `run` is real
        public bool[] cleared = new bool[Train.DemoCars];    // demo cars cleared at least once
        public BestTicket[] best = new BestTicket[Train.DemoCars];
        public Settings settings = new Settings();
    }

    // The run in progress, as of the start of the current player turn: which car, its seed and the health you came
    // in with, and every command since (Carriage.Do), replayed onto a fresh car to restore it exactly.
    [System.Serializable]
    public class RunSave
    {
        public bool endless, walk;  // walk: in the walk-through car before car `car`
        public int car;             // demo: 0-4; endless: car number from 1
        public int carSeed;         // endless: the layout's seed
        public int seed, startHp, turn;
        public List<string> history = new List<string>();
    }

    // Best result per demo car: most killed plus broken, then fewest turns.
    [System.Serializable]
    public class BestTicket
    {
        public bool set;
        public int killed, broken, turns;
        public bool Beats(BestTicket o) => !o.set || killed + broken > o.killed + o.broken || (killed + broken == o.killed + o.broken && turns < o.turns);
    }

    [System.Serializable]
    public class Settings
    {
        public bool killCam = true;
        public float killCamSlowMo = 1f;
        public float killCamChance = 1f; // how often a kill gets the kill cam (0-1), when it's on
        public int enemySpeed = 1;       // the enemy turn: 0 slow, 1 normal, 2 fast
        public float masterVolume = 1f;  // everything (AudioListener.volume), 0-1
        public float musicVolume = 1f;   // the music on top of that, 0-1
        public int windowMode = -1; // FullScreenMode (FullScreenWindow or Windowed; an old ExclusiveFullScreen reads as FullScreenWindow), -1 = as the player started
        public int width, height;   // the windowed size; 0 = as the player started
    }

    // The train and its menus: the demo's five cars in order (Train), legend and cooldowns reset each car and half
    // your health back, the walk-through car between cars, Endless mode, saves, and the start / Journey / Options
    // screens over the real carriages.
    public partial class Game
    {
        enum Phase { Menu, Entering, Play }
        enum MenuScreen { Start, Journey, Options, Controls, ResetData }
        Phase phase = Phase.Menu;
        MenuScreen screen = MenuScreen.Start;
        CarDef car = Train.Demo(0);
        bool endless, walking;
        int carIndex;          // demo: 0-4; endless: car number from 1 (in the walk-through car: the car ahead)
        int carSeed;           // endless: the car's layout
        int rulesSeed = 1, startHp = 30;
        List<string> replay;   // a save's commands, replayed by the next Build
        SaveData save = new SaveData();
        string choice;         // the centre panel: null, "pause", "board" (the next car) or "done" (demo complete)
        float winHeld = -1f;   // holding Space on the win card
        public float standUpTime = 0.4f; // clearing a car crouched: the beat to stand before walking out
        bool boardAsked;       // the board popup has shown for this visit to the glowing tile
        GameObject glow;
        int journeyPick = -1;
        public float tableLampHeight = 1.01f; // a dining table lamp's bulb, above the floor
        public float journeyZoom = 55f; // Journey: how far the camera stands from the picked car

        static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");
        static readonly float[] SlowMos = { 1f, 0.75f, 0.5f, 0.25f };
        static readonly float[] Chances = { 1f, 0.75f, 0.5f, 0.25f };
        static readonly string[] EnemySpeeds = { "Slow", "Normal", "Fast" };
        static readonly float[] EnemyPaces = { 0.6f, 1f, 1.8f };
        // How fast the enemy turn plays (Options): the pauses between enemies shorter, their walks quicker.
        float EnemyPace => EnemyPaces[Mathf.Clamp(save.settings.enemySpeed, 0, EnemyPaces.Length - 1)];
        bool KillCamOn => save.settings.killCam;
        // Whether this kill gets the kill cam: on, and the Options chance comes up.
        bool RollKillCam() => KillCamOn && Random.value < save.settings.killCamChance;
        public const int MaxHpCarry = 30;

        // ---- Saves ----

        void LoadSave()
        {
            try { if (File.Exists(SavePath)) save = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) ?? new SaveData(); }
            catch (System.Exception ex) { Debug.LogWarning("Rail Tracer: couldn't read the save (" + ex.Message + "); starting fresh."); save = new SaveData(); }
            if (save.cleared == null || save.cleared.Length != Train.DemoCars) save.cleared = new bool[Train.DemoCars];
            if (save.best == null || save.best.Length != Train.DemoCars) save.best = Enumerable.Range(0, Train.DemoCars).Select(_ => new BestTicket()).ToArray();
            for (int i = 0; i < save.best.Length; i++) save.best[i] ??= new BestTicket();
            save.settings ??= new Settings();
            if (!save.hasRun) save.run = null;
            if (save.run != null && (save.run.history == null || (!save.run.endless && (save.run.car < 0 || save.run.car >= Train.DemoCars)))) save.run = null;
        }

        void WriteSave()
        {
            save.hasRun = save.run != null;
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(save)); }
            catch (System.Exception ex) { Debug.LogWarning("Rail Tracer: couldn't write the save: " + ex.Message); }
        }

        // The run as of now (called at the start of each player turn, and when a car or walk-through begins).
        void AutoSave()
        {
            save.run = new RunSave
            {
                endless = endless, walk = walking, car = carIndex, carSeed = carSeed, seed = rulesSeed, startHp = startHp,
                turn = c.Round, history = walking ? new List<string>() : c.history.ToList(),
            };
            WriteSave();
        }

        // Fullscreen is always a borderless window at the desktop's own resolution, never exclusive: exclusive mode switches
        // the monitor's display mode, so alt-tab resized every other window and Print Screen broke. The Resolution
        // setting is the windowed size.
        static bool Windowed(Settings s) => s.windowMode >= 0 ? s.windowMode == (int)FullScreenMode.Windowed : Screen.fullScreenMode == FullScreenMode.Windowed;

        void ApplySettings()
        {
            var s = save.settings;
            killCamSlowMo = s.killCamSlowMo;
            AudioListener.volume = s.masterVolume;
            if (s.windowMode == (int)FullScreenMode.ExclusiveFullScreen || s.windowMode == (int)FullScreenMode.MaximizedWindow) s.windowMode = (int)FullScreenMode.FullScreenWindow;
            if (Windowed(s))
            {
                if (s.width > 0 && s.height > 0) Screen.SetResolution(s.width, s.height, FullScreenMode.Windowed);
                else Screen.fullScreenMode = FullScreenMode.Windowed;
            }
            else if (s.windowMode >= 0 || Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
        }

        // ---- Cars ----

        CarDef CurrentDef() => walking ? Train.WalkThrough() : endless ? Train.Endless(carIndex, carSeed) : Train.Demo(carIndex);

        string CarLabel() => walking ? "Between cars" : endless ? $"Endless · car {carIndex}" : $"Car {carIndex + 1} of {Train.DemoCars}";

        // A car from scratch: legend 0, cooldowns clear, lamps warm, `hp` health.
        void StartCar(bool endlessRun, int index, int hp, bool newLayout = true)
        {
            endless = endlessRun;
            walking = false;
            demoOver = thanksUp = false;
            carIndex = index;
            if (endless && newLayout) carSeed = Random.Range(1, int.MaxValue);
            car = CurrentDef();
            rulesSeed = seed != 0 ? seed : Random.Range(1, int.MaxValue);
            startHp = Mathf.Clamp(hp, 1, MaxHpCarry);
            replay = null;
            StopAllCoroutines();
            hopShots.Clear();
            if (hud != null) hud.shownHp = -1;
            Build();
            AutoSave();
        }

        // R: the current car again from its start (the health you came in with).
        void RestartCar()
        {
            if (!walking) StartCar(endless, carIndex, startHp, newLayout: false); // an endless car keeps its layout
        }

        // The run in the save, rebuilt exactly as of the start of its turn.
        void ResumeSave()
        {
            var r = save.run;
            endless = r.endless;
            walking = r.walk;
            carIndex = r.car;
            carSeed = r.carSeed;
            car = CurrentDef();
            rulesSeed = r.seed;
            startHp = r.startHp;
            replay = r.history;
            Build();
        }

        // Car 1 with nobody in it: the start screen's backdrop when there's no save.
        void BuildEmptyBackdrop()
        {
            endless = walking = false;
            carIndex = 0;
            var first = Train.Demo(0);
            car = new CarDef { name = first.name, blurb = first.blurb, kind = first.kind, map = first.map, spawns = new int[0][], patrols = new Dictionary<int, Cell[]>() };
            rulesSeed = 1;
            startHp = MaxHpCarry;
            Build();
        }

        bool BackdropIsEmpty => save.run == null;

        // Options > Reset data, confirmed: a fresh save (no run, nothing cleared, no best tickets, default settings), the
        // tutorial tips forgotten, and the start screen over an empty car 1 again.
        void ResetData()
        {
            save = new SaveData();
            WriteSave();
            Tutorial.ResetAll();
            ApplySettings();
            BuildEmptyBackdrop();
            BuildMenuTrain();
        }

        // The win card is up: record the ticket, mark the car cleared.
        void RecordClear()
        {
            if (endless || walking) return;
            var t = new BestTicket
            {
                set = true, killed = c.enemies.Count(e => e.state == EnemyState.Dead),
                broken = c.enemies.Count(e => e.state == EnemyState.Broken || e.state == EnemyState.Fled), turns = c.Round,
            };
            save.cleared[carIndex] = true;
            if (t.Beats(save.best[carIndex])) save.best[carIndex] = t;
            WriteSave();
        }

        static string Ticket(BestTicket b) => b == null || !b.set ? "No ticket yet" : $"{b.killed} killed, {b.broken} broken, {b.turns} turns";

        // Off the cleared car: walk out of the far door, then the walk-through car (or the demo's end).
        IEnumerator ExitCar()
        {
            busy = true;
            // Triumphant: on your feet first, then out of the far door at the normal pace (a confident walk clip later).
            if (c.crouched)
            {
                c.crouched = false; // the car is won, so the rules take no more moves: just stand
                for (float time = 0f; time < standUpTime; time += Time.deltaTime) yield return null;
            }
            var door = new Cell(c.W - 1, c.H / 2);
            var path = PathOut(c.player, door);
            hopRunning = exiting = true; // the view walks you, not the rules
            var points = path.Select(World).ToList();
            points.Add(World(door) + Vector3.right * 1.6f); // through the door
            foreach (var p in points)
            {
                while ((playerShown - p).sqrMagnitude > 1e-4f)
                {
                    var before = playerShown;
                    playerShown = Vector3.MoveTowards(playerShown, p, WalkOutSpeed * Time.deltaTime);
                    hopMoving = true;
                    var step = playerShown - before;
                    if (step.sqrMagnitude > 1e-6f) playerFacing = step.normalized;
                    camTarget = playerShown;
                    yield return null;
                }
            }
            hopMoving = false;
            hopRunning = exiting = false;
            busy = false;
            // After the last car: on into the quiet carriage all the same (no next car to board), then the thank-you card.
            bool last = !endless && carIndex == Train.DemoCars - 1;
            if (last)
            {
                save.run = null; // the demo is done: nothing to continue
                WriteSave();
                demoOver = true;
            }
            int hp = c.hp;
            walking = true;
            carIndex++;
            car = Train.WalkThrough();
            rulesSeed = Random.Range(1, int.MaxValue);
            startHp = hp;
            Build();
            if (demoOver) { StartCoroutine(ThanksCard()); yield break; }
            AutoSave();
        }

        // The end of the demo, in the quiet carriage: a card like the tutorial's; any key or click, then the choices.
        bool demoOver, thanksUp;
        public float thanksDelay = 1.2f;
        IEnumerator ThanksCard()
        {
            for (float time = 0f; time < thanksDelay; time += Time.deltaTime) yield return null;
            hud.ShowTutorial("Thanks for playing", null,
                "Thanks for playing the demo! If you enjoyed it, please leave a review and join our Discord.\n\nTry the Endless mode for more fun!",
                "Click or press any key");
            hud.SetTutorialTargets(new List<Rect>());
            thanksUp = true;
        }

        // True while the thank-you card is up (it takes the input).
        bool ThanksStep(Keyboard kb, Mouse mouse)
        {
            if (!thanksUp) return false;
            bool any = (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame));
            if (any)
            {
                thanksUp = false;
                hud.HideTutorial();
                OpenChoice("done");
            }
            return true;
        }

        // The shortest walk from `a` to `b` round seats, luggage and anyone still standing (bodies are stepped over).
        List<Cell> PathOut(Cell a, Cell b)
        {
            var parent = new Dictionary<Cell, Cell>();
            var q = new Queue<Cell>();
            q.Enqueue(a);
            parent[a] = a;
            while (q.Count > 0)
            {
                var at = q.Dequeue();
                if (at == b) break;
                foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                {
                    var n = new Cell(at.x + d.x, at.y + d.y);
                    if (!c.InBounds(n) || c.Blocked(n) || parent.ContainsKey(n) || (c.EnemyAt(n) != null && n != b)) continue;
                    parent[n] = at;
                    q.Enqueue(n);
                }
            }
            var path = new List<Cell>();
            if (!parent.ContainsKey(b)) return path;
            for (var at = b; at != a; at = parent[at]) path.Add(at);
            path.Reverse();
            return path;
        }

        // The walk-through car: the glowing tile by the far door asks to board the next car.
        Cell GlowTile => new Cell(c.W - 1, c.H / 2);

        void BuildGlow()
        {
            if (!walking) return;
            glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(glow.GetComponent<Collider>());
            glow.name = "Board Tile";
            glow.transform.SetParent(root, false);
            glow.transform.localPosition = World(GlowTile) + Vector3.up * 0.035f;
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localScale = Vector3.one * 0.9f;
            glow.GetComponent<Renderer>().sharedMaterial = Transparent(Fill(Bone, 0.5f));
            var light = new GameObject("Board Light").AddComponent<Light>();
            light.transform.SetParent(glow.transform, false);
            light.transform.localPosition = new Vector3(0f, 0f, -0.6f); // the quad is turned flat: its -Z is up
            light.type = LightType.Point;
            light.color = new Color(1f, 0.93f, 0.8f);
            light.range = 2.5f;
            light.intensity = 2f;
            boardAsked = false;
        }

        void WalkStep()
        {
            if (demoOver && glow) glow.SetActive(false); // nothing left to board
            if (!walking || glow == null || demoOver) return;
            float pulse = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f);
            glow.GetComponent<Renderer>().sharedMaterial.color = Fill(Bone, pulse);
            bool on = c.player == GlowTile && playerRoute.Count == 0 && (playerShown - World(GlowTile)).sqrMagnitude < 0.01f;
            if (!on) boardAsked = false;
            else if (!boardAsked && choice == null && !busy) { boardAsked = true; OpenChoice("board"); }
        }

        void BoardNext()
        {
            int hp = Mathf.Min(MaxHpCarry, c.hp + MaxHpCarry / 2); // half your health back
            StartCar(endless, carIndex, hp);
        }

        // ---- The centre panel: pause, board the next car, demo complete ----

        void OpenChoice(string which)
        {
            choice = which;
            hud.ShowPause(true, which == "board" ? NextName() : which == "done" ? "Demo complete" : "Paused", false); // no close X: each panel has its own way out (Resume, Not yet)
        }

        void CloseChoice()
        {
            choice = null;
            hud.ShowPause(false);
        }

        string NextName() => endless ? $"Endless car {carIndex}" : $"Car {carIndex + 1}: {Train.Demo(carIndex).name}";

        string[] ChoiceItems() => choice == "board" ? new[] { "Board", "Not yet" }
            : choice == "done" ? new[] { "Restart from car 1", "Try Endless mode", "Main menu" }
            : new[] { "Resume", $"Tutorial tips: {(tut.Skipped ? "Off" : "On")}", $"Kill cam: {(KillCamOn ? "On" : "Off")}", "Restart car", "Main menu" };

        // True while the panel is open (it takes the input).
        bool ChoiceStep(Keyboard kb, Mouse mouse)
        {
            if (choice == null) return false;
            var at = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            int hit = hud.RefreshPause(ChoiceItems(), at);
            bool x = mouse != null && hud.RefreshButtons(at, mouse.leftButton.isPressed, mouse.leftButton.wasPressedThisFrame) == "closepause";
            bool esc = x || (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (esc && choice != "done") { CloseChoice(); return true; }
            if (kb != null && choice == "board" && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) hit = 0;
            else if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return true;
            switch (choice)
            {
                case "board":
                    if (hit == 0) { CloseChoice(); BoardNext(); }
                    else if (hit == 1) CloseChoice();
                    break;
                case "done":
                    if (hit == 0) { CloseChoice(); StartCar(false, 0, MaxHpCarry); }
                    else if (hit == 1) { CloseChoice(); StartCar(true, 1, MaxHpCarry); }
                    else if (hit == 2) { CloseChoice(); OpenMenu(); }
                    break;
                default:
                    if (hit == 0) CloseChoice();
                    else if (hit == 1) tut.SetTips(tut.Skipped);
                    else if (hit == 2) { save.settings.killCam = !save.settings.killCam; WriteSave(); }
                    else if (hit == 3) { CloseChoice(); RestartCar(); }
                    else if (hit == 4) { CloseChoice(); OpenMenu(); }
                    break;
            }
            return true;
        }

        // ---- Menus: over the real train ----

        const float Pitch = 30f;     // one car to the next along the menu train (26 tiles, end caps and the gap)
        GameObject menuTrain;
        Vector3 menuCamPos; Quaternion menuCamRot;
        readonly List<(Transform car, float x)> menuCars = new List<(Transform, float)>();
        readonly List<List<Light>> statusLamps = new List<List<Light>>();
        readonly List<Light> settingLamps = new List<Light>();

        // Which demo slot the live carriage sits in (the menu train is laid out round it); -1 = none of them.
        int LiveSlot => walking || endless ? -1 : carIndex;
        float SlotX(int k) => (k - LiveSlot) * Pitch;
        int NextDemo => System.Array.IndexOf(save.cleared, false) is int i && i >= 0 ? i : Train.DemoCars;

        // The five demo cars side by side, empty (no people, no props), the live car's slot left to the live car.
        // Lit by progress: red lamps cleared, white the next to play, dark the cars ahead.
        void BuildMenuTrain()
        {
            if (menuTrain) Destroy(menuTrain);
            menuCars.Clear();
            statusLamps.Clear();
            settingLamps.Clear();
            menuTrain = new GameObject("Menu Train");
            var kit = useTrainKit ? Resources.Load<TrainKit>("Kit/TrainKit") : null;
            for (int k = 0; k < Train.DemoCars; k++)
            {
                var t = new GameObject("Car " + (k + 1)).transform;
                t.SetParent(menuTrain.transform, false);
                t.localPosition = new Vector3(SlotX(k), 0f, 0f);
                menuCars.Add((t, SlotX(k)));
                var lit = new List<Light>();
                statusLamps.Add(lit);
                if (k != LiveSlot && kit)
                {
                    var def = Train.Demo(k);
                    BuildKitCarriage(kit, new Carriage(def.map, tuning, 1), def.map, def.kind, t, false);
                }
                bool cleared = save.cleared[k], next = k == NextDemo;
                if (!cleared && !next) continue;
                foreach (float x in new[] { 4f, 12.5f, 21f })
                {
                    var l = new GameObject("Menu Lamp").AddComponent<Light>();
                    l.transform.SetParent(t, false);
                    l.transform.localPosition = new Vector3(x, 2.1f, -2f);
                    l.type = LightType.Point;
                    l.color = cleared ? new Color(0.9f, 0.12f, 0.08f) : new Color(1f, 0.95f, 0.85f);
                    l.intensity = 2.5f;
                    l.range = 9f;
                    l.shadows = LightShadows.None;
                    lit.Add(l);
                }
            }
            // The Options car: a lamp per setting, down the car in menu order (SyncSettingLamps).
            for (int i = 0; i < 8; i++)
            {
                var l = new GameObject("Setting Lamp " + (i + 1)).AddComponent<Light>();
                l.transform.SetParent(menuTrain.transform, false);
                l.transform.localPosition = new Vector3(OptionsX + 2f + i * 3.2f, 2.1f, -2f);
                l.type = LightType.Point;
                l.color = new Color(1f, 0.9f, 0.75f);
                l.range = 7f;
                l.shadows = LightShadows.None;
                settingLamps.Add(l);
            }
        }

        // Where the Options screen looks: the car after the live one (before it, from the last).
        float OptionsX => (LiveSlot >= 0 ? SlotX(LiveSlot) : 0f) + (LiveSlot == Train.DemoCars - 1 ? -Pitch : Pitch);

        // On the Options screens the car's lamps are the settings: off when a setting is off, a slider's value as
        // the brightness (the volumes, kill cam chance and slow motion, enemy speed). Its progress lamps go out.
        void SyncSettingLamps(bool showing)
        {
            var s = save.settings;
            float[] level = { s.masterVolume, s.musicVolume, s.killCam ? 1f : 0f, s.killCam ? s.killCamChance : 0f, s.killCamSlowMo, 0.35f + 0.325f * Mathf.Clamp(s.enemySpeed, 0, 2), 1f, 1f }; // master, music, kill cam, chance, slow motion, enemy speed, window mode, resolution
            for (int i = 0; i < settingLamps.Count; i++) settingLamps[i].intensity = showing ? 2.8f * level[i] : 0f;
            for (int k = 0; k < menuCars.Count; k++)
            {
                bool here = Mathf.Abs(menuCars[k].x - OptionsX) < 1f;
                foreach (var l in statusLamps[k]) l.enabled = !(showing && here);
            }
        }

        void OpenMenu()
        {
            if (busy) return;
            CloseChoice();
            phase = Phase.Menu;
            screen = MenuScreen.Start;
            hud.SetVisible(false);
            hud.ShowMenu(true);
            BuildMenuTrain();
            menuCamPos = cam.transform.position;
            menuCamRot = cam.transform.rotation;
        }

        // The camera for the current screen: Start over the saved car, Options over the next car along, Journey
        // pulled out side-on to the whole train. It glides between them (and drifts slowly).
        (Vector3 pos, Quaternion rot) MenuPose()
        {
            float drift = Mathf.Sin(Time.unscaledTime * 0.08f) * 1.5f;
            if (screen == MenuScreen.Journey)
            {
                // Side-on, closed in on the picked car (its neighbours still in view, to pick them).
                float mid = (journeyPick >= 0 ? SlotX(journeyPick) : (SlotX(0) + SlotX(Train.DemoCars - 1)) / 2f) + (Map[0].Length - 1) / 2f;
                float back = journeyPick >= 0 ? journeyZoom : 95f;
                var look = new Vector3(mid, 0.6f, -2f);
                var pos = new Vector3(mid + drift * back / 95f, 7f * back / 95f + 1f, -2f - back);
                return (pos, Quaternion.LookRotation(look - pos));
            }
            int slot = LiveSlot >= 0 ? LiveSlot : -1;
            float x = slot >= 0 ? SlotX(slot) : 0f; // the live car (an endless or walk-through car sits at 0)
            if (screen == MenuScreen.Options || screen == MenuScreen.Controls || screen == MenuScreen.ResetData) x = OptionsX;
            var focusAt = new Vector3(x + (Map[0].Length - 1) / 2f + drift, 0f, -2f);
            var rot = Quaternion.Euler(50f, 0f, 0f);
            return (focusAt - rot * Vector3.forward * 24f, rot);
        }

        void MenuCamera()
        {
            var (pos, rot) = MenuPose();
            float k = 1f - Mathf.Exp(-2.2f * Time.unscaledDeltaTime);
            menuCamPos = Vector3.Lerp(menuCamPos, pos, k);
            menuCamRot = Quaternion.Slerp(menuCamRot, rot, k);
            cam.transform.SetPositionAndRotation(menuCamPos, menuCamRot);
        }

        // From the menu straight down into the live car, then play.
        IEnumerator Enter()
        {
            phase = Phase.Entering;
            hud.ShowMenu(false);
            var rot = Quaternion.Euler(55f, 0f, 0f);
            focus = playerShown;
            camDistance = 12f;
            camTarget = null;
            Vector3 p0 = cam.transform.position, p1 = root.TransformPoint(focus) - rot * Vector3.forward * camDistance;
            var r0 = cam.transform.rotation;
            for (float t = 0f; t < 1.1f; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.1f);
                cam.transform.SetPositionAndRotation(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, rot, k));
                yield return null;
            }
            cam.transform.rotation = rot;
            if (menuTrain) Destroy(menuTrain);
            menuCars.Clear();
            phase = Phase.Play;
            hud.SetVisible(true);
            if (c.Round <= 1 && c.history.Count == 0)
            {
                if (walking && !demoOver) hud.ShowCard("Between cars", "A quiet carriage.", "Walk on to the far door.", null, 2.5f, true);
                else hud.ShowCard(CarLabel(), car.name, "Time to collect.", null, 3f, true);
            }
        }

        // Start into a car from the menu: rebuilt fresh (or kept, to continue), then the camera dives in. `slot`: the
        // menu train car being played (Journey). The new car is built where the live one was, so the menu train is
        // laid out again round it and the camera moved by the same amount: nothing jumps, and the dive goes down
        // into the car that was picked.
        void Dive(System.Action setUp, int slot = -1)
        {
            float from = slot >= 0 ? SlotX(slot) : 0f; // the live car always sits at 0
            setUp?.Invoke();
            if (Mathf.Abs(from) > 0.01f)
            {
                BuildMenuTrain();
                cam.transform.position -= new Vector3(from, 0f, 0f);
                menuCamPos -= new Vector3(from, 0f, 0f);
            }
            StartCoroutine(Enter());
        }

        string ContinueText()
        {
            var r = save.run;
            if (r.walk) return r.endless ? $"Continue · before endless car {r.car}" : $"Continue · before car {r.car + 1}";
            return r.endless ? $"Continue · endless car {r.car}, turn {r.turn}" : $"Continue · car {r.car + 1}, turn {r.turn}";
        }

        void MenuStep(Keyboard kb, Mouse mouse)
        {
            MenuCamera();
            SyncSettingLamps(screen == MenuScreen.Options || screen == MenuScreen.Controls || screen == MenuScreen.ResetData);
            var at = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool back = kb != null && kb.escapeKey.wasPressedThisFrame;
            bool held = mouse != null && mouse.leftButton.isPressed;
            bool down = mouse != null && mouse.rightButton.wasPressedThisFrame; // right-click: a volume down
            switch (screen)
            {
                case MenuScreen.Start:
                {
                    var items = new[] { BackdropIsEmpty ? "Board the train" : ContinueText(), "Journey", "Endless", "Options", "Quit" };
                    int hit = hud.RefreshMenu(true, null, items, null, null, at);
                    if (!click) break;
                    if (hit == 0) Dive(BackdropIsEmpty ? () => StartCar(false, 0, MaxHpCarry) : (System.Action)null);
                    else if (hit == 1) { screen = MenuScreen.Journey; journeyPick = Mathf.Min(NextDemo, Train.DemoCars - 1); }
                    else if (hit == 2) Dive(() => StartCar(true, 1, MaxHpCarry));
                    else if (hit == 3) screen = MenuScreen.Options;
                    else if (hit == 4) Quit();
                    break;
                }
                case MenuScreen.Journey:
                {
                    int next = NextDemo;
                    var states = Enumerable.Range(0, Train.DemoCars).Select(k => save.cleared[k] ? 2 : k == next ? 1 : 0).ToArray();
                    var names = Enumerable.Range(0, Train.DemoCars).Select(k => $"{k + 1}  {Train.Demo(k).name}").ToArray();
                    var screenAt = Enumerable.Range(0, Train.DemoCars).Select(k =>
                    {
                        var p = cam.WorldToScreenPoint(new Vector3(SlotX(k) + 12.5f, -0.6f, -4.6f));
                        return new Vector2(p.x, p.y);
                    }).ToArray();
                    int over = hud.RefreshJourney(screenAt, names, states, journeyPick, at);
                    bool again = click && over >= 0 && over == journeyPick; // clicking the picked car plays it, like a double click
                    if (click && over >= 0) journeyPick = over;
                    var pick = Train.Demo(journeyPick);
                    bool cleared = save.cleared[journeyPick], isNext = journeyPick == next;
                    bool resumable = isNext && save.run != null && !save.run.endless && save.run.car == journeyPick;
                    string action = cleared ? "Replay" : isNext ? (resumable ? "Continue" : "Board") : "Not yet";
                    var info = $"<b>{journeyPick + 1}. {pick.name}</b>\n{pick.blurb}\n\nBest ticket: {Ticket(save.best[journeyPick])}";
                    int hit = hud.RefreshMenu(false, "Journey", new[] { action, "Back" }, new[] { cleared || isNext, true }, info, at, held);
                    if (back || (click && (hit == 1 || hit == -2))) { screen = MenuScreen.Start; break; }
                    if ((again || (click && hit == 0)) && (cleared || isNext))
                    {
                        int k = journeyPick;
                        Dive(resumable ? (System.Action)null : () => StartCar(false, k, MaxHpCarry), k);
                    }
                    break;
                }
                case MenuScreen.Options:
                {
                    var s = save.settings;
                    bool windowed = Windowed(s);
                    string modeName = windowed ? "Windowed" : "Fullscreen";
                    int w = s.width > 0 ? s.width : Screen.width, h = s.height > 0 ? s.height : Screen.height;
                    if (!windowed) (w, h) = (Display.main.systemWidth, Display.main.systemHeight); // fullscreen: the desktop's own
                    var items = new[]
                    {
                        $"Master volume: {Mathf.RoundToInt(100 * s.masterVolume)}%", $"Music volume: {Mathf.RoundToInt(100 * s.musicVolume)}%",
                        $"Kill cam: {(s.killCam ? "On" : "Off")}", $"Kill cam chance: {Mathf.RoundToInt(100 * s.killCamChance)}%",
                        $"Kill cam slow motion: {s.killCamSlowMo:0.##}x", $"Enemy turn speed: {EnemySpeeds[Mathf.Clamp(s.enemySpeed, 0, 2)]}",
                        $"Window: {modeName}", windowed ? $"Resolution: {w} x {h}" : $"Resolution: {w} x {h} (screen)", "Controls", "Back",
                    };
                    int hit = hud.RefreshMenu(false, "Options", items, null, "Volumes: click to turn up, right-click to turn down.", at, held, -1, "Reset data");
                    if (click && hit == -3) { screen = MenuScreen.ResetData; break; }
                    if (back || (click && (hit == 9 || hit == -2))) { screen = MenuScreen.Start; break; }
                    if ((click || down) && (hit == 0 || hit == 1)) // 10% steps, wrapping round
                    {
                        float Step(float v) => Mathf.Repeat(Mathf.Round(v * 10f) + (down ? -1f : 1f), 11f) / 10f;
                        if (hit == 0) AudioListener.volume = s.masterVolume = Step(s.masterVolume);
                        else s.musicVolume = Step(s.musicVolume);
                        WriteSave();
                        break;
                    }
                    if (!click) break;
                    if (hit == 2) s.killCam = !s.killCam;
                    else if (hit == 3) s.killCamChance = Chances[(System.Array.IndexOf(Chances, s.killCamChance) + 1) % Chances.Length];
                    else if (hit == 4) s.killCamSlowMo = SlowMos[(System.Array.IndexOf(SlowMos, s.killCamSlowMo) + 1) % SlowMos.Length];
                    else if (hit == 5) s.enemySpeed = (s.enemySpeed + 1) % EnemySpeeds.Length;
                    else if (hit == 6)
                    {
                        s.windowMode = (int)(windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                        if (!windowed && (s.width <= 0 || s.width >= Display.main.systemWidth)) // first window: the biggest that fits
                        {
                            var fit = Screen.resolutions.Where(r => r.width < Display.main.systemWidth && r.height < Display.main.systemHeight)
                                .OrderBy(r => r.width * r.height).LastOrDefault();
                            if (fit.width > 0) (s.width, s.height) = (fit.width, fit.height);
                        }
                    }
                    else if (hit == 7)
                    {
                        if (!windowed) break; // fullscreen always uses the screen's own resolution
                        var sizes = Screen.resolutions.Select(r => (r.width, r.height)).Distinct()
                            .Where(r => r.width < Display.main.systemWidth && r.height < Display.main.systemHeight).ToList(); // a window smaller than the desktop
                        if (sizes.Count > 0)
                        {
                            int i = sizes.IndexOf((w, h));
                            (s.width, s.height) = sizes[(i + 1) % sizes.Count];
                        }
                    }
                    else if (hit == 8) { screen = MenuScreen.Controls; break; }
                    else break;
                    ApplySettings();
                    WriteSave();
                    break;
                }
                case MenuScreen.ResetData:
                {
                    // The warning: everything goes, for good.
                    const string warning = "This deletes all your progress: the saved run, the cars you've cleared, your best tickets, " +
                                           "your settings and the tutorial tips you've seen.\n\nIt can't be undone.";
                    int hit = hud.RefreshMenu(false, "Reset data?", new[] { "Reset everything", "Cancel" }, null, warning, at, held, 0);
                    if (back || (click && (hit == 1 || hit == -2))) { screen = MenuScreen.Options; break; }
                    if (click && hit == 0) { ResetData(); screen = MenuScreen.Start; }
                    break;
                }
                case MenuScreen.Controls:
                {
                    var info = string.Join("\n", SkillHud.ControlsList.Select(k => $"<b>{string.Join(" to ", k.keys)}</b>   {k.what}"));
                    int hit = hud.RefreshMenu(false, "Controls", new[] { "Back" }, null, info, at, held);
                    if (back || (click && (hit == 0 || hit == -2))) screen = MenuScreen.Options;
                    break;
                }
            }
        }

        // Greybox stand-ins for the dining car and baggage hold, until their art arrives: tables with chairs tucked
        // under (cloth white), the bar counter, mail sacks, crates, and the safe's cage of heavy beams with gaps.
        void Greybox(Carriage car, string[] map, Transform parent, TrainKit kit = null, List<Transform> bulbs = null)
        {
            var modelled = kit ? DiningModels(car, map, parent, kit, bulbs) : new HashSet<Cell>();
            Color cloth = new Color(0.86f, 0.83f, 0.76f), wood = new Color(0.3f, 0.19f, 0.11f), sack = new Color(0.55f, 0.48f, 0.34f),
                crate = new Color(0.5f, 0.36f, 0.2f), beam = new Color(0.25f, 0.16f, 0.09f), metal = new Color(0.62f, 0.6f, 0.56f);
            for (int x = 0; x < car.W; x++)
                for (int y = 0; y < car.H; y++)
                {
                    var at = World(new Cell(x, y));
                    if (modelled.Contains(new Cell(x, y))) continue;
                    switch (map[y][x])
                    {
                        case 'T':
                            Box(at + Vector3.up * 0.37f, new Vector3(0.9f, 0.74f, 0.9f), cloth, solid: true, parent: parent);
                            foreach (float side in new[] { -1f, 1f }) // chairs pushed in under the table: they block nothing
                            {
                                Box(at + new Vector3(side * 0.28f, 0.23f, 0f), new Vector3(0.32f, 0.46f, 0.4f), wood, parent: parent);
                                Box(at + new Vector3(side * 0.47f, 0.62f, 0f), new Vector3(0.05f, 0.5f, 0.4f), wood, parent: parent);
                            }
                            break;
                        case 'O':
                            // A round table across its 2x2 block (drawn once, from its top-left tile), chairs at four sides.
                            if (x > 0 && map[y][x - 1] == 'O' || y > 0 && map[y - 1][x] == 'O') break;
                            var mid = at + new Vector3(0.5f, 0f, -0.5f);
                            Box(mid + Vector3.up * 0.37f, new Vector3(1.7f, 0.37f, 1.7f), cloth, PrimitiveType.Cylinder, solid: true, parent: parent);
                            foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                            {
                                Box(mid + d * 0.62f + Vector3.up * 0.23f, new Vector3(0.36f, 0.46f, 0.36f), wood, parent: parent);
                                Box(mid + d * 0.84f + Vector3.up * 0.62f, d.x != 0 ? new Vector3(0.05f, 0.5f, 0.4f) : new Vector3(0.4f, 0.5f, 0.05f), wood, parent: parent);
                            }
                            break;
                        case 'D': // the dining cart: a trolley with two trays
                            Box(at + Vector3.up * 0.3f, new Vector3(0.8f, 0.05f, 0.5f), metal, solid: true, parent: parent);
                            Box(at + Vector3.up * 0.8f, new Vector3(0.8f, 0.05f, 0.5f), metal, solid: true, parent: parent);
                            foreach (float sx in new[] { -0.37f, 0.37f })
                                foreach (float sz in new[] { -0.22f, 0.22f })
                                    Box(at + new Vector3(sx, 0.45f, sz), new Vector3(0.04f, 0.9f, 0.04f), metal, parent: parent);
                            Box(at + Vector3.up * 0.9f, new Vector3(0.5f, 0.15f, 0.3f), cloth, parent: parent);
                            break;
                        case 'B': Box(at + Vector3.up * 0.53f, new Vector3(0.96f, 1.06f, 0.7f), wood, solid: true, parent: parent); break;
                        case 'M': Box(at + Vector3.up * 0.3f, new Vector3(0.75f, 0.6f, 0.7f), sack, solid: true, parent: parent); break;
                        case 'X':
                        case 'x':
                            Box(at + Vector3.up * 0.45f, new Vector3(0.94f, 0.9f, 0.94f), crate, solid: true, parent: parent);
                            Box(at + new Vector3(0.05f, 1.25f, -0.04f), new Vector3(0.75f, 0.7f, 0.75f), crate * 0.85f, solid: true, parent: parent);
                            break;
                        case 'C':
                            // Bars along every side facing out of the cage, a beam across the top.
                            foreach (var d in new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) })
                            {
                                var n = new Cell(x + d.x, y + d.y);
                                if (car.InBounds(n) && map[n.y][n.x] == 'C') continue;
                                var edge = new Vector3(d.x, 0f, -d.y) * 0.46f;
                                var along = new Vector3(-edge.z, 0f, edge.x).normalized;
                                for (int k = -2; k <= 2; k++)
                                    Box(at + edge + along * (k * 0.22f) + Vector3.up * 1.1f, new Vector3(0.07f, 2.2f, 0.07f), beam, solid: true, parent: parent);
                                Box(at + edge + Vector3.up * 2.15f, new Vector3(Mathf.Lerp(0.1f, 0.96f, Mathf.Abs(along.x)), 0.1f, Mathf.Lerp(0.1f, 0.96f, Mathf.Abs(along.z))), beam, parent: parent);
                            }
                            break;
                    }
                }
        }

        // Furniture from the kit, each fitted to its tiles. Dining car (Train Assets/Dining): tables of 1, 2
        // (straight) or 3 (L) 'T' tiles, the 2x2 round table, the cart, the L bar (4 + 1). Baggage hold (Train
        // Assets/Baggage): a crate stack per crate tile and a sack pile per mail sack tile (variant and quarter turn
        // from the tile), the safe's cage on a 3 x 2 block against a wall. Returns the tiles they cover; any other
        // shape keeps the greybox.
        HashSet<Cell> DiningModels(Carriage car, string[] map, Transform parent, TrainKit kit, List<Transform> bulbs)
        {
            var done = new HashSet<Cell>();
            var seen = new HashSet<Cell>();
            for (int y = 0; y < car.H; y++)
                for (int x = 0; x < car.W; x++)
                {
                    char ch = map[y][x];
                    var start = new Cell(x, y);
                    if ("TOBDXxMC".IndexOf(ch) < 0 || seen.Contains(start)) continue;
                    bool single = "XxM".IndexOf(ch) >= 0; // one model per tile
                    var group = single ? new HashSet<Cell> { start } : TileGroup(map, start, ch);
                    seen.UnionWith(group);
                    string model = null;
                    var pos = World(start);
                    var rot = Quaternion.identity;
                    bool mirror = false;
                    int hash = (x * 73856093) ^ (y * 19349663);
                    if (single)
                    {
                        model = ch == 'M' ? (hash & 1) == 0 ? "Mail_Sacks_A" : "Mail_Sacks_B" : "Crate_Stack_" + "ABC"[(hash & 0x7fffffff) % 3];
                        rot = Quaternion.Euler(0f, 90f * ((hash >> 3) & 3), 0f);
                    }
                    else if (ch == 'C')
                    {
                        // The safe's cage: a full 3 x 2 block along a wall row, its back (local +Z) to that wall.
                        int x0 = group.Min(g => g.x), x1 = group.Max(g => g.x), y0 = group.Min(g => g.y), y1 = group.Max(g => g.y);
                        bool atWall = y0 == 0 || y1 == car.H - 1;
                        if (group.Count == 6 && x1 - x0 == 2 && y1 - y0 == 1 && atWall)
                        {
                            model = "Safe_Cage";
                            pos = group.Aggregate(Vector3.zero, (s, g) => s + World(g)) / 6f;
                            rot = y0 == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                        }
                    }
                    else if (ch == 'D' && group.Count == 1) model = "Dining_Cart"; // long side along the car
                    else if (ch == 'O' && group.Count == 4)
                    {
                        model = "Dining_Table_Round";
                        pos = group.Aggregate(Vector3.zero, (s, g) => s + World(g)) / 4f;
                    }
                    else if (ch == 'T' && group.Count == 1) model = "Dining_Table_1";
                    else if (ch == 'T' && group.Count == 2)
                    {
                        model = "Dining_Table_2";
                        var a = group.OrderBy(g => g.x + g.y).First();
                        pos = World(a);
                        var d1 = World(group.First(g => g != a)) - pos;
                        rot = Quaternion.LookRotation(Vector3.Cross(d1, Vector3.up)); // local +X along the table
                    }
                    else if ((ch == 'T' && group.Count == 3) || (ch == 'B' && group.Count == 5))
                    {
                        // An L: the corner has neighbours both ways; local +X runs along the long arm, local -Z the other
                        // (the bar's short leg: local +Z, back to the wall).
                        var dirs = new[] { new Cell(1, 0), new Cell(-1, 0), new Cell(0, 1), new Cell(0, -1) };
                        int Arm(Cell from, Cell d) { int n = 0; while (group.Contains(new Cell(from.x + d.x * (n + 1), from.y + d.y * (n + 1)))) n++; return n; }
                        foreach (var corner in group)
                        {
                            var arms = dirs.Where(d => Arm(corner, d) > 0).OrderByDescending(d => Arm(corner, d)).ToList();
                            if (arms.Count != 2 || arms[0].x == -arms[1].x && arms[0].y == -arms[1].y) continue;
                            int a1 = Arm(corner, arms[0]), a2 = Arm(corner, arms[1]);
                            if (a1 + a2 + 1 != group.Count || (ch == 'B' ? a1 != 3 || a2 != 1 : a1 != 1 || a2 != 1)) break;
                            model = ch == 'B' ? "Bar_L" : "Dining_Table_L";
                            pos = World(corner);
                            Vector3 Dir(Cell d) => new Vector3(d.x, 0f, -d.y);
                            Vector3 d1 = Dir(arms[0]), d2 = Dir(arms[1]);
                            rot = Quaternion.LookRotation(ch == 'B' ? d2 : -d2);
                            if (Vector3.Dot(rot * Vector3.right, d1) < 0.5f)
                            {
                                if (ch == 'B') mirror = true; // the bar isn't symmetric: its mirror image
                                else rot = Quaternion.LookRotation(-d1); // the L table is: swap its arms
                            }
                            break;
                        }
                    }
                    var prefab = model != null ? kit.Get(model) : null;
                    if (!prefab) continue;
                    var go = Instantiate(prefab, parent);
                    go.transform.localPosition = pos;
                    go.transform.localRotation = rot;
                    if (mirror) go.transform.localScale = new Vector3(-1f, 1f, 1f);
                    // A table's lamp bulb, in the model's own space (the FBX's anchor empties don't get the meshes' axis
                    // conversion, so they're not used): 1.01 m up in the lamp's shade, over the pivot tile (the 2-tile
                    // table: between its tiles).
                    if (bulbs != null && model.StartsWith("Dining_Table"))
                    {
                        var bulb = new GameObject("Lamp Bulb").transform;
                        bulb.SetParent(go.transform, false);
                        bulb.localPosition = new Vector3(model == "Dining_Table_2" ? 0.5f : 0f, tableLampHeight, 0f);
                        bulbs.Add(bulb);
                    }
                    done.UnionWith(group);
                }
            return done;
        }

        // The 4-connected tiles of `ch` around `start`.
        static HashSet<Cell> TileGroup(string[] map, Cell start, char ch)
        {
            var group = new HashSet<Cell> { start };
            var q = new Queue<Cell>(group);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                foreach (var n in new[] { new Cell(p.x + 1, p.y), new Cell(p.x - 1, p.y), new Cell(p.x, p.y + 1), new Cell(p.x, p.y - 1) })
                    if (n.y >= 0 && n.y < map.Length && n.x >= 0 && n.x < map[0].Length && map[n.y][n.x] == ch && group.Add(n)) q.Enqueue(n);
            }
            return group;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
