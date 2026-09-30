using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>What one gear card did: new gear, a level, or Gems for a maxed duplicate.</summary>
    public readonly struct GearCard
    {
        public readonly int Index;
        public readonly bool IsNew;
        public readonly bool LevelledUp;
        public readonly int Gems;

        public GearCard(int index, bool isNew, bool levelledUp, int gems)
        {
            Index = index;
            IsNew = isNew;
            LevelledUp = levelledUp;
            Gems = gems;
        }
    }

    /// <summary>A claimed creek chest; the Dollars are already paid, a rewarded ad can pay them once more.</summary>
    public sealed class ChestReward
    {
        public readonly BigNumber Dollars;
        public readonly GearCard Card;

        public bool Doubled { get; internal set; }

        public ChestReward(BigNumber dollars, GearCard card)
        {
            Dollars = dollars;
            Card = card;
        }
    }

    /// <summary>Gear slots, gear cards, creek chests and Gem gear boxes (design doc 6.4).</summary>
    public sealed partial class GameSession
    {
        // --- Gear ---

        public int GearLevel(int index) => Progress.GearLevels[index];

        public bool OwnsGear(int index) => Progress.GearLevels[index] > 0;

        /// <summary>The ITEMS tab opens with the first chest (design doc 10).</summary>
        public bool GearUnlocked
        {
            get
            {
                if (Progress.ChestsOpened > 0)
                    return true;
                foreach (int level in Progress.GearLevels)
                    if (level > 0)
                        return true;
                return false;
            }
        }

        public bool IsGearSlotUnlocked(int slot)
        {
            switch (slot)
            {
                case 0: return GearUnlocked;
                case 1: return GearUnlocked && Math.Max(Progress.BestRegionsUnlocked, Progress.RegionsUnlocked) > Config.GearSecondSlotRegion;
                case 2: return Progress.ThirdGearSlotBought;
                case 3: return Progress.FourthGearSlotOwned;
                default: return false;
            }
        }

        /// <summary>Gear index in a slot, -1 when empty.</summary>
        public int GearInSlot(int slot) => Progress.GearSlots[slot];

        public bool IsEquipped(int index) => Array.IndexOf(Progress.GearSlots, index) >= 0;

        /// <summary>Puts owned gear into the first free unlocked slot.</summary>
        public bool Equip(int index)
        {
            if (!OwnsGear(index) || IsEquipped(index))
                return false;
            for (int slot = 0; slot < Progress.GearSlots.Length; slot++)
            {
                if (IsGearSlotUnlocked(slot) && Progress.GearSlots[slot] < 0)
                {
                    Progress.GearSlots[slot] = index;
                    RebuildStats();
                    return true;
                }
            }
            return false;
        }

        public bool Unequip(int index)
        {
            int slot = Array.IndexOf(Progress.GearSlots, index);
            if (slot < 0)
                return false;
            Progress.GearSlots[slot] = -1;
            RebuildStats();
            return true;
        }

        public int? ThirdGearSlotCost => Progress.ThirdGearSlotBought ? (int?)null : Config.GearThirdSlotGems;

        public bool BuyThirdGearSlot()
        {
            if (!GearUnlocked || !TrySpendGems(ThirdGearSlotCost, "gear_slot"))
                return false;
            Progress.ThirdGearSlotBought = true;
            return true;
        }

        /// <summary>Gem price of the next level (2, 4, 6 ...); null when not owned or maxed.</summary>
        public int? GearLevelUpCost(int index)
        {
            int level = Progress.GearLevels[index];
            return level >= 1 && level < GameCatalog.GearMaxLevel ? Config.GearLevelUpGemsStep * level : (int?)null;
        }

        public bool LevelUpGear(int index)
        {
            if (!TrySpendGems(GearLevelUpCost(index), "gear_level"))
                return false;
            Progress.GearLevels[index]++;
            RebuildStats();
            CountJob(DailyJobKind.GearLevels);
            return true;
        }

        void ApplyEquippedGear()
        {
            for (int slot = 0; slot < Progress.GearSlots.Length; slot++)
            {
                int index = Progress.GearSlots[slot];
                if (index >= 0 && IsGearSlotUnlocked(slot))
                    GameCatalog.ApplyGear(Stats, GameCatalog.Gear[index], Progress.GearLevels[index]);
            }
        }

        // --- Cards ---

        /// <summary>New gear arrives at level 1 and is worn if a slot is free; a duplicate adds a level.</summary>
        /// <param name="source">gear_card's source: chest, gear_box, streak or pack.</param>
        public GearCard GrantGearCard(int index, string source = "other")
        {
            int level = Progress.GearLevels[index];
            GearDefinition gear = GameCatalog.Gear[index];
            Emit("gear_card", ("gear_id", gear.Id), ("rarity", EventValues.Snake(gear.Rarity.ToString())), ("source", source), ("dup", level > 0));
            if (level == 0)
            {
                Progress.GearLevels[index] = 1;
                if (!Equip(index))
                    RebuildStats();
                return new GearCard(index, true, false, 0);
            }
            if (level < GameCatalog.GearMaxLevel)
            {
                Progress.GearLevels[index] = level + 1;
                RebuildStats();
                CountJob(DailyJobKind.GearLevels);
                return new GearCard(index, false, true, 0);
            }
            EarnGems(Config.GearMaxedDuplicateGems, "gear_dup");
            return new GearCard(index, false, false, Config.GearMaxedDuplicateGems);
        }

        /// <summary>Rarity from odds[offset..offset+2] (Common, Rare, Legendary). roll is uniform in [0, 1).</summary>
        public static Rarity RollRarity(IReadOnlyList<double> odds, int offset, double roll)
        {
            double total = odds[offset] + odds[offset + 1] + odds[offset + 2];
            double target = roll * total;
            for (int i = 0; i < 3; i++)
            {
                target -= odds[offset + i];
                if (target < 0)
                    return (Rarity)i;
            }
            return Rarity.Legendary;
        }

        /// <summary>One gear of a rarity, uniform among that rarity. roll is uniform in [0, 1).</summary>
        public static int PickGear(Rarity rarity, double roll)
        {
            var pool = new List<int>();
            for (int i = 0; i < GameCatalog.Gear.Count; i++)
                if (GameCatalog.Gear[i].Rarity == rarity)
                    pool.Add(i);
            return pool[Math.Min(pool.Count - 1, (int)(roll * pool.Count))];
        }

        // --- Creek chests ---

        /// <summary>The unlocking chest was finished with Gems (chest_open.method).</summary>
        bool chestBoughtOpen;

        /// <summary>Free unlock time of a chest after the Guild's chest milestone.</summary>
        public double ChestOpenSeconds => Config.ChestOpenSeconds * Stats.CostMultiplier(Stat.ChestTime);

        /// <summary>
        /// Counts a manual catch; every ChestEveryCatches (the first after fewer) earns a chest,
        /// which then floats down the creek to be caught (design doc 3.1.4).
        /// </summary>
        void CountTowardChest()
        {
            Progress.CatchesTowardChest++;
            bool first = FirstChestAhead && Progress.ChestsDue == 0;
            int needed = first ? Config.FirstChestAfterCatches : Config.ChestEveryCatches;
            if (Progress.CatchesTowardChest < needed)
                return;
            Progress.CatchesTowardChest = 0;
            Progress.ChestsDue++;
        }

        /// <summary>No chest has been caught yet: the next one is the tutorial's, which opens at once.</summary>
        bool FirstChestAhead => Progress.ChestsOpened == 0 && Progress.ChestsWaiting == 0 && !Progress.ChestOpening;

        public bool HasChest => Progress.ChestsWaiting > 0 || Progress.ChestOpening;

        public bool ChestReady => Progress.ChestOpening && Progress.ChestSecondsLeft <= 0;

        /// <summary>Starts unlocking the next waiting chest; the very first one needs no wait.</summary>
        public bool StartChest()
        {
            if (Progress.ChestOpening || Progress.ChestsWaiting == 0)
                return false;
            Progress.ChestsWaiting--;
            Progress.ChestOpening = true;
            Progress.ChestSecondsLeft = Progress.ChestsOpened == 0 ? 0 : ChestOpenSeconds;
            return true;
        }

        void TickChest(double deltaSeconds)
        {
            if (Progress.ChestOpening && Progress.ChestSecondsLeft > 0)
                Progress.ChestSecondsLeft = Math.Max(0, Progress.ChestSecondsLeft - deltaSeconds);
        }

        /// <summary>Gems to skip the rest of the unlock; null when no chest is unlocking.</summary>
        public int? ChestInstantCost
        {
            get
            {
                if (!Progress.ChestOpening || Progress.ChestSecondsLeft <= 0)
                    return null;
                int gems = (int)Math.Ceiling(Progress.ChestSecondsLeft / Config.ChestInstantSecondsPerGem);
                return Math.Max(1, Math.Min(Config.ChestInstantMaxGems, gems));
            }
        }

        public bool OpenChestNow()
        {
            if (!TrySpendGems(ChestInstantCost, "chest_instant"))
                return false;
            Progress.ChestSecondsLeft = 0;
            chestBoughtOpen = true;
            return true;
        }

        /// <summary>Dollars the ready chest pays: ChestIncomeSeconds of current income, times the chest value bonus.</summary>
        public BigNumber ChestDollars => IncomePerSecond * (Config.ChestIncomeSeconds * Stats.Multiplier(Stat.ChestValue));

        /// <summary>Opens a ready chest: pays its Dollars and one gear card. The first chest's card is Common.</summary>
        public ChestReward ClaimChest()
        {
            if (!ChestReady)
                return null;
            bool first = Progress.ChestsOpened == 0;
            BigNumber dollars = ChestDollars;
            Progress.ChestOpening = false;
            Progress.ChestsOpened++;
            CountJob(DailyJobKind.ChestsOpened);
            Earn(dollars, IncomeSource.Chest);
            Emit("chest_open", ("source", "creek"), ("method", chestBoughtOpen ? "gem" : "free"));
            chestBoughtOpen = false;
            Rarity rarity = first ? Rarity.Common : RollRarity(Config.ChestCardOdds, 0, random.NextDouble());
            GearCard card = GrantGearCard(PickGear(rarity, random.NextDouble()), "chest");
            return new ChestReward(dollars, card);
        }

        /// <summary>Pays a chest's Dollars a second time after a rewarded ad; once per chest.</summary>
        public bool DoubleChest(ChestReward reward)
        {
            if (reward == null || reward.Doubled)
                return false;
            reward.Doubled = true;
            Earn(reward.Dollars, IncomeSource.Chest);
            return true;
        }

        // --- Gear boxes (Gems) ---

        public int GearBoxCount => Config.GearBoxGems.Length;

        /// <summary>Buys a box and grants its cards; at least one meets the box's guaranteed rarity.</summary>
        public List<GearCard> BuyGearBox(int box)
        {
            if (!GearUnlocked || !TrySpendGems(Config.GearBoxGems[box], "gear_box"))
                return null;
            return GrantGearBox(box, "gear_box");
        }
    }
}
