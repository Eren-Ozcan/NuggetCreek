using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class GearTests
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

        static int GearIndex(string id) =>
            GameCatalog.Gear.Select((g, i) => (g, i)).First(p => p.g.Id == id).i;

        static void Catch(GameSession session, int times)
        {
            for (int i = 0; i < times; i++)
                session.Collect(CollectibleKind.GoldDust, false);
        }

        /// <summary>An earned chest floats down the creek and is caught into the tray (design doc 3.1.4).</summary>
        static void CatchChestFromRiver(GameSession session)
        {
            session.TickPlay(10);
            Assert.That(session.LaunchFloater(), Is.EqualTo(FloaterKind.Chest));
            Assert.That(session.CatchFloater(), Is.True);
        }

        // --- Catalog ---

        [Test]
        public void TwelvePiecesSixCommonFourRareTwoLegendary()
        {
            Assert.That(GameCatalog.Gear.Count, Is.EqualTo(12));
            Assert.That(GameCatalog.Gear.Count(g => g.Rarity == Rarity.Common), Is.EqualTo(6));
            Assert.That(GameCatalog.Gear.Count(g => g.Rarity == Rarity.Rare), Is.EqualTo(4));
            Assert.That(GameCatalog.Gear.Count(g => g.Rarity == Rarity.Legendary), Is.EqualTo(2));
            Assert.That(GameCatalog.Gear.Select(g => g.Id).Distinct().Count(), Is.EqualTo(12));
        }

        // --- Cards and levels ---

        [Test]
        public void NewCardGivesLevelOneAndWearsItWhenASlotIsFree()
        {
            GameSession session = NewSession();
            int hat = GearIndex("prospectors_hat");
            GearCard card = session.GrantGearCard(hat);

            Assert.That(card.IsNew, Is.True);
            Assert.That(session.GearLevel(hat), Is.EqualTo(1));
            Assert.That(session.GearUnlocked, Is.True);
            Assert.That(session.IsEquipped(hat), Is.True);
            Assert.That(session.Stats[Stat.AllIncome], Is.EqualTo(0.10).Within(1e-12));
        }

        [Test]
        public void DuplicatesLevelUpThenTurnIntoGems()
        {
            GameSession session = NewSession();
            int cup = GearIndex("tin_cup");
            session.GrantGearCard(cup);
            for (int level = 2; level <= GameCatalog.GearMaxLevel; level++)
                Assert.That(session.GrantGearCard(cup).LevelledUp, Is.True);
            Assert.That(session.GearLevel(cup), Is.EqualTo(10));
            Assert.That(economy.CritChance(session.Stats), Is.EqualTo(0.08).Within(1e-12));

            GearCard extra = session.GrantGearCard(cup);
            Assert.That(extra.Gems, Is.EqualTo(2));
            Assert.That(session.Progress.Gems, Is.EqualTo(2));
            Assert.That(session.GearLevel(cup), Is.EqualTo(10));
        }

        [Test]
        public void GemLevelUpCostsTwoFourSix()
        {
            GameSession session = NewSession(new PlayerProgress { Gems = 1000 });
            int gloves = GearIndex("leather_gloves");
            Assert.That(session.GearLevelUpCost(gloves), Is.Null, "not owned");
            session.GrantGearCard(gloves);

            int total = 0;
            for (int level = 1; level < GameCatalog.GearMaxLevel; level++)
            {
                Assert.That(session.GearLevelUpCost(gloves), Is.EqualTo(2 * level));
                total += session.GearLevelUpCost(gloves).Value;
                Assert.That(session.LevelUpGear(gloves), Is.True);
            }
            Assert.That(total, Is.EqualTo(90));
            Assert.That(session.GearLevelUpCost(gloves), Is.Null, "maxed");
            Assert.That(session.Stats[Stat.ActiveIncome], Is.EqualTo(0.5).Within(1e-12));
        }

        // --- Slots ---

        [Test]
        public void SlotsOpenWithFirstGearSilverForkAndGems()
        {
            GameSession session = NewSession(new PlayerProgress { Gems = 100 });
            Assert.That(session.IsGearSlotUnlocked(0), Is.False);

            session.GrantGearCard(0);
            session.GrantGearCard(1);
            Assert.That(session.IsGearSlotUnlocked(0), Is.True);
            Assert.That(session.IsGearSlotUnlocked(1), Is.False);
            Assert.That(session.IsEquipped(0), Is.True);
            Assert.That(session.IsEquipped(1), Is.False, "only one slot so far");
            Assert.That(session.Equip(1), Is.False);

            session.Progress.RegionsUnlocked = 4;
            Assert.That(session.IsGearSlotUnlocked(1), Is.False, "Bear Falls");
            session.Progress.RegionsUnlocked = 5;
            Assert.That(session.IsGearSlotUnlocked(1), Is.True, "Silver Fork");
            Assert.That(session.Equip(1), Is.True);

            Assert.That(session.BuyThirdGearSlot(), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(20));
            Assert.That(session.IsGearSlotUnlocked(2), Is.True);
            Assert.That(session.IsGearSlotUnlocked(3), Is.False, "the fourth slot is an IAP");
        }

        [Test]
        public void UnequipFreesTheSlotAndRemovesTheStat()
        {
            GameSession session = NewSession();
            int bell = GearIndex("mule_bell");
            int oil = GearIndex("lamp_oil");
            session.GrantGearCard(bell);
            session.GrantGearCard(oil);
            Assert.That(session.Stats[Stat.IdleSpeed], Is.EqualTo(0.04).Within(1e-12));

            Assert.That(session.Unequip(bell), Is.True);
            Assert.That(session.Stats[Stat.IdleSpeed], Is.EqualTo(0));
            Assert.That(session.Equip(oil), Is.True);
            Assert.That(session.Stats[Stat.OfflineIncome], Is.EqualTo(0.05).Within(1e-12));
        }

        [Test]
        public void NormalizeDropsUnownedAndDuplicateSlots()
        {
            var progress = new PlayerProgress { GearSlots = new[] { 3, 3, 5 } };
            progress.GearLevels[3] = 2;
            progress.Normalize();
            Assert.That(progress.GearSlots, Is.EqualTo(new[] { 3, -1, -1, -1 }));
        }

        // --- Chests ---

        [Test]
        public void FirstChestAfter150CatchesOpensAtOnceWithACommonCard()
        {
            GameSession session = NewSession();
            Catch(session, 149);
            Assert.That(session.HasChest, Is.False);
            Catch(session, 1);
            Assert.That(session.Progress.ChestsDue, Is.EqualTo(1), "it floats in first");
            Assert.That(session.HasChest, Is.False);
            CatchChestFromRiver(session);
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(1));

            Assert.That(session.StartChest(), Is.True);
            Assert.That(session.ChestReady, Is.True, "the first chest has no timer");
            ChestReward reward = session.ClaimChest();
            Assert.That(GameCatalog.Gear[reward.Card.Index].Rarity, Is.EqualTo(Rarity.Common));
            Assert.That(reward.Card.IsNew, Is.True);
            Assert.That(reward.Dollars.ToDouble(), Is.GreaterThan(0));
            Assert.That(session.GearUnlocked, Is.True);
        }

        [Test]
        public void LaterChestsEvery250CatchesUnlockInTenMinutesOfPlay()
        {
            GameSession session = NewSession(new PlayerProgress { ChestsOpened = 1 });
            Catch(session, 249);
            Assert.That(session.HasChest, Is.False);
            Catch(session, 1);
            CatchChestFromRiver(session);
            Assert.That(session.StartChest(), Is.True);
            Assert.That(session.ChestReady, Is.False);
            Assert.That(session.ChestInstantCost, Is.EqualTo(5));

            session.TickPlay(9 * 60);
            Assert.That(session.ChestInstantCost, Is.EqualTo(1));
            session.TickPlay(60);
            Assert.That(session.ChestReady, Is.True);
            Assert.That(session.ChestInstantCost, Is.Null);
        }

        [Test]
        public void OpeningNowCostsOneGemPerStartedTwoMinutes()
        {
            GameSession session = NewSession(new PlayerProgress { ChestsOpened = 1, ChestsWaiting = 1, Gems = 10 });
            session.StartChest();
            session.TickPlay(10 * 60 - 200);
            Assert.That(session.ChestInstantCost, Is.EqualTo(2));
            Assert.That(session.OpenChestNow(), Is.True);
            Assert.That(session.Progress.Gems, Is.EqualTo(8));
            Assert.That(session.ChestReady, Is.True);
        }

        [Test]
        public void EarnedChestsAreNeverLostToAFullTray()
        {
            GameSession session = NewSession(new PlayerProgress { ChestsOpened = 1 });
            Catch(session, 250 * 5);
            Assert.That(session.Progress.ChestsDue, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
                CatchChestFromRiver(session);
            Assert.That(session.Progress.ChestsWaiting, Is.EqualTo(5));
            Assert.That(session.TrayCount, Is.EqualTo(5));
        }

        [Test]
        public void ChestPaysThreeMinutesOfIncomeAndDoublesOnce()
        {
            var progress = new PlayerProgress { ChestsOpened = 1, ChestsWaiting = 1, AmosLevel = 1, ManualCollected = 100 };
            progress.CrewLevels[5] = 2; // Doc: +30% chest value per level
            GameSession session = NewSession(progress);
            BigNumber expected = session.IncomePerSecond * (180 * 1.6);
            session.StartChest();
            session.TickPlay(600);

            ChestReward reward = session.ClaimChest();
            Assert.That(reward.Dollars.ToDouble(), Is.EqualTo(expected.ToDouble()).Within(expected.ToDouble() * 1e-9));
            Assert.That(session.DoubleChest(reward), Is.True);
            Assert.That(session.DoubleChest(reward), Is.False);
            Assert.That(session.Progress.Dollars.ToDouble(), Is.EqualTo(2 * expected.ToDouble()).Within(expected.ToDouble() * 1e-9));
        }

        // --- Rolls and boxes ---

        [Test]
        public void RarityRollFollowsTheOdds()
        {
            Assert.That(GameSession.RollRarity(config.ChestCardOdds, 0, 0.79), Is.EqualTo(Rarity.Common));
            Assert.That(GameSession.RollRarity(config.ChestCardOdds, 0, 0.81), Is.EqualTo(Rarity.Rare));
            Assert.That(GameSession.RollRarity(config.ChestCardOdds, 0, 0.985), Is.EqualTo(Rarity.Legendary));
            Assert.That(GameSession.RollRarity(config.GearBoxOdds, 6, 0.86), Is.EqualTo(Rarity.Legendary));
        }

        [Test]
        public void EveryBoxHoldsItsGuaranteedRarity()
        {
            for (int box = 0; box < 3; box++)
            {
                for (int seed = 0; seed < 50; seed++)
                {
                    var session = new GameSession(economy, new PlayerProgress { Gems = 5000, ChestsOpened = 1 }, new System.Random(seed));
                    var cards = session.BuyGearBox(box);
                    Assert.That(cards.Count, Is.EqualTo(config.GearBoxCards[box]));
                    Assert.That(cards.Any(c => GameCatalog.Gear[c.Index].Rarity >= config.GearBoxGuarantee[box]), Is.True,
                        $"box {box} seed {seed}");
                    Assert.That(session.Progress.Gems, Is.EqualTo(5000 - config.GearBoxGems[box]));
                }
            }
        }

        [Test]
        public void BoxesWaitForTheItemsTab()
        {
            GameSession session = NewSession(new PlayerProgress { Gems = 5000 });
            Assert.That(session.BuyGearBox(0), Is.Null);
            Assert.That(session.Progress.Gems, Is.EqualTo(5000));
        }
    }
}
