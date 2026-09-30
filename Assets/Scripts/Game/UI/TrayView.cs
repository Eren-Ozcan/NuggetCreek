using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The tray down the right side (design doc 3.1.4): one tile per caught chest, pouch and
    /// crate, chests first (the unlocking one on top), then pouches, then crates. As many tiles
    /// show as fit above the shop button, at most <see cref="MaxTiles"/>; the rest wait out of
    /// sight and a "+N" on the last tile says how many.
    /// </summary>
    public sealed class TrayView
    {
        public const int MaxTiles = 10;

        sealed class Tile
        {
            public Button Button;
            public Text Caption;
            public Text More;
            public RectTransform Picture;
            public FloaterKind? Kind;
        }

        readonly GameSession session;
        readonly RectTransform column;
        readonly float tileSize;
        readonly float gap;
        readonly float bottomReserve;
        readonly Action<FloaterKind> open;
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<FloaterKind> items = new List<FloaterKind>();

        /// <param name="column">The right-hand column; tiles go under the buttons already in it.</param>
        /// <param name="bottomReserve">Screen kept free at the bottom, for the shop button.</param>
        public TrayView(GameSession session, RectTransform column, float tileSize, float gap, float bottomReserve, Action<FloaterKind> open)
        {
            this.session = session;
            this.column = column;
            this.tileSize = tileSize;
            this.gap = gap;
            this.bottomReserve = bottomReserve;
            this.open = open;
            for (int i = 0; i < MaxTiles; i++)
                tiles.Add(CreateTile(i));
        }

        /// <summary>Tiles shown right now.</summary>
        public int VisibleCount { get; private set; }

        /// <summary>Items waiting out of sight.</summary>
        public int HiddenCount => Math.Max(0, items.Count - VisibleCount);

        Tile CreateTile(int index)
        {
            var tile = new Tile();
            int slot = index;
            tile.Button = Ui.Button("Tray" + index, column, "", Palette.ButtonAlt, () => Press(slot), out tile.Caption, 24);
            tile.Button.AsRect().sizeDelta = new Vector2(tileSize, tileSize);
            float captionHeight = tileSize * 0.3f;
            tile.Caption.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(4, 12), new Vector2(-4, captionHeight + 10));
            tile.Caption.resizeTextForBestFit = true;
            tile.Caption.resizeTextMinSize = 12;
            tile.Caption.resizeTextMaxSize = Mathf.RoundToInt(tileSize * 0.2f);
            tile.Caption.verticalOverflow = VerticalWrapMode.Truncate;
            var outline = tile.Caption.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.45f);
            outline.effectDistance = new Vector2(2, -2);
            var shadow = tile.Button.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0.08f, 0.08f, 0.35f);
            shadow.effectDistance = new Vector2(0, -8);

            tile.More = Ui.Label("More", tile.Button.transform, "", 30, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            RectTransform more = Ui.Image("MoreBadge", tile.Button.transform, Palette.Badge, Ui.Circle).rectTransform
                .Box(new Vector2(0, 1), new Vector2(64, 64), new Vector2(-10, 10));
            more.GetComponent<Image>().raycastTarget = false;
            tile.More.transform.SetParent(more, false);
            tile.More.rectTransform.Fill();
            tile.Button.gameObject.SetActive(false);
            return tile;
        }

        /// <summary>Lays out the tiles for the current tray; call every frame.</summary>
        public void Refresh(bool hidden)
        {
            items.Clear();
            for (int i = 0; i < session.ChestsInTray; i++)
                items.Add(FloaterKind.Chest);
            for (int i = 0; i < session.Progress.PouchesWaiting; i++)
                items.Add(FloaterKind.Pouch);
            for (int i = 0; i < session.Progress.CratesWaiting; i++)
                items.Add(FloaterKind.Crate);

            VisibleCount = hidden ? 0 : Math.Min(items.Count, Capacity());
            for (int i = 0; i < tiles.Count; i++)
            {
                Tile tile = tiles[i];
                bool show = i < VisibleCount;
                if (tile.Button.gameObject.activeSelf != show)
                    tile.Button.gameObject.SetActive(show);
                if (!show)
                    continue;
                FloaterKind kind = items[i];
                if (tile.Kind != kind)
                    Dress(tile, kind);
                bool firstChest = kind == FloaterKind.Chest && i == 0;
                tile.Caption.SetText(Caption(kind, firstChest));
                float bob = firstChest && session.ChestReady ? Motion.Bob(Time.unscaledTime) : 1;
                tile.Picture.localScale = Vector3.one * bob;
                bool last = i == VisibleCount - 1 && HiddenCount > 0;
                tile.More.transform.parent.gameObject.SetActive(last);
                if (last)
                    tile.More.SetText("+" + HiddenCount);
            }
        }

        /// <summary>How many tiles fit between the buttons above them and the reserve at the bottom.</summary>
        int Capacity()
        {
            var screen = (RectTransform)column.parent;
            int above = 0;
            foreach (Transform child in column)
                if (child.gameObject.activeSelf && !IsTile(child))
                    above++;
            float top = -column.anchoredPosition.y;
            float room = screen.rect.height - top - bottomReserve - above * (tileSize + gap);
            return Mathf.Clamp(Mathf.FloorToInt((room + gap) / (tileSize + gap)), 0, MaxTiles);
        }

        bool IsTile(Transform child)
        {
            foreach (Tile tile in tiles)
                if (tile.Button.transform == child)
                    return true;
            return false;
        }

        void Dress(Tile tile, FloaterKind kind)
        {
            tile.Kind = kind;
            tile.Button.GetComponent<Image>().color = kind == FloaterKind.Chest ? Palette.Nugget
                : kind == FloaterKind.Pouch ? Palette.Ad : Palette.ButtonAlt;
            if (tile.Picture != null)
                UnityEngine.Object.Destroy(tile.Picture.gameObject);
            float captionHeight = tileSize * 0.3f;
            tile.Picture = FloaterPicture.Build("Picture", tile.Button.transform, kind, tileSize);
            tile.Picture.Place(Vector2.zero, Vector2.one, new Vector2(14, captionHeight + 4), new Vector2(-14, -10));
            // The caption and the badge stay on top of the picture.
            tile.Caption.transform.SetAsLastSibling();
            tile.More.transform.parent.SetAsLastSibling();
        }

        string Caption(FloaterKind kind, bool firstChest)
        {
            switch (kind)
            {
                case FloaterKind.Chest:
                    if (firstChest && session.ChestReady)
                        return "Ready!";
                    if (firstChest && session.Progress.ChestOpening)
                        return CandidateModal.Clock(session.Progress.ChestSecondsLeft);
                    return "Chest";
                case FloaterKind.Pouch:
                    return "Gems";
                default:
                    return "Crate";
            }
        }

        void Press(int slot)
        {
            if (slot < items.Count)
                open(items[slot]);
        }

        /// <summary>World position a caught floater of this kind flies to: its last tile, else the last tile shown.</summary>
        public Vector3 LandingPoint(FloaterKind kind)
        {
            int target = -1;
            for (int i = 0; i < VisibleCount; i++)
                if (items[i] == kind)
                    target = i;
            if (target < 0)
                target = VisibleCount - 1;
            if (target < 0)
                return column.position;
            RectTransform rect = tiles[target].Button.AsRect();
            return rect.TransformPoint(rect.rect.center);
        }
    }
}
