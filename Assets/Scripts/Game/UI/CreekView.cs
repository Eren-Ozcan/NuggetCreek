using System;
using System.Collections.Generic;
using System.Globalization;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The creek seen from above: the river with the dredge in the middle. Gold Dust and Nuggets
    /// (plain, Rich, Giant) drift down the water on either side of the dredge, glinting under
    /// the surface, and a swipe over them collects them (design doc 3.1, 3.1.1): the money is
    /// paid at once, then the gold flies up to the dredge's chest. Rolls critical catches,
    /// shows the vein level once it is open, and Amos panning while idle is active.
    /// </summary>
    public sealed class CreekView : MonoBehaviour
    {
        const float DustSize = 80;
        const float NuggetSize = 100;
        const float RichNuggetSize = 115;
        const float GiantNuggetSize = 180;
        const float PopupSeconds = 0.9f;
        const float AmosPopupEvery = 2f;
        const int HintUntilCollected = 3;
        const float BannerSeconds = 2.5f;
        /// <summary>Gap the gold keeps from the bank and from the hull.</summary>
        const float LaneMargin = 14;
        /// <summary>Side-to-side wander of gold in the current.</summary>
        const float WanderPixels = 14;
        const float FlightSeconds = 0.45f;
        /// <summary>Gold scale at the top of its arc to the chest, and when it lands.</summary>
        const float FlightPeak = 1.9f;
        const float FlightLanding = 0.5f;
        const float CaptionSeconds = 2.4f;

        sealed class Collectible
        {
            public RectTransform Root;
            public Image Body;
            public Image Art;
            public Color BodyColor;
            public CollectibleKind Kind;
            public int NuggetType = -1;
            public float Size;
            public float Age;
            public float Lifetime;
            public Vector2 Start;
            public float Speed;
            public float Wander;
            public float GlintIn;
        }

        sealed class Flight
        {
            public Collectible Gold;
            public Image Shadow;
            public Vector2 From;
            public float Age;
            public float DropletIn;
        }

        sealed class Popup
        {
            public Text Label;
            public StarRow Stars;
            public float Age;
        }

        GameSession session;
        RectTransform area;
        RiverView river;
        DredgeView dredge;
        RectTransform goldLayer;
        RectTransform flightLayer;
        Particles particles;

        /// <summary>The swipe area in pixels, the space <see cref="Sweep"/> works in.</summary>
        public Vector2 AreaSize => area.rect.size;
        RectTransform amos;
        Text hint;
        Text vein;
        Text banner;
        float bannerLeft;
        CanvasGroup caption;
        Text captionTitle;
        Text captionSubtitle;
        float captionAge = CaptionSeconds;
        int shownRegion = -1;
        readonly List<Collectible> live = new List<Collectible>();
        readonly List<Flight> flights = new List<Flight>();
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
            gameObject.AddComponent<RectMask2D>();

            // Bottom to top: water, gold under the surface, the dredge, gold in flight, sparkles.
            river = Ui.Rect("River", area).Fill().gameObject.AddComponent<RiverView>();
            river.Init(area);
            goldLayer = Ui.Rect("Gold", area).Fill();
            RectTransform dredgeLayer = Ui.Rect("Dredge", area).Fill();
            flightLayer = Ui.Rect("Flights", area).Fill();
            particles = new Particles(Ui.Rect("Particles", area).Fill());
            dredge = dredgeLayer.gameObject.AddComponent<DredgeView>();
            dredge.Init(session, area, particles);

            hint = Legible(Ui.Label("Hint", area, "Swipe over the glinting gold!", 44, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold));
            hint.rectTransform.Place(new Vector2(0, 0.45f), new Vector2(1, 0.55f));

            vein = Legible(Ui.Label("Vein", area, "", 34, TextAnchor.MiddleLeft, Palette.Gold, FontStyle.Bold));
            vein.rectTransform.Box(new Vector2(0, 1), new Vector2(520, 60), new Vector2(30, -30));

            // A short banner, not a modal: a new Nugget type must not stop the swipe.
            banner = Legible(Ui.Label("NewNugget", area, "", 44, TextAnchor.MiddleCenter, Palette.GiantNugget, FontStyle.Bold));
            banner.rectTransform.Place(new Vector2(0, 0.78f), new Vector2(1, 0.86f));
            banner.SetActive(false);

            // A new tier or creek gets a heading over the upper creek; like the banner, no modal.
            caption = Ui.Rect("Caption", area).Place(new Vector2(0, 0.64f), new Vector2(1, 0.8f)).gameObject.AddComponent<CanvasGroup>();
            caption.blocksRaycasts = false;
            captionTitle = Legible(Ui.Title("Title", caption.transform, "", 76, Palette.TextLight));
            captionTitle.rectTransform.Place(new Vector2(0, 0.4f), Vector2.one);
            captionSubtitle = Legible(Ui.Label("Subtitle", caption.transform, "", 40, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold));
            captionSubtitle.rectTransform.Place(Vector2.zero, new Vector2(1, 0.4f));
            caption.gameObject.SetActive(false);
            string multiplier = session.Economy.Config.TierMultiplier.ToString("0.#", CultureInfo.InvariantCulture);
            dredge.TierArrived += tier => ShowCaption($"SLUICE TIER {tier + 1}", $"x{multiplier} income, 2 new upgrades");

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

            TickScenery(deltaTime);
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

        /// <summary>
        /// The moving river, the dredge and gold already in flight. Runs on its own while the
        /// Mother Lode covers the creek, so the water does not freeze under it.
        /// </summary>
        public void TickScenery(float deltaTime)
        {
            float currentSpeed = CurrentSpeed;
            FollowRegion();
            river.Tick(deltaTime, currentSpeed);
            dredge.Tick(deltaTime, currentSpeed, InputEnabled, river.Travel);
            AgeFlights(deltaTime);
            particles.Tick(deltaTime);
            AgeCaption(deltaTime);
        }

        /// <summary>A tier show or a journey to another creek is playing: not the moment for an ad break.</summary>
        public bool ShowPlaying => dredge.ShowBusy || river.Traveling;

        /// <summary>A new creek waits until the map is closed, then the dredge sails there.</summary>
        void FollowRegion()
        {
            int region = session.Progress.RegionIndex;
            if (region == shownRegion || (shownRegion >= 0 && !InputEnabled))
                return;
            bool travel = shownRegion >= 0;
            shownRegion = region;
            river.SetRegion(region, travel);
            if (travel)
                ShowCaption(GameCatalog.RegionNames[region], $"Creek {region + 1}");
        }

        void ShowCaption(string title, string subtitle)
        {
            captionTitle.SetText(title);
            captionSubtitle.SetText(subtitle);
            caption.gameObject.SetActive(true);
            captionAge = 0;
            AgeCaption(0);
        }

        /// <summary>Springs in, holds, then fades.</summary>
        void AgeCaption(float deltaTime)
        {
            if (captionAge >= CaptionSeconds)
                return;
            captionAge += deltaTime;
            if (captionAge >= CaptionSeconds)
            {
                caption.gameObject.SetActive(false);
                return;
            }
            float pop = Mathf.Clamp01(captionAge / 0.25f);
            caption.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1, Motion.EaseOutBack(pop));
            caption.alpha = Mathf.Clamp01((CaptionSeconds - captionAge) / 0.5f);
        }

        /// <summary>
        /// Pixels per second the water carries gold: from entering at the top to leaving at the
        /// bottom takes the collectible lifetime, so upgrades that extend it slow the current.
        /// </summary>
        float CurrentSpeed => (area.rect.height + NuggetSize) / (float)session.CollectibleLifetimeSeconds;

        void ScheduleNextSpawn()
        {
            double rate = session.SpawnRate;
            // Jitter keeps the rhythm organic while the mean stays 1 / rate.
            spawnIn = (float)(1 / rate) * UnityEngine.Random.Range(0.6f, 1.4f);
        }

        void Spawn(CollectibleKind kind)
        {
            int nuggetType = kind == CollectibleKind.GoldDust ? -1 : session.RollNuggetType(UnityEngine.Random.value);
            Sprite sprite = kind == CollectibleKind.GoldDust ? Art.GoldDust : Art.Nugget(nuggetType);
            float size = SizeOf(kind);
            Rect bounds = area.rect;

            // Enters above the creek in the water on one side of the dredge, never on the banks.
            bool left = UnityEngine.Random.value < 0.5f;
            float laneMin = left ? river.WaterLeft + LaneMargin : dredge.HullRight + LaneMargin;
            float laneMax = left ? dredge.HullLeft - LaneMargin : river.WaterRight - LaneMargin;
            float room = WanderPixels + size / 2;
            float x = laneMax - laneMin > room * 2
                ? UnityEngine.Random.Range(laneMin + room, laneMax - room)
                : (laneMin + laneMax) / 2;
            var start = new Vector2(x, bounds.height + size / 2);

            // Rich and Giant Nuggets glow behind the drawing; without art the circle is the target.
            bool glow = sprite == null || kind == CollectibleKind.RichNugget || kind == CollectibleKind.GiantNugget;
            Color bodyColor = glow ? GlowOf(kind, sprite != null) : Color.clear;
            Image root = Ui.Image(kind.ToString(), goldLayer, bodyColor * Palette.Underwater, Ui.Circle);
            root.raycastTarget = false;
            root.rectTransform.Box(Vector2.zero, new Vector2(size, size), start);
            root.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            root.rectTransform.anchoredPosition = start;

            Image art = null;
            if (sprite != null)
            {
                art = Ui.Image("Art", root.rectTransform, Palette.Underwater, sprite);
                art.raycastTarget = false;
                art.preserveAspect = true;
                art.rectTransform.Fill(glow ? size * 0.12f : 0);
            }
            // High contrast (design doc 14.3): a dark edge sets every target off the water.
            if (session.Progress.HighContrast)
            {
                var ring = (art != null ? art : root).gameObject.AddComponent<Outline>();
                ring.effectColor = Color.black;
                ring.effectDistance = new Vector2(5, -5);
            }

            float lifetime = (float)session.CollectibleLifetimeSeconds;
            live.Add(new Collectible
            {
                Root = root.rectTransform,
                Body = root,
                Art = art,
                BodyColor = bodyColor,
                Kind = kind,
                NuggetType = nuggetType,
                Size = size,
                Lifetime = lifetime,
                Start = start,
                Speed = (bounds.height + size) / lifetime,
                Wander = UnityEngine.Random.Range(0, Mathf.PI * 2),
                GlintIn = UnityEngine.Random.Range(0.1f, 0.4f),
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
                    // It has drifted out at the bottom.
                    if (session.LoseCollectible() && session.VeinOpen)
                        ShowPopup(new Vector2(c.Root.anchoredPosition.x, 80), "Vein lost", 34, Palette.TextLight);
                    Destroy(c.Root.gameObject);
                    live.RemoveAt(i);
                    continue;
                }

                var position = new Vector2(
                    c.Start.x + WanderPixels * Mathf.Sin(c.Wander + c.Age * 1.7f),
                    c.Start.y - c.Speed * c.Age);
                c.Root.anchoredPosition = position;
                c.Root.localScale = Vector3.one * (1 + 0.05f * Mathf.Sin(c.Wander + c.Age * 6));

                // Glints come and go on the gold and drift with it.
                c.GlintIn -= deltaTime;
                if (c.GlintIn <= 0)
                {
                    c.GlintIn = UnityEngine.Random.Range(0.25f, 0.6f);
                    float scale = c.Size / DustSize;
                    particles.Emit(position + UnityEngine.Random.insideUnitCircle * (c.Size * 0.35f), new Vector2(0, -c.Speed),
                        UnityEngine.Random.Range(24, 40) * scale, 0.45f, Palette.Glint, Ui.Sparkle, 0, 90);
                }
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
                live.RemoveAt(i);
                Launch(c);
            }
        }

        /// <summary>The caught gold leaves the water and flies to the dredge's chest; the money is already paid.</summary>
        void Launch(Collectible c)
        {
            Vector2 from = c.Root.anchoredPosition;
            // Same anchors in both layers, so the gold stays where it was caught.
            c.Root.SetParent(flightLayer, false);
            c.Root.localScale = Vector3.one;
            c.Body.color = c.BodyColor;
            if (c.Art != null)
                c.Art.color = Color.white;

            Image shadow = Ui.Image("Shadow", flightLayer, Palette.Shadow, Ui.Circle);
            shadow.raycastTarget = false;
            shadow.rectTransform.Box(Vector2.zero, new Vector2(c.Size * 0.8f, c.Size * 0.4f), from);
            shadow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            shadow.rectTransform.anchoredPosition = from;
            c.Root.SetAsLastSibling();

            for (int i = 0; i < 6; i++)
            {
                Vector2 velocity = UnityEngine.Random.insideUnitCircle * 160 + new Vector2(0, 220);
                particles.Emit(from, velocity, UnityEngine.Random.Range(12, 20), 0.4f, Palette.Droplet, Ui.Circle, -900);
            }
            flights.Add(new Flight { Gold = c, Shadow = shadow, From = from });
        }

        void AgeFlights(float deltaTime)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                Flight f = flights[i];
                f.Age += deltaTime;
                float k = Mathf.Clamp01(f.Age / FlightSeconds);
                if (k >= 1)
                {
                    dredge.ChestHit();
                    Destroy(f.Gold.Root.gameObject);
                    Destroy(f.Shadow.gameObject);
                    flights.RemoveAt(i);
                    continue;
                }

                // The shadow slides straight to the chest on the water; the gold arcs above it.
                Vector2 to = dredge.ChestPoint;
                Vector2 ground = Vector2.Lerp(f.From, to, Mathf.SmoothStep(0, 1, k));
                float lift = Mathf.Sin(k * Mathf.PI);
                float apex = 120 + 0.35f * Vector2.Distance(f.From, to);
                Vector2 position = ground + new Vector2(0, lift * apex);
                f.Gold.Root.anchoredPosition = position;
                f.Gold.Root.localScale = Vector3.one * (Mathf.Lerp(1, FlightLanding, k) + (FlightPeak - 1) * lift);
                f.Shadow.rectTransform.anchoredPosition = ground;
                f.Shadow.rectTransform.localScale = Vector3.one * (1 - 0.4f * lift);

                // Drops fall off it on the way up.
                f.DropletIn -= deltaTime;
                if (k < 0.6f && f.DropletIn <= 0)
                {
                    f.DropletIn = 0.03f;
                    var velocity = new Vector2(UnityEngine.Random.Range(-40, 40), -60);
                    particles.Emit(position, velocity, UnityEngine.Random.Range(10, 16), 0.35f, Palette.Droplet, Ui.Circle, -700);
                }
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

        /// <summary>A dark edge keeps light text readable on the river and the dredge.</summary>
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
