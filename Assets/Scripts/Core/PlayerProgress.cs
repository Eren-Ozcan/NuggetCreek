using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Everything a save file holds. Plain data: rules live in <see cref="GameSession"/>,
    /// serialization lives in the platform layer.
    /// </summary>
    public sealed class PlayerProgress
    {
        public BigNumber Dollars = BigNumber.Zero;

        /// <summary>Lifetime Dollars earned this claim; feeds Prospecting XP.</summary>
        public BigNumber TotalEarned = BigNumber.Zero;

        /// <summary>Region the player is panning in (0-based).</summary>
        public int RegionIndex;

        /// <summary>Number of unlocked regions; region 0 is always unlocked.</summary>
        public int RegionsUnlocked = 1;

        /// <summary>Highest sluice tier owned (0-based).</summary>
        public int TierIndex;

        /// <summary>Owned levels, indexed like <see cref="GameCatalog.Upgrades"/>.</summary>
        public int[] UpgradeLevels = new int[GameCatalog.Upgrades.Count];

        /// <summary>0 until Amos joins; his level is the offline cap in hours.</summary>
        public int AmosLevel;

        /// <summary>Gold Dust and Nuggets collected by hand, lifetime (onboarding and goals).</summary>
        public long ManualCollected;

        public long ProspectingXp;

        public int Gems;

        /// <summary>Crew levels, indexed like <see cref="GameCatalog.Crew"/>; 0 = not hired.</summary>
        public int[] CrewLevels = new int[GameCatalog.Crew.Count];

        /// <summary>Manual catches per Nugget type, indexed like <see cref="GameCatalog.Nuggets"/>.
        /// Survives prestige (design doc 6.5).</summary>
        public int[] NuggetCatches = new int[GameCatalog.Nuggets.Count];

        /// <summary>Gear levels, indexed like <see cref="GameCatalog.Gear"/>; 0 = not owned.</summary>
        public int[] GearLevels = new int[GameCatalog.Gear.Count];

        /// <summary>Gear index worn in each slot, -1 when empty.</summary>
        public int[] GearSlots = { -1, -1, -1, -1 };

        public bool ThirdGearSlotBought;

        /// <summary>Set by the IAP pack (phase 3); the greybox keeps it locked.</summary>
        public bool FourthGearSlotOwned;

        public int ChestsWaiting;

        public bool ChestOpening;

        /// <summary>Unlock time left on the opening chest, counted while the game is open.</summary>
        public double ChestSecondsLeft;

        public int CatchesTowardChest;

        public int ChestsOpened;

        /// <summary>Guild perk ranks, indexed like <see cref="GameCatalog.Perks"/>.</summary>
        public int[] PerkRanks = new int[GameCatalog.Perks.Count];

        public int GuildLevel;

        /// <summary>Guild XP collected toward the next level.</summary>
        public long GuildXp;

        /// <summary>New claims staked, lifetime.</summary>
        public int Rebirths;

        /// <summary>Most creeks ever open at once; survives rebirth (the free second gear slot).</summary>
        public int BestRegionsUnlocked = 1;

        // --- Daily systems ---

        /// <summary>Last game day seen; -1 until trusted time gives one.</summary>
        public long CurrentDay = -1;

        public long LastStreakClaimDay = -1;

        /// <summary>0-based calendar day the next claim pays.</summary>
        public int StreakIndex;

        /// <summary>Calendar day an ad can restore after one missed day; -1 when none.</summary>
        public int RescueStreakIndex = -1;

        public int[] JobKinds = { 0, 1, 2 };
        public int[] JobProgress = new int[3];
        public bool[] JobClaimed = new bool[3];

        public bool FreeWashUsed;
        public int AdWashes;

        public double BoostMultiplier = 1;

        /// <summary>Trusted UTC second the income boost ends.</summary>
        public double BoostEndUtc;

        // --- Onboarding (design doc 9.1) ---

        /// <summary>Bit per <see cref="PeteLine"/> already shown.</summary>
        public int PeteSeen;

        /// <summary>The tutorial's free first candidate hire is spent.</summary>
        public bool FreeHireUsed;

        /// <summary>The second-session gift chest was given.</summary>
        public bool ReturnGiftGiven;

        /// <summary>First game day played; 0 until trusted time gives one.</summary>
        public long FirstDay;

        // --- Shop (design doc 8.2e) ---

        /// <summary>Lifetime real-money spend in USD cents; removes forced ads at the threshold (8.3).</summary>
        public long SpentCents;

        public bool AdsRemoved;

        /// <summary>The one-time "ads are off" card has not been shown yet.</summary>
        public bool AdsRemovedNoticePending;

        /// <summary>Trusted UTC second the Starter Pack stops selling; 0 until trusted time first arrives.</summary>
        public double StarterEndUtc;

        public bool StarterBought;

        /// <summary>New Creek Welcome on sale: 0 none, 1 Small, 2 Large.</summary>
        public int WelcomeOffer;

        public double WelcomeEndUtc;

        /// <summary>Trusted UTC second today's Daily Offer ends.</summary>
        public double DailyOfferEndUtc;

        /// <summary>Game day the Daily Offer was last bought; 0 = never.</summary>
        public long DailyOfferBoughtDay;

        /// <summary>Friday game day of the weekend the packs in WeekendBoughtMask were bought.</summary>
        public long WeekendBoughtId;

        /// <summary>Bit per weekend pack (Small, Medium, Large) bought this weekend.</summary>
        public int WeekendBoughtMask;

        /// <summary>Gold Wash boost time left, counted while the game is open.</summary>
        public double GoldWashSecondsLeft;

        /// <summary>Giant Nuggets bought with Rich Vein and not spawned yet.</summary>
        public int PendingGiantNuggets;

        /// <summary>Most recent store transaction ids, so a purchase is never granted twice.</summary>
        public string[] Transactions = new string[0];

        /// <summary>Crew indices on offer right now; empty when no candidate event is open.</summary>
        public int[] CrewCandidates = new int[0];

        public double CandidateSecondsLeft;

        /// <summary>Index of the active goal in <see cref="GameCatalog.Goals"/>; Count once all are claimed.</summary>
        public int GoalIndex;

        /// <summary>Seconds of play with the game open, lifetime; gates the first Mother Lode.</summary>
        public double PlaySeconds;

        /// <summary>Manual catches since the last Mother Lode.</summary>
        public int CollectedSinceMotherLode;

        /// <summary>Play seconds since the last Mother Lode.</summary>
        public double SecondsSinceMotherLode;

        // --- Clock readings from the last time the game went to background (OfflineEarnings) ---

        public double LastSeenUtc;
        public double LastDeviceUtc;
        public double LastMonotonicSeconds;

        /// <summary>Device boot time (device UTC minus monotonic) at save; detects reboots.</summary>
        public double LastBootUtc;

        /// <summary>Brings arrays saved by an older catalog up to the current size.</summary>
        public void Normalize()
        {
            if (UpgradeLevels == null)
                UpgradeLevels = new int[GameCatalog.Upgrades.Count];
            else if (UpgradeLevels.Length != GameCatalog.Upgrades.Count)
                Array.Resize(ref UpgradeLevels, GameCatalog.Upgrades.Count);
            if (CrewLevels == null)
                CrewLevels = new int[GameCatalog.Crew.Count];
            else if (CrewLevels.Length != GameCatalog.Crew.Count)
                Array.Resize(ref CrewLevels, GameCatalog.Crew.Count);
            if (NuggetCatches == null)
                NuggetCatches = new int[GameCatalog.Nuggets.Count];
            else if (NuggetCatches.Length != GameCatalog.Nuggets.Count)
                Array.Resize(ref NuggetCatches, GameCatalog.Nuggets.Count);
            for (int i = 0; i < NuggetCatches.Length; i++)
                NuggetCatches[i] = Math.Max(0, NuggetCatches[i]);
            if (GearLevels == null)
                GearLevels = new int[GameCatalog.Gear.Count];
            else if (GearLevels.Length != GameCatalog.Gear.Count)
                Array.Resize(ref GearLevels, GameCatalog.Gear.Count);
            for (int i = 0; i < GearLevels.Length; i++)
                GearLevels[i] = Math.Max(0, Math.Min(GearLevels[i], GameCatalog.GearMaxLevel));
            NormalizeGearSlots();
            if (PerkRanks == null)
                PerkRanks = new int[GameCatalog.Perks.Count];
            else if (PerkRanks.Length != GameCatalog.Perks.Count)
                Array.Resize(ref PerkRanks, GameCatalog.Perks.Count);
            for (int i = 0; i < PerkRanks.Length; i++)
                PerkRanks[i] = Math.Max(0, Math.Min(PerkRanks[i], GameCatalog.Perks[i].MaxRank));
            if (JobKinds == null || JobKinds.Length != 3)
                JobKinds = new[] { 0, 1, 2 };
            if (JobProgress == null || JobProgress.Length != 3)
                JobProgress = new int[3];
            if (JobClaimed == null || JobClaimed.Length != 3)
                JobClaimed = new bool[3];
            StreakIndex = Math.Max(0, Math.Min(StreakIndex, 29));
            RescueStreakIndex = Math.Max(-1, Math.Min(RescueStreakIndex, 29));
            AdWashes = Math.Max(0, AdWashes);
            BoostMultiplier = Math.Max(1, BoostMultiplier);
            GuildLevel = Math.Max(0, GuildLevel);
            GuildXp = Math.Max(0, GuildXp);
            Rebirths = Math.Max(0, Rebirths);
            ChestsWaiting = Math.Max(0, ChestsWaiting);
            ChestSecondsLeft = Math.Max(0, ChestSecondsLeft);
            CatchesTowardChest = Math.Max(0, CatchesTowardChest);
            ChestsOpened = Math.Max(0, ChestsOpened);
            CrewCandidates = Array.FindAll(CrewCandidates ?? new int[0],
                index => index >= 0 && index < GameCatalog.Crew.Count && CrewLevels[index] == 0);
            if (CrewCandidates.Length == 0)
                CandidateSecondsLeft = 0;
            Gems = Math.Max(0, Gems);
            SpentCents = Math.Max(0, SpentCents);
            WelcomeOffer = Math.Max(0, Math.Min(WelcomeOffer, 2));
            WeekendBoughtMask &= 7;
            GoldWashSecondsLeft = Math.Max(0, GoldWashSecondsLeft);
            PendingGiantNuggets = Math.Max(0, PendingGiantNuggets);
            if (Transactions == null)
                Transactions = new string[0];
            PlaySeconds = Math.Max(0, PlaySeconds);
            CollectedSinceMotherLode = Math.Max(0, CollectedSinceMotherLode);
            SecondsSinceMotherLode = Math.Max(0, SecondsSinceMotherLode);
            GoalIndex = Math.Max(0, Math.Min(GoalIndex, GameCatalog.Goals.Count));
            RegionsUnlocked = Math.Max(1, RegionsUnlocked);
            RegionIndex = Math.Max(0, Math.Min(RegionIndex, RegionsUnlocked - 1));
            BestRegionsUnlocked = Math.Max(BestRegionsUnlocked, RegionsUnlocked);
        }

        /// <summary>Four slots, each holding owned gear at most once.</summary>
        void NormalizeGearSlots()
        {
            int[] slots = { -1, -1, -1, -1 };
            if (GearSlots != null)
            {
                for (int i = 0; i < Math.Min(slots.Length, GearSlots.Length); i++)
                {
                    int gear = GearSlots[i];
                    if (gear >= 0 && gear < GearLevels.Length && GearLevels[gear] > 0 && Array.IndexOf(slots, gear) < 0)
                        slots[i] = gear;
                }
            }
            GearSlots = slots;
        }
    }
}
