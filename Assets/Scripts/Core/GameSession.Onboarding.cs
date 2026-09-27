using System;

namespace NuggetCreek.Core
{
    /// <summary>A bottom-bar or top-bar screen that opens during onboarding (design doc 9.1).</summary>
    public enum Feature
    {
        Upgrades,
        Map,
        Shop,
        Daily,
        Guild,
    }

    /// <summary>
    /// Old Pete's one-time lines (design doc 9.1). Earlier members win when two are due;
    /// the return gift comes first because it belongs to the moment it is given.
    /// </summary>
    public enum PeteLine
    {
        WelcomeBack,
        Welcome,
        FirstDust,
        UpgradesOpen,
        AmosForHire,
        FirstChest,
        MapOpen,
        FreeHire,
        DailyOpen,
        GuildOpen,
    }

    /// <summary>Onboarding (design doc 9, 9.1, 10): feature locks, Old Pete, the free first hire and the return gift.</summary>
    public sealed partial class GameSession
    {
        /// <summary>Development only: opens every feature and silences Pete. Not saved.</summary>
        public bool IntroSkipped { get; set; }

        public bool IsUnlocked(Feature feature)
        {
            if (IntroSkipped)
                return true;
            switch (feature)
            {
                case Feature.Upgrades:
                    return Progress.ManualCollected >= Config.UpgradesUnlockCollected || AnyUpgradeBought || Progress.AmosLevel > 0;
                case Feature.Map:
                    return Progress.AmosLevel > 0 || Progress.BestRegionsUnlocked > 1;
                case Feature.Shop:
                    return Progress.BestRegionsUnlocked >= Config.ShopUnlockRegions;
                case Feature.Daily:
                    return Progress.PlaySeconds >= Config.DailyUnlockPlaySeconds
                        || (Progress.FirstDay > 0 && Progress.CurrentDay > Progress.FirstDay)
                        || Progress.LastStreakClaimDay >= 0;
                case Feature.Guild:
                    return Progress.BestRegionsUnlocked >= Config.GuildUnlockRegions || Progress.Rebirths > 0 || Progress.GuildLevel > 0;
                default:
                    return true;
            }
        }

        bool AnyUpgradeBought
        {
            get
            {
                foreach (int level in Progress.UpgradeLevels)
                    if (level > 0)
                        return true;
                return false;
            }
        }

        // --- The free first hire (the tutorial pays for it) ---

        /// <summary>The first candidate hire costs nothing; a pack's crew does not use it up.</summary>
        public bool FreeHireReady => !Progress.FreeHireUsed;

        // --- Old Pete ---

        static readonly PeteLine[] PeteLines = (PeteLine[])Enum.GetValues(typeof(PeteLine));

        public bool HasSeen(PeteLine line) => (Progress.PeteSeen & (1 << (int)line)) != 0;

        /// <summary>Records a shown line; Pete's lines are the tutorial steps in analytics (tutorial_step).</summary>
        public void MarkSeen(PeteLine line)
        {
            if (HasSeen(line))
                return;
            if (Progress.PeteSeen == 0)
                Emit("tutorial_begin");
            Progress.PeteSeen |= 1 << (int)line;
            Emit("tutorial_step", ("step", (int)line), ("step_name", EventValues.Snake(line.ToString())));
        }

        /// <summary>The first unseen line whose moment is now; a line whose moment has passed never shows.</summary>
        public PeteLine? NextPeteLine()
        {
            if (IntroSkipped)
                return null;
            foreach (PeteLine line in PeteLines)
                if (!HasSeen(line) && IsPeteMoment(line))
                    return line;
            return null;
        }

        bool IsPeteMoment(PeteLine line)
        {
            switch (line)
            {
                case PeteLine.Welcome:
                    return Progress.ManualCollected < 3 && Progress.RegionsUnlocked == 1 && Progress.Rebirths == 0;
                case PeteLine.FirstDust:
                    return Progress.ManualCollected >= 5 && Progress.ManualCollected < Config.UpgradesUnlockCollected;
                case PeteLine.UpgradesOpen:
                    return IsUnlocked(Feature.Upgrades) && !AnyUpgradeBought && Progress.AmosLevel == 0;
                case PeteLine.AmosForHire:
                    return Progress.AmosLevel == 0 && CanAfford(AmosNextCost);
                case PeteLine.FirstChest:
                    return HasChest && Progress.ChestsOpened == 0;
                case PeteLine.MapOpen:
                    return IsUnlocked(Feature.Map) && Progress.BestRegionsUnlocked == 1;
                case PeteLine.FreeHire:
                    return HasCandidates && FreeHireReady;
                case PeteLine.DailyOpen:
                    return IsUnlocked(Feature.Daily) && Progress.LastStreakClaimDay < 0;
                case PeteLine.GuildOpen:
                    return IsUnlocked(Feature.Guild) && Progress.GuildLevel == 0 && Progress.Rebirths == 0;
                case PeteLine.WelcomeBack:
                    return Progress.ReturnGiftGiven;
                default:
                    return false;
            }
        }

        // --- Second session (design doc 10) ---

        /// <summary>Gives the one-time return gift after the first real offline haul; false if already given.</summary>
        public bool GiveReturnGift()
        {
            if (Progress.ReturnGiftGiven)
                return false;
            Progress.ReturnGiftGiven = true;
            Progress.ChestsWaiting += Config.ReturnGiftChests;
            return true;
        }
    }
}
