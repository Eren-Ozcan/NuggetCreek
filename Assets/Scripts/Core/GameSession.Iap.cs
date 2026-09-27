using System;
using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>
    /// Store analytics and restores (design doc 8.2, 8.3, 13.4.3): the purchase funnel events,
    /// the payer tier, and non-consumables coming back after a reinstall.
    /// </summary>
    public sealed partial class GameSession
    {
        readonly HashSet<string> offersShown = new HashSet<string>();

        /// <summary>none, first, repeat or whale (13.4.3 user property).</summary>
        public string PayerTier =>
            Progress.Purchases == 0 ? "none"
            : Progress.SpentCents >= Config.WhaleCents ? "whale"
            : Progress.Purchases == 1 ? "first"
            : "repeat";

        /// <summary>The store sheet opened for this product.</summary>
        public void StartPurchase(ShopItem item) => Emit("iap_start", ("sku", item.Id));

        /// <summary>The store sheet closed without a payment ("cancelled", "error", "pending").</summary>
        public void FailPurchase(ShopItem item, string reason) => Emit("iap_fail", ("sku", item.Id), ("reason", reason ?? "error"));

        /// <summary>Sends offer_shown the first time an offer is seen in this session.</summary>
        public void ReportOfferShown(ShopItem item, string trigger)
        {
            if (item == null || !item.IsOffer || !offersShown.Add(item.Id))
                return;
            Emit("offer_shown", ("offer_id", item.Id), ("trigger", trigger));
        }

        /// <summary>
        /// Brings back what the store says the player owns for good: Remove Ads and the
        /// premium gear slot. Consumables are never restored.
        /// </summary>
        /// <returns>True when anything changed.</returns>
        public bool RestoreOwned(IEnumerable<string> ownedIds)
        {
            bool changed = false;
            foreach (string id in ownedIds ?? Array.Empty<string>())
            {
                ShopItem item = ShopCatalog.Find(id);
                if (item == null)
                    continue;
                if (item.Kind == ShopKind.RemoveAds && !Progress.AdsRemoved)
                {
                    Progress.AdsRemoved = true;
                    Progress.AdsRemovedNoticePending = true;
                    Emit("ads_removed", ("path", "restore"));
                    changed = true;
                }
                else if (item.Kind == ShopKind.GearSlot && !Progress.FourthGearSlotOwned)
                {
                    Progress.FourthGearSlotOwned = true;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>iap_purchase and, when this purchase turned ads off, ads_removed.</summary>
        void ReportPurchase(ShopItem item, bool first, double priceLocal, string currency, bool adsRemovedNow)
        {
            Emit("iap_purchase", ("sku", item.Id), ("price_usd", item.PriceCents / 100.0),
                ("price_local", priceLocal > 0 ? priceLocal : item.PriceCents / 100.0),
                ("currency", string.IsNullOrEmpty(currency) ? "USD" : currency),
                ("first", first), ("cum_usd", Progress.SpentCents / 100.0));
            if (adsRemovedNow)
                Emit("ads_removed", ("path", item.Kind == ShopKind.RemoveAds ? "sku" : "threshold"));
        }
    }
}
