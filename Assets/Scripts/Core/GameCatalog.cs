using System.Collections.Generic;

namespace NuggetCreek.Core
{
    public sealed class UpgradeDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int TierIndex;
        public readonly Stat Stat;
        public readonly double PerLevel;
        public readonly int MaxLevel;

        public UpgradeDefinition(string id, string name, int tierIndex, Stat stat, double perLevel, int maxLevel)
        {
            Id = id;
            Name = name;
            TierIndex = tierIndex;
            Stat = stat;
            PerLevel = perLevel;
            MaxLevel = maxLevel;
        }
    }

    /// <summary>A hireable crew member. Each one moves exactly one stat (single-axis roles).</summary>
    public sealed class CrewDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly Stat Stat;
        public readonly double PerLevel;

        public CrewDefinition(string id, string name, Stat stat, double perLevel)
        {
            Id = id;
            Name = name;
            Stat = stat;
            PerLevel = perLevel;
        }
    }

    /// <summary>
    /// Fixed game content: sluice upgrades (design doc 6.1) and crew (6.3). Prices live in
    /// <see cref="EconomyConfig"/> so Remote Config can tune them; effects are content.
    /// </summary>
    public static class GameCatalog
    {
        public const int CrewMaxLevel = 10;

        /// <summary>Amos is the 16th crew member but outside the hire system: he joins for
        /// Dollars, starts idle collection and his level is the offline cap in hours.</summary>
        public const string AmosId = "amos";

        public static readonly IReadOnlyList<UpgradeDefinition> Upgrades = new[]
        {
            new UpgradeDefinition("sturdy_shovel", "Sturdy Shovel", 0, Stat.DustValue, 0.20, 10),
            new UpgradeDefinition("creek_scatter", "Creek Scatter", 0, Stat.SpawnRate, 0.25, 4),
            new UpgradeDefinition("steel_sieve", "Steel Sieve", 1, Stat.IdleSpeed, 0.10, 10),
            new UpgradeDefinition("wide_pan", "Wide Pan", 1, Stat.CollectRadius, 0.08, 5),
            new UpgradeDefinition("water_channel", "Water Channel", 2, Stat.DoubleCatch, 0.01, 10),
            new UpgradeDefinition("dust_value", "Dust Value", 2, Stat.DustValue, 0.15, 10),
            new UpgradeDefinition("night_shift", "Night Shift", 3, Stat.OfflineIncome, 0.10, 10),
            new UpgradeDefinition("rich_sand", "Rich Sand", 3, Stat.NuggetChance, 0.01, 10),
            new UpgradeDefinition("fine_riffle", "Fine Riffle", 4, Stat.NuggetValue, 0.20, 10),
            new UpgradeDefinition("sorting", "Sorting", 4, Stat.DustValue, 0.30, 10),
            new UpgradeDefinition("master_craft", "Master Craft", 5, Stat.AllIncome, 0.05, 10),
            new UpgradeDefinition("vein_detector", "Vein Detector", 5, Stat.MotherLodeReward, 0.10, 10),
            new UpgradeDefinition("steamer_line", "Steamer Line", 6, Stat.AllIncome, 0.10, 10),
            new UpgradeDefinition("groundwork", "Groundwork", 6, Stat.NuggetChance, 0.015, 10),
            new UpgradeDefinition("passive_sluices", "Passive Sluices", 7, Stat.IdleSpeed, 0.15, 10),
            new UpgradeDefinition("big_pan", "Big Pan", 7, Stat.CollectRadius, 0.05, 10),
        };

        /// <summary>The 15 hireable crew members, in design doc order.</summary>
        public static readonly IReadOnlyList<CrewDefinition> Crew = new[]
        {
            new CrewDefinition("ezra", "Ezra", Stat.IdleSpeed, 0.08),
            new CrewDefinition("ma_perkins", "Ma Perkins", Stat.NuggetChance, 0.01),
            new CrewDefinition("cassidy", "Cassidy", Stat.TierCost, -0.03),
            new CrewDefinition("marta", "Marta", Stat.RegionCost, -0.03),
            new CrewDefinition("elias", "Elias", Stat.ProspectingXp, 0.10),
            new CrewDefinition("doc", "Doc", Stat.ChestValue, 0.30),
            new CrewDefinition("nell", "Nell", Stat.DustValue, 0.15),
            new CrewDefinition("silas", "Silas", Stat.NuggetValue, 0.15),
            new CrewDefinition("rosa", "Rosa", Stat.CollectibleLifetime, 0.15),
            new CrewDefinition("tobias", "Tobias", Stat.MotherLodeReward, 0.20),
            new CrewDefinition("wren", "Wren", Stat.MotherLodeFrequency, -0.03),
            new CrewDefinition("big_ole", "Big Ole", Stat.ActiveIncome, 0.08),
            new CrewDefinition("mei", "Mei", Stat.OfflineIncome, 0.10),
            new CrewDefinition("clementine", "Clementine", Stat.UpgradeCost, -0.03),
            new CrewDefinition("gus", "Gus", Stat.DoubleCatch, 0.005),
        };

        /// <summary>Adds owned upgrade levels to a sheet. levels[i] belongs to Upgrades[i].</summary>
        public static void ApplyUpgrades(StatSheet sheet, IReadOnlyList<int> levels)
        {
            for (int i = 0; i < Upgrades.Count; i++)
            {
                UpgradeDefinition upgrade = Upgrades[i];
                int level = System.Math.Min(levels[i], upgrade.MaxLevel);
                if (level > 0)
                    sheet.Add(upgrade.Stat, upgrade.PerLevel * level);
            }
        }

        public static void ApplyCrew(StatSheet sheet, CrewDefinition crew, int level)
        {
            int clamped = System.Math.Min(level, CrewMaxLevel);
            if (clamped > 0)
                sheet.Add(crew.Stat, crew.PerLevel * clamped);
        }
    }
}
