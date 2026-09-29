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
        /// <summary>
        /// Where gold turns up (design doc 3.1.3): from the top, from under a bank, or rising to the
        /// surface anywhere on the open water, so the finger has to look all over the creek, not
        /// only along the top. The rest comes in from the top.
        /// </summary>
        const float BankEntryShare = 0.25f;
        const float SurfaceShare = 0.35f;
        /// <summary>Gold that turns up in the water, not over the edge, grows in over this time.</summary>
        const float EmergeSeconds = 0.3f;
        /// <summary>The finger has to move this far, in creek pixels, before a stroke collects.</summary>
        const float MinSwipeStep = 6;
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
            /// <summary>Its path: a curve from where it enters to where it leaves the creek.</summary>
            public Vector2 Start;
            public Vector2 Bend;
            public Vector2 End;
            /// <summary>Turned up in the water (from a bank or rising): it grows in instead of drifting in.</summary>
            public bool Emerges;
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
        SwipeTrail trail;

        /// <summary>The swipe area in pixels, the space <see cref="Sweep"/> works in.</summary>
        public Vector2 AreaSize => area.rect.size;
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

        /// <summary>Screen kept by the controls at the top and bottom; gold never rises under them.</summary>
        float stageTop;
        float stageBottom;

        /// <summary>The open water between the controls, as heights in <see cref="AreaSize"/>: where gold may turn up.</summary>
        public float OpenWaterBottom => stageBottom;
        public float OpenWaterTop => AreaSize.y - stageTop;

        /// <param name="topInset">Screen kept by the controls at the top; the dredge stays below it.</param>
        /// <param name="bottomInset">Screen kept by the controls at the bottom.</param>
        public void Init(GameSession gameSession, float topInset = 0, float bottomInset = 0)
        {
            session = gameSession;
            area = (RectTransform)transform;
            stageTop = topInset;
            stageBottom = bottomInset;
            gameObject.AddComponent<RectMask2D>();

            // Bottom to top: water, gold under the surface, the dredge, gold in flight, sparkles.
            river = Ui.Rect("River", area).Fill().gameObject.AddComponent<RiverView>();
            river.Init(area);
            RectTransform wakeLayer = Ui.Rect("Wake", area).Fill();
            goldLayer = Ui.Rect("Gold", area).Fill();
            RectTransform dredgeLayer = Ui.Rect("Dredge", area).Fill();
            flightLayer = Ui.Rect("Flights", area).Fill();
            particles = new Particles(Ui.Rect("Particles", area).Fill());
            dredge = dredgeLayer.gameObject.AddComponent<DredgeView>();
            dredge.Init(session, area, wakeLayer, particles);
            dredge.SetStage(topInset, bottomInset);
            GameObject trailObject = Ui.Rect("SwipeTrail", area).Fill().gameObject;
            trailObject.AddComponent<CanvasRenderer>();
            trail = trailObject.AddComponent<SwipeTrail>();

            hint = Legible(Ui.Label("Hint", area, "Swipe over the glinting gold!", 44, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold));
            hint.rectTransform.Place(new Vector2(0, 0.45f), new Vector2(1, 0.55f));

            vein = Legible(Ui.Label("Vein", area, "", 34, TextAnchor.MiddleLeft, Palette.Gold, FontStyle.Bold));
            vein.rectTransform.Box(new Vector2(0.5f, 0), new Vector2(520, 60), new Vector2(0, bottomInset + 10));
            vein.alignment = TextAnchor.MiddleCenter;

            // A short banner, not a modal: a new Nugget type must not stop the swipe.
            banner = Legible(Ui.Label("NewNugget", area, "", 44, TextAnchor.MiddleCenter, Palette.GiantNugget, FontStyle.Bold));
            banner.rectTransform.Place(new Vector2(0, 0.74f), new Vector2(1, 0.8f));
            banner.SetActive(false);

            // A new tier or creek gets a heading over the upper creek; like the banner, no modal.
            caption = Ui.Rect("Caption", area).Place(new Vector2(0, 0.6f), new Vector2(1, 0.74f)).gameObject.AddComponent<CanvasGroup>();
            caption.blocksRaycasts = false;
            captionTitle = Legible(Ui.Title("Title", caption.transform, "", 76, Palette.TextLight));
            captionTitle.rectTransform.Place(new Vector2(0, 0.4f), Vector2.one);
            captionSubtitle = Legible(Ui.Label("Subtitle", caption.transform, "", 40, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold));
            captionSubtitle.rectTransform.Place(Vector2.zero, new Vector2(1, 0.4f));
            caption.gameObject.SetActive(false);
            string multiplier = session.Economy.Config.TierMultiplier.ToString("0.#", CultureInfo.InvariantCulture);
            dredge.TierArrived += tier => ShowCaption($"SLUICE TIER {tier + 1}", $"x{multiplier} income, 2 new upgrades");

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
            FollowRegion();
            river.Tick(deltaTime);
            dredge.Tick(deltaTime, RiverView.FlowSpeed, InputEnabled, river.Travel);
            trail.Tick(deltaTime);
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

            // Stays in the water on one side of the dredge, never under it or on the banks.
            bool left = UnityEngine.Random.value < 0.5f;
            float laneMin = left ? river.WaterLeft + LaneMargin : dredge.HullRight + LaneMargin;
            float laneMax = left ? dredge.HullLeft - LaneMargin : river.WaterRight - LaneMargin;
            float room = WanderPixels + size / 2;
            float LaneX() => laneMax - laneMin > room * 2
                ? UnityEngine.Random.Range(laneMin + room, laneMax - room)
                : (laneMin + laneMax) / 2;

            // Enters from the top, washes out from under its bank, or rises to the surface out on
            // the water; the current carries it out at the bottom either way. Never upstream.
            float roll = UnityEngine.Random.value;
            bool fromBank = roll < BankEntryShare;
            bool rises = !fromBank && roll < BankEntryShare + SurfaceShare;
            // The open water between the controls, where rising gold can be seen and reached.
            float lowest = stageBottom + 60;
            float highest = Mathf.Max(lowest, bounds.height - stageTop - 60);
            Vector2 start;
            Vector2 bend;
            if (fromBank)
            {
                // A wobble inside the water line, so the wobble never carries it onto the sand.
                float bankX = left ? river.WaterLeft + WanderPixels : river.WaterRight - WanderPixels;
                start = new Vector2(bankX, UnityEngine.Random.Range(Mathf.Lerp(lowest, highest, 0.2f), highest));
                bend = new Vector2(LaneX(), start.y - bounds.height * UnityEngine.Random.Range(0.08f, 0.2f));
            }
            else if (rises)
            {
                start = new Vector2(LaneX(), UnityEngine.Random.Range(Mathf.Lerp(lowest, highest, 0.15f), highest));
                bend = new Vector2(LaneX(), start.y * UnityEngine.Random.Range(0.4f, 0.7f));
                // Rings spread where it breaks the surface.
                particles.Emit(start, Vector2.zero, size * 0.8f, 0.7f, Palette.Foam, Ui.Ring, 0, 0, 2.4f);
            }
            else
            {
                start = new Vector2(LaneX(), bounds.height + size / 2);
                bend = new Vector2(LaneX(), bounds.height * UnityEngine.Random.Range(0.35f, 0.65f));
            }
            var end = new Vector2(LaneX(), -size / 2);

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
                Bend = bend,
                End = end,
                Emerges = fromBank || rises,
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

                // Every path takes the whole lifetime, so upgrades that extend it slow the gold.
                float t = c.Age / c.Lifetime;
                Vector2 position = Bezier(c.Start, c.Bend, c.End, t) + new Vector2(WanderPixels * Mathf.Sin(c.Wander + c.Age * 1.7f), 0);
                Vector2 velocity = BezierSlope(c.Start, c.Bend, c.End, t) / c.Lifetime;
                c.Root.anchoredPosition = position;
                float grow = c.Emerges ? Motion.EaseOutBack(Mathf.Clamp01(c.Age / EmergeSeconds)) : 1;
                c.Root.localScale = Vector3.one * (grow * (1 + 0.05f * Mathf.Sin(c.Wander + c.Age * 6)));

                // Glints come and go on the gold and drift with it.
                c.GlintIn -= deltaTime;
                if (c.GlintIn <= 0)
                {
                    c.GlintIn = UnityEngine.Random.Range(0.25f, 0.6f);
                    float scale = c.Size / DustSize;
                    particles.Emit(position + UnityEngine.Random.insideUnitCircle * (c.Size * 0.35f), velocity,
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
            trail.Add(point, lastPointer == null);
            // A swipe, not a tap: only a moving finger collects.
            if (lastPointer is Vector2 from)
            {
                if ((point - from).sqrMagnitude < MinSwipeStep * MinSwipeStep)
                    return;
                Sweep(from, point);
            }
            lastPointer = point;
        }

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
        {
            float u = 1 - t;
            return u * u * a + 2 * u * t * b + t * t * c;
        }

        static Vector2 BezierSlope(Vector2 a, Vector2 b, Vector2 c, float t) => 2 * (1 - t) * (b - a) + 2 * t * (c - b);

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

        /// <summary>Amos works on the dredge: what he pans rises from its chest now and then.</summary>
        void UpdateAmos(float deltaTime, BigNumber idleEarned)
        {
            if (!session.IdleActive)
                return;

            amosPending += idleEarned;
            amosTimer += deltaTime;
            if (amosTimer < AmosPopupEvery)
                return;
            amosTimer = 0;
            if (!amosPending.IsZero)
                ShowPopup(dredge.ChestPoint + new Vector2(0, 90), "+" + NumberFormat.Dollars(amosPending), 34, Palette.TextLight);
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
