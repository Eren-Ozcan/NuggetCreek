using System;
using System.Security.Cryptography;
using System.Text;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Loads and saves <see cref="PlayerProgress"/> as signed JSON in PlayerPrefs
    /// (design doc 13.1 rule 4). A tampered or unreadable save starts a fresh game until
    /// cloud backup exists.
    /// </summary>
    public static class SaveStore
    {
        const string Key = "nc_save";

        [Serializable]
        sealed class SaveData
        {
            public int version = 1;
            public string dollars;
            public string totalEarned;
            public int regionIndex;
            public int regionsUnlocked;
            public int tierIndex;
            public int[] upgradeLevels;
            public int amosLevel;
            public long manualCollected;
            public long prospectingXp;
            public int gems;
            public int[] crewLevels;
            public int[] crewCandidates;
            public double candidateSecondsLeft;
            public int goalIndex;
            public double playSeconds;
            public int collectedSinceMotherLode;
            public double secondsSinceMotherLode;
            public double lastSeenUtc;
            public double lastDeviceUtc;
            public double lastMonotonicSeconds;
            public double lastBootUtc;
        }

        public static bool HasSave => PlayerPrefs.HasKey(Key);

        public static PlayerProgress Load()
        {
            string text = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(text))
                return new PlayerProgress();
            if (!SaveEnvelope.TryUnwrap(text, SigningKey(), out string json))
            {
                Debug.LogWarning("Save signature mismatch; starting a fresh game.");
                return new PlayerProgress();
            }

            SaveData data = JsonUtility.FromJson<SaveData>(json);
            var progress = new PlayerProgress
            {
                Dollars = ParseOrZero(data.dollars),
                TotalEarned = ParseOrZero(data.totalEarned),
                RegionIndex = data.regionIndex,
                RegionsUnlocked = data.regionsUnlocked,
                TierIndex = data.tierIndex,
                UpgradeLevels = data.upgradeLevels,
                AmosLevel = data.amosLevel,
                ManualCollected = data.manualCollected,
                ProspectingXp = data.prospectingXp,
                Gems = data.gems,
                CrewLevels = data.crewLevels,
                CrewCandidates = data.crewCandidates,
                CandidateSecondsLeft = data.candidateSecondsLeft,
                GoalIndex = data.goalIndex,
                PlaySeconds = data.playSeconds,
                CollectedSinceMotherLode = data.collectedSinceMotherLode,
                SecondsSinceMotherLode = data.secondsSinceMotherLode,
                LastSeenUtc = data.lastSeenUtc,
                LastDeviceUtc = data.lastDeviceUtc,
                LastMonotonicSeconds = data.lastMonotonicSeconds,
                LastBootUtc = data.lastBootUtc,
            };
            progress.Normalize();
            return progress;
        }

        public static void Save(PlayerProgress progress)
        {
            var data = new SaveData
            {
                dollars = progress.Dollars.ToString(),
                totalEarned = progress.TotalEarned.ToString(),
                regionIndex = progress.RegionIndex,
                regionsUnlocked = progress.RegionsUnlocked,
                tierIndex = progress.TierIndex,
                upgradeLevels = progress.UpgradeLevels,
                amosLevel = progress.AmosLevel,
                manualCollected = progress.ManualCollected,
                prospectingXp = progress.ProspectingXp,
                gems = progress.Gems,
                crewLevels = progress.CrewLevels,
                crewCandidates = progress.CrewCandidates,
                candidateSecondsLeft = progress.CandidateSecondsLeft,
                goalIndex = progress.GoalIndex,
                playSeconds = progress.PlaySeconds,
                collectedSinceMotherLode = progress.CollectedSinceMotherLode,
                secondsSinceMotherLode = progress.SecondsSinceMotherLode,
                lastSeenUtc = progress.LastSeenUtc,
                lastDeviceUtc = progress.LastDeviceUtc,
                lastMonotonicSeconds = progress.LastMonotonicSeconds,
                lastBootUtc = progress.LastBootUtc,
            };
            PlayerPrefs.SetString(Key, SaveEnvelope.Wrap(JsonUtility.ToJson(data), SigningKey()));
            PlayerPrefs.Save();
        }

        public static void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        static BigNumber ParseOrZero(string text) => BigNumber.TryParse(text, out BigNumber value) ? value : BigNumber.Zero;

        // Placeholder until the platform milestone moves the key into Android Keystore.
        static byte[] SigningKey()
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes("nugget-creek-save/" + SystemInfo.deviceUniqueIdentifier));
        }
    }
}
