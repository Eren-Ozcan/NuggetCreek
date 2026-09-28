using System;
using System.Globalization;

namespace NuggetCreek.Core
{
    /// <summary>What to do with the local save once the cloud copy has been read.</summary>
    public enum CloudChoice
    {
        /// <summary>Play on with the local save; it overwrites the cloud copy on the next upload.</summary>
        KeepLocal,

        /// <summary>Replace the local save with the cloud copy without asking.</summary>
        RestoreCloud,

        /// <summary>Both saves hold real progress and the cloud one is further along: the player picks.</summary>
        AskPlayer,
    }

    /// <summary>
    /// Rules for the Play Games cloud backup (design doc 13.1 rule 4, 19.5 item 3.3). The cloud
    /// copy restores a fresh install or a damaged save by itself; when both sides hold real
    /// progress and the cloud one is further along, the player chooses, so a second phone never
    /// silently overwrites the first.
    /// </summary>
    public static class CloudSave
    {
        /// <summary>Name of the single Saved Games slot.</summary>
        public const string SlotName = "progress";

        /// <summary>Upload at most this often while playing; leaving the game always uploads.</summary>
        public const double UploadIntervalSeconds = 300;

        /// <summary>Play time below which a save with nothing else to show counts as fresh.</summary>
        public const double MeaningfulPlaySeconds = 120;

        /// <summary>
        /// Whether a save holds anything worth keeping. Settings and the first-launch answer do not
        /// count: a fresh install writes those before the cloud copy arrives.
        /// </summary>
        public static bool HasProgress(PlayerProgress progress) =>
            progress != null
            && (progress.Rebirths > 0 || progress.RegionsUnlocked > 1 || progress.Purchases > 0
                || progress.PlaySeconds >= MeaningfulPlaySeconds);

        /// <summary>Positive when <paramref name="a"/> is further along than <paramref name="b"/>.</summary>
        public static int Compare(PlayerProgress a, PlayerProgress b)
        {
            if (a.Rebirths != b.Rebirths)
                return a.Rebirths.CompareTo(b.Rebirths);
            if (a.RegionsUnlocked != b.RegionsUnlocked)
                return a.RegionsUnlocked.CompareTo(b.RegionsUnlocked);
            int earned = a.TotalEarned.CompareTo(b.TotalEarned);
            if (earned != 0)
                return earned;
            return a.PlaySeconds.CompareTo(b.PlaySeconds);
        }

        /// <param name="local">The save loaded on this device.</param>
        /// <param name="localDamaged">The local save failed its signature or could not be read.</param>
        /// <param name="cloud">The cloud copy, or null when there is none.</param>
        public static CloudChoice Choose(PlayerProgress local, bool localDamaged, PlayerProgress cloud)
        {
            if (!HasProgress(cloud))
                return CloudChoice.KeepLocal;
            if (localDamaged || !HasProgress(local))
                return CloudChoice.RestoreCloud;
            return Compare(cloud, local) > 0 ? CloudChoice.AskPlayer : CloudChoice.KeepLocal;
        }

        /// <summary>
        /// Clears what only made sense on the device that uploaded the save: the monotonic clock
        /// readings (so the next return is judged on trusted time alone, like after a reboot) and
        /// the notifications scheduled there.
        /// </summary>
        public static void PrepareRestored(PlayerProgress restored)
        {
            restored.LastMonotonicSeconds = 0;
            restored.LastBootUtc = 0;
            restored.NotifPendingKinds = new int[0];
            restored.NotifPendingUtc = new double[0];
        }

        /// <summary>One line for the save chooser: "Creeks open: 3 · Rebirths: 1 · Played: 5h".</summary>
        public static string Describe(PlayerProgress progress)
        {
            string played = NumberFormat.Hours(Math.Max(0, progress.PlaySeconds) / 3600);
            return string.Format(CultureInfo.InvariantCulture, "Creeks open: {0} · Rebirths: {1} · Played: {2}",
                progress.RegionsUnlocked, progress.Rebirths, played);
        }
    }
}
