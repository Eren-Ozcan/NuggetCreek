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
    /// A claim is staked from a card that shows the income before and after, and what comes
    /// along and what starts over.
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

        const float MilestoneHeight = 46;

        static readonly string[] MilestoneTexts =
        {
            "The vein opens (2 levels)",
            "Crates pay x2, chests open 25% faster",
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
        readonly Image[] milestoneChecks;
        readonly Text[] milestoneTexts;
        readonly Color checkOn;
        readonly Text pointsText;
        readonly List<PerkRow> perkRows = new List<PerkRow>();
        readonly RectTransform confirm;
        readonly RectTransform confirmCard;
        readonly Text confirmIncome;
        readonly Text confirmXp;

        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>The claim card is up over the Guild.</summary>
        public bool ConfirmOpen => confirm.gameObject.activeSelf;

        /// <summary>"Stake a new claim", for Pete to point at.</summary>
        public Button ClaimButton => rebirth;

        /// <summary>The claim card's "Stake claim" button.</summary>
        public Button ConfirmButton { get; }

        /// <summary>The claim card, for Pete to frame.</summary>
        public RectTransform ConfirmCard => confirmCard;

        /// <summary>Raised after a new claim was staked.</summary>
        public event Action Reborn;

        public GuildPanel(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("Guild", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Title("Title", root, "PROSPECTORS' GUILD", 52);
            // Clear of the close button on narrow phones.
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(150, -150), new Vector2(-150, 0));
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 36;
            title.resizeTextMaxSize = Mathf.RoundToInt(52 * Ui.TextScale);
            Ui.CloseButton(root, Close);

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            Section(list, "NEW CLAIM");
            claimText = Ui.Label("ClaimText", list, "", 32, TextAnchor.UpperLeft, Palette.Text);
            Ui.PreferredHeight(claimText, 250);
            rebirth = Ui.Button("Rebirth", list, "", Palette.Button, OnRebirth, out rebirthLabel, 40);
            Ui.PreferredHeight(rebirth, 120);

            Section(list, "GUILD");
            guildText = Ui.Label("GuildText", list, "", 34, TextAnchor.MiddleLeft, Palette.GoldText, FontStyle.Bold);
            Ui.PreferredHeight(guildText, 90);
            watchAd = Ui.Button("GuildAd", list, $"Watch ad  +{session.Economy.Config.GuildXpPerAd} Guild XP", Palette.Ad, WatchAd, out _, 38);
            Ui.PreferredHeight(watchAd, 120);
            RectTransform milestones = Ui.Rect("Milestones", list);
            int milestoneCount = MilestoneTexts.Length;
            Ui.PreferredHeight(milestones, milestoneCount * MilestoneHeight);
            Sprite check = Art.Icon("check");
            checkOn = check != null ? Color.white : Palette.GemText;
            milestoneChecks = new Image[milestoneCount];
            milestoneTexts = new Text[milestoneCount];
            for (int i = 0; i < milestoneCount; i++)
            {
                RectTransform line = Ui.Rect("Milestone", milestones)
                    .Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -(i + 1) * MilestoneHeight), new Vector2(0, -i * MilestoneHeight));
                milestoneChecks[i] = Ui.Icon("Check", line, check, checkOn);
                milestoneChecks[i].rectTransform.Box(new Vector2(0, 0.5f), new Vector2(36, 36));
                milestoneTexts[i] = Ui.Label("Text", line, "", 30, TextAnchor.MiddleLeft, Palette.TextMuted);
                milestoneTexts[i].rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(50, 0));
            }

            Section(list, "PERKS");
            pointsText = Ui.Label("PerkPoints", list, "", 34, TextAnchor.MiddleLeft, Palette.Text, FontStyle.Bold);
            Ui.PreferredHeight(pointsText, 80);
            for (int i = 0; i < GameCatalog.Perks.Count; i++)
                perkRows.Add(NewPerkRow(list, i));
            Button reset = Ui.Button("ResetPerks", list, "Reset perks (free)", Palette.ButtonAlt, () => session.ResetPerks(), out _, 36);
            Ui.PreferredHeight(reset, Ui.TapHeight);

            // The claim card, over the whole panel.
            confirm = Ui.Image("ClaimConfirm", root, Palette.Dim).rectTransform.Fill();
            confirmCard = Ui.Panel("Card", confirm, Palette.Panel).rectTransform.Box(new Vector2(0.5f, 0.5f), new Vector2(920, 980));
            Text heading = Ui.Title("Title", confirmCard, "STAKE A NEW CLAIM?", 50);
            heading.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -140), new Vector2(-40, -40));
            heading.resizeTextForBestFit = true;
            heading.resizeTextMinSize = 32;
            heading.resizeTextMaxSize = 50;
            confirmIncome = Ui.Label("Income", confirmCard, "", 52, TextAnchor.MiddleCenter, Palette.GoldText, FontStyle.Bold);
            confirmIncome.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -260), new Vector2(-40, -150));
            confirmXp = Ui.Label("Xp", confirmCard, "", 32, TextAnchor.MiddleCenter, Palette.TextMuted);
            confirmXp.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -320), new Vector2(-40, -260));
            ClaimList(confirmCard, "COMES ALONG", "Gems, crew and Amos, gear, Nuggets and their stars, the Guild and its perks", -350, Palette.Text);
            ClaimList(confirmCard, "STARTS OVER", "Dollars, creeks (back to Nugget Creek), sluice tier and upgrades", -570, Palette.Text);
            ConfirmButton = Ui.Button("ClaimConfirmYes", confirmCard, "Stake claim", Palette.Button, StakeClaim, out _, 42);
            ConfirmButton.AsRect().Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(10, 40), new Vector2(-40, 40 + Ui.TapHeight));
            Button notYet = Ui.Button("ClaimConfirmNo", confirmCard, "Not yet", Palette.ButtonAlt, CloseConfirm, out _, 40);
            notYet.AsRect().Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-10, 40 + Ui.TapHeight));
            confirm.SetActive(false);

            Close();
        }

        static void ClaimList(RectTransform card, string title, string items, float top, Color color)
        {
            Text heading = Ui.Label(title, card, title, 34, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            heading.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(60, top - 60), new Vector2(-60, top));
            Text body = Ui.Label(title + "Items", card, items, 36, TextAnchor.UpperLeft, color);
            body.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(60, top - 210), new Vector2(-60, top - 70));
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 26;
            body.resizeTextMaxSize = 36;
        }

        public void Open()
        {
            confirm.SetActive(false);
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        void CloseConfirm() => confirm.SetActive(false);

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
            rebirthLabel.SetText(session.CanRebirth ? "Stake a new claim" : "Earn more to stake a claim");
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
            for (int i = 0; i < milestoneTexts.Length; i++)
            {
                bool reached = session.HasMilestone(i);
                milestoneChecks[i].color = reached ? checkOn : Palette.IconOff;
                milestoneTexts[i].color = reached ? Palette.Text : Palette.TextMuted;
                milestoneTexts[i].SetText(i < levels.Length ? $"Lv {levels[i]}: {MilestoneTexts[i]}" : "");
            }
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
            const float height = 160;
            string id = GameCatalog.Perks[index].Id;
            Image background = Ui.Panel(id, list, Palette.Row);
            Ui.PreferredHeight(background, height);
            RectTransform rt = background.rectTransform;
            var row = new PerkRow { Index = index };
            float inset = Ui.RowPicture(rt, height, Art.Perk(id), out _);
            row.Title = Ui.Label("Title", rt, "", 40, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 0), new Vector2(-330, -20));
            row.Detail = Ui.Label("Detail", rt, "", 30, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 20), new Vector2(-330, -76));
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
            if (!session.CanRebirth)
                return;
            confirmIncome.SetText($"Income x{session.PrestigeMultiplier:0.00}  ->  x{session.PrestigeMultiplierAfterRebirth:0.00}");
            confirmXp.SetText($"+{session.ClaimXp} Prospecting XP");
            confirm.SetActive(true);
        }

        void StakeClaim()
        {
            confirm.SetActive(false);
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
