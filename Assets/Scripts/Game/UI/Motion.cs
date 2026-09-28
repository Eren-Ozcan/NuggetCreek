using UnityEngine;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Small time-based curves for UI animation. Panels call these from their per-frame Refresh
    /// with the seconds since the animation started, so no coroutine or tween library is needed.
    /// </summary>
    public static class Motion
    {
        /// <summary>Scale that jumps to <paramref name="peak"/> and settles back to 1 over <paramref name="seconds"/>.</summary>
        public static float Punch(float t, float seconds, float peak = 1.2f)
        {
            if (t <= 0 || t >= seconds)
                return 1;
            float k = t / seconds;
            return 1 + (peak - 1) * (1 - k) * Mathf.Cos(k * Mathf.PI * 1.5f);
        }

        /// <summary>Side-to-side wobble in degrees that grows toward the end of <paramref name="seconds"/>.</summary>
        public static float Shake(float t, float seconds, float degrees = 10)
        {
            if (t <= 0 || t >= seconds)
                return 0;
            return Mathf.Sin(t * 45) * degrees * (t / seconds);
        }

        /// <summary>0 before <paramref name="start"/>, 1 after <paramref name="start"/> + <paramref name="seconds"/>.</summary>
        public static float FadeIn(float t, float start, float seconds) =>
            seconds <= 0 ? (t >= start ? 1 : 0) : Mathf.Clamp01((t - start) / seconds);

        /// <summary>Gentle idle breathing for something waiting to be tapped.</summary>
        public static float Bob(float time, float amount = 0.04f) => 1 + amount * Mathf.Sin(time * 4);
    }
}
