using System;
using System.Collections.Generic;
using System.Globalization;
using NuggetCreek.Core;
using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif

namespace NuggetCreek.Game
{
    /// <summary>What the game needs from the local notification system (design doc 10.1).</summary>
    public interface INotifications
    {
        /// <summary>The OS lets the game post notifications.</summary>
        bool Allowed { get; }

        /// <summary>Shows the OS permission dialog; reports "granted" or "denied".</summary>
        void RequestPermission(Action<string> onResult);

        /// <summary>Replaces everything pending with this plan.</summary>
        void Schedule(IList<PlannedNotification> plan);

        void CancelAll();

        /// <summary>The notification the app was opened from since the last call, if any.</summary>
        (NotificationKind Kind, int Variant, double FireUtc)? TakeOpened();

        void Tick();
    }

    /// <summary>Editor and non-Android stand-in: nothing is posted.</summary>
    public sealed class NoNotifications : INotifications
    {
        public bool Allowed => false;
        public void RequestPermission(Action<string> onResult) => onResult?.Invoke("denied");
        public void Schedule(IList<PlannedNotification> plan) { }
        public void CancelAll() { }
        public (NotificationKind Kind, int Variant, double FireUtc)? TakeOpened() => null;
        public void Tick() { }
    }

#if UNITY_ANDROID
    /// <summary>
    /// Unity Mobile Notifications on Android: three channels a player can switch off one by
    /// one (10.1.1 rule 8), inexact scheduling, and the plan kept in each notification's
    /// intent data so the return can tell which one opened the game.
    /// </summary>
    public sealed class AndroidNotifications : INotifications
    {
        const string Title = "Nugget Creek";

        PermissionRequest request;
        Action<string> requestDone;
        int lastOpenedId = -1;

        public AndroidNotifications()
        {
            AndroidNotificationCenter.Initialize();
            Register("rewards", "Full pan", "When Amos's pan is full.");
            Register("daily", "Daily Wash", "When the Daily Wash and new jobs are ready.");
            Register("news", "Creek news", "Now and then, when your claim has been quiet.");
        }

        static void Register(string id, string name, string description) =>
            AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(id, name, description, Importance.Default));

        public bool Allowed => AndroidNotificationCenter.UserPermissionToPost == PermissionStatus.Allowed;

        public void RequestPermission(Action<string> onResult)
        {
            request = new PermissionRequest();
            requestDone = onResult;
            Tick();
        }

        public void Tick()
        {
            if (request == null || request.Status == PermissionStatus.RequestPending)
                return;
            string result = request.Status == PermissionStatus.Allowed ? "granted" : "denied";
            Action<string> done = requestDone;
            request = null;
            requestDone = null;
            done?.Invoke(result);
        }

        public void Schedule(IList<PlannedNotification> plan)
        {
            AndroidNotificationCenter.CancelAllNotifications();
            foreach (PlannedNotification p in plan)
            {
                double fireUtc = p.FireAt.ToUnixTimeMilliseconds() / 1000.0;
                var notification = new AndroidNotification(Title, p.Text, p.FireAt.LocalDateTime)
                {
                    IntentData = $"{(int)p.Kind}|{p.Variant}|{fireUtc.ToString("R", CultureInfo.InvariantCulture)}",
                };
                AndroidNotificationCenter.SendNotification(notification, p.Channel);
            }
        }

        public void CancelAll() => AndroidNotificationCenter.CancelAllNotifications();

        public (NotificationKind Kind, int Variant, double FireUtc)? TakeOpened()
        {
            AndroidNotificationIntentData intent = AndroidNotificationCenter.GetLastNotificationIntent();
            if (intent == null || intent.Id == lastOpenedId)
                return null;
            lastOpenedId = intent.Id;
            string[] parts = (intent.Notification.IntentData ?? "").Split('|');
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int variant)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double fireUtc))
                return null;
            return ((NotificationKind)kind, variant, fireUtc);
        }
    }
#endif
}
