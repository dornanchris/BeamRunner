using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Services;

namespace BeamRunner.Game
{
    /// <summary>
    /// Online services wiring (Google Play Games / Game Center / cloud save): started after the first-launch consent
    /// flow; submits scores and achievements; runs cloud syncs at sign-in, on app pause, after runs (rate-limited),
    /// when another device changed the cloud copy, and from Settings › Sync now. Hooks: <see cref="InitializeOnline"/>
    /// and <see cref="StartOnline"/> (GameController.Services.cs). Nothing here throws into the game.
    /// </summary>
    public sealed partial class GameController
    {
        private const float ExternalChangePollSeconds = 5f;

        private IOnlineService _online;
        private CloudSync _cloudSync;
        private bool _onlineWired;
        private bool _onlineStarted;
        private bool _onlineWasSignedIn;
        private int _achievementsSeen = -1;
        private int _externalChangesSeen = -1;
        private float _externalPollTimer;

        /// <summary>Online services (a null service when nothing is configured; never null after boot).</summary>
        public IOnlineService Online
        {
            get { return _online ?? (_online = new NullOnlineService()); }
        }

        /// <summary>Cloud save coordinator (null before boot).</summary>
        public CloudSync CloudSave
        {
            get { return _cloudSync; }
        }

        /// <summary>Raised when the online sign-in state or the cloud save status changes (Settings / Records refresh).</summary>
        public event Action OnlineChanged;

        /// <summary>Opens the platform leaderboards UI (toasts when that is not possible).</summary>
        public void ShowOnlineLeaderboards()
        {
            if (!SafeOnline(() => Online.ShowLeaderboards()))
            {
                RaiseNotify(Online.IsSignedIn ? Loc.T("online.leaderboards_unavailable") : Loc.T("online.leaderboards_sign_in"));
            }
        }

        /// <summary>Opens the platform achievements UI (toasts when that is not possible).</summary>
        public void ShowOnlineAchievements()
        {
            if (!SafeOnline(() => Online.ShowAchievements()))
            {
                RaiseNotify(Online.IsSignedIn ? Loc.T("online.achievements_unavailable") : Loc.T("online.achievements_sign_in"));
            }
        }

        /// <summary>Interactive sign-in (Settings). <paramref name="onDone"/> gets true when signed in.</summary>
        public void SignInOnline(Action<bool> onDone)
        {
            try
            {
                Online.SignIn(ok =>
                {
                    if (!ok && Online.Platform == OnlinePlatform.GameCenter && !Online.IsSignedIn)
                    {
                        RaiseNotify(Loc.T("online.game_center_settings"));
                    }

                    onDone?.Invoke(ok);
                });
            }
            catch (Exception e)
            {
                FieldLog.Exception("Online sign-in", e);
                onDone?.Invoke(false);
            }
        }

        /// <summary>
        /// Settings › Sync now. <paramref name="mergeAccounts"/>: the player confirmed merging this device's progress
        /// into another account's cloud save (after <see cref="CloudSaveState.AccountConflict"/>).
        /// </summary>
        public void SyncCloudNow(bool mergeAccounts, Action<bool> onDone)
        {
            if (_cloudSync == null || IsRunActive())
            {
                onDone?.Invoke(false);
                return;
            }

            _cloudSync.Request(mergeAccounts ? CloudSyncReason.ManualMergeAccounts : CloudSyncReason.Manual, onDone);
        }

        // ------------------------------------------------------------------ boot hooks

        /// <summary>Wires events; does not start anything (called from InitializeServices). Never throws.</summary>
        private void InitializeOnline()
        {
            if (_onlineWired)
            {
                return;
            }

            _onlineWired = true;
            try
            {
                WireOnline();
            }
            catch (Exception e)
            {
                // Must not keep InitializeServices from subscribing the IAP grant handler.
                FieldLog.Exception("Online wiring", e);
                _online = new NullOnlineService();
            }
        }

        private void WireOnline()
        {
            _online = ServiceHub.Online ?? new NullOnlineService();
            _cloudSync = new CloudSync(Store, _online);
            _cloudSync.Changed += RaiseOnlineChanged;
            _cloudSync.Merged += OnCloudMerged;
            _online.StateChanged += OnOnlineStateChanged;
            if (!_online.IsAvailable)
            {
                return;
            }

            RunFinished += OnRunFinishedOnline;
            Profile.DailyRunCompleted += OnDailyRunCompletedOnline;
            Profile.Changed += OnProfileChangedOnline;
            ServiceRunner.PauseChanged += OnPauseChangedOnline;
            ServiceRunner.Ticked += TickOnline;
        }

        /// <summary>Starts sign-in and the first cloud sync; called once the consent flow is done (StartServices).</summary>
        private void StartOnline()
        {
            if (_onlineStarted || _online == null || !_online.IsAvailable)
            {
                return;
            }

            _onlineStarted = true;
            _achievementsSeen = Profile.Profile.Achievements.Count;
            SafeOnline(() =>
            {
                _online.Start();
                return true;
            });

            // Cloud save that does not need the platform account (iCloud) syncs right away; account-based cloud saves
            // sync when the sign-in completes (OnOnlineStateChanged).
            if (_cloudSync.IsAvailable && _online.CloudSave.IsAvailable)
            {
                _cloudSync.Request(CloudSyncReason.Startup);
            }
        }

        // ------------------------------------------------------------------ events

        private void OnOnlineStateChanged()
        {
            bool signedIn = _online.IsSignedIn;
            if (signedIn && !_onlineWasSignedIn)
            {
                CatchUpOnline();
                if (_cloudSync.IsAvailable && !IsRunActive())
                {
                    _cloudSync.Request(CloudSyncReason.Startup);
                }
            }

            _onlineWasSignedIn = signedIn;
            RaiseOnlineChanged();
        }

        /// <summary>
        /// After a sign-in: submit the bests and re-report every unlocked achievement (quietly). Skipped while this
        /// device's progress is bound to another account's cloud save (an account switch the player has not resolved).
        /// </summary>
        private void CatchUpOnline()
        {
            PlayerProfile p = Profile.Profile;
            _achievementsSeen = p.Achievements.Count;
            if (IsBoundToAnotherAccount(p))
            {
                FieldLog.Info("Online: signed-in account differs from this device's cloud save; no catch-up reports.");
                return;
            }

            SafeOnline(() =>
            {
                _online.SubmitScore(LeaderboardKind.BestScore, p.Stats.BestScore, 0);
                DailyRunState daily = p.DailyRun;
                if (daily != null && daily.Day > 0 && daily.Day == Profile.DailyRunToday && daily.BestScore > 0)
                {
                    _online.SubmitScore(LeaderboardKind.DailyRun, daily.BestScore, daily.Day);
                }

                _online.UnlockAchievements(p.Achievements, true);
                return true;
            });
        }

        private bool IsBoundToAnotherAccount(PlayerProfile p)
        {
            ICloudSave cloud = _online.CloudSave;
            if (cloud == null || p.Sync == null || string.IsNullOrEmpty(p.Sync.CloudId))
            {
                return false;
            }

            string key;
            try
            {
                key = cloud.AccountKey ?? string.Empty;
            }
            catch (Exception)
            {
                return false;
            }

            return key.Length > 0 && key != p.Sync.CloudId;
        }

        private void OnRunFinishedOnline(RunSummary summary)
        {
            if (summary == null || summary.Result == null)
            {
                return;
            }

            long score = summary.Result.Score;
            SafeOnline(() =>
            {
                _online.SubmitScore(LeaderboardKind.BestScore, score, 0);
                return true;
            });
            ReportNewAchievements();
            if (_cloudSync != null && _onlineStarted)
            {
                _cloudSync.Request(CloudSyncReason.AfterRun);
            }
        }

        private void OnDailyRunCompletedOnline(DailyRunOutcome outcome)
        {
            if (outcome == null || !outcome.Counted || outcome.Score <= 0)
            {
                return;
            }

            SafeOnline(() =>
            {
                _online.SubmitScore(LeaderboardKind.DailyRun, outcome.Score, outcome.Day);
                return true;
            });
        }

        private void OnProfileChangedOnline()
        {
            ReportNewAchievements();
        }

        /// <summary>Reports achievements unlocked since the last report (runs, ship purchases, cloud merges).</summary>
        private void ReportNewAchievements()
        {
            List<string> all = Profile != null && Profile.Profile != null ? Profile.Profile.Achievements : null;
            if (all == null || !_onlineStarted)
            {
                return;
            }

            if (all.Count < _achievementsSeen)
            {
                _achievementsSeen = all.Count; // progress reset
                return;
            }

            if (all.Count == _achievementsSeen)
            {
                return;
            }

            var fresh = all.GetRange(Math.Max(0, _achievementsSeen), all.Count - Math.Max(0, _achievementsSeen));
            _achievementsSeen = all.Count;
            SafeOnline(() =>
            {
                _online.UnlockAchievements(fresh, false);
                return true;
            });
        }

        private void OnPauseChangedOnline(bool paused)
        {
            if (_cloudSync == null || !_onlineStarted || IsRunActive())
            {
                return;
            }

            // Going to the background: push progress while the OS still lets the app run. Coming back: pick up
            // other devices' progress.
            _cloudSync.Request(CloudSyncReason.Pause);
        }

        private void TickOnline()
        {
            if (!_onlineStarted || _cloudSync == null || !(_online.CloudSave is IExternalCloudChanges external))
            {
                return;
            }

            _externalPollTimer -= UnityEngine.Time.unscaledDeltaTime;
            if (_externalPollTimer > 0f)
            {
                return;
            }

            _externalPollTimer = ExternalChangePollSeconds;
            int changes;
            try
            {
                changes = external.ExternalChangeCount;
            }
            catch (Exception)
            {
                return;
            }

            if (_externalChangesSeen < 0)
            {
                _externalChangesSeen = changes;
                return;
            }

            if (changes != _externalChangesSeen && !IsRunActive())
            {
                _externalChangesSeen = changes;
                _cloudSync.Request(CloudSyncReason.External);
            }
        }

        private void OnCloudMerged(ProfileMergeReport report)
        {
            if (report == null)
            {
                return;
            }

            if (report.GainedAdsRemoved)
            {
                Ads?.SetAdsRemoved(true);
            }

            if (!Profile.Owns(PreviewShipId))
            {
                PreviewShipId = Profile.Profile.SelectedShip;
            }

            if (report.NewAchievements.Count > 0)
            {
                // Unlocked on another device: make sure this account has them too (quietly, no banners).
                SafeOnline(() =>
                {
                    _online.UnlockAchievements(report.NewAchievements, true);
                    return true;
                });
            }

            _achievementsSeen = Profile.Profile.Achievements.Count;
            PresentSelectedShip();
            Profile.NotifyChanged();
            if (report.NewShips.Count > 0 || report.NewAchievements.Count > 0 || report.GemsAfter > report.GemsBefore ||
                report.AdoptedRemoteEpoch)
            {
                RaiseNotify(Loc.T("online.synced_from_cloud"));
            }
        }

        private void RaiseOnlineChanged()
        {
            try
            {
                OnlineChanged?.Invoke();
            }
            catch (Exception e)
            {
                FieldLog.Exception("OnlineChanged handler", e);
            }
        }

        private bool IsRunActive()
        {
            return Run != null || State == GameState.Countdown || State == GameState.Running || State == GameState.Paused ||
                   State == GameState.Crashed;
        }

        private static bool SafeOnline(Func<bool> call)
        {
            try
            {
                return call();
            }
            catch (Exception e)
            {
                FieldLog.Exception("Online call", e);
                return false;
            }
        }
    }
}
