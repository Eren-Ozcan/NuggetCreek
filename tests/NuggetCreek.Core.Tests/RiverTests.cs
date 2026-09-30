using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    /// <summary>River floaters and the tray (design doc 3.1.4).</summary>
    public class RiverTests
    {
        EconomyConfig config;
        Economy economy;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
        }

        GameSession NewSession(PlayerProgress progress = null, int seed = 1) =>
            new GameSession(economy, progress ?? new PlayerProgress(), new System.Random(seed));

        /// <summary>Past the tutorial chest, on a trusted game day.</summary>
        GameSession PastTutorial(int seed = 1)
        {
            GameSession session = NewSession(new PlayerProgress { ChestsOpened = 1, AmosLevel = 1, ManualCollected = 100 }, seed);
            session.UpdateDay(100);
            return session;
        }

        static void Catch(GameSession session, int times)
        {
            for (int i = 0; i < times; i++)
                session.Collect(CollectibleKind.GoldDust, false);
        }

        /// <summary>Plays in one-second steps until a floater goes on the water; returns it and the seconds played.</summary>
        static (FloaterKind? Kind, double Seconds) PlayUntilFloater(GameSession session, double limitSeconds)
        {
            for (double t = 1; t <= limitSeconds; t++)
            {
                session.TickPlay(1);
                FloaterKind? kind = session.LaunchFloater();
                if (kind.HasValue)
                    return (kind, t);
            }
            return (null, limitSeconds);
        }

        [Test]
        public void NothingFloatsBeforeTheFirstChest()
        {
            GameSession session = NewSession();
            session.UpdateDay(100);
            Assert.That(PlayUntilFloater(session, 30 * 60).Kind, Is.Null);
        }

        [Test]
        public void ACrateFloatsInEvery110To160SecondsOfPlay()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                GameSession session = PastTutorial(seed);
                (FloaterKind? kind, double seconds) = PlayUntilFloater(session, 200);
                Assert.That(kind, Is.EqualTo(FloaterKind.Crate));
                Assert.That(seconds, Is.InRange(110, 161));
                Assert.That(session.FloaterOnWater, Is.EqualTo(FloaterKind.Crate));
            }
        }

        [Test]
        public void OneFloaterAtATimeWithAGapAfterIt()
        {
            GameSession session = PastTutorial();
            PlayUntilFloater(session, 200);
            session.Progress.ChestsDue = 1;
            Assert.That(session.LaunchFloater(), Is.Null, "the crate is still on the water");

            Assert.That(session.CatchFloater(), Is.True);
            Assert.That(session.LaunchFloater(), Is.Null, "the gap after a floater");
            session.TickPlay(4);
            Assert.That(session.LaunchFloater(), Is.Null);
            session.TickPlay(1);
            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
        }

        [Test]
        public void ACaughtCratePaysTwentySecondsOfIncomeAndDoublesOnce()
        {
            GameSession session = PastTutorial();
            PlayUntilFloater(session, 200);
            session.CatchFloater();
            Assert.That(session.Progress.CratesWaiting, Is.EqualTo(1));
            Assert.That(session.TrayCount, Is.EqualTo(1));

            BigNumber before = session.Progress.Dollars;
            BigNumber expected = session.IncomePerSecond * 20;
            CrateReward reward = session.ClaimCrate();
            Assert.That(reward.Dollars.ToDouble(), Is.EqualTo(expected.ToDouble()).Within(expected.ToDouble() * 1e-9));
            Assert.That(session.DoubleCrate(reward), Is.True);
            Assert.That(session.DoubleCrate(reward), Is.False);
            Assert.That((session.Progress.Dollars - before).ToDouble(), Is.EqualTo(2 * expected.ToDouble()).Within(expected.ToDouble() * 1e-9));
            Assert.That(session.Progress.CratesWaiting, Is.EqualTo(0));
            Assert.That(session.ClaimCrate(), Is.Null);
        }

        [Test]
        public void DocRaisesCratesLikeChests()
        {
            var progress = new PlayerProgress { ChestsOpened = 1, AmosLevel = 1, ManualCollected = 100, CratesWaiting = 1 };
            progress.CrewLevels[5] = 2; // Doc: +30% chest value per level
            GameSession session = NewSession(progress);
            BigNumber expected = session.IncomePerSecond * (20 * 1.6);
            Assert.That(session.ClaimCrate().Dollars.ToDouble(), Is.EqualTo(expected.ToDouble()).Within(expected.ToDouble() * 1e-9));
        }

        [Test]
        public void AMissedCrateIsLost()
        {
            GameSession session = PastTutorial();
            PlayUntilFloater(session, 200);
            Assert.That(session.MissFloater(), Is.True);
            Assert.That(session.FloaterOnWater, Is.Null);
            Assert.That(session.TrayCount, Is.EqualTo(0));
            Assert.That(session.Tally.FloatersMissed, Is.EqualTo(1));
        }

        [Test]
        public void EarnedChestsFloatFirstAndAMissedOneIsLost()
        {
            GameSession session = PastTutorial();
            Catch(session, 250 * 2);
            Assert.That(session.Progress.ChestsDue, Is.EqualTo(2));
            Assert.That(session.HasChest, Is.False);

            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
            session.MissFloater();
            Assert.That(session.Progress.ChestsDue, Is.EqualTo(1), "lost");

            session.TickPlay(5);
            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
            session.CatchFloater();
            Assert.That(session.Progress.ChestsDue, Is.EqualTo(0));
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(1));
        }

        [Test]
        public void TheTutorialChestFloatsAgainUntilCaught()
        {
            GameSession session = NewSession();
            Catch(session, 150);
            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
            session.MissFloater();
            Assert.That(session.Progress.ChestsDue, Is.EqualTo(1), "the first chest comes back");

            session.TickPlay(5);
            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
            session.CatchFloater();
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(1));
        }

        [Test]
        public void PeteCallsOutTheFirstChestWhileItFloats()
        {
            GameSession session = NewSession(new PlayerProgress { PeteSeen = ~(1 << (int)PeteLine.FirstChest) });
            Assert.That(session.NextPeteLine(), Is.Null);
            session.Progress.ChestsDue = 1;
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.FirstChest));
        }

        [Test]
        public void AGemPouchComesEverySixToEightMinutesAtMostFourADay()
        {
            GameSession session = PastTutorial();
            int pouches = 0;
            double firstAt = -1;
            for (double t = 1; t <= 3 * 3600; t++)
            {
                session.TickPlay(1);
                FloaterKind? kind = session.LaunchFloater();
                if (kind == null)
                    continue;
                if (kind == FloaterKind.Pouch)
                {
                    pouches++;
                    if (firstAt < 0)
                        firstAt = t;
                }
                session.CatchFloater();
            }
            Assert.That(firstAt, Is.InRange(360, 490), "a waiting crate can hold it a few seconds");
            Assert.That(pouches, Is.EqualTo(4));
            Assert.That(session.Progress.PouchesWaiting, Is.EqualTo(4));

            session.UpdateDay(101);
            Assert.That(session.Progress.PouchesToday, Is.EqualTo(0));
            Assert.That(PlayUntilPouch(session, 600), Is.True, "a new day brings more");
        }

        static bool PlayUntilPouch(GameSession session, double limitSeconds)
        {
            for (double t = 1; t <= limitSeconds; t++)
            {
                session.TickPlay(1);
                FloaterKind? kind = session.LaunchFloater();
                if (kind == FloaterKind.Pouch)
                    return true;
                if (kind.HasValue)
                    session.CatchFloater();
            }
            return false;
        }

        [Test]
        public void NoPouchWithoutATrustedDay()
        {
            GameSession session = NewSession(new PlayerProgress { ChestsOpened = 1 });
            Assert.That(PlayUntilPouch(session, 1200), Is.False);
        }

        [Test]
        public void APouchOpensForOneToSixGemsOrIsThrownAway()
        {
            var seen = new bool[7];
            for (int seed = 1; seed <= 200; seed++)
            {
                GameSession session = NewSession(new PlayerProgress { PouchesWaiting = 1 }, seed);
                int gems = session.OpenPouch();
                Assert.That(gems, Is.InRange(1, 6));
                Assert.That(session.Progress.Gems, Is.EqualTo(gems));
                Assert.That(session.Progress.PouchesWaiting, Is.EqualTo(0));
                seen[gems] = true;
            }
            for (int gems = 1; gems <= 6; gems++)
                Assert.That(seen[gems], Is.True, $"{gems} Gems");

            GameSession other = NewSession(new PlayerProgress { PouchesWaiting = 1 });
            Assert.That(other.DiscardPouch(), Is.True);
            Assert.That(other.OpenPouch(), Is.EqualTo(0));
            Assert.That(other.Progress.Gems, Is.EqualTo(0));
        }

        [Test]
        public void TimersCountOnlyWhileTheCreekRuns()
        {
            GameSession session = PastTutorial();
            session.TickPlay(1);
            double left = session.Progress.CrateSecondsLeft;
            session.TickIdle(600);
            Assert.That(session.Progress.CrateSecondsLeft, Is.EqualTo(left), "idle and offline time do not count");
            session.TickPlay(10);
            Assert.That(session.Progress.CrateSecondsLeft, Is.EqualTo(left - 10).Within(1e-9));
        }

        [Test]
        public void ATamperedTimerIsClampedToTheLongestWait()
        {
            GameSession session = NewSession(new PlayerProgress { ChestsOpened = 1, CrateSecondsLeft = 1e9, PouchSecondsLeft = 1e9 });
            Assert.That(session.Progress.CrateSecondsLeft, Is.EqualTo(160));
            Assert.That(session.Progress.PouchSecondsLeft, Is.EqualTo(480));
        }
    }
}
