using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using Unity.Advertisement.IosSupport;
#endif

namespace BeamRunner.Services
{
    /// <summary>
    /// Apple App Tracking Transparency (ATT) through <c>com.unity.ads.ios-support</c> 1.2.0
    /// (<c>Unity.Advertisement.IosSupport.ATTrackingStatusBinding</c>). Only active on an iOS device; everywhere
    /// else it is "not applicable" and never blocks personalised ads.
    /// <list type="bullet">
    /// <item><see cref="Request"/> shows the system prompt at most once per session and only while the status is
    /// "not determined" (iOS itself never shows it twice), then calls back on the main thread — never synchronously
    /// — with <see cref="AllowsPersonalizedAds"/>. Concurrent requests share one prompt.</item>
    /// <item>The prompt needs <c>NSUserTrackingUsageDescription</c> in Info.plist (added by the
    /// <c>FieldIosTrackingPlist</c> post-build step); without it the package answers "not determined" at once.</item>
    /// <item>If no answer arrives within 60 s of active app time the waiters are released with the current
    /// status; the late answer then raises <see cref="StatusChanged"/>. So does a status changed in the iOS
    /// Settings app, detected when the game regains focus.</item>
    /// </list>
    /// Wired through <see cref="ServiceHub.Initialize"/>: asked after the in-game consent answer and before the ad
    /// SDK starts; personalised ads require the player's consent AND an "authorized" status.
    /// </summary>
    public static class TrackingConsent
    {
        /// <summary>ATT authorization states (plus <see cref="NotApplicable"/> off iOS devices).</summary>
        public enum Status
        {
            /// <summary>Not an iOS device: ATT does not exist, nothing restricts personalised ads.</summary>
            NotApplicable,

            /// <summary>The player has not answered yet (or the prompt is unavailable).</summary>
            NotDetermined,

            /// <summary>Tracking is restricted on the device (parental controls, MDM).</summary>
            Restricted,

            /// <summary>The player declined tracking.</summary>
            Denied,

            /// <summary>The player allowed tracking.</summary>
            Authorized,
        }

        private const float AnswerTimeoutSeconds = 60f;

        private static readonly List<Action<bool>> s_waiters = new List<Action<bool>>();

        private static Action s_statusChanged;
        private static bool s_prompted;
        private static bool s_pending;
        private static int s_timeoutTimer;
        private static bool s_focusHooked;
        private static Status s_lastKnown = Status.NotDetermined;

        /// <summary>
        /// Raised on the main thread when the status changes outside a pending <see cref="Request"/> — a late
        /// answer after the time-out, or a change made in the iOS Settings app. Re-apply ad consent then.
        /// </summary>
        public static event Action StatusChanged
        {
            add
            {
                s_statusChanged += value;
                HookFocus();
            }
            remove
            {
                s_statusChanged -= value;
            }
        }

        /// <summary>True on an iOS device (the only place ATT exists).</summary>
        public static bool IsApplicable
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>The current authorization status (read from iOS each time).</summary>
        public static Status CurrentStatus => ReadStatus();

        /// <summary>True when ATT does not restrict personalised ads: not applicable, or authorized.</summary>
        public static bool AllowsPersonalizedAds => !IsApplicable || ReadStatus() == Status.Authorized;

        /// <summary>True while the system prompt is on screen (or awaiting its answer).</summary>
        public static bool IsRequestPending => s_pending;

        /// <summary>True when <see cref="Request"/> would show the system prompt.</summary>
        public static bool CanPrompt => IsApplicable && !s_prompted && ReadStatus() == Status.NotDetermined;

        /// <summary>
        /// Shows the ATT prompt if it can still be shown, then calls <paramref name="onDone"/> with
        /// <see cref="AllowsPersonalizedAds"/>. Main thread only. The callback always runs exactly once, on the main
        /// thread, never synchronously. Never throws.
        /// </summary>
        public static void Request(Action<bool> onDone)
        {
            if (onDone != null)
            {
                s_waiters.Add(onDone);
            }

            if (s_pending)
            {
                return; // joins the prompt already on screen
            }

            if (!CanPrompt)
            {
                ServiceRunner.Post(() =>
                {
                    if (!s_pending)
                    {
                        FlushWaiters();
                    }
                });
                return;
            }

            s_pending = true;
            s_prompted = true;
            HookFocus();
            s_timeoutTimer = ServiceRunner.Delay(AnswerTimeoutSeconds, OnTimeout, true);
            ServiceLog.Info("ATT: requesting tracking authorization.");
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                ATTrackingStatusBinding.RequestAuthorizationTracking(OnNativeAnswer);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("ATT: RequestAuthorizationTracking threw", e);
                ServiceRunner.Post(() => HandleAnswer(-1));
            }
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>Native completion (arbitrary thread): marshal to the main thread.</summary>
        private static void OnNativeAnswer(int status)
        {
            ServiceRunner.Post(() => HandleAnswer(status));
        }
#endif

        private static void HandleAnswer(int rawStatus)
        {
            bool late = !s_pending; // the time-out already released the waiters
            s_pending = false;
            if (s_timeoutTimer != 0)
            {
                ServiceRunner.Cancel(s_timeoutTimer);
                s_timeoutTimer = 0;
            }

            s_lastKnown = ReadStatus();
            ServiceLog.Info("ATT: answered " + s_lastKnown + " (raw " + rawStatus + ").");
            FlushWaiters();
            if (late)
            {
                RaiseStatusChanged();
            }
        }

        private static void OnTimeout()
        {
            s_timeoutTimer = 0;
            if (!s_pending)
            {
                return;
            }

            s_pending = false;
            s_lastKnown = ReadStatus();
            ServiceLog.Warn("ATT: no answer after " + AnswerTimeoutSeconds + " s; continuing with " + s_lastKnown + ".");
            FlushWaiters();
        }

        private static void FlushWaiters()
        {
            if (s_waiters.Count == 0)
            {
                return;
            }

            bool allowed = AllowsPersonalizedAds;
            Action<bool>[] waiters = s_waiters.ToArray();
            s_waiters.Clear();
            for (int i = 0; i < waiters.Length; i++)
            {
                try
                {
                    waiters[i](allowed);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("ATT callback failed", e);
                }
            }
        }

        private static void HookFocus()
        {
            if (s_focusHooked || !IsApplicable)
            {
                return;
            }

            s_focusHooked = true;
            s_lastKnown = ReadStatus();
            ServiceRunner.FocusChanged += OnFocusChanged;
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus || s_pending)
            {
                return;
            }

            Status now = ReadStatus();
            if (now == s_lastKnown)
            {
                return;
            }

            ServiceLog.Info("ATT: status changed from " + s_lastKnown + " to " + now + ".");
            s_lastKnown = now;
            RaiseStatusChanged();
        }

        private static void RaiseStatusChanged()
        {
            Action handler = s_statusChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("ATT StatusChanged handler failed", e);
            }
        }

        private static Status ReadStatus()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                switch (ATTrackingStatusBinding.GetAuthorizationTrackingStatus())
                {
                    case ATTrackingStatusBinding.AuthorizationTrackingStatus.AUTHORIZED:
                        return Status.Authorized;
                    case ATTrackingStatusBinding.AuthorizationTrackingStatus.DENIED:
                        return Status.Denied;
                    case ATTrackingStatusBinding.AuthorizationTrackingStatus.RESTRICTED:
                        return Status.Restricted;
                    default:
                        return Status.NotDetermined;
                }
            }
            catch (Exception e)
            {
                ServiceLog.Exception("ATT: GetAuthorizationTrackingStatus threw", e);
                return Status.NotDetermined;
            }
#else
            return Status.NotApplicable;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_waiters.Clear();
            s_statusChanged = null;
            s_prompted = false;
            s_pending = false;
            s_timeoutTimer = 0;
            s_focusHooked = false;
            s_lastKnown = Status.NotDetermined;
        }
    }
}
