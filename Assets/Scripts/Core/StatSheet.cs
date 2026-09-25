using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Every number a sluice upgrade, crew member or perk can move. Bonuses of the same
    /// stat add up; different stats multiply. Values are fractions (+0.2 = +20%), except
    /// the chance stats, which are absolute probability points, CollectibleLifetime,
    /// which is seconds, and the whole-number stats (vein levels and catches, offline cap
    /// hours, chest capacity, Mother Lode max combo).
    /// </summary>
    public enum Stat
    {
        DustValue,
        SpawnRate,
        IdleSpeed,
        OfflineIncome,
        NuggetChance,
        NuggetValue,
        RichNuggetChance,
        RichNuggetValue,
        GiantNuggetChance,
        GiantNuggetValue,
        CritChance,
        CritValue,
        VeinMaxLevel,
        VeinCatchesPerLevel,
        OfflineCapHours,
        ChestCapacity,
        ChestTime,
        MotherLodeMaxCombo,
        DoubleCatch,
        AllIncome,
        ActiveIncome,
        MotherLodeReward,
        MotherLodeFrequency,
        CollectRadius,
        CollectibleLifetime,
        ChestValue,
        ProspectingXp,
        TierCost,
        RegionCost,
        UpgradeCost,
    }

    /// <summary>Summed bonuses per <see cref="Stat"/>.</summary>
    public sealed class StatSheet
    {
        static readonly int StatCount = Enum.GetValues(typeof(Stat)).Length;

        readonly double[] values = new double[StatCount];

        public double this[Stat stat] => values[(int)stat];

        public void Add(Stat stat, double amount) => values[(int)stat] += amount;

        public void Clear() => Array.Clear(values, 0, values.Length);

        /// <summary>1 + bonus, the factor a percentage stat applies.</summary>
        public double Multiplier(Stat stat) => 1 + values[(int)stat];

        /// <summary>Factor for a cost reduction stat; never lets a price reach zero.</summary>
        public double CostMultiplier(Stat stat) => Math.Max(0.1, 1 + values[(int)stat]);
    }
}
