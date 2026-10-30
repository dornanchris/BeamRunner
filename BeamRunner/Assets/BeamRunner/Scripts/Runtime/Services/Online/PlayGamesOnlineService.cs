#if UNITY_ANDROID
using System;
using System.Globalization;
using System.Text;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;

namespace BeamRunner.Services
{
    /// <summary>
    /// Google Play Games Services (PGS v2) through the Google Play Games plugin for Unity 2.2.1 (vendored under
    /// <c>Assets/GooglePlayGames</c>). Uses <see cref="PlayGamesPlatform.Instance"/> directly — never
    /// <c>PlayGamesPlatform.Activate()</c> / <c>UnityEngine.Social</c>, which Unity 6.3 marks obsolete.
    /// <para>Sign-in: PGS v2 signs players in automatically when the SDK starts; <see cref="Start"/> (after the
    /// first-launch consent flow) creates the platform — which initialises the SDK — and asks for the result
    /// (<c>Authenticate</c>). A declined/failed automatic sign-in leaves a "Sign in" button
    /// (<c>ManuallyAuthenticate</c>). Cloud save: one Saved Games snapshot, opened with automatic conflict
    /// resolution (longest play time), read, merged by the caller and committed in one pass.</para>
    /// <para>Only created when the plugin's Android setup was run (<see cref="PlayGamesSettings"/> has an app id):
    /// without it the SDK would be initialised without the manifest's APP_ID.</para>
    /// </summary>
    public sealed class PlayGamesOnlineService : OnlineServiceBase
    {
        private bool _started;
        private bool _signingInManually;

        /// <summary>Creates the service (does not touch the SDK yet).</summary>
        public PlayGamesOnlineService(OnlineConfig config)
            : base(config, OnlinePlatform.GooglePlayGames)
        {
            if (Config.CloudSaveAndroid)
            {
                CloudSave = new PlayGamesCloudSave(this);
            }
        }

        /// <summary>True when the plugin's Android setup stored a Play Games app id (Resources/PlayGamesSettings).</summary>
        public static bool IsConfigured
        {
            get
            {
                try
                {
                    PlayGamesSettings settings = PlayGamesSettings.LoadInstance();
                    return settings != null && !string.IsNullOrEmpty(settings.AppId);
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Play Games: reading PlayGamesSettings failed", e);
                    return false;
                }
            }
        }

        /// <inheritdoc />
        public override string PlatformName
        {
            get { return "Google Play Games"; }
        }

        /// <inheritdoc />
        public override bool UsesAccount
        {
            get { return base.UsesAccount || CloudSave != null; }
        }

        /// <inheritdoc />
        public override bool CanSignInManually
        {
            get { return _started && SignInState == OnlineSignInState.SignedOut; }
        }

        /// <inheritdoc />
        public override void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            SetState(OnlineSignInState.SigningIn);
            bool started = Guard(() =>
            {
                // First access creates the platform, which initialises the Play Games SDK.
                PlayGamesPlatform.Instance.Authenticate(OnAutomaticSignIn);
                return true;
            }, "Play Games sign-in");

            if (started)
            {
                StartSignInWatchdog();
            }
            else
            {
                SetState(OnlineSignInState.SignedOut);
            }
        }

        /// <inheritdoc />
        public override void SignIn(Action<bool> onDone)
        {
            if (IsSignedIn)
            {
                Complete(onDone, true);
                return;
            }

            if (!_started || (_signingInManually && SignInState == OnlineSignInState.SigningIn))
            {
                Complete(onDone, false);
                return;
            }

            _signingInManually = true;
            SetState(OnlineSignInState.SigningIn);
            bool started = Guard(() =>
            {
                PlayGamesPlatform.Instance.ManuallyAuthenticate(status =>
                {
                    _signingInManually = false;
                    Apply(status);
                    Complete(onDone, status == SignInStatus.Success);
                });
                return true;
            }, "Play Games manual sign-in");

            if (!started)
            {
                _signingInManually = false;
                SetState(OnlineSignInState.SignedOut);
                Complete(onDone, false);
                return;
            }

            StartSignInWatchdog();
        }

        /// <inheritdoc />
        protected override void SubmitScoreNative(string leaderboardId, long score, long context)
        {
            // The score tag (≤ 64 URI-safe characters) records the Daily Run's UTC day.
            string tag = context > 0 ? "d" + context.ToString(CultureInfo.InvariantCulture) : null;
            if (tag != null)
            {
                PlayGamesPlatform.Instance.ReportScore(score, leaderboardId, tag, ok => LogResult("score", ok));
            }
            else
            {
                PlayGamesPlatform.Instance.ReportScore(score, leaderboardId, ok => LogResult("score", ok));
            }
        }

        /// <inheritdoc />
        protected override void UnlockAchievementNative(string achievementId, bool announce)
        {
            // PGS shows its own "achievement unlocked" pop-up only for a first unlock.
            PlayGamesPlatform.Instance.UnlockAchievement(achievementId, ok => LogResult("achievement", ok));
        }

        /// <inheritdoc />
        protected override bool ShowLeaderboardsNative()
        {
            bool best = HasLeaderboard(LeaderboardKind.BestScore);
            bool daily = HasLeaderboard(LeaderboardKind.DailyRun);

            // One leaderboard: open it directly; both: the list of all leaderboards (a null id).
            string only = best && !daily ? Config.LeaderboardId(LeaderboardKind.BestScore, Platform)
                : (daily && !best ? Config.LeaderboardId(LeaderboardKind.DailyRun, Platform) : null);
            PlayGamesPlatform.Instance.ShowLeaderboardUI(only, null);
            return true;
        }

        /// <inheritdoc />
        protected override bool ShowAchievementsNative()
        {
            PlayGamesPlatform.Instance.ShowAchievementsUI();
            return true;
        }

        private void OnAutomaticSignIn(SignInStatus status)
        {
            Apply(status);
        }

        private void Apply(SignInStatus status)
        {
            if (status == SignInStatus.Success)
            {
                string name = string.Empty;
                Guard(() => { name = PlayGamesPlatform.Instance.GetUserDisplayName() ?? string.Empty; }, "Play Games player name");
                SetState(OnlineSignInState.SignedIn, name);
            }
            else
            {
                ServiceLog.Info("Play Games: not signed in (" + status + ").");
                SetState(OnlineSignInState.SignedOut);
            }
        }

        private static void LogResult(string what, bool ok)
        {
            if (!ok)
            {
                ServiceLog.Info("Play Games: " + what + " not accepted (offline or not configured in the Play Console?).");
            }
        }

        /// <summary>Saved Games backend: open → read → merge → commit, one snapshot per player.</summary>
        private sealed class PlayGamesCloudSave : ICloudSave
        {
            private const string SnapshotName = "beamrunner_profile";
            private const float TimeoutSeconds = 45f;

            private readonly PlayGamesOnlineService _owner;
            private CloudSyncOperation _operation;

            public PlayGamesCloudSave(PlayGamesOnlineService owner)
            {
                _owner = owner;
            }

            public bool IsAvailable
            {
                get { return _owner.IsSignedIn; }
            }

            public string AccountKey
            {
                get
                {
                    if (!_owner.IsSignedIn)
                    {
                        return string.Empty;
                    }

                    string id = null;
                    Guard(() => { id = PlayGamesPlatform.Instance.GetUserId(); }, "Play Games player id");
                    return id == "0" ? string.Empty : OnlineIds.AccountKey("pgs-", id);
                }
            }

            public void Sync(Func<string, CloudDocument> merge, Action<CloudSyncResult> onDone)
            {
                if (!IsAvailable)
                {
                    CloudSyncOperation.Reject(onDone, CloudSyncStatus.Unavailable, "not signed in");
                    return;
                }

                if (_operation != null && !_operation.IsDone)
                {
                    CloudSyncOperation.Reject(onDone, CloudSyncStatus.Busy, "busy");
                    return;
                }

                CloudSyncOperation op = _operation = new CloudSyncOperation(onDone, TimeoutSeconds);
                bool started = Guard(() =>
                {
                    ISavedGameClient client = PlayGamesPlatform.Instance.SavedGame;
                    if (client == null)
                    {
                        op.Finish(CloudSyncResult.Of(CloudSyncStatus.Unavailable, "no saved game client"));
                        return true;
                    }

                    client.OpenWithAutomaticConflictResolution(SnapshotName, DataSource.ReadCacheOrNetwork,
                        ConflictResolutionStrategy.UseLongestPlaytime,
                        (status, metadata) => OnOpened(client, op, merge, status, metadata));
                    return true;
                }, "Opening the Play Games saved game");

                if (!started)
                {
                    op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, "open threw"));
                }
            }

            private static void OnOpened(ISavedGameClient client, CloudSyncOperation op, Func<string, CloudDocument> merge,
                SavedGameRequestStatus status, ISavedGameMetadata metadata)
            {
                if (status != SavedGameRequestStatus.Success || metadata == null)
                {
                    op.Finish(CloudSyncResult.Of(status == SavedGameRequestStatus.AuthenticationError
                        ? CloudSyncStatus.Unavailable
                        : CloudSyncStatus.Failed, "open: " + status));
                    return;
                }

                Guard(() => client.ReadBinaryData(metadata, (readStatus, bytes) =>
                    OnRead(client, op, merge, metadata, readStatus, bytes)), "Reading the Play Games saved game");
            }

            private static void OnRead(ISavedGameClient client, CloudSyncOperation op, Func<string, CloudDocument> merge,
                ISavedGameMetadata metadata, SavedGameRequestStatus status, byte[] bytes)
            {
                if (status != SavedGameRequestStatus.Success)
                {
                    // The snapshot stays open until the next open of the same name; never commit without its data.
                    op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, "read: " + status));
                    return;
                }

                byte[] original = bytes ?? new byte[0];
                CloudDocument doc = null;
                if (!op.IsDone)
                {
                    string text = null;
                    try
                    {
                        text = original.Length > 0 ? Encoding.UTF8.GetString(original) : null;
                    }
                    catch (Exception e)
                    {
                        ServiceLog.Warn("Play Games: saved game is not text (" + e.Message + ").");
                    }

                    doc = op.Merge(merge, text);
                }

                // Always commit to close the snapshot: the merged document, or the original bytes unchanged (declined,
                // or the operation already timed out).
                byte[] toWrite = original;
                var update = new SavedGameMetadataUpdate.Builder();
                if (doc != null && doc.Text != null)
                {
                    toWrite = Encoding.UTF8.GetBytes(doc.Text);
                    update = update.WithUpdatedDescription(doc.Description ?? string.Empty)
                        .WithUpdatedPlayedTime(TimeSpan.FromSeconds(Math.Max(0.0, doc.PlayedSeconds)));
                }

                bool wrote = doc != null && doc.Text != null;
                Guard(() => client.CommitUpdate(metadata, update.Build(), toWrite, (commitStatus, committed) =>
                {
                    if (commitStatus == SavedGameRequestStatus.Success)
                    {
                        op.Finish(wrote
                            ? CloudSyncResult.Of(CloudSyncStatus.Success, null, true)
                            : CloudSyncResult.Of(CloudSyncStatus.Declined, "merge declined"));
                    }
                    else
                    {
                        op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, "commit: " + commitStatus));
                    }
                }), "Committing the Play Games saved game");
            }
        }
    }
}
#endif
