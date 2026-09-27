using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Local notification state (design doc 10.1): the player's usual hour, the plan made at
    /// each leave, the fatigue rule, and when Amos asks for permission (10.1.4).
    /// </summary>
    public sealed partial class GameSession
    {
        const int HoursPerWeek = 7 * 24;
        const int MaxSessionHours = 64;

        /// <summary>Pine Hollow was just opened for the first time; the moment for the first ask.</summary>
        bool pineHollowJustOpened;

        void MarkFirstPineHollow() => pineHollowJustOpened = true;

        /// <summary>Logs a session start at this local time; keeps the last 7 days.</summary>
        public void RecordSessionHour(DateTimeOffset nowLocal)
        {
            int stamp = LocalHourStamp(nowLocal);
            var kept = new List<int>();
            foreach (int old in Progress.SessionHourLog)
                if (stamp - old < HoursPerWeek && old <= stamp)
                    kept.Add(old);
            kept.Add(stamp);
            if (kept.Count > MaxSessionHours)
                kept.RemoveRange(0, kept.Count - MaxSessionHours);
            Progress.SessionHourLog = kept.ToArray();
        }

        /// <summary>Median local hour of the last week's session starts; 19:00 without data.</summary>
        public int PlayerHour
        {
            get
            {
                var hours = new List<int>(Progress.SessionHourLog.Length);
                foreach (int stamp in Progress.SessionHourLog)
                    hours.Add(((stamp % 24) + 24) % 24);
                return NotificationPlanner.MedianHour(hours);
            }
        }

        /// <summary>
        /// Plans the chain for a leave now and remembers it, so the return can tell which
        /// notifications fired.
        /// </summary>
        public List<PlannedNotification> PlanNotifications(DateTimeOffset now)
        {
            double nowUtc = now.ToUnixTimeMilliseconds() / 1000.0;
            var context = new NotificationContext
            {
                Now = now,
                IdleActive = IdleActive,
                CapSeconds = OfflineCapSeconds,
                CapAmount = NumberFormat.Dollars(OfflineRate * OfflineCapSeconds),
                DailyUnlocked = IsUnlocked(Feature.Daily),
                PlayerHour = PlayerHour,
                ShortTermMuted = nowUtc < Progress.NotifMutedUntilUtc,
                CreekName = GameCatalog.RegionNames[Progress.RegionIndex],
                Sent = Progress.NotifSent,
            };
            List<PlannedNotification> plan = NotificationPlanner.Plan(context);
            Progress.NotifPendingKinds = new int[plan.Count];
            Progress.NotifPendingUtc = new double[plan.Count];
            for (int i = 0; i < plan.Count; i++)
            {
                Progress.NotifPendingKinds[i] = (int)plan[i].Kind;
                Progress.NotifPendingUtc[i] = plan[i].FireAt.ToUnixTimeMilliseconds() / 1000.0;
            }
            return plan;
        }

        /// <summary>
        /// Settles the last plan on a return: counts what fired, sends notif_open when the
        /// player came back through one, and applies the fatigue rule (10.1.1 rule 7) when
        /// they keep coming back on their own.
        /// </summary>
        /// <param name="opened">The notification the app was opened from, if any.</param>
        public void ResolveNotifications(double nowUtc, (NotificationKind Kind, int Variant, double FireUtc)? opened)
        {
            int fired = 0;
            for (int i = 0; i < Progress.NotifPendingKinds.Length; i++)
            {
                if (Progress.NotifPendingUtc[i] > nowUtc)
                    continue;
                fired++;
                int kind = Progress.NotifPendingKinds[i];
                if (kind >= 0 && kind < Progress.NotifSent.Length)
                    Progress.NotifSent[kind]++;
            }
            Progress.NotifPendingKinds = new int[0];
            Progress.NotifPendingUtc = new double[0];

            if (opened.HasValue)
            {
                Progress.NotifIgnored = 0;
                Emit("notif_open", ("type", KindName(opened.Value.Kind)), ("variant", opened.Value.Variant),
                    ("hours_late", Math.Round(Math.Max(0, nowUtc - opened.Value.FireUtc) / 3600, 1)));
                return;
            }
            Progress.NotifIgnored += fired;
            if (Progress.NotifIgnored >= Math.Max(1, Config.NotifFatigueCount))
            {
                Progress.NotifIgnored = 0;
                Progress.NotifMutedUntilUtc = nowUtc + Config.NotifFatigueMuteSeconds;
            }
        }

        /// <summary>
        /// Whether Amos's pre-prompt card should show now (10.1.4). The first ask comes at the
        /// first Pine Hollow unlock in the session1_region2 arm, otherwise after a later
        /// session's offline modal; the second and last ask after the next session's modal.
        /// </summary>
        /// <param name="afterOfflineModal">The welcome back modal was just closed.</param>
        /// <param name="alreadyAllowed">The OS already lets the game post notifications.</param>
        public bool NotifAskDue(bool afterOfflineModal, bool alreadyAllowed)
        {
            if (alreadyAllowed || Progress.NotifAsks >= 2)
                return false;
            if (Progress.NotifAsks == 1)
                return afterOfflineModal && Progress.Sessions > Progress.NotifAskSession;
            // Never at launch: the first moment is the unlock itself, not a save that is past it.
            if (Config.NotifAskMoment == NotifAskMoment.session1_region2 && pineHollowJustOpened && !afterOfflineModal)
                return true;
            return afterOfflineModal && Progress.Sessions >= 2;
        }

        /// <summary>Records an ask and sends notif_permission.</summary>
        /// <param name="osResult">"granted", "denied" or "skipped" when the player said Not now.</param>
        public void RecordNotifAsk(bool afterOfflineModal, bool softYes, string osResult)
        {
            pineHollowJustOpened = false;
            Progress.NotifAsks++;
            Progress.NotifAskSession = Progress.Sessions;
            Emit("notif_permission", ("attempt", Progress.NotifAsks),
                ("moment", afterOfflineModal ? "session2_offline" : "session1_region2"),
                ("soft_result", softYes ? "yes" : "not_now"), ("os_result", osResult));
        }

        public static string KindName(NotificationKind kind)
        {
            switch (kind)
            {
                case NotificationKind.CapFull:
                    return "cap_full";
                case NotificationKind.DailyReady:
                case NotificationKind.DailyReadyAgain:
                    return "daily";
                case NotificationKind.WinBack3:
                    return "winback_3";
                case NotificationKind.WinBack7:
                    return "winback_7";
                case NotificationKind.WinBack14:
                    return "winback_14";
                default:
                    return "winback_30";
            }
        }

        static int LocalHourStamp(DateTimeOffset nowLocal)
        {
            long days = (long)Math.Floor((nowLocal.DateTime - DateTime.MinValue).TotalDays);
            return (int)days * 24 + nowLocal.Hour;
        }
    }
}
