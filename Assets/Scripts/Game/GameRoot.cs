using System;
using System.Collections.Generic;
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
    /// goals, crew candidates, the Mother Lode and the shop, saves, and handles the offline
    /// return (design doc 3.2, 3.3, 3.4, 6.3, 8.2e, 13.1).
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        const float AutosaveSeconds = 10;
        const float TrustedTimeWaitSeconds = 6;
        /// <summary>The screen must stay calm this long after a break before its interstitial plays.</summary>
        const float BreakSettleSeconds = 1;

        GameSession session;
        readonly GameClock clock = new GameClock();
        IRewardedAds ads;
        IInterstitialAds interstitials;
        AdMobAds admob;
        INotifications notifications;
        NotifAskCard notifAsk;
        bool askAfterOffline;
        bool permissionPending;
        float askSettledFor;
        bool debugNotifSoon;
        IStore store;
        ShopPanel shop;
        Text shopLabel;
        PeteBanner pete;
        Button upgradesButton;
        Button quickBuyButton;
        Button mapButton;
        Button shopButton;
        Button dailyButton;
        Button guildButton;
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
        // Buttons whose icon turns into a lock while their feature is closed, with the open icon.
        readonly Dictionary<Button, (Image icon, Sprite open)> lockIcons = new Dictionary<Button, (Image, Sprite)>();
        readonly Dictionary<Button, Image> badges = new Dictionary<Button, Image>();
        Button goalButton;
        Button goalAd;
        Text goalAdLabel;
        Text statusLabel;
        Text goalLabel;
        Text mapLabel;
        Text upgradesLabel;
        Text quickBuyLabel;
        Button lateDoubleChip;
        Button crewChip;
        Text crewChipLabel;
        Button summonLode;
        PrivacyGate gate;
        CloudSync cloud;
        CloudChoiceModal cloudChoice;
        PlayerProgress pendingCloud;
        SettingsPanel settings;

        float autosaveIn = AutosaveSeconds;
        bool returnPending;
        double returnDeviceUtc;
        double returnMonotonic;
        float returnWaitStarted;
        bool resetting;
        bool loadLogged;
        float foregroundSince;
        DateTime pausedAtUtc;
        bool wasPaused;
        bool pausedUnderAd;
        float settledFor;

        void Awake()
        {
            Application.targetFrameRate = 60;
            // The game is English only (design doc 0): format numbers the same on every device locale.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            // Server values fetched last session apply now, before anything reads the config.
            EconomyConfig config = RemoteConfigCache.BuildConfig();
            session = new GameSession(new Economy(config), SaveStore.Load());
            // Nothing that collects data starts before the first-launch age answer (design doc 14.2).
            if (!Compliance.NeedsGate(session.Progress))
                FirebaseServices.Start(Compliance.Audience(session.Progress.AgeBand));
            Haptics.Mode = session.Progress.Vibration;
            Sound.MusicOn = session.Progress.MusicOn;
            Sound.SoundOn = session.Progress.SoundOn;
            Sound.Create();
            Ui.TextScale = Compliance.TextScale(DeviceSettings.FontScale());
            session.Events = new AnalyticsSink(session);
            FirebaseServices.Events = session.Events;
            session.BeginSession();
            session.RecordSessionHour(DateTimeOffset.Now);
            ReportPayer();
            if (SaveStore.LoadIssue.HasValue)
                session.Events.Emit("save_error",
                    ("stage", SaveStore.LoadIssue.Value.Stage), ("code", SaveStore.LoadIssue.Value.Code));
            CreateAds();
#if UNITY_ANDROID && !UNITY_EDITOR
            notifications = new AndroidNotifications();
#else
            notifications = new NoNotifications();
#endif
            lateDouble = new LateDoubleOffer(config);
            CreateCloud();
            BuildUi();
        }

        void CreateCloud()
        {
            cloud = new CloudSync(CloudSync.CreateSlot(), () => session.Progress, SaveStore.LoadDamaged);
            cloud.Failed += (stage, code) => session.Events.Emit("save_error", ("stage", stage), ("code", code));
            cloud.Found += OnCloudFound;
        }

        void Start()
        {
            clock.TrustedTimeArrived += OnTrustedTime;
            StartCoroutine(clock.FetchTrustedTime());
            if (Compliance.NeedsGate(session.Progress))
                gate.Open();
            else
                admob?.Start(Compliance.Audience(session.Progress.AgeBand));
            cloud.Start(false);
            ReturnFromNotifications();
            // Preload before the offline modal opens so most returns open on Ready (3.3.1 rule 3).
            ads.Load();
            if (session.Progress.LastSeenUtc > 0)
                BeginReturn();
        }

        void Update()
        {
            if (!loadLogged)
            {
                loadLogged = true;
                session.Events.Emit("game_loaded", ("load_ms", (long)(Time.realtimeSinceStartup * 1000)), ("cold", 1));
            }
            float dt = Time.deltaTime;
            UpdateTrustedDay();
            ads.Tick(Time.unscaledDeltaTime);
            if (!ReferenceEquals(interstitials, ads))
                interstitials.Tick(Time.unscaledDeltaTime);
            store.Tick(Time.unscaledDeltaTime);
            session.TickShop(dt);

            Sound.Region = session.Progress.RegionIndex;
            Sound.MotherLode = motherLode.IsActive;

            BigNumber idle = session.TickIdle(dt);
            session.TickCandidates(dt);
            bool modalOpen = upgrades.IsOpen || map.IsOpen || offlineModal.IsOpen || candidateModal.IsOpen || collection.IsOpen || chestModal.IsOpen || guild.IsOpen || daily.IsOpen || shop.IsOpen
                || gate.IsOpen || settings.IsOpen || cloudChoice.IsOpen;
            creek.InputEnabled = !modalOpen;
            if (motherLode.IsActive)
            {
                motherLode.Tick(dt);
                creek.TickScenery(dt);
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
            notifications.Tick();
            UpdateNotifAsk(modalOpen);
            UpdateInterstitial(modalOpen || notifAsk.IsOpen);

            if (returnPending && Time.realtimeSinceStartup - returnWaitStarted > TrustedTimeWaitSeconds)
                offlineModal.ShowWaitingForConnection();

            RefreshHud();
            upgrades.Refresh();
            map.Refresh();
            collection.Refresh();
            chestModal.Refresh();
            guild.Refresh();
            daily.Refresh();
            shop.Refresh();
            settings.Refresh();
            candidateModal.Refresh();

            pete.Refresh(modalOpen || motherLode.IsActive || offlineModal.IsOpen);

            autosaveIn -= Time.unscaledDeltaTime;
            if (autosaveIn <= 0)
                Save();
        }

        void OnApplicationPause(bool paused)
        {
            // A full screen ad or the OS permission dialog pauses the app on Android; that is
            // neither a leave nor a return.
            if (paused && (ads.IsShowing || interstitials.IsShowing || permissionPending))
            {
                pausedUnderAd = true;
                Save();
                return;
            }
            if (!paused && pausedUnderAd)
            {
                pausedUnderAd = false;
                return;
            }
            if (paused)
            {
                motherLode.FinishNow();
                session.ClearPendingInterstitial();
                session.Events.Emit("session_end", session.EndSessionParameters(Time.realtimeSinceStartup - foregroundSince));
                pausedAtUtc = DateTime.UtcNow;
                wasPaused = true;
                ScheduleNotifications();
                Save(true);
                return;
            }
            if (wasPaused)
            {
                ReturnFromNotifications();
                session.RecordSessionHour(DateTimeOffset.Now);
            }
            if (!returnPending && !offlineModal.HasUnclaimed)
            {
                foregroundSince = Time.realtimeSinceStartup;
                // Unity also reports an unpause at launch; only a real return counts.
                if (wasPaused)
                    session.Events.Emit("app_resume", ("away_s", (long)(DateTime.UtcNow - pausedAtUtc).TotalSeconds));
                ads.Load();
                BeginReturn();
            }
        }

        void OnApplicationQuit() => Save(true);

        void Save() => Save(false);

        /// <param name="leaving">The app is going to the background: upload to the cloud now.</param>
        void Save(bool leaving)
        {
            if (resetting)
                return;
            autosaveIn = AutosaveSeconds;
            // An unresolved return keeps the old leave stamp so the haul is not lost if the app dies now.
            if (!returnPending && !offlineModal.HasUnclaimed)
                clock.StampLeave(session.Progress);
            SaveStore.Save(session.Progress);
            cloud.Upload(leaving);
        }

        // --- Ads (design doc 8.4) ---

        void CreateAds()
        {
            if (Application.isEditor)
            {
                ads = new FakeRewardedAds();
                interstitials = new FakeInterstitialAds();
                interstitials.Opened += StampFullScreenAd;
            }
            else
            {
                admob = new AdMobAds { Log = (name, parameters) => session.Events.Emit(name, parameters) };
                admob.ConsentResolved += FirebaseServices.SetConsent;
                ads = admob;
                interstitials = admob;
            }
            ads.Opened += StampFullScreenAd;
            ads.Rewarded += placement => session.Events.Emit("ad_rewarded", ("placement", placement));
        }

        void StampFullScreenAd() => session.MarkFullScreenAdShown(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);

        /// <summary>
        /// Plays the interstitial a creek unlock, tier or rebirth earned once the screen is calm:
        /// no panel, modal, Pete line, Mother Lode, return, tier show or journey to a creek in
        /// progress (studio ad policy 2-4).
        /// </summary>
        void UpdateInterstitial(bool modalOpen)
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            string block = session.InterstitialBlock(now);
            // Early players and paying players never even request one.
            if (block == null || block == "cooldown")
                interstitials.Load();

            bool settled = !modalOpen && !motherLode.IsActive && !returnPending && !offlineModal.HasUnclaimed
                && !pete.IsShowing && !ads.IsShowing && !interstitials.IsShowing && !creek.ShowPlaying;
            settledFor = settled ? settledFor + Time.unscaledDeltaTime : 0;
            if (session.PendingInterstitial == null || settledFor < BreakSettleSeconds)
                return;
            string trigger = session.PendingInterstitial;
            if (session.TakeInterstitial(now, interstitials.IsLoaded))
                interstitials.Show(trigger, Save);
        }

        // --- Store (roadmap 3.5) ---

        /// <summary>RevenueCat on a device once its key exists; the test sheet otherwise.</summary>
        IStore CreateStore(Transform root)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(RevenueCatKeys.Google))
            {
                var real = new RevenueCatStore(gameObject, RevenueCatKeys.Google);
                // A reinstall brings Remove Ads and the gear slot back without a button press.
                real.Restore(owned =>
                {
                    if (session.RestoreOwned(owned))
                        Save();
                    ReportPayer();
                });
                return real;
            }
#endif
            return new FakeStore(root);
        }

        void ReportPayer()
        {
            FirebaseServices.SetUserProperty("payer_tier", session.PayerTier);
            FirebaseServices.SetUserProperty("ads_removed", session.Progress.AdsRemoved ? "1" : "0");
        }

        // --- Notifications (design doc 10.1) ---

        /// <summary>Plans the chain for this leave; nothing is planned without permission.</summary>
        void ScheduleNotifications()
        {
            if (!notifications.Allowed)
                return;
            List<PlannedNotification> plan = session.PlanNotifications(DateTimeOffset.Now);
            if (debugNotifSoon)
            {
                debugNotifSoon = false;
                plan.Insert(0, new PlannedNotification(NotificationKind.CapFull, "rewards", DateTimeOffset.Now.AddMinutes(1),
                    "Test from the debug button. Tap me.", 0));
            }
            notifications.Schedule(plan);
        }

        /// <summary>Cancels whatever is pending and settles what fired while away (10.1.1 rule 3).</summary>
        void ReturnFromNotifications()
        {
            notifications.CancelAll();
            session.ResolveNotifications(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0, notifications.TakeOpened());
            FirebaseServices.SetUserProperty("notif_opt_in", notifications.Allowed ? "1" : "0");
        }

        /// <summary>Amos's card at the first Pine Hollow unlock or after a later welcome back (10.1.4).</summary>
        void UpdateNotifAsk(bool modalOpen)
        {
            if (notifAsk.IsOpen || permissionPending || session.IntroSkipped)
                return;
            bool busy = modalOpen || motherLode.IsActive || returnPending || offlineModal.HasUnclaimed
                || pete.IsShowing || ads.IsShowing || interstitials.IsShowing;
            askSettledFor = busy ? 0 : askSettledFor + Time.unscaledDeltaTime;
            if (askSettledFor < BreakSettleSeconds)
                return;
            bool afterOffline = askAfterOffline;
            askAfterOffline = false;
            if (session.NotifAskDue(afterOffline, notifications.Allowed))
                OpenNotifAsk(afterOffline);
        }

        void OpenNotifAsk(bool afterOffline)
        {
            notifAsk.Open(yes =>
            {
                if (!yes)
                {
                    session.RecordNotifAsk(afterOffline, false, "skipped");
                    Save();
                    return;
                }
                permissionPending = true;
                notifications.RequestPermission(result =>
                {
                    permissionPending = false;
                    session.RecordNotifAsk(afterOffline, true, result);
                    FirebaseServices.SetUserProperty("notif_opt_in", result == "granted" ? "1" : "0");
                    Save();
                });
            });
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
            // Its purchase sheet puts itself on top when it opens.
            store = CreateStore(root);
            const float topHeight = 330;
            const float bottomHeight = 220;

            RectTransform creekRect = Ui.Rect("Creek", root).Place(Vector2.zero, Vector2.one, new Vector2(0, bottomHeight), new Vector2(0, -topHeight));
            creek = creekRect.gameObject.AddComponent<CreekView>();
            creek.Init(session);
            // Above the creek, below the bars, so the Dollar counter stays in view during the event.
            motherLode = new MotherLodeView(session, root, creekRect);
            motherLode.Finished += Save;

            // Three rows (design doc 12.2): buttons and the Dollar counter, Gems and status, the
            // goal bar. Every control is at least TapHeight tall: 48 dp on a 1080 px wide phone.
            RectTransform top = Ui.Image("TopBar", root, Palette.Bar).rectTransform
                .Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -topHeight), Vector2.zero);
            // A dark rule where the cream bar meets the painting.
            Ui.Image("Edge", top, Palette.Text).rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(0, -6), Vector2.zero);

            Button settingsButton = Ui.Button("SettingsButton", top, "Settings", Palette.ButtonAlt, OpenSettings, out Text settingsLabel, 26);
            settingsButton.AsRect().Box(new Vector2(0, 1), new Vector2(Ui.TapHeight, Ui.TapHeight), new Vector2(16, -14));
            Image settingsIcon = Ui.Icon("Icon", settingsButton.transform, Art.Icon("settings"));
            if (settingsIcon != null)
            {
                settingsIcon.rectTransform.Fill(16);
                settingsLabel.SetText("");
            }
            dailyButton = Ui.Button("DailyButton", top, "", Palette.ButtonAlt, () => daily.Open(), out dailyLabel, 30);
            dailyButton.AsRect().Box(new Vector2(0, 1), new Vector2(210, Ui.TapHeight), new Vector2(146, -14));
            LeadIcon(dailyButton, dailyLabel, Art.Icon("gift"));
            guildButton = Ui.Button("GuildButton", top, "", Palette.ButtonAlt, () => guild.Open(), out guildLabel, 30);
            guildButton.AsRect().Box(Vector2.one, new Vector2(230, Ui.TapHeight), new Vector2(-16, -14));
            LeadIcon(guildButton, guildLabel, Art.GuildEmblem);

            // The coin sits just left of the amount, however long it gets.
            dollarsLabel = Ui.Label("Dollars", top, "", 104, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            dollarsLabel.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(460, -144), new Vector2(-256, -4));
            IconBesideText.Attach(dollarsLabel, Art.Dollar, Palette.Gold, 92);
            // Late-game amounts are long; shrink rather than wrap.
            dollarsLabel.resizeTextForBestFit = true;
            dollarsLabel.resizeTextMinSize = 56;
            dollarsLabel.resizeTextMaxSize = 104;
            // Best fit only shrinks text that may not overflow.
            dollarsLabel.verticalOverflow = VerticalWrapMode.Truncate;

            Ui.Icon("GemIcon", top, Art.Gem, Palette.Gem).rectTransform
                .Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -202), new Vector2(80, -148));
            gemsLabel = Ui.Label("Gems", top, "", 40, TextAnchor.MiddleLeft, Palette.GemText, FontStyle.Bold);
            gemsLabel.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(92, -206), new Vector2(330, -144));
            statusLabel = Ui.Label("Status", top, "", 36, TextAnchor.MiddleCenter, Palette.TextMuted);
            statusLabel.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(330, -206), new Vector2(-30, -144));
            statusLabel.resizeTextForBestFit = true;
            statusLabel.resizeTextMinSize = 24;
            statusLabel.resizeTextMaxSize = 36;
            statusLabel.verticalOverflow = VerticalWrapMode.Truncate;

            goalButton = Ui.Button("GoalButton", top, "", Palette.Row, ClaimGoal, out goalLabel, 34);
            goalButton.AsRect().Place(Vector2.zero, new Vector2(1, 0), new Vector2(20, 4), new Vector2(-250, 4 + Ui.TapHeight));
            // An unfinished goal is a plain strip, not a greyed-out button.
            ColorBlock goalColors = goalButton.colors;
            goalColors.disabledColor = Color.white;
            goalButton.colors = goalColors;
            goalLabel.name = "Goal";
            Image goalIcon = Ui.Icon("Icon", goalButton.transform, Art.Icon("goals"));
            if (goalIcon != null)
            {
                goalIcon.rectTransform.Place(Vector2.zero, new Vector2(0, 1), new Vector2(0, 20), new Vector2(80, -20));
                goalLabel.rectTransform.offsetMin = new Vector2(88, 8);
            }
            // Goal bonus (design doc 8.4): a rewarded ad for a few Gems, a handful of times a day.
            goalAd = Ui.Button("GoalAd", top, "", Palette.Ad, WatchGoalAd, out goalAdLabel, 28);
            goalAd.AsRect().Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-240, 4), new Vector2(-20, 4 + Ui.TapHeight));
            goalAd.SetActive(false);

            RectTransform bottom = Ui.Image("BottomBar", root, Palette.Bar).rectTransform
                .Place(Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, bottomHeight));
            Ui.Image("Edge", bottom, Palette.Text).rectTransform.Place(new Vector2(0, 1), Vector2.one, Vector2.zero, new Vector2(0, 6));
            // Map / Upgrades / Shop (design doc 12.2).
            mapButton = Ui.Button("MapButton", bottom, "Map", Palette.ButtonAlt, OpenMap, out mapLabel, 44);
            mapButton.AsRect().Place(Vector2.zero, new Vector2(1 / 3f, 1), new Vector2(30, 30), new Vector2(-10, -30));
            TopIcon(mapButton, mapLabel, Art.Icon("map"));
            upgradesButton = Ui.Button("UpgradesButton", bottom, "Upgrades", Palette.Button, () => OpenUpgrades(false), out upgradesLabel, 40);
            upgradesButton.AsRect().Place(new Vector2(1 / 3f, 0), new Vector2(0.53f, 1), new Vector2(10, 30), new Vector2(-4, -30));
            TopIcon(upgradesButton, upgradesLabel, Art.Icon("upgrades"));
            // Quick buy (design doc 12.2, v0.19): one tap buys the cheapest affordable sluice
            // upgrade without opening the panel; holding keeps buying.
            quickBuyButton = Ui.Button("QuickBuy", bottom, "", Palette.ButtonAlt, QuickBuy, out quickBuyLabel, 28);
            quickBuyButton.AsRect().Place(new Vector2(0.53f, 0), new Vector2(2 / 3f, 1), new Vector2(4, 30), new Vector2(-10, -30));
            quickBuyButton.gameObject.AddComponent<HoldRepeat>().Repeat = QuickBuy;
            shopButton = Ui.Button("ShopButton", bottom, "Shop", Palette.GemButton, OpenShop, out shopLabel, 44);
            shopButton.AsRect().Place(new Vector2(2 / 3f, 0), Vector2.one, new Vector2(10, 30), new Vector2(-30, -30));
            TopIcon(shopButton, shopLabel, Art.Icon("shop"));

            lateDoubleChip = Ui.Button("LateDouble", root, "Double your last haul?", Palette.Ad, WatchLateDouble, out _, 34);
            lateDoubleChip.AsRect().Box(new Vector2(0.5f, 1), new Vector2(560, Ui.TapHeight), new Vector2(0, -topHeight - 20));
            lateDoubleChip.SetActive(false);

            crewChip = Ui.Button("CrewChip", root, "", Palette.GemButton, () => candidateModal.Open(), out crewChipLabel, 34);
            crewChip.AsRect().Box(new Vector2(0, 1), new Vector2(360, Ui.TapHeight), new Vector2(20, -topHeight - 20));
            crewChip.SetActive(false);

            Button collectionButton = Ui.Button("CollectionButton", root, "", Palette.ButtonAlt, () => collection.Open(), out collectionLabel, 32);
            collectionButton.AsRect().Box(new Vector2(1, 0), new Vector2(300, Ui.TapHeight), new Vector2(-20, bottomHeight + 155));

            claimChip = Ui.Button("ClaimChip", root, "New claim?", Palette.Button, () => guild.Open(), out _, 32);
            claimChip.AsRect().Box(new Vector2(0, 1), new Vector2(360, Ui.TapHeight), new Vector2(20, -topHeight - 150));
            claimChip.SetActive(false);

            chestChip = Ui.Button("ChestChip", root, "", Palette.Nugget, () => chestModal.Open(), out chestChipLabel, 32);
            chestChip.AsRect().Box(new Vector2(1, 0), new Vector2(300, Ui.TapHeight), new Vector2(-20, bottomHeight + 290));
            chestChip.SetActive(false);

            summonLode = Ui.Button("SummonLode", root, "", Palette.GemButton, SummonMotherLode, out Text summonLabel, 32);
            summonLode.AsRect().Box(new Vector2(1, 0), new Vector2(400, Ui.TapHeight), new Vector2(-20, bottomHeight + 20));
            summonLabel.SetText($"Mother Lode  {Effects.Gems(session.Economy.Config.MotherLodeSummonGems)}");
            summonLode.SetActive(false);

            pete = new PeteBanner(session, root, bottomHeight);

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
            upgrades.Purchased += () => Sound.Play(Sfx.Upgrade);
            map = new MapPanel(session, root);
            // A creek unlock may bring crew candidates (design doc 6.3); show them right away.
            map.RegionChanged += () =>
            {
                Haptics.Important();
                candidateModal.Open();
            };
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
            offlineModal.Claimed += () =>
            {
                // Second session (design doc 10): Amos brings a chest after the first real haul.
                session.GiveReturnGift();
                askAfterOffline = true;
                Save();
            };
            candidateModal = new CandidateModal(session, root);
            notifAsk = new NotifAskCard(root);
            candidateModal.Hired += () =>
            {
                Haptics.Important();
                Sound.Play(Sfx.Crew);
                Save();
            };
            shop = new ShopPanel(session, store, root);
            shop.Purchased += () =>
            {
                ReportPayer();
                Save();
            };
            shop.SummonLodeRequested += () =>
            {
                shop.Close();
                SummonMotherLode();
            };
            settings = new SettingsPanel(session.Progress, () => admob != null && admob.PrivacyOptionsRequired, cloud, root);
            settings.Changed += Save;
            settings.PrivacyChoicesRequested += () => admob?.ShowPrivacyOptions();
            settings.DeleteConfirmed += DeleteMyData;
            // Last, so it covers everything on a first launch.
            gate = new PrivacyGate(root);
            gate.Accepted += AcceptGate;
            // Above the gate: a restored save answers it.
            cloudChoice = new CloudChoiceModal(root);
            cloudChoice.KeepLocal += () =>
            {
                session.Events.Emit("cloud_restore", ("stage", "ask"), ("code", "kept_local"));
                cloud.KeepLocal();
            };
            cloudChoice.LoadCloud += () => RestoreFromCloud(pendingCloud, "chosen");
        }

        // --- Cloud backup (design doc 13.1) ---

        void OnCloudFound(PlayerProgress found, CloudChoice choice)
        {
            if (choice == CloudChoice.RestoreCloud)
            {
                RestoreFromCloud(found, SaveStore.LoadDamaged ? "damaged" : "fresh");
                return;
            }
            pendingCloud = found;
            cloudChoice.Open(session.Progress, found);
        }

        /// <summary>Writes the cloud copy as this phone's save and starts over from it.</summary>
        void RestoreFromCloud(PlayerProgress restored, string reason)
        {
            session.Events.Emit("cloud_restore", ("stage", reason), ("code", "ok"));
            CloudSave.PrepareRestored(restored);
            notifications.CancelAll();
            resetting = true;
            SaveStore.Save(restored);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // --- Privacy (design doc 14.2) ---

        void AcceptGate(AgeBand band)
        {
            Compliance.Accept(session.Progress, band);
            Save();
            DataAudience audience = Compliance.Audience(band);
            FirebaseServices.Start(audience);
            admob?.Start(audience);
            session.Events.Emit("privacy_accept", ("age_band", band == AgeBand.Adult ? "adult" : band == AgeBand.Teen ? "teen" : "child"));
        }

        void OpenSettings()
        {
            if (motherLode.IsActive)
                return;
            upgrades.Close();
            map.Close();
            shop.Close();
            settings.Open();
        }

        /// <summary>
        /// Deletes the save, the analytics data and the consent answer, then starts over at the
        /// first-launch screen. Purchases stay with the store account and come back on restore.
        /// </summary>
        void DeleteMyData()
        {
            notifications.CancelAll();
            FirebaseServices.DeleteData();
            admob?.ResetConsent();
            cloud.Delete();
            ResetGame();
        }

        void OpenUpgrades(bool focusAmos)
        {
            if (motherLode.IsActive)
                return;
            map.Close();
            shop.Close();
            upgrades.Open(focusAmos);
        }

        void OpenMap()
        {
            if (motherLode.IsActive)
                return;
            upgrades.Close();
            shop.Close();
            map.Open();
        }

        void OpenShop()
        {
            if (motherLode.IsActive)
                return;
            upgrades.Close();
            map.Close();
            shop.Open();
            session.Events.Emit("shop_open", ("from", "hud"));
        }

        void RefreshHud()
        {
            PlayerProgress progress = session.Progress;
            dollarsLabel.SetText(NumberFormat.Dollars(progress.Dollars));
            string idle = session.IdleActive ? $"Amos +{NumberFormat.DollarsPerSecond(session.IdleRate)}" : "No crew yet";
            string boost = session.BoostMultiplier > 1
                ? $"  |  x{session.BoostMultiplier:0} {CandidateModal.Clock(session.BoostSecondsLeft)}" : "";
            string goldWash = progress.GoldWashSecondsLeft > 0
                ? $"  |  Gold Wash {CandidateModal.Clock(progress.GoldWashSecondsLeft)}" : "";
            statusLabel.SetText($"{session.RegionName}  |  {idle}{boost}{goldWash}");
            SetLock(dailyButton, dailyLabel, Feature.Daily, "Daily", "Daily\ntomorrow", daily.AnythingToCollect());
            gemsLabel.SetText(progress.Gems.ToString());
            RefreshGoal();
            RefreshGoalAd();

            bool upgradeBadge = upgrades.AnythingAffordable();
            bool mapBadge = session.CanAfford(session.NextRegionCost);
            SetLock(upgradesButton, upgradesLabel, Feature.Upgrades, "Upgrades", "Upgrades\nswipe 15 by hand", upgradeBadge);
            RefreshQuickBuy();
            SetLock(mapButton, mapLabel, Feature.Map, "Map", "Map\nhire Amos first", mapBadge);
            SetLock(shopButton, shopLabel, Feature.Shop, "Shop", "Shop\nreach Pine Hollow", shop.HasTimedOffer());
            collectionLabel.SetText($"Nuggets  {session.TotalStars}/{session.MaxStars}");
            collectionLabel.transform.parent.gameObject.SetActive(!motherLode.IsActive);
            chestChip.SetActive(session.HasChest && !chestModal.IsOpen && !motherLode.IsActive);
            chestChipLabel.SetText(ChestChipText());
            SetLock(guildButton, guildLabel, Feature.Guild, $"Guild\nLv {session.GuildLevel}", "Guild\nRed Gulch");
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

        /// <summary>A locked button stays visible and says what opens it (design doc 9.1, 12.2).</summary>
        void QuickBuy()
        {
            if (session.QuickBuy())
            {
                Sound.Play(Sfx.Upgrade);
                RefreshHud();
            }
        }

        void RefreshQuickBuy()
        {
            bool open = session.IsUnlocked(Feature.Upgrades);
            quickBuyButton.SetActive(open);
            if (!open)
                return;
            int index = session.QuickBuyUpgrade;
            if (index < 0)
            {
                quickBuyLabel.SetText("Quick buy\n-");
                if (quickBuyButton.interactable)
                    quickBuyButton.interactable = false;
                return;
            }
            UpgradeDefinition upgrade = GameCatalog.Upgrades[index];
            int count = session.AffordableUpgradeCount;
            quickBuyLabel.SetText($"+ {upgrade.Name} L{session.UpgradeLevel(index) + 1}\n{NumberFormat.Dollars(session.UpgradeCost(index).Value)}" + (count > 1 ? $"  ({count})" : ""));
            if (!quickBuyButton.interactable)
                quickBuyButton.interactable = true;
        }

        void SetLock(Button button, Text label, Feature feature, string open, string locked, bool badge = false)
        {
            bool unlocked = session.IsUnlocked(feature);
            label.SetText(unlocked ? open : locked);
            if (!badges.TryGetValue(button, out Image dot))
                badges[button] = dot = Ui.Badge(button);
            dot.SetActive(unlocked && badge);
            if (button.interactable != unlocked)
                button.interactable = unlocked;
            if (lockIcons.TryGetValue(button, out (Image icon, Sprite open) entry))
            {
                Sprite sprite = unlocked ? entry.open : Art.Icon("lock") ?? entry.open;
                if (entry.icon.sprite != sprite)
                    entry.icon.sprite = sprite;
            }
        }

        /// <summary>Icon on the left of a top bar button; the label keeps the rest.</summary>
        void LeadIcon(Button button, Text label, Sprite sprite)
        {
            Image icon = Ui.Icon("Icon", button.transform, sprite);
            if (icon == null)
                return;
            icon.rectTransform.Place(Vector2.zero, new Vector2(0, 1), new Vector2(10, 26), new Vector2(78, -26));
            label.rectTransform.offsetMin = new Vector2(80, 8);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = Mathf.RoundToInt(label.fontSize * Ui.TextScale);
            label.verticalOverflow = VerticalWrapMode.Truncate;
            lockIcons[button] = (icon, sprite);
        }

        /// <summary>Icon above the label of a bottom bar button.</summary>
        void TopIcon(Button button, Text label, Sprite sprite)
        {
            Image icon = Ui.Icon("Icon", button.transform, sprite);
            if (icon == null)
                return;
            icon.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(0, -84), new Vector2(0, -8));
            label.rectTransform.offsetMax = new Vector2(-8, -84);
            // Locked buttons say what opens them in two lines; shrink rather than spill.
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 20;
            label.resizeTextMaxSize = Mathf.RoundToInt(label.fontSize * Ui.TextScale);
            label.verticalOverflow = VerticalWrapMode.Truncate;
            lockIcons[button] = (icon, sprite);
        }

        // --- Mother Lode ---

        void StartMotherLode(MotherLodeRun run)
        {
            if (run == null)
                return;
            upgrades.Close();
            map.Close();
            shop.Close();
            candidateModal.Close();
            Haptics.Important();
            Sound.Play(Sfx.VeinStart);
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
            goalButton.GetComponent<Image>().color = done ? Palette.GemButton : Palette.Row;
            goalLabel.color = done ? Palette.TextLight : Palette.Text;
            if (goal == null)
                goalLabel.SetText(NextStep());
            else if (done)
                goalLabel.SetText($"Goal done! Tap to claim {Effects.Gems(session.CurrentGoalReward)}");
            else
                goalLabel.SetText($"Goal: {GoalText(goal)}  {session.GoalProgress(goal)}/{goal.Target}  (+{Effects.Gems(session.CurrentGoalReward)})");
        }

        void RefreshGoalAd()
        {
            // Never an ad prompt in the first five minutes (design doc 9).
            bool show = session.GoalAdsLeftToday > 0 && session.Progress.PlaySeconds >= 5 * 60 && !motherLode.IsActive;
            goalAd.SetActive(show);
            if (!show)
                return;
            goalAdLabel.SetText($"+{session.Economy.Config.GoalAdGems} Gems (ad)");
            if (goalAd.interactable != ads.IsLoaded)
                goalAd.interactable = ads.IsLoaded;
        }

        void WatchGoalAd()
        {
            ads.Show("goal_bar", rewarded =>
            {
                if (rewarded && session.ClaimGoalAd())
                    Save();
                ads.Load();
            });
        }

        void ClaimGoal()
        {
            if (session.ClaimGoal())
            {
                Sound.Play(Sfx.Goal);
                Save();
            }
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
            session.UtcOffsetSeconds = offset;
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
            ads.Show("late_double", rewarded =>
            {
                if (rewarded)
                    session.Earn(amount, IncomeSource.Offline);
            });
        }

        // --- Development-only helpers ---

        void BuildDebugButtons(Transform root, float topHeight)
        {
            RectTransform column = Ui.Rect("Debug", root).Box(Vector2.one, new Vector2(200, 1500), new Vector2(-10, -topHeight - 10));
            AddDebugButton(column, 0, "+$1K", () => session.Earn(1e3));
            AddDebugButton(column, 1, "+$1M", () => session.Earn(1e6));
            AddDebugButton(column, 2, "+$1T", () => session.Earn(1e12));
            AddDebugButton(column, 3, "+50 Gems", () => session.EarnGems(50));
            AddDebugButton(column, 4, "Away 2h", SimulateAway);
            AddDebugButton(column, 5, "Lode now", MakeMotherLodeDue);
            AddDebugButton(column, 6, "Layers", OpenCollectionLayers);
            AddDebugButton(column, 7, "Chest now", AddDebugChest);
            AddDebugButton(column, 8, "Next day", () => debugDayShift++);
            AddDebugButton(column, 9, "Skip intro", SkipIntro);
            AddDebugButton(column, 10, "Interstitial", DebugInterstitial);
            AddDebugButton(column, 11, AdMobAds.DebugEea ? "EEA: on" : "EEA: off", ToggleDebugEea);
            AddDebugButton(column, 12, "Notif 1m", () => debugNotifSoon = true);
            AddDebugButton(column, 13, "Ask notif", () => OpenNotifAsk(false));
            AddDebugButton(column, 14, "Reset", ResetGame);
        }

        static void AddDebugButton(RectTransform column, int index, string text, System.Action onClick)
        {
            Button button = Ui.Button(text, column, text, new Color(0, 0, 0, 0.45f), onClick, out _, 30);
            button.AsRect().Box(new Vector2(1, 1), new Vector2(200, 84), new Vector2(0, -index * 96));
        }

        /// <summary>Skips the locks and the first-launch screen for this session; nothing is accepted.</summary>
        void SkipIntro()
        {
            session.IntroSkipped = true;
            gate.Dismiss();
        }

        /// <summary>Consent form test (debug builds): takes effect on the next launch.</summary>
        void ToggleDebugEea()
        {
            if (admob == null)
                return;
            AdMobAds.DebugEea = !AdMobAds.DebugEea;
            Debug.Log($"[Ads] debug EEA {(AdMobAds.DebugEea ? "on" : "off")}; restart the app");
        }

        /// <summary>First press loads (early players never preload one), the next shows it.</summary>
        void DebugInterstitial()
        {
            if (interstitials.IsLoaded)
                interstitials.Show("debug", Save);
            else
                interstitials.Load();
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
