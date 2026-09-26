using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class OfflineEarningsTests
    {
        const double Leave = 1_700_000_000;
        const double Hour = 3600;

        OfflineEarnings offline;

        [SetUp]
        public void SetUp() => offline = new OfflineEarnings(new EconomyConfig());

        static OfflineClockInput Away(double seconds, double? trustedOffset = 0, double deviceJump = 0, bool sameBoot = true)
        {
            return new OfflineClockInput
            {
                LastSeenUtc = Leave,
                TrustedNowUtc = trustedOffset.HasValue ? Leave + seconds + trustedOffset.Value : (double?)null,
                LastDeviceUtc = Leave,
                DeviceNowUtc = Leave + seconds + deviceJump,
                LastMonotonicSeconds = 5000,
                MonotonicNowSeconds = 5000 + seconds,
                SameBoot = sameBoot,
            };
        }

        [Test]
        public void HonestAbsence_UnderCap_PaysFullTime()
        {
            OfflineResult r = offline.Evaluate(Away(1800), Hour, 10);
            Assert.That(r.Status, Is.EqualTo(OfflineStatus.Credited));
            Assert.That(r.Amount.ToDouble(), Is.EqualTo(18000));
            Assert.That(r.CapReached, Is.False);
            Assert.That(r.IsPayable);
        }

        [Test]
        public void LongAbsence_IsClippedToCap()
        {
            OfflineResult r = offline.Evaluate(Away(10 * Hour), 3 * Hour, 2);
            Assert.That(r.CreditedSeconds, Is.EqualTo(3 * Hour));
            // Nothing is paid past the cap by default (design doc 3.3).
            Assert.That(r.Amount.ToDouble(), Is.EqualTo(2 * 3 * Hour).Within(1e-6));
            Assert.That(r.CapReached);
        }

        [Test]
        public void PastCapRate_PaysATrickleWhenTuned()
        {
            var tuned = new OfflineEarnings(new EconomyConfig { OfflinePastCapRate = 0.2 });
            OfflineResult r = tuned.Evaluate(Away(10 * Hour), 3 * Hour, 2);
            // 3 h at the full rate, the other 7 h at 20%.
            Assert.That(r.Amount.ToDouble(), Is.EqualTo(2 * (3 * Hour + 0.2 * 7 * Hour)).Within(1e-6));
        }

        [Test]
        public void NoTrickleWithoutACrew()
        {
            OfflineResult r = offline.Evaluate(Away(10 * Hour), 0, 2);
            Assert.That(r.Amount.IsZero);
        }

        [Test]
        public void DeviceClockPushedForward_IsRejected()
        {
            // Real absence 10 minutes, device clock moved 5 hours ahead.
            OfflineResult r = offline.Evaluate(Away(600, trustedOffset: null, deviceJump: 5 * Hour), Hour, 10);
            Assert.That(r.Status, Is.EqualTo(OfflineStatus.RejectedClockTamper));
            Assert.That(r.Amount.IsZero);
        }

        [Test]
        public void SmallDrift_WithinTolerance_IsAccepted()
        {
            OfflineResult r = offline.Evaluate(Away(600, deviceJump: 90), Hour, 1);
            Assert.That(r.Status, Is.EqualTo(OfflineStatus.Credited));
        }

        [Test]
        public void AfterReboot_MonotonicIsIgnored_AndTrustedTimePays()
        {
            OfflineClockInput clock = Away(2 * Hour, sameBoot: false);
            clock.MonotonicNowSeconds = 40; // reset by reboot
            OfflineResult r = offline.Evaluate(clock, 3 * Hour, 1);
            Assert.That(r.Status, Is.EqualTo(OfflineStatus.Credited));
            Assert.That(r.CreditedSeconds, Is.EqualTo(2 * Hour));
        }

        [Test]
        public void NoTrustedTime_ComputesButDoesNotPay()
        {
            OfflineResult r = offline.Evaluate(Away(1800, trustedOffset: null), Hour, 10);
            Assert.That(r.Status, Is.EqualTo(OfflineStatus.PendingTrustedTime));
            Assert.That(r.Amount.ToDouble(), Is.EqualTo(18000));
            Assert.That(r.IsPayable, Is.False);
        }

        [Test]
        public void TrustedTimeBeforeLastSeen_PaysNothing()
        {
            OfflineResult r = offline.Evaluate(Away(600, trustedOffset: -2 * Hour, sameBoot: false), Hour, 10);
            Assert.That(r.Status, Is.EqualTo(OfflineStatus.RejectedClockBackwards));
            Assert.That(r.Amount.IsZero);
        }

        [Test]
        public void WithoutAmos_NothingAccrues()
        {
            OfflineResult r = offline.Evaluate(Away(5 * Hour), 0, 10);
            Assert.That(r.Amount.IsZero);
            Assert.That(r.IsPayable, Is.False);
            Assert.That(r.CapReached, Is.False);
        }

        // Design doc 3.3.2: 5 Gems per credited hour, rounded up; 1h cap = 5, 12h cap = 60.
        [TestCase(1800.0, 12 * Hour, 3)]
        [TestCase(Hour, Hour, 5)]
        [TestCase(10 * Hour, Hour, 5)]
        [TestCase(12 * Hour, 12 * Hour, 60)]
        [TestCase(2 * Hour + 1, 12 * Hour, 11)]
        public void GemDoublePrice(double awaySeconds, double capSeconds, int expected)
        {
            OfflineResult r = offline.Evaluate(Away(awaySeconds), capSeconds, 1);
            Assert.That(offline.GemDoublePrice(r), Is.EqualTo(expected));
        }

        [Test]
        public void GemDouble_HiddenForShortOrUnpaidAbsences()
        {
            Assert.That(offline.GemDoublePrice(offline.Evaluate(Away(1799), Hour, 1)), Is.Null);
            Assert.That(offline.GemDoublePrice(offline.Evaluate(Away(Hour, trustedOffset: null), Hour, 1)), Is.Null);
            Assert.That(offline.GemDoublePrice(offline.Evaluate(Away(Hour), 0, 1)), Is.Null);
        }
    }
}
