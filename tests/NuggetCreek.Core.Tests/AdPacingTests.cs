using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class AdPacingTests
    {
        const double Now = 2_000_000_000;

        GameSession session;
        RecordedGameEvents events;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new Economy(new EconomyConfig()), new PlayerProgress(), new System.Random(7));
            events = new RecordedGameEvents();
            session.Events = events;
        }

        void PastEarlyGame()
        {
            session.Progress.BestRegionsUnlocked = 4;
            session.Progress.PlaySeconds = 21 * 60;
        }

        void OpenEveryCreek()
        {
            session.Progress.RegionsUnlocked = session.Economy.Config.RegionCount;
            session.Earn(1e30);
        }

        [Test]
        public void CreekUnlockTierAndRebirthMarkANaturalBreak()
        {
            session.Earn(1e12);
            Assert.That(session.UnlockNextRegion(), Is.True);
            Assert.That(session.PendingInterstitial, Is.EqualTo("region_unlock"));

            session.ClearPendingInterstitial();
            OpenEveryCreek();
            Assert.That(session.BuyNextTier(), Is.True);
            Assert.That(session.PendingInterstitial, Is.EqualTo("tier_up"));

            session.ClearPendingInterstitial();
            session.Progress.TotalEarned = 1e30;
            Assert.That(session.Rebirth(), Is.True);
            Assert.That(session.PendingInterstitial, Is.EqualTo("prestige"));
        }

        [Test]
        public void NothingPlaysWithoutABreak()
        {
            PastEarlyGame();
            Assert.That(session.TakeInterstitial(Now, true), Is.False);
            Assert.That(events.Named("ad_interstitial_skip"), Is.Empty);
        }

        [Test]
        public void FirstTwentyMinutesAndFirstThreeCreeksAreTooEarly()
        {
            session.Progress.BestRegionsUnlocked = 3;
            session.Progress.PlaySeconds = 60 * 60;
            Assert.That(session.InterstitialBlock(Now), Is.EqualTo("too_early"));

            session.Progress.BestRegionsUnlocked = 4;
            session.Progress.PlaySeconds = 19 * 60;
            Assert.That(session.InterstitialBlock(Now), Is.EqualTo("too_early"));

            session.Progress.PlaySeconds = 20 * 60;
            Assert.That(session.InterstitialBlock(Now), Is.Null);
        }

        [Test]
        public void AnyFullScreenAdStartsTheSharedCooldown()
        {
            PastEarlyGame();
            session.MarkFullScreenAdShown(Now);
            Assert.That(session.InterstitialBlock(Now + 239), Is.EqualTo("cooldown"));
            Assert.That(session.InterstitialBlock(Now + 240), Is.Null);
        }

        [Test]
        public void ClockSetBackDoesNotLockAdsOut()
        {
            PastEarlyGame();
            session.MarkFullScreenAdShown(Now);
            Assert.That(session.InterstitialBlock(Now - 3600), Is.Null);
        }

        [Test]
        public void RemovedAdsAndTheRemoteSwitchStopForcedAds()
        {
            PastEarlyGame();
            session.Progress.AdsRemoved = true;
            Assert.That(session.InterstitialBlock(Now), Is.EqualTo("removed"));

            session.Progress.AdsRemoved = false;
            session.Economy.Config.InterstitialEnabled = 0;
            Assert.That(session.InterstitialBlock(Now), Is.EqualTo("removed"));
        }

        [Test]
        public void TakeConsumesTheBreakAndReportsSkips()
        {
            PastEarlyGame();
            session.Earn(1e12);
            session.UnlockNextRegion();
            Assert.That(session.TakeInterstitial(Now, false), Is.False);
            Assert.That(session.PendingInterstitial, Is.Null);
            var skip = events.Named("ad_interstitial_skip").Single();
            Assert.That(skip["reason"], Is.EqualTo("not_ready"));
            Assert.That(skip["trigger"], Is.EqualTo("region_unlock"));

            OpenEveryCreek();
            Assert.That(session.BuyNextTier(), Is.True);
            Assert.That(session.TakeInterstitial(Now, true), Is.True);
            Assert.That(events.Named("ad_interstitial_skip").Count(), Is.EqualTo(1));
        }
    }
}
