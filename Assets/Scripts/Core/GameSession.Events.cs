using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Analytics hooks (design doc 13.4): the rules emit events where they happen, the
    /// platform layer adds <see cref="CommonParameters"/> and sends them on.
    /// </summary>
    public sealed partial class GameSession
    {
        static readonly Feature[] Features = (Feature[])Enum.GetValues(typeof(Feature));

        public IGameEvents Events { get; set; } = NoGameEvents.Instance;

        /// <summary>This session's counts for session_end; restarted by <see cref="EndSessionParameters"/>.</summary>
        public SessionTally Tally { get; private set; } = new SessionTally();

        void Emit(string name, params (string Key, object Value)[] parameters) => Events.Emit(name, parameters);

        /// <summary>Counts a cold start; returns the new session number.</summary>
        public int BeginSession()
        {
            Progress.Sessions++;
            Tally = new SessionTally();
            return Progress.Sessions;
        }

        /// <summary>The snapshot every event carries (13.4.2), taken when the event is sent.</summary>
        public (string Key, object Value)[] CommonParameters() => new (string, object)[]
        {
            ("creek", Progress.RegionIndex + 1),
            ("tier", Progress.TierIndex + 1),
            ("prestige_n", Progress.Rebirths),
            ("crew_n", CrewHiredCount),
            ("amos_lvl", Progress.AmosLevel),
            ("guild_lvl", Progress.GuildLevel),
            ("gem_bal", Progress.Gems),
            ("dollar_log10", EventValues.Log10(Progress.Dollars)),
            ("session_n", Progress.Sessions),
        };

        /// <summary>session_end's own parameters; starts a fresh tally for the next foreground stretch.</summary>
        public (string Key, object Value)[] EndSessionParameters(double durationSeconds)
        {
            SessionTally t = Tally;
            Tally = new SessionTally();
            return new (string, object)[]
            {
                ("duration_s", (long)Math.Round(durationSeconds)),
                ("catches_n", t.ManualCatches),
                ("nuggets_n", t.Nuggets),
                ("upgrades_n", t.Upgrades),
                ("manual_log10", EventValues.Log10(t.ManualDollars)),
                ("idle_log10", EventValues.Log10(t.IdleDollars)),
                ("chest_log10", EventValues.Log10(t.ChestDollars)),
                ("event_log10", EventValues.Log10(t.EventDollars)),
                ("offline_log10", EventValues.Log10(t.OfflineDollars)),
            };
        }

        /// <summary>Sends feature_unlock once per feature, the first time its lock opens (9.1).</summary>
        public void CheckFeatureUnlocks()
        {
            if (IntroSkipped)
                return;
            foreach (Feature feature in Features)
            {
                int bit = 1 << (int)feature;
                if ((Progress.FeaturesAnnounced & bit) != 0 || !IsUnlocked(feature))
                    continue;
                Progress.FeaturesAnnounced |= bit;
                Emit("feature_unlock", ("feature", EventValues.Snake(feature.ToString())));
            }
        }

        void EmitCandidates(string outcome, int gemCost)
        {
            foreach (int member in Progress.CrewCandidates)
                Emit("crew_candidate", ("member", GameCatalog.Crew[member].Id), ("outcome", outcome), ("gem_cost", gemCost));
        }
    }
}
