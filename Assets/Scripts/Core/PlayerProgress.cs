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
            ChestsWaiting = Math.Max(0, ChestsWaiting);
            ChestSecondsLeft = Math.Max(0, ChestSecondsLeft);
            CatchesTowardChest = Math.Max(0, CatchesTowardChest);
            ChestsOpened = Math.Max(0, ChestsOpened);
            CrewCandidates = Array.FindAll(CrewCandidates ?? new int[0],
                index => index >= 0 && index < GameCatalog.Crew.Count && CrewLevels[index] == 0);
            if (CrewCandidates.Length == 0)
                CandidateSecondsLeft = 0;
            Gems = Math.Max(0, Gems);
            PlaySeconds = Math.Max(0, PlaySeconds);
            CollectedSinceMotherLode = Math.Max(0, CollectedSinceMotherLode);
            SecondsSinceMotherLode = Math.Max(0, SecondsSinceMotherLode);
            GoalIndex = Math.Max(0, Math.Min(GoalIndex, GameCatalog.Goals.Count));
            RegionsUnlocked = Math.Max(1, RegionsUnlocked);
            RegionIndex = Math.Max(0, Math.Min(RegionIndex, RegionsUnlocked - 1));
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
