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

    // A custom shape drawn through an Image: the effect replaces the Image's quad with its own triangles. (Custom
    // Graphic subclasses drew nothing in this project; Images with mesh effects, like UISkew, do.) Add it before
    // UISkew so the shape is drawn, then slanted.
    // Edges are feathered (Update 19.4): an overlay canvas gets no MSAA, so a hard polygon edge on a long shallow slope
    // or a tilted card stair-steps. FanAA / RingAA fade each outline over one screen pixel, half in, half out.
    [RequireComponent(typeof(Image))]
    public abstract class UIShape : BaseMeshEffect
    {
        public Color color { get => graphic.color; set => graphic.color = value; }
        public bool raycastTarget { set => graphic.raycastTarget = value; }
        public bool shown { set => graphic.enabled = value; }
        public RectTransform rectTransform => (RectTransform)transform;
        public void Redraw() => graphic.SetVerticesDirty();
        // > 0: the Image's sprite tiles every 1 / uvPerUnit units, fixed to the rect (the hatch's stripes).
        public float uvPerUnit;
        // One screen pixel in this rect's units (overlay canvas: world units are screen pixels).
        // ponytail: worked out when the mesh is built; a resolution change keeps the old width until the next redraw.
        protected float px = 1f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            vh.Clear();
            px = 1f / Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
            var r = rectTransform.rect;
            Build(vh, r, graphic.color);
            if (uvPerUnit <= 0f) return;
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.uv0 = (Vector2)(v.position - (Vector3)r.min) * uvPerUnit;
                vh.SetUIVertex(v, i);
            }
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

        // A convex outline, fanned from its first point (or from `hub`, for a star-shaped one).
        protected static void Fan(VertexHelper vh, Color32 col, IList<Vector2> pts, Vector2? hub = null)
        {
            int i0 = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = col;
            if (hub.HasValue) { v.position = hub.Value; vh.AddVert(v); }
            foreach (var p in pts) { v.position = p; vh.AddVert(v); }
            if (hub.HasValue) for (int i = 0; i < pts.Count; i++) vh.AddTriangle(i0, i0 + 1 + i, i0 + 1 + (i + 1) % pts.Count);
            else for (int i = 1; i + 1 < pts.Count; i++) vh.AddTriangle(i0, i0 + i, i0 + i + 1);
        }

        // The band between two closed outlines of the same length, `ca` along the first, `cb` along the second.
        static void Band(VertexHelper vh, IList<Vector2> a, IList<Vector2> b, Color32 ca, Color32 cb)
        {
            int n = a.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n, i0 = vh.currentVertCount;
                var v = UIVertex.simpleVert;
                v.color = ca; v.position = a[i]; vh.AddVert(v); v.position = a[j]; vh.AddVert(v);
                v.color = cb; v.position = b[j]; vh.AddVert(v); v.position = b[i]; vh.AddVert(v);
                vh.AddTriangle(i0, i0 + 1, i0 + 2);
                vh.AddTriangle(i0 + 2, i0 + 3, i0);
            }
        }

        // The outline moved `d` outward (inward if negative), each edge parallel to itself (mitred corners, capped
        // at sharp ones). Consecutive repeated points are dropped first.
        protected static List<Vector2> Offset(IList<Vector2> pts, float d)
        {
            var p = Clean(pts);
            int n = p.Count;
            float area = 0f;
            for (int i = 0; i < n; i++) area += p[i].x * p[(i + 1) % n].y - p[(i + 1) % n].x * p[i].y;
            if (area < 0f) d = -d; // clockwise: outward is the other side
            var o = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
                Vector2 n1 = Out(b - a), n2 = Out(c - b);
                float k = 1f + Vector2.Dot(n1, n2);
                o.Add(b + (k > 0.25f ? (n1 + n2) / k : (n1 + n2).normalized * 2f) * d);
            }
            return o;
        }

        static Vector2 Out(Vector2 e) => new Vector2(e.y, -e.x).normalized;

        protected static List<Vector2> Clean(IList<Vector2> pts)
        {
            var p = new List<Vector2>(pts.Count);
            foreach (var q in pts)
                if (p.Count == 0 || (q - p[p.Count - 1]).sqrMagnitude > 1e-4f) p.Add(q);
            while (p.Count > 1 && (p[0] - p[p.Count - 1]).sqrMagnitude <= 1e-4f) p.RemoveAt(p.Count - 1);
            return p;
        }

        // A filled outline with its edge faded over one screen pixel.
        protected void FanAA(VertexHelper vh, Color32 col, IList<Vector2> pts, Vector2? hub = null)
        {
            var p = Clean(pts);
            if (p.Count < 3) return;
            var inner = Offset(p, -px / 2f);
            Fan(vh, col, inner, hub);
            Band(vh, inner, Offset(p, px / 2f), col, new Color32(col.r, col.g, col.b, 0));
        }

        // The ring between two outlines (`inner` inside `outer`, same number of points), both edges faded.
        protected void RingAA(VertexHelper vh, Color32 col, IList<Vector2> outer, IList<Vector2> inner)
        {
            var clear = new Color32(col.r, col.g, col.b, 0);
            var oIn = Offset(outer, -px / 2f);
            var iOut = Offset(inner, px / 2f);
            Band(vh, oIn, iOut, col, col);
            Band(vh, oIn, Offset(outer, px / 2f), col, clear);
            Band(vh, iOut, Offset(inner, -px / 2f), col, clear);
        }
    }

    // A bar's wedge (the legend and health bars): `taper` of the full height at the left end, full height at the right,
    // bottom flat; drawn only between `from` and `to` (0..1 along it), so it is also the bar's fill and its hatched gain.
    public class Wedge : UIShape
    {
        public float taper = 0.3f;
        float a, b = 1f;

        public void Span(float from, float to)
        {
            from = Mathf.Clamp01(from); to = Mathf.Clamp01(to);
            if (from != a || to != b) { a = from; b = to; Redraw(); }
        }

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            float x0 = Mathf.Lerp(r.xMin, r.xMax, a), x1 = Mathf.Lerp(r.xMin, r.xMax, b);
            if (x1 - x0 < 0.01f) return;
            float Top(float x) => r.yMin + r.height * Mathf.Lerp(taper, 1f, (x - r.xMin) / r.width);
            FanAA(vh, col, new[] { new Vector2(x0, r.yMin), new Vector2(x1, r.yMin), new Vector2(x1, Top(x1)), new Vector2(x0, Top(x0)) });
        }
    }

    // The sketch HUD's tile (Update 19): the rect with `cut` px corners cut off, drawn up to `fill` of its height from
    // the bottom (the fill rising in a locked or not-yet-Bloodied tile).
    public class Octagon : UIShape
    {
        public float cut = 10f;
        float fillTo = 1f;
        public float fill { get => fillTo; set { if (value != fillTo) { fillTo = value; Redraw(); } } }
        // Below 1: only a clockwise sweep from 12 o'clock, this fraction of the way round (the cooldown's dark sweep).
        float sweepTo = 1f;
        public float sweep { get => sweepTo; set { if (value != sweepTo) { sweepTo = value; Redraw(); } } }

        public static Vector2[] Points(Rect r, float cut) => new[]
        {
            new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax - cut, r.yMin), new Vector2(r.xMax, r.yMin + cut), new Vector2(r.xMax, r.yMax - cut),
            new Vector2(r.xMax - cut, r.yMax), new Vector2(r.xMin + cut, r.yMax), new Vector2(r.xMin, r.yMax - cut), new Vector2(r.xMin, r.yMin + cut),
        };

        // Angle clockwise from straight up, 0..2pi.
        static float Clock(Vector2 v) => Mathf.Repeat(Mathf.Atan2(v.x, v.y), 2f * Mathf.PI);

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            if (fillTo <= 0f || sweepTo <= 0f) return;
            if (sweepTo < 1f)
            {
                // A fan from the centre: straight up, every corner before the sweep's end, then where its end ray leaves.
                var c = r.center;
                float end = sweepTo * 2f * Mathf.PI;
                var corners = Points(r, cut);
                var arc = new List<Vector2> { c, new Vector2(c.x, r.yMax) };
                arc.AddRange(corners.Where(p => Clock(p - c) > 0f && Clock(p - c) < end).OrderBy(p => Clock(p - c)));
                var dir = new Vector2(Mathf.Sin(end), Mathf.Cos(end));
                for (int i = 0; i < corners.Length; i++) // the edge the end ray crosses
                {
                    Vector2 a = corners[i], e = corners[(i + 1) % corners.Length] - a;
                    float den = dir.x * e.y - dir.y * e.x;
                    if (Mathf.Abs(den) < 1e-6f) continue;
                    var ac = a - c;
                    float t = (ac.x * e.y - ac.y * e.x) / den, u = (ac.x * dir.y - ac.y * dir.x) / den;
                    if (t > 0f && u >= 0f && u <= 1f) { arc.Add(c + dir * t); break; }
                }
                Fan(vh, col, arc); // inside the tile: no edge to smooth
                return;
            }
            float top = r.yMin + Mathf.Clamp01(fillTo) * r.height;
            var o = Points(r, cut);
            var pts = new List<Vector2>();
            for (int i = 0; i < o.Length; i++) // the outline cut off at `top`
            {
                Vector2 a = o[i], b = o[(i + 1) % o.Length];
                if (a.y <= top) pts.Add(a);
                if ((a.y < top) != (b.y < top)) pts.Add(Vector2.Lerp(a, b, (top - a.y) / (b.y - a.y)));
            }
            FanAA(vh, col, pts);
        }
    }

    // The tile's frame: `thickness` px inside the octagon's edge, solid (gap 0) or dashed (`dash` on, `gap` off).
    public class OctFrame : UIShape
    {
        public float cut = 10f, thickness = 3f, dash = 7f;
        float gapLen;
        public float gap { get => gapLen; set { if (value != gapLen) { gapLen = value; Redraw(); } } }
        const float Tan22 = 0.4142f; // an octagon inset by d loses d * tan 22.5 of each corner cut

        static Rect Inset(Rect r, float d) => new Rect(r.xMin + d, r.yMin + d, r.width - 2f * d, r.height - 2f * d);

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            if (gapLen <= 0f)
            {
                var outer = Octagon.Points(r, cut);
                var inner = Octagon.Points(Inset(r, thickness), Mathf.Max(0f, cut - thickness * Tan22));
                RingAA(vh, col, outer, inner);
                return;
            }
            float h = thickness / 2f, along = 0f;
            var p = Octagon.Points(Inset(r, h), Mathf.Max(0f, cut - h * Tan22));
            for (int i = 0; i < 8; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % 8];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                var dir = (b - a) / len;
                var n = new Vector2(-dir.y, dir.x) * h;
                for (float s = 0f; s < len;)
                {
                    float phase = along % (dash + gapLen);
                    bool on = phase < dash;
                    float e = Mathf.Min(len, s + Mathf.Max(0.01f, on ? dash - phase : dash + gapLen - phase));
                    if (on) FanAA(vh, col, new[] { a + dir * s - n, a + dir * e - n, a + dir * e + n, a + dir * s + n });
                    along += e - s;
                    s = e;
                }
            }
        }
    }

    // A card slanted `slant` px: its top edge shifted right, its bottom left (the tooltip).
    public class Slant : UIShape
    {
        public float slant = 14f;

        protected override void Build(VertexHelper vh, Rect r, Color32 col) => FanAA(vh, col, new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.xMax - slant, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin + slant, r.yMax),
        });
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
                Box(vh, col, r.xMin + s, r.yMin, r.xMin + e, r.yMin + thickness);
                Box(vh, col, r.xMin + s, r.yMax - thickness, r.xMin + e, r.yMax);
            }
            for (float s = 0; s < r.height; s += dash + gap)
            {
                float e = Mathf.Min(s + dash, r.height);
                Box(vh, col, r.xMin, r.yMin + s, r.xMin + thickness, r.yMin + e);
                Box(vh, col, r.xMax - thickness, r.yMin + s, r.xMax, r.yMin + e);
            }
        }

        void Box(VertexHelper vh, Color32 col, float x0, float y0, float x1, float y1) =>
            FanAA(vh, col, new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) });
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
            LockedBox = Hex("3A3536"), LockedText = Hex("B4AEAF"), Grey = Hex("8A8A8A"), Burgundy = Hex("6E1A26");
        // The sketch HUD (Update 19): locked ink, the tooltip's locked grey and muted Bloodied line, bar backs, notches ahead.
        static readonly Color LockedInk = Hex("9A9093"), TipGrey = Hex("8A8285"), TipBurgundy = Hex("7A1424"), BarBack = Hex("1A1416"), NotchAhead = Hex("6E6466");
        const float BarWidth = 652f, BarHeight = 20f, Taper = 0.3f, HpWidth = 318f, HpHeight = 18f, HpTaper = 0.6f, Left = 22f, TileLeft = 18f; // the stack 16 px in (bars and health 6 more, tiles 2)
        const float TileW = 80f, TileH = 76f, TileGap = 6f;
        // Per tile, slots 1-8: fixed, so they read as hand-placed cards.
        static readonly float[] TileTilt = { -1.2f, 0.8f, -0.5f, 1f, -0.8f, 0.6f, -1f, 0.7f };
        // Bottom up from 14, 8 between rows: health row (28), notch labels (18), 2, legend bar (20), 12, tiles (76 and
        // room to lift: 86), status row (18), 12, tooltip.
        const float HealthY = 8f, LabelsY = HealthY + 28f + 8f, BarY = LabelsY + 18f + 2f, BoxY = BarY + BarHeight + 12f,
            MovesY = BoxY + 86f + 8f, TipY = MovesY + 18f + 12f;

        class Box { public RectTransform rt; public Octagon card, fill, cdTint, cd; public OctFrame frame; public Image icon; public TMP_Text key, name, wait; }

        readonly Box[] boxes = new Box[8];
        SkillDef[] skills;
        int maxLegend;
        int[] notchAt;
        Wedge legendFill, hpFill, hatch;
        // The legend the targeted kill would give (0 = no kill targeted): shown hatched on the bar, beside the big
        // number, and as the notches it crosses. Game sets it each frame before Refresh.
        public int legendGain;
        public int shownHp = -1; // health to show instead of the rules' (-1: the rules')
        Image[] notches;
        Octagon[] movePips;
        TMP_Text[] notchLabels;
        TMP_Text legendNum, hpNum, hpWord, log;
        RectTransform bar, tip, legendGroup;
        Slant tipBorder;
        TMP_Text tipName, tipStatus, tipLegend, tipDesc, tipUp;
        Image legendIcon, tipStatusIcon, tipLegendIcon, tipUpIcon; // the legend icon (19.30) in place of the word
        Octagon actionPip;
        Image stanceIcon, sightIcon;
        TMP_Text stanceWord, sightWord;
        GameObject undo, undoKey, dragging;
        Image endFill;
        TMP_Text endWord;
        // The player's stack above the health row (legend bar, skill boxes, moves line, tooltip) is drawn at this
        // scale, shrinking towards its bottom-left corner; the health row keeps its size.
        const float DragIconLift = 4f; // px: the Dragging icon raised to sit level with its word
        const float StackScale = 0.8f;
        static readonly Vector2 StackOrigin = new Vector2(16f, LabelsY);

        // Hold Space to end the turn: the key cap fills as it's held. Dim during the enemy turn.
        public void SetEndTurn(float held, bool ready)
        {
            endFill.fillAmount = Mathf.Clamp01(held);
            endWord.color = ready ? (held > 0f ? Bone : LockedText) : Alpha(LockedText, 0.4f);
        }
        RectTransform banner;
        TMP_Text bannerName, bannerHint;
        GameObject controls;
        CanvasGroup card;
        TMP_Text cardSmall, cardLine1, cardLine2;
        GameObject cardFoot, cardHold;
        readonly (GameObject row, GameObject cap, TMP_Text key, TMP_Text words)[] cardHints = new (GameObject, GameObject, TMP_Text, TMP_Text)[2];
        Wedge cardHoldFill;
        float cardShown, cardAuto, cardFading = -1f;
        bool cardDismissable;
        TMP_FontAsset title, body, sketch, sketchBold; // sketch: Kalam, the sketch style (HUD, menus, cards; the log and banner keep Barlow)
        Sprite white;
        readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        // Enemy chips: a pool, filled each frame. Awareness 0..3 = Unaware, Searching, Startled, Combat; 4 = Broken.
        class Tag
        {
            public RectTransform rt;
            public GameObject label;
            public TMP_Text name, words, intent, glyph;
            public Octagon chipBg, chipBorder;
            public Slant labelBorder;
            public Image icon, eye, plate;
            public Image[] segs, segEdges;
            public GameObject notYet;
            public RectTransform chip;
            public Image ghost; // pops up over the chip when their fear rises (19.31)
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
            // Kalam (Editor/FontSetup builds it from Fonts/Kalam-*.ttf), else the old fonts.
            sketch = Resources.Load<TMP_FontAsset>("Fonts/Kalam SDF") ?? body;
            sketchBold = Resources.Load<TMP_FontAsset>("Fonts/Kalam-Bold SDF") ?? title;
            if (sketch != body && sketch.fontWeightTable.Length > 7) sketch.fontWeightTable[7].regularTypeface = sketchBold; // <b> in Kalam: Kalam Bold
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

            // ---- Health: a wedge (60% high at its left end) outlined in bone, filled bone; the number and the word ----
            var hp = hpBar = Rect("Health Bar", root, Vector2.zero, new Vector2(Left, HealthY + (28f - HpHeight) / 2f), new Vector2(HpWidth, HpHeight));
            hpFill = OutlinedWedge(hp, HpHeight, HpTaper, Bone);
            hpNum = Text(root, sketchBold, 28, TextAlignmentOptions.MidlineLeft, Vector2.zero, new Vector2(60, 28));
            Place(hpNum.rectTransform, Vector2.zero, new Vector2(Left + HpWidth + 10f, HealthY));
            hpNum.color = Bone;
            hpWord = SketchLabel(root, "Health", 15, Bone);
            Place(hpWord.rectTransform, Vector2.zero, new Vector2(Left + HpWidth + 50f, HealthY));
            hpWord.rectTransform.sizeDelta = new Vector2(90, 28);

            // ---- Legend bar: a wedge (30% high at its left end) outlined in bone, red fill, a notch every 10 ----
            bar = Rect("Legend Bar", root, Vector2.zero, new Vector2(Left, BarY), new Vector2(BarWidth, BarHeight));
            bar.localRotation = Quaternion.Euler(0f, 0f, -0.4f);
            legendFill = OutlinedWedge(bar, BarHeight, Taper, Blood);
            // A targeted kill's legend, not happened yet: hatched from your legend to where the kill lands.
            hatch = Draw<Wedge>(bar, Color.white, 2f); // the stripes, tiled every 12 px and fixed to the bar
            hatch.taper = InnerTaper(BarHeight, Taper);
            hatch.uvPerUnit = 1f / 12f;
            hatch.GetComponent<Image>().sprite = Sprite.Create(Stripes(), new Rect(0, 0, 12, 12), Vector2.zero);
            hatch.Span(0f, 0f);
            notchAt = skills.SelectMany(s => new[] { s.unlock, s.bloodied }).Where(v => v > 0).Distinct().OrderBy(v => v).ToArray();
            notches = new Image[notchAt.Length];
            notchLabels = new TMP_Text[notchAt.Length];
            for (int i = 0; i < notchAt.Length; i++)
            {
                float u = notchAt[i] / (float)maxLegend;
                var n = Rect("Notch " + notchAt[i], bar, Vector2.zero, new Vector2(u * BarWidth - 1.5f, 0), new Vector2(3, BarHeight * Mathf.Lerp(Taper, 1f, u)));
                notches[i] = n.gameObject.AddComponent<Image>();
                notches[i].sprite = white;
                var label = notchLabels[i] = Text(root, sketchBold, 13, TextAlignmentOptions.Center, Vector2.zero, new Vector2(40, 18));
                Place(label.rectTransform, Vector2.zero, new Vector2(Left + u * BarWidth - 20, LabelsY));
                label.text = notchAt[i].ToString();
            }
            // The big number and "LEGEND" under it, right of the skill row, tilted -2 degrees.
            legendGroup = Rect("Legend Number", root, Vector2.zero, new Vector2(716, 54), Vector2.zero); // moves with the stack
            legendGroup.localRotation = Quaternion.Euler(0f, 0f, -2f);
            // 19.30: the legend icon beside it in place of the word "LEGEND" under it (the number drops into its spot).
            legendNum = Text(legendGroup, sketchBold, 32, TextAlignmentOptions.BottomLeft, Vector2.zero, new Vector2(220, 100));
            Place(legendNum.rectTransform, Vector2.zero, Vector2.zero);
            legendIcon = LegendIcon(legendNum, 30);
            legendNum.textWrappingMode = TextWrappingModes.NoWrap; // "42 +30" on one line

            // ---- Skill tiles: octagon cards, each tilted its own way; the frame is dashed while there's no target ----
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i] = new Box();
                b.rt = Rect("Skill " + (i + 1), root, Vector2.zero, new Vector2(TileLeft + i * (TileW + TileGap), BoxY), new Vector2(TileW, TileH));
                b.rt.pivot = new Vector2(0.5f, 0.5f); // tilted about its middle
                b.rt.anchoredPosition += new Vector2(TileW, TileH) / 2f;
                b.rt.localRotation = Quaternion.Euler(0f, 0f, TileTilt[i]);
                b.card = Draw<Octagon>(b.rt, Bone, 0f);
                b.fill = Draw<Octagon>(b.rt, Bone, 0f);
                b.frame = Draw<OctFrame>(b.rt, Black, 0f);
                b.icon = Icon(b.rt, "skill_" + skills[i].shortName.ToLowerInvariant(), 38, 34);
                Place(b.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f));
                b.key = Text(b.rt, sketchBold, 16, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(24, 20));
                Place(b.key.rectTransform, new Vector2(0f, 1f), new Vector2(9f, -3f));
                b.key.text = (i + 1).ToString();
                b.name = Text(b.rt, sketchBold, 15, TextAlignmentOptions.Center, new Vector2(0, -26.5f), new Vector2(TileW, 18));
                b.name.fontStyle = FontStyles.UpperCase;
                b.name.characterSpacing = 3f;
                b.name.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
                b.name.text = skills[i].shortName;
                // Cooldown: a light black tint and a dark sweep from 12 o'clock that loses a step each turn, 4 px in,
                // and the turns left.
                b.cdTint = Draw<Octagon>(b.rt, Alpha(Black, 0.18f), 4f);
                b.cd = Draw<Octagon>(b.rt, Alpha(Black, 0.72f), 4f);
                b.cdTint.cut = b.cd.cut = 8f;
                b.wait = Text(b.rt, sketchBold, 40, TextAlignmentOptions.Center, Vector2.zero, new Vector2(TileW, 60));
                b.wait.color = Bone;
            }

            // ---- Status row: move pips, action pip, stance, sight, dragging, undo ----
            var line = movesLine = Row("Moves Line", root, 18f);
            Place(line, Vector2.zero, new Vector2(Left, MovesY));
            Fit(line);
            var moves = Row("Moves", line, 6f);
            var pips = Row("Pips", moves, 3f);
            movePips = new Octagon[moveRange];
            for (int i = 0; i < moveRange; i++) movePips[i] = Pip(pips, 16f);
            SketchLabel(moves, "Moves", 14, LockedText);
            var action = Row("Action", line, 6f);
            actionPip = Pip(action, 24f);
            SketchLabel(action, "Action", 14, LockedText);
            var state = Row("State", line, 18f);
            var stance = Row("Stance", state, 6f);
            var crouchCap = SketchCap(stance, "C", 14);
            stanceIcon = Icon(stance, "stand", 14, 16);
            stanceIcon.color = Bone;
            stanceWord = SketchLabel(stance, "Standing", 14, Bone);
            Button("crouch", stance, crouchCap.border, crouchCap.text, stanceWord);
            var sight = sightRow = Row("Sight", state, 6f);
            sightIcon = Icon(sight, "eye_slash", 18, 12);
            sightWord = SketchLabel(sight, "Unseen", 14, LockedText);
            // Hauling a body: red on purpose (you're dragging a corpse). A lying figure: the stand icon on its side.
            var drag = Row("Dragging", state, 6f);
            var corpseHolder = Rect("Corpse", drag, Vector2.zero, Vector2.zero, new Vector2(16, 16)); // laid out by the row; the icon inside it isn't
            Size(corpseHolder, 16, 16);
            var corpse = Icon(corpseHolder, "stand", 16, 16);
            Place(corpse.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, DragIconLift)); // turned on its side it sits low: lifted to the word
            corpse.color = Blood;
            corpse.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            SketchLabel(drag, "Dragging", 14, Blood);
            dragging = drag.gameObject;
            // Undo: the key while you can undo, the padlock once it's locked, nothing when there's nothing to undo.
            var undoRow = Row("Undo Key", state, 6f);
            var undoCap = SketchCap(undoRow, "Z", 14);
            Button("undo", undoRow, undoCap.border, undoCap.text, SketchLabel(undoRow, "Undo", 14, LockedText));
            undoKey = undoRow.gameObject;
            var lockRow = Row("Undo", state, 6f);
            Icon(lockRow, "lock", 12, 14).color = LockedText;
            SketchLabel(lockRow, "Undo", 14, LockedText);
            undo = lockRow.gameObject;

            // ---- Tooltip: above the status row, a parallelogram slanted 14 px, sized to its text, tilted -1 degree ----
            tip = Rect("Tooltip", root, Vector2.zero, new Vector2(16, TipY), new Vector2(364, 100));
            tip.localRotation = Quaternion.Euler(0f, 0f, -1f);
            tipBorder = Draw<Slant>(tip, Bone, 0f);
            Draw<Slant>(tip, Black, 3f).slant = 13f;
            IgnoreLayout(tip);
            var column = Column(tip, new RectOffset(30, 30, 12, 14), 4f);
            column.childAlignment = TextAnchor.UpperLeft;
            column.childForceExpandWidth = true;
            tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Top down (19.29): the status ("No target", "Ready"), the name, a kill's legend under it in red.
            tipStatus = Text(tip, sketch, 14, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipStatus.fontStyle = FontStyles.UpperCase;
            tipStatus.characterSpacing = 5f;
            tipStatus.color = Bone;
            tipName = Text(tip, sketchBold, 24, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipName.fontStyle = FontStyles.UpperCase;
            tipName.lineSpacing = -30f;
            tipLegend = Text(tip, sketchBold, 15, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipLegend.fontStyle = FontStyles.UpperCase;
            tipLegend.color = Bright;
            tipStatusIcon = LegendIcon(tipStatus, 15);
            tipLegendIcon = LegendIcon(tipLegend, 16);
            tipDesc = Text(tip, sketch, 16, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipUp = Text(tip, sketchBold, 16, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tipDesc.textWrappingMode = tipUp.textWrappingMode = TextWrappingModes.Normal;
            tipUpIcon = LegendIcon(tipUp, 16);

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

            // ---- Bottom right: the key hints (ESC MENU, H CONTROLS, L LOG; under them SPACE HOLD: END TURN), the log above ----
            var end = Row("End Turn", root, 6f);
            Place(end, Vector2.right, new Vector2(-16, 8));
            Fit(end);
            var spaceCap = Row("Key Space", end, 0f);
            spaceCap.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 0, 0);
            var spaceBorder = Img(spaceCap, LockedText, 0f);
            Img(spaceCap, Black, 1.5f);
            endFill = Img(spaceCap, Alpha(Blood, 0.85f), 1.5f); // fills left to right while held
            Filled(endFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            IgnoreLayout(spaceCap);
            var spaceWord = SketchLabel(spaceCap, "Space", 15, LockedText);
            spaceWord.gameObject.AddComponent<LayoutElement>().minHeight = 20;
            endWord = SketchLabel(end, "Hold: end turn", 15, LockedText);
            Glow(endWord);
            Button("end", end, spaceBorder, spaceWord, endWord); // a click ends the turn too
            var hint = Row("Controls Hint", root, 16f);
            Place(hint, Vector2.right, new Vector2(-16, 38));
            Fit(hint);
            foreach (var (key, caption, id) in new[] { ("Esc", "Menu", "pause"), ("H", "Controls", "controls"), ("L", "Log", "log") })
            {
                var b = Row(caption, hint, 6f);
                var cap = SketchCap(b, key, 15);
                var w = SketchLabel(b, caption, 15, LockedText);
                Glow(w);
                Button(id, b, cap.border, cap.text, w);
            }
            log = Text(root, body, 14, TextAlignmentOptions.BottomRight, Vector2.right, new Vector2(280, 200)); // clear of the legend number at 72 px
            Place(log.rectTransform, Vector2.right, new Vector2(-16, 78)); // the same gap above the key hints
            log.textWrappingMode = TextWrappingModes.Normal;
            log.color = Bone;
            Glow(log);

            // ---- Controls panel (H): the centre card's shape (19.27), key boxes and Kalam ----
            var panel = Rect("Controls", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 100));
            CardShape(panel);
            IgnoreLayout(panel);
            var rows = Column(panel, new RectOffset(48, 48, 18, 22), 7f);
            rows.childAlignment = TextAnchor.UpperLeft;
            Fit(panel);
            var head = Text(panel, sketchBold, 30, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            head.fontStyle = FontStyles.UpperCase;
            head.color = Bone;
            head.text = "Controls";
            head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
            ControlRows(panel, 15, false);
            var cx = CloseX(panel);
            Button("closecontrols", cx.rt, cx.x);
            controls = panel.gameObject;
            controls.SetActive(false);

            // Shrink the player's stack (everything above the health row) towards its bottom-left corner.
            var stack = Rect("Player Stack", root, Vector2.zero, StackOrigin, Vector2.zero);
            var parts = new[] { bar, legendGroup, movesLine, tip }
                .Concat(boxes.Select(b => b.rt)).Concat(notchLabels.Select(l => l.rectTransform)).ToList();
            foreach (var part in parts) part.SetParent(stack, true);
            stack.localScale = Vector3.one * StackScale;

            BuildTickets(root);
            BuildTurnCount(root);

            // ---- Intro / end card: the screen dimmed, a slanted card with a blood bar down its left edge ----
            var cardRoot = new GameObject("Card", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            cardRoot.SetParent(root, false);
            Stretch(cardRoot, 0f);
            card = cardRoot.GetComponent<CanvasGroup>();
            card.blocksRaycasts = false;
            Img(cardRoot, new Color(0.02f, 0.012f, 0.016f, 0.55f), 0f);
            var box = Rect("Box", cardRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 100));
            CardShape(box); // 19.27: the centre card's shape (the blood bar down its left edge is gone, for now)
            IgnoreLayout(box);
            var lines = Column(box, new RectOffset(50, 50, 22, 26), 6f);
            lines.childAlignment = TextAnchor.UpperLeft;
            Fit(box);
            cardSmall = SketchLabel(box, "", 16, LockedText);
            cardLine1 = Text(box, sketchBold, 46, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            cardLine2 = Text(box, sketchBold, 46, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            cardLine1.fontStyle = cardLine2.fontStyle = FontStyles.UpperCase;
            cardLine1.color = cardLine2.color = Bone;
            cardLine1.lineSpacing = cardLine2.lineSpacing = -30f;
            // The foot: up to two hints; one that starts with a single key ("R to ride again") shows it as a key box.
            var foot = Row("Foot", box, 18f);
            cardFoot = foot.gameObject;
            for (int i = 0; i < 2; i++)
            {
                var footHint = Row("Hint", foot, 6f);
                var cap = SketchCap(footHint, "", 15);
                cardHints[i] = (footHint.gameObject, cap.border.transform.parent.gameObject, cap.text, SketchLabel(footHint, "", 15, LockedText));
            }
            // The win card's hold to walk on: a HUD wedge (bone outline, dark inside, bone fill), filling while held.
            var hold = Rect("Hold", box, Vector2.zero, Vector2.zero, new Vector2(300, 12));
            Size(hold, 300, 12);
            cardHoldFill = OutlinedWedge(hold, 12f, Taper, Bright); // red fill, as before 19.27
            cardHold = hold.gameObject;
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
            close.anchoredPosition += new Vector2(-40f, 0f); // clear of the X
            var lx = CloseX(panel, lean * 0.5f + 8f);
            Button("closelog", lx.rt, lx.x);
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
            var hints = string.IsNullOrEmpty(foot) ? new string[0] : foot.Split(new[] { "  /  " }, System.StringSplitOptions.None);
            cardFoot.SetActive(hints.Length > 0);
            for (int i = 0; i < cardHints.Length; i++)
            {
                var h = cardHints[i];
                h.row.SetActive(i < hints.Length);
                if (i >= hints.Length) continue;
                int sp = hints[i].IndexOf(' ');
                bool key = sp == 1; // "R to ride again", "R or click to retry"
                h.cap.SetActive(key);
                h.key.text = key ? hints[i].Substring(0, 1) : "";
                h.words.text = key ? hints[i].Substring(2) : hints[i];
            }
            SetCardHold(-1f);
            cardShown = Time.unscaledTime;
            cardAuto = autoHide;
            cardDismissable = dismissable;
            cardFading = -1f;
            card.alpha = 1f;
            card.gameObject.SetActive(true);
        }

        // The card's hold bar at `k` (0..1), or hidden (k < 0).
        public void SetCardHold(float k)
        {
            cardHold.SetActive(k >= 0f);
            cardHoldFill.Span(0f, Mathf.Clamp01(k));
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
                // Locked (grey) beats holding a body (red DROP) beats no target (black, dashed) beats Bloodied (red)
                // and ready (bone). Cooling down isn't "no target": the tile keeps its look under the dark overlay.
                bool locked = !c.Has(id), bloodied = c.Bloodied(id), holding = id == SkillId.Drag && c.held != null;
                bool cooling = !locked && d.cooldown > 0 && c.Wait(id) > 0;
                bool noTarget = !locked && !cooling && !holding && !c.Usable(id);
                bool picked = selected == id || holding, over = i == hovered;
                Color card, ink, frame = Black;
                if (locked) { card = LockedBox; ink = LockedInk; }
                else if (holding) { card = Blood; ink = Bone; }
                else if (noTarget) { card = Black; ink = frame = bloodied ? Blood : Bone; }
                else if (bloodied) { card = Blood; ink = Bone; }
                else { card = Bone; ink = Black; }
                if (picked) frame = Blood; // selected (or holding a body): red, lifted 10 px
                else if (over) frame = Bone; // hovered: bone, solid, lifted 4 px
                b.card.color = card;
                b.icon.color = b.key.color = b.name.color = ink;
                b.frame.color = frame;
                b.frame.gap = noTarget && !picked && !over ? 5f : 0f;
                b.name.text = holding ? "Drop" : d.shortName;
                float lift = picked ? 10f : over ? 4f : 0f;
                b.rt.anchoredPosition = new Vector2(TileLeft + i * (TileW + TileGap) + TileW / 2f, BoxY + TileH / 2f + lift) - StackOrigin; // inside the stack
                // What it becomes next, rising from the bottom: toward the unlock (bone, or red for skills that unlock
                // already Bloodied), then red toward Bloodied.
                float fill = 0f;
                if (locked) { fill = d.unlock > 0 ? L / (float)d.unlock : 0f; b.fill.color = d.bloodied == d.unlock ? Blood : Bone; }
                else if (!bloodied && d.bloodied > d.unlock) { fill = (L - d.unlock) / (float)(d.bloodied - d.unlock); b.fill.color = Blood; }
                b.fill.fill = Mathf.Clamp01(fill);
                // Cooldown: the tint, the dark sweep (turns left of the cooldown) and the turns left.
                int wait = locked || d.cooldown <= 0 ? 0 : Mathf.Min(c.Wait(id), d.cooldown);
                b.cdTint.shown = b.cd.shown = wait > 0;
                b.cd.sweep = wait / (float)Mathf.Max(1, d.cooldown);
                b.wait.enabled = wait > 0;
                b.wait.text = wait.ToString();
            }

            legendFill.Span(0f, L / (float)maxLegend);
            int to = Mathf.Min(maxLegend, L + legendGain);
            hatch.Span(L / (float)maxLegend, to / (float)maxLegend); // nothing when no kill is targeted
            for (int i = 0; i < notchAt.Length; i++)
                notches[i].color = notchLabels[i].color = L >= notchAt[i] ? Bone : notchAt[i] <= to ? Bright : NotchAhead;
            float k = L / (float)maxLegend;
            legendNum.text = legendGain > 0 ? $"{L}<size=60%><color=#E0202F> +{legendGain}</color></size>" : L.ToString();
            legendNum.fontSize = Mathf.Round(32f + 40f * k);
            legendNum.color = k >= 1f ? Hex("7A0618") : k >= 0.75f ? Hex("9E0B22") : k >= 0.5f ? Blood : k >= 0.25f ? Bright : Bone;
            Pin(legendIcon, legendNum, legendGain > 0 ? 26f : 30f); // after the number, or after "+30" while a kill is targeted

            tip.gameObject.SetActive(hovered >= 0);
            if (hovered >= 0)
            {
                var d = skills[hovered];
                var id = (SkillId)hovered;
                bool locked = !c.Has(id), bloodied = c.Bloodied(id), usable = c.Usable(id);
                tipBorder.color = locked ? Grey : bloodied ? Bright : Bone; // the outline keeps the game's state colour
                tipName.color = locked ? TipGrey : bloodied ? Bright : Bone;
                // A kill's legend, small and red under its name.
                Act? kill = id == SkillId.Stealth ? Act.QuietKill : id == SkillId.Rail ? Act.RailGrab : id == SkillId.Brutal ? Act.BrutalKill : (Act?)null;
                tipName.text = d.name;
                tipLegend.gameObject.SetActive(kill.HasValue);
                if (kill.HasValue) { tipLegend.text = $"+{c.LegendGain(kill.Value)}"; Pin(tipLegendIcon, tipLegend, 16f); }
                int cool = c.Wait(id);
                tipStatus.text = locked ? $"Unlocks at {d.unlock}" : cool > 0 ? $"Ready in {cool}" : !usable ? (c.WhyNot(id) ?? "No target") : bloodied ? "Bloodied" : "Ready";
                tipDesc.text = d.description;
                tipDesc.color = locked ? TipGrey : Bone;
                bool upgrade = d.bloodied > d.unlock && !string.IsNullOrEmpty(d.upgrade);
                tipUp.gameObject.SetActive(upgrade);
                string at = $"At {d.bloodied}"; // then the icon, in a gap left for it, then ": ..."
                tipUp.text = upgrade ? $"{at}<space={16f + 2f * IconGap}>: {d.upgrade}" : "";
                tipUp.color = bloodied ? Bright : TipBurgundy;
                if (locked) Pin(tipStatusIcon, tipStatus, 15f); else tipStatusIcon.enabled = false;
                if (upgrade) Pin(tipUpIcon, tipUp, 16f, at.Length - 1); else tipUpIcon.enabled = false;
            }

            int hp = shownHp >= 0 ? shownHp : c.hp; // held back while a heavy's swing is still on its way
            hpFill.Span(0f, hp / (float)c.t.maxHp);
            hpNum.text = hp.ToString();
            hpWord.rectTransform.anchoredPosition = new Vector2(Left + HpWidth + 10f + hpNum.preferredWidth + 10f, HealthY);

            for (int i = 0; i < movePips.Length; i++) movePips[i].shown = i < c.movesLeft; // filled while unspent, hollow when spent
            actionPip.shown = !(c.acted || c.turnOver);
            stanceIcon.sprite = IconSprite(c.crouched ? "crouch" : "stand");
            stanceWord.text = c.crouched ? "Crouched" : "Standing";
            bool seen = c.SeenByAnyone(c.player);
            sightIcon.sprite = IconSprite(seen ? "eye" : "eye_slash");
            sightIcon.color = sightWord.color = seen ? Bright : LockedText;
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

        public const float FearPopHold = 2f, FearPopFade = 0.5f; // the fear ghost: seconds shown, then its fade

        public void BeginTags() => tagCount = 0;

        public void EndTags()
        {
            for (int i = tagCount; i < tags.Count; i++) tags[i].rt.gameObject.SetActive(false);
        }

        // One enemy's chip (and, when hovered, its label above it), bottom centre at `screen` (pixels).
        // awareness: 0 Unaware, 1 Searching, 2 Startled, 3 Combat, 4 Broken. id: the enemy's, for its fixed tilt.
        // threat: they'll fire on what you're aiming (the label's edge red). danger: what they'll do next can hurt you
        // (a shot, a charge or swing, wild fire: the intent line red). icon: what they'll do if you end your turn there
        // ("crosshair", "charge", "wildfire" or null). plate: a tough's armour, 1 whole, 2 cracked (killable).
        // notYet: "Not yet" over a tough you're targeting before it's Shaken.
        public RectTransform AddTag(Vector2 screen, float scale, int awareness, int terrorNow, bool seen, bool hovered, string name, string role, string words, string intent,
            bool threat = false, string icon = null, int plate = 0, bool notYet = false, bool danger = false, int id = 0, float fearAge = -1f)
        {
            if (tagCount == tags.Count) tags.Add(MakeTag());
            var g = tags[tagCount++];
            g.rt.gameObject.SetActive(true);
            g.rt.position = screen;
            g.rt.localScale = Vector3.one * scale;
            g.chip.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-1.2f, 1.1f, (id * 7919 % 23) / 22f)); // fixed per enemy
            // What they know, by colour: black (bone frame) unaware, bone (black frame) searching or startled, red (black
            // frame) in combat; dimmed when broken. The icon is what they'll do.
            bool combat = awareness == 3, broken = awareness == 4, white = awareness == 1 || awareness == 2;
            g.chipBg.color = combat ? Blood : white ? Bone : broken ? Alpha(Black, 0.7f) : Black;
            g.chipBorder.color = combat || white ? Black : broken ? LockedBox : Bone;
            var ink = white ? Black : broken ? Grey : Bone;
            // Startled: "!". Searching: "?". A panicked freeze turn: the ghost, frozen with fear.
            if (icon == "freeze") icon = "ghost";
            bool glyph = (awareness == 1 || awareness == 2) && icon == null;
            g.glyph.gameObject.SetActive(glyph);
            g.glyph.text = awareness == 2 ? "!" : "?";
            g.glyph.color = ink;
            g.icon.enabled = !glyph && (broken || icon != null || awareness == 0);
            if (g.icon.enabled) g.icon.sprite = IconSprite(broken ? "flag" : icon ?? "ring");
            g.icon.color = ink;
            for (int i = 0; i < 3; i++) g.segEdges[i].color = white ? Black : Bone; // the red fill always reads
            g.plate.gameObject.SetActive(plate > 0);
            if (plate > 0) g.plate.sprite = IconSprite(plate == 2 ? "plate_cracked" : "plate");
            g.plate.color = ink;
            g.notYet.SetActive(notYet);
            // fearAge: seconds since their fear last rose (-1: none). A quick pop, held, then a fade.
            float ghostA = fearAge < 0f ? 0f : 1f - Mathf.Clamp01((fearAge - FearPopHold) / FearPopFade);
            g.ghost.transform.parent.gameObject.SetActive(ghostA > 0f);
            g.ghost.color = Alpha(Bone, ghostA);
            g.ghost.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(fearAge / 0.15f));
            // Three boxes, one per stage (Shaken, Panicked, Broken), each filling smoothly within its stage.
            for (int i = 0; i < 3; i++)
                g.segs[i].fillAmount = broken ? 1f : Mathf.Clamp01((terrorNow - terror[i]) / (float)(terror[i + 1] - terror[i]));
            g.eye.gameObject.SetActive(seen);
            g.eye.color = ink;
            g.label.SetActive(hovered);
            if (hovered)
            {
                g.name.text = $"<b><size=17>{name}</size></b><size=14><color=#B4AEAF> · {role}</color></size>";
                g.words.text = words;
                g.intent.text = intent;
                g.intent.gameObject.SetActive(!string.IsNullOrEmpty(intent));
                g.intent.color = danger || threat ? Bright : LockedText;
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
            var stack = Column(g.rt, new RectOffset(0, 0, 0, 0), 8f);
            stack.childAlignment = TextAnchor.LowerCenter;
            Fit(g.rt);

            // Hover label, like the skill tooltip: a card slanted 10 px, tilted -1 degree. Name and role, then the
            // state line, then what they'll do.
            var label = Rect("Label", g.rt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            label.localRotation = Quaternion.Euler(0f, 0f, -1f);
            g.labelBorder = Draw<Slant>(label, Bone, 0f);
            g.labelBorder.slant = 10f;
            Draw<Slant>(label, Black, 2f).slant = 9f;
            IgnoreLayout(label);
            Column(label, new RectOffset(22, 22, 6, 7), 1f).childAlignment = TextAnchor.UpperCenter;
            g.name = Text(label, sketch, 17, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero); // Kalam; <b> uses its Bold
            g.name.color = Bone;
            g.words = Text(label, sketchBold, 14, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            g.words.color = Bone;
            g.intent = Text(label, sketchBold, 14, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            g.label = label.gameObject;

            // "Not yet": over a tough you're targeting before it's Shaken. A card slanted 6 px, red edge, tilted +1.
            var notYet = Row("Not Yet", g.rt, 0f);
            notYet.localRotation = Quaternion.Euler(0f, 0f, 1f);
            notYet.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(14, 14, 2, 3);
            Draw<Slant>(notYet, Blood, 0f).slant = 6f;
            Draw<Slant>(notYet, Black, 2f).slant = 5f;
            IgnoreLayout(notYet);
            var ny = Text(notYet, sketchBold, 14, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            ny.fontStyle = FontStyles.UpperCase;
            ny.color = Bone;
            ny.text = "Not yet";
            g.notYet = notYet.gameObject;

            // Fear rose: the ghost pops up over the chip, holds, fades.
            var ghost = Rect("Fear", g.rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30, 30));
            Size(ghost, 30, 30);
            g.ghost = Img(ghost, Bone, 0f);
            g.ghost.sprite = IconSprite("ghost");
            g.ghost.preserveAspect = true;

            // Chip: an octagon (6 px corners) 30 high: the state icon, three fear boxes, the plate (toughs), the eye.
            var chip = g.chip = Row("Chip", g.rt, 6f);
            chip.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(9, 9, 6, 6);
            g.chipBorder = Draw<Octagon>(chip, LockedBox, 0f);
            g.chipBorder.cut = 6f;
            g.chipBg = Draw<Octagon>(chip, Black, 2f);
            g.chipBg.cut = 6f - 2f * 0.4142f;
            IgnoreLayout(chip);
            var slot = Rect("Awareness", chip, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            Size(slot, 18, 18);
            g.icon = Img(slot, Grey, 0f);
            g.icon.preserveAspect = true;
            g.glyph = Text(slot, sketchBold, 20, TextAlignmentOptions.Center, Vector2.zero, new Vector2(18, 24));
            var segs = Row("Terror", chip, 3f);
            g.segs = new Image[3];
            g.segEdges = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var seg = Rect("Segment", segs, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 9));
                Size(seg, 14, 9);
                g.segEdges[i] = Img(seg, Bone, 0f); // straight, not skewed
                Img(seg, Black, 1.5f);
                g.segs[i] = Img(seg, Blood, 1.5f);
                Filled(g.segs[i], Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            }
            g.plate = Icon(chip, "plate", 14, 16);
            g.eye = Icon(chip, "eye", 18, 12);
            return g;
        }

        // ---- Building blocks ----

        // Diagonal stripes, #E0202F on #4A0C15, tiling every 12 px.
        static Texture2D Stripes()
        {
            var tex = new Texture2D(12, 12) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var dark = Hex("4A0C15");
            for (int x = 0; x < 12; x++)
                for (int y = 0; y < 12; y++)
                    tex.SetPixel(x, y, (x + y) % 12 < 5 ? Bright : dark);
            tex.Apply();
            return tex;
        }

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

        // A pip (moves, action) at w x 9, skewed 12 degrees: a 1.5 px bone outline, and the returned fill (shown while unspent).
        Octagon Pip(Transform parent, float w)
        {
            var rt = Rect("Pip", parent, Vector2.zero, Vector2.zero, new Vector2(w, 9));
            Size(rt, w, 9);
            var edge = Draw<DashedFrame>(rt, Bone, 0f);
            edge.thickness = 1.5f;
            edge.dash = 100000f; // one unbroken stroke per side
            edge.gameObject.AddComponent<UISkew>().degrees = 12f;
            var fill = Draw<Octagon>(rt, Bone, 0f);
            fill.cut = 0f; // a plain rectangle, edges smoothed
            fill.gameObject.AddComponent<UISkew>().degrees = 12f;
            return fill;
        }

        // A UIShape (Octagon, OctFrame, Slant, ...) filling its parent, `inset` in from the edges.
        T Draw<T>(Transform parent, Color col, float inset) where T : UIShape
        {
            var rt = new GameObject(typeof(T).Name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt, inset);
            var shape = rt.gameObject.AddComponent<T>();
            shape.color = col;
            shape.raycastTarget = false;
            return shape;
        }

        // A wedge's taper for the area 2 px inside its outline, so the slanted top edge stays 2 px thick.
        static float InnerTaper(float height, float taper) => Mathf.Max(0f, (taper * height - 4f) / (height - 4f));

        // A bar (the legend, health): a wedge `taper` high at its left end, a 2 px bone outline round a dark inside;
        // returns the fill (left to right) inside the outline.
        Wedge OutlinedWedge(RectTransform bar, float height, float taper, Color fill)
        {
            Draw<Wedge>(bar, Bone, 0f).taper = taper;
            float inner = InnerTaper(height, taper);
            Draw<Wedge>(bar, BarBack, 2f).taper = inner;
            var f = Draw<Wedge>(bar, fill, 2f);
            f.taper = inner;
            return f;
        }

        // Kalam, uppercase, a little letter-spaced (the sketch HUD's labels: MOVES, HEALTH, key hints).
        TMP_Text SketchLabel(Transform parent, string text, float size, Color col)
        {
            var t = Text(parent, sketch, size, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            t.fontStyle = FontStyles.UpperCase;
            t.characterSpacing = 5f;
            t.color = col;
            t.text = text;
            return t;
        }

        // The sketch HUD's key cap: the key in Kalam in a 1.5 px #B4AEAF box.
        (Image border, TMP_Text text) SketchCap(Transform parent, string key, float size)
        {
            var cap = Row("Key " + key, parent, 0f);
            cap.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(6, 6, 0, 0);
            var border = Img(cap, LockedText, 0f);
            Img(cap, Black, 1.5f);
            IgnoreLayout(cap);
            var t = SketchLabel(cap, key, size, LockedText);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 20;
            return (border, t);
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

        // The close button every panel has in its top-right corner: a red X on black with a white border (the X turns
        // white under the mouse, red again while pressed). Out of the panel's layout.
        (RectTransform rt, TMP_Text x) CloseX(RectTransform panel, float inset = 10f)
        {
            var x = Rect("Close X", panel, Vector2.one, new Vector2(-inset, -inset), new Vector2(28, 28));
            x.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Img(x, Bone, 0f);
            Img(x, Black, 2f);
            var t = Text(x, title, 22, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            Stretch(t.rectTransform, 0f);
            t.text = "X";
            t.color = Blood;
            return (x, t);
        }

        HudButton ButtonUnder(Vector2 screen) => hudCanvas.enabled
            ? buttons.LastOrDefault(b => b.rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(b.rt, screen, null)) : null;

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
