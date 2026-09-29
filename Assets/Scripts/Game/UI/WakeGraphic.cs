using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The pale strip of churned water the current leaves behind the dredge: it widens from the
    /// stern to the bottom of the creek, is brightest at the stern and soft at its edges, and
    /// faint bands run down it with the water. Positions are in the layer's pixels from its
    /// bottom-left corner, like the gold.
    /// </summary>
    public sealed class WakeGraphic : MaskableGraphic
    {
        const int Rows = 28;
        /// <summary>Bands along the strip; they run down with the water.</summary>
        const float Bands = 6;
        static readonly float[] Across = { -1, -0.62f, -0.2f, 0.2f, 0.62f, 1 };
        static readonly float[] Strength = { 0, 0.85f, 1, 1, 0.85f, 0 };

        Vector2 stern;
        float sternWidth;
        float endWidth;
        float length;
        float phase;

        public void Place(Vector2 sternPoint, float widthAtStern, float widthAtEnd, float stripLength, float bandPhase)
        {
            stern = sternPoint;
            sternWidth = widthAtStern;
            endWidth = widthAtEnd;
            length = stripLength;
            phase = bandPhase;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (length <= 0)
                return;
            // The rect's pivot is its bottom-left corner, so local space is the layer's pixels.
            int columns = Across.Length;
            for (int row = 0; row <= Rows; row++)
            {
                float k = row / (float)Rows;
                float half = Mathf.Lerp(sternWidth, endWidth, Mathf.Sqrt(k)) / 2;
                float y = stern.y - k * length;
                // Churned white right at the stern, thinning out down the creek.
                float fade = Mathf.Lerp(1, 0.25f, k) * Mathf.Clamp01(k * 12 + 0.35f);
                float band = 0.8f + 0.2f * Mathf.Sin((k * Bands - phase) * 2 * Mathf.PI);
                for (int column = 0; column < columns; column++)
                {
                    Color c = color;
                    c.a *= Strength[column] * fade * band;
                    vh.AddVert(new Vector3(stern.x + Across[column] * half, y), c, Vector4.zero);
                }
            }
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < columns - 1; column++)
                {
                    int a = row * columns + column;
                    int b = a + columns;
                    vh.AddTriangle(a, a + 1, b + 1);
                    vh.AddTriangle(a, b + 1, b);
                }
            }
        }
    }
}
