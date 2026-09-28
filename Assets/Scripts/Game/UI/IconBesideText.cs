using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Keeps an icon just left of a centred label's text while the text changes length, so a
    /// counter and its currency icon read as one group. The icon is a child of the label.
    /// With <see cref="CenterGroup"/> the label also moves right by half the icon, so icon and
    /// text together sit in the middle; hiding the icon puts the label back.
    /// With a <see cref="Lead"/> ("Hire" plus room, from <see cref="MakeLead"/>) the icon sits
    /// inside the text instead, between the lead word and the price.
    /// </summary>
    public class IconBesideText : MonoBehaviour
    {
        public Text Label;
        public float Gap = 12;
        public bool CenterGroup;

        string lead;
        string lastText;
        float lastWidth = -1;
        Vector2 labelHome;
        bool homeKnown;

        /// <summary>Text before the icon, spaces included; null puts the icon before the whole text.</summary>
        public string Lead
        {
            get => lead;
            set
            {
                if (lead == value)
                    return;
                lead = value;
                lastText = null;
            }
        }

        public static IconBesideText Attach(Text label, Sprite sprite, Color fallback, float size, bool centerGroup = false)
        {
            Image icon = Ui.Icon("Icon", label.transform, sprite, fallback);
            RectTransform rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var follow = icon.gameObject.AddComponent<IconBesideText>();
            follow.Label = label;
            follow.CenterGroup = centerGroup;
            return follow;
        }

        /// <summary>The word followed by enough spaces to hold the icon, for <see cref="Lead"/>.</summary>
        public string MakeLead(string word)
        {
            float room = ((RectTransform)transform).sizeDelta.x + 2 * Gap;
            string text = word + " ";
            while (WidthBetween(word, text) < room)
                text += " ";
            return text;
        }

        void OnEnable() => lastText = null;

        void OnDisable()
        {
            if (homeKnown && Label != null)
                Label.rectTransform.anchoredPosition = labelHome;
        }

        void LateUpdate()
        {
            if (!homeKnown)
            {
                labelHome = Label.rectTransform.anchoredPosition;
                homeKnown = true;
            }
            float width = Label.rectTransform.rect.width;
            if (Label.text == lastText && Mathf.Approximately(width, lastWidth))
                return;
            lastText = Label.text;
            lastWidth = width;

            var rt = (RectTransform)transform;
            // Best fit shrinks text wider than the box, so the drawn text is at most the box.
            float textWidth = Mathf.Min(Label.preferredWidth, width);
            bool inside = !string.IsNullOrEmpty(lead) && Label.text.StartsWith(lead);
            float shift = CenterGroup && !inside ? (rt.sizeDelta.x + Gap) / 2 : 0;
            Label.rectTransform.anchoredPosition = labelHome + new Vector2(shift, 0);
            if (inside)
            {
                string word = lead.TrimEnd();
                float scale = Label.preferredWidth > 0 ? textWidth / Label.preferredWidth : 1;
                float wordEnd = Width(word) * scale;
                float gapMiddle = wordEnd + WidthBetween(word, lead) * scale / 2;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(-textWidth / 2 + gapMiddle, 0);
            }
            else
            {
                rt.pivot = new Vector2(1, 0.5f);
                rt.anchoredPosition = new Vector2(-textWidth / 2 - Gap, 0);
            }
        }

        float Width(string text)
        {
            TextGenerationSettings settings = Label.GetGenerationSettings(Vector2.zero);
            return Label.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / Label.pixelsPerUnit;
        }

        /// <summary>Width that <paramref name="longer"/> adds after <paramref name="shorter"/>; a bar
        /// closes both so trailing spaces count.</summary>
        float WidthBetween(string shorter, string longer) => Width(longer + "|") - Width(shorter + "|");
    }
}
