using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Release version rule. The version name is "major.minor.patch" and the Android versionCode
    /// is derived from it, so the two never drift: code = major * 10000 + minor * 100 + patch.
    /// Every Play upload needs a higher versionCode, so every upload bumps at least the patch.
    /// </summary>
    public readonly struct AppVersion : IComparable<AppVersion>
    {
        public const int MaxMinor = 99;
        public const int MaxPatch = 99;

        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;

        public AppVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || minor > MaxMinor || patch < 0 || patch > MaxPatch)
                throw new ArgumentOutOfRangeException(nameof(major), $"invalid version {major}.{minor}.{patch}");
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int VersionCode => Major * 10000 + Minor * 100 + Patch;

        public static bool TryParse(string text, out AppVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text.Split('.');
            if (parts.Length != 3)
                return false;
            var numbers = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 4)
                    return false;
                foreach (char c in parts[i])
                    if (c < '0' || c > '9')
                        return false;
                numbers[i] = int.Parse(parts[i]);
            }
            if (numbers[1] > MaxMinor || numbers[2] > MaxPatch)
                return false;
            version = new AppVersion(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        public static AppVersion Parse(string text)
        {
            if (!TryParse(text, out AppVersion version))
                throw new FormatException($"version must be major.minor.patch (minor, patch <= 99): '{text}'");
            return version;
        }

        public AppVersion NextPatch() => new AppVersion(Major, Minor, Patch + 1);

        public int CompareTo(AppVersion other) => VersionCode.CompareTo(other.VersionCode);

        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }
}
