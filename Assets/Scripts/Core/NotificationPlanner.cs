using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>Local notification kinds (design doc 10.1.2), in priority order.</summary>
    public enum NotificationKind
    {
        CapFull,
        DailyReady,
        DailyReadyAgain,
        WinBack3,
        WinBack7,
        WinBack14,
        WinBack30,
    }

    /// <summary>One notification to hand to the platform scheduler.</summary>
    public readonly struct PlannedNotification
    {
        public readonly NotificationKind Kind;
        /// <summary>Android channel: rewards, daily or news (10.1.1 rule 8).</summary>
        public readonly string Channel;
        public readonly DateTimeOffset FireAt;
        public readonly string Text;
        public readonly int Variant;

        public PlannedNotification(NotificationKind kind, string channel, DateTimeOffset fireAt, string text, int variant)
        {
            Kind = kind;
            Channel = channel;
            FireAt = fireAt;
            Text = text;
            Variant = variant;
        }
    }

    /// <summary>Everything the planner reads, taken when the game goes to background.</summary>
    public struct NotificationContext
    {
        /// <summary>Now, with the device's local offset.</summary>
        public DateTimeOffset Now;
        public bool IdleActive;
        public double CapSeconds;
        /// <summary>Formatted Dollars the full pan holds.</summary>
        public string CapAmount;
        public bool DailyUnlocked;
        /// <summary>Local hour the player usually plays (0-23).</summary>
        public int PlayerHour;
        /// <summary>Cap and daily reminders are off after three ignored ones (10.1.1 rule 7).</summary>
        public bool ShortTermMuted;
        public string CreekName;
        /// <summary>How many of each kind were already sent; picks the text variant.</summary>
        public int[] Sent;
    }

    /// <summary>
    /// The local notification chain (design doc 10.1): every notice is tied to a real game
    /// state, quiet hours 22:00-09:00 move it to 09:30, at most two a day and three hours
    /// apart, and after the 30th day there is silence. Engine-free so the rules are tested.
    /// </summary>
    public static class NotificationPlanner
    {
        public const int QuietStartHour = 22;
        public const int QuietEndHour = 9;
        public const int MaxPerDay = 2;
        public static readonly TimeSpan MinGap = TimeSpan.FromHours(3);
        public static readonly TimeSpan MorningSlot = new TimeSpan(9, 30, 0);

        static readonly string[][] Texts =
        {
            new[]
            {
                "Amos's pan is full. {amount} sitting on the bank, kid.",
                "Creek's been generous. Amos stopped at {cap}h. {amount} waiting.",
                "Pan's full. Amos is having coffee till you come back.",
            },
            new[]
            {
                "Fresh water in the Daily Wash. Could be nothing. Could be a nugget.",
                "New day on the creek. Three jobs on the board.",
                "Daily Wash is ready. I already looked. Didn't touch.",
            },
            null,
            new[] { "Creek's still running, kid. Amos has been saving your share." },
            new[] { "{amount} still waiting where you left it. Nobody's touched your claim." },
            new[] { "Something glinting at {creek}. Thought you should know." },
            new[] { "Last letter, kid. Your claim's still here if you want it." },
        };

        public static string ChannelOf(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.CapFull:
                    return "rewards";
                case NotificationKind.DailyReady:
                case NotificationKind.DailyReadyAgain:
                    return "daily";
                default:
                    return "news";
            }
        }

        public static List<PlannedNotification> Plan(NotificationContext c)
        {
            var wanted = new List<(NotificationKind Kind, DateTimeOffset At)>();
            if (!c.ShortTermMuted)
            {
                if (c.IdleActive && c.CapSeconds >= 3600)
                    wanted.Add((NotificationKind.CapFull, c.Now.AddSeconds(c.CapSeconds)));
                if (c.DailyUnlocked)
                {
                    DateTimeOffset daily = AtHourOnOrAfter(c.Now.AddHours(6), c.PlayerHour);
                    wanted.Add((NotificationKind.DailyReady, daily));
                    wanted.Add((NotificationKind.DailyReadyAgain, daily.AddDays(1)));
                }
            }
            wanted.Add((NotificationKind.WinBack3, OnDayAtHour(c.Now, 3, c.PlayerHour)));
            wanted.Add((NotificationKind.WinBack7, OnDayAtHour(c.Now, 7, c.PlayerHour)));
            wanted.Add((NotificationKind.WinBack14, OnDayAtHour(c.Now, 14, c.PlayerHour)));
            wanted.Add((NotificationKind.WinBack30, OnDayAtHour(c.Now, 30, c.PlayerHour)));

            // Highest priority first; a later one that breaks the daily cap or the gap is dropped.
            var kept = new List<(NotificationKind Kind, DateTimeOffset At)>();
            foreach ((NotificationKind kind, DateTimeOffset at) in wanted)
            {
                DateTimeOffset when = OutOfQuietHours(at);
                if (Fits(kept, when))
                    kept.Add((kind, when));
            }
            kept.Sort((a, b) => a.At.CompareTo(b.At));

            var plan = new List<PlannedNotification>(kept.Count);
            foreach ((NotificationKind kind, DateTimeOffset at) in kept)
            {
                string[] texts = Texts[(int)(kind == NotificationKind.DailyReadyAgain ? NotificationKind.DailyReady : kind)];
                int sent = c.Sent != null && (int)kind < c.Sent.Length ? c.Sent[(int)kind] : 0;
                if (kind == NotificationKind.DailyReadyAgain)
                    sent++;
                int variant = sent % texts.Length;
                plan.Add(new PlannedNotification(kind, ChannelOf(kind), at, Fill(texts[variant], c), variant));
            }
            return plan;
        }

        /// <summary>Moves a time inside 22:00-09:00 to the next 09:30.</summary>
        public static DateTimeOffset OutOfQuietHours(DateTimeOffset at)
        {
            int hour = at.Hour;
            if (hour >= QuietStartHour)
                return Midnight(at).AddDays(1).Add(MorningSlot);
            if (hour < QuietEndHour)
                return Midnight(at).Add(MorningSlot);
            return at;
        }

        /// <summary>The median local hour of the given session starts, or 19:00 with none.</summary>
        public static int MedianHour(IList<int> hours)
        {
            if (hours == null || hours.Count == 0)
                return 19;
            var sorted = new List<int>(hours);
            sorted.Sort();
            return sorted[(sorted.Count - 1) / 2];
        }

        static bool Fits(List<(NotificationKind Kind, DateTimeOffset At)> kept, DateTimeOffset at)
        {
            int sameDay = 0;
            foreach ((NotificationKind _, DateTimeOffset other) in kept)
            {
                if ((other - at).Duration() < MinGap)
                    return false;
                if (other.Date == at.Date)
                    sameDay++;
            }
            return sameDay < MaxPerDay;
        }

        static DateTimeOffset Midnight(DateTimeOffset at) => new DateTimeOffset(at.Date, at.Offset);

        static DateTimeOffset AtHourOnOrAfter(DateTimeOffset earliest, int hour)
        {
            DateTimeOffset slot = Midnight(earliest).AddHours(hour);
            return slot >= earliest ? slot : slot.AddDays(1);
        }

        static DateTimeOffset OnDayAtHour(DateTimeOffset now, int days, int hour) =>
            Midnight(now).AddDays(days).AddHours(hour);

        static string Fill(string text, NotificationContext c) => text
            .Replace("{amount}", c.CapAmount ?? "")
            .Replace("{cap}", Math.Round(c.CapSeconds / 3600).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{creek}", c.CreekName ?? "the creek");
    }
}
