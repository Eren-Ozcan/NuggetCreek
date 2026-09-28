using System;
using System.Collections.Generic;
using System.Text;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen shop (design doc 8.2e, 12.1 screen 4) in three tabs: OFFERS (timed packs),
    /// GEM SHOP (boosts, Mother Lode, gear boxes) and GEMS (the Gem ladder). Real-money
    /// purchases go through <see cref="IStore"/>; the session grants each transaction once.
    /// </summary>
    public sealed class ShopPanel
    {
        public enum Tab
        {
            Offers,
            GemShop,
            Gems,
        }

        static readonly string[] TabNames = { "OFFERS", "GEM SHOP", "GEMS" };
        static readonly string[] BoxNames = { "Green Box", "Orange Box", "Red Box" };
        static readonly string[] BoxSprites = { "Chests/box_green", "Chests/box_orange", "Chests/box_red" };

        sealed class Row
        {
            public RectTransform Root;
            public Text Title;
            public Text Detail;
            public Button Buy;
            public Text BuyLabel;
            public IconBesideText PriceIcon;
            public Action Refresh;
        }

        readonly GameSession session;
        readonly IStore store;
        readonly RectTransform root;
        readonly Button[] tabButtons = new Button[3];
        readonly RectTransform[] pages = new RectTransform[3];
        readonly List<Row> rows = new List<Row>();
        readonly Text noOffers;
        readonly Text adsLine;
        readonly Text result;
        readonly GearBoxReveal boxReveal;
        Tab tab = Tab.Offers;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Something was bought; save now.</summary>
        public event Action Purchased;

        /// <summary>The player asked for a Mother Lode from the GEM SHOP tab.</summary>
        public event Action SummonLodeRequested;

        public ShopPanel(GameSession session, IStore store, Transform canvas)
        {
            this.session = session;
            this.store = store;
            root = Ui.Image("Shop", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "SHOP", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            for (int i = 0; i < 3; i++)
            {
                var index = (Tab)i;
                tabButtons[i] = Ui.Button("Tab" + index, root, TabNames[i], Palette.ButtonAlt, () => Show(index), out _, 38);
                tabButtons[i].AsRect().Place(new Vector2(i / 3f, 1), new Vector2((i + 1) / 3f, 1),
                    new Vector2(i == 0 ? 20 : 8, -170 - Ui.TapHeight), new Vector2(i == 2 ? -20 : -8, -170));
            }

            result = Ui.Label("ShopResult", root, "", 34, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            result.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(30, 20), new Vector2(-30, 170));

            for (int i = 0; i < 3; i++)
            {
                pages[i] = Ui.Rect("Page" + (Tab)i, root).Place(Vector2.zero, Vector2.one, new Vector2(0, 180), new Vector2(0, -180 - Ui.TapHeight));
            }

            RectTransform offers = Ui.ScrollList(pages[0], 16, 24);
            noOffers = Ui.Label("NoOffers", offers, "No timed offers right now. Check back tomorrow.", 34, TextAnchor.MiddleCenter, Palette.TextMuted);
            Ui.PreferredHeight(noOffers, 120);
            foreach (ShopItem item in ShopCatalog.Items)
                if (item.IsOffer)
                    AddOfferRow(offers, item);
            adsLine = Ui.Label("AdsLine", offers, "", 30, TextAnchor.MiddleCenter, Palette.TextMuted);
            Ui.PreferredHeight(adsLine, 80);

            RectTransform gemShop = Ui.ScrollList(pages[1], 16, 24);
            Section(gemShop, "BOOSTS");
            AddBoostRow(gemShop, ShopBoost.GoldWash, "Gold Wash",
                $"Income x{session.Economy.Config.GoldWashMultiplier:0} for {session.Economy.Config.GoldWashSeconds / 60:0} min");
            AddBoostRow(gemShop, ShopBoost.ExtraShift, "Extra Shift", null);
            AddBoostRow(gemShop, ShopBoost.RichVein, "Rich Vein", "A Giant Nugget shows up now");
            AddLodeRow(gemShop);
            Section(gemShop, "GEAR BOXES");
            for (int box = 0; box < session.GearBoxCount; box++)
                AddBoxRow(gemShop, box);

            RectTransform gems = Ui.ScrollList(pages[2], 16, 24);
            foreach (ShopItem item in ShopCatalog.Items)
                if (!item.IsOffer)
                    AddGemRow(gems, item);
            Button restore = Ui.Button("Restore", gems, "Restore purchases", Palette.ButtonAlt, RestorePurchases, out _, 34);
            Ui.PreferredHeight(restore, Ui.TapHeight);

            var boxArt = new Sprite[BoxSprites.Length];
            for (int i = 0; i < boxArt.Length; i++)
                boxArt[i] = Art.Get(BoxSprites[i]);
            int maxCards = 0;
            foreach (int count in session.Economy.Config.GearBoxCards)
                maxCards = Mathf.Max(maxCards, count);
            boxReveal = new GearBoxReveal(root, boxArt, maxCards);

            Close();
        }

        public void Open() => Open(tab);

        public void Open(Tab start)
        {
            result.SetText("");
            root.SetActive(true);
            Show(start);
        }

        public void Close()
        {
            boxReveal.Close();
            root.SetActive(false);
        }

        public void Show(Tab next)
        {
            tab = next;
            for (int i = 0; i < 3; i++)
            {
                pages[i].SetActive(i == (int)next);
                tabButtons[i].GetComponent<Image>().color = i == (int)next ? Palette.Button : Palette.ButtonAlt;
            }
            Refresh();
        }

        public void Refresh()
        {
            if (!IsOpen)
                return;
            boxReveal.Refresh();
            foreach (Row row in rows)
                row.Refresh();
            noOffers.SetActive(ActiveTimedOffers() == 0);
            long cents = session.CentsToRemoveAds;
            adsLine.SetText(session.Progress.AdsRemoved ? "Ads are off for good. Thank you!"
                : $"{Money(cents)} more in any purchase removes ads.");
            if (session.Progress.AdsRemovedNoticePending)
            {
                result.SetText(result.text + (result.text.Length > 0 ? "\n" : "") + "Ads are off for good.");
                session.AcknowledgeAdsRemoved();
            }
        }

        int ActiveTimedOffers()
        {
            int count = 0;
            foreach (ShopItem item in session.ActiveOffers())
                if (item.Kind != ShopKind.RemoveAds && item.Kind != ShopKind.GearSlot)
                    count++;
            return count;
        }

        /// <summary>Timed packs on sale, for the badge on the Shop button.</summary>
        public bool HasTimedOffer() => ActiveTimedOffers() > 0;

        // --- Rows ---

        void AddOfferRow(Transform list, ShopItem item)
        {
            Row row = NewRow(list, "Offer_" + item.Kind, OfferPicture(item), () => BuyReal(item));
            row.Refresh = () =>
            {
                double left = session.OfferSecondsLeft(item);
                row.Root.SetActive(left > 0);
                if (left <= 0)
                    return;
                session.ReportOfferShown(item, "shop");
                row.Title.SetText(item.Name);
                string clock = double.IsInfinity(left) ? "" : "  -  ends in " + TextFormat.Duration(left);
                row.Detail.SetText(Contents(item) + clock + Badge(item));
                SetButton(row, store.PriceText(item), !store.IsBusy);
            };
        }

        void AddGemRow(Transform list, ShopItem item)
        {
            Row row = NewRow(list, "Gems_" + item.Id, Art.Gem, () => BuyReal(item));
            row.Refresh = () =>
            {
                row.Title.SetText(item.Name);
                row.Detail.SetText(Effects.Gems(item.Gems) + Badge(item));
                SetButton(row, store.PriceText(item), !store.IsBusy);
            };
        }

        void AddBoostRow(Transform list, ShopBoost boost, string name, string detail)
        {
            Row row = NewRow(list, "Boost_" + boost, BoostPicture(boost), () => BuyBoost(boost), gemPrice: true);
            row.Refresh = () =>
            {
                row.Title.SetText(name);
                if (boost == ShopBoost.ExtraShift)
                    row.Detail.SetText(session.BoostLocked(boost) ? "Skip 2 hours of crew work. Needs Amos."
                        : $"Skip 2 hours: {NumberFormat.Dollars(session.ExtraShiftPayout)} now");
                else if (boost == ShopBoost.GoldWash && session.Progress.GoldWashSecondsLeft > 0)
                    row.Detail.SetText($"{detail}  -  running {CandidateModal.Clock(session.Progress.GoldWashSecondsLeft)}");
                else
                    row.Detail.SetText(detail);
                double cooldown = session.BoostCooldown(boost);
                string label = cooldown > 0 ? "OUT OF STOCK " + CandidateModal.Clock(cooldown) : session.BoostGems.ToString();
                SetButton(row, label, session.CanBuyBoost(boost), price: cooldown <= 0);
            };
        }

        void AddLodeRow(Transform list)
        {
            int gems = session.Economy.Config.MotherLodeSummonGems;
            Row row = NewRow(list, "SummonLode", Art.Get("MotherLode/boulder_0"), () => SummonLodeRequested?.Invoke(), gemPrice: true);
            row.Refresh = () =>
            {
                // Known once a natural Mother Lode has come (design doc 8.2c).
                bool known = session.Progress.PlaySeconds >= session.Economy.Config.MotherLodeFirstAfterSeconds;
                row.Root.SetActive(known);
                row.Title.SetText("Mother Lode");
                row.Detail.SetText("Call the Mother Lode now");
                SetButton(row, gems.ToString(), session.CanAffordGems(gems), price: true);
            };
        }

        void AddBoxRow(Transform list, int box)
        {
            EconomyConfig config = session.Economy.Config;
            Row row = NewRow(list, "Box_" + box, Art.Get(BoxSprites[box]), () => BuyBox(box), gemPrice: true);
            row.Refresh = () =>
            {
                int start = box * 3;
                row.Title.SetText($"{BoxNames[box]}  -  {config.GearBoxCards[box]} cards");
                string odds = $"At least 1 {config.GearBoxGuarantee[box]}.  C {config.GearBoxOdds[start] * 100:0}% / R {config.GearBoxOdds[start + 1] * 100:0}% / L {config.GearBoxOdds[start + 2] * 100:0}%";
                row.Detail.SetText(session.GearUnlocked ? odds : "Opens with your first creek chest");
                SetButton(row, config.GearBoxGems[box].ToString(), session.GearUnlocked && session.CanAffordGems(config.GearBoxGems[box]), price: true);
            };
        }

        /// <summary>Real-money rows show a store price, so only Gem-priced rows get the Gem icon.</summary>
        Row NewRow(Transform list, string name, Sprite picture, Action onBuy, bool gemPrice = false)
        {
            const float height = 170;
            Image background = Ui.Image(name, list, Palette.Row);
            Ui.PreferredHeight(background, height);
            var row = new Row { Root = background.rectTransform };
            float inset = Ui.RowPicture(row.Root, height, picture, out _);
            row.Title = Ui.Label("Title", row.Root, "", 42, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 0), new Vector2(-330, -22));
            row.Detail = Ui.Label("Detail", row.Root, "", 30, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 16), new Vector2(-330, -80));
            row.Buy = Ui.Button("Buy", row.Root, "", Palette.GemButton, onBuy, out row.BuyLabel, 32);
            row.Buy.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));
            if (gemPrice)
                row.PriceIcon = Ui.PriceIcon(row.BuyLabel, Art.Gem, Palette.Gem);
            rows.Add(row);
            return row;
        }

        static Sprite BoostPicture(ShopBoost boost)
        {
            switch (boost)
            {
                case ShopBoost.GoldWash: return Art.GoldDust;
                case ShopBoost.ExtraShift: return Art.Icon("timer");
                default: return Art.NuggetIcon;
            }
        }

        static Sprite OfferPicture(ShopItem item)
        {
            switch (item.Kind)
            {
                case ShopKind.RemoveAds: return Art.Icon("watch_ad");
                case ShopKind.GearSlot: return Art.Icon("plus");
                default: return Art.Icon("gift");
            }
        }

        static void Section(Transform list, string text)
        {
            Text label = Ui.Label(text, list, text, 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
        }

        static void SetButton(Row row, string text, bool interactable, bool price = false)
        {
            row.BuyLabel.SetText(text);
            if (row.PriceIcon != null)
                row.PriceIcon.SetActive(price);
            if (row.Buy.interactable != interactable)
                row.Buy.interactable = interactable;
        }

        string Badge(ShopItem item) =>
            item.Kind != ShopKind.RemoveAds && session.RemovesAds(item) ? "  -  Removes ads" : "";

        static string Contents(ShopItem item)
        {
            if (item.Kind == ShopKind.GearSlot)
                return "A 4th gear slot, for good";
            if (item.Kind == ShopKind.RemoveAds)
                return "No more forced ads. Rewarded ads stay optional.";
            var parts = new List<string>();
            if (item.Crew > 0)
                parts.Add(item.Crew == 1 ? "1 crew member" : $"{item.Crew} crew");
            if (item.Gems > 0)
                parts.Add(Effects.Gems(item.Gems));
            if (item.GearCards > 0)
                parts.Add(item.GearCards == 1 ? "1 gear card" : $"{item.GearCards} gear cards");
            if (item.BoostHours > 0)
                parts.Add($"x{item.BoostMultiplier:0} income {item.BoostHours:0} h");
            return string.Join(" + ", parts);
        }

        static string Money(long cents) => "$" + (cents / 100.0).ToString("0.00");

        // --- Buying ---

        void BuyReal(ShopItem item)
        {
            session.StartPurchase(item);
            store.Purchase(item, outcome =>
            {
                if (!outcome.Paid)
                {
                    session.FailPurchase(item, outcome.Reason);
                    return;
                }
                PurchaseResult granted = session.GrantPurchase(item, outcome.TransactionId, outcome.PriceLocal, outcome.Currency);
                if (granted == null)
                    return;
                result.SetText(Describe(granted));
                Purchased?.Invoke();
                Refresh();
            });
            Refresh();
        }

        /// <summary>Brings back Remove Ads and the gear slot after a reinstall (8.3).</summary>
        void RestorePurchases()
        {
            store.Restore(owned =>
            {
                bool changed = session.RestoreOwned(owned);
                result.SetText(changed ? "Restored." : "Nothing to restore.");
                if (changed)
                    Purchased?.Invoke();
                Refresh();
            });
        }

        void BuyBoost(ShopBoost boost)
        {
            BigNumber payout = session.ExtraShiftPayout;
            if (!session.BuyBoost(boost))
                return;
            switch (boost)
            {
                case ShopBoost.GoldWash: result.SetText("Gold Wash is running!"); break;
                case ShopBoost.ExtraShift: result.SetText("Extra Shift paid " + NumberFormat.Dollars(payout)); break;
                case ShopBoost.RichVein: result.SetText("A Giant Nugget is on its way!"); break;
            }
            Purchased?.Invoke();
            Refresh();
        }

        void BuyBox(int box)
        {
            List<GearCard> cards = session.BuyGearBox(box);
            if (cards == null)
                return;
            var text = new StringBuilder("Got:");
            foreach (GearCard card in cards)
                text.Append("  ").Append(CardText(card)).Append(',');
            result.SetText(text.ToString().TrimEnd(','));
            boxReveal.Show(box, BoxNames[box], cards);
            Purchased?.Invoke();
            Refresh();
        }

        static string CardText(GearCard card)
        {
            string name = GameCatalog.Gear[card.Index].Name;
            return card.IsNew ? name + " (new)" : card.LevelledUp ? name + " +1" : $"{name} +{Effects.Gems(card.Gems)}";
        }

        static string Describe(PurchaseResult granted)
        {
            var parts = new List<string>();
            if (granted.Gems > 0)
                parts.Add(Effects.Gems(granted.Gems));
            foreach (int member in granted.Crew)
                parts.Add(GameCatalog.Crew[member].Name + " joins");
            foreach (GearCard card in granted.Cards)
                parts.Add(CardText(card));
            if (granted.Boost)
                parts.Add($"x{granted.Item.BoostMultiplier:0} income for {granted.Item.BoostHours:0} h");
            if (granted.GearSlot)
                parts.Add("4th gear slot");
            return parts.Count > 0 ? "You got: " + string.Join(", ", parts) : "Thank you!";
        }
    }
}
