using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>A Gem boost from the GEM SHOP tab (design doc 8.2c).</summary>
    public enum ShopBoost
    {
        GoldWash,
        ExtraShift,
        RichVein,
    }

    /// <summary>What a store purchase granted, for the reward card.</summary>
    public sealed class PurchaseResult
    {
        public ShopItem Item;
        public int Gems;
        public readonly List<int> Crew = new List<int>();
        public readonly List<GearCard> Cards = new List<GearCard>();
        public bool Boost;
        public bool GearSlot;
        /// <summary>This purchase turned forced ads off.</summary>
        public bool AdsRemoved;
    }

    /// <summary>Shop rules (design doc 8.2e): offer windows, purchase grants, the ad threshold and Gem boosts.</summary>
    public sealed partial class GameSession
    {
        readonly double[] boostCooldowns = new double[3];

        /// <summary>Device time zone offset, set by the platform with <see cref="NowUtc"/>; places the weekend's end.</summary>
        public double UtcOffsetSeconds { get; set; }

        // --- Offer windows ---

        /// <summary>Weekday of a game day, 0 = Sunday (day 0 is Thursday 1 January 1970).</summary>
        public static int Weekday(long day) => (int)(((day + 4) % 7 + 7) % 7);

        /// <summary>Friday game day of the weekend a day belongs to, or -1 on a weekday.</summary>
        public static long WeekendOf(long day)
        {
            switch (Weekday(day))
            {
                case 5: return day;
                case 6: return day - 1;
                case 0: return day - 2;
                default: return -1;
            }
        }

        /// <summary>Starts the offer clocks that wait for trusted time; call when a day starts.</summary>
        void StartDayOffers()
        {
            if (!NowUtc.HasValue)
                return;
            if (Progress.StarterEndUtc <= 0)
                Progress.StarterEndUtc = NowUtc.Value + Config.StarterOfferHours * 3600;
            Progress.DailyOfferEndUtc = NowUtc.Value + Config.DailyOfferHours * 3600;
        }

        /// <summary>First arrival at every creek puts New Creek Welcome on sale (user decision, v0.19);
        /// replays after a rebirth do not.</summary>
        void OfferWelcome(int regionIndex)
        {
            if (!NowUtc.HasValue || regionIndex < 1)
                return;
            Progress.WelcomeOffer = regionIndex >= Config.WelcomeLargeFromRegion ? 2 : 1;
            Progress.WelcomeEndUtc = NowUtc.Value + Config.WelcomeOfferHours * 3600;
        }

        bool CrewLeftToHire => CrewHiredCount < GameCatalog.Crew.Count;

        /// <summary>Seconds a timed offer stays on sale; 0 when it is not on sale, infinity when it has no clock.</summary>
        public double OfferSecondsLeft(ShopItem item)
        {
            if (item.Crew > 0 && !CrewLeftToHire)
                return 0;
            double now = NowUtc ?? 0;
            switch (item.Kind)
            {
                case ShopKind.Gems:
                    return double.PositiveInfinity;
                case ShopKind.Starter:
                    return NowUtc.HasValue && !Progress.StarterBought ? Math.Max(0, Progress.StarterEndUtc - now) : 0;
                case ShopKind.WelcomeSmall:
                case ShopKind.WelcomeLarge:
                    int band = item.Kind == ShopKind.WelcomeSmall ? 1 : 2;
                    return NowUtc.HasValue && Progress.WelcomeOffer == band ? Math.Max(0, Progress.WelcomeEndUtc - now) : 0;
                case ShopKind.Daily:
                    return NowUtc.HasValue && DayKnown && Progress.DailyOfferBoughtDay != Progress.CurrentDay
                        ? Math.Max(0, Progress.DailyOfferEndUtc - now) : 0;
                case ShopKind.WeekendSmall:
                case ShopKind.WeekendMedium:
                case ShopKind.WeekendLarge:
                    return WeekendSecondsLeft(WeekendBit(item.Kind));
                case ShopKind.GearSlot:
                    return GearUnlocked && !Progress.FourthGearSlotOwned ? double.PositiveInfinity : 0;
                case ShopKind.RemoveAds:
                    return Progress.AdsRemoved ? 0 : double.PositiveInfinity;
                default:
                    return 0;
            }
        }

        public bool IsOnSale(ShopItem item) => OfferSecondsLeft(item) > 0;

        /// <summary>Offers on sale now, in catalog order (the OFFERS tab).</summary>
        public List<ShopItem> ActiveOffers()
        {
            var offers = new List<ShopItem>();
            foreach (ShopItem item in ShopCatalog.Items)
                if (item.IsOffer && IsOnSale(item))
                    offers.Add(item);
            return offers;
        }

        static int WeekendBit(ShopKind kind) => 1 << (kind - ShopKind.WeekendSmall);

        double WeekendSecondsLeft(int bit)
        {
            if (!NowUtc.HasValue || !DayKnown)
                return 0;
            long friday = WeekendOf(Progress.CurrentDay);
            if (friday < 0)
                return 0;
            if (Progress.WeekendBoughtId == friday && (Progress.WeekendBoughtMask & bit) != 0)
                return 0;
            double mondayStart = (friday + 3) * 86400.0 - UtcOffsetSeconds + Config.DailyRolloverHour * 3600;
            return Math.Max(0, mondayStart - NowUtc.Value);
        }

        // --- Remove ads (8.3) ---

        /// <summary>Cents of any purchase still needed before forced ads turn off; 0 once they are off.</summary>
        public long CentsToRemoveAds =>
            Progress.AdsRemoved ? 0 : Math.Max(0, Config.RemoveAdsThresholdCents - Progress.SpentCents);

        /// <summary>True when buying this item turns forced ads off (the "Removes ads" badge).</summary>
        public bool RemovesAds(ShopItem item) =>
            !Progress.AdsRemoved && (item.Kind == ShopKind.RemoveAds || item.PriceCents >= CentsToRemoveAds);

        // --- Purchases ---

        /// <summary>
        /// Grants a paid purchase once per transaction id. A paid product is always granted, even
        /// if its offer ended while the store was processing; null means a repeated transaction.
        /// </summary>
        /// <param name="priceLocal">What the store charged, in <paramref name="currency"/>; 0 when unknown.</param>
        public PurchaseResult GrantPurchase(ShopItem item, string transactionId, double priceLocal = 0, string currency = null)
        {
            if (item == null || string.IsNullOrEmpty(transactionId) || Array.IndexOf(Progress.Transactions, transactionId) >= 0)
                return null;
            RememberTransaction(transactionId);

            var result = new PurchaseResult { Item = item, Gems = item.Gems };
            EarnGems(item.Gems, "iap");
            for (int i = 0; i < item.Crew; i++)
            {
                int member = HireRandomCrew();
                if (member >= 0)
                    result.Crew.Add(member);
            }
            for (int i = 0; i < item.GearCards; i++)
            {
                Rarity rarity = RollRarity(Config.ChestCardOdds, 0, random.NextDouble());
                result.Cards.Add(GrantGearCard(PickGear(rarity, random.NextDouble())));
            }
            if (item.BoostMultiplier > 1 && item.BoostHours > 0)
            {
                ApplyBoost(item.BoostMultiplier, item.BoostHours * 3600);
                result.Boost = true;
            }
            MarkBought(item);
            if (item.Kind == ShopKind.GearSlot)
            {
                result.GearSlot = !Progress.FourthGearSlotOwned;
                Progress.FourthGearSlotOwned = true;
            }

            bool adsWereOn = !Progress.AdsRemoved;
            bool first = Progress.Purchases == 0;
            Progress.SpentCents += item.PriceCents;
            Progress.Purchases++;
            if (adsWereOn && (item.Kind == ShopKind.RemoveAds || Progress.SpentCents >= Config.RemoveAdsThresholdCents))
            {
                Progress.AdsRemoved = true;
                Progress.AdsRemovedNoticePending = true;
                result.AdsRemoved = true;
            }
            ReportPurchase(item, first, priceLocal, currency, result.AdsRemoved);
            return result;
        }

        void RememberTransaction(string transactionId)
        {
            var ids = new List<string>(Progress.Transactions) { transactionId };
            int extra = ids.Count - Config.TransactionMemory;
            if (extra > 0)
                ids.RemoveRange(0, extra);
            Progress.Transactions = ids.ToArray();
        }

        void MarkBought(ShopItem item)
        {
            switch (item.Kind)
            {
                case ShopKind.Starter:
                    Progress.StarterBought = true;
                    break;
                case ShopKind.WelcomeSmall:
                case ShopKind.WelcomeLarge:
                    Progress.WelcomeOffer = 0;
                    break;
                case ShopKind.Daily:
                    Progress.DailyOfferBoughtDay = Progress.CurrentDay;
                    break;
                case ShopKind.WeekendSmall:
                case ShopKind.WeekendMedium:
                case ShopKind.WeekendLarge:
                    long friday = DayKnown ? WeekendOf(Progress.CurrentDay) : -1;
                    if (Progress.WeekendBoughtId != friday)
                    {
                        Progress.WeekendBoughtId = friday;
                        Progress.WeekendBoughtMask = 0;
                    }
                    Progress.WeekendBoughtMask |= WeekendBit(item.Kind);
                    break;
            }
        }

        /// <summary>A random member not hired yet joins at level 1 (a pack's crew x1); -1 when everyone is hired.</summary>
        int HireRandomCrew()
        {
            var pool = new List<int>();
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                if (Progress.CrewLevels[i] == 0)
                    pool.Add(i);
            if (pool.Count == 0)
                return -1;
            int member = pool[random.Next(pool.Count)];
            Progress.CrewLevels[member] = 1;
            if (Array.IndexOf(Progress.CrewCandidates, member) >= 0)
                CloseCandidates();
            RebuildStats();
            return member;
        }

        /// <summary>Call once after the notice card is shown.</summary>
        public void AcknowledgeAdsRemoved() => Progress.AdsRemovedNoticePending = false;

        // --- Gem boosts (8.2c) ---

        public int BoostGems => Config.ShopBoostGems;

        /// <summary>OUT OF STOCK seconds left after a purchase.</summary>
        public double BoostCooldown(ShopBoost boost) => boostCooldowns[(int)boost];

        public bool BoostLocked(ShopBoost boost) => boost == ShopBoost.ExtraShift && !IdleActive;

        public bool CanBuyBoost(ShopBoost boost) =>
            !BoostLocked(boost) && BoostCooldown(boost) <= 0 && CanAffordGems(Config.ShopBoostGems);

        /// <summary>Dollars Extra Shift would pay right now.</summary>
        public BigNumber ExtraShiftPayout => IdleRate * Config.ExtraShiftSeconds;

        public bool BuyBoost(ShopBoost boost)
        {
            if (!CanBuyBoost(boost) || !TrySpendGems(Config.ShopBoostGems, "boost_" + EventValues.Snake(boost.ToString())))
                return false;
            switch (boost)
            {
                case ShopBoost.GoldWash:
                    Progress.GoldWashSecondsLeft += Config.GoldWashSeconds;
                    break;
                case ShopBoost.ExtraShift:
                    Earn(ExtraShiftPayout, IncomeSource.Idle);
                    break;
                case ShopBoost.RichVein:
                    Progress.PendingGiantNuggets++;
                    break;
            }
            boostCooldowns[(int)boost] = Config.ShopBoostCooldownSeconds;
            return true;
        }

        /// <summary>
        /// Takes one Giant Nugget bought with Rich Vein; the creek spawns it when the player can
        /// see it (no menu open), so it is never lost behind the shop.
        /// </summary>
        public bool TakeBoughtGiant()
        {
            if (Progress.PendingGiantNuggets <= 0)
                return false;
            Progress.PendingGiantNuggets--;
            return true;
        }

        public double GoldWashMultiplier => Progress.GoldWashSecondsLeft > 0 ? Config.GoldWashMultiplier : 1;

        /// <summary>Runs shop clocks with the game open, menus included: boost cooldowns and the Gold Wash.</summary>
        public void TickShop(double deltaSeconds)
        {
            if (deltaSeconds <= 0)
                return;
            for (int i = 0; i < boostCooldowns.Length; i++)
                boostCooldowns[i] = Math.Max(0, boostCooldowns[i] - deltaSeconds);
            Progress.GoldWashSecondsLeft = Math.Max(0, Progress.GoldWashSecondsLeft - deltaSeconds);
        }
    }
}
