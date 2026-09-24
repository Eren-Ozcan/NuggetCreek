using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Economy formulas from design doc section 5. Region and tier indices are 0-based:
    /// region 0 is Nugget Creek, tier 0 is the starting sluice.
    /// tools/economy_tune.py is the reference implementation; both must agree.
    /// </summary>
    public sealed class Economy
    {
        public EconomyConfig Config { get; }

        public Economy(EconomyConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public double SpawnBaseFor(int regionIndex) =>
            regionIndex == 0 ? Config.SpawnBaseFirstRegion : Config.SpawnBase;

        /// <summary>Spawns per second after Creek Scatter, clamped to the swipe cadence cap.</summary>
        public double SpawnRate(double spawnBase, StatSheet stats) =>
            Math.Min(spawnBase * stats.Multiplier(Stat.SpawnRate), Config.SpawnCap);

        /// <summary>V(r): base value of one Gold Dust in a region.</summary>
        public BigNumber DustBaseValue(int regionIndex) =>
            BigNumber.Pow(Config.ValueGrowth, regionIndex);

        /// <summary>T(t): income multiplier of a sluice tier.</summary>
        public BigNumber TierMultiplier(int tierIndex) =>
            BigNumber.Pow(Config.TierMultiplier, tierIndex);

        /// <summary>Value of one Gold Dust with tier and upgrades, before the Nugget mix.</summary>
        public BigNumber DustValue(int regionIndex, int tierIndex, StatSheet stats) =>
            DustBaseValue(regionIndex) * TierMultiplier(tierIndex) * stats.Multiplier(Stat.DustValue);

        public double NuggetChance(StatSheet stats) =>
            Math.Min(1, Config.NuggetChanceBase + stats[Stat.NuggetChance]);

        public double NuggetValueMultiplier(StatSheet stats) =>
            Config.NuggetValueMultiplier * stats.Multiplier(Stat.NuggetValue);

        /// <summary>Expected Dollars per collected item: Gold Dust/Nugget mix, double catches, all-income and prestige.</summary>
        public BigNumber CollectValue(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier)
        {
            double mix = 1 + NuggetChance(stats) * (NuggetValueMultiplier(stats) - 1);
            double factor = mix * stats.Multiplier(Stat.DoubleCatch) * stats.Multiplier(Stat.AllIncome) * prestigeMultiplier;
            return DustValue(regionIndex, tierIndex, stats) * factor;
        }

        /// <summary>Manual collection income per second. Idle income derives from this, not part of it.</summary>
        public BigNumber ActiveRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier) =>
            ActiveRate(regionIndex, tierIndex, stats, prestigeMultiplier, SpawnBaseFor(regionIndex));

        public BigNumber ActiveRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier, double spawnBase) =>
            BaseRate(regionIndex, tierIndex, stats, prestigeMultiplier, spawnBase) * stats.Multiplier(Stat.ActiveIncome);

        /// <summary>Crew idle collection per second (needs Amos).</summary>
        public BigNumber IdleRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier) =>
            IdleRate(regionIndex, tierIndex, stats, prestigeMultiplier, SpawnBaseFor(regionIndex));

        public BigNumber IdleRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier, double spawnBase) =>
            BaseRate(regionIndex, tierIndex, stats, prestigeMultiplier, spawnBase) * (stats.Multiplier(Stat.IdleSpeed) / Config.ActiveIdleRatio);

        /// <summary>Idle rate while the app is closed.</summary>
        public BigNumber OfflineRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier) =>
            IdleRate(regionIndex, tierIndex, stats, prestigeMultiplier) * stats.Multiplier(Stat.OfflineIncome);

        BigNumber BaseRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier, double spawnBase) =>
            CollectValue(regionIndex, tierIndex, stats, prestigeMultiplier) * SpawnRate(spawnBase, stats);

        public double CollectibleLifetimeSeconds(StatSheet stats) =>
            Config.CollectibleLifetimeSeconds + stats[Stat.CollectibleLifetime];

        /// <summary>Collectibles between Mother Lodes after frequency bonuses.</summary>
        public int MotherLodeEveryCollectibles(StatSheet stats) =>
            (int)Math.Ceiling(Config.MotherLodeEveryCollectibles * stats.CostMultiplier(Stat.MotherLodeFrequency));

        // --- Prices ---

        /// <summary>Price of an upgrade level (1-based): tier base * growth^(level-1).</summary>
        public BigNumber UpgradeCost(UpgradeDefinition upgrade, int level, StatSheet stats)
        {
            if (level < 1 || level > upgrade.MaxLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            BigNumber raw = BigNumber.Pow(Config.UpgradeCostGrowth, level - 1) * Config.UpgradeBaseCosts[upgrade.TierIndex];
            return raw * stats.CostMultiplier(Stat.UpgradeCost);
        }

        public BigNumber RegionUnlockCost(int regionIndex, StatSheet stats) =>
            BigNumber.FromDouble(Config.RegionUnlockCosts[regionIndex]) * stats.CostMultiplier(Stat.RegionCost);

        public BigNumber TierCost(int tierIndex, StatSheet stats) =>
            BigNumber.FromDouble(Config.TierCosts[tierIndex]) * stats.CostMultiplier(Stat.TierCost);

        /// <summary>Gem price of the next crew hire; null once all 15 are hired.</summary>
        public int? CrewHireCost(int alreadyHired) =>
            alreadyHired >= 0 && alreadyHired < Config.CrewHireCosts.Length ? Config.CrewHireCosts[alreadyHired] : (int?)null;

        /// <summary>Gem price to raise a crew member from currentLevel to currentLevel+1; null at max.</summary>
        public int? CrewLevelUpCost(int currentLevel) =>
            currentLevel >= 1 && currentLevel <= Config.CrewLevelUpCosts.Length ? Config.CrewLevelUpCosts[currentLevel - 1] : (int?)null;

        // --- Prestige ---

        /// <summary>prospecting_xp = floor((total_earned / 1e6) ^ 0.45) * (1 + XP bonus)</summary>
        public long ProspectingXp(BigNumber totalEarned, StatSheet stats = null)
        {
            if (totalEarned <= BigNumber.Zero)
                return 0;
            double xp = BigNumber.Pow(totalEarned / Config.PrestigeXpDivisor, Config.PrestigeXpExponent).ToDouble();
            if (stats != null)
                xp *= stats.Multiplier(Stat.ProspectingXp);
            // Log/pow round-trips land a hair under exact integers (1e6^0.45 style inputs).
            return (long)Math.Floor(xp + 1e-9);
        }

        /// <summary>Permanent multiplier = 1 + 0.02 * xp.</summary>
        public double PrestigeMultiplier(long prospectingXp) => 1 + Config.PrestigeBonusPerXp * prospectingXp;

        // --- Amos and offline cap ---

        /// <summary>Amos' level ceiling: 2 per unlocked region, never above the absolute max.</summary>
        public int AmosMaxLevel(int regionsUnlocked) =>
            Math.Min(Config.AmosMaxLevel, Config.AmosLevelsPerRegion * Math.Max(1, regionsUnlocked));

        /// <summary>Offline cap in seconds. Level 0 means Amos has not joined: no idle, no offline.</summary>
        public double OfflineCapSeconds(int amosLevel)
        {
            int level = Math.Max(0, Math.Min(amosLevel, Config.AmosMaxLevel));
            return level * Config.OfflineCapHoursPerAmosLevel * 3600;
        }

        /// <summary>Dollar price to reach targetLevel (2..12); level 1 is the join cost.</summary>
        public BigNumber AmosLevelCost(int targetLevel)
        {
            if (targetLevel == 1)
                return Config.AmosJoinCost;
            if (targetLevel < 1 || targetLevel - 2 >= Config.AmosLevelCosts.Length)
                throw new ArgumentOutOfRangeException(nameof(targetLevel));
            return Config.AmosLevelCosts[targetLevel - 2];
        }
    }
}
