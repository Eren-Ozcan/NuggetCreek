using System;
using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>Where a tutorial cue is on screen right now.</summary>
    public sealed class CueTarget
    {
        /// <summary>The clear hole: the control itself, or the card around it.</summary>
        public RectTransform Frame;

        /// <summary>The controls a pressing step waits for; only these take a tap.</summary>
        public readonly List<Button> Buttons = new List<Button>();

        /// <summary>The whole frame takes taps, for a button whose tap area reaches past its own rect.</summary>
        public bool FramePasses;

        /// <summary>Where the hand points, when not at the first button.</summary>
        public RectTransform Pointer;

        /// <summary>A spot on the water followed every frame (gold, a floater), in world space; null when gone.</summary>
        public Func<Vector3?> Spot;

        /// <summary>The spot's hole side, in canvas units.</summary>
        public float SpotSize;

        public static CueTarget Of(Button button, RectTransform frame = null) =>
            Of(new[] { button }, frame ?? button.AsRect());

        public static CueTarget Of(IEnumerable<Button> buttons, RectTransform frame)
        {
            var target = new CueTarget { Frame = frame };
            target.Buttons.AddRange(buttons);
            return target;
        }

        public static CueTarget Highlight(RectTransform frame) => new CueTarget { Frame = frame };

        public static CueTarget At(Func<Vector3?> spot, float size) => new CueTarget { Spot = spot, SpotSize = size };
    }

    /// <summary>
    /// Old Pete's lessons (design doc 9.1, 14.4), played as a guided overlay: the screen shades
    /// over except for what Pete points at, Pete stands large beside a speech panel, and a
    /// gloved hand shows the tap or the swipe. Nothing can be skipped: a talking step waits for
    /// a tap, a pointing step lets only the cued control take one, a river step waits for the
    /// catches. The script is <see cref="Lessons"/>; the game says where each cue is on screen.
    /// A lesson pauses while something else has the screen (the Mother Lode, the offline haul)
    /// and is saved as seen only once its last step is done.
    /// </summary>
    public sealed class PeteTutorial
    {
        const float CharsPerSecond = 55;
        const float EnterSeconds = 0.4f;
        const float Margin = 24;
        const float PeteWidth = 380;
        const float PanelHeight = 230;
        const float CompactHeight = 210;
        /// <summary>How much of Pete's waist the speech panel covers, hiding the cut at the bottom of the drawing.</summary>
        const float PeteBehindPanel = 110;
        const float ScreenEdge = 26;
        const float HolePadding = 18;
        const float HandWidth = 150;
        const float TapCycle = 1.1f;
        const float TapReach = 56;
        const float SwipeSeconds = 1.4f;
        const float SwipeLength = 380;

        readonly GameSession session;
        readonly Func<LessonStep, CueTarget> find;
        readonly RectTransform root;
        readonly TutorialDim dim;
        readonly Image frame;
        readonly RectTransform stage;
        readonly Image pete;
        readonly float peteHeight;
        readonly RectTransform panel;
        readonly RectTransform namePlate;
        readonly Image face;
        readonly Text text;
        readonly Text more;
        readonly RectTransform hand;
        readonly Image handImage;
        readonly HashSet<Button> watched = new HashSet<Button>();
        readonly Vector3[] corners = new Vector3[4];

        PeteLine? lesson;
        IReadOnlyList<LessonStep> steps;
        int index;
        LessonStep step;
        CueTarget target;
        bool pressed;
        bool tapped;
        long catchesAtStart;
        float stepAge;
        float lessonAge;
        float typed;
        float poseAge;
        bool compact;

        /// <summary>Raised after a lesson's last step, once it is saved as seen.</summary>
        public event Action<PeteLine> Finished;

        public bool IsActive => lesson.HasValue;

        /// <summary>The lesson playing, if any.</summary>
        public PeteLine? Lesson => lesson;

        /// <summary>What Pete is saying now, in full; null between lessons.</summary>
        public string Line => step?.Line;

        /// <summary>A river step: the creek takes swipes and taps, the floater waits.</summary>
        public bool RiverOpen => step != null && (step.Until == Until.Catch || step.Until == Until.Floater);

        /// <param name="find">Where a step's cue is on screen now; null when it is not there.</param>
        public PeteTutorial(GameSession session, Transform canvas, Func<LessonStep, CueTarget> find)
        {
            this.session = session;
            this.find = find;
            root = Ui.Rect("Tutorial", canvas).Fill();

            // The shade takes every tap that is not meant for the cued control.
            var shade = new GameObject("TutorialTap", typeof(RectTransform), typeof(CanvasRenderer));
            shade.transform.SetParent(root, false);
            dim = shade.AddComponent<TutorialDim>();
            dim.rectTransform.Fill();
            dim.color = Palette.Veil;
            var tap = shade.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.navigation = new Navigation { mode = Navigation.Mode.None };
            tap.onClick.AddListener(OnTap);

            frame = Ui.Image("Frame", root, Palette.TextLight, Ui.Frame);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;

            stage = Ui.Rect("Stage", root);
            Sprite drawing = Art.Pete(PetePose.Talk);
            if (drawing != null)
            {
                pete = Ui.Image("Pete", stage, Color.white, drawing);
                pete.raycastTarget = false;
                pete.preserveAspect = true;
                peteHeight = PeteWidth * drawing.rect.height / drawing.rect.width;
                pete.rectTransform.Box(Vector2.zero, new Vector2(PeteWidth, peteHeight));
            }

            Image speech = Ui.Panel("Speech", stage, Palette.Panel);
            speech.raycastTarget = false;
            panel = speech.rectTransform;
            var shadow = speech.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.35f);
            shadow.effectDistance = new Vector2(0, -10);

            Image plate = Ui.Panel("NamePlate", panel, Palette.Button);
            plate.raycastTarget = false;
            namePlate = plate.rectTransform;
            Text name = Ui.Title("Name", namePlate, "OLD PETE", 34, Palette.TextLight);
            name.rectTransform.Fill(6);

            // In the small panel over the river, Pete is a face inside the panel instead.
            face = Ui.Icon("Face", panel, drawing ?? Art.Portrait("old_pete_portrait"));
            face?.rectTransform.Box(new Vector2(0, 0), new Vector2(160, CompactHeight - 30), new Vector2(18, 10));

            text = Ui.Label("Text", panel, "", 42, TextAnchor.UpperLeft, Palette.Text);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 28;
            text.resizeTextMaxSize = Mathf.RoundToInt(42 * Ui.TextScale);
            text.verticalOverflow = VerticalWrapMode.Truncate;
            more = Ui.Label("More", panel, "Tap to continue", 28, TextAnchor.LowerRight, Palette.TextMuted);
            more.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(36, 14), new Vector2(-30, -14));

            hand = Ui.Rect("Hand", root);
            Sprite glove = Art.Hand;
            handImage = hand.gameObject.AddComponent<Image>();
            handImage.raycastTarget = false;
            if (glove != null)
            {
                handImage.sprite = glove;
                hand.sizeDelta = new Vector2(HandWidth, HandWidth * glove.rect.height / glove.rect.width);
                hand.pivot = Art.HandTip;
            }
            else
            {
                // Without the drawing, a white dot marks the spot.
                handImage.sprite = Ui.Circle;
                hand.sizeDelta = new Vector2(70, 70);
                hand.pivot = new Vector2(0.5f, 0.5f);
            }

            root.SetActive(false);
        }

        /// <summary>
        /// Starts the next due lesson when the screen is free and plays the current one.
        /// </summary>
        /// <param name="busy">Something else has the screen: a lesson waits, one under way pauses.</param>
        /// <param name="canStart">No panel is open that a lesson would cover.</param>
        public void Tick(float deltaTime, bool busy, bool canStart)
        {
            if (!lesson.HasValue)
            {
                if (busy || !canStart || !(session.NextPeteLine() is PeteLine next))
                {
                    Show(false);
                    return;
                }
                Begin(next);
                if (!lesson.HasValue)
                    return;
            }
            if (busy)
            {
                Show(false);
                return;
            }

            lessonAge += deltaTime;
            stepAge += deltaTime;
            poseAge += deltaTime;
            target = find(step);
            if (target == null && NeedsTarget(step))
            {
                // The control went away (a candidate offer ran out): this step has nothing left to do.
                Advance();
                if (!lesson.HasValue)
                    return;
            }
            Watch();
            if (IsDone())
            {
                Advance();
                if (!lesson.HasValue)
                    return;
            }

            Show(true);
            typed = Mathf.Min(step.Line.Length, typed + deltaTime * CharsPerSecond);
            text.SetText(step.Line.Substring(0, (int)typed));
            Layout();
        }

        /// <summary>Development only: drops the lesson under way without saving it.</summary>
        public void Stop()
        {
            Unwatch();
            lesson = null;
            step = null;
            target = null;
            Show(false);
        }

        void Begin(PeteLine line)
        {
            lesson = line;
            steps = Lessons.Of(line);
            index = -1;
            lessonAge = 0;
            Advance();
        }

        void Advance()
        {
            Unwatch();
            index++;
            // A step whose control is not on screen is passed over: the goal is not done yet,
            // the panel it opens is open already.
            while (index < steps.Count)
            {
                step = steps[index];
                target = find(step);
                if (target != null || !NeedsTarget(step))
                    break;
                index++;
            }
            if (index >= steps.Count)
            {
                Finish();
                return;
            }
            stepAge = 0;
            typed = 0;
            pressed = false;
            tapped = false;
            catchesAtStart = session.Progress.ManualCollected;
            if (pete != null && pete.sprite != Art.Pete(step.Pose) && Art.Pete(step.Pose) != null)
            {
                pete.sprite = Art.Pete(step.Pose);
                poseAge = 0;
            }
            if (face != null && Art.Pete(step.Pose) != null)
                face.sprite = Art.Pete(step.Pose);
            if (target?.Frame != null)
                Ui.ScrollIntoView(target.Frame);
            Watch();
        }

        void Finish()
        {
            Unwatch();
            PeteLine done = lesson.Value;
            session.MarkSeen(done);
            lesson = null;
            step = null;
            target = null;
            Show(false);
            Finished?.Invoke(done);
        }

        static bool NeedsTarget(LessonStep step) => step.Until == Until.Press || step.Until == Until.Floater;

        bool IsDone()
        {
            switch (step.Until)
            {
                case Until.Tap: return tapped;
                case Until.Press: return pressed;
                case Until.Catch: return session.Progress.ManualCollected - catchesAtStart >= step.Count;
                case Until.Floater: return session.FloaterOnWater == null;
                default: return true;
            }
        }

        void OnTap()
        {
            if (step == null)
                return;
            if (typed < step.Line.Length)
            {
                typed = step.Line.Length;
                return;
            }
            if (step.Until == Until.Tap)
            {
                tapped = true;
                Sound.Play(Sfx.Tap);
            }
        }

        void OnPressed() => pressed = true;

        void Watch()
        {
            if (step == null || step.Until != Until.Press || target == null)
                return;
            foreach (Button button in target.Buttons)
                if (button != null && watched.Add(button))
                    button.onClick.AddListener(OnPressed);
        }

        void Unwatch()
        {
            foreach (Button button in watched)
                if (button != null)
                    button.onClick.RemoveListener(OnPressed);
            watched.Clear();
        }

        void Show(bool show)
        {
            if (root.gameObject.activeSelf == show)
                return;
            root.gameObject.SetActive(show);
            // Over any panel the lesson opened.
            if (show)
                root.SetAsLastSibling();
        }

        // --- Layout and motion ---

        void Layout()
        {
            Rect screen = root.rect;
            Rect? hole = HoleRect();
            Vector2? point = PointAt(hole);

            // The shade: none while swiping for gold, so the water reads clearly.
            dim.color = step.Until == Until.Catch ? Color.clear : Palette.Veil;
            dim.SetHole(hole);
            dim.PassRects.Clear();
            if (step.Until == Until.Press && target != null)
            {
                foreach (Button button in target.Buttons)
                    if (button != null && button.gameObject.activeInHierarchy)
                        dim.PassRects.Add(LocalRect(button.AsRect()));
                if (target.FramePasses && target.Frame != null)
                    dim.PassRects.Add(LocalRect(target.Frame));
            }

            // A ring that breathes around the hole.
            frame.gameObject.SetActive(hole.HasValue);
            if (hole is Rect h)
            {
                float grow = 6 + 8 * (0.5f + 0.5f * Mathf.Sin(stepAge * 5));
                frame.rectTransform.Box(new Vector2(0.5f, 0.5f), h.size + 2 * grow * Vector2.one);
                frame.rectTransform.anchoredPosition = h.center - screen.center;
                frame.color = new Color(1, 1, 1, 0.75f + 0.25f * Mathf.Sin(stepAge * 5));
            }

            PlaceStage(screen, hole);
            AnimateHand(screen, point);
            more.gameObject.SetActive(step.Until == Until.Tap && typed >= step.Line.Length);
            more.color = new Color(Palette.TextMuted.r, Palette.TextMuted.g, Palette.TextMuted.b, 0.55f + 0.45f * Mathf.Sin(stepAge * 4));
        }

        Rect? HoleRect()
        {
            if (target == null)
                return null;
            if (target.Frame != null && target.Frame.gameObject.activeInHierarchy)
            {
                Rect r = LocalRect(target.Frame);
                return new Rect(r.x - HolePadding, r.y - HolePadding, r.width + 2 * HolePadding, r.height + 2 * HolePadding);
            }
            // Gold has no hole: the whole river is open while swiping.
            if (target.Spot != null && step.Until != Until.Catch && target.Spot() is Vector3 spot)
            {
                Vector2 c = Local(spot);
                float side = target.SpotSize + 2 * HolePadding;
                return new Rect(c.x - side / 2, c.y - side / 2, side, side);
            }
            return null;
        }

        /// <summary>Where the fingertip goes: the cued button, else the middle of the hole or the spot.</summary>
        Vector2? PointAt(Rect? hole)
        {
            if (target == null)
                return null;
            if (target.Spot != null)
                return target.Spot() is Vector3 spot ? Local(spot) : (Vector2?)null;
            if (target.Pointer != null && target.Pointer.gameObject.activeInHierarchy)
                return LocalRect(target.Pointer).center;
            foreach (Button button in target.Buttons)
                if (button != null && button.gameObject.activeInHierarchy)
                    return LocalRect(button.AsRect()).center;
            return hole?.center;
        }

        void PlaceStage(Rect screen, Rect? hole)
        {
            float full = PanelHeight + (pete != null ? peteHeight - PeteBehindPanel : 0);
            bool top;
            if (RiverOpen)
            {
                compact = true;
                top = true;
            }
            else if (!(hole is Rect h))
            {
                compact = false;
                top = false;
            }
            else
            {
                // Pete stands on the far side from what he points at, small if it does not fit.
                float above = screen.yMax - h.yMax - ScreenEdge;
                float below = h.yMin - screen.yMin - ScreenEdge;
                bool lowTarget = h.center.y < screen.center.y;
                float near = lowTarget ? above : below;
                float far = lowTarget ? below : above;
                if (near >= full)
                    top = lowTarget;
                else if (far >= full)
                    top = !lowTarget;
                else
                    top = above >= below;
                compact = (top ? above : below) < full;
            }

            float height = compact ? CompactHeight : full;
            if (top)
                stage.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -ScreenEdge - height), new Vector2(0, -ScreenEdge));
            else
                stage.Place(Vector2.zero, new Vector2(1, 0), new Vector2(0, ScreenEdge), new Vector2(0, ScreenEdge + height));

            float panelHeight = compact ? CompactHeight : PanelHeight;
            panel.Place(Vector2.zero, new Vector2(1, 0), new Vector2(Margin, 0), new Vector2(-Margin, panelHeight));
            float textLeft = compact && face != null ? 196 : 40;
            text.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(textLeft, 52), new Vector2(-36, -44));
            face?.gameObject.SetActive(compact);

            float plateLeft = compact ? textLeft - 6 : (pete != null ? PeteWidth + Margin : 40);
            namePlate.Box(new Vector2(0, 1), new Vector2(240, 62), new Vector2(plateLeft, 30));

            float enter = Motion.EaseOutBack(Mathf.Clamp01(lessonAge / EnterSeconds));
            panel.localScale = new Vector3(1, Mathf.Lerp(0.6f, 1, enter), 1);
            if (pete != null)
            {
                pete.gameObject.SetActive(!compact);
                float slide = Mathf.LerpUnclamped(-PeteWidth - Margin, Margin + 8, enter);
                pete.rectTransform.anchoredPosition = new Vector2(slide, panelHeight - PeteBehindPanel);
                float pop = Motion.Punch(poseAge, 0.3f, 1.05f);
                pete.rectTransform.localScale = new Vector3(pop, pop * Motion.Bob(lessonAge, 0.012f), 1);
            }
        }

        void AnimateHand(Rect screen, Vector2? point)
        {
            bool show = point.HasValue && (step.Cue != Cue.None);
            hand.gameObject.SetActive(show);
            if (!show)
                return;
            Vector2 at = point.Value;
            if (step.Until == Until.Catch)
            {
                // Strokes across the gold, fading in and out at the ends.
                float k = (stepAge % SwipeSeconds) / SwipeSeconds;
                float move = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k / 0.7f));
                Vector2 along = new Vector2(SwipeLength, 120);
                hand.anchoredPosition = at - along / 2 + along * move - screen.center;
                hand.localRotation = Quaternion.Euler(0, 0, 20);
                hand.localScale = Vector3.one;
                handImage.color = new Color(1, 1, 1, Mathf.Clamp01(k / 0.12f) * Mathf.Clamp01((0.95f - k) / 0.2f));
                return;
            }
            // Near the bottom of the screen the hand reaches down from above.
            bool fromAbove = at.y < screen.yMin + 0.3f * screen.height;
            float angle = fromAbove ? 200 : 20;
            Vector2 finger = Quaternion.Euler(0, 0, angle) * Vector2.up;
            float t = (stepAge % TapCycle) / TapCycle;
            float reach = t < 0.55f ? Mathf.SmoothStep(1, 0, t / 0.55f) : t < 0.7f ? 0 : Mathf.SmoothStep(0, 1, (t - 0.7f) / 0.3f);
            float squeeze = t >= 0.55f && t < 0.7f ? 0.9f : 1;
            hand.anchoredPosition = at - finger * (TapReach * reach) - screen.center;
            hand.localRotation = Quaternion.Euler(0, 0, angle);
            hand.localScale = new Vector3(squeeze, squeeze, 1);
            handImage.color = Color.white;
        }

        Vector2 Local(Vector3 world) => root.InverseTransformPoint(world);

        Rect LocalRect(RectTransform rect)
        {
            rect.GetWorldCorners(corners);
            Vector2 min = Local(corners[0]);
            Vector2 max = Local(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }
    }
}
