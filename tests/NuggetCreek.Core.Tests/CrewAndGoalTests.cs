using System;
using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class CrewAndGoalTests
    {
        EconomyConfig config;
        Economy economy;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
        }

        GameSession NewSession(int gems = 0, PlayerProgress progress = null)
        {
            var session = new GameSession(economy, progress ?? new PlayerProgress(), new Random(7));
            session.EarnGems(gems);
            return session;
        }

        static int CrewIndex(string id)
        {
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                if (GameCatalog.Crew[i].Id == id)
                    return i;
            throw new ArgumentException(id);
        }

        /// <summary>Unlocks creeks until the given count, paying for each.</summary>
        static void UnlockRegions(GameSession session, int regionsUnlocked)
        {
            while (session.Progress.RegionsUnlocked < regionsUnlocked)
            {
                session.Earn(session.NextRegionCost.Value);
                Assert.That(session.UnlockNextRegion(), Is.True);
            }
        }

        // --- Candidates ---

        [Test]
        public void PineHollowBringsNoCandidates()
        {
            GameSession session = NewSession();
            UnlockRegions(session, 2);
            Assert.That(session.HasCandidates, Is.False);
        }

        [Test]
        public void SilverForkOffersTwoDistinctUnhiredCandidates()
        {
            GameSession session = NewSession();
            UnlockRegions(session, 4);
            Assert.That(session.HasCandidates, Is.False, "Bear Falls brings none");
            UnlockRegions(session, 5);

            Assert.That(session.Candidates.Count, Is.EqualTo(2));
            Assert.That(session.Candidates.Distinct().Count(), Is.EqualTo(2));
            Assert.That(session.Progress.CandidateSecondsLeft, Is.EqualTo(600));
        }

        [Test]
        public void CandidatesSkipTheCreeksBetweenOldPoints()
        {
            GameSession session = NewSession();
            UnlockRegions(session, 5);
            session.PassCandidates();
            UnlockRegions(session, 6);
            Assert.That(session.HasCandidates, Is.False, "Copper Bluff (creek 6)");
            UnlockRegions(session, 7);
            Assert.That(session.HasCandidates, Is.True, "Red Gulch (creek 7)");
        }

        [Test]
        public void CandidatesAreDrawnOnlyFromTheUnhiredPool()
        {
            var progress = new PlayerProgress();
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                progress.CrewLevels[i] = 1;
            int ezra = CrewIndex("ezra");
            progress.CrewLevels[ezra] = 0;
            GameSession session = NewSession(progress: progress);

            session.OfferCandidates();

            Assert.That(session.Candidates, Is.EqualTo(new[] { ezra }));
        }

        [Test]
        public void EveryoneHiredOpensNoEvent()
        {
            var progress = new PlayerProgress();
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                progress.CrewLevels[i] = 1;
            GameSession session = NewSession(progress: progress);

            session.OfferCandidates();

            Assert.That(session.HasCandidates, Is.False);
            Assert.That(session.CrewHireCost, Is.Null);
        }

        [Test]
        public void HiringCostsGemsAndClosesTheEvent()
        {
            GameSession session = NewSession(gems: 25, progress: new PlayerProgress { FreeHireUsed = true });
            session.OfferCandidates();
            int chosen = session.Candidates[0];

            Assert.That(session.CrewHireCost, Is.EqualTo(10));
            Assert.That(session.HireCandidate(chosen), Is.True);

            Assert.That(session.CrewLevel(chosen), Is.EqualTo(1));
            Assert.That(session.Progress.Gems, Is.EqualTo(15));
            Assert.That(session.HasCandidates, Is.False);
            Assert.That(session.CrewHireCost, Is.EqualTo(15), "second hire follows the 10, 15, 20 ladder");
        }

        [Test]
        public void HiringNeedsEnoughGemsAndAnOfferedCandidate()
        {
            GameSession session = NewSession(gems: 9, progress: new PlayerProgress { FreeHireUsed = true });
            session.OfferCandidates();
            int offered = session.Candidates[0];
            int notOffered = Enumerable.Range(0, GameCatalog.Crew.Count).First(i => !session.Candidates.Contains(i));

            Assert.That(session.HireCandidate(offered), Is.False, "9 Gems < 10");
            session.EarnGems(100);
            Assert.That(session.HireCandidate(notOffered), Is.False);
            Assert.That(session.HasCandidates, Is.True);
        }

        [Test]
        public void WindowExpiresAfterTenMinutesOfPlay()
        {
            GameSession session = NewSession();
            session.OfferCandidates();

            Assert.That(session.TickCandidates(599), Is.False);
            Assert.That(session.HasCandidates, Is.True);
            Assert.That(session.TickCandidates(1), Is.True);
            Assert.That(session.HasCandidates, Is.False);
        }

        [Test]
        public void PassingReturnsBothToThePool()
        {
            GameSession session = NewSession(gems: 100);
            session.OfferCandidates();
            session.PassCandidates();

            Assert.That(session.HasCandidates, Is.False);
            Assert.That(session.CrewHiredCount, Is.Zero);
        }

        // --- Crew effects and levels ---

        [Test]
        public void HiredCrewMovesItsStat()
        {
            var progress = new PlayerProgress();
            GameSession session = NewSession(progress: progress);
            BigNumber before = session.Economy.IdleRate(0, 0, session.Stats, 1);

            progress.CrewLevels[CrewIndex("ezra")] = 10;
            session.RebuildStats();

            Assert.That(session.Stats[Stat.IdleSpeed], Is.EqualTo(0.8).Within(1e-12));
            Assert.That(session.Economy.IdleRate(0, 0, session.Stats, 1).ToDouble(),
                Is.EqualTo(before.ToDouble() * 1.8).Within(before.ToDouble() * 1e-12));
        }

        [Test]
        public void LevelUpFollowsTheGemTableUpToTen()
        {
            var progress = new PlayerProgress();
            int rosa = CrewIndex("rosa");
            progress.CrewLevels[rosa] = 1;
            GameSession session = NewSession(gems: 371, progress: progress);

            for (int level = 1; level < GameCatalog.CrewMaxLevel; level++)
                Assert.That(session.LevelUpCrew(rosa), Is.True, $"L{level} to L{level + 1}");

            Assert.That(session.CrewLevel(rosa), Is.EqualTo(10));
            Assert.That(session.Progress.Gems, Is.Zero, "L1 to L10 costs 371 Gems (design doc 6.3)");
            Assert.That(session.CrewLevelUpCost(rosa), Is.Null);
            Assert.That(session.LevelUpCrew(rosa), Is.False);
            Assert.That(session.CollectibleLifetimeSeconds, Is.EqualTo(config.CollectibleLifetimeSeconds + 1.5).Within(1e-9));
        }

        [Test]
        public void UnhiredCrewCannotLevelUp()
        {
            GameSession session = NewSession(gems: 100);
            Assert.That(session.CrewLevelUpCost(0), Is.Null);
            Assert.That(session.LevelUpCrew(0), Is.False);
        }

        // --- Goals ---

        [Test]
        public void EveryGoalHasAReward()
        {
            Assert.That(config.GoalGemRewards.Length, Is.EqualTo(GameCatalog.Goals.Count));
        }

        [Test]
        public void FirstGoalCountsManualCatchesAndPaysTenGems()
        {
            GameSession session = NewSession();
            Assert.That(session.CurrentGoal.Kind, Is.EqualTo(GoalKind.ManualCollected));
            Assert.That(session.CurrentGoal.Target, Is.EqualTo(15));

            session.Earn(1e6);
            session.TickIdle(3600);
            Assert.That(session.IsCurrentGoalComplete, Is.False, "idle income does not count");

            for (int i = 0; i < 15; i++)
                session.Collect(CollectibleKind.GoldDust, false);

            Assert.That(session.ClaimGoal(), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(10));
            Assert.That(session.Progress.GoalIndex, Is.EqualTo(1));
            Assert.That(session.ClaimGoal(), Is.False, "the next goal is not done yet");
        }

        [Test]
        public void GoalAlreadyMetWhenItBecomesActiveCanBeClaimedAtOnce()
        {
            var progress = new PlayerProgress { ManualCollected = 15, AmosLevel = 1, GoalIndex = 2 };
            GameSession session = NewSession(progress: progress);

            Assert.That(session.CurrentGoal.Kind, Is.EqualTo(GoalKind.AmosLevel));
            Assert.That(session.ClaimGoal(), Is.True);
        }

        [Test]
        public void TheChainRunsToTheLastCreekTierAndCrewMember()
        {
            int Max(GoalKind kind) => GameCatalog.Goals.Where(g => g.Kind == kind).Max(g => g.Target);
            Assert.That(Max(GoalKind.RegionsUnlocked), Is.EqualTo(config.RegionCount));
            Assert.That(Max(GoalKind.SluiceTier), Is.EqualTo(config.TierCount));
            Assert.That(Max(GoalKind.CrewHired), Is.EqualTo(GameCatalog.Crew.Count));
            foreach (GoalKind kind in new[] { GoalKind.ChestsOpened, GoalKind.Stars, GoalKind.Claims, GoalKind.BossesBeaten })
                Assert.That(GameCatalog.Goals.Any(g => g.Kind == kind), kind.ToString());
            Assert.That(Max(GoalKind.Stars), Is.LessThanOrEqualTo(GameCatalog.Nuggets.Count * 3));
        }

        [Test]
        public void EachKindOnlyClimbsAlongTheChain()
        {
            // A target at or below an earlier one of the same kind would pay twice for one step.
            var last = new System.Collections.Generic.Dictionary<GoalKind, int>();
            foreach (GoalDefinition goal in GameCatalog.Goals)
            {
                if (last.TryGetValue(goal.Kind, out int before))
                    Assert.That(goal.Target, Is.GreaterThan(before), goal.Kind.ToString());
                last[goal.Kind] = goal.Target;
            }
        }

        [Test]
        public void SystemGoalsCountChestsStarsClaimsAndBosses()
        {
            var progress = new PlayerProgress { ChestsOpened = 4, Rebirths = 2 };
            progress.NuggetCatches[GameCatalog.BossNugget(0)] = 1;
            progress.NuggetCatches[GameCatalog.BossNugget(1)] = 1;
            GameSession session = NewSession(progress: progress);
            Assert.That(session.GoalProgress(new GoalDefinition(GoalKind.ChestsOpened, 3)), Is.EqualTo(4));
            Assert.That(session.GoalProgress(new GoalDefinition(GoalKind.Claims, 1)), Is.EqualTo(2));
            Assert.That(session.GoalProgress(new GoalDefinition(GoalKind.BossesBeaten, 2)), Is.EqualTo(2));
            Assert.That(session.GoalProgress(new GoalDefinition(GoalKind.Stars, 15)), Is.EqualTo(session.TotalStars));
        }

        [Test]
        public void AfterTheChainEndlessGoalsCountFromTheirStart()
        {
            var progress = new PlayerProgress { GoalIndex = GameCatalog.Goals.Count, ManualCollected = 50_000, ChestsOpened = 40 };
            GameSession session = NewSession(progress: progress);

            GoalDefinition first = session.CurrentGoal;
            Assert.That(first.Kind, Is.EqualTo(GoalKind.ManualCollected));
            Assert.That(first.Relative, Is.True);
            Assert.That(session.GoalProgress(first), Is.Zero, "the 50,000 caught before do not count");
            Assert.That(session.CurrentGoalReward, Is.EqualTo(config.GoalEndlessGems));

            progress.ManualCollected += first.Target;
            Assert.That(session.ClaimGoal(), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(config.GoalEndlessGems));

            Assert.That(session.CurrentGoal.Kind, Is.EqualTo(GoalKind.ChestsOpened));
            Assert.That(session.Progress.GoalBase, Is.EqualTo(40));
            progress.ChestsOpened += session.CurrentGoal.Target;
            Assert.That(session.ClaimGoal(), Is.True);
            Assert.That(session.CurrentGoal.Kind, Is.EqualTo(GoalKind.MotherLodes));

            progress.MotherLodesFinished += session.CurrentGoal.Target;
            Assert.That(session.ClaimGoal(), Is.True);
            Assert.That(session.CurrentGoal.Kind, Is.EqualTo(GoalKind.ManualCollected), "round again");
            Assert.That(session.IsCurrentGoalComplete, Is.False);
        }

        [Test]
        public void GoalChainStaysWithinTheGemBudget()
        {
            // Design doc 5.0.6 budgets 8 Gems a day from progress goals. The goals the target
            // player reaches in the first month (through Echo Gorge, day 28) pay 5-8 a day;
            // the long tail after it pays far less a day.
            int firstMonth = GameCatalog.Goals.Select((g, i) => (g, i))
                .TakeWhile(p => !(p.g.Kind == GoalKind.RegionsUnlocked && p.g.Target > 12)).Sum(p => config.GoalGemRewards[p.i]);
            Assert.That(firstMonth, Is.InRange(150, 240));
            int total = config.GoalGemRewards.Sum();
            Assert.That(total - firstMonth, Is.LessThan(160 * 3), "under 3 a day from day 30 to 190");
        }

        // --- Save shape ---

        [Test]
        public void NormalizeRepairsOldSaves()
        {
            var progress = new PlayerProgress
            {
                CrewLevels = null,
                CrewCandidates = new[] { 0, 99, -1 },
                CandidateSecondsLeft = 100,
                GoalIndex = 1000,
                Gems = -5,
            };
            progress.Normalize();

            Assert.That(progress.CrewLevels.Length, Is.EqualTo(GameCatalog.Crew.Count));
            Assert.That(progress.CrewCandidates, Is.EqualTo(new[] { 0 }));
            Assert.That(progress.GoalIndex, Is.EqualTo(1000), "endless goals go past the chain");
            Assert.That(progress.Gems, Is.Zero);
        }

        [Test]
        public void NormalizeDropsCandidatesThatAreAlreadyHired()
        {
            var progress = new PlayerProgress { CrewCandidates = new[] { 3 }, CandidateSecondsLeft = 100 };
            progress.CrewLevels[3] = 2;
            progress.Normalize();

            Assert.That(progress.CrewCandidates, Is.Empty);
            Assert.That(progress.CandidateSecondsLeft, Is.Zero);
        }
    }
}
