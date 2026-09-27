using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Typical-player income model used to solve the cost tables (design doc 5.0.4).
    /// Not used at runtime; it mirrors tools/economy_tune.py so the C# formulas can be
    /// checked against it, and so the EconomyConfig defaults can be re-derived in tests.
    ///
    /// 20 creeks (design doc 6.2, v0.19): the old 10-creek curve keeps its points on the even
    /// creek indices (old creek k is creek 2k-1) and one creek sits halfway between each
    /// pair, at the geometric mean of its neighbours' opening days. Tier t is bought in
    /// creek index 2t, so a typical player on creek index r owns tier r / 2.
    /// </summary>
    public sealed class PacingModel
    {
        public const double SessionMinutes = 7;
        public const double SessionsPerDay = 4;
        public const double ActiveSecondsPerDay = SessionMinutes * 60 * SessionsPerDay;
        public const double IdleSecondsPerDay = 10 * 3600;

        /// <summary>The opening session reaches creek 2 at 5 minutes and creek 3 at 10 (design doc 10).</summary>
        public const double FirstSessionMinutes = 10;

        public const double RegionShare = 0.6;
        public const double TierShare = 0.25;
        public const double UpgradeBaseShare = 0.03;
        public const double AmosCostIdleSeconds = 45 * 60;

        /// <summary>Opening days of the old 10-creek curve; they sit on creek indices 0, 2 ... 18.</summary>
        static readonly double[] OldCurveDays = { 0, FirstSessionMinutes / 1440, 0.6, 3, 8, 20, 40, 70, 110, 160 };

        /// <summary>Last creek: the curve's extension past the old last point.</summary>
        const double LastCreekDay = 190;

        /// <summary>
        /// Creek 4 opens on the first return. Its geometric slot (~1.5 h) falls between the
        /// opening session and the first return (4.5 h later in the day plan), where nobody plays.
        /// </summary>
        public const double FirstReturnDay = 0.195;

        /// <summary>Opening days per creek index, from the target curve.</summary>
        public static readonly double[] TargetDays = BuildTargetDays();

        /// <summary>
        /// Bot calibration per creek index: the bot playing the real core reaches creeks faster
        /// than this model, so each solved cost is scaled to land on its target day. Creeks 2-11
        /// are bisected with 30-day runs; creeks 12-20 are tuned together with 200-day runs
        /// (Balance --tune-late). Mirrors BOT_CALIBRATION in tools/economy_tune.py.
        /// </summary>
        public static readonly double[] BotCalibration =
        {
            1, 1.825, 2.973, 4.51, 4.987, 4.564, 4.505, 2.75, 2.005, 1.962,
            6.277, 6.309, 10.582, 8.392, 14.544, 6.786, 20.027, 6.042, 9.959, 11.352,
        };

        /// <summary>Last creek index the calibration solves; later creeks copy its scale.</summary>
        public const int LastCalibratedRegion = 10;

        readonly Economy economy;

        public PacingModel(Economy economy)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        static double[] BuildTargetDays()
        {
            var days = new double[20];
            for (int k = 0; k < OldCurveDays.Length; k++)
                days[2 * k] = OldCurveDays[k];
            days[1] = FirstSessionMinutes / 2 / 1440;
            for (int r = 3; r < 19; r += 2)
                days[r] = Math.Sqrt(days[r - 1] * days[r + 1]);
            days[3] = FirstReturnDay;
            days[19] = LastCreekDay;
            return days;
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

        /// <summary>Sluice tier a typical player owns on a creek.</summary>
        public static int TypicalTier(int regionIndex) => regionIndex / 2;

        /// <summary>
        /// Cumulative prestige multiplier of a typical player on a creek. Anchors from the old
        /// curve: x2.5 at Red Gulch, x6 at Frost Basin, x15 at Deep Canyon; x2.5 per old creek
        /// after that; creeks between two anchors take the geometric mean.
        /// </summary>
        public static double PrestigeFor(int regionIndex)
        {
            if (regionIndex % 2 == 1)
                return Math.Sqrt(PrestigeOnOldPoint(regionIndex - 1) * PrestigeOnOldPoint(regionIndex + 1));
            return PrestigeOnOldPoint(regionIndex);
        }

        static double PrestigeOnOldPoint(int regionIndex)
        {
            switch (regionIndex)
            {
                case 6: return 2.5;
                case 8: return 6.0;
                case 10: return 15.0;
            }
            return regionIndex < 6 ? 1.0 : 15.0 * Math.Pow(2.5, (regionIndex - 10) / 2.0);
        }

        static StatSheet TypicalStatsFor(int regionIndex)
        {
            int tier = TypicalTier(regionIndex);
            return TypicalStats(tier, Math.Min(4, tier + 2));
        }

        public BigNumber DayIncome(int regionIndex)
        {
            StatSheet stats = TypicalStatsFor(regionIndex);
            int tier = TypicalTier(regionIndex);
            double prestige = PrestigeFor(regionIndex);
            double spawnBase = economy.Config.SpawnBase;
            BigNumber active = economy.ActiveRate(regionIndex, tier, stats, prestige, spawnBase);
            BigNumber idle = economy.IdleRate(regionIndex, tier, stats, prestige, spawnBase);
            return active * ActiveSecondsPerDay + idle * IdleSecondsPerDay;
        }

        public const double ActiveCompareMinutes = 40;
        public const double AwayCompareHours = 24;

        /// <summary>
        /// Dollars from ActiveCompareMinutes of full active play divided by one
        /// AwayCompareHours absence (no ad double), for a typical player in regionIndex with
        /// Amos at the region's level cap. The design rule keeps this at 1 or more at every creek.
        /// </summary>
        public double ActiveToAwayRatio(int regionIndex)
        {
            StatSheet stats = TypicalStatsFor(regionIndex);
            int tier = TypicalTier(regionIndex);
            double prestige = PrestigeFor(regionIndex);
            double spawnBase = economy.SpawnBaseFor(regionIndex);
            BigNumber active = economy.ActiveRate(regionIndex, tier, stats, prestige, spawnBase) * (ActiveCompareMinutes * 60);
            double cap = economy.OfflineCapSeconds(economy.AmosMaxLevel(regionIndex + 1), stats);
            double away = AwayCompareHours * 3600;
            double paid = Math.Min(away, cap) + Math.Max(0, away - cap) * economy.Config.OfflinePastCapRate;
            BigNumber offline = economy.OfflineRate(regionIndex, tier, stats, prestige) * paid;
            return active.ToDouble() / offline.ToDouble();
        }

        /// <summary>
        /// Income of one half of the uninterrupted opening session. Creek 1 (first half): half
        /// the time with nothing bought, half with Creek Scatter L1 and Sturdy Shovel L2.
        /// Creek 2 (second half): those upgrades, at creek 2's value.
        /// </summary>
        public BigNumber FirstSessionIncome(int regionIndex)
        {
            double seconds = FirstSessionMinutes / 2 * 60;
            StatSheet late = TypicalStats(0, 1, 2);
            if (regionIndex == 0)
            {
                double spawnBase = economy.Config.SpawnBaseFirstRegion;
                BigNumber early = economy.ActiveRate(0, 0, TypicalStats(0, 0, 0), 1, spawnBase);
                return (early * 0.5 + economy.ActiveRate(0, 0, late, 1, spawnBase) * 0.5) * seconds;
            }
            return economy.ActiveRate(regionIndex, 0, late, 1, economy.SpawnBaseFor(regionIndex)) * seconds;
        }

        /// <summary>Unrounded unlock cost of creek index 1..19.</summary>
        public BigNumber SolveRegionCost(int regionIndex)
        {
            if (regionIndex <= 2)
                return FirstSessionIncome(regionIndex - 1) * (RegionShare * BotCalibration[regionIndex]);
            double window = TargetDays[regionIndex] - TargetDays[regionIndex - 1];
            return DayIncome(regionIndex - 1) * (window * RegionShare * BotCalibration[regionIndex]);
        }

        /// <summary>Unrounded cost of Amos level 2..12: 45 minutes of idle at the creek that
        /// unlocks it (level L with creek index L - 2).</summary>
        public BigNumber SolveAmosLevelCost(int level)
        {
            int regionIndex = level - 2;
            BigNumber idle = economy.IdleRate(regionIndex, TypicalTier(regionIndex), TypicalStatsFor(regionIndex), PrestigeFor(regionIndex), economy.Config.SpawnBase);
            return idle * AmosCostIdleSeconds;
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
