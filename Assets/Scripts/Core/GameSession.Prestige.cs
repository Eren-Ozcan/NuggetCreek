using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Rebirth ("Stake a New Claim", design doc 6.5) and the Prospectors' Guild: a separate
    /// level line fed by rewarded ads whose levels are Perk Points for the perk tree (6.6)
    /// and unlock milestones.
    /// </summary>
    public sealed partial class GameSession
    {
        // --- Rebirth ---

        /// <summary>Prospecting XP a new claim would add now; the Guild's level 100 milestone raises it.</summary>
        public long ClaimXp
        {
            get
            {
                long xp = Economy.ProspectingXp(Progress.TotalEarned, Stats);
                if (HasMilestone(4))
                    xp = (long)Math.Floor(xp * Config.MilestoneXpMultiplier);
                return xp;
            }
        }

        public bool CanRebirth => ClaimXp >= Math.Max(1, Config.RebirthMinXp);

        /// <summary>Prestige multiplier after staking a new claim now.</summary>
        public double PrestigeMultiplierAfterRebirth =>
            Economy.PrestigeMultiplier(Progress.ProspectingXp + ClaimXp, TotalStars) * GuildIncomeMultiplier;

        /// <summary>
        /// Starts over at Nugget Creek with the claim's Prospecting XP. Dollars, sluice tier,
        /// sluice upgrades and creeks reset; crew, Amos, Gems, gear, collection, XP and the
        /// Guild stay.
        /// </summary>
        public bool Rebirth()
        {
            if (!CanRebirth)
                return false;
            long xp = ClaimXp;
            Emit("prestige", ("xp_gain", xp), ("region", Progress.RegionsUnlocked), ("play_mins", (long)(Progress.PlaySeconds / 60)));
            Progress.ProspectingXp += xp;
            Progress.Rebirths++;
            Progress.Dollars = BigNumber.Zero;
            Progress.TotalEarned = BigNumber.Zero;
            Progress.RegionIndex = 0;
            Progress.RegionsUnlocked = 1;
            Progress.TierIndex = 0;
            Array.Clear(Progress.UpgradeLevels, 0, Progress.UpgradeLevels.Length);
            RebuildStats();
            return true;
        }

        /// <summary>
        /// Hours a typical day of play (design doc 5.0.4: 28 min active, 10 h away) needs to
        /// afford the next creek, or the next tier once every creek is open. Infinity when
        /// nothing is left to buy.
        /// </summary>
        public double HoursToNextTarget
        {
            get
            {
                BigNumber? cost = HasNextRegion ? NextRegionCost : HasNextTier ? NextTierCost : null;
                if (!cost.HasValue)
                    return double.PositiveInfinity;
                if (Progress.Dollars >= cost.Value)
                    return 0;
                double away = Config.WallAwaySecondsPerDay;
                double cap = OfflineCapSeconds;
                double paidAway = Math.Min(away, cap) + Math.Max(0, away - cap) * Config.OfflinePastCapRate;
                BigNumber perDay = IncomePerSecond * Config.WallActiveSecondsPerDay + OfflineRate * paidAway;
                if (perDay.IsZero)
                    return double.PositiveInfinity;
                return ((cost.Value - Progress.Dollars) / perDay).ToDouble() * 24;
            }
        }

        /// <summary>
        /// The wall of design doc 5.0.5: the next target is more than RebirthWallHours of
        /// typical play away and a new claim would at least multiply income by
        /// RebirthSuggestGain.
        /// </summary>
        public bool RebirthSuggested =>
            ClaimXp >= Config.RebirthSuggestMinXp
            && PrestigeMultiplierAfterRebirth >= PrestigeMultiplier * Config.RebirthSuggestGain
            && HoursToNextTarget > Config.RebirthWallHours;

        // --- Guild ---

        public int GuildLevel => Progress.GuildLevel;

        public bool GuildMaxed => Progress.GuildLevel >= Config.GuildMaxLevel;

        public long GuildXpToNext => Economy.GuildXpToNext(Progress.GuildLevel);

        /// <summary>Adds Guild XP (rewarded ads; IAP packs in phase 3); returns levels gained.</summary>
        public int AddGuildXp(long amount)
        {
            if (amount <= 0 || GuildMaxed)
                return 0;
            int before = Progress.GuildLevel;
            Progress.GuildXp += amount;
            while (!GuildMaxed && Progress.GuildXp >= GuildXpToNext)
            {
                Progress.GuildXp -= GuildXpToNext;
                Progress.GuildLevel++;
            }
            if (GuildMaxed)
                Progress.GuildXp = 0;
            int gained = Progress.GuildLevel - before;
            if (gained > 0)
                RebuildStats();
            for (int level = before + 1; level <= Progress.GuildLevel; level++)
                Emit("level_up", ("character", "guild"), ("level", level), ("milestone", Array.IndexOf(Config.GuildMilestoneLevels, level) >= 0));
            return gained;
        }

        /// <summary>Guild XP for one rewarded ad watched from the Guild panel.</summary>
        public int AddGuildAdXp() => AddGuildXp(Config.GuildXpPerAd);

        /// <summary>Whether the milestone at GuildMilestoneLevels[index] is reached.</summary>
        public bool HasMilestone(int index) =>
            index >= 0 && index < Config.GuildMilestoneLevels.Length && Progress.GuildLevel >= Config.GuildMilestoneLevels[index];

        double GuildIncomeMultiplier => HasMilestone(3) ? Config.MilestoneIncomeMultiplier : 1;

        // --- Perks ---

        public int PerkRank(int index) => Progress.PerkRanks[index];

        public int PerkPointsSpent
        {
            get
            {
                int spent = 0;
                for (int i = 0; i < Progress.PerkRanks.Length; i++)
                    spent += Progress.PerkRanks[i] * GameCatalog.Perks[i].Cost;
                return spent;
            }
        }

        /// <summary>One Perk Point per Guild level, minus what the tree holds.</summary>
        public int PerkPoints => Progress.GuildLevel - PerkPointsSpent;

        public bool CanRankUpPerk(int index)
        {
            PerkDefinition perk = GameCatalog.Perks[index];
            return Progress.PerkRanks[index] < perk.MaxRank && PerkPoints >= perk.Cost;
        }

        public bool RankUpPerk(int index)
        {
            if (!CanRankUpPerk(index))
                return false;
            Progress.PerkRanks[index]++;
            RebuildStats();
            Emit("perk_buy", ("perk_id", GameCatalog.Perks[index].Id), ("rank", Progress.PerkRanks[index]), ("pp_left", PerkPoints));
            return true;
        }

        /// <summary>Free respec: every Perk Point comes back.</summary>
        public void ResetPerks()
        {
            int spent = PerkPointsSpent;
            Array.Clear(Progress.PerkRanks, 0, Progress.PerkRanks.Length);
            RebuildStats();
            Emit("perk_respec", ("pp_returned", spent));
        }

        void ApplyGuild()
        {
            for (int i = 0; i < GameCatalog.Perks.Count; i++)
            {
                PerkDefinition perk = GameCatalog.Perks[i];
                int rank = Math.Min(Progress.PerkRanks[i], perk.MaxRank);
                if (rank > 0)
                    Stats.Add(perk.Stat, perk.PerRank * rank);
            }
            if (HasMilestone(0))
                Stats.Add(Stat.VeinMaxLevel, Config.MilestoneVeinLevels);
            if (HasMilestone(1))
            {
                Stats.Add(Stat.ChestCapacity, Config.MilestoneChestCapacity);
                Stats.Add(Stat.ChestTime, Config.MilestoneChestTime);
            }
            if (HasMilestone(2))
                Stats.Add(Stat.MotherLodeMaxCombo, Config.MilestoneMotherLodeCombo);
        }
    }
}
