using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Game art from Resources/Sprites, loaded on first use and cached. A missing file returns
    /// null so every caller keeps its greybox shape as a fallback.
    /// </summary>
    public static class Art
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string path)
        {
            if (!cache.TryGetValue(path, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>("Sprites/" + path);
                cache[path] = sprite;
            }
            return sprite;
        }

        /// <summary>Creek background; files are numbered from 1 like the design doc.</summary>
        public static Sprite Creek(int regionIndex) => Get($"Creeks/creek_{regionIndex + 1:00}");

        public static Sprite Nugget(int nuggetIndex) =>
            nuggetIndex >= 0 && nuggetIndex < GameCatalog.Nuggets.Count
                ? Get("Nuggets/" + GameCatalog.Nuggets[nuggetIndex].Id)
                : null;

        /// <summary>Sluice machine for a tier index (0 = tier 1).</summary>
        public static Sprite Sluice(int tierIndex) => Get($"Sluices/tier_{tierIndex + 1:00}");

        /// <summary>The main-screen dredge for a tier index (0 = tier 1), seen from above, bow up.</summary>
        public static Sprite Dredge(int tierIndex) => Get($"Dredge/tier_{tierIndex + 1:00}");

        /// <summary>White silhouette of a tier's dredge, for the tier-up show.</summary>
        public static Sprite DredgeGhost(int tierIndex) => Get($"Dredge/tier_{tierIndex + 1:00}_ghost");

        /// <summary>A moving piece cut from a dredge drawing, by its file name in parts.json.</summary>
        public static Sprite DredgePart(string file) => Get("Dredge/" + file);

        /// <summary>The creek's river seen from above; null until it is painted.</summary>
        public static Sprite River(int regionIndex) => Get($"Rivers/river_{regionIndex + 1:00}");

        static DredgeTierParts[] dredgeParts;
        static bool dredgePartsLoaded;

        /// <summary>
        /// The moving pieces, chimneys and spray points of a tier's drawing, written by the sprite
        /// tool next to the drawings; null when the file or the tier is missing.
        /// </summary>
        public static DredgeTierParts DredgeParts(int tierIndex)
        {
            if (!dredgePartsLoaded)
            {
                dredgePartsLoaded = true;
                var file = Resources.Load<TextAsset>("Sprites/Dredge/parts");
                dredgeParts = file != null ? JsonUtility.FromJson<DredgePartFile>(file.text)?.tiers : null;
            }
            if (dredgeParts == null)
                return null;
            foreach (DredgeTierParts tier in dredgeParts)
                if (tier.tier == tierIndex + 1)
                    return tier;
            return null;
        }

        [Serializable]
        sealed class DredgePartFile
        {
            public DredgeTierParts[] tiers;
        }

        public static Sprite Upgrade(string id) => Get("Upgrades/" + id);

        public static Sprite Portrait(string crewId) => Get("Portraits/" + crewId);

        public static Sprite Gear(string id) => Get("Gear/" + id);

        public static Sprite Perk(string id) => Get("Perks/" + id);

        public static Sprite GoldDust => Get("Currency/dust");

        public static Sprite NuggetIcon => Get("Currency/nugget");

        public static Sprite Dollar => Get("Currency/dollar");

        public static Sprite Gem => Get("Currency/gem");

        public static Sprite GuildEmblem => Get("Identity/guild_emblem");

        /// <summary>HUD icon by file name, e.g. "map" or "lock".</summary>
        public static Sprite Icon(string name) => Get("Icons/" + name);

        /// <summary>A river floater's picture (design doc 3.1.4); null until its art is drawn.</summary>
        public static Sprite Floater(FloaterKind kind) =>
            kind == FloaterKind.Chest ? Get("Chests/creek_float") ?? Get("Chests/creek_closed")
            : Get(kind == FloaterKind.Crate ? "Chests/crate" : "Chests/gem_pouch");

        /// <summary>Painted map panels, bottom (0) to top.</summary>
        public const int MapPanelCount = 4;

        public static Sprite MapPanel(int index) => Get($"Map/panel_{index + 1}");

        [Serializable]
        sealed class MarkerFile
        {
            public float[] x;
            public float[] y;
        }

        static Vector2[] mapMarkers;
        static bool mapMarkersLoaded;

        /// <summary>
        /// Each creek's marker on the painted map as fractions of the whole map (y from the
        /// bottom), written by the map tool next to the panels; null when it is missing.
        /// </summary>
        public static Vector2[] MapMarkers
        {
            get
            {
                if (mapMarkersLoaded)
                    return mapMarkers;
                mapMarkersLoaded = true;
                var file = Resources.Load<TextAsset>("Sprites/Map/markers");
                MarkerFile data = file != null ? JsonUtility.FromJson<MarkerFile>(file.text) : null;
                if (data?.x == null || data.y == null || data.x.Length != data.y.Length)
                    return null;
                mapMarkers = new Vector2[data.x.Length];
                for (int i = 0; i < mapMarkers.Length; i++)
                    mapMarkers[i] = new Vector2(data.x[i], data.y[i]);
                return mapMarkers;
            }
        }
    }

    /// <summary>One tier's entry in the dredge parts file. Points are fractions of the drawing from its bottom-left corner.</summary>
    [Serializable]
    public sealed class DredgeTierParts
    {
        public int tier;
        public DredgePart[] parts;
        /// <summary>Chimney tops as x, y pairs.</summary>
        public float[] smoke;
        /// <summary>Spray nozzles and spouts as x, y pairs.</summary>
        public float[] spray;
    }

    /// <summary>
    /// A moving piece: a "flow" band whose texture slides downward at <see cref="speed"/> texture
    /// pixels per second and shows <see cref="repeat"/> texture heights, a "dip" part that hangs
    /// from its pivot and dips into the water, an "arm" (a crane) that swings about its pivot, or a
    /// "drum" whose holes turn round at <see cref="speed"/> degrees per second.
    /// </summary>
    [Serializable]
    public sealed class DredgePart
    {
        public string file;
        public string kind;
        public float x0;
        public float x1;
        public float y0;
        public float y1;
        public float speed;
        public float repeat;
        public float pivotX;
        public float pivotY;
        /// <summary>A grab bucket drawn in three layers: <c>file_fixed</c> stays put, <c>file_left</c> and <c>file_right</c> swing open.</summary>
        public bool jaws;
        /// <summary>The left and the right half's hinge as x, y pairs, fractions of the part from its bottom-left corner.</summary>
        public float[] hinges;
        /// <summary>The halves' angle in degrees when wide open.</summary>
        public float jawOpen;
        /// <summary>The halves' angle in degrees when bitten shut; below 0 closes past the drawing.</summary>
        public float jawShut;
        /// <summary>How far an arm swings each way, in degrees.</summary>
        public float swing;
        /// <summary>The arm this part hangs from, by its file name; empty when it hangs from nothing.</summary>
        public string on;
        /// <summary>
        /// A line drawn from <c>file_tether</c> between the part and the drawing: x, y on the part,
        /// x, y on the drawing (both from the bottom-left corner), then its width as a share of the
        /// drawing's height.
        /// </summary>
        public float[] tether;
        /// <summary>A drum's holes as angle from its crest (degrees) and height pairs, the height on the crest from the part's bottom.</summary>
        public float[] holes;
        /// <summary>A drum hole's width and height, as fractions of the part; drawn from <c>file_hole</c>.</summary>
        public float[] holeSize;
        /// <summary>How far a row of holes drops at the drum's sides, as a fraction of the part's height.</summary>
        public float sag;
    }
}
