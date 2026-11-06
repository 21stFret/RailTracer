using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailTracer
{
    // The menus' screen layer (on their own canvas, so the HUD can hide under them), keeping the middle clear for
    // the train: the "Red Line" title (or a screen's heading) top left, the menu lines bottom left with a block of
    // text beside them; and for Journey, the car names under the real train with short links between them. No
    // panel behind: the text has a dark glow. The 3D train behind is Game's (GameFlow).
    public partial class SkillHud
    {
        Canvas menuCanvas;
        TMP_Text menuTitle, menuHeading, menuInfo;
        readonly List<TMP_Text> menuItems = new List<TMP_Text>();
        readonly List<TMP_Text> journeyNames = new List<TMP_Text>();
        readonly List<Image> journeyLinks = new List<Image>();
        RectTransform menuClose;
        TMP_Text menuCloseX;
        TMP_Text menuCorner; // a line on its own in the bottom-right corner (Options: Reset data)

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
            menuTitle = Text(top, title, 104, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            menuTitle.text = $"<color=#C8102E>RED</color> LINE"; // "Red" in blood red, "Line" in bone
            menuTitle.color = Bone;
            menuTitle.fontStyle = FontStyles.UpperCase;
            Glow(menuTitle);
            menuHeading = Text(top, title, 48, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            menuHeading.fontStyle = FontStyles.UpperCase;
            menuHeading.color = Bone;
            Glow(menuHeading);

            var bottom = Row("Menu", root, 56f);
            Place(bottom, Vector2.zero, new Vector2(64f, 44f));
            bottom.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            Fit(bottom);
            var col = Rect("Lines", bottom, Vector2.zero, Vector2.zero, Vector2.zero);
            Column(col, new RectOffset(0, 0, 0, 0), 10f).childAlignment = TextAnchor.LowerLeft;
            for (int i = 0; i < 9; i++) { var item = Label(col, "", 22, Grey, 1.5f); Glow(item); menuItems.Add(item); }
            menuInfo = Text(bottom, body, 17, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.zero);
            menuInfo.textWrappingMode = TextWrappingModes.Normal;
            menuInfo.color = Bone;
            Glow(menuInfo);
            menuInfo.gameObject.AddComponent<LayoutElement>().preferredWidth = 460f;

            menuCorner = Label(Rect("Corner", root, Vector2.right, new Vector2(-64f, 44f), Vector2.zero), "", 22, Blood, 1.5f);
            var crt = menuCorner.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = Vector2.right; // its bottom-right on the corner point
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(240f, 30f); // a real box, for the mouse
            menuCorner.alignment = TextAlignmentOptions.BottomRight;
            Glow(menuCorner);
            var screenRect = Rect("Screen", root, Vector2.zero, Vector2.zero, Vector2.zero);
            Stretch(screenRect, 0f);
            (menuClose, menuCloseX) = CloseX(screenRect, 32f); // a submenu's way back
            for (int i = 0; i < Train.DemoCars; i++)
            {
                var n = Text(root, title, 22, TextAlignmentOptions.Center, Vector2.zero, new Vector2(220, 30));
                n.fontStyle = FontStyles.UpperCase;
                Glow(n);
                journeyNames.Add(n);
                if (i == 0) continue;
                var link = Rect("Link", root, Vector2.zero, Vector2.zero, new Vector2(46, 3));
                var img = link.gameObject.AddComponent<Image>();
                img.sprite = white;
                img.raycastTarget = false;
                journeyLinks.Add(img);
            }
            ShowMenu(false);
        }

        public void ShowMenu(bool on) => menuCanvas.gameObject.SetActive(on);

        // One frame of a menu screen. title: the "Red Line" title (else `heading`, with the close X top right);
        // enabled: per item (null = all); info: a block of text beside the lines (or null). Returns the enabled line
        // under the mouse, -2 for the close X, -3 for the corner line, or -1. danger: a line drawn in red, or -1.
        // corner: a red line on its own in the bottom-right corner (null: none).
        public int RefreshMenu(bool showTitle, string heading, string[] items, bool[] enabled, string info, Vector2 screen, bool held = false, int danger = -1, string corner = null)
        {
            menuClose.gameObject.SetActive(!showTitle);
            bool onX = !showTitle && RectTransformUtility.RectangleContainsScreenPoint(menuClose, screen, null);
            menuCloseX.color = onX && !held ? Bone : Blood;
            menuTitle.gameObject.SetActive(showTitle);
            menuHeading.gameObject.SetActive(!showTitle && heading != null);
            menuHeading.text = heading;
            menuInfo.gameObject.SetActive(!string.IsNullOrEmpty(info));
            menuInfo.text = info;
            if (heading != "Journey") HideJourney();
            menuCorner.gameObject.SetActive(corner != null);
            bool onCorner = corner != null && RectTransformUtility.RectangleContainsScreenPoint(menuCorner.rectTransform, screen, null);
            if (corner != null)
            {
                menuCorner.text = corner;
                menuCorner.color = onCorner ? Bright : Blood;
            }
            int hit = -1;
            for (int i = 0; i < menuItems.Count; i++)
            {
                var t = menuItems[i];
                t.gameObject.SetActive(i < items.Length);
                if (i >= items.Length) continue;
                t.text = items[i];
                bool on = enabled == null || enabled[i];
                bool over = on && RectTransformUtility.RectangleContainsScreenPoint(t.rectTransform, screen, null);
                if (over) hit = i;
                t.color = !on ? LockedBox : i == danger ? (over ? Bright : Blood) : over ? Bone : Grey;
            }
            return onX ? -2 : onCorner ? -3 : hit;
        }

        // Journey: each car's name under it on screen (`at`, screen pixels), red cleared (2), white next (1), grey
        // ahead (0), the picked one underlined; short links between neighbours. Returns the car under the mouse.
        public int RefreshJourney(Vector2[] at, string[] names, int[] states, int picked, Vector2 screen)
        {
            int hit = -1;
            for (int i = 0; i < journeyNames.Count; i++)
            {
                var n = journeyNames[i];
                n.gameObject.SetActive(true);
                n.rectTransform.position = at[i];
                n.text = names[i];
                n.color = states[i] == 2 ? Blood : states[i] == 1 ? Bone : Grey;
                n.fontStyle = FontStyles.UpperCase | (i == picked ? FontStyles.Underline : 0);
                if (RectTransformUtility.RectangleContainsScreenPoint(n.rectTransform, screen, null)) hit = i;
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
