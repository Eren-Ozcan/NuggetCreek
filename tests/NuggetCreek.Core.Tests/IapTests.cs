using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class IapTests
    {
        GameSession session;
        RecordedGameEvents events;

        [SetUp]
        public void SetUp()
        {
            session = new GameSession(new Economy(new EconomyConfig()), new PlayerProgress(), new System.Random(5));
            events = new RecordedGameEvents();
            session.Events = events;
        }

        static ShopItem Item(string id) => ShopCatalog.Find(id);

        [Test]
        public void PurchaseReportsLocalPriceFirstAndCumulative()
        {
            session.GrantPurchase(Item("nc.gems.pouch"), "t1", 179.99, "TRY");
            var purchase = events.Named("iap_purchase").Single();
            Assert.That(purchase["sku"], Is.EqualTo("nc.gems.pouch"));
            Assert.That(purchase["price_usd"], Is.EqualTo(4.99));
            Assert.That(purchase["price_local"], Is.EqualTo(179.99));
            Assert.That(purchase["currency"], Is.EqualTo("TRY"));
            Assert.That(purchase["first"], Is.EqualTo(true));
            Assert.That(session.PayerTier, Is.EqualTo("first"));

            session.GrantPurchase(Item("nc.gems.handful"), "t2");
            var second = events.Named("iap_purchase").Last();
            Assert.That(second["first"], Is.EqualTo(false));
            Assert.That(second["currency"], Is.EqualTo("USD"));
            Assert.That(second["cum_usd"], Is.EqualTo(5.98));
            Assert.That(session.PayerTier, Is.EqualTo("repeat"));
        }

        [Test]
        public void RepeatedTransactionReportsNothing()
        {
            session.GrantPurchase(Item("nc.gems.pouch"), "same");
            session.GrantPurchase(Item("nc.gems.pouch"), "same");
            Assert.That(events.Named("iap_purchase").Count(), Is.EqualTo(1));
        }

        [Test]
        public void AdsRemovedSaysWhichPath()
        {
            session.GrantPurchase(Item("nc.offer.remove_ads"), "t1");
            Assert.That(events.Named("ads_removed").Single()["path"], Is.EqualTo("sku"));

            SetUp();
            session.GrantPurchase(Item("nc.gems.pouch"), "t1");
            Assert.That(events.Named("ads_removed"), Is.Empty);
            session.GrantPurchase(Item("nc.gems.pouch"), "t2");
            Assert.That(events.Named("ads_removed").Single()["path"], Is.EqualTo("threshold"));
        }

        [Test]
        public void WhaleFromOneHundredDollars()
        {
            session.GrantPurchase(Item("nc.gems.vein"), "t1");
            Assert.That(session.PayerTier, Is.EqualTo("first"), "99.99 is below the line");
            session.GrantPurchase(Item("nc.gems.handful"), "t2");
            Assert.That(session.PayerTier, Is.EqualTo("whale"));
        }

        [Test]
        public void FunnelEvents()
        {
            session.StartPurchase(Item("nc.offer.starter"));
            session.FailPurchase(Item("nc.offer.starter"), "cancelled");
            Assert.That(events.Named("iap_start").Single()["sku"], Is.EqualTo("nc.offer.starter"));
            Assert.That(events.Named("iap_fail").Single()["reason"], Is.EqualTo("cancelled"));
        }

        [Test]
        public void OfferShownOncePerSessionAndNeverForGems()
        {
            session.ReportOfferShown(Item("nc.offer.starter"), "shop");
            session.ReportOfferShown(Item("nc.offer.starter"), "shop");
            session.ReportOfferShown(Item("nc.gems.pouch"), "shop");
            Assert.That(events.Named("offer_shown").Single()["offer_id"], Is.EqualTo("nc.offer.starter"));
        }

        [Test]
        public void RestoreBringsBackOnlyNonConsumables()
        {
            bool changed = session.RestoreOwned(new[] { "nc.offer.remove_ads", "nc.offer.gear_slot", "nc.gems.pouch", "unknown" });
            Assert.That(changed, Is.True);
            Assert.That(session.Progress.AdsRemoved, Is.True);
            Assert.That(session.Progress.FourthGearSlotOwned, Is.True);
            Assert.That(session.Progress.Gems, Is.Zero, "consumables never come back");
            Assert.That(events.Named("ads_removed").Single()["path"], Is.EqualTo("restore"));
            Assert.That(session.RestoreOwned(new[] { "nc.offer.remove_ads" }), Is.False, "already owned");
        }

        [Test]
        public void OldSavesWithSpendCountAsPayers()
        {
            var progress = new PlayerProgress { SpentCents = 499 };
            progress.Normalize();
            Assert.That(progress.Purchases, Is.EqualTo(1));
        }
    }
}
