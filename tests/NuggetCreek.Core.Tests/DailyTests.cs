using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class DailyTests
    {
        const double Day = 86400;
        const double Start = 1_790_000_000; // a trusted UTC second

        EconomyConfig config;
        Economy economy;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
        }

        GameSession NewSession(PlayerProgress progress = null)
        {
            var session = new GameSession(economy, progress ?? new PlayerProgress { ManualCollected = 100 }, new System.Random(1));
            session.NowUtc = Start;
            return session;
        }

        // --- Day boundary ---

        [Test]
        public void DaysRollOverAtFourInTheMorningLocalTime()
        {
            double midnightUtc = 20000 * Day;
            double istanbul = 3 * 3600;
            // 03:59 local is still yesterday, 04:00 local is today.
            Assert.That(GameSession.DayNumber(midnightUtc - istanbul + 3 * 3600 + 59 * 60, istanbul, 4), Is.EqualTo(19999));
            Assert.That(GameSession.DayNumber(midnightUtc - istanbul + 4 * 3600, istanbul, 4), Is.EqualTo(20000));
        }

        [Test]
        public void NothingDailyBeforeTheFirstTrustedDay()
        {
            GameSession session = NewSession();
            Assert.That(session.CanClaimStreak, Is.False);
            Assert.That(session.CanWashFree, Is.False);
            Assert.That(session.UpdateDay(100), Is.True);
            Assert.That(session.UpdateDay(100), Is.False);
            Assert.That(session.CanClaimStreak, Is.True);
        }

        // --- Streak ---

        [Test]
        public void CalendarPaysThirtyDaysThenStartsOver()
        {
            GameSession session = NewSession();
            int gems = 0;
            for (int day = 0; day < 30; day++)
            {
                session.UpdateDay(100 + day);
                StreakReward reward = session.ClaimStreak().Value;
                Assert.That(reward.Day, Is.EqualTo(day + 1));
                gems += reward.Gems;
                Assert.That(session.CanClaimStreak, Is.False, "one claim a day");
            }
            Assert.That(gems, Is.EqualTo(305));
            Assert.That(session.StreakDay, Is.EqualTo(1));
            Assert.That(config.StreakBoxes[14], Is.EqualTo(0), "day 15 Green Box");
            Assert.That(config.StreakBoxes[29], Is.EqualTo(1), "day 30 Orange Box");
        }

        [Test]
        public void StreakRewardsPayIncomeChestsAndBoxes()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, StreakIndex = 1 });
            session.UpdateDay(10);
            BigNumber expected = session.IncomePerSecond * (15 * 60);
            StreakReward day2 = session.ClaimStreak().Value;
            Assert.That(day2.Dollars.ToDouble(), Is.EqualTo(expected.ToDouble()).Within(1e-9));

            session.UpdateDay(11);
            session.ClaimStreak();
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(1), "day 3 chest");

            session.Progress.StreakIndex = 14;
            session.UpdateDay(12);
            session.ClaimStreak();
            Assert.That(session.Progress.GearLevels.Sum(), Is.EqualTo(3), "Green Box: 3 cards");
        }

        [Test]
        public void OneMissedDayCanBeRescuedWithAnAd()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, StreakIndex = 6, LastStreakClaimDay = 10, CurrentDay = 10 });
            session.UpdateDay(12);
            Assert.That(session.StreakDay, Is.EqualTo(1), "the streak broke");
            Assert.That(session.CanRescueStreak, Is.True);
            Assert.That(session.RescueStreakDay, Is.EqualTo(7));

            Assert.That(session.RescueStreak(), Is.True);
            Assert.That(session.StreakDay, Is.EqualTo(7));
            Assert.That(session.ClaimStreak().Value.Day, Is.EqualTo(7));
        }

        [Test]
        public void TwoMissedDaysStartOverWithoutRescue()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, StreakIndex = 6, LastStreakClaimDay = 10, CurrentDay = 10 });
            session.UpdateDay(13);
            Assert.That(session.StreakDay, Is.EqualTo(1));
            Assert.That(session.CanRescueStreak, Is.False);
        }

        [Test]
        public void ClaimingWithoutTheAdGivesUpTheRescue()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, StreakIndex = 6, LastStreakClaimDay = 10, CurrentDay = 10 });
            session.UpdateDay(12);
            Assert.That(session.ClaimStreak().Value.Day, Is.EqualTo(1));
            Assert.That(session.RescueStreak(), Is.False);
        }

        // --- Jobs ---

        [Test]
        public void ThreeDifferentJobsPerDaySameForTheSameDay()
        {
            GameSession a = NewSession();
            GameSession b = NewSession();
            a.UpdateDay(500);
            b.UpdateDay(500);
            var kinds = Enumerable.Range(0, 3).Select(a.JobKind).ToArray();
            Assert.That(kinds.Distinct().Count(), Is.EqualTo(3));
            Assert.That(Enumerable.Range(0, 3).Select(b.JobKind), Is.EqualTo(kinds));
        }

        [Test]
        public void JobsCountActionsAndPayTenGemsOnce()
        {
            GameSession session = NewSession();
            session.UpdateDay(1);
            session.Progress.JobKinds = new[] { (int)DailyJobKind.ManualCatches, (int)DailyJobKind.Nuggets, (int)DailyJobKind.Washes };

            for (int i = 0; i < 299; i++)
                session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.JobDone(0), Is.False);
            session.Collect(CollectibleKind.RichNugget, false);
            Assert.That(session.JobDone(0), Is.True);
            Assert.That(session.JobProgress(1), Is.EqualTo(1));

            Assert.That(session.ClaimJob(0), Is.True);
            Assert.That(session.ClaimJob(0), Is.False);
            Assert.That(session.Progress.Gems, Is.EqualTo(10));

            session.Wash(false);
            Assert.That(session.JobDone(2), Is.True);
        }

        [Test]
        public void NewDayResetsJobsAndWashes()
        {
            GameSession session = NewSession();
            session.UpdateDay(1);
            session.Progress.JobProgress[0] = 999;
            session.Wash(false);
            Assert.That(session.CanWashFree, Is.False);

            session.UpdateDay(2);
            Assert.That(session.Progress.JobProgress[0], Is.EqualTo(0));
            Assert.That(session.CanWashFree, Is.True);
            Assert.That(session.WashesLeftToday, Is.EqualTo(5));
        }

        // --- Daily Wash ---

        [Test]
        public void OneFreeWashThenFourAfterAds()
        {
            GameSession session = NewSession();
            session.UpdateDay(1);
            Assert.That(session.Wash(true), Is.EqualTo(-1), "the free one comes first");
            Assert.That(session.Wash(false), Is.GreaterThanOrEqualTo(0));
            for (int i = 0; i < 4; i++)
                Assert.That(session.Wash(true), Is.GreaterThanOrEqualTo(0));
            Assert.That(session.CanWashWithAd, Is.False);
            Assert.That(session.WashesLeftToday, Is.EqualTo(0));
        }

        [Test]
        public void WashOddsFollowTheListedTable()
        {
            Assert.That(config.WashChances.Sum(), Is.EqualTo(1).Within(1e-12));
            GameSession session = NewSession();
            Assert.That(session.RollWash(0.24), Is.EqualTo(0));
            Assert.That(session.RollWash(0.26), Is.EqualTo(1));
            Assert.That(session.RollWash(0.985), Is.EqualTo(7));
        }

        [Test]
        public void StrongerBoostWinsAndTheSameOneAddsTime()
        {
            GameSession session = NewSession();
            session.ApplyBoost(2, 1800);
            Assert.That(session.BoostMultiplier, Is.EqualTo(2));
            session.ApplyBoost(2, 1800);
            Assert.That(session.BoostSecondsLeft, Is.EqualTo(3600).Within(1e-6));
            session.ApplyBoost(3, 600);
            Assert.That(session.BoostMultiplier, Is.EqualTo(3));
            Assert.That(session.BoostSecondsLeft, Is.EqualTo(600).Within(1e-6));
            session.ApplyBoost(2, 7200);
            Assert.That(session.BoostMultiplier, Is.EqualTo(3), "a weaker boost does not replace a stronger one");

            session.NowUtc = Start + 601;
            Assert.That(session.BoostMultiplier, Is.EqualTo(1));
        }

        [Test]
        public void BoostMultipliesSwipeAndIdleIncome()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, AmosLevel = 1 });
            BigNumber catchBefore = session.CatchValue(CollectibleKind.GoldDust, false);
            BigNumber idleBefore = session.IdleRate;
            session.ApplyBoost(3, 3600);
            Assert.That(session.CatchValue(CollectibleKind.GoldDust, false).ToDouble(), Is.EqualTo(catchBefore.ToDouble() * 3).Within(1e-9));
            Assert.That(session.IdleRate.ToDouble(), Is.EqualTo(idleBefore.ToDouble() * 3).Within(1e-9));
        }

        [Test]
        public void OfflinePaysTheBoostForTheCoveredPart()
        {
            var progress = new PlayerProgress { ManualCollected = 100, AmosLevel = 3 };
            GameSession session = NewSession(progress);
            session.ApplyBoost(2, 3600); // ends at Start + 1 h
            BigNumber rate = session.OfflineRate;

            var clock = new OfflineClockInput
            {
                LastSeenUtc = Start, TrustedNowUtc = Start + 2 * 3600,
                LastDeviceUtc = Start, DeviceNowUtc = Start + 2 * 3600,
                LastMonotonicSeconds = 0, MonotonicNowSeconds = 2 * 3600, SameBoot = true,
            };
            OfflineResult result = session.EvaluateOffline(clock);
            // 2 h away: 2 h at the base rate plus 1 h of boost on top.
            Assert.That(result.Amount.ToDouble(), Is.EqualTo(rate.ToDouble() * 3 * 3600).Within(rate.ToDouble() * 1e-6));
        }
    }
}
