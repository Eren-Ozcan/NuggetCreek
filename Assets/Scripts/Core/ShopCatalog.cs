using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>What a shop product is, which decides when it is on sale.</summary>
    public enum ShopKind
    {
        Gems,
        Starter,
        WelcomeSmall,
        WelcomeLarge,
        Daily,
        WeekendSmall,
        WeekendMedium,
        WeekendLarge,
        GearSlot,
        RemoveAds,
    }

    /// <summary>A real-money product (design doc 8.2a, 8.2b, 8.2e). Prices are USD cents until the store gives local prices.</summary>
    public sealed class ShopItem
    {
        public readonly string Id;
        public readonly string Name;
        public readonly ShopKind Kind;
        public readonly int PriceCents;
        public readonly int Gems;
        /// <summary>Random crew members not hired yet who join at level 1.</summary>
        public readonly int Crew;
        /// <summary>Gear cards drawn with creek chest odds.</summary>
        public readonly int GearCards;
        public readonly double BoostMultiplier;
        public readonly double BoostHours;

        public ShopItem(string id, string name, ShopKind kind, int priceCents, int gems,
            int crew = 0, int gearCards = 0, double boostMultiplier = 1, double boostHours = 0)
        {
            Id = id;
            Name = name;
            Kind = kind;
            PriceCents = priceCents;
            Gems = gems;
            Crew = crew;
            GearCards = gearCards;
            BoostMultiplier = boostMultiplier;
            BoostHours = boostHours;
        }

        public bool IsOffer => Kind != ShopKind.Gems;
    }

    /// <summary>Every product the greybox shop sells. Ids become store product ids in phase 3.</summary>
    public static class ShopCatalog
    {
        public static readonly IReadOnlyList<ShopItem> Items = new[]
        {
            new ShopItem("nc.gems.handful", "Handful of Gems", ShopKind.Gems, 99, 40),
            new ShopItem("nc.gems.pouch", "Pouch of Gems", ShopKind.Gems, 499, 300),
            new ShopItem("nc.gems.chest", "Chest of Gems", ShopKind.Gems, 999, 800),
            new ShopItem("nc.gems.wagon", "Wagon of Gems", ShopKind.Gems, 2499, 3000),
            new ShopItem("nc.gems.mountain", "Mountain of Gems", ShopKind.Gems, 4999, 8000),
            new ShopItem("nc.gems.vein", "Vein of Gems", ShopKind.Gems, 9999, 20000),

            new ShopItem("nc.offer.starter", "Starter Pack", ShopKind.Starter, 499, 300, crew: 1),
            new ShopItem("nc.offer.welcome_small", "New Creek Welcome", ShopKind.WelcomeSmall, 199, 100, crew: 1),
            new ShopItem("nc.offer.welcome_large", "New Creek Welcome", ShopKind.WelcomeLarge, 499, 300, crew: 1),
            new ShopItem("nc.offer.daily", "Daily Offer", ShopKind.Daily, 999, 1000, gearCards: 1),
            new ShopItem("nc.offer.weekend_small", "Weekend Pack", ShopKind.WeekendSmall, 99, 150, boostMultiplier: 5, boostHours: 1),
            new ShopItem("nc.offer.weekend_medium", "Weekend Pack+", ShopKind.WeekendMedium, 299, 500, gearCards: 1),
            new ShopItem("nc.offer.weekend_large", "Weekend Pack Max", ShopKind.WeekendLarge, 1099, 2500, crew: 1, gearCards: 1),
            new ShopItem("nc.offer.gear_slot", "Premium Gear Slot", ShopKind.GearSlot, 499, 0),
            new ShopItem("nc.offer.remove_ads", "Remove Ads", ShopKind.RemoveAds, 599, 0),
        };

        public static ShopItem Find(string id)
        {
            foreach (ShopItem item in Items)
                if (item.Id == id)
                    return item;
            return null;
        }

        public static ShopItem Of(ShopKind kind)
        {
            foreach (ShopItem item in Items)
                if (item.Kind == kind)
                    return item;
            return null;
        }
    }
}
