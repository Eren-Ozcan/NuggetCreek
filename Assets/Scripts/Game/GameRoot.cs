using NuggetCreek.Core;
using NuggetCreek.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Greybox entry point: owns the session, builds the UI in code, runs idle income,
    /// saves, and handles the offline return (design doc 3.2, 3.3, 13.1).
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        const float AutosaveSeconds = 10;
        const float TrustedTimeWaitSeconds = 6;

        GameSession session;
        readonly GameClock clock = new GameClock();
        IRewardedAds ads;
        LateDoubleOffer lateDouble;

        CreekView creek;
        UpgradesPanel upgrades;
        MapPanel map;
        OfflineModal offlineModal;

        Text dollarsLabel;
        Text statusLabel;
        Text goalLabel;
        Text mapLabel;
        Text upgradesLabel;
        Button lateDoubleChip;

        float autosaveIn = AutosaveSeconds;
        bool returnPending;
        double returnDeviceUtc;
        double returnMonotonic;
        float returnWaitStarted;
        bool resetting;

        void Awake()
        {
            Application.targetFrameRate = 60;
            var config = new EconomyConfig();
            session = new GameSession(new Economy(config), SaveStore.Load());
            ads = new FakeRewardedAds();
            lateDouble = new LateDoubleOffer(config);
            BuildUi();
        }

        void Start()
        {
            clock.TrustedTimeArrived += OnTrustedTime;
            StartCoroutine(clock.FetchTrustedTime());
            // Preload before the offline modal opens so most returns open on Ready (3.3.1 rule 3).
            ads.Load();
            if (session.Progress.LastSeenUtc > 0)
                BeginReturn();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            ads.Tick(Time.unscaledDeltaTime);

            BigNumber idle = session.TickIdle(dt);
            creek.InputEnabled = !upgrades.IsOpen && !map.IsOpen && !offlineModal.IsOpen;
            creek.Tick(dt, idle);
            offlineModal.Tick(Time.unscaledDeltaTime);

            if (returnPending && Time.realtimeSinceStartup - returnWaitStarted > TrustedTimeWaitSeconds)
                offlineModal.ShowWaitingForConnection();

            RefreshHud();
            upgrades.Refresh();
            map.Refresh();

            autosaveIn -= Time.unscaledDeltaTime;
            if (autosaveIn <= 0)
                Save();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
            else if (!returnPending && !offlineModal.HasUnclaimed)
            {
                ads.Load();
                BeginReturn();
            }
        }

        void OnApplicationQuit() => Save();

        void Save()
        {
            if (resetting)
                return;
            autosaveIn = AutosaveSeconds;
            // An unresolved return keeps the old leave stamp so the haul is not lost if the app dies now.
            if (!returnPending && !offlineModal.HasUnclaimed)
                clock.StampLeave(session.Progress);
            SaveStore.Save(session.Progress);
        }

        // --- Offline return ---

        void BeginReturn()
        {
            returnDeviceUtc = GameClock.DeviceUtc;
            returnMonotonic = GameClock.MonotonicSeconds;
            returnWaitStarted = Time.realtimeSinceStartup;
            returnPending = true;
            EvaluateReturn();
        }

        void OnTrustedTime()
        {
            if (returnPending)
                EvaluateReturn();
        }

        void EvaluateReturn()
        {
            // Trusted time that arrives later is projected back to the moment of return.
            OfflineClockInput input = clock.ReturnInput(session.Progress, returnDeviceUtc, returnMonotonic);
            OfflineResult result = session.EvaluateOffline(input);
            switch (result.Status)
            {
                case OfflineStatus.Credited:
                    returnPending = false;
                    if (!result.IsPayable)
                        break;
                    if (IsShortAbsence(result))
                        session.ClaimOffline(result, 1);
                    else
                        offlineModal.Show(result);
                    break;
                case OfflineStatus.PendingTrustedTime:
                    // A short absence is not worth holding the player on a spinner; it is dropped.
                    if (result.Amount.IsZero || IsShortAbsence(result))
                        returnPending = false;
                    else
                        offlineModal.ShowChecking(result);
                    break;
                default:
                    // TODO: log clock_tamper once analytics exists (design doc 13.4).
                    Debug.LogWarning($"Offline earnings rejected: {result.Status}");
                    returnPending = false;
                    break;
            }
        }

        bool IsShortAbsence(OfflineResult result) =>
            result.ElapsedSeconds < session.Economy.Config.OfflineModalMinAwaySeconds;

        // --- UI ---

        void BuildUi()
        {
            Canvas canvas = Ui.CreateCanvas("UI", 0);
            Transform root = canvas.transform;
            const float topHeight = 300;
            const float bottomHeight = 220;

            RectTransform creekRect = Ui.Rect("Creek", root).Place(Vector2.zero, Vector2.one, new Vector2(0, bottomHeight), new Vector2(0, -topHeight));
            creek = creekRect.gameObject.AddComponent<CreekView>();
            creek.Init(session);

            RectTransform top = Ui.Image("TopBar", root, Palette.Bar).rectTransform
                .Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -topHeight), Vector2.zero);
            dollarsLabel = Ui.Label("Dollars", top, "", 104, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            dollarsLabel.rectTransform.Place(new Vector2(0, 0.45f), Vector2.one, Vector2.zero, new Vector2(0, -20));
            statusLabel = Ui.Label("Status", top, "", 36, TextAnchor.MiddleCenter, Palette.TextMuted);
            statusLabel.rectTransform.Place(new Vector2(0, 0.22f), new Vector2(1, 0.45f));
            goalLabel = Ui.Label("Goal", top, "", 34, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            goalLabel.rectTransform.Place(Vector2.zero, new Vector2(1, 0.22f), new Vector2(0, 10), Vector2.zero);

            RectTransform bottom = Ui.Image("BottomBar", root, Palette.Bar).rectTransform
                .Place(Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, bottomHeight));
            Button mapButton = Ui.Button("MapButton", bottom, "Map", Palette.ButtonAlt, OpenMap, out mapLabel, 48);
            mapButton.AsRect().Place(Vector2.zero, new Vector2(0.5f, 1), new Vector2(30, 30), new Vector2(-15, -30));
            Button upgradesButton = Ui.Button("UpgradesButton", bottom, "Upgrades", Palette.Button, () => OpenUpgrades(false), out upgradesLabel, 48);
            upgradesButton.AsRect().Place(new Vector2(0.5f, 0), Vector2.one, new Vector2(15, 30), new Vector2(-30, -30));

            lateDoubleChip = Ui.Button("LateDouble", root, "Double your last haul?", Palette.Ad, WatchLateDouble, out _, 34);
            lateDoubleChip.AsRect().Box(new Vector2(0.5f, 1), new Vector2(560, 100), new Vector2(0, -topHeight - 20));
            lateDoubleChip.SetActive(false);

            // Before the panels so full-screen modals cover them.
            if (Debug.isDebugBuild)
                BuildDebugButtons(root, topHeight);

            upgrades = new UpgradesPanel(session, root);
            upgrades.Closed += () => offlineModal.Unhide();
            map = new MapPanel(session, root);
            offlineModal = new OfflineModal(session, ads, root);
            offlineModal.UpgradeAmosRequested += () =>
            {
                offlineModal.Hide();
                OpenUpgrades(true);
            };
            offlineModal.ClaimedWithoutAd += amount =>
            {
                lateDouble.ArmAfterClaim(Time.realtimeSinceStartup, amount);
                ads.Load();
            };
            offlineModal.Claimed += Save;
        }

        void OpenUpgrades(bool focusAmos)
        {
            map.Close();
            upgrades.Open(focusAmos);
        }

        void OpenMap()
        {
            upgrades.Close();
            map.Open();
        }

        void RefreshHud()
        {
            PlayerProgress progress = session.Progress;
            dollarsLabel.SetText(NumberFormat.Dollars(progress.Dollars));
            string idle = session.IdleActive ? $"Amos +{NumberFormat.DollarsPerSecond(session.IdleRate)}" : "No crew yet";
            statusLabel.SetText($"{session.RegionName}  |  {idle}");
            goalLabel.SetText(NextGoal());

            bool upgradeBadge = upgrades.AnythingAffordable();
            bool mapBadge = session.CanAfford(session.NextRegionCost);
            upgradesLabel.SetText(upgradeBadge ? "Upgrades  (!)" : "Upgrades");
            mapLabel.SetText(mapBadge ? "Map  (!)" : "Map");

            float now = Time.realtimeSinceStartup;
            if (ads.IsLoaded)
                lateDouble.NotifyAdReady(now);
            lateDoubleChip.SetActive(lateDouble.IsVisible(now) && ads.IsLoaded && !offlineModal.IsOpen);
        }

        string NextGoal()
        {
            if (session.Progress.AmosLevel == 0)
                return $"Next: hire Amos for {NumberFormat.Dollars(session.AmosNextCost.Value)} (Upgrades)";
            if (session.HasNextRegion)
                return $"Next: {GameCatalog.RegionNames[session.Progress.RegionsUnlocked]} for {NumberFormat.Dollars(session.NextRegionCost.Value)} (Map)";
            return "All launch creeks unlocked";
        }

        void WatchLateDouble()
        {
            BigNumber amount = lateDouble.Amount;
            lateDouble.Clear();
            lateDoubleChip.SetActive(false);
            ads.Show(rewarded =>
            {
                if (rewarded)
                    session.Earn(amount);
            });
        }

        // --- Development-only helpers ---

        void BuildDebugButtons(Transform root, float topHeight)
        {
            RectTransform column = Ui.Rect("Debug", root).Box(Vector2.one, new Vector2(200, 400), new Vector2(-10, -topHeight - 10));
            AddDebugButton(column, 0, "+$1K", () => session.Earn(1e3));
            AddDebugButton(column, 1, "+$1M", () => session.Earn(1e6));
            AddDebugButton(column, 2, "Away 2h", SimulateAway);
            AddDebugButton(column, 3, "Reset", ResetGame);
        }

        static void AddDebugButton(RectTransform column, int index, string text, System.Action onClick)
        {
            Button button = Ui.Button(text, column, text, new Color(0, 0, 0, 0.45f), onClick, out _, 30);
            button.AsRect().Box(new Vector2(1, 1), new Vector2(200, 84), new Vector2(0, -index * 96));
        }

        /// <summary>Pretends the player left 2 hours ago; exercises the whole return path without touching the device clock.</summary>
        void SimulateAway()
        {
            if (returnPending || offlineModal.HasUnclaimed)
                return;
            clock.StampLeave(session.Progress);
            session.Progress.LastSeenUtc -= 2 * 3600;
            ads.Load();
            BeginReturn();
        }

        void ResetGame()
        {
            resetting = true;
            SaveStore.Delete();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
