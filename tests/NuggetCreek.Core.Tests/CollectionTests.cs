using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class CollectionTests
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

        static int IndexOf(string id) =>
            GameCatalog.Nuggets.Select((n, i) => (n, i)).First(p => p.n.Id == id).i;

        // --- Catalog ---

        [Test]
        public void EachCreekHasACommonAndABossPlusSixGlobalRares()
        {
            Assert.That(GameCatalog.Nuggets.Count, Is.EqualTo(46));
            for (int region = 0; region < config.RegionCount; region++)
            {
                NuggetDefinition common = GameCatalog.Nuggets[GameCatalog.CommonNugget(region)];
                NuggetDefinition boss = GameCatalog.Nuggets[GameCatalog.BossNugget(region)];
                Assert.That(common.Rarity, Is.EqualTo(Rarity.Common), "creek " + region);
                Assert.That(common.RegionIndex, Is.EqualTo(region));
                Assert.That(boss.Rarity, Is.EqualTo(Rarity.Legendary), "creek " + region);
                Assert.That(boss.RegionIndex, Is.EqualTo(region));
            }
            for (int i = GameCatalog.FirstRareNugget; i < GameCatalog.Nuggets.Count; i++)
            {
                Assert.That(GameCatalog.Nuggets[i].Rarity, Is.EqualTo(Rarity.Rare));
                Assert.That(GameCatalog.Nuggets[i].RegionIndex, Is.EqualTo(GameCatalog.GlobalRegion));
            }
            Assert.That(GameCatalog.RareNuggetCount, Is.EqualTo(6));
        }

        [Test]
        public void IdsAndNamesAreUnique()
        {
            Assert.That(GameCatalog.Nuggets.Select(n => n.Id).Distinct().Count(), Is.EqualTo(46));
            Assert.That(GameCatalog.Nuggets.Select(n => n.Name).Distinct().Count(), Is.EqualTo(46));
        }

        [Test]
        public void RollWeightsAddUpToOne()
        {
            Assert.That(config.NuggetOwnWeight + config.NuggetPreviousWeight + config.NuggetRareWeight, Is.EqualTo(1).Within(1e-12));
        }

        // --- Rolls ---

        [Test]
        public void RollPicksOwnCommonPreviousCommonOrARare()
        {
            GameSession session = NewSession(new PlayerProgress { RegionsUnlocked = 3, RegionIndex = 2 });
            Assert.That(session.RollNuggetType(0), Is.EqualTo(IndexOf("pine_cone")));
            Assert.That(session.RollNuggetType(0.6999), Is.EqualTo(IndexOf("pine_cone")));
            Assert.That(session.RollNuggetType(0.7001), Is.EqualTo(IndexOf("button")));
            Assert.That(session.RollNuggetType(0.9699), Is.EqualTo(IndexOf("button")));
            Assert.That(session.RollNuggetType(0.9701), Is.EqualTo(IndexOf("crooked_thumb")));
            Assert.That(session.RollNuggetType(0.9751), Is.EqualTo(IndexOf("owl_eye")));
            Assert.That(session.RollNuggetType(0.999999), Is.EqualTo(IndexOf("miners_fist")));
        }

        [Test]
        public void FirstCreekKeepsThePreviousShare()
        {
            GameSession session = NewSession();
            Assert.That(session.RollNuggetType(0.9699), Is.EqualTo(IndexOf("pebble")));
            Assert.That(session.RollNuggetType(0.9701), Is.EqualTo(IndexOf("crooked_thumb")));
        }

        [Test]
        public void RollNeverGivesABossNugget()
        {
            GameSession session = NewSession(new PlayerProgress { RegionsUnlocked = 20, RegionIndex = 19 });
            for (double roll = 0; roll < 1; roll += 0.001)
                Assert.That(GameCatalog.IsBossNugget(session.RollNuggetType(roll)), Is.False, "roll " + roll);
            Assert.That(session.CatchNugget(GameCatalog.BossNugget(19)).Index, Is.EqualTo(-1), "bosses only come from a Mother Lode");
            Assert.That(session.CatchNugget(-1).Index, Is.EqualTo(-1));
        }

        // --- Stars ---

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(9, 1)]
        [TestCase(10, 2)]
        [TestCase(39, 2)]
        [TestCase(40, 3)]
        [TestCase(500, 3)]
        public void StarsAtOneTenAndFortyCatches(int catches, int stars)
        {
            Assert.That(economy.NuggetStars(catches), Is.EqualTo(stars));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        public void BossStarsAtKillSecondAndFifthDrop(int drops, int stars)
        {
            Assert.That(economy.BossNuggetStars(drops), Is.EqualTo(stars));
            Assert.That(economy.NuggetStars(GameCatalog.BossNugget(3), drops), Is.EqualTo(stars));
        }

        [Test]
        public void NextStarThreshold()
        {
            Assert.That(economy.NextStarAt(0), Is.EqualTo(1));
            Assert.That(economy.NextStarAt(3), Is.EqualTo(10));
            Assert.That(economy.NextStarAt(10), Is.EqualTo(40));
            Assert.That(economy.NextStarAt(40), Is.Null);
        }

        [Test]
        public void CatchDiscoversOnceAndReportsStars()
        {
            GameSession session = NewSession();
            int pebble = IndexOf("pebble");

            NuggetCatch first = session.CatchNugget(pebble);
            Assert.That(first.Discovered, Is.True);
            Assert.That(first.StarsGained, Is.EqualTo(1));
            Assert.That(session.IsNuggetDiscovered(pebble), Is.True);

            for (int i = 2; i < 10; i++)
                Assert.That(session.CatchNugget(pebble).StarsGained, Is.EqualTo(0));
            NuggetCatch tenth = session.CatchNugget(pebble);
            Assert.That(tenth.Discovered, Is.False);
            Assert.That(tenth.StarsGained, Is.EqualTo(1));
            Assert.That(session.NuggetStars(pebble), Is.EqualTo(2));
            Assert.That(session.TotalStars, Is.EqualTo(2));
            Assert.That(session.MaxStars, Is.EqualTo(138));
        }

        // --- Boss fight (3.4.1) ---

        [Test]
        public void MotherLodeFightsTheLowestUnbeatenBoss()
        {
            var progress = new PlayerProgress { RegionsUnlocked = 5, RegionIndex = 4, PlaySeconds = 3600 };
            progress.NuggetCatches[GameCatalog.BossNugget(0)] = 1;
            GameSession session = NewSession(progress);
            MotherLodeRun run = session.StartMotherLode();
            Assert.That(run.IsBossFight, Is.True);
            Assert.That(run.BossRegion, Is.EqualTo(1));
            Assert.That(run.BossHealth, Is.EqualTo(500 * 2.2).Within(1e-9));
        }

        [Test]
        public void BeatenBossDropsItsNuggetAndEndsTheRun()
        {
            GameSession session = NewSession(new PlayerProgress { PlaySeconds = 3600 });
            MotherLodeRun run = session.StartMotherLode();
            Assert.That(run.BossRegion, Is.EqualTo(0));
            int hits = 0;
            while (!run.IsOver && hits < 1000)
            {
                run.Hit();
                run.Tick(0.2);
                hits++;
            }
            Assert.That(run.BossBeaten, Is.True);
            Assert.That(run.SecondsLeft, Is.GreaterThan(0).Or.EqualTo(0));
            Assert.That(session.FinishMotherLode(run).IsZero, Is.False, "the Dollar reward is paid as usual");
            Assert.That(run.DroppedNugget, Is.EqualTo(GameCatalog.BossNugget(0)));
            Assert.That(session.IsBossBeaten(0), Is.True);
            Assert.That(session.NuggetStars(GameCatalog.BossNugget(0)), Is.EqualTo(1));
            Assert.That(session.NextBossRegion, Is.EqualTo(-1), "only creek 1 is open");
        }

        [Test]
        public void EscapedBossPaysButDropsNothing()
        {
            GameSession session = NewSession(new PlayerProgress { PlaySeconds = 3600 });
            MotherLodeRun run = session.StartMotherLode();
            run.Hit();
            run.Tick(30);
            Assert.That(run.BossBeaten, Is.False);
            Assert.That(session.FinishMotherLode(run).IsZero, Is.False);
            Assert.That(run.DroppedNugget, Is.EqualTo(-1));
            Assert.That(session.NextBossRegion, Is.EqualTo(0));
        }

        [Test]
        public void HitDamageGrowsWithPrestigeGuildAndTobias()
        {
            var progress = new PlayerProgress { ProspectingXp = 50, GuildLevel = 10 };
            progress.CrewLevels[GameCatalog.Crew.Select((c, i) => (c, i)).First(p => p.c.Id == "tobias").i] = 2;
            GameSession session = NewSession(progress);
            double expected = session.PrestigeMultiplier * 1.10 * 1.30;
            Assert.That(session.BossHitDamage, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void PlainMotherLodeRedropsTheCurrentBossAtTheConfiguredChance()
        {
            config.BossRedropChance = 1;
            var progress = new PlayerProgress { PlaySeconds = 3600 };
            progress.NuggetCatches[GameCatalog.BossNugget(0)] = 1;
            GameSession session = NewSession(progress);
            MotherLodeRun run = session.StartMotherLode();
            Assert.That(run.IsBossFight, Is.False);
            session.FinishMotherLode(run);
            Assert.That(run.DroppedNugget, Is.EqualTo(GameCatalog.BossNugget(0)));
            Assert.That(session.NuggetStars(GameCatalog.BossNugget(0)), Is.EqualTo(2), "second drop is the second star");

            config.BossRedropChance = 0;
            MotherLodeRun second = session.StartMotherLode();
            session.FinishMotherLode(second);
            Assert.That(second.DroppedNugget, Is.EqualTo(-1));
        }

        // --- Prestige bonus ---

        [Test]
        public void StarsGrowThePrestigeBonus()
        {
            // Python: prestige_multiplier(501, 42)
            Assert.That(economy.PrestigeMultiplier(501, 42), Is.EqualTo(1 + 0.02 * 501 * (1 + 0.0065 * 42)).Within(1e-12));
            Assert.That(economy.PrestigeMultiplier(501, 138), Is.EqualTo(1 + 0.02 * 501 * 1.897).Within(1e-9), "full collection x1.9");
            Assert.That(economy.PrestigeMultiplier(501, 0), Is.EqualTo(11.02).Within(1e-12));
            Assert.That(economy.PrestigeMultiplier(0, 90), Is.EqualTo(1), "no effect before the first prestige");
        }

        [Test]
        public void SessionPrestigeMultiplierCountsStars()
        {
            var progress = new PlayerProgress { ProspectingXp = 100 };
            progress.NuggetCatches[IndexOf("pebble")] = 40;
            progress.NuggetCatches[IndexOf("owl_eye")] = 1;
            progress.NuggetCatches[IndexOf("creek_heart")] = 2;
            GameSession session = NewSession(progress);
            Assert.That(session.TotalStars, Is.EqualTo(6));
            Assert.That(session.PrestigeMultiplier, Is.EqualTo(1 + 0.02 * 100 * (1 + 0.0065 * 6)).Within(1e-12));
        }

        // --- Save ---

        [Test]
        public void NormalizeResizesOldSaves()
        {
            var progress = new PlayerProgress { NuggetCatches = new[] { 3, -2 } };
            progress.Normalize();
            Assert.That(progress.NuggetCatches.Length, Is.EqualTo(46));
            Assert.That(progress.NuggetCatches[0], Is.EqualTo(3));
            Assert.That(progress.NuggetCatches[1], Is.EqualTo(0));

            var missing = new PlayerProgress { NuggetCatches = null };
            missing.Normalize();
            Assert.That(missing.NuggetCatches.Length, Is.EqualTo(46));
        }
    }
}
