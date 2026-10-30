#if UNITY_ANDROID || UNITY_IOS
using System;
using Unity.Services.LevelPlay;

namespace BeamRunner.Services
{
    /// <summary>
    /// Unity LevelPlay (ads mediation, <c>com.unity.services.levelplay</c> 9.5.1) implementation for Android and
    /// iOS devices. <see cref="AdsServiceFactory"/> only creates it on a device build with an app key and at least
    /// one ad unit id for the platform (the Editor always uses <see cref="SimulatedAdsService"/>).
    /// <list type="bullet">
    /// <item>Privacy (GDPR consent, CCPA "do not sell" = !personalized, COPPA = false) is applied through
    /// <c>LevelPlayPrivacySettings</c> before <c>LevelPlay.Init</c> and again on every <see cref="SetConsent"/>.
    /// The value passed in is already the effective one (player choice AND iOS tracking authorization, see
    /// <see cref="ServiceHub"/>).</item>
    /// <item>After <c>OnInitSuccess</c> one <c>LevelPlayRewardedAd</c> and one <c>LevelPlayInterstitialAd</c> are
    /// created and loaded; each is reloaded after every show or display failure. Failed loads (and loads without
    /// any answer for 90 s) retry with capped exponential back-off (4 s → 120 s); a failed init retries after
    /// 10 s → 300 s. Loaded ads are re-checked with <c>IsAdReady()</c> every 60 s and before every show.</item>
    /// <item>A rewarded show reports true only when <c>OnAdRewarded</c> arrived — before <c>OnAdClosed</c> or up
    /// to 4 s after it (networks differ in the order).</item>
    /// <item>Every SDK callback is marshalled to the main thread through <see cref="ServiceRunner"/> and matched
    /// to the active request, so late or duplicate callbacks never complete the wrong request.</item>
    /// <item>Watchdogs (active app time, which stands still while an ad suspends the app) guarantee every show
    /// request completes: 30 s for the ad to appear, 240 s for it to finish, and (Android) 8 s after the app
    /// regains focus without a close callback. A reward that already arrived is honoured by all of them.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// LevelPlay 9.5.1 members used — every one of them is used the same way by the maintained
    /// <c>wagenheimer/UnityLevelPlayHelper</c> 2.12 wrapper (which pins levelplay 9.5.1):
    /// <c>LevelPlay.Init(string appKey)</c>, <c>LevelPlay.OnInitSuccess</c> (<c>LevelPlayConfiguration</c>),
    /// <c>LevelPlay.OnInitFailed</c> (<c>LevelPlayInitError.ErrorCode/ErrorMessage</c>),
    /// <c>LevelPlay.SetMetaData("is_test_suite", "enable")</c>, <c>LevelPlay.LaunchTestSuite()</c>,
    /// <c>LevelPlayPrivacySettings.SetGDPRConsent(bool)</c>, <c>.SetCCPA(bool)</c>, <c>.SetCOPPA(bool)</c>,
    /// <c>new LevelPlayRewardedAd(string)</c>, <c>new LevelPlayInterstitialAd(string)</c>, their <c>LoadAd()</c>,
    /// <c>ShowAd()</c>, <c>IsAdReady()</c> and events <c>OnAdLoaded</c> (<c>LevelPlayAdInfo</c>),
    /// <c>OnAdLoadFailed</c> (<c>LevelPlayAdError.ErrorCode/ErrorMessage</c>), <c>OnAdDisplayed</c>
    /// (<c>LevelPlayAdInfo.AdNetwork</c>), <c>OnAdDisplayFailed</c> (<c>LevelPlayAdInfo</c>, <c>LevelPlayAdError</c>),
    /// <c>OnAdClosed</c> (<c>LevelPlayAdInfo</c>) and <c>LevelPlayRewardedAd.OnAdRewarded</c> (<c>LevelPlayAdInfo</c>,
    /// <c>LevelPlayReward.Amount/Name</c>).
    /// Deliberately not used because the wrapper does not confirm them for 9.x: <c>LevelPlay.SetPauseGame</c>
    /// (on iOS the game keeps running under the ad; <see cref="IAdsService.AdShowingChanged"/> mutes it and the
    /// game flow checks <see cref="IAdsService.IsShowing"/>), adapter-debug / integration-helper calls (use the
    /// Ads Mediation settings asset instead) and <c>DestroyAd()</c> (the ad objects live for the whole session).
    /// </remarks>
    public sealed class LevelPlayAdsService : AdsServiceBase
    {
        private const double StartTimeoutSeconds = 30.0;
        private const double PlaybackTimeoutSeconds = 240.0;
        private const double FocusGraceSeconds = 8.0;
        private const float RewardGraceSeconds = 4f;
        private const double LoadTimeoutSeconds = 90.0;
        private const double ReadyRecheckSeconds = 60.0;
        private const double InitStallWarnSeconds = 60.0;
        private const float MinBackoffSeconds = 4f;
        private const float MaxBackoffSeconds = 120f;
        private const float MinInitRetrySeconds = 10f;
        private const float MaxInitRetrySeconds = 300f;

        /// <summary>
        /// Android shows ads in their own activity, so regaining focus means the ad is gone. iOS presents them inside
        /// the app (no focus change when they close): a focus change there means the player left the app, so only
        /// the timeouts apply.
        /// </summary>
        private static readonly bool FocusSignalsAdClosed =
#if UNITY_IOS
            false;
#else
            true;
#endif

        private enum InitState
        {
            NotStarted,
            Initializing,
            Ready,
            Failed,
        }

        private enum SlotState
        {
            Idle,
            Loading,
            Loaded,
        }

        private readonly string _appKey;
        private readonly bool _testSuite;
        private readonly Slot _rewarded;
        private readonly Slot _interstitial;

        private InitState _initState = InitState.NotStarted;
        private int _initAttempts;
        private int _initRetryTimer;
        private double _initStartedAt;
        private bool _initStallLogged;
        private bool _testSuiteLaunched;
        private bool _hooked;
        private bool _personalized;
        private double _nextReadyRecheck;
        private ShowRequest _active;

        /// <summary>
        /// Creates the service; nothing touches the SDK until <see cref="Initialize"/>. Empty ad unit ids disable
        /// that format. <paramref name="enableTestSuite"/> (development builds only) opens the LevelPlay Test
        /// Suite once after the SDK has initialized.
        /// </summary>
        public LevelPlayAdsService(string appKey, string rewardedAdUnitId, string interstitialAdUnitId,
            bool enableTestSuite)
        {
            _appKey = appKey ?? "";
            _testSuite = enableTestSuite;
            _rewarded = new Slot(rewardedAdUnitId ?? "", true);
            _interstitial = new Slot(interstitialAdUnitId ?? "", false);
        }

        /// <inheritdoc />
        public override bool IsInitialized => _initState == InitState.Ready;

        /// <inheritdoc />
        public override bool RewardedReady =>
            _initState == InitState.Ready && _active == null && !IsShowing && _rewarded.State == SlotState.Loaded;

        /// <inheritdoc />
        public override bool InterstitialReady =>
            Enabled && _initState == InitState.Ready && _active == null && !IsShowing &&
            _interstitial.State == SlotState.Loaded;

        /// <inheritdoc />
        public override void Initialize(bool personalizedConsent)
        {
            _personalized = personalizedConsent;
            ApplyPrivacy();
            if (_initState != InitState.NotStarted)
            {
                return;
            }

            if (!_hooked)
            {
                _hooked = true;
                ServiceRunner.Ticked += OnTick;
                ServiceRunner.FocusChanged += OnFocusChanged;
            }

            if (_testSuite)
            {
                // Must be set before LevelPlay.Init for LaunchTestSuite to work.
                SafeSdk(() => LevelPlay.SetMetaData("is_test_suite", "enable"), "LevelPlay.SetMetaData");
            }

            ServiceLog.Info("Ads: initializing LevelPlay (personalized " + (personalizedConsent ? "on" : "off") +
                            ", test suite " + (_testSuite ? "on" : "off") + ").");
            StartInitialize();
        }

        /// <inheritdoc />
        public override void SetConsent(bool personalizedConsent)
        {
            if (_personalized == personalizedConsent && _initState != InitState.NotStarted)
            {
                return;
            }

            _personalized = personalizedConsent;
            ApplyPrivacy();
        }

        /// <inheritdoc />
        public override void ShowRewarded(Action<bool> onComplete)
        {
            Show(_rewarded, onComplete, null);
        }

        /// <inheritdoc />
        public override void ShowInterstitial(Action onClosed)
        {
            if (!Enabled)
            {
                DeliverClosedLater(onClosed);
                return;
            }

            Show(_interstitial, null, onClosed);
        }

        /// <inheritdoc />
        protected override void OnAdsRemovedChanged(bool removed)
        {
            if (!removed)
            {
                RequestLoad(_interstitial);
            }
        }

        // ---------------------------------------------------------------- privacy

        private void ApplyPrivacy()
        {
            bool personalized = _personalized;
            SafeSdk(() => LevelPlayPrivacySettings.SetGDPRConsent(personalized), "LevelPlayPrivacySettings.SetGDPRConsent");
            SafeSdk(() => LevelPlayPrivacySettings.SetCCPA(!personalized), "LevelPlayPrivacySettings.SetCCPA");
            SafeSdk(() => LevelPlayPrivacySettings.SetCOPPA(false), "LevelPlayPrivacySettings.SetCOPPA");
            ServiceLog.Info("Ads: privacy applied (GDPR consent " + personalized + ", CCPA do-not-sell " +
                            !personalized + ", COPPA false).");
        }

        // ---------------------------------------------------------------- initialization

        private void StartInitialize()
        {
            _initRetryTimer = 0;
            _initState = InitState.Initializing;
            _initAttempts++;
            _initStartedAt = ServiceRunner.ActiveTime;
            _initStallLogged = false;
            try
            {
                // The SDK events are static: remove before adding so retries never stack handlers.
                LevelPlay.OnInitSuccess -= OnSdkInitSuccess;
                LevelPlay.OnInitFailed -= OnSdkInitFailed;
                LevelPlay.OnInitSuccess += OnSdkInitSuccess;
                LevelPlay.OnInitFailed += OnSdkInitFailed;
                LevelPlay.Init(_appKey);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads: LevelPlay.Init threw", e);
                HandleInitFailed("exception");
            }
        }

        /// <summary>SDK callback (any thread).</summary>
        private void OnSdkInitSuccess(LevelPlayConfiguration configuration)
        {
            ServiceRunner.Post(HandleInitSuccess);
        }

        /// <summary>SDK callback (any thread).</summary>
        private void OnSdkInitFailed(LevelPlayInitError error)
        {
            string reason;
            try
            {
                reason = error.ErrorCode + ": " + error.ErrorMessage;
            }
            catch (Exception)
            {
                reason = "unknown error";
            }

            ServiceRunner.Post(() => HandleInitFailed(reason));
        }

        private void HandleInitSuccess()
        {
            if (_initState == InitState.Ready)
            {
                return;
            }

            _initState = InitState.Ready;
            _initAttempts = 0;
            if (_initRetryTimer != 0)
            {
                ServiceRunner.Cancel(_initRetryTimer);
                _initRetryTimer = 0;
            }

            ServiceLog.Info("Ads: LevelPlay initialized.");
            CreateAdObject(_rewarded);
            CreateAdObject(_interstitial);
            _nextReadyRecheck = ServiceRunner.ActiveTime + ReadyRecheckSeconds;
            RequestLoad(_rewarded);
            RequestLoad(_interstitial);
            RaiseReadinessChanged();

            if (_testSuite && !_testSuiteLaunched)
            {
                _testSuiteLaunched = true;
                SafeSdk(() => LevelPlay.LaunchTestSuite(), "LevelPlay.LaunchTestSuite");
            }
        }

        private void HandleInitFailed(string reason)
        {
            if (_initState == InitState.Ready)
            {
                return;
            }

            _initState = InitState.Failed;

            // Keep retrying (slowly) instead of giving up: a game launched offline and left in the background for
            // days would otherwise never get ads back until it is killed.
            int exponent = Math.Max(0, Math.Min(_initAttempts - 1, 8));
            float delay = Math.Min(MinInitRetrySeconds * (1 << exponent), MaxInitRetrySeconds);
            ServiceLog.Warn("Ads: LevelPlay initialization failed (" + reason + "); retrying in " + delay + " s.");
            if (_initRetryTimer == 0)
            {
                _initRetryTimer = ServiceRunner.Delay(delay, StartInitialize);
            }
        }

        // ---------------------------------------------------------------- ad objects & loading

        private void CreateAdObject(Slot slot)
        {
            if (slot.Created || slot.AdUnitId.Length == 0)
            {
                if (slot.AdUnitId.Length == 0)
                {
                    ServiceLog.Info("Ads: no " + slot.Name + " ad unit for this platform; format disabled.");
                }

                return;
            }

            slot.Created = true;
            try
            {
                if (slot.Rewarded)
                {
                    var ad = new LevelPlayRewardedAd(slot.AdUnitId);
                    ad.OnAdLoaded += info => ServiceRunner.Post(() => HandleLoaded(slot));
                    ad.OnAdLoadFailed += error => OnSdkLoadFailed(slot, Describe(error));
                    ad.OnAdDisplayed += info => OnSdkDisplayed(slot, NetworkOf(info));
                    ad.OnAdDisplayFailed += (info, error) => OnSdkDisplayFailed(slot, Describe(error));
                    ad.OnAdClosed += info => ServiceRunner.Post(() => HandleClosed(slot));
                    ad.OnAdRewarded += (info, reward) => OnSdkRewarded(slot, DescribeReward(reward));
                    slot.LoadAd = () => ad.LoadAd();
                    slot.ShowAd = () => ad.ShowAd();
                    slot.IsAdReady = () => ad.IsAdReady();
                }
                else
                {
                    var ad = new LevelPlayInterstitialAd(slot.AdUnitId);
                    ad.OnAdLoaded += info => ServiceRunner.Post(() => HandleLoaded(slot));
                    ad.OnAdLoadFailed += error => OnSdkLoadFailed(slot, Describe(error));
                    ad.OnAdDisplayed += info => OnSdkDisplayed(slot, NetworkOf(info));
                    ad.OnAdDisplayFailed += (info, error) => OnSdkDisplayFailed(slot, Describe(error));
                    ad.OnAdClosed += info => ServiceRunner.Post(() => HandleClosed(slot));
                    slot.LoadAd = () => ad.LoadAd();
                    slot.ShowAd = () => ad.ShowAd();
                    slot.IsAdReady = () => ad.IsAdReady();
                }

                ServiceLog.Info("Ads: " + slot.Name + " ad object created (" + slot.AdUnitId + ").");
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads: creating the " + slot.Name + " ad object failed", e);
                slot.LoadAd = null;
                slot.ShowAd = null;
                slot.IsAdReady = null;
            }
        }

        private void OnSdkLoadFailed(Slot slot, string reason)
        {
            ServiceRunner.Post(() => HandleLoadFailed(slot, reason));
        }

        private void OnSdkDisplayed(Slot slot, string network)
        {
            ServiceRunner.Post(() => HandleDisplayed(slot, network));
        }

        private void OnSdkDisplayFailed(Slot slot, string reason)
        {
            ServiceRunner.Post(() => HandleDisplayFailed(slot, reason));
        }

        private void OnSdkRewarded(Slot slot, string reward)
        {
            ServiceRunner.Post(() => HandleRewarded(slot, reward));
        }

        private void RequestLoad(Slot slot)
        {
            if (_initState != InitState.Ready || slot.LoadAd == null || slot.RetryTimer != 0 ||
                slot.State == SlotState.Loading)
            {
                return;
            }

            if (_active != null && _active.Slot == slot)
            {
                return; // reloaded once the current show finishes
            }

            if (!slot.Rewarded && !Enabled)
            {
                return; // no point preloading interstitials once Remove Ads is owned
            }

            if (IsSdkReady(slot))
            {
                if (slot.State != SlotState.Loaded)
                {
                    slot.State = SlotState.Loaded;
                    RaiseReadinessChanged();
                }

                return;
            }

            bool wasLoaded = slot.State == SlotState.Loaded;
            slot.State = SlotState.Loading;
            slot.LoadStartedAt = ServiceRunner.ActiveTime;
            if (wasLoaded)
            {
                RaiseReadinessChanged();
            }

            try
            {
                slot.LoadAd();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads: " + slot.Name + " LoadAd threw", e);
                HandleLoadFailed(slot, "exception");
            }
        }

        private void HandleLoaded(Slot slot)
        {
            // Accepted in any state: there is one ad object per format, so even an answer to a load we already
            // gave up on (timeout) means an ad is ready now.
            if (slot.RetryTimer != 0)
            {
                ServiceRunner.Cancel(slot.RetryTimer);
                slot.RetryTimer = 0;
            }

            slot.Failures = 0;
            if (slot.State == SlotState.Loaded)
            {
                return;
            }

            slot.State = SlotState.Loaded;
            ServiceLog.Info("Ads: " + slot.Name + " loaded.");
            RaiseReadinessChanged();
        }

        private void HandleLoadFailed(Slot slot, string reason)
        {
            if (slot.State != SlotState.Loading)
            {
                return; // stale answer to a load that already timed out
            }

            slot.State = SlotState.Idle;
            slot.Failures++;
            int exponent = Math.Max(0, Math.Min(slot.Failures - 1, 8));
            float delay = Math.Min(MinBackoffSeconds * (1 << exponent), MaxBackoffSeconds);
            ServiceLog.Info("Ads: " + slot.Name + " failed to load (" + reason + "); retry in " + delay + " s.");
            if (slot.RetryTimer == 0)
            {
                slot.RetryTimer = ServiceRunner.Delay(delay, () =>
                {
                    slot.RetryTimer = 0;
                    RequestLoad(slot);
                });
            }
        }

        private static bool IsSdkReady(Slot slot)
        {
            if (slot.IsAdReady == null)
            {
                return false;
            }

            try
            {
                return slot.IsAdReady();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads: " + slot.Name + " IsAdReady threw", e);
                return false;
            }
        }

        // ---------------------------------------------------------------- showing

        private void Show(Slot slot, Action<bool> onRewarded, Action onClosed)
        {
            if (_active != null || IsShowing)
            {
                ServiceLog.Warn("Ads: an ad is already showing; request ignored.");
                Deliver(slot, onRewarded, onClosed);
                return;
            }

            if (_initState != InitState.Ready || slot.State != SlotState.Loaded || slot.ShowAd == null ||
                !IsSdkReady(slot))
            {
                ServiceLog.Info("Ads: " + slot.Name + " not ready.");
                Deliver(slot, onRewarded, onClosed);
                if (slot.State == SlotState.Loaded)
                {
                    slot.State = SlotState.Idle; // expired
                    RaiseReadinessChanged();
                }

                RequestLoad(slot);
                return;
            }

            var request = new ShowRequest(slot, onRewarded, onClosed, ServiceRunner.ActiveTime);
            _active = request;
            slot.State = SlotState.Idle; // a loaded ad can be shown once
            BeginShow();

            try
            {
                slot.ShowAd();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads: " + slot.Name + " ShowAd threw", e);
                ServiceRunner.Post(() => Finish(request, false, "exception"));
            }
        }

        private static void Deliver(Slot slot, Action<bool> onRewarded, Action onClosed)
        {
            if (slot.Rewarded)
            {
                DeliverRewardedLater(onRewarded, false);
            }
            else
            {
                DeliverClosedLater(onClosed);
            }
        }

        private ShowRequest ActiveFor(Slot slot)
        {
            ShowRequest request = _active;
            return request != null && request.Slot == slot && !request.Done ? request : null;
        }

        private void HandleDisplayed(Slot slot, string network)
        {
            ShowRequest request = ActiveFor(slot);
            if (request == null || request.Started)
            {
                return;
            }

            request.Started = true;
            request.StartedAt = ServiceRunner.ActiveTime;
            ServiceLog.Info("Ads: " + slot.Name + " displayed (" + network + ").");
        }

        private void HandleDisplayFailed(Slot slot, string reason)
        {
            ShowRequest request = ActiveFor(slot);
            if (request == null)
            {
                ServiceLog.Info("Ads: stray " + slot.Name + " display failure (" + reason + ").");
                RequestLoad(slot);
                return;
            }

            Finish(request, request.Rewarded, "display failed: " + reason);
        }

        private void HandleRewarded(Slot slot, string reward)
        {
            ShowRequest request = ActiveFor(slot);
            if (request == null)
            {
                ServiceLog.Warn("Ads: reward (" + reward + ") arrived after its request completed; ignored.");
                return;
            }

            request.Rewarded = true;
            ServiceLog.Info("Ads: reward received (" + reward + ").");
            if (request.Closed)
            {
                Finish(request, true, "reward arrived after close");
            }
        }

        private void HandleClosed(Slot slot)
        {
            ShowRequest request = ActiveFor(slot);
            if (request == null)
            {
                RequestLoad(slot);
                return;
            }

            if (request.Closed)
            {
                return;
            }

            request.Closed = true;
            EndShow(); // the ad is gone; the callback follows once the outcome is known

            if (!slot.Rewarded || request.Rewarded)
            {
                Finish(request, true, "closed");
                return;
            }

            // Some networks deliver OnAdRewarded after OnAdClosed: wait briefly before reporting "no reward".
            request.GraceTimer = ServiceRunner.Delay(RewardGraceSeconds, () =>
            {
                request.GraceTimer = 0;
                Finish(request, false, "closed without reward");
            }, true);
        }

        private void Finish(ShowRequest request, bool completed, string reason)
        {
            if (request.Done)
            {
                return;
            }

            request.Done = true;
            if (request.GraceTimer != 0)
            {
                ServiceRunner.Cancel(request.GraceTimer);
                request.GraceTimer = 0;
            }

            if (_active == request)
            {
                _active = null;
            }

            bool rewarded = request.Slot.Rewarded;
            ServiceLog.Info("Ads: " + request.Slot.Name + " finished (" + reason + ")" +
                            (rewarded ? (completed ? ", reward granted." : ", no reward.") : "."));

            EndShow();
            RequestLoad(request.Slot);
            RaiseReadinessChanged(); // the other format may be showable again now that no request is active

            if (rewarded)
            {
                InvokeRewarded(request.OnRewarded, completed);
            }
            else
            {
                InvokeClosed(request.OnClosed);
            }
        }

        // ---------------------------------------------------------------- watchdogs

        private void OnTick()
        {
            double now = ServiceRunner.ActiveTime;

            ShowRequest request = _active;
            if (request != null && !request.Done && !request.Closed)
            {
                if (!request.Started && now - request.RequestedAt > StartTimeoutSeconds)
                {
                    Finish(request, request.Rewarded, "timed out waiting for the ad to appear");
                }
                else if (request.Started && now - request.StartedAt > PlaybackTimeoutSeconds)
                {
                    Finish(request, request.Rewarded, "timed out waiting for the ad to close");
                }
                else if (request.FocusRegainedAt >= 0.0 && now - request.FocusRegainedAt > FocusGraceSeconds)
                {
                    Finish(request, request.Rewarded, "app resumed without a close callback");
                }
            }

            if (_initState == InitState.Initializing && !_initStallLogged &&
                now - _initStartedAt > InitStallWarnSeconds)
            {
                _initStallLogged = true;
                ServiceLog.Warn("Ads: no LevelPlay init callback after " + InitStallWarnSeconds +
                                " s; check the app key, the network and the LevelPlay dashboard.");
            }

            if (_initState != InitState.Ready)
            {
                return;
            }

            CheckLoadTimeout(_rewarded, now);
            CheckLoadTimeout(_interstitial, now);

            if (now >= _nextReadyRecheck)
            {
                _nextReadyRecheck = now + ReadyRecheckSeconds;
                RecheckLoaded(_rewarded);
                RecheckLoaded(_interstitial);
            }
        }

        private void CheckLoadTimeout(Slot slot, double now)
        {
            if (slot.State == SlotState.Loading && now - slot.LoadStartedAt > LoadTimeoutSeconds)
            {
                HandleLoadFailed(slot, "no answer within " + LoadTimeoutSeconds + " s");
            }
        }

        private void RecheckLoaded(Slot slot)
        {
            if (slot.State != SlotState.Loaded || _active != null || IsSdkReady(slot))
            {
                return;
            }

            ServiceLog.Info("Ads: " + slot.Name + " is no longer ready (expired); reloading.");
            slot.State = SlotState.Idle;
            RaiseReadinessChanged();
            RequestLoad(slot);
        }

        private void OnFocusChanged(bool hasFocus)
        {
            ShowRequest request = _active;
            if (!FocusSignalsAdClosed || request == null || request.Done || request.Closed)
            {
                return;
            }

            if (!hasFocus)
            {
                request.LostFocus = true;
                request.FocusRegainedAt = -1.0;
            }
            else if (request.LostFocus)
            {
                // The ad activity closed. Its close callback normally arrives within a frame or two; if it never
                // does, the grace period completes the request.
                request.FocusRegainedAt = ServiceRunner.ActiveTime;
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void SafeSdk(Action call, string what)
        {
            try
            {
                call();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Ads: " + what + " threw", e);
            }
        }

        private static string Describe(LevelPlayAdError error)
        {
            try
            {
                return error == null ? "unknown error" : error.ErrorCode + ": " + error.ErrorMessage;
            }
            catch (Exception)
            {
                return "unknown error";
            }
        }

        private static string NetworkOf(LevelPlayAdInfo info)
        {
            try
            {
                return info == null ? "unknown network" : "network " + info.AdNetwork;
            }
            catch (Exception)
            {
                return "unknown network";
            }
        }

        private static string DescribeReward(LevelPlayReward reward)
        {
            try
            {
                return reward.Amount + " " + reward.Name;
            }
            catch (Exception)
            {
                return "reward";
            }
        }

        // ---------------------------------------------------------------- nested types

        private sealed class Slot
        {
            public readonly string AdUnitId;
            public readonly bool Rewarded;
            public readonly string Name;
            public bool Created;
            public Action LoadAd;
            public Action ShowAd;
            public Func<bool> IsAdReady;
            public SlotState State;
            public int Failures;
            public int RetryTimer;
            public double LoadStartedAt;

            public Slot(string adUnitId, bool rewarded)
            {
                AdUnitId = adUnitId.Trim();
                Rewarded = rewarded;
                Name = rewarded ? "rewarded" : "interstitial";
            }
        }

        private sealed class ShowRequest
        {
            public readonly Slot Slot;
            public readonly Action<bool> OnRewarded;
            public readonly Action OnClosed;
            public readonly double RequestedAt;
            public bool Started;
            public double StartedAt;
            public bool Rewarded;
            public bool Closed;
            public int GraceTimer;
            public bool LostFocus;
            public double FocusRegainedAt = -1.0;
            public bool Done;

            public ShowRequest(Slot slot, Action<bool> onRewarded, Action onClosed, double requestedAt)
            {
                Slot = slot;
                OnRewarded = onRewarded;
                OnClosed = onClosed;
                RequestedAt = requestedAt;
            }
        }
    }
}
#endif
