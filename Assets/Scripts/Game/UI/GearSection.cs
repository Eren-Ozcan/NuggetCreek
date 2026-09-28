using System;
using System.Collections.Generic;
using System.Text;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// GEAR part of the Upgrades panel (design doc 6.4): slots, owned gear with wear and level
    /// buttons and the third slot. Gear boxes sell in the shop (design doc 8.2e). Hidden until
    /// the first chest.
    /// </summary>
    public sealed class GearSection
    {
        sealed class Row
        {
            public RectTransform Root;
            public Text Title;
            public Text Detail;
            public Button Left;
            public Text LeftLabel;
            public Button Right;
            public Text RightLabel;
            public IconBesideText PriceIcon;
            public Action Refresh;
        }

        readonly GameSession session;
        readonly Transform list;
        readonly Action purchased;
        readonly List<Row> rows = new List<Row>();
        readonly List<GameObject> parts = new List<GameObject>();
        Text slotsLabel;
        bool? shownUnlocked;

        public GearSection(GameSession session, Transform list, Action purchased)
        {
            this.session = session;
            this.list = list;
            this.purchased = purchased;

            Text header = Ui.Label("GEAR", list, "GEAR", 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(header, 70);
            parts.Add(header.gameObject);

            slotsLabel = Ui.Label("GearSlots", list, "", 32, TextAnchor.MiddleLeft, Palette.Text);
            Ui.PreferredHeight(slotsLabel, 90);
            parts.Add(slotsLabel.gameObject);

            AddThirdSlotRow();
            for (int i = 0; i < GameCatalog.Gear.Count; i++)
                AddGearRow(i);
        }

        public void Refresh()
        {
            bool unlocked = session.GearUnlocked;
            if (shownUnlocked != unlocked)
            {
                // Rows hide themselves below; only flip the whole section when the tab opens.
                foreach (GameObject part in parts)
                    part.SetActive(unlocked);
                shownUnlocked = unlocked;
            }
            if (!unlocked)
                return;

            var slots = new StringBuilder("Slots:");
            for (int slot = 0; slot < session.Economy.Config.GearSlotCount; slot++)
            {
                string text = !session.IsGearSlotUnlocked(slot) ? "locked"
                    : session.GearInSlot(slot) < 0 ? "empty" : GameCatalog.Gear[session.GearInSlot(slot)].Name;
                slots.Append(slot == 0 ? "  " : "  |  ").Append(text);
            }
            slotsLabel.SetText(slots.ToString());

            foreach (Row row in rows)
                row.Refresh();
        }

        public bool AnythingAffordable()
        {
            if (!session.GearUnlocked)
                return false;
            for (int i = 0; i < GameCatalog.Gear.Count; i++)
                if (session.CanAffordGems(session.GearLevelUpCost(i)))
                    return true;
            return false;
        }

        void AddThirdSlotRow()
        {
            Row row = NewRow("ThirdSlot", Art.Icon("plus"), "", null, "", () => Buy(session.BuyThirdGearSlot()));
            row.Refresh = () =>
            {
                row.Root.SetActive(!session.Progress.ThirdGearSlotBought);
                row.Title.SetText("Third gear slot");
                row.Detail.SetText("Wear one more piece, for good.");
                SetButton(row.Right, row.RightLabel, session.Economy.Config.GearThirdSlotGems.ToString(), session.CanAffordGems(session.ThirdGearSlotCost));
            };
        }

        void AddGearRow(int index)
        {
            GearDefinition gear = GameCatalog.Gear[index];
            Row row = NewRow(gear.Id, Art.Gear(gear.Id), "", () => ToggleWear(index), "", () => Buy(session.LevelUpGear(index)));
            row.Refresh = () =>
            {
                int level = session.GearLevel(index);
                row.Root.SetActive(level > 0);
                if (level == 0)
                    return;
                row.Title.SetText($"{gear.Name}  Lv {level}/{GameCatalog.GearMaxLevel}");
                string now = Effects.PerLevel(gear.Stat, gear.PerLevel * level);
                row.Detail.SetText($"{gear.Rarity}  -  {now}");
                bool worn = session.IsEquipped(index);
                SetButton(row.Left, row.LeftLabel, worn ? "Take off" : "Wear", worn || HasFreeSlot());
                int? cost = session.GearLevelUpCost(index);
                SetButton(row.Right, row.RightLabel, cost.HasValue ? cost.Value.ToString() : "MAX", session.CanAffordGems(cost));
                row.PriceIcon.SetActive(cost.HasValue);
            };
        }

        bool HasFreeSlot()
        {
            for (int slot = 0; slot < session.Economy.Config.GearSlotCount; slot++)
                if (session.IsGearSlotUnlocked(slot) && session.GearInSlot(slot) < 0)
                    return true;
            return false;
        }

        void ToggleWear(int index)
        {
            if (session.IsEquipped(index) ? session.Unequip(index) : session.Equip(index))
                purchased();
        }

        void Buy(bool done)
        {
            if (done)
                purchased();
        }

        Row NewRow(string name, Sprite picture, string leftText, Action onLeft, string rightText, Action onRight)
        {
            const float height = 170;
            Image background = Ui.Panel(name, list, Palette.Row);
            Ui.PreferredHeight(background, height);
            var row = new Row { Root = background.rectTransform };
            parts.Add(background.gameObject);
            float inset = Ui.RowPicture(row.Root, height, picture, out _);

            row.Title = Ui.Label("Title", row.Root, name, 42, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 0), new Vector2(-330, -22));
            row.Detail = Ui.Label("Detail", row.Root, "", 30, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(inset, 22), new Vector2(-330, -80));

            row.Right = Ui.Button("Right", row.Root, rightText, Palette.GemButton, onRight, out row.RightLabel, 32);
            row.PriceIcon = Ui.PriceIcon(row.RightLabel, Art.Gem, Palette.Gem);
            if (onLeft != null)
            {
                row.Left = Ui.Button("Left", row.Root, leftText, Palette.ButtonAlt, onLeft, out row.LeftLabel, 32);
                row.Left.AsRect().Place(new Vector2(1, 0.5f), Vector2.one, new Vector2(-300, 8), new Vector2(-24, -16));
                row.Right.AsRect().Place(new Vector2(1, 0), new Vector2(1, 0.5f), new Vector2(-300, 16), new Vector2(-24, -8));
            }
            else
            {
                row.Right.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 22), new Vector2(-24, -22));
            }

            rows.Add(row);
            return row;
        }

        static void SetButton(Button button, Text label, string text, bool interactable)
        {
            label.SetText(text);
            if (button.interactable != interactable)
                button.interactable = interactable;
        }
    }
}
