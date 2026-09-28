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
    }
}
