using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen Nugget collection (design doc 3.1.2, 12.1 screen 6): the five types of each
    /// creek with their stars. Undiscovered types show as "???".
    /// </summary>
    public sealed class CollectionPanel
    {
        sealed class Card
        {
            public int Index;
            public Image Swatch;
            public Text Title;
            public Text Detail;
            public Text Flavor;
        }

        readonly GameSession session;
        readonly RectTransform root;
        readonly Text total;
        readonly List<Card> cards = new List<Card>();

        public bool IsOpen => root.gameObject.activeSelf;

        public CollectionPanel(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("Collection", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "NUGGETS", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            total = Ui.Label("TotalStars", root, "", 34, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            total.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -230), new Vector2(0, -150));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -240));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            for (int region = 0; region < session.Economy.Config.RegionCount; region++)
            {
                Text section = Ui.Label(GameCatalog.RegionNames[region], list, GameCatalog.RegionNames[region].ToUpperInvariant(),
                    38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
                Ui.PreferredHeight(section, 70);
                foreach (int index in GameCatalog.NuggetsInRegion(region))
                    cards.Add(NewCard(list, index));
            }

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
            int stars = session.TotalStars;
            string bonus = stars > 0 ? $"  -  prestige bonus +{stars * session.Economy.Config.StarPrestigeBonus * 100:0}%" : "";
            total.SetText($"{stars}/{session.MaxStars} stars{bonus}");

            foreach (Card card in cards)
            {
                NuggetDefinition nugget = GameCatalog.Nuggets[card.Index];
                int catches = session.NuggetCatches(card.Index);
                if (catches == 0)
                {
                    card.Swatch.color = Palette.Row * 1.4f;
                    card.Title.SetText("???");
                    card.Detail.SetText($"{nugget.Rarity}  -  not found yet");
                    card.Flavor.SetText("");
                    continue;
                }
                card.Swatch.color = ColorOf(nugget.Rarity);
                int starCount = session.NuggetStars(card.Index);
                card.Title.SetText($"{nugget.Name}  {StarText(starCount, session.Economy.MaxStarsPerNugget)}");
                int? next = session.Economy.NextStarAt(catches);
                string progress = next.HasValue ? $"{catches}/{next.Value} to next star" : $"{catches} found  -  MAX";
                card.Detail.SetText($"{nugget.Rarity}  -  {progress}");
                card.Flavor.SetText(nugget.Flavor);
            }
        }

        Card NewCard(Transform list, int index)
        {
            Image background = Ui.Image(GameCatalog.Nuggets[index].Id, list, Palette.Row);
            Ui.PreferredHeight(background, 190);
            RectTransform rt = background.rectTransform;
            var card = new Card { Index = index };

            card.Swatch = Ui.Image("Swatch", rt, Palette.Nugget, Ui.Circle);
            card.Swatch.rectTransform.Box(new Vector2(0, 0.5f), new Vector2(110, 110), new Vector2(30, 0));
            card.Title = Ui.Label("Title", rt, "", 42, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            card.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(170, 0), new Vector2(-24, -18));
            card.Detail = Ui.Label("Detail", rt, "", 30, TextAnchor.MiddleLeft, Palette.TextMuted);
            card.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(170, 40), new Vector2(-24, -70));
            card.Flavor = Ui.Label("Flavor", rt, "", 28, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Italic);
            card.Flavor.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(170, 18), new Vector2(-24, -130));
            return card;
        }

        /// <summary>Stars as text, e.g. "[**-]"; the greybox font has no star glyph.</summary>
        public static string StarText(int stars, int max) =>
            "[" + new string('*', stars) + new string('-', Mathf.Max(0, max - stars)) + "]";

        static Color ColorOf(NuggetRarity rarity)
        {
            switch (rarity)
            {
                case NuggetRarity.Rare: return Palette.RichNugget;
                case NuggetRarity.Legendary: return Palette.GiantNugget;
                default: return Palette.Nugget;
            }
        }
    }
}
