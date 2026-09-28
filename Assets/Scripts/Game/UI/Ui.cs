using System;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// 1950s cartoon palette (design doc 18, art direction): cream panels, rust / petrol / mustard
    /// buttons, dark brown text. <see cref="Text"/> is for cream surfaces; anything drawn on a
    /// button, the creek painting or a dimmed backdrop uses <see cref="TextLight"/>.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Background = new Color32(0x3B, 0x2A, 0x20, 0xFF);
        public static readonly Color Water = new Color32(0x2E, 0x6F, 0x8E, 0xFF);
        public static readonly Color Bar = new Color32(0xF4, 0xE9, 0xD0, 0xFF);
        public static readonly Color Panel = new Color32(0xF4, 0xE9, 0xD0, 0xFF);
        public static readonly Color Row = new Color32(0xE8, 0xD6, 0xAF, 0xFF);
        /// <summary>A row the panel points the player to.</summary>
        public static readonly Color Highlight = new Color32(0xF5, 0xC8, 0x62, 0xFF);
        public static readonly Color Dim = new Color(0.12f, 0.07f, 0.04f, 0.72f);
        public static readonly Color Text = new Color32(0x4A, 0x2E, 0x1F, 0xFF);
        public static readonly Color TextMuted = new Color32(0x86, 0x66, 0x4B, 0xFF);
        public static readonly Color TextLight = new Color32(0xFF, 0xF8, 0xE8, 0xFF);
        public static readonly Color Gold = new Color32(0xFF, 0xD5, 0x4A, 0xFF);
        /// <summary>Gold for amounts written on cream, where the bright gold would vanish.</summary>
        public static readonly Color GoldText = new Color32(0xB0, 0x6A, 0x12, 0xFF);
        public static readonly Color Nugget = new Color32(0xE8, 0x93, 0x2D, 0xFF);
        public static readonly Color RichNugget = new Color32(0xFF, 0x6A, 0x3D, 0xFF);
        public static readonly Color GiantNugget = new Color32(0xFF, 0xF2, 0xA8, 0xFF);
        public static readonly Color Critical = new Color32(0xFF, 0x4F, 0x6E, 0xFF);
        public static readonly Color Button = new Color32(0xC2, 0x52, 0x34, 0xFF);
        public static readonly Color ButtonAlt = new Color32(0x2F, 0x6E, 0x72, 0xFF);
        public static readonly Color Ad = new Color32(0x7B, 0x4B, 0x7E, 0xFF);
        public static readonly Color Amos = new Color32(0x8D, 0x5E, 0x3A, 0xFF);
        public static readonly Color Gem = new Color32(0x4F, 0xD1, 0x9A, 0xFF);
        /// <summary>Gem colour for numbers written on cream.</summary>
        public static readonly Color GemText = new Color32(0x1C, 0x7A, 0x66, 0xFF);
        public static readonly Color GemButton = new Color32(0x24, 0x86, 0x70, 0xFF);
        public static readonly Color Badge = new Color32(0xD8, 0x3A, 0x2A, 0xFF);
    }

    /// <summary>Code-built uGUI helpers for the greybox. Sizes are in 1080x1920 reference pixels.</summary>
    public static class Ui
    {
        static Font font, boldFont, headingFont;
        static Sprite circle, rounded, raised;

        /// <summary>Body font; the built-in one until Fonts/Resources is imported.</summary>
        public static Font Font => font ??= Resources.Load<Font>("Fonts/Body")
            ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static bool boldFontLoaded;

        /// <summary>Real bold cut of the body font, or null to let Unity embolden the regular.</summary>
        public static Font BoldFont
        {
            get
            {
                // Loaded once: a missing file must not cost a lookup per label.
                if (!boldFontLoaded)
                {
                    boldFont = Resources.Load<Font>("Fonts/BodyBold");
                    boldFontLoaded = true;
                }
                return boldFont;
            }
        }

        /// <summary>Period signboard serif for panel titles (design doc 18, art direction).</summary>
        public static Font HeadingFont => headingFont ??= Resources.Load<Font>("Fonts/Heading") ?? BoldFont ?? Font;

        public static Sprite Circle => circle ??= CreateCircle(128);

        /// <summary>Corner radius of panels, rows and buttons in reference units.</summary>
        public const int Radius = 26;

        /// <summary>Height of the darker lip under a button, which makes it look pressable.</summary>
        public const int Lip = 8;

        /// <summary>Nine-sliced rounded rectangle for cards and list rows.</summary>
        public static Sprite Rounded => rounded ??= CreateRounded(0);

        /// <summary>Rounded rectangle with a darker bottom lip, for buttons.</summary>
        public static Sprite Raised => raised ??= CreateRounded(Lip);

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

        /// <summary>Rounded card or list row. Keep it at least 2 x <see cref="Radius"/> tall.</summary>
        public static Image Panel(string name, Transform parent, Color color)
        {
            Image image = Image(name, parent, color, Rounded);
            image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        /// <summary>
        /// Red dot with "!" on the top right corner of a button: something there is ready.
        /// Hidden until the caller turns it on.
        /// </summary>
        public static Image Badge(Component button)
        {
            const float size = 54;
            Image dot = Image("Badge", button.transform, Palette.Badge, Circle);
            dot.rectTransform.Box(Vector2.one, new Vector2(size, size), new Vector2(10, 10));
            dot.raycastTarget = false;
            Text mark = Label("Mark", dot.transform, "!", 38, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            mark.rectTransform.Fill();
            mark.resizeTextForBestFit = false;
            dot.gameObject.SetActive(false);
            return dot;
        }

        /// <summary>Close button on the top right corner of a full-screen panel.</summary>
        public static Button CloseButton(RectTransform panel, Action onClose)
        {
            Button button = Button("Close", panel, "X", Palette.Button, onClose, out Text label);
            button.AsRect().Box(Vector2.one, new Vector2(TapHeight, TapHeight), new Vector2(-20, -15));
            Image icon = Icon("Icon", button.transform, Art.Icon("close"));
            if (icon != null)
            {
                icon.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(22, 22 + Lip), new Vector2(-22, -22));
                label.SetText("");
            }
            return button;
        }

        /// <summary>Panel or card title in the heading font.</summary>
        public static Text Title(string name, Transform parent, string text, int size, Color? color = null)
        {
            Text label = Label(name, parent, text, size, TextAnchor.MiddleCenter, color ?? Palette.Text);
            label.font = HeadingFont;
            return label;
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
            // A real bold cut reads better than Unity's smeared synthetic bold.
            bool realBold = style == FontStyle.Bold && BoldFont != null;
            label.font = realBold ? BoldFont : Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = realBold ? FontStyle.Normal : style;
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
            Image background = Image(name, parent, color, Raised);
            background.type = UnityEngine.UI.Image.Type.Sliced;
            var button = background.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.disabledColor = new Color(0.62f, 0.58f, 0.52f, 0.75f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            label = Label("Label", background.transform, text, fontSize, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            // Centred on the face, above the lip.
            label.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(8, 8 + Lip), new Vector2(-8, -8));
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

        /// <summary>
        /// White rounded rectangle, sliced so the corners keep their size. With a lip, the bottom
        /// <paramref name="lip"/> pixels are a darker shade that the image colour tints as well.
        /// </summary>
        static Sprite CreateRounded(int lip)
        {
            int size = Radius * 2 + 4 + lip;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float alpha = Coverage(x + 0.5f, y + 0.5f, 0, size, 0, size);
                // The face sits on top of the lip; below its edge only the lip shows.
                float face = Coverage(x + 0.5f, y + 0.5f, 0, size, lip, size);
                byte shade = (byte)(255 * Mathf.Lerp(0.66f, 1, face));
                pixels[y * size + x] = new Color32(shade, shade, shade, (byte)(alpha * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            // pixelsPerUnit 100 matches the canvas, so one texel is one reference unit.
            var border = new Vector4(Radius + 1, Radius + 1 + lip, Radius + 1, Radius + 1);
            return Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100,
                0, SpriteMeshType.FullRect, border);
        }

        /// <summary>Anti-aliased coverage of a point by a rounded rectangle.</summary>
        static float Coverage(float x, float y, float left, float right, float bottom, float top)
        {
            float cx = Mathf.Clamp(x, left + Radius, right - Radius);
            float cy = Mathf.Clamp(y, bottom + Radius, top - Radius);
            float distance = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            return Mathf.Clamp01(Radius - distance + 0.5f);
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
