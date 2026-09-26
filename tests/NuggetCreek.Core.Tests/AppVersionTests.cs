using System;
using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class AppVersionTests
    {
        [TestCase("0.1.0", 100)]
        [TestCase("0.1.1", 101)]
        [TestCase("1.0.0", 10000)]
        [TestCase("1.2.3", 10203)]
        [TestCase("2.99.99", 29999)]
        public void VersionCodeFollowsName(string name, int code)
        {
            Assert.That(AppVersion.Parse(name).VersionCode, Is.EqualTo(code));
        }

        [TestCase("")]
        [TestCase("1.0")]
        [TestCase("1.0.0.0")]
        [TestCase("1.100.0")]
        [TestCase("1.0.100")]
        [TestCase("1.-1.0")]
        [TestCase("1.a.0")]
        [TestCase("1..0")]
        public void RejectsBadNames(string name)
        {
            Assert.That(AppVersion.TryParse(name, out _), Is.False);
            Assert.Throws<FormatException>(() => AppVersion.Parse(name));
        }

        [Test]
        public void CodesGrowWithVersions()
        {
            string[] order = { "0.1.0", "0.1.1", "0.1.99", "0.2.0", "0.99.99", "1.0.0", "1.0.1", "10.0.0" };
            for (int i = 1; i < order.Length; i++)
                Assert.That(AppVersion.Parse(order[i]).VersionCode,
                    Is.GreaterThan(AppVersion.Parse(order[i - 1]).VersionCode), order[i]);
        }

        [Test]
        public void NextPatchRaisesCodeByOne()
        {
            AppVersion v = AppVersion.Parse("0.3.7");
            Assert.That(v.NextPatch().ToString(), Is.EqualTo("0.3.8"));
            Assert.That(v.NextPatch().VersionCode, Is.EqualTo(v.VersionCode + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AppVersion.Parse("0.3.99").NextPatch());
        }
    }
}
