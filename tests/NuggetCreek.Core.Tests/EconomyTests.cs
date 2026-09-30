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
            AssertClose(114.45928560000003, economy.CollectValue(2, 1, stats, 1.5));
        }

        [Test]
        public void CollectValue_WithCollectionLayers_MatchesPython()
        {
            StatSheet stats = Sheet((Stat.DustValue, 0.5), (Stat.NuggetChance, 0.02), (Stat.NuggetValue, 0.4),
                (Stat.DoubleCatch, 0.03), (Stat.AllIncome, 0.1), (Stat.RichNuggetChance, 0.05),
                (Stat.RichNuggetValue, 0.5), (Stat.GiantNuggetChance, 0.02), (Stat.GiantNuggetValue, 0.8),
                (Stat.CritChance, 0.1), (Stat.CritValue, 0.5));
            AssertClose(171.75549441600006, economy.CollectValue(2, 1, stats, 1.5));
        }

        [Test]
        public void ActiveRate_WithCriticalCatches_MatchesPython()
        {
            StatSheet stats = Sheet((Stat.DustValue, 0.5), (Stat.NuggetChance, 0.02), (Stat.CritChance, 0.1),
                (Stat.CritValue, 0.5), (Stat.GiantNuggetChance, 0.02));
            AssertClose(108.97441920000001, economy.ActiveRate(2, 1, stats, 1.5, 0.8));
        }

        [Test]
        public void TypicalStats_MatchPython()
        {
            StatSheet s = PacingModel.TypicalStats(3, 4);
            Assert.That(s[Stat.DustValue], Is.EqualTo(3.5).Within(1e-12));
            Assert.That(s[Stat.SpawnRate], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(s[Stat.AllIncome], Is.EqualTo(1.5).Within(1e-12));
            Assert.That(s[Stat.DoubleCatch], Is.EqualTo(0.1).Within(1e-12));
            Assert.That(s[Stat.NuggetChance], Is.EqualTo(0.05).Within(1e-12));
        }

        [Test]
        public void ActiveAndIdleRate_MatchPython()
        {
            StatSheet s = PacingModel.TypicalStats(3, 4);
            AssertClose(20539.329217074257, economy.ActiveRate(3, 3, s, 2.5, 0.8));
            AssertClose(1711.6107680895213, economy.IdleRate(3, 3, s, 2.5, 0.8));
        }

        [Test]
        public void FirstSessionIncome_MatchesPython()
        {
            AssertClose(707.8499999999999, pacing.FirstSessionIncome(0));
            AssertClose(2326.1137977321746, pacing.FirstSessionIncome(1));
        }

        [TestCase(0, 16061.760000000002)]
        [TestCase(1, 62206.92899078045)]
        [TestCase(3, 3919036.5264191683)]
        [TestCase(5, 278844337.27978253)]
        [TestCase(8, 1032065715732.4807)]
        [TestCase(11, 711750967604751.5)]
        public void DayIncome_MatchesPython(int region, double expected)
        {
            AssertClose(expected, pacing.DayIncome(region));
        }

        [TestCase(1, 775.0957499999998)]
        [TestCase(2, 4149.321792394652)]
        [TestCase(3, 514929.3445296002)]
        [TestCase(5, 92477629.19268143)]
        [TestCase(10, 174971071106980.9)]
        [TestCase(19, 2.5724581206778955e+24)]
        public void SolvedRegionCost_MatchesPython(int region, double expected)
        {
            AssertClose(expected, pacing.SolveRegionCost(region));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        [TestCase(11)]
        [TestCase(12)]
        [TestCase(13)]
        [TestCase(14)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(18)]
        [TestCase(19)]
        public void ActiveToAway_FortyMinutesEarnAtLeastOneDayAway(int region)
        {
            // Design rule: 40 min of full active play earn at least one 24 h absence (no ad
            // double). Nothing is paid past the cap, so early creeks with short caps run higher.
            // Creeks 1-2 belong to the opening session, before Amos and before any absence.
            Assert.That(pacing.ActiveToAwayRatio(region), Is.InRange(1.0, 4.5));
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
            // Tier t is bought in creek index 2t (design doc 6.1).
            for (int t = 1; t < config.TierCount; t++)
            {
                double expected = PacingModel.RoundSignificant(config.RegionUnlockCosts[2 * t] * PacingModel.TierShare);
                Assert.That(config.TierCosts[t], Is.EqualTo(expected).Within(expected * 1e-9), "tier " + (t + 1));
            }
        }

        [Test]
        public void TargetDays_KeepTheOldCurveOnOddCreeks()
        {
            double[] old = { 0, 10.0 / 1440, 0.6, 3, 8, 20, 40, 70, 110, 160 };
            for (int k = 0; k < old.Length; k++)
                Assert.That(PacingModel.TargetDays[2 * k], Is.EqualTo(old[k]).Within(1e-12), "creek " + (2 * k + 1));
            Assert.That(PacingModel.TargetDays[5], Is.EqualTo(System.Math.Sqrt(0.6 * 3)).Within(1e-12));
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
        [TestCase(1, 3.872983346207417)]
        [TestCase(2, 15.0)]
        [TestCase(4, 225.0)]
        [TestCase(10, 759375.0)]
        public void DustBaseValue_IsFifteenEverySecondCreek(int region, double expected)
        {
            Assert.That(economy.DustBaseValue(region).ToDouble(), Is.EqualTo(expected).Within(expected * 1e-12));
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
            Assert.That(economy.CollectibleLifetimeSeconds(s), Is.EqualTo(6));
        }

        // Design doc 5.0.3 table.
        [TestCase(1e3, 1L)]
        [TestCase(1e11, 39L)]
        [TestCase(1e12, 63L)]
        [TestCase(1e13, 100L)]
        [TestCase(1e15, 251L)]
        [TestCase(1e18, 1000L)]
        [TestCase(999.0, 0L)]
        [TestCase(0.0, 0L)]
        public void ProspectingXp_MatchesDesignTable(double total, long expectedXp)
        {
            Assert.That(economy.ProspectingXp(total), Is.EqualTo(expectedXp));
        }

        [Test]
        public void ProspectingXp_EliasBonus()
        {
            Assert.That(economy.ProspectingXp(1e12, Sheet((Stat.ProspectingXp, 1.0))), Is.EqualTo(126));
        }

        [Test]
        public void PrestigeMultiplier_IsTwoPercentPerXp()
        {
            Assert.That(economy.PrestigeMultiplier(501), Is.EqualTo(11.02).Within(1e-12));
        }

        [TestCase(0, 0.0)]
        [TestCase(1, 1800.0)]
        [TestCase(3, 5400.0)]
        [TestCase(12, 21600.0)]
        [TestCase(20, 21600.0)]
        public void OfflineCap_HalfHourPerAmosLevel(int amosLevel, double expectedSeconds)
        {
            Assert.That(economy.OfflineCapSeconds(amosLevel), Is.EqualTo(expectedSeconds));
        }

        [TestCase(1, 2)]
        [TestCase(3, 4)]
        [TestCase(11, 12)]
        [TestCase(20, 12)]
        public void AmosMaxLevel_OpenCreeksPlusOne(int regions, int expected)
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
