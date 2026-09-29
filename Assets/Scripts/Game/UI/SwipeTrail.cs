using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The bright streak a swipe leaves on the water: a ribbon through the finger's last points,
    /// thick at the finger and thin at its tail, with a soft glow around it. Points fade in a
    /// fraction of a second, so the streak follows the finger and dies away when it lifts.
    /// Positions are in the layer's pixels from its bottom-left corner, like the gold.
    /// </summary>
    public sealed class SwipeTrail : MaskableGraphic
    {
        const float PointLife = 0.16f;
        const float CoreWidth = 22;
        const float GlowWidth = 58;
        const int MaxPoints = 40;

        struct TrailPoint
        {
            public Vector2 Position;
            public float Age;
            public int Stroke;
        }

        readonly List<TrailPoint> points = new List<TrailPoint>();
        int stroke;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Palette.Trail;
            // Stretched anchors keep the rect where it is; local space becomes the layer's pixels.
            rectTransform.pivot = Vector2.zero;
        }

        /// <param name="newStroke">The finger just touched down: do not join this point to the last stroke.</param>
        public void Add(Vector2 point, bool newStroke)
        {
            if (newStroke)
                stroke++;
            if (points.Count > 0)
            {
                TrailPoint last = points[points.Count - 1];
                if (last.Stroke == stroke && (last.Position - point).sqrMagnitude < 9)
                    return;
            }
            points.Add(new TrailPoint { Position = point, Stroke = stroke });
            if (points.Count > MaxPoints)
                points.RemoveAt(0);
            SetVerticesDirty();
        }

        public void Tick(float deltaTime)
        {
            if (points.Count == 0)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                TrailPoint p = points[i];
                p.Age += deltaTime;
                points[i] = p;
            }
            while (points.Count > 0 && points[0].Age >= PointLife)
                points.RemoveAt(0);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int runStart = 0;
            for (int i = 1; i <= points.Count; i++)
            {
                if (i < points.Count && points[i].Stroke == points[runStart].Stroke)
                    continue;
                if (i - runStart >= 2)
                {
                    Ribbon(vh, runStart, i, GlowWidth, 0.3f);
                    Ribbon(vh, runStart, i, CoreWidth, 1);
                }
                runStart = i;
            }
        }

        /// <summary>A strip through points [from, to): the oldest end tapers to nothing.</summary>
        void Ribbon(VertexHelper vh, int from, int to, float width, float strength)
        {
            int first = vh.currentVertCount;
            int count = to - from;
            for (int i = from; i < to; i++)
            {
                Vector2 before = points[Mathf.Max(i - 1, from)].Position;
                Vector2 after = points[Mathf.Min(i + 1, to - 1)].Position;
                Vector2 along = after - before;
                Vector2 normal = along.sqrMagnitude > 0.0001f ? new Vector2(-along.y, along.x).normalized : Vector2.up;
                float life = 1 - points[i].Age / PointLife;
                float taper = count > 1 ? (i - from) / (float)(count - 1) : 1;
                float half = width * 0.5f * Mathf.Lerp(0.15f, 1, taper) * Mathf.Lerp(0.4f, 1, life);
                Color c = color;
                c.a *= strength * life;
                Vector2 p = points[i].Position;
                vh.AddVert(p + normal * half, c, Vector4.zero);
                vh.AddVert(p - normal * half, c, Vector4.zero);
            }
            for (int i = 0; i < count - 1; i++)
            {
                int a = first + i * 2;
                vh.AddTriangle(a, a + 1, a + 3);
                vh.AddTriangle(a, a + 3, a + 2);
            }
        }
    }
}
