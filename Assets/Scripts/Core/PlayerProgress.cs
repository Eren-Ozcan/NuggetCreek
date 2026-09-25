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
    }
}
