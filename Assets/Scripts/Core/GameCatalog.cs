using System.Collections.Generic;

namespace NuggetCreek.Core
{
    public sealed class UpgradeDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int TierIndex;
        public readonly Stat Stat;
        public readonly double PerLevel;
        public readonly int MaxLevel;

        public UpgradeDefinition(string id, string name, int tierIndex, Stat stat, double perLevel, int maxLevel)
        {
            Id = id;
            Name = name;
            TierIndex = tierIndex;
            Stat = stat;
            PerLevel = perLevel;
            MaxLevel = maxLevel;
        }
    }

    /// <summary>A hireable crew member. Each one moves exactly one stat (single-axis roles).</summary>
    public sealed class CrewDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly Stat Stat;
        public readonly double PerLevel;

        public CrewDefinition(string id, string name, Stat stat, double perLevel)
        {
            Id = id;
            Name = name;
            Stat = stat;
            PerLevel = perLevel;
        }
    }

    public enum Rarity
    {
        Common,
        Rare,
        Legendary,
    }

    /// <summary>One of the 30 collectible Nugget types; each creek drops its own five.</summary>
    public sealed class NuggetDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int RegionIndex;
        public readonly Rarity Rarity;
        public readonly string Flavor;

        public NuggetDefinition(string id, string name, int regionIndex, Rarity rarity, string flavor)
        {
            Id = id;
            Name = name;
            RegionIndex = regionIndex;
            Rarity = rarity;
            Flavor = flavor;
        }
    }

    /// <summary>A piece of gear (design doc 6.4). Equipped gear adds PerLevel * level to one stat.</summary>
    public sealed class GearDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly Rarity Rarity;
        public readonly Stat Stat;
        public readonly double PerLevel;

        public GearDefinition(string id, string name, Rarity rarity, Stat stat, double perLevel)
        {
            Id = id;
            Name = name;
            Rarity = rarity;
            Stat = stat;
            PerLevel = perLevel;
        }
    }

    /// <summary>A Prospectors' Guild perk (design doc 6.6): each rank adds PerRank to one stat.</summary>
    public sealed class PerkDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly Stat Stat;
        public readonly double PerRank;
        public readonly int MaxRank;
        public readonly int Cost;

        public PerkDefinition(string id, string name, Stat stat, double perRank, int maxRank, int cost)
        {
            Id = id;
            Name = name;
            Stat = stat;
            PerRank = perRank;
            MaxRank = maxRank;
            Cost = cost;
        }
    }

    /// <summary>What a progress goal counts. Every kind reads a lifetime value from the save.</summary>
    public enum GoalKind
    {
        ManualCollected,
        UpgradeLevels,
        AmosLevel,
        RegionsUnlocked,
        SluiceTier,
        CrewHired,
    }

    /// <summary>One step of the progress goal chain; its Gem reward lives in <see cref="EconomyConfig.GoalGemRewards"/>.</summary>
    public sealed class GoalDefinition
    {
        public readonly GoalKind Kind;
        public readonly int Target;

        public GoalDefinition(GoalKind kind, int target)
        {
            Kind = kind;
            Target = target;
        }
    }

    /// <summary>
    /// Fixed game content: sluice upgrades (design doc 6.1), crew (6.3) and progress goals. Prices live in
    /// <see cref="EconomyConfig"/> so Remote Config can tune them; effects are content.
    /// </summary>
    public static class GameCatalog
    {
        public const int CrewMaxLevel = 10;

        public const int GearMaxLevel = 10;

        /// <summary>Amos is the 16th crew member but outside the hire system: he joins for
        /// Dollars, starts idle collection and his level is the offline cap in hours.</summary>
        public const string AmosId = "amos";

        /// <summary>Creek names in unlock order (design doc 6.2); the launch economy covers the first 6.</summary>
        public static readonly IReadOnlyList<string> RegionNames = new[]
        {
            "Nugget Creek", "Pine Hollow", "Silver Fork", "Red Gulch", "Frost Basin", "Deep Canyon",
            "Quartz Flats", "Glacier Run", "Sunken Mine", "Last Chance Lode",
        };

        public static readonly IReadOnlyList<UpgradeDefinition> Upgrades = new[]
        {
            new UpgradeDefinition("sturdy_shovel", "Sturdy Shovel", 0, Stat.DustValue, 0.20, 10),
            new UpgradeDefinition("creek_scatter", "Creek Scatter", 0, Stat.SpawnRate, 0.25, 4),
            new UpgradeDefinition("steel_sieve", "Steel Sieve", 1, Stat.IdleSpeed, 0.10, 10),
            new UpgradeDefinition("wide_pan", "Wide Pan", 1, Stat.CollectRadius, 0.08, 5),
            new UpgradeDefinition("water_channel", "Water Channel", 2, Stat.DoubleCatch, 0.01, 10),
            new UpgradeDefinition("dust_value", "Dust Value", 2, Stat.DustValue, 0.15, 10),
            new UpgradeDefinition("night_shift", "Night Shift", 3, Stat.OfflineIncome, 0.10, 10),
            new UpgradeDefinition("rich_sand", "Rich Sand", 3, Stat.NuggetChance, 0.01, 10),
            new UpgradeDefinition("fine_riffle", "Fine Riffle", 4, Stat.NuggetValue, 0.20, 10),
            new UpgradeDefinition("sorting", "Sorting", 4, Stat.DustValue, 0.30, 10),
            new UpgradeDefinition("master_craft", "Master Craft", 5, Stat.AllIncome, 0.05, 10),
            new UpgradeDefinition("vein_detector", "Vein Detector", 5, Stat.MotherLodeReward, 0.10, 10),
            new UpgradeDefinition("steamer_line", "Steamer Line", 6, Stat.AllIncome, 0.10, 10),
            new UpgradeDefinition("groundwork", "Groundwork", 6, Stat.NuggetChance, 0.015, 10),
            new UpgradeDefinition("passive_sluices", "Passive Sluices", 7, Stat.IdleSpeed, 0.15, 10),
            new UpgradeDefinition("big_pan", "Big Pan", 7, Stat.CollectRadius, 0.05, 10),
        };

        /// <summary>
        /// The Nugget collection (design doc 3.1.2): five types per launch creek, three Common,
        /// one Rare, one Legendary, in creek order. Drop weights live in EconomyConfig.
        /// </summary>
        public static readonly IReadOnlyList<NuggetDefinition> Nuggets = new[]
        {
            new NuggetDefinition("pebble", "Pebble", 0, Rarity.Common, "Small, round and still worth the stoop."),
            new NuggetDefinition("button", "Button", 0, Rarity.Common, "Flat as a coat button. Nobody sews it on."),
            new NuggetDefinition("teardrop", "Teardrop", 0, Rarity.Common, "The creek cried gold, just this once."),
            new NuggetDefinition("crooked_thumb", "Crooked Thumb", 0, Rarity.Rare, "Bent like a thumb that has panned too long."),
            new NuggetDefinition("creek_heart", "Creek Heart", 0, Rarity.Legendary, "The creek keeps one of these. Now you do."),

            new NuggetDefinition("pine_cone", "Pine Cone", 1, Rarity.Common, "Scales of gold, no seeds inside."),
            new NuggetDefinition("bark_chip", "Bark Chip", 1, Rarity.Common, "Rough on one side, bright on the other."),
            new NuggetDefinition("acorn", "Acorn", 1, Rarity.Common, "The squirrels would never let it go."),
            new NuggetDefinition("owl_eye", "Owl Eye", 1, Rarity.Rare, "Round, yellow and a little too watchful."),
            new NuggetDefinition("hollow_crown", "Hollow Crown", 1, Rarity.Legendary, "Fit for the king of an empty valley."),

            new NuggetDefinition("tine", "Tine", 2, Rarity.Common, "One prong of a fork nobody ever ate with."),
            new NuggetDefinition("coin_flake", "Coin Flake", 2, Rarity.Common, "Thin enough to spend, if anyone would take it."),
            new NuggetDefinition("river_spoon", "River Spoon", 2, Rarity.Common, "The river scooped it. You scooped the river."),
            new NuggetDefinition("two_tone", "Two-Tone", 2, Rarity.Rare, "Half gold, half silver, all trouble to assay."),
            new NuggetDefinition("wishbone", "Wishbone", 2, Rarity.Legendary, "Snap it and you lose half. Keep your wish."),

            new NuggetDefinition("ember", "Ember", 3, Rarity.Common, "Glows like a coal. Cold to the touch."),
            new NuggetDefinition("rust_knot", "Rust Knot", 3, Rarity.Common, "Red clay tied up in a gold knot."),
            new NuggetDefinition("clay_brick", "Clay Brick", 3, Rarity.Common, "Build nothing with it. Just keep it."),
            new NuggetDefinition("rattler", "Rattler", 3, Rarity.Rare, "Coiled up in the gravel. Pick it up slowly."),
            new NuggetDefinition("sunset_slab", "Sunset Slab", 3, Rarity.Legendary, "The gulch at dusk, poured into one piece."),

            new NuggetDefinition("icicle", "Icicle", 4, Rarity.Common, "Gold that dripped and froze on the way down."),
            new NuggetDefinition("snowball", "Snowball", 4, Rarity.Common, "Heavy enough to win any snowball fight."),
            new NuggetDefinition("frost_flake", "Frost Flake", 4, Rarity.Common, "No two alike, same as the real ones."),
            new NuggetDefinition("polar_tooth", "Polar Tooth", 4, Rarity.Rare, "Sharp, white-tipped and best left unexplained."),
            new NuggetDefinition("glacier_eye", "Glacier Eye", 4, Rarity.Legendary, "Ice held it for ten thousand years."),

            new NuggetDefinition("canyon_shard", "Canyon Shard", 5, Rarity.Common, "A splinter off the canyon wall."),
            new NuggetDefinition("echo_stone", "Echo Stone", 5, Rarity.Common, "Shout into the canyon. This comes back."),
            new NuggetDefinition("lantern", "Lantern", 5, Rarity.Common, "Lights up the pan without a flame."),
            new NuggetDefinition("miners_fist", "Miner's Fist", 5, Rarity.Rare, "Clenched tight, like it knows what it is worth."),
            new NuggetDefinition("deep_king", "Deep King", 5, Rarity.Legendary, "The canyon's oldest piece. It came up for you."),
        };

        /// <summary>Indices into <see cref="Nuggets"/> that drop in a creek; empty past the launch creeks.</summary>
        public static List<int> NuggetsInRegion(int regionIndex)
        {
            var result = new List<int>();
            for (int i = 0; i < Nuggets.Count; i++)
                if (Nuggets[i].RegionIndex == regionIndex)
                    result.Add(i);
            return result;
        }

        /// <summary>The 12 pieces of gear (design doc 6.4): six Common, four Rare, two Legendary.</summary>
        public static readonly IReadOnlyList<GearDefinition> Gear = new[]
        {
            new GearDefinition("leather_gloves", "Leather Gloves", Rarity.Common, Stat.ActiveIncome, 0.05),
            new GearDefinition("tin_cup", "Tin Cup", Rarity.Common, Stat.CritChance, 0.008),
            new GearDefinition("canvas_apron", "Canvas Apron", Rarity.Common, Stat.CollectibleLifetime, 0.1),
            new GearDefinition("pocket_scale", "Pocket Scale", Rarity.Common, Stat.NuggetChance, 0.003),
            new GearDefinition("mule_bell", "Mule Bell", Rarity.Common, Stat.IdleSpeed, 0.04),
            new GearDefinition("lamp_oil", "Lamp Oil", Rarity.Common, Stat.OfflineIncome, 0.05),
            new GearDefinition("hickory_pick", "Hickory Pick", Rarity.Rare, Stat.CritValue, 0.25),
            new GearDefinition("assay_lens", "Assay Lens", Rarity.Rare, Stat.RichNuggetChance, 0.01),
            new GearDefinition("brass_compass", "Brass Compass", Rarity.Rare, Stat.MotherLodeReward, 0.10),
            new GearDefinition("horseshoe", "Horseshoe", Rarity.Rare, Stat.DoubleCatch, 0.005),
            new GearDefinition("prospectors_hat", "Prospector's Hat", Rarity.Legendary, Stat.AllIncome, 0.10),
            new GearDefinition("grandpas_pan", "Grandpa's Pan", Rarity.Legendary, Stat.DustValue, 0.25),
        };

        /// <summary>The 12 Guild perks (design doc 6.6); Perk Points come from Guild levels.</summary>
        public static readonly IReadOnlyList<PerkDefinition> Perks = new[]
        {
            new PerkDefinition("night_watch", "Night Watch", Stat.OfflineCapHours, 1, 4, 2),
            new PerkDefinition("steady_stream", "Steady Stream", Stat.IdleSpeed, 0.15, 10, 1),
            new PerkDefinition("quick_hands", "Quick Hands", Stat.CollectibleLifetime, 0.2, 5, 1),
            new PerkDefinition("deep_vein", "Deep Vein", Stat.VeinMaxLevel, 3, 10, 1),
            new PerkDefinition("sure_grip", "Sure Grip", Stat.VeinCatchesPerLevel, -2, 4, 2),
            new PerkDefinition("nugget_nose", "Nugget Nose", Stat.NuggetChance, 0.005, 10, 1),
            new PerkDefinition("midas_touch", "Midas Touch", Stat.RichNuggetValue, 0.5, 5, 2),
            new PerkDefinition("big_strike", "Big Strike", Stat.GiantNuggetChance, 0.0007, 10, 1),
            new PerkDefinition("assay_bonus", "Assay Bonus", Stat.GiantNuggetValue, 0.08, 10, 2),
            new PerkDefinition("keen_eye", "Keen Eye", Stat.CritValue, 0.2, 5, 1),
            new PerkDefinition("cartographer", "Cartographer", Stat.RegionCost, -0.03, 5, 2),
            new PerkDefinition("old_hand", "Old Hand", Stat.ProspectingXp, 0.10, 10, 1),
        };

        /// <summary>The 15 hireable crew members, in design doc order.</summary>
        public static readonly IReadOnlyList<CrewDefinition> Crew = new[]
        {
            new CrewDefinition("ezra", "Ezra", Stat.IdleSpeed, 0.08),
            new CrewDefinition("ma_perkins", "Ma Perkins", Stat.NuggetChance, 0.01),
            new CrewDefinition("cassidy", "Cassidy", Stat.TierCost, -0.03),
            new CrewDefinition("marta", "Marta", Stat.RegionCost, -0.03),
            new CrewDefinition("elias", "Elias", Stat.ProspectingXp, 0.10),
            new CrewDefinition("doc", "Doc", Stat.ChestValue, 0.30),
            new CrewDefinition("nell", "Nell", Stat.DustValue, 0.15),
            new CrewDefinition("silas", "Silas", Stat.NuggetValue, 0.15),
            new CrewDefinition("rosa", "Rosa", Stat.CollectibleLifetime, 0.15),
            new CrewDefinition("tobias", "Tobias", Stat.MotherLodeReward, 0.20),
            new CrewDefinition("wren", "Wren", Stat.MotherLodeFrequency, -0.03),
            new CrewDefinition("big_ole", "Big Ole", Stat.ActiveIncome, 0.08),
            new CrewDefinition("mei", "Mei", Stat.OfflineIncome, 0.10),
            new CrewDefinition("clementine", "Clementine", Stat.UpgradeCost, -0.03),
            new CrewDefinition("gus", "Gus", Stat.DoubleCatch, 0.005),
        };

        /// <summary>
        /// Progress goals, one active at a time (design doc 9, 12.1 screen 5). The first one is
        /// the onboarding card and counts manual catches only; later ones walk the player
        /// through each creek, tier and the first crew hires.
        /// </summary>
        public static readonly IReadOnlyList<GoalDefinition> Goals = new[]
        {
            new GoalDefinition(GoalKind.ManualCollected, 15),
            new GoalDefinition(GoalKind.UpgradeLevels, 3),
            new GoalDefinition(GoalKind.AmosLevel, 1),
            new GoalDefinition(GoalKind.RegionsUnlocked, 2),
            new GoalDefinition(GoalKind.SluiceTier, 2),
            new GoalDefinition(GoalKind.ManualCollected, 300),
            new GoalDefinition(GoalKind.UpgradeLevels, 12),
            new GoalDefinition(GoalKind.AmosLevel, 3),
            new GoalDefinition(GoalKind.RegionsUnlocked, 3),
            new GoalDefinition(GoalKind.CrewHired, 1),
            new GoalDefinition(GoalKind.SluiceTier, 3),
            new GoalDefinition(GoalKind.UpgradeLevels, 25),
            new GoalDefinition(GoalKind.AmosLevel, 5),
            new GoalDefinition(GoalKind.RegionsUnlocked, 4),
            new GoalDefinition(GoalKind.ManualCollected, 3000),
            new GoalDefinition(GoalKind.CrewHired, 2),
            new GoalDefinition(GoalKind.SluiceTier, 4),
            new GoalDefinition(GoalKind.UpgradeLevels, 40),
            new GoalDefinition(GoalKind.RegionsUnlocked, 5),
            new GoalDefinition(GoalKind.CrewHired, 3),
            new GoalDefinition(GoalKind.SluiceTier, 5),
            new GoalDefinition(GoalKind.RegionsUnlocked, 6),
        };

        /// <summary>Adds owned upgrade levels to a sheet. levels[i] belongs to Upgrades[i].</summary>
        public static void ApplyUpgrades(StatSheet sheet, IReadOnlyList<int> levels)
        {
            for (int i = 0; i < Upgrades.Count; i++)
            {
                UpgradeDefinition upgrade = Upgrades[i];
                int level = System.Math.Min(levels[i], upgrade.MaxLevel);
                if (level > 0)
                    sheet.Add(upgrade.Stat, upgrade.PerLevel * level);
            }
        }

        public static void ApplyGear(StatSheet sheet, GearDefinition gear, int level)
        {
            int clamped = System.Math.Min(level, GearMaxLevel);
            if (clamped > 0)
                sheet.Add(gear.Stat, gear.PerLevel * clamped);
        }

        public static void ApplyCrew(StatSheet sheet, CrewDefinition crew, int level)
        {
            int clamped = System.Math.Min(level, CrewMaxLevel);
            if (clamped > 0)
                sheet.Add(crew.Stat, crew.PerLevel * clamped);
        }
    }
}
