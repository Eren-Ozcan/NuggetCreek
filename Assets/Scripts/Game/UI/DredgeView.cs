using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The dredge anchored mid-river: the current sluice tier's drawing, rocking on the water with
    /// foam at the bow and along the hull and a V wake behind it, its moving parts working
    /// (<see cref="DredgeParts"/>). Buying an upgrade bumps it. A new tier waits until nothing
    /// covers the creek, then lands with a show: its silhouette drops in at twice the size, a wave
    /// runs over the water and the new drawing grows in with a golden glow. A rebirth back to a
    /// lower tier swaps it quietly. Caught gold flies to its chest (<see cref="ChestPoint"/>),
    /// which hops when the gold lands.
    /// </summary>
    public sealed class DredgeView : MonoBehaviour
    {
        /// <summary>Where the hull and the chest sit on one tier's drawing.</summary>
        readonly struct TierLayout
        {
            /// <summary>Hull width as a share of the creek width: the dredge grows by hull, not by drawing.</summary>
            public readonly float HullShare;
            /// <summary>Drawing height over width, for the greybox when the art is missing.</summary>
            public readonly float Aspect;
            /// <summary>The hull, as fractions of the drawing from its bottom-left corner.</summary>
            public readonly Rect Hull;
            /// <summary>The gold in the chest, as fractions of the drawing.</summary>
            public readonly Rect Chest;

            public TierLayout(float hullShare, float aspect, float hullLeft, float hullRight, float hullBottom, float hullTop,
                float chestLeft, float chestRight, float chestBottom, float chestTop)
            {
                HullShare = hullShare;
                Aspect = aspect;
                Hull = Rect.MinMaxRect(hullLeft, hullBottom, hullRight, hullTop);
                Chest = Rect.MinMaxRect(chestLeft, chestBottom, chestRight, chestTop);
            }
        }

        // Measured on the cut drawings. Tier 6's long, narrow hull reads bigger than its width, so
        // it gets a smaller share than tier 5; the big step comes at tier 7.
        static readonly TierLayout[] Tiers =
        {
            new TierLayout(0.20f, 1.215f, 0.052f, 0.944f, 0.054f, 0.957f, 0.657f, 0.786f, 0.303f, 0.377f),
            new TierLayout(0.22f, 1.234f, 0.019f, 0.981f, 0.021f, 0.981f, 0.672f, 0.813f, 0.284f, 0.367f),
            new TierLayout(0.25f, 1.221f, 0.017f, 0.985f, 0.018f, 0.951f, 0.219f, 0.386f, 0.810f, 0.880f),
            new TierLayout(0.24f, 3.030f, 0.012f, 0.947f, 0.035f, 0.700f, 0.740f, 0.876f, 0.427f, 0.502f),
            new TierLayout(0.27f, 1.588f, 0.008f, 0.668f, 0.059f, 0.965f, 0.089f, 0.229f, 0.546f, 0.587f),
            new TierLayout(0.24f, 3.151f, 0.028f, 0.972f, 0.040f, 0.970f, 0.372f, 0.566f, 0.336f, 0.385f),
            new TierLayout(0.34f, 1.080f, 0.274f, 0.992f, 0.018f, 0.650f, 0.835f, 0.915f, 0.116f, 0.150f),
            new TierLayout(0.36f, 1.719f, 0.066f, 0.932f, 0.053f, 0.890f, 0.617f, 0.731f, 0.217f, 0.250f),
            new TierLayout(0.38f, 1.756f, 0.057f, 0.947f, 0.050f, 0.895f, 0.621f, 0.740f, 0.215f, 0.250f),
            new TierLayout(0.38f, 2.702f, 0.011f, 0.989f, 0.184f, 0.795f, 0.625f, 0.765f, 0.302f, 0.328f),
        };

        /// <summary>The drawing never takes more than this share of the creek height.</summary>
        const float MaxHeightShare = 0.8f;
        const float PurchasePeak = 1.05f;
        const float PurchaseSeconds = 0.3f;
        const float ChestPeak = 1.35f;
        const float ChestSeconds = 0.3f;
        /// <summary>Foam a second: churn at the stern, streaks behind it, dashes on each V arm and hull side.</summary>
        const float SternFoamRate = 95;
        const float StreakRate = 14;
        const float ArmFoamRate = 20;
        const float SideFoamRate = 9;
        /// <summary>The V arms open this far from straight back, in degrees.</summary>
        const float ArmAngle = 17;

        // The tier-up show, in seconds from the swap.
        const float GrowSeconds = 0.7f;
        const float FlashSeconds = 0.9f;
        /// <summary>The silhouette drops from twice the size onto the dredge in this time.</summary>
        const float LandSeconds = 0.55f;
        const float HaloStart = 0.35f;
        const float HaloSeconds = 0.95f;
        const float WaveSeconds = 0.8f;
        const float WashSeconds = 0.35f;
        const float ShowSeconds = 1.6f;

        GameSession session;
        Particles particles;
        RectTransform area;
        RectTransform root;
        Image art;
        Image greyHull;
        RectTransform chest;
        Image chestPatch;
        Sprite chestSprite;
        Image flash;
        Image halo;
        Image ghost;
        Image wave;
        Image wash;
        WakeGraphic wakeStrip;
        Particles foam;
        float sternFoamDue;
        float armFoamDue;
        float streakDue;
        float sideFoamDue;
        DredgeParts parts;

        int shownTier = -1;
        int boughtLevels = -1;
        float time;
        float wakePhase;
        float stageTop;
        float stageBottom;
        float flowSpeed;
        float purchaseAge = PurchaseSeconds;
        float showAge = ShowSeconds;
        float chestAge = ChestSeconds;
        Vector2 hullSize;

        /// <summary>A new tier is on screen and its show has started.</summary>
        public event Action<int> TierArrived;

        /// <summary>The tier on screen, 0 for tier 1.</summary>
        public int ShownTier => shownTier;

        /// <summary>A bought tier waits for the creek to be uncovered, or its show is playing.</summary>
        public bool ShowBusy => session.Progress.TierIndex > shownTier || showAge < ShowSeconds;

        /// <summary>The chest's centre in creek-area pixels from the bottom-left corner, where caught gold lands.</summary>
        public Vector2 ChestPoint => ToArea(chest.TransformPoint(Vector3.zero));

        /// <summary>The hull's sides in creek-area pixels from the left; gold drifts past outside them.</summary>
        public float HullLeft => area.rect.width / 2 - hullSize.x / 2;

        public float HullRight => area.rect.width / 2 + hullSize.x / 2;

        /// <summary>The whole drawing, in creek-area pixels from the bottom-left corner.</summary>
        public Rect ArtBounds => AreaBounds(art.rectTransform);

        public Rect ChestBounds => AreaBounds(chest);

        /// <param name="wakeLayer">Under the gold, so foam never hides it.</param>
        public void Init(GameSession gameSession, RectTransform creekArea, RectTransform wakeLayer, Particles sparkles)
        {
            session = gameSession;
            area = creekArea;
            particles = sparkles;

            GameObject strip = Ui.Rect("WakeStrip", wakeLayer).Fill().gameObject;
            strip.AddComponent<CanvasRenderer>();
            wakeStrip = strip.AddComponent<WakeGraphic>();
            wakeStrip.rectTransform.pivot = Vector2.zero;
            wakeStrip.color = Palette.WakeStrip;
            wakeStrip.raycastTarget = false;
            foam = new Particles(wakeLayer);

            root = Ui.Rect("Body", transform).Box(Vector2.zero, Vector2.zero);
            root.pivot = new Vector2(0.5f, 0.5f);

            flash = Water("Flash", Palette.Gold, Ui.Glow);
            halo = Silhouette("Halo", Palette.Glint);

            art = Ui.Image("Art", root, Color.white);
            art.raycastTarget = false;
            art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ghost = Silhouette("Ghost", Color.white);

            // Without the drawing: a plank-brown hull and a gold chest in the same places.
            greyHull = Ui.Image("Hull", art.transform, Palette.Amos, Ui.Rounded);
            greyHull.type = Image.Type.Sliced;
            greyHull.raycastTarget = false;

            Image chestMask = Ui.Image("Chest", art.transform, Color.white, Ui.Circle);
            chestMask.raycastTarget = false;
            chestMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            chest = chestMask.rectTransform;
            chestPatch = Ui.Image("Gold", chest, Color.white);
            chestPatch.raycastTarget = false;
            chestPatch.rectTransform.Fill();

            parts = new DredgeParts(art.rectTransform, particles, ToArea);

            // Over the whole creek, not turning with the dredge.
            wave = Ui.Image("Wave", transform, Palette.Foam, Ui.Ring);
            wave.raycastTarget = false;
            wave.rectTransform.Box(Vector2.zero, Vector2.zero);
            wave.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            wash = Ui.Image("Wash", transform, Palette.Wash);
            wash.raycastTarget = false;
            wash.rectTransform.Fill();

            Tick(0, 0, true, 0);
        }

        /// <summary>Keeps the dredge between the controls at the top and bottom of the screen.</summary>
        public void SetStage(float topInset, float bottomInset)
        {
            stageTop = topInset;
            stageBottom = bottomInset;
        }

        Image Water(string name, Color color, Sprite sprite)
        {
            Image image = Ui.Image(name, root, color, sprite);
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        Image Silhouette(string name, Color color)
        {
            Image image = Ui.Image(name, root, color);
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            return image;
        }

        /// <param name="visible">False while a panel covers the creek: a new tier waits for it.</param>
        /// <param name="travel">0 to 1 while the dredge is under way to another creek.</param>
        public void Tick(float deltaTime, float currentSpeed, bool visible, float travel)
        {
            time += deltaTime;
            purchaseAge += deltaTime;
            showAge += deltaTime;
            chestAge += deltaTime;
            Follow(session.Progress.TierIndex, visible);

            TierLayout layout = Tiers[shownTier];
            Rect bounds = area.rect;
            var stage = new Vector2(bounds.width, Mathf.Max(200, bounds.height - stageTop - stageBottom));
            Vector2 artSize = ArtSize(layout, AspectOf(layout, art.sprite), stage);
            hullSize = Vector2.Scale(artSize, layout.Hull.size);
            Fit(art, layout, artSize);
            Fit(ghost, layout, artSize);
            Fit(halo, layout, artSize);

            // Hull centre on the middle line; the drawing as a whole centred in the creek.
            float centreY = stageBottom + stage.y / 2 - (0.5f - layout.Hull.center.y) * artSize.y;
            var sway = new Vector2(5 * Mathf.Sin(time * 0.55f), 6 * Mathf.Sin(time * 1.2f));
            root.anchoredPosition = new Vector2(bounds.width / 2, centreY) + sway;
            root.localRotation = Quaternion.Euler(0, 0, 1.1f * Mathf.Sin(time * 0.8f) + 0.4f * Mathf.Sin(time * 1.9f));

            float grow = showAge < GrowSeconds ? Mathf.LerpUnclamped(0.8f, 1, Motion.EaseOutBack(showAge / GrowSeconds)) : 1;
            float scale = grow * Motion.Punch(purchaseAge, PurchaseSeconds, PurchasePeak);
            // A slight roll: the hull narrows a touch as it leans.
            root.localScale = new Vector3(scale * (1 + 0.01f * Mathf.Sin(time * 1.6f)), scale, 1);

            chest.localScale = Vector3.one * Motion.Punch(chestAge, ChestSeconds, ChestPeak);
            PlaceFoam(deltaTime, currentSpeed, travel);
            PlaceShow(deltaTime);
            parts.Tick(deltaTime);
        }

        /// <summary>Caught gold reached the chest: it hops and throws sparkles.</summary>
        public void ChestHit()
        {
            chestAge = 0;
            particles.Burst(ChestPoint, 6, 260, 34, 0.45f, Palette.Glint);
        }

        void Follow(int tier, bool visible)
        {
            int bought = 0;
            foreach (int level in session.Progress.UpgradeLevels)
                bought += level;
            tier = Mathf.Clamp(tier, 0, Tiers.Length - 1);
            if (tier != shownTier)
            {
                bool first = shownTier < 0;
                bool up = !first && tier > shownTier;
                // Bought behind the Upgrades panel: the show waits until the creek is in view.
                if (!up || visible)
                {
                    Show(tier);
                    // Up starts the show; a rebirth ends any show still playing.
                    showAge = up ? 0 : ShowSeconds;
                    if (up)
                        TierArrived?.Invoke(tier);
                }
            }
            else if (boughtLevels >= 0 && bought > boughtLevels)
            {
                purchaseAge = 0;
            }
            boughtLevels = bought;
        }

        void Show(int tier)
        {
            shownTier = tier;
            TierLayout layout = Tiers[tier];
            Sprite sprite = Art.Dredge(tier);
            art.sprite = sprite;
            art.color = sprite != null ? Color.white : Color.clear;
            Sprite silhouette = Art.DredgeGhost(tier);
            ghost.sprite = halo.sprite = silhouette;

            greyHull.gameObject.SetActive(sprite == null);
            greyHull.rectTransform.anchorMin = layout.Hull.min;
            greyHull.rectTransform.anchorMax = layout.Hull.max;
            greyHull.rectTransform.offsetMin = greyHull.rectTransform.offsetMax = Vector2.zero;

            // The chest hop lifts a round patch of the drawing itself, cut from the same texture.
            if (chestSprite != null)
                Destroy(chestSprite);
            Rect patch = ChestPatch(layout, sprite);
            chestSprite = sprite != null
                ? Sprite.Create(sprite.texture, new Rect(sprite.rect.position + Vector2.Scale(patch.position, sprite.rect.size),
                    Vector2.Scale(patch.size, sprite.rect.size)), new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect)
                : null;
            chestPatch.sprite = chestSprite;
            chestPatch.color = chestSprite != null ? Color.white : Palette.Gold;
            chest.anchorMin = patch.min;
            chest.anchorMax = patch.max;
            chest.offsetMin = chest.offsetMax = Vector2.zero;
            chest.pivot = new Vector2(0.5f, 0.5f);

            parts.Show(tier, sprite != null);
        }

        /// <summary>A square around the chest's gold, as fractions of the drawing, kept inside it.</summary>
        static Rect ChestPatch(TierLayout layout, Sprite sprite)
        {
            float aspect = AspectOf(layout, sprite);
            // Side as a share of the drawing width; the same length is a smaller share of its height.
            float side = Mathf.Max(layout.Chest.width, layout.Chest.height * aspect) * 1.35f;
            var size = new Vector2(side, side / aspect);
            var patch = new Rect(layout.Chest.center - size / 2, size);
            patch.xMin = Mathf.Max(patch.xMin, 0);
            patch.yMin = Mathf.Max(patch.yMin, 0);
            patch.xMax = Mathf.Min(patch.xMax, 1);
            patch.yMax = Mathf.Min(patch.yMax, 1);
            return patch;
        }

        static float AspectOf(TierLayout layout, Sprite sprite) =>
            sprite != null ? sprite.rect.height / sprite.rect.width : layout.Aspect;

        static Vector2 ArtSize(TierLayout layout, float aspect, Vector2 areaSize)
        {
            float width = layout.HullShare * areaSize.x / layout.Hull.width;
            float height = width * aspect;
            float maxHeight = MaxHeightShare * areaSize.y;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height / aspect;
            }
            return new Vector2(width, height);
        }

        /// <summary>
        /// The water the dredge holds back: a pale strip widening behind the stern, foam churned up
        /// at the stern that the current carries off, two V arms of foam from the stern corners and
        /// a little foam slipping past the hull sides. No glow at the bow.
        /// </summary>
        /// <param name="travel">Under way there is more foam and the strip brightens.</param>
        void PlaceFoam(float deltaTime, float current, float travel)
        {
            float halfWidth = hullSize.x / 2;
            float halfHeight = hullSize.y / 2;
            float flow = current * (1 + 2 * travel);
            float more = 1 + 2 * travel;

            Vector2 stern = ToArea(root.TransformPoint(new Vector3(0, -halfHeight + 6)));
            wakePhase = Mathf.Repeat(wakePhase + flow * deltaTime / 260, 1);
            wakeStrip.color = Palette.WakeStrip * new Color(1, 1, 1, 1 + 0.6f * travel);
            wakeStrip.Place(stern, hullSize.x * 0.8f, hullSize.x * 2.1f, stern.y + 60, wakePhase);

            foam.Tick(deltaTime);
            if (deltaTime <= 0)
                return;

            // The churn: big soft white blobs packed at the stern, so they run together into one
            // boiling mass that thins out as the current takes it.
            sternFoamDue += deltaTime * SternFoamRate * more;
            for (; sternFoamDue >= 1; sternFoamDue--)
            {
                float across = Random.Range(-0.4f, 0.4f);
                Vector2 at = ToArea(root.TransformPoint(new Vector3(across * hullSize.x, -halfHeight + Random.Range(-6f, 14f))));
                var velocity = new Vector2(across * Random.Range(30f, 80f), -flow * Random.Range(0.45f, 0.8f));
                bool soft = Random.value < 0.75f;
                foam.Emit(at, velocity, soft ? Random.Range(56f, 96f) : Random.Range(16f, 28f), Random.Range(0.7f, 1.2f),
                    Foam(0.75f, 1), soft ? Ui.Glow : Ui.Circle, 0, 0, 0.5f);
            }

            // Foam streaks drawn out along the current in the churned strip.
            streakDue += deltaTime * StreakRate * more;
            for (; streakDue >= 1; streakDue--)
            {
                Vector2 at = ToArea(root.TransformPoint(new Vector3(Random.Range(-0.34f, 0.34f) * hullSize.x, -halfHeight - 10)));
                foam.Emit(at, new Vector2(0, -flow * Random.Range(0.8f, 0.95f)), Random.Range(10f, 16f), Random.Range(1.8f, 2.6f),
                    Foam(0.35f, 0.55f), Ui.Glow, 0, 0, 1.25f, Random.Range(3f, 5f), 0);
            }

            // Two V arms of foam dashes from the stern corners, each lying along its arm.
            armFoamDue += deltaTime * ArmFoamRate * more;
            for (; armFoamDue >= 1; armFoamDue--)
            {
                for (int arm = 0; arm < 2; arm++)
                {
                    float sign = arm == 0 ? -1 : 1;
                    var direction = new Vector2(sign * Mathf.Sin(ArmAngle * Mathf.Deg2Rad), -Mathf.Cos(ArmAngle * Mathf.Deg2Rad));
                    Vector2 corner = ToArea(root.TransformPoint(new Vector3(sign * halfWidth * 0.82f, -halfHeight + 12)));
                    foam.Emit(corner + direction * Random.Range(0f, 14f), direction * flow * Random.Range(0.9f, 1.05f),
                        Random.Range(11f, 17f), Random.Range(2f, 2.8f), Foam(0.55f, 0.85f), Ui.Glow, 0, 0, 1.6f,
                        Random.Range(2.2f, 3.2f), sign * ArmAngle);
                }
            }

            // Water slipping past the hull sides in thin streaks.
            sideFoamDue += deltaTime * SideFoamRate * more;
            for (; sideFoamDue >= 1; sideFoamDue--)
            {
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1 : 1;
                    Vector2 at = ToArea(root.TransformPoint(new Vector3(sign * (halfWidth + 5), Random.Range(-halfHeight, halfHeight * 0.7f))));
                    foam.Emit(at, new Vector2(sign * 10, -flow), Random.Range(7f, 11f), Random.Range(0.8f, 1.2f),
                        Foam(0.4f, 0.65f), Ui.Glow, 0, 0, 1.2f, Random.Range(2.5f, 3.5f), 0);
                }
            }
        }

        static Color Foam(float minAlpha, float maxAlpha)
        {
            Color color = Palette.Foam;
            color.a = Random.Range(minAlpha, maxAlpha);
            return color;
        }

        /// <summary>
        /// The tier-up show: the golden glow and the silhouette falling from twice the size, then
        /// on landing a wave over the water, a light wash and sparkles.
        /// </summary>
        void PlaceShow(float deltaTime)
        {
            float k = Mathf.Clamp01(showAge / FlashSeconds);
            flash.gameObject.SetActive(k < 1);
            if (k < 1)
                Set(flash, Vector2.zero, Vector2.one * (Mathf.Max(hullSize.x, hullSize.y) * (1.3f + 0.7f * k)), 0.9f * (1 - k) * (1 - k));

            float fall = showAge / LandSeconds;
            ghost.gameObject.SetActive(fall < 1 && ghost.sprite != null);
            if (fall < 1)
            {
                ghost.rectTransform.localScale = Vector3.one * Mathf.Lerp(2, 1, fall * fall);
                ghost.color = new Color(1, 1, 1, 0.8f * Mathf.Clamp01(fall * 8) * (1 - fall));
            }
            bool landed = showAge >= LandSeconds && showAge - deltaTime < LandSeconds;
            if (landed)
            {
                Haptics.Important();
                particles.Burst(ToArea(root.position), 16, 520, 64, 0.8f, Palette.Glint);
            }

            float glow = (showAge - HaloStart) / HaloSeconds;
            halo.gameObject.SetActive(glow >= 0 && glow < 1 && halo.sprite != null);
            if (glow >= 0 && glow < 1)
            {
                halo.rectTransform.localScale = Vector3.one * (1.03f + 0.06f * glow);
                halo.color = new Color(Palette.Glint.r, Palette.Glint.g, Palette.Glint.b, 0.7f * (1 - glow) * (1 - glow));
            }

            float spread = (showAge - LandSeconds) / WaveSeconds;
            wave.gameObject.SetActive(spread >= 0 && spread < 1);
            if (spread >= 0 && spread < 1)
            {
                Rect bounds = area.rect;
                float reach = Mathf.Sqrt(bounds.width * bounds.width + bounds.height * bounds.height) * 2.2f;
                float eased = 1 - (1 - spread) * (1 - spread);
                Set(wave, ToArea(root.position), Vector2.one * Mathf.Lerp(Mathf.Max(hullSize.x, hullSize.y), reach, eased), 0.6f * (1 - spread));
            }

            float light = (showAge - LandSeconds) / WashSeconds;
            wash.gameObject.SetActive(light >= 0 && light < 1);
            if (light >= 0 && light < 1)
                wash.color = new Color(Palette.Wash.r, Palette.Wash.g, Palette.Wash.b, Palette.Wash.a * (1 - light));
        }

        /// <summary>The drawing and its silhouettes share one box, pivoting on the hull's centre.</summary>
        static void Fit(Image drawing, TierLayout layout, Vector2 size)
        {
            drawing.rectTransform.pivot = layout.Hull.center;
            drawing.rectTransform.sizeDelta = size;
            drawing.rectTransform.anchoredPosition = Vector2.zero;
        }

        static void Set(Image image, Vector2 position, Vector2 size, float alpha)
        {
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

        Vector2 ToArea(Vector3 world) => (Vector2)area.InverseTransformPoint(world) - area.rect.min;

        Rect AreaBounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = ToArea(corners[0]);
            Vector2 max = min;
            for (int i = 1; i < 4; i++)
            {
                Vector2 p = ToArea(corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
