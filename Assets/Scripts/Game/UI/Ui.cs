using System;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>Greybox palette. Final art replaces every colour here.</summary>
    public static class Palette
    {
        public static readonly Color Background = new Color32(0x2B, 0x3A, 0x2F, 0xFF);
        public static readonly Color Water = new Color32(0x2E, 0x6F, 0x8E, 0xFF);
        public static readonly Color Bar = new Color32(0x1E, 0x24, 0x20, 0xF2);
        public static readonly Color Panel = new Color32(0x26, 0x2C, 0x28, 0xFF);
        public static readonly Color Row = new Color32(0x33, 0x3B, 0x36, 0xFF);
        public static readonly Color Dim = new Color(0, 0, 0, 0.7f);
        public static readonly Color Text = new Color32(0xF4, 0xEE, 0xDC, 0xFF);
        public static readonly Color TextMuted = new Color32(0xB5, 0xAE, 0x9A, 0xFF);
        public static readonly Color Gold = new Color32(0xFF, 0xD5, 0x4A, 0xFF);
        public static readonly Color Nugget = new Color32(0xE8, 0x93, 0x2D, 0xFF);
        public static readonly Color RichNugget = new Color32(0xFF, 0x6A, 0x3D, 0xFF);
        public static readonly Color GiantNugget = new Color32(0xFF, 0xF2, 0xA8, 0xFF);
        public static readonly Color Critical = new Color32(0xFF, 0x4F, 0x6E, 0xFF);
        public static readonly Color Button = new Color32(0x3F, 0x8F, 0x4E, 0xFF);
        public static readonly Color ButtonAlt = new Color32(0x4A, 0x55, 0x4E, 0xFF);
        public static readonly Color Ad = new Color32(0x7A, 0x4F, 0xC2, 0xFF);
        public static readonly Color Amos = new Color32(0x8D, 0x6E, 0x4C, 0xFF);
        public static readonly Color Gem = new Color32(0x4F, 0xD1, 0x9A, 0xFF);
        public static readonly Color GemButton = new Color32(0x2A, 0x8A, 0x6A, 0xFF);
    }

    /// <summary>Code-built uGUI helpers for the greybox. Sizes are in 1080x1920 reference pixels.</summary>
    public static class Ui
    {
        static Font font;
        static Sprite circle;

        public static Font Font => font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Sprite Circle => circle ??= CreateCircle(128);

        /// <summary>
        /// System font scale the labels follow, 1 to 1.3 (design doc 14.3). Set before the UI
        /// is built; text grows only where its box has room, so layouts never break.
        /// </summary>
        public static float TextScale { get; set; } = 1;

        /// <summary>
        /// Smallest control height in reference units: 48 dp on a 1080 x 2340 phone, the most
        /// common shape (Android's minimum touch target).
        /// </summary>
        public const float TapHeight = 120;

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Anchors to a fraction of the parent with pixel insets.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float inset = 0) =>
            rt.Place(Vector2.zero, Vector2.one, new Vector2(inset, inset), new Vector2(-inset, -inset));

        /// <summary>Fixed size box anchored at a point of the parent.</summary>
        public static RectTransform Box(this RectTransform rt, Vector2 anchor, Vector2 size, Vector2 position = default)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return rt;
        }

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null)
        {
            RectTransform rt = Rect(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            return image;
        }

        /// <summary>
        /// Sprite icon that keeps its aspect and ignores taps. Without the sprite (art not
        /// fetched) it is a plain circle in the fallback colour, or nothing when there is none.
        /// </summary>
        public static Image Icon(string name, Transform parent, Sprite sprite, Color? fallback = null)
        {
            if (sprite == null && fallback == null)
                return null;
            Image image = Image(name, parent, sprite != null ? Color.white : fallback.Value, sprite != null ? sprite : Circle);
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Left inset of a list row's text when the row has no picture.</summary>
        public const float RowTextInset = 28;

        /// <summary>
        /// Square picture on the left of a list row, as tall as the row minus a margin. Returns
        /// the left inset for the row's text, which stays at <see cref="RowTextInset"/> when the
        /// art is missing. <paramref name="cover"/> crops a non-square picture to fill the square.
        /// </summary>
        public static float RowPicture(RectTransform row, float rowHeight, Sprite sprite, out Image picture, bool cover = false)
        {
            picture = null;
            if (sprite == null)
                return RowTextInset;
            const float margin = 18;
            float size = rowHeight - 2 * margin;
            RectTransform slot = Rect("Picture", row).Box(new Vector2(0, 0.5f), new Vector2(size, size), new Vector2(margin, 0));
            if (cover)
            {
                slot.gameObject.AddComponent<RectMask2D>();
                picture = Image("Image", slot, Color.white, sprite);
                picture.rectTransform.anchorMin = Vector2.zero;
                picture.rectTransform.anchorMax = Vector2.one;
                var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
            else
            {
                picture = slot.gameObject.AddComponent<Image>();
                picture.sprite = sprite;
                picture.preserveAspect = true;
            }
            picture.raycastTarget = false;
            return margin + size + 22;
        }

        /// <summary>
        /// Currency icon on a price button. The icon and the price are centred together; hide
        /// the icon while the button shows a word such as MAX.
        /// </summary>
        public static IconBesideText PriceIcon(Text label, Sprite sprite, Color fallback) =>
            IconBesideText.Attach(label, sprite, fallback, Mathf.Round(label.fontSize * 1.25f), centerGroup: true);

        public static Text Label(string name, Transform parent, string text, int size,
            TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Rect(name, parent);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color ?? Palette.Text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            if (TextScale > 1)
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = size;
                label.resizeTextMaxSize = Mathf.RoundToInt(size * TextScale);
            }
            return label;
        }

        public static Button Button(string name, Transform parent, string text, Color color, Action onClick, out Text label, int fontSize = 40)
        {
            Image background = Image(name, parent, color);
            var button = background.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            label = Label("Label", background.transform, text, fontSize, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            label.rectTransform.Fill(8);
            return button;
        }

        public static void SetText(this Text label, string text)
        {
            if (label.text != text)
                label.text = text;
        }

        public static void SetActive(this Component component, bool active)
        {
            if (component.gameObject.activeSelf != active)
                component.gameObject.SetActive(active);
        }

        /// <summary>Vertical list that sizes to its children (for scroll content).</summary>
        public static VerticalLayoutGroup VerticalList(RectTransform rt, float spacing, int padding)
        {
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return layout;
        }

        public static void PreferredHeight(Component component, float height)
        {
            if (!component.TryGetComponent(out LayoutElement element))
                element = component.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        /// <summary>Scroll view filling its parent; returns the content rect to add rows to.</summary>
        public static RectTransform ScrollList(Transform parent, float spacing, int padding)
        {
            RectTransform viewport = Rect("Scroll", parent).Fill();
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            RectTransform content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VerticalList(content, spacing, padding);

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        static Sprite CreateCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float radius = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                byte alpha = (byte)(Mathf.Clamp01(radius - distance) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }
    }

    public static class TextFormat
    {
        public static string Duration(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalHours >= 1)
                return span.Minutes > 0 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{(int)span.TotalHours}h";
            if (span.TotalMinutes >= 1)
                return $"{span.Minutes}m";
            return $"{span.Seconds}s";
        }
    }
}
