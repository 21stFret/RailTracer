using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RailTracer
{
    // A menu line with the sketch-style hover (Update 19.27), the same on every menu, card and tutorial button.
    // Hovered: a red slash just left of the text grows up from its bottom, holds, fades; Alex's brush stripe paints
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
        public Image swipe;       // Alex's stripe (Resources/UI/stripe), sliced: its ragged ends keep their shape
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
            sw.sizeDelta = new Vector2(sw.sizeDelta.x * k, sw.sizeDelta.y); // painted in from its left end
            swipe.enabled = k > 0f;
            // The slash: grows over the first 20% of its time, holds to 55%, fades out by the end.
            float q = s < 0f ? 1f : s / slashTime;
            slash.shown = s >= 0f;
            slash.rectTransform.localScale = new Vector3(1f, Mathf.Clamp01(q / 0.2f), 1f);
            slash.color = new Color(slash.color.r, slash.color.g, slash.color.b, q < 0.55f ? 1f : 1f - (q - 0.55f) / 0.45f);
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
        static Sprite stripeSprite;

        // The hover band (19.49): Alex's brush stripe, sliced so its ragged ends (the first 50 and last 60 px of the
        // image) keep their shape and only the middle stretches. Null if the image is missing (a plain band then).
        static Sprite StripeSprite()
        {
            if (stripeSprite) return stripeSprite;
            var tex = Resources.Load<Texture2D>("UI/stripe");
            return tex ? stripeSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(50f, 0f, 60f, 0f)) : null;
        }

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
            line.swipe = sw.gameObject.AddComponent<Image>();
            var stripe = StripeSprite();
            line.swipe.sprite = stripe ? stripe : white;
            line.swipe.color = stripe ? Color.white : Blood; // the stripe has its own red (Blood)
            line.swipe.type = Image.Type.Sliced;
            if (stripe) line.swipe.pixelsPerUnitMultiplier = stripe.rect.height / (h + 2f); // ends scaled with the band's height
            line.swipe.raycastTarget = false;
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
            line.slash.shown = false;
            line.swipe.enabled = false;
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

        const float IconGap = 4f; // px either side of the legend icon

        // The legend icon (19.30): blood red art, never tinted; placed with Pin.
        Image LegendIcon(TMP_Text text, float size)
        {
            var icon = Icon(text.rectTransform, "legend", size, size);
            icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.GetComponent<LayoutElement>().ignoreLayout = true;
            icon.enabled = false;
            return icon;
        }

        // Puts `icon` (`size` px square) just after character `after` of `t` (its last one when -1), its centre
        // halfway up the capitals of that character's size, never on the baseline.
        static void Pin(Image icon, TMP_Text t, float size, int after = -1)
        {
            icon.enabled = false;
            if (!t.gameObject.activeInHierarchy) return;
            t.ForceMeshUpdate();
            var info = t.textInfo;
            if (after < 0) after = info.characterCount - 1;
            if (after < 0 || after >= info.characterCount) return;
            var ch = info.characterInfo[after];
            var face = t.font.faceInfo;
            float cap = face.capLine * ch.pointSize / face.pointSize;
            icon.rectTransform.sizeDelta = new Vector2(size, size);
            icon.rectTransform.localPosition = new Vector3(ch.xAdvance + IconGap + size / 2f, ch.baseLine + cap / 2f, 0f);
            icon.enabled = true;
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
