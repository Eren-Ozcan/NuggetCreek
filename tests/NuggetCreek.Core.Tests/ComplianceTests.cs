using System;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class ComplianceTests
    {
        [Test]
        public void FreshSaveNeedsTheGate()
        {
            Assert.That(Compliance.NeedsGate(new PlayerProgress()), Is.True);
        }

        [Test]
        public void AcceptingClosesTheGateUntilTheTermsChange()
        {
            var progress = new PlayerProgress();
            Compliance.Accept(progress, AgeBand.Adult);
            Assert.That(Compliance.NeedsGate(progress), Is.False);
            Assert.That(progress.TermsAccepted, Is.EqualTo(Compliance.TermsVersion));

            progress.TermsAccepted = Compliance.TermsVersion - 1;
            Assert.That(Compliance.NeedsGate(progress), Is.True, "an older acceptance asks again");
        }

        [Test]
        public void AcceptingNeedsAnAgeAnswer()
        {
            Assert.Throws<ArgumentException>(() => Compliance.Accept(new PlayerProgress(), AgeBand.Unknown));
        }

        [Test]
        public void ChildrenGetChildDirectedAdsAndNoAnalytics()
        {
            DataAudience child = Compliance.Audience(AgeBand.Under13);
            Assert.That(child.Child, Is.True);
            Assert.That(child.UnderAgeOfConsent, Is.True);
            Assert.That(child.MaxAdRating, Is.EqualTo(AdRating.G));
            Assert.That(child.Analytics, Is.False);
        }

        [Test]
        public void UnansweredIsTreatedLikeAChild()
        {
            DataAudience unknown = Compliance.Audience(AgeBand.Unknown);
            Assert.That(unknown.Child && !unknown.Analytics, Is.True);
        }

        [Test]
        public void TeensSkipPersonalisedAdsButKeepAnalytics()
        {
            DataAudience teen = Compliance.Audience(AgeBand.Teen);
            Assert.That(teen.Child, Is.False);
            Assert.That(teen.Teen, Is.True);
            Assert.That(teen.UnderAgeOfConsent, Is.True);
            Assert.That(teen.MaxAdRating, Is.EqualTo(AdRating.T));
            Assert.That(teen.Analytics, Is.True);
        }

        [Test]
        public void AdultsAreUntaggedButNeverSeeMatureAds()
        {
            DataAudience adult = Compliance.Audience(AgeBand.Adult);
            Assert.That(adult.Child || adult.Teen || adult.UnderAgeOfConsent, Is.False);
            Assert.That(adult.MaxAdRating, Is.EqualTo(AdRating.T));
            Assert.That(adult.Analytics, Is.True);
        }

        [TestCase(VibrationMode.Off, false, false)]
        [TestCase(VibrationMode.Off, true, false)]
        [TestCase(VibrationMode.Important, false, false)]
        [TestCase(VibrationMode.Important, true, true)]
        [TestCase(VibrationMode.All, false, true)]
        [TestCase(VibrationMode.All, true, true)]
        public void VibrationFollowsTheSetting(VibrationMode mode, bool important, bool expected)
        {
            Assert.That(Compliance.Vibrates(mode, important), Is.EqualTo(expected));
        }

        [TestCase(0.85f, 1f)]
        [TestCase(1f, 1f)]
        [TestCase(1.15f, 1.15f)]
        [TestCase(1.3f, 1.3f)]
        [TestCase(2f, 1.3f)]
        [TestCase(float.NaN, 1f)]
        public void TextScaleFollowsTheSystemUpTo130Percent(float system, float expected)
        {
            Assert.That(Compliance.TextScale(system), Is.EqualTo(expected).Within(1e-6));
        }

        [Test]
        public void NormalizeRepairsBadSettings()
        {
            var progress = new PlayerProgress { AgeBand = (AgeBand)9, Vibration = (VibrationMode)(-1) };
            progress.Normalize();
            Assert.That(progress.AgeBand, Is.EqualTo(AgeBand.Unknown));
            Assert.That(progress.Vibration, Is.EqualTo(VibrationMode.All));
        }
    }
}
