using NUnit.Framework;
using NuggetCreek.Core;

namespace NuggetCreek.Core.Tests
{
    public class BigNumberTests
    {
        [TestCase(0.0)]
        [TestCase(1.0)]
        [TestCase(0.3)]
        [TestCase(225.0)]
        [TestCase(-42.5)]
        [TestCase(1.23456789e250)]
        public void FromDouble_RoundTrips(double value)
        {
            Assert.That(BigNumber.FromDouble(value).ToDouble(), Is.EqualTo(value));
        }

        [Test]
        public void Mantissa_IsNormalized()
        {
            BigNumber n = BigNumber.Create(12345, 2);
            Assert.That(n.Mantissa, Is.EqualTo(1.2345).Within(1e-15));
            Assert.That(n.Exponent, Is.EqualTo(6));
        }

        [Test]
        public void Arithmetic_BeyondDoubleRange()
        {
            BigNumber huge = BigNumber.Create(5, 400);
            BigNumber sum = huge + huge;
            Assert.That(sum.Mantissa, Is.EqualTo(1));
            Assert.That(sum.Exponent, Is.EqualTo(401));
            Assert.That((huge * huge).Exponent, Is.EqualTo(801));
            Assert.That((huge / BigNumber.Create(5, 399)).ToDouble(), Is.EqualTo(10));
            Assert.That(double.IsPositiveInfinity(huge.ToDouble()));
        }

        [Test]
        public void Add_IgnoresOperandBelowPrecision()
        {
            BigNumber big = BigNumber.Create(1, 30);
            Assert.That(big + 1, Is.EqualTo(big));
            Assert.That(1 + big, Is.EqualTo(big));
        }

        [Test]
        public void Subtract_ToZeroAndNegative()
        {
            Assert.That((BigNumber.FromDouble(7) - 7).IsZero);
            Assert.That((BigNumber.FromDouble(3) - 10).ToDouble(), Is.EqualTo(-7));
        }

        [Test]
        public void Compare_HandlesSignsAndExponents()
        {
            Assert.That(BigNumber.Create(9, 3) < BigNumber.Create(1, 4));
            Assert.That(BigNumber.Create(-1, 10) < BigNumber.Create(-1, 2));
            Assert.That(BigNumber.Create(-1, 2) < BigNumber.Zero);
            Assert.That(BigNumber.Zero < BigNumber.Create(1, -5));
            Assert.That(BigNumber.FromDouble(5).CompareTo(5), Is.EqualTo(0));
        }

        [Test]
        public void Pow_UsesLogPathForHugeResults()
        {
            BigNumber r = BigNumber.Pow(15, 1000);
            Assert.That(r.Exponent, Is.EqualTo(1176));
            Assert.That(r.Log10(), Is.EqualTo(1000 * System.Math.Log10(15)).Within(1e-9));
        }

        [TestCase("1.5e18")]
        [TestCase("-2.25e3")]
        [TestCase("0e0")]
        public void Parse_RoundTripsToString(string text)
        {
            BigNumber n = BigNumber.Parse(text);
            Assert.That(BigNumber.Parse(n.ToString()), Is.EqualTo(n));
        }

        [TestCase("")]
        [TestCase("12")]
        [TestCase("e5")]
        [TestCase("1e")]
        [TestCase("abce3")]
        [TestCase("NaNe1")]
        public void TryParse_RejectsGarbage(string text)
        {
            Assert.That(BigNumber.TryParse(text, out _), Is.False);
        }
    }
}
