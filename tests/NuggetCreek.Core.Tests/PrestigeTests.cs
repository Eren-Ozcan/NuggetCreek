using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class PrestigeTests
    {
        EconomyConfig config;
        Economy economy;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
        }

        GameSession NewSession(PlayerProgress progress = null) =>
            new GameSession(economy, progress ?? new PlayerProgress(), new System.Random(1));

        static int PerkIndex(string id) =>
            GameCatalog.Perks.Select((p, i) => (p, i)).First(x => x.p.Id == id).i;

        // --- Rebirth ---

        [Test]
        public void RebirthResetsTheClaimAndKeepsTheRest()
        {
            var progress = new PlayerProgress
            {
                Dollars = 5e9, TotalEarned = 2e15, RegionIndex = 4, RegionsUnlocked = 5, TierIndex = 4,
                AmosLevel = 9, Gems = 120, ProspectingXp = 10, GuildLevel = 3,
            };
            progress.UpgradeLevels[0] = 10;
            progress.CrewLevels[0] = 4;
            progress.GearLevels[0] = 2;
            progress.NuggetCatches[0] = 12;
            GameSession session = NewSession(progress);

            Assert.That(session.ClaimXp, Is.EqualTo(861));
            Assert.That(session.Rebirth(), Is.True);

            Assert.That(progress.ProspectingXp, Is.EqualTo(871));
            Assert.That(progress.Rebirths, Is.EqualTo(1));
            Assert.That(progress.Dollars.IsZero, Is.True);
            Assert.That(progress.TotalEarned.IsZero, Is.True);
            Assert.That(progress.RegionIndex, Is.EqualTo(0));
            Assert.That(progress.RegionsUnlocked, Is.EqualTo(1));
            Assert.That(progress.TierIndex, Is.EqualTo(0));
            Assert.That(progress.UpgradeLevels.All(l => l == 0), Is.True);

            Assert.That(progress.AmosLevel, Is.EqualTo(9));
            Assert.That(progress.Gems, Is.EqualTo(120));
            Assert.That(progress.CrewLevels[0], Is.EqualTo(4));
            Assert.That(progress.GearLevels[0], Is.EqualTo(2));
            Assert.That(progress.NuggetCatches[0], Is.EqualTo(12));
            Assert.That(progress.GuildLevel, Is.EqualTo(3));
            Assert.That(progress.BestRegionsUnlocked, Is.EqualTo(5), "the free second gear slot stays open");
        }

        [Test]
        public void NoRebirthWithoutXp()
        {
            GameSession session = NewSession(new PlayerProgress { TotalEarned = 999_999 });
            Assert.That(session.CanRebirth, Is.False);
            Assert.That(session.Rebirth(), Is.False);
        }

        [Test]
        public void MultiplierAfterRebirthCountsTheClaim()
        {
            GameSession session = NewSession(new PlayerProgress { TotalEarned = 2e15 });
            Assert.That(session.PrestigeMultiplierAfterRebirth, Is.EqualTo(18.22).Within(1e-12));
            session.Rebirth();
            Assert.That(session.PrestigeMultiplier, Is.EqualTo(18.22).Within(1e-12));
        }

        [Test]
        public void SuggestionWaitsForTheWall()
        {
            // $20T lifetime in Nugget Creek: 63 XP would make x2.26, and Pine Hollow is cheap.
            var progress = new PlayerProgress { TotalEarned = 2e13, AmosLevel = 1, ManualCollected = 100 };
            GameSession session = NewSession(progress);
            Assert.That(session.PrestigeMultiplierAfterRebirth, Is.GreaterThan(2));
            Assert.That(session.HoursToNextTarget, Is.LessThan(48));
            Assert.That(session.RebirthSuggested, Is.False, "the next creek is close");

            // Everything bought: nothing left but a new claim.
            progress.RegionsUnlocked = config.RegionCount;
            progress.RegionIndex = config.RegionCount - 1;
            progress.TierIndex = config.TierCount - 1;
            Assert.That(session.HoursToNextTarget, Is.EqualTo(double.PositiveInfinity));
            Assert.That(session.RebirthSuggested, Is.True);
        }

        [Test]
        public void SuggestionNeedsTheClaimToDoubleIncome()
        {
            var progress = new PlayerProgress { TotalEarned = 2e13, AmosLevel = 1, ProspectingXp = 100 };
            progress.RegionsUnlocked = config.RegionCount;
            progress.RegionIndex = config.RegionCount - 1;
            progress.TierIndex = config.TierCount - 1;
            GameSession session = NewSession(progress);
            // x3 now, x4.26 after: not worth a restart yet.
            Assert.That(session.PrestigeMultiplierAfterRebirth, Is.LessThan(2 * session.PrestigeMultiplier));
            Assert.That(session.RebirthSuggested, Is.False);
        }

        [Test]
        public void HoursToNextTargetUseATypicalDay()
        {
            var progress = new PlayerProgress { AmosLevel = 1, ManualCollected = 100 };
            GameSession session = NewSession(progress);
            // 10 h away with a 30 min cap: only the cap is paid.
            BigNumber perDay = session.IncomePerSecond * (28 * 60) + session.OfflineRate * 1800;
            double expected = (session.NextRegionCost.Value / perDay).ToDouble() * 24;
            Assert.That(session.HoursToNextTarget, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void NoSuggestionForASmallClaim()
        {
            GameSession session = NewSession(new PlayerProgress { TotalEarned = 2e11, AmosLevel = 1 });
            Assert.That(session.ClaimXp, Is.LessThan(25));
            Assert.That(session.RebirthSuggested, Is.False);
        }

        // --- Guild ---

        [Test]
        public void GuildLevelsNeedFortyPlusThirtyPerLevel()
        {
            Assert.That(economy.GuildXpToNext(0), Is.EqualTo(40));
            Assert.That(economy.GuildXpToNext(1), Is.EqualTo(70));
            Assert.That(economy.GuildXpToNext(99), Is.EqualTo(3010));

            GameSession session = NewSession();
            for (int ad = 0; ad < 3; ad++)
                Assert.That(session.AddGuildAdXp(), Is.EqualTo(0));
            Assert.That(session.AddGuildAdXp(), Is.EqualTo(1), "4 ads x 10 XP = level 1");
            Assert.That(session.PerkPoints, Is.EqualTo(1));
            Assert.That(session.AddGuildXp(70 + 100), Is.EqualTo(2));
            Assert.That(session.Progress.GuildXp, Is.EqualTo(0));
        }

        [Test]
        public void GuildStopsAtLevelHundred()
        {
            GameSession session = NewSession();
            session.AddGuildXp(long.MaxValue / 4);
            Assert.That(session.GuildLevel, Is.EqualTo(100));
            Assert.That(session.Progress.GuildXp, Is.EqualTo(0));
            Assert.That(session.AddGuildXp(10), Is.EqualTo(0));
        }

        [Test]
        public void MilestonesOpenVeinChestsComboIncomeAndXp()
        {
            GameSession session = NewSession(new PlayerProgress { TotalEarned = 2e15, ChestsOpened = 1, ChestsWaiting = 1 });
            Assert.That(session.VeinOpen, Is.False);

            session.Progress.GuildLevel = 8;
            session.RebuildStats();
            Assert.That(session.VeinMaxLevel, Is.EqualTo(2));

            session.Progress.GuildLevel = 20;
            session.RebuildStats();
            Assert.That(session.ChestCapacity, Is.EqualTo(5));
            session.StartChest();
            Assert.That(session.Progress.ChestSecondsLeft, Is.EqualTo(450).Within(1e-9));

            session.Progress.GuildLevel = 40;
            session.RebuildStats();
            Assert.That(economy.MotherLodeMaxCombo(session.Stats), Is.EqualTo(15));
            Assert.That(session.StartMotherLode().MaxCombo, Is.EqualTo(15));

            double before = session.PrestigeMultiplierAfterRebirth;
            session.Progress.GuildLevel = 65;
            session.RebuildStats();
            Assert.That(session.PrestigeMultiplierAfterRebirth, Is.EqualTo(before * 1.5).Within(1e-9));

            session.Progress.GuildLevel = 100;
            session.RebuildStats();
            Assert.That(session.ClaimXp, Is.EqualTo(1291), "861 x 1.5");
        }

        [Test]
        public void MotherLodeRewardScalesToTheRaisedCombo()
        {
            var stats = new StatSheet();
            stats.Add(Stat.MotherLodeMaxCombo, 5);
            BigNumber income = 1;
            Assert.That(economy.MotherLodeReward(income, 15, stats).ToDouble(), Is.EqualTo(90).Within(1e-9));
            Assert.That(economy.MotherLodeReward(income, 10, stats).ToDouble(), Is.EqualTo(60 + 30 * 9 / 14.0).Within(1e-9));
        }

        // --- Perks ---

        [Test]
        public void PerksSpendGuildLevelsAndResetForFree()
        {
            GameSession session = NewSession(new PlayerProgress { GuildLevel = 5, AmosLevel = 2 });
            int nightWatch = PerkIndex("night_watch");
            int stream = PerkIndex("steady_stream");

            Assert.That(session.RankUpPerk(nightWatch), Is.True);
            Assert.That(session.PerkPoints, Is.EqualTo(3), "Night Watch costs 2");
            Assert.That(session.OfflineCapSeconds, Is.EqualTo(1.5 * 3600));

            Assert.That(session.RankUpPerk(stream), Is.True);
            Assert.That(session.RankUpPerk(stream), Is.True);
            Assert.That(session.RankUpPerk(stream), Is.True);
            Assert.That(session.PerkPoints, Is.EqualTo(0));
            Assert.That(session.RankUpPerk(stream), Is.False);
            Assert.That(session.Stats[Stat.AllIncome], Is.EqualTo(0.45).Within(1e-12));

            session.ResetPerks();
            Assert.That(session.PerkPoints, Is.EqualTo(5));
            Assert.That(session.Stats[Stat.AllIncome], Is.EqualTo(0));
            Assert.That(session.OfflineCapSeconds, Is.EqualTo(3600));
        }

        [Test]
        public void PerkRanksStopAtTheirMax()
        {
            GameSession session = NewSession(new PlayerProgress { GuildLevel = 100 });
            int grip = PerkIndex("sure_grip");
            for (int i = 0; i < 4; i++)
                Assert.That(session.RankUpPerk(grip), Is.True);
            Assert.That(session.RankUpPerk(grip), Is.False);
            Assert.That(session.VeinCatchesPerLevel, Is.EqualTo(12));
        }

        [Test]
        public void OfflineCapIgnoresNightWatchBeforeAmos()
        {
            var stats = new StatSheet();
            stats.Add(Stat.OfflineCapHours, 4);
            Assert.That(economy.OfflineCapSeconds(0, stats), Is.EqualTo(0));
            Assert.That(economy.OfflineCapSeconds(1, stats), Is.EqualTo(4.5 * 3600));
        }

        [Test]
        public void GiantLayerStaysUnderTwentyPercentAtFullInvestment()
        {
            // Full sluice Nugget upgrades and crew, Big Strike 10/10, Assay Bonus 10/10, no Midas.
            var stats = new StatSheet();
            stats.Add(Stat.NuggetChance, 0.25);
            stats.Add(Stat.NuggetValue, 3.5);
            stats.Add(Stat.GiantNuggetChance, 0.007);
            stats.Add(Stat.GiantNuggetValue, 0.8);
            double p = economy.NuggetChance(stats);
            double nugget = economy.NuggetValueMultiplier(stats);
            double giant = p * nugget * economy.GiantNuggetChance(stats) * economy.GiantNuggetValueMultiplier(stats);
            double mix = 1 + p * (nugget * economy.NuggetLayerFactor(stats) - 1);
            Assert.That(giant / mix, Is.LessThanOrEqualTo(0.2005));
        }
    }
}
