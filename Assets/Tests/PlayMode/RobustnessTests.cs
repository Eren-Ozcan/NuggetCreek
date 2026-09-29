using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NuggetCreek.Core;
using NuggetCreek.Game;
using NuggetCreek.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace NuggetCreek.PlayModeTests
{
    /// <summary>
    /// Save round trip, damaged saves, random button presses and layout at several screen
    /// shapes. Any error or exception logged during a test fails it (Unity Test Framework).
    /// </summary>
    public class RobustnessTests
    {
        const string SaveKeyName = "nc_save";

        // Buttons a random press must never hit: they leave the app or change the next launch.
        static readonly HashSet<string> MonkeySkip = new HashSet<string> { "ReadPolicy", "PrivacyPolicy", "EEA: on", "EEA: off" };

        [UnitySetUp]
        public IEnumerator ClearBefore()
        {
            SaveStore.Delete();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ClearAfter()
        {
            Time.timeScale = 1;
            SaveStore.Delete();
            yield return null;
        }

        // --- save ---

        [Test]
        public void EveryProgressFieldSurvivesSaveAndLoad()
        {
            PlayerProgress expected = MutatedProgress();
            expected.Normalize();

            var fresh = new PlayerProgress();
            fresh.Normalize();
            var untouched = new List<string>();
            foreach (FieldInfo field in ProgressFields())
                if (ValuesEqual(field.GetValue(expected), field.GetValue(fresh)))
                    untouched.Add(field.Name);
            Assert.That(untouched, Is.Empty, "the mutation must move every field off its default, or a missing mapping hides");

            SaveStore.Save(expected);
            PlayerProgress loaded = SaveStore.Load();

            var lost = ProgressFields()
                .Where(f => !ValuesEqual(f.GetValue(expected), f.GetValue(loaded)))
                .Select(f => $"{f.Name}: saved {Show(f.GetValue(expected))}, loaded {Show(f.GetValue(loaded))}")
                .ToList();
            Assert.That(lost, Is.Empty, "fields SaveStore does not carry");
        }

        [Test]
        public void ForeignTextStartsAFreshGame()
        {
            PlayerPrefs.SetString(SaveKeyName, "not a save at all");
            PlayerProgress progress = SaveStore.Load();
            Assert.That(progress.Dollars, Is.EqualTo(BigNumber.Zero));
            Assert.That(SaveStore.LoadIssue, Is.EqualTo(((string, string)?)("load", "signature")));
        }

        [Test]
        public void SignedButBrokenJsonStartsAFreshGame()
        {
            PlayerPrefs.SetString(SaveKeyName, SaveEnvelope.Wrap("{\"dollars\":\"12\",\"regionIn", SaveKey.Current()));
            PlayerProgress progress = SaveStore.Load();
            Assert.That(progress.Dollars, Is.EqualTo(BigNumber.Zero));
            Assert.That(SaveStore.LoadIssue, Is.EqualTo(((string, string)?)("load", "parse")));
        }

        /// <summary>
        /// Out-of-range values in a correctly signed save (a bad migration or a bug in an older
        /// build) must be clamped, not crash the launch or any panel.
        /// </summary>
        [UnityTest]
        public IEnumerator OutOfRangeSaveLoadsAndPlays()
        {
            string json = "{\"version\":" + SaveMigration.CurrentVersion +
                ",\"dollars\":\"-5000\",\"totalEarned\":\"nonsense\",\"regionIndex\":999,\"regionsUnlocked\":999" +
                ",\"tierIndex\":999,\"upgradeLevels\":[-3,99999,5],\"amosLevel\":-4,\"manualCollected\":-10" +
                ",\"gems\":-5,\"crewLevels\":[-1,9999],\"nuggetCatches\":[-2],\"gearLevels\":[99,-9]" +
                ",\"gearSlots\":[99,99,-7,3,4,5],\"chestsWaiting\":999,\"chestOpening\":true,\"chestSecondsLeft\":-50" +
                ",\"perkRanks\":[99,-1],\"guildLevel\":9999,\"guildXp\":-7,\"rebirths\":-2,\"bestRegionsUnlocked\":-3" +
                ",\"hasDaily\":true,\"currentDay\":-50,\"streakIndex\":99,\"rescueStreakIndex\":99,\"jobKinds\":[99,-1,7]" +
                ",\"jobProgress\":[-5,1,2],\"adWashes\":99,\"boostMultiplier\":-2,\"boostEndUtc\":-1" +
                ",\"crewCandidates\":[99,-1,3],\"candidateSecondsLeft\":-4,\"goalIndex\":-4,\"playSeconds\":-100" +
                ",\"welcomeOffer\":9,\"weekendBoughtMask\":255,\"goldWashSecondsLeft\":-3,\"pendingGiantNuggets\":-1" +
                ",\"sessionHourLog\":[99,-1],\"notifSent\":[1,2,3,4,5,6,7,8,9],\"notifPendingKinds\":[99],\"notifPendingUtc\":[1e300]" +
                ",\"ageBand\":3,\"termsAccepted\":1,\"vibration\":9,\"peteSeen\":-1,\"featuresAnnounced\":-1}";
            yield return StopGame();
            PlayerPrefs.SetString(SaveKeyName, SaveEnvelope.Wrap(json, SaveKey.Current()));

            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
            yield return null;
            PlayerProgress progress = Session().Progress;
            Assert.That(progress.Dollars >= BigNumber.Zero, "dollars never negative");
            Assert.That(progress.RegionsUnlocked, Is.InRange(1, GameCatalog.RegionNames.Count));
            Assert.That(progress.Gems, Is.GreaterThanOrEqualTo(0));

            yield return PressRandomly(new System.Random(7), 400);
        }

        // --- random presses ---

        [UnityTest]
        public IEnumerator RandomPressesKeepTheGameSane()
        {
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
            Press("AgeAdult");
            Press("GateAccept");
            yield return null;
            // Unlocks and money so the presses reach the late systems too.
            Press("Skip intro");
            Press("+$1T");
            Press("+50 Gems");
            yield return PressRandomly(new System.Random(12345), 2500);
        }

        [UnityTest]
        public IEnumerator RandomPressesFromAFreshStart()
        {
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
            Press("AgeUnder13");
            Press("GateAccept");
            yield return null;
            // Spawns and idle income run 30x so a random player gets past the first creek.
            Time.timeScale = 30;
            // Only what a new player can reach: no debug column, and no Settings, whose Delete
            // would keep restarting the run (it has its own test).
            yield return PressRandomly(new System.Random(99), 3000, false, 0.8, "SettingsButton");
            PlayerProgress p = Session().Progress;
            TestContext.Out.WriteLine($"reached: {p.ManualCollected} caught, Amos {p.AmosLevel}, creeks {p.RegionsUnlocked}, {NumberFormat.Dollars(p.Dollars)}");
        }

        static IEnumerator PressRandomly(System.Random random, int presses, bool includeDebug = true, double swipeChance = 0.33, params string[] skip)
        {
            var counts = new Dictionary<string, int>();
            int sweeps = 0;
            for (int i = 0; i < presses; i++)
            {
                // Some steps swipe across the creek when nothing covers it.
                CreekView creek = UnityEngine.Object.FindAnyObjectByType<CreekView>();
                if (creek != null && creek.InputEnabled && random.NextDouble() < swipeChance)
                {
                    Vector2 size = creek.AreaSize;
                    float y = (float)random.NextDouble() * size.y;
                    creek.Sweep(new Vector2(0, y), new Vector2(size.x, y));
                    sweeps++;
                    yield return null;
                    continue;
                }
                List<Button> buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.InstanceID)
                    .Where(b => b.isActiveAndEnabled && b.interactable && !MonkeySkip.Contains(b.name)
                        && !skip.Contains(b.name) && (includeDebug || !IsDebug(b)) && Reachable(b))
                    .ToList();
                if (buttons.Count == 0)
                {
                    yield return null;
                    continue;
                }
                // Closing is always on offer; mostly skip it so the presses reach inside panels.
                List<Button> stay = buttons.Where(b => b.name != "Close" && b.name != "Later").ToList();
                if (stay.Count > 0 && random.NextDouble() < 0.85)
                    buttons = stay;
                Button button = buttons[random.Next(buttons.Count)];
                counts[button.name] = counts.TryGetValue(button.name, out int n) ? n + 1 : 1;
                button.onClick.Invoke();
                // One frame per press, so GameRoot sees the new screen before the next one.
                yield return null;
                if (i % 50 == 0)
                    CheckInvariants();
            }
            CheckInvariants();
            TestContext.Out.WriteLine($"{sweeps} swipes, {counts.Count} distinct buttons pressed: " +
                string.Join(", ", counts.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
        }

        static bool IsDebug(Component control) => control.transform.parent != null && control.transform.parent.name == "Debug";

        /// <summary>Only what a finger could reach: the button is not under a full-screen panel opened after it.</summary>
        static bool Reachable(Button button)
        {
            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null)
                return true;
            var rect = (RectTransform)button.transform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 centre = (corners[0] + corners[2]) / 2;
            var data = new UnityEngine.EventSystems.PointerEventData(eventSystem) { position = centre };
            eventSystem.RaycastAll(data, results);
            if (results.Count == 0)
                return false;
            Transform hit = results[0].gameObject.transform;
            return hit == button.transform || hit.IsChildOf(button.transform);
        }

        static void CheckInvariants()
        {
            PlayerProgress p = Session().Progress;
            Assert.That(p.Dollars >= BigNumber.Zero, $"dollars {p.Dollars}");
            Assert.That(double.IsNaN(p.Dollars.Mantissa) || double.IsInfinity(p.Dollars.Mantissa), Is.False, "dollars finite");
            Assert.That(p.Gems, Is.GreaterThanOrEqualTo(0), "gems");
            Assert.That(p.RegionIndex, Is.InRange(0, p.RegionsUnlocked - 1), "region");
            Assert.That(p.ChestsWaiting, Is.GreaterThanOrEqualTo(0), "chests");
            Assert.That(p.UpgradeLevels.All(l => l >= 0), "upgrade levels");
            Assert.That(p.CrewLevels.All(l => l >= 0), "crew levels");
        }

        // --- layout ---

        static readonly Vector2Int[] Screens =
        {
            new Vector2Int(1080, 1920), // 16:9
            new Vector2Int(1080, 2340), // 19.5:9
            new Vector2Int(1080, 2400), // 20:9
            new Vector2Int(720, 1600),  // low-end 20:9
            new Vector2Int(1440, 3200), // high-density 20:9
            new Vector2Int(1536, 2048), // 4:3 tablet
        };

        static readonly string[] PanelButtons = { null, "UpgradesButton", "MapButton", "ShopButton", "GuildButton", "DailyButton", "SettingsButton" };

        [UnityTest]
        public IEnumerator EveryPanelFitsCommonScreens()
        {
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
            var findings = new List<string>();
            var small = new SortedSet<string>();

            // The first-launch screen, then the game with everything unlocked.
            foreach (Vector2Int screen in Screens)
            {
                yield return Resize(screen);
                CollectLayout("PrivacyGate", screen, findings, small);
            }
            Press("AgeAdult");
            Press("GateAccept");
            Press("Skip intro");
            Press("+$1T");
            yield return null;

            foreach (Vector2Int screen in Screens)
            {
                yield return Resize(screen);
                foreach (string open in PanelButtons)
                {
                    if (open != null)
                    {
                        Press(open);
                        yield return null;
                        yield return null;
                    }
                    CollectLayout(open ?? "HUD", screen, findings, small);
                    CloseAll();
                    yield return null;
                }
            }
            yield return Resize(Vector2Int.zero);

            Assert.That(findings, Is.Empty, "controls outside the screen:\n" + string.Join("\n", findings));
            Assert.That(small, Is.Empty, "tap targets under 48 dp:\n" + string.Join("\n", small));
        }

        [UnityTest]
        public IEnumerator DredgeAndChestFitEveryScreenAtEveryTier()
        {
            yield return SceneManager.LoadSceneAsync("Creek");
            yield return null;
            Press("Skip intro");
            yield return null;
            var creek = UnityEngine.Object.FindAnyObjectByType<CreekView>();
            var river = UnityEngine.Object.FindAnyObjectByType<RiverView>();
            var dredge = UnityEngine.Object.FindAnyObjectByType<DredgeView>();
            var chest = (RectTransform)dredge.transform.Find("Body/Art/Chest");
            GameSession session = Session();
            var findings = new List<string>();

            foreach (Vector2Int screen in Screens)
            {
                yield return Resize(screen);
                for (int tier = 0; tier < session.Economy.Config.TierCount; tier++)
                {
                    session.Progress.TierIndex = tier;
                    yield return null;
                    yield return null;
                    string where = $"{screen.x}x{screen.y} tier {tier + 1}";
                    var creekRect = new Rect(Vector2.zero, creek.AreaSize);
                    if (!Inside(dredge.ArtBounds, creekRect))
                        findings.Add($"{where}: dredge {dredge.ArtBounds} leaves the creek {creekRect.size}");
                    if (!Inside(dredge.ChestBounds, creekRect))
                        findings.Add($"{where}: chest {dredge.ChestBounds} leaves the creek");
                    // The lanes beside the hull must fit a Nugget with room to wander.
                    float lane = Mathf.Min(dredge.HullLeft - river.WaterLeft, river.WaterRight - dredge.HullRight);
                    if (lane < 150)
                        findings.Add($"{where}: water lane only {lane:0} px wide");
                    Rect chestOnScreen = ScreenRect(chest, Camera.main);
                    foreach (Selectable control in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
                        if (control.isActiveAndEnabled && !IsDebug(control) && ScreenRect((RectTransform)control.transform, Camera.main).Overlaps(chestOnScreen))
                            findings.Add($"{where}: {Path(control.transform)} covers the chest");
                }
            }
            yield return Resize(Vector2Int.zero);
            Assert.That(findings, Is.Empty, string.Join("\n", findings));
        }

        static bool Inside(Rect inner, Rect outer) =>
            inner.xMin >= outer.xMin && inner.yMin >= outer.yMin && inner.xMax <= outer.xMax && inner.yMax <= outer.yMax;

        static RenderTexture target;

        /// <summary>Renders the UI through the camera at the given size; zero restores the overlay.</summary>
        static IEnumerator Resize(Vector2Int size)
        {
            Camera camera = Camera.main;
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas).ToArray();
            if (target != null)
            {
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(target);
                target = null;
            }
            if (size == Vector2Int.zero)
            {
                foreach (Canvas canvas in canvases)
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                yield return null;
                yield break;
            }
            target = new RenderTexture(size.x, size.y, 24);
            camera.targetTexture = target;
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
            }
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        static void CollectLayout(string state, Vector2Int screen, List<string> findings, SortedSet<string> small)
        {
            Camera camera = Camera.main;
            // Android's baseline: a 411 dp wide phone, so 48 dp is this many pixels here.
            float minPixels = 48f * screen.x / 411f;
            Rect visible = new Rect(0, 0, screen.x, screen.y);
            foreach (Selectable control in UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                // The debug column is not in store builds.
                if (!control.isActiveAndEnabled || IsDebug(control))
                    continue;
                Rect r = ScreenRect((RectTransform)control.transform, camera);
                string where = $"{state} {screen.x}x{screen.y} {Path(control.transform)}";
                // List items scroll into view, so only their size counts.
                bool inList = control.GetComponentInParent<ScrollRect>() != null;
                if (!inList && (r.xMin < visible.xMin - 1 || r.yMin < visible.yMin - 1 || r.xMax > visible.xMax + 1 || r.yMax > visible.yMax + 1))
                    findings.Add($"{where} at {r}");
                // Held to 48 dp on the common 1080 px wide phones (Ui.TapHeight).
                if (screen.x == 1080 && screen.y >= 2340 && Math.Min(r.width, r.height) < minPixels)
                    small.Add($"{Path(control.transform)} {Mathf.RoundToInt(r.width / minPixels * 48)}x{Mathf.RoundToInt(r.height / minPixels * 48)} dp");
            }
        }

        static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p.GetComponent<Canvas>() == null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        static void CloseAll()
        {
            for (int i = 0; i < 6; i++)
            {
                Button close = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                    .FirstOrDefault(b => b.isActiveAndEnabled && b.interactable && (b.name == "Close" || b.name == "Later"));
                if (close == null)
                    return;
                close.onClick.Invoke();
            }
        }

        // --- helpers ---

        /// <summary>
        /// Removes the running game before a test writes a save for the next load, so a late
        /// callback (trusted time, autosave) of the old game cannot write over it.
        /// </summary>
        static IEnumerator StopGame()
        {
            var root = UnityEngine.Object.FindAnyObjectByType<GameRoot>();
            if (root != null)
                UnityEngine.Object.Destroy(root.gameObject);
            yield return null;
        }

        internal static GameSession Session()
        {
            var root = UnityEngine.Object.FindAnyObjectByType<GameRoot>();
            return (GameSession)typeof(GameRoot).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(root);
        }

        static void Press(string name)
        {
            Button button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .FirstOrDefault(b => b.name == name && b.isActiveAndEnabled);
            Assert.That(button, Is.Not.Null, $"'{name}' not found");
            Assert.That(button.interactable, Is.True, $"'{name}' is not interactable");
            button.onClick.Invoke();
        }

        static IEnumerable<FieldInfo> ProgressFields() =>
            typeof(PlayerProgress).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>Every field moved off its default to a value Normalize keeps.</summary>
        static PlayerProgress MutatedProgress()
        {
            var p = new PlayerProgress();
            foreach (FieldInfo field in ProgressFields())
            {
                object value = field.GetValue(p);
                switch (value)
                {
                    case int i:
                        field.SetValue(p, i + 1);
                        break;
                    case long l:
                        field.SetValue(p, l + 2);
                        break;
                    case double d:
                        field.SetValue(p, d + 1.5);
                        break;
                    case bool b:
                        field.SetValue(p, !b);
                        break;
                    case BigNumber _:
                        field.SetValue(p, BigNumber.Create(4.25, 15));
                        break;
                    case int[] ints:
                        field.SetValue(p, ints.Length == 0 ? new[] { 1 } : ints.Select(x => x + 1).ToArray());
                        break;
                    case bool[] bools:
                        field.SetValue(p, bools.Select(x => !x).ToArray());
                        break;
                    case double[] doubles:
                        field.SetValue(p, doubles.Length == 0 ? new[] { 1.5 } : doubles.Select(x => x + 1.5).ToArray());
                        break;
                    case string[] strings:
                        field.SetValue(p, strings.Concat(new[] { "txn-1" }).ToArray());
                        break;
                }
            }
            // Values Normalize would otherwise pull back to the defaults.
            p.RegionsUnlocked = 3;
            p.BestRegionsUnlocked = 4;
            p.RegionIndex = 2;
            p.CrewLevels[0] = 0;
            p.CrewCandidates = new[] { 0 };
            p.GearSlots = new[] { 0, 1, -1, -1 };
            p.AgeBand = AgeBand.Teen;
            p.Vibration = VibrationMode.Important;
            p.BoostMultiplier = 2;
            p.WeekendBoughtMask = 5;
            p.JobKinds = new[] { 3, 4, 5 };
            p.NotifPendingKinds = new[] { 2 };
            p.NotifPendingUtc = new[] { 1.8e9 };
            return p;
        }

        static bool ValuesEqual(object a, object b)
        {
            if (a is Array x && b is Array y)
                return x.Length == y.Length && x.Cast<object>().SequenceEqual(y.Cast<object>());
            return Equals(a, b);
        }

        static string Show(object value) =>
            value is Array array ? "[" + string.Join(",", array.Cast<object>()) + "]" : value?.ToString() ?? "null";
    }
}
