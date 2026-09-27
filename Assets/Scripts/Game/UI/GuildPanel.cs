using System;
using System.Collections.Generic;
using System.Text;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen Prospectors' Guild (design doc 6.5, 6.6, 12.1 screen 7): stake a new claim
    /// (rebirth), the Guild level with its rewarded-ad XP and milestones, and the perk tree.
    /// </summary>
    public sealed class GuildPanel
    {
        sealed class PerkRow
        {
            public int Index;
            public Text Title;
            public Text Detail;
            public Button RankUp;
            public Text RankUpLabel;
        }

        static readonly string[] MilestoneTexts =
        {
            "The vein opens (2 levels)",
            "+2 chest capacity, chests open 25% faster",
            "Mother Lode combo up to x15",
            "All income x1.5",
            "Prospecting XP x1.5",
        };

        readonly GameSession session;
        readonly IRewardedAds ads;
        readonly RectTransform root;
        readonly Text claimText;
        readonly Button rebirth;
        readonly Text rebirthLabel;
        readonly Text guildText;
        readonly Button watchAd;
        readonly Text milestones;
        readonly Text pointsText;
        readonly List<PerkRow> perkRows = new List<PerkRow>();
        bool confirming;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Raised after a new claim was staked.</summary>
        public event Action Reborn;

        public GuildPanel(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("Guild", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "PROSPECTORS' GUILD", 52, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            Section(list, "NEW CLAIM");
            claimText = Ui.Label("ClaimText", list, "", 32, TextAnchor.UpperLeft, Palette.Text);
            Ui.PreferredHeight(claimText, 250);
            rebirth = Ui.Button("Rebirth", list, "", Palette.Button, OnRebirth, out rebirthLabel, 40);
            Ui.PreferredHeight(rebirth, 120);

            Section(list, "GUILD");
            guildText = Ui.Label("GuildText", list, "", 34, TextAnchor.MiddleLeft, Palette.Gold, FontStyle.Bold);
            Ui.PreferredHeight(guildText, 90);
            watchAd = Ui.Button("GuildAd", list, $"Watch ad  +{session.Economy.Config.GuildXpPerAd} Guild XP", Palette.Ad, WatchAd, out _, 38);
            Ui.PreferredHeight(watchAd, 120);
            milestones = Ui.Label("Milestones", list, "", 30, TextAnchor.UpperLeft, Palette.TextMuted);
            Ui.PreferredHeight(milestones, 230);

            Section(list, "PERKS");
            pointsText = Ui.Label("PerkPoints", list, "", 34, TextAnchor.MiddleLeft, Palette.Text, FontStyle.Bold);
            Ui.PreferredHeight(pointsText, 80);
            for (int i = 0; i < GameCatalog.Perks.Count; i++)
                perkRows.Add(NewPerkRow(list, i));
            Button reset = Ui.Button("ResetPerks", list, "Reset perks (free)", Palette.ButtonAlt, () => session.ResetPerks(), out _, 36);
            Ui.PreferredHeight(reset, Ui.TapHeight);

            Close();
        }

        public void Open()
        {
            confirming = false;
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            RefreshClaim();
            RefreshGuild();
            RefreshPerks();
        }

        void RefreshClaim()
        {
            long xp = session.ClaimXp;
            var text = new StringBuilder();
            text.Append($"Prospecting XP this claim: +{xp}\n");
            text.Append($"Prestige bonus: x{session.PrestigeMultiplier:0.00} -> x{session.PrestigeMultiplierAfterRebirth:0.00}\n");
            text.Append("Starts over at Nugget Creek: Dollars, sluice tier, upgrades and creeks reset.\n");
            text.Append("Kept: crew, Amos, Gems, gear, Nuggets, the Guild.");
            claimText.SetText(text.ToString());
            string label = !session.CanRebirth ? "Earn more to stake a claim"
                : confirming ? "Tap again to confirm" : "Stake a new claim";
            rebirthLabel.SetText(label);
            if (rebirth.interactable != session.CanRebirth)
                rebirth.interactable = session.CanRebirth;
        }

        void RefreshGuild()
        {
            string progress = session.GuildMaxed ? "MAX" : $"{session.Progress.GuildXp}/{session.GuildXpToNext} XP";
            guildText.SetText($"Guild Lv {session.GuildLevel}  -  {progress}");
            bool canWatch = ads.IsLoaded && !session.GuildMaxed;
            if (watchAd.interactable != canWatch)
                watchAd.interactable = canWatch;

            int[] levels = session.Economy.Config.GuildMilestoneLevels;
            var text = new StringBuilder();
            for (int i = 0; i < levels.Length; i++)
                text.Append(session.HasMilestone(i) ? "[x] " : "[ ] ").Append($"Lv {levels[i]}: {MilestoneTexts[i]}\n");
            milestones.SetText(text.ToString().TrimEnd());
        }

        void RefreshPerks()
        {
            pointsText.SetText($"Perk Points: {session.PerkPoints}");
            foreach (PerkRow row in perkRows)
            {
                PerkDefinition perk = GameCatalog.Perks[row.Index];
                int rank = session.PerkRank(row.Index);
                row.Title.SetText($"{perk.Name}  {rank}/{perk.MaxRank}");
                string now = rank > 0 ? Effects.PerLevel(perk.Stat, perk.PerRank * rank) + " now. " : "";
                row.Detail.SetText($"{now}{Effects.PerLevel(perk.Stat, perk.PerRank)} per rank");
                bool maxed = rank >= perk.MaxRank;
                row.RankUpLabel.SetText(maxed ? "MAX" : $"+1  ({perk.Cost} pt)");
                bool can = session.CanRankUpPerk(row.Index);
                if (row.RankUp.interactable != can)
                    row.RankUp.interactable = can;
            }
        }

        PerkRow NewPerkRow(Transform list, int index)
        {
            Image background = Ui.Image(GameCatalog.Perks[index].Id, list, Palette.Row);
            Ui.PreferredHeight(background, 160);
            RectTransform rt = background.rectTransform;
            var row = new PerkRow { Index = index };
            row.Title = Ui.Label("Title", rt, "", 40, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 0), new Vector2(-330, -20));
            row.Detail = Ui.Label("Detail", rt, "", 30, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 20), new Vector2(-330, -76));
            row.RankUp = Ui.Button("RankUp", rt, "", Palette.Button, () => session.RankUpPerk(index), out row.RankUpLabel, 34);
            row.RankUp.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));
            return row;
        }

        static void Section(Transform list, string text)
        {
            Text label = Ui.Label(text, list, text, 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
        }

        void OnRebirth()
        {
            if (!confirming)
            {
                confirming = true;
                Refresh();
                return;
            }
            confirming = false;
            if (!session.Rebirth())
                return;
            Close();
            Reborn?.Invoke();
        }

        void WatchAd()
        {
            ads.Show("guild_xp", rewarded =>
            {
                if (rewarded)
                    session.AddGuildAdXp();
                ads.Load();
                Refresh();
            });
        }
    }
}
