using System;

namespace NuggetCreek.Core
{
    public enum OfflineStatus
    {
        /// <summary>Elapsed time came from a trusted source; the amount can be paid out.</summary>
        Credited,

        /// <summary>No trusted time yet. Amount is a preview from the device clock and must be
        /// recomputed once trusted time arrives.</summary>
        PendingTrustedTime,

        /// <summary>Device clock jumped forward while the monotonic clock did not.</summary>
        RejectedClockTamper,

        /// <summary>Now is earlier than last seen. No reward and no penalty.</summary>
        RejectedClockBackwards,
    }

    /// <summary>Clock readings taken when the app last went to background and now.</summary>
    public struct OfflineClockInput
    {
        /// <summary>Best-known UTC when the player left, in unix seconds (saved as last_seen_utc).</summary>
        public double LastSeenUtc;

        /// <summary>UTC from a trusted source (time server), or null when offline.</summary>
        public double? TrustedNowUtc;

        /// <summary>Device wall clock at leave and now, unix seconds.</summary>
        public double LastDeviceUtc;
        public double DeviceNowUtc;

        /// <summary>Monotonic seconds since boot (Android elapsedRealtime) at leave and now.</summary>
        public double LastMonotonicSeconds;
        public double MonotonicNowSeconds;

        /// <summary>False when the device rebooted in between; monotonic readings are then incomparable.</summary>
        public bool SameBoot;
    }

    public readonly struct OfflineResult
    {
        public readonly OfflineStatus Status;
        public readonly double ElapsedSeconds;
        public readonly double CreditedSeconds;
        public readonly BigNumber Amount;
        public readonly bool CapReached;

        public OfflineResult(OfflineStatus status, double elapsedSeconds, double creditedSeconds, BigNumber amount, bool capReached)
        {
            Status = status;
            ElapsedSeconds = elapsedSeconds;
            CreditedSeconds = creditedSeconds;
            Amount = amount;
            CapReached = capReached;
        }

        public bool IsPayable => Status == OfflineStatus.Credited && !Amount.IsZero;
    }

    /// <summary>
    /// Offline earnings with the anti clock-cheat rules of design doc 13.1:
    /// the device clock is never trusted for payouts, forward jumps without matching
    /// monotonic time are rejected, and backwards time pays nothing.
    /// </summary>
    public sealed class OfflineEarnings
    {
        readonly EconomyConfig config;

        public OfflineEarnings(EconomyConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public OfflineResult Evaluate(OfflineClockInput clock, double capSeconds, BigNumber idleRatePerSecond)
        {
            if (clock.SameBoot && IsTampered(clock))
                return new OfflineResult(OfflineStatus.RejectedClockTamper, 0, 0, BigNumber.Zero, false);

            bool trusted = clock.TrustedNowUtc.HasValue;
            double now = trusted ? clock.TrustedNowUtc.Value : clock.DeviceNowUtc;
            double elapsed = now - clock.LastSeenUtc;
            if (elapsed < 0)
                return new OfflineResult(OfflineStatus.RejectedClockBackwards, 0, 0, BigNumber.Zero, false);

            double cap = Math.Max(0, capSeconds);
            double credited = Math.Min(elapsed, cap);
            // Past the cap the crew keeps a slow trickle going (design doc 3.3); nothing without a crew.
            double pastCap = cap > 0 ? Math.Max(0, elapsed - cap) * config.OfflinePastCapRate : 0;
            BigNumber amount = idleRatePerSecond * (credited + pastCap);
            var status = trusted ? OfflineStatus.Credited : OfflineStatus.PendingTrustedTime;
            return new OfflineResult(status, elapsed, credited, amount, cap > 0 && elapsed >= cap);
        }

        bool IsTampered(OfflineClockInput clock)
        {
            double wallDelta = clock.DeviceNowUtc - clock.LastDeviceUtc;
            double monotonicDelta = clock.MonotonicNowSeconds - clock.LastMonotonicSeconds;
            return wallDelta - monotonicDelta > config.ClockTamperToleranceSeconds;
        }

        /// <summary>
        /// Gem price of the second double, offered only after a successful ad double:
        /// 5 Gems per credited hour, rounded up, at least 1. Null when the absence is too
        /// short to show it.
        /// </summary>
        public int? GemDoublePrice(OfflineResult result)
        {
            if (!result.IsPayable || result.ElapsedSeconds < config.OfflineGemDoubleMinAwaySeconds)
                return null;
            double hours = result.CreditedSeconds / 3600;
            // Guard against 2.0000000001 hours turning into an extra step.
            int price = (int)Math.Ceiling(hours * config.OfflineGemDoublePerHour - 1e-9);
            return Math.Max(1, price);
        }
    }
}
