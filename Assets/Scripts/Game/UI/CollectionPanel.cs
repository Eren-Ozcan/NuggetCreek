using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen Nugget collection (design doc 3.1.2, 12.1 screen 6): the six global Rares,
    /// then each creek's Common and boss nugget with their stars. Each card shows the nugget's
    /// own picture; undiscovered types show it as a dark silhouette with "???", and an unbeaten
    /// boss names the creek where it waits.
    /// </summary>
    public sealed class CollectionPanel
    {
        /// <summary>Tint that turns a nugget picture into a shadow of itself.</summary>
        static readonly Color Silhouette = new Color(0.06f, 0.07f, 0.06f, 0.9f);

        sealed class Card
        {
            public int Index;
            public Image Swatch;
            public bool HasArt;
            public Text Title;
            public StarRow Stars;
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

            Text title = Ui.Title("Title", root, "NUGGETS", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.CloseButton(root, Close);

            total = Ui.Label("TotalStars", root, "", 34, TextAnchor.MiddleCenter, Palette.GoldText, FontStyle.Bold);
            total.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -230), new Vector2(0, -150));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -240));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            Section(list, "RARE  -  ANY CREEK");
            for (int index = GameCatalog.FirstRareNugget; index < GameCatalog.Nuggets.Count; index++)
                cards.Add(NewCard(list, index));
            for (int region = 0; region < session.Economy.Config.RegionCount; region++)
            {
                Section(list, $"{region + 1}. {GameCatalog.RegionNames[region].ToUpperInvariant()}");
                cards.Add(NewCard(list, GameCatalog.CommonNugget(region)));
                cards.Add(NewCard(list, GameCatalog.BossNugget(region)));
            }

            Close();
        }

        static void Section(Transform list, string text)
        {
            Text section = Ui.Label(text, list, text, 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(section, 70);
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
                bool boss = GameCatalog.IsBossNugget(card.Index);
                string kind = boss ? "Boss" : nugget.Rarity.ToString();
                int catches = session.NuggetCatches(card.Index);
                if (catches == 0)
                {
                    card.Swatch.color = card.HasArt ? Silhouette : Palette.Row * 1.4f;
                    card.Title.SetText("???");
                    card.Stars.SetActive(false);
                    card.Detail.SetText(boss ? $"Boss  -  beat the boss at {GameCatalog.RegionNames[nugget.RegionIndex]}" : $"{kind}  -  not found yet");
                    card.Flavor.SetText("");
                    continue;
                }
                card.Swatch.color = card.HasArt ? Color.white : ColorOf(nugget.Rarity);
                card.Title.SetText(nugget.Name);
                card.Stars.SetActive(true);
                card.Stars.Set(session.NuggetStars(card.Index));
                int? next = session.Economy.NextStarAt(card.Index, catches);
                string unit = boss ? "drops" : "found";
                string progress = next.HasValue ? $"{catches}/{next.Value} to next star" : $"{catches} {unit}  -  MAX";
                card.Detail.SetText($"{kind}  -  {progress}");
                card.Flavor.SetText(nugget.Flavor);
            }
        }

        Card NewCard(Transform list, int index)
        {
            Image background = Ui.Panel(GameCatalog.Nuggets[index].Id, list, Palette.Row);
            Ui.PreferredHeight(background, 190);
            RectTransform rt = background.rectTransform;
            var card = new Card { Index = index };

            Sprite art = Art.Nugget(index);
            card.HasArt = art != null;
            card.Swatch = Ui.Icon("Swatch", rt, art, Palette.Nugget);
            card.Swatch.rectTransform.Box(new Vector2(0, 0.5f), new Vector2(130, 130), new Vector2(22, 0));
            card.Stars = StarRow.Create("Stars", rt, session.Economy.MaxStarsPerNugget, 44);
            ((RectTransform)card.Stars.transform).Box(Vector2.one, new Vector2(card.Stars.Width, 44), new Vector2(-24, -22));
            card.Title = Ui.Label("Title", rt, "", 42, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            card.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(170, 0), new Vector2(-48 - card.Stars.Width, -18));
            card.Detail = Ui.Label("Detail", rt, "", 30, TextAnchor.MiddleLeft, Palette.TextMuted);
            card.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(170, 40), new Vector2(-24, -70));
            card.Flavor = Ui.Label("Flavor", rt, "", 28, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Italic);
            card.Flavor.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(170, 18), new Vector2(-24, -130));
            return card;
        }

        static Color ColorOf(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Rare: return Palette.RichNugget;
                case Rarity.Legendary: return Palette.GiantNugget;
                default: return Palette.Nugget;
            }
        }
    }
}
