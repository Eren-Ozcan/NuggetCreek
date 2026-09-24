using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class GameSessionTests
    {
        const double RelativeTolerance = 1e-12;

        EconomyConfig config;
        Economy economy;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
        }

        GameSession NewSession(double dollars = 0)
        {
            var session = new GameSession(economy, new PlayerProgress());
            session.Earn(dollars);
            return session;
        }

        static int UpgradeIndex(string id)
        {
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                if (GameCatalog.Upgrades[i].Id == id)
                    return i;
            throw new System.ArgumentException(id);
        }

        static void AssertClose(BigNumber expected, BigNumber actual)
        {
            double e = expected.ToDouble();
            Assert.That(actual.ToDouble(), Is.EqualTo(e).Within(System.Math.Abs(e) * RelativeTolerance));
        }

        [Test]
        public void FirstGoldDustInNuggetCreekIsWorthOneDollar()
        {
            GameSession session = NewSession();
            Assert.That(session.Collect(CollectibleKind.GoldDust, false).ToDouble(), Is.EqualTo(1));
            Assert.That(session.Progress.Dollars.ToDouble(), Is.EqualTo(1));
            Assert.That(session.Progress.ManualCollected, Is.EqualTo(1));
        }

        [Test]
        public void OnboardingSpawnsAreAlwaysGoldDust()
        {
            GameSession session = NewSession();
            for (int i = 0; i < config.OnboardingGuaranteedDust; i++)
            {
                Assert.That(session.RollKind(0), Is.EqualTo(CollectibleKind.GoldDust));
                session.Collect(CollectibleKind.GoldDust, false);
            }
            Assert.That(session.RollKind(0), Is.EqualTo(CollectibleKind.Nugget));
            Assert.That(session.RollKind(0.05), Is.EqualTo(CollectibleKind.GoldDust));
        }

        [Test]
        public void AverageCatchTimesSpawnRateMatchesActiveRate()
        {
            var progress = new PlayerProgress { RegionIndex = 2, RegionsUnlocked = 3, TierIndex = 2 };
            progress.UpgradeLevels[UpgradeIndex("sturdy_shovel")] = 10;
            progress.UpgradeLevels[UpgradeIndex("creek_scatter")] = 3;
            progress.UpgradeLevels[UpgradeIndex("water_channel")] = 5;
            progress.UpgradeLevels[UpgradeIndex("dust_value")] = 4;
            progress.ManualCollected = 100;
            var session = new GameSession(economy, progress);

            double p = economy.NuggetChance(session.Stats);
            double d = session.Stats[Stat.DoubleCatch];
            BigNumber average = session.CatchValue(CollectibleKind.GoldDust, false) * ((1 - p) * (1 - d))
                + session.CatchValue(CollectibleKind.GoldDust, true) * ((1 - p) * d)
                + session.CatchValue(CollectibleKind.Nugget, false) * (p * (1 - d))
                + session.CatchValue(CollectibleKind.Nugget, true) * (p * d);

            AssertClose(economy.ActiveRate(2, 2, session.Stats, 1), average * session.SpawnRate);
        }

        [Test]
        public void BuyingAnUpgradeSpendsDollarsAndRaisesValue()
        {
            GameSession session = NewSession(30);
            int shovel = UpgradeIndex("sturdy_shovel");

            Assert.That(session.BuyUpgrade(shovel), Is.True);
            Assert.That(session.Progress.Dollars.IsZero, Is.True);
            Assert.That(session.UpgradeLevel(shovel), Is.EqualTo(1));
            Assert.That(session.CatchValue(CollectibleKind.GoldDust, false).ToDouble(), Is.EqualTo(1.2).Within(1e-12));
            AssertClose(30 * 1.22, session.UpgradeCost(shovel).Value);
        }

        [Test]
        public void PurchasesFailWithoutDollars()
        {
            GameSession session = NewSession(29);
            Assert.That(session.BuyUpgrade(UpgradeIndex("sturdy_shovel")), Is.False);
            Assert.That(session.BuyAmosLevel(), Is.False);
            Assert.That(session.Progress.Dollars.ToDouble(), Is.EqualTo(29));
        }

        [Test]
        public void UpgradesOfLockedTiersCannotBeBought()
        {
            GameSession session = NewSession(1e6);
            int sieve = UpgradeIndex("steel_sieve");
            Assert.That(session.IsUpgradeUnlocked(sieve), Is.False);
            Assert.That(session.BuyUpgrade(sieve), Is.False);
        }

        [Test]
        public void MaxedUpgradeHasNoPrice()
        {
            GameSession session = NewSession(1e6);
            int scatter = UpgradeIndex("creek_scatter");
            for (int i = 0; i < 4; i++)
                Assert.That(session.BuyUpgrade(scatter), Is.True);
            Assert.That(session.UpgradeCost(scatter), Is.Null);
            Assert.That(session.BuyUpgrade(scatter), Is.False);
            Assert.That(session.SpawnRate, Is.EqualTo(config.SpawnCap));
        }

        [Test]
        public void SecondTierWaitsForPineHollow()
        {
            GameSession session = NewSession(1e6);
            Assert.That(session.IsNextTierUnlocked, Is.False);
            Assert.That(session.BuyNextTier(), Is.False);

            Assert.That(session.UnlockNextRegion(), Is.True);
            Assert.That(session.Progress.RegionIndex, Is.EqualTo(1));
            Assert.That(session.RegionName, Is.EqualTo("Pine Hollow"));
            Assert.That(session.IsNextTierUnlocked, Is.True);
            Assert.That(session.BuyNextTier(), Is.True);
            Assert.That(session.Progress.Dollars.ToDouble(), Is.EqualTo(1e6 - 910 - 230).Within(1e-6));
            Assert.That(session.IsUpgradeUnlocked(UpgradeIndex("steel_sieve")), Is.True);
            Assert.That(session.IsNextTierUnlocked, Is.False);
        }

        [Test]
        public void TravelOnlyToUnlockedRegions()
        {
            GameSession session = NewSession(1000);
            Assert.That(session.TravelTo(1), Is.False);
            session.UnlockNextRegion();
            Assert.That(session.TravelTo(0), Is.True);
            Assert.That(session.CatchValue(CollectibleKind.GoldDust, false).ToDouble(), Is.EqualTo(1));
            Assert.That(session.TravelTo(1), Is.True);
            Assert.That(session.CatchValue(CollectibleKind.GoldDust, false).ToDouble(), Is.EqualTo(15));
        }

        [Test]
        public void NoIdleIncomeBeforeAmos()
        {
            GameSession session = NewSession();
            Assert.That(session.TickIdle(60).IsZero, Is.True);
            Assert.That(session.OfflineCapSeconds, Is.EqualTo(0));
        }

        [Test]
        public void AmosJoinsForSeventyDollarsAndStartsIdle()
        {
            GameSession session = NewSession(70);
            Assert.That(session.AmosNextCost.Value.ToDouble(), Is.EqualTo(70));
            Assert.That(session.BuyAmosLevel(), Is.True);
            Assert.That(session.Progress.AmosLevel, Is.EqualTo(1));
            Assert.That(session.OfflineCapSeconds, Is.EqualTo(3600));

            // Region 1 idle = 1.2 spawns/s * $1.35 expected catch / 3.
            BigNumber expected = economy.IdleRate(0, 0, session.Stats, 1);
            Assert.That(expected.ToDouble(), Is.EqualTo(1.2 * 1.35 / 3).Within(1e-12));
            AssertClose(expected * 10, session.TickIdle(10));
        }

        [Test]
        public void AmosLevelWaitsForTheNextCreek()
        {
            GameSession session = NewSession(1e6);
            session.BuyAmosLevel();
            session.BuyAmosLevel();
            Assert.That(session.Progress.AmosLevel, Is.EqualTo(2));
            Assert.That(session.AmosNextCost, Is.Null);
            Assert.That(session.AmosWaitsForRegion, Is.True);

            session.UnlockNextRegion();
            Assert.That(session.AmosWaitsForRegion, Is.False);
            AssertClose(180e3, session.AmosNextCost.Value);
        }

        [Test]
        public void OfflineClaimPaysCappedAmountTimesMultiplier()
        {
            GameSession session = NewSession(70);
            session.BuyAmosLevel();
            var clock = new OfflineClockInput
            {
                LastSeenUtc = 1000,
                TrustedNowUtc = 1000 + 3 * 3600,
                LastDeviceUtc = 1000,
                DeviceNowUtc = 1000 + 3 * 3600,
                LastMonotonicSeconds = 50,
                MonotonicNowSeconds = 50 + 3 * 3600,
                SameBoot = true,
            };

            OfflineResult result = session.EvaluateOffline(clock);
            Assert.That(result.Status, Is.EqualTo(OfflineStatus.Credited));
            Assert.That(result.CapReached, Is.True);
            Assert.That(result.CreditedSeconds, Is.EqualTo(3600));
            AssertClose(session.OfflineRate * 3600, result.Amount);

            Assert.That(session.ClaimOffline(result, 2), Is.True);
            AssertClose(result.Amount * 2, session.Progress.Dollars);
        }

        [Test]
        public void UntrustedOfflineResultCannotBeClaimed()
        {
            GameSession session = NewSession(70);
            session.BuyAmosLevel();
            var clock = new OfflineClockInput
            {
                LastSeenUtc = 1000,
                LastDeviceUtc = 1000,
                DeviceNowUtc = 1600,
                SameBoot = false,
            };

            OfflineResult result = session.EvaluateOffline(clock);
            Assert.That(result.Status, Is.EqualTo(OfflineStatus.PendingTrustedTime));
            Assert.That(session.ClaimOffline(result, 1), Is.False);
            Assert.That(session.Progress.Dollars.IsZero, Is.True);
        }

        [Test]
        public void NormalizeResizesOldUpgradeArrays()
        {
            var progress = new PlayerProgress { UpgradeLevels = new[] { 3, 1 }, RegionIndex = 4, RegionsUnlocked = 2 };
            progress.Normalize();
            Assert.That(progress.UpgradeLevels.Length, Is.EqualTo(GameCatalog.Upgrades.Count));
            Assert.That(progress.UpgradeLevels[0], Is.EqualTo(3));
            Assert.That(progress.RegionIndex, Is.EqualTo(1));
        }
    }
}
