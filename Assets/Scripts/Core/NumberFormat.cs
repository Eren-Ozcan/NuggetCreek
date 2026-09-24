using System;
using System.Globalization;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Display notation for Dollar amounts (design doc 5.0.8 and 13): one suffix list,
    /// at most 4 significant digits, no scientific notation, no raw digit strings.
    /// Gems and Perk Points are never abbreviated; format them as plain integers.
    /// </summary>
    public static class NumberFormat
    {
        const int SignificantDigits = 4;

        static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

        public static string Dollars(BigNumber value)
        {
            if (value.IsNegative)
                return "-" + Dollars(-value);
            return "$" + Abbreviate(value);
        }

        /// <summary>Same notation as <see cref="Dollars"/> without the currency sign.</summary>
        public static string Abbreviate(BigNumber value)
        {
            if (value.IsNegative)
                return "-" + Abbreviate(-value);
            if (value < 1000)
                return Math.Round(value.ToDouble(), MidpointRounding.ToEven).ToString("N0", CultureInfo.InvariantCulture);

            int tier = (int)Math.Min(value.Exponent / 3, Suffixes.Length - 1);
            double scaled = (value / BigNumber.Create(1, tier * 3L)).ToDouble();
            double rounded = RoundSignificant(scaled);

            // 999.96K rounds to 1000K; show it as 1M instead.
            if (rounded >= 1000 && tier < Suffixes.Length - 1)
            {
                tier++;
                rounded = RoundSignificant(rounded / 1000);
            }

            int decimals = DecimalsFor(rounded);
            string digits = rounded.ToString("N" + decimals, CultureInfo.InvariantCulture);
            if (decimals > 0)
                digits = digits.TrimEnd('0').TrimEnd('.');
            return digits + Suffixes[tier];
        }

        static double RoundSignificant(double value) => Math.Round(value, DecimalsFor(value), MidpointRounding.ToEven);

        static int DecimalsFor(double value)
        {
            // Past the last suffix the integer part can exceed 4 digits; it is kept whole.
            int integerDigits = value < 1 ? 1 : (int)Math.Floor(Math.Log10(value)) + 1;
            return Math.Max(0, SignificantDigits - integerDigits);
        }
    }
}
