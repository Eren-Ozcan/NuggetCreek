using System;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// Mother Lode event over the creek (design doc 3.4): the screen darkens, a boulder appears,
    /// and every swipe across it is a hit. The boulder cracks and then shatters as the damage
    /// grows. Pays when the timer runs out, then shows the haul.
    /// </summary>
    public sealed class MotherLodeView
    {
        const float BoulderSize = 440;
        const float HitPulseSeconds = 0.12f;
        const float StagePulseSeconds = 0.3f;
        static readonly string[] BoulderSprites = { "MotherLode/boulder_0", "MotherLode/boulder_1", "MotherLode/boulder_2" };

        readonly GameSession session;
        readonly RectTransform root;
        readonly RectTransform boulder;
        readonly Image boulderImage;
        readonly Sprite[] boulders = new Sprite[3];
        readonly Text timer;
        readonly Text combo;
        readonly Text hits;
        readonly Text prompt;
        readonly Text title;
        readonly Image bossBar;
        readonly RectTransform bossBarBack;
        readonly RectTransform result;
        readonly Text resultAmount;

        MotherLodeRun run;
        Vector2? lastPointer;
        bool pointerInside;
        float pulse;
        float stagePulse;
        int stage;

        public bool IsActive => root.gameObject.activeSelf;

        public event Action Finished;

        public MotherLodeView(GameSession session, Transform canvas, RectTransform area)
        {
            this.session = session;
            root = Ui.Image("MotherLode", canvas, new Color(0.05f, 0.04f, 0.03f, 0.88f)).rectTransform;
            root.Place(area.anchorMin, area.anchorMax, area.offsetMin, area.offsetMax);

            title = Ui.Title("Title", root, "MOTHER LODE!", 64, Palette.Gold);
            title.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(30, -140), new Vector2(-30, -30));
            // Boss names are long in the wide heading font; shrink rather than spill.
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 36;
            title.resizeTextMaxSize = 64;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            timer = Ui.Label("Timer", root, "", 44, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            timer.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -220), new Vector2(0, -140));

            // Boss health (design doc 3.4.1): shrinks as the combo hits land.
            bossBarBack = Ui.Image("BossBar", root, Palette.Text).rectTransform;
            bossBarBack.Place(new Vector2(0, 1), Vector2.one, new Vector2(80, -290), new Vector2(-80, -240));
            bossBar = Ui.Image("Health", bossBarBack, Palette.GiantNugget);
            bossBar.rectTransform.Fill();

            for (int i = 0; i < boulders.Length; i++)
                boulders[i] = Art.Get(BoulderSprites[i]);
            boulderImage = Ui.Icon("Boulder", root, boulders[0], Palette.Nugget);
            boulder = boulderImage.rectTransform.Box(new Vector2(0.5f, 0.5f), new Vector2(BoulderSize, BoulderSize), new Vector2(0, 40));
            combo = Ui.Label("Combo", boulder, "", 110, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            combo.rectTransform.Fill();
            // The combo sits on the rock art; an outline keeps it readable.
            var outline = combo.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.85f);
            outline.effectDistance = new Vector2(4, -4);

            hits = Ui.Label("Hits", root, "", 40, TextAnchor.MiddleCenter, new Color32(0xE0, 0xCF, 0xB0, 0xFF), FontStyle.Bold);
            hits.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -290), new Vector2(0, -210));
            prompt = Ui.Label("Prompt", root, "Swipe back and forth across the boulder!", 40, TextAnchor.MiddleCenter, Palette.TextLight);
            prompt.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(40, -380), new Vector2(-40, -300));

            result = Ui.Panel("Result", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0.5f), new Vector2(860, 560));
            Text resultTitle = Ui.Title("Title", result, "THE BOULDER CRACKED OPEN", 48);
            resultTitle.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -120), new Vector2(0, -30));
            resultAmount = Ui.Label("Amount", result, "", 44, TextAnchor.MiddleCenter, Palette.GoldText, FontStyle.Bold);
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
            boulder.SetActive(true);
            SetRunLabelsActive(true);
            stage = 0;
            stagePulse = 0;
            SetBoulder(0);
            bossBarBack.SetActive(run.IsBossFight);
            title.SetText(run.IsBossFight ? $"BOSS: {GameCatalog.Nuggets[GameCatalog.BossNugget(run.BossRegion)].Name.ToUpperInvariant()}" : "MOTHER LODE!");
            RefreshLabels();
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive || run == null || run.IsPaid)
                return;

            run.Tick(deltaTime);
            HandleSwipe();
            pulse = Mathf.Max(0, pulse - deltaTime);
            stagePulse = Mathf.Max(0, stagePulse - deltaTime);
            int damageStage = DamageStage();
            if (damageStage > stage)
            {
                stage = damageStage;
                stagePulse = StagePulseSeconds;
                SetBoulder(stage);
            }
            float scale = 1 + 0.08f * (pulse / HitPulseSeconds) + 0.03f * Mathf.Sin(Time.time * 6)
                + 0.12f * (stagePulse / StagePulseSeconds);
            boulder.localScale = Vector3.one * scale;
            boulder.localRotation = Quaternion.Euler(0, 0, 4 * (pulse / HitPulseSeconds) * Mathf.Sin(Time.time * 60));
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
            boulder.SetActive(false);
            SetRunLabelsActive(false);
            bossBarBack.SetActive(false);
            result.SetActive(true);
            string haul = $"+{NumberFormat.Dollars(reward)}\n+{Effects.Gems(gems)}\n\n{hitCount} hits, best combo x{peak}";
            if (run.DroppedNugget >= 0)
                haul = $"NEW BOSS NUGGET: {GameCatalog.Nuggets[run.DroppedNugget].Name}\n" + haul;
            else if (run.IsBossFight)
                haul = "The boss got away. Grow stronger and try again.\n" + haul;
            resultAmount.SetText(haul);
            Finished?.Invoke();
        }

        void Close()
        {
            root.SetActive(false);
            run = null;
        }

        /// <summary>
        /// Boulder damage: 0 whole, 1 cracked, 2 shattered. A boss boulder follows its health;
        /// a plain Mother Lode follows the best combo, so the rock never heals when a combo drops.
        /// </summary>
        int DamageStage()
        {
            float damage;
            if (run.IsBossFight)
                damage = run.BossHealth > 0 ? Mathf.Clamp01((float)(run.BossDamage / run.BossHealth)) : 0;
            else
                damage = run.MaxCombo > 1 ? (run.PeakCombo - 1f) / (run.MaxCombo - 1) : 0;
            return damage >= 2f / 3 ? 2 : damage >= 1f / 3 ? 1 : 0;
        }

        void SetBoulder(int damageStage)
        {
            Sprite sprite = boulders[damageStage];
            if (sprite != null)
                boulderImage.sprite = sprite;
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
            if (run.IsBossFight)
            {
                float left = 1 - Mathf.Clamp01((float)(run.BossDamage / run.BossHealth));
                bossBar.rectTransform.anchorMax = new Vector2(left, 1);
            }
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

            Vector2 center = boulder.anchoredPosition + (Vector2)root.rect.center;
            float radius = BoulderSize / 2;
            // A fast swipe can cross the whole boulder between two frames; the segment still counts.
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
