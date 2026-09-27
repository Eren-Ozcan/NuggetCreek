using System;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Brings older saves up to the current layout. Version 1 had 6 creeks and 30 Nugget
    /// types (five per creek); version 2 has 20 creeks and 46 types (design doc 6.2, 3.1.2).
    /// </summary>
    public static class SaveMigration
    {
        public const int CurrentVersion = 2;

        /// <summary>Nugget ids of version 1, in its save order.</summary>
        static readonly string[] VersionOneNuggets =
        {
            "pebble", "button", "teardrop", "crooked_thumb", "creek_heart",
            "pine_cone", "bark_chip", "acorn", "owl_eye", "hollow_crown",
            "tine", "coin_flake", "river_spoon", "two_tone", "wishbone",
            "ember", "rust_knot", "clay_brick", "rattler", "sunset_slab",
            "icicle", "snowball", "frost_flake", "polar_tooth", "glacier_eye",
            "canyon_shard", "echo_stone", "lantern", "miners_fist", "deep_king",
        };

        /// <summary>Upgrades a loaded save in place; returns true when anything changed.</summary>
        public static bool Upgrade(PlayerProgress progress, int savedVersion)
        {
            if (progress == null)
                throw new ArgumentNullException(nameof(progress));
            if (savedVersion >= CurrentVersion)
                return false;
            // Old creek k (1-based) is creek 2k-1: every old creek keeps its place on the curve.
            progress.RegionIndex = 2 * Math.Max(0, progress.RegionIndex);
            progress.RegionsUnlocked = 2 * Math.Max(1, progress.RegionsUnlocked) - 1;
            progress.BestRegionsUnlocked = 2 * Math.Max(1, progress.BestRegionsUnlocked) - 1;
            progress.NuggetCatches = RemapNuggets(progress.NuggetCatches);
            return true;
        }

        /// <summary>Types that survived keep their count by id; boss nuggets count as one drop
        /// per old catch, up to their third star. Dropped types are lost.</summary>
        static int[] RemapNuggets(int[] old)
        {
            var result = new int[GameCatalog.Nuggets.Count];
            if (old == null)
                return result;
            for (int i = 0; i < old.Length && i < VersionOneNuggets.Length; i++)
            {
                int index = IndexOf(VersionOneNuggets[i]);
                if (index < 0 || old[i] <= 0)
                    continue;
                result[index] = GameCatalog.IsBossNugget(index) ? Math.Min(old[i], 5) : old[i];
            }
            return result;
        }

        static int IndexOf(string id)
        {
            for (int i = 0; i < GameCatalog.Nuggets.Count; i++)
                if (GameCatalog.Nuggets[i].Id == id)
                    return i;
            return -1;
        }
    }
}
