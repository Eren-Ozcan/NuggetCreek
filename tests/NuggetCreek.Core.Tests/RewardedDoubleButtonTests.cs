using System.Collections.Generic;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class RewardedDoubleButtonTests
    {
        EconomyConfig config;
        RewardedDoubleButton button;
        List<string> transitions;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            button = new RewardedDoubleButton(config);
            transitions = new List<string>();
            button.StateChanged += (from, to) => transitions.Add(from + ">" + to);
        }

        [Test]
        public void LoadedAd_OpensReady()
        {
            button.Open(adLoaded: true, online: true);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Ready));
        }

        [Test]
        public void LateFill_FlipsToReadyWithoutPress()
        {
            button.Open(adLoaded: false, online: true);
            button.Tick(4);
            button.NotifyAdLoaded();
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Ready));
        }

        [Test]
        public void Loading_NeverOutlivesTimeout()
        {
            button.Open(adLoaded: false, online: true);
            button.Tick(9.9);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Loading));
            button.Tick(0.2);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Unavailable));
        }

        [Test]
        public void Retry_StartsFreshTimeout()
        {
            button.Open(adLoaded: false, online: true);
            button.Tick(10);
            button.Retry();
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Loading));
            Assert.That(button.LoadingSecondsLeft, Is.EqualTo(config.RewardedLoadTimeoutSeconds));
        }

        [Test]
        public void Offline_ShowsNoConnection_ThenRecovers()
        {
            button.Open(adLoaded: false, online: false);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.NoConnection));
            button.NotifyConnection(online: true, adLoaded: false);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Loading));
        }

        [Test]
        public void FailedAd_DropsToUnavailable_NotCompleted()
        {
            button.Open(adLoaded: true, online: true);
            Assert.That(button.Press(), Is.True);
            button.NotifyShowFinished(rewarded: false);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Unavailable));
        }

        [Test]
        public void RewardedAd_Completes_AndCannotBePressedAgain()
        {
            button.Open(adLoaded: true, online: true);
            button.Press();
            button.NotifyShowFinished(rewarded: true);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Completed));
            Assert.That(button.Press(), Is.False);
            button.NotifyConnection(online: false, adLoaded: false);
            Assert.That(button.State, Is.EqualTo(RewardedButtonState.Completed));
        }

        [Test]
        public void Press_OnlyWorksWhenReady()
        {
            button.Open(adLoaded: false, online: true);
            Assert.That(button.Press(), Is.False);
        }

        [Test]
        public void Transitions_AreReported()
        {
            button.Open(adLoaded: false, online: true);
            button.NotifyAdLoaded();
            button.Press();
            Assert.That(transitions, Is.EqualTo(new[] { "Loading>Ready", "Ready>Showing" }));
        }

        [Test]
        public void LateDouble_AppearsOnceInsideWindow()
        {
            var chip = new LateDoubleOffer(config);
            chip.ArmAfterClaim(100, 5000);
            Assert.That(chip.NotifyAdReady(200), Is.True);
            Assert.That(chip.NotifyAdReady(210), Is.False, "only once per claim");
            Assert.That(chip.IsVisible(200 + 299), Is.True);
            Assert.That(chip.IsVisible(200 + 300), Is.False);
            Assert.That(chip.Amount.ToDouble(), Is.EqualTo(5000));
        }

        [Test]
        public void LateDouble_ExpiresIfAdFillsTooLate()
        {
            var chip = new LateDoubleOffer(config);
            chip.ArmAfterClaim(0, 5000);
            Assert.That(chip.NotifyAdReady(301), Is.False);
            Assert.That(chip.IsVisible(301), Is.False);
        }
    }
}
