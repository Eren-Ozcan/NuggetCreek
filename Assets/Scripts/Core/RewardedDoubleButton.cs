using System;

namespace NuggetCreek.Core
{
    public enum RewardedButtonState
    {
        Ready,
        Loading,
        Unavailable,
        NoConnection,
        Showing,
        Completed,
    }

    /// <summary>
    /// State machine for the offline "x2 - watch ad" button (design doc 3.3.1, binding).
    /// The button subscribes to ad load events, so a late fill flips it to Ready without the
    /// player pressing again, and Loading always ends within the timeout. Claim is never
    /// gated by this machine.
    /// </summary>
    public sealed class RewardedDoubleButton
    {
        readonly EconomyConfig config;
        double loadingRemaining;

        public RewardedButtonState State { get; private set; } = RewardedButtonState.Loading;

        /// <summary>Raised on every transition as (from, to); feeds the ad_rv_state event.</summary>
        public event Action<RewardedButtonState, RewardedButtonState> StateChanged;

        public RewardedDoubleButton(EconomyConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public double LoadingSecondsLeft => State == RewardedButtonState.Loading ? loadingRemaining : 0;

        /// <summary>Call when the offline modal opens.</summary>
        public void Open(bool adLoaded, bool online)
        {
            if (!online)
                SetState(RewardedButtonState.NoConnection);
            else if (adLoaded)
                SetState(RewardedButtonState.Ready);
            else
                StartLoading();
        }

        public void Tick(double deltaSeconds)
        {
            if (State != RewardedButtonState.Loading)
                return;
            loadingRemaining -= deltaSeconds;
            if (loadingRemaining <= 0)
                SetState(RewardedButtonState.Unavailable);
        }

        public void NotifyAdLoaded()
        {
            if (State == RewardedButtonState.Loading || State == RewardedButtonState.Unavailable)
                SetState(RewardedButtonState.Ready);
        }

        /// <summary>A loaded ad expired or was consumed elsewhere.</summary>
        public void NotifyAdLost()
        {
            if (State == RewardedButtonState.Ready)
                StartLoading();
        }

        public void NotifyConnection(bool online, bool adLoaded)
        {
            if (State == RewardedButtonState.Showing || State == RewardedButtonState.Completed)
                return;
            if (!online)
                SetState(RewardedButtonState.NoConnection);
            else if (State == RewardedButtonState.NoConnection)
                Open(adLoaded, true);
        }

        public void Retry()
        {
            if (State == RewardedButtonState.Unavailable)
                StartLoading();
        }

        /// <summary>Returns true when the caller should show the ad now.</summary>
        public bool Press()
        {
            if (State != RewardedButtonState.Ready)
                return false;
            SetState(RewardedButtonState.Showing);
            return true;
        }

        /// <summary>
        /// Result of the shown ad. A failed or skipped ad costs the player nothing: the
        /// modal stays open and the button drops to Unavailable so they can retry or claim.
        /// </summary>
        public void NotifyShowFinished(bool rewarded)
        {
            if (State != RewardedButtonState.Showing)
                return;
            SetState(rewarded ? RewardedButtonState.Completed : RewardedButtonState.Unavailable);
        }

        void StartLoading()
        {
            loadingRemaining = config.RewardedLoadTimeoutSeconds;
            SetState(RewardedButtonState.Loading);
        }

        void SetState(RewardedButtonState next)
        {
            RewardedButtonState previous = State;
            State = next;
            if (previous != next)
                StateChanged?.Invoke(previous, next);
        }
    }

    /// <summary>
    /// "Double your last haul?" chip (design doc 3.3.1 rule 6): if the player claimed
    /// without an ad and a rewarded ad fills within the window, offer the double once.
    /// </summary>
    public sealed class LateDoubleOffer
    {
        readonly EconomyConfig config;
        double armedAt = double.NaN;
        double shownAt = double.NaN;

        public BigNumber Amount { get; private set; }

        public LateDoubleOffer(EconomyConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void ArmAfterClaim(double nowSeconds, BigNumber claimedAmount)
        {
            armedAt = nowSeconds;
            shownAt = double.NaN;
            Amount = claimedAmount;
        }

        /// <summary>Returns true when the chip should appear now.</summary>
        public bool NotifyAdReady(double nowSeconds)
        {
            if (double.IsNaN(armedAt) || !double.IsNaN(shownAt))
                return false;
            if (nowSeconds - armedAt > config.LateDoubleWindowSeconds)
            {
                Clear();
                return false;
            }
            shownAt = nowSeconds;
            return true;
        }

        public bool IsVisible(double nowSeconds) =>
            !double.IsNaN(shownAt) && nowSeconds - shownAt < config.LateDoubleWindowSeconds;

        /// <summary>Accepted, dismissed or expired: the chip never comes back for this claim.</summary>
        public void Clear()
        {
            armedAt = double.NaN;
            shownAt = double.NaN;
            Amount = BigNumber.Zero;
        }
    }
}
