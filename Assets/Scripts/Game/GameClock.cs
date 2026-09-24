using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Clock readings for offline earnings (design doc 13.1). Trusted UTC comes from the
    /// Date header of an HTTPS response and is carried forward on the monotonic clock, so
    /// it survives device clock changes for the rest of the process.
    /// </summary>
    public sealed class GameClock
    {
        const string TimeUrl = "https://www.google.com/generate_204";
        const float RetrySeconds = 15;

        /// <summary>Trusted UTC minus monotonic seconds; null until a time response arrives.</summary>
        double? trustedOffset;

        public bool HasTrustedTime => trustedOffset.HasValue;

        public event Action TrustedTimeArrived;

        public static double DeviceUtc => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        /// <summary>Seconds since device boot; keeps counting through sleep on Android.</summary>
        public static double MonotonicSeconds
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var clock = new AndroidJavaClass("android.os.SystemClock"))
                    return clock.CallStatic<long>("elapsedRealtime") / 1000.0;
#else
                return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
#endif
            }
        }

        /// <summary>Trusted UTC at a given monotonic reading, if known.</summary>
        public double? TrustedUtcAt(double monotonicSeconds) => trustedOffset + monotonicSeconds;

        public double? TrustedNowUtc => TrustedUtcAt(MonotonicSeconds);

        /// <summary>Best UTC for saving last-seen: trusted when known, device otherwise.</summary>
        public double BestNowUtc => TrustedNowUtc ?? DeviceUtc;

        /// <summary>Records when the player leaves.</summary>
        public void StampLeave(PlayerProgress progress)
        {
            double device = DeviceUtc;
            double monotonic = MonotonicSeconds;
            progress.LastSeenUtc = TrustedUtcAt(monotonic) ?? device;
            progress.LastDeviceUtc = device;
            progress.LastMonotonicSeconds = monotonic;
            progress.LastBootUtc = device - monotonic;
        }

        /// <summary>
        /// Clock input for the return, taken at monotonic reading <paramref name="monotonicNow"/>.
        /// A later call with the same reading picks up trusted time that arrived in between.
        /// </summary>
        public OfflineClockInput ReturnInput(PlayerProgress progress, double deviceNow, double monotonicNow)
        {
            double bootNow = deviceNow - monotonicNow;
            return new OfflineClockInput
            {
                LastSeenUtc = progress.LastSeenUtc,
                TrustedNowUtc = TrustedUtcAt(monotonicNow),
                LastDeviceUtc = progress.LastDeviceUtc,
                DeviceNowUtc = deviceNow,
                LastMonotonicSeconds = progress.LastMonotonicSeconds,
                MonotonicNowSeconds = monotonicNow,
                // A forward clock change also moves the boot estimate; that case falls back
                // to trusted time for the payout, so it is still safe.
                SameBoot = Math.Abs(bootNow - progress.LastBootUtc) < 5,
            };
        }

        public IEnumerator FetchTrustedTime()
        {
            while (!trustedOffset.HasValue)
            {
                using (UnityWebRequest request = UnityWebRequest.Head(TimeUrl))
                {
                    request.timeout = 8;
                    double sentAt = MonotonicSeconds;
                    yield return request.SendWebRequest();
                    double receivedAt = MonotonicSeconds;

                    string date = request.result == UnityWebRequest.Result.Success ? request.GetResponseHeader("Date") : null;
                    if (date != null && DateTimeOffset.TryParseExact(date, "r", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out DateTimeOffset serverTime))
                    {
                        // The header has 1 s resolution; the midpoint of the round trip is the best estimate.
                        double midpoint = (sentAt + receivedAt) / 2;
                        trustedOffset = serverTime.ToUnixTimeSeconds() + 0.5 - midpoint;
                        TrustedTimeArrived?.Invoke();
                        yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(RetrySeconds);
            }
        }
    }
}
