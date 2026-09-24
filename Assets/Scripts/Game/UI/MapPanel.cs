using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>Full-screen Map modal: travel between unlocked creeks and unlock the next one.</summary>
    public sealed class MapPanel
    {
        sealed class Row
        {
            public Text Title;
            public Text Detail;
            public Button Action;
            public Text ActionLabel;
        }

        readonly GameSession session;
        readonly RectTransform root;
        readonly List<Row> rows = new List<Row>();

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised after an unlock or travel changes the active creek.</summary>
        public event Action RegionChanged;

        public MapPanel(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("Map", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "MAP", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            int count = session.Economy.Config.RegionCount;
            for (int i = 0; i < count; i++)
                rows.Add(NewRow(list, i));

            Close();
        }

        public void Open()
        {
            root.SetActive(true);
            Refresh();
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
                    SetAction(row, NumberFormat.Dollars(session.NextRegionCost.Value), session.CanAfford(session.NextRegionCost));
                }
                else
                {
                    row.Detail.SetText($"Unlock {GameCatalog.RegionNames[i - 1]} first.");
                    SetAction(row, "LOCKED", false);
                }
            }
        }

        Row NewRow(Transform list, int index)
        {
            Image background = Ui.Image(GameCatalog.RegionNames[index], list, Palette.Row);
            Ui.PreferredHeight(background, 170);
            var row = new Row();
            RectTransform rt = background.rectTransform;

            row.Title = Ui.Label("Title", rt, $"{index + 1}. {GameCatalog.RegionNames[index]}", 44, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 0), new Vector2(-330, -22));
            row.Detail = Ui.Label("Detail", rt, "", 32, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 22), new Vector2(-330, -80));

            row.Action = Ui.Button("Action", rt, "", Palette.Button, () => OnAction(index), out row.ActionLabel, 36);
            row.Action.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));
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

        static void SetAction(Row row, string text, bool interactable)
        {
            row.ActionLabel.SetText(text);
            if (row.Action.interactable != interactable)
                row.Action.interactable = interactable;
        }
    }
}
