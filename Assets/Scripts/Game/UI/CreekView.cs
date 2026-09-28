using System;
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
        const float DustSize = 80;
        const float NuggetSize = 100;
        const float RichNuggetSize = 115;
        const float GiantNuggetSize = 180;
        const float FadeSeconds = 0.6f;
        const float PopupSeconds = 0.9f;
        const float EdgeMargin = 90;
        const float AmosPopupEvery = 2f;
        const int HintUntilCollected = 3;
        const float BannerSeconds = 2.5f;

        sealed class Collectible
        {
            public RectTransform Root;
            public CanvasGroup Fade;
            public CollectibleKind Kind;
            public int NuggetType = -1;
            public float Age;
            public float Lifetime;
        }

        sealed class Popup
        {
            public Text Label;
            public StarRow Stars;
            public float Age;
        }

        GameSession session;
        RectTransform area;

        /// <summary>The swipe area in pixels, the space <see cref="Sweep"/> works in.</summary>
        public Vector2 AreaSize => area.rect.size;
        Image background;
        int backgroundRegion = -1;
        RectTransform amos;
        Text hint;
        Text vein;
        Text banner;
        float bannerLeft;
        readonly List<Collectible> live = new List<Collectible>();
        readonly List<Popup> popups = new List<Popup>();
        float spawnIn;
        Vector2? lastPointer;
        float amosTimer;
        BigNumber amosPending = BigNumber.Zero;

        /// <summary>Raised when a manual catch finds a Nugget type for the first time.</summary>
        public event Action<int> NuggetDiscovered;

        /// <summary>False while a modal covers the creek.</summary>
        public bool InputEnabled { get; set; } = true;

        public void Init(GameSession gameSession)
        {
            session = gameSession;
            area = (RectTransform)transform;

            var water = gameObject.AddComponent<Image>();
            water.color = Palette.Water;
            water.raycastTarget = false;
            gameObject.AddComponent<RectMask2D>();

            // The creek painting covers the area and crops its sides on tall phones.
            background = Ui.Image("Background", area, Color.white);
            background.raycastTarget = false;
            background.rectTransform.Fill();
            var cover = background.gameObject.AddComponent<AspectRatioFitter>();
            cover.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            UpdateBackground();

            hint = Legible(Ui.Label("Hint", area, "Swipe over the glinting gold!", 44, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold));
            hint.rectTransform.Place(new Vector2(0, 0.45f), new Vector2(1, 0.55f));

            vein = Legible(Ui.Label("Vein", area, "", 34, TextAnchor.MiddleLeft, Palette.Gold, FontStyle.Bold));
            vein.rectTransform.Box(new Vector2(0, 1), new Vector2(520, 60), new Vector2(30, -30));

            // A short banner, not a modal: a new Nugget type must not stop the swipe.
            banner = Legible(Ui.Label("NewNugget", area, "", 44, TextAnchor.MiddleCenter, Palette.GiantNugget, FontStyle.Bold));
            banner.rectTransform.Place(new Vector2(0, 0.78f), new Vector2(1, 0.86f));
            banner.SetActive(false);

            Image amosBody = Ui.Image("AmosMarker", area, Palette.Amos, Ui.Circle);
            amos = amosBody.rectTransform.Box(new Vector2(0, 0), new Vector2(130, 130), new Vector2(30, 30));
            Sprite amosFace = Art.Portrait("amos");
            if (amosFace != null)
            {
                // His portrait in a cream ring instead of the name disc.
                amosBody.color = Palette.Panel;
                Image inner = Ui.Image("Inner", amos, Color.white, Ui.Circle);
                inner.rectTransform.Fill(7);
                inner.raycastTarget = false;
                inner.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                Image face = Ui.Image("Face", inner.transform, Color.white, amosFace);
                face.rectTransform.Fill();
                face.raycastTarget = false;
            }
            Text amosName = Ui.Label("Name", amos, "AMOS", 30, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            amosName.rectTransform.Fill();
            if (amosFace != null)
            {
                amosName.alignment = TextAnchor.LowerCenter;
                amosName.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(-20, -8), new Vector2(20, 32));
                Legible(amosName);
            }

            ScheduleNextSpawn();
        }

        public void Tick(float deltaTime, BigNumber idleEarned)
        {
            if (session == null)
                return;

            UpdateBackground();
            hint.SetActive(session.Progress.ManualCollected < HintUntilCollected);
            UpdateAmos(deltaTime, idleEarned);
            UpdateVein();

            // A Giant bought with Rich Vein waits until no menu covers the creek.
            if (InputEnabled && session.TakeBoughtGiant())
                Spawn(CollectibleKind.GiantNugget);

            spawnIn -= deltaTime;
            if (spawnIn <= 0)
            {
                Spawn(session.RollKind(UnityEngine.Random.value));
                ScheduleNextSpawn();
            }

            AgeCollectibles(deltaTime);
            HandleSwipe();
            AgePopups(deltaTime);
            AgeBanner(deltaTime);
        }

        void ScheduleNextSpawn()
        {
            double rate = session.SpawnRate;
            // Jitter keeps the rhythm organic while the mean stays 1 / rate.
            spawnIn = (float)(1 / rate) * UnityEngine.Random.Range(0.6f, 1.4f);
        }

        void UpdateBackground()
        {
            int region = session.Progress.RegionIndex;
            if (region == backgroundRegion)
                return;
            backgroundRegion = region;
            Sprite sprite = Art.Creek(region);
            background.sprite = sprite;
            background.enabled = sprite != null;
            if (sprite != null)
                background.GetComponent<AspectRatioFitter>().aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        void Spawn(CollectibleKind kind)
        {
            int nuggetType = kind == CollectibleKind.GoldDust ? -1 : session.RollNuggetType(UnityEngine.Random.value);
            Sprite sprite = kind == CollectibleKind.GoldDust ? Art.GoldDust : Art.Nugget(nuggetType);
            float size = SizeOf(kind);
            Rect bounds = area.rect;
            var position = new Vector2(
                UnityEngine.Random.Range(EdgeMargin, bounds.width - EdgeMargin),
                UnityEngine.Random.Range(EdgeMargin + 160, bounds.height - EdgeMargin));

            // Rich and Giant Nuggets glow behind the drawing; without art the circle is the target.
            bool glow = sprite == null || kind == CollectibleKind.RichNugget || kind == CollectibleKind.GiantNugget;
            Image root = Ui.Image(kind.ToString(), area, glow ? GlowOf(kind, sprite != null) : Color.clear, Ui.Circle);
            root.raycastTarget = false;
            root.rectTransform.Box(Vector2.zero, new Vector2(size, size), position);
            root.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            root.rectTransform.anchoredPosition = position;

            Image target = root;
            if (sprite != null)
            {
                target = Ui.Image("Art", root.rectTransform, Color.white, sprite);
                target.raycastTarget = false;
                target.preserveAspect = true;
                target.rectTransform.Fill(glow ? size * 0.12f : 0);
            }
            // High contrast (design doc 14.3): a dark edge sets every target off the water.
            if (session.Progress.HighContrast)
            {
                var ring = target.gameObject.AddComponent<Outline>();
                ring.effectColor = Color.black;
                ring.effectDistance = new Vector2(5, -5);
            }

            live.Add(new Collectible
            {
                Root = root.rectTransform,
                Fade = root.gameObject.AddComponent<CanvasGroup>(),
                Kind = kind,
                NuggetType = nuggetType,
                Lifetime = (float)session.CollectibleLifetimeSeconds,
            });
        }

        static Color GlowOf(CollectibleKind kind, bool behindArt)
        {
            Color color = ColorOf(kind);
            color.a = behindArt ? 0.55f : 1;
            return color;
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
                        ShowPopup(c.Root.anchoredPosition, "Vein lost", 34, Palette.TextLight);
                    Destroy(c.Root.gameObject);
                    live.RemoveAt(i);
                    continue;
                }

                float popIn = Mathf.Clamp01(c.Age / 0.15f);
                float glint = 1 + 0.08f * Mathf.Sin(c.Age * 10);
                c.Root.localScale = Vector3.one * (popIn * glint);
                c.Fade.alpha = Mathf.Clamp01((c.Lifetime - c.Age) / FadeSeconds);
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
            Sweep(from, point);
        }

        /// <summary>
        /// Collects everything within reach of one finger stroke, in creek-area pixels from its
        /// bottom-left corner. Public so the PlayMode tests can swipe without an input device.
        /// </summary>
        public void Sweep(Vector2 from, Vector2 point)
        {
            float radius = (float)session.CollectRadiusPixels;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Collectible c = live[i];
                if (DistanceToSegment(c.Root.anchoredPosition, from, point) > radius)
                    continue;
                bool doubleCatch = session.RollDoubleCatch(UnityEngine.Random.value);
                bool critical = session.RollCritical(UnityEngine.Random.value);
                BigNumber value = session.Collect(c.Kind, doubleCatch, critical);
                string text = (critical ? "CRIT! +" : "+") + NumberFormat.Dollars(value) + (doubleCatch ? " x2" : "");
                int size = c.Kind == CollectibleKind.GoldDust ? 40 : c.Kind == CollectibleKind.GiantNugget ? 64 : 52;
                ShowPopup(c.Root.anchoredPosition, text, critical ? size + 8 : size,
                    critical ? Palette.Critical : ColorOf(c.Kind));
                Haptics.Tap();
                Sound.Pick(manual: true);
                if (c.NuggetType >= 0)
                    RecordNugget(c.NuggetType, c.Root.anchoredPosition);
                Destroy(c.Root.gameObject);
                live.RemoveAt(i);
            }
        }

        void RecordNugget(int index, Vector2 position)
        {
            NuggetCatch result = session.CatchNugget(index);
            string name = GameCatalog.Nuggets[index].Name;
            if (result.Discovered)
            {
                banner.SetText($"NEW NUGGET!  {name}");
                banner.SetActive(true);
                bannerLeft = BannerSeconds;
                Haptics.Important();
                NuggetDiscovered?.Invoke(index);
            }
            else if (result.StarsGained > 0)
            {
                Popup popup = ShowPopup(position + new Vector2(0, 70), name, 36, Palette.GiantNugget);
                popup.Stars = StarRow.Create("Stars", popup.Label.transform, session.Economy.MaxStarsPerNugget, 40);
                ((RectTransform)popup.Stars.transform).Box(new Vector2(0.5f, 0), new Vector2(popup.Stars.Width, 40), new Vector2(0, -36));
                popup.Stars.Set(session.NuggetStars(index));
            }
        }

        void AgeBanner(float deltaTime)
        {
            if (bannerLeft <= 0)
                return;
            bannerLeft -= deltaTime;
            if (bannerLeft <= 0)
                banner.SetActive(false);
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
                ShowPopup(amos.anchoredPosition + new Vector2(65, 150), "+" + NumberFormat.Dollars(amosPending), 34, Palette.TextLight);
            amosPending = BigNumber.Zero;
        }

        Popup ShowPopup(Vector2 position, string text, int size, Color color)
        {
            Text label = Legible(Ui.Label("Popup", area, text, size, TextAnchor.MiddleCenter, color, FontStyle.Bold));
            label.rectTransform.Box(Vector2.zero, new Vector2(400, 80), position);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = position;
            var popup = new Popup { Label = label };
            popups.Add(popup);
            return popup;
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
                if (p.Stars != null)
                    p.Stars.Fade(color.a);
            }
        }

        /// <summary>A dark edge keeps light text readable on the creek painting.</summary>
        static Text Legible(Text label)
        {
            var edge = label.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0, 0, 0, 0.75f);
            edge.effectDistance = new Vector2(3, -3);
            return label;
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
