using System.Collections;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Settings overlay (from the menu, and from Pause during a run). Every change writes to
    /// <see cref="ProfileService.Settings"/> and calls <see cref="GameController.ApplySettings"/> immediately (sliders
    /// apply live). Mid-run it hides what makes no sense there (RESET PROGRESS).
    /// </summary>
    public sealed partial class SettingsScreen : MetaScreen
    {
        private static readonly string[] ControlHintKeys =
        {
            "settings.hint_swipe",
            "settings.hint_tap_sides",
        };

        private ScrollList _list;
        private NeonSlider _music;
        private NeonSlider _sfx;
        private NeonToggle _haptics;
        private SegmentedControl _control;
        private Text _controlHint;
        private NeonSlider _sensitivity;
        private Text _sensitivityValue;

        private SegmentedControl _graphics;
        private NeonSlider _shake;
        private NeonToggle _reduceFlashing;
        private NeonToggle _showFps;
        private NeonToggle _ghost;

        private Card _privacyCard;
        private RectTransform _adsRow;
        private Text _adsCaption;
        private NeonToggle _personalised;
        private RectTransform _crashRow;
        private Text _crashCaption;
        private NeonToggle _crashReports;
        private Card _remindersCard;
        private NeonToggle _reminders;
        private Text _remindersCaption;
        private bool _remindersBusy;
        private NeonButton _rate;
        private Card _purchasesCard;
        private NeonButton _restore;
        private Card _dangerCard;
        private Text _version;

        private bool _binding;
        private bool _restoring;
        private float _nextSfxPreview;

        protected override bool ListensToStore
        {
            get { return true; }
        }

        // One card per step: the build queue spreads the screen over several frames (UIScreen.BuildSteps).
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.SlideUp;
            RectTransform r = Rect;
            MetaUI.Dimmer(r, 0.8f);
            RectTransform column = UIFactory.ContentColumn(r, 1000f, 32f);
            NeonTitle title;
            MetaUI.Header(column, Loc.T("settings.title"), MetaUI.CloseTop, out title);

            _list = UIFactory.ScrollList(column, UITheme.SpaceMd, UIFactory.Padding(32, 32, 24, 96), 40f);
            UIFactory.Stretch(_list.Rect, -32f, MetaUI.HeaderBottom, -32f, 0f);
            RectTransform c = _list.Content;

            BuildLanguage(c); // SettingsScreen.Language.cs
            BuildAudio(c);
            yield return null;
            BuildControls(c);
            yield return null;
            BuildDisplay(c);
            yield return null;
            BuildReminders(c);
            BuildPrivacy(c);
            BuildPurchases(c);
            BuildOnline(c); // SettingsScreen.Online.cs
            yield return null;
            BuildSupport(c);
            BuildCredits(c);
            BuildDanger(c);

            _version = UIFactory.Label(c, string.Empty, UITheme.TextCaption, UITheme.TextMuted, TextAnchor.MiddleCenter, UIFont.Bold);
        }

        // ------------------------------------------------------------------ sections

        private void BuildAudio(RectTransform c)
        {
            Card card = UIFactory.Card(c, Loc.T("settings.audio"), IconId.Music);
            _music = UIFactory.SliderRow(card.Body, Loc.T("settings.music"), 0.8f, v => Change(s => s.MusicVolume = v), IconId.Music);
            _sfx = UIFactory.SliderRow(card.Body, Loc.T("settings.sfx"), 0.9f, v =>
            {
                Change(s => s.SfxVolume = v);
                PreviewSfx();
            }, IconId.SoundOn);
            _haptics = UIFactory.ToggleRow(card.Body, Loc.T("settings.vibration"), true, on =>
            {
                Change(s => s.Haptics = on);
                if (on)
                {
                    Haptic();
                }
            }, IconId.Vibration);
        }

        private void BuildControls(RectTransform c)
        {
            Card card = UIFactory.Card(c, Loc.T("settings.controls"), IconId.Gamepad);
            Caption(card.Body, Loc.T("settings.steering"));
            _control = UIFactory.SegmentedControl(card.Body,
                new[] { Loc.T("settings.swipe"), Loc.T("settings.tap_sides") }, 0, i =>
            {
                Change(s => s.ControlMode = (ControlMode)i);
                UpdateControlHint(i);
            });
            _controlHint = UIFactory.Label(card.Body, Loc.T(ControlHintKeys[0]), UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Body);

            RectTransform sens = UIFactory.SettingRow(card.Body, Loc.T("settings.sensitivity"), IconId.Speed);
            _sensitivityValue = UIFactory.LabelLine(sens, SensitivityText(1f), 40, UITheme.Accent, TextAnchor.MiddleRight, UIFont.Title);
            UIAccentFollower.Attach(_sensitivityValue, AccentSlot.Primary);
            UIFactory.SetLayout(_sensitivityValue, 110f, -1f);
            _sensitivity = UIFactory.Slider(sens, 1f, v =>
            {
                Change(s => s.Sensitivity = Mathf.Clamp(v, 0.4f, 2.5f));
                UpdateSensitivityLabel(v);
            }, 0.4f, 2.5f, false, 340f);

        }

        private void BuildDisplay(RectTransform c)
        {
            Card card = UIFactory.Card(c, Loc.T("settings.display"), IconId.Star);
            Caption(card.Body, Loc.T("settings.graphics"));
            _graphics = UIFactory.SegmentedControl(card.Body,
                new[] { Loc.T("settings.low"), Loc.T("settings.med"), Loc.T("settings.high") }, 2, i => Change(s => s.GraphicsQuality = i));
            Hint(card.Body, Loc.T("settings.graphics_hint"));
            _shake = UIFactory.SliderRow(card.Body, Loc.T("settings.camera_shake"), 1f, v => Change(s => s.CameraShake = v), IconId.Vibration);
            _reduceFlashing = UIFactory.ToggleRow(card.Body, Loc.T("settings.reduce_flashing"), false, on => Change(s => s.ReduceFlashing = on), IconId.Bolt);
            _showFps = UIFactory.ToggleRow(card.Body, Loc.T("settings.show_fps"), false, on =>
            {
                Change(s => s.ShowFps = on);
                UIRoot root = UIRoot.Instance;
                if (root != null)
                {
                    root.ShowFps(on);
                }
            }, IconId.Stats);
            _ghost = UIFactory.ToggleRow(card.Body, Loc.T("settings.ghost"), true, on => Change(s => s.ShowGhost = on), IconId.Calendar);
            Hint(card.Body, Loc.T("settings.ghost_hint"));
        }

        private void BuildPrivacy(RectTransform c)
        {
            _privacyCard = UIFactory.Card(c, Loc.T("settings.privacy"), IconId.Shield);
            _personalised = UIFactory.ToggleRow(_privacyCard.Body, Loc.T("settings.personalised_ads"), false, OnPersonalisedChanged, IconId.Ad);
            _adsRow = (RectTransform)_personalised.transform.parent;
            _adsCaption = Hint(_privacyCard.Body, Loc.T("settings.personalised_hint"));
            _crashReports = UIFactory.ToggleRow(_privacyCard.Body, Loc.T("consent.crash_toggle"), false, OnCrashReportsChanged, IconId.Warning);
            _crashRow = (RectTransform)_crashReports.transform.parent;
            _crashCaption = Hint(_privacyCard.Body, Loc.T("settings.crash_hint"));
            RectTransform links = ButtonRow(_privacyCard.Body);
            UIFactory.Button(links, Loc.T("settings.privacy_policy"), ButtonStyle.Ghost, OnPrivacyPolicy, IconId.Info);
        }

        private void BuildReminders(RectTransform c)
        {
            _remindersCard = UIFactory.Card(c, Loc.T("settings.reminders"), IconId.Calendar);
            _reminders = UIFactory.ToggleRow(_remindersCard.Body, Loc.T("settings.daily_reminders"), false, OnRemindersChanged, IconId.Calendar);
            _remindersCaption = Hint(_remindersCard.Body, Loc.T("settings.reminders_hint"));
        }

        private void BuildPurchases(RectTransform c)
        {
            _purchasesCard = UIFactory.Card(c, Loc.T("settings.purchases"), IconId.Cart);
            Hint(_purchasesCard.Body, Loc.T("settings.purchases_hint"));
            RectTransform row = ButtonRow(_purchasesCard.Body);
            _restore = UIFactory.Button(row, Loc.T("store.restore"), ButtonStyle.Secondary, OnRestore, IconId.Restore);
        }

        private void BuildSupport(RectTransform c)
        {
            Card card = UIFactory.Card(c, Loc.T("settings.support"), IconId.Heart);
            Hint(card.Body, Loc.T("settings.support_hint"));
            RectTransform row = ButtonRow(card.Body);
            UIFactory.Button(row, Loc.T("settings.contact"), ButtonStyle.Secondary, OnSupport, IconId.Info);
            RectTransform rateRow = ButtonRow(card.Body);
            _rate = UIFactory.Button(rateRow, Loc.T("settings.rate"), ButtonStyle.Ghost, OnRate, IconId.Star);
        }

        private void BuildCredits(RectTransform c)
        {
            Card card = UIFactory.Card(c, Loc.T("settings.credits"), IconId.Star);
            CreditLine(card.Body, Loc.T("settings.credit_fonts"));
            CreditLine(card.Body, Loc.T("settings.credit_generated"));
        }

        private void BuildDanger(RectTransform c)
        {
            _dangerCard = UIFactory.Card(c, Loc.T("settings.danger"), IconId.Warning, UITheme.Danger);
            Hint(_dangerCard.Body, Loc.T("settings.danger_hint"));
            RectTransform row = ButtonRow(_dangerCard.Body);
            UIFactory.Button(row, Loc.T("settings.reset"), ButtonStyle.Danger, OnReset, IconId.Warning);
        }

        // ------------------------------------------------------------------ small builders

        private static void Caption(RectTransform parent, string text)
        {
            Text t = UIFactory.Label(parent, text, UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleLeft, UIFont.Title);
            UIFactory.SetLayout(t, -1f, 52f);
        }

        private static Text Hint(RectTransform parent, string text)
        {
            return UIFactory.Label(parent, text, UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);
        }

        private static void CreditLine(RectTransform parent, string text)
        {
            UIFactory.Label(parent, text, UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);
        }

        private static RectTransform ButtonRow(RectTransform parent)
        {
            RectTransform row = UIFactory.CreateRect(parent, "Buttons");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, UIFactory.Padding(0, 0, 8, 8), TextAnchor.MiddleCenter, true);
            h.childForceExpandWidth = false;
            return row;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShow()
        {
            base.OnShow();
            _list.ScrollToTop();
            if (!UITheme.ReducedMotion)
            {
                UIFactory.StaggerIn(_list.Content, 0.06f, 0.04f);
            }
        }

        protected override void Refresh(bool showing)
        {
            PlayerSettings s = Profile.Settings;
            if (s == null)
            {
                return;
            }

            _binding = true;
            try
            {
                _music.SetValueWithoutNotify(s.MusicVolume);
                _sfx.SetValueWithoutNotify(s.SfxVolume);
                _haptics.SetIsOnWithoutNotify(s.Haptics, !showing);
                int mode = Mathf.Clamp(s.Control, 0, ControlHintKeys.Length - 1);
                _control.Select(mode, false, !showing);
                UpdateControlHint(mode);
                _sensitivity.SetValueWithoutNotify(s.Sensitivity);
                UpdateSensitivityLabel(s.Sensitivity);

                _graphics.Select(Mathf.Clamp(s.GraphicsQuality < 0 ? 2 : s.GraphicsQuality, 0, 2), false, !showing);
                _shake.SetValueWithoutNotify(s.CameraShake);
                _reduceFlashing.SetIsOnWithoutNotify(s.ReduceFlashing, !showing);
                _showFps.SetIsOnWithoutNotify(s.ShowFps, !showing);
                _ghost.SetIsOnWithoutNotify(s.ShowGhost, !showing);

                bool ads = Game.AdsAvailable;
                _adsRow.gameObject.SetActive(ads);
                _adsCaption.gameObject.SetActive(ads);
                _personalised.SetIsOnWithoutNotify(Profile.Profile.Flags != null && Profile.Profile.Flags.PersonalizedAds, !showing);

                bool crash = Game.CrashReportsAvailable;
                _crashRow.gameObject.SetActive(crash);
                _crashCaption.gameObject.SetActive(crash);
                _crashReports.SetIsOnWithoutNotify(crash && Game.CrashReportsEnabled, !showing);

                bool reminders = Game.RemindersSupported;
                _remindersCard.gameObject.SetActive(reminders);
                if (reminders && !_remindersBusy)
                {
                    _reminders.SetIsOnWithoutNotify(Game.RemindersEnabled, !showing);
                    _remindersCaption.text = Game.NotificationsBlocked
                        ? Loc.T("settings.reminders_blocked_hint")
                        : Loc.T("settings.reminders_hint");
                }

                _rate.gameObject.SetActive(PlatformUtil.CanOpenStorePage);

                IIapService iap = Game.Iap;
                _purchasesCard.gameObject.SetActive(iap != null && iap.SupportsRestore);
                _restore.SetInteractable(!_restoring && iap != null && iap.Available);
                _restore.SetLabel(_restoring ? Loc.T("store.restoring") : Loc.T("store.restore"));
                _dangerCard.gameObject.SetActive(!InRun());
                RefreshLanguage(); // SettingsScreen.Language.cs
                RefreshOnline(); // SettingsScreen.Online.cs

                string version = PlatformUtil.AppVersion;
                _version.text = "BEAMRUNNER" + (string.IsNullOrEmpty(version) ? string.Empty : "  v" + version); // l10n-ok: brand + version
            }
            finally
            {
                _binding = false;
            }
        }

        // ------------------------------------------------------------------ actions

        private void Change(System.Action<PlayerSettings> apply)
        {
            if (_binding || !HasData || Profile.Settings == null)
            {
                return;
            }

            apply(Profile.Settings);
            Game.ApplySettings();
        }

        private void UpdateControlHint(int mode)
        {
            _controlHint.text = Loc.T(ControlHintKeys[Mathf.Clamp(mode, 0, ControlHintKeys.Length - 1)]);
        }

        private void UpdateSensitivityLabel(float v)
        {
            _sensitivityValue.text = SensitivityText(v);
        }

        private static string SensitivityText(float v)
        {
            return Loc.T("settings.sensitivity_value", ("value", Loc.N(v, 1)));
        }

        private void PreviewSfx()
        {
            if (_binding || Time.unscaledTime < _nextSfxPreview)
            {
                return;
            }

            _nextSfxPreview = Time.unscaledTime + 0.18f;
            Sfx(SfxId.GemPickup);
        }

        private void OnPersonalisedChanged(bool on)
        {
            if (_binding || Game == null)
            {
                return;
            }

            Game.SetConsent(on);
        }

        private void OnCrashReportsChanged(bool on)
        {
            if (_binding || Game == null)
            {
                return;
            }

            Game.SetCrashReportsEnabled(on);
        }

        private void OnRemindersChanged(bool on)
        {
            if (_binding || Game == null)
            {
                return;
            }

            if (_remindersBusy)
            {
                MarkDirty(); // a permission prompt is still open: snap the switch back to the real state
                return;
            }

            if (on && Game.NotificationsBlocked)
            {
                // The OS won't show its prompt again: explain and offer the system settings.
                MarkDirty();
                Dialogs.Confirm(Loc.T("settings.blocked_title"),
                    Loc.T("settings.blocked_body", ("setting", Loc.T("settings.daily_reminders"))), Loc.T("settings.open_settings"),
                    Loc.T("common.cancel"), open =>
                    {
                        if (open && Game != null)
                        {
                            Game.OpenNotificationSettings();
                        }
                    }, false, IconId.Calendar);
                return;
            }

            _remindersBusy = on;
            Game.SetRemindersEnabled(on, enabled =>
            {
                _remindersBusy = false;
                if (this == null)
                {
                    return;
                }

                if (on && !enabled)
                {
                    MetaUI.Toast(Loc.T("toast.reminders_permission"), IconId.Warning);
                }

                MarkDirty();
            });
        }

        private void OnRate()
        {
            if (!PlatformUtil.OpenWriteReviewPage())
            {
                MetaUI.Toast(Loc.T("toast.store_open_failed"), IconId.Warning);
            }
        }

        private void OnPrivacyPolicy()
        {
            if (!PlatformUtil.OpenPrivacyPolicy())
            {
                MetaUI.Toast(Loc.T("toast.privacy_open_failed"), IconId.Warning);
            }
        }

        private void OnSupport()
        {
            if (!PlatformUtil.ComposeSupportEmail())
            {
                MetaUI.Toast(Loc.T("toast.no_email"), IconId.Warning);
            }
        }

        private void OnRestore()
        {
            if (Game == null || _restoring)
            {
                return;
            }

            _restoring = true;
            MarkDirty();
            Game.RestorePurchases(ok =>
            {
                _restoring = false;
                if (this != null)
                {
                    MarkDirty();
                }
            });
        }

        private void OnReset()
        {
            Dialogs.Confirm(Loc.T("settings.reset_title"), Loc.T("settings.reset_body"), Loc.T("settings.reset_confirm"),
                Loc.T("common.cancel"), ok =>
                {
                    if (!ok || Game == null || InRun())
                    {
                        return;
                    }

                    Game.ResetProgress();
                    MetaUI.Toast(Loc.T("toast.progress_reset"), IconId.Restore);
                    MarkDirty();
                }, true, IconId.Warning);
        }

        private bool InRun()
        {
            if (Game == null)
            {
                return false;
            }

            if (Game.Run != null)
            {
                return true;
            }

            GameState st = Game.State;
            return st == GameState.Countdown || st == GameState.Running || st == GameState.Paused || st == GameState.Crashed ||
                   st == GameState.GameOver;
        }
    }
}
