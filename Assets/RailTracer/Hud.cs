using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailTracer
{
    // Leans a UI graphic like the mockup's skewX(-10deg): the top edge shifts right, about the rect's centre line.
    public class UISkew : BaseMeshEffect
    {
        public float degrees = 10f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var r = ((RectTransform)transform).rect;
            float k = Mathf.Tan(degrees * Mathf.Deg2Rad);
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.position.x += (v.position.y - r.center.y) * k;
                vh.SetUIVertex(v, i);
            }
        }
    }

    // A bar's wedge: `start` of the full height at the left end, full height at the right, bottom flat.
    public class UITaper : BaseMeshEffect
    {
        public float start = 0.28f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var r = ((RectTransform)transform).rect;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.position.y = r.yMin + (v.position.y - r.yMin) * Mathf.Lerp(start, 1f, (v.position.x - r.xMin) / r.width);
                vh.SetUIVertex(v, i);
            }
        }
    }

    // A custom shape drawn through an Image: the effect replaces the Image's quad with its own triangles. (Custom
    // Graphic subclasses drew nothing in this project; Images with mesh effects, like UISkew, do.) Add it before
    // UISkew so the shape is drawn, then slanted.
    [RequireComponent(typeof(Image))]
    public abstract class UIShape : BaseMeshEffect
    {
        public Color color { get => graphic.color; set => graphic.color = value; }
        public bool raycastTarget { set => graphic.raycastTarget = value; }
        public bool shown { set => graphic.enabled = value; }
        public RectTransform rectTransform => (RectTransform)transform;
        public void Redraw() => graphic.SetVerticesDirty();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            vh.Clear();
            Build(vh, rectTransform.rect, graphic.color);
        }

        protected abstract void Build(VertexHelper vh, Rect r, Color32 col);

        protected static void Quad(VertexHelper vh, Color32 col, float x0, float y0, float x1, float y1)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = new Vector3(x0, y0); vh.AddVert(v);
            v.position = new Vector3(x0, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y0); vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    // A dashed border round the rect (the "no target" skill box; one long dash per side makes a solid frame).
    public class DashedFrame : UIShape
    {
        public float thickness = 3f, dash = 8f, gap = 5f;

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            for (float s = 0; s < r.width; s += dash + gap)
            {
                float e = Mathf.Min(s + dash, r.width);
                Quad(vh, col, r.xMin + s, r.yMin, r.xMin + e, r.yMin + thickness);
                Quad(vh, col, r.xMin + s, r.yMax - thickness, r.xMin + e, r.yMax);
            }
            for (float s = 0; s < r.height; s += dash + gap)
            {
                float e = Mathf.Min(s + dash, r.height);
                Quad(vh, col, r.xMin, r.yMin + s, r.xMin + thickness, r.yMin + e);
                Quad(vh, col, r.xMax - thickness, r.yMin + s, r.xMax, r.yMin + e);
            }
        }
    }

    // The whole HUD (the Rail Tracer HUD mockup, rebuilt in Unity UI at 1280x720). Bottom-left, bottom up: health,
    // legend bar, skill boxes, moves line, tooltip. Top centre: the mode banner. Bottom right: the log and the
    // controls hint. Over the carriage: enemy chips. Over everything: the controls panel and the intro / end card.
    // Everything reads the skill table and the live carriage.
    public partial class SkillHud
    {
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
        static readonly Color Black = Hex("0B0809"), Bone = Hex("F2EFEA"), Blood = Hex("C8102E"), Bright = Hex("E0202F"),
            LockedBox = Hex("3A3536"), LockedText = Hex("B4AEAF"), Grey = Hex("8A8A8A"), Burgundy = Hex("6E1A26"), NotchOff = Hex("6A6264");
        const float BarWidth = 774f, BarHeight = 26f, Taper = 0.28f, Left = 48f;
        // Bottom up from 36: health row (24), 6, notch labels (18), 4, legend bar (26), 14, boxes (88), 10, moves line, 14, tooltip.
        const float HealthY = 36f, LabelsY = HealthY + 24f + 6f, BarY = LabelsY + 18f + 4f, BoxY = BarY + BarHeight + 14f,
            MovesY = BoxY + 88f + 10f, TipY = MovesY + 16f + 14f;

        class Box { public RectTransform rt; public Image border, bg, fill, cdTint, cdDark; public DashedFrame dashed; public TMP_Text key, name, wait; }

        readonly Box[] boxes = new Box[8];
        SkillDef[] skills;
        int maxLegend;
        int[] notchAt;
        Image legendFill, hpFill;
        Image[] notches, movePips;
        TMP_Text[] notchLabels;
        TMP_Text legendNum, hpNum, hpWord, log;
        RectTransform bar, tip;
        Image tipBorder;
        TMP_Text tipName, tipStatus, tipDesc, tipUp;
        Image actionPip, stanceIcon, sightIcon;
        TMP_Text stanceWord, sightWord;
        GameObject undo, undoKey, dragging;
        Image endFill;
        TMP_Text endWord;
        // The player's stack above the health row (legend bar, skill boxes, moves line, tooltip) is drawn at this
        // scale, shrinking towards its bottom-left corner; the health row keeps its size.
        const float StackScale = 0.8f;
        static readonly Vector2 StackOrigin = new Vector2(40f, LabelsY);

        // Hold Space to end the turn: the key cap fills as it's held. Dim during the enemy turn.
        public void SetEndTurn(float held, bool ready)
        {
            endFill.fillAmount = Mathf.Clamp01(held);
            endWord.color = ready ? (held > 0f ? Bone : Grey) : Alpha(Grey, 0.4f);
        }
        RectTransform banner;
        TMP_Text bannerName, bannerHint;
        GameObject controls;
        CanvasGroup card;
        TMP_Text cardSmall, cardLine1, cardLine2, cardFoot;
        float cardShown, cardAuto, cardFading = -1f;
        bool cardDismissable;
        TMP_FontAsset title, body;
        Sprite white;
        readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        // Enemy chips: a pool, filled each frame. Awareness 0..3 = Unaware, Searching, Startled, Combat; 4 = Broken.
        class Tag
        {
            public RectTransform rt;
            public GameObject label;
            public TMP_Text name, role, words, intent, glyph;
            public Image chipBg, chipBorder, icon, eye, labelBorder, plate;
            public Image[] segs, segEdges;
            public GameObject notYet;
            public RectTransform chip;
        }
        readonly List<Tag> tags = new List<Tag>();
        RectTransform tagRoot;
        int[] terror; // 0, Shaken, Panicked, Broken thresholds
        int tagCount;

        Canvas hudCanvas;
        public void SetVisible(bool on) => hudCanvas.enabled = on; // the kill cam hides it

        public bool CardUp => card.gameObject.activeSelf && cardFading < 0f;
        public bool CardVisible => card.gameObject.activeSelf;
        public bool CardDismissable => cardDismissable;
        public bool ControlsOpen => controls.activeSelf;
        public void ToggleControls() => controls.SetActive(!controls.activeSelf);
        public void CloseControls() => controls.SetActive(false);

        // The controls, for the H panel and the Options menu's controls page.
        public static readonly (string[] keys, string what)[] ControlsList =
        {
            (new[] { "Click" }, "Move to a tile; with a kill selected, act on an enemy"),
            (new[] { "1", "8" }, "Select a skill (again or Esc to cancel); 1 grabs or drops a body"),
            (new[] { "Shift" }, "Hold and click: leap"),
            (new[] { "Ctrl" }, "Hold and click: window hop"),
            (new[] { "G" }, "Grab or drop a body"),
            (new[] { "C" }, "Crouch: hidden behind seats, half speed"),
            (new[] { "Space" }, "Hold: end turn"),
            (new[] { "Z" }, "Undo (until you act, are seen or move a body)"),
            (new[] { "V" }, "Hold: every enemy's senses"),
            (new[] { "L" }, "The full log (paused while open)"),
            (new[] { "B" }, "Blood tile outlines (off at first)"),
            (new[] { "O" }, "Open or shut the window under the mouse"),
            (new[] { "R" }, "Restart the car"),
            (new[] { "Esc" }, "Pause; the main menu is in there"),
            (new[] { "WASD" }, "Pan the camera (or right-drag); scroll zooms"),
            (new[] { "H" }, "Close this"),
        };

        public void Build(SkillDef[] table, int maxLegend, int moveRange, int shakenAt, int panickedAt, int brokenAt)
        {
            skills = table;
            this.maxLegend = maxLegend;
            terror = new[] { 0, shakenAt, panickedAt, brokenAt };
            // Anton / Barlow Condensed once imported as TextMeshPro assets (Resources/Fonts), else TMP's default.
            title = Resources.Load<TMP_FontAsset>("Fonts/Anton SDF") ?? TMP_Settings.defaultFontAsset;
            body = Resources.Load<TMP_FontAsset>("Fonts/BarlowCondensed SDF") ?? TMP_Settings.defaultFontAsset;
            white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)); // Filled images need a sprite
            foreach (var s in Resources.LoadAll<Sprite>("UI/Icons")) icons[s.name] = s;

            var canvas = hudCanvas = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvas.transform;
            tagRoot = new GameObject("Enemy Chips", typeof(RectTransform)).GetComponent<RectTransform>(); // first: under the rest of the HUD
            tagRoot.SetParent(root, false);
            Stretch(tagRoot, 0f);

            // ---- Health: a tapered white bar, the number and the word ----
            var hp = hpBar = Rect("Health Bar", root, Vector2.zero, new Vector2(Left, HealthY + 4f), new Vector2(300, 16));
            Img(hp, Hex("2A2224"), 0f).gameObject.AddComponent<UITaper>().start = 0.55f;
            hpFill = Img(hp, Bone, 0f);
            Filled(hpFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            hpFill.gameObject.AddComponent<UITaper>().start = 0.55f;
            hpNum = Text(root, title, 24, TextAlignmentOptions.MidlineLeft, Vector2.zero, new Vector2(60, 24));
            Place(hpNum.rectTransform, Vector2.zero, new Vector2(Left + 310, HealthY));
            hpNum.color = Bone;
            hpWord = Label(root, "Health", 13, LockedText, 3f);
            Place(hpWord.rectTransform, Vector2.zero, new Vector2(Left + 350, HealthY));
            hpWord.rectTransform.sizeDelta = new Vector2(80, 24);
            hpWord.alignment = TextAlignmentOptions.MidlineLeft;

            // ---- Legend bar: black wedge, blood fill from the left, a notch at every unlock and Bloodied point ----
            bar = Rect("Legend Bar", root, Vector2.zero, new Vector2(Left, BarY), new Vector2(BarWidth, BarHeight));
            Img(bar, Black, 0f).gameObject.AddComponent<UITaper>().start = Taper;
            legendFill = Img(bar, Blood, 0f);
            Filled(legendFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            legendFill.gameObject.AddComponent<UITaper>().start = Taper;
            notchAt = skills.SelectMany(s => new[] { s.unlock, s.bloodied }).Where(v => v > 0).Distinct().OrderBy(v => v).ToArray();
            notches = new Image[notchAt.Length];
            notchLabels = new TMP_Text[notchAt.Length];
            for (int i = 0; i < notchAt.Length; i++)
            {
                float u = notchAt[i] / (float)maxLegend;
                var n = Rect("Notch " + notchAt[i], bar, Vector2.zero, new Vector2(u * BarWidth - 2f, 0), new Vector2(3, BarHeight * Mathf.Lerp(Taper, 1f, u)));
                notches[i] = n.gameObject.AddComponent<Image>();
                notches[i].sprite = white;
                var label = notchLabels[i] = Text(root, body, 14, TextAlignmentOptions.Center, Vector2.zero, new Vector2(40, 18));
                Place(label.rectTransform, Vector2.zero, new Vector2(Left + u * BarWidth - 20, LabelsY));
                label.fontStyle = FontStyles.Bold;
                label.text = notchAt[i].ToString();
            }
            // The big number and "LEGEND", just right of the bar's end, level with it.
            var word = legendWord = Label(root, "Legend", 15, Bone, 4f);
            Place(word.rectTransform, Vector2.zero, new Vector2(868, BarY - 4f));
            legendNum = Text(root, title, 32, TextAlignmentOptions.BottomLeft, Vector2.zero, new Vector2(160, 80));
            Place(legendNum.rectTransform, Vector2.zero, new Vector2(868, BarY + 20f));

            // ---- Skill boxes ----
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i] = new Box();
                b.rt = Rect("Skill " + (i + 1), root, Vector2.zero, new Vector2(Left + i * 98, BoxY), new Vector2(88, 88));
                b.border = Img(b.rt, Black, 0f, skew: true); // white while hovered or selected
                b.bg = Img(b.rt, Bone, 3f, skew: true);
                b.fill = Img(b.rt, Bone, 3f, skew: true);
                Filled(b.fill, Image.FillMethod.Vertical, (int)Image.OriginVertical.Bottom);
                var dash = new GameObject("Dashed", typeof(RectTransform)).GetComponent<RectTransform>();
                dash.SetParent(b.rt, false);
                Stretch(dash, 0f);
                b.dashed = dash.gameObject.AddComponent<DashedFrame>();
                b.dashed.raycastTarget = false;
                dash.gameObject.AddComponent<UISkew>();
                b.key = Text(b.rt, title, 30, TextAlignmentOptions.Center, new Vector2(0, 10), new Vector2(88, 36));
                b.key.text = (i + 1).ToString();
                b.name = Text(b.rt, body, 13, TextAlignmentOptions.Center, new Vector2(0, -18), new Vector2(88, 18));
                b.name.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
                b.name.characterSpacing = 3f;
                b.name.text = skills[i].shortName;
                b.cdTint = Img(b.rt, Alpha(Black, 0.18f), 3f, skew: true);
                b.cdDark = Img(b.rt, Alpha(Black, 0.84f), 3f, skew: true);
                Filled(b.cdDark, Image.FillMethod.Radial360, (int)Image.Origin360.Top);
                b.cdDark.fillClockwise = true;
                b.wait = Text(b.rt, title, 40, TextAlignmentOptions.Center, Vector2.zero, new Vector2(88, 60));
                b.wait.color = Bone;
                Glow(b.wait);
            }

            // ---- Moves line: move pips, action pip, stance, sight, undo lock ----
            var line = movesLine = Row("Moves Line", root, 22f);
            Place(line, Vector2.zero, new Vector2(Left, MovesY));
            Fit(line);
            var moves = Row("Moves", line, 8f);
            var pips = Row("Pips", moves, 4f);
            movePips = new Image[moveRange];
            for (int i = 0; i < moveRange; i++) movePips[i] = Pip(pips, 18f);
            Label(moves, "Moves", 13, LockedText, 2f);
            var action = Row("Action", line, 8f);
            actionPip = Pip(action, 26f);
            Label(action, "Action", 13, LockedText, 2f);
            var state = Row("State", line, 16f);
            var stance = Row("Stance", state, 6f);
            var crouchCap = KeyCap(stance, "C");
            stanceIcon = Icon(stance, "stand", 16, 16);
            stanceWord = Label(stance, "Standing", 14, Bone, 1.5f);
            Button("crouch", stance, crouchCap.border, crouchCap.text, stanceWord);
            var sight = sightRow = Row("Sight", state, 6f);
            sightIcon = Icon(sight, "eye_slash", 18, 12);
            sightWord = Label(sight, "Unseen", 14, Grey, 1.5f);
            // Hauling a body: red on purpose (you're dragging a corpse). A lying figure: the stand icon on its side.
            var drag = Row("Dragging", state, 6f);
            var corpse = Icon(drag, "stand", 16, 16);
            corpse.color = Blood;
            corpse.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            Label(drag, "Dragging", 14, Blood, 1.5f);
            dragging = drag.gameObject;
            // Undo: the key while you can undo, the padlock once it's locked, nothing when there's nothing to undo.
            var undoRow = Row("Undo Key", state, 6f);
            var undoCap = KeyCap(undoRow, "Z");
            Button("undo", undoRow, undoCap.border, undoCap.text, Label(undoRow, "Undo", 14, Grey, 1.5f));
            undoKey = undoRow.gameObject;
            var lockRow = Row("Undo", state, 6f);
            Icon(lockRow, "lock", 12, 14).color = Grey;
            Label(lockRow, "Undo", 14, Grey, 1.5f);
            undo = lockRow.gameObject;

            // ---- Tooltip: above the moves line, slanted, sized to its text ----
            tip = Rect("Tooltip", root, Vector2.zero, new Vector2(40, TipY), new Vector2(420, 100));
            tipBorder = Img(tip, Bone, 0f, skew: true);
            Img(tip, Black, 2f, skew: true);
            IgnoreLayout(tip);
            var column = Column(tip, new RectOffset(22, 22, 14, 16), 6f);
            column.childAlignment = TextAnchor.UpperLeft;
            column.childForceExpandWidth = true;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            header.SetParent(tip, false);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
            tipName = Text(header, title, 26, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.zero);
            Stretch(tipName.rectTransform, 0f);
            tipName.fontStyle = FontStyles.UpperCase;
            tipName.characterSpacing = 3f;
            tipStatus = Text(header, body, 15, TextAlignmentOptions.BottomRight, Vector2.zero, Vector2.zero);
            Stretch(tipStatus.rectTransform, 0f);
            tipStatus.margin = new Vector4(0, 0, 0, 3);
            tipStatus.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            tipStatus.characterSpacing = 5f;
            tipDesc = Text(tip, body, 18, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipUp = Text(tip, body, 18, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipUp.fontStyle = FontStyles.Bold;
            tipDesc.textWrappingMode = tipUp.textWrappingMode = TextWrappingModes.Normal;

            // ---- Mode banner, top centre ----
            banner = Row("Mode Banner", root, 16f);
            Place(banner, new Vector2(0.5f, 1f), new Vector2(0, -28));
            banner.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(22, 22, 8, 9);
            Img(banner, Bone, 0f, skew: true);
            Img(banner, Black, 2f, skew: true);
            IgnoreLayout(banner);
            Fit(banner);
            bannerName = Text(banner, title, 24, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            bannerName.fontStyle = FontStyles.UpperCase;
            bannerName.characterSpacing = 2f;
            bannerName.color = Bone;
            bannerHint = Text(banner, body, 17, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            bannerHint.color = Bone;
            var esc = Row("Esc", banner, 6f);
            var escCap = KeyCap(esc, "Esc");
            Button("cancel", esc, escCap.border, escCap.text, Label(esc, "Cancel", 14, Grey, 2f));

            // ---- Bottom right: end turn (hold Space), the controls hint above it, the log above that ----
            var end = Row("End Turn", root, 6f);
            Place(end, Vector2.right, new Vector2(-32, 26));
            Fit(end);
            var spaceCap = Row("Key Space", end, 0f);
            spaceCap.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(10, 10, 0, 0);
            var spaceBorder = Img(spaceCap, Grey, 0f);
            Img(spaceCap, Black, 1.5f);
            endFill = Img(spaceCap, Alpha(Blood, 0.85f), 1.5f); // fills left to right while held
            Filled(endFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            IgnoreLayout(spaceCap);
            var spaceWord = Label(spaceCap, "Space", 14, Grey, 1f);
            spaceWord.gameObject.AddComponent<LayoutElement>().minHeight = 20;
            endWord = Label(end, "Hold: end turn", 14, Grey, 2f);
            Glow(endWord);
            Button("end", end, spaceBorder, spaceWord, endWord); // a click ends the turn too
            var hint = Row("Controls Hint", root, 12f);
            Place(hint, Vector2.right, new Vector2(-32, 54));
            Fit(hint);
            foreach (var (key, caption, id) in new[] { ("Esc", "Menu", "pause"), ("H", "Controls", "controls"), ("L", "Log", "log") })
            {
                var b = Row(caption, hint, 6f);
                var cap = KeyCap(b, key);
                var w = Label(b, caption, 14, Grey, 2f);
                Glow(w);
                Button(id, b, cap.border, cap.text, w);
            }
            log = Text(root, body, 14, TextAlignmentOptions.BottomRight, Vector2.right, new Vector2(280, 200)); // clear of the legend number at 72 px
            Place(log.rectTransform, Vector2.right, new Vector2(-32, 84));
            log.textWrappingMode = TextWrappingModes.Normal;
            log.color = Bone;
            Glow(log);

            // ---- Controls panel (H) ----
            var panel = Rect("Controls", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 100));
            Img(panel, Bone, 0f, skew: true);
            Img(panel, Black, 2f, skew: true);
            IgnoreLayout(panel);
            var rows = Column(panel, new RectOffset(34, 34, 18, 22), 7f);
            rows.childAlignment = TextAnchor.UpperLeft;
            Fit(panel);
            var head = Text(panel, title, 26, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            head.fontStyle = FontStyles.UpperCase;
            head.color = Bone;
            head.text = "Controls";
            foreach (var (keys, what) in ControlsList)
            {
                var row = Row("Row", panel, 14f);
                var caps = Row("Keys", row, 4f);
                caps.gameObject.AddComponent<LayoutElement>().minWidth = 96;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (i > 0) Label(caps, "to", 13, Grey, 1f);
                    KeyCap(caps, keys[i]);
                }
                var t = Text(row, body, 17, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
                t.color = Bone;
                t.text = what;
            }
            // Keep every line clear of the slanted left edge (and the right): the slant shifts the corners by half the
            // panel's height x tan 10 degrees, so pad by that plus 28 px.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            int lean = Mathf.CeilToInt(panel.rect.height / 2f * Mathf.Tan(10f * Mathf.Deg2Rad));
            rows.padding = new RectOffset(28 + lean, 28 + lean, 18, 22);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            controls = panel.gameObject;
            controls.SetActive(false);

            // Shrink the player's stack (everything above the health row) towards its bottom-left corner.
            var stack = Rect("Player Stack", root, Vector2.zero, StackOrigin, Vector2.zero);
            var parts = new[] { bar, legendWord.rectTransform, legendNum.rectTransform, movesLine, tip }
                .Concat(boxes.Select(b => b.rt)).Concat(notchLabels.Select(l => l.rectTransform)).ToList();
            foreach (var part in parts) part.SetParent(stack, true);
            stack.localScale = Vector3.one * StackScale;

            BuildTickets(root);

            // ---- Intro / end card: the screen dimmed, a slanted card with a blood bar down its left edge ----
            var cardRoot = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            cardRoot.SetParent(root, false);
            Stretch(cardRoot, 0f);
            card = cardRoot.GetComponent<CanvasGroup>();
            card.blocksRaycasts = false;
            Img(cardRoot, new Color(0.02f, 0.012f, 0.016f, 0.55f), 0f);
            var box = Rect("Box", cardRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 100));
            Img(box, Black, 0f, skew: true);
            var stripe = Img(box, Blood, 0f, skew: true).rectTransform;
            stripe.anchorMax = new Vector2(0f, 1f);
            stripe.offsetMax = new Vector2(10f, 0f);
            IgnoreLayout(box);
            var lines = Column(box, new RectOffset(46, 48, 22, 26), 6f);
            lines.childAlignment = TextAnchor.UpperLeft;
            Fit(box);
            cardSmall = Label(box, "", 16, LockedText, 6f);
            cardLine1 = Text(box, title, 46, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            cardLine2 = Text(box, title, 46, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            cardLine1.fontStyle = cardLine2.fontStyle = FontStyles.UpperCase;
            cardLine1.color = cardLine2.color = Bone;
            cardLine1.lineSpacing = cardLine2.lineSpacing = -10f;
            cardFoot = Label(box, "", 14, Grey, 3f);
            cardTickets = MakeTicketRow(box);
            cardRoot.gameObject.SetActive(false);

            BuildLog(root);
            BuildOverlays(root);
            controls.transform.SetSiblingIndex(card.transform.GetSiblingIndex()); // over the HUD, under the cards
            logPanel.transform.SetSiblingIndex(card.transform.GetSiblingIndex());
        }

        // ---- Log panel (L): the level's whole log, slanted like the controls panel, newest at the bottom ----
        GameObject logPanel;
        RectTransform logContent, logView;
        TMP_Text logAll;
        float logScroll;
        public bool LogOpen => logPanel.activeSelf;
        public void HideLog() => logPanel.SetActive(false);

        void BuildLog(Transform root)
        {
            var panel = Rect("Log Panel", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 460));
            Img(panel, Bone, 0f, skew: true);
            Img(panel, Black, 2f, skew: true);
            int lean = Mathf.CeilToInt(460f / 2f * Mathf.Tan(10f * Mathf.Deg2Rad)) + 28; // clear of the slanted edges
            var head = Text(panel, title, 26, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            Stretch(head.rectTransform, 0f);
            head.margin = new Vector4(lean, 18, lean, 0);
            head.fontStyle = FontStyles.UpperCase;
            head.color = Bone;
            head.text = "Log";
            var close = Row("Close", panel, 6f);
            Place(close, Vector2.one, new Vector2(-lean, -22));
            close.pivot = new Vector2(1f, 1f);
            Fit(close);
            KeyCap(close, "L");
            Label(close, "or", 13, Grey, 1f);
            KeyCap(close, "Esc");
            Label(close, "Close", 14, Grey, 2f);
            logView = Rect("View", panel, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(logView, 0f);
            logView.offsetMin = new Vector2(lean, 22);
            logView.offsetMax = new Vector2(-lean, -62);
            logView.gameObject.AddComponent<RectMask2D>();
            logAll = Text(logView, body, 17, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.zero);
            logContent = logAll.rectTransform;
            logContent.anchorMin = Vector2.zero;
            logContent.anchorMax = new Vector2(1f, 0f);
            logContent.pivot = new Vector2(0.5f, 0f);
            logAll.textWrappingMode = TextWrappingModes.Normal;
            logAll.color = Bone;
            logPanel = panel.gameObject;
            logPanel.SetActive(false);
        }

        // Opens on the newest lines (scrolled to the bottom).
        public void ShowLog(List<string> lines)
        {
            logAll.text = string.Join("\n", lines);
            logPanel.SetActive(true);
            logContent.sizeDelta = new Vector2(0f, logAll.GetPreferredValues(logAll.text, logView.rect.width, 0f).y);
            logScroll = 0f;
            ScrollLog(0f);
        }

        // Positive = back towards older lines.
        public void ScrollLog(float by)
        {
            logScroll = Mathf.Clamp(logScroll + by, 0f, Mathf.Max(0f, logContent.sizeDelta.y - logView.rect.height));
            logContent.anchoredPosition = new Vector2(0f, -logScroll);
        }

        // The card over a dimmed screen. autoHide > 0: fades after that many seconds; dismissable: any input fades it.
        // tickets: the ticket row to show on the card (the win card), or null.
        public void ShowCard(string small, string line1, string line2, string foot, float autoHide, bool dismissable, int[] tickets = null)
        {
            cardTickets.rt.gameObject.SetActive(tickets != null);
            if (tickets != null) cardTickets.Set(tickets);
            cardSmall.text = small;
            cardLine1.text = line1;
            cardLine2.text = line2;
            cardFoot.text = foot;
            cardFoot.gameObject.SetActive(!string.IsNullOrEmpty(foot));
            cardShown = Time.unscaledTime;
            cardAuto = autoHide;
            cardDismissable = dismissable;
            cardFading = -1f;
            card.alpha = 1f;
            card.gameObject.SetActive(true);
        }

        public void HideCard()
        {
            if (card.gameObject.activeSelf && cardFading < 0f) cardFading = Time.unscaledTime;
        }

        // Which skill box the mouse is over (-1 = none), and whether the mouse is over the HUD at all.
        public int SlotAt(Vector2 screen)
        {
            for (int i = 0; i < boxes.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(boxes[i].rt, screen, null)) return i;
            return -1;
        }

        public bool Blocks(Vector2 screen) =>
            SlotAt(screen) >= 0 || ButtonUnder(screen) != null || RectTransformUtility.RectangleContainsScreenPoint(bar, screen, null)
            || (tip.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(tip, screen, null))
            || (controls.activeSelf && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)controls.transform, screen, null));

        // mode: the banner's skill name and hint (null = no targeting mode). log: oldest first.
        public void Refresh(Carriage c, SkillId? selected, int hovered, string mode, string modeHint, IList<string> logLines)
        {
            int L = c.legend;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                var d = skills[i];
                var id = (SkillId)i;
                // Locked (solid grey) beats no target (hollow, dashed) beats the plain available / bloodied look.
                // Cooling down isn't "no target": the box keeps its look so the dark sweep shows over it.
                // Selected, or (the drag box) while you hold a body.
                bool locked = !c.Has(id), bloodied = c.Bloodied(id), lifted = selected == id || (id == SkillId.Drag && c.held != null);
                bool cooling = !locked && d.cooldown > 0 && c.Wait(id) > 0;
                bool noTarget = !locked && !cooling && !c.Usable(id);
                var own = bloodied ? Bright : Bone;
                b.bg.color = locked ? Alpha(LockedBox, 0.92f) : noTarget ? Black : bloodied ? Blood : Bone;
                b.key.color = b.name.color = locked ? LockedText : noTarget ? own : bloodied ? Bone : Black;
                bool white = i == hovered || lifted;
                b.border.enabled = white || !noTarget;
                b.border.color = white ? Bone : Black;
                b.dashed.shown = noTarget && !white;
                b.dashed.color = own;
                b.rt.anchoredPosition = new Vector2(Left + i * 98, BoxY + (lifted ? 6f : 0f)) - StackOrigin; // inside the stack
                // Progress: white toward the unlock (red for skills that unlock already bloodied), then red toward Bloodied.
                float fill = 0f;
                if (locked) { fill = d.unlock > 0 ? L / (float)d.unlock : 0f; b.fill.color = d.bloodied == d.unlock ? Blood : Bone; }
                else if (!bloodied && d.bloodied > d.unlock) { fill = (L - d.unlock) / (float)(d.bloodied - d.unlock); b.fill.color = Blood; }
                b.fill.fillAmount = Mathf.Clamp01(fill);
                b.fill.enabled = fill > 0f;
                // Cooldown: a dark sweep from 12 o'clock that loses a step each turn, and the turns left.
                int wait = locked || d.cooldown <= 0 ? 0 : Mathf.Min(c.Wait(id), d.cooldown);
                b.cdTint.enabled = b.cdDark.enabled = b.wait.enabled = wait > 0;
                b.cdDark.fillAmount = wait / (float)Mathf.Max(1, d.cooldown);
                b.wait.text = wait.ToString();
            }

            legendFill.fillAmount = L / (float)maxLegend;
            for (int i = 0; i < notchAt.Length; i++) notches[i].color = notchLabels[i].color = L >= notchAt[i] ? Bone : NotchOff;
            float k = L / (float)maxLegend;
            legendNum.text = L.ToString();
            legendNum.fontSize = Mathf.Round(32f + 40f * k);
            legendNum.color = k >= 1f ? Hex("7A0618") : k >= 0.75f ? Hex("9E0B22") : k >= 0.5f ? Blood : k >= 0.25f ? Bright : Bone;

            tip.gameObject.SetActive(hovered >= 0);
            if (hovered >= 0)
            {
                var d = skills[hovered];
                var id = (SkillId)hovered;
                bool locked = !c.Has(id), bloodied = c.Bloodied(id), usable = c.Usable(id);
                var state = locked ? Grey : bloodied ? Bright : Bone;
                tipBorder.color = tipName.color = tipStatus.color = state;
                tipName.text = d.name;
                int cool = c.Wait(id);
                tipStatus.text = locked ? $"Unlocks at {d.unlock}" : cool > 0 ? $"Ready in {cool}" : !usable ? (c.WhyNot(id) ?? "No target") : bloodied ? "Bloodied" : "Ready";
                tipDesc.text = d.description;
                tipDesc.color = locked ? Grey : Bone;
                bool upgrade = d.bloodied > d.unlock && !string.IsNullOrEmpty(d.upgrade);
                tipUp.gameObject.SetActive(upgrade);
                tipUp.text = upgrade ? $"At {d.bloodied}: {d.upgrade}" : "";
                tipUp.color = bloodied ? Bright : Burgundy;
            }

            hpFill.fillAmount = c.hp / (float)c.t.maxHp;
            hpNum.text = c.hp.ToString();
            hpWord.rectTransform.anchoredPosition = new Vector2(Left + 310 + hpNum.preferredWidth + 10, HealthY);

            for (int i = 0; i < movePips.Length; i++) movePips[i].color = i < c.movesLeft ? Bone : LockedBox;
            actionPip.color = c.acted || c.turnOver ? LockedBox : Bone;
            stanceIcon.sprite = IconSprite(c.crouched ? "crouch" : "stand");
            stanceWord.text = c.crouched ? "Crouched" : "Standing";
            bool seen = c.SeenByAnyone(c.player);
            sightIcon.sprite = IconSprite(seen ? "eye" : "eye_slash");
            sightIcon.color = sightWord.color = seen ? Bone : Grey;
            sightWord.text = seen ? "Seen" : "Unseen";
            dragging.SetActive(c.held != null);
            undoKey.SetActive(c.CanReset);
            undo.SetActive(!c.CanReset && c.UndoLocked);

            banner.gameObject.SetActive(mode != null);
            if (mode != null) { bannerName.text = mode; bannerHint.text = modeHint; }

            // Newest at the bottom, older lines fading out.
            int n = logLines.Count;
            log.text = string.Join("\n", logLines.Select((l, i) => $"<alpha=#{Mathf.RoundToInt(255 * (0.3f + 0.7f * (i + 1) / n)):X2}>{l}"));

            if (card.gameObject.activeSelf)
            {
                if (cardFading < 0f && cardAuto > 0f && Time.unscaledTime - cardShown > cardAuto) HideCard();
                if (cardFading >= 0f)
                {
                    card.alpha = 1f - (Time.unscaledTime - cardFading) / 0.4f;
                    if (card.alpha <= 0f) card.gameObject.SetActive(false);
                }
            }
        }

        public void BeginTags() => tagCount = 0;

        public void EndTags()
        {
            for (int i = tagCount; i < tags.Count; i++) tags[i].rt.gameObject.SetActive(false);
        }

        // One enemy's chip (and, when hovered, its label above it), bottom centre at `screen` (pixels).
        // awareness: 0 Unaware, 1 Searching, 2 Startled, 3 Combat, 4 Broken.
        // threat: they'll fire on what you're aiming (a red border, the shot in red). icon: what they'll do if you end
        // your turn there ("crosshair", "charge", "wildfire" or null). plate: a tough's armour, 1 whole, 2 cracked
        // (killable). notYet: "Not yet" over a tough you're targeting before it's Shaken.
        public RectTransform AddTag(Vector2 screen, float scale, int awareness, int terrorNow, bool seen, bool hovered, string name, string role, string words, string intent,
            bool threat = false, string icon = null, int plate = 0, bool notYet = false)
        {
            if (tagCount == tags.Count) tags.Add(MakeTag());
            var g = tags[tagCount++];
            g.rt.gameObject.SetActive(true);
            g.rt.position = screen;
            g.rt.localScale = Vector3.one * scale;
            // What they know, by colour: black (white outline) unaware, white searching or startled, red in combat;
            // dimmed when broken. The icon is what they'll do.
            bool combat = awareness == 3, broken = awareness == 4, white = awareness == 1 || awareness == 2;
            g.chipBg.color = combat ? Blood : white ? Bone : broken ? Alpha(Black, 0.7f) : Black;
            g.chipBorder.color = combat ? Blood : broken ? LockedBox : Bone;
            var ink = white ? Black : broken ? Grey : Bone;
            // Startled, or a panicked freeze turn (it loses the turn the same way): the scared face. Searching: "?".
            if (icon == "freeze" || (awareness == 2 && icon == null)) icon = "startled";
            bool glyph = awareness == 1 && icon == null;
            g.glyph.gameObject.SetActive(glyph);
            g.glyph.text = "?";
            g.glyph.color = ink;
            g.icon.enabled = !glyph && (broken || icon != null || awareness == 0);
            if (g.icon.enabled) g.icon.sprite = IconSprite(broken ? "flag" : icon ?? "ring");
            g.icon.color = ink;
            for (int i = 0; i < 3; i++) g.segEdges[i].color = white ? Black : Bone; // the red fill always reads
            g.plate.gameObject.SetActive(plate > 0);
            if (plate > 0) g.plate.sprite = IconSprite(plate == 2 ? "plate_cracked" : "plate");
            g.plate.color = ink;
            g.notYet.SetActive(notYet);
            // Three segments, one per stage (Shaken, Panicked, Broken), each filling smoothly within its stage.
            for (int i = 0; i < 3; i++)
                g.segs[i].fillAmount = broken ? 1f : Mathf.Clamp01((terrorNow - terror[i]) / (float)(terror[i + 1] - terror[i]));
            g.eye.gameObject.SetActive(seen);
            g.eye.color = ink;
            g.label.SetActive(hovered);
            if (hovered)
            {
                g.name.text = name;
                g.role.text = role;
                g.words.text = words;
                g.intent.text = intent;
                g.intent.gameObject.SetActive(!string.IsNullOrEmpty(intent));
                g.intent.color = threat ? Bright : LockedText;
                g.labelBorder.color = threat ? Blood : Bone;
            }
            return g.chip;
        }

        Tag MakeTag()
        {
            var g = new Tag();
            g.rt = new GameObject("Enemy", typeof(RectTransform)).GetComponent<RectTransform>();
            g.rt.SetParent(tagRoot, false);
            g.rt.anchorMin = g.rt.anchorMax = Vector2.zero; // positioned in screen pixels
            g.rt.pivot = new Vector2(0.5f, 0f);
            var stack = Column(g.rt, new RectOffset(0, 0, 0, 0), 4f);
            stack.childAlignment = TextAnchor.LowerCenter;
            Fit(g.rt);

            // Hover label: role, then awareness and stage, then what they'll do.
            var label = Rect("Label", g.rt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            g.labelBorder = Img(label, Bone, 0f, skew: true);
            Img(label, Black, 2f, skew: true);
            IgnoreLayout(label);
            Column(label, new RectOffset(12, 12, 4, 5), 1f).childAlignment = TextAnchor.UpperCenter;
            g.name = Text(label, title, 16, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            g.name.fontStyle = FontStyles.UpperCase;
            g.name.characterSpacing = 2f;
            g.name.color = Bone;
            g.role = Label(label, "", 12, LockedText, 1f);
            g.role.alignment = TextAlignmentOptions.Center;
            g.words = Label(label, "", 13, LockedText, 1f);
            g.words.alignment = TextAlignmentOptions.Center;
            g.intent = Label(label, "", 13, LockedText, 1f);
            g.intent.alignment = TextAlignmentOptions.Center;
            g.label = label.gameObject;

            // "Not yet": over a tough you're targeting before it's Shaken.
            var notYet = Row("Not Yet", g.rt, 0f);
            notYet.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(9, 9, 2, 3);
            Img(notYet, Blood, 0f, skew: true);
            Img(notYet, Black, 1.5f, skew: true);
            IgnoreLayout(notYet);
            Label(notYet, "Not yet", 12, Bone, 2f);
            g.notYet = notYet.gameObject;

            // Chip: awareness icon, three terror segments, the has-seen eye.
            var chip = g.chip = Row("Chip", g.rt, 6f);
            chip.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(7, 7, 3, 3);
            g.chipBorder = Img(chip, LockedBox, 0f, skew: true);
            g.chipBg = Img(chip, Black, 2f, skew: true);
            IgnoreLayout(chip);
            var slot = Rect("Awareness", chip, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            Size(slot, 18, 18);
            g.icon = Img(slot, Grey, 2f);
            g.icon.preserveAspect = true;
            g.glyph = Text(slot, title, 18, TextAlignmentOptions.Center, Vector2.zero, new Vector2(18, 22));
            var segs = Row("Terror", chip, 3f);
            g.segs = new Image[3];
            g.segEdges = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var seg = Rect("Segment", segs, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 9));
                Size(seg, 14, 9);
                g.segEdges[i] = Img(seg, Bone, 0f, skew: true);
                Img(seg, LockedBox, 1.5f, skew: true);
                g.segs[i] = Img(seg, Blood, 1.5f, skew: true);
                Filled(g.segs[i], Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            }
            g.eye = Icon(chip, "eye", 18, 12);
            g.plate = Icon(chip, "plate", 14, 14);
            return g;
        }

        // ---- Building blocks ----

        Sprite IconSprite(string name) => icons.TryGetValue(name, out var s) ? s : white;

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            Place(rt, anchor, pos);
            return rt;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        static void Size(RectTransform rt, float w, float h)
        {
            var e = rt.gameObject.AddComponent<LayoutElement>();
            e.minWidth = e.preferredWidth = w;
            e.minHeight = e.preferredHeight = h;
        }

        static void Fit(RectTransform rt)
        {
            var f = rt.gameObject.AddComponent<ContentSizeFitter>();
            f.horizontalFit = f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // Backgrounds and borders added so far sit behind the layout instead of in it.
        static void IgnoreLayout(RectTransform rt)
        {
            foreach (Transform child in rt) child.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        // Children laid out left to right at their preferred sizes, centred vertically.
        static RectTransform Row(string name, Transform parent, float spacing)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return rt;
        }

        static VerticalLayoutGroup Column(RectTransform rt, RectOffset padding, float spacing)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = padding;
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;
            return v;
        }

        static void Filled(Image img, Image.FillMethod method, int origin)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = method;
            img.fillOrigin = origin;
        }

        // A plain rectangle filling its parent, `inset` in from the edges (negative = outset).
        Image Img(Transform parent, Color col, float inset, bool skew = false)
        {
            var rt = new GameObject("Image", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt, inset);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = white;
            img.color = col;
            img.raycastTarget = false;
            if (skew) rt.gameObject.AddComponent<UISkew>();
            return img;
        }

        // A slanted pip (moves, action), laid out at w x 11.
        Image Pip(Transform parent, float w)
        {
            var rt = Rect("Pip", parent, Vector2.zero, Vector2.zero, new Vector2(w, 11));
            Size(rt, w, 11);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = white;
            img.raycastTarget = false;
            rt.gameObject.AddComponent<UISkew>();
            return img;
        }

        Image Icon(Transform parent, string sprite, float w, float h)
        {
            var rt = Rect("Icon", parent, Vector2.zero, Vector2.zero, new Vector2(w, h));
            Size(rt, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = IconSprite(sprite);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        // Bold, uppercase, letter-spaced Barlow (labels like MOVES, HEALTH, CANCEL).
        TMP_Text Label(Transform parent, string text, float size, Color col, float spacing)
        {
            var t = Text(parent, body, size, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            t.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            t.characterSpacing = spacing * 4f;
            t.color = col;
            t.text = text;
            return t;
        }

        // A key cap: the key's name in a thin box (grey, or another colour).
        (Image border, TMP_Text text) KeyCap(Transform parent, string key) => KeyCap(parent, key, Grey);

        (Image border, TMP_Text text) KeyCap(Transform parent, string key, Color col) => KeyCap(parent, key, col, Black);

        (Image border, TMP_Text text) KeyCap(Transform parent, string key, Color col, Color fill)
        {
            var cap = Row("Key " + key, parent, 0f);
            cap.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(6, 6, 0, 0);
            var border = Img(cap, col, 0f);
            Img(cap, fill, 1.5f);
            IgnoreLayout(cap);
            var t = Label(cap, key, 14, col, 1f);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 20;
            return (border, t);
        }

        // ---- Mouse buttons: the HUD's key hints are clickable too (white under the mouse, red while pressed) ----

        class HudButton { public string id; public RectTransform rt; public (Graphic g, Color normal)[] parts; public bool hot; }
        readonly List<HudButton> buttons = new List<HudButton>();

        void Button(string id, RectTransform rt, params Graphic[] parts) =>
            buttons.Add(new HudButton { id = id, rt = rt, parts = parts.Select(g => (g, g.color)).ToArray() });

        HudButton ButtonUnder(Vector2 screen) => hudCanvas.enabled
            ? buttons.FirstOrDefault(b => b.rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(b.rt, screen, null)) : null;

        // Colours the buttons for this frame and returns the one clicked (pressed this frame), if any.
        public string RefreshButtons(Vector2 screen, bool held, bool pressed)
        {
            var under = ButtonUnder(screen);
            foreach (var b in buttons)
            {
                if (b == under)
                {
                    b.hot = true;
                    foreach (var (g, _) in b.parts) g.color = held ? Blood : Bone;
                }
                else if (b.hot)
                {
                    b.hot = false;
                    foreach (var (g, normal) in b.parts) g.color = normal;
                }
            }
            return pressed ? under?.id : null;
        }

        static TMP_Text Text(Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions align, Vector2 pos, Vector2 box)
        {
            var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = box;
            rt.anchoredPosition = pos;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // A soft dark halo so text reads over white boxes and the carriage.
        static void Glow(TMP_Text t)
        {
            var m = t.fontMaterial;
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", Alpha(Black, 0.9f));
            m.SetFloat("_UnderlaySoftness", 0.6f);
            m.SetFloat("_UnderlayDilate", 0.5f);
        }
    }
}
