using System;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The creek seen from above: the creek's painted river when it exists, otherwise one drawn
    /// in code (teal water with deeper pools, sandy banks and round pine tops), with current
    /// streaks over either. The land scrolls down slowly, so the dredge seems to work its way
    /// upstream; the streaks run with the current at the speed the gold drifts. Moving to another
    /// creek, the dredge gets under way: the land races by and the new creek's river takes over.
    /// </summary>
    public sealed class RiverView : MonoBehaviour
    {
        /// <summary>Each bank's width as a share of the creek width; painted rivers keep the same banks.</summary>
        const float BankShare = 0.07f;
        /// <summary>How far the sand lobes reach past the bank line, wet rim included.</summary>
        const float LobeReach = 28;
        /// <summary>Taller than any creek area, so two stacked tiles always cover it.</summary>
        const float TileHeight = 2200;
        const float LandSpeed = 55;
        const float TravelSeconds = 2.2f;
        /// <summary>Extra land speed, in multiples of the normal one, at the height of a journey.</summary>
        const float TravelRush = 11;
        /// <summary>A painted river repeats flipped, so its tiles join; four cover any creek.</summary>
        const int PaintedTiles = 4;

        RectTransform area;
        RectTransform land;
        RectTransform painted;
        readonly Image[] paintings = new Image[PaintedTiles];
        RectTransform current;
        float landOffset;
        float paintedOffset;
        float currentOffset;
        float travelAge = TravelSeconds;
        int pendingRegion = -1;

        /// <summary>Where open water starts and ends, in creek-area pixels from the left.</summary>
        public float WaterLeft => area.rect.width * BankShare + LobeReach;

        public float WaterRight => area.rect.width - WaterLeft;

        public bool Traveling => travelAge < TravelSeconds;

        /// <summary>0 at rest, rising to 1 in the middle of a journey and back.</summary>
        public float Travel
        {
            get
            {
                if (!Traveling)
                    return 0;
                float s = Mathf.Sin(travelAge / TravelSeconds * Mathf.PI);
                return s * s;
            }
        }

        public void Init(RectTransform creekArea)
        {
            area = creekArea;
            var water = gameObject.AddComponent<Image>();
            water.color = Palette.Water;
            water.raycastTarget = false;

            land = Strip("Land", TileHeight * 2);
            painted = Strip("Painted", 0);
            for (int i = 0; i < PaintedTiles; i++)
            {
                paintings[i] = Ui.Image("Painting", painted, Color.white);
                paintings[i].raycastTarget = false;
                RectTransform rt = paintings[i].rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = new Vector3(1, i % 2 == 0 ? 1 : -1, 1);
            }
            current = Strip("Current", TileHeight * 2);
            // Both tiles of a strip get the same seed, so the second repeats the first seamlessly.
            for (int tile = 0; tile < 2; tile++)
            {
                BuildLand(Tile(land, tile), new System.Random(11));
                BuildCurrent(Tile(current, tile), new System.Random(23));
            }
            Paint(null);
        }

        /// <summary>Shows a creek's river; with <paramref name="travel"/> the dredge sails there first.</summary>
        public void SetRegion(int region, bool travel)
        {
            if (travel)
            {
                travelAge = 0;
                pendingRegion = region;
            }
            else
            {
                Paint(Art.River(region));
            }
        }

        public void Tick(float deltaTime, float currentSpeed)
        {
            travelAge += deltaTime;
            float rush = Travel;
            // The new river slides in at full speed, where the swap is lost in the motion.
            if (pendingRegion >= 0 && travelAge >= TravelSeconds / 2)
            {
                Paint(Art.River(pendingRegion));
                pendingRegion = -1;
            }
            float landStep = LandSpeed * (1 + TravelRush * rush) * deltaTime;
            landOffset = Scroll(land, landOffset, landStep, TileHeight);
            if (painted.gameObject.activeSelf)
                ScrollPainted(landStep);
            currentOffset = Scroll(current, currentOffset, currentSpeed * (1 + 2 * rush) * deltaTime, TileHeight);
        }

        static float Scroll(RectTransform strip, float offset, float step, float period)
        {
            offset = Mathf.Repeat(offset - step, period) - period;
            strip.anchoredPosition = new Vector2(0, offset);
            return offset;
        }

        void Paint(Sprite river)
        {
            painted.gameObject.SetActive(river != null);
            land.gameObject.SetActive(river == null);
            foreach (Image painting in paintings)
                painting.sprite = river;
        }

        /// <summary>Tiles follow the creek width; the pattern repeats every two tiles (one flipped).</summary>
        void ScrollPainted(float step)
        {
            Sprite river = paintings[0].sprite;
            float height = area.rect.width * river.rect.height / river.rect.width;
            for (int i = 0; i < PaintedTiles; i++)
            {
                paintings[i].rectTransform.sizeDelta = new Vector2(0, height);
                paintings[i].rectTransform.anchoredPosition = new Vector2(0, (i + 0.5f) * height);
            }
            paintedOffset = Scroll(painted, paintedOffset, step, height * 2);
        }

        RectTransform Strip(string name, float height)
        {
            RectTransform strip = Ui.Rect(name, transform);
            strip.anchorMin = Vector2.zero;
            strip.anchorMax = new Vector2(1, 0);
            strip.pivot = new Vector2(0.5f, 0);
            strip.sizeDelta = new Vector2(0, height);
            return strip;
        }

        static RectTransform Tile(RectTransform strip, int index)
        {
            RectTransform tile = Ui.Rect("Tile", strip);
            tile.anchorMin = Vector2.zero;
            tile.anchorMax = new Vector2(1, 0);
            tile.pivot = new Vector2(0.5f, 0);
            tile.sizeDelta = new Vector2(0, TileHeight);
            tile.anchoredPosition = new Vector2(0, index * TileHeight);
            return tile;
        }

        static void BuildLand(RectTransform tile, System.Random random)
        {
            // Deeper pools on the river bed travel with the land.
            for (int i = 0; i < 5; i++)
            {
                float x = Range(random, 0.25f, 0.75f);
                var size = new Vector2(Range(random, 320, 520), Range(random, 600, 1000));
                Wrapped(Range(random, 0, TileHeight), size.y / 2, y =>
                    Blob(tile, "Pool", 0, x, 0, y, size, Palette.DeepWater).sprite = Ui.Glow);
            }

            for (int side = 0; side < 2; side++)
            {
                // Sand lobes along the waterline, each over a slightly bigger wet one, so the
                // edge gets a darker rim without the wet lobes covering the dry sand.
                int lobes = (int)(TileHeight / 75);
                var lobeY = new float[lobes];
                var lobeX = new float[lobes];
                var lobeR = new float[lobes];
                for (int i = 0; i < lobes; i++)
                {
                    lobeR[i] = Range(random, 60, 95);
                    lobeX[i] = Range(random, LobeReach - 34, LobeReach - 8) - lobeR[i];
                    lobeY[i] = (i + Range(random, -0.25f, 0.25f)) * TileHeight / lobes;
                }
                for (int i = 0; i < lobes; i++)
                {
                    float x = lobeX[i];
                    float size = (lobeR[i] + 8) * 2;
                    Wrapped(lobeY[i], size / 2, y => Blob(tile, "Wet", side, BankShare, x, y, Vector2.one * size, Palette.WetSand));
                }
                Image sand = Ui.Image("Sand", tile, Palette.Sand);
                sand.raycastTarget = false;
                sand.rectTransform.anchorMin = new Vector2(side == 0 ? 0 : 1 - BankShare, 0);
                sand.rectTransform.anchorMax = new Vector2(side == 0 ? BankShare : 1, 1);
                sand.rectTransform.offsetMin = new Vector2(side == 0 ? 0 : 40, 0);
                sand.rectTransform.offsetMax = new Vector2(side == 0 ? -40 : 0, 0);
                for (int i = 0; i < lobes; i++)
                {
                    float x = lobeX[i];
                    float size = lobeR[i] * 2;
                    Wrapped(lobeY[i], size / 2, y => Blob(tile, "Sand", side, BankShare, x, y, Vector2.one * size, Palette.Sand));
                }

                for (int i = 0; i < 3; i++)
                {
                    float x = Range(random, LobeReach - 20, LobeReach);
                    var size = new Vector2(Range(random, 34, 54), Range(random, 24, 36));
                    Wrapped(Range(random, 0, TileHeight), size.x, y => Blob(tile, "Rock", side, BankShare, x, y, size, Palette.Rock));
                }

                // Round pine tops leaning in from the outer edge, a lighter crown on each.
                int pines = (int)(TileHeight / 150);
                for (int i = 0; i < pines; i++)
                {
                    float size = Range(random, 120, 190);
                    float x = Range(random, -100, -45);
                    Wrapped((i + Range(random, -0.35f, 0.35f)) * TileHeight / pines, size / 2, y =>
                    {
                        Image pine = Blob(tile, "Pine", side, 0, x, y, Vector2.one * size, Palette.Pine);
                        Image crown = Ui.Image("Crown", pine.transform, Palette.PineLight, Ui.Circle);
                        crown.raycastTarget = false;
                        crown.rectTransform.Box(new Vector2(0.5f, 0.5f), Vector2.one * (size * 0.6f), new Vector2(size * 0.05f, size * 0.07f));
                        crown.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                    });
                }
            }
        }

        static void BuildCurrent(RectTransform tile, System.Random random)
        {
            for (int i = 0; i < 26; i++)
            {
                float x = Range(random, BankShare + 0.07f, 1 - BankShare - 0.07f);
                var size = new Vector2(Range(random, 5, 8), Range(random, 50, 150));
                Color color = Palette.Current;
                color.a *= Range(random, 0.5f, 1);
                Wrapped(Range(random, 0, TileHeight), size.y / 2, y => Blob(tile, "Streak", 0, x, 0, y, size, color));
            }
        }

        /// <summary>
        /// A soft round shape at <paramref name="x"/> pixels from an anchor line, mirrored for the
        /// right bank (side 1), whose anchor is measured from the right edge.
        /// </summary>
        static Image Blob(RectTransform tile, string name, int side, float anchorX, float x, float y, Vector2 size, Color color)
        {
            Image image = Ui.Image(name, tile, color, Ui.Circle);
            image.raycastTarget = false;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(side == 0 ? anchorX : 1 - anchorX, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(side == 0 ? x : -x, y);
            return image;
        }

        /// <summary>Adds a copy one tile away for shapes that cross a tile edge, so tiles join without a seam.</summary>
        static void Wrapped(float y, float reach, Action<float> make)
        {
            make(y);
            if (y < reach)
                make(y + TileHeight);
            if (y > TileHeight - reach)
                make(y - TileHeight);
        }

        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
