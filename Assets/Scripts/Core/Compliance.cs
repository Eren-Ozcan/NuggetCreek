namespace NuggetCreek.Core
{
    /// <summary>Answer on the first-launch age screen (design doc 8.6, 14.2).</summary>
    public enum AgeBand
    {
        /// <summary>Not answered yet; nothing that collects data may start.</summary>
        Unknown = 0,
        Under13 = 1,
        Teen = 2,
        Adult = 3,
    }

    /// <summary>Vibration setting (design doc 14.3).</summary>
    public enum VibrationMode
    {
        Off = 0,
        /// <summary>Only chests, new Nuggets, unlocks and the Mother Lode.</summary>
        Important = 1,
        All = 2,
    }

    /// <summary>Content rating cap for ads; mirrors AdMob's G / PG / T / MA.</summary>
    public enum AdRating
    {
        G,
        PG,
        T,
        MA,
    }

    /// <summary>How ads and analytics must treat the player, from their age answer.</summary>
    public readonly struct DataAudience
    {
        /// <summary>COPPA child-directed treatment: no personalised ads, family-safe demand only.</summary>
        public readonly bool Child;

        /// <summary>Teen treatment in the ad SDK (under 18).</summary>
        public readonly bool Teen;

        /// <summary>Tagged under the age of consent: the consent form is skipped and ads stay non-personalised.</summary>
        public readonly bool UnderAgeOfConsent;

        public readonly AdRating MaxAdRating;

        /// <summary>Analytics and crash reports may be collected.</summary>
        public readonly bool Analytics;

        public DataAudience(bool child, bool teen, bool underAgeOfConsent, AdRating maxAdRating, bool analytics)
        {
            Child = child;
            Teen = teen;
            UnderAgeOfConsent = underAgeOfConsent;
            MaxAdRating = maxAdRating;
            Analytics = analytics;
        }
    }

    /// <summary>
    /// First-launch privacy screen and the age gate (design doc 8.6, 9, 14.2). The game is
    /// rated for everyone but not directed at children; a player who says they are under 13
    /// still plays, with child-directed ads and no analytics.
    /// </summary>
    public static class Compliance
    {
        /// <summary>Raise when the privacy text changes enough that players must accept it again.</summary>
        public const int TermsVersion = 1;

        public const string PrivacyPolicyUrl = "https://yilkgames.com/privacy-policy/";
        public const string DataDeletionUrl = "https://yilkgames.com/account-deletion/#data-only";

        /// <summary>The first-launch screen must be answered before anything else starts.</summary>
        public static bool NeedsGate(PlayerProgress progress) =>
            progress.AgeBand == AgeBand.Unknown || progress.TermsAccepted < TermsVersion;

        public static void Accept(PlayerProgress progress, AgeBand band)
        {
            if (band == AgeBand.Unknown)
                throw new System.ArgumentException("An age answer is required.", nameof(band));
            progress.AgeBand = band;
            progress.TermsAccepted = TermsVersion;
        }

        /// <summary>
        /// Everyone-rated game: no MA ads for anyone. An unanswered gate is treated like a
        /// child, so nothing collected before the answer can be personalised.
        /// </summary>
        public static DataAudience Audience(AgeBand band)
        {
            switch (band)
            {
                case AgeBand.Adult:
                    return new DataAudience(false, false, false, AdRating.T, true);
                case AgeBand.Teen:
                    return new DataAudience(false, true, true, AdRating.T, true);
                default:
                    return new DataAudience(true, false, true, AdRating.G, false);
            }
        }

        /// <summary>Whether a haptic of this weight plays under the player's setting.</summary>
        public static bool Vibrates(VibrationMode mode, bool important) =>
            mode == VibrationMode.All || (mode == VibrationMode.Important && important);

        /// <summary>
        /// Text scale the UI follows: the system font scale, capped at 130% (design doc
        /// 14.3) and never below 100%.
        /// </summary>
        public static float TextScale(float systemFontScale)
        {
            if (float.IsNaN(systemFontScale) || systemFontScale < 1)
                return 1;
            return systemFontScale > 1.3f ? 1.3f : systemFontScale;
        }
    }
}
