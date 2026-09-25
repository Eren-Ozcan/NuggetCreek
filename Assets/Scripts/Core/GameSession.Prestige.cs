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

        public bool CanRebirth => ClaimXp >= 1;

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
            long xp = ClaimXp;
            if (xp < 1)
                return false;
            Progress.ProspectingXp += xp;
            Progress.Rebirths++;
            Progress.Dollars = BigNumber.Zero;
            Progress.TotalEarned = BigNumber.Zero;
            Progress.RegionIndex = 0;
            Progress.RegionsUnlocked = 1;
            Progress.TierIndex = 0;
            Array.Clear(Progress.UpgradeLevels, 0, Progress.UpgradeLevels.Length);
            incomeSamples = null;
            RebuildStats();
            return true;
        }

        // Income once a minute over the suggestion window, oldest first. Not saved.
        BigNumber[] incomeSamples;
        int sampleCount;
        double sinceSample;

        const double SampleEverySeconds = 60;

        void SampleIncome(double deltaSeconds)
        {
            int size = (int)Math.Round(Config.RebirthSuggestWindowSeconds / SampleEverySeconds) + 1;
            if (incomeSamples == null || incomeSamples.Length != size)
            {
                incomeSamples = new BigNumber[size];
                sampleCount = 0;
                sinceSample = SampleEverySeconds;
            }
            sinceSample += deltaSeconds;
            if (sinceSample < SampleEverySeconds)
                return;
            sinceSample = 0;
            if (sampleCount == size)
            {
                Array.Copy(incomeSamples, 1, incomeSamples, 0, size - 1);
                sampleCount--;
            }
            incomeSamples[sampleCount++] = IncomePerSecond;
        }

        /// <summary>
        /// True when a new claim pays enough and income has stalled: grew less than
        /// RebirthSuggestGrowth over the last RebirthSuggestWindowSeconds of play.
        /// </summary>
        public bool RebirthSuggested
        {
            get
            {
                if (ClaimXp < Config.RebirthSuggestMinXp || incomeSamples == null || sampleCount < incomeSamples.Length)
                    return false;
                BigNumber oldest = incomeSamples[0];
                return IncomePerSecond < oldest * (1 + Config.RebirthSuggestGrowth);
            }
        }

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
            return true;
        }

        /// <summary>Free respec: every Perk Point comes back.</summary>
        public void ResetPerks()
        {
            Array.Clear(Progress.PerkRanks, 0, Progress.PerkRanks.Length);
            RebuildStats();
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
