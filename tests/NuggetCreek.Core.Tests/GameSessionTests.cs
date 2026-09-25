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
            Assert.That(session.RollKind(0.01), Is.EqualTo(CollectibleKind.Nugget));
            Assert.That(session.RollKind(0.05), Is.EqualTo(CollectibleKind.GoldDust));
        }

        [Test]
        public void OneRollSplitsNuggetsIntoGiantRichAndPlain()
        {
            GameSession session = NewSession();
            session.Progress.ManualCollected = 100;
            session.Stats.Add(Stat.GiantNuggetChance, 0.2);

            // Nugget 5%: Giant takes the first 20% of it, Rich 10% of the rest.
            Assert.That(session.RollKind(0.0099), Is.EqualTo(CollectibleKind.GiantNugget));
            Assert.That(session.RollKind(0.0101), Is.EqualTo(CollectibleKind.RichNugget));
            Assert.That(session.RollKind(0.0139), Is.EqualTo(CollectibleKind.RichNugget));
            Assert.That(session.RollKind(0.0141), Is.EqualTo(CollectibleKind.Nugget));
            Assert.That(session.RollKind(0.0501), Is.EqualTo(CollectibleKind.GoldDust));
        }

        [Test]
        public void NuggetLayersPayTheirMultiples()
        {
            GameSession session = NewSession();
            Assert.That(session.CatchValue(CollectibleKind.Nugget, false).ToDouble(), Is.EqualTo(8).Within(1e-12));
            Assert.That(session.CatchValue(CollectibleKind.RichNugget, false).ToDouble(), Is.EqualTo(24).Within(1e-12));
            Assert.That(session.CatchValue(CollectibleKind.GiantNugget, false).ToDouble(), Is.EqualTo(200).Within(1e-12));
            Assert.That(session.CatchValue(CollectibleKind.GoldDust, true, true).ToDouble(), Is.EqualTo(6).Within(1e-12));
        }

        [Test]
        public void LaunchDefaultsKeepCriticalGiantAndVeinClosed()
        {
            GameSession session = NewSession();
            Assert.That(session.RollCritical(0), Is.False);
            Assert.That(session.VeinOpen, Is.False);
            session.Progress.ManualCollected = 100;
            Assert.That(session.RollKind(0), Is.EqualTo(CollectibleKind.RichNugget));
            for (int i = 0; i < 50; i++)
                session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.VeinLevel, Is.EqualTo(0));
            Assert.That(session.VeinStreak, Is.EqualTo(0));
        }

        [Test]
        public void VeinClimbsWithCatchesInARowAndBreaksOnALostCollectible()
        {
            GameSession session = NewSession();
            session.Stats.Add(Stat.VeinMaxLevel, 2);
            session.Stats.Add(Stat.VeinCatchesPerLevel, -4);
            Assert.That(session.VeinCatchesPerLevel, Is.EqualTo(16));

            for (int i = 0; i < 16; i++)
                session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.VeinLevel, Is.EqualTo(1));
            Assert.That(session.CatchValue(CollectibleKind.GoldDust, false).ToDouble(), Is.EqualTo(1.1).Within(1e-12));

            for (int i = 0; i < 40; i++)
                session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.VeinLevel, Is.EqualTo(2), "capped at the max level");

            Assert.That(session.LoseCollectible(), Is.True);
            Assert.That(session.VeinLevel, Is.EqualTo(0));
            Assert.That(session.LoseCollectible(), Is.False);
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

            session.Stats.Add(Stat.GiantNuggetChance, 0.1);
            session.Stats.Add(Stat.CritChance, 0.1);
            session.Stats.Add(Stat.CritValue, 0.5);

            double p = economy.NuggetChance(session.Stats);
            double g = economy.GiantNuggetChance(session.Stats);
            double r = economy.RichNuggetChance(session.Stats);
            double d = session.Stats[Stat.DoubleCatch];
            double c = economy.CritChance(session.Stats);
            var kinds = new[]
            {
                (CollectibleKind.GoldDust, 1 - p),
                (CollectibleKind.GiantNugget, p * g),
                (CollectibleKind.RichNugget, p * (1 - g) * r),
                (CollectibleKind.Nugget, p * (1 - g) * (1 - r)),
            };
            BigNumber average = BigNumber.Zero;
            foreach (var (kind, chance) in kinds)
                foreach (bool doubleCatch in new[] { false, true })
                    foreach (bool critical in new[] { false, true })
                        average += session.CatchValue(kind, doubleCatch, critical)
                            * (chance * (doubleCatch ? d : 1 - d) * (critical ? c : 1 - c));

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
            Assert.That(session.Progress.Dollars.ToDouble(), Is.EqualTo(1e6 - 970 - 240).Within(1e-6));
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
            Assert.That(expected.ToDouble(), Is.EqualTo(1.2 * 1.43 / 3).Within(1e-12));
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
            AssertClose(190e3, session.AmosNextCost.Value);
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
