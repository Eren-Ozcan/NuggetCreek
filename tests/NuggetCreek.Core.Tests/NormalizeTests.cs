using System;
using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    /// <summary>
    /// A save that is signed but out of range (a bad migration, or a build with more content
    /// than this one) must be pulled back into range, never index past a table.
    /// </summary>
    public class NormalizeTests
    {
        [Test]
        public void NegativeAmountsAndLevelsBecomeZero()
        {
            var p = new PlayerProgress
            {
                Dollars = BigNumber.FromDouble(-500),
                TotalEarned = BigNumber.FromDouble(-1),
                TierIndex = -2,
                AmosLevel = -1,
                ManualCollected = -10,
                ProspectingXp = -3,
                UpgradeLevels = Enumerable.Repeat(-4, GameCatalog.Upgrades.Count).ToArray(),
                CrewLevels = Enumerable.Repeat(-1, GameCatalog.Crew.Count).ToArray(),
                JobProgress = new[] { -5, 2, -1 },
            };
            p.Normalize();

            Assert.That(p.Dollars, Is.EqualTo(BigNumber.Zero));
            Assert.That(p.TotalEarned, Is.EqualTo(BigNumber.Zero));
            Assert.That(p.TierIndex, Is.Zero);
            Assert.That(p.AmosLevel, Is.Zero);
            Assert.That(p.ManualCollected, Is.Zero);
            Assert.That(p.ProspectingXp, Is.Zero);
            Assert.That(p.UpgradeLevels, Is.All.EqualTo(0));
            Assert.That(p.CrewLevels, Is.All.EqualTo(0));
            Assert.That(p.JobProgress, Is.EqualTo(new[] { 0, 2, 0 }));
        }

        [Test]
        public void CreeksBeyondTheCatalogAreCapped()
        {
            var p = new PlayerProgress { RegionsUnlocked = 999, RegionIndex = 998, BestRegionsUnlocked = 999 };
            p.Normalize();

            int count = GameCatalog.RegionNames.Count;
            Assert.That(p.RegionsUnlocked, Is.EqualTo(count));
            Assert.That(p.RegionIndex, Is.EqualTo(count - 1));
            Assert.That(p.BestRegionsUnlocked, Is.EqualTo(count));
        }

        [Test]
        public void CrewAboveTheMaxLevelIsCapped()
        {
            var p = new PlayerProgress();
            p.CrewLevels[3] = 999;
            p.Normalize();
            Assert.That(p.CrewLevels[3], Is.EqualTo(GameCatalog.CrewMaxLevel));
        }

        [Test]
        public void UnknownDailyJobKindsResetTheJobs()
        {
            var p = new PlayerProgress { JobKinds = new[] { 1, 99, 2 } };
            p.Normalize();
            Assert.That(p.JobKinds, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void DailyJobKindCountMatchesTheEnumAndTheTargets()
        {
            Assert.That(PlayerProgress.DailyJobKinds, Is.EqualTo(Enum.GetValues(typeof(DailyJobKind)).Length));
            Assert.That(PlayerProgress.DailyJobKinds, Is.EqualTo(new EconomyConfig().DailyJobTargets.Length));
        }

        [Test]
        public void SessionCapsWhatDependsOnTheConfig()
        {
            var config = new EconomyConfig();
            var p = new PlayerProgress { TierIndex = 999, AmosLevel = 999, GuildLevel = 9999 };
            var session = new GameSession(new Economy(config), p);

            Assert.That(session.Progress.TierIndex, Is.EqualTo(config.TierCount - 1));
            Assert.That(session.Progress.AmosLevel, Is.EqualTo(config.AmosMaxLevel));
            Assert.That(session.Progress.GuildLevel, Is.EqualTo(config.GuildMaxLevel));
            Assert.DoesNotThrow(() => _ = session.RegionName);
            Assert.DoesNotThrow(() => _ = session.IdleRate);
        }

        [Test]
        public void RemoteConfigWithFewerCreeksCapsTheSave()
        {
            var config = new EconomyConfig();
            config.RegionUnlockCosts = config.RegionUnlockCosts.Take(5).ToArray();
            var p = new PlayerProgress { RegionsUnlocked = 12, RegionIndex = 11 };
            var session = new GameSession(new Economy(config), p);

            Assert.That(session.Progress.RegionsUnlocked, Is.EqualTo(5));
            Assert.That(session.Progress.RegionIndex, Is.EqualTo(4));
            Assert.DoesNotThrow(() => _ = session.IdleRate);
        }
    }
}
