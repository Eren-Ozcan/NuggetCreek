using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    public enum CollectibleKind
    {
        GoldDust,
        Nugget,
    }

    /// <summary>
    /// Game rules over a <see cref="PlayerProgress"/>: collecting, idle and offline income,
    /// every Dollar purchase (upgrades, sluice tiers, regions, Amos), Gems, crew and goals.
    /// The platform layer feeds it random rolls, time deltas and clock readings; it never
    /// reads them itself.
    /// </summary>
    public sealed class GameSession
    {
        public Economy Economy { get; }
        public PlayerProgress Progress { get; }
        public StatSheet Stats { get; } = new StatSheet();

        readonly OfflineEarnings offline;
        readonly Random random;

        /// <param name="random">Draws crew candidates; pass a seeded one in tests.</param>
        public GameSession(Economy economy, PlayerProgress progress, Random random = null)
        {
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            Progress.Normalize();
            offline = new OfflineEarnings(economy.Config);
            this.random = random ?? new Random();
            RebuildStats();
        }

        EconomyConfig Config => Economy.Config;

        public void RebuildStats()
        {
            Stats.Clear();
            GameCatalog.ApplyUpgrades(Stats, Progress.UpgradeLevels);
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                GameCatalog.ApplyCrew(Stats, GameCatalog.Crew[i], Progress.CrewLevels[i]);
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
            Progress.CollectedSinceMotherLode++;
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
            if (Progress.RegionIndex >= Config.CrewCandidateFirstRegion)
                OfferCandidates();
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

        // --- Mother Lode (design doc 3.4) ---

        /// <summary>Counts play time toward the Mother Lode; call every frame while the creek runs.</summary>
        public void TickPlay(double deltaSeconds)
        {
            if (deltaSeconds <= 0)
                return;
            Progress.PlaySeconds += deltaSeconds;
            Progress.SecondsSinceMotherLode += deltaSeconds;
        }

        /// <summary>Dollars per second right now from swiping and the crew together.</summary>
        public BigNumber IncomePerSecond =>
            Economy.ActiveRate(Progress.RegionIndex, Progress.TierIndex, Stats, PrestigeMultiplier) + IdleRate;

        /// <summary>After 400 catches (fewer with Wren) or 10 minutes of play, whichever first.</summary>
        public bool MotherLodeDue =>
            Progress.PlaySeconds >= Config.MotherLodeFirstAfterSeconds
            && (Progress.CollectedSinceMotherLode >= Economy.MotherLodeEveryCollectibles(Stats)
                || Progress.SecondsSinceMotherLode >= Config.MotherLodeEverySeconds);

        /// <summary>Starts the event and restarts both counters; the income it pays is fixed now.</summary>
        public MotherLodeRun StartMotherLode()
        {
            Progress.CollectedSinceMotherLode = 0;
            Progress.SecondsSinceMotherLode = 0;
            return new MotherLodeRun(Config, IncomePerSecond);
        }

        /// <summary>Starts a Mother Lode at once for Gems; null when the player cannot pay.</summary>
        public MotherLodeRun SummonMotherLode() =>
            TrySpendGems(Config.MotherLodeSummonGems) ? StartMotherLode() : null;

        public BigNumber MotherLodeReward(MotherLodeRun run) =>
            Economy.MotherLodeReward(run.IncomePerSecond, run.PeakCombo, Stats);

        /// <summary>Pays a run once, even if it was cut short; returns the Dollars paid.</summary>
        public BigNumber FinishMotherLode(MotherLodeRun run)
        {
            if (run.IsPaid)
                return BigNumber.Zero;
            run.End();
            BigNumber reward = MotherLodeReward(run);
            Earn(reward);
            EarnGems(Config.MotherLodeGemReward);
            return reward;
        }

        // --- Gems ---

        public void EarnGems(int amount)
        {
            if (amount > 0)
                Progress.Gems += amount;
        }

        public bool CanAffordGems(int? cost) => cost.HasValue && Progress.Gems >= cost.Value;

        bool TrySpendGems(int? cost)
        {
            if (!CanAffordGems(cost))
                return false;
            Progress.Gems -= cost.Value;
            return true;
        }

        // --- Crew (design doc 6.3) ---

        public int CrewLevel(int index) => Progress.CrewLevels[index];

        public int CrewHiredCount
        {
            get
            {
                int count = 0;
                foreach (int level in Progress.CrewLevels)
                    if (level > 0)
                        count++;
                return count;
            }
        }

        /// <summary>Gem price of the next hire, whoever it is; null once everyone is hired.</summary>
        public int? CrewHireCost => Economy.CrewHireCost(CrewHiredCount);

        /// <summary>Gem price of the next level for this member; null when not hired or maxed.</summary>
        public int? CrewLevelUpCost(int index)
        {
            int level = Progress.CrewLevels[index];
            return level >= 1 && level < GameCatalog.CrewMaxLevel ? Economy.CrewLevelUpCost(level) : null;
        }

        public bool LevelUpCrew(int index)
        {
            if (!TrySpendGems(CrewLevelUpCost(index)))
                return false;
            Progress.CrewLevels[index]++;
            RebuildStats();
            return true;
        }

        public bool HasCandidates => Progress.CrewCandidates.Length > 0;

        public IReadOnlyList<int> Candidates => Progress.CrewCandidates;

        /// <summary>
        /// Opens a candidate event: draws from the members not hired yet. A pair still on
        /// offer goes back to the pool first, so the newest creek always brings a fresh pair.
        /// </summary>
        public void OfferCandidates()
        {
            var pool = new List<int>();
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                if (Progress.CrewLevels[i] == 0)
                    pool.Add(i);
            int count = Math.Min(Config.CrewCandidatesPerEvent, pool.Count);
            var picked = new int[count];
            for (int i = 0; i < count; i++)
            {
                int draw = random.Next(pool.Count);
                picked[i] = pool[draw];
                pool.RemoveAt(draw);
            }
            Progress.CrewCandidates = picked;
            Progress.CandidateSecondsLeft = count > 0 ? Config.CrewCandidateWindowSeconds : 0;
        }

        /// <summary>Hires one of the candidates on offer; the other one returns to the pool.</summary>
        public bool HireCandidate(int crewIndex)
        {
            if (Array.IndexOf(Progress.CrewCandidates, crewIndex) < 0 || !TrySpendGems(CrewHireCost))
                return false;
            Progress.CrewLevels[crewIndex] = 1;
            CloseCandidates();
            RebuildStats();
            return true;
        }

        public void PassCandidates() => CloseCandidates();

        /// <summary>Runs the candidate window down while the game is open; returns true when it just expired.</summary>
        public bool TickCandidates(double deltaSeconds)
        {
            if (!HasCandidates || deltaSeconds <= 0)
                return false;
            Progress.CandidateSecondsLeft -= deltaSeconds;
            if (Progress.CandidateSecondsLeft > 0)
                return false;
            CloseCandidates();
            return true;
        }

        void CloseCandidates()
        {
            Progress.CrewCandidates = new int[0];
            Progress.CandidateSecondsLeft = 0;
        }

        // --- Progress goals ---

        /// <summary>The active goal, or null once the chain is done.</summary>
        public GoalDefinition CurrentGoal =>
            Progress.GoalIndex < GameCatalog.Goals.Count ? GameCatalog.Goals[Progress.GoalIndex] : null;

        public int CurrentGoalReward =>
            Progress.GoalIndex < Config.GoalGemRewards.Length ? Config.GoalGemRewards[Progress.GoalIndex] : 0;

        public long GoalProgress(GoalDefinition goal)
        {
            switch (goal.Kind)
            {
                case GoalKind.ManualCollected: return Progress.ManualCollected;
                case GoalKind.UpgradeLevels:
                    long total = 0;
                    foreach (int level in Progress.UpgradeLevels)
                        total += level;
                    return total;
                case GoalKind.AmosLevel: return Progress.AmosLevel;
                case GoalKind.RegionsUnlocked: return Progress.RegionsUnlocked;
                case GoalKind.SluiceTier: return Progress.TierIndex + 1;
                case GoalKind.CrewHired: return CrewHiredCount;
                default: throw new ArgumentOutOfRangeException(nameof(goal));
            }
        }

        public bool IsCurrentGoalComplete
        {
            get
            {
                GoalDefinition goal = CurrentGoal;
                return goal != null && GoalProgress(goal) >= goal.Target;
            }
        }

        /// <summary>Pays the finished goal in Gems and moves to the next one.</summary>
        public bool ClaimGoal()
        {
            if (!IsCurrentGoalComplete)
                return false;
            EarnGems(CurrentGoalReward);
            Progress.GoalIndex++;
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
