using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// The two arms of the notification ask test (design doc 10.1.4). Names match the Remote
    /// Config values.
    /// </summary>
    public enum NotifAskMoment
    {
        session1_region2,
        session2_offline,
    }

    /// <summary>
    /// Every tunable economy number. Defaults are the launch values from the design doc;
    /// Remote Config overrides them at session start (never mid-session). Each field's key
    /// is its snake_case name unless a RemoteKey attribute says otherwise (see RemoteConfig).
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

        /// <summary>No Mother Lode before this much play time, so it lands after Pine Hollow and the first chest (design doc 10).</summary>
        public double MotherLodeFirstAfterSeconds = 25 * 60;

        public double MotherLodeDurationSeconds = 20;

        /// <summary>Hits that raise the combo by one step, x1 up to MotherLodeMaxCombo.</summary>
        public int MotherLodeHitsPerCombo = 4;

        public int MotherLodeMaxCombo = 10;

        /// <summary>A pause longer than this drops the combo back to x1.</summary>
        public double MotherLodeComboHoldSeconds = 1.0;

        /// <summary>Reward in seconds of current income at combo x1 and at the max combo.</summary>
        public double MotherLodeRewardMinSeconds = 60;

        public double MotherLodeRewardMaxSeconds = 90;

        /// <summary>Gems per finished Mother Lode (5.0.6 budgets ~5 a day).</summary>
        public int MotherLodeGemReward = 1;

        /// <summary>Gem price to call a Mother Lode at once (8.2c).</summary>
        public int MotherLodeSummonGems = 5;

        /// <summary>A Nugget is worth this many Gold Dust of the same region.</summary>
        public double NuggetValueMultiplier = 8.0;

        // --- Collection layers (3.1.1, 3.5) ---

        /// <summary>Share of Nuggets that come up Rich.</summary>
        public double RichNuggetChanceBase = 0.10;

        /// <summary>A Rich Nugget is worth this many Nuggets; Midas Touch raises it.</summary>
        public double RichNuggetValueMultiplier = 3.0;

        /// <summary>Share of Nuggets that come up Giant. Zero until the Big Strike perk.</summary>
        public double GiantNuggetChanceBase = 0;

        /// <summary>A Giant Nugget is worth this many Nuggets; Assay Bonus raises it.</summary>
        public double GiantNuggetValueMultiplier = 25.0;

        /// <summary>Critical catch chance on manual catches. Zero until gear adds it.</summary>
        public double CritChanceBase = 0;

        /// <summary>A critical manual catch pays this many times its value; Precision raises it.</summary>
        public double CritValueMultiplier = 3.0;

        /// <summary>Manual catches in a row, with no collectible lost, per vein level.</summary>
        public int VeinCatchesPerLevel = 20;

        /// <summary>Vein level ceiling before perks. Zero keeps the vein closed until Rich Vein.</summary>
        public int VeinMaxLevelBase = 0;

        /// <summary>Manual catch bonus per vein level.</summary>
        public double VeinValuePerLevel = 0.10;

        // --- Nugget collection (3.1.2) ---

        /// <summary>Nugget roll: the creek's own Common. In the first creek it also takes the
        /// previous creek's share.</summary>
        public double NuggetOwnWeight = 0.70;

        /// <summary>Nugget roll: the previous creek's Common.</summary>
        public double NuggetPreviousWeight = 0.27;

        /// <summary>Nugget roll: the six global Rares together, split evenly.</summary>
        public double NuggetRareWeight = 0.03;

        /// <summary>Manual catches of a Common or Rare type for each of its stars.</summary>
        public int[] NuggetStarThresholds = { 1, 10, 40 };

        /// <summary>Drops of a boss nugget for each of its stars: the kill, the 2nd and the 5th drop.</summary>
        public int[] BossStarThresholds = { 1, 2, 5 };

        /// <summary>Each collection star raises the prestige bonus by this fraction (x1.9 at 138 stars).</summary>
        public double StarPrestigeBonus = 0.0065;

        // --- Mother Lode boss fight (3.4.1) ---

        /// <summary>Health of the first creek's boss: about 12 seconds of swiping for a fresh player.</summary>
        public double BossHealthBase = 500;

        /// <summary>Each creek's boss has this many times the previous one's health.</summary>
        public double BossHealthGrowth = 2.2;

        /// <summary>Hit damage bonus per Guild level.</summary>
        public double BossDamagePerGuildLevel = 0.01;

        /// <summary>Chance a Mother Lode re-drops the current creek's boss nugget once that boss is beaten.</summary>
        public double BossRedropChance = 0.25;

        // --- Gear and chests (6.4) ---

        public int GearSlotCount = 4;

        /// <summary>The second slot opens free once more than this many creeks are open (Silver Fork, creek 5).</summary>
        public int GearSecondSlotRegion = 4;

        /// <summary>Permanent third slot. The fourth comes with an IAP pack.</summary>
        public int GearThirdSlotGems = 80;

        /// <summary>Gem price to raise gear from level L to L+1 is this times L (2, 4, 6 ... 18).</summary>
        public int GearLevelUpGemsStep = 2;

        /// <summary>A duplicate card of maxed gear turns into this many Gems.</summary>
        public int GearMaxedDuplicateGems = 2;

        /// <summary>Manual catches to the first chest (onboarding ~3:30, design doc 9), then between chests.</summary>
        public int FirstChestAfterCatches = 150;

        public int ChestEveryCatches = 250;

        /// <summary>Earned chests that can wait unopened; more are lost. Storehouse raises it later.</summary>
        public int ChestCapacity = 3;

        /// <summary>Free unlock time of a chest, counted only while the game is open. The first chest has none.</summary>
        public double ChestOpenSeconds = 10 * 60;

        /// <summary>Opening at once costs 1 Gem per started block of this many seconds left, up to ChestInstantMaxGems.</summary>
        public double ChestInstantSecondsPerGem = 120;

        public int ChestInstantMaxGems = 5;

        /// <summary>A chest pays this many seconds of current income (Doc raises it).</summary>
        public double ChestIncomeSeconds = 180;

        /// <summary>Card rarity odds in a creek chest (Common, Rare, Legendary).</summary>
        public double[] ChestCardOdds = { 0.80, 0.18, 0.02 };

        /// <summary>Gear boxes, indexed Green, Orange, Red: Gem price and cards inside.</summary>
        public int[] GearBoxGems = { 60, 300, 900 };

        public int[] GearBoxCards = { 3, 5, 8 };

        /// <summary>Card rarity odds per box, three values (Common, Rare, Legendary) per box in box order.</summary>
        public double[] GearBoxOdds = { 0.75, 0.23, 0.02, 0.60, 0.30, 0.10, 0.50, 0.35, 0.15 };

        /// <summary>Each box holds at least one card of this rarity or better.</summary>
        public Rarity[] GearBoxGuarantee = { Rarity.Rare, Rarity.Rare, Rarity.Legendary };

        // --- Rebirth and the Prospectors' Guild (6.5, 6.6) ---

        /// <summary>Smallest claim a new claim can be staked for: about $540M lifetime, where
        /// the first XP used to start before the v0.19 formula.</summary>
        public long RebirthMinXp = 14;

        /// <summary>The game suggests a new claim once it would pay at least this much Prospecting XP ...</summary>
        public long RebirthSuggestMinXp = 25;

        /// <summary>... the claim at least multiplies income by this much ...</summary>
        public double RebirthSuggestGain = 2;

        /// <summary>... and the next target is more than this many hours of typical play away (5.0.5 wall).</summary>
        public double RebirthWallHours = 48;

        /// <summary>A typical day for the wall estimate: seconds of active play and seconds away (5.0.4).</summary>
        public double WallActiveSecondsPerDay = 28 * 60;

        public double WallAwaySecondsPerDay = 10 * 3600;

        public int GuildMaxLevel = 100;

        /// <summary>Guild XP to go from level L to L+1: GuildXpBase + GuildXpPerLevel * L.</summary>
        public int GuildXpBase = 40;

        public int GuildXpPerLevel = 30;

        /// <summary>Guild XP per rewarded ad watched from the Guild panel. RC: guild_xp_per_ad.</summary>
        public int GuildXpPerAd = 10;

        /// <summary>Guild milestone levels: vein opens, chests, Mother Lode combo, all income, Prospecting XP.</summary>
        public int[] GuildMilestoneLevels = { 8, 20, 40, 65, 100 };

        public int MilestoneVeinLevels = 2;
        public int MilestoneChestCapacity = 2;
        public double MilestoneChestTime = -0.25;
        public int MilestoneMotherLodeCombo = 5;
        public double MilestoneIncomeMultiplier = 1.5;
        public double MilestoneXpMultiplier = 1.5;

        // --- Daily systems (7.1) ---

        /// <summary>Local hour the game day rolls over, so a late session is not split in two.</summary>
        public double DailyRolloverHour = 4;

        /// <summary>30-day login calendar, one entry per day: Gems, minutes of income, creek chests, gear box (-1 none).</summary>
        public int[] StreakGems = { 5, 0, 0, 10, 20, 5, 0, 0, 10, 25, 5, 0, 0, 10, 40, 5, 0, 0, 10, 30, 5, 0, 0, 10, 40, 5, 0, 0, 10, 60 };

        public double[] StreakIncomeMinutes = { 0, 15, 0, 0, 0, 0, 15, 0, 0, 0, 0, 15, 0, 0, 0, 0, 15, 0, 0, 0, 0, 15, 0, 0, 0, 0, 15, 0, 0, 0 };

        public int[] StreakChests = { 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0 };

        public int[] StreakBoxes = { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 1 };

        /// <summary>Target of each daily job, indexed by <see cref="DailyJobKind"/>.</summary>
        public int[] DailyJobTargets = { 300, 15, 1, 2, 5, 1, 1, 1 };

        public int DailyJobGems = 10;

        /// <summary>Rewarded ad on the goal bar (design doc 8.4 "goal bonus"): Gems per ad and ads per game day.</summary>
        public int GoalAdGems = 3;

        public int GoalAdsPerDay = 5;

        /// <summary>Washes after the free one, each after a rewarded ad.</summary>
        public int WashAdMax = 4;

        /// <summary>Daily Wash outcomes, shown with their odds: 3 / 8 / 25 Gems, x2 30 min, x2 2 h, x3 1 h, x4 30 min, a creek chest.</summary>
        public double[] WashChances = { 0.25, 0.15, 0.03, 0.25, 0.15, 0.10, 0.05, 0.02 };

        public int[] WashGems = { 3, 8, 25, 0, 0, 0, 0, 0 };

        public double[] WashMultipliers = { 1, 1, 1, 2, 2, 3, 4, 1 };

        public double[] WashMinutes = { 0, 0, 0, 30, 120, 60, 30, 0 };

        public int[] WashChests = { 0, 0, 0, 0, 0, 0, 0, 1 };

        // --- Onboarding locks (9.1) ---

        /// <summary>Manual catches that open Upgrades (the first goal).</summary>
        public int UpgradesUnlockCollected = 15;

        /// <summary>Creeks ever open that open the Shop (Pine Hollow, creek 3).</summary>
        public int ShopUnlockRegions = 3;

        /// <summary>Play time that opens Daily if the second game day has not come yet.</summary>
        public double DailyUnlockPlaySeconds = 60 * 60;

        /// <summary>Creeks ever open that open the Guild (Red Gulch, creek 7).</summary>
        public int GuildUnlockRegions = 7;

        /// <summary>Creek chests Amos brings on the first real return (design doc 10).</summary>
        public int ReturnGiftChests = 1;

        // --- Shop (8.2c, 8.2e) ---

        /// <summary>Gem price of each shop boost (Gold Wash, Extra Shift, Rich Vein).</summary>
        public int ShopBoostGems = 5;

        /// <summary>A boost shows OUT OF STOCK this long after it is bought.</summary>
        public double ShopBoostCooldownSeconds = 60;

        public double GoldWashMultiplier = 7;

        public double GoldWashSeconds = 60;

        /// <summary>Extra Shift pays this many seconds of idle income at once.</summary>
        public double ExtraShiftSeconds = 2 * 3600;

        public double StarterOfferHours = 72;

        public double WelcomeOfferHours = 24;

        /// <summary>Creek index (0-based) from which New Creek Welcome is the Large pack (Red Gulch).</summary>
        public int WelcomeLargeFromRegion = 6;

        public double DailyOfferHours = 6;

        /// <summary>Lifetime spend that turns forced ads off for good (8.3): one 9.99 purchase or two 4.99 ones.</summary>
        public int RemoveAdsThresholdCents = 998;

        /// <summary>Store transaction ids kept to reject a repeated grant.</summary>
        public int TransactionMemory = 50;

        /// <summary>
        /// Active income divided by this gives idle income. Chosen so 40 minutes of active
        /// play earn at least one 24 h absence (PacingModel.ActiveToAwayRatio). RC: active_idle_ratio.
        /// </summary>
        public double ActiveIdleRatio = 12;

        /// <summary>Base collectible value multiplier per region: sqrt(15), so every second creek
        /// is x15 like the old 10-creek curve (design doc 6.2). RC: value_growth.</summary>
        public double ValueGrowth = 3.872983346207417;

        /// <summary>Income multiplier per sluice tier. RC: tier_mult.</summary>
        [RemoteKey("tier_mult")]
        public double TierMultiplier = 1.6;

        // --- Progression tables (5.0.4, 6.1, 6.2), solved by tools/economy_tune.py ---

        /// <summary>Region unlock costs for the 20 launch creeks; index 0 is free.</summary>
        public double[] RegionUnlockCosts = { 0, 780, 4.1e3, 510e3, 4.7e6, 92e6, 1.2e9, 17e9, 120e9, 5.6e12, 170e12, 3.6e15, 53e15, 780e15, 11e18, 88e18, 2e21, 9.8e21, 120e21, 2.6e24 };

        /// <summary>Sluice tier costs for the 10 launch tiers; index 0 is the starting sluice.</summary>
        public double[] TierCosts = { 0, 1e3, 1.2e6, 300e6, 30e9, 42e12, 13e15, 2.8e18, 500e18, 30e21 };

        /// <summary>Level 1 price of each tier's two upgrades (3% of the tier cost, floored at 2x the previous).</summary>
        public double[] UpgradeBaseCosts = { 30, 60, 36e3, 9e6, 900e6, 1.3e12, 390e12, 84e15, 15e18, 900e18 };

        /// <summary>Upgrade price growth per level. RC: upgrade_growth.</summary>
        [RemoteKey("upgrade_growth")]
        public double UpgradeCostGrowth = 1.22;

        // --- Prestige (5.0.3) ---

        /// <summary>XP = floor((total / divisor) ^ exponent). v0.19: 0.45 made the multiplier grow
        /// x3.4 per creek and ran through all 20 creeks by day 33; 0.2 grows about x2 (design doc 5.0.3).</summary>
        public double PrestigeXpDivisor = 1000;
        public double PrestigeXpExponent = 0.2;
        public double PrestigeBonusPerXp = 0.02;

        // --- Offline and Amos (3.3, 3.3.2, 3.3.3) ---

        /// <summary>Dollars at which Amos joins and idle collection starts.</summary>
        public double AmosJoinCost = 70;

        /// <summary>Amos level costs for L2..L12: 45 minutes of typical idle income at the creek
        /// that unlocks the level (L2 with creek 1 ... L12 with creek 11).</summary>
        public double[] AmosLevelCosts = { 770, 3e3, 49e3, 190e3, 2.2e6, 13e6, 270e6, 1.6e9, 50e9, 300e9, 5.6e12 };

        /// <summary>Offline cap: 30 minutes when Amos joins, 30 more per level, 6 h at level 12.</summary>
        public double OfflineCapHoursPerAmosLevel = 0.5;

        /// <summary>Share of the offline rate paid for time past the cap; 0 = accrual stops at the cap (design doc 3.3).</summary>
        public double OfflinePastCapRate = 0;
        public int AmosMaxLevel = 12;

        /// <summary>Amos' level ceiling is AmosLevelsBase + AmosLevelsPerRegion x open creeks (design doc 3.3.3).</summary>
        public int AmosLevelsBase = 1;
        public int AmosLevelsPerRegion = 1;

        /// <summary>Sluice tier t is bought in creek index t x TierRegionStep (design doc 6.1: tier k in creek 2k-1).</summary>
        public int TierRegionStep = 2;

        // --- Crew (6.3) ---

        /// <summary>Gem price of the next hire, indexed by crew already hired (Amos excluded).</summary>
        public int[] CrewHireCosts = { 10, 15, 20, 30, 45, 65, 95, 135, 195, 285, 410, 595, 865, 900, 900 };

        /// <summary>Gem price to go from level i+1 to i+2 (L1 to L2 first, L9 to L10 last).</summary>
        public int[] CrewLevelUpCosts = { 6, 9, 13, 19, 27, 39, 57, 82, 119 };

        /// <summary>Candidates appear when this creek (0-based; Silver Fork) or a later one unlocks.</summary>
        public int CrewCandidateFirstRegion = 4;

        /// <summary>After the first, candidates appear every this many creeks (the odd creeks 5, 7 ... 19).</summary>
        public int CrewCandidateRegionStep = 2;

        public int CrewCandidatesPerEvent = 2;

        /// <summary>How long a candidate pair stays open, counted while the game runs. RC: crew_window_s.</summary>
        [RemoteKey("crew_window_s")]
        public double CrewCandidateWindowSeconds = 10 * 60;

        // --- Progress goals (5.0.6: ~8 Gems a day) ---

        /// <summary>Gem reward per goal, indexed like <see cref="GameCatalog.Goals"/>.</summary>
        public int[] GoalGemRewards = { 10, 3, 5, 5, 3, 3, 3, 3, 5, 5, 3, 3, 3, 5, 5, 5, 3, 3, 5, 5, 3, 10 };

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
        [RemoteKey("rv_load_timeout_s")]
        public double RewardedLoadTimeoutSeconds = 10;

        /// <summary>RC: late_double_window_s.</summary>
        [RemoteKey("late_double_window_s")]
        public double LateDoubleWindowSeconds = 5 * 60;

        // --- Interstitials (8.4) ---

        /// <summary>1 lets forced ads play; 0 turns them off for everyone.</summary>
        public int InterstitialEnabled = 1;

        /// <summary>
        /// Shortest gap after any full screen ad, rewarded included (4 minutes).
        /// RC: interstitial_cooldown_s.
        /// </summary>
        [RemoteKey("interstitial_cooldown_s")]
        public double InterstitialCooldownSeconds = 4 * 60;

        /// <summary>Forced ads start only once the player has gone past this creek.</summary>
        public int InterstitialUnlockRegion = 3;

        /// <summary>The first 20 minutes of play never show a forced ad.</summary>
        public double InterstitialMinPlaySeconds = 20 * 60;

        // --- Notifications (10.1) ---

        /// <summary>When Amos first asks to holler (10.1.4 A/B). RC: notif_ask_moment.</summary>
        [RemoteKey("notif_ask_moment")]
        public NotifAskMoment NotifAskMoment = NotifAskMoment.session1_region2;

        /// <summary>Ignored notifications in a row before cap and daily reminders pause.</summary>
        public int NotifFatigueCount = 3;

        public double NotifFatigueMuteSeconds = 7 * 24 * 3600;

        public int RegionCount => RegionUnlockCosts.Length;
        public int TierCount => TierCosts.Length;
    }
}
