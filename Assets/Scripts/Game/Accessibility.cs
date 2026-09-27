using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Short vibrations under the player's setting (design doc 14.3): a light tick per catch,
    /// a firmer pulse for the moments that matter. Android only; silent elsewhere.
    /// </summary>
    public static class Haptics
    {
        const float MinGapSeconds = 0.06f;
        const long TapMs = 12;
        const long ImportantMs = 45;
        // VibrationEffect.createOneShot needs API 26; older phones get Unity's fixed buzz.
        const int OneShotApi = 26;

        public static VibrationMode Mode { get; set; } = VibrationMode.All;

        static float lastAt = -1;
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static int sdk = -1;
#endif

        /// <summary>Every catch; dropped in "important only" mode.</summary>
        public static void Tap() => Play(false, TapMs, 60);

        /// <summary>Chests, new Nuggets, unlocks and the Mother Lode.</summary>
        public static void Important() => Play(true, ImportantMs, 200);

        static void Play(bool important, long milliseconds, int amplitude)
        {
            if (!Compliance.Vibrates(Mode, important))
                return;
            // A fast swipe catches several at once; one tick is enough.
            if (!important && lastAt >= 0 && Time.unscaledTime - lastAt < MinGapSeconds)
                return;
            lastAt = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (sdk < 0)
                {
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                        sdk = version.GetStatic<int>("SDK_INT");
                }
                if (sdk < OneShotApi)
                {
                    if (important)
                        Handheld.Vibrate();
                    return;
                }
                if (vibrator == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                using (AndroidJavaObject effect = effects.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude))
                    vibrator?.Call("vibrate", effect);
            }
            catch (AndroidJavaException e)
            {
                Debug.LogWarning("Vibration failed: " + e.Message);
                Mode = VibrationMode.Off;
            }
#endif
        }
    }

    /// <summary>Accessibility readings from the operating system.</summary>
    public static class DeviceSettings
    {
        /// <summary>The system font size setting (1 = default); 1 off Android.</summary>
        public static float FontScale()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject resources = activity.Call<AndroidJavaObject>("getResources"))
                using (AndroidJavaObject configuration = resources.Call<AndroidJavaObject>("getConfiguration"))
                    return configuration.Get<float>("fontScale");
            }
            catch (AndroidJavaException e)
            {
                Debug.LogWarning("Font scale unavailable: " + e.Message);
            }
#endif
            return 1;
        }
    }
}
