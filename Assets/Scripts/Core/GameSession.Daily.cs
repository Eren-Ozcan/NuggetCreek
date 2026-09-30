using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>What a daily job counts (design doc 7.1).</summary>
    public enum DailyJobKind
    {
        ManualCatches,
        Nuggets,
        MotherLodes,
        ChestsOpened,
        UpgradesBought,
        Washes,
        GearLevels,
        CollectionStars,
    }

    /// <summary>One day of the login streak calendar.</summary>
    public readonly struct StreakReward
    {
        public readonly int Day;
        public readonly int Gems;
        public readonly BigNumber Dollars;
        public readonly int Chests;
        /// <summary>Gear box index (0 Green, 1 Orange, 2 Red) or -1.</summary>
        public readonly int Box;

        public StreakReward(int day, int gems, BigNumber dollars, int chests, int box)
        {
            Day = day;
            Gems = gems;
            Dollars = dollars;
            Chests = chests;
            Box = box;
        }
    }

    /// <summary>Daily systems (design doc 7.1): login streak, daily jobs, Daily Wash and its income boost.</summary>
    public sealed partial class GameSession
    {
        /// <summary>
        /// Trusted UTC now, set by the platform every frame; null until trusted time arrives.
        /// Days and boosts never read the device clock.
        /// </summary>
        public double? NowUtc { get; set; }

        /// <summary>Game day of a moment: days roll over at DailyRolloverHour local time.</summary>
        public static long DayNumber(double utcSeconds, double utcOffsetSeconds, double rolloverHour) =>
            (long)Math.Floor((utcSeconds + utcOffsetSeconds - rolloverHour * 3600) / 86400);

        public bool DayKnown => Progress.CurrentDay >= 0;

        /// <summary>Starts a new game day: resets jobs and washes and checks the streak. False if the day did not change.</summary>
        public bool UpdateDay(long today)
        {
            if (today <= Progress.CurrentDay)
                return false;
            Progress.CurrentDay = today;
            if (Progress.FirstDay <= 0)
                Progress.FirstDay = today;

            if (Progress.LastStreakClaimDay >= 0)
            {
                long gap = today - Progress.LastStreakClaimDay;
                if (gap >= 2)
                {
                    if (Progress.StreakIndex > 0)
                        Emit("daily_streak_reset", ("lost_day", Progress.StreakIndex + 1));
                    // One missed day can be bought back with an ad; more starts over.
                    Progress.RescueStreakIndex = gap == 2 ? Progress.StreakIndex : -1;
                    Progress.StreakIndex = 0;
                }
            }

            var dayRandom = new Random((int)(today % int.MaxValue));
            var kinds = new List<int>();
            for (int i = 0; i < Config.DailyJobTargets.Length; i++)
                kinds.Add(i);
            for (int slot = 0; slot < Progress.JobKinds.Length; slot++)
            {
                int pick = dayRandom.Next(kinds.Count);
                Progress.JobKinds[slot] = kinds[pick];
                kinds.RemoveAt(pick);
                Progress.JobProgress[slot] = 0;
                Progress.JobClaimed[slot] = false;
            }

            Progress.FreeWashUsed = false;
            Progress.AdWashes = 0;
            Progress.PouchesToday = 0;
            StartDayOffers();
            return true;
        }

        // --- Login streak ---

        /// <summary>1-based calendar day the next claim pays.</summary>
        public int StreakDay => Progress.StreakIndex + 1;

        public bool CanClaimStreak => DayKnown && Progress.LastStreakClaimDay < Progress.CurrentDay;

        /// <summary>True when yesterday was missed and an ad can keep the streak.</summary>
        public bool CanRescueStreak => CanClaimStreak && Progress.RescueStreakIndex >= 0;

        public int RescueStreakDay => Progress.RescueStreakIndex + 1;

        public StreakReward StreakRewardFor(int index)
        {
            double minutes = Config.StreakIncomeMinutes[index];
            BigNumber dollars = minutes > 0 ? IncomePerSecond * (minutes * 60) : BigNumber.Zero;
            return new StreakReward(index + 1, Config.StreakGems[index], dollars, Config.StreakChests[index], Config.StreakBoxes[index]);
        }

        /// <summary>Restores the streak after a rewarded ad; call before claiming today.</summary>
        public bool RescueStreak()
        {
            if (!CanRescueStreak)
                return false;
            Progress.StreakIndex = Progress.RescueStreakIndex;
            Progress.RescueStreakIndex = -1;
            streakRescued = true;
            return true;
        }

        public StreakReward? ClaimStreak()
        {
            if (!CanClaimStreak)
                return null;
            StreakReward reward = StreakRewardFor(Progress.StreakIndex);
            EarnGems(reward.Gems, "streak");
            Earn(reward.Dollars);
            Progress.ChestsWaiting += reward.Chests;
            if (reward.Box >= 0)
                GrantGearBox(reward.Box, "streak");
            Emit("daily_streak_claim", ("day", reward.Day), ("rescued", streakRescued));
            streakRescued = false;
            Progress.RescueStreakIndex = -1;
            Progress.LastStreakClaimDay = Progress.CurrentDay;
            Progress.StreakIndex = (Progress.StreakIndex + 1) % Config.StreakGems.Length;
            return reward;
        }

        // --- Daily jobs ---

        public int JobCount => Progress.JobKinds.Length;

        /// <summary>The next streak claim follows an ad rescue (daily_streak_claim.rescued).</summary>
        bool streakRescued;

        public DailyJobKind JobKind(int slot) => (DailyJobKind)Progress.JobKinds[slot];

        public int JobTarget(int slot) => Config.DailyJobTargets[Progress.JobKinds[slot]];

        public int JobProgress(int slot) => Math.Min(Progress.JobProgress[slot], JobTarget(slot));

        public bool JobDone(int slot) => DayKnown && Progress.JobProgress[slot] >= JobTarget(slot);

        public bool JobClaimed(int slot) => Progress.JobClaimed[slot];

        public bool ClaimJob(int slot)
        {
            if (!JobDone(slot) || Progress.JobClaimed[slot])
                return false;
            Progress.JobClaimed[slot] = true;
            EarnGems(Config.DailyJobGems, "daily_job");
            Emit("daily_job_done", ("job", EventValues.Snake(JobKind(slot).ToString())));
            return true;
        }

        void CountJob(DailyJobKind kind, int amount = 1)
        {
            if (!DayKnown || amount <= 0)
                return;
            for (int slot = 0; slot < Progress.JobKinds.Length; slot++)
                if (Progress.JobKinds[slot] == (int)kind && !Progress.JobClaimed[slot])
                    Progress.JobProgress[slot] += amount;
        }

        // --- Daily Wash ---

        public int WashesLeftToday => !DayKnown ? 0 : (Progress.FreeWashUsed ? 0 : 1) + Math.Max(0, Config.WashAdMax - Progress.AdWashes);

        public bool CanWashFree => DayKnown && NowUtc.HasValue && !Progress.FreeWashUsed;

        public bool CanWashWithAd => DayKnown && NowUtc.HasValue && Progress.FreeWashUsed && Progress.AdWashes < Config.WashAdMax;

        /// <summary>Outcome index from the listed odds. roll is uniform in [0, 1).</summary>
        public int RollWash(double roll)
        {
            double total = 0;
            foreach (double chance in Config.WashChances)
                total += chance;
            double target = roll * total;
            for (int i = 0; i < Config.WashChances.Length; i++)
            {
                target -= Config.WashChances[i];
                if (target < 0)
                    return i;
            }
            return Config.WashChances.Length - 1;
        }

        /// <summary>Runs one wash (the free one, or one after a rewarded ad) and pays it; returns the outcome index or -1.</summary>
        public int Wash(bool afterAd)
        {
            if (afterAd ? !CanWashWithAd : !CanWashFree)
                return -1;
            if (afterAd)
                Progress.AdWashes++;
            else
                Progress.FreeWashUsed = true;

            int outcome = RollWash(random.NextDouble());
            EarnGems(Config.WashGems[outcome], "wash");
            Emit("daily_wash", ("method", afterAd ? "ad" : "free"), ("outcome", outcome));
            if (Config.WashMultipliers[outcome] > 1)
                ApplyBoost(Config.WashMultipliers[outcome], Config.WashMinutes[outcome] * 60);
            if (Config.WashChests[outcome] > 0)
                Progress.ChestsWaiting += Config.WashChests[outcome];
            CountJob(DailyJobKind.Washes);
            return outcome;
        }

        // --- Income boost (real time) ---

        public double BoostMultiplier =>
            NowUtc.HasValue && NowUtc.Value < Progress.BoostEndUtc ? Math.Max(1, Progress.BoostMultiplier) : 1;

        public double BoostSecondsLeft =>
            BoostMultiplier > 1 ? Progress.BoostEndUtc - NowUtc.Value : 0;

        /// <summary>The stronger boost wins; the same multiplier adds its time.</summary>
        public void ApplyBoost(double multiplier, double seconds)
        {
            if (!NowUtc.HasValue || multiplier <= 1 || seconds <= 0)
                return;
            double now = NowUtc.Value;
            double current = BoostMultiplier;
            if (multiplier < current)
                return;
            if (multiplier == current)
            {
                Progress.BoostEndUtc += seconds;
                return;
            }
            Progress.BoostMultiplier = multiplier;
            Progress.BoostEndUtc = now + seconds;
        }

        /// <summary>Prestige multiplier times the running boosts: applies to swipe, idle, chests and events.</summary>
        public double IncomeMultiplier => PrestigeMultiplier * BoostMultiplier * GoldWashMultiplier;

        /// <summary>Extra offline Dollars for the part of an absence a boost covered.</summary>
        BigNumber OfflineBoostBonus(double leftUtc, double creditedSeconds)
        {
            if (Progress.BoostMultiplier <= 1 || creditedSeconds <= 0)
                return BigNumber.Zero;
            double covered = Math.Min(Progress.BoostEndUtc, leftUtc + creditedSeconds) - leftUtc;
            if (covered <= 0)
                return BigNumber.Zero;
            return OfflineRate * (covered * (Progress.BoostMultiplier - 1));
        }

        // --- Gear boxes granted as rewards ---

        /// <summary>Grants a gear box's cards for free (streak rewards).</summary>
        public List<GearCard> GrantGearBox(int box, string source = "streak")
        {
            int count = Config.GearBoxCards[box];
            var rarities = new Rarity[count];
            bool guaranteed = false;
            for (int i = 0; i < count; i++)
            {
                rarities[i] = RollRarity(Config.GearBoxOdds, box * 3, random.NextDouble());
                if (rarities[i] >= Config.GearBoxGuarantee[box])
                    guaranteed = true;
            }
            if (!guaranteed)
                rarities[count - 1] = Config.GearBoxGuarantee[box];

            var cards = new List<GearCard>(count);
            foreach (Rarity rarity in rarities)
                cards.Add(GrantGearCard(PickGear(rarity, random.NextDouble()), source));
            return cards;
        }
    }
}
