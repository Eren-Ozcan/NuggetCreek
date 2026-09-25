using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class MotherLodeTests
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

        static void HitTimes(MotherLodeRun run, int hits, double gap = 0.1)
        {
            for (int i = 0; i < hits; i++)
            {
                run.Tick(gap);
                run.Hit();
            }
        }

        // --- Trigger ---

        [Test]
        public void NothingBeforeTwentyFiveMinutesOfPlay()
        {
            GameSession session = NewSession(new PlayerProgress { CollectedSinceMotherLode = 1000 });
            session.TickPlay(25 * 60 - 1);
            Assert.That(session.MotherLodeDue, Is.False);
            session.TickPlay(1);
            Assert.That(session.MotherLodeDue, Is.True);
        }

        [Test]
        public void DueAfterFourHundredCatches()
        {
            GameSession session = NewSession(new PlayerProgress { PlaySeconds = 3600 });
            for (int i = 0; i < 399; i++)
                session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.MotherLodeDue, Is.False);
            session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.MotherLodeDue, Is.True);
        }

        [Test]
        public void DueAfterTenMinutesWithoutCatches()
        {
            GameSession session = NewSession(new PlayerProgress { PlaySeconds = 3600 });
            session.TickPlay(599);
            Assert.That(session.MotherLodeDue, Is.False);
            session.TickPlay(1);
            Assert.That(session.MotherLodeDue, Is.True);
        }

        [Test]
        public void WrenAtMaxNeedsTwoHundredEightyCatches()
        {
            var progress = new PlayerProgress { PlaySeconds = 3600, CollectedSinceMotherLode = 280 };
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                if (GameCatalog.Crew[i].Id == "wren")
                    progress.CrewLevels[i] = 10;
            GameSession session = NewSession(progress);
            Assert.That(session.MotherLodeDue, Is.True);
        }

        [Test]
        public void StartingResetsBothCounters()
        {
            GameSession session = NewSession(new PlayerProgress { PlaySeconds = 3600, CollectedSinceMotherLode = 400, SecondsSinceMotherLode = 700 });
            session.StartMotherLode();
            Assert.That(session.MotherLodeDue, Is.False);
            Assert.That(session.Progress.CollectedSinceMotherLode, Is.Zero);
            Assert.That(session.Progress.SecondsSinceMotherLode, Is.Zero);
        }

        // --- Combo ---

        [Test]
        public void EveryFourHitsRaiseTheComboUpToTen()
        {
            var run = new MotherLodeRun(config, 1);
            HitTimes(run, 3);
            Assert.That(run.Combo, Is.EqualTo(1));
            HitTimes(run, 1);
            Assert.That(run.Combo, Is.EqualTo(2));
            HitTimes(run, 100);
            Assert.That(run.Combo, Is.EqualTo(10));
            Assert.That(run.PeakCombo, Is.EqualTo(10));
            Assert.That(run.Hits, Is.EqualTo(104));
        }

        [Test]
        public void APauseDropsTheComboButKeepsThePeak()
        {
            var run = new MotherLodeRun(config, 1);
            HitTimes(run, 12);
            Assert.That(run.Combo, Is.EqualTo(4));
            run.Tick(1.1);
            Assert.That(run.Combo, Is.EqualTo(1));
            Assert.That(run.PeakCombo, Is.EqualTo(4));
        }

        [Test]
        public void RunEndsAfterTwentySecondsAndIgnoresLateHits()
        {
            var run = new MotherLodeRun(config, 1);
            run.Tick(19.9);
            Assert.That(run.IsOver, Is.False);
            run.Tick(0.2);
            Assert.That(run.IsOver, Is.True);
            run.Hit();
            Assert.That(run.Hits, Is.Zero);
        }

        // --- Reward ---

        [Test]
        public void RewardIsSixtyToNinetySecondsOfIncome()
        {
            var stats = new StatSheet();
            Assert.That(economy.MotherLodeReward(100, 1, stats).ToDouble(), Is.EqualTo(6000).Within(1e-9));
            Assert.That(economy.MotherLodeReward(100, 10, stats).ToDouble(), Is.EqualTo(9000).Within(1e-9));
            Assert.That(economy.MotherLodeReward(100, 4, stats).ToDouble(), Is.EqualTo(7000).Within(1e-9));
        }

        [Test]
        public void RewardBonusScalesThePayout()
        {
            var stats = new StatSheet();
            stats.Add(Stat.MotherLodeReward, 2.0);
            Assert.That(economy.MotherLodeReward(100, 1, stats).ToDouble(), Is.EqualTo(18000).Within(1e-9));
        }

        [Test]
        public void FinishPaysDollarsAndOneGemOnce()
        {
            GameSession session = NewSession(new PlayerProgress { AmosLevel = 1 });
            BigNumber income = session.IncomePerSecond;
            MotherLodeRun run = session.StartMotherLode();
            HitTimes(run, 40);

            BigNumber paid = session.FinishMotherLode(run);

            Assert.That(paid.ToDouble(), Is.EqualTo(income.ToDouble() * 90).Within(income.ToDouble() * 1e-9));
            Assert.That(session.Progress.Dollars, Is.EqualTo(paid));
            Assert.That(session.Progress.Gems, Is.EqualTo(1));
            Assert.That(session.FinishMotherLode(run).IsZero, Is.True, "a run pays only once");
            Assert.That(session.Progress.Gems, Is.EqualTo(1));
        }

        [Test]
        public void IncomeIncludesSwipingAndCrew()
        {
            GameSession session = NewSession(new PlayerProgress { AmosLevel = 1 });
            double active = economy.ActiveRate(0, 0, session.Stats, 1).ToDouble();
            double idle = session.IdleRate.ToDouble();
            Assert.That(session.IncomePerSecond.ToDouble(), Is.EqualTo(active + idle).Within(active * 1e-9));
        }

        [Test]
        public void SummonCostsFiveGems()
        {
            GameSession session = NewSession();
            session.EarnGems(4);
            Assert.That(session.SummonMotherLode(), Is.Null);
            session.EarnGems(1);
            Assert.That(session.SummonMotherLode(), Is.Not.Null);
            Assert.That(session.Progress.Gems, Is.Zero);
        }
    }
}
