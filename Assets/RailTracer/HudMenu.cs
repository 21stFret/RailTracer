using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailTracer
{
    // The menus' screen layer (on their own canvas, so the HUD can hide under them), keeping the middle clear for
    // the train: the "Red Line" title (or a screen's heading) top left, the menu lines bottom left with a block of
    // text beside them; and for Journey, the car names under the real train with short links between them. No
    // panel behind the lines: the text has a dark glow. The 3D train behind is Game's (GameFlow).
    // Sketch style (Update 19.27): Kalam, each heading and line with its own small fixed tilt, the hover on every
    // line (MenuLine); Journey's car text in a centre-card-style panel; Controls as key boxes.
    public partial class SkillHud
    {
        const float MenuSize = 28f, MenuNote = 18f;
        Canvas menuCanvas;
        TMP_Text menuTitle, menuHeading, menuInfo;
        RectTransform menuInfoBox, menuControls;
        UIShape[] menuInfoCard;
        readonly List<MenuLine> menuItems = new List<MenuLine>();
        readonly List<MenuLine> journeyNames = new List<MenuLine>();
        readonly List<Image> journeyLinks = new List<Image>();
        RectTransform menuClose;
        TMP_Text menuCloseX;
        MenuLine menuCorner; // a line on its own in the bottom-right corner (Options: Reset data)

        public void BuildMenu()
        {
            menuCanvas = new GameObject("Menus", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            menuCanvas.sortingOrder = 20;
            var scaler = menuCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            var root = menuCanvas.transform;

            var top = Rect("Title", root, new Vector2(0f, 1f), new Vector2(64f, -40f), Vector2.zero);
            Column(top, new RectOffset(0, 0, 0, 0), 0f).childAlignment = TextAnchor.UpperLeft;
            Fit(top);
            menuTitle = Text(top, sketchBold, 104, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            menuTitle.text = $"<color=#C8102E>RED</color> LINE"; // "Red" in blood red, "Line" in bone
            menuTitle.color = Bone;
            menuTitle.fontStyle = FontStyles.UpperCase;
            menuTitle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
            Glow(menuTitle);
            menuHeading = Text(top, sketchBold, 48, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            menuHeading.fontStyle = FontStyles.UpperCase;
            menuHeading.color = Bone;
            menuHeading.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
            Glow(menuHeading);

            var bottom = Row("Menu", root, 56f);
            Place(bottom, Vector2.zero, new Vector2(64f, 44f));
            bottom.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Fit(bottom);
            var col = Rect("Lines", bottom, Vector2.zero, Vector2.zero, Vector2.zero);
            Column(col, new RectOffset(0, 0, 0, 0), 10f).childAlignment = TextAnchor.LowerLeft;
            for (int i = 0; i < 10; i++) menuItems.Add(Line(col, MenuSize, MenuNote, 3f, i));
            // The text beside the lines; on Journey inside a card (the picked car's name, description, best ticket).
            menuInfoBox = Rect("Info", bottom, Vector2.zero, Vector2.zero, Vector2.zero);
            CardShape(menuInfoBox);
            menuInfoBox.localRotation = Quaternion.identity; // tilted only while it's a card
            menuInfoCard = menuInfoBox.GetComponentsInChildren<UIShape>();
            IgnoreLayout(menuInfoBox);
            Column(menuInfoBox, new RectOffset(0, 0, 0, 0), 0f);
            menuInfo = Text(menuInfoBox, sketch, 18, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.zero);
            menuInfo.textWrappingMode = TextWrappingModes.Normal;
            menuInfo.color = Bone;
            Glow(menuInfo);
            menuInfo.gameObject.AddComponent<LayoutElement>().preferredWidth = 460f;
            // Controls: each control as key boxes and what it does (the H panel's list).
            menuControls = Rect("Controls", bottom, Vector2.zero, Vector2.zero, Vector2.zero);
            Column(menuControls, new RectOffset(0, 0, 0, 0), 5f).childAlignment = TextAnchor.LowerLeft;
            ControlRows(menuControls, 15, true);

            var corner = Rect("Corner", root, Vector2.right, new Vector2(-64f, 44f), Vector2.zero);
            menuCorner = Line(corner, MenuSize, MenuNote, 3f, 1, idle: Bright);
            var crt = menuCorner.rt;
            crt.anchorMin = crt.anchorMax = crt.pivot = Vector2.right; // its bottom-right on the corner point
            crt.anchoredPosition = Vector2.zero;
            var screenRect = Rect("Screen", root, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(screenRect, 0f);
            (menuClose, menuCloseX) = CloseX(screenRect, 32f); // a submenu's way back
            for (int i = 0; i < Train.TrainCars; i++)
            {
                if (i > 0)
                {
                    var link = Rect("Link", root, Vector2.zero, Vector2.zero, new Vector2(46, 3));
                    var img = link.gameObject.AddComponent<Image>();
                    img.sprite = white;
                    img.raycastTarget = false;
                    journeyLinks.Add(img);
                }
                var n = Line(root, 22, 14, 3f, i + 2);
                n.rt.anchorMin = n.rt.anchorMax = Vector2.zero;
                journeyNames.Add(n);
            }
            ShowMenu(false);
        }

        // The controls list as rows: key boxes (Kalam) then what each does. glow: over the train (the menu page).
        void ControlRows(RectTransform parent, float size, bool glow)
        {
            foreach (var (keys, what) in ControlsList)
            {
                var row = Row("Row", parent, 14f);
                var caps = Row("Keys", row, 4f);
                caps.gameObject.AddComponent<LayoutElement>().minWidth = 96;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (i > 0) SketchLabel(caps, "to", 13, LockedText);
                    SketchCap(caps, keys[i], 14);
                }
                var t = Text(row, sketch, size + 2, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
                t.color = Bone;
                t.text = what;
                if (glow) Glow(t);
            }
        }

        public void ShowMenu(bool on) => menuCanvas.gameObject.SetActive(on);

        // An item's label and note: "Label\tnote" (a tab between them), else no note.
        static (string label, string note) Split(string item)
        {
            int tab = item.IndexOf('\t');
            return tab < 0 ? (item, "") : (item.Substring(0, tab), item.Substring(tab + 1));
        }

        // One frame of a menu screen. title: the "Red Line" title (else `heading`, with the close X top right);
        // enabled: per item (null = all); info: a block of text beside the lines (or null). Returns the enabled line
        // under the mouse, -2 for the close X, -3 for the corner line, or -1. danger: a line drawn in red, or -1.
        // corner: a red line on its own in the bottom-right corner (null: none). An item's note follows a tab.
        public int RefreshMenu(bool showTitle, string heading, string[] items, bool[] enabled, string info, Vector2 screen, bool held = false, int danger = -1, string corner = null)
        {
            menuClose.gameObject.SetActive(!showTitle);
            bool onX = !showTitle && RectTransformUtility.RectangleContainsScreenPoint(menuClose, screen, null);
            menuCloseX.color = onX && !held ? Bone : Blood;
            menuTitle.gameObject.SetActive(showTitle);
            menuHeading.gameObject.SetActive(!showTitle && heading != null);
            menuHeading.text = heading;
            bool journey = heading == "Journey";
            menuInfoBox.gameObject.SetActive(!string.IsNullOrEmpty(info));
            menuInfo.text = info;
            foreach (var s in menuInfoCard) s.shown = journey;
            menuInfoBox.GetComponent<VerticalLayoutGroup>().padding = journey ? new RectOffset(40, 40, 18, 20) : new RectOffset(0, 0, 0, 0);
            menuInfoBox.localRotation = Quaternion.Euler(0f, 0f, journey ? -1f : 0f);
            menuControls.gameObject.SetActive(heading == "Controls");
            if (!journey) HideJourney();
            menuCorner.gameObject.SetActive(corner != null);
            bool onCorner = corner != null && menuCorner.Over(screen);
            if (corner != null)
            {
                menuCorner.Set(corner);
                menuCorner.hot = onCorner;
            }
            int hit = -1;
            for (int i = 0; i < menuItems.Count; i++)
            {
                var t = menuItems[i];
                t.gameObject.SetActive(i < items.Length);
                if (i >= items.Length) continue;
                var (label, note) = Split(items[i]);
                t.Set(label, note);
                bool on = enabled == null || enabled[i];
                bool over = on && t.Over(screen);
                if (over) hit = i;
                t.idle = !on ? LockedBox : i == danger ? Bright : Idle;
                t.hot = over;
            }
            return onX ? -2 : onCorner ? -3 : hit;
        }

        // Journey: each car's name under it on screen (`at`, screen pixels), red cleared (2), white next (1), grey
        // ahead (0); the picked one keeps its hover; short links between neighbours. Returns the car under the mouse.
        public int RefreshJourney(Vector2[] at, string[] names, int[] states, int picked, Vector2 screen)
        {
            int hit = -1;
            for (int i = 0; i < journeyNames.Count; i++)
            {
                var n = journeyNames[i];
                n.gameObject.SetActive(true);
                n.rt.position = at[i];
                n.Set(names[i]);
                n.idle = states[i] == 2 ? Blood : states[i] == 1 ? Bone : Grey;
                bool over = n.Over(screen);
                if (over) hit = i;
                n.hot = over || i == picked;
            }
            for (int i = 0; i < journeyLinks.Count; i++)
            {
                var l = journeyLinks[i];
                l.gameObject.SetActive(true);
                l.rectTransform.position = (at[i] + at[i + 1]) / 2f;
                l.color = states[i] == 2 && states[i + 1] == 2 ? Blood : Grey;
            }
            return hit;
        }

        void HideJourney()
        {
            foreach (var n in journeyNames) n.gameObject.SetActive(false);
            foreach (var l in journeyLinks) l.gameObject.SetActive(false);
        }
    }
}
