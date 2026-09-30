using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Where the rules report what happened, for analytics (design doc 13.4). The platform
    /// layer forwards events to Firebase and adds the common snapshot; tests record them.
    /// Values are int, long, double, bool or string.
    /// </summary>
    public interface IGameEvents
    {
        void Emit(string name, params (string Key, object Value)[] parameters);
    }

    /// <summary>Drops every event; the default until the platform layer plugs a sink in.</summary>
    public sealed class NoGameEvents : IGameEvents
    {
        public static readonly NoGameEvents Instance = new NoGameEvents();

        NoGameEvents()
        {
        }

        public void Emit(string name, params (string Key, object Value)[] parameters)
        {
        }
    }

    /// <summary>Keeps every event in order; for tests and debugging.</summary>
    public sealed class RecordedGameEvents : IGameEvents
    {
        public readonly List<(string Name, Dictionary<string, object> Parameters)> Events =
            new List<(string, Dictionary<string, object>)>();

        public void Emit(string name, params (string Key, object Value)[] parameters)
        {
            var map = new Dictionary<string, object>();
            foreach ((string key, object value) in parameters)
                map[key] = value;
            Events.Add((name, map));
        }

        public List<Dictionary<string, object>> Named(string name) =>
            Events.FindAll(e => e.Name == name).ConvertAll(e => e.Parameters);

        public void Clear() => Events.Clear();
    }

    /// <summary>
    /// What one session earned and did, sent with session_end (13.4.3). Per-catch actions
    /// never send their own events; they are counted here instead.
    /// </summary>
    public sealed class SessionTally
    {
        public long ManualCatches;
        public long Nuggets;
        public long Upgrades;
        /// <summary>Chests, crates and pouches that drifted out uncaught (design doc 3.1.4).</summary>
        public long FloatersMissed;
        public BigNumber ManualDollars = BigNumber.Zero;
        public BigNumber IdleDollars = BigNumber.Zero;
        public BigNumber ChestDollars = BigNumber.Zero;
        public BigNumber EventDollars = BigNumber.Zero;
        public BigNumber OfflineDollars = BigNumber.Zero;

        public void Add(IncomeSource source, BigNumber amount)
        {
            switch (source)
            {
                case IncomeSource.Manual: ManualDollars += amount; break;
                case IncomeSource.Idle: IdleDollars += amount; break;
                case IncomeSource.Chest: ChestDollars += amount; break;
                case IncomeSource.Event: EventDollars += amount; break;
                case IncomeSource.Offline: OfflineDollars += amount; break;
            }
        }
    }

    /// <summary>Where Dollars came from, for the session_end split.</summary>
    public enum IncomeSource
    {
        Manual,
        Idle,
        Chest,
        Event,
        Offline,
        Other,
    }

    public static class EventValues
    {
        /// <summary>Dollar amounts go out as log10 (13.4.2); zero sends 0.</summary>
        public static double Log10(BigNumber amount) =>
            amount <= BigNumber.Zero ? 0 : Math.Round(amount.Log10(), 3);

        /// <summary>"GoldWash" to "gold_wash".</summary>
        public static string Snake(string name) => RemoteConfig.SnakeCase(name);
    }
}
