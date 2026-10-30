using System;
using BeamRunner.Core.Meta;
using BeamRunner.Services;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// Reminders (local notifications), the rating prompt and crash-report consent. Device-level state
    /// (opt-ins, review bookkeeping) lives in <see cref="DevicePrefs"/>, not in the profile, so it never travels with a
    /// cloud save to another device.
    /// <list type="bullet">
    /// <item><b>Reminders</b>: offered once, with an in-game explanation, after the first daily reward was claimed
    /// (<see cref="ReminderOfferPending"/> → the UI shows the offer → <see cref="AnswerReminderOffer"/>, which asks for
    /// the OS permission only after "REMIND ME"). Settings ▸ Reminders toggles it later. When the app goes to the
    /// background (or quits) the single reminder is (re)planned by <see cref="ReminderPlanner"/>; coming back cancels it
    /// and clears the tray.</item>
    /// <item><b>Rating prompt</b>: every third launch (<see cref="ReviewPromptPolicy"/>) the menu, once idle, sees
    /// <see cref="ReviewPromptPending"/> and calls <see cref="BeginReviewPrompt"/>: on iOS that requests Apple's review
    /// sheet; elsewhere the UI shows the game's own dialog and reports the answer to <see cref="AnswerReviewPrompt"/>
    /// (<see cref="ReviewPromptStyle"/>).</item>
    /// <item><b>Crash reports</b>: the first-launch privacy dialog's switch (<see cref="SetPendingCrashReportsChoice"/>,
    /// committed with the dialog's answer) and Settings ▸ Privacy (<see cref="SetCrashReportsEnabled"/>).</item>
    /// </list>
    /// </summary>
    public sealed partial class GameController
    {
        private const string RemindersKey = "BeamRunner.Reminders.Enabled";          // 0 = off, 1 = on (unset = off)
        private const string RemindersOfferedKey = "BeamRunner.Reminders.Offered";   // 1 once the offer was shown
        private const string ReviewLaunchesKey = "BeamRunner.Review.Launches";
        private const string ReviewOptedOutKey = "BeamRunner.Review.OptedOut";   // 1 = never ask again
        private const string ReviewLastKey = "BeamRunner.Review.LastUtcTicks";
        private const string ReviewCountKey = "BeamRunner.Review.Count";

        private bool _engagementHooked;
        private ProfileService _engagementProfile;
        private bool _dailyClaimedThisSession;
        private bool _reminderOfferShown;
        private bool _crashReportsDialogChoice;
        private bool _crashReportsDialogDefaultKnown;
        private bool _crashReportsSwitchShown;
        private bool _crashedLastSession;
        private bool _reviewDueThisLaunch;
        private bool _reviewPromptStarted;

        // ------------------------------------------------------------------ reminders

        /// <summary>True on devices that can show local notifications (Android/iOS; not the Editor).</summary>
        public bool RemindersSupported
        {
            get { return LocalReminders.IsSupported; }
        }

        /// <summary>The player's Reminders setting AND the OS permission (what Settings shows).</summary>
        public bool RemindersEnabled
        {
            get
            {
                return RemindersSupported && DevicePrefs.GetInt(RemindersKey, 0) == 1 &&
                       LocalReminders.CurrentPermission == LocalReminders.Permission.Granted;
            }
        }

        /// <summary>
        /// True when the menu should show the one-time reminder offer: a daily reward was claimed this session, the offer
        /// was never shown, reminders are supported and the OS would still let us ask (or no permission is needed).
        /// </summary>
        public bool ReminderOfferPending
        {
            get
            {
                HookEngagementProfile();
                // Not while another system prompt is up (iOS tracking consent right after the first-launch dialog).
                if (!_dailyClaimedThisSession || _reminderOfferShown || !RemindersSupported ||
                    LocalReminders.IsRequestingPermission || TrackingConsent.IsRequestPending ||
                    DevicePrefs.GetInt(RemindersOfferedKey, 0) == 1)
                {
                    return false;
                }

                LocalReminders.Permission permission = LocalReminders.CurrentPermission;
                return permission == LocalReminders.Permission.NotDetermined ||
                       permission == LocalReminders.Permission.Granted;
            }
        }

        /// <summary>
        /// The player's answer to the reminder offer. Yes → the OS permission prompt (if any), then reminders on when
        /// granted. No → reminders stay off; the offer is never repeated (Settings can turn them on). <paramref name="onDone"/>
        /// gets the resulting <see cref="RemindersEnabled"/> on the main thread.
        /// </summary>
        public void AnswerReminderOffer(bool accept, Action<bool> onDone = null)
        {
            _reminderOfferShown = true;
            DevicePrefs.SetInt(RemindersOfferedKey, 1);
            if (!accept)
            {
                DevicePrefs.SetInt(RemindersKey, 0);
                onDone?.Invoke(false);
                return;
            }

            EnableRemindersWithPermission(onDone);
        }

        /// <summary>
        /// Settings ▸ Reminders. Turning on asks for the OS permission when needed; if the OS no longer shows a prompt
        /// (denied before) the callback gets false and the UI can offer <see cref="OpenNotificationSettings"/>.
        /// </summary>
        public void SetRemindersEnabled(bool on, Action<bool> onDone = null)
        {
            if (!on)
            {
                DevicePrefs.SetInt(RemindersKey, 0);
                LocalReminders.CancelAll();
                onDone?.Invoke(false);
                return;
            }

            DevicePrefs.SetInt(RemindersOfferedKey, 1); // the player chose it: never show the offer afterwards
            _reminderOfferShown = true;
            EnableRemindersWithPermission(onDone);
        }

        /// <summary>True when the OS permission was denied (turning Reminders on must go through the system settings).</summary>
        public bool NotificationsBlocked
        {
            get { return RemindersSupported && LocalReminders.CurrentPermission == LocalReminders.Permission.Denied; }
        }

        /// <summary>Opens the system notification settings for the game.</summary>
        public void OpenNotificationSettings()
        {
            LocalReminders.OpenSystemSettings();
        }

        private void EnableRemindersWithPermission(Action<bool> onDone)
        {
            if (LocalReminders.IsRequestingPermission)
            {
                // A prompt is already up (offer and Settings racing): its answer decides; don't record a "no" here.
                onDone?.Invoke(false);
                return;
            }

            LocalReminders.RequestPermission(granted =>
            {
                DevicePrefs.SetInt(RemindersKey, granted ? 1 : 0);
                if (!granted)
                {
                    LocalReminders.CancelAll();
                }

                try
                {
                    onDone?.Invoke(granted);
                }
                catch (Exception e)
                {
                    FieldLog.Exception("Reminder permission callback", e);
                }
            });
        }

        /// <summary>Plans and schedules the single reminder (app going to the background or quitting).</summary>
        private void ScheduleReminder()
        {
            if (!RemindersSupported || Profile == null)
            {
                return;
            }

            try
            {
                if (DevicePrefs.GetInt(RemindersKey, 0) != 1)
                {
                    return;
                }

                ReminderContext context = ReminderContext.Create(DateTime.Now, Profile.Profile.Daily);
                AugmentReminderContext(ref context);
                LocalReminders.Schedule(ReminderPlanner.Plan(context));
            }
            catch (Exception e)
            {
                FieldLog.Exception("Scheduling the reminder", e);
            }
        }

        /// <summary>
        /// Extension point for the reminder: Daily Run streak at risk (read-only <see cref="ProfileService"/> state). Fills
        /// the Daily Run fields of <paramref name="context"/>; the planner prefers a Daily Run reminder when that streak
        /// would be lost before the daily-reward reminder.
        /// </summary>
        private void AugmentReminderContext(ref ReminderContext context)
        {
            try
            {
                if (!Profile.DailyRunUnlocked)
                {
                    return;
                }

                bool atRisk = Profile.DailyRunStreakAtRisk;
                context.DailyRunPlayedToday = Profile.DailyRunPlayedToday;
                context.DailyRunStreak = atRisk || context.DailyRunPlayedToday ? Profile.DailyRunStreak : 0;
                context.DailyRunResetLocal = DateTime.SpecifyKind(Profile.DailyRunNextResetUtc, DateTimeKind.Utc).ToLocalTime();
            }
            catch (Exception e)
            {
                FieldLog.Exception("Reading the Daily Run state for the reminder", e);
                context.DailyRunStreak = -1;
            }
        }

        private void OnEngagementPauseChanged(bool paused)
        {
            if (paused)
            {
                ScheduleReminder();
            }
            else
            {
                OnEngagementForeground();
            }
        }

        private void OnEngagementForeground()
        {
            // Back in the game: the pending reminder is obsolete and a delivered one has done its job. (Players who were
            // never offered reminders don't touch the notification system at all - no channel, no JNI.)
            if (RemindersSupported && (DevicePrefs.GetInt(RemindersKey, 0) == 1 || DevicePrefs.GetInt(RemindersOfferedKey, 0) == 1))
            {
                LocalReminders.CancelAll();
            }
        }

        private void OnEngagementQuitting()
        {
            // Static event: unsubscribe (Enter Play Mode without a domain reload must not keep a destroyed controller).
            Application.quitting -= OnEngagementQuitting;
            if (this != null)
            {
                ScheduleReminder();
            }
        }

        private void OnEngagementGemsAdded(long amount, string reason)
        {
            if (reason == "daily" && amount > 0)
            {
                _dailyClaimedThisSession = true;
            }
        }

        /// <summary>(Re)subscribes to the profile service (a cloud-save merge may replace it).</summary>
        private void HookEngagementProfile()
        {
            if (ReferenceEquals(_engagementProfile, Profile))
            {
                return;
            }

            if (_engagementProfile != null)
            {
                _engagementProfile.GemsAdded -= OnEngagementGemsAdded;
            }

            _engagementProfile = Profile;
            if (_engagementProfile != null)
            {
                _engagementProfile.GemsAdded += OnEngagementGemsAdded;
            }
        }

        // ------------------------------------------------------------------ rating prompt

        /// <summary>How this platform asks for a rating: Apple's sheet on iOS, the game's own dialog elsewhere.</summary>
        public ReviewPromptStyle ReviewPromptStyle
        {
            get
            {
                return Application.platform == RuntimePlatform.IPhonePlayer
                    ? ReviewPromptStyle.SystemSheet
                    : ReviewPromptStyle.GameDialog;
            }
        }

        /// <summary>
        /// True while this launch's rating prompt (every third launch) is still to be shown and the game is on the menu.
        /// The UI checks it once the menu is idle and then calls <see cref="BeginReviewPrompt"/>.
        /// </summary>
        public bool ReviewPromptPending
        {
            get
            {
                if (!_reviewDueThisLaunch || _reviewPromptStarted || State != GameState.Menu || (Ads != null && Ads.IsShowing))
                {
                    return false;
                }

                return ReviewPromptStyle == ReviewPromptStyle.GameDialog || (StoreReview.IsSupported && !StoreReview.IsBusy);
            }
        }

        /// <summary>
        /// Shows this launch's prompt (once). <see cref="ReviewPromptStyle.SystemSheet"/>: requests Apple's review sheet.
        /// <see cref="ReviewPromptStyle.GameDialog"/>: only records it; the caller shows the dialog and reports the
        /// answer to <see cref="AnswerReviewPrompt"/>.
        /// </summary>
        public void BeginReviewPrompt()
        {
            if (_reviewPromptStarted)
            {
                return;
            }

            _reviewPromptStarted = true;
            try
            {
                ReviewPromptState state = LoadReviewState();
                ReviewPromptPolicy.MarkRequested(state, DateTime.UtcNow);
                SaveReviewState(state);
                if (Profile != null && Profile.Profile.Flags != null && !Profile.Profile.Flags.RatePrompted)
                {
                    Profile.Profile.Flags.RatePrompted = true; // legacy profile flag: "the game has asked for a rating"
                    Profile.NotifyChanged();
                }

                if (ReviewPromptStyle == ReviewPromptStyle.SystemSheet)
                {
                    StoreReview.Request(ok => FieldLog.Info("Review sheet requested (flow ok=" + ok + ")."),
                        () => this != null && State == GameState.Menu && !(Ads != null && Ads.IsShowing));
                }
            }
            catch (Exception e)
            {
                FieldLog.Exception("Rating prompt", e);
            }
        }

        /// <summary>
        /// The player's answer to the game's rating dialog. <see cref="ReviewAnswer.Rate"/> opens the store's review page;
        /// it and <see cref="ReviewAnswer.Never"/> end the prompts for good. Returns false when the store page could not
        /// be opened (no store link configured): the prompt then comes back at a later launch.
        /// </summary>
        public bool AnswerReviewPrompt(ReviewAnswer answer)
        {
            bool opened = true;
            if (answer == ReviewAnswer.Rate)
            {
                opened = PlatformUtil.OpenWriteReviewPage();
                if (!opened)
                {
                    answer = ReviewAnswer.Later;
                }
            }

            try
            {
                ReviewPromptState state = LoadReviewState();
                ReviewPromptPolicy.Answer(state, answer);
                SaveReviewState(state);
                FieldLog.Info("Rating prompt answered: " + answer + ".");
            }
            catch (Exception e)
            {
                FieldLog.Exception("Rating prompt answer", e);
            }

            return opened;
        }

        private ReviewPromptState LoadReviewState()
        {
            return new ReviewPromptState
            {
                Launches = DevicePrefs.GetInt(ReviewLaunchesKey, 0),
                OptedOut = DevicePrefs.GetInt(ReviewOptedOutKey, 0) == 1,
                LastRequestUtcTicks = DevicePrefs.GetLong(ReviewLastKey, 0),
                Requests = DevicePrefs.GetInt(ReviewCountKey, 0),
            };
        }

        private void SaveReviewState(ReviewPromptState state)
        {
            DevicePrefs.SetInt(ReviewLaunchesKey, state.Launches);
            DevicePrefs.SetInt(ReviewOptedOutKey, state.OptedOut ? 1 : 0);
            DevicePrefs.SetLong(ReviewLastKey, state.LastRequestUtcTicks);
            DevicePrefs.SetInt(ReviewCountKey, state.Requests);
        }

        /// <summary>Boot: counts this launch and decides whether it is a prompt launch.</summary>
        private void RecordReviewLaunch()
        {
            ReviewPromptState state = LoadReviewState();
            _reviewDueThisLaunch = ReviewPromptPolicy.RecordLaunch(state, _crashedLastSession);
            SaveReviewState(state);
            if (_reviewDueThisLaunch)
            {
                FieldLog.Info("Rating prompt due at launch " + state.Launches + ".");
            }
        }

        private void OnEngagementStateChanged(GameState previous, GameState next)
        {
            HookEngagementProfile();
        }

        // ------------------------------------------------------------------ crash reports

        /// <summary>True when crash reporting is configured for this build (Settings shows the switch).</summary>
        public bool CrashReportsAvailable
        {
            get { return CrashReporting.IsConfigured; }
        }

        /// <summary>The player's crash-report choice (false until answered).</summary>
        public bool CrashReportsEnabled
        {
            get { return CrashReporting.StoredChoice == CrashReporting.Choice.On; }
        }

        /// <summary>
        /// The initial position of the first-launch dialog's "Send crash reports" switch (regional default; see
        /// <see cref="PrivacyRegion"/>).
        /// </summary>
        public bool CrashReportsDialogDefault
        {
            get
            {
                if (!_crashReportsDialogDefaultKnown)
                {
                    _crashReportsDialogDefaultKnown = true;
                    _crashReportsDialogChoice = CrashReporting.RegionalDefault;
                }

                return _crashReportsDialogChoice;
            }
        }

        /// <summary>
        /// The first-launch dialog shows its "Send crash reports" switch at <paramref name="on"/> (call when it is built
        /// and whenever it moves). Only a switch the player could see is committed with the dialog's answer.
        /// </summary>
        public void SetPendingCrashReportsChoice(bool on)
        {
            _crashReportsDialogDefaultKnown = true;
            _crashReportsDialogChoice = on;
            _crashReportsSwitchShown = true;
        }

        /// <summary>Settings ▸ Privacy ▸ Send crash reports.</summary>
        public void SetCrashReportsEnabled(bool on)
        {
            CrashReporting.SetChoice(on);
        }

        /// <summary>Called with the first-launch dialog's answer: records the switch's final position.</summary>
        private void CommitDialogCrashReportsChoice()
        {
            if (_crashReportsSwitchShown && CrashReporting.IsConfigured)
            {
                CrashReporting.SetChoice(_crashReportsDialogChoice);
            }
        }

        // ------------------------------------------------------------------ boot

        /// <summary>Boot (from <c>InitializeServices</c>): crash-report safety net, launch count, reminder hooks.</summary>
        private void InitializeEngagement()
        {
            if (_engagementHooked)
            {
                return;
            }

            _engagementHooked = true;
            try
            {
                CrashReporting.Apply();
                _crashedLastSession = CrashReporting.CrashedLastSession;
            }
            catch (Exception e)
            {
                FieldLog.Exception("Crash reporting", e);
            }

            try
            {
                RecordReviewLaunch();
            }
            catch (Exception e)
            {
                FieldLog.Exception("Rating prompt bookkeeping", e);
            }

            HookEngagementProfile();
            StateChanged += OnEngagementStateChanged;
            ServiceRunner.PauseChanged += OnEngagementPauseChanged;
            Application.quitting += OnEngagementQuitting;
            OnEngagementForeground();
        }
    }
}
