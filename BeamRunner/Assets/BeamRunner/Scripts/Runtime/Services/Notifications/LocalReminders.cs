using System;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using Unity.Notifications.Android;
#elif UNITY_IOS && !UNITY_EDITOR
using Unity.Notifications.iOS;
#endif

namespace BeamRunner.Services
{
    /// <summary>
    /// Local reminder notifications through <c>com.unity.mobile.notifications</c> 2.5.0 (Android:
    /// <c>AndroidNotificationCenter</c>, iOS: <c>iOSNotificationCenter</c>). At most one reminder is ever pending: every
    /// <see cref="Schedule"/> replaces it, <see cref="CancelAll"/> (called when the game comes back to the foreground)
    /// removes it and any delivered one from the notification tray. Main thread only, never throws.
    /// <list type="bullet">
    /// <item>Only on Android/iOS devices (<see cref="IsSupported"/>); a no-op in the Editor and elsewhere.</item>
    /// <item>Permission (iOS authorization, Android 13+ <c>POST_NOTIFICATIONS</c>) is only requested by
    /// <see cref="RequestPermission"/>, which the game calls after its own explanation. The package's "request
    /// authorization on app launch" setting is off (<c>ProjectSettings/NotificationsSettings.asset</c>).</item>
    /// <item>Android: channel <see cref="ChannelId"/> (default importance: sound, no heads-up), small icon
    /// <see cref="SmallIconId"/> (a white-on-transparent ship registered in the package settings, generated from
    /// <c>Art/Notification/NotificationSmall.png</c> at build time).</item>
    /// </list>
    /// </summary>
    public static class LocalReminders
    {
        /// <summary>The permission to post notifications, as far as the platform tells.</summary>
        public enum Permission
        {
            /// <summary>Not a device with notification support (Editor, desktop, WebGL).</summary>
            Unsupported,

            /// <summary>Never asked (asking shows the system prompt).</summary>
            NotDetermined,

            /// <summary>Allowed.</summary>
            Granted,

            /// <summary>Denied or blocked in the system settings; asking again may show nothing.</summary>
            Denied,
        }

        /// <summary>Android notification channel of the reminders.</summary>
        public const string ChannelId = "field_reminders";

        /// <summary>Drawable id of the small icon (package settings ▸ Android ▸ Notification Icons).</summary>
        public const string SmallIconId = "field_notification_small";

        private const int ReminderId = 4201;
        private const string ReminderIdentifier = "field.reminder";
        private const float PermissionTimeoutSeconds = 90f;
        private const float PollInterval = 0.2f;

        private static bool s_initialized;
        private static bool s_requesting;

        /// <summary>True on an Android or iOS device.</summary>
        public static bool IsSupported
        {
            get
            {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>True while a permission request is waiting for the player's answer.</summary>
        public static bool IsRequestingPermission => s_requesting;

        /// <summary>Reads the current permission from the OS (cheap; call it again after the app regains focus).</summary>
        public static Permission CurrentPermission
        {
            get
            {
                if (!IsSupported || !EnsureInitialized())
                {
                    return Permission.Unsupported;
                }

                try
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    switch (AndroidNotificationCenter.UserPermissionToPost)
                    {
                        case PermissionStatus.Allowed:
                            return Permission.Granted;
                        case PermissionStatus.NotRequested:
                        case PermissionStatus.RequestPending:
                            return Permission.NotDetermined;
                        default:
                            return Permission.Denied;
                    }
#elif UNITY_IOS && !UNITY_EDITOR
                    switch (iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus)
                    {
                        case AuthorizationStatus.Authorized:
                        case AuthorizationStatus.Provisional:
                        case AuthorizationStatus.Ephemeral:
                            return Permission.Granted;
                        case AuthorizationStatus.NotDetermined:
                            return Permission.NotDetermined;
                        default:
                            return Permission.Denied;
                    }
#else
                    return Permission.Unsupported;
#endif
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Reminders: reading the notification permission failed", e);
                    return Permission.Denied;
                }
            }
        }

        /// <summary>
        /// Prepares the platform side (Android channel). Idempotent; called lazily by every other member. Returns false
        /// when notifications are unavailable.
        /// </summary>
        public static bool EnsureInitialized()
        {
            if (!IsSupported)
            {
                return false;
            }

            if (s_initialized)
            {
                return true;
            }

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!AndroidNotificationCenter.Initialize())
                {
                    return false;
                }

                // Name and description show in the system's notification settings (re-registering updates them, so they
                // follow the game's language from the next launch).
                var channel = new AndroidNotificationChannel(ChannelId, Loc.T("notifications.channel_name"),
                    Loc.T("notifications.channel_description"), Importance.Default)
                {
                    CanShowBadge = false,
                    EnableVibration = true,
                    LockScreenVisibility = LockScreenVisibility.Public,
                };
                AndroidNotificationCenter.RegisterNotificationChannel(channel);
#endif
                s_initialized = true;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reminders: initializing notifications failed", e);
                return false;
            }

            return s_initialized;
        }

        /// <summary>
        /// Asks for permission to post notifications (the system prompt, if the OS still shows one) and calls back on
        /// the main thread — never synchronously — with true when notifications are allowed. Android below 13 needs no
        /// permission (true unless blocked in the settings). Concurrent calls are refused (false).
        /// </summary>
        public static void RequestPermission(Action<bool> onDone)
        {
            if (!IsSupported || !EnsureInitialized())
            {
                Finish(onDone, false);
                return;
            }

            if (s_requesting)
            {
                Finish(onDone, false);
                return;
            }

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var request = new PermissionRequest(); // shows the system dialog on Android 13+ unless decided already
                StartPolling(() => request.Status != PermissionStatus.RequestPending,
                    () => request.Status == PermissionStatus.Allowed, onDone);
#elif UNITY_IOS && !UNITY_EDITOR
                // Alert + sound; no badge, no push registration (local notifications only).
                var request = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Sound, false);
                StartPolling(() => request.IsFinished, () =>
                {
                    bool granted = request.Granted;
                    request.Dispose();
                    return granted;
                }, onDone);
#else
                Finish(onDone, false);
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reminders: requesting the notification permission failed", e);
                s_requesting = false;
                Finish(onDone, false);
            }
        }

        /// <summary>
        /// Replaces the pending reminder with <paramref name="plan"/> (cancels it when there is nothing to schedule).
        /// Does nothing without permission. Returns true when a reminder is now pending.
        /// </summary>
        public static bool Schedule(ReminderPlan plan)
        {
            if (!IsSupported || !EnsureInitialized())
            {
                return false;
            }

            CancelScheduled();
            if (!plan.HasReminder || CurrentPermission != Permission.Granted)
            {
                return false;
            }

            DateTime fire = plan.FireLocal;
            if (fire <= DateTime.Now.AddMinutes(1))
            {
                return false;
            }

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var notification = new AndroidNotification(plan.Title, plan.Body, DateTime.SpecifyKind(fire, DateTimeKind.Local))
                {
                    SmallIcon = SmallIconId,
                    ShouldAutoCancel = true,
                    ShowTimestamp = true,
                    Color = new Color(0f, 0.9f, 1f, 1f),
                    Style = NotificationStyle.BigTextStyle,
                };
                AndroidNotificationCenter.SendNotificationWithExplicitID(notification, ChannelId, ReminderId);
#elif UNITY_IOS && !UNITY_EDITOR
                var notification = new iOSNotification(ReminderIdentifier)
                {
                    Title = plan.Title,
                    Body = plan.Body,
                    ShowInForeground = false,
                    ThreadIdentifier = "reminders",
                    // Local wall clock (not UTC): 19:00 stays 19:00 across DST changes and time-zone moves.
                    Trigger = new iOSNotificationCalendarTrigger
                    {
                        Year = fire.Year,
                        Month = fire.Month,
                        Day = fire.Day,
                        Hour = fire.Hour,
                        Minute = fire.Minute,
                        Second = 0,
                        UtcTime = false,
                        Repeats = false,
                    },
                };
                iOSNotificationCenter.ScheduleNotification(notification);
#endif
                ServiceLog.Info("Reminders: scheduled '" + plan.Kind + "' for " + fire.ToString("yyyy-MM-dd HH:mm") + " local.");
                return true;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reminders: scheduling failed", e);
                return false;
            }
        }

        /// <summary>Cancels the pending reminder and clears delivered ones from the notification tray.</summary>
        public static void CancelAll()
        {
            if (!IsSupported || !EnsureInitialized())
            {
                return;
            }

            CancelScheduled();
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                AndroidNotificationCenter.CancelAllDisplayedNotifications();
#elif UNITY_IOS && !UNITY_EDITOR
                iOSNotificationCenter.RemoveAllDeliveredNotifications();
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reminders: clearing delivered notifications failed", e);
            }
        }

        /// <summary>Opens the system notification settings of the game (when the permission was denied).</summary>
        public static void OpenSystemSettings()
        {
            if (!IsSupported || !EnsureInitialized())
            {
                return;
            }

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                AndroidNotificationCenter.OpenNotificationSettings();
#elif UNITY_IOS && !UNITY_EDITOR
                iOSNotificationCenter.OpenNotificationSettings();
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reminders: opening the notification settings failed", e);
            }
        }

        private static void CancelScheduled()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                AndroidNotificationCenter.CancelAllScheduledNotifications();
#elif UNITY_IOS && !UNITY_EDITOR
                iOSNotificationCenter.RemoveAllScheduledNotifications();
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reminders: cancelling the pending reminder failed", e);
            }
        }

        /// <summary>
        /// Polls <paramref name="isDone"/> on the main thread (the package completes both requests from native callbacks
        /// on other threads and only exposes polled state) until it is true or <see cref="PermissionTimeoutSeconds"/> of
        /// active app time passed, then calls back once.
        /// </summary>
        private static void StartPolling(Func<bool> isDone, Func<bool> result, Action<bool> onDone)
        {
            s_requesting = true;
            double deadline = ServiceRunner.ActiveTime + PermissionTimeoutSeconds;
            Action poll = null;
            poll = () =>
            {
                bool done;
                try
                {
                    done = isDone();
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Reminders: permission request failed", e);
                    s_requesting = false;
                    Finish(onDone, false);
                    return;
                }

                if (!done && ServiceRunner.ActiveTime < deadline)
                {
                    ServiceRunner.Delay(PollInterval, poll, true);
                    return;
                }

                s_requesting = false;
                bool granted = false;
                try
                {
                    granted = done ? result() : CurrentPermission == Permission.Granted;
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Reminders: reading the permission answer failed", e);
                }

                ServiceLog.Info("Reminders: permission " + (done ? "answered" : "timed out") + ", granted=" + granted + ".");
                Finish(onDone, granted);
            };
            ServiceRunner.Delay(PollInterval, poll, true);
        }

        private static void Finish(Action<bool> onDone, bool value)
        {
            if (onDone == null)
            {
                return;
            }

            ServiceRunner.Post(() => onDone(value));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_initialized = false;
            s_requesting = false;
        }
    }
}
