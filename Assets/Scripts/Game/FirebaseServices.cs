using System;
using System.Collections.Generic;
using System.Diagnostics;
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Extensions;
using Firebase.RemoteConfig;
using NuggetCreek.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Firebase Analytics, Remote Config and Crashlytics (roadmap 3.2). Initialises in the
    /// background; events logged before that are queued. Remote Config is fetched here but
    /// only written to <see cref="RemoteConfigCache"/>, so new values take effect on the next
    /// launch. Off in the editor, so play mode and the smoke tests never touch the network.
    /// </summary>
    public static class FirebaseServices
    {
        const int MaxQueued = 64;

        static readonly Queue<KeyValuePair<string, Parameter[]>> queued = new Queue<KeyValuePair<string, Parameter[]>>();
        static bool started;
        static TcfConsent? pendingConsent;
        static readonly Dictionary<string, string> pendingProperties = new Dictionary<string, string>();

        public static bool Ready { get; private set; }

        /// <summary>Where this class's own events (config_fetch) go, so they carry the common snapshot.</summary>
        public static IGameEvents Events { get; set; }

        public static void Start()
        {
            if (started || Application.isEditor)
                return;
            started = true;
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.Result != DependencyStatus.Available)
                {
                    Debug.LogWarning("Firebase unavailable: " + task.Result);
                    return;
                }
                Crashlytics.ReportUncaughtExceptionsAsFatal = true;
                FirebaseAnalytics.SetUserProperty("build", UnityEngine.Debug.isDebugBuild ? "dev" : "release");
                Ready = true;
                ApplyConsent();
                foreach (KeyValuePair<string, string> property in pendingProperties)
                    FirebaseAnalytics.SetUserProperty(property.Key, property.Value);
                pendingProperties.Clear();
                while (queued.Count > 0)
                {
                    KeyValuePair<string, Parameter[]> e = queued.Dequeue();
                    FirebaseAnalytics.LogEvent(e.Key, e.Value);
                }
                FetchRemoteConfig();
            });
        }

        /// <summary>
        /// Consent Mode v2 from the consent form (design doc 13.4.4); applied as soon as
        /// Firebase is up if it is not yet.
        /// </summary>
        public static void SetConsent(TcfConsent consent)
        {
            pendingConsent = consent;
            if (Ready)
                ApplyConsent();
        }

        static void ApplyConsent()
        {
            if (!pendingConsent.HasValue)
                return;
            TcfConsent c = pendingConsent.Value;
            FirebaseAnalytics.SetConsent(new Dictionary<ConsentType, ConsentStatus>
            {
                { ConsentType.AnalyticsStorage, c.AnalyticsStorage ? ConsentStatus.Granted : ConsentStatus.Denied },
                { ConsentType.AdStorage, c.AdStorage ? ConsentStatus.Granted : ConsentStatus.Denied },
                { ConsentType.AdUserData, c.AdUserData ? ConsentStatus.Granted : ConsentStatus.Denied },
                { ConsentType.AdPersonalization, c.AdPersonalization ? ConsentStatus.Granted : ConsentStatus.Denied },
            });
        }

        /// <summary>Sets a user property now, or once Firebase is up.</summary>
        public static void SetUserProperty(string name, string value)
        {
            if (!started)
                return;
            if (Ready)
                FirebaseAnalytics.SetUserProperty(name, value);
            else
                pendingProperties[name] = value;
        }

        /// <summary>Logs one analytics event; values are long, double or string.</summary>
        public static void Log(string name, params (string key, object value)[] parameters)
        {
            if (!started)
                return;
            var list = new Parameter[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                list[i] = ToParameter(parameters[i].key, parameters[i].value);
            if (Ready)
                FirebaseAnalytics.LogEvent(name, list);
            else if (queued.Count < MaxQueued)
                queued.Enqueue(new KeyValuePair<string, Parameter[]>(name, list));
        }

        static void Emit(string name, params (string Key, object Value)[] parameters)
        {
            if (Events != null)
                Events.Emit(name, parameters);
            else
                Log(name, parameters);
        }

        static Parameter ToParameter(string key, object value)
        {
            switch (value)
            {
                case bool b:
                    return new Parameter(key, b ? 1L : 0L);
                case int i:
                    return new Parameter(key, (long)i);
                case long l:
                    return new Parameter(key, l);
                case float f:
                    return new Parameter(key, (double)f);
                case double d:
                    return new Parameter(key, d);
                default:
                    return new Parameter(key, value?.ToString() ?? "");
            }
        }

        static void FetchRemoteConfig()
        {
            var watch = Stopwatch.StartNew();
            FirebaseRemoteConfig rc = FirebaseRemoteConfig.DefaultInstance;
            // Development builds fetch every launch so a console change shows on the next start.
            TimeSpan maxAge = UnityEngine.Debug.isDebugBuild ? TimeSpan.Zero : TimeSpan.FromHours(1);
            rc.FetchAsync(maxAge).ContinueWithOnMainThread(fetch =>
            {
                if (fetch.IsFaulted || fetch.IsCanceled)
                {
                    Emit("config_fetch", ("ok", 0), ("ms", watch.ElapsedMilliseconds));
                    return;
                }
                rc.ActivateAsync().ContinueWithOnMainThread(_ =>
                {
                    var values = new List<KeyValuePair<string, string>>();
                    foreach (KeyValuePair<string, ConfigValue> pair in rc.AllValues)
                    {
                        if (pair.Value.Source == ValueSource.RemoteValue)
                            values.Add(new KeyValuePair<string, string>(pair.Key, pair.Value.StringValue));
                    }
                    RemoteConfigCache.Save(values);
                    RemoteConfigResult check = RemoteConfig.Apply(new EconomyConfig(), values);
                    if (check.Rejected.Count > 0)
                        Debug.LogWarning("RemoteConfig fetched bad values: " + string.Join(", ", check.Rejected));
                    Emit("config_fetch", ("ok", 1), ("ms", watch.ElapsedMilliseconds), ("values", values.Count), ("rejected", check.Rejected.Count));
                });
            });
        }
    }
}
