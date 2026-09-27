using System;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using GoogleMobileAds.Ump.Api;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    /// <summary>
    /// AdMob rewarded and interstitial ads behind the consent form (roadmap 3.4, studio ad
    /// policy rule 8): UMP consent first, then the SDK starts and ads load. When consent
    /// cannot be had the game runs without ads; nothing waits on it. Debug builds always use
    /// Google's test units, so a phone playtest never clicks a live ad.
    /// </summary>
    public sealed class AdMobAds : IRewardedAds, IInterstitialAds
    {
        // Google's sample units; see AdUnits for the live ones.
        const string TestRewardedUnit = "ca-app-pub-3940256099942544/5224354917";
        const string TestInterstitialUnit = "ca-app-pub-3940256099942544/1033173712";

        const float MaxRetrySeconds = 64;
        const string DebugEeaKey = "nc_debug_eea";

        /// <summary>
        /// Hashed ids of the team's test phones (from UMP's logcat hint); only they can be
        /// placed in the EEA for a consent form test.
        /// </summary>
        static readonly List<string> TestDeviceHashes = new List<string> { "C6072BA852407A04D43B8DDE46C30DAD" };

        /// <summary>
        /// Debug builds only: the next launch acts as if the phone were in the EEA, so the
        /// consent form shows. Toggling it forgets the stored answer.
        /// </summary>
        public static bool DebugEea
        {
            get => Debug.isDebugBuild && PlayerPrefs.GetInt(DebugEeaKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(DebugEeaKey, value ? 1 : 0);
                PlayerPrefs.Save();
                ConsentInformation.Reset();
            }
        }

        readonly Slot<RewardedAd> rewarded = new Slot<RewardedAd>("rewarded", AdUnits.Rewarded, TestRewardedUnit);
        readonly Slot<InterstitialAd> interstitial = new Slot<InterstitialAd>("interstitial", AdUnits.Interstitial, TestInterstitialUnit);

        bool initialized;
        bool rewardEarned;
        string placement;
        Action<bool> rewardedDone;
        Action interstitialDone;

        /// <summary>
        /// Sends ad_show (design doc 13.4.3, v0.20). Revenue comes from the ad_impression the
        /// Analytics SDK logs by itself once AdMob is linked to Firebase; a second ad_impression
        /// from here would count every impression twice.
        /// </summary>
        public Action<string, (string, object)[]> Log { get; set; }

        /// <summary>Raised once the consent form is settled, with the Consent Mode flags for Firebase.</summary>
        public event Action<TcfConsent> ConsentResolved;

        public bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>The player is in a region that needs a way back to the consent choices.</summary>
        public bool PrivacyOptionsRequired =>
            ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        bool IRewardedAds.IsLoaded => rewarded.Ad != null;
        bool IInterstitialAds.IsLoaded => interstitial.Ad != null;
        bool IRewardedAds.IsShowing => rewardedDone != null;
        bool IInterstitialAds.IsShowing => interstitialDone != null;

        public event Action Opened;
        public event Action Loaded;
        public event Action<string> Rewarded;

        /// <summary>Starts consent and the SDK for the player's age answer (design doc 8.6, 14.2).</summary>
        public void Start(DataAudience audience)
        {
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            // Before any request: child and teen treatment, and no mature ads in an Everyone game.
            MobileAds.SetRequestConfiguration(new RequestConfiguration
            {
                AgeRestrictedTreatment = audience.Child ? AgeRestrictedTreatment.Child
                    : audience.Teen ? AgeRestrictedTreatment.Teen : AgeRestrictedTreatment.Unspecified,
                TagForUnderAgeOfConsent = audience.UnderAgeOfConsent ? TagForUnderAgeOfConsent.True : TagForUnderAgeOfConsent.False,
                MaxAdContentRating = ToRating(audience.MaxAdRating),
            });
            // The queue behind ExecuteInUpdate; MobileAds.Initialize would create it too late for UMP.
            MobileAdsEventExecutor.Initialize();
            Debug.Log($"[Ads] start, can request {ConsentInformation.CanRequestAds()}");
            // A returning player who already answered can load ads while the form info refreshes.
            if (ConsentInformation.CanRequestAds())
                Initialize();
            // UMP answers on its own thread; everything after it runs on Unity's.
            var request = new ConsentRequestParameters { TagForUnderAgeOfConsent = audience.UnderAgeOfConsent };
            if (DebugEea)
                request.ConsentDebugSettings = new ConsentDebugSettings
                {
                    DebugGeography = DebugGeography.EEA,
                    TestDeviceHashedIds = TestDeviceHashes,
                };
            ConsentInformation.Update(request, updateError => MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                if (updateError != null)
                    Debug.LogWarning("Consent info update failed: " + updateError.Message);
                ConsentForm.LoadAndShowConsentFormIfRequired(formError => MobileAdsEventExecutor.ExecuteInUpdate(() =>
                {
                    if (formError != null)
                        Debug.LogWarning("Consent form failed: " + formError.Message);
                    Debug.Log($"[Ads] consent {ConsentInformation.ConsentStatus}, can request {ConsentInformation.CanRequestAds()}");
                    ConsentResolved?.Invoke(ReadTcfConsent());
                    if (ConsentInformation.CanRequestAds())
                        Initialize();
                }));
            }));
        }

        static MaxAdContentRating ToRating(AdRating rating)
        {
            switch (rating)
            {
                case AdRating.G:
                    return MaxAdContentRating.G;
                case AdRating.PG:
                    return MaxAdContentRating.PG;
                case AdRating.MA:
                    return MaxAdContentRating.MA;
                default:
                    return MaxAdContentRating.T;
            }
        }

        /// <summary>Forgets the consent answer ("Delete my data"); the form asks again next time.</summary>
        public void ResetConsent() => ConsentInformation.Reset();

        /// <summary>Reopens the consent choices (EEA and UK players).</summary>
        public void ShowPrivacyOptions()
        {
            ConsentForm.ShowPrivacyOptionsForm(error => MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                if (error != null)
                    Debug.LogWarning("Privacy options failed: " + error.Message);
                ConsentResolved?.Invoke(ReadTcfConsent());
            }));
        }

        void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            MobileAds.Initialize(_ =>
            {
                Debug.Log("[Ads] SDK ready");
                if (rewarded.Wanted)
                    LoadRewarded();
                if (interstitial.Wanted)
                    LoadInterstitial();
            });
        }

        // --- Rewarded ---

        void IRewardedAds.Load()
        {
            rewarded.Wanted = true;
            if (initialized)
                LoadRewarded();
        }

        void LoadRewarded()
        {
            if (!rewarded.CanLoad || !IsOnline)
                return;
            rewarded.Loading = true;
            RewardedAd.Load(rewarded.UnitId, new AdRequest(), (ad, error) =>
            {
                rewarded.Loading = false;
                if (error != null || ad == null)
                {
                    rewarded.Failed(error?.GetMessage());
                    return;
                }
                rewarded.Succeeded(ad);
                Debug.Log("[Ads] rewarded loaded");
                string where = null;
                ad.OnAdFullScreenContentOpened += () =>
                {
                    where = placement;
                    Opened?.Invoke();
                };
                ad.OnAdPaid += value => LogImpression(ad.GetResponseInfo(), rewarded, value, where);
                ad.OnAdFullScreenContentClosed += () => FinishRewarded(ad);
                ad.OnAdFullScreenContentFailed += error2 =>
                {
                    Debug.LogWarning("Rewarded failed to show: " + error2.GetMessage());
                    FinishRewarded(ad);
                };
                Loaded?.Invoke();
            });
        }

        void IRewardedAds.Show(string where, Action<bool> onFinished)
        {
            RewardedAd ad = rewarded.Ad;
            if (ad == null || !ad.CanShowAd() || rewardedDone != null)
            {
                onFinished?.Invoke(false);
                return;
            }
            rewarded.Ad = null;
            placement = where;
            rewardEarned = false;
            rewardedDone = onFinished;
            ad.Show(_ => rewardEarned = true);
        }

        void FinishRewarded(RewardedAd ad)
        {
            ad.Destroy();
            Action<bool> callback = rewardedDone;
            rewardedDone = null;
            if (rewardEarned)
                Rewarded?.Invoke(placement);
            callback?.Invoke(rewardEarned);
            LoadRewarded();
        }

        // --- Interstitial ---

        void IInterstitialAds.Load()
        {
            interstitial.Wanted = true;
            if (initialized)
                LoadInterstitial();
        }

        void LoadInterstitial()
        {
            if (!interstitial.CanLoad || !IsOnline)
                return;
            interstitial.Loading = true;
            InterstitialAd.Load(interstitial.UnitId, new AdRequest(), (ad, error) =>
            {
                interstitial.Loading = false;
                if (error != null || ad == null)
                {
                    interstitial.Failed(error?.GetMessage());
                    return;
                }
                interstitial.Succeeded(ad);
                Debug.Log("[Ads] interstitial loaded");
                string trigger = null;
                ad.OnAdFullScreenContentOpened += () =>
                {
                    trigger = placement;
                    Opened?.Invoke();
                };
                ad.OnAdPaid += value => LogImpression(ad.GetResponseInfo(), interstitial, value, trigger);
                ad.OnAdFullScreenContentClosed += () => FinishInterstitial(ad);
                ad.OnAdFullScreenContentFailed += error2 =>
                {
                    Debug.LogWarning("Interstitial failed to show: " + error2.GetMessage());
                    FinishInterstitial(ad);
                };
            });
        }

        void IInterstitialAds.Show(string trigger, Action onClosed)
        {
            InterstitialAd ad = interstitial.Ad;
            if (ad == null || !ad.CanShowAd() || interstitialDone != null)
            {
                onClosed?.Invoke();
                return;
            }
            interstitial.Ad = null;
            placement = trigger;
            interstitialDone = onClosed;
            ad.Show();
        }

        void FinishInterstitial(InterstitialAd ad)
        {
            ad.Destroy();
            Action callback = interstitialDone;
            interstitialDone = null;
            callback?.Invoke();
            LoadInterstitial();
        }

        /// <summary>Retries failed loads with a doubling wait (4 s up to 64 s).</summary>
        public void Tick(float deltaSeconds)
        {
            if (!initialized)
                return;
            if (rewarded.TickRetry(deltaSeconds))
                LoadRewarded();
            if (interstitial.TickRetry(deltaSeconds))
                LoadInterstitial();
        }

        void LogImpression<T>(ResponseInfo info, Slot<T> slot, AdValue value, string where) where T : class
        {
            AdapterResponseInfo adapter = info?.GetLoadedAdapterResponseInfo();
            Log?.Invoke("ad_show", new (string, object)[]
            {
                ("ad_platform", "AdMob"),
                ("ad_source", adapter?.AdSourceName ?? ""),
                ("ad_unit_name", slot.UnitId),
                ("ad_format", slot.Format),
                ("value", value.Value / 1e6),
                ("currency", value.CurrencyCode ?? "USD"),
                ("placement", where ?? ""),
            });
        }

        /// <summary>The IAB TCF strings UMP wrote to the default SharedPreferences.</summary>
        static TcfConsent ReadTcfConsent()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = new AndroidJavaClass("android.preference.PreferenceManager"))
                using (AndroidJavaObject prefs = manager.CallStatic<AndroidJavaObject>("getDefaultSharedPreferences", activity))
                {
                    int gdprApplies = prefs.Call<int>("getInt", "IABTCF_gdprApplies", -1);
                    string purposes = prefs.Call<string>("getString", "IABTCF_PurposeConsents", "");
                    return TcfConsent.From(gdprApplies, purposes);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Reading TCF consent failed: " + e.Message);
                return TcfConsent.From(1, null);
            }
        }

        /// <summary>One ad unit: the loaded ad, the load in flight and the retry wait.</summary>
        sealed class Slot<T> where T : class
        {
            public readonly string Format;
            public readonly string UnitId;
            public T Ad;
            public bool Loading;
            public bool Wanted;
            int failures;
            float retryIn = -1;

            public Slot(string format, string liveUnit, string testUnit)
            {
                Format = format;
                UnitId = Debug.isDebugBuild || string.IsNullOrEmpty(liveUnit) ? testUnit : liveUnit;
            }

            public bool CanLoad => Ad == null && !Loading && retryIn < 0;

            public void Succeeded(T ad)
            {
                Ad = ad;
                failures = 0;
            }

            public void Failed(string message)
            {
                Debug.LogWarning($"{Format} ad failed to load: {message}");
                failures++;
                retryIn = Mathf.Min(MaxRetrySeconds, 2f * (1 << Mathf.Min(failures, 6)));
            }

            /// <summary>True once when the retry wait runs out.</summary>
            public bool TickRetry(float deltaSeconds)
            {
                if (retryIn < 0)
                    return false;
                retryIn -= deltaSeconds;
                if (retryIn >= 0)
                    return false;
                retryIn = -1;
                return Wanted;
            }
        }
    }

    /// <summary>
    /// Live AdMob units (app ca-app-pub-9709993577664180~5438201458). Release builds only;
    /// the interstitial has a 2 per hour cap in the AdMob console (studio ad policy 4).
    /// </summary>
    static class AdUnits
    {
        public const string Rewarded = "ca-app-pub-9709993577664180/8120205111";
        public const string Interstitial = "ca-app-pub-9709993577664180/6008961375";
    }
}
