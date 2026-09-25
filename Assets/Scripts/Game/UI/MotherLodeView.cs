using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Mother Lode event over the creek (design doc 3.4): the screen darkens, a vein appears,
    /// and every swipe across it is a hit. Pays when the timer runs out, then shows the haul.
    /// </summary>
    public sealed class MotherLodeView
    {
        const float VeinSize = 440;
        const float HitPulseSeconds = 0.12f;

        readonly GameSession session;
        readonly RectTransform root;
        readonly RectTransform vein;
        readonly Text timer;
        readonly Text combo;
        readonly Text hits;
        readonly Text prompt;
        readonly RectTransform result;
        readonly Text resultAmount;

        MotherLodeRun run;
        Vector2? lastPointer;
        bool pointerInside;
        float pulse;

        public bool IsActive => root.gameObject.activeSelf;

        public event Action Finished;

        public MotherLodeView(GameSession session, Transform canvas, RectTransform area)
        {
            this.session = session;
            root = Ui.Image("MotherLode", canvas, new Color(0.05f, 0.04f, 0.03f, 0.88f)).rectTransform;
            root.Place(area.anchorMin, area.anchorMax, area.offsetMin, area.offsetMax);

            Text title = Ui.Label("Title", root, "MOTHER LODE!", 64, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -140), new Vector2(0, -30));
            timer = Ui.Label("Timer", root, "", 44, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            timer.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -220), new Vector2(0, -140));

            vein = Ui.Image("Vein", root, Palette.Nugget, Ui.Circle).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(VeinSize, VeinSize), new Vector2(0, 40));
            vein.GetComponent<Image>().raycastTarget = false;
            combo = Ui.Label("Combo", vein, "", 110, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            combo.rectTransform.Fill();

            hits = Ui.Label("Hits", root, "", 40, TextAnchor.MiddleCenter, Palette.TextMuted, FontStyle.Bold);
            hits.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -290), new Vector2(0, -210));
            prompt = Ui.Label("Prompt", root, "Swipe back and forth across the vein!", 40, TextAnchor.MiddleCenter, Palette.Text);
            prompt.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(40, -380), new Vector2(-40, -300));

            result = Ui.Image("Result", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(860, 560));
            Text resultTitle = Ui.Label("Title", result, "THE VEIN CRACKED OPEN", 48, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            resultTitle.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -120), new Vector2(0, -30));
            resultAmount = Ui.Label("Amount", result, "", 44, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            resultAmount.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(30, 200), new Vector2(-30, -130));
            Ui.Button("LodeCollect", result, "Collect", Palette.Button, Close, out _, 44).AsRect()
                .Box(new Vector2(0.5f, 0), new Vector2(420, 140), new Vector2(0, 40));

            root.SetActive(false);
        }

        public void Begin(MotherLodeRun newRun)
        {
            run = newRun;
            lastPointer = null;
            pointerInside = false;
            root.SetActive(true);
            result.SetActive(false);
            vein.SetActive(true);
            SetRunLabelsActive(true);
            RefreshLabels();
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || run == null || run.IsPaid)
                return;

            run.Tick(deltaTime);
            HandleSwipe();
            pulse = Mathf.Max(0, pulse - deltaTime);
            float scale = 1 + 0.08f * (pulse / HitPulseSeconds) + 0.03f * Mathf.Sin(Time.time * 6);
            vein.localScale = Vector3.one * scale;
            RefreshLabels();

            if (run.IsOver)
                Finish();
        }

        /// <summary>Pays a run that is still going (app sent to background); the haul is shown on return.</summary>
        public void FinishNow()
        {
            if (IsActive && run != null && !run.IsPaid)
                Finish();
        }

        void Finish()
        {
            int peak = run.PeakCombo;
            int hitCount = run.Hits;
            BigNumber reward = session.FinishMotherLode(run);
            int gems = session.Economy.Config.MotherLodeGemReward;
            vein.SetActive(false);
            SetRunLabelsActive(false);
            result.SetActive(true);
            resultAmount.SetText($"+{NumberFormat.Dollars(reward)}\n+{Effects.Gems(gems)}\n\n{hitCount} hits, best combo x{peak}");
            Finished?.Invoke();
        }

        void Close()
        {
            root.SetActive(false);
            run = null;
        }

        void SetRunLabelsActive(bool active)
        {
            timer.SetActive(active);
            hits.SetActive(active);
            prompt.SetActive(active);
        }

        void RefreshLabels()
        {
            timer.SetText($"{Mathf.CeilToInt((float)run.SecondsLeft)}s");
            combo.SetText($"x{run.Combo}");
            hits.SetText($"{run.Hits} hits");
        }

        void HandleSwipe()
        {
            if (!Input.GetMouseButton(0))
            {
                lastPointer = null;
                pointerInside = false;
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, null, out Vector2 point);
            Vector2 from = lastPointer ?? point;
            lastPointer = point;

            Vector2 center = vein.anchoredPosition + (Vector2)root.rect.center;
            float radius = VeinSize / 2;
            // A fast swipe can cross the whole vein between two frames; the segment still counts.
            bool crossed = DistanceToSegment(center, from, point) <= radius;
            if (crossed && !pointerInside)
            {
                run.Hit();
                pulse = HitPulseSeconds;
            }
            pointerInside = Vector2.Distance(center, point) <= radius;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared < 0.0001f)
                return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
