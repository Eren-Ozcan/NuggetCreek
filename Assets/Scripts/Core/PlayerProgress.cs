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
            RegionsUnlocked = Math.Max(1, RegionsUnlocked);
            RegionIndex = Math.Max(0, Math.Min(RegionIndex, RegionsUnlocked - 1));
        }
    }
}
