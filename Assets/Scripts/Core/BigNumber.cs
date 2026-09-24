using System;
using System.Globalization;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Arbitrary-magnitude number stored as mantissa * 10^exponent.
    /// Dollar values grow past what a double can print readably, and the design
    /// requires critical counters to live outside plain int/float fields.
    /// Invariant: Mantissa is 0, or 1 &lt;= |Mantissa| &lt; 10.
    /// </summary>
    [Serializable]
    public readonly struct BigNumber : IComparable<BigNumber>, IEquatable<BigNumber>
    {
        // Past this exponent gap the smaller operand cannot change a double mantissa.
        const int PrecisionDigits = 17;

        public static readonly BigNumber Zero = new BigNumber(0, 0);
        public static readonly BigNumber One = new BigNumber(1, 0);

        public readonly double Mantissa;
        public readonly long Exponent;

        BigNumber(double mantissa, long exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        public bool IsZero => Mantissa == 0;
        public bool IsNegative => Mantissa < 0;

        public static BigNumber Create(double mantissa, long exponent)
        {
            if (double.IsNaN(mantissa) || double.IsInfinity(mantissa))
                throw new ArgumentOutOfRangeException(nameof(mantissa), "Mantissa must be finite.");
            if (mantissa == 0)
                return Zero;

            int shift = (int)Math.Floor(Math.Log10(Math.Abs(mantissa)));
            // Dividing by 10^-k is exact where multiplying by 0.1-style factors is not.
            double m = shift >= 0 ? mantissa / Math.Pow(10, shift) : mantissa * Math.Pow(10, -shift);
            long e = exponent + shift;

            // Pow/Log10 rounding can leave the mantissa a hair outside [1, 10).
            if (Math.Abs(m) >= 10)
            {
                m /= 10;
                e++;
            }
            else if (Math.Abs(m) < 1)
            {
                m *= 10;
                e--;
            }
            return new BigNumber(m, e);
        }

        public static BigNumber FromDouble(double value) => Create(value, 0);

        public static implicit operator BigNumber(double value) => FromDouble(value);

        /// <summary>Returns 10^log10Value, for results too large to compute as a double.</summary>
        public static BigNumber FromLog10(double log10Value)
        {
            double e = Math.Floor(log10Value);
            return Create(Math.Pow(10, log10Value - e), (long)e);
        }

        public double Log10()
        {
            if (Mantissa <= 0)
                throw new InvalidOperationException("Log10 is only defined for positive values.");
            return Math.Log10(Mantissa) + Exponent;
        }

        /// <summary>Converts to double; overflows to infinity above ~1e308.</summary>
        public double ToDouble()
        {
            if (IsZero)
                return 0;
            if (Exponent > 308)
                return IsNegative ? double.NegativeInfinity : double.PositiveInfinity;
            if (Exponent < -324)
                return 0;
            return Exponent >= 0 ? Mantissa * Math.Pow(10, Exponent) : Mantissa / Math.Pow(10, -Exponent);
        }

        public static BigNumber Pow(BigNumber value, double power)
        {
            if (value.IsZero)
                return power == 0 ? One : Zero;

            // Math.Pow is exact for small cases like 15^2; the log path would drift a few ulps.
            if (Math.Abs(value.Exponent) < 300)
            {
                double direct = Math.Pow(value.ToDouble(), power);
                if (direct != 0 && !double.IsInfinity(direct) && !double.IsNaN(direct))
                    return FromDouble(direct);
            }
            return FromLog10(value.Log10() * power);
        }

        public static BigNumber Max(BigNumber a, BigNumber b) => a >= b ? a : b;
        public static BigNumber Min(BigNumber a, BigNumber b) => a <= b ? a : b;

        public static BigNumber operator -(BigNumber a) => new BigNumber(-a.Mantissa, a.Exponent);

        public static BigNumber operator +(BigNumber a, BigNumber b)
        {
            if (a.IsZero)
                return b;
            if (b.IsZero)
                return a;

            long gap = a.Exponent - b.Exponent;
            if (gap > PrecisionDigits)
                return a;
            if (gap < -PrecisionDigits)
                return b;
            double aligned = gap >= 0 ? b.Mantissa / Math.Pow(10, gap) : b.Mantissa * Math.Pow(10, -gap);
            return Create(a.Mantissa + aligned, a.Exponent);
        }

        public static BigNumber operator -(BigNumber a, BigNumber b) => a + -b;

        public static BigNumber operator *(BigNumber a, BigNumber b)
        {
            if (a.IsZero || b.IsZero)
                return Zero;
            return Create(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);
        }

        public static BigNumber operator /(BigNumber a, BigNumber b)
        {
            if (b.IsZero)
                throw new DivideByZeroException();
            if (a.IsZero)
                return Zero;
            return Create(a.Mantissa / b.Mantissa, a.Exponent - b.Exponent);
        }

        public int CompareTo(BigNumber other)
        {
            int sign = Math.Sign(Mantissa);
            int otherSign = Math.Sign(other.Mantissa);
            if (sign != otherSign)
                return sign.CompareTo(otherSign);
            if (sign == 0)
                return 0;

            // Same sign: a larger exponent means a larger magnitude.
            int magnitude = Exponent != other.Exponent
                ? Exponent.CompareTo(other.Exponent)
                : Math.Abs(Mantissa).CompareTo(Math.Abs(other.Mantissa));
            return sign > 0 ? magnitude : -magnitude;
        }

        public bool Equals(BigNumber other) => Mantissa == other.Mantissa && Exponent == other.Exponent;
        public override bool Equals(object obj) => obj is BigNumber other && Equals(other);
        public override int GetHashCode() => Mantissa.GetHashCode() * 397 ^ Exponent.GetHashCode();

        public static bool operator ==(BigNumber a, BigNumber b) => a.Equals(b);
        public static bool operator !=(BigNumber a, BigNumber b) => !a.Equals(b);
        public static bool operator <(BigNumber a, BigNumber b) => a.CompareTo(b) < 0;
        public static bool operator >(BigNumber a, BigNumber b) => a.CompareTo(b) > 0;
        public static bool operator <=(BigNumber a, BigNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >=(BigNumber a, BigNumber b) => a.CompareTo(b) >= 0;

        /// <summary>Round-trip text form for saves, e.g. "1.2345e18". Not for display.</summary>
        public override string ToString() =>
            Mantissa.ToString("R", CultureInfo.InvariantCulture) + "e" +
            Exponent.ToString(CultureInfo.InvariantCulture);

        public static BigNumber Parse(string text)
        {
            if (!TryParse(text, out BigNumber value))
                throw new FormatException($"Not a BigNumber: '{text}'.");
            return value;
        }

        public static bool TryParse(string text, out BigNumber value)
        {
            value = Zero;
            if (string.IsNullOrEmpty(text))
                return false;

            int split = text.IndexOf('e');
            if (split <= 0 || split == text.Length - 1)
                return false;
            if (!double.TryParse(text.Substring(0, split), NumberStyles.Float, CultureInfo.InvariantCulture, out double m))
                return false;
            if (!long.TryParse(text.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long e))
                return false;
            if (double.IsNaN(m) || double.IsInfinity(m))
                return false;

            value = Create(m, e);
            return true;
        }
    }
}
