using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class NumberFormatTests
    {
        // First block matches fmt() in tools/economy_tune.py.
        [TestCase(0.0, "$0")]
        [TestCase(1.0, "$1")]
        [TestCase(0.5, "$0")]
        [TestCase(999.0, "$999")]
        [TestCase(1000.0, "$1K")]
        [TestCase(1234.5, "$1.234K")]
        [TestCase(9999.4, "$9.999K")]
        [TestCase(99999.0, "$100K")]
        [TestCase(639.3e9, "$639.3B")]
        [TestCase(1.24e12, "$1.24T")]
        [TestCase(1e18, "$1Qi")]
        [TestCase(2.5e21, "$2.5Sx")]
        [TestCase(7.1e33, "$7.1Dc")]
        [TestCase(2.5e36, "$2,500Dc")]
        // Deliberate fix over the Python helper: a value that rounds up to 1000 moves to the next suffix.
        [TestCase(999960.0, "$1M")]
        [TestCase(-1500.0, "-$1.5K")]
        public void Dollars(double value, string expected)
        {
            Assert.That(NumberFormat.Dollars(value), Is.EqualTo(expected));
        }

        [Test]
        public void NeverUsesScientificNotation()
        {
            for (int e = 0; e < 40; e++)
            {
                string text = NumberFormat.Dollars(BigNumber.Create(7.77777, e));
                Assert.That(text, Does.Not.Contain("E").And.Not.Contain("e"), text);
            }
        }
    }
}
