using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class OnboardingTests
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
            new GameSession(economy, progress ?? new PlayerProgress(), new System.Random(5));

        // --- Locks ---

        [Test]
        public void FreshGameHasEveryFeatureLocked()
        {
            GameSession session = NewSession();
            foreach (Feature feature in System.Enum.GetValues(typeof(Feature)))
                Assert.That(session.IsUnlocked(feature), Is.False, feature.ToString());
        }

        [Test]
        public void UpgradesOpenAfterFifteenCatchesByHand()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 14 });
            Assert.That(session.IsUnlocked(Feature.Upgrades), Is.False);
            session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.IsUnlocked(Feature.Upgrades), Is.True);
        }

        [Test]
        public void MapOpensWithAmosAndShopWithPineHollow()
        {
            var progress = new PlayerProgress { ManualCollected = 20, Dollars = 1e6 };
            GameSession session = NewSession(progress);
            Assert.That(session.IsUnlocked(Feature.Map), Is.False);
            session.BuyAmosLevel();
            Assert.That(session.IsUnlocked(Feature.Map), Is.True);
            Assert.That(session.IsUnlocked(Feature.Shop), Is.False);
            session.UnlockNextRegion();
            Assert.That(session.IsUnlocked(Feature.Shop), Is.False, "Willow Bend is too early");
            session.UnlockNextRegion();
            Assert.That(session.IsUnlocked(Feature.Shop), Is.True);
        }

        [Test]
        public void DailyOpensOnTheSecondGameDayOrAfterAnHour()
        {
            GameSession session = NewSession();
            session.UpdateDay(100);
            Assert.That(session.IsUnlocked(Feature.Daily), Is.False);
            session.UpdateDay(101);
            Assert.That(session.IsUnlocked(Feature.Daily), Is.True);

            GameSession player = NewSession();
            player.UpdateDay(100);
            player.TickPlay(3599);
            Assert.That(player.IsUnlocked(Feature.Daily), Is.False);
            player.TickPlay(1);
            Assert.That(player.IsUnlocked(Feature.Daily), Is.True);
        }

        [Test]
        public void GuildOpensAtRedGulchAndStaysOpen()
        {
            GameSession session = NewSession(new PlayerProgress { BestRegionsUnlocked = 6, RegionsUnlocked = 6 });
            Assert.That(session.IsUnlocked(Feature.Guild), Is.False);
            GameSession later = NewSession(new PlayerProgress { BestRegionsUnlocked = 7, RegionsUnlocked = 1 });
            Assert.That(later.IsUnlocked(Feature.Guild), Is.True, "a rebirth keeps the best creek count");
        }

        [Test]
        public void SkippingTheIntroOpensEverythingAndSilencesPete()
        {
            GameSession session = NewSession();
            session.IntroSkipped = true;
            Assert.That(session.IsUnlocked(Feature.Guild), Is.True);
            Assert.That(session.NextPeteLine(), Is.Null);
        }

        // --- Free first hire ---

        [Test]
        public void TheFirstCandidateHireIsFreeThenTheLadderStarts()
        {
            GameSession session = NewSession();
            session.OfferCandidates();
            Assert.That(session.CrewHireCost, Is.EqualTo(0));
            Assert.That(session.HireCandidate(session.Candidates[0]), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(0));
            Assert.That(session.CrewHireCost, Is.EqualTo(15), "the ladder continues after one member");
            session.OfferCandidates();
            Assert.That(session.HireCandidate(session.Candidates[0]), Is.False, "no Gems for the second");
        }

        [Test]
        public void APackCrewDoesNotSpendTheFreeHire()
        {
            GameSession session = NewSession();
            session.GrantPurchase(ShopCatalog.Of(ShopKind.Starter), "t1");
            Assert.That(session.FreeHireReady, Is.True);
            session.OfferCandidates();
            Assert.That(session.CrewHireCost, Is.EqualTo(0));
        }

        // --- Old Pete ---

        [Test]
        public void PeteWelcomesThenTalksAboutDust()
        {
            GameSession session = NewSession();
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.Welcome));
            session.MarkSeen(PeteLine.Welcome);
            Assert.That(session.NextPeteLine(), Is.Null);
            for (int i = 0; i < 5; i++)
                session.Collect(CollectibleKind.GoldDust, false);
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.FirstDust));
            session.MarkSeen(PeteLine.FirstDust);
            Assert.That(session.HasSeen(PeteLine.FirstDust), Is.True);
        }

        [Test]
        public void PeteSkipsLinesWhoseMomentHasPassed()
        {
            // An older save far past the start: none of the early lines queue up.
            var progress = new PlayerProgress { ManualCollected = 500, AmosLevel = 3, RegionsUnlocked = 2, BestRegionsUnlocked = 2 };
            progress.UpgradeLevels[0] = 4;
            progress.ChestsOpened = 2;
            GameSession session = NewSession(progress);
            Assert.That(session.NextPeteLine(), Is.Null);
        }

        [Test]
        public void PeteTalksThroughTheFirstMinutesInOrder()
        {
            var progress = new PlayerProgress { ManualCollected = 15, Dollars = 100 };
            GameSession session = NewSession(progress);
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.UpgradesOpen));
            session.MarkSeen(PeteLine.UpgradesOpen);
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.AmosForHire));
            session.MarkSeen(PeteLine.AmosForHire);
            session.BuyAmosLevel();
            Assert.That(session.NextPeteLine(), Is.Null, "the next claim is not affordable yet");
            session.Progress.Dollars = 1e6;
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.MapOpen));
            session.MarkSeen(PeteLine.MapOpen);
            session.Progress.ChestsWaiting = 1;
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.FirstChest));
        }

        [Test]
        public void TheFirstUpgradeLessonWaitsUntilTheShovelIsAffordable()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 15 });
            Assert.That(session.IsUnlocked(Feature.Upgrades), Is.True);
            Assert.That(session.NextPeteLine(), Is.Null);
            session.Progress.Dollars = session.UpgradeCost(0).Value;
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.UpgradesOpen));
        }

        [Test]
        public void TheChestLessonWaitsForTheChestOnTheWater()
        {
            var progress = new PlayerProgress { ManualCollected = 200, AmosLevel = 1, ChestsDue = 1, PeteSeen = 1 << (int)PeteLine.MapOpen };
            GameSession session = NewSession(progress);
            Assert.That(session.NextPeteLine(), Is.Null, "due, but not floating yet");
            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.FirstChest));
        }

        [Test]
        public void TheBossLessonFollowsTheFirstBossOnly()
        {
            var progress = new PlayerProgress { ManualCollected = 5000, AmosLevel = 3, RegionsUnlocked = 4, BestRegionsUnlocked = 4, ChestsOpened = 3 };
            progress.UpgradeLevels[0] = 5;
            GameSession session = NewSession(progress);
            Assert.That(session.NextPeteLine(), Is.Null);
            progress.NuggetCatches[GameCatalog.BossNugget(0)] = 1;
            Assert.That(session.BossesBeaten, Is.EqualTo(1));
            Assert.That(session.FirstBeatenBoss, Is.EqualTo(0));
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.FirstBoss));

            // A save already past its second boss has missed the moment.
            progress.NuggetCatches[GameCatalog.BossNugget(1)] = 1;
            Assert.That(session.NextPeteLine(), Is.Null);
        }

        [Test]
        public void TheClaimLessonComesWithTheFirstSuggestedClaim()
        {
            var progress = new PlayerProgress
            {
                TotalEarned = 2e13, AmosLevel = 1, ManualCollected = 5000, ChestsOpened = 3, PeteSeen = 1 << (int)PeteLine.GuildOpen,
            };
            progress.UpgradeLevels[0] = 5;
            progress.RegionsUnlocked = progress.BestRegionsUnlocked = config.RegionCount;
            progress.RegionIndex = config.RegionCount - 1;
            progress.TierIndex = config.TierCount - 1;
            GameSession session = NewSession(progress);
            Assert.That(session.RebirthSuggested, Is.True);
            Assert.That(session.ClaimProgress, Is.EqualTo(1));
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.ClaimReady));

            progress.Rebirths = 1;
            Assert.That(session.NextPeteLine(), Is.Null, "only before the first claim");
        }

        [Test]
        public void PeteOffersTheFreeHireWithTheFirstCandidates()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 500, RegionsUnlocked = 3, BestRegionsUnlocked = 3, AmosLevel = 1 });
            session.OfferCandidates();
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.FreeHire));
            session.HireCandidate(session.Candidates[0]);
            Assert.That(session.NextPeteLine(), Is.Null);
        }

        // --- Second session ---

        [Test]
        public void TheReturnGiftIsOneChestOnce()
        {
            GameSession session = NewSession(new PlayerProgress { ManualCollected = 500, AmosLevel = 1, ChestsOpened = 1 });
            Assert.That(session.GiveReturnGift(), Is.True);
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(1));
            Assert.That(session.NextPeteLine(), Is.EqualTo(PeteLine.WelcomeBack));
            Assert.That(session.GiveReturnGift(), Is.False);
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(1));
        }
    }
}
