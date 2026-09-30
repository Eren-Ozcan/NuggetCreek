using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Creek chests, Gem Pouches and Driftwood Crates floating down the creek (design doc 3.1.4):
    /// one at a time, on the surface, slower than gold. A swipe or a tap catches one and it flies
    /// to its tile in the tray; one that drifts out at the bottom is lost. The rules live in
    /// <see cref="GameSession"/>; this is the water and the flight.
    /// </summary>
    public sealed class RiverFloaters
    {
        const float Size = 150;
        const float LaneMargin = 14;
        const float FlightSeconds = 0.6f;
        const float RippleEvery = 0.8f;
        /// <summary>A tap this close to a floater's centre catches it (well over the 48 dp minimum).</summary>
        const float TapReach = Size * 0.7f;

        readonly GameSession session;
        readonly RectTransform area;
        readonly RectTransform waterLayer;
        readonly RectTransform flightLayer;
        readonly Particles particles;
        readonly Func<(float Left, float Right)> water;
        readonly Func<(float Left, float Right)> hull;

        FloaterKind kind;
        RectTransform floater;
        Image shadow;
        Vector2 start;
        Vector2 bend;
        Vector2 end;
        float age;
        float lifetime;
        float rippleIn;
        float phase;

        RectTransform flying;
        Image flyingShadow;
        Vector2 flyFrom;
        float flyAge;

        /// <summary>Width kept clear down each side for the button columns, so a floater never drifts under them.</summary>
        public float SideInset { get; set; }

        /// <summary>Where a caught floater lands, in creek pixels from the bottom-left; set by the HUD.</summary>
        public Func<FloaterKind, Vector2> TrayPoint { get; set; }

        /// <summary>Raised when a floater is caught, after the session has put it in the tray.</summary>
        public event Action<FloaterKind> Caught;

        /// <summary>The floater's centre on the water, for the PlayMode tests' swipes; null when none floats.</summary>
        public Vector2? Position => floater != null ? floater.anchoredPosition : (Vector2?)null;

        public RiverFloaters(GameSession session, RectTransform area, RectTransform waterLayer, RectTransform flightLayer,
            Particles particles, Func<(float Left, float Right)> water, Func<(float Left, float Right)> hull)
        {
            this.session = session;
            this.area = area;
            this.waterLayer = waterLayer;
            this.flightLayer = flightLayer;
            this.particles = particles;
            this.water = water;
            this.hull = hull;
        }

        /// <summary>Launches the next due floater and moves the one on the water; call while the creek runs.</summary>
        /// <param name="canLaunch">False while a show or a journey plays: a new floater waits.</param>
        public void Tick(float deltaTime, bool canLaunch)
        {
            if (floater == null && canLaunch && session.LaunchFloater() is FloaterKind next)
                Launch(next);
            if (floater == null)
                return;

            age += deltaTime;
            if (age >= lifetime)
            {
                // Drifted out at the bottom: lost (the tutorial chest comes back).
                session.MissFloater();
                Clear();
                return;
            }
            float t = age / lifetime;
            Vector2 position = Bezier(start, bend, end, t) + new Vector2(10 * Mathf.Sin(phase + age * 1.1f), 0);
            floater.anchoredPosition = position;
            floater.localRotation = Quaternion.Euler(0, 0, 6 * Mathf.Sin(phase + age * 1.7f));
            floater.localScale = Vector3.one * Motion.Bob(age + phase, 0.05f);
            shadow.rectTransform.anchoredPosition = position + new Vector2(10, -18);

            rippleIn -= deltaTime;
            if (rippleIn <= 0)
            {
                rippleIn = RippleEvery;
                particles.Emit(position, Vector2.zero, Size * 0.9f, 1.1f, Palette.Foam, Ui.Ring, 0, 0, 1.8f);
            }
        }

        /// <summary>Moves a caught floater to the tray; runs under the Mother Lode as well.</summary>
        public void TickFlight(float deltaTime)
        {
            if (flying == null)
                return;
            flyAge += deltaTime;
            float k = Mathf.Clamp01(flyAge / FlightSeconds);
            Vector2 to = TrayPoint != null ? TrayPoint(kind) : new Vector2(area.rect.width, area.rect.height * 0.6f);
            Vector2 ground = Vector2.Lerp(flyFrom, to, Mathf.SmoothStep(0, 1, k));
            float lift = Mathf.Sin(k * Mathf.PI);
            flying.anchoredPosition = ground + new Vector2(0, lift * (100 + 0.2f * Vector2.Distance(flyFrom, to)));
            flying.localScale = Vector3.one * (Mathf.Lerp(1, 0.5f, k) + 0.4f * lift);
            flyingShadow.rectTransform.anchoredPosition = ground;
            flyingShadow.color = Palette.Shadow * new Color(1, 1, 1, 1 - k);
            if (k < 1)
                return;
            UnityEngine.Object.Destroy(flying.gameObject);
            UnityEngine.Object.Destroy(flyingShadow.gameObject);
            flying = null;
        }

        /// <summary>A finger stroke over the creek; catches the floater when it passes within reach.</summary>
        public bool Sweep(Vector2 from, Vector2 to, float radius)
        {
            if (floater == null)
                return false;
            float reach = Mathf.Max(radius, Size * 0.5f);
            if (DistanceToSegment(floater.anchoredPosition, from, to) > reach)
                return false;
            Catch();
            return true;
        }

        /// <summary>A tap on the water: floaters, unlike gold, can be picked up without a swipe.</summary>
        public bool Tap(Vector2 point)
        {
            if (floater == null || Vector2.Distance(point, floater.anchoredPosition) > TapReach)
                return false;
            Catch();
            return true;
        }

        void Launch(FloaterKind next)
        {
            kind = next;
            Rect bounds = area.rect;
            // Down the wider stretch of water beside the dredge, entering at the top so it is seen.
            (float waterLeft, float waterRight) = water();
            (float hullLeft, float hullRight) = hull();
            bool left = hullLeft - waterLeft > waterRight - hullRight
                || (Mathf.Approximately(hullLeft - waterLeft, waterRight - hullRight) && UnityEngine.Random.value < 0.5f);
            float laneMin = left ? waterLeft + LaneMargin + Size / 2 : hullRight + LaneMargin + Size / 2;
            float laneMax = left ? hullLeft - LaneMargin - Size / 2 : waterRight - LaneMargin - Size / 2;
            // Out from under the button columns when the lane leaves room for it.
            float clearMin = Mathf.Max(laneMin, SideInset + Size / 2);
            float clearMax = Mathf.Min(laneMax, bounds.width - SideInset - Size / 2);
            if (clearMax >= clearMin)
            {
                laneMin = clearMin;
                laneMax = clearMax;
            }
            float LaneX() => laneMax > laneMin ? UnityEngine.Random.Range(laneMin, laneMax) : (laneMin + laneMax) / 2;
            start = new Vector2(LaneX(), bounds.height + Size / 2);
            bend = new Vector2(LaneX(), bounds.height * UnityEngine.Random.Range(0.4f, 0.6f));
            end = new Vector2(LaneX(), -Size / 2);
            age = 0;
            rippleIn = 0;
            phase = UnityEngine.Random.Range(0, Mathf.PI * 2);
            lifetime = (float)session.FloaterLifetimeSeconds;

            shadow = Ui.Image("FloaterShadow", waterLayer, Palette.Shadow, Ui.Circle);
            shadow.raycastTarget = false;
            shadow.rectTransform.Box(Vector2.zero, new Vector2(Size * 0.9f, Size * 0.5f), start);
            shadow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            floater = FloaterPicture.Build("Floater_" + kind, waterLayer, kind, Size);
            floater.anchorMin = floater.anchorMax = Vector2.zero;
            floater.pivot = new Vector2(0.5f, 0.5f);
            floater.anchoredPosition = start;
            if (session.Progress.HighContrast)
            {
                var ring = floater.gameObject.AddComponent<Outline>();
                ring.effectColor = Color.black;
                ring.effectDistance = new Vector2(5, -5);
            }
        }

        void Catch()
        {
            Vector2 from = floater.anchoredPosition;
            session.CatchFloater();
            Haptics.Important();
            Sound.Play(Sfx.Chest);
            for (int i = 0; i < 10; i++)
            {
                Vector2 velocity = UnityEngine.Random.insideUnitCircle * 200 + new Vector2(0, 260);
                particles.Emit(from, velocity, UnityEngine.Random.Range(14, 24), 0.5f, Palette.Droplet, Ui.Circle, -900);
            }

            // A floater still flying when the next is caught simply lands at once.
            if (flying != null)
                TickFlight(FlightSeconds);
            flying = floater;
            flyingShadow = shadow;
            flying.SetParent(flightLayer, false);
            flyingShadow.rectTransform.SetParent(flightLayer, false);
            flying.SetAsLastSibling();
            flying.localRotation = Quaternion.identity;
            flyFrom = from;
            flyAge = 0;
            floater = null;
            shadow = null;
            Caught?.Invoke(kind);
        }

        void Clear()
        {
            if (floater != null)
                UnityEngine.Object.Destroy(floater.gameObject);
            if (shadow != null)
                UnityEngine.Object.Destroy(shadow.gameObject);
            floater = null;
            shadow = null;
        }

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
        {
            float u = 1 - t;
            return u * u * a + 2 * u * t * b + t * t * c;
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

    /// <summary>
    /// A floater's picture: its drawing when there is one, else a stand-in built from the chest
    /// drawing and the Gem icon in the floater's own colour (design doc 3.1.4).
    /// </summary>
    public static class FloaterPicture
    {
        public static RectTransform Build(string name, Transform parent, FloaterKind kind, float size)
        {
            RectTransform root = Ui.Rect(name, parent);
            root.sizeDelta = new Vector2(size, size);
            Sprite drawn = Art.Floater(kind);
            if (drawn != null)
            {
                Picture(root, drawn, Color.white, 0);
                return root;
            }
            switch (kind)
            {
                case FloaterKind.Crate:
                    Sprite chest = Art.Floater(FloaterKind.Chest);
                    if (chest != null)
                        Picture(root, chest, Palette.Driftwood, 0);
                    else
                        Picture(root, Ui.Rounded, Palette.Driftwood, size * 0.12f);
                    break;
                case FloaterKind.Pouch:
                    Picture(root, Ui.Circle, Palette.Leather, size * 0.08f);
                    Picture(root, Art.Gem ?? Ui.Circle, Art.Gem != null ? Color.white : Palette.Gem, size * 0.28f);
                    break;
                default:
                    Picture(root, Ui.Rounded, Palette.Nugget, size * 0.12f);
                    break;
            }
            return root;
        }

        static void Picture(RectTransform root, Sprite sprite, Color color, float inset)
        {
            Image image = Ui.Image("Picture", root, color, sprite);
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.rectTransform.Fill(inset);
        }
    }
}
