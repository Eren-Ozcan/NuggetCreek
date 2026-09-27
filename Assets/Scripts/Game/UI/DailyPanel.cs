using System;
using System.Text;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Full-screen daily screen (design doc 7.1, 12.1 screen 12): login streak with the ad
    /// rescue, the three daily jobs and the Daily Wash with its listed odds. The Wash is a
    /// sluice, never a wheel (brand rule): the button says WASH.
    /// </summary>
    public sealed class DailyPanel
    {
        static readonly string[] WashNames =
        {
            "3 Gems", "8 Gems", "25 Gems", "Income x2 for 30 min", "Income x2 for 2 h",
            "Income x3 for 1 h", "Income x4 for 30 min", "A creek chest",
        };

        sealed class JobRow
        {
            public Text Title;
            public Text Detail;
            public Button Claim;
            public Text ClaimLabel;
        }

        readonly GameSession session;
        readonly IRewardedAds ads;
        readonly RectTransform root;
        readonly Text streakText;
        readonly Button claimStreak;
        readonly Text claimStreakLabel;
        readonly Button rescue;
        readonly Text rescueLabel;
        readonly JobRow[] jobs = new JobRow[3];
        readonly Text washText;
        readonly Button wash;
        readonly Text washLabel;
        readonly Text washResult;

        public bool IsOpen => root.gameObject.activeSelf;

        public event Action Changed;

        public DailyPanel(GameSession session, IRewardedAds ads, Transform canvas)
        {
            this.session = session;
            this.ads = ads;
            root = Ui.Image("Daily", canvas, Palette.Panel).rectTransform.Fill();

            Text title = Ui.Label("Title", root, "DAILY", 56, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -150), Vector2.zero);
            Ui.Button("Close", root, "X", Palette.ButtonAlt, Close, out _).AsRect()
                .Box(Vector2.one, new Vector2(120, 120), new Vector2(-20, -15));

            RectTransform body = Ui.Rect("Body", root).Place(Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -160));
            RectTransform list = Ui.ScrollList(body, 16, 24);

            Section(list, "STREAK");
            streakText = Ui.Label("StreakText", list, "", 34, TextAnchor.MiddleLeft, Palette.Text);
            Ui.PreferredHeight(streakText, 130);
            rescue = Ui.Button("RescueStreak", list, "", Palette.Ad, Rescue, out rescueLabel, 36);
            Ui.PreferredHeight(rescue, 110);
            claimStreak = Ui.Button("ClaimStreak", list, "", Palette.Button, ClaimStreak, out claimStreakLabel, 40);
            Ui.PreferredHeight(claimStreak, 120);

            Section(list, "JOBS");
            for (int i = 0; i < jobs.Length; i++)
                jobs[i] = NewJobRow(list, i);

            Section(list, "DAILY WASH");
            washText = Ui.Label("WashOdds", list, OddsText(), 28, TextAnchor.UpperLeft, Palette.TextMuted);
            Ui.PreferredHeight(washText, 290);
            wash = Ui.Button("Wash", list, "", Palette.Button, Wash, out washLabel, 44);
            Ui.PreferredHeight(wash, 130);
            washResult = Ui.Label("WashResult", list, "", 38, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            Ui.PreferredHeight(washResult, 90);

            Close();
        }

        public void Open()
        {
            washResult.SetText("");
            root.SetActive(true);
            Refresh();
        }

        public void Close() => root.SetActive(false);

        /// <summary>True when something on the screen can be collected (badge on the HUD button).</summary>
        public bool AnythingToCollect()
        {
            if (session.CanClaimStreak || session.CanWashFree)
                return true;
            for (int slot = 0; slot < session.JobCount; slot++)
                if (session.JobDone(slot) && !session.JobClaimed(slot))
                    return true;
            return false;
        }

        public void Refresh()
        {
            if (!IsOpen)
                return;
            if (!session.DayKnown)
            {
                streakText.SetText("Waiting for the time server...");
                claimStreak.SetActive(false);
                rescue.SetActive(false);
                wash.SetActive(false);
                return;
            }

            StreakReward today = session.StreakRewardFor(session.Progress.StreakIndex);
            string when = session.CanClaimStreak ? "Today" : "Tomorrow";
            streakText.SetText($"Day {session.StreakDay}/30\n{when}: {RewardText(today)}");
            claimStreak.SetActive(true);
            claimStreakLabel.SetText(session.CanClaimStreak ? "Claim" : "Come back tomorrow");
            if (claimStreak.interactable != session.CanClaimStreak)
                claimStreak.interactable = session.CanClaimStreak;
            rescue.SetActive(session.CanRescueStreak);
            if (session.CanRescueStreak)
            {
                rescueLabel.SetText($"Missed a day! Watch ad to keep day {session.RescueStreakDay}");
                if (rescue.interactable != ads.IsLoaded)
                    rescue.interactable = ads.IsLoaded;
            }

            for (int slot = 0; slot < jobs.Length; slot++)
            {
                JobRow row = jobs[slot];
                row.Title.SetText(JobText(session.JobKind(slot), session.JobTarget(slot)));
                row.Detail.SetText($"{session.JobProgress(slot)}/{session.JobTarget(slot)}");
                bool claimed = session.JobClaimed(slot);
                row.ClaimLabel.SetText(claimed ? "Done" : "+" + Effects.Gems(session.Economy.Config.DailyJobGems));
                bool can = session.JobDone(slot) && !claimed;
                if (row.Claim.interactable != can)
                    row.Claim.interactable = can;
            }

            wash.SetActive(true);
            bool free = session.CanWashFree;
            washLabel.SetText(free ? "WASH  (free)" : session.CanWashWithAd ? $"WASH  (watch ad, {session.WashesLeftToday} left)" : "Washed out for today");
            bool canWash = free || (session.CanWashWithAd && ads.IsLoaded);
            if (wash.interactable != canWash)
                wash.interactable = canWash;
        }

        static string RewardText(StreakReward reward)
        {
            var parts = new StringBuilder();
            if (reward.Gems > 0)
                parts.Append(Effects.Gems(reward.Gems)).Append("  ");
            if (!reward.Dollars.IsZero)
                parts.Append(NumberFormat.Dollars(reward.Dollars)).Append("  ");
            if (reward.Chests > 0)
                parts.Append("creek chest  ");
            if (reward.Box == 0)
                parts.Append("Green Box  ");
            if (reward.Box == 1)
                parts.Append("Orange Box  ");
            return parts.ToString().Trim();
        }

        static string JobText(DailyJobKind kind, int target)
        {
            switch (kind)
            {
                case DailyJobKind.ManualCatches: return $"Swipe up {target} by hand";
                case DailyJobKind.Nuggets: return $"Catch {target} Nuggets";
                case DailyJobKind.MotherLodes: return "Finish a Mother Lode";
                case DailyJobKind.ChestsOpened: return $"Open {target} chests";
                case DailyJobKind.UpgradesBought: return $"Buy {target} sluice upgrades";
                case DailyJobKind.Washes: return "Use the Daily Wash";
                case DailyJobKind.GearLevels: return "Level up a piece of gear";
                case DailyJobKind.CollectionStars: return "Earn a collection star";
                default: return kind.ToString();
            }
        }

        string OddsText()
        {
            double[] chances = session.Economy.Config.WashChances;
            var text = new StringBuilder("Odds:\n");
            for (int i = 0; i < chances.Length; i++)
                text.Append($"{WashNames[i]}  {chances[i] * 100:0}%\n");
            return text.ToString().TrimEnd();
        }

        JobRow NewJobRow(Transform list, int slot)
        {
            Image background = Ui.Image("Job" + slot, list, Palette.Row);
            Ui.PreferredHeight(background, 150);
            RectTransform rt = background.rectTransform;
            var row = new JobRow();
            row.Title = Ui.Label("Title", rt, "", 38, TextAnchor.UpperLeft, Palette.Text, FontStyle.Bold);
            row.Title.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 0), new Vector2(-330, -18));
            row.Detail = Ui.Label("Detail", rt, "", 32, TextAnchor.LowerLeft, Palette.TextMuted);
            row.Detail.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(28, 18), new Vector2(-330, -70));
            row.Claim = Ui.Button("Claim", rt, "", Palette.GemButton, () => ClaimJob(slot), out row.ClaimLabel, 34);
            row.Claim.AsRect().Place(new Vector2(1, 0), Vector2.one, new Vector2(-300, 20), new Vector2(-24, -20));
            return row;
        }

        static void Section(Transform list, string text)
        {
            Text label = Ui.Label(text, list, text, 38, TextAnchor.LowerLeft, Palette.TextMuted, FontStyle.Bold);
            Ui.PreferredHeight(label, 70);
        }

        void ClaimStreak()
        {
            if (session.ClaimStreak().HasValue)
                Changed?.Invoke();
        }

        void ClaimJob(int slot)
        {
            if (session.ClaimJob(slot))
                Changed?.Invoke();
        }

        void Rescue()
        {
            ads.Show("streak_rescue", rewarded =>
            {
                if (rewarded)
                    session.RescueStreak();
                ads.Load();
                Refresh();
            });
        }

        void Wash()
        {
            if (session.CanWashFree)
            {
                ShowWash(session.Wash(false));
                return;
            }
            ads.Show("daily_wash", rewarded =>
            {
                if (rewarded)
                    ShowWash(session.Wash(true));
                ads.Load();
                Refresh();
            });
        }

        void ShowWash(int outcome)
        {
            if (outcome < 0)
                return;
            washResult.SetText("The pan shows: " + WashNames[outcome]);
            Changed?.Invoke();
            Refresh();
        }
    }
}
