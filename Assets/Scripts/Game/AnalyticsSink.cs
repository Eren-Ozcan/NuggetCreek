using System;
using System.Globalization;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Sends the session's events to Firebase with the common snapshot (design doc 13.4.2)
    /// and minutes since install. The install time comes from the device clock on first
    /// launch; analytics only, never a payout.
    /// </summary>
    public sealed class AnalyticsSink : IGameEvents
    {
        const string InstallKey = "nc_install_utc";

        readonly GameSession session;
        readonly double installUtc;

        public AnalyticsSink(GameSession session)
        {
            this.session = session;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            string stored = PlayerPrefs.GetString(InstallKey, "");
            if (!double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out installUtc))
            {
                installUtc = now;
                PlayerPrefs.SetString(InstallKey, now.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        public void Emit(string name, params (string Key, object Value)[] parameters)
        {
            (string Key, object Value)[] common = session.CommonParameters();
            var all = new (string, object)[parameters.Length + common.Length + 1];
            parameters.CopyTo(all, 0);
            common.CopyTo(all, parameters.Length);
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            all[all.Length - 1] = ("mins_since_install", (long)Math.Max(0, (now - installUtc) / 60));
            FirebaseServices.Log(name, all);
        }
    }
}
