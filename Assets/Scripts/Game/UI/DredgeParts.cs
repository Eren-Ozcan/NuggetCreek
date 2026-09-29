using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The moving pieces on the shown tier's drawing, as the sprite tool cut them (parts.json):
    /// bucket chains, belts and wheels that slide along, buckets and the sweep ring that dip into
    /// the water, smoke from the chimneys and drips from the spray pipes. Without the file the
    /// dredge simply stands still.
    /// </summary>
    public sealed class DredgeParts
    {
        const float DipEvery = 5.5f;
        const float DipDown = 0.6f;
        const float DipHold = 1.0f;
        const float DipUp = 0.7f;
        const float DipDepth = 0.78f;
        const float SmokeEvery = 0.22f;
        const float SprayEvery = 0.12f;

        sealed class Flow
        {
            public RawImage Image;
            public float Rate;
            public float Offset;
        }

        sealed class Dip
        {
            public Image Image;
            public float Phase;
            public bool Splashed;
        }

        readonly RectTransform art;
        readonly Particles particles;
        readonly Func<Vector3, Vector2> toArea;
        readonly List<GameObject> built = new List<GameObject>();
        readonly List<Flow> flows = new List<Flow>();
        readonly List<Dip> dips = new List<Dip>();
        Vector2[] smoke = new Vector2[0];
        Vector2[] spray = new Vector2[0];
        float smokeIn;
        float sprayIn;
        float time;

        public DredgeParts(RectTransform art, Particles particles, Func<Vector3, Vector2> toArea)
        {
            this.art = art;
            this.particles = particles;
            this.toArea = toArea;
        }

        /// <summary>Builds the pieces of a tier's drawing, or nothing without the drawing or its parts.</summary>
        public void Show(int tier, bool hasArt)
        {
            foreach (GameObject go in built)
                UnityEngine.Object.Destroy(go);
            built.Clear();
            flows.Clear();
            dips.Clear();
            DredgeTierParts entry = hasArt ? Art.DredgeParts(tier) : null;
            smoke = Points(entry?.smoke);
            spray = Points(entry?.spray);
            if (entry?.parts == null)
                return;

            foreach (DredgePart part in entry.parts)
            {
                Sprite sprite = Art.DredgePart(part.file);
                if (sprite == null)
                    continue;
                RectTransform rect;
                if (part.kind == "flow")
                {
                    var image = Ui.Rect(part.file, art).gameObject.AddComponent<RawImage>();
                    image.texture = sprite.texture;
                    image.raycastTarget = false;
                    image.uvRect = new Rect(0, 0, 1, part.repeat);
                    flows.Add(new Flow { Image = image, Rate = part.speed / sprite.texture.height });
                    rect = image.rectTransform;
                }
                else
                {
                    Image image = Ui.Image(part.file, art, Color.white, sprite);
                    image.raycastTarget = false;
                    dips.Add(new Dip { Image = image, Phase = UnityEngine.Random.Range(0, DipEvery) });
                    rect = image.rectTransform;
                }
                rect.anchorMin = new Vector2(part.x0, part.y0);
                rect.anchorMax = new Vector2(part.x1, part.y1);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                rect.pivot = new Vector2(part.pivotX, part.pivotY);
                built.Add(rect.gameObject);
            }
        }

        public void Tick(float deltaTime)
        {
            time += deltaTime;
            // Content moves down the screen, so the window climbs up the texture.
            foreach (Flow flow in flows)
            {
                flow.Offset = Mathf.Repeat(flow.Offset + flow.Rate * deltaTime, 1);
                Rect uv = flow.Image.uvRect;
                uv.y = flow.Offset;
                flow.Image.uvRect = uv;
            }
            foreach (Dip dip in dips)
                TickDip(dip, deltaTime);

            smokeIn -= deltaTime;
            if (smokeIn <= 0)
            {
                smokeIn = SmokeEvery;
                foreach (Vector2 point in smoke)
                {
                    // Puffs drift downstream and spread as they thin out.
                    var velocity = new Vector2(UnityEngine.Random.Range(8, 30), UnityEngine.Random.Range(-70, -45));
                    particles.Emit(AreaPoint(point), velocity, UnityEngine.Random.Range(50, 70), 2.4f, Palette.Smoke, Ui.Glow,
                        0, UnityEngine.Random.Range(-30f, 30f), 2.6f);
                }
            }
            sprayIn -= deltaTime;
            if (sprayIn <= 0)
            {
                sprayIn = SprayEvery;
                foreach (Vector2 point in spray)
                {
                    var velocity = new Vector2(UnityEngine.Random.Range(-25, 25), -60);
                    particles.Emit(AreaPoint(point), velocity, UnityEngine.Random.Range(7, 11), 0.35f, Palette.Droplet, Ui.Circle, -300);
                }
            }
        }

        /// <summary>Hangs and sways, sinks into the water with a splash, waits, and comes up dripping.</summary>
        void TickDip(Dip dip, float deltaTime)
        {
            dip.Phase = Mathf.Repeat(dip.Phase + deltaTime, DipEvery);
            float t = dip.Phase;
            float depth;
            if (t < DipDown)
                depth = Mathf.SmoothStep(0, 1, t / DipDown);
            else if (t < DipDown + DipHold)
                depth = 1;
            else if (t < DipDown + DipHold + DipUp)
                depth = 1 - Mathf.SmoothStep(0, 1, (t - DipDown - DipHold) / DipUp);
            else
                depth = 0;

            RectTransform rect = dip.Image.rectTransform;
            rect.localScale = Vector3.one * Mathf.Lerp(1, DipDepth, depth);
            rect.localRotation = Quaternion.Euler(0, 0, (1 - depth) * 3 * Mathf.Sin(time * 1.4f + dip.Phase));
            dip.Image.color = Color.Lerp(Color.white, Palette.Underwater, depth);

            if (t < DipDown)
                dip.Splashed = false;
            else if (!dip.Splashed)
            {
                dip.Splashed = true;
                Vector2 centre = CentreOf(rect);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 velocity = UnityEngine.Random.insideUnitCircle * 150 + new Vector2(0, 160);
                    particles.Emit(centre, velocity, UnityEngine.Random.Range(12, 20), 0.45f, Palette.Droplet, Ui.Circle, -800);
                }
            }
            bool rising = t > DipDown + DipHold && t < DipDown + DipHold + DipUp + 0.4f;
            if (rising && UnityEngine.Random.value < deltaTime * 12)
            {
                Vector2 drip = CentreOf(rect) + UnityEngine.Random.insideUnitCircle * (rect.rect.width * 0.25f);
                particles.Emit(drip, new Vector2(0, -40), UnityEngine.Random.Range(8, 12), 0.35f, Palette.Droplet, Ui.Circle, -500);
            }
        }

        Vector2 CentreOf(RectTransform rect) => toArea(rect.TransformPoint(rect.rect.center));

        /// <summary>A point on the drawing, as fractions from its bottom-left corner, in creek-area pixels.</summary>
        Vector2 AreaPoint(Vector2 fraction)
        {
            Rect r = art.rect;
            return toArea(art.TransformPoint(new Vector3(r.x + fraction.x * r.width, r.y + fraction.y * r.height)));
        }

        static Vector2[] Points(float[] pairs)
        {
            if (pairs == null)
                return new Vector2[0];
            var points = new Vector2[pairs.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(pairs[i * 2], pairs[i * 2 + 1]);
            return points;
        }
    }
}
