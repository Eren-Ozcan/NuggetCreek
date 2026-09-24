using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>Full-screen Upgrades modal: SLUICE (tier and upgrades) and CREW (Amos) for the greybox.</summary>
    public sealed class UpgradesPanel
    {
        sealed class Row
        {
            public RectTransform Root;
            public Text Title;
            public Text Detail;
            public Button Buy;
            public Text BuyLabel;
            public Action Refresh;
        }

        readonly GameSession session;
        readonly RectTransform root;
        readonly RectTransform list;
        readonly List<Row> rows = new List<Row>();
        Row amosRow;

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action Purchased;

        public event Action Closed;

        public UpgradesPanel(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("Upgrades", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "UPGRADES", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);

            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, new Vector2(0, 0), new Vector2(0, -160));
            list = Ui.ScrollList(body, 16, 24);

            Section("SLUICE");
            AddTierRow();
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                AddUpgradeRow(i);
            Section("CREW");
            amosRow = AddAmosRow();

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
                amosRow.Root.GetComponent<Image>().color = Palette.Amos;
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
        }

        /// <summary>True when something in the panel can be bought right now (badge on the bar button).</summary>
        public bool AnythingAffordable()
        {
            for (int i = 0; i < GameCatalog.Upgrades.Count; i++)
                if (session.IsUpgradeUnlocked(i) && session.CanAfford(session.UpgradeCost(i)))
                    return true;
            return (session.IsNextTierUnlocked && session.CanAfford(session.NextTierCost))
                || session.CanAfford(session.AmosNextCost);
        }

        void Section(string text)
        {
            Text label = Ui.Label(text, list, text, 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
        }

        void AddTierRow()
        {
            Row row = NewRow("Tier", () =>
            {
                if (session.BuyNextTier())
                    Purchased?.Invoke();
            });
            row.Refresh = () =>
            {
                int owned = session.Progress.TierIndex;
                row.Title.SetText($"Sluice Tier {owned + 1}");
                if (!session.HasNextTier)
                {
                    row.Detail.SetText("Best sluice on the creek.");
                    SetBuy(row, null, "MAX", false);
                    return;
                }
                int next = owned + 1;
                if (!session.IsNextTierUnlocked)
                {
                    row.Detail.SetText($"Tier {next + 1} unlocks in {GameCatalog.RegionNames[next]}.");
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
            Row row = NewRow(upgrade.Id, () =>
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
            Row row = NewRow("Amos", () =>
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
                    row.Detail.SetText("\"I'll keep panning while you're gone.\" Starts idle income, 1h away cap.");
                    SetBuy(row, session.AmosNextCost, null, true, "Hire ");
                    return;
                }
                row.Title.SetText($"Amos  Lv {level}");
                string cap = $"Away cap {level}h.";
                if (session.AmosNextCost.HasValue)
                {
                    row.Detail.SetText($"{cap} Next level: {level + 1}h.");
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

        Row NewRow(string name, Action onBuy)
        {
            Image background = Ui.Image(name, list, Palette.Row);
            Ui.PreferredHeight(background, 170);
            var row = new Row { Root = background.rectTransform };

            row.Title = Ui.Label("Title", row.Root, name, 42, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 0), new Vector2(-330, -22));
            row.Detail = Ui.Label("Detail", row.Root, "", 32, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 22), new Vector2(-330, -80));

            row.Buy = Ui.Button("Buy", row.Root, "", Palette.Button, onBuy, out row.BuyLabel, 36);
            row.Buy.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));

            rows.Add(row);
            return row;
        }

        void SetBuy(Row row, BigNumber? cost, string fixedText, bool enabled, string prefix = "")
        {
            row.BuyLabel.SetText(fixedText ?? prefix + NumberFormat.Dollars(cost.Value));
            bool interactable = enabled && session.CanAfford(cost);
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
        public static string PerLevel(Stat stat, double amount)
        {
            string sign = amount >= 0 ? "+" : "";
            switch (stat)
            {
                case Stat.NuggetChance:
                case Stat.DoubleCatch:
                    return $"{sign}{amount * 100:0.#} pt {Name(stat)}";
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
                case Stat.DoubleCatch: return "double catch chance";
                case Stat.AllIncome: return "all income";
                case Stat.ActiveIncome: return "swipe income";
                case Stat.MotherLodeReward: return "Mother Lode reward";
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
