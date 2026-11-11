using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RailTracer
{
    // A menu line with the sketch-style hover (Update 19.27), the same on every menu, card and tutorial button.
    // Hovered: a red slash just left of the text grows up from its bottom, holds, fades; a blood-red brush band wipes
    // in from its left end; the text turns bone, steps 10 px right and grows from its left edge (a note on the line
    // goes with it). Leaving plays the band and text back in reverse; the slash doesn't replay. Real time throughout.
    // The swipe is the line's height plus 2 px and only ever grows in length: on screen it runs from 14 px left of the
    // text to `past` beyond its end (notes included); in a card it spans the line's width, 12 px past each side.
    public class MenuLine : MonoBehaviour
    {
        // Game's "Hover ..." fields, copied in every frame.
        public static float slashTime = 0.42f, swipeDelay = 0.06f, swipeTime = 0.15f, grow = 1.2f, swipePast = 100f;
        public static Action onSlash; // the hover's sound
        const float ColourTime = 0.12f, MoveTime = 0.16f, Step = 10f, Lead = 14f, CardPast = 12f, NoteGap = 8f;
        static readonly Color Bone = new Color32(0xF2, 0xEF, 0xEA, 0xFF);

        public TMP_Text label, note;
        public Brush swipe;
        public Slash slash;
        public Color idle, noteIdle;
        public bool hot;          // set by the owner each frame (or by the mouse, with autoHover)
        public bool autoHover;    // hovered whenever the mouse is over it
        public bool card;         // the swipe spans the line's width
        public float past = -1f;  // px the swipe runs past the text on screen (-1: swipePast)
        public float height;      // the line's height (the swipe is 2 px taller)
        float u, s = -1f, textW, labelW;
        bool wasHot;
        string shownLabel, shownNote;

        public RectTransform rt => (RectTransform)transform;

        // The label and an optional note ("" or null: none). Measured at normal size when they change.
        public void Set(string text, string noteText = null)
        {
            noteText ??= "";
            if (text == shownLabel && noteText == shownNote) return;
            shownLabel = label.text = text;
            shownNote = note.text = noteText;
            note.gameObject.SetActive(noteText.Length > 0);
            labelW = label.GetPreferredValues(text).x;
            textW = labelW + (noteText.Length > 0 ? NoteGap + note.GetPreferredValues(noteText).x : 0f);
            label.rectTransform.sizeDelta = new Vector2(labelW, height);
            note.rectTransform.sizeDelta = new Vector2(textW - labelW, height);
            var e = GetComponent<LayoutElement>();
            if (e) e.preferredWidth = textW; // a card line also stretches to the card's width
            if (card) return;
            if (e) e.minWidth = textW;
            rt.sizeDelta = new Vector2(textW, height); // for a line outside a layout (the corner line, car names)
        }

        public bool Over(Vector2 screen) => gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        void OnDisable() { u = 0f; s = -1f; wasHot = hot = false; Apply(); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (autoHover) hot = Mouse.current != null && Over(Mouse.current.position.ReadValue());
            if (hot && !wasHot) { s = 0f; onSlash?.Invoke(); }
            wasHot = hot;
            u = Mathf.Clamp(u + (hot ? dt : -dt), 0f, swipeDelay + Mathf.Max(swipeTime, MoveTime));
            if (s >= 0f && (s += dt) >= slashTime) s = -1f;
            Apply();
        }

        static float Out(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

        void Apply()
        {
            if (!label) return;
            float t = u - swipeDelay;
            float c = Mathf.Clamp01(t / ColourTime), m = Out(t / MoveTime), k = Out(t / swipeTime);
            float scale = Mathf.Lerp(1f, grow, m);
            label.color = Color.Lerp(idle, Bone, c);
            note.color = Color.Lerp(noteIdle, Bone, c);
            label.rectTransform.anchoredPosition = new Vector2(Step * m, 0f);
            label.rectTransform.localScale = Vector3.one * scale;
            note.rectTransform.anchoredPosition = new Vector2(Step * m + labelW * scale + NoteGap * scale, 0f);
            note.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1f + (grow - 1f) / 2f, m);
            var sw = swipe.rectTransform;
            sw.anchoredPosition = new Vector2(card ? -CardPast : -Lead, 0f);
            sw.sizeDelta = new Vector2(card ? rt.rect.width + 2f * CardPast : Lead + textW + (past >= 0f ? past : swipePast), height + 2f);
            swipe.fill = k;
            swipe.shown = k > 0f;
            // The slash: grows over the first 20% of its time, holds to 55%, fades out by the end.
            float q = s < 0f ? 1f : s / slashTime;
            slash.shown = s >= 0f;
            slash.rectTransform.localScale = new Vector3(1f, Mathf.Clamp01(q / 0.2f), 1f);
            slash.color = new Color(slash.color.r, slash.color.g, slash.color.b, q < 0.55f ? 1f : 1f - (q - 0.55f) / 0.45f);
        }
    }

    // The hover's brush band: ragged, uneven ends and broken bristle marks at its right end, drawn from its left end
    // up to `fill` of its length. Its ends are fixed in px, so a longer band is only longer, never thicker.
    public class Brush : UIShape
    {
        public int seed;
        float fillTo = 1f;
        public float fill { get => fillTo; set { value = Mathf.Clamp01(value); if (value != fillTo) { fillTo = value; Redraw(); } } }

        float Noise(int i) { float x = Mathf.Sin((i + 1) * 12.9898f + seed * 78.233f) * 43758.5453f; return (x - Mathf.Floor(x)) * 2f - 1f; }

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            float L = r.width, H = r.height, cut = fillTo * L;
            if (cut < 0.5f || L < 50f) return;
            float bodyEnd = L - 14f;
            // Columns along the band: (x, top inset, bottom inset) as shares of the height.
            var cols = new List<Vector3> { new Vector3(0f, .30f, .20f), new Vector3(3f, .06f, .38f), new Vector3(6f, .16f, .08f), new Vector3(10f, .02f, .04f) };
            int n = 0;
            for (float x = 22f; x < bodyEnd - 12f; x += 14f, n++) cols.Add(new Vector3(x, .03f + .03f * Noise(n), .03f + .03f * Noise(n + 50)));
            cols.Add(new Vector3(bodyEnd - 8f, .12f, .04f));
            cols.Add(new Vector3(bodyEnd - 4f, .04f, .30f));
            cols.Add(new Vector3(bodyEnd, .36f, .22f));
            Strip(vh, col, r, cols, cut);
            // Bristles off the right end: (bottom, top as shares of the height, start, end in px from the right end).
            foreach (var b in new[] { new Vector4(.80f, .93f, 34f, 1f), new Vector4(.47f, .57f, 24f, 5f), new Vector4(.08f, .21f, 38f, 3f) })
            {
                float x0 = L - b.z, x1 = Mathf.Min(cut, L - b.w);
                if (x1 - x0 < 1f) continue;
                Strip(vh, col, r, new List<Vector3> { new Vector3(x0, 1f - b.y, b.x), new Vector3(x1, 1f - b.y + .03f, b.x + .02f) }, cut);
            }
        }

        // Quads between neighbouring columns, cut off at `cut`, top and bottom edges faded over one screen pixel.
        void Strip(VertexHelper vh, Color32 col, Rect r, List<Vector3> cols, float cut)
        {
            var clear = new Color32(col.r, col.g, col.b, 0);
            float h = px / 2f;
            Vector3? prev = null;
            foreach (var c in cols)
            {
                var cur = c;
                bool last = false;
                if (cur.x > cut)
                {
                    if (prev == null) return;
                    float f = (cut - prev.Value.x) / (cur.x - prev.Value.x);
                    cur = Vector3.Lerp(prev.Value, cur, f);
                    last = true;
                }
                if (prev != null)
                {
                    var p = prev.Value;
                    float aTop = r.yMax - p.y * r.height, aBot = r.yMin + p.z * r.height, bTop = r.yMax - cur.y * r.height, bBot = r.yMin + cur.z * r.height;
                    float ax = r.xMin + p.x, bx = r.xMin + cur.x;
                    Quad4(vh, ax, aBot - h, aBot + h, bx, bBot - h, bBot + h, clear, col);
                    Quad4(vh, ax, aBot + h, aTop - h, bx, bBot + h, bTop - h, col, col);
                    Quad4(vh, ax, aTop - h, aTop + h, bx, bTop - h, bTop + h, col, clear);
                }
                if (last) return;
                prev = cur;
            }
        }

        static void Quad4(VertexHelper vh, float ax, float ay0, float ay1, float bx, float by0, float by1, Color32 c0, Color32 c1)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = c0; v.position = new Vector3(ax, ay0); vh.AddVert(v); v.position = new Vector3(bx, by0); vh.AddVert(v);
            v.color = c1; v.position = new Vector3(bx, by1); vh.AddVert(v); v.position = new Vector3(ax, ay1); vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    // The hover's knife slash: a rough stroke leaning right (the mockup's 24 x 60 path, fitted to the rect).
    public class Slash : UIShape
    {
        static readonly Vector2[] Path = { new(16, 0), new(23, 2), new(21, 9), new(22, 12), new(9, 58), new(1, 60), new(3, 51), new(2, 47) };

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            var pts = new Vector2[Path.Length];
            var hub = Vector2.zero;
            for (int i = 0; i < Path.Length; i++)
            {
                pts[i] = new Vector2(r.xMin + Path[i].x / 24f * r.width, r.yMax - Path[i].y / 60f * r.height);
                hub += pts[i] / Path.Length;
            }
            FanAA(vh, col, pts, hub);
        }
    }

    public partial class SkillHud
    {
        static readonly Color Idle = Hex("D9D3CF");
        // Menu lines alternate their tilts down a list.
        static readonly float[] LineTilt = { -0.8f, 0.6f, -0.4f, 0.8f, -0.6f };
        int lineSeed;

        // A menu line: Kalam Bold `size` (its note `noteSize`), `pad` px above and below, the i-th tilt of its list.
        // card: the swipe spans the line's width (set by its column).
        MenuLine Line(Transform parent, float size, float noteSize, float pad, int i, bool card = false, Color? idle = null)
        {
            float h = Mathf.Round(size * 1.15f + 2f * pad);
            var rt = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(100f, h);
            rt.localRotation = Quaternion.Euler(0f, 0f, LineTilt[i % LineTilt.Length]);
            var e = rt.gameObject.AddComponent<LayoutElement>();
            e.minHeight = e.preferredHeight = h;
            if (card) e.flexibleWidth = 1f;
            var line = rt.gameObject.AddComponent<MenuLine>();
            line.height = h;
            line.card = card;
            line.idle = idle ?? Idle;
            line.noteIdle = LockedText;
            RectTransform Child(string name)
            {
                var c = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                c.SetParent(rt, false);
                c.anchorMin = c.anchorMax = c.pivot = new Vector2(0f, 0.5f);
                return c;
            }
            var sw = Child("Swipe");
            line.swipe = sw.gameObject.AddComponent<Brush>();
            line.swipe.GetComponent<Image>().sprite = white;
            line.swipe.color = Blood;
            line.swipe.raycastTarget = false;
            line.swipe.seed = lineSeed++;
            var sl = Child("Slash");
            sl.pivot = Vector2.zero; // grows up from its bottom
            sl.sizeDelta = new Vector2(24f, h + 18f);
            sl.anchoredPosition = new Vector2(-26f, -(h + 18f) / 2f);
            line.slash = sl.gameObject.AddComponent<Slash>();
            line.slash.GetComponent<Image>().sprite = white;
            line.slash.color = Bright;
            line.slash.raycastTarget = false;
            TMP_Text Words(string name, float s)
            {
                var t = Text(rt, sketchBold, s, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
                t.name = name;
                var trt = t.rectTransform;
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 0.5f); // grows from its left edge
                t.fontStyle = FontStyles.UpperCase;
                t.characterSpacing = 1f;
                Glow(t);
                return t;
            }
            line.label = Words("Label", size);
            line.note = Words("Note", noteSize);
            line.Set("", "");
            line.slash.shown = line.swipe.shown = false;
            return line;
        }

        // The centre card's shape (the HUD tooltip's): a parallelogram slanted 16 px, a 3 px bone edge round a black
        // inside, tilted -1 degree. Call before adding the card's contents (then IgnoreLayout).
        void CardShape(RectTransform rt)
        {
            Draw<Slant>(rt, Bone, 0f).slant = 16f;
            Draw<Slant>(rt, Black, 3f).slant = 15f;
            rt.localRotation = Quaternion.Euler(0f, 0f, -1f);
        }

        // A key hint: the key in the sketch key box, then what it does.
        RectTransform KeyHint(Transform parent, string key, string what, out TMP_Text words)
        {
            var row = Row("Hint", parent, 6f);
            SketchCap(row, key, 15);
            words = SketchLabel(row, what, 15, LockedText);
            return row;
        }
    }
}
