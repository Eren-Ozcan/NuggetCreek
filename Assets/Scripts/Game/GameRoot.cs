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
        PeteTutorial tutorial;
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
        CrateModal crateModal;
        PouchModal pouchModal;
        TrayView tray;
        GuildPanel guild;
        DailyPanel daily;
        Text dailyLabel;
        long debugDayShift;
        Text guildLabel;
        Image claimBar;
        Image claimHit;
        RectTransform guildCorner;
        RectTransform claimFill;
        Text claimLabel;
        Button collectionButton;
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
        Text statusLabel;
        Text goalLabel;
        Text mapLabel;
        Text upgradesLabel;
        Text quickBuyLabel;
        Button lateDoubleChip;
        Button crewChip;
        Text crewChipLabel;
        RectTransform eventColumn;
        RectTransform menuColumn;
        Image creekBadge;
        int shownBadgeRegion = -1;
        Image quickBuyIcon;
        Text quickBuyLevel;
        RectTransform crewColumn;
        readonly List<Button> crewFaces = new List<Button>();
        int shownCrewFaces = -1;
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
                || gate.IsOpen || settings.IsOpen || cloudChoice.IsOpen || crateModal.IsOpen || pouchModal.IsOpen;
            // Pete's lessons take the screen: the creek only listens on a river step.
            creek.InputEnabled = !modalOpen && (!tutorial.IsActive || tutorial.RiverOpen);
            creek.Floaters.Hold = tutorial.IsActive;
            creek.Floaters.HoldLine = creek.OpenWaterTop;
            if (motherLode.IsActive)
            {
                motherLode.Tick(dt);
                creek.TickScenery(dt);
            }
            else
            {
                creek.Tick(dt, idle);
                if (!modalOpen)
                    session.TickPlay(dt);
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
            crateModal.Refresh();
            pouchModal.Refresh();
            guild.Refresh();
            daily.Refresh();
            shop.Refresh();
            settings.Refresh();
            candidateModal.Refresh();

            // A lesson waits for a calm screen and pauses under anything that takes it over; it
            // may start over the crew offer, which opens by itself when a creek brings one.
            bool screenTaken = motherLode.IsActive || offlineModal.IsOpen || gate.IsOpen || cloudChoice.IsOpen || notifAsk.IsOpen
                || returnPending || store.IsBusy;
            bool panelOpen = upgrades.IsOpen || map.IsOpen || collection.IsOpen || chestModal.IsOpen || guild.IsOpen || daily.IsOpen
                || shop.IsOpen || settings.IsOpen || crateModal.IsOpen || pouchModal.IsOpen;
            tutorial.Tick(Time.unscaledDeltaTime, screenTaken, !panelOpen && !creek.ShowPlaying);

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
        /// no panel, modal, Pete lesson, Mother Lode, return, tier show or journey to a creek in
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
                && !tutorial.IsActive && !ads.IsShowing && !interstitials.IsShowing && !creek.ShowPlaying;
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
                || tutorial.IsActive || ads.IsShowing || interstitials.IsShowing;
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
            // The creek fills the screen and the controls float over the water (design doc 12.2):
            // the money card at the top, the guild badge and the crew down the left, events down
            // the right, the map, the upgrade button and the shop along the bottom. The dredge
            // works in the band between the top and bottom controls.
            RectTransform creekRect = Ui.Rect("Creek", root).Fill();
            creek = creekRect.gameObject.AddComponent<CreekView>();
            creek.Init(session, HudTop, HudBottom);
            // Above the creek, below the controls, so the Dollar counter stays in view during the event.
            motherLode = new MotherLodeView(session, root, creekRect, HudTop, SideMargin + SideSize + SideGap);
            motherLode.Finished += ShowLodeHaul;

            BuildTop(root);
            BuildSideColumns(root);
            BuildBottom(root);

            lateDoubleChip = Ui.Button("LateDouble", root, "Double your last haul?", Palette.Ad, WatchLateDouble, out _, 34);
            lateDoubleChip.AsRect().Box(new Vector2(0.5f, 1), new Vector2(560, Ui.TapHeight), new Vector2(0, -HudTop - 10));
            Float(lateDoubleChip);
            lateDoubleChip.SetActive(false);

            // Before the panels so full-screen modals cover them.
            if (Debug.isDebugBuild)
                BuildDebugButtons(root, HudTop);

            upgrades = new UpgradesPanel(session, root);
            collection = new CollectionPanel(session, root);
            chestModal = new ChestModal(session, ads, root);
            chestModal.Claimed += Save;
            crateModal = new CrateModal(session, ads, root);
            crateModal.Claimed += Save;
            pouchModal = new PouchModal(session, ads, root);
            pouchModal.Changed += Save;
            // A caught floater flies to its tile in the tray (design doc 3.1.4); a caught boulder
            // starts the Mother Lode (3.4) or a boss fight (3.4.1); neither starts by itself.
            creek.Floaters.TrayPoint = kind => creek.ToCreek(tray.LandingPoint(kind));
            creek.Floaters.SideInset = SideMargin + SideSize + SideGap;
            creek.Floaters.Caught += kind =>
            {
                if (kind == FloaterKind.Boulder)
                    StartMotherLode(session.StartMotherLode());
                else if (kind == FloaterKind.Boss)
                    StartMotherLode(session.StartBossFight(session.CaughtBoss));
                Save();
            };
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
            // Over every panel a lesson opens; the gate and the choices below still cover it.
            tutorial = new PeteTutorial(session, root, FindCue);
            tutorial.Finished += _ => Save();
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

        /// <summary>Height kept clear at the top for the money card and the goal strip.</summary>
        const float HudTop = 336;
        /// <summary>Height kept clear at the bottom for the map, the upgrade button and the shop.</summary>
        const float HudBottom = 250;
        /// <summary>Screen kept free under the tray, for the shop button.</summary>
        const float TrayBottom = 206;
        /// <summary>Gap between the screen edge and the button columns down each side.</summary>
        const float SideMargin = 14;
        const float SideSize = 118;
        const float SideGap = 12;
        /// <summary>Tap area of the settings gear in the top-right corner; the gear itself is drawn smaller.</summary>
        const float SettingsSize = 120;
        /// <summary>Crew faces shown down the left; the rest are in the Upgrades panel.</summary>
        const int CrewFaces = 6;
        const float ClaimBarWidth = 136;
        const float ClaimBarHeight = 54;

        void BuildTop(Transform root)
        {
            // The money card: the creek's picture on its left edge, the Dollars, the creek and
            // Amos's rate under them, the Gems pill in its corner.
            RectTransform card = Ui.Panel("MoneyCard", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 1), new Vector2(600, 172), new Vector2(30, -26));
            Float(card);

            Image badgeRing = Ui.Image("CreekBadge", card, Palette.Text, Ui.Circle);
            badgeRing.raycastTarget = false;
            badgeRing.rectTransform.Box(new Vector2(0, 0.5f), new Vector2(150, 150), new Vector2(-62, 0));
            Image badgeMask = Ui.Image("Mask", badgeRing.transform, Color.white, Ui.Circle);
            badgeMask.raycastTarget = false;
            badgeMask.rectTransform.Fill(7);
            badgeMask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            creekBadge = Ui.Image("Creek", badgeMask.transform, Color.white);
            creekBadge.raycastTarget = false;
            creekBadge.preserveAspect = false;
            creekBadge.rectTransform.Fill();

            dollarsLabel = Ui.Label("Dollars", card, "", 84, TextAnchor.MiddleCenter, Palette.Text, FontStyle.Bold);
            dollarsLabel.rectTransform.Place(new Vector2(0, 1), Vector2.one, new Vector2(96, -112), new Vector2(-150, -8));
            // Late-game amounts are long; shrink rather than wrap. Best fit only shrinks text that may not overflow.
            dollarsLabel.resizeTextForBestFit = true;
            dollarsLabel.resizeTextMinSize = 44;
            dollarsLabel.resizeTextMaxSize = 84;
            dollarsLabel.verticalOverflow = VerticalWrapMode.Truncate;

            statusLabel = Ui.Label("Status", card, "", 28, TextAnchor.MiddleCenter, Palette.TextMuted);
            statusLabel.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(96, 14), new Vector2(-20, 62));
            statusLabel.resizeTextForBestFit = true;
            statusLabel.resizeTextMinSize = 18;
            statusLabel.resizeTextMaxSize = 28;
            statusLabel.verticalOverflow = VerticalWrapMode.Truncate;

            RectTransform gems = Ui.Panel("GemPill", card, Palette.GemButton).rectTransform
                .Box(Vector2.one, new Vector2(136, 66), new Vector2(-12, -16));
            Ui.Icon("GemIcon", gems, Art.Gem, Palette.Gem).rectTransform
                .Place(Vector2.zero, new Vector2(0, 1), new Vector2(8, 10), new Vector2(52, -10));
            gemsLabel = Ui.Label("Gems", gems, "", 34, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            gemsLabel.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(52, 4), new Vector2(-8, -4));
            gemsLabel.resizeTextForBestFit = true;
            gemsLabel.resizeTextMinSize = 20;
            gemsLabel.resizeTextMaxSize = 34;

            // The goal strip under the card: what to do next, green when it can be claimed.
            goalButton = Ui.Button("GoalButton", root, "", Palette.Row, ClaimGoal, out goalLabel, 28);
            goalButton.AsRect().Box(new Vector2(0.5f, 1), new Vector2(600, Ui.TapHeight), new Vector2(30, -204));
            Float(goalButton);
            // An unfinished goal is a plain strip, not a greyed-out button.
            ColorBlock goalColors = goalButton.colors;
            goalColors.disabledColor = Color.white;
            goalButton.colors = goalColors;
            goalLabel.name = "Goal";
            goalLabel.resizeTextForBestFit = true;
            goalLabel.resizeTextMinSize = 18;
            goalLabel.resizeTextMaxSize = 28;
            goalLabel.verticalOverflow = VerticalWrapMode.Truncate;
            Image goalIcon = Ui.Icon("Icon", goalButton.transform, Art.Icon("goals"));
            if (goalIcon != null)
            {
                goalIcon.rectTransform.Place(Vector2.zero, new Vector2(0, 1), new Vector2(10, 16), new Vector2(66, -10));
                goalLabel.rectTransform.offsetMin = new Vector2(72, 8);
            }

            // The corners: the guild badge on the left; on the right a faint gear, not a tile, so
            // the column under it holds only what comes and goes.
            guildButton = SquareButton("GuildButton", root, Art.GuildEmblem, Palette.ButtonAlt, () => guild.Open(), out guildLabel);
            guildButton.AsRect().Box(new Vector2(0, 1), new Vector2(136, 150), new Vector2(14, -26));
            BuildClaimBar(root);

            Button settingsButton = Ui.Button("SettingsButton", root, "", Color.clear, OpenSettings, out Text settingsLabel, 22);
            settingsButton.AsRect().Box(Vector2.one, new Vector2(SettingsSize, SettingsSize), new Vector2(-SideMargin, -26));
            Image gear = Ui.Icon("Icon", settingsButton.transform, Art.Icon("settings"));
            if (gear != null)
            {
                gear.color = Palette.Faint;
                gear.rectTransform.Fill(24);
            }
            else
            {
                settingsLabel.SetText("Settings");
                settingsLabel.color = Palette.Faint;
            }
        }

        /// <summary>
        /// The claim bar under the guild badge: how close a new claim is to paying off (design
        /// doc 5.0.5). A tap on it reaches the badge above, which opens the Guild where the claim
        /// is staked; the badge stays the one control, so the bar can be thinner than a tap target.
        /// </summary>
        void BuildClaimBar(Transform root)
        {
            claimBar = Ui.Panel("ClaimBar", root, Palette.Text);
            claimBar.raycastTarget = false;
            claimBar.rectTransform.Box(new Vector2(0, 1), new Vector2(ClaimBarWidth, ClaimBarHeight), new Vector2(14, -26 - 150 - SideGap));
            Float(claimBar);
            Image fill = Ui.Panel("Fill", claimBar.transform, Palette.Button);
            fill.raycastTarget = false;
            claimFill = fill.rectTransform;
            claimFill.Place(Vector2.zero, new Vector2(0, 1), new Vector2(5, 5), new Vector2(0, -5));
            claimLabel = Ui.Label("Label", claimBar.transform, "", 24, TextAnchor.MiddleCenter, Palette.TextLight, FontStyle.Bold);
            claimLabel.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(4, 0), new Vector2(-4, 0));
            claimLabel.resizeTextForBestFit = true;
            claimLabel.resizeTextMinSize = 14;
            claimLabel.resizeTextMaxSize = 24;
            Outlined(claimLabel);
            claimBar.SetActive(false);
            claimHit = Ui.Image("ClaimHit", guildButton.transform, Color.clear);
            claimHit.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(0, -SideGap - ClaimBarHeight), Vector2.zero);
            // The badge and the bar together, for Pete to point at.
            guildCorner = Ui.Rect("GuildCorner", root).Box(new Vector2(0, 1), new Vector2(ClaimBarWidth, 150 + SideGap + ClaimBarHeight), new Vector2(14, -26));
        }

        void RefreshClaimBar()
        {
            bool show = session.IsUnlocked(Feature.Guild) && !motherLode.IsActive;
            claimBar.SetActive(show);
            claimHit.SetActive(show);
            if (!show)
                return;
            double progress = session.ClaimProgress;
            claimFill.gameObject.SetActive(progress > 0.005);
            claimFill.anchorMax = new Vector2(Mathf.Max(0.12f, (float)progress), 1);
            claimFill.offsetMax = new Vector2(-5 * (float)progress, -5);
            bool ready = session.RebirthSuggested;
            claimLabel.SetText(ready ? "New claim!" : $"Claim {Mathf.FloorToInt((float)progress * 100)}%");
            float bob = ready ? UI.Motion.Bob(Time.unscaledTime) : 1;
            claimBar.transform.localScale = new Vector3(bob, bob, 1);
        }

        void BuildSideColumns(Transform root)
        {
            // Left, under the claim bar: a new crew candidate on top, then the crew's faces.
            crewColumn = Column("CrewColumn", root, new Vector2(0, 1), new Vector2(SideMargin, -26 - 150 - 2 * SideGap - ClaimBarHeight), TextAnchor.UpperLeft);
            crewChip = SquareButton("CrewChip", crewColumn, Art.Icon("plus"), Palette.GemButton, () => candidateModal.Open(), out crewChipLabel);
            crewChip.SetActive(false);

            // Right, under the settings gear: things that come and go, as icons only. A Daily with
            // something to collect waits on top; once collected it moves down beside the map.
            eventColumn = Column("EventColumn", root, Vector2.one, new Vector2(-SideMargin, -26 - SettingsSize - SideGap), TextAnchor.UpperRight);
            // Under it, the tray: one tile per caught chest, pouch and crate (design doc 3.1.4).
            tray = new TrayView(session, eventColumn, SideSize, SideGap, TrayBottom, OpenTrayItem);
        }

        /// <summary>The Daily sits in the right column while it has something to collect, else in the menu by the map.</summary>
        void PlaceDaily(bool waiting)
        {
            Transform parent = waiting ? eventColumn : menuColumn;
            if (dailyButton.transform.parent == parent)
                return;
            dailyButton.transform.SetParent(parent, false);
            dailyButton.transform.SetAsFirstSibling();
            IconOnly(dailyButton, waiting);
        }

        void OpenTrayItem(FloaterKind kind)
        {
            switch (kind)
            {
                case FloaterKind.Chest: chestModal.Open(); break;
                case FloaterKind.Pouch: pouchModal.Open(); break;
                default: crateModal.Open(); break;
            }
        }

        void BuildBottom(Transform root)
        {
            mapButton = SquareButton("MapButton", root, Art.Icon("map"), Palette.ButtonAlt, OpenMap, out mapLabel, 150);
            mapButton.AsRect().Box(Vector2.zero, new Vector2(150, 150), new Vector2(20, 40));
            // Over the map: the collection and, once collected, the Daily.
            menuColumn = Column("MenuColumn", root, Vector2.zero, new Vector2(20 + (150 - SideSize) / 2, 40 + 150 + SideGap), TextAnchor.LowerLeft);
            dailyButton = SquareButton("DailyButton", menuColumn, Art.Icon("gift"), Palette.ButtonAlt, () => daily.Open(), out dailyLabel);
            collectionButton = SquareButton("CollectionButton", menuColumn, Art.NuggetIcon, Palette.ButtonAlt, () => collection.Open(), out collectionLabel);
            shopButton = SquareButton("ShopButton", root, Art.Icon("shop"), Palette.GemButton, OpenShop, out shopLabel, 150);
            shopButton.AsRect().Box(new Vector2(1, 0), new Vector2(150, 150), new Vector2(-20, 40));

            // The upgrade button (design doc 12.2, v0.19): its left half buys the cheapest
            // affordable upgrade at once and keeps buying while held; its right half opens the panel.
            RectTransform card = Ui.Panel("UpgradeCard", root, Palette.Panel).rectTransform
                .Box(new Vector2(0.5f, 0), new Vector2(400, 190), new Vector2(0, 30));
            Float(card);
            quickBuyButton = Ui.Button("QuickBuy", card, "", Palette.ButtonAlt, QuickBuy, out quickBuyLabel, 30);
            quickBuyButton.AsRect().Place(Vector2.zero, new Vector2(0.5f, 1), new Vector2(12, 12), new Vector2(-4, -12));
            quickBuyButton.gameObject.AddComponent<HoldRepeat>().Repeat = QuickBuy;
            quickBuyIcon = Ui.Image("UpgradeIcon", quickBuyButton.transform, Color.white);
            quickBuyIcon.raycastTarget = false;
            quickBuyIcon.preserveAspect = true;
            quickBuyIcon.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(24, 58), new Vector2(-24, -12));
            quickBuyLabel.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(4, 14), new Vector2(-4, 56));
            quickBuyLabel.resizeTextForBestFit = true;
            quickBuyLabel.resizeTextMinSize = 16;
            quickBuyLabel.resizeTextMaxSize = 30;
            quickBuyLabel.verticalOverflow = VerticalWrapMode.Truncate;
            quickBuyLevel = Ui.Label("Level", quickBuyButton.transform, "", 26, TextAnchor.UpperLeft, Palette.TextLight, FontStyle.Bold);
            quickBuyLevel.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(0, -8));
            Outlined(quickBuyLevel);
            Outlined(quickBuyLabel);

            upgradesButton = Ui.Button("UpgradesButton", card, "Upgrades", Palette.Button, () => OpenUpgrades(false), out upgradesLabel, 26);
            upgradesButton.AsRect().Place(new Vector2(0.5f, 0), Vector2.one, new Vector2(4, 12), new Vector2(-12, -12));
            Image chevrons = Ui.Icon("Icon", upgradesButton.transform, Art.Icon("level_up") ?? Art.Icon("upgrades"));
            if (chevrons != null)
            {
                chevrons.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(24, 58), new Vector2(-24, -12));
                lockIcons[upgradesButton] = (chevrons, chevrons.sprite);
            }
            upgradesLabel.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(4, 14), new Vector2(-4, 56));
            upgradesLabel.resizeTextForBestFit = true;
            upgradesLabel.resizeTextMinSize = 14;
            upgradesLabel.resizeTextMaxSize = 26;
            upgradesLabel.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>A column that stacks its visible children from its corner, skipping hidden ones.</summary>
        static RectTransform Column(string name, Transform root, Vector2 corner, Vector2 position, TextAnchor alignment)
        {
            RectTransform column = Ui.Rect(name, root).Box(corner, new Vector2(SideSize, 1100), position);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = SideGap;
            layout.childAlignment = alignment;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return column;
        }

        /// <summary>A square control floating over the water: an icon with a short caption under it.</summary>
        Button SquareButton(string name, Transform parent, Sprite icon, Color color, System.Action onClick, out Text caption, float size = SideSize)
        {
            Button button = Ui.Button(name, parent, "", color, onClick, out caption, 24);
            button.AsRect().sizeDelta = new Vector2(size, size);
            float captionHeight = size * 0.3f;
            Image image = Ui.Icon("Icon", button.transform, icon);
            if (image != null)
            {
                image.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(14, captionHeight + 4), new Vector2(-14, -10));
                lockIcons[button] = (image, icon);
            }
            caption.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(4, 12), new Vector2(-4, captionHeight + 10));
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 12;
            caption.resizeTextMaxSize = Mathf.RoundToInt(size * 0.2f);
            caption.verticalOverflow = VerticalWrapMode.Truncate;
            Outlined(caption);
            Float(button);
            return button;
        }

        /// <summary>Lets a <see cref="SquareButton"/>'s icon fill the face without a caption, or makes room for one.</summary>
        void IconOnly(Button button, bool iconOnly)
        {
            if (!lockIcons.TryGetValue(button, out (Image icon, Sprite open) entry))
                return;
            float captionHeight = button.AsRect().sizeDelta.y * 0.3f;
            if (iconOnly)
                entry.icon.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(14, 14 + Ui.Lip), new Vector2(-14, -14));
            else
                entry.icon.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(14, captionHeight + 4), new Vector2(-14, -10));
        }

        /// <summary>A soft drop shadow, so a control reads as lying on the water.</summary>
        static void Float(Component control)
        {
            var shadow = control.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0.08f, 0.08f, 0.35f);
            shadow.effectDistance = new Vector2(0, -8);
        }

        static void Outlined(Text label)
        {
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.45f);
            outline.effectDistance = new Vector2(2, -2);
        }

        /// <summary>Shows the first hired crew down the left; tapping a face opens their row.</summary>
        void RefreshCrewColumn()
        {
            int hired = session.CrewHiredCount + (session.IdleActive ? 1 : 0);
            if (hired == shownCrewFaces)
                return;
            shownCrewFaces = hired;
            foreach (Button face in crewFaces)
                Destroy(face.gameObject);
            crewFaces.Clear();
            if (session.IdleActive)
                AddCrewFace("amos", true);
            for (int i = 0; i < GameCatalog.Crew.Count && crewFaces.Count < CrewFaces; i++)
            {
                if (session.CrewLevel(i) > 0)
                    AddCrewFace(GameCatalog.Crew[i].Id, false);
            }
        }

        void AddCrewFace(string id, bool amos)
        {
            Button face = Ui.Button("Crew_" + id, crewColumn, "", Palette.Panel, () => OpenUpgrades(amos), out Text label, 20);
            face.AsRect().sizeDelta = new Vector2(SideSize - 14, SideSize - 14);
            Image picture = Ui.Icon("Face", face.transform, Art.Portrait(id), Palette.Amos);
            picture.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(8, 8 + Ui.Lip), new Vector2(-8, -8));
            label.SetText("");
            Float(face);
            crewFaces.Add(face);
        }

        void RefreshCreekBadge()
        {
            int region = session.Progress.RegionIndex;
            if (region == shownBadgeRegion)
                return;
            shownBadgeRegion = region;
            Sprite painting = Art.Creek(region);
            creekBadge.sprite = painting;
            creekBadge.color = painting != null ? Color.white : Palette.Water;
            // Cover the circle: the painting is wider than tall.
            if (painting != null)
            {
                float aspect = painting.rect.width / painting.rect.height;
                creekBadge.rectTransform.Place(new Vector2(0.5f - aspect / 2, 0), new Vector2(0.5f + aspect / 2, 1), Vector2.zero, Vector2.zero);
            }
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
            bool dailyWaiting = session.IsUnlocked(Feature.Daily) && daily.AnythingToCollect();
            PlaceDaily(dailyWaiting);
            SetLock(dailyButton, dailyLabel, Feature.Daily, dailyWaiting ? "" : "Daily", "Tomorrow", dailyWaiting);
            gemsLabel.SetText(progress.Gems.ToString());
            RefreshCreekBadge();
            RefreshCrewColumn();
            RefreshGoal();

            bool upgradeBadge = upgrades.AnythingAffordable();
            bool mapBadge = session.CanAfford(session.NextRegionCost);
            SetLock(upgradesButton, upgradesLabel, Feature.Upgrades, "Upgrades", "Swipe 15 by hand", upgradeBadge);
            RefreshQuickBuy();
            SetLock(mapButton, mapLabel, Feature.Map, "Map", "Hire Amos", mapBadge);
            SetLock(shopButton, shopLabel, Feature.Shop, "Shop", "Pine Hollow", shop.HasTimedOffer());
            collectionLabel.SetText($"{session.TotalStars}/{session.MaxStars}");
            collectionLabel.transform.parent.gameObject.SetActive(!motherLode.IsActive);
            SetLock(guildButton, guildLabel, Feature.Guild, $"Guild Lv {session.GuildLevel}", "Red Gulch");
            RefreshClaimBar();

            float now = Time.realtimeSinceStartup;
            if (ads.IsLoaded)
                lateDouble.NotifyAdReady(now);
            lateDoubleChip.SetActive(lateDouble.IsVisible(now) && ads.IsLoaded && !offlineModal.IsOpen);

            crewChip.SetActive(session.HasCandidates && !candidateModal.IsOpen && !motherLode.IsActive);
            if (session.HasCandidates)
                crewChipLabel.SetText(CandidateModal.Clock(progress.CandidateSecondsLeft));
            // After the column's own buttons, so the tray knows how much room is left.
            tray.Refresh(motherLode.IsActive);
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
                quickBuyLabel.SetText("-");
                quickBuyLevel.SetText("");
                quickBuyIcon.color = quickBuyIcon.sprite != null ? Palette.IconOff : Color.clear;
                if (quickBuyButton.interactable)
                    quickBuyButton.interactable = false;
                return;
            }
            UpgradeDefinition upgrade = GameCatalog.Upgrades[index];
            int count = session.AffordableUpgradeCount;
            quickBuyLabel.SetText("+ " + NumberFormat.Dollars(session.UpgradeCost(index).Value));
            quickBuyLevel.SetText($"L{session.UpgradeLevel(index) + 1}" + (count > 1 ? $"  x{count}" : ""));
            Sprite icon = Art.Upgrade(upgrade.Id);
            if (quickBuyIcon.sprite != icon)
                quickBuyIcon.sprite = icon;
            quickBuyIcon.color = icon != null ? Color.white : Color.clear;
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

        // --- Old Pete's lessons ---

        /// <summary>Where a lesson step's cue is on screen now; null when it is not there to press.</summary>
        CueTarget FindCue(LessonStep step)
        {
            CueTarget target = Locate(step);
            // A step that waits for a press needs a control that takes one.
            if (target != null && step.Until == Until.Press && !target.Buttons.Exists(b => b.interactable))
                return null;
            return target;
        }

        CueTarget Locate(LessonStep step)
        {
            switch (step.Cue)
            {
                case Cue.Gold:
                    return CueTarget.At(() => creek.GoldWorldPoint() ?? creek.OpenWaterCentre, 80);
                case Cue.Floater:
                    return creek.Floaters.WorldPosition.HasValue && !motherLode.IsActive
                        ? CueTarget.At(() => creek.Floaters.WorldPosition, creek.Floaters.FloaterSize) : null;
                case Cue.GoalButton:
                    // Pressing waits for a finished goal; pointing it out does not.
                    return step.Until == Until.Press && !session.IsCurrentGoalComplete ? null : Shown(goalButton);
                case Cue.UpgradesButton:
                    return upgrades.IsOpen ? null : Shown(upgradesButton);
                case Cue.FirstUpgrade:
                    return upgrades.IsOpen ? InRow(upgrades.UpgradeButton(0)) : null;
                case Cue.AmosHire:
                    return upgrades.IsOpen ? InRow(upgrades.AmosButton) : null;
                case Cue.CloseUpgrades:
                    return upgrades.IsOpen ? Shown(upgrades.CloseButton) : null;
                case Cue.MapButton:
                    return map.IsOpen ? null : Shown(mapButton);
                case Cue.NextCreek:
                    return map.IsOpen && session.HasNextRegion ? Shown(map.ActionOf(session.Progress.RegionsUnlocked)) : null;
                case Cue.TrayChest:
                    return chestModal.IsOpen ? null : Shown(tray.TileOf(FloaterKind.Chest));
                case Cue.CrewChip:
                    return candidateModal.IsOpen ? null : Shown(crewChip);
                case Cue.CandidateHire:
                    return candidateModal.IsOpen ? CueTarget.Of(candidateModal.HireButtons, candidateModal.Card) : null;
                case Cue.CollectionButton:
                    return collection.IsOpen ? null : Shown(collectionButton);
                case Cue.BossNugget:
                    int boss = session.FirstBeatenBoss;
                    return collection.IsOpen && boss >= 0 ? CueTarget.Highlight(collection.CardOf(GameCatalog.BossNugget(boss))) : null;
                case Cue.CloseCollection:
                    return collection.IsOpen ? Shown(collection.CloseButton) : null;
                case Cue.DailyButton:
                    return daily.IsOpen ? null : Shown(dailyButton);
                case Cue.GuildButton:
                    return guild.IsOpen ? null : Shown(guildButton);
                case Cue.ClaimBar:
                    // Pointing at the bar; pressing goes through the badge above it.
                    if (guild.IsOpen || !claimBar.gameObject.activeInHierarchy)
                        return null;
                    if (step.Until != Until.Press)
                        return CueTarget.Highlight(claimBar.rectTransform);
                    CueTarget badge = CueTarget.Of(new[] { guildButton }, guildCorner);
                    badge.FramePasses = true;
                    badge.Pointer = claimBar.rectTransform;
                    return badge;
                case Cue.StakeClaim:
                    return guild.IsOpen ? Shown(guild.ClaimButton) : null;
                default:
                    return null;
            }
        }

        /// <summary>A control Pete can point at only while it is on screen.</summary>
        static CueTarget Shown(Button button) =>
            button != null && button.gameObject.activeInHierarchy ? CueTarget.Of(button) : null;

        /// <summary>A list row's button, with the whole row left clear so its name and price show.</summary>
        static CueTarget InRow(Button button) =>
            Shown(button) != null ? CueTarget.Of(button, (RectTransform)button.transform.parent) : null;

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

        /// <summary>No dialog after the event: the haul flies to its counters and a boss gets a banner.</summary>
        void ShowLodeHaul(MotherLodeRun run, BigNumber reward)
        {
            creek.FlyReward("+" + NumberFormat.Dollars(reward), Palette.Gold, creek.ToCreek(Center(dollarsLabel)));
            string gems = "+" + Effects.Gems(session.Economy.Config.MotherLodeGemReward);
            creek.FlyReward(gems, Palette.Gem, creek.ToCreek(Center(gemsLabel)), 0.25f);
            if (run.DroppedNugget >= 0)
                creek.ShowBanner($"NEW BOSS NUGGET!  {GameCatalog.Nuggets[run.DroppedNugget].Name}");
            else if (run.IsBossFight)
                creek.ShowBanner("The boss got away!");
            Save();
        }

        static Vector3 Center(Graphic graphic) => graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center);

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
            // Folded away behind a small toggle so it does not cover the creek. Hidden, not
            // inactive: the PlayMode tests press these buttons by name.
            RectTransform column = Ui.Rect("Debug", root).Box(new Vector2(0.5f, 1), new Vector2(200, 1500), new Vector2(0, -topHeight - 10));
            var fold = column.gameObject.AddComponent<CanvasGroup>();
            void ShowDebug(bool show)
            {
                fold.alpha = show ? 1 : 0;
                fold.blocksRaycasts = show;
            }
            ShowDebug(false);
            Button toggle = Ui.Button("DebugToggle", root, "DEV", new Color(0, 0, 0, 0.35f), () => ShowDebug(fold.alpha < 0.5f), out _, 26);
            // Bottom left, between the map and the upgrade button: clear of the menu over the map.
            toggle.AsRect().Box(Vector2.zero, new Vector2(96, 64), new Vector2(20 + 150 + SideGap, 40));
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
            AddDebugButton(column, 15, "Floaters now", DebugFloaters);
            AddDebugButton(column, 16, "Boss now", MakeBossDue);
        }

        static void AddDebugButton(RectTransform column, int index, string text, System.Action onClick)
        {
            Button button = Ui.Button(text, column, text, new Color(0, 0, 0, 0.45f), onClick, out _, 30);
            // Sixteen to a column; more start a second column to the left.
            const int perColumn = 16;
            button.AsRect().Box(new Vector2(1, 1), new Vector2(200, 84), new Vector2(-(index / perColumn) * 210, -(index % perColumn) * 96));
        }

        /// <summary>Skips the locks and the first-launch screen for this session; nothing is accepted.</summary>
        void SkipIntro()
        {
            session.IntroSkipped = true;
            tutorial.Stop();
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

        /// <summary>Floats the next boss in now, as if the creek's catches were done.</summary>
        void MakeBossDue()
        {
            session.Progress.ChestsOpened = System.Math.Max(1, session.Progress.ChestsOpened);
            session.Progress.CatchesTowardBoss = session.Economy.Config.BossAfterCatches;
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

        /// <summary>A pouch (with a trusted day) and a crate float in next, past the tutorial chest if needed.</summary>
        void DebugFloaters()
        {
            PlayerProgress progress = session.Progress;
            progress.ChestsOpened = System.Math.Max(1, progress.ChestsOpened);
            progress.CrateSecondsLeft = 0.01;
            progress.PouchSecondsLeft = 0.01;
            progress.PouchesToday = 0;
        }

        /// <summary>Earns a creek chest now; it floats down the creek like one earned by catches.</summary>
        void AddDebugChest() => session.Progress.ChestsDue++;

        void ResetGame()
        {
            resetting = true;
            SaveStore.Delete();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
