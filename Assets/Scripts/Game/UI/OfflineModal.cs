using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// "Welcome back" modal (design doc 3.3). Claim is always enabled; the x2 button follows
    /// <see cref="RewardedDoubleButton"/> so a late ad fill turns it Ready on its own.
    /// </summary>
    public sealed class OfflineModal
    {
        readonly GameSession session;
        readonly IRewardedAds ads;
        readonly RectTransform root;
        readonly Text body;
        readonly Text amount;
        readonly Text capLine;
        readonly Button upgradeAmos;
        readonly Button claim;
        readonly Button doubleButton;
        readonly Text doubleLabel;
        readonly Button okButton;

        RewardedDoubleButton machine;
        OfflineResult result;
        bool hasResult;
        bool hidden;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>A result is waiting to be claimed (the modal may be hidden behind Upgrades).</summary>
        public bool HasUnclaimed => hasResult;

        public event Action UpgradeAmosRequested;

        /// <summary>Claimed without watching an ad; arms the late double chip.</summary>
        public event Action<BigNumber> ClaimedWithoutAd;

        public event Action Claimed;

        public OfflineModal(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            ads.Loaded += () => machine?.NotifyAdLoaded();

            root = Ui.Image("OfflineModal", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(940, 1000));

            Text title = Ui.Title("Title", card, "WELCOME BACK", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -130), new Vector2(0, -30));

            body = Ui.Label("Body", card, "", 38, TextAnchor.MiddleCenter, Palette.TextMuted);
            body.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -300), new Vector2(-40, -140));

            amount = Ui.Label("Amount", card, "", 92, TextAnchor.MiddleCenter, Palette.GoldText, FontStyle.Bold);
            amount.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -430), new Vector2(0, -300));

            capLine = Ui.Label("CapLine", card, "", 34, TextAnchor.MiddleCenter, Palette.Text);
            capLine.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -560), new Vector2(-40, -440));

            upgradeAmos = Ui.Button("UpgradeAmos", card, "Upgrade Amos", Palette.Amos, () => UpgradeAmosRequested?.Invoke(), out _, 36);
            upgradeAmos.AsRect().Box(new Vector2(0.5f, 1), new Vector2(460, Ui.TapHeight), new Vector2(0, -580));

            claim = Ui.Button("Claim", card, "Claim", Palette.ButtonAlt, Claim, out _, 44);
            claim.AsRect().Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 50), new Vector2(-15, 210));

            doubleButton = Ui.Button("Double", card, "", Palette.Ad, PressDouble, out doubleLabel, 36);
            doubleButton.AsRect().Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(15, 50), new Vector2(-40, 210));

            okButton = Ui.Button("Ok", card, "OK", Palette.ButtonAlt, Close, out _, 44);
            okButton.AsRect().Box(new Vector2(0.5f, 0), new Vector2(420, 160), new Vector2(0, 50));

            Close();
        }

        /// <summary>Trusted time has not arrived yet: show a preview, allow no payout.</summary>
        public void ShowChecking(OfflineResult preview)
        {
            hasResult = false;
            machine = null;
            root.SetActive(true);
            body.SetText($"You were away {TextFormat.Duration(preview.ElapsedSeconds)}.\nChecking the time...");
            amount.SetText("");
            capLine.SetText("");
            upgradeAmos.SetActive(false);
            claim.SetActive(false);
            doubleButton.SetActive(false);
            okButton.SetActive(false);
        }

        /// <summary>Trusted time never came: the haul waits for a connection.</summary>
        public void ShowWaitingForConnection()
        {
            if (!IsOpen || hasResult)
                return;
            body.SetText("Your haul is waiting.\nConnect to the internet to collect it.");
            okButton.SetActive(true);
        }

        public void Show(OfflineResult payable)
        {
            result = payable;
            hasResult = true;
            hidden = false;
            root.SetActive(true);

            body.SetText($"You were away {TextFormat.Duration(payable.ElapsedSeconds)}.\nAmos kept panning:");
            amount.SetText(NumberFormat.Dollars(payable.Amount));
            bool capped = payable.CapReached;
            string cap = NumberFormat.Hours(session.OfflineCapSeconds / 3600);
            capLine.SetText(capped ? $"Amos stopped at {cap}. Level him up to keep digging longer." : "");
            upgradeAmos.SetActive(capped);
            claim.SetActive(true);
            doubleButton.SetActive(true);
            okButton.SetActive(false);

            machine = new RewardedDoubleButton(session.Economy.Config);
            machine.Open(ads.IsLoaded, ads.IsOnline);
            if (!ads.IsLoaded)
                ads.Load();
        }

        /// <summary>Temporarily hides the modal while Upgrades is open (one modal layer at a time).</summary>
        public void Hide()
        {
            hidden = true;
            root.SetActive(false);
        }

        public void Unhide()
        {
            if (hidden && hasResult)
                root.SetActive(true);
            hidden = false;
        }

        public void Tick(float deltaTime)
        {
            if (!IsOpen || machine == null)
                return;
            machine.Tick(deltaTime);
            machine.NotifyConnection(ads.IsOnline, ads.IsLoaded);
            RefreshDouble();
        }

        void RefreshDouble()
        {
            bool interactable = true;
            switch (machine.State)
            {
                case RewardedButtonState.Ready:
                    doubleLabel.SetText("x2  Watch ad");
                    break;
                case RewardedButtonState.Loading:
                    doubleLabel.SetText($"Loading ad... {Mathf.CeilToInt((float)machine.LoadingSecondsLeft)}");
                    interactable = false;
                    break;
                case RewardedButtonState.Unavailable:
                    doubleLabel.SetText("No ad right now\nRetry");
                    break;
                case RewardedButtonState.NoConnection:
                    doubleLabel.SetText("Ads need a connection");
                    interactable = false;
                    break;
                default:
                    doubleLabel.SetText("...");
                    interactable = false;
                    break;
            }
            if (doubleButton.interactable != interactable)
                doubleButton.interactable = interactable;
            if (claim.interactable == (machine.State == RewardedButtonState.Showing))
                claim.interactable = machine.State != RewardedButtonState.Showing;
        }

        void PressDouble()
        {
            if (machine.State == RewardedButtonState.Unavailable)
            {
                machine.Retry();
                ads.Load();
                return;
            }
            if (!machine.Press())
                return;
            ads.Show("offline_double", rewarded =>
            {
                machine.NotifyShowFinished(rewarded);
                if (rewarded && session.ClaimOffline(result, 2))
                    Finish();
            });
        }

        void Claim()
        {
            if (!hasResult || !session.ClaimOffline(result, 1))
                return;
            bool adWasReady = machine != null && machine.State == RewardedButtonState.Ready;
            BigNumber claimed = result.Amount;
            Finish();
            if (!adWasReady)
                ClaimedWithoutAd?.Invoke(claimed);
        }

        void Finish()
        {
            hasResult = false;
            Close();
            Claimed?.Invoke();
        }

        void Close()
        {
            hidden = false;
            machine = null;
            root.SetActive(false);
        }
    }
}
