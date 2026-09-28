using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>Full-screen Upgrades modal: SLUICE (tier and upgrades), CREW (Amos, hired crew) and GEAR for the greybox.</summary>
    public sealed class UpgradesPanel
    {
        sealed class Row
        {
            public RectTransform Root;
            public Text Title;
            public Text Detail;
            public Button Buy;
            public Text BuyLabel;
            public IconBesideText PriceIcon;
            public Image Picture;
            public Action Refresh;
        }

        readonly GameSession session;
        readonly RectTransform root;
        readonly RectTransform list;
        readonly List<Row> rows = new List<Row>();
        Row amosRow;
        Text crewHint;
        readonly GearSection gear;

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action Purchased;

        public event Action Closed;

        public UpgradesPanel(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("Upgrades", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Title("Title", root, "UPGRADES", 56);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);

            Ui.CloseButton(root, Close);

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(0, -160));
            list = Ui.ScrollList(body, 16, 24);

            Section("SLUICE");
            AddTierRow();
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                AddUpgradeRow(i);
            Section("CREW");
            amosRow = AddAmosRow();
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                AddCrewRow(i);
            crewHint = Ui.Label("CrewHint", list, "", 32, TextAnchor.MiddleLeft, Palette.TextMuted);
            Ui.PreferredHeight(crewHint, 110);
            gear = new GearSection(session, list, () => Purchased?.Invoke());

            Close();
        }

        public void Open(bool focusAmos = false)
        {
            root.SetActive(true);
            Refresh();
            if (focusAmos)
            {
                Canvas.ForceUpdateCanvases();
                list.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 0;
                amosRow.Root.GetComponent<Image>().color = Palette.Highlight;
            }
            else
            {
                amosRow.Root.GetComponent<Image>().color = Palette.Row;
            }
        }

        public void Close()
        {
            bool wasOpen = IsOpen;
            root.SetActive(false);
            if (wasOpen)
                Closed?.Invoke();
        }

        public void Refresh()
        {
            if (!IsOpen)
                return;
            foreach (Row row in rows)
                row.Refresh();
            crewHint.SetText(CrewHintText());
            gear.Refresh();
        }

        /// <summary>True when something in the panel can be bought right now (badge on the bar button).</summary>
        public bool AnythingAffordable()
        {
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                if (session.IsUpgradeUnlocked(i) && session.CanAfford(session.UpgradeCost(i)))
                    return true;
            for (int i = 0; i < GameCatalog.Crew.Count; i++)
                if (session.CanAffordGems(session.CrewLevelUpCost(i)))
                    return true;
            if (gear.AnythingAffordable())
                return true;
            return (session.IsNextTierUnlocked && session.CanAfford(session.NextTierCost))
                || session.CanAfford(session.AmosNextCost);
        }

        string CrewHintText()
        {
            if (session.CrewHireCost == null)
                return "The whole crew is on the creek.";
            if (session.HasCandidates)
                return "Candidates are waiting to be hired. Tap the crew chip on the creek.";
            string first = GameCatalog.RegionNames[session.Economy.Config.CrewCandidateFirstRegion];
            return $"New hands turn up each time you unlock a creek, from {first} on.";
        }

        void Section(string text)
        {
            Text label = Ui.Label(text, list, text, 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
        }

        void AddTierRow()
        {
            Row row = NewRow("Tier", Art.Sluice(session.Progress.TierIndex), () =>
            {
                if (session.BuyNextTier())
                    Purchased?.Invoke();
            });
            row.Refresh = () =>
            {
                int owned = session.Progress.TierIndex;
                row.Title.SetText($"Sluice Tier {owned + 1}");
                if (row.Picture != null)
                    row.Picture.sprite = Art.Sluice(owned) ?? row.Picture.sprite;
                if (!session.HasNextTier)
                {
                    row.Detail.SetText("Best sluice on the creek.");
                    SetBuy(row, null, "MAX", false);
                    return;
                }
                int next = owned + 1;
                if (!session.IsNextTierUnlocked)
                {
                    int creek = System.Math.Min(session.Economy.TierRegion(next), GameCatalog.RegionNames.Count - 1);
                    row.Detail.SetText($"Tier {next + 1} unlocks in {GameCatalog.RegionNames[creek]}.");
                    SetBuy(row, null, "LOCKED", false);
                    return;
                }
                row.Detail.SetText($"Tier {next + 1}: x{session.Economy.Config.TierMultiplier:0.#} income, 2 new upgrades.");
                SetBuy(row, session.NextTierCost, null, true);
            };
        }

        void AddUpgradeRow(int index)
        {
            UpgradeDefinition upgrade = GameCatalog.Upgrades[index];
            Row row = NewRow(upgrade.Id, Art.Upgrade(upgrade.Id), () =>
            {
                if (session.BuyUpgrade(index))
                    Purchased?.Invoke();
            });
            row.Refresh = () =>
            {
                bool unlocked = session.IsUpgradeUnlocked(index);
                // Only the next locked tier is listed, so the list stays short and shows what comes next.
                row.Root.SetActive(upgrade.TierIndex <= session.Progress.TierIndex + 1);
                int level = session.UpgradeLevel(index);
                row.Title.SetText($"{upgrade.Name}  {level}/{upgrade.MaxLevel}");
                string effect = Effects.PerLevel(upgrade.Stat, upgrade.PerLevel) + " per level";
                if (!unlocked)
                {
                    row.Detail.SetText($"{effect}. Needs Sluice Tier {upgrade.TierIndex + 1}.");
                    SetBuy(row, null, "LOCKED", false);
                    return;
                }
                row.Detail.SetText(effect + ".");
                BigNumber? cost = session.UpgradeCost(index);
                SetBuy(row, cost, cost.HasValue ? null : "MAX", cost.HasValue);
            };
        }

        Row AddAmosRow()
        {
            Row row = NewRow("Amos", Art.Portrait("amos"), () =>
            {
                if (session.BuyAmosLevel())
                    Purchased?.Invoke();
            });
            row.Refresh = () =>
            {
                int level = session.Progress.AmosLevel;
                if (level == 0)
                {
                    row.Title.SetText("Amos");
                    string firstCap = NumberFormat.Hours(session.Economy.OfflineCapSeconds(1, session.Stats) / 3600);
                    row.Detail.SetText($"\"I'll keep panning while you're gone.\" Starts idle income, {firstCap} away cap.");
                    SetBuy(row, session.AmosNextCost, null, true, "Hire");
                    return;
                }
                row.Title.SetText($"Amos  Lv {level}");
                string cap = $"Away cap {NumberFormat.Hours(session.OfflineCapSeconds / 3600)}.";
                if (session.AmosNextCost.HasValue)
                {
                    string next = NumberFormat.Hours(session.Economy.OfflineCapSeconds(level + 1, session.Stats) / 3600);
                    row.Detail.SetText($"{cap} Next level: {next}.");
                    SetBuy(row, session.AmosNextCost, null, true);
                }
                else if (session.AmosWaitsForRegion)
                {
                    row.Detail.SetText($"{cap} Unlock {GameCatalog.RegionNames[session.Progress.RegionsUnlocked]} to train him further.");
                    SetBuy(row, null, "LOCKED", false);
                }
                else
                {
                    row.Detail.SetText(cap);
                    SetBuy(row, null, "MAX", false);
                }
            };
            return row;
        }

        void AddCrewRow(int index)
        {
            CrewDefinition crew = GameCatalog.Crew[index];
            Row row = NewRow(crew.Id, Art.Portrait(crew.Id), () =>
            {
                if (session.LevelUpCrew(index))
                    Purchased?.Invoke();
            }, gems: true);
            row.Buy.GetComponent<Image>().color = Palette.GemButton;
            row.Refresh = () =>
            {
                int level = session.CrewLevel(index);
                row.Root.SetActive(level > 0);
                if (level == 0)
                    return;
                row.Title.SetText($"{crew.Name}  Lv {level}/{GameCatalog.CrewMaxLevel}");
                string now = Effects.PerLevel(crew.Stat, crew.PerLevel * level);
                int? cost = session.CrewLevelUpCost(index);
                row.Detail.SetText(cost.HasValue ? $"{now}. Next: {Effects.PerLevel(crew.Stat, crew.PerLevel)}." : now + ".");
                SetGemBuy(row, cost, cost.HasValue ? null : "MAX");
            };
        }

        Row NewRow(string name, Sprite picture, Action onBuy, bool gems = false)
        {
            const float height = 170;
            Image background = Ui.Panel(name, list, Palette.Row);
            Ui.PreferredHeight(background, height);
            var row = new Row { Root = background.rectTransform };
            float inset = Ui.RowPicture(row.Root, height, picture, out row.Picture);

            row.Title = Ui.Label("Title", row.Root, name, 42, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 0), new Vector2(-330, -22));
            row.Detail = Ui.Label("Detail", row.Root, "", 32, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 22), new Vector2(-330, -80));

            row.Buy = Ui.Button("Buy", row.Root, "", Palette.Button, onBuy, out row.BuyLabel, 36);
            row.Buy.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));
            row.PriceIcon = gems ? Ui.PriceIcon(row.BuyLabel, Art.Gem, Palette.Gem) : Ui.PriceIcon(row.BuyLabel, Art.Dollar, Palette.Gold);

            rows.Add(row);
            return row;
        }

        /// <summary>A <paramref name="leadWord"/> such as "Hire" comes before the coin: "Hire (coin) $70".</summary>
        void SetBuy(Row row, BigNumber? cost, string fixedText, bool enabled, string leadWord = null)
        {
            row.PriceIcon.Lead = leadWord == null ? null : row.PriceIcon.Lead ?? row.PriceIcon.MakeLead(leadWord);
            row.BuyLabel.SetText(fixedText ?? row.PriceIcon.Lead + NumberFormat.Dollars(cost.Value));
            row.PriceIcon.SetActive(fixedText == null);
            bool interactable = enabled && session.CanAfford(cost);
            if (row.Buy.interactable != interactable)
                row.Buy.interactable = interactable;
        }

        void SetGemBuy(Row row, int? cost, string fixedText)
        {
            row.BuyLabel.SetText(fixedText ?? cost.Value.ToString());
            row.PriceIcon.SetActive(fixedText == null);
            bool interactable = session.CanAffordGems(cost);
            if (row.Buy.interactable != interactable)
                row.Buy.interactable = interactable;
        }
    }

    static class ComponentExtensions
    {
        public static RectTransform AsRect(this Component component) => (RectTransform)component.transform;
    }

    /// <summary>Player-facing wording for stat effects.</summary>
    public static class Effects
    {
        public static string Gems(int amount) => amount == 1 ? "1 Gem" : $"{amount} Gems";

        public static string PerLevel(Stat stat, double amount)
        {
            string sign = amount >= 0 ? "+" : "";
            switch (stat)
            {
                case Stat.NuggetChance:
                case Stat.DoubleCatch:
                case Stat.RichNuggetChance:
                case Stat.GiantNuggetChance:
                case Stat.CritChance:
                    return $"{sign}{amount * 100:0.#} pt {Name(stat)}";
                case Stat.VeinMaxLevel:
                    return $"{sign}{amount:0} {Name(stat)}";
                case Stat.OfflineCapHours:
                    return $"{sign}{NumberFormat.Hours(amount)} {Name(stat)}";
                case Stat.VeinCatchesPerLevel:
                    return $"{sign}{amount:0} {Name(stat)}";
                case Stat.CollectibleLifetime:
                    return $"{sign}{amount:0.##}s {Name(stat)}";
                default:
                    return $"{sign}{amount * 100:0.#}% {Name(stat)}";
            }
        }

        public static string Name(Stat stat)
        {
            switch (stat)
            {
                case Stat.DustValue: return "Gold Dust value";
                case Stat.SpawnRate: return "spawn rate";
                case Stat.IdleSpeed: return "idle speed";
                case Stat.OfflineIncome: return "offline income";
                case Stat.NuggetChance: return "Nugget chance";
                case Stat.NuggetValue: return "Nugget value";
                case Stat.RichNuggetChance: return "Rich Nugget chance";
                case Stat.RichNuggetValue: return "Rich Nugget value";
                case Stat.GiantNuggetChance: return "Giant Nugget chance";
                case Stat.GiantNuggetValue: return "Giant Nugget value";
                case Stat.CritChance: return "critical chance";
                case Stat.CritValue: return "critical value";
                case Stat.VeinMaxLevel: return "max vein levels";
                case Stat.VeinCatchesPerLevel: return "catches per vein level";
                case Stat.OfflineCapHours: return "away cap";
                case Stat.DoubleCatch: return "double catch chance";
                case Stat.AllIncome: return "all income";
                case Stat.ActiveIncome: return "swipe income";
                case Stat.MotherLodeReward: return "Mother Lode reward";
                case Stat.MotherLodeDamage: return "boss damage";
                case Stat.MotherLodeFrequency: return "Mother Lode wait";
                case Stat.CollectRadius: return "pick-up radius";
                case Stat.CollectibleLifetime: return "time on screen";
                case Stat.ChestValue: return "chest value";
                case Stat.ProspectingXp: return "Prospecting XP";
                case Stat.TierCost: return "sluice tier cost";
                case Stat.RegionCost: return "creek cost";
                case Stat.UpgradeCost: return "upgrade cost";
                default: return stat.ToString();
            }
        }
    }
}
