using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// A nugget's stars as a row of star pictures, earned ones lit and the rest faint. Without
    /// the star art the stars are gold dots. The row is <see cref="Width"/> wide and starts at
    /// its left edge.
    /// </summary>
    public sealed class StarRow : MonoBehaviour
    {
        const float Gap = 6;

        Image[] stars;
        Color lit;
        float alpha = 1;

        public int Filled { get; private set; }

        public float Width { get; private set; }

        public static StarRow Create(string name, Transform parent, int max, float size)
        {
            RectTransform rt = Ui.Rect(name, parent);
            var row = rt.gameObject.AddComponent<StarRow>();
            Sprite sprite = Art.Icon("star");
            row.lit = sprite != null ? Color.white : Palette.Gold;
            row.stars = new Image[max];
            for (int i = 0; i < max; i++)
            {
                row.stars[i] = Ui.Icon("Star", rt, sprite, Palette.Gold);
                row.stars[i].rectTransform.Box(new Vector2(0, 0.5f), new Vector2(size, size), new Vector2(i * (size + Gap), 0));
            }
            row.Width = max * size + (max - 1) * Gap;
            rt.sizeDelta = new Vector2(row.Width, size);
            row.Set(0);
            return row;
        }

        public void Set(int filled)
        {
            Filled = filled;
            Paint();
        }

        /// <summary>Fades the whole row, for a popup that floats away.</summary>
        public void Fade(float value)
        {
            alpha = value;
            Paint();
        }

        void Paint()
        {
            for (int i = 0; i < stars.Length; i++)
            {
                Color color = i < Filled ? lit : Palette.IconOff;
                color.a *= alpha;
                stars[i].color = color;
            }
        }
    }
}
