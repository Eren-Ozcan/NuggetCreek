using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Creek chest card (design doc 6.4, 12.1 screen 11): starts the unlock, shows the timer and
    /// the Gem skip, then the reward with a rewarded-ad double. The card odds are printed on it
    /// (brand rule: chest contents are always listed).
    /// </summary>
    public sealed class ChestModal
    {
        readonly GameSession session;
        readonly IRewardedAds ads;
        readonly RectTransform root;
        readonly Text status;
        readonly Text reward;
        readonly Button openNow;
        readonly Text openNowLabel;
        readonly Button open;
        readonly Button doubleButton;
        readonly Button collect;
        ChestReward shown;

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action Claimed;

        public ChestModal(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("ChestModal", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Image("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(940, 1000));

            Text title = Ui.Label("Title", card, "CREEK CHEST", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -130), new Vector2(0, -30));

            EconomyConfig config = session.Economy.Config;
            string odds = $"Inside: Dollars and 1 gear card.\nCommon {config.ChestCardOdds[0] * 100:0}%  -  Rare {config.ChestCardOdds[1] * 100:0}%  -  Legendary {config.ChestCardOdds[2] * 100:0}%";
            Text contents = Ui.Label("Odds", card, odds, 32, TextAnchor.MiddleCenter, Palette.TextMuted);
            contents.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -260), new Vector2(-40, -140));

            status = Ui.Label("ChestStatus", card, "", 44, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            status.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -380), new Vector2(-40, -280));

            reward = Ui.Label("ChestReward", card, "", 40, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            reward.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -620), new Vector2(-40, -400));

            openNow = Ui.Button("OpenNow", card, "", Palette.GemButton, OpenNow, out openNowLabel, 40);
            openNow.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            open = Ui.Button("OpenChest", card, "Open", Palette.Button, Claim, out _, 44);
            open.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            doubleButton = Ui.Button("ChestDouble", card, "x2  (watch ad)", Palette.Ad, WatchDouble, out _, 40);
            doubleButton.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            collect = Ui.Button("ChestCollect", card, "Collect", Palette.ButtonAlt, Close, out _, 40);
            collect.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 40), new Vector2(-40, 150));

            Close();
        }

        /// <summary>Shows the chest, starting the unlock of a waiting one.</summary>
        public void Open()
        {
            if (!session.HasChest)
                return;
            session.StartChest();
            shown = null;
            root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            bool wasOpen = IsOpen;
            root.SetActive(false);
            if (wasOpen && shown != null)
                Claimed?.Invoke();
            shown = null;
        }

        public void Refresh()
        {
            if (!IsOpen)
                return;
            if (shown != null)
            {
                status.SetText("+" + NumberFormat.Dollars(shown.Doubled ? shown.Dollars * 2 : shown.Dollars));
                reward.SetText(CardText(shown.Card));
                openNow.SetActive(false);
                open.SetActive(false);
                doubleButton.SetActive(!shown.Doubled);
                if (doubleButton.interactable != ads.IsLoaded)
                    doubleButton.interactable = ads.IsLoaded;
                collect.SetActive(true);
                return;
            }

            reward.SetText("");
            doubleButton.SetActive(false);
            bool ready = session.ChestReady;
            status.SetText(ready ? "Ready!" : "Opens in " + CandidateModal.Clock(session.Progress.ChestSecondsLeft));
            open.SetActive(ready);
            int? cost = session.ChestInstantCost;
            openNow.SetActive(!ready && cost.HasValue);
            if (cost.HasValue)
            {
                openNowLabel.SetText("Open now  " + Effects.Gems(cost.Value));
                bool affordable = session.CanAffordGems(cost);
                if (openNow.interactable != affordable)
                    openNow.interactable = affordable;
            }
            collect.SetActive(true);
        }

        /// <summary>Player-facing line for one gear card.</summary>
        public static string CardText(GearCard card)
        {
            GearDefinition gear = GameCatalog.Gear[card.Index];
            if (card.IsNew)
                return $"NEW {gear.Rarity.ToString().ToUpperInvariant()} GEAR\n{gear.Name}";
            if (card.LevelledUp)
                return $"{gear.Name}\nlevel up!";
            return $"{gear.Name} is maxed\n+{Effects.Gems(card.Gems)}";
        }

        void OpenNow()
        {
            session.OpenChestNow();
            Refresh();
        }

        void Claim()
        {
            shown = session.ClaimChest();
            Refresh();
        }

        void WatchDouble()
        {
            ChestReward target = shown;
            ads.Show(rewarded =>
            {
                if (rewarded)
                    session.DoubleChest(target);
                ads.Load();
                Refresh();
            });
        }
    }
}
