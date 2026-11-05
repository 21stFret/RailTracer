using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RailTracer
{
    // The menus' screen layer (on their own canvas, so the HUD can hide under them): a shaded strip down the left
    // with the "Red Line" title, a heading, the menu lines and a block of text; and for Journey, the car names
    // under the real train with short links between them. The 3D train behind is Game's (GameFlow).
    public partial class SkillHud
    {
        Canvas menuCanvas;
        TMP_Text menuTitle, menuHeading, menuInfo;
        readonly List<TMP_Text> menuItems = new List<TMP_Text>();
        readonly List<TMP_Text> journeyNames = new List<TMP_Text>();
        readonly List<Image> journeyLinks = new List<Image>();

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

            // The shaded strip: the carriage still shows through it.
            var shade = Rect("Shade", root, Vector2.zero, Vector2.zero, Vector2.zero);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = new Vector2(0f, 1f);
            shade.pivot = Vector2.zero;
            shade.sizeDelta = new Vector2(540f, 0f);
            Img(shade, Alpha(Black, 0.62f), 0f);

            var col = Rect("Menu", root, new Vector2(0f, 0.5f), new Vector2(72f, 0f), Vector2.zero);
            var v = Column(col, new RectOffset(0, 0, 0, 0), 12f);
            v.childAlignment = TextAnchor.MiddleLeft;
            Fit(col);
            menuTitle = Text(col, title, 104, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            menuTitle.text = $"<color=#C8102E>RED</color> LINE"; // "Red" in blood red, "Line" in bone
            menuTitle.color = Bone;
            menuTitle.fontStyle = FontStyles.UpperCase;
            menuHeading = Text(col, title, 40, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            menuHeading.fontStyle = FontStyles.UpperCase;
            menuHeading.color = Bone;
            for (int i = 0; i < 6; i++) menuItems.Add(Label(col, "", 22, Grey, 1.5f));
            menuInfo = Text(col, body, 17, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            menuInfo.textWrappingMode = TextWrappingModes.Normal;
            menuInfo.color = LockedText;
            menuInfo.gameObject.AddComponent<LayoutElement>().preferredWidth = 420f;

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

        // One frame of a menu screen. title: the "Red Line" title (else `heading`); enabled: per item (null = all);
        // info: a block of text under the lines (or null). Returns the enabled line under the mouse, or -1.
        public int RefreshMenu(bool showTitle, string heading, string[] items, bool[] enabled, string info, Vector2 screen)
        {
            menuTitle.gameObject.SetActive(showTitle);
            menuHeading.gameObject.SetActive(!showTitle && heading != null);
            menuHeading.text = heading;
            menuInfo.gameObject.SetActive(!string.IsNullOrEmpty(info));
            menuInfo.text = info;
            if (heading != "Journey") HideJourney();
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
                t.color = !on ? LockedBox : over ? Bone : Grey;
            }
            return hit;
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
