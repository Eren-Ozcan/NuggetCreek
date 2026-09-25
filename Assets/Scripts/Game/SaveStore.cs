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
            public int[] nuggetCatches;
            public int[] gearLevels;
            public int[] gearSlots;
            public bool thirdGearSlotBought;
            public bool fourthGearSlotOwned;
            public int chestsWaiting;
            public bool chestOpening;
            public double chestSecondsLeft;
            public int catchesTowardChest;
            public int chestsOpened;
            public int[] perkRanks;
            public int guildLevel;
            public long guildXp;
            public int rebirths;
            public int bestRegionsUnlocked;
            // Older saves lack the daily block; JsonUtility would read its -1 defaults as 0.
            public bool hasDaily;
            public long currentDay;
            public long lastStreakClaimDay;
            public int streakIndex;
            public int rescueStreakIndex;
            public int[] jobKinds;
            public int[] jobProgress;
            public bool[] jobClaimed;
            public bool freeWashUsed;
            public int adWashes;
            public int goalAds;
            public double boostMultiplier;
            public double boostEndUtc;
            public int peteSeen;
            public bool freeHireUsed;
            public bool returnGiftGiven;
            public long firstDay;
            public long spentCents;
            public bool adsRemoved;
            public bool adsRemovedNoticePending;
            public double starterEndUtc;
            public bool starterBought;
            public int welcomeOffer;
            public double welcomeEndUtc;
            public double dailyOfferEndUtc;
            public long dailyOfferBoughtDay;
            public long weekendBoughtId;
            public int weekendBoughtMask;
            public double goldWashSecondsLeft;
            public int pendingGiantNuggets;
            public string[] transactions;
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
                NuggetCatches = data.nuggetCatches,
                GearLevels = data.gearLevels,
                GearSlots = data.gearSlots,
                ThirdGearSlotBought = data.thirdGearSlotBought,
                FourthGearSlotOwned = data.fourthGearSlotOwned,
                ChestsWaiting = data.chestsWaiting,
                ChestOpening = data.chestOpening,
                ChestSecondsLeft = data.chestSecondsLeft,
                CatchesTowardChest = data.catchesTowardChest,
                ChestsOpened = data.chestsOpened,
                PerkRanks = data.perkRanks,
                GuildLevel = data.guildLevel,
                GuildXp = data.guildXp,
                Rebirths = data.rebirths,
                BestRegionsUnlocked = data.bestRegionsUnlocked,
                CurrentDay = data.hasDaily ? data.currentDay : -1,
                LastStreakClaimDay = data.hasDaily ? data.lastStreakClaimDay : -1,
                StreakIndex = data.streakIndex,
                RescueStreakIndex = data.hasDaily ? data.rescueStreakIndex : -1,
                JobKinds = data.jobKinds,
                JobProgress = data.jobProgress,
                JobClaimed = data.jobClaimed,
                FreeWashUsed = data.freeWashUsed,
                AdWashes = data.adWashes,
                GoalAds = data.goalAds,
                BoostMultiplier = data.boostMultiplier,
                BoostEndUtc = data.boostEndUtc,
                PeteSeen = data.peteSeen,
                FreeHireUsed = data.freeHireUsed,
                ReturnGiftGiven = data.returnGiftGiven,
                FirstDay = data.firstDay,
                SpentCents = data.spentCents,
                AdsRemoved = data.adsRemoved,
                AdsRemovedNoticePending = data.adsRemovedNoticePending,
                StarterEndUtc = data.starterEndUtc,
                StarterBought = data.starterBought,
                WelcomeOffer = data.welcomeOffer,
                WelcomeEndUtc = data.welcomeEndUtc,
                DailyOfferEndUtc = data.dailyOfferEndUtc,
                DailyOfferBoughtDay = data.dailyOfferBoughtDay,
                WeekendBoughtId = data.weekendBoughtId,
                WeekendBoughtMask = data.weekendBoughtMask,
                GoldWashSecondsLeft = data.goldWashSecondsLeft,
                PendingGiantNuggets = data.pendingGiantNuggets,
                Transactions = data.transactions,
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
                nuggetCatches = progress.NuggetCatches,
                gearLevels = progress.GearLevels,
                gearSlots = progress.GearSlots,
                thirdGearSlotBought = progress.ThirdGearSlotBought,
                fourthGearSlotOwned = progress.FourthGearSlotOwned,
                chestsWaiting = progress.ChestsWaiting,
                chestOpening = progress.ChestOpening,
                chestSecondsLeft = progress.ChestSecondsLeft,
                catchesTowardChest = progress.CatchesTowardChest,
                chestsOpened = progress.ChestsOpened,
                perkRanks = progress.PerkRanks,
                guildLevel = progress.GuildLevel,
                guildXp = progress.GuildXp,
                rebirths = progress.Rebirths,
                bestRegionsUnlocked = progress.BestRegionsUnlocked,
                hasDaily = true,
                currentDay = progress.CurrentDay,
                lastStreakClaimDay = progress.LastStreakClaimDay,
                streakIndex = progress.StreakIndex,
                rescueStreakIndex = progress.RescueStreakIndex,
                jobKinds = progress.JobKinds,
                jobProgress = progress.JobProgress,
                jobClaimed = progress.JobClaimed,
                freeWashUsed = progress.FreeWashUsed,
                adWashes = progress.AdWashes,
                goalAds = progress.GoalAds,
                boostMultiplier = progress.BoostMultiplier,
                boostEndUtc = progress.BoostEndUtc,
                peteSeen = progress.PeteSeen,
                freeHireUsed = progress.FreeHireUsed,
                returnGiftGiven = progress.ReturnGiftGiven,
                firstDay = progress.FirstDay,
                spentCents = progress.SpentCents,
                adsRemoved = progress.AdsRemoved,
                adsRemovedNoticePending = progress.AdsRemovedNoticePending,
                starterEndUtc = progress.StarterEndUtc,
                starterBought = progress.StarterBought,
                welcomeOffer = progress.WelcomeOffer,
                welcomeEndUtc = progress.WelcomeEndUtc,
                dailyOfferEndUtc = progress.DailyOfferEndUtc,
                dailyOfferBoughtDay = progress.DailyOfferBoughtDay,
                weekendBoughtId = progress.WeekendBoughtId,
                weekendBoughtMask = progress.WeekendBoughtMask,
                goldWashSecondsLeft = progress.GoldWashSecondsLeft,
                pendingGiantNuggets = progress.PendingGiantNuggets,
                transactions = progress.Transactions,
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
