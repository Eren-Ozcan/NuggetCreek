using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    public enum CollectibleKind
    {
        GoldDust,
        Nugget,
        RichNugget,
        GiantNugget,
    }

    /// <summary>What a Nugget catch did to the collection.</summary>
    public readonly struct NuggetCatch
    {
        public readonly int Index;
        public readonly bool Discovered;
        public readonly int StarsGained;

        public NuggetCatch(int index, bool discovered, int starsGained)
        {
            Index = index;
            Discovered = discovered;
            StarsGained = starsGained;
        }
    }

    /// <summary>
    /// Game rules over a <see cref="PlayerProgress"/>: collecting, idle and offline income,
    /// every Dollar purchase (upgrades, sluice tiers, regions, Amos), Gems, crew and goals.
    /// The platform layer feeds it random rolls, time deltas and clock readings; it never
    /// reads them itself.
    /// </summary>
    public sealed partial class GameSession
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
            ApplyEquippedGear();
            ApplyGuild();
        }

        public double PrestigeMultiplier =>
            Economy.PrestigeMultiplier(Progress.ProspectingXp, TotalStars) * GuildIncomeMultiplier;

        public string RegionName => GameCatalog.RegionNames[Progress.RegionIndex];

        // --- Collecting ---

        public double SpawnRate => Economy.SpawnRate(Economy.SpawnBaseFor(Progress.RegionIndex), Stats);

        public double CollectibleLifetimeSeconds => Economy.CollectibleLifetimeSeconds(Stats);

        public double CollectRadiusPixels => Config.CollectRadiusPixels * Stats.Multiplier(Stat.CollectRadius);

        /// <summary>
        /// Kind of the next spawn. roll is uniform in [0, 1). A Nugget spawn comes up Giant
        /// first, then Rich, else plain (design doc 3.1.1), all from the same roll.
        /// </summary>
        public CollectibleKind RollKind(double roll)
        {
            if (Progress.ManualCollected < Config.OnboardingGuaranteedDust)
                return CollectibleKind.GoldDust;
            double nugget = Economy.NuggetChance(Stats);
            if (roll >= nugget)
                return CollectibleKind.GoldDust;
            double giant = nugget * Economy.GiantNuggetChance(Stats);
            if (roll < giant)
                return CollectibleKind.GiantNugget;
            double rich = giant + (nugget - giant) * Economy.RichNuggetChance(Stats);
            return roll < rich ? CollectibleKind.RichNugget : CollectibleKind.Nugget;
        }

        /// <summary>Whether a catch counts twice (Water Channel, Gus). roll is uniform in [0, 1).</summary>
        public bool RollDoubleCatch(double roll) => roll < Stats[Stat.DoubleCatch];

        /// <summary>Whether a manual catch is critical. roll is uniform in [0, 1).</summary>
        public bool RollCritical(double roll) => roll < Economy.CritChance(Stats);

        /// <summary>Value of a collectible kind in Gold Dust of the current region.</summary>
        public double KindMultiplier(CollectibleKind kind)
        {
            switch (kind)
            {
                case CollectibleKind.Nugget: return Economy.NuggetValueMultiplier(Stats);
                case CollectibleKind.RichNugget: return Economy.NuggetValueMultiplier(Stats) * Economy.RichNuggetValueMultiplier(Stats);
                case CollectibleKind.GiantNugget: return Economy.NuggetValueMultiplier(Stats) * Economy.GiantNuggetValueMultiplier(Stats);
                default: return 1;
            }
        }

        // --- Nugget collection (3.1.2) ---

        /// <summary>
        /// Type of a Nugget spawned in the current creek (design doc 3.1.2): the creek's Common
        /// 70%, the previous creek's Common 27% (the first creek keeps it), the six global
        /// Rares 3% together. Boss nuggets never come from this roll. roll is uniform in [0, 1).
        /// </summary>
        public int RollNuggetType(double roll)
        {
            int region = Progress.RegionIndex;
            if (region < 0 || GameCatalog.CommonNugget(region) >= GameCatalog.FirstRareNugget)
                return -1;
            double own = Config.NuggetOwnWeight + (region == 0 ? Config.NuggetPreviousWeight : 0);
            double previous = region == 0 ? 0 : Config.NuggetPreviousWeight;
            double total = own + previous + Config.NuggetRareWeight;
            double target = roll * total;
            if (target < own)
                return GameCatalog.CommonNugget(region);
            if (target < own + previous)
                return GameCatalog.CommonNugget(region - 1);
            int count = GameCatalog.RareNuggetCount;
            int rare = (int)((target - own - previous) / Config.NuggetRareWeight * count);
            return GameCatalog.FirstRareNugget + Math.Max(0, Math.Min(count - 1, rare));
        }

        public int NuggetCatches(int index) => Progress.NuggetCatches[index];

        public int NuggetStars(int index) => Economy.NuggetStars(index, Progress.NuggetCatches[index]);

        public bool IsNuggetDiscovered(int index) => Progress.NuggetCatches[index] > 0;

        public int TotalStars
        {
            get
            {
                int stars = 0;
                for (int i = 0; i < Progress.NuggetCatches.Length; i++)
                    stars += Economy.NuggetStars(i, Progress.NuggetCatches[i]);
                return stars;
            }
        }

        public int MaxStars => GameCatalog.Nuggets.Count * Economy.MaxStarsPerNugget;

        /// <summary>Counts a manual Nugget catch (plain, Rich or Giant) toward its type's stars.
        /// Boss nuggets only come from a Mother Lode (<see cref="FinishMotherLode"/>).</summary>
        public NuggetCatch CatchNugget(int index)
        {
            if (index < 0 || index >= Progress.NuggetCatches.Length || GameCatalog.IsBossNugget(index))
                return new NuggetCatch(-1, false, 0);
            return AddToCollection(index);
        }

        NuggetCatch AddToCollection(int index)
        {
            int before = Progress.NuggetCatches[index];
            int after = before + 1;
            Progress.NuggetCatches[index] = after;
            int stars = Economy.NuggetStars(index, after) - Economy.NuggetStars(index, before);
            CountJob(DailyJobKind.CollectionStars, stars);
            if (stars > 0)
                Emit("collection_star", ("nugget_id", GameCatalog.Nuggets[index].Id), ("stars", Economy.NuggetStars(index, after)), ("total_stars", TotalStars));
            return new NuggetCatch(index, before == 0, stars);
        }

        // --- Vein (3.1.1): manual catches in a row raise a manual value bonus ---

        /// <summary>Current vein level; resets when a collectible is lost. Not saved.</summary>
        public int VeinLevel { get; private set; }

        /// <summary>Manual catches toward the next vein level.</summary>
        public int VeinStreak { get; private set; }

        public int VeinMaxLevel => Economy.VeinMaxLevel(Stats);

        public bool VeinOpen => VeinMaxLevel > 0;

        public int VeinCatchesPerLevel => Economy.VeinCatchesPerLevel(Stats);

        public double VeinMultiplier => Economy.VeinMultiplier(VeinLevel);

        /// <summary>A collectible left the screen uncaught; returns whether a vein broke.</summary>
        public bool LoseCollectible()
        {
            bool broke = VeinLevel > 0 || VeinStreak > 0;
            VeinLevel = 0;
            VeinStreak = 0;
            return broke;
        }

        void AdvanceVein()
        {
            int max = VeinMaxLevel;
            if (max <= 0)
                return;
            if (VeinLevel >= max)
            {
                VeinLevel = max;
                VeinStreak = 0;
                return;
            }
            VeinStreak++;
            if (VeinStreak >= VeinCatchesPerLevel)
            {
                VeinLevel++;
                VeinStreak = 0;
            }
        }

        /// <summary>
        /// Dollars for one manual catch at the current vein level. Averaged over the rolls
        /// with the vein closed this equals <see cref="Economy.CollectValue"/> times the active
        /// income and critical factors, so SpawnRate * average =
        /// <see cref="Economy.ActiveRate(int,int,StatSheet,double)"/>.
        /// </summary>
        public BigNumber CatchValue(CollectibleKind kind, bool doubleCatch, bool critical = false)
        {
            double factor = KindMultiplier(kind);
            if (doubleCatch)
                factor *= 2;
            if (critical)
                factor *= Economy.CritValueMultiplier(Stats);
            factor *= Stats.Multiplier(Stat.AllIncome) * IncomeMultiplier * Stats.Multiplier(Stat.ActiveIncome) * VeinMultiplier;
            return Economy.DustValue(Progress.RegionIndex, Progress.TierIndex, Stats) * factor;
        }

        /// <summary>Credits a manual catch, advances the vein and returns the catch's value.</summary>
        public BigNumber Collect(CollectibleKind kind, bool doubleCatch, bool critical = false)
        {
            BigNumber value = CatchValue(kind, doubleCatch, critical);
            if (Progress.ManualCollected == 0)
                Emit("first_manual_catch");
            Tally.ManualCatches++;
            if (kind != CollectibleKind.GoldDust)
                Tally.Nuggets++;
            AdvanceVein();
            CountTowardChest();
            CountJob(DailyJobKind.ManualCatches);
            if (kind != CollectibleKind.GoldDust)
                CountJob(DailyJobKind.Nuggets);
            Progress.ManualCollected++;
            Progress.CollectedSinceMotherLode++;
            Earn(value, IncomeSource.Manual);
            return value;
        }

        public void Earn(BigNumber amount, IncomeSource source = IncomeSource.Other)
        {
            if (amount <= BigNumber.Zero)
                return;
            Progress.Dollars += amount;
            Progress.TotalEarned += amount;
            Tally.Add(source, amount);
        }

        // --- Idle and offline ---

        public bool IdleActive => Progress.AmosLevel > 0;

        public BigNumber IdleRate =>
            IdleActive ? Economy.IdleRate(Progress.RegionIndex, Progress.TierIndex, Stats, IncomeMultiplier) : BigNumber.Zero;

        public BigNumber OfflineRate =>
            IdleActive ? Economy.OfflineRate(Progress.RegionIndex, Progress.TierIndex, Stats, PrestigeMultiplier) : BigNumber.Zero;

        /// <summary>Idle income while the game is open; returns what was earned.</summary>
        public BigNumber TickIdle(double deltaSeconds)
        {
            if (!IdleActive || deltaSeconds <= 0)
                return BigNumber.Zero;
            BigNumber earned = IdleRate * deltaSeconds;
            Earn(earned, IncomeSource.Idle);
            return earned;
        }

        public double OfflineCapSeconds => Economy.OfflineCapSeconds(Progress.AmosLevel, Stats);

        /// <summary>Offline Dollars for an absence, plus the boost for the part of it a Daily Wash boost covered.</summary>
        public OfflineResult EvaluateOffline(OfflineClockInput clock)
        {
            OfflineResult result = offline.Evaluate(clock, OfflineCapSeconds, OfflineRate);
            if (result.Status == OfflineStatus.RejectedClockTamper)
                Emit("clock_tamper", ("elapsed_s", (long)result.ElapsedSeconds), ("source", "offline"));
            BigNumber bonus = OfflineBoostBonus(clock.LastSeenUtc, result.CreditedSeconds);
            if (bonus.IsZero)
                return result;
            return new OfflineResult(result.Status, result.ElapsedSeconds, result.CreditedSeconds, result.Amount + bonus, result.CapReached);
        }

        /// <summary>Pays a trusted offline result; multiplier is 2 after a rewarded ad.</summary>
        public bool ClaimOffline(OfflineResult result, int multiplier)
        {
            if (!result.IsPayable || multiplier < 1)
                return false;
            BigNumber paid = result.Amount * multiplier;
            Earn(paid, IncomeSource.Offline);
            Emit("offline_claim",
                ("away_s", (long)result.ElapsedSeconds),
                ("capped", result.CapReached),
                ("cap_h", Math.Round(OfflineCapSeconds / 3600, 2)),
                ("amount_log10", EventValues.Log10(paid)),
                ("mult", multiplier),
                ("double_path", multiplier > 1 ? "ad" : "none"));
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
            CountJob(DailyJobKind.UpgradesBought);
            Tally.Upgrades++;
            return true;
        }

        // --- Sluice tiers ---

        public bool HasNextTier => Progress.TierIndex + 1 < Config.TierCount;

        /// <summary>
        /// Tier t is bought in creek 2t (design doc 6.1: tier 2 in Pine Hollow, creek 3). Tiers
        /// past the last creek open once the last creek is unlocked.
        /// </summary>
        public bool IsNextTierUnlocked
        {
            get
            {
                if (!HasNextTier)
                    return false;
                int highestRegion = Progress.RegionsUnlocked - 1;
                return Economy.TierRegion(Progress.TierIndex + 1) <= highestRegion || Progress.RegionsUnlocked >= Config.RegionCount;
            }
        }

        public BigNumber? NextTierCost => HasNextTier ? Economy.TierCost(Progress.TierIndex + 1, Stats) : (BigNumber?)null;

        public bool BuyNextTier()
        {
            if (!IsNextTierUnlocked || !TrySpend(NextTierCost))
                return false;
            Progress.TierIndex++;
            Emit("tier_up", ("new_tier", Progress.TierIndex + 1));
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
            if (Progress.RegionsUnlocked > Progress.BestRegionsUnlocked)
                OfferWelcome(Progress.RegionIndex);
            Progress.BestRegionsUnlocked = Math.Max(Progress.BestRegionsUnlocked, Progress.RegionsUnlocked);
            if (IsCandidateRegion(Progress.RegionIndex))
                OfferCandidates();
            Emit("region_unlock", ("region", Progress.RegionIndex + 1));
            return true;
        }

        /// <summary>Creeks whose unlock brings a candidate pair (design doc 6.3: odd creeks from Silver Fork).</summary>
        bool IsCandidateRegion(int regionIndex) =>
            regionIndex >= Config.CrewCandidateFirstRegion
            && (regionIndex - Config.CrewCandidateFirstRegion) % Math.Max(1, Config.CrewCandidateRegionStep) == 0;

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
            BigNumber? cost = AmosNextCost;
            if (!TrySpend(cost))
                return false;
            Progress.AmosLevel++;
            if (Progress.AmosLevel == 1)
                Emit("idle_unlocked");
            Emit("amos_level_up",
                ("level", Progress.AmosLevel),
                ("cap_h", Math.Round(OfflineCapSeconds / 3600, 2)),
                ("cost_log10", EventValues.Log10(cost.Value)));
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
            TickChest(deltaSeconds);
            CheckFeatureUnlocks();
        }

        /// <summary>Dollars per second right now from swiping and the crew together.</summary>
        public BigNumber IncomePerSecond =>
            Economy.ActiveRate(Progress.RegionIndex, Progress.TierIndex, Stats, IncomeMultiplier) + IdleRate;

        /// <summary>After 400 catches (fewer with Wren) or 10 minutes of play, whichever first.</summary>
        public bool MotherLodeDue =>
            Progress.PlaySeconds >= Config.MotherLodeFirstAfterSeconds
            && (Progress.CollectedSinceMotherLode >= Economy.MotherLodeEveryCollectibles(Stats)
                || Progress.SecondsSinceMotherLode >= Config.MotherLodeEverySeconds);

        /// <summary>
        /// Starts the event and restarts both counters; the income it pays is fixed now. While
        /// an open creek's boss is still unbeaten, the run fights the lowest one (design doc 3.4.1).
        /// </summary>
        public MotherLodeRun StartMotherLode()
        {
            Progress.CollectedSinceMotherLode = 0;
            Progress.SecondsSinceMotherLode = 0;
            var run = new MotherLodeRun(Config, IncomePerSecond, Economy.MotherLodeMaxCombo(Stats));
            int boss = NextBossRegion;
            if (boss >= 0)
                run.SetBoss(boss, Economy.BossHealth(boss), BossHitDamage);
            return run;
        }

        /// <summary>Lowest open creek whose boss is not beaten yet; -1 when every open boss is.</summary>
        public int NextBossRegion
        {
            get
            {
                for (int r = 0; r < Progress.RegionsUnlocked && GameCatalog.BossNugget(r) < GameCatalog.FirstRareNugget; r++)
                    if (Progress.NuggetCatches[GameCatalog.BossNugget(r)] == 0)
                        return r;
                return -1;
            }
        }

        /// <summary>Damage of one Mother Lode hit at combo x1 right now.</summary>
        public double BossHitDamage => Economy.BossHitDamage(PrestigeMultiplier, Progress.GuildLevel, Stats);

        public bool IsBossBeaten(int regionIndex) => Progress.NuggetCatches[GameCatalog.BossNugget(regionIndex)] > 0;

        /// <summary>Starts a Mother Lode at once for Gems; null when the player cannot pay.</summary>
        public MotherLodeRun SummonMotherLode()
        {
            if (!TrySpendGems(Config.MotherLodeSummonGems, "mother_lode_summon"))
                return null;
            MotherLodeRun run = StartMotherLode();
            run.Summoned = true;
            return run;
        }

        public BigNumber MotherLodeReward(MotherLodeRun run) =>
            Economy.MotherLodeReward(run.IncomePerSecond, run.PeakCombo, Stats);

        /// <summary>Pays a run once, even if it was cut short; returns the Dollars paid.</summary>
        public BigNumber FinishMotherLode(MotherLodeRun run)
        {
            if (run.IsPaid)
                return BigNumber.Zero;
            run.End();
            BigNumber reward = MotherLodeReward(run);
            Earn(reward, IncomeSource.Event);
            CountJob(DailyJobKind.MotherLodes);
            EarnGems(Config.MotherLodeGemReward, "mother_lode");
            DropBossNugget(run);
            Emit("mother_lode",
                ("trigger", run.Summoned ? "gem" : "natural"),
                ("reward_log10", EventValues.Log10(reward)),
                ("taps", run.Hits),
                ("peak_combo", run.PeakCombo));
            return reward;
        }

        /// <summary>A beaten boss drops its nugget; a plain run re-drops the current creek's at 25%.</summary>
        void DropBossNugget(MotherLodeRun run)
        {
            int region = -1;
            if (run.IsBossFight)
            {
                if (run.BossBeaten)
                    region = run.BossRegion;
                Emit("boss_fight", ("region", run.BossRegion + 1), ("outcome", run.BossBeaten ? "beaten" : "escaped"),
                    ("damage_pct", (long)Math.Round(100 * Math.Min(1, run.BossDamage / run.BossHealth))));
            }
            else if (IsBossBeaten(Progress.RegionIndex) && random.NextDouble() < Config.BossRedropChance)
            {
                region = Progress.RegionIndex;
            }
            if (region < 0)
                return;
            run.DroppedNugget = GameCatalog.BossNugget(region);
            AddToCollection(run.DroppedNugget);
        }

        // --- Gems ---

        /// <param name="source">earn_virtual_currency's source (13.4.3), e.g. "goal" or "streak".</param>
        public void EarnGems(int amount, string source = "other")
        {
            if (amount <= 0)
                return;
            Progress.Gems += amount;
            Emit("earn_virtual_currency", ("virtual_currency_name", "gem"), ("value", amount), ("source", source));
        }

        public bool CanAffordGems(int? cost) => cost.HasValue && Progress.Gems >= cost.Value;

        /// <param name="item">spend_virtual_currency's item_name (13.4.3).</param>
        bool TrySpendGems(int? cost, string item)
        {
            if (!CanAffordGems(cost))
                return false;
            Progress.Gems -= cost.Value;
            if (cost.Value > 0)
                Emit("spend_virtual_currency", ("virtual_currency_name", "gem"), ("value", cost.Value), ("item_name", item));
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

        /// <summary>Gem price of the next candidate hire; 0 for the tutorial's free first one, null once everyone is hired.</summary>
        public int? CrewHireCost
        {
            get
            {
                int? cost = Economy.CrewHireCost(CrewHiredCount);
                return cost.HasValue && FreeHireReady ? 0 : cost;
            }
        }

        /// <summary>Gem price of the next level for this member; null when not hired or maxed.</summary>
        public int? CrewLevelUpCost(int index)
        {
            int level = Progress.CrewLevels[index];
            return level >= 1 && level < GameCatalog.CrewMaxLevel ? Economy.CrewLevelUpCost(level) : null;
        }

        public bool LevelUpCrew(int index)
        {
            if (!TrySpendGems(CrewLevelUpCost(index), "crew_level"))
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
            int? cost = CrewHireCost;
            if (Array.IndexOf(Progress.CrewCandidates, crewIndex) < 0 || !TrySpendGems(cost, "crew_hire"))
                return false;
            Emit("crew_candidate", ("member", GameCatalog.Crew[crewIndex].Id), ("outcome", "hired"), ("gem_cost", cost.Value));
            Progress.CrewLevels[crewIndex] = 1;
            Progress.FreeHireUsed = true;
            CloseCandidates();
            RebuildStats();
            return true;
        }

        public void PassCandidates()
        {
            EmitCandidates("passed", 0);
            CloseCandidates();
        }

        /// <summary>Runs the candidate window down while the game is open; returns true when it just expired.</summary>
        public bool TickCandidates(double deltaSeconds)
        {
            if (!HasCandidates || deltaSeconds <= 0)
                return false;
            Progress.CandidateSecondsLeft -= deltaSeconds;
            if (Progress.CandidateSecondsLeft > 0)
                return false;
            EmitCandidates("expired", 0);
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
            int reward = CurrentGoalReward;
            EarnGems(reward, "goal");
            Progress.GoalIndex++;
            Emit("goal_complete", ("goal_n", Progress.GoalIndex), ("gems", reward));
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
