#if UNITY_IOS
using System;
using System.Text;

namespace BeamRunner.Services
{
    /// <summary>
    /// Game Center (GameKit) for sign-in, leaderboards and achievements, and the iCloud key-value store for cloud
    /// save, through the game's own small native bridge (<c>Assets/Plugins/iOS/FieldGameCenter.m</c>,
    /// <see cref="GameCenterNative"/>). Unity's built-in <c>UnityEngine.Social</c> Game Center platform is not used:
    /// it is <c>[Obsolete]</c> in Unity 6.3.
    /// <para>Sign-in: installing GameKit's authenticate handler (<see cref="Start"/>) signs a player who is signed in
    /// on the device in silently (GameKit shows its welcome banner). Otherwise GameKit offers a sign-in screen, which
    /// is kept until the player taps "Sign in" (<see cref="SignIn"/>); once dismissed, iOS only lets the player sign in
    /// from the Settings app. The native state is polled (no callbacks into managed code).</para>
    /// <para>Cloud save does not depend on Game Center: the key-value store belongs to the device's Apple Account.</para>
    /// </summary>
    public sealed class GameCenterOnlineService : OnlineServiceBase
    {
        private const float PollInterval = 0.25f;

        private bool _started;
        private bool _loginAvailable;
        private int _seenChanges = -1;
        private float _nextPoll;
        private Action<bool> _pendingSignIn;

        /// <summary>Creates the service (nothing native is touched until <see cref="Start"/>).</summary>
        public GameCenterOnlineService(OnlineConfig config)
            : base(config, OnlinePlatform.GameCenter)
        {
            if (Config.CloudSaveIos)
            {
                CloudSave = new KeyValueCloudSave();
            }
        }

        /// <inheritdoc />
        public override string PlatformName
        {
            get { return "Game Center"; }
        }

        /// <inheritdoc />
        public override bool CanSignInManually
        {
            get { return _loginAvailable && SignInState != OnlineSignInState.SignedIn; }
        }

        /// <inheritdoc />
        public override void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            if (!UsesAccount)
            {
                // Cloud save only: never show Game Center's sign-in banner for nothing.
                SetState(OnlineSignInState.SignedOut);
                return;
            }

            SetState(OnlineSignInState.SigningIn);
            Guard(GameCenterNative.Authenticate, "Game Center authentication");
            ServiceRunner.Ticked += Poll;
            StartSignInWatchdog();
        }

        /// <inheritdoc />
        public override void SignIn(Action<bool> onDone)
        {
            if (IsSignedIn)
            {
                Complete(onDone, true);
                return;
            }

            if (!_loginAvailable || !Guard(GameCenterNative.PresentLogin, "Presenting the Game Center sign-in"))
            {
                Complete(onDone, false);
                return;
            }

            _loginAvailable = false;
            SetState(OnlineSignInState.SigningIn);
            StartSignInWatchdog(300f); // the player may take a while on GameKit's sign-in screen
            Action<bool> previous = _pendingSignIn;
            _pendingSignIn = onDone;
            Complete(previous, false);
        }

        /// <inheritdoc />
        protected override void SubmitScoreNative(string leaderboardId, long score, long context)
        {
            GameCenterNative.SubmitScore(leaderboardId, score, context);
        }

        /// <inheritdoc />
        protected override void UnlockAchievementNative(string achievementId, bool announce)
        {
            GameCenterNative.ReportAchievement(achievementId, announce);
        }

        /// <inheritdoc />
        protected override bool ShowLeaderboardsNative()
        {
            return GameCenterNative.ShowLeaderboards();
        }

        /// <inheritdoc />
        protected override bool ShowAchievementsNative()
        {
            return GameCenterNative.ShowAchievements();
        }

        private void Poll()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextPoll)
            {
                return;
            }

            _nextPoll = now + PollInterval;
            int changes;
            int state;
            try
            {
                changes = GameCenterNative.AuthChangeCount();
                if (changes == _seenChanges)
                {
                    return;
                }

                _seenChanges = changes;
                state = GameCenterNative.AuthState();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Game Center: polling failed", e);
                ServiceRunner.Ticked -= Poll;
                SetState(OnlineSignInState.SignedOut);
                return;
            }

            _loginAvailable = state == GameCenterNative.StateLoginAvailable;
            switch (state)
            {
                case GameCenterNative.StateAuthenticated:
                    string name = string.Empty;
                    Guard(() => { name = GameCenterNative.PlayerName(); }, "Game Center player name");
                    SetState(OnlineSignInState.SignedIn, name);
                    break;
                case GameCenterNative.StateAuthenticating:
                    SetState(OnlineSignInState.SigningIn);
                    return; // a pending interactive sign-in is still running
                default:
                    ServiceLog.Info("Game Center: not signed in (state " + state + ", error " +
                                    GameCenterNative.LastErrorCode() + ").");
                    SetState(OnlineSignInState.SignedOut);
                    break;
            }

            Action<bool> pending = _pendingSignIn;
            _pendingSignIn = null;
            Complete(pending, IsSignedIn);
        }

        /// <summary>
        /// Cloud save in the iCloud key-value store: one string value holding the signed profile. Reads and writes are
        /// local and immediate; iCloud propagates them in the background (seconds to minutes) and reports other
        /// devices' changes (<see cref="ExternalChangeCount"/>). Needs the iCloud key-value storage entitlement, which
        /// the iOS build adds when <c>cloudSaveIos</c> is on (FieldIosOnlineCapabilities).
        /// </summary>
        private sealed class KeyValueCloudSave : ICloudSave, IExternalCloudChanges
        {
            private const string Key = "beamrunner.profile.v1";

            // The store holds 1 MB in total; stay well below it.
            private const int MaxBytes = 900 * 1024;

            private CloudSyncOperation _operation;

            public bool IsAvailable
            {
                get { return true; }
            }

            // The key-value store belongs to the device's Apple Account, which the game cannot identify.
            public string AccountKey
            {
                get { return string.Empty; }
            }

            public int ExternalChangeCount
            {
                get
                {
                    try
                    {
                        return GameCenterNative.KvsChangeCount();
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }
            }

            public void Sync(Func<string, CloudDocument> merge, Action<CloudSyncResult> onDone)
            {
                if (_operation != null && !_operation.IsDone)
                {
                    CloudSyncOperation.Reject(onDone, CloudSyncStatus.Busy, "busy");
                    return;
                }

                // Synchronous (the store is local; iCloud propagates in the background), so a sync requested while the
                // app goes to the background still writes before iOS suspends it. The result is reported next frame.
                CloudSyncOperation op = _operation = new CloudSyncOperation(onDone, 20f);
                string text;
                try
                {
                    GameCenterNative.KvsSynchronize();
                    text = GameCenterNative.KvsGetString(Key);
                }
                catch (Exception e)
                {
                    op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, e.Message));
                    return;
                }

                CloudDocument doc = op.Merge(merge, string.IsNullOrEmpty(text) ? null : text);
                if (doc == null || doc.Text == null)
                {
                    op.Finish(CloudSyncResult.Of(CloudSyncStatus.Declined, "merge declined"));
                    return;
                }

                if (Encoding.UTF8.GetByteCount(doc.Text) > MaxBytes)
                {
                    op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, "document too large for the key-value store"));
                    return;
                }

                try
                {
                    bool stored = GameCenterNative.KvsSetString(Key, doc.Text);
                    GameCenterNative.KvsSynchronize();
                    op.Finish(stored
                        ? CloudSyncResult.Of(CloudSyncStatus.Success, null, true)
                        : CloudSyncResult.Of(CloudSyncStatus.Failed, "store refused the value"));
                }
                catch (Exception e)
                {
                    op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, e.Message));
                }
            }
        }
    }
}
#endif
