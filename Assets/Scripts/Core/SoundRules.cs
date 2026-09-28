using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Sound rules from the design doc (15.3, 15.5) that need no engine: the rising pitch of
    /// back-to-back catches, the voice cap and the music levels.
    /// </summary>
    public static class SoundRules
    {
        /// <summary>At most this many effects sound at once; extra catches are dropped.</summary>
        public const int MaxVoices = 3;

        /// <summary>A run of catches climbs this many scale degrees, then starts over.</summary>
        public const int PickSteps = 5;

        /// <summary>A pause this long between catches ends the run.</summary>
        public const double PickRunGapSeconds = 1.0;

        /// <summary>Music sits 6 dB under the effects.</summary>
        public const float MusicDb = -6;

        /// <summary>The music dips while the Mother Lode rumbles.</summary>
        public const float MotherLodeDuckDb = -4;

        /// <summary>The dulcimer layer joins from this creek on (0-based: the third creek).</summary>
        public const int DulcimerFromRegion = 2;

        /// <summary>The harmonica layer plays while the player swiped this recently.</summary>
        public const double HarmonicaHoldSeconds = 1.5;

        // D mixolydian, the key of every track: semitones above the root.
        static readonly int[] Mixolydian = { 0, 2, 4, 5, 7, 9, 10 };

        /// <summary>Pitch factor for the catch at <paramref name="runIndex"/> (0 = first) in a run.</summary>
        public static float PickPitch(int runIndex)
        {
            int step = (runIndex % PickSteps + PickSteps) % PickSteps;
            return (float)Math.Pow(2, Mixolydian[step] / 12.0);
        }

        public static float Gain(float db) => (float)Math.Pow(10, db / 20);
    }
}
