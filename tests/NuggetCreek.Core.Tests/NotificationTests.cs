using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class NotificationTests
    {
        static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

        static DateTimeOffset At(int day, int hour, int minute = 0) => new DateTimeOffset(2026, 9, day, hour, minute, 0, Istanbul);

        static NotificationContext Context(DateTimeOffset now, double capHours = 2, bool daily = true) => new NotificationContext
        {
            Now = now,
            IdleActive = true,
            CapSeconds = capHours * 3600,
            CapAmount = "$1.2K",
            DailyUnlocked = daily,
            PlayerHour = 19,
            CreekName = "Pine Hollow",
            Sent = new int[PlayerProgress.NotificationKinds],
        };

        [Test]
        public void FullChainForALeaveAtNoon()
        {
            List<PlannedNotification> plan = NotificationPlanner.Plan(Context(At(1, 12)));
            Assert.That(plan.Select(p => p.Kind), Is.EqualTo(new[]
            {
                NotificationKind.CapFull, NotificationKind.DailyReady, NotificationKind.DailyReadyAgain,
                NotificationKind.WinBack3, NotificationKind.WinBack7, NotificationKind.WinBack14, NotificationKind.WinBack30,
            }));
            Assert.That(plan[0].FireAt, Is.EqualTo(At(1, 14)));
            Assert.That(plan[0].Channel, Is.EqualTo("rewards"));
            Assert.That(plan[0].Text, Is.EqualTo("Amos's pan is full. $1.2K sitting on the bank, kid."));
            // Daily: the first 19:00 at least 6 hours out.
            Assert.That(plan[1].FireAt, Is.EqualTo(At(1, 19)));
            Assert.That(plan[2].FireAt, Is.EqualTo(At(2, 19)));
            Assert.That(plan[3].FireAt, Is.EqualTo(At(4, 19)));
            Assert.That(plan[6].FireAt, Is.EqualTo(new DateTimeOffset(2026, 10, 1, 19, 0, 0, Istanbul)));
            Assert.That(plan[5].Text, Does.Contain("Pine Hollow"));
            Assert.That(plan.Count(p => p.FireAt < At(8, 0)), Is.LessThanOrEqualTo(5), "first week");
        }

        [Test]
        public void QuietHoursMoveToHalfPastNine()
        {
            Assert.That(NotificationPlanner.OutOfQuietHours(At(1, 23, 10)), Is.EqualTo(At(2, 9, 30)));
            Assert.That(NotificationPlanner.OutOfQuietHours(At(2, 3)), Is.EqualTo(At(2, 9, 30)));
            Assert.That(NotificationPlanner.OutOfQuietHours(At(2, 9)), Is.EqualTo(At(2, 9)));
            Assert.That(NotificationPlanner.OutOfQuietHours(At(2, 21, 59)), Is.EqualTo(At(2, 21, 59)));
        }

        [Test]
        public void NeverMoreThanTwoADayOrCloserThanThreeHours()
        {
            // Leave at 21:00 with a 12 h cap: the cap lands at 09:00 and moves to 09:30.
            List<PlannedNotification> plan = NotificationPlanner.Plan(Context(At(1, 21), capHours: 12));
            foreach (IGrouping<DateTime, PlannedNotification> day in plan.GroupBy(p => p.FireAt.Date))
                Assert.That(day.Count(), Is.LessThanOrEqualTo(2), day.Key.ToString());
            for (int i = 1; i < plan.Count; i++)
                Assert.That(plan[i].FireAt - plan[i - 1].FireAt, Is.GreaterThanOrEqualTo(TimeSpan.FromHours(3)));
            foreach (PlannedNotification p in plan)
                Assert.That(p.FireAt.Hour >= 9 && p.FireAt.Hour < 22, Is.True, p.Kind.ToString());
        }

        [Test]
        public void ShortCapNoAmosAndLockedDailyLeaveOnlyWinBacks()
        {
            NotificationContext c = Context(At(1, 12), capHours: 0.5);
            c.DailyUnlocked = false;
            Assert.That(NotificationPlanner.Plan(c).Select(p => p.Kind), Is.EqualTo(new[]
            {
                NotificationKind.WinBack3, NotificationKind.WinBack7, NotificationKind.WinBack14, NotificationKind.WinBack30,
            }));
            c = Context(At(1, 12));
            c.IdleActive = false;
            Assert.That(NotificationPlanner.Plan(c).Any(p => p.Kind == NotificationKind.CapFull), Is.False);
        }

        [Test]
        public void TextsRotateBySentCount()
        {
            NotificationContext c = Context(At(1, 12));
            c.Sent[(int)NotificationKind.CapFull] = 1;
            Assert.That(NotificationPlanner.Plan(c)[0].Text, Is.EqualTo("Creek's been generous. Amos stopped at 2h. $1.2K waiting."));
            c.Sent[(int)NotificationKind.CapFull] = 2;
            Assert.That(NotificationPlanner.Plan(c)[0].Variant, Is.EqualTo(2));
        }

        [Test]
        public void MedianHourDefaultsToSevenPm()
        {
            Assert.That(NotificationPlanner.MedianHour(new int[0]), Is.EqualTo(19));
            Assert.That(NotificationPlanner.MedianHour(new[] { 8, 21, 20, 22 }), Is.EqualTo(20));
        }

        static GameSession NewSession(RecordedGameEvents events = null)
        {
            var session = new GameSession(new Economy(new EconomyConfig()), new PlayerProgress(), new Random(3));
            if (events != null)
                session.Events = events;
            return session;
        }

        [Test]
        public void SessionHoursKeepOneWeekAndGiveThePlayerHour()
        {
            GameSession session = NewSession();
            session.RecordSessionHour(At(1, 8));
            session.RecordSessionHour(At(9, 21));
            session.RecordSessionHour(At(10, 20));
            session.RecordSessionHour(At(10, 22));
            Assert.That(session.Progress.SessionHourLog.Length, Is.EqualTo(3), "day 1 is older than a week");
            Assert.That(session.PlayerHour, Is.EqualTo(21));
        }

        [Test]
        public void ThreeIgnoredNotificationsMuteReminders()
        {
            var events = new RecordedGameEvents();
            GameSession session = NewSession(events);
            session.Progress.AmosLevel = 1;
            session.Progress.PlaySeconds = 2 * 3600;
            session.PlanNotifications(At(1, 12));
            double later = At(4, 20).ToUnixTimeSeconds();
            session.ResolveNotifications(later, null);
            Assert.That(session.Progress.NotifMutedUntilUtc, Is.GreaterThan(later), "three fired unopened");
            Assert.That(session.Progress.NotifSent[(int)NotificationKind.WinBack3], Is.EqualTo(1));

            List<PlannedNotification> muted = session.PlanNotifications(At(4, 20));
            Assert.That(muted.Any(p => p.Kind == NotificationKind.CapFull || p.Kind == NotificationKind.DailyReady), Is.False);
            Assert.That(muted.Any(p => p.Kind == NotificationKind.WinBack3), Is.True);
            Assert.That(events.Named("notif_open"), Is.Empty);
        }

        [Test]
        public void OpeningOneResetsTheCountAndIsReported()
        {
            var events = new RecordedGameEvents();
            GameSession session = NewSession(events);
            session.Progress.NotifIgnored = 2;
            double fire = At(1, 14).ToUnixTimeSeconds();
            session.ResolveNotifications(fire + 1800, (NotificationKind.CapFull, 1, fire));
            Assert.That(session.Progress.NotifIgnored, Is.Zero);
            var open = events.Named("notif_open").Single();
            Assert.That(open["type"], Is.EqualTo("cap_full"));
            Assert.That(open["hours_late"], Is.EqualTo(0.5));
        }

        [Test]
        public void AmosAsksAtMostTwiceAtTheRightMoments()
        {
            var events = new RecordedGameEvents();
            GameSession session = NewSession(events);
            session.Progress.Sessions = 1;
            Assert.That(session.NotifAskDue(false, false), Is.False, "before Pine Hollow");
            session.Progress.BestRegionsUnlocked = 3;
            Assert.That(session.NotifAskDue(false, false), Is.False, "never at launch for a save past it");
            session.Progress.BestRegionsUnlocked = 1;
            session.Earn(1e9);
            session.UnlockNextRegion();
            Assert.That(session.NotifAskDue(false, true), Is.False, "already allowed");
            Assert.That(session.NotifAskDue(false, false), Is.True);
            session.RecordNotifAsk(false, false, "skipped");
            Assert.That(session.NotifAskDue(true, false), Is.False, "same session");
            session.Progress.Sessions = 2;
            Assert.That(session.NotifAskDue(false, false), Is.False, "only after the offline modal");
            Assert.That(session.NotifAskDue(true, false), Is.True);
            session.RecordNotifAsk(true, true, "denied");
            session.Progress.Sessions = 3;
            Assert.That(session.NotifAskDue(true, false), Is.False, "never a third time");
            var asks = events.Named("notif_permission").ToList();
            Assert.That(asks[1]["attempt"], Is.EqualTo(2));
            Assert.That(asks[1]["os_result"], Is.EqualTo("denied"));
        }

        [Test]
        public void OtherArmWaitsForTheSecondSession()
        {
            GameSession session = NewSession();
            session.Economy.Config.NotifAskMoment = NotifAskMoment.session2_offline;
            session.Progress.Sessions = 1;
            session.Progress.BestRegionsUnlocked = 3;
            Assert.That(session.NotifAskDue(false, false), Is.False);
            session.Progress.Sessions = 2;
            Assert.That(session.NotifAskDue(true, false), Is.True);
        }

        [Test]
        public void AskMomentRoundTripsThroughRemoteConfig()
        {
            var config = new EconomyConfig();
            RemoteConfig.Apply(config, new[] { new KeyValuePair<string, string>("notif_ask_moment", "session2_offline") });
            Assert.That(config.NotifAskMoment, Is.EqualTo(NotifAskMoment.session2_offline));
            Assert.That(RemoteConfig.Export(config).Single(p => p.Key == "notif_ask_moment").Value, Is.EqualTo("session2_offline"));
        }
    }
}
