using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Every tunable economy number. Defaults are the launch values from the design doc;
    /// Remote Config overrides them at session start (never mid-session). Field comments
    /// name the Remote Config key where one exists.
    /// </summary>
    [Serializable]
    public sealed class EconomyConfig
    {
        // --- Collection (design doc 3.1, 5.0.1) ---

        /// <summary>Surface spawns per second outside region 1. RC: spawn_base.</summary>
        public double SpawnBase = 0.8;

        /// <summary>Region 1 runs hotter so the tutorial reads clearly.</summary>
        public double SpawnBaseFirstRegion = 1.2;

        /// <summary>Measured human swipe cadence caps useful spawns. RC: spawn_cap.</summary>
        public double SpawnCap = 1.5;

        /// <summary>Seconds a collectible stays on screen before it is lost.</summary>
        public double CollectibleLifetimeSeconds = 3;

        public double NuggetChanceBase = 0.05;

        /// <summary>The first spawns are always Gold Dust so the tutorial reads clearly (design doc 9).</summary>
        public int OnboardingGuaranteedDust = 5;

        /// <summary>Base swipe pick-up radius in reference-screen pixels; Wide Pan and Big Pan scale it.</summary>
        public double CollectRadiusPixels = 70;

        // --- Mother Lode (3.4) ---

        /// <summary>Mother Lode triggers after this many collectibles or MotherLodeEverySeconds, whichever first.</summary>
        public int MotherLodeEveryCollectibles = 400;

        public double MotherLodeEverySeconds = 10 * 60;

        /// <summary>A Nugget is worth this many Gold Dust of the same region.</summary>
        public double NuggetValueMultiplier = 8.0;

        /// <summary>Active income divided by this gives idle income. RC: active_idle_ratio.</summary>
        public double ActiveIdleRatio = 3.0;

        /// <summary>Base collectible value multiplier per region. RC: value_growth.</summary>
        public double ValueGrowth = 15.0;

        /// <summary>Income multiplier per sluice tier. RC: tier_mult.</summary>
        public double TierMultiplier = 1.6;

        // --- Progression tables (5.0.4, 6.1, 6.2), solved by tools/economy_tune.py ---

        /// <summary>Region unlock costs for the 6 launch regions; index 0 is free.</summary>
        public double[] RegionUnlockCosts = { 0, 910, 950e3, 170e6, 34e9, 12e12 };

        /// <summary>Sluice tier costs for the 8 launch tiers; index 0 is the starting sluice.</summary>
        public double[] TierCosts = { 0, 230, 240e3, 42e6, 8.5e9, 3e12, 620e12, 130e15 };

        /// <summary>Level 1 price of each tier's two upgrades (3% of the tier cost, floored at 2x the previous).</summary>
        public double[] UpgradeBaseCosts = { 30, 60, 7.2e3, 1.3e6, 260e6, 90e9, 19e12, 3.9e15 };

        /// <summary>Upgrade price growth per level. RC: upgrade_growth.</summary>
        public double UpgradeCostGrowth = 1.22;

        // --- Prestige (5.0.3) ---

        public double PrestigeXpDivisor = 1e6;
        public double PrestigeXpExponent = 0.45;
        public double PrestigeBonusPerXp = 0.02;

        // --- Offline and Amos (3.3, 3.3.2, 3.3.3) ---

        /// <summary>Dollars at which Amos joins and idle collection starts.</summary>
        public double AmosJoinCost = 70;

        /// <summary>Amos level costs for L2..L12: 45 minutes of typical idle income when the
        /// level unlocks, the second level of each region pair at 1.5x.</summary>
        public double[] AmosLevelCosts = { 4.4e3, 180e3, 280e3, 8.3e6, 12e6, 790e6, 1.2e9, 120e9, 170e9, 15e12, 22e12 };

        public double OfflineCapHoursPerAmosLevel = 1;
        public int AmosMaxLevel = 12;
        public int AmosLevelsPerRegion = 2;

        // --- Crew (6.3) ---

        /// <summary>Gem price of the next hire, indexed by crew already hired (Amos excluded).</summary>
        public int[] CrewHireCosts = { 10, 15, 20, 30, 45, 65, 95, 135, 195, 285, 410, 595, 865, 900, 900 };

        /// <summary>Gem price to go from level i+1 to i+2 (L1 to L2 first, L9 to L10 last).</summary>
        public int[] CrewLevelUpCosts = { 6, 9, 13, 19, 27, 39, 57, 82, 119 };

        // --- Offline return (3.3.2, 13.1) ---

        /// <summary>RC: offline_gem_double_per_hour.</summary>
        public int OfflineGemDoublePerHour = 5;

        /// <summary>The Gem double is hidden for absences shorter than this.</summary>
        public double OfflineGemDoubleMinAwaySeconds = 30 * 60;

        /// <summary>Shorter absences are credited quietly, without the welcome back modal.</summary>
        public double OfflineModalMinAwaySeconds = 60;

        /// <summary>
        /// Allowed drift between wall-clock and monotonic deltas before an absence counts as
        /// clock tampering. Covers NTP corrections and suspend accounting noise.
        /// </summary>
        public double ClockTamperToleranceSeconds = 120;

        // --- Rewarded ads (3.3.1) ---

        /// <summary>RC: rv_load_timeout_s.</summary>
        public double RewardedLoadTimeoutSeconds = 10;

        /// <summary>RC: late_double_window_s.</summary>
        public double LateDoubleWindowSeconds = 5 * 60;

        public int RegionCount => RegionUnlockCosts.Length;
        public int TierCount => TierCosts.Length;
    }
}
