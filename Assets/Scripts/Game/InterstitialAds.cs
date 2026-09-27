using System;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>What the game needs from an interstitial ad SDK (<see cref="AdMobAds"/> on a device).</summary>
    public interface IInterstitialAds
    {
        bool IsLoaded { get; }
        bool IsShowing { get; }

        /// <summary>Raised when the ad takes the screen; stamps the shared cooldown.</summary>
        event Action Opened;

        void Load();

        /// <summary>Shows the loaded ad; the callback runs when it closes or fails to open.</summary>
        /// <param name="trigger">The natural break it follows, e.g. "region_unlock".</param>
        void Show(string trigger, Action onClosed);

        void Tick(float deltaSeconds);
    }

    /// <summary>Greybox stand-in: fills after a short delay and "plays" for a moment.</summary>
    public sealed class FakeInterstitialAds : IInterstitialAds
    {
        const float FillSeconds = 3f;
        const float ShowSeconds = 1.5f;

        float fillIn = -1;
        float showLeft = -1;
        Action pendingClose;

        public bool IsLoaded { get; private set; }
        public bool IsShowing => showLeft >= 0;

        public event Action Opened;

        public void Load()
        {
            if (IsLoaded || fillIn >= 0 || Application.internetReachability == NetworkReachability.NotReachable)
                return;
            fillIn = FillSeconds;
        }

        public void Show(string trigger, Action onClosed)
        {
            if (!IsLoaded)
            {
                onClosed?.Invoke();
                return;
            }
            IsLoaded = false;
            pendingClose = onClosed;
            showLeft = ShowSeconds;
            Debug.Log($"[FakeInterstitial] {trigger}");
            Opened?.Invoke();
        }

        public void Tick(float deltaSeconds)
        {
            if (fillIn >= 0)
            {
                fillIn -= deltaSeconds;
                if (fillIn < 0)
                    IsLoaded = true;
            }
            if (showLeft >= 0)
            {
                showLeft -= deltaSeconds;
                if (showLeft < 0)
                {
                    Action callback = pendingClose;
                    pendingClose = null;
                    callback?.Invoke();
                }
            }
        }
    }
}
