using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Opening card shown over the shop after a gear box is bought: the box shakes and bursts,
    /// then the cards drop in one by one with their gear pictures. Driven from the shop's
    /// per-frame Refresh; Collect works from the first frame.
    /// </summary>
    public sealed class GearBoxReveal
    {
        const float ShakeSeconds = 0.45f;
        const float BurstSeconds = 0.3f;
        const float CardStepSeconds = 0.12f;
        const float CardPopSeconds = 0.25f;
        const float RowHeight = 104;
        const float RowStep = RowHeight + 8;
        const float RowsTop = 490;
        const float FooterHeight = 190;

        sealed class CardRow
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public Image Picture;
            public Text Name;
            public Text State;
        }

        readonly RectTransform root;
        readonly RectTransform card;
        readonly Text title;
        readonly Image box;
        readonly List<CardRow> rows = new List<CardRow>();
        readonly Sprite[] boxSprites;
        int shownCards;
        float openedAt;

        public bool IsOpen => root.gameObject.activeSelf;

        public GearBoxReveal(Transform parent, Sprite[] boxSprites, int maxCards)
        {
            this.boxSprites = boxSprites;
            root = Ui.Image("GearBoxReveal", parent, Palette.Dim).rectTransform.Fill();
            card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(960, CardHeight(maxCards)));

            title = Ui.Title("Title", card, "", 52);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -130), new Vector2(0, -30));

            box = Ui.Icon("BoxPicture", card, null, Palette.Nugget);
            box.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(260, -470), new Vector2(-260, -150));

            for (int i = 0; i < maxCards; i++)
                rows.Add(NewRow(card, i));

            Ui.Button("BoxCollect", card, "Collect", Palette.Button, Close, out _, 44).AsRect()
                .Box(new Vector2(0.5f, 0), new Vector2(420, 130), new Vector2(0, 30));

            Close();
        }

        public void Show(int boxIndex, string boxName, IReadOnlyList<GearCard> cards)
        {
            title.SetText(boxName.ToUpperInvariant());
            Sprite sprite = boxIndex >= 0 && boxIndex < boxSprites.Length ? boxSprites[boxIndex] : null;
            box.sprite = sprite != null ? sprite : Ui.Circle;
            box.color = sprite != null ? Color.white : Palette.Nugget;
            shownCards = Mathf.Min(cards.Count, rows.Count);
            card.sizeDelta = new Vector2(card.sizeDelta.x, CardHeight(shownCards));
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Root.SetActive(i < shownCards);
                if (i < shownCards)
                    Fill(rows[i], cards[i]);
            }
            openedAt = Time.unscaledTime;
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            float t = Time.unscaledTime - openedAt;

            // The box shakes, swells and fades to a faint backdrop behind the title.
            float burst = Motion.FadeIn(t, ShakeSeconds, BurstSeconds);
            box.rectTransform.localRotation = Quaternion.Euler(0, 0, Motion.Shake(t, ShakeSeconds, 12));
            box.rectTransform.localScale = Vector3.one * (1 + 0.1f * burst);
            Color tint = box.color;
            tint.a = 1 - 0.7f * burst;
            box.color = tint;

            float cardsStart = ShakeSeconds + BurstSeconds * 0.5f;
            for (int i = 0; i < shownCards; i++)
            {
                float local = t - cardsStart - i * CardStepSeconds;
                rows[i].Group.alpha = Motion.FadeIn(local, 0, 0.1f);
                rows[i].Root.localScale = Vector3.one * (local <= 0 ? 0.8f : Motion.Punch(local, CardPopSeconds, 1.1f));
            }
        }

        static float CardHeight(int cardCount) => RowsTop + cardCount * RowStep + FooterHeight;

        CardRow NewRow(RectTransform card, int index)
        {
            float top = -RowsTop - index * RowStep;
            Image background = Ui.Panel("Card" + index, card, Palette.Row);
            RectTransform rt = background.rectTransform.Place(new Vector2(0, 1), Vector2.one,
                new Vector2(40, top - RowHeight), new Vector2(-40, top));
            var row = new CardRow { Root = rt, Group = rt.gameObject.AddComponent<CanvasGroup>() };

            // Every row reserves the picture slot; the sprite is set per card.
            float inset = Ui.RowPicture(rt, RowHeight, Ui.Circle, out row.Picture);
            row.Name = Ui.Label("Name", rt, "", 36, TextAnchor.MiddleLeft, Palette.Text, FontStyle.Bold);
            row.Name.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 0), new Vector2(-300, 0));
            row.State = Ui.Label("State", rt, "", 32, TextAnchor.MiddleRight, Palette.TextMuted, FontStyle.Bold);
            row.State.rectTransform.Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 0), new Vector2(-24, 0));
            return row;
        }

        static void Fill(CardRow row, GearCard card)
        {
            GearDefinition gear = GameCatalog.Gear[card.Index];
            Sprite sprite = Art.Gear(gear.Id);
            row.Picture.sprite = sprite != null ? sprite : Ui.Circle;
            row.Picture.color = sprite != null ? Color.white : RarityColor(gear.Rarity);
            row.Name.SetText(gear.Name);
            if (card.IsNew)
            {
                row.State.SetText("NEW " + gear.Rarity.ToString().ToUpperInvariant());
                row.State.color = RarityColor(gear.Rarity);
            }
            else if (card.LevelledUp)
            {
                row.State.SetText("Level up!");
                row.State.color = Palette.Text;
            }
            else
            {
                row.State.SetText("+" + Effects.Gems(card.Gems));
                row.State.color = Palette.GemText;
            }
        }

        static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Rare: return Palette.RichNugget;
                case Rarity.Legendary: return Palette.GoldText;
                default: return Palette.TextMuted;
            }
        }
    }
}
