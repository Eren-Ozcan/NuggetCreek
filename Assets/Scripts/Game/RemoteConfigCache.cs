using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// The Remote Config values fetched in an earlier session (design doc 13.2: config applies
    /// at session start, never mid-session). Read synchronously in Awake so the economy is
    /// final before the first frame; the background fetch only writes here for the next
    /// launch. Signed like the save so an edited PlayerPrefs file cannot retune the economy.
    /// </summary>
    public static class RemoteConfigCache
    {
        const string Key = "nc_rc";

        public static List<KeyValuePair<string, string>> Load()
        {
            string text = PlayerPrefs.GetString(Key, "");
            if (text.Length == 0)
                return new List<KeyValuePair<string, string>>();
            if (!SaveEnvelope.TryUnwrap(text, SaveStore.SigningKey(), out string payload))
            {
                Debug.LogWarning("RemoteConfigCache: signature mismatch, using defaults");
                return new List<KeyValuePair<string, string>>();
            }
            return RemoteConfig.Deserialize(payload);
        }

        public static void Save(IEnumerable<KeyValuePair<string, string>> values)
        {
            PlayerPrefs.SetString(Key, SaveEnvelope.Wrap(RemoteConfig.Serialize(values), SaveStore.SigningKey()));
            PlayerPrefs.Save();
        }

        /// <summary>Builds the session's config: defaults, then the cached server values.</summary>
        public static EconomyConfig BuildConfig()
        {
            var config = new EconomyConfig();
            List<KeyValuePair<string, string>> cached = Load();
            if (cached.Count > 0)
            {
                RemoteConfigResult result = RemoteConfig.Apply(config, cached);
                if (result.Rejected.Count > 0)
                    Debug.LogWarning("RemoteConfig rejected: " + string.Join(", ", result.Rejected));
                Debug.Log($"RemoteConfig applied {result.Applied.Count} cached values");
            }
            return config;
        }
    }
}
