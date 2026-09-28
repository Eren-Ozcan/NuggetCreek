using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen Map modal (design doc 6.2): one scrolling list of the 20 creeks, opened at the
    /// current one; travel between unlocked creeks, unlock the next one, see each boss's state.
    /// </summary>
    public sealed class MapPanel
    {
        sealed class Row
        {
            public Text Title;
            public Text Detail;
            public Button Action;
            public Text ActionLabel;
            public IconBesideText PriceIcon;
            public Image Picture;
        }

        readonly GameSession session;
        readonly RectTransform root;
        readonly ScrollRect scroll;
        readonly List<Row> rows = new List<Row>();

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised after an unlock or travel changes the active creek.</summary>
        public event Action RegionChanged;

        public MapPanel(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("Map", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Title("Title", root, "MAP", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.CloseButton(root, Close);

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);
            scroll = list.GetComponentInParent<ScrollRect>();

            int count = session.Economy.Config.RegionCount;
            for (int i = 0; i < count; i++)
                rows.Add(NewRow(list, i));

            Close();
        }

        public void Open()
        {
            root.SetActive(true);
            Refresh();
            // Open at the current creek: 0 is the bottom of the list, 1 the top.
            Canvas.ForceUpdateCanvases();
            int last = Mathf.Max(1, rows.Count - 1);
            scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((float)session.Progress.RegionIndex / last);
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            PlayerProgress progress = session.Progress;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                string valueText = "Gold Dust " + NumberFormat.Dollars(session.Economy.DustBaseValue(i));
                if (i < progress.RegionsUnlocked)
                    valueText += session.IsBossBeaten(i) ? "  -  boss beaten" : "  -  boss waiting";
                if (i == progress.RegionIndex)
                {
                    row.Detail.SetText(valueText + "  -  you are here");
                    SetAction(row, "HERE", false);
                }
                else if (i < progress.RegionsUnlocked)
                {
                    row.Detail.SetText(valueText);
                    SetAction(row, "GO", true);
                }
                else if (i == progress.RegionsUnlocked)
                {
                    row.Detail.SetText(valueText);
                    SetAction(row, NumberFormat.Dollars(session.NextRegionCost.Value), session.CanAfford(session.NextRegionCost), price: true);
                }
                else
                {
                    row.Detail.SetText($"Unlock {GameCatalog.RegionNames[i - 1]} first.");
                    SetAction(row, "LOCKED", false);
                }
                // Creeks beyond the next one stay in shadow until the trail reaches them.
                if (row.Picture != null)
                    row.Picture.color = i <= progress.RegionsUnlocked ? Color.white : LockedPicture;
            }
        }

        Row NewRow(Transform list, int index)
        {
            const float height = 170;
            Image background = Ui.Panel(GameCatalog.RegionNames[index], list, Palette.Row);
            Ui.PreferredHeight(background, height);
            var row = new Row();
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
            bool changed = index == session.Progress.RegionsUnlocked ? session.UnlockNextRegion() : session.TravelTo(index);
            if (!changed)
                return;
            RegionChanged?.Invoke();
            Close();
        }

        static readonly Color LockedPicture = new Color(0.3f, 0.3f, 0.3f, 1);

        static void SetAction(Row row, string text, bool interactable, bool price = false)
        {
            row.ActionLabel.SetText(text);
            row.PriceIcon.SetActive(price);
            if (row.Action.interactable != interactable)
                row.Action.interactable = interactable;
        }
    }
}
