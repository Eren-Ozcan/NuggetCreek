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

        /// <summary>Share of Nugget spawns that come up Giant (rolled before Rich).</summary>
        public double GiantNuggetChance(StatSheet stats) =>
            Math.Min(1, Config.GiantNuggetChanceBase + stats[Stat.GiantNuggetChance]);

        /// <summary>Share of the remaining Nugget spawns that come up Rich.</summary>
        public double RichNuggetChance(StatSheet stats) =>
            Math.Min(1, Config.RichNuggetChanceBase + stats[Stat.RichNuggetChance]);

        /// <summary>Giant Nugget value in Nuggets.</summary>
        public double GiantNuggetValueMultiplier(StatSheet stats) =>
            Config.GiantNuggetValueMultiplier * stats.Multiplier(Stat.GiantNuggetValue);

        /// <summary>Rich Nugget value in Nuggets.</summary>
        public double RichNuggetValueMultiplier(StatSheet stats) =>
            Config.RichNuggetValueMultiplier * stats.Multiplier(Stat.RichNuggetValue);

        /// <summary>Expected value of one Nugget spawn in Nuggets: Giant first, then Rich, else plain.</summary>
        public double NuggetLayerFactor(StatSheet stats)
        {
            double giant = GiantNuggetChance(stats);
            double rich = RichNuggetChance(stats);
            return giant * GiantNuggetValueMultiplier(stats) + (1 - giant) * (1 + rich * (RichNuggetValueMultiplier(stats) - 1));
        }

        public double CritChance(StatSheet stats) =>
            Math.Min(1, Config.CritChanceBase + stats[Stat.CritChance]);

        public double CritValueMultiplier(StatSheet stats) =>
            Config.CritValueMultiplier * stats.Multiplier(Stat.CritValue);

        /// <summary>Expected factor critical catches add to manual income.</summary>
        public double CritFactor(StatSheet stats) =>
            1 + CritChance(stats) * (CritValueMultiplier(stats) - 1);

        /// <summary>Vein level ceiling; zero means the vein is closed.</summary>
        public int VeinMaxLevel(StatSheet stats) =>
            Math.Max(0, Config.VeinMaxLevelBase + (int)Math.Round(stats[Stat.VeinMaxLevel]));

        /// <summary>Manual catches in a row per vein level, never below 1.</summary>
        public int VeinCatchesPerLevel(StatSheet stats) =>
            Math.Max(1, Config.VeinCatchesPerLevel + (int)Math.Round(stats[Stat.VeinCatchesPerLevel]));

        public double VeinMultiplier(int veinLevel) => 1 + Config.VeinValuePerLevel * Math.Max(0, veinLevel);

        /// <summary>
        /// Expected Dollars per collected item: Gold Dust/Nugget mix (Rich and Giant included),
        /// double catches, all-income and prestige. Critical catches are manual only, see ActiveRate.
        /// </summary>
        public BigNumber CollectValue(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier)
        {
            double mix = 1 + NuggetChance(stats) * (NuggetValueMultiplier(stats) * NuggetLayerFactor(stats) - 1);
            double factor = mix * stats.Multiplier(Stat.DoubleCatch) * stats.Multiplier(Stat.AllIncome) * prestigeMultiplier;
            return DustValue(regionIndex, tierIndex, stats) * factor;
        }

        /// <summary>
        /// Manual collection income per second, critical catches included. The vein bonus is
        /// left out: it depends on skill and is closed before perks.
        /// </summary>
        public BigNumber ActiveRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier) =>
            ActiveRate(regionIndex, tierIndex, stats, prestigeMultiplier, SpawnBaseFor(regionIndex));

        public BigNumber ActiveRate(int regionIndex, int tierIndex, StatSheet stats, double prestigeMultiplier, double spawnBase) =>
            BaseRate(regionIndex, tierIndex, stats, prestigeMultiplier, spawnBase) * (stats.Multiplier(Stat.ActiveIncome) * CritFactor(stats));

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

        /// <summary>
        /// Mother Lode payout: 60 s of income at combo x1 rising linearly to 90 s at the max
        /// combo (design doc 3.4), then the Mother Lode reward bonus (Vein Detector, Tobias).
        /// </summary>
        public BigNumber MotherLodeReward(BigNumber incomePerSecond, int peakCombo, StatSheet stats)
        {
            int maxCombo = Math.Max(2, Config.MotherLodeMaxCombo);
            double t = (Math.Max(1, Math.Min(peakCombo, maxCombo)) - 1) / (double)(maxCombo - 1);
            double seconds = Config.MotherLodeRewardMinSeconds + (Config.MotherLodeRewardMaxSeconds - Config.MotherLodeRewardMinSeconds) * t;
            return incomePerSecond * (seconds * stats.Multiplier(Stat.MotherLodeReward));
        }

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

        /// <summary>Permanent multiplier = 1 + 0.02 * xp * (1 + 0.01 * collection stars).</summary>
        public double PrestigeMultiplier(long prospectingXp, int stars = 0) =>
            1 + Config.PrestigeBonusPerXp * prospectingXp * (1 + Config.StarPrestigeBonus * Math.Max(0, stars));

        // --- Nugget collection ---

        /// <summary>Stars a Nugget type has after this many manual catches.</summary>
        public int NuggetStars(int catches)
        {
            int stars = 0;
            foreach (int threshold in Config.NuggetStarThresholds)
                if (catches >= threshold)
                    stars++;
            return stars;
        }

        public int MaxStarsPerNugget => Config.NuggetStarThresholds.Length;

        /// <summary>Catches needed for the next star, or null at max stars.</summary>
        public int? NextStarAt(int catches)
        {
            foreach (int threshold in Config.NuggetStarThresholds)
                if (catches < threshold)
                    return threshold;
            return null;
        }

        public double NuggetWeight(Rarity rarity) => Config.RarityWeights[(int)rarity];

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
