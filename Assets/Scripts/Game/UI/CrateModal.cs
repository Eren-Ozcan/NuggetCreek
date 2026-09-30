using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Driftwood Crate card (design doc 3.1.4): the Dollars inside, Claim, or a rewarded ad that
    /// pays them twice. No forced ad here; those only come at natural breaks (8.4).
    /// </summary>
    public sealed class CrateModal
    {
        readonly GameSession session;
        readonly IRewardedAds ads;
        readonly RectTransform root;
        readonly Text amount;
        readonly Button doubleButton;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised once a crate has been paid out.</summary>
        public event Action Claimed;

        public CrateModal(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("CrateModal", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(900, 980));
            Ui.CloseButton(card, Close);

            // Kept clear of the close button in the corner; shrinks rather than running under it.
            Text title = Ui.Title("Title", card, "DRIFTWOOD CRATE", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(110, -130), new Vector2(-110, -30));
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 32;
            title.resizeTextMaxSize = 56;
            Text contents = Ui.Label("Contents", card, "Washed down the creek. Dollars inside.", 32, TextAnchor.MiddleCenter, Palette.TextMuted);
            contents.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -200), new Vector2(-40, -130));

            RectTransform picture = FloaterPicture.Build("CratePicture", card, FloaterKind.Crate, 300);
            picture.Box(new Vector2(0.5f, 1), new Vector2(300, 300), new Vector2(0, -220));

            amount = Ui.Label("CrateAmount", card, "", 60, TextAnchor.MiddleCenter, Palette.GoldText, FontStyle.Bold);
            amount.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -640), new Vector2(-40, -540));

            Button claim = Ui.Button("CrateClaim", card, "Claim", Palette.Button, Claim, out _, 44);
            claim.AsRect().Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-10, 180));
            doubleButton = Ui.Button("CrateDouble", card, "x2  (watch ad)", Palette.Ad, WatchDouble, out _, 40);
            doubleButton.AsRect().Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(10, 40), new Vector2(-40, 180));

            root.SetActive(false);
        }

        public void Open()
        {
            if (session.Progress.CratesWaiting <= 0)
                return;
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            amount.SetText("+" + NumberFormat.Dollars(session.CrateDollars));
            if (doubleButton.interactable != ads.IsLoaded)
                doubleButton.interactable = ads.IsLoaded;
        }

        void Claim()
        {
            if (session.ClaimCrate() != null)
            {
                Sound.Play(Sfx.Chest);
                Claimed?.Invoke();
            }
            Close();
        }

        /// <summary>Pays the crate at once, then the ad pays it again; a skipped ad keeps the first payment.</summary>
        void WatchDouble()
        {
            CrateReward reward = session.ClaimCrate();
            Close();
            if (reward == null)
                return;
            Sound.Play(Sfx.Chest);
            ads.Show("crate_double", rewarded =>
            {
                if (rewarded)
                    session.DoubleCrate(reward);
                ads.Load();
                Claimed?.Invoke();
            });
        }
    }
}
