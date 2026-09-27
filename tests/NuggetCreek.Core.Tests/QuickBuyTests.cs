using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class QuickBuyTests
    {
        static int UpgradeIndex(string id)
        {
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                if (GameCatalog.Upgrades[i].Id == id)
                    return i;
            return -1;
        }

        static GameSession NewSession(double dollars, int tier = 0) =>
            new GameSession(new Economy(new EconomyConfig()), new PlayerProgress { Dollars = dollars, TierIndex = tier }, new System.Random(1));

        [Test]
        public void NothingAffordableBuysNothing()
        {
            GameSession session = NewSession(10);
            Assert.That(session.QuickBuyUpgrade, Is.EqualTo(-1));
            Assert.That(session.AffordableUpgradeCount, Is.EqualTo(0));
            Assert.That(session.QuickBuy(), Is.False);
        }

        [Test]
        public void BuysTheCheapestAffordableUpgradeEachTap()
        {
            GameSession session = NewSession(1000);
            int shovel = UpgradeIndex("sturdy_shovel");
            int scatter = UpgradeIndex("creek_scatter");
            Assert.That(session.AffordableUpgradeCount, Is.EqualTo(2));

            // Both start at $30; after one level each, the cheaper next level is picked.
            Assert.That(session.QuickBuy(), Is.True);
            Assert.That(session.QuickBuy(), Is.True);
            Assert.That(session.UpgradeLevel(shovel) + session.UpgradeLevel(scatter), Is.EqualTo(2));
            int next = session.QuickBuyUpgrade;
            BigNumber nextCost = session.UpgradeCost(next).Value;
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                if (session.IsUpgradeUnlocked(i) && session.CanAfford(session.UpgradeCost(i)))
                    Assert.That(session.UpgradeCost(i).Value, Is.GreaterThanOrEqualTo(nextCost));
        }

        [Test]
        public void NeverBuysTiersOrLockedUpgrades()
        {
            GameSession session = NewSession(1e30);
            while (session.QuickBuy()) { }
            Assert.That(session.Progress.TierIndex, Is.EqualTo(0), "tiers are not a one-tap buy");
            Assert.That(session.UpgradeLevel(UpgradeIndex("steel_sieve")), Is.EqualTo(0), "tier 2 upgrades stay locked");
            Assert.That(session.UpgradeLevel(UpgradeIndex("sturdy_shovel")), Is.EqualTo(10), "maxed upgrades drop out");
            Assert.That(session.Progress.AmosLevel, Is.EqualTo(0));
        }
    }
}
