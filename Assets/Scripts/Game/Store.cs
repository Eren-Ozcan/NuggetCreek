using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using NuggetCreek.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game
{
    /// <summary>How a store purchase ended.</summary>
    public readonly struct StoreResult
    {
        public readonly bool Paid;
        /// <summary>Store transaction id; the session grants each one once.</summary>
        public readonly string TransactionId;
        /// <summary>What the store charged, in <see cref="Currency"/>; 0 when unknown.</summary>
        public readonly double PriceLocal;
        public readonly string Currency;
        /// <summary>Why nothing was paid: "cancelled", "busy", "pending" or a store error code.</summary>
        public readonly string Reason;

        public StoreResult(bool paid, string transactionId, double priceLocal = 0, string currency = null, string reason = null)
        {
            Paid = paid;
            TransactionId = transactionId;
            PriceLocal = priceLocal;
            Currency = currency;
            Reason = reason;
        }

        public static StoreResult Failed(string reason) => new StoreResult(false, null, reason: reason);
    }

    /// <summary>What the game needs from a billing SDK (<see cref="RevenueCatStore"/> on a device).</summary>
    public interface IStore
    {
        /// <summary>A purchase sheet is open or processing.</summary>
        bool IsBusy { get; }

        /// <summary>Price text the store shows; the fake store shows USD.</summary>
        string PriceText(ShopItem item);

        void Purchase(ShopItem item, Action<StoreResult> onFinished);

        /// <summary>Asks the store what the player owns; reports product ids.</summary>
        void Restore(Action<IReadOnlyList<string>> onOwned);

        void Tick(float deltaSeconds);
    }

    /// <summary>
    /// Greybox stand-in for the platform purchase sheet (design doc 8.2e): a "Test purchase"
    /// card with Buy and Cancel, then a short processing wait. Nothing is charged.
    /// </summary>
    public sealed class FakeStore : IStore
    {
        const float ProcessingSeconds = 1f;

        readonly RectTransform sheet;
        readonly Text details;
        readonly Button buy;
        readonly Button cancel;
        readonly Text buyLabel;

        ShopItem pending;
        Action<StoreResult> callback;
        float processingLeft = -1;

        public bool IsBusy => pending != null;

        public FakeStore(Transform canvas)
        {
            sheet = Ui.Image("StoreSheet", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Panel("Card", sheet, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(900, 620));
            Text title = Ui.Title("Title", card, "Test purchase", 48);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -110), new Vector2(0, -20));
            details = Ui.Label("Details", card, "", 36, TextAnchor.UpperCenter, Palette.Text);
            details.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(40, 180), new Vector2(-40, -130));
            cancel = Ui.Button("Cancel", card, "Cancel", Palette.ButtonAlt, Cancel, out _, 40);
            cancel.AsRect().Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-15, 150));
            buy = Ui.Button("Buy", card, "Buy", Palette.Button, Confirm, out buyLabel, 40);
            buy.AsRect().Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(15, 40), new Vector2(-40, 150));
            sheet.SetActive(false);
        }

        public string PriceText(ShopItem item) => "$" + (item.PriceCents / 100.0).ToString("0.00");

        public void Purchase(ShopItem item, Action<StoreResult> onFinished)
        {
            if (IsBusy || item == null)
            {
                onFinished?.Invoke(StoreResult.Failed("busy"));
                return;
            }
            pending = item;
            callback = onFinished;
            details.SetText($"{item.Name}\n{PriceText(item)}\n\nNothing is charged in this build.");
            buyLabel.SetText("Buy");
            buy.interactable = true;
            cancel.interactable = true;
            sheet.SetAsLastSibling();
            sheet.SetActive(true);
        }

        /// <summary>The fake store keeps no receipts; nothing comes back.</summary>
        public void Restore(Action<IReadOnlyList<string>> onOwned) => onOwned?.Invoke(Array.Empty<string>());

        public void Tick(float deltaSeconds)
        {
            if (processingLeft < 0)
                return;
            processingLeft -= deltaSeconds;
            if (processingLeft < 0)
                Finish(new StoreResult(true, "fake-" + Guid.NewGuid().ToString("N"), pending.PriceCents / 100.0, "USD"));
        }

        void Confirm()
        {
            buyLabel.SetText("Processing...");
            buy.interactable = false;
            cancel.interactable = false;
            processingLeft = ProcessingSeconds;
        }

        void Cancel() => Finish(StoreResult.Failed("cancelled"));

        void Finish(StoreResult result)
        {
            processingLeft = -1;
            sheet.SetActive(false);
            Action<StoreResult> done = callback;
            pending = null;
            callback = null;
            done?.Invoke(result);
        }
    }
}
