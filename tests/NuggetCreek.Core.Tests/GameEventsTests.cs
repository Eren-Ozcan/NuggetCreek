using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class GameEventsTests
    {
        GameSession session;
        RecordedGameEvents events;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new Economy(new EconomyConfig()), new PlayerProgress(), new Random(7));
            events = new RecordedGameEvents();
            session.Events = events;
        }

        [Test]
        public void EventNamesFollowFirebaseRules()
        {
            session.Collect(CollectibleKind.GoldDust, false);
            session.EarnGems(100, "debug");
            session.Earn(1e9);
            session.BuyAmosLevel();
            session.UnlockNextRegion();
            session.BuyNextTier();
            session.MarkSeen(PeteLine.Welcome);

            Assert.That(events.Events, Is.Not.Empty);
            foreach ((string name, Dictionary<string, object> parameters) in events.Events)
            {
                Assert.That(name.Length, Is.LessThanOrEqualTo(40), name);
                Assert.That(name.Any(char.IsDigit), Is.False, name);
                Assert.That(name, Does.Match("^[a-z][a-z_]*$"));
                Assert.That(parameters.Count + session.CommonParameters().Length, Is.LessThanOrEqualTo(25), name);
            }
        }

        [Test]
        public void GemsReportSourceAndItem()
        {
            session.EarnGems(10, "goal");
            session.Progress.CrewCandidates = new[] { 0, 1 };
            session.Progress.FreeHireUsed = true;
            session.HireCandidate(0);

            var earn = events.Named("earn_virtual_currency").Single();
            Assert.That(earn["value"], Is.EqualTo(10));
            Assert.That(earn["source"], Is.EqualTo("goal"));
            var spend = events.Named("spend_virtual_currency").Single();
            Assert.That(spend["item_name"], Is.EqualTo("crew_hire"));
            Assert.That(spend["value"], Is.EqualTo(session.Economy.CrewHireCost(0).Value));
            Assert.That(events.Named("crew_candidate").Single()["outcome"], Is.EqualTo("hired"));
        }

        [Test]
        public void FreeHireSpendsNothingAndSendsNoSpendEvent()
        {
            session.Progress.CrewCandidates = new[] { 0, 1 };
            Assert.That(session.HireCandidate(1), Is.True);
            Assert.That(events.Named("spend_virtual_currency"), Is.Empty);
            Assert.That(events.Named("crew_candidate").Single()["gem_cost"], Is.EqualTo(0));
        }

        [Test]
        public void PassedCandidatesAreEachReported()
        {
            session.Progress.CrewCandidates = new[] { 2, 3 };
            session.PassCandidates();
            Assert.That(events.Named("crew_candidate").Select(e => e["outcome"]), Is.EqualTo(new[] { "passed", "passed" }));
        }

        [Test]
        public void AmosFirstLevelUnlocksIdle()
        {
            session.Earn(session.AmosNextCost.Value);
            session.BuyAmosLevel();
            Assert.That(events.Named("idle_unlocked").Count, Is.EqualTo(1));
            var amos = events.Named("amos_level_up").Single();
            Assert.That(amos["level"], Is.EqualTo(1));
            Assert.That(amos["cap_h"], Is.EqualTo(0.5));
        }

        [Test]
        public void GoalCompleteCarriesItsNumber()
        {
            // The first goal counts manual catches.
            session.Progress.ManualCollected = session.CurrentGoal.Target;
            int reward = session.CurrentGoalReward;
            Assert.That(session.ClaimGoal(), Is.True);
            var goal = events.Named("goal_complete").Single();
            Assert.That(goal["goal_n"], Is.EqualTo(1));
            Assert.That(goal["gems"], Is.EqualTo(reward));
        }

        [Test]
        public void FeatureUnlockIsSentOnce()
        {
            session.Progress.ManualCollected = 1000;
            session.CheckFeatureUnlocks();
            session.CheckFeatureUnlocks();
            var unlocks = events.Named("feature_unlock").Select(e => e["feature"]).ToList();
            Assert.That(unlocks, Is.EqualTo(new[] { "upgrades" }));
        }

        [Test]
        public void IntroSkipAnnouncesNothing()
        {
            session.IntroSkipped = true;
            session.CheckFeatureUnlocks();
            Assert.That(events.Named("feature_unlock"), Is.Empty);
        }

        [Test]
        public void TutorialBeginsOnceWithTheFirstPeteLine()
        {
            session.MarkSeen(PeteLine.Welcome);
            session.MarkSeen(PeteLine.Welcome);
            session.MarkSeen(PeteLine.FirstDust);
            Assert.That(events.Named("tutorial_begin").Count, Is.EqualTo(1));
            Assert.That(events.Named("tutorial_step").Select(e => e["step_name"]), Is.EqualTo(new[] { "welcome", "first_dust" }));
        }

        [Test]
        public void StreakClaimReportsTheRescue()
        {
            session.Progress.StreakIndex = 4;
            session.Progress.LastStreakClaimDay = 10;
            session.UpdateDay(10);
            session.UpdateDay(12);
            Assert.That(events.Named("daily_streak_reset").Single()["lost_day"], Is.EqualTo(5));

            session.RescueStreak();
            session.ClaimStreak();
            var claim = events.Named("daily_streak_claim").Single();
            Assert.That(claim["day"], Is.EqualTo(5));
            Assert.That(claim["rescued"], Is.EqualTo(true));
        }

        [Test]
        public void SessionTallySplitsIncomeAndRestarts()
        {
            session.Collect(CollectibleKind.Nugget, false);
            session.Earn(500, IncomeSource.Offline);
            var end = session.EndSessionParameters(61.4).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(end["duration_s"], Is.EqualTo(61L));
            Assert.That(end["catches_n"], Is.EqualTo(1L));
            Assert.That(end["nuggets_n"], Is.EqualTo(1L));
            Assert.That((double)end["manual_log10"], Is.GreaterThan(0));
            Assert.That(end["offline_log10"], Is.EqualTo(Math.Round(Math.Log10(500), 3)));

            var next = session.EndSessionParameters(1).ToDictionary(p => p.Key, p => p.Value);
            Assert.That(next["catches_n"], Is.EqualTo(0L));
        }

        [Test]
        public void CommonParametersDescribeThePlayer()
        {
            session.EarnGems(12);
            session.BeginSession();
            var common = session.CommonParameters().ToDictionary(p => p.Key, p => p.Value);
            Assert.That(common.Keys, Is.EquivalentTo(new[] { "creek", "tier", "prestige_n", "crew_n", "amos_lvl", "guild_lvl", "gem_bal", "dollar_log10", "session_n" }));
            Assert.That(common["creek"], Is.EqualTo(1));
            Assert.That(common["gem_bal"], Is.EqualTo(12));
            Assert.That(common["session_n"], Is.EqualTo(1));
        }

        [Test]
        public void GuildLevelsAndMilestonesAreReported()
        {
            session.AddGuildXp(100000);
            var levels = events.Named("level_up");
            Assert.That(levels.Count, Is.EqualTo(session.GuildLevel));
            Assert.That(levels.All(e => (string)e["character"] == "guild"), Is.True);
            Assert.That(levels.Count(e => (bool)e["milestone"]), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void MotherLodeReportsTrigger()
        {
            session.EarnGems(50);
            MotherLodeRun run = session.SummonMotherLode();
            run.Hit();
            session.FinishMotherLode(run);
            var lode = events.Named("mother_lode").Single();
            Assert.That(lode["trigger"], Is.EqualTo("gem"));
            Assert.That(lode["taps"], Is.EqualTo(1));
            Assert.That(events.Named("spend_virtual_currency").Single()["item_name"], Is.EqualTo("mother_lode_summon"));
        }
    }
}
