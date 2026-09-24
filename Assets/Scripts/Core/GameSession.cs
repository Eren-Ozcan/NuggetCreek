using System;

namespace NuggetCreek.Core
{
    public enum CollectibleKind
    {
        GoldDust,
        Nugget,
    }

    /// <summary>
    /// Game rules over a <see cref="PlayerProgress"/>: collecting, idle and offline income,
    /// and every Dollar purchase (upgrades, sluice tiers, regions, Amos). The platform layer
    /// feeds it random rolls, time deltas and clock readings; it never reads them itself.
    /// </summary>
    public sealed class GameSession
    {
        public Economy Economy { get; }
        public PlayerProgress Progress { get; }
        public StatSheet Stats { get; } = new StatSheet();

        readonly OfflineEarnings offline;

        public GameSession(Economy economy, PlayerProgress progress)
        {
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Progress.Normalize();
            offline = new OfflineEarnings(economy.Config);
            RebuildStats();
        }

        EconomyConfig Config => Economy.Config;

        public void RebuildStats()
        {
            Stats.Clear();
            GameCatalog.ApplyUpgrades(Stats, Progress.UpgradeLevels);
        }

        public double PrestigeMultiplier => Economy.PrestigeMultiplier(Progress.ProspectingXp);

        public string RegionName => GameCatalog.RegionNames[Progress.RegionIndex];

        // --- Collecting ---

        public double SpawnRate => Economy.SpawnRate(Economy.SpawnBaseFor(Progress.RegionIndex), Stats);

        public double CollectibleLifetimeSeconds => Economy.CollectibleLifetimeSeconds(Stats);

        public double CollectRadiusPixels => Config.CollectRadiusPixels * Stats.Multiplier(Stat.CollectRadius);

        /// <summary>Kind of the next spawn. roll is uniform in [0, 1).</summary>
        public CollectibleKind RollKind(double roll)
        {
            if (Progress.ManualCollected < Config.OnboardingGuaranteedDust)
                return CollectibleKind.GoldDust;
            return roll < Economy.NuggetChance(Stats) ? CollectibleKind.Nugget : CollectibleKind.GoldDust;
        }

        /// <summary>Whether a catch counts twice (Water Channel, Gus). roll is uniform in [0, 1).</summary>
        public bool RollDoubleCatch(double roll) => roll < Stats[Stat.DoubleCatch];

        /// <summary>
        /// Dollars for one manual catch. Averaged over the rolls this equals
        /// <see cref="Economy.CollectValue"/> times the active income bonus, so
        /// SpawnRate * average = <see cref="Economy.ActiveRate(int,int,StatSheet,double)"/>.
        /// </summary>
        public BigNumber CatchValue(CollectibleKind kind, bool doubleCatch)
        {
            double factor = kind == CollectibleKind.Nugget ? Economy.NuggetValueMultiplier(Stats) : 1;
            if (doubleCatch)
                factor *= 2;
            factor *= Stats.Multiplier(Stat.AllIncome) * PrestigeMultiplier * Stats.Multiplier(Stat.ActiveIncome);
            return Economy.DustValue(Progress.RegionIndex, Progress.TierIndex, Stats) * factor;
        }

        /// <summary>Credits a manual catch and returns its value.</summary>
        public BigNumber Collect(CollectibleKind kind, bool doubleCatch)
        {
            BigNumber value = CatchValue(kind, doubleCatch);
            Progress.ManualCollected++;
            Earn(value);
            return value;
        }

        public void Earn(BigNumber amount)
        {
            if (amount <= BigNumber.Zero)
                return;
            Progress.Dollars += amount;
            Progress.TotalEarned += amount;
        }

        // --- Idle and offline ---

        public bool IdleActive => Progress.AmosLevel > 0;

        public BigNumber IdleRate =>
            IdleActive ? Economy.IdleRate(Progress.RegionIndex, Progress.TierIndex, Stats, PrestigeMultiplier) : BigNumber.Zero;

        public BigNumber OfflineRate =>
            IdleActive ? Economy.OfflineRate(Progress.RegionIndex, Progress.TierIndex, Stats, PrestigeMultiplier) : BigNumber.Zero;

        /// <summary>Idle income while the game is open; returns what was earned.</summary>
        public BigNumber TickIdle(double deltaSeconds)
        {
            if (!IdleActive || deltaSeconds <= 0)
                return BigNumber.Zero;
            BigNumber earned = IdleRate * deltaSeconds;
            Earn(earned);
            return earned;
        }

        public double OfflineCapSeconds => Economy.OfflineCapSeconds(Progress.AmosLevel);

        public OfflineResult EvaluateOffline(OfflineClockInput clock) =>
            offline.Evaluate(clock, OfflineCapSeconds, OfflineRate);

        /// <summary>Pays a trusted offline result; multiplier is 2 after a rewarded ad.</summary>
        public bool ClaimOffline(OfflineResult result, int multiplier)
        {
            if (!result.IsPayable || multiplier < 1)
                return false;
            Earn(result.Amount * multiplier);
            return true;
        }

        // --- Sluice upgrades ---

        public bool IsUpgradeUnlocked(int index) => GameCatalog.Upgrades[index].TierIndex <= Progress.TierIndex;

        public int UpgradeLevel(int index) => Progress.UpgradeLevels[index];

        /// <summary>Price of the next level, or null when maxed.</summary>
        public BigNumber? UpgradeCost(int index)
        {
            UpgradeDefinition upgrade = GameCatalog.Upgrades[index];
            int next = Progress.UpgradeLevels[index] + 1;
            return next > upgrade.MaxLevel ? (BigNumber?)null : Economy.UpgradeCost(upgrade, next, Stats);
        }

        public bool BuyUpgrade(int index)
        {
            if (!IsUpgradeUnlocked(index) || !TrySpend(UpgradeCost(index)))
                return false;
            Progress.UpgradeLevels[index]++;
            RebuildStats();
            return true;
        }

        // --- Sluice tiers ---

        public bool HasNextTier => Progress.TierIndex + 1 < Config.TierCount;

        /// <summary>
        /// Tier t is bought in region t (design doc 6.1: tier 2 in Pine Hollow). Tiers past
        /// the last launch region open once that region is unlocked.
        /// </summary>
        public bool IsNextTierUnlocked
        {
            get
            {
                if (!HasNextTier)
                    return false;
                int highestRegion = Progress.RegionsUnlocked - 1;
                return Progress.TierIndex + 1 <= highestRegion || Progress.RegionsUnlocked >= Config.RegionCount;
            }
        }

        public BigNumber? NextTierCost => HasNextTier ? Economy.TierCost(Progress.TierIndex + 1, Stats) : (BigNumber?)null;

        public bool BuyNextTier()
        {
            if (!IsNextTierUnlocked || !TrySpend(NextTierCost))
                return false;
            Progress.TierIndex++;
            return true;
        }

        // --- Regions ---

        public bool HasNextRegion => Progress.RegionsUnlocked < Config.RegionCount;

        public BigNumber? NextRegionCost =>
            HasNextRegion ? Economy.RegionUnlockCost(Progress.RegionsUnlocked, Stats) : (BigNumber?)null;

        /// <summary>Unlocks the next creek and moves the player there.</summary>
        public bool UnlockNextRegion()
        {
            if (!HasNextRegion || !TrySpend(NextRegionCost))
                return false;
            Progress.RegionIndex = Progress.RegionsUnlocked;
            Progress.RegionsUnlocked++;
            return true;
        }

        public bool TravelTo(int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= Progress.RegionsUnlocked)
                return false;
            Progress.RegionIndex = regionIndex;
            return true;
        }

        // --- Amos ---

        public int AmosLevelCap => Economy.AmosMaxLevel(Progress.RegionsUnlocked);

        /// <summary>Price of Amos' next level (level 1 = joining), or null when capped by regions or maxed.</summary>
        public BigNumber? AmosNextCost =>
            Progress.AmosLevel < AmosLevelCap ? Economy.AmosLevelCost(Progress.AmosLevel + 1) : (BigNumber?)null;

        /// <summary>True when the next Amos level waits for another creek rather than for Dollars.</summary>
        public bool AmosWaitsForRegion => Progress.AmosLevel >= AmosLevelCap && Progress.AmosLevel < Config.AmosMaxLevel;

        public bool BuyAmosLevel()
        {
            if (!TrySpend(AmosNextCost))
                return false;
            Progress.AmosLevel++;
            return true;
        }

        public bool CanAfford(BigNumber? cost) => cost.HasValue && Progress.Dollars >= cost.Value;

        bool TrySpend(BigNumber? cost)
        {
            if (!CanAfford(cost))
                return false;
            Progress.Dollars -= cost.Value;
            return true;
        }
    }
}
