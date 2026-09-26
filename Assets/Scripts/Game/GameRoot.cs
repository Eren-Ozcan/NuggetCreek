using System.Globalization;
using NuggetCreek.Core;
using NuggetCreek.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NuggetCreek.Game
{
    /// <summary>
    /// Greybox entry point: owns the session, builds the UI in code, runs idle income,
    /// goals, crew candidates and the Mother Lode, saves, and handles the offline return
    /// (design doc 3.2, 3.3, 3.4, 6.3, 13.1).
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
        CollectionPanel collection;
        ChestModal chestModal;
        GuildPanel guild;
        DailyPanel daily;
        Text dailyLabel;
        long debugDayShift;
        Text guildLabel;
        Button claimChip;
        Button chestChip;
        Text chestChipLabel;
        Text collectionLabel;
        UpgradesPanel upgrades;
        MapPanel map;
        OfflineModal offlineModal;
        CandidateModal candidateModal;
        MotherLodeView motherLode;

        Text dollarsLabel;
        Text gemsLabel;
        Button goalButton;
        Text statusLabel;
        Text goalLabel;
        Text mapLabel;
        Text upgradesLabel;
        Button lateDoubleChip;
        Button crewChip;
        Text crewChipLabel;
        Button summonLode;

        float autosaveIn = AutosaveSeconds;
        bool returnPending;
        double returnDeviceUtc;
        double returnMonotonic;
        float returnWaitStarted;
        bool resetting;

        void Awake()
        {
            Application.targetFrameRate = 60;
            // The game is English only (design doc 0): format numbers the same on every device locale.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
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
            UpdateTrustedDay();
            ads.Tick(Time.unscaledDeltaTime);

            BigNumber idle = session.TickIdle(dt);
            session.TickCandidates(dt);
            bool modalOpen = upgrades.IsOpen || map.IsOpen || offlineModal.IsOpen || candidateModal.IsOpen || collection.IsOpen || chestModal.IsOpen || guild.IsOpen || daily.IsOpen;
            creek.InputEnabled = !modalOpen;
            if (motherLode.IsActive)
            {
                motherLode.Tick(dt);
            }
            else
            {
                creek.Tick(dt, idle);
                if (!modalOpen)
                {
                    session.TickPlay(dt);
                    if (session.MotherLodeDue)
                        StartMotherLode(session.StartMotherLode());
                }
            }
            offlineModal.Tick(Time.unscaledDeltaTime);

            if (returnPending && Time.realtimeSinceStartup - returnWaitStarted > TrustedTimeWaitSeconds)
                offlineModal.ShowWaitingForConnection();

            RefreshHud();
            upgrades.Refresh();
            map.Refresh();
            collection.Refresh();
            chestModal.Refresh();
            guild.Refresh();
            daily.Refresh();
            candidateModal.Refresh();

            autosaveIn -= Time.unscaledDeltaTime;
            if (autosaveIn <= 0)
                Save();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                motherLode.FinishNow();
                Save();
            }
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
            // Above the creek, below the bars, so the Dollar counter stays in view during the event.
            motherLode = new MotherLodeView(session, root, creekRect);
            motherLode.Finished += Save;

            RectTransform top = Ui.Image("TopBar", root, Palette.Bar).rectTransform
                .Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -topHeight), Vector2.zero);
            dollarsLabel = Ui.Label("Dollars", top, "", 104, TextAnchor.MiddleCenter, Palette.Gold, FontStyle.Bold);
            dollarsLabel.rectTransform.Place(new Vector2(0, 0.45f), Vector2.one, Vector2.zero, new Vector2(0, -20));
            gemsLabel = Ui.Label("Gems", top, "", 40, TextAnchor.UpperLeft, Palette.Gem, FontStyle.Bold);
            gemsLabel.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(30, -80), new Vector2(0, -24));
            statusLabel = Ui.Label("Status", top, "", 36, TextAnchor.MiddleCenter, Palette.TextMuted);
            statusLabel.rectTransform.Place(new Vector2(0, 0.22f), new Vector2(1, 0.45f));
            Button guildButton = Ui.Button("GuildButton", top, "", Palette.ButtonAlt, () => guild.Open(), out guildLabel, 30);
            guildButton.AsRect().Box(Vector2.one, new Vector2(220, 70), new Vector2(-20, -20));

            Button dailyButton = Ui.Button("DailyButton", top, "", Palette.ButtonAlt, () => daily.Open(), out dailyLabel, 30);
            dailyButton.AsRect().Box(new Vector2(0, 1), new Vector2(200, 70), new Vector2(20, -95));

            goalButton = Ui.Button("GoalButton", top, "", Color.clear, ClaimGoal, out goalLabel, 34);
            goalButton.AsRect().Place(Vector2.zero, new Vector2(1, 0.22f), new Vector2(20, 6), new Vector2(-20, -2));
            goalLabel.name = "Goal";

            RectTransform bottom = Ui.Image("BottomBar", root, Palette.Bar).rectTransform
                .Place(Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, bottomHeight));
            Button mapButton = Ui.Button("MapButton", bottom, "Map", Palette.ButtonAlt, OpenMap, out mapLabel, 48);
            mapButton.AsRect().Place(Vector2.zero, new Vector2(0.5f, 1), new Vector2(30, 30), new Vector2(-15, -30));
            Button upgradesButton = Ui.Button("UpgradesButton", bottom, "Upgrades", Palette.Button, () => OpenUpgrades(false), out upgradesLabel, 48);
            upgradesButton.AsRect().Place(new Vector2(0.5f, 0), Vector2.one, new Vector2(15, 30), new Vector2(-30, -30));

            lateDoubleChip = Ui.Button("LateDouble", root, "Double your last haul?", Palette.Ad, WatchLateDouble, out _, 34);
            lateDoubleChip.AsRect().Box(new Vector2(0.5f, 1), new Vector2(560, 100), new Vector2(0, -topHeight - 20));
            lateDoubleChip.SetActive(false);

            crewChip = Ui.Button("CrewChip", root, "", Palette.GemButton, () => candidateModal.Open(), out crewChipLabel, 34);
            crewChip.AsRect().Box(new Vector2(0, 1), new Vector2(360, 100), new Vector2(20, -topHeight - 20));
            crewChip.SetActive(false);

            Button collectionButton = Ui.Button("CollectionButton", root, "", Palette.ButtonAlt, () => collection.Open(), out collectionLabel, 32);
            collectionButton.AsRect().Box(new Vector2(1, 0), new Vector2(300, 100), new Vector2(-20, bottomHeight + 140));

            claimChip = Ui.Button("ClaimChip", root, "New claim?", Palette.Button, () => guild.Open(), out _, 32);
            claimChip.AsRect().Box(new Vector2(0, 1), new Vector2(360, 100), new Vector2(20, -topHeight - 130));
            claimChip.SetActive(false);

            chestChip = Ui.Button("ChestChip", root, "", Palette.Nugget, () => chestModal.Open(), out chestChipLabel, 32);
            chestChip.AsRect().Box(new Vector2(1, 0), new Vector2(300, 100), new Vector2(-20, bottomHeight + 260));
            chestChip.SetActive(false);

            summonLode = Ui.Button("SummonLode", root, "", Palette.GemButton, SummonMotherLode, out Text summonLabel, 32);
            summonLode.AsRect().Box(new Vector2(1, 0), new Vector2(400, 100), new Vector2(-20, bottomHeight + 20));
            summonLabel.SetText($"Mother Lode  {Effects.Gems(session.Economy.Config.MotherLodeSummonGems)}");
            summonLode.SetActive(false);

            // Before the panels so full-screen modals cover them.
            if (Debug.isDebugBuild)
                BuildDebugButtons(root, topHeight);

            upgrades = new UpgradesPanel(session, root);
            collection = new CollectionPanel(session, root);
            chestModal = new ChestModal(session, ads, root);
            chestModal.Claimed += Save;
            guild = new GuildPanel(session, ads, root);
            guild.Reborn += Save;
            daily = new DailyPanel(session, ads, root);
            daily.Changed += Save;
            creek.NuggetDiscovered += _ => Save();
            upgrades.Closed += () => offlineModal.Unhide();
            map = new MapPanel(session, root);
            // A creek unlock may bring crew candidates (design doc 6.3); show them right away.
            map.RegionChanged += () => candidateModal.Open();
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
            candidateModal = new CandidateModal(session, root);
            candidateModal.Hired += Save;
        }

        void OpenUpgrades(bool focusAmos)
        {
            if (motherLode.IsActive)
                return;
            map.Close();
            upgrades.Open(focusAmos);
        }

        void OpenMap()
        {
            if (motherLode.IsActive)
                return;
            upgrades.Close();
            map.Open();
        }

        void RefreshHud()
        {
            PlayerProgress progress = session.Progress;
            dollarsLabel.SetText(NumberFormat.Dollars(progress.Dollars));
            string idle = session.IdleActive ? $"Amos +{NumberFormat.DollarsPerSecond(session.IdleRate)}" : "No crew yet";
            string boost = session.BoostMultiplier > 1
                ? $"  |  x{session.BoostMultiplier:0} {CandidateModal.Clock(session.BoostSecondsLeft)}" : "";
            statusLabel.SetText($"{session.RegionName}  |  {idle}{boost}");
            dailyLabel.SetText(daily.AnythingToCollect() ? "Daily  (!)" : "Daily");
            gemsLabel.SetText(Effects.Gems(progress.Gems));
            RefreshGoal();

            bool upgradeBadge = upgrades.AnythingAffordable();
            bool mapBadge = session.CanAfford(session.NextRegionCost);
            upgradesLabel.SetText(upgradeBadge ? "Upgrades  (!)" : "Upgrades");
            mapLabel.SetText(mapBadge ? "Map  (!)" : "Map");
            collectionLabel.SetText($"Nuggets  {session.TotalStars}/{session.MaxStars}");
            collectionLabel.transform.parent.gameObject.SetActive(!motherLode.IsActive);
            chestChip.SetActive(session.HasChest && !chestModal.IsOpen && !motherLode.IsActive);
            chestChipLabel.SetText(ChestChipText());
            guildLabel.SetText($"Guild Lv {session.GuildLevel}");
            claimChip.SetActive(session.RebirthSuggested && !guild.IsOpen && !motherLode.IsActive);

            float now = Time.realtimeSinceStartup;
            if (ads.IsLoaded)
                lateDouble.NotifyAdReady(now);
            lateDoubleChip.SetActive(lateDouble.IsVisible(now) && ads.IsLoaded && !offlineModal.IsOpen);

            crewChip.SetActive(session.HasCandidates && !candidateModal.IsOpen && !motherLode.IsActive);

            // The Gem call appears once the player has met a natural Mother Lode (design doc 8.2c).
            bool lodeKnown = progress.PlaySeconds >= session.Economy.Config.MotherLodeFirstAfterSeconds;
            summonLode.SetActive(lodeKnown && !motherLode.IsActive);
            bool canSummon = session.CanAffordGems(session.Economy.Config.MotherLodeSummonGems);
            if (summonLode.interactable != canSummon)
                summonLode.interactable = canSummon;
            if (session.HasCandidates)
                crewChipLabel.SetText($"New crew  {CandidateModal.Clock(progress.CandidateSecondsLeft)}");
        }

        // --- Mother Lode ---

        void StartMotherLode(MotherLodeRun run)
        {
            if (run == null)
                return;
            upgrades.Close();
            map.Close();
            candidateModal.Close();
            motherLode.Begin(run);
        }

        void SummonMotherLode()
        {
            if (!motherLode.IsActive)
                StartMotherLode(session.SummonMotherLode());
        }

        // --- Goals ---

        void RefreshGoal()
        {
            GoalDefinition goal = session.CurrentGoal;
            bool done = session.IsCurrentGoalComplete;
            if (goalButton.interactable != done)
                goalButton.interactable = done;
            goalButton.GetComponent<Image>().color = done ? Palette.GemButton : Color.clear;
            if (goal == null)
                goalLabel.SetText(NextStep());
            else if (done)
                goalLabel.SetText($"Goal done! Tap to claim {Effects.Gems(session.CurrentGoalReward)}");
            else
                goalLabel.SetText($"Goal: {GoalText(goal)}  {session.GoalProgress(goal)}/{goal.Target}  (+{Effects.Gems(session.CurrentGoalReward)})");
        }

        void ClaimGoal()
        {
            if (session.ClaimGoal())
                Save();
        }

        static string GoalText(GoalDefinition goal)
        {
            switch (goal.Kind)
            {
                case GoalKind.ManualCollected: return $"swipe up {goal.Target} Gold Dust by hand";
                case GoalKind.UpgradeLevels: return $"buy {goal.Target} upgrade levels";
                case GoalKind.AmosLevel: return goal.Target == 1 ? "hire Amos" : $"train Amos to Lv {goal.Target}";
                case GoalKind.RegionsUnlocked: return $"reach {GameCatalog.RegionNames[goal.Target - 1]}";
                case GoalKind.SluiceTier: return $"build Sluice Tier {goal.Target}";
                case GoalKind.CrewHired: return goal.Target == 1 ? "hire a crew member" : $"hire {goal.Target} crew";
                default: return goal.Kind.ToString();
            }
        }

        string NextStep()
        {
            if (session.Progress.AmosLevel == 0)
                return $"Next: hire Amos for {NumberFormat.Dollars(session.AmosNextCost.Value)} (Upgrades)";
            if (session.HasNextRegion)
                return $"Next: {GameCatalog.RegionNames[session.Progress.RegionsUnlocked]} for {NumberFormat.Dollars(session.NextRegionCost.Value)} (Map)";
            return "All launch creeks unlocked";
        }

        /// <summary>Feeds trusted time to the session and rolls the game day (design doc 7.1, 13.1).</summary>
        void UpdateTrustedDay()
        {
            session.NowUtc = clock.TrustedNowUtc;
            if (!session.NowUtc.HasValue)
                return;
            double offset = System.TimeZoneInfo.Local.GetUtcOffset(System.DateTime.UtcNow).TotalSeconds;
            long today = GameSession.DayNumber(session.NowUtc.Value, offset, session.Economy.Config.DailyRolloverHour);
            if (session.UpdateDay(today + debugDayShift))
                Save();
        }

        string ChestChipText()
        {
            PlayerProgress progress = session.Progress;
            if (session.ChestReady)
                return "Chest ready!";
            if (progress.ChestOpening)
                return "Chest " + CandidateModal.Clock(progress.ChestSecondsLeft);
            return progress.ChestsWaiting == 1 ? "Open chest" : $"Chests x{progress.ChestsWaiting}";
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
            RectTransform column = Ui.Rect("Debug", root).Box(Vector2.one, new Vector2(200, 900), new Vector2(-10, -topHeight - 10));
            AddDebugButton(column, 0, "+$1K", () => session.Earn(1e3));
            AddDebugButton(column, 1, "+$1M", () => session.Earn(1e6));
            AddDebugButton(column, 2, "+50 Gems", () => session.EarnGems(50));
            AddDebugButton(column, 3, "Away 2h", SimulateAway);
            AddDebugButton(column, 4, "Lode now", MakeMotherLodeDue);
            AddDebugButton(column, 5, "Layers", OpenCollectionLayers);
            AddDebugButton(column, 6, "Chest now", AddDebugChest);
            AddDebugButton(column, 7, "Next day", () => debugDayShift++);
            AddDebugButton(column, 8, "Reset", ResetGame);
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

        void MakeMotherLodeDue()
        {
            EconomyConfig config = session.Economy.Config;
            session.Progress.PlaySeconds = System.Math.Max(session.Progress.PlaySeconds, config.MotherLodeFirstAfterSeconds);
            session.Progress.SecondsSinceMotherLode = config.MotherLodeEverySeconds;
        }

        /// <summary>Opens the perk and gear layers (critical, Giant, vein) until restart, to see them before those systems exist.</summary>
        void OpenCollectionLayers()
        {
            EconomyConfig config = session.Economy.Config;
            config.CritChanceBase = 0.25;
            config.GiantNuggetChanceBase = 0.2;
            config.VeinMaxLevelBase = 5;
        }

        void AddDebugChest()
        {
            PlayerProgress progress = session.Progress;
            progress.ChestsWaiting = System.Math.Min(progress.ChestsWaiting + 1, session.ChestCapacity);
        }

        void ResetGame()
        {
            resetting = true;
            SaveStore.Delete();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
