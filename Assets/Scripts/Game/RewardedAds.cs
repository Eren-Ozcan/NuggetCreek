using System;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>What the game needs from a rewarded ad SDK (<see cref="AdMobAds"/> on a device).</summary>
    public interface IRewardedAds
    {
        bool IsLoaded { get; }
        bool IsOnline { get; }

        /// <summary>True from Show until the ad closes; the app pauses underneath a real ad.</summary>
        bool IsShowing { get; }

        /// <summary>Raised when the ad takes the screen; stamps the shared cooldown.</summary>
        event Action Opened;

        /// <summary>Raised when an ad finishes loading, including late fills.</summary>
        event Action Loaded;

        void Load();

        /// <summary>Raised with the placement when a reward is earned (the ad_rewarded event).</summary>
        event Action<string> Rewarded;

        /// <summary>Shows the loaded ad; the callback reports whether the reward was earned.</summary>
        /// <param name="placement">Where the ad was offered, e.g. "offline_double" (design doc 13.4.4).</param>
        void Show(string placement, Action<bool> onFinished);

        void Tick(float deltaSeconds);
    }

    /// <summary>
    /// Greybox stand-in: fills after a random delay that sometimes runs past the 10 s
    /// timeout, so every state of the x2 button gets exercised.
    /// </summary>
    public sealed class FakeRewardedAds : IRewardedAds
    {
        const float MinFillSeconds = 0.5f;
        const float MaxFillSeconds = 13f;
        const float ShowSeconds = 1.5f;
        const float RewardChance = 0.9f;

        float fillIn = -1;
        float showLeft = -1;
        Action<bool> pendingShow;
        string pendingPlacement;

        public bool IsLoaded { get; private set; }
        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;
        public bool IsShowing => showLeft >= 0;

        public event Action Opened;
        public event Action Loaded;
        public event Action<string> Rewarded;

        public void Load()
        {
            if (IsLoaded || fillIn >= 0 || !IsOnline)
                return;
            fillIn = UnityEngine.Random.Range(MinFillSeconds, MaxFillSeconds);
        }

        public void Show(string placement, Action<bool> onFinished)
        {
            if (!IsLoaded)
            {
                onFinished?.Invoke(false);
                return;
            }
            IsLoaded = false;
            pendingShow = onFinished;
            pendingPlacement = placement;
            showLeft = ShowSeconds;
            Opened?.Invoke();
        }

        public void Tick(float deltaSeconds)
        {
            if (fillIn >= 0)
            {
                fillIn -= deltaSeconds;
                if (fillIn < 0)
                {
                    IsLoaded = true;
                    Loaded?.Invoke();
                }
            }

            if (showLeft >= 0)
            {
                showLeft -= deltaSeconds;
                if (showLeft < 0)
                {
                    Action<bool> callback = pendingShow;
                    pendingShow = null;
                    bool rewarded = UnityEngine.Random.value < RewardChance;
                    if (rewarded)
                        Rewarded?.Invoke(pendingPlacement);
                    callback?.Invoke(rewarded);
                    Load();
                }
            }
        }
    }
}
