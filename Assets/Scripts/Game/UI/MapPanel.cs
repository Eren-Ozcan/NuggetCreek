using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen Map modal (design doc 6.2): the 20 creeks from the bottom up, opened at the
    /// current one; travel between unlocked creeks, unlock the next one, see each boss's state.
    /// With the map art it is the painted map: a marker per creek on the water, a dotted trail
    /// up to the next creek and mist above it. Without the art it is a plain list.
    /// Either way each creek is an object named after it with an "Action" button.
    /// </summary>
    public sealed class MapPanel
    {
        /// <summary>One creek on screen: a list row or a map marker.</summary>
        sealed class Creek
        {
            public Text Title;
            public Text Detail;
            public Button Action;
            public Text ActionLabel;
            public IconBesideText PriceIcon;
            public Image Picture;
            // Map only.
            public Image Pin;
            public Outline Ring;
            public Text Number;
            public Image Boss;
            public readonly List<Image> TrailIn = new List<Image>();
        }

        const float PinSize = 84;
        const float PlateWidth = 340;
        const float BossSize = 54;
        const float TrailStep = 42;
        const float FogEdge = 260;
        const float MistAlpha = 0.45f;

        readonly GameSession session;
        readonly RectTransform root;
        readonly ScrollRect scroll;
        readonly List<Creek> creeks = new List<Creek>();
        readonly Vector2[] markers;
        readonly List<List<Image>> trails = new List<List<Image>>();
        readonly RectTransform fog;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised after an unlock or travel changes the active creek.</summary>
        public event Action RegionChanged;

        public MapPanel(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("Map", canvas, Palette.Panel).rectTransform.Fill();
            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));

            int count = session.Economy.Config.RegionCount;
            markers = PaintedMarkers(count);
            if (markers != null)
            {
                // The painting runs under the title bar's edge; the title sits on a cream band.
                RectTransform map = PaintedMap(body, count);
                scroll = map.GetComponentInParent<ScrollRect>();
                fog = Fog(map);
                for (int i = 0; i < count; i++)
                    creeks.Add(NewMarker(map, i));
            }
            else
            {
                RectTransform list = Ui.ScrollList(body, 16, 24);
                scroll = list.GetComponentInParent<ScrollRect>();
                for (int i = 0; i < count; i++)
                    creeks.Add(NewRow(list, i));
            }

            Text title = Ui.Title("Title", root, "MAP", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.CloseButton(root, Close);
            Close();
        }

        /// <summary>The painted map's markers, or null when a panel or the marker file is missing.</summary>
        static Vector2[] PaintedMarkers(int count)
        {
            Vector2[] markers = Art.MapMarkers;
            if (markers == null || markers.Length < count)
                return null;
            for (int i = 0; i < Art.MapPanelCount; i++)
                if (Art.MapPanel(i) == null)
                    return null;
            return markers;
        }

        public void Open()
        {
            root.SetActive(true);
            Refresh();
            // Open at the current creek: 0 is the bottom of the list, 1 the top.
            Canvas.ForceUpdateCanvases();
            int current = session.Progress.RegionIndex;
            if (markers != null)
            {
                float height = scroll.content.rect.height;
                float view = scroll.viewport.rect.height;
                float fromTop = (1 - markers[current].y) * height - view / 2;
                scroll.verticalNormalizedPosition = height > view ? 1 - Mathf.Clamp01(fromTop / (height - view)) : 0;
            }
            else
            {
                int last = Mathf.Max(1, creeks.Count - 1);
                scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((float)current / last);
            }
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            PlayerProgress progress = session.Progress;
            for (int i = 0; i < creeks.Count; i++)
            {
                Creek creek = creeks[i];
                if (markers != null)
                {
                    RefreshMarker(creek, i);
                    continue;
                }
                string valueText = "Gold Dust " + NumberFormat.Dollars(session.Economy.DustBaseValue(i));
                if (i < progress.RegionsUnlocked)
                    valueText += session.IsBossBeaten(i) ? "  -  boss beaten" : "  -  boss waiting";
                if (i == progress.RegionIndex)
                {
                    creek.Detail.SetText(valueText + "  -  you are here");
                    SetAction(creek, "HERE", false);
                }
                else if (i < progress.RegionsUnlocked)
                {
                    creek.Detail.SetText(valueText);
                    SetAction(creek, "GO", true);
                }
                else if (i == progress.RegionsUnlocked)
                {
                    creek.Detail.SetText(valueText);
                    SetAction(creek, NumberFormat.Dollars(session.NextRegionCost.Value), session.CanAfford(session.NextRegionCost), price: true);
                }
                else
                {
                    creek.Detail.SetText($"Unlock {GameCatalog.RegionNames[i - 1]} first.");
                    SetAction(creek, "LOCKED", false);
                }
                // Creeks beyond the next one stay in shadow until the trail reaches them.
                if (creek.Picture != null)
                    creek.Picture.color = i <= progress.RegionsUnlocked ? Color.white : LockedPicture;
            }
            if (fog != null)
                PlaceFog();
        }

        // --- painted map ---

        RectTransform PaintedMap(RectTransform body, int count)
        {
            RectTransform viewport = Ui.Rect("Scroll", body).Fill();
            viewport.gameObject.AddComponent<RectMask2D>();
            var scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
            Ui.Image("Hit", viewport, Color.clear).rectTransform.Fill();

            RectTransform map = Ui.Rect("Content", viewport);
            map.anchorMin = new Vector2(0, 1);
            map.anchorMax = new Vector2(1, 1);
            map.pivot = new Vector2(0.5f, 1);
            map.offsetMin = map.offsetMax = Vector2.zero;
            Sprite first = Art.MapPanel(0);
            var fitter = map.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            fitter.aspectRatio = first.rect.width / (first.rect.height * Art.MapPanelCount);

            for (int i = 0; i < Art.MapPanelCount; i++)
            {
                Image panel = Ui.Image($"Panel{i + 1}", map, Color.white, Art.MapPanel(i));
                panel.raycastTarget = false;
                panel.rectTransform.Place(new Vector2(0, (float)i / Art.MapPanelCount), new Vector2(1, (float)(i + 1) / Art.MapPanelCount));
            }

            // Trail dots from each creek to the next, under the markers.
            for (int i = 1; i < count; i++)
                trails.Add(Trail(map, markers[i - 1], markers[i]));

            scrollRect.content = map;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            return map;
        }

        List<Image> Trail(RectTransform map, Vector2 from, Vector2 to)
        {
            // Map fractions to reference pixels on a 1080 wide map, enough to space the dots.
            var pixels = new Vector2(1080, 1080 / map.GetComponent<AspectRatioFitter>().aspectRatio);
            float length = Vector2.Scale(to - from, pixels).magnitude;
            int steps = Mathf.Max(2, Mathf.RoundToInt(length / TrailStep));
            var dots = new List<Image>();
            for (int s = 1; s < steps; s++)
            {
                // Skip the ends, which sit under the pins.
                float along = length * s / steps;
                if (along < PinSize * 0.7f || length - along < PinSize * 0.7f)
                    continue;
                Vector2 at = Vector2.Lerp(from, to, (float)s / steps);
                Image dot = Ui.Image("Trail", map, Palette.TextLight, Ui.Circle);
                dot.raycastTarget = false;
                dot.rectTransform.Box(at, new Vector2(18, 18));
                dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                dot.rectTransform.anchoredPosition = Vector2.zero;
                var edge = dot.gameObject.AddComponent<Outline>();
                edge.effectColor = Palette.Text;
                edge.effectDistance = new Vector2(2, -2);
                dots.Add(dot);
            }
            return dots;
        }

        static Sprite fade;

        /// <summary>
        /// Mist with a soft lower edge; its bottom is moved to the next creek on refresh. Built
        /// after the trail and before the markers, so it covers the painting and the far trail only.
        /// </summary>
        static RectTransform Fog(RectTransform map)
        {
            if (fade == null)
            {
                var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++)
                    texture.SetPixel(0, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, y / 63f)));
                texture.Apply();
                fade = Sprite.Create(texture, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100);
            }
            RectTransform fog = Ui.Rect("Fog", map);
            Image solid = Ui.Image("Solid", fog, Palette.Fog);
            solid.raycastTarget = false;
            solid.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(0, FogEdge), Vector2.zero);
            Image edge = Ui.Image("Edge", fog, Palette.Fog, fade);
            edge.raycastTarget = false;
            edge.rectTransform.Place(Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, FogEdge));
            return fog;
        }

        /// <summary>Mist from just above the next creek to unlock up to the top of the map.</summary>
        void PlaceFog()
        {
            int next = session.Progress.RegionsUnlocked;
            fog.SetActive(next < markers.Length && next < creeks.Count);
            if (!fog.gameObject.activeSelf)
                return;
            float bottom = markers[next].y;
            fog.Place(new Vector2(0, bottom), Vector2.one, new Vector2(0, PinSize / 2 + 30 - FogEdge), Vector2.zero);
        }

        Creek NewMarker(RectTransform map, int index)
        {
            var creek = new Creek();
            if (index > 0)
                creek.TrailIn.AddRange(trails[index - 1]);
            Vector2 at = markers[index];
            RectTransform marker = Ui.Rect(GameCatalog.RegionNames[index], map).Box(at, new Vector2(PinSize, PinSize));
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.anchoredPosition = Vector2.zero;

            creek.Pin = Ui.Image("Pin", marker, Palette.Panel, Ui.Circle);
            creek.Pin.raycastTarget = false;
            creek.Pin.rectTransform.Fill();
            creek.Ring = creek.Pin.gameObject.AddComponent<Outline>();
            creek.Ring.effectDistance = new Vector2(4, -4);
            creek.Number = Ui.Label("Number", creek.Pin.transform, (index + 1).ToString(), 38, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            creek.Number.rectTransform.Fill();

            Sprite boss = Art.Nugget(GameCatalog.BossNugget(index));
            creek.Boss = Ui.Icon("Boss", marker, boss);
            if (creek.Boss != null)
                creek.Boss.rectTransform.Box(Vector2.one, new Vector2(BossSize, BossSize), new Vector2(BossSize / 2, BossSize / 2));

            // The plate goes on the side with more room.
            bool right = at.x < 0.5f;
            float side = right ? 1 : -1;
            creek.Action = Ui.Button("Action", marker, "", Palette.Button, () => OnAction(index), out creek.ActionLabel, 32);
            RectTransform plate = creek.Action.AsRect().Box(new Vector2(right ? 1 : 0, 0.5f), new Vector2(PlateWidth, Ui.TapHeight));
            plate.pivot = new Vector2(right ? 0 : 1, 0.5f);
            plate.anchoredPosition = new Vector2(side * 18, 0);
            creek.ActionLabel.rectTransform.Place(Vector2.zero, new Vector2(1, 0.5f), new Vector2(12, 8 + Ui.Lip), new Vector2(-12, 4));
            creek.PriceIcon = Ui.PriceIcon(creek.ActionLabel, Art.Dollar, Palette.Gold);
            creek.Title = Ui.Label("Title", creek.Action.transform, GameCatalog.RegionNames[index], 34, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            creek.Title.rectTransform.Place(new Vector2(0, 0.5f), Vector2.one, new Vector2(12, -2), new Vector2(-12, -8));
            creek.Title.resizeTextForBestFit = true;
            creek.Title.resizeTextMinSize = 24;
            creek.Title.resizeTextMaxSize = Mathf.RoundToInt(34 * Ui.TextScale);
            return creek;
        }

        void RefreshMarker(Creek creek, int index)
        {
            PlayerProgress progress = session.Progress;
            bool unlocked = index < progress.RegionsUnlocked;
            bool next = index == progress.RegionsUnlocked;
            bool here = index == progress.RegionIndex;

            // Creeks beyond the next one show through the mist as faint pins.
            bool reached = unlocked || next;
            creek.Pin.color = Faded(here ? Palette.Gold : Palette.Panel, reached);
            creek.Ring.effectColor = Faded(Palette.Text, reached);
            creek.Number.color = Faded(reached ? Palette.Text : Palette.TextMuted, reached);
            foreach (Image dot in creek.TrailIn)
                dot.SetActive(unlocked || next);
            if (creek.Boss != null)
            {
                creek.Boss.SetActive(unlocked);
                creek.Boss.color = session.IsBossBeaten(index) ? Color.white : Palette.Dim;
            }

            // Only the creeks the trail reaches name themselves; the rest wait in the mist.
            creek.Action.SetActive(unlocked || next);
            // Your own creek stays a bright plate; tapping it does nothing.
            if (here)
                SetAction(creek, "You are here", true);
            else if (unlocked)
                SetAction(creek, "Travel", true);
            else if (next)
                SetAction(creek, NumberFormat.Dollars(session.NextRegionCost.Value), session.CanAfford(session.NextRegionCost), price: true);
            creek.Action.image.color = here ? Palette.GoldText : unlocked ? Palette.ButtonAlt : Palette.Button;
        }

        static Color Faded(Color color, bool reached)
        {
            if (!reached)
                color.a *= MistAlpha;
            return color;
        }

        // --- list (no map art) ---

        Creek NewRow(Transform list, int index)
        {
            const float height = 170;
            Image background = Ui.Panel(GameCatalog.RegionNames[index], list, Palette.Row);
            Ui.PreferredHeight(background, height);
            var row = new Creek();
            RectTransform rt = background.rectTransform;
            float inset = Ui.RowPicture(rt, height, Art.Creek(index), out row.Picture, cover: true);

            row.Title = Ui.Label("Title", rt, $"{index + 1}. {GameCatalog.RegionNames[index]}", 44, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 0), new Vector2(-330, -22));
            row.Detail = Ui.Label("Detail", rt, "", 32, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 22), new Vector2(-330, -80));

            row.Action = Ui.Button("Action", rt, "", Palette.Button, () => OnAction(index), out row.ActionLabel, 36);
            row.Action.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));
            row.PriceIcon = Ui.PriceIcon(row.ActionLabel, Art.Dollar, Palette.Gold);
            return row;
        }

        void OnAction(int index)
        {
            if (index == session.Progress.RegionIndex)
                return;
            bool changed = index == session.Progress.RegionsUnlocked ? session.UnlockNextRegion() : session.TravelTo(index);
            if (!changed)
                return;
            RegionChanged?.Invoke();
            Close();
        }

        static readonly Color LockedPicture = new Color(0.3f, 0.3f, 0.3f, 1);

        static void SetAction(Creek creek, string text, bool interactable, bool price = false)
        {
            creek.ActionLabel.SetText(text);
            creek.PriceIcon.SetActive(price);
            if (creek.Action.interactable != interactable)
                creek.Action.interactable = interactable;
        }
    }
}
