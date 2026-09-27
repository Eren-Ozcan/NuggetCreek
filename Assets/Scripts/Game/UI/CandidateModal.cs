using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Crew candidate card (design doc 6.3, 12.1 screen 9): two members, pick one for Gems.
    /// "Later" keeps the offer open until the window runs out; "Pass" returns both to the pool.
    /// </summary>
    public sealed class CandidateModal
    {
        sealed class Slot
        {
            public RectTransform Root;
            public Text Name;
            public Text Effect;
            public Button Hire;
            public Text HireLabel;
            public int CrewIndex = -1;
        }

        readonly GameSession session;
        readonly RectTransform root;
        readonly Text timer;
        readonly Slot[] slots = new Slot[2];

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action Hired;

        public CandidateModal(GameSession session, Transform canvas)
        {
            this.session = session;
            root = Ui.Image("CandidateModal", canvas, Palette.Dim).rectTransform.Fill();
            RectTransform card = Ui.Image("Card", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(940, 1080));

            Text title = Ui.Label("Title", card, "NEW CREW", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -130), new Vector2(0, -30));

            timer = Ui.Label("Timer", card, "", 34, TextAnchor.MiddleCenter, Palette.TextMuted);
            timer.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(40, -200), new Vector2(-40, -130));

            for (int i = 0; i < slots.Length; i++)
                slots[i] = AddSlot(card, i);

            Ui.Button("Later", card, "Later", Palette.ButtonAlt, Close, out _, 40).AsRect()
                .Place(Vector2.zero, new Vector2(0.5f, 0), new Vector2(40, 40), new Vector2(-15, 170));
            Ui.Button("Pass", card, "Pass on both", Palette.ButtonAlt, Pass, out _, 40).AsRect()
                .Place(new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(15, 40), new Vector2(-40, 170));

            Close();
        }

        public void Open()
        {
            if (!session.HasCandidates)
                return;
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            if (!IsOpen)
                return;
            if (!session.HasCandidates)
            {
                Close();
                return;
            }

            timer.SetText($"Pick one. The offer ends in {Clock(session.Progress.CandidateSecondsLeft)}.");
            int? cost = session.CrewHireCost;
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                bool used = i < session.Candidates.Count;
                slot.Root.SetActive(used);
                if (!used)
                    continue;
                slot.CrewIndex = session.Candidates[i];
                CrewDefinition crew = GameCatalog.Crew[slot.CrewIndex];
                slot.Root.name = "Candidate_" + crew.Id;
                slot.Name.SetText(crew.Name);
                slot.Effect.SetText($"{Effects.PerLevel(crew.Stat, crew.PerLevel)} per level, up to Lv {GameCatalog.CrewMaxLevel}.");
                // The tutorial pays for the first hire (design doc 9.1).
                slot.HireLabel.SetText(cost == 0 ? "Hire  Free" : "Hire  " + Effects.Gems(cost ?? 0));
                bool affordable = session.CanAffordGems(cost);
                if (slot.Hire.interactable != affordable)
                    slot.Hire.interactable = affordable;
            }
        }

        /// <summary>Minutes and seconds, for a countdown the player watches.</summary>
        public static string Clock(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        Slot AddSlot(RectTransform card, int position)
        {
            var slot = new Slot();
            slot.Root = Ui.Image("Candidate", card, Palette.Row).rectTransform
                .Box(new Vector2(0.5f, 1), new Vector2(860, 300), new Vector2(0, -220 - position * 330));

            slot.Name = Ui.Label("Name", slot.Root, "", 46, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            slot.Name.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(30, 0), new Vector2(-30, -26));
            slot.Effect = Ui.Label("Effect", slot.Root, "", 34, TextAnchor.UpperLeft, Palette.TextMuted);
            slot.Effect.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(30, 0), new Vector2(-30, -90));

            slot.Hire = Ui.Button("Hire", slot.Root, "", Palette.GemButton, () => Hire(slot), out slot.HireLabel, 38);
            slot.Hire.AsRect().Box(new Vector2(1, 0), new Vector2(340, Ui.TapHeight), new Vector2(-30, 30));
            return slot;
        }

        void Hire(Slot slot)
        {
            if (!session.HireCandidate(slot.CrewIndex))
                return;
            Close();
            Hired?.Invoke();
        }

        void Pass()
        {
            session.PassCandidates();
            Close();
        }
    }
}
