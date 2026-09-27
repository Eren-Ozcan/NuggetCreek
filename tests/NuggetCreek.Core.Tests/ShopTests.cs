using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class ShopTests
    {
        const double Day = 86400;
        const double Hour = 3600;
        // Game day 20721 is a Friday; with UTC offset 0 it starts at 04:00 UTC.
        const long Friday = 20721;

        EconomyConfig config;
        Economy economy;

        [SetUp]
        public void SetUp()
        {
            config = new EconomyConfig();
            economy = new Economy(config);
        }

        static double DayStart(long day) => day * Day + 4 * Hour;

        GameSession NewSession(PlayerProgress progress = null, long day = Friday - 4)
        {
            var session = new GameSession(economy, progress ?? new PlayerProgress { ManualCollected = 100 }, new System.Random(3));
            GoTo(session, day, 1 * Hour);
            return session;
        }

        static void GoTo(GameSession session, long day, double intoDay)
        {
            session.NowUtc = DayStart(day) + intoDay;
            session.UpdateDay(day);
        }

        static ShopItem Item(ShopKind kind) => ShopCatalog.Of(kind);

        static int transaction;

        static PurchaseResult Buy(GameSession session, ShopKind kind) =>
            session.GrantPurchase(Item(kind), "t" + ++transaction);

        // --- Catalog ---

        [Test]
        public void GemLadderMatchesTheDesignDoc()
        {
            var gems = ShopCatalog.Items.Where(i => i.Kind == ShopKind.Gems).ToList();
            Assert.That(gems.Select(i => i.PriceCents), Is.EqualTo(new[] { 99, 499, 999, 2499, 4999, 9999 }));
            Assert.That(gems.Select(i => i.Gems), Is.EqualTo(new[] { 40, 300, 800, 3000, 8000, 20000 }));
            // Gems per dollar never go down along the ladder (8.2.1).
            for (int i = 1; i < gems.Count; i++)
                Assert.That((double)gems[i].Gems / gems[i].PriceCents, Is.GreaterThan((double)gems[i - 1].Gems / gems[i - 1].PriceCents));
            Assert.That(ShopCatalog.Items.Select(i => i.Id).Distinct().Count(), Is.EqualTo(ShopCatalog.Items.Count));
        }

        [Test]
        public void WeekdaysCountFromThursdayTheFirst()
        {
            Assert.That(GameSession.Weekday(0), Is.EqualTo(4));
            Assert.That(GameSession.Weekday(Friday), Is.EqualTo(5));
            Assert.That(GameSession.WeekendOf(Friday), Is.EqualTo(Friday));
            Assert.That(GameSession.WeekendOf(Friday + 2), Is.EqualTo(Friday));
            Assert.That(GameSession.WeekendOf(Friday + 3), Is.EqualTo(-1));
            Assert.That(GameSession.WeekendOf(Friday - 1), Is.EqualTo(-1));
        }

        // --- Offer windows ---

        [Test]
        public void NoTimedOffersBeforeTrustedTime()
        {
            var session = new GameSession(economy, new PlayerProgress(), new System.Random(3));
            Assert.That(session.ActiveOffers().Select(o => o.Kind), Is.EqualTo(new[] { ShopKind.RemoveAds }));
        }

        [Test]
        public void StarterPackSellsForSeventyTwoHoursOnce()
        {
            GameSession session = NewSession();
            Assert.That(session.OfferSecondsLeft(Item(ShopKind.Starter)), Is.EqualTo(72 * Hour));
            session.NowUtc += 71 * Hour;
            Assert.That(session.IsOnSale(Item(ShopKind.Starter)), Is.True);
            session.NowUtc += 2 * Hour;
            Assert.That(session.IsOnSale(Item(ShopKind.Starter)), Is.False);

            GameSession buyer = NewSession();
            Buy(buyer, ShopKind.Starter);
            Assert.That(buyer.IsOnSale(Item(ShopKind.Starter)), Is.False);
        }

        [Test]
        public void StarterClockSurvivesNewDays()
        {
            GameSession session = NewSession();
            GoTo(session, Friday - 3, 1 * Hour);
            Assert.That(session.OfferSecondsLeft(Item(ShopKind.Starter)), Is.EqualTo(48 * Hour));
        }

        [Test]
        public void NewCreekWelcomeIsSmallThenLargeAndOnlyOnFirstArrival()
        {
            var progress = new PlayerProgress { ManualCollected = 100, Dollars = 1e15, ProspectingXp = 0 };
            GameSession session = NewSession(progress);
            Assert.That(session.IsOnSale(Item(ShopKind.WelcomeSmall)), Is.False);

            session.UnlockNextRegion();
            Assert.That(session.OfferSecondsLeft(Item(ShopKind.WelcomeSmall)), Is.EqualTo(24 * Hour), "every creek, Willow Bend too");
            Assert.That(session.IsOnSale(Item(ShopKind.WelcomeLarge)), Is.False);
            for (int i = 0; i < 5; i++)
                session.UnlockNextRegion();
            Assert.That(session.Progress.RegionIndex, Is.EqualTo(6));
            Assert.That(session.IsOnSale(Item(ShopKind.WelcomeSmall)), Is.False);
            Assert.That(session.OfferSecondsLeft(Item(ShopKind.WelcomeLarge)), Is.EqualTo(24 * Hour), "Red Gulch");

            Buy(session, ShopKind.WelcomeLarge);
            Assert.That(session.IsOnSale(Item(ShopKind.WelcomeLarge)), Is.False);

            // A creek reached again after a new claim is not new.
            progress.TotalEarned = 1e15;
            Assert.That(session.Rebirth(), Is.True);
            session.EarnGems(0);
            progress.Dollars = 1e15;
            session.UnlockNextRegion();
            Assert.That(session.IsOnSale(Item(ShopKind.WelcomeSmall)), Is.False);
        }

        [Test]
        public void DailyOfferLastsSixHoursOncePerDay()
        {
            GameSession session = NewSession();
            ShopItem daily = Item(ShopKind.Daily);
            Assert.That(session.OfferSecondsLeft(daily), Is.EqualTo(6 * Hour));
            session.NowUtc += 6 * Hour;
            Assert.That(session.IsOnSale(daily), Is.False);

            GoTo(session, Friday - 3, 10 * Hour);
            Assert.That(session.OfferSecondsLeft(daily), Is.EqualTo(6 * Hour));
            PurchaseResult result = Buy(session, ShopKind.Daily);
            Assert.That(result.Gems, Is.EqualTo(1000));
            Assert.That(result.Cards.Count, Is.EqualTo(1));
            Assert.That(session.IsOnSale(daily), Is.False);

            GoTo(session, Friday - 2, 1 * Hour);
            Assert.That(session.IsOnSale(daily), Is.True);
        }

        [Test]
        public void WeekendPacksSellFridayToMondayOncePerWeekend()
        {
            GameSession session = NewSession();
            ShopItem small = Item(ShopKind.WeekendSmall);
            Assert.That(session.IsOnSale(small), Is.False);

            GoTo(session, Friday, 1 * Hour);
            Assert.That(session.OfferSecondsLeft(small), Is.EqualTo(3 * Day - 1 * Hour));
            Buy(session, ShopKind.WeekendSmall);
            Assert.That(session.IsOnSale(small), Is.False);
            Assert.That(session.IsOnSale(Item(ShopKind.WeekendMedium)), Is.True);

            GoTo(session, Friday + 2, 1 * Hour);
            Assert.That(session.IsOnSale(small), Is.False);
            Assert.That(session.OfferSecondsLeft(Item(ShopKind.WeekendLarge)), Is.EqualTo(1 * Day - 1 * Hour));

            GoTo(session, Friday + 3, 1 * Hour);
            Assert.That(session.IsOnSale(Item(ShopKind.WeekendLarge)), Is.False);

            GoTo(session, Friday + 7, 1 * Hour);
            Assert.That(session.IsOnSale(small), Is.True);
        }

        [Test]
        public void WeekendEndsAtLocalRollover()
        {
            GameSession session = NewSession();
            session.UtcOffsetSeconds = 3 * Hour;
            // Monday 04:00 in UTC+3 is 01:00 UTC.
            GoTo(session, Friday, 1 * Hour);
            Assert.That(session.OfferSecondsLeft(Item(ShopKind.WeekendSmall)), Is.EqualTo(3 * Day - 4 * Hour));
        }

        [Test]
        public void PremiumGearSlotSellsOnceGearIsOpen()
        {
            GameSession session = NewSession();
            ShopItem slot = Item(ShopKind.GearSlot);
            Assert.That(session.IsOnSale(slot), Is.False);
            session.Progress.ChestsOpened = 1;
            Assert.That(session.IsOnSale(slot), Is.True);
            Assert.That(Buy(session, ShopKind.GearSlot).GearSlot, Is.True);
            Assert.That(session.Progress.FourthGearSlotOwned, Is.True);
            Assert.That(session.IsGearSlotUnlocked(3), Is.True);
            Assert.That(session.IsOnSale(slot), Is.False);
        }

        // --- Grants ---

        [Test]
        public void CrewTimesOneHiresARandomNewMember()
        {
            GameSession session = NewSession();
            PurchaseResult result = Buy(session, ShopKind.Starter);
            Assert.That(result.Gems, Is.EqualTo(300));
            Assert.That(session.Progress.Gems, Is.EqualTo(300));
            Assert.That(result.Crew.Count, Is.EqualTo(1));
            Assert.That(session.CrewLevel(result.Crew[0]), Is.EqualTo(1));
            Assert.That(session.CrewHiredCount, Is.EqualTo(1));
        }

        [Test]
        public void CrewOffersHideOnceEveryoneIsHired()
        {
            var progress = new PlayerProgress { ManualCollected = 100 };
            for (int i = 0; i < progress.CrewLevels.Length; i++)
                progress.CrewLevels[i] = 1;
            GameSession session = NewSession(progress);
            Assert.That(session.IsOnSale(Item(ShopKind.Starter)), Is.False);
            Assert.That(session.IsOnSale(Item(ShopKind.Daily)), Is.True);
            // A paid pack still pays its Gems.
            PurchaseResult result = Buy(session, ShopKind.Starter);
            Assert.That(result.Crew, Is.Empty);
            Assert.That(result.Gems, Is.EqualTo(300));
        }

        [Test]
        public void HiringACandidateFromAPackClosesTheCandidatePair()
        {
            var progress = new PlayerProgress { ManualCollected = 100 };
            for (int i = 0; i < progress.CrewLevels.Length - 1; i++)
                progress.CrewLevels[i] = 1;
            int last = progress.CrewLevels.Length - 1;
            progress.CrewCandidates = new[] { last };
            progress.CandidateSecondsLeft = 100;
            GameSession session = NewSession(progress);
            Assert.That(Buy(session, ShopKind.Starter).Crew, Is.EqualTo(new[] { last }));
            Assert.That(session.HasCandidates, Is.False);
        }

        [Test]
        public void WeekendSmallRunsAFiveTimesBoostForAnHour()
        {
            GameSession session = NewSession();
            GoTo(session, Friday, 1 * Hour);
            Assert.That(Buy(session, ShopKind.WeekendSmall).Boost, Is.True);
            Assert.That(session.BoostMultiplier, Is.EqualTo(5));
            Assert.That(session.BoostSecondsLeft, Is.EqualTo(1 * Hour));
        }

        [Test]
        public void ATransactionIsGrantedOnce()
        {
            GameSession session = NewSession();
            ShopItem handful = ShopCatalog.Find("nc.gems.handful");
            Assert.That(session.GrantPurchase(handful, "same"), Is.Not.Null);
            Assert.That(session.GrantPurchase(handful, "same"), Is.Null);
            Assert.That(session.GrantPurchase(handful, ""), Is.Null);
            Assert.That(session.Progress.Gems, Is.EqualTo(40));
            Assert.That(session.Progress.SpentCents, Is.EqualTo(99));
        }

        [Test]
        public void TransactionMemoryKeepsTheLatestFifty()
        {
            GameSession session = NewSession();
            ShopItem handful = ShopCatalog.Find("nc.gems.handful");
            for (int i = 0; i < 60; i++)
                session.GrantPurchase(handful, "id" + i);
            Assert.That(session.Progress.Transactions.Length, Is.EqualTo(50));
            Assert.That(session.Progress.Transactions[0], Is.EqualTo("id10"));
            Assert.That(session.GrantPurchase(handful, "id59"), Is.Null);
        }

        [Test]
        public void AnExpiredOfferPaidAtTheStoreIsStillGranted()
        {
            GameSession session = NewSession();
            session.NowUtc += 7 * Hour;
            Assert.That(session.IsOnSale(Item(ShopKind.Daily)), Is.False);
            Assert.That(Buy(session, ShopKind.Daily).Gems, Is.EqualTo(1000));
        }

        // --- Remove ads (8.3) ---

        [Test]
        public void TwoSmallPurchasesRemoveAds()
        {
            GameSession session = NewSession();
            ShopItem pouch = ShopCatalog.Find("nc.gems.pouch");
            Assert.That(session.RemovesAds(pouch), Is.False);
            Assert.That(session.CentsToRemoveAds, Is.EqualTo(998));
            Assert.That(session.GrantPurchase(pouch, "a").AdsRemoved, Is.False);
            Assert.That(session.CentsToRemoveAds, Is.EqualTo(499));
            Assert.That(session.RemovesAds(pouch), Is.True);
            Assert.That(session.GrantPurchase(pouch, "b").AdsRemoved, Is.True);
            Assert.That(session.Progress.AdsRemoved, Is.True);
            Assert.That(session.Progress.AdsRemovedNoticePending, Is.True);
            Assert.That(session.RemovesAds(pouch), Is.False);
            Assert.That(session.IsOnSale(Item(ShopKind.RemoveAds)), Is.False);
            session.AcknowledgeAdsRemoved();
            Assert.That(session.Progress.AdsRemovedNoticePending, Is.False);
        }

        [Test]
        public void RemoveAdsProductAndOneBigPurchaseEachRemoveAds()
        {
            GameSession a = NewSession();
            Assert.That(a.RemovesAds(Item(ShopKind.RemoveAds)), Is.True);
            Assert.That(Buy(a, ShopKind.RemoveAds).AdsRemoved, Is.True);

            GameSession b = NewSession();
            Assert.That(b.RemovesAds(ShopCatalog.Find("nc.gems.chest")), Is.True);
            Assert.That(b.RemovesAds(ShopCatalog.Find("nc.gems.handful")), Is.False);
            Assert.That(b.GrantPurchase(ShopCatalog.Find("nc.gems.chest"), "x").AdsRemoved, Is.True);
        }

        // --- Gem boosts ---

        [Test]
        public void BoostsCostFiveGemsAndGoOutOfStockForAMinute()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 20 });
            Assert.That(session.BuyBoost(ShopBoost.GoldWash), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(15));
            Assert.That(session.BoostCooldown(ShopBoost.GoldWash), Is.EqualTo(60));
            Assert.That(session.BuyBoost(ShopBoost.GoldWash), Is.False);
            Assert.That(session.CanBuyBoost(ShopBoost.RichVein), Is.True);
            session.TickShop(59);
            Assert.That(session.CanBuyBoost(ShopBoost.GoldWash), Is.False);
            session.TickShop(1);
            Assert.That(session.BuyBoost(ShopBoost.GoldWash), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(10));
        }

        [Test]
        public void BoostsNeedGems()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 4 });
            Assert.That(session.BuyBoost(ShopBoost.RichVein), Is.False);
            Assert.That(session.BoostCooldown(ShopBoost.RichVein), Is.Zero);
        }

        [Test]
        public void GoldWashMultipliesIncomeSevenTimesForAMinute()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 20, AmosLevel = 1 });
            BigNumber idle = session.IdleRate;
            session.BuyBoost(ShopBoost.GoldWash);
            Assert.That(session.IdleRate.ToDouble(), Is.EqualTo(idle.ToDouble() * 7).Within(1e-9));
            session.TickShop(60);
            session.BuyBoost(ShopBoost.GoldWash);
            // Bought again while running: time adds up, the multiplier does not stack.
            session.TickShop(60);
            session.BuyBoost(ShopBoost.GoldWash);
            session.TickShop(30);
            Assert.That(session.Progress.GoldWashSecondsLeft, Is.EqualTo(30));
            Assert.That(session.IdleRate.ToDouble(), Is.EqualTo(idle.ToDouble() * 7).Within(1e-9));
            session.TickShop(30);
            Assert.That(session.IdleRate.ToDouble(), Is.EqualTo(idle.ToDouble()).Within(1e-9));
        }

        [Test]
        public void GoldWashStacksWithTheDailyWashBoostButNotOffline()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 20, AmosLevel = 1 });
            BigNumber idle = session.IdleRate;
            BigNumber offline = session.OfflineRate;
            session.ApplyBoost(2, 600);
            session.BuyBoost(ShopBoost.GoldWash);
            Assert.That(session.IncomeMultiplier, Is.EqualTo(session.PrestigeMultiplier * 14).Within(1e-9));
            Assert.That(session.IdleRate.ToDouble(), Is.EqualTo(idle.ToDouble() * 14).Within(1e-6));
            Assert.That(session.OfflineRate.ToDouble(), Is.EqualTo(offline.ToDouble()).Within(1e-9));
        }

        [Test]
        public void ExtraShiftPaysTwoHoursOfIdleIncomeAndNeedsAmos()
        {
            GameSession noAmos = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 20 });
            Assert.That(noAmos.BoostLocked(ShopBoost.ExtraShift), Is.True);
            Assert.That(noAmos.BuyBoost(ShopBoost.ExtraShift), Is.False);
            Assert.That(noAmos.Progress.Gems, Is.EqualTo(20));

            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 20, AmosLevel = 1 });
            BigNumber expected = session.IdleRate * 7200;
            Assert.That(session.ExtraShiftPayout.ToDouble(), Is.EqualTo(expected.ToDouble()).Within(1e-9));
            Assert.That(session.BuyBoost(ShopBoost.ExtraShift), Is.True);
            Assert.That(session.Progress.Dollars.ToDouble(), Is.EqualTo(expected.ToDouble()).Within(1e-9));
        }

        [Test]
        public void RichVeinQueuesOneGiantNugget()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 100, Gems = 20 });
            Assert.That(session.TakeBoughtGiant(), Is.False);
            session.BuyBoost(ShopBoost.RichVein);
            // Normal rolls are untouched; the creek takes the bought Giant when it can show it.
            Assert.That(session.RollKind(0.99), Is.EqualTo(CollectibleKind.GoldDust));
            Assert.That(session.TakeBoughtGiant(), Is.True);
            Assert.That(session.TakeBoughtGiant(), Is.False);
        }
    }
}
