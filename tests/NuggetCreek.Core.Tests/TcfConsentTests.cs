using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class TcfConsentTests
    {
        [Test]
        public void OutsideTheEeaEverythingIsGranted()
        {
            TcfConsent consent = TcfConsent.From(0, null);
            Assert.That(consent.AnalyticsStorage && consent.AdStorage && consent.AdUserData && consent.AdPersonalization, Is.True);
        }

        [Test]
        public void AcceptAllGrantsEverything()
        {
            TcfConsent consent = TcfConsent.From(1, "11111111111");
            Assert.That(consent.AnalyticsStorage && consent.AdStorage && consent.AdUserData && consent.AdPersonalization, Is.True);
        }

        [Test]
        public void RejectAllOrNoAnswerDeniesEverything()
        {
            foreach (string consents in new[] { "00000000000", "", null })
            {
                TcfConsent consent = TcfConsent.From(1, consents);
                Assert.That(consent.AnalyticsStorage || consent.AdStorage || consent.AdUserData || consent.AdPersonalization, Is.False, consents);
            }
        }

        [Test]
        public void StorageWithoutPersonalisation()
        {
            TcfConsent consent = TcfConsent.From(1, "10000010000");
            Assert.That(consent.AdStorage, Is.True);
            Assert.That(consent.AdUserData, Is.True);
            Assert.That(consent.AdPersonalization, Is.False);
        }
    }
}
