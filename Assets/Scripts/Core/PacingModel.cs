using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Typical-player income model used to solve the cost tables (design doc 5.0.4).
    /// Not used at runtime; it mirrors tools/economy_tune.py so the C# formulas can be
    /// checked against it, and so the EconomyConfig defaults can be re-derived in tests.
    /// </summary>
    public sealed class PacingModel
    {
        public const double SessionMinutes = 7;
        public const double SessionsPerDay = 4;
        public const double ActiveSecondsPerDay = SessionMinutes * 60 * SessionsPerDay;
        public const double IdleSecondsPerDay = 10 * 3600;
        public const double FirstSessionMinutes = 10;

        public const double RegionShare = 0.6;
        public const double TierShare = 0.25;
        public const double UpgradeBaseShare = 0.03;
        public const double AmosCostIdleSeconds = 45 * 60;
        public const double AmosPairStep = 1.5;

        /// <summary>Opening days per region (index = region), from the target curve.</summary>
        public static readonly double[] TargetDays = { 0, 0.01, 0.6, 3, 8, 20, 40 };

        /// <summary>
        /// Phase 2.8 bot calibration per region (index = region): the 30-day bot playing the
        /// real core with every phase 2 system reaches creeks faster than this model, so each
        /// solved cost is scaled to land on its target day. Mirrors BOT_CALIBRATION in
        /// tools/economy_tune.py.
        /// </summary>
        public static readonly double[] BotCalibration = { 1.0, 3.4, 7.817, 7.503, 8.837, 9.932, 9.932 };

        static readonly Dictionary<int, double> PrestigeAt = new Dictionary<int, double> { { 4, 2.5 }, { 5, 6.0 }, { 6, 15.0 } };

        readonly Economy economy;

        public PacingModel(Economy economy)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        /// <summary>
        /// Upgrades a typical player owns on tierIndex: earlier tiers maxed, the current tier's
        /// upgrades at currentTierLevels (half of max when null), Creek Scatter at spawnLevel.
        /// </summary>
        public static StatSheet TypicalStats(int tierIndex, int spawnLevel, int? currentTierLevels = null)
        {
            var sheet = new StatSheet();
            foreach (UpgradeDefinition upgrade in GameCatalog.Upgrades)
            {
                if (upgrade.TierIndex > tierIndex)
                    continue;
                int level;
                if (upgrade.Stat == Stat.SpawnRate)
                    level = spawnLevel;
                else if (upgrade.TierIndex < tierIndex)
                    level = upgrade.MaxLevel;
                else
                    level = currentTierLevels ?? upgrade.MaxLevel / 2;
                sheet.Add(upgrade.Stat, upgrade.PerLevel * Math.Min(level, upgrade.MaxLevel));
            }
            return sheet;
        }

        static double PrestigeFor(int regionIndex) =>
            PrestigeAt.TryGetValue(regionIndex + 1, out double p) ? p : 1.0;

        static StatSheet TypicalStatsFor(int regionIndex) =>
            TypicalStats(regionIndex, Math.Min(4, regionIndex + 2));

        public BigNumber DayIncome(int regionIndex)
        {
            StatSheet stats = TypicalStatsFor(regionIndex);
            double prestige = PrestigeFor(regionIndex);
            double spawnBase = economy.Config.SpawnBase;
            BigNumber active = economy.ActiveRate(regionIndex, regionIndex, stats, prestige, spawnBase);
            BigNumber idle = economy.IdleRate(regionIndex, regionIndex, stats, prestige, spawnBase);
            return active * ActiveSecondsPerDay + idle * IdleSecondsPerDay;
        }

        /// <summary>Uninterrupted opening session in region 1: first quarter with nothing
        /// bought, the rest with Creek Scatter L1 and Sturdy Shovel L2.</summary>
        public BigNumber FirstSessionIncome()
        {
            double spawnBase = economy.Config.SpawnBaseFirstRegion;
            BigNumber early = economy.ActiveRate(0, 0, TypicalStats(0, 0, 0), 1, spawnBase);
            BigNumber late = economy.ActiveRate(0, 0, TypicalStats(0, 1, 2), 1, spawnBase);
            return (early * 0.25 + late * 0.75) * (FirstSessionMinutes * 60);
        }

        /// <summary>Unrounded unlock cost of regionIndex (1..6).</summary>
        public BigNumber SolveRegionCost(int regionIndex)
        {
            if (regionIndex == 1)
                return FirstSessionIncome() * (RegionShare * BotCalibration[1]);
            double window = TargetDays[regionIndex] - TargetDays[regionIndex - 1];
            return DayIncome(regionIndex - 1) * (window * RegionShare * BotCalibration[regionIndex]);
        }

        /// <summary>Unrounded cost of Amos level 2..12, 45 minutes of idle at the unlocking region.</summary>
        public BigNumber SolveAmosLevelCost(int level)
        {
            int regionIndex = (level - 1) / 2;
            BigNumber idle = economy.IdleRate(regionIndex, regionIndex, TypicalStatsFor(regionIndex), PrestigeFor(regionIndex), economy.Config.SpawnBase);
            double step = level % 2 == 0 ? AmosPairStep : 1.0;
            return idle * (AmosCostIdleSeconds * step);
        }

        /// <summary>Round to two significant figures, as the cost tables are written.</summary>
        public static double RoundSignificant(double value, int digits = 2)
        {
            if (value == 0)
                return 0;
            double scale = Math.Pow(10, digits - 1 - Math.Floor(Math.Log10(Math.Abs(value))));
            return Math.Round(value * scale, MidpointRounding.ToEven) / scale;
        }
    }
}
