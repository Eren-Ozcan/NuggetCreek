using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Keeps an icon just left of a centred label's text while the text changes length, so a
    /// counter and its currency icon read as one group. The icon is a child of the label.
    /// </summary>
    public class IconBesideText : MonoBehaviour
    {
        public Text Label;
        public float Gap = 12;

        string lastText;
        float lastWidth = -1;

        public static IconBesideText Attach(Text label, Sprite sprite, Color fallback, float size)
        {
            Image icon = Ui.Icon("Icon", label.transform, sprite, fallback);
            RectTransform rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var follow = icon.gameObject.AddComponent<IconBesideText>();
            follow.Label = label;
            return follow;
        }

        void LateUpdate()
        {
            float width = Label.rectTransform.rect.width;
            if (Label.text == lastText && Mathf.Approximately(width, lastWidth))
                return;
            lastText = Label.text;
            lastWidth = width;
            // Best fit shrinks text wider than the box, so the drawn text is at most the box.
            float textWidth = Mathf.Min(Label.preferredWidth, width);
            var rt = (RectTransform)transform;
            rt.anchoredPosition = new Vector2(-textWidth / 2 - Gap, 0);
        }
    }
}
