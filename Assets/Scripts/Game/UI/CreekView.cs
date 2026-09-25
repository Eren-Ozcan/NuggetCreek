using System.Collections.Generic;
using System.Globalization;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The creek: spawns glinting Gold Dust and Nuggets (plain, Rich, Giant) and collects
    /// them when a swipe passes over them (design doc 3.1, 3.1.1). Rolls critical catches,
    /// shows the vein level once it is open, and Amos panning while idle is active.
    /// </summary>
    public sealed class CreekView : MonoBehaviour
    {
        const float DustSize = 60;
        const float NuggetSize = 100;
        const float RichNuggetSize = 115;
        const float GiantNuggetSize = 180;
        const float FadeSeconds = 0.6f;
        const float PopupSeconds = 0.9f;
        const float EdgeMargin = 90;
        const float AmosPopupEvery = 2f;
        const int HintUntilCollected = 3;

        sealed class Collectible
        {
            public Image Image;
            public CollectibleKind Kind;
            public float Age;
            public float Lifetime;
        }

        sealed class Popup
        {
            public Text Label;
            public float Age;
        }

        GameSession session;
        RectTransform area;
        RectTransform amos;
        Text hint;
        Text vein;
        readonly List<Collectible> live = new List<Collectible>();
        readonly List<Popup> popups = new List<Popup>();
        float spawnIn;
        Vector2? lastPointer;
        float amosTimer;
        BigNumber amosPending = BigNumber.Zero;

        /// <summary>False while a modal covers the creek.</summary>
        public bool InputEnabled { get; set; } = true;

        public void Init(GameSession gameSession)
        {
            session = gameSession;
            area = (RectTransform)transform;

            var water = gameObject.AddComponent<Image>();
            water.color = Palette.Water;
            water.raycastTarget = false;

            hint = Ui.Label("Hint", area, "Swipe over the glinting gold!", 44, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            hint.rectTransform.Place(new Vector2(0, 0.45f), new Vector2(1, 0.55f));

            vein = Ui.Label("Vein", area, "", 34, TextAnchor.MiddleLeft, Palette.Gold, FontStyle.Bold);
            vein.rectTransform.Box(new Vector2(0, 1), new Vector2(520, 60), new Vector2(30, -30));

            Image amosBody = Ui.Image("AmosMarker", area, Palette.Amos, Ui.Circle);
            amos = amosBody.rectTransform.Box(new Vector2(0, 0), new Vector2(130, 130), new Vector2(30, 30));
            Text amosName = Ui.Label("Name", amos, "AMOS", 30, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            amosName.rectTransform.Fill();

            ScheduleNextSpawn();
        }

        public void Tick(float deltaTime, BigNumber idleEarned)
        {
            if (session == null)
                return;

            hint.SetActive(session.Progress.ManualCollected < HintUntilCollected);
            UpdateAmos(deltaTime, idleEarned);
            UpdateVein();

            spawnIn -= deltaTime;
            if (spawnIn <= 0)
            {
                Spawn();
                ScheduleNextSpawn();
            }

            AgeCollectibles(deltaTime);
            HandleSwipe();
            AgePopups(deltaTime);
        }

        void ScheduleNextSpawn()
        {
            double rate = session.SpawnRate;
            // Jitter keeps the rhythm organic while the mean stays 1 / rate.
            spawnIn = (float)(1 / rate) * Random.Range(0.6f, 1.4f);
        }

        void Spawn()
        {
            CollectibleKind kind = session.RollKind(Random.value);
            Image image = Ui.Image(kind.ToString(), area, ColorOf(kind), Ui.Circle);
            image.raycastTarget = false;
            float size = SizeOf(kind);
            Rect bounds = area.rect;
            var position = new Vector2(
                Random.Range(EdgeMargin, bounds.width - EdgeMargin),
                Random.Range(EdgeMargin + 160, bounds.height - EdgeMargin));
            image.rectTransform.Box(Vector2.zero, new Vector2(size, size), position);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = position;

            live.Add(new Collectible
            {
                Image = image,
                Kind = kind,
                Lifetime = (float)session.CollectibleLifetimeSeconds,
            });
        }

        void AgeCollectibles(float deltaTime)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Collectible c = live[i];
                c.Age += deltaTime;
                if (c.Age >= c.Lifetime)
                {
                    if (session.LoseCollectible() && session.VeinOpen)
                        ShowPopup(c.Image.rectTransform.anchoredPosition, "Vein lost", 34, Palette.TextMuted);
                    Destroy(c.Image.gameObject);
                    live.RemoveAt(i);
                    continue;
                }

                float popIn = Mathf.Clamp01(c.Age / 0.15f);
                float glint = 1 + 0.08f * Mathf.Sin(c.Age * 10);
                c.Image.rectTransform.localScale = Vector3.one * (popIn * glint);
                Color color = c.Image.color;
                color.a = Mathf.Clamp01((c.Lifetime - c.Age) / FadeSeconds);
                c.Image.color = color;
            }
        }

        void HandleSwipe()
        {
            if (!InputEnabled || !Input.GetMouseButton(0))
            {
                lastPointer = null;
                return;
            }

            Vector2 screen = Input.mousePosition;
            if (!RectTransformUtility.RectangleContainsScreenPoint(area, screen, null))
            {
                lastPointer = null;
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);
            // Local space is centred on the pivot; collectibles are placed from the bottom-left corner.
            Vector2 point = local - area.rect.min;
            Vector2 from = lastPointer ?? point;
            lastPointer = point;

            float radius = (float)session.CollectRadiusPixels;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Collectible c = live[i];
                if (DistanceToSegment(c.Image.rectTransform.anchoredPosition, from, point) > radius)
                    continue;
                bool doubleCatch = session.RollDoubleCatch(Random.value);
                bool critical = session.RollCritical(Random.value);
                BigNumber value = session.Collect(c.Kind, doubleCatch, critical);
                string text = (critical ? "CRIT! +" : "+") + NumberFormat.Dollars(value) + (doubleCatch ? " x2" : "");
                int size = c.Kind == CollectibleKind.GoldDust ? 40 : c.Kind == CollectibleKind.GiantNugget ? 64 : 52;
                ShowPopup(c.Image.rectTransform.anchoredPosition, text, critical ? size + 8 : size,
                    critical ? Palette.Critical : ColorOf(c.Kind));
                Destroy(c.Image.gameObject);
                live.RemoveAt(i);
            }
        }

        void UpdateVein()
        {
            vein.SetActive(session.VeinOpen);
            if (!session.VeinOpen)
                return;
            string multiplier = session.VeinMultiplier.ToString("0.0#", CultureInfo.InvariantCulture);
            vein.text = session.VeinLevel >= session.VeinMaxLevel
                ? $"VEIN x{multiplier}  MAX"
                : $"VEIN x{multiplier}  {session.VeinStreak}/{session.VeinCatchesPerLevel}";
        }

        static Color ColorOf(CollectibleKind kind)
        {
            switch (kind)
            {
                case CollectibleKind.Nugget: return Palette.Nugget;
                case CollectibleKind.RichNugget: return Palette.RichNugget;
                case CollectibleKind.GiantNugget: return Palette.GiantNugget;
                default: return Palette.Gold;
            }
        }

        static float SizeOf(CollectibleKind kind)
        {
            switch (kind)
            {
                case CollectibleKind.Nugget: return NuggetSize;
                case CollectibleKind.RichNugget: return RichNuggetSize;
                case CollectibleKind.GiantNugget: return GiantNuggetSize;
                default: return DustSize;
            }
        }

        void UpdateAmos(float deltaTime, BigNumber idleEarned)
        {
            amos.SetActive(session.IdleActive);
            if (!session.IdleActive)
                return;

            amos.localScale = Vector3.one * (1 + 0.04f * Mathf.Sin(Time.time * 3));
            amosPending += idleEarned;
            amosTimer += deltaTime;
            if (amosTimer < AmosPopupEvery)
                return;
            amosTimer = 0;
            if (!amosPending.IsZero)
                ShowPopup(amos.anchoredPosition + new Vector2(65, 150), "+" + NumberFormat.Dollars(amosPending), 34, Palette.TextMuted);
            amosPending = BigNumber.Zero;
        }

        void ShowPopup(Vector2 position, string text, int size, Color color)
        {
            Text label = Ui.Label("Popup", area, text, size, TextAnchor.MiddleCenter, color, FontStyle.Bold);
            label.rectTransform.Box(Vector2.zero, new Vector2(400, 80), position);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = position;
            popups.Add(new Popup { Label = label });
        }

        void AgePopups(float deltaTime)
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup p = popups[i];
                p.Age += deltaTime;
                if (p.Age >= PopupSeconds)
                {
                    Destroy(p.Label.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                p.Label.rectTransform.anchoredPosition += new Vector2(0, 140 * deltaTime);
                Color color = p.Label.color;
                color.a = 1 - p.Age / PopupSeconds;
                p.Label.color = color;
            }
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 0.0001f)
                return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
