using System.Text;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class SaveEnvelopeTests
    {
        static readonly byte[] Key = Encoding.UTF8.GetBytes("test-key-not-for-release");
        const string Payload = "{\"dollars\":\"1.5e12\",\"gems\":40}";

        [Test]
        public void RoundTrip()
        {
            string wrapped = SaveEnvelope.Wrap(Payload, Key);
            Assert.That(SaveEnvelope.TryUnwrap(wrapped, Key, out string payload), Is.True);
            Assert.That(payload, Is.EqualTo(Payload));
        }

        [Test]
        public void EditedPayload_IsRejected()
        {
            string wrapped = SaveEnvelope.Wrap(Payload, Key).Replace("\"gems\":40", "\"gems\":99999");
            Assert.That(SaveEnvelope.TryUnwrap(wrapped, Key, out _), Is.False);
        }

        [Test]
        public void WrongKey_IsRejected()
        {
            string wrapped = SaveEnvelope.Wrap(Payload, Key);
            Assert.That(SaveEnvelope.TryUnwrap(wrapped, Encoding.UTF8.GetBytes("other"), out _), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("NC1|abc")]
        [TestCase("NC0|abc|{}")]
        [TestCase("{\"dollars\":1}")]
        public void Malformed_IsRejected(string text)
        {
            Assert.That(SaveEnvelope.TryUnwrap(text, Key, out _), Is.False);
        }

        [Test]
        public void PayloadMayContainSeparator()
        {
            const string tricky = "{\"note\":\"a|b|c\"}";
            Assert.That(SaveEnvelope.TryUnwrap(SaveEnvelope.Wrap(tricky, Key), Key, out string payload), Is.True);
            Assert.That(payload, Is.EqualTo(tricky));
        }
    }
}
