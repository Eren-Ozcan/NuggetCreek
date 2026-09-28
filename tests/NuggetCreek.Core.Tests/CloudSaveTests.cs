using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class CloudSaveTests
    {
        static PlayerProgress Played(int regions = 1, int rebirths = 0, double seconds = 3600, double earned = 1000)
        {
            return new PlayerProgress
            {
                RegionsUnlocked = regions,
                Rebirths = rebirths,
                PlaySeconds = seconds,
                TotalEarned = earned,
            };
        }

        [Test]
        public void AFreshSaveHasNoProgressEvenAfterTheGate()
        {
            var fresh = new PlayerProgress();
            Compliance.Accept(fresh, AgeBand.Adult);
            fresh.MusicOn = false;
            fresh.PlaySeconds = 30;
            Assert.That(CloudSave.HasProgress(fresh), Is.False);
            Assert.That(CloudSave.HasProgress(null), Is.False);
        }

        [Test]
        public void PlayTimeCreeksRebirthsOrAPurchaseCountAsProgress()
        {
            Assert.That(CloudSave.HasProgress(new PlayerProgress { PlaySeconds = CloudSave.MeaningfulPlaySeconds }), Is.True);
            Assert.That(CloudSave.HasProgress(new PlayerProgress { RegionsUnlocked = 2 }), Is.True);
            Assert.That(CloudSave.HasProgress(new PlayerProgress { Rebirths = 1 }), Is.True);
            Assert.That(CloudSave.HasProgress(new PlayerProgress { Purchases = 1 }), Is.True);
        }

        [Test]
        public void AFreshInstallRestoresTheCloudCopy()
        {
            Assert.That(CloudSave.Choose(new PlayerProgress(), false, Played()), Is.EqualTo(CloudChoice.RestoreCloud));
        }

        [Test]
        public void ADamagedSaveRestoresTheCloudCopyEvenWhenItLooksFurther()
        {
            Assert.That(CloudSave.Choose(Played(regions: 5), true, Played(regions: 2)), Is.EqualTo(CloudChoice.RestoreCloud));
        }

        [Test]
        public void NoCloudCopyOrAnEmptyOneKeepsTheLocalSave()
        {
            Assert.That(CloudSave.Choose(Played(), false, null), Is.EqualTo(CloudChoice.KeepLocal));
            Assert.That(CloudSave.Choose(new PlayerProgress(), true, new PlayerProgress()), Is.EqualTo(CloudChoice.KeepLocal));
        }

        [Test]
        public void TwoRealSavesAskOnlyWhenTheCloudIsFurther()
        {
            Assert.That(CloudSave.Choose(Played(regions: 2), false, Played(regions: 3)), Is.EqualTo(CloudChoice.AskPlayer));
            Assert.That(CloudSave.Choose(Played(regions: 3), false, Played(regions: 2)), Is.EqualTo(CloudChoice.KeepLocal));
            Assert.That(CloudSave.Choose(Played(), false, Played()), Is.EqualTo(CloudChoice.KeepLocal), "a tie keeps this phone");
        }

        [Test]
        public void RebirthsOutrankCreeksWhichOutrankDollarsAndPlayTime()
        {
            Assert.That(CloudSave.Compare(Played(regions: 1, rebirths: 1), Played(regions: 6)), Is.Positive);
            Assert.That(CloudSave.Compare(Played(regions: 3, earned: 1), Played(regions: 2, earned: 1e9)), Is.Positive);
            Assert.That(CloudSave.Compare(Played(earned: 2000, seconds: 10), Played(earned: 1000, seconds: 9999)), Is.Positive);
            Assert.That(CloudSave.Compare(Played(seconds: 7200), Played(seconds: 3600)), Is.Positive);
        }

        [Test]
        public void ARestoredSaveForgetsTheOtherDevicesClockAndNotifications()
        {
            PlayerProgress restored = Played();
            restored.LastSeenUtc = 1_800_000_000;
            restored.LastMonotonicSeconds = 5000;
            restored.LastBootUtc = 1_799_995_000;
            restored.NotifPendingKinds = new[] { 1 };
            restored.NotifPendingUtc = new[] { 1_800_000_600.0 };

            CloudSave.PrepareRestored(restored);

            Assert.That(restored.LastSeenUtc, Is.EqualTo(1_800_000_000), "the leave stamp still pays the absence");
            Assert.That(restored.LastMonotonicSeconds, Is.Zero);
            Assert.That(restored.LastBootUtc, Is.Zero);
            Assert.That(restored.NotifPendingKinds, Is.Empty);
            Assert.That(restored.NotifPendingUtc, Is.Empty);
        }

        [Test]
        public void TheChooserLineShowsCreeksRebirthsAndPlayTime()
        {
            Assert.That(CloudSave.Describe(Played(regions: 3, rebirths: 1, seconds: 5 * 3600)),
                Is.EqualTo("Creeks open: 3 · Rebirths: 1 · Played: 5h"));
        }
    }
}
