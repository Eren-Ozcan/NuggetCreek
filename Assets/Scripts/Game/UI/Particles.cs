using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Short-lived sprites for glints, droplets and sparkle bursts on the creek. Positions are in
    /// the layer's pixels from its bottom-left corner, like the gold. Finished particles are
    /// hidden and reused, so a busy swipe does not allocate.
    /// </summary>
    public sealed class Particles
    {
        sealed class Particle
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Gravity;
            public float Spin;
            public float Size;
            public float Age;
            public float Life;
            public float EndScale;
            /// <summary>Height over width: above 1 for streaks drawn out along their angle.</summary>
            public float Stretch;
            public Color Color;
        }

        readonly RectTransform layer;
        readonly List<Particle> live = new List<Particle>();
        readonly Stack<Particle> spare = new Stack<Particle>();

        public Particles(RectTransform layer)
        {
            this.layer = layer;
        }

        /// <summary>
        /// One particle; it ends at <paramref name="endScale"/> times its size, below 1 for sparks that
        /// shrink away and above 1 for smoke that spreads. A <paramref name="stretch"/> above 1 draws it
        /// out into a streak along <paramref name="angle"/> (degrees, 0 = up); without an angle it
        /// lands at a random turn.
        /// </summary>
        public void Emit(Vector2 position, Vector2 velocity, float size, float life, Color color, Sprite sprite,
            float gravity = 0, float spin = 0, float endScale = 0.35f, float stretch = 1, float angle = float.NaN)
        {
            Particle p = spare.Count > 0 ? spare.Pop() : Create();
            p.Rect.gameObject.SetActive(true);
            p.Rect.SetAsLastSibling();
            p.Rect.anchoredPosition = position;
            p.Rect.localRotation = Quaternion.Euler(0, 0, float.IsNaN(angle) ? Random.Range(0, 90f) : angle);
            p.Image.sprite = sprite;
            p.Velocity = velocity;
            p.Gravity = gravity;
            p.Spin = spin;
            p.Size = size;
            p.Age = 0;
            p.Life = life;
            p.EndScale = endScale;
            p.Stretch = stretch;
            p.Color = color;
            Place(p);
            live.Add(p);
        }

        /// <summary>A ring of sparkles thrown out from a point.</summary>
        public void Burst(Vector2 position, int count, float speed, float size, float life, Color color)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (i + Random.value * 0.6f) / count * Mathf.PI * 2;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Emit(position + direction * size * 0.3f, direction * speed * Random.Range(0.6f, 1.2f),
                    size * Random.Range(0.7f, 1.2f), life * Random.Range(0.8f, 1.2f), color, Ui.Sparkle,
                    -speed * 0.8f, Random.Range(-180f, 180f));
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Particle p = live[i];
                p.Age += deltaTime;
                if (p.Age >= p.Life)
                {
                    p.Rect.gameObject.SetActive(false);
                    spare.Push(p);
                    live.RemoveAt(i);
                    continue;
                }
                p.Velocity.y += p.Gravity * deltaTime;
                p.Rect.anchoredPosition += p.Velocity * deltaTime;
                p.Rect.localRotation *= Quaternion.Euler(0, 0, p.Spin * deltaTime);
                Place(p);
            }
        }

        /// <summary>Pops in quickly, then changes size toward its end scale and fades over the last part of its life.</summary>
        static void Place(Particle p)
        {
            float k = p.Age / p.Life;
            float grow = Mathf.Clamp01(k / 0.15f);
            float scale = grow * Mathf.Lerp(1, p.EndScale, Mathf.Clamp01((k - 0.3f) / 0.7f));
            p.Rect.sizeDelta = new Vector2(p.Size * scale, p.Size * scale * p.Stretch);
            Color color = p.Color;
            color.a *= Mathf.Clamp01((1 - k) / 0.4f);
            p.Image.color = color;
        }

        Particle Create()
        {
            Image image = Ui.Image("Particle", layer, Color.white);
            image.raycastTarget = false;
            image.rectTransform.Box(Vector2.zero, Vector2.zero);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return new Particle { Rect = image.rectTransform, Image = image };
        }
    }
}
