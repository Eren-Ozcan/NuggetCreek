using System;

namespace NuggetCreek.Core
{
    /// <summary>What floats down the creek to be caught (design doc 3.1.4).</summary>
    public enum FloaterKind
    {
        /// <summary>A creek chest earned by catches; opens on a timer or with Gems.</summary>
        Chest,
        /// <summary>A Gem Pouch; opens only with a rewarded ad.</summary>
        Pouch,
        /// <summary>A Driftwood Crate; pays Dollars at once, a rewarded ad doubles them.</summary>
        Crate,
    }

    /// <summary>A claimed Driftwood Crate; the Dollars are already paid, a rewarded ad can pay them once more.</summary>
    public sealed class CrateReward
    {
        public readonly BigNumber Dollars;

        public bool Doubled { get; internal set; }

        public CrateReward(BigNumber dollars) => Dollars = dollars;
    }

    /// <summary>
    /// River floaters and the tray (design doc 3.1.4): creek chests, Gem Pouches and Driftwood
    /// Crates float down one at a time; a caught one waits in the tray until it is opened, a
    /// missed one is gone. Timers count only while the creek runs (<see cref="TickPlay"/>).
    /// </summary>
    public sealed partial class GameSession
    {
        double floaterGap;

        /// <summary>The floater on the water right now; the next one waits until it is caught or lost.</summary>
        public FloaterKind? FloaterOnWater { get; private set; }

        /// <summary>Crates and pouches start once the tutorial's first chest has been opened.</summary>
        bool FloatersOpen => Progress.ChestsOpened > 0;

        /// <summary>Floater lifetime on the water: gold's crossing time, slowed down.</summary>
        public double FloaterLifetimeSeconds => CollectibleLifetimeSeconds * Config.FloaterSlowdown;

        void TickRiver(double deltaSeconds)
        {
            if (FloaterOnWater == null)
                floaterGap = Math.Max(0, floaterGap - deltaSeconds);
            if (!FloatersOpen)
                return;
            Progress.CrateSecondsLeft = Progress.CrateSecondsLeft < 0
                ? Draw(Config.CrateMinSeconds, Config.CrateMaxSeconds)
                : Math.Max(0, Progress.CrateSecondsLeft - deltaSeconds);
            Progress.PouchSecondsLeft = Progress.PouchSecondsLeft < 0
                ? Draw(Config.GemPouchMinSeconds, Config.GemPouchMaxSeconds)
                : Math.Max(0, Progress.PouchSecondsLeft - deltaSeconds);
        }

        double Draw(double min, double max) => min + random.NextDouble() * Math.Max(0, max - min);

        bool CrateDue => FloatersOpen && Progress.CrateSecondsLeft == 0;

        /// <summary>Needs a trusted game day, since only so many come a day.</summary>
        bool PouchDue => FloatersOpen && Progress.PouchSecondsLeft == 0 && DayKnown
            && Progress.PouchesToday < Config.GemPouchesPerDay;

        /// <summary>
        /// Puts the next due floater on the water and returns it, or null when none is due, one
        /// is already out, or the gap after the last has not passed. Earned chests go first.
        /// </summary>
        public FloaterKind? LaunchFloater()
        {
            if (FloaterOnWater != null || floaterGap > 0)
                return null;
            if (Progress.ChestsDue > 0)
            {
                FloaterOnWater = FloaterKind.Chest;
            }
            else if (PouchDue)
            {
                Progress.PouchesToday++;
                Progress.PouchSecondsLeft = Draw(Config.GemPouchMinSeconds, Config.GemPouchMaxSeconds);
                FloaterOnWater = FloaterKind.Pouch;
            }
            else if (CrateDue)
            {
                Progress.CrateSecondsLeft = Draw(Config.CrateMinSeconds, Config.CrateMaxSeconds);
                FloaterOnWater = FloaterKind.Crate;
            }
            return FloaterOnWater;
        }

        /// <summary>The floater on the water was caught: it goes into the tray.</summary>
        public bool CatchFloater()
        {
            if (!(FloaterOnWater is FloaterKind kind))
                return false;
            EndFloater();
            switch (kind)
            {
                case FloaterKind.Chest:
                    Progress.ChestsDue = Math.Max(0, Progress.ChestsDue - 1);
                    Progress.ChestsWaiting++;
                    break;
                case FloaterKind.Pouch:
                    Progress.PouchesWaiting++;
                    break;
                default:
                    Progress.CratesWaiting++;
                    break;
            }
            return true;
        }

        /// <summary>
        /// The floater drifted out uncaught and is lost; only the tutorial's first chest comes
        /// back until it is caught.
        /// </summary>
        public bool MissFloater()
        {
            if (!(FloaterOnWater is FloaterKind kind))
                return false;
            EndFloater();
            Tally.FloatersMissed++;
            if (kind == FloaterKind.Chest && !FirstChestAhead)
                Progress.ChestsDue = Math.Max(0, Progress.ChestsDue - 1);
            return true;
        }

        void EndFloater()
        {
            FloaterOnWater = null;
            floaterGap = Config.FloaterGapSeconds;
        }

        // --- The tray ---

        /// <summary>Creek chests in the tray, the unlocking one included.</summary>
        public int ChestsInTray => Progress.ChestsWaiting + (Progress.ChestOpening ? 1 : 0);

        public int TrayCount => ChestsInTray + Progress.PouchesWaiting + Progress.CratesWaiting;

        /// <summary>Dollars a crate pays now: CrateIncomeSeconds of current income, raised by chest value and crate value.</summary>
        public BigNumber CrateDollars =>
            IncomePerSecond * (Config.CrateIncomeSeconds * Stats.Multiplier(Stat.ChestValue) * Stats.Multiplier(Stat.CrateValue));

        /// <summary>Opens a crate from the tray and pays it; null when none waits.</summary>
        public CrateReward ClaimCrate()
        {
            if (Progress.CratesWaiting <= 0)
                return null;
            Progress.CratesWaiting--;
            var reward = new CrateReward(CrateDollars);
            Earn(reward.Dollars, IncomeSource.Chest);
            Emit("chest_open", ("source", "crate"), ("method", "free"));
            return reward;
        }

        /// <summary>Pays a crate's Dollars a second time after a rewarded ad; once per crate.</summary>
        public bool DoubleCrate(CrateReward reward)
        {
            if (reward == null || reward.Doubled)
                return false;
            reward.Doubled = true;
            Earn(reward.Dollars, IncomeSource.Chest);
            return true;
        }

        /// <summary>Opens a pouch after its rewarded ad and pays its Gems; 0 when none waits.</summary>
        public int OpenPouch()
        {
            if (Progress.PouchesWaiting <= 0)
                return 0;
            Progress.PouchesWaiting--;
            int gems = random.Next(Config.GemPouchMinGems, Math.Max(Config.GemPouchMinGems, Config.GemPouchMaxGems) + 1);
            EarnGems(gems, "gem_pouch");
            Emit("chest_open", ("source", "gem_pouch"), ("method", "ad"));
            return gems;
        }

        /// <summary>Throws a pouch away unopened.</summary>
        public bool DiscardPouch()
        {
            if (Progress.PouchesWaiting <= 0)
                return false;
            Progress.PouchesWaiting--;
            return true;
        }
    }
}
