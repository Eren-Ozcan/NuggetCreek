using System;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Loads and saves <see cref="PlayerProgress"/> as signed JSON in PlayerPrefs
    /// (design doc 13.1 rule 4) with <see cref="SaveKey"/>. A tampered or unreadable save
    /// starts a fresh game until cloud backup exists.
    /// </summary>
    public static class SaveStore
    {
        const string Key = "nc_save";

        /// <summary>
        /// What went wrong on the last load, as save_error (stage, code); null when nothing did.
        /// The load runs before analytics exists, so GameRoot sends it afterwards.
        /// </summary>
        public static (string Stage, string Code)? LoadIssue { get; private set; }

        [Serializable]
        sealed class SaveData
        {
            public int version = SaveMigration.CurrentVersion;
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
            public int featuresAnnounced;
            public int sessions;
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
            public double lastFullScreenAdUtc;
            public int[] sessionHourLog;
            public int[] notifSent;
            public int[] notifPendingKinds;
            public double[] notifPendingUtc;
            public int notifIgnored;
            public double notifMutedUntilUtc;
            public int notifAsks;
            public int notifAskSession;
            public int purchases;
            public int collectedSinceMotherLode;
            public double secondsSinceMotherLode;
            public double lastSeenUtc;
            public double lastDeviceUtc;
            public double lastMonotonicSeconds;
            public double lastBootUtc;
            public int ageBand;
            public int termsAccepted;
            // Saves from before the setting keep vibration on.
            public int vibration = (int)VibrationMode.All;
            public bool highContrast;
            // Saves from before the settings keep music and effects on.
            public bool musicOn = true;
            public bool soundOn = true;
        }

        public static bool HasSave => PlayerPrefs.HasKey(Key);

        public static PlayerProgress Load()
        {
            LoadIssue = SaveKey.Failure != null ? ("keystore", SaveKey.Failure) : ((string, string)?)null;
            string text = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(text))
                return new PlayerProgress();
            if (!TryUnwrap(text, out string json))
            {
                Debug.LogWarning("Save signature mismatch; starting a fresh game.");
                LoadIssue = ("load", "signature");
                return new PlayerProgress();
            }

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (ArgumentException)
            {
                data = null;
            }
            if (data == null)
            {
                // Signed but unreadable: only a bug writes this, and nothing in it can be trusted.
                Debug.LogWarning("Save is not valid JSON; starting a fresh game.");
                LoadIssue = ("load", "parse");
                return new PlayerProgress();
            }
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
                FeaturesAnnounced = data.featuresAnnounced,
                Sessions = data.sessions,
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
                LastFullScreenAdUtc = data.lastFullScreenAdUtc,
                SessionHourLog = data.sessionHourLog,
                NotifSent = data.notifSent,
                NotifPendingKinds = data.notifPendingKinds,
                NotifPendingUtc = data.notifPendingUtc,
                NotifIgnored = data.notifIgnored,
                NotifMutedUntilUtc = data.notifMutedUntilUtc,
                NotifAsks = data.notifAsks,
                NotifAskSession = data.notifAskSession,
                Purchases = data.purchases,
                CollectedSinceMotherLode = data.collectedSinceMotherLode,
                SecondsSinceMotherLode = data.secondsSinceMotherLode,
                LastSeenUtc = data.lastSeenUtc,
                LastDeviceUtc = data.lastDeviceUtc,
                LastMonotonicSeconds = data.lastMonotonicSeconds,
                LastBootUtc = data.lastBootUtc,
                AgeBand = (AgeBand)data.ageBand,
                TermsAccepted = data.termsAccepted,
                Vibration = (VibrationMode)data.vibration,
                HighContrast = data.highContrast,
                MusicOn = data.musicOn,
                SoundOn = data.soundOn,
            };
            SaveMigration.Upgrade(progress, data.version);
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
                featuresAnnounced = progress.FeaturesAnnounced,
                sessions = progress.Sessions,
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
                lastFullScreenAdUtc = progress.LastFullScreenAdUtc,
                sessionHourLog = progress.SessionHourLog,
                notifSent = progress.NotifSent,
                notifPendingKinds = progress.NotifPendingKinds,
                notifPendingUtc = progress.NotifPendingUtc,
                notifIgnored = progress.NotifIgnored,
                notifMutedUntilUtc = progress.NotifMutedUntilUtc,
                notifAsks = progress.NotifAsks,
                notifAskSession = progress.NotifAskSession,
                purchases = progress.Purchases,
                collectedSinceMotherLode = progress.CollectedSinceMotherLode,
                secondsSinceMotherLode = progress.SecondsSinceMotherLode,
                lastSeenUtc = progress.LastSeenUtc,
                lastDeviceUtc = progress.LastDeviceUtc,
                lastMonotonicSeconds = progress.LastMonotonicSeconds,
                lastBootUtc = progress.LastBootUtc,
                ageBand = (int)progress.AgeBand,
                termsAccepted = progress.TermsAccepted,
                vibration = (int)progress.Vibration,
                highContrast = progress.HighContrast,
                musicOn = progress.MusicOn,
                soundOn = progress.SoundOn,
            };
            PlayerPrefs.SetString(Key, SaveEnvelope.Wrap(JsonUtility.ToJson(data), SaveKey.Current()));
            PlayerPrefs.Save();
        }

        public static void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        static BigNumber ParseOrZero(string text) => BigNumber.TryParse(text, out BigNumber value) ? value : BigNumber.Zero;

        /// <summary>
        /// Checks the signature with the current key, then (during the one-time migration) with
        /// the pre-Keystore device key so saves from older builds carry over; the next write
        /// re-signs them.
        /// </summary>
        internal static bool TryUnwrap(string text, out string payload)
        {
            if (SaveEnvelope.TryUnwrap(text, SaveKey.Current(), out payload))
                return true;
            return SaveKey.AcceptsLegacy && SaveEnvelope.TryUnwrap(text, SaveKey.Legacy(), out payload);
        }
    }
}
