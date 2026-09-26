using System;
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

        public StoreResult(bool paid, string transactionId)
        {
            Paid = paid;
            TransactionId = transactionId;
        }
    }

    /// <summary>What the game needs from a billing SDK. The real Play Billing adapter comes in phase 3.5.</summary>
    public interface IStore
    {
        /// <summary>A purchase sheet is open or processing.</summary>
        bool IsBusy { get; }

        /// <summary>Price text the store shows; the fake store shows USD.</summary>
        string PriceText(ShopItem item);

        void Purchase(ShopItem item, Action<StoreResult> onFinished);

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
            RectTransform card = Ui.Image("Card", sheet, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(900, 620));
            Text title = Ui.Label("Title", card, "Test purchase", 48, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
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
                onFinished?.Invoke(new StoreResult(false, null));
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

        public void Tick(float deltaSeconds)
        {
            if (processingLeft < 0)
                return;
            processingLeft -= deltaSeconds;
            if (processingLeft < 0)
                Finish(new StoreResult(true, "fake-" + Guid.NewGuid().ToString("N")));
        }

        void Confirm()
        {
            buyLabel.SetText("Processing...");
            buy.interactable = false;
            cancel.interactable = false;
            processingLeft = ProcessingSeconds;
        }

        void Cancel() => Finish(new StoreResult(false, null));

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
