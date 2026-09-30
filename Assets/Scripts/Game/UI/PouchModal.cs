using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Gem Pouch card (design doc 3.1.4): the Gem range is printed before the ad (brand rule),
    /// a rewarded ad opens it, Throw away drops it. Closing keeps it in the tray, so a pouch
    /// caught with no ad available is not lost.
    /// </summary>
    public sealed class PouchModal
    {
        readonly GameSession session;
        readonly IRewardedAds ads;
        readonly RectTransform root;
        readonly Text status;
        readonly Button openButton;
        readonly Text openLabel;
        readonly Button throwButton;
        readonly Button collect;
        int shownGems;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised when a pouch has been opened or thrown away.</summary>
        public event Action Changed;

        public PouchModal(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("PouchModal", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(900, 1000));
            Ui.CloseButton(card, Close);

            // Kept clear of the close button in the corner; shrinks rather than running under it.
            Text title = Ui.Title("Title", card, "GEM POUCH", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(110, -130), new Vector2(-110, -30));
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 32;
            title.resizeTextMaxSize = 56;
            EconomyConfig config = session.Economy.Config;
            Text contents = Ui.Label("Contents", card, $"Inside: {config.GemPouchMinGems}-{config.GemPouchMaxGems} Gems.\nIt opens with a short ad.",
                32, TextAnchor.MiddleCenter, Palette.TextMuted);
            contents.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -240), new Vector2(-40, -130));

            RectTransform picture = FloaterPicture.Build("PouchPicture", card, FloaterKind.Pouch, 280);
            picture.Box(new Vector2(0.5f, 1), new Vector2(280, 280), new Vector2(0, -260));

            status = Ui.Label("PouchStatus", card, "", 56, TextAnchor.MiddleCenter, Palette.GemText, FontStyle.Bold);
            status.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -660), new Vector2(-40, -560));

            openButton = Ui.Button("PouchOpen", card, "", Palette.Ad, WatchAd, out openLabel, 40);
            openButton.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 170), new Vector2(-40, 290));
            throwButton = Ui.Button("PouchThrow", card, "Throw away", Palette.ButtonAlt, Throw, out _, 36);
            throwButton.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 40), new Vector2(-40, 150));
            collect = Ui.Button("PouchCollect", card, "Collect", Palette.GemButton, Close, out _, 40);
            collect.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(40, 40), new Vector2(-40, 150));

            root.SetActive(false);
        }

        public void Open()
        {
            if (session.Progress.PouchesWaiting <= 0)
                return;
            shownGems = 0;
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            bool opened = shownGems > 0;
            status.SetText(opened ? "+" + Effects.Gems(shownGems) : "");
            openButton.SetActive(!opened);
            throwButton.SetActive(!opened);
            collect.SetActive(opened);
            if (opened)
                return;
            openLabel.SetText(ads.IsLoaded ? "Open  (watch ad)" : "No ad right now");
            if (openButton.interactable != ads.IsLoaded)
                openButton.interactable = ads.IsLoaded;
        }

        void WatchAd()
        {
            ads.Show("gem_pouch", rewarded =>
            {
                if (rewarded)
                {
                    shownGems = session.OpenPouch();
                    if (shownGems > 0)
                    {
                        Sound.Play(Sfx.Chest);
                        Changed?.Invoke();
                    }
                }
                ads.Load();
                Refresh();
            });
        }

        void Throw()
        {
            if (session.DiscardPouch())
                Changed?.Invoke();
            Close();
        }
    }
}
