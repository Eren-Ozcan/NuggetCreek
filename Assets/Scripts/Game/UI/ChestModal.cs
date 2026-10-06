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
        const float ShakeSeconds = 0.35f;
        const float OpeningSeconds = 0.2f;
        const float PopSeconds = 0.3f;

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
        readonly Button swap;
        readonly Text swapLabel;
        readonly Image picture;
        readonly CanvasGroup rewardGroup;
        readonly Sprite closedSprite = Art.Get("Chests/creek_closed");
        readonly Sprite openingSprite = Art.Get("Chests/creek_opening");
        readonly Sprite openSprite = Art.Get("Chests/creek_open");
        ChestReward shown;
        float openedAt;
        bool swapOffered;

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action Claimed;

        public ChestModal(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("ChestModal", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(940, 1380));

            Text title = Ui.Title("Title", card, "CREEK CHEST", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -130), new Vector2(0, -30));

            EconomyConfig config = session.Economy.Config;
            string odds = $"Inside: Dollars and 1 gear card.\nCommon {config.ChestCardOdds[0] * 100:0}%  -  Rare {config.ChestCardOdds[1] * 100:0}%  -  Legendary {config.ChestCardOdds[2] * 100:0}%";
            Text contents = Ui.Label("Odds", card, odds, 32, TextAnchor.MiddleCenter, Palette.TextMuted);
            contents.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -260), new Vector2(-40, -140));

            picture = Ui.Icon("ChestPicture", card, closedSprite);
            picture?.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(170, -610), new Vector2(-170, -280));

            // Dollars and the card fade in once the lid is open.
            RectTransform rewardArea = Ui.Rect("RewardArea", card).Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -940), new Vector2(-40, -610));
            rewardGroup = rewardArea.gameObject.AddComponent<CanvasGroup>();
            status = Ui.Label("ChestStatus", rewardArea, "", 44, TextAnchor.MiddleCenter, Palette.GoldText, FontStyle.Bold);
            status.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -90), Vector2.zero);
            reward = Ui.Label("ChestReward", rewardArea, "", 40, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            reward.rectTransform.Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -100));

            openNow = Ui.Button("OpenNow", card, "", Palette.GemButton, OpenNow, out openNowLabel, 40);
            openNow.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            open = Ui.Button("OpenChest", card, "Open", Palette.Button, Claim, out _, 44);
            open.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            doubleButton = Ui.Button("ChestDouble", card, "x2  (watch ad)", Palette.Ad, WatchDouble, out _, 40);
            doubleButton.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            collect = Ui.Button("ChestCollect", card, "Collect", Palette.ButtonAlt, Close, out _, 40);
            collect.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 40), new Vector2(-40, 150));
            // New gear that outranks a worn piece while every slot is full can swap in from here.
            swap = Ui.Button("ChestSwap", card, "", Palette.Button, Swap, out swapLabel, 36);
            swap.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 305), new Vector2(-40, 425));

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
                AnimateOpening(Time.unscaledTime - openedAt);
                status.SetText("+" + NumberFormat.Dollars(shown.Doubled ? shown.Dollars * 2 : shown.Dollars));
                reward.SetText(CardText(shown.Card));
                openNow.SetActive(false);
                open.SetActive(false);
                doubleButton.SetActive(!shown.Doubled);
                if (doubleButton.interactable != ads.IsLoaded)
                    doubleButton.interactable = ads.IsLoaded;
                RefreshSwap();
                collect.SetActive(true);
                return;
            }
            swap.SetActive(false);

            reward.SetText("");
            doubleButton.SetActive(false);
            bool ready = session.ChestReady;
            rewardGroup.alpha = 1;
            ShowPicture(closedSprite, ready ? Motion.Bob(Time.unscaledTime) : 1, 0);
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

        /// <summary>
        /// Lid animation after Open: the closed chest shakes, pops through the opening frame to
        /// the open one, and the reward fades in. Buttons work from the first frame.
        /// </summary>
        void AnimateOpening(float t)
        {
            if (t < ShakeSeconds)
                ShowPicture(closedSprite, 1, Motion.Shake(t, ShakeSeconds));
            else if (t < ShakeSeconds + OpeningSeconds)
                ShowPicture(openingSprite, 1.05f, 0);
            else
                ShowPicture(openSprite, Motion.Punch(t - ShakeSeconds - OpeningSeconds, PopSeconds), 0);
            rewardGroup.alpha = Motion.FadeIn(t, ShakeSeconds + OpeningSeconds, 0.2f);
        }

        void ShowPicture(Sprite sprite, float scale, float degrees)
        {
            if (picture == null)
                return;
            if (sprite != null && picture.sprite != sprite)
                picture.sprite = sprite;
            picture.rectTransform.localScale = Vector3.one * scale;
            picture.rectTransform.localRotation = Quaternion.Euler(0, 0, degrees);
        }

        void RefreshSwap()
        {
            swap.SetActive(swapOffered);
            if (!swapOffered)
                return;
            int index = shown.Card.Index;
            bool worn = session.IsEquipped(index);
            int slot = session.GearSlotToReplace(index);
            swapLabel.SetText(worn ? $"Wearing {GameCatalog.Gear[index].Name}"
                : slot >= 0 ? $"Wear instead of {GameCatalog.Gear[session.GearInSlot(slot)].Name}" : "Wear it from Upgrades");
            bool can = !worn && slot >= 0;
            if (swap.interactable != can)
                swap.interactable = can;
        }

        void Swap()
        {
            if (shown != null && session.SwapGear(shown.Card.Index))
                Sound.Play(Sfx.Upgrade);
            Refresh();
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
            openedAt = Time.unscaledTime;
            swapOffered = shown != null && shown.Card.IsNew && session.GearSlotToReplace(shown.Card.Index) >= 0;
            if (shown != null)
                Sound.Play(Sfx.Chest);
            Refresh();
        }

        void WatchDouble()
        {
            ChestReward target = shown;
            ads.Show("chest_open", rewarded =>
            {
                if (rewarded)
                    session.DoubleChest(target);
                ads.Load();
                Refresh();
            });
        }
    }
}
