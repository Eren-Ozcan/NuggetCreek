using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuggetCreek.Game.UI
{
    /// <summary>
    /// The moving pieces on the shown tier's drawing, as the sprite tool cut them (parts.json):
    /// bucket chains, belts and wheels that slide along, buckets and the sweep ring that dip into
    /// the water, smoke from the chimneys and drips from the spray pipes. A grab bucket opens its
    /// jaws on the way down, bites shut on the bottom and lets the water out once it is up, and the
    /// crane it hangs from swings it over to the other side between dips. Trommel drums turn.
    /// Without the file the dredge simply stands still.
    /// </summary>
    public sealed class DredgeParts
    {
        const float DipEvery = 5.5f;
        const float DipDown = 0.6f;
        const float DipHold = 1.0f;
        const float DipUp = 0.7f;
        const float DipDepth = 0.78f;
        const float SmokeEvery = 0.22f;
        const float SprayEvery = 0.12f;
        const float BiteSeconds = 0.35f;
        /// <summary>The grab opens this long before it drops, so it goes down open.</summary>
        const float OpenSeconds = 0.5f;
        /// <summary>Hanging, it lets the water out through half-open jaws once.</summary>
        const float DrainAt = 3.2f;
        const float DrainSeconds = 0.9f;
        /// <summary>The crane swings the grab over once it has settled after a dip, and is still before the jaws open.</summary>
        const float SwingFrom = DipDown + DipHold + DipUp + 0.2f;
        const float SwingTo = DipEvery - OpenSeconds - 0.1f;
        /// <summary>A drum's holes fade out between these cosines of their angle from its crest, before its outline.</summary>
        const float HoleFull = 0.4f;
        const float HoleGone = 0.2f;

        sealed class Flow
        {
            public RawImage Image;
            public float Rate;
            public float Offset;
        }

        sealed class Arm
        {
            public RectTransform Rect;
            public string File;
            public float Swing;
            /// <summary>The grab hanging from it, whose dips set its pace; null when nothing hangs from it.</summary>
            public Dip Load;
        }

        sealed class Dip
        {
            public RectTransform Rect;
            public readonly List<Image> Images = new List<Image>();
            public RectTransform LeftJaw;
            public RectTransform RightJaw;
            public float JawOpen;
            public float JawShut;
            /// <summary>Where the water pours out between the jaws, as a fraction of the part's width.</summary>
            public float MouthX;
            public float Phase;
            public bool Splashed;
            public string On;
            public Arm Arm;
            /// <summary>Where it hangs from, as a fraction of the drawing.</summary>
            public Vector2 HangAt;
            /// <summary>The way the crane swings it next, 1 or -1.</summary>
            public float Side = 1;
            public RectTransform Tether;
            /// <summary>The tether's ends: on the part, and on the drawing, as fractions from their bottom-left corners.</summary>
            public Vector2 TetherFrom;
            public Vector2 TetherTo;
            /// <summary>The tether's width as a share of the drawing's height.</summary>
            public float TetherWidth;
        }

        sealed class Drum
        {
            public RectTransform Rect;
            /// <summary>Degrees a second.</summary>
            public float Speed;
            public float Turn;
            /// <summary>How far a row drops at the sides, as a fraction of the drum's height.</summary>
            public float Sag;
            /// <summary>A hole's width and height as fractions of the drum.</summary>
            public Vector2 HoleSize;
            /// <summary>Each hole's angle from the crest, and its height on the crest as a fraction from the bottom.</summary>
            public float[] Angles;
            public float[] Heights;
            public Image[] Holes;
        }

        readonly RectTransform art;
        readonly Particles particles;
        readonly Func<Vector3, Vector2> toArea;
        readonly List<GameObject> built = new List<GameObject>();
        readonly List<Flow> flows = new List<Flow>();
        readonly List<Dip> dips = new List<Dip>();
        readonly List<Arm> arms = new List<Arm>();
        readonly List<Drum> drums = new List<Drum>();
        Vector2[] smoke = new Vector2[0];
        Vector2[] spray = new Vector2[0];
        float smokeIn;
        float sprayIn;
        float time;

        public DredgeParts(RectTransform art, Particles particles, Func<Vector3, Vector2> toArea)
        {
            this.art = art;
            this.particles = particles;
            this.toArea = toArea;
        }

        /// <summary>Builds the pieces of a tier's drawing, or nothing without the drawing or its parts.</summary>
        public void Show(int tier, bool hasArt)
        {
            foreach (GameObject go in built)
                UnityEngine.Object.Destroy(go);
            built.Clear();
            flows.Clear();
            dips.Clear();
            arms.Clear();
            drums.Clear();
            DredgeTierParts entry = hasArt ? Art.DredgeParts(tier) : null;
            smoke = Points(entry?.smoke);
            spray = Points(entry?.spray);
            if (entry?.parts == null)
                return;

            foreach (DredgePart part in entry.parts)
            {
                Sprite sprite = Art.DredgePart(part.kind == "drum" ? part.file + "_hole" : part.file);
                if (sprite == null)
                    continue;
                RectTransform rect;
                if (part.kind == "flow")
                {
                    var image = Ui.Rect(part.file, art).gameObject.AddComponent<RawImage>();
                    image.texture = sprite.texture;
                    image.raycastTarget = false;
                    image.uvRect = new Rect(0, 0, 1, part.repeat);
                    flows.Add(new Flow { Image = image, Rate = part.speed / sprite.texture.height });
                    rect = image.rectTransform;
                }
                else if (part.kind == "arm")
                {
                    // The frame covers the whole drawing and turns about the crane's turntable.
                    RectTransform frame = Ui.Rect(part.file, art).Fill();
                    frame.pivot = new Vector2(Mathf.Lerp(part.x0, part.x1, part.pivotX), Mathf.Lerp(part.y0, part.y1, part.pivotY));
                    built.Add(frame.gameObject);
                    arms.Add(new Arm { Rect = frame, File = part.file, Swing = part.swing });
                    Image image = Ui.Image("Crane", frame, Color.white, sprite);
                    image.raycastTarget = false;
                    rect = image.rectTransform;
                }
                else if (part.kind == "drum")
                {
                    if (part.holes == null || part.holeSize == null || part.holeSize.Length < 2)
                        continue;
                    rect = BuildDrum(part, sprite);
                }
                else
                {
                    var dip = new Dip
                    {
                        Phase = UnityEngine.Random.Range(0, DipEvery),
                        On = part.on,
                        HangAt = new Vector2(Mathf.Lerp(part.x0, part.x1, part.pivotX), Mathf.Lerp(part.y0, part.y1, part.pivotY)),
                    };
                    rect = dip.Rect = Ui.Rect(part.file, art);
                    if (!(part.jaws && BuildJaws(dip, part)))
                        dip.Images.Add(Piece("Bucket", rect, sprite, new Vector2(0.5f, 0.5f)));
                    BuildTether(dip, part);
                    dips.Add(dip);
                }
                rect.anchorMin = new Vector2(part.x0, part.y0);
                rect.anchorMax = new Vector2(part.x1, part.y1);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                rect.pivot = new Vector2(part.pivotX, part.pivotY);
                built.Add(rect.gameObject);
            }

            // Grabs hang from their crane, which comes before them; a tether lies under both.
            foreach (Dip dip in dips)
            {
                dip.Arm = string.IsNullOrEmpty(dip.On) ? null : arms.Find(arm => arm.File == dip.On);
                if (dip.Arm != null)
                    dip.Arm.Load = dip;
                if (dip.Tether != null)
                    dip.Tether.SetSiblingIndex((dip.Arm != null ? dip.Arm.Rect : dip.Rect).GetSiblingIndex());
            }
        }

        public void Tick(float deltaTime)
        {
            time += deltaTime;
            // Content moves down the screen, so the window climbs up the texture.
            foreach (Flow flow in flows)
            {
                flow.Offset = Mathf.Repeat(flow.Offset + flow.Rate * deltaTime, 1);
                Rect uv = flow.Image.uvRect;
                uv.y = flow.Offset;
                flow.Image.uvRect = uv;
            }
            foreach (Dip dip in dips)
                TickDip(dip, deltaTime);
            // A crane with nothing hanging from it sways on its own.
            foreach (Arm arm in arms)
            {
                if (arm.Load == null)
                    arm.Rect.localRotation = Quaternion.Euler(0, 0, arm.Swing * Mathf.Sin(time * Mathf.PI / DipEvery));
            }
            foreach (Drum drum in drums)
                TickDrum(drum, deltaTime);

            smokeIn -= deltaTime;
            if (smokeIn <= 0)
            {
                smokeIn = SmokeEvery;
                foreach (Vector2 point in smoke)
                {
                    // Puffs drift downstream and spread as they thin out.
                    var velocity = new Vector2(UnityEngine.Random.Range(8, 30), UnityEngine.Random.Range(-70, -45));
                    particles.Emit(AreaPoint(point), velocity, UnityEngine.Random.Range(50, 70), 2.4f, Palette.Smoke, Ui.Glow,
                        0, UnityEngine.Random.Range(-30f, 30f), 2.6f);
                }
            }
            sprayIn -= deltaTime;
            if (sprayIn <= 0)
            {
                sprayIn = SprayEvery;
                foreach (Vector2 point in spray)
                {
                    var velocity = new Vector2(UnityEngine.Random.Range(-25, 25), -60);
                    particles.Emit(AreaPoint(point), velocity, UnityEngine.Random.Range(7, 11), 0.35f, Palette.Droplet, Ui.Circle, -300);
                }
            }
        }

        /// <summary>
        /// Builds a grab from its three layers, each the size of the part: the two halves swing
        /// about their own hinges under the fixed layer (head, centre column, side arm), which is
        /// drawn last so it covers where they meet it. False when a layer is missing.
        /// </summary>
        bool BuildJaws(Dip dip, DredgePart part)
        {
            Sprite leftSprite = Art.DredgePart(part.file + "_left");
            Sprite rightSprite = Art.DredgePart(part.file + "_right");
            Sprite fixedSprite = Art.DredgePart(part.file + "_fixed");
            if (leftSprite == null || rightSprite == null || fixedSprite == null || part.hinges == null || part.hinges.Length < 4)
                return false;
            Image left = Piece("LeftJaw", dip.Rect, leftSprite, new Vector2(part.hinges[0], part.hinges[1]));
            Image right = Piece("RightJaw", dip.Rect, rightSprite, new Vector2(part.hinges[2], part.hinges[3]));
            dip.Images.Add(left);
            dip.Images.Add(right);
            dip.Images.Add(Piece("Head", dip.Rect, fixedSprite, new Vector2(0.5f, 0.5f)));
            dip.LeftJaw = left.rectTransform;
            dip.RightJaw = right.rectTransform;
            dip.JawOpen = part.jawOpen;
            dip.JawShut = part.jawShut;
            dip.MouthX = (part.hinges[0] + part.hinges[2]) / 2;
            return true;
        }

        /// <summary>A line from the part to a fixed point on the drawing, such as a grab's tag line to the deck.</summary>
        void BuildTether(Dip dip, DredgePart part)
        {
            Sprite sprite = Art.DredgePart(part.file + "_tether");
            if (sprite == null || part.tether == null || part.tether.Length < 5)
                return;
            Image line = Ui.Image("Tether", art, Color.white, sprite);
            line.raycastTarget = false;
            dip.Tether = line.rectTransform;
            dip.Tether.anchorMin = dip.Tether.anchorMax = new Vector2(0.5f, 0.5f);
            dip.Tether.pivot = new Vector2(0, 0.5f);
            dip.TetherFrom = new Vector2(part.tether[0], part.tether[1]);
            dip.TetherTo = new Vector2(part.tether[2], part.tether[3]);
            dip.TetherWidth = part.tether[4];
            built.Add(line.gameObject);
        }

        /// <summary>A trommel: a hole for each spot of its lattice, under a cover of the pipes, hoops and rims on it.</summary>
        RectTransform BuildDrum(DredgePart part, Sprite hole)
        {
            RectTransform rect = Ui.Rect(part.file, art);
            int count = part.holes.Length / 2;
            var drum = new Drum
            {
                Rect = rect,
                Speed = part.speed,
                Sag = part.sag,
                HoleSize = new Vector2(part.holeSize[0], part.holeSize[1]),
                Angles = new float[count],
                Heights = new float[count],
                Holes = new Image[count],
            };
            for (int i = 0; i < count; i++)
            {
                drum.Angles[i] = part.holes[i * 2];
                drum.Heights[i] = part.holes[i * 2 + 1];
                Image image = Ui.Image("Hole", rect, Color.white, hole);
                image.raycastTarget = false;
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.zero;
                drum.Holes[i] = image;
            }
            Sprite cover = Art.DredgePart(part.file + "_cover");
            if (cover != null)
                Piece("Cover", rect, cover, new Vector2(0.5f, 0.5f));
            drums.Add(drum);
            return rect;
        }

        /// <summary>A layer covering the whole part, turning about <paramref name="pivot"/>.</summary>
        static Image Piece(string name, RectTransform parent, Sprite sprite, Vector2 pivot)
        {
            Image image = Ui.Image(name, parent, Color.white, sprite);
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = pivot;
            return image;
        }

        /// <summary>How far open a grab's jaws are, 0 shut to 1 wide, at a point of its dip cycle.</summary>
        static float JawOpening(float t)
        {
            if (t < DipDown)
                return 1;
            if (t < DipDown + BiteSeconds)
                return 1 - Mathf.SmoothStep(0, 1, (t - DipDown) / BiteSeconds);
            if (t >= DrainAt && t < DrainAt + DrainSeconds)
                return 0.45f * Mathf.Sin(Mathf.PI * (t - DrainAt) / DrainSeconds);
            if (t >= DipEvery - OpenSeconds)
                return Mathf.SmoothStep(0, 1, (t - (DipEvery - OpenSeconds)) / OpenSeconds);
            return 0;
        }

        /// <summary>
        /// Hangs and sways, sinks into the water with a splash, waits, and comes up dripping. A crane
        /// holds still while its grab digs and swings it over to the other side while it hangs.
        /// </summary>
        void TickDip(Dip dip, float deltaTime)
        {
            float before = dip.Phase;
            dip.Phase = Mathf.Repeat(dip.Phase + deltaTime, DipEvery);
            if (dip.Phase < before)
                dip.Side = -dip.Side;
            float t = dip.Phase;
            float depth;
            if (t < DipDown)
                depth = Mathf.SmoothStep(0, 1, t / DipDown);
            else if (t < DipDown + DipHold)
                depth = 1;
            else if (t < DipDown + DipHold + DipUp)
                depth = 1 - Mathf.SmoothStep(0, 1, (t - DipDown - DipHold) / DipUp);
            else
                depth = 0;

            RectTransform rect = dip.Rect;
            if (dip.Arm != null)
            {
                float over = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(SwingFrom, SwingTo, t));
                Quaternion swing = Quaternion.Euler(0, 0, dip.Arm.Swing * dip.Side * (2 * over - 1));
                dip.Arm.Rect.localRotation = swing;
                // Carried round with the crane's tip, it keeps hanging straight down.
                Rect drawing = art.rect;
                Vector2 hang = drawing.min + Vector2.Scale(dip.HangAt, drawing.size);
                Vector2 turntable = drawing.min + Vector2.Scale(dip.Arm.Rect.pivot, drawing.size);
                rect.anchoredPosition = turntable + (Vector2)(swing * (hang - turntable)) - hang;
            }
            rect.localScale = Vector3.one * Mathf.Lerp(1, DipDepth, depth);
            rect.localRotation = Quaternion.Euler(0, 0, (1 - depth) * 3 * Mathf.Sin(time * 1.4f + dip.Phase));
            Color tint = Color.Lerp(Color.white, Palette.Underwater, depth);
            foreach (Image image in dip.Images)
                image.color = tint;
            if (dip.LeftJaw != null)
            {
                float angle = Mathf.Lerp(dip.JawShut, dip.JawOpen, JawOpening(t));
                dip.LeftJaw.localRotation = Quaternion.Euler(0, 0, -angle);
                dip.RightJaw.localRotation = Quaternion.Euler(0, 0, angle);
                // Water pours out of the half-open jaws.
                if (t >= DrainAt && t < DrainAt + DrainSeconds && UnityEngine.Random.value < deltaTime * 30)
                {
                    Rect r = rect.rect;
                    Vector2 mouth = toArea(rect.TransformPoint(new Vector3(r.x + dip.MouthX * r.width, r.yMin)));
                    particles.Emit(mouth + new Vector2(UnityEngine.Random.Range(-14f, 14f), 0), new Vector2(0, -60),
                        UnityEngine.Random.Range(9, 14), 0.4f, Palette.Droplet, Ui.Circle, -600);
                }
            }

            if (t < DipDown)
                dip.Splashed = false;
            else if (!dip.Splashed)
            {
                dip.Splashed = true;
                Vector2 centre = CentreOf(rect);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 velocity = UnityEngine.Random.insideUnitCircle * 150 + new Vector2(0, 160);
                    particles.Emit(centre, velocity, UnityEngine.Random.Range(12, 20), 0.45f, Palette.Droplet, Ui.Circle, -800);
                }
            }
            bool rising = t > DipDown + DipHold && t < DipDown + DipHold + DipUp + 0.4f;
            if (rising && UnityEngine.Random.value < deltaTime * 12)
            {
                Vector2 drip = CentreOf(rect) + UnityEngine.Random.insideUnitCircle * (rect.rect.width * 0.25f);
                particles.Emit(drip, new Vector2(0, -40), UnityEngine.Random.Range(8, 12), 0.35f, Palette.Droplet, Ui.Circle, -500);
            }
            if (dip.Tether != null)
                PlaceTether(dip);
        }

        /// <summary>Stretches a tether from its point on the moving part to its fixed end on the drawing.</summary>
        void PlaceTether(Dip dip)
        {
            Rect part = dip.Rect.rect;
            Vector2 from = art.InverseTransformPoint(dip.Rect.TransformPoint(
                new Vector3(part.x + dip.TetherFrom.x * part.width, part.y + dip.TetherFrom.y * part.height)));
            Rect drawing = art.rect;
            var to = new Vector2(drawing.x + dip.TetherTo.x * drawing.width, drawing.y + dip.TetherTo.y * drawing.height);
            Vector2 span = to - from;
            dip.Tether.localPosition = from;
            dip.Tether.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg);
            dip.Tether.sizeDelta = new Vector2(span.magnitude, dip.TetherWidth * drawing.height);
        }

        /// <summary>
        /// Turns a trommel: its holes slide across, narrowing and dropping along their rows toward
        /// the sides as the hoops do, and fade out before they go round the back.
        /// </summary>
        static void TickDrum(Drum drum, float deltaTime)
        {
            drum.Turn = Mathf.Repeat(drum.Turn + drum.Speed * deltaTime, 360);
            Rect r = drum.Rect.rect;
            float radius = r.width / 2;
            float sag = drum.Sag * r.height;
            float halfWidth = drum.HoleSize.x * r.width / 2;
            float halfHeight = drum.HoleSize.y * r.height / 2;
            for (int i = 0; i < drum.Holes.Length; i++)
            {
                float angle = (drum.Angles[i] + drum.Turn) * Mathf.Deg2Rad;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                float fade = Mathf.InverseLerp(HoleGone, HoleFull, cos);
                Image image = drum.Holes[i];
                image.enabled = fade > 0;
                if (fade <= 0)
                    continue;
                // The round hole on the curved sheet: its width runs round the drum, narrowed and
                // tilted toward the sides; its height runs along the drum. Drawn as that ellipse.
                float ux = halfWidth * cos;
                float uy = -halfWidth * sag / radius * sin;
                float xx = ux * ux;
                float xy = ux * uy;
                float yy = uy * uy + halfHeight * halfHeight;
                float mid = (xx + yy) / 2;
                float spread = Mathf.Sqrt((xx - yy) * (xx - yy) / 4 + xy * xy);
                RectTransform hole = image.rectTransform;
                hole.anchoredPosition = new Vector2(radius * (1 + sin), drum.Heights[i] * r.height - sag * (1 - cos));
                hole.sizeDelta = new Vector2(2 * Mathf.Sqrt(mid + spread), 2 * Mathf.Sqrt(Mathf.Max(mid - spread, 0)));
                hole.localRotation = Quaternion.Euler(0, 0, 0.5f * Mathf.Atan2(2 * xy, xx - yy) * Mathf.Rad2Deg);
                image.color = new Color(1, 1, 1, fade);
            }
        }

        Vector2 CentreOf(RectTransform rect) => toArea(rect.TransformPoint(rect.rect.center));

        /// <summary>A point on the drawing, as fractions from its bottom-left corner, in creek-area pixels.</summary>
        Vector2 AreaPoint(Vector2 fraction)
        {
            Rect r = art.rect;
            return toArea(art.TransformPoint(new Vector3(r.x + fraction.x * r.width, r.y + fraction.y * r.height)));
        }

        static Vector2[] Points(float[] pairs)
        {
            if (pairs == null)
                return new Vector2[0];
            var points = new Vector2[pairs.Length / 2];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(pairs[i * 2], pairs[i * 2 + 1]);
            return points;
        }
    }
}
