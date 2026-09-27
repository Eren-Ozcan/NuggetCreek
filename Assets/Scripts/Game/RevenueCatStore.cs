using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Google Play Billing through RevenueCat (roadmap 3.5). Prices come from the store in
    /// the player's currency; the catalog's USD cents are only a fallback until they load.
    /// Receipts are validated by RevenueCat; the session still grants each transaction once.
    /// </summary>
    public sealed class RevenueCatStore : IStore
    {
        const string InApp = "inapp";

        readonly Purchases purchases;
        readonly Dictionary<string, Purchases.StoreProduct> products = new Dictionary<string, Purchases.StoreProduct>();
        ShopItem pending;

        public bool IsBusy => pending != null;

        public RevenueCatStore(GameObject host, string apiKey)
        {
            purchases = host.AddComponent<Purchases>();
            // Configured from code, so the key is not kept in the scene.
            purchases.useRuntimeSetup = true;
            purchases.Configure(Purchases.PurchasesConfiguration.Builder.Init(apiKey).Build());

            var ids = new List<string>();
            foreach (ShopItem item in ShopCatalog.Items)
                ids.Add(item.Id);
            purchases.GetProducts(ids.ToArray(), (list, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[Store] products failed: {error.Message}");
                    return;
                }
                foreach (Purchases.StoreProduct product in list)
                    products[product.Identifier] = product;
                Debug.Log($"[Store] {products.Count} products loaded");
            }, InApp);
        }

        public string PriceText(ShopItem item) =>
            products.TryGetValue(item.Id, out Purchases.StoreProduct product) && !string.IsNullOrEmpty(product.PriceString)
                ? product.PriceString
                : "$" + (item.PriceCents / 100.0).ToString("0.00");

        public void Purchase(ShopItem item, Action<StoreResult> onFinished)
        {
            if (IsBusy || item == null)
            {
                onFinished?.Invoke(StoreResult.Failed("busy"));
                return;
            }
            pending = item;
            purchases.PurchaseProduct(item.Id, result =>
            {
                pending = null;
                if (result.UserCancelled)
                {
                    onFinished?.Invoke(StoreResult.Failed("cancelled"));
                    return;
                }
                if (result.Error != null || result.StoreTransaction == null)
                {
                    Debug.LogWarning($"[Store] purchase failed: {result.Error?.Message}");
                    onFinished?.Invoke(StoreResult.Failed(result.Error?.ReadableErrorCode ?? "error"));
                    return;
                }
                products.TryGetValue(item.Id, out Purchases.StoreProduct product);
                onFinished?.Invoke(new StoreResult(true, result.StoreTransaction.TransactionIdentifier,
                    product?.Price ?? 0, product?.CurrencyCode));
            }, InApp);
        }

        public void Restore(Action<IReadOnlyList<string>> onOwned)
        {
            purchases.RestorePurchases((info, error) =>
            {
                if (error != null || info == null)
                {
                    Debug.LogWarning($"[Store] restore failed: {error?.Message}");
                    onOwned?.Invoke(Array.Empty<string>());
                    return;
                }
                onOwned?.Invoke(info.AllPurchasedProductIdentifiers ?? new List<string>());
            });
        }

        public void Tick(float deltaSeconds) { }
    }

    /// <summary>
    /// RevenueCat public SDK keys (safe to ship). Empty until the Nugget Creek project exists
    /// in RevenueCat, which needs the Play Console app; until then every build uses the fake
    /// store.
    /// </summary>
    static class RevenueCatKeys
    {
        public const string Google = "";
    }
}
