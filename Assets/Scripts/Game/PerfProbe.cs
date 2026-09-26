#if NC_PERF_PROBE || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.Profiling;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Logs frame rate and Unity memory to logcat every few seconds, for the phase 3.1 device
    /// check (30 FPS on an old phone, under 300 MB). Compiled only into development builds and
    /// the measurement APK (NC_PERF_PROBE); store builds never carry it. PSS, the number the
    /// RAM budget is about, comes from `adb shell dumpsys meminfo`, not from here.
    /// Grep with: adb logcat -s Unity | grep PerfProbe
    /// </summary>
    sealed class PerfProbe : MonoBehaviour
    {
        const float WindowSeconds = 10f;
        const float SlowFrameSeconds = 1f / 30f;

        float windowStart;
        int frames;
        int slowFrames;
        float worstFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var go = new GameObject(nameof(PerfProbe));
            DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>();
        }

        void OnEnable() => ResetWindow();

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            frames++;
            if (dt > SlowFrameSeconds)
                slowFrames++;
            if (dt > worstFrame)
                worstFrame = dt;

            float elapsed = Time.realtimeSinceStartup - windowStart;
            if (elapsed < WindowSeconds)
                return;
            const float Mb = 1024f * 1024f;
            Debug.Log($"[PerfProbe] fps={frames / elapsed:F1} slow={slowFrames}/{frames} " +
                      $"worstMs={worstFrame * 1000f:F0} " +
                      $"monoMb={Profiler.GetMonoUsedSizeLong() / Mb:F1} " +
                      $"allocMb={Profiler.GetTotalAllocatedMemoryLong() / Mb:F1} " +
                      $"reservedMb={Profiler.GetTotalReservedMemoryLong() / Mb:F1} " +
                      $"gfxMb={Profiler.GetAllocatedMemoryForGraphicsDriver() / Mb:F1}");
            ResetWindow();
        }

        void ResetWindow()
        {
            windowStart = Time.realtimeSinceStartup;
            frames = 0;
            slowFrames = 0;
            worstFrame = 0f;
        }
    }
}
#endif
