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
        public void EachLaunchCreekHasThreeCommonsARareAndALegendary()
        {
            Assert.That(GameCatalog.Nuggets.Count, Is.EqualTo(30));
            for (int region = 0; region < config.RegionCount; region++)
            {
                var rarities = GameCatalog.NuggetsInRegion(region).Select(i => GameCatalog.Nuggets[i].Rarity).ToList();
                Assert.That(rarities.Count(r => r == NuggetRarity.Common), Is.EqualTo(3), "region " + region);
                Assert.That(rarities.Count(r => r == NuggetRarity.Rare), Is.EqualTo(1), "region " + region);
                Assert.That(rarities.Count(r => r == NuggetRarity.Legendary), Is.EqualTo(1), "region " + region);
            }
        }

        [Test]
        public void IdsAndNamesAreUnique()
        {
            Assert.That(GameCatalog.Nuggets.Select(n => n.Id).Distinct().Count(), Is.EqualTo(30));
            Assert.That(GameCatalog.Nuggets.Select(n => n.Name).Distinct().Count(), Is.EqualTo(30));
        }

        [Test]
        public void CreekWeightsAddUpToOne()
        {
            double total = GameCatalog.NuggetsInRegion(0).Sum(i => economy.NuggetWeight(GameCatalog.Nuggets[i].Rarity));
            Assert.That(total, Is.EqualTo(1).Within(1e-12));
        }

        // --- Rolls ---

        [Test]
        public void RollPicksTypesOfTheCurrentCreekByWeight()
        {
            GameSession session = NewSession(new PlayerProgress { RegionsUnlocked = 2, RegionIndex = 1 });
            Assert.That(session.RollNuggetType(0), Is.EqualTo(IndexOf("pine_cone")));
            Assert.That(session.RollNuggetType(0.2799), Is.EqualTo(IndexOf("pine_cone")));
            Assert.That(session.RollNuggetType(0.2801), Is.EqualTo(IndexOf("bark_chip")));
            Assert.That(session.RollNuggetType(0.8401), Is.EqualTo(IndexOf("owl_eye")));
            Assert.That(session.RollNuggetType(0.9699), Is.EqualTo(IndexOf("owl_eye")));
            Assert.That(session.RollNuggetType(0.9701), Is.EqualTo(IndexOf("hollow_crown")));
            Assert.That(session.RollNuggetType(0.999999), Is.EqualTo(IndexOf("hollow_crown")));
        }

        [Test]
        public void CreeksPastTheLaunchSetHaveNoCollection()
        {
            GameSession session = NewSession(new PlayerProgress { RegionsUnlocked = 7, RegionIndex = 6 });
            Assert.That(session.RollNuggetType(0.5), Is.EqualTo(-1));
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
            Assert.That(session.MaxStars, Is.EqualTo(90));
        }

        // --- Prestige bonus ---

        [Test]
        public void StarsGrowThePrestigeBonus()
        {
            // Python: prestige_multiplier(501, 42)
            Assert.That(economy.PrestigeMultiplier(501, 42), Is.EqualTo(15.228399999999999).Within(1e-12));
            Assert.That(economy.PrestigeMultiplier(501, 0), Is.EqualTo(11.02).Within(1e-12));
            Assert.That(economy.PrestigeMultiplier(0, 90), Is.EqualTo(1), "no effect before the first prestige");
        }

        [Test]
        public void SessionPrestigeMultiplierCountsStars()
        {
            var progress = new PlayerProgress { ProspectingXp = 100 };
            progress.NuggetCatches[IndexOf("pebble")] = 40;
            progress.NuggetCatches[IndexOf("acorn")] = 1;
            GameSession session = NewSession(progress);
            Assert.That(session.TotalStars, Is.EqualTo(4));
            Assert.That(session.PrestigeMultiplier, Is.EqualTo(1 + 0.02 * 100 * 1.04).Within(1e-12));
        }

        // --- Save ---

        [Test]
        public void NormalizeResizesOldSaves()
        {
            var progress = new PlayerProgress { NuggetCatches = new[] { 3, -2 } };
            progress.Normalize();
            Assert.That(progress.NuggetCatches.Length, Is.EqualTo(30));
            Assert.That(progress.NuggetCatches[0], Is.EqualTo(3));
            Assert.That(progress.NuggetCatches[1], Is.EqualTo(0));

            var missing = new PlayerProgress { NuggetCatches = null };
            missing.Normalize();
            Assert.That(missing.NuggetCatches.Length, Is.EqualTo(30));
        }
    }
}
