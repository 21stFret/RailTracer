using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailTracer
{
    // A ticket: a rectangle with a V notch cut into the middle of each long side, filled with the Image's colour.
    public class TicketShape : UIShape
    {
        static readonly Vector2[] Outline =
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0.4f), new Vector2(0.8f, 0.5f), new Vector2(1, 0.6f),
            new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0.6f), new Vector2(0.2f, 0.5f), new Vector2(0, 0.4f),
        };

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            var v = UIVertex.simpleVert;
            v.color = col;
            v.position = r.center;
            vh.AddVert(v); // a fan from the centre: every edge of the notched outline can see it
            foreach (var p in Outline)
            {
                v.position = new Vector3(r.xMin + p.x * r.width, r.yMin + p.y * r.height);
                vh.AddVert(v);
            }
            for (int i = 0; i < Outline.Length; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % Outline.Length);
        }
    }

    // Everything but the holes, in this rect's own coordinates (pivot at the bottom left).
    public class DimHoles : UIShape
    {
        public readonly List<Rect> holes = new List<Rect>();

        public void Set(IEnumerable<Rect> rects)
        {
            holes.Clear();
            holes.AddRange(rects);
            Redraw();
        }

        protected override void Build(VertexHelper vh, Rect r, Color32 col)
        {
            // Cut the screen into a grid at every hole edge and fill each cell no hole covers.
            var xs = holes.SelectMany(h => new[] { h.xMin, h.xMax }).Append(r.xMin).Append(r.xMax).Select(x => Mathf.Clamp(x, r.xMin, r.xMax)).Distinct().OrderBy(x => x).ToList();
            var ys = holes.SelectMany(h => new[] { h.yMin, h.yMax }).Append(r.yMin).Append(r.yMax).Select(y => Mathf.Clamp(y, r.yMin, r.yMax)).Distinct().OrderBy(y => y).ToList();
            for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < ys.Count; j++)
                {
                    var mid = new Vector2((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
                    if (!holes.Exists(h => h.Contains(mid))) Quad(vh, col, xs[i], ys[j], xs[i + 1], ys[j + 1]);
                }
        }
    }

    // Tickets (top right, and on the win card), tutorial cards with their highlight, and the pause menu.
    public partial class SkillHud
    {
        static readonly Color Hole = Hex("120D0E");
        RectTransform hpBar, movesLine, sightRow, uiRoot;
        TMP_Text legendWord;
        TicketRow tickets, cardTickets;
        RectTransform ticketBox;
        TMP_Text ticketCount;

        // ---- Tickets ----

        // Ticket states: 0 live, 1 killed (red splatter), 2 broken (white splatter).
        public class TicketRow
        {
            public RectTransform rt;
            public readonly List<(RectTransform rt, GameObject tear, Image splat, Image hole, int state, float pop)> tickets = new List<(RectTransform, GameObject, Image, Image, int, float)>();
            public System.Func<Transform, (RectTransform, GameObject, Image, Image)> make;

            public void Set(int[] states)
            {
                while (tickets.Count < states.Length) { var (t, tear, splat, hole) = make(rt); tickets.Add((t, tear, splat, hole, -1, 0f)); }
                for (int i = 0; i < tickets.Count; i++)
                {
                    var k = tickets[i];
                    k.rt.gameObject.SetActive(i < states.Length);
                    if (i >= states.Length) continue;
                    int s = states[i];
                    if (s != k.state)
                    {
                        if (k.state == 0 && s != 0) k.pop = 1f; // punched just now
                        k.state = s;
                        k.tear.SetActive(s == 0);
                        k.splat.enabled = k.hole.enabled = s != 0;
                        k.splat.color = s == 1 ? Blood : Bone;
                    }
                    k.pop = Mathf.MoveTowards(k.pop, 0f, Time.unscaledDeltaTime * 4f);
                    k.rt.localScale = Vector3.one * (1f + 0.35f * k.pop * k.pop);
                    tickets[i] = k;
                }
            }
        }

        TicketRow MakeTicketRow(Transform parent)
        {
            var row = new TicketRow { rt = Row("Tickets", parent, 6f) };
            row.make = p =>
            {
                var t = Rect("Ticket", p, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 34));
                Size(t, 22, 34);
                Shape(t, Bone, 0f);
                Shape(t, Black, 2f);
                // Live: a dashed tear line across the middle.
                var tear = Rect("Tear", t, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 1.5f));
                for (int i = 0; i < 3; i++)
                {
                    var d = Rect("Dash", tear, new Vector2(0f, 0.5f), new Vector2(i * 5.5f, 0), new Vector2(3f, 1.5f)).gameObject.AddComponent<Image>();
                    d.sprite = white;
                    d.color = Bone;
                    d.raycastTarget = false;
                }
                // Punched: a splatter round a round hole.
                var splat = Img(t, Blood, 2f, skew: true);
                splat.sprite = IconSprite("splat");
                var hole = Rect("Hole", t, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9, 9)).gameObject.AddComponent<Image>();
                hole.sprite = IconSprite("dot");
                hole.color = Hole;
                hole.raycastTarget = false;
                return (t, tear.gameObject, splat, hole);
            };
            return row;
        }

        void Shape(RectTransform parent, Color col, float inset)
        {
            var rt = new GameObject("Shape", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt, inset);
            var g = rt.gameObject.AddComponent<TicketShape>();
            g.color = col;
            g.raycastTarget = false;
            rt.gameObject.AddComponent<UISkew>();
        }

        void BuildTickets(Transform root)
        {
            ticketBox = Rect("Ticket Counter", root, Vector2.one, new Vector2(-32, -26), Vector2.zero);
            var col = Column(ticketBox, new RectOffset(0, 0, 0, 0), 6f);
            col.childAlignment = TextAnchor.UpperRight;
            Fit(ticketBox);
            tickets = MakeTicketRow(ticketBox);
            tickets.rt.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
            var count = Row("Count", ticketBox, 8f);
            count.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerRight;
            ticketCount = Text(count, title, 26, TextAlignmentOptions.BottomRight, Vector2.zero, Vector2.zero);
            ticketCount.color = Bone;
            Glow(ticketCount);
            var left = Label(count, "Tickets left", 13, LockedText, 2f);
            left.margin = new Vector4(0, 0, 0, 3);
            Glow(left);
        }

        // Taken tickets first (in the order taken), then the live ones.
        public void SetTickets(int[] states)
        {
            tickets.Set(states);
            ticketCount.text = states.Count(s => s == 0).ToString();
        }

        // ---- Screen rects of HUD elements, for the tutorial's highlight ----

        static Rect ScreenRect(params RectTransform[] rts)
        {
            var corners = new Vector3[4];
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var rt in rts)
            {
                rt.GetWorldCorners(corners); // overlay canvas: world = screen pixels
                foreach (var p in corners) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            }
            return UnityEngine.Rect.MinMaxRect(x0, y0, x1, y1);
        }

        // Text by what it draws, not its layout box (the legend number's box is far taller than the digits).
        static Rect TextRect(TMP_Text t)
        {
            t.ForceMeshUpdate();
            var b = t.textBounds;
            var lo = t.rectTransform.TransformPoint(b.min);
            var hi = t.rectTransform.TransformPoint(b.max);
            return UnityEngine.Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        static Rect Union(params Rect[] rs) => rs.Aggregate((a, b) => UnityEngine.Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));

        public Rect HealthRect => Union(ScreenRect(hpBar), TextRect(hpNum), TextRect(hpWord));
        public Rect MovesRect => ScreenRect(movesLine);
        public Rect SkillRowRect => ScreenRect(boxes[0].rt, boxes[boxes.Length - 1].rt);
        public Rect BoxRect(SkillId s) => ScreenRect(boxes[(int)s].rt);
        public Rect LegendRect => Union(ScreenRect(bar), TextRect(notchLabels[0]), TextRect(notchLabels[notchLabels.Length - 1]), TextRect(legendNum), TextRect(legendWord));
        public Rect TicketsRect => ScreenRect(ticketBox);
        public Rect TicketRect(int i) => ScreenRect(tickets.tickets[i].rt);
        public Rect BannerRect => ScreenRect(banner);
        public Rect SightRect => ScreenRect(sightRow);
        public static Rect ScreenRectOf(RectTransform rt) => ScreenRect(rt);

        // ---- Tutorial card and highlight ----

        RectTransform tut, tutCard, tutNext, tutSkip, tutUnderline;
        DimHoles tutDim;
        readonly List<DashedFrame> tutFrames = new List<DashedFrame>();
        TMP_Text tutTitle, tutNumber, tutText, tutTip;
        Canvas canvasRef;

        // ---- Pause menu ----

        RectTransform pause;
        TMP_Text pauseHead;
        readonly List<TMP_Text> pauseItems = new List<TMP_Text>();

        void BuildOverlays(Transform root)
        {
            uiRoot = (RectTransform)root;
            canvasRef = root.GetComponent<Canvas>();

            tut = new GameObject("Tutorial", typeof(RectTransform)).GetComponent<RectTransform>();
            tut.SetParent(root, false);
            Stretch(tut, 0f);
            tut.pivot = Vector2.zero;
            var dimRt = new GameObject("Dim", typeof(RectTransform)).GetComponent<RectTransform>();
            dimRt.SetParent(tut, false);
            Stretch(dimRt, 0f);
            dimRt.pivot = Vector2.zero;
            tutDim = dimRt.gameObject.AddComponent<DimHoles>();
            tutDim.color = new Color(0.02f, 0.012f, 0.016f, 0.72f);
            tutDim.raycastTarget = false;

            // The card: 420 wide, slanted, black, an 8 px bone bar down its left edge.
            tutCard = Rect("Card", tut, Vector2.zero, Vector2.zero, new Vector2(420, 100));
            Img(tutCard, Black, 0f, skew: true);
            var stripe = Img(tutCard, Bone, 0f, skew: true).rectTransform;
            stripe.anchorMax = new Vector2(0f, 1f);
            stripe.offsetMax = new Vector2(8f, 0f);
            IgnoreLayout(tutCard);
            var col = Column(tutCard, new RectOffset(30, 26, 18, 18), 10f);
            col.childForceExpandWidth = true;
            tutCard.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            header.SetParent(tutCard, false);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            tutTitle = Text(header, title, 28, TextAlignmentOptions.Bottom, Vector2.zero, Vector2.zero); // centred, underline under it
            Stretch(tutTitle.rectTransform, 0f);
            tutTitle.margin = new Vector4(0, 0, 0, 6);
            tutTitle.fontStyle = FontStyles.UpperCase;
            tutTitle.characterSpacing = 2f;
            tutTitle.color = Bone;
            tutUnderline = Rect("Underline", header, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(100, 3));
            var ul = tutUnderline.gameObject.AddComponent<Image>();
            ul.sprite = white;
            ul.color = Bone;
            tutNumber = Label(header, "", 15, Grey, 1.5f);
            Stretch(tutNumber.rectTransform, 0f);
            tutNumber.alignment = TextAlignmentOptions.TopRight;
            tutText = Text(tutCard, body, 19, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tutTip = Text(tutCard, body, 16, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            tutText.textWrappingMode = tutTip.textWrappingMode = TextWrappingModes.Normal;
            tutText.color = Bone;
            tutTip.color = LockedText;
            var buttons = Row("Buttons", tutCard, 0f);
            buttons.gameObject.AddComponent<LayoutElement>().minHeight = 34;
            var skip = Label(buttons, "Skip tutorial", 15, Grey, 1.5f);
            tutSkip = skip.rectTransform;
            var spacer = new GameObject("Spacer", typeof(RectTransform));
            spacer.transform.SetParent(buttons, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f; // pushes NEXT to the right
            tutNext = Row("Next", buttons, 10f);
            tutNext.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(18, 18, 6, 7);
            Img(tutNext, Bone, 0f, skew: true);
            IgnoreLayout(tutNext);
            var next = Text(tutNext, title, 20, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            next.fontStyle = FontStyles.UpperCase;
            next.characterSpacing = 2f;
            next.color = Black;
            next.text = "Next";
            KeyCap(tutNext, "Space", Blood, Bone); // red on white: the card's only red
            tut.gameObject.SetActive(false);

            // Pause menu: a slanted panel in the centre.
            pause = Rect("Pause", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 100));
            Img(pause, Bone, 0f, skew: true);
            Img(pause, Black, 2f, skew: true);
            IgnoreLayout(pause);
            var list = Column(pause, new RectOffset(34, 34, 18, 22), 10f);
            list.childAlignment = TextAnchor.UpperLeft;
            Fit(pause);
            var head = Text(pause, title, 30, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            head.fontStyle = FontStyles.UpperCase;
            head.color = Bone;
            head.text = "Paused";
            pauseHead = head;
            for (int i = 0; i < 5; i++) pauseItems.Add(Label(pause, "", 18, Bone, 1.5f));
            pause.gameObject.SetActive(false);
        }

        public bool TutorialUp => tut.gameObject.activeSelf;

        // Shows a tutorial card. number: "5 / 7" or null. The highlight follows SetTutorialTargets.
        public void ShowTutorial(string heading, string number, string text, string tip)
        {
            tutTitle.text = heading;
            tutNumber.text = number ?? "";
            tutText.text = text;
            tutTip.text = tip ?? "";
            tutTip.gameObject.SetActive(!string.IsNullOrEmpty(tip));
            tut.gameObject.SetActive(true);
            tutUnderline.sizeDelta = new Vector2(tutTitle.GetPreferredValues(heading).x, 3);
            tutUnderline.anchoredPosition = new Vector2(0, 1);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tutCard);
        }

        public void HideTutorial() => tut.gameObject.SetActive(false);

        // The highlight: each target (screen pixels) padded, framed in slanted bone white, left undimmed;
        // the card beside them where it fits (above, right, left, below), else in the centre.
        public void SetTutorialTargets(IList<Rect> screenRects)
        {
            float s = canvasRef.scaleFactor;
            var holes = screenRects.Select(r => new Rect(r.x / s - 6, r.y / s - 6, r.width / s + 12, r.height / s + 12)).ToList();
            tutDim.Set(holes);
            while (tutFrames.Count < holes.Count)
            {
                var f = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
                f.SetParent(tut, false);
                f.SetSiblingIndex(1);
                f.anchorMin = f.anchorMax = f.pivot = Vector2.zero;
                var frame = f.gameObject.AddComponent<DashedFrame>();
                frame.thickness = 3f;
                frame.dash = 100000f; // one unbroken stroke per side
                frame.color = Bone;
                frame.raycastTarget = false;
                f.gameObject.AddComponent<UISkew>();
                tutFrames.Add(frame);
            }
            for (int i = 0; i < tutFrames.Count; i++)
            {
                tutFrames[i].gameObject.SetActive(i < holes.Count);
                if (i >= holes.Count) continue;
                var rt = tutFrames[i].rectTransform;
                rt.anchoredPosition = holes[i].position;
                rt.sizeDelta = holes[i].size;
            }

            var area = tut.rect;
            var size = tutCard.rect.size;
            Vector2 at = (area.size - size) / 2;
            if (holes.Count > 0)
            {
                var u = holes.Aggregate((a, b) => UnityEngine.Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
                const float gap = 24f, edge = 16f;
                float cx = Mathf.Clamp(u.center.x - size.x / 2, edge, area.width - size.x - edge);
                float cy = Mathf.Clamp(u.center.y - size.y / 2, edge, area.height - size.y - edge);
                if (u.yMax + gap + size.y <= area.height - edge) at = new Vector2(cx, u.yMax + gap);
                else if (u.xMax + gap + size.x <= area.width - edge) at = new Vector2(u.xMax + gap, cy);
                else if (u.xMin - gap - size.x >= edge) at = new Vector2(u.xMin - gap - size.x, cy);
                else if (u.yMin - gap - size.y >= edge) at = new Vector2(cx, u.yMin - gap - size.y);
            }
            tutCard.anchoredPosition = at;
        }

        public bool TutorialNextHit(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(tutNext, screen, null);
        public bool TutorialSkipHit(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(tutSkip, screen, null);

        // ---- Pause menu ----

        public bool PauseOpen => pause.gameObject.activeSelf;
        // The centre panel: the pause menu, or another short choice under its own heading (up to 5 lines).
        public void ShowPause(bool on, string heading = "Paused")
        {
            pauseHead.text = heading;
            pause.gameObject.SetActive(on);
        }

        // items: the menu's lines; the one under the mouse is bone white, the rest grey. Returns its index, or -1.
        public int RefreshPause(string[] items, Vector2 screen)
        {
            int hit = -1;
            for (int i = 0; i < pauseItems.Count; i++)
            {
                pauseItems[i].gameObject.SetActive(i < items.Length);
                if (i >= items.Length) continue;
                pauseItems[i].text = items[i];
                bool over = RectTransformUtility.RectangleContainsScreenPoint(pauseItems[i].rectTransform, screen, null);
                if (over) hit = i;
                pauseItems[i].color = over ? Bone : Grey;
            }
            return hit;
        }
    }
}
