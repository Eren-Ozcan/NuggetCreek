using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    /// <summary>
    /// Expected values come from tools/economy_tune.py (design doc 13.3: the C# class and
    /// the Python reference must produce the same numbers). The config-default tests
    /// re-solve the tables so a formula change cannot silently drift from the defaults.
    /// </summary>
    public class EconomyTests
    {
        const double RelativeTolerance = 1e-12;

        EconomyConfig config;
        Economy economy;
        PacingModel pacing;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
            pacing = new PacingModel(economy);
        }

        static void AssertClose(double expected, BigNumber actual)
        {
            Assert.That(actual.ToDouble(), Is.EqualTo(expected).Within(System.Math.Abs(expected) * RelativeTolerance));
        }

        static StatSheet Sheet(params (Stat stat, double value)[] entries)
        {
            var sheet = new StatSheet();
            foreach (var (stat, value) in entries)
                sheet.Add(stat, value);
            return sheet;
        }

        // --- Python parity ---

        [Test]
        public void CollectValue_MatchesPython()
        {
            StatSheet stats = Sheet((Stat.DustValue, 0.5), (Stat.NuggetChance, 0.02), (Stat.NuggetValue, 0.4),
                (Stat.DoubleCatch, 0.03), (Stat.AllIncome, 0.1));
            AssertClose(1716.8892840000003, economy.CollectValue(2, 1, stats, 1.5));
        }

        [Test]
        public void CollectValue_WithCollectionLayers_MatchesPython()
        {
            StatSheet stats = Sheet((Stat.DustValue, 0.5), (Stat.NuggetChance, 0.02), (Stat.NuggetValue, 0.4),
                (Stat.DoubleCatch, 0.03), (Stat.AllIncome, 0.1), (Stat.RichNuggetChance, 0.05),
                (Stat.RichNuggetValue, 0.5), (Stat.GiantNuggetChance, 0.02), (Stat.GiantNuggetValue, 0.8),
                (Stat.CritChance, 0.1), (Stat.CritValue, 0.5));
            AssertClose(2576.3324162400004, economy.CollectValue(2, 1, stats, 1.5));
        }

        [Test]
        public void ActiveRate_WithCriticalCatches_MatchesPython()
        {
            StatSheet stats = Sheet((Stat.DustValue, 0.5), (Stat.NuggetChance, 0.02), (Stat.CritChance, 0.1),
                (Stat.CritValue, 0.5), (Stat.GiantNuggetChance, 0.02));
            AssertClose(1634.6162880000002, economy.ActiveRate(2, 1, stats, 1.5, 0.8));
        }

        [Test]
        public void TypicalStats_MatchPython()
        {
            StatSheet s = PacingModel.TypicalStats(3, 4);
            Assert.That(s[Stat.DustValue], Is.EqualTo(3.5).Within(1e-12));
            Assert.That(s[Stat.SpawnRate], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(s[Stat.IdleSpeed], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(s[Stat.DoubleCatch], Is.EqualTo(0.1).Within(1e-12));
            Assert.That(s[Stat.OfflineIncome], Is.EqualTo(0.5).Within(1e-12));
            Assert.That(s[Stat.NuggetChance], Is.EqualTo(0.05).Within(1e-12));
        }

        [Test]
        public void ActiveAndIdleRate_MatchPython()
        {
            StatSheet s = PacingModel.TypicalStats(3, 4);
            AssertClose(477290.88000000006, economy.ActiveRate(3, 3, s, 2.5, 0.8));
            AssertClose(318193.92000000004, economy.IdleRate(3, 3, s, 2.5, 0.8));
        }

        [Test]
        public void FirstSessionIncome_MatchesPython()
        {
            AssertClose(1608.7499999999995, pacing.FirstSessionIncome());
        }

        [TestCase(0, 46949.76000000001)]
        [TestCase(1, 2836753.92)]
        [TestCase(2, 124929604.80000001)]
        [TestCase(3, 12256829798.400002)]
        [TestCase(4, 1887709941596.161)]
        [TestCase(5, 245294748610560.1)]
        public void DayIncome_MatchesPython(int region, double expected)
        {
            AssertClose(expected, pacing.DayIncome(region));
        }

        [TestCase(1, 965.2499999999997)]
        [TestCase(2, 1004210.8876799999)]
        [TestCase(3, 179898630.91200003)]
        [TestCase(4, 36770489395.200005)]
        [TestCase(5, 13591511579492.357)]
        public void SolvedRegionCost_MatchesPython(int region, double expected)
        {
            AssertClose(expected, pacing.SolveRegionCost(region));
        }

        // --- Config defaults are the solved, rounded tables ---

        [Test]
        public void RegionCostDefaults_AreSolvedValues()
        {
            for (int r = 1; r < config.RegionCount; r++)
            {
                double solved = PacingModel.RoundSignificant(pacing.SolveRegionCost(r).ToDouble());
                Assert.That(config.RegionUnlockCosts[r], Is.EqualTo(solved).Within(solved * 1e-9), "region " + r);
            }
        }

        [Test]
        public void TierCostDefaults_AreQuarterOfRegionCost()
        {
            // Tier t is bought inside region t; tiers 7 and 8 use regions 7 and 8 (post-launch).
            double region7 = PacingModel.RoundSignificant(pacing.SolveRegionCost(6).ToDouble());
            double region8 = region7 * region7 / config.RegionUnlockCosts[5];
            double[] regionCosts = config.RegionUnlockCosts.Skip(1).Concat(new[] { region7, region8 }).ToArray();
            for (int t = 1; t < config.TierCount; t++)
            {
                double expected = PacingModel.RoundSignificant(regionCosts[t - 1] * PacingModel.TierShare);
                Assert.That(config.TierCosts[t], Is.EqualTo(expected).Within(expected * 1e-9), "tier " + (t + 1));
            }
        }

        [Test]
        public void UpgradeBaseDefaults_FollowShareWithFloor()
        {
            Assert.That(config.UpgradeBaseCosts[0], Is.EqualTo(30));
            for (int t = 1; t < config.UpgradeBaseCosts.Length; t++)
            {
                double share = PacingModel.RoundSignificant(config.TierCosts[t] * PacingModel.UpgradeBaseShare);
                double expected = System.Math.Max(share, 2 * config.UpgradeBaseCosts[t - 1]);
                Assert.That(config.UpgradeBaseCosts[t], Is.EqualTo(expected).Within(expected * 1e-9), "tier " + (t + 1));
            }
        }

        [Test]
        public void AmosLevelDefaults_AreSolvedValues()
        {
            for (int level = 2; level <= 12; level++)
            {
                double solved = PacingModel.RoundSignificant(pacing.SolveAmosLevelCost(level).ToDouble());
                Assert.That(economy.AmosLevelCost(level).ToDouble(), Is.EqualTo(solved).Within(solved * 1e-9), "L" + level);
            }
        }

        [Test]
        public void CrewHireDefaults_FollowGrowthWithCap()
        {
            for (int hired = 0; hired < config.CrewHireCosts.Length; hired++)
            {
                double raw = 10 * System.Math.Pow(1.45, hired);
                int expected = System.Math.Min(900, (int)(5 * System.Math.Round(raw / 5, System.MidpointRounding.ToEven)));
                Assert.That(config.CrewHireCosts[hired], Is.EqualTo(expected), "hire " + (hired + 1));
            }
        }

        [Test]
        public void NoGemSpend_NeedsMoreThanTwelveF2PDays()
        {
            // Design doc 5.0.7 rule 1, at 78 F2P Gems per day.
            const int twelveDays = 12 * 78;
            Assert.That(config.CrewHireCosts.Max(), Is.LessThanOrEqualTo(twelveDays));
            Assert.That(config.CrewLevelUpCosts.Max(), Is.LessThanOrEqualTo(twelveDays));
        }

        // --- Catalog ---

        [Test]
        public void Catalog_HasSixteenCrewIncludingAmos()
        {
            Assert.That(GameCatalog.Crew.Count + 1, Is.EqualTo(16));
            Assert.That(GameCatalog.Crew.Select(c => c.Id).Distinct().Count(), Is.EqualTo(GameCatalog.Crew.Count));
            Assert.That(GameCatalog.Crew.Any(c => c.Id == GameCatalog.AmosId), Is.False);
            Assert.That(config.CrewHireCosts.Length, Is.EqualTo(GameCatalog.Crew.Count));
        }

        [Test]
        public void Catalog_TwoUpgradesPerTier()
        {
            for (int t = 0; t < config.TierCount; t++)
                Assert.That(GameCatalog.Upgrades.Count(u => u.TierIndex == t), Is.EqualTo(2), "tier " + (t + 1));
        }

        // --- Formula behaviour ---

        [Test]
        public void SpawnRate_IsCappedAtSwipeCadence()
        {
            Assert.That(economy.SpawnRate(1.2, Sheet((Stat.SpawnRate, 1.0))), Is.EqualTo(1.5));
            Assert.That(economy.SpawnRate(0.8, Sheet((Stat.SpawnRate, 0.5))), Is.EqualTo(1.2).Within(1e-12));
        }

        [Test]
        public void FirstRegionRunsHotter()
        {
            Assert.That(economy.SpawnBaseFor(0), Is.EqualTo(1.2));
            Assert.That(economy.SpawnBaseFor(1), Is.EqualTo(0.8));
        }

        [TestCase(0, 1.0)]
        [TestCase(1, 15.0)]
        [TestCase(2, 225.0)]
        [TestCase(5, 759375.0)]
        public void DustBaseValue_Is15PowRegion(int region, double expected)
        {
            Assert.That(economy.DustBaseValue(region).ToDouble(), Is.EqualTo(expected));
        }

        [Test]
        public void ActiveIncome_BoostsActiveButNotIdle()
        {
            StatSheet plain = new StatSheet();
            StatSheet ole = Sheet((Stat.ActiveIncome, 0.8));
            AssertClose(economy.ActiveRate(1, 1, plain, 1).ToDouble() * 1.8, economy.ActiveRate(1, 1, ole, 1));
            AssertClose(economy.IdleRate(1, 1, plain, 1).ToDouble(), economy.IdleRate(1, 1, ole, 1));
        }

        [Test]
        public void OfflineRate_AppliesOfflineIncome()
        {
            StatSheet s = Sheet((Stat.OfflineIncome, 0.5));
            AssertClose(economy.IdleRate(2, 2, s, 1).ToDouble() * 1.5, economy.OfflineRate(2, 2, s, 1));
        }

        [TestCase(1, 30.0)]
        [TestCase(2, 36.6)]
        [TestCase(5, 66.46003679999998)]
        [TestCase(10, 179.62208398593236)]
        public void UpgradeCost_Grows22PercentPerLevel(int level, double expected)
        {
            AssertClose(expected, economy.UpgradeCost(GameCatalog.Upgrades[0], level, new StatSheet()));
        }

        [Test]
        public void CostReductions_ApplyAndNeverReachZero()
        {
            StatSheet s = Sheet((Stat.RegionCost, -0.3), (Stat.TierCost, -5));
            AssertClose(config.RegionUnlockCosts[2] * 0.7, economy.RegionUnlockCost(2, s));
            AssertClose(config.TierCosts[2] * 0.1, economy.TierCost(2, s));
        }

        [Test]
        public void CrewPrices_EndAtLimits()
        {
            Assert.That(economy.CrewHireCost(0), Is.EqualTo(10));
            Assert.That(economy.CrewHireCost(15), Is.Null);
            Assert.That(economy.CrewLevelUpCost(1), Is.EqualTo(6));
            Assert.That(economy.CrewLevelUpCost(9), Is.EqualTo(119));
            Assert.That(economy.CrewLevelUpCost(10), Is.Null);
        }

        [Test]
        public void MotherLodeAndLifetime_FollowCrewStats()
        {
            StatSheet s = Sheet((Stat.MotherLodeFrequency, -0.3), (Stat.CollectibleLifetime, 1.5));
            Assert.That(economy.MotherLodeEveryCollectibles(s), Is.EqualTo(280));
            Assert.That(economy.CollectibleLifetimeSeconds(s), Is.EqualTo(4.5));
        }

        // Design doc 5.0.3 table.
        [TestCase(1e6, 1L)]
        [TestCase(1e8, 7L)]
        [TestCase(1e10, 63L)]
        [TestCase(1e12, 501L)]
        [TestCase(1e15, 11220L)]
        [TestCase(1e18, 251188L)]
        [TestCase(999999.0, 0L)]
        [TestCase(0.0, 0L)]
        public void ProspectingXp_MatchesDesignTable(double total, long expectedXp)
        {
            Assert.That(economy.ProspectingXp(total), Is.EqualTo(expectedXp));
        }

        [Test]
        public void ProspectingXp_EliasBonus()
        {
            Assert.That(economy.ProspectingXp(1e12, Sheet((Stat.ProspectingXp, 1.0))), Is.EqualTo(1002));
        }

        [Test]
        public void PrestigeMultiplier_IsTwoPercentPerXp()
        {
            Assert.That(economy.PrestigeMultiplier(501), Is.EqualTo(11.02).Within(1e-12));
        }

        [TestCase(0, 0.0)]
        [TestCase(1, 3600.0)]
        [TestCase(3, 10800.0)]
        [TestCase(12, 43200.0)]
        [TestCase(20, 43200.0)]
        public void OfflineCap_OneHourPerAmosLevel(int amosLevel, double expectedSeconds)
        {
            Assert.That(economy.OfflineCapSeconds(amosLevel), Is.EqualTo(expectedSeconds));
        }

        [TestCase(1, 2)]
        [TestCase(3, 6)]
        [TestCase(6, 12)]
        [TestCase(8, 12)]
        public void AmosMaxLevel_TwoPerRegion(int regions, int expected)
        {
            Assert.That(economy.AmosMaxLevel(regions), Is.EqualTo(expected));
        }

        [Test]
        public void AmosLevelCosts_Increase()
        {
            Assert.That(economy.AmosLevelCost(1).ToDouble(), Is.EqualTo(70));
            for (int level = 2; level <= 12; level++)
                Assert.That(economy.AmosLevelCost(level), Is.GreaterThan(economy.AmosLevelCost(level - 1)), "L" + level);
        }
    }
}
