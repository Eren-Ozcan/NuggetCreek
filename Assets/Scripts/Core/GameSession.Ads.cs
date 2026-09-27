using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Forced ad pacing (design doc 8.4, studio ad policy): interstitials only at a natural
    /// break the player just finished, one shared and saved cooldown for every full screen ad.
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>
        /// The natural break waiting for the UI to settle ("region_unlock", "tier_up",
        /// "prestige"); null when there is none. The platform layer shows the ad once no
        /// panel is open, so the reward and its celebration always come first.
        /// </summary>
        public string PendingInterstitial { get; private set; }

        void MarkNaturalBreak(string trigger) => PendingInterstitial = trigger;

        /// <summary>Drops a pending break, e.g. when the app goes to background.</summary>
        public void ClearPendingInterstitial() => PendingInterstitial = null;

        /// <summary>
        /// Why a forced ad may not play now ("removed", "too_early", "cooldown"), or null
        /// when it may. A stamp in the future means the device clock went back; it is
        /// ignored so a clock change never locks ads out for good.
        /// </summary>
        public string InterstitialBlock(double nowUtc)
        {
            if (Config.InterstitialEnabled == 0 || Progress.AdsRemoved)
                return "removed";
            if (Progress.BestRegionsUnlocked <= Config.InterstitialUnlockRegion
                || Progress.PlaySeconds < Config.InterstitialMinPlaySeconds)
                return "too_early";
            double since = nowUtc - Progress.LastFullScreenAdUtc;
            if (since >= 0 && since < Config.InterstitialCooldownSeconds)
                return "cooldown";
            return null;
        }

        /// <summary>
        /// Consumes the pending break and decides whether its interstitial plays. A skip
        /// sends ad_interstitial_skip with the reason (13.4.3).
        /// </summary>
        public bool TakeInterstitial(double nowUtc, bool adReady)
        {
            if (PendingInterstitial == null)
                return false;
            string trigger = PendingInterstitial;
            PendingInterstitial = null;
            string reason = InterstitialBlock(nowUtc) ?? (adReady ? null : "not_ready");
            if (reason == null)
                return true;
            Emit("ad_interstitial_skip", ("reason", reason), ("trigger", trigger));
            return false;
        }

        /// <summary>
        /// Stamps the shared cooldown. Every full screen ad calls this, rewarded included:
        /// a rewarded ad is never blocked, but no forced ad follows right after it.
        /// </summary>
        public void MarkFullScreenAdShown(double nowUtc) => Progress.LastFullScreenAdUtc = nowUtc;
    }
}
