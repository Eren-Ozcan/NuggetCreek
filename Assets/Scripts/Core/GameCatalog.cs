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

    /// <summary>
    /// One of the 46 collectible Nugget types (design doc 3.1.2): per creek one Common and one
    /// Legendary boss nugget (dropped only by that creek's Mother Lode boss), plus six Rare types
    /// that drop in every creek (RegionIndex = GameCatalog.GlobalRegion).
    /// </summary>
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

        /// <summary>The 20 launch creeks in unlock order (design doc 6.2). Odd creeks (even indices)
        /// are the points of the old 10-creek curve; one creek sits between each pair.</summary>
        public static readonly IReadOnlyList<string> RegionNames = new[]
        {
            "Nugget Creek", "Willow Bend", "Pine Hollow", "Bear Falls", "Silver Fork",
            "Copper Bluff", "Red Gulch", "Sagebrush Flats", "Frost Basin", "Snowshoe Pass",
            "Deep Canyon", "Echo Gorge", "Quartz Flats", "Shimmer Grotto", "Glacier Run",
            "Aurora Lake", "Flooded Mine", "Blackpowder Ridge", "Bonanza Heights", "Last Chance Lode",
        };

        public static readonly IReadOnlyList<UpgradeDefinition> Upgrades = new[]
        {
            new UpgradeDefinition("sturdy_shovel", "Sturdy Shovel", 0, Stat.DustValue, 0.20, 10),
            new UpgradeDefinition("creek_scatter", "Creek Scatter", 0, Stat.SpawnRate, 0.25, 4),
            new UpgradeDefinition("steel_sieve", "Steel Sieve", 1, Stat.AllIncome, 0.10, 10),
            new UpgradeDefinition("wide_pan", "Wide Pan", 1, Stat.CollectRadius, 0.08, 5),
            new UpgradeDefinition("water_channel", "Water Channel", 2, Stat.DoubleCatch, 0.01, 10),
            new UpgradeDefinition("dust_value", "Dust Value", 2, Stat.DustValue, 0.15, 10),
            new UpgradeDefinition("steamer_line", "Steamer Line", 3, Stat.AllIncome, 0.10, 10),
            new UpgradeDefinition("rich_sand", "Rich Sand", 3, Stat.NuggetChance, 0.01, 10),
            new UpgradeDefinition("fine_riffle", "Fine Riffle", 4, Stat.NuggetValue, 0.20, 10),
            new UpgradeDefinition("sorting", "Sorting", 4, Stat.DustValue, 0.30, 10),
            new UpgradeDefinition("master_craft", "Master Craft", 5, Stat.AllIncome, 0.05, 10),
            new UpgradeDefinition("vein_detector", "Vein Detector", 5, Stat.MotherLodeReward, 0.10, 10),
            new UpgradeDefinition("night_shift", "Night Shift", 6, Stat.AllIncome, 0.10, 10),
            new UpgradeDefinition("groundwork", "Groundwork", 6, Stat.NuggetChance, 0.015, 10),
            new UpgradeDefinition("passive_sluices", "Passive Sluices", 7, Stat.AllIncome, 0.15, 10),
            new UpgradeDefinition("big_pan", "Big Pan", 7, Stat.CollectRadius, 0.05, 10),
            new UpgradeDefinition("pressure_hose", "Pressure Hose", 8, Stat.AllIncome, 0.10, 10),
            new UpgradeDefinition("nugget_trap", "Nugget Trap", 8, Stat.NuggetValue, 0.20, 10),
            new UpgradeDefinition("dredge_line", "Dredge Line", 9, Stat.AllIncome, 0.15, 10),
            new UpgradeDefinition("bucket_line", "Bucket Line", 9, Stat.DustValue, 0.30, 10),
        };

        /// <summary>RegionIndex of the Rare types, which drop in every creek.</summary>
        public const int GlobalRegion = -1;

        /// <summary>
        /// The Nugget collection (design doc 3.1.2), in creek order: each creek's Common then its
        /// boss nugget (indices 2r and 2r + 1), then the six global Rares. Drop weights live in
        /// EconomyConfig.
        /// </summary>
        public static readonly IReadOnlyList<NuggetDefinition> Nuggets = new[]
        {
            new NuggetDefinition("pebble", "Pebble", 0, Rarity.Common, "Small, round and still worth the stoop."),
            new NuggetDefinition("creek_heart", "Creek Heart", 0, Rarity.Legendary, "The creek keeps one of these. Now you do."),
            new NuggetDefinition("button", "Button", 1, Rarity.Common, "Flat as a coat button. Nobody sews it on."),
            new NuggetDefinition("willow_tear", "Willow Tear", 1, Rarity.Legendary, "The willows wept for a hundred years. This is what fell."),
            new NuggetDefinition("pine_cone", "Pine Cone", 2, Rarity.Common, "Scales of gold, no seeds inside."),
            new NuggetDefinition("hollow_crown", "Hollow Crown", 2, Rarity.Legendary, "Fit for the king of an empty valley."),
            new NuggetDefinition("paw_print", "Paw Print", 3, Rarity.Common, "Something big walked here and left this behind."),
            new NuggetDefinition("grizzly_claw", "Grizzly Claw", 3, Rarity.Legendary, "Three claws of gold. Do not ask who lost them."),
            new NuggetDefinition("tine", "Tine", 4, Rarity.Common, "One prong of a fork nobody ever ate with."),
            new NuggetDefinition("wishbone", "Wishbone", 4, Rarity.Legendary, "Snap it and you lose half. Keep your wish."),
            new NuggetDefinition("coin_flake", "Coin Flake", 5, Rarity.Common, "Thin enough to spend, if anyone would take it."),
            new NuggetDefinition("copper_kettle", "Copper Kettle", 5, Rarity.Legendary, "Gold inside, copper bands outside. Still hot."),
            new NuggetDefinition("ember", "Ember", 6, Rarity.Common, "Glows like a coal. Cold to the touch."),
            new NuggetDefinition("sunset_slab", "Sunset Slab", 6, Rarity.Legendary, "The gulch at dusk, poured into one piece."),
            new NuggetDefinition("cactus_pad", "Cactus Pad", 7, Rarity.Common, "Flat, green-gold and blessedly without spines."),
            new NuggetDefinition("tumbleweed_king", "Tumbleweed King", 7, Rarity.Legendary, "It rolled across three counties to find you."),
            new NuggetDefinition("icicle", "Icicle", 8, Rarity.Common, "Gold that dripped and froze on the way down."),
            new NuggetDefinition("glacier_eye", "Glacier Eye", 8, Rarity.Legendary, "Ice held it for ten thousand years."),
            new NuggetDefinition("snowball", "Snowball", 9, Rarity.Common, "Heavy enough to win any snowball fight."),
            new NuggetDefinition("avalanche", "Avalanche", 9, Rarity.Legendary, "A whole mountainside of gold, stopped just in time."),
            new NuggetDefinition("canyon_shard", "Canyon Shard", 10, Rarity.Common, "A splinter off the canyon wall."),
            new NuggetDefinition("deep_king", "Deep King", 10, Rarity.Legendary, "The canyon's oldest piece. It came up for you."),
            new NuggetDefinition("echo_stone", "Echo Stone", 11, Rarity.Common, "Shout into the gorge. This comes back."),
            new NuggetDefinition("thunder_drum", "Thunder Drum", 11, Rarity.Legendary, "Knock on it and the gorge answers."),
            new NuggetDefinition("salt_crystal", "Salt Crystal", 12, Rarity.Common, "Square as a sugar cube. Do not taste it."),
            new NuggetDefinition("quartz_throne", "Quartz Throne", 12, Rarity.Legendary, "A seat for whoever rules the flats."),
            new NuggetDefinition("geode", "Geode", 13, Rarity.Common, "Plain outside. Crack it and look again."),
            new NuggetDefinition("grotto_star", "Grotto Star", 13, Rarity.Legendary, "The only star that shines underground."),
            new NuggetDefinition("frost_flake", "Frost Flake", 14, Rarity.Common, "No two alike, same as the real ones."),
            new NuggetDefinition("ice_mammoth", "Ice Mammoth", 14, Rarity.Legendary, "The glacier gave it back, tusks and all."),
            new NuggetDefinition("moon_drop", "Moon Drop", 15, Rarity.Common, "Fell off the moon into the lake. Probably."),
            new NuggetDefinition("aurora_pearl", "Aurora Pearl", 15, Rarity.Legendary, "The northern lights, rolled up into a pearl."),
            new NuggetDefinition("lantern", "Lantern", 16, Rarity.Common, "Lights up the pan without a flame."),
            new NuggetDefinition("drowned_bell", "Drowned Bell", 16, Rarity.Legendary, "It still rings when the water moves."),
            new NuggetDefinition("fuse_coil", "Fuse Coil", 17, Rarity.Common, "Coiled tight and, thankfully, unlit."),
            new NuggetDefinition("powder_keg", "Powder Keg", 17, Rarity.Legendary, "Handle with care. It is only gold. Probably."),
            new NuggetDefinition("summit_stone", "Summit Stone", 18, Rarity.Common, "A little mountain you can keep in a pocket."),
            new NuggetDefinition("eagles_nest", "Eagle's Nest", 18, Rarity.Legendary, "The eagle traded it for a view. Fair enough."),
            new NuggetDefinition("last_coin", "Last Coin", 19, Rarity.Common, "Worn smooth by every hand it passed through."),
            new NuggetDefinition("the_old_claim", "The Old Claim", 19, Rarity.Legendary, "The first claim on the creek. Now it is yours."),

            new NuggetDefinition("crooked_thumb", "Crooked Thumb", GlobalRegion, Rarity.Rare, "Bent like a thumb that has panned too long."),
            new NuggetDefinition("owl_eye", "Owl Eye", GlobalRegion, Rarity.Rare, "Round, yellow and a little too watchful."),
            new NuggetDefinition("two_tone", "Two-Tone", GlobalRegion, Rarity.Rare, "Half gold, half silver, all trouble to assay."),
            new NuggetDefinition("rattler", "Rattler", GlobalRegion, Rarity.Rare, "Coiled up in the gravel. Pick it up slowly."),
            new NuggetDefinition("polar_tooth", "Polar Tooth", GlobalRegion, Rarity.Rare, "Sharp, white-tipped and best left unexplained."),
            new NuggetDefinition("miners_fist", "Miner's Fist", GlobalRegion, Rarity.Rare, "Clenched tight, like it knows what it is worth."),
        };

        /// <summary>Index into <see cref="Nuggets"/> of a creek's Common type.</summary>
        public static int CommonNugget(int regionIndex) => 2 * regionIndex;

        /// <summary>Index into <see cref="Nuggets"/> of a creek's boss nugget.</summary>
        public static int BossNugget(int regionIndex) => 2 * regionIndex + 1;

        public static bool IsBossNugget(int index) => index >= 0 && index < Nuggets.Count && Nuggets[index].Rarity == Rarity.Legendary;

        /// <summary>First index of the six global Rares; they run to the end of the list.</summary>
        public const int FirstRareNugget = 40;

        public static int RareNuggetCount => Nuggets.Count - FirstRareNugget;

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
            new GearDefinition("brass_compass", "Brass Compass", Rarity.Rare, Stat.MotherLodeDamage, 0.10),
            new GearDefinition("horseshoe", "Horseshoe", Rarity.Rare, Stat.DoubleCatch, 0.005),
            new GearDefinition("prospectors_hat", "Prospector's Hat", Rarity.Legendary, Stat.AllIncome, 0.10),
            new GearDefinition("grandpas_pan", "Grandpa's Pan", Rarity.Legendary, Stat.DustValue, 0.25),
        };

        /// <summary>The 12 Guild perks (design doc 6.6); Perk Points come from Guild levels.</summary>
        public static readonly IReadOnlyList<PerkDefinition> Perks = new[]
        {
            new PerkDefinition("night_watch", "Night Watch", Stat.OfflineCapHours, 0.5, 4, 2),
            new PerkDefinition("steady_stream", "Steady Stream", Stat.AllIncome, 0.15, 10, 1),
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
            new CrewDefinition("tobias", "Tobias", Stat.MotherLodeDamage, 0.15),
            new CrewDefinition("wren", "Wren", Stat.MotherLodeFrequency, -0.03),
            new CrewDefinition("big_ole", "Big Ole", Stat.ActiveIncome, 0.08),
            new CrewDefinition("mei", "Mei", Stat.OfflineIncome, 0.10),
            new CrewDefinition("clementine", "Clementine", Stat.UpgradeCost, -0.03),
            new CrewDefinition("gus", "Gus", Stat.DoubleCatch, 0.005),
        };

        /// <summary>
        /// Progress goals, one active at a time (design doc 9, 12.1 screen 5). The first one is
        /// the onboarding card and counts manual catches only; later ones walk the player
        /// through each creek, tier and the first crew hires. Creek goals sit on the odd creeks,
        /// the points of the old 10-creek curve, so their timing did not move with 20 creeks.
        /// </summary>
        public static readonly IReadOnlyList<GoalDefinition> Goals = new[]
        {
            new GoalDefinition(GoalKind.ManualCollected, 15),
            new GoalDefinition(GoalKind.UpgradeLevels, 3),
            new GoalDefinition(GoalKind.AmosLevel, 1),
            new GoalDefinition(GoalKind.RegionsUnlocked, 3),
            new GoalDefinition(GoalKind.SluiceTier, 2),
            new GoalDefinition(GoalKind.ManualCollected, 300),
            new GoalDefinition(GoalKind.UpgradeLevels, 12),
            new GoalDefinition(GoalKind.AmosLevel, 3),
            new GoalDefinition(GoalKind.RegionsUnlocked, 5),
            new GoalDefinition(GoalKind.CrewHired, 1),
            new GoalDefinition(GoalKind.SluiceTier, 3),
            new GoalDefinition(GoalKind.UpgradeLevels, 25),
            new GoalDefinition(GoalKind.AmosLevel, 5),
            new GoalDefinition(GoalKind.RegionsUnlocked, 7),
            new GoalDefinition(GoalKind.ManualCollected, 3000),
            new GoalDefinition(GoalKind.CrewHired, 2),
            new GoalDefinition(GoalKind.SluiceTier, 4),
            new GoalDefinition(GoalKind.UpgradeLevels, 40),
            new GoalDefinition(GoalKind.RegionsUnlocked, 9),
            new GoalDefinition(GoalKind.CrewHired, 3),
            new GoalDefinition(GoalKind.SluiceTier, 5),
            new GoalDefinition(GoalKind.RegionsUnlocked, 11),
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
