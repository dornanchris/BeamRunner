using System;
using System.Collections.Generic;

namespace BeamRunner.Services
{
    /// <summary>
    /// Shared plumbing of the real online services: state + <see cref="StateChanged"/> (always posted to a later frame),
    /// id mapping from <see cref="OnlineConfig"/>, guards (signed in, id configured, positive score) and exception
    /// containment around every platform call.
    /// </summary>
    public abstract class OnlineServiceBase : IOnlineService
    {
        private readonly HashSet<string> _reportedAchievements = new HashSet<string>(StringComparer.Ordinal);
        private OnlineSignInState _state = OnlineSignInState.NotStarted;
        private string _playerName = string.Empty;
        private bool _statePosted;
        private int _signInWatchdogGeneration;

        /// <summary>Creates the service for <paramref name="platform"/> with <paramref name="config"/>.</summary>
        protected OnlineServiceBase(OnlineConfig config, OnlinePlatform platform)
        {
            Config = config ?? OnlineConfig.Current;
            Platform = platform;
        }

        /// <summary>The configuration (ids, switches).</summary>
        protected OnlineConfig Config { get; private set; }

        /// <inheritdoc />
        public event Action StateChanged;

        /// <inheritdoc />
        public OnlinePlatform Platform { get; private set; }

        /// <inheritdoc />
        public abstract string PlatformName { get; }

        /// <inheritdoc />
        public virtual bool IsAvailable
        {
            get { return true; }
        }

        /// <inheritdoc />
        public OnlineSignInState SignInState
        {
            get { return _state; }
        }

        /// <inheritdoc />
        public bool IsSignedIn
        {
            get { return _state == OnlineSignInState.SignedIn; }
        }

        /// <inheritdoc />
        public string PlayerName
        {
            get { return _playerName; }
        }

        /// <inheritdoc />
        public abstract bool CanSignInManually { get; }

        /// <inheritdoc />
        public virtual bool UsesAccount
        {
            get
            {
                return HasAchievements || HasLeaderboard(LeaderboardKind.BestScore) ||
                       HasLeaderboard(LeaderboardKind.DailyRun);
            }
        }

        /// <inheritdoc />
        public bool HasAchievements
        {
            get { return Config.HasAnyAchievement(Platform); }
        }

        /// <inheritdoc />
        public ICloudSave CloudSave { get; protected set; }

        /// <inheritdoc />
        public abstract void Start();

        /// <inheritdoc />
        public abstract void SignIn(Action<bool> onDone);

        /// <inheritdoc />
        public bool HasLeaderboard(LeaderboardKind kind)
        {
            return Config.LeaderboardId(kind, Platform).Length > 0;
        }

        /// <inheritdoc />
        public void SubmitScore(LeaderboardKind kind, long score, long context)
        {
            string id = Config.LeaderboardId(kind, Platform);
            if (!IsSignedIn || score <= 0 || id.Length == 0)
            {
                return;
            }

            Guard(() => SubmitScoreNative(id, score, Math.Max(0L, context)), "Submitting a score");
        }

        /// <inheritdoc />
        public void UnlockAchievements(IList<string> gameAchievementIds, bool catchUp)
        {
            if (!IsSignedIn || gameAchievementIds == null)
            {
                return;
            }

            for (int i = 0; i < gameAchievementIds.Count; i++)
            {
                string id = Config.AchievementId(gameAchievementIds[i], Platform);
                if (id.Length == 0 || !_reportedAchievements.Add(id))
                {
                    continue;
                }

                string platformId = id;
                Guard(() => UnlockAchievementNative(platformId, !catchUp), "Unlocking an achievement");
            }
        }

        /// <inheritdoc />
        public bool ShowLeaderboards()
        {
            if (!IsSignedIn || (!HasLeaderboard(LeaderboardKind.BestScore) && !HasLeaderboard(LeaderboardKind.DailyRun)))
            {
                return false;
            }

            return Guard(ShowLeaderboardsNative, "Showing leaderboards");
        }

        /// <inheritdoc />
        public bool ShowAchievements()
        {
            if (!IsSignedIn || !HasAchievements)
            {
                return false;
            }

            return Guard(ShowAchievementsNative, "Showing achievements");
        }

        /// <summary>Platform call: submit to the leaderboard <paramref name="leaderboardId"/>.</summary>
        protected abstract void SubmitScoreNative(string leaderboardId, long score, long context);

        /// <summary>
        /// Platform call: unlock <paramref name="achievementId"/> (a platform id). <paramref name="announce"/>: show the
        /// platform's completion banner where that is optional.
        /// </summary>
        protected abstract void UnlockAchievementNative(string achievementId, bool announce);

        /// <summary>Platform call: open the leaderboards UI. Return false when it could not be opened.</summary>
        protected abstract bool ShowLeaderboardsNative();

        /// <summary>Platform call: open the achievements UI. Return false when it could not be opened.</summary>
        protected abstract bool ShowAchievementsNative();

        /// <summary>
        /// Updates the state; <see cref="StateChanged"/> is raised on a later frame (coalesced). Signing in again (e.g.
        /// another account) forgets which achievements were reported this session.
        /// </summary>
        protected void SetState(OnlineSignInState state, string playerName = null)
        {
            string name = state == OnlineSignInState.SignedIn ? (playerName ?? string.Empty) : string.Empty;
            if (state == _state && string.Equals(name, _playerName, StringComparison.Ordinal))
            {
                return;
            }

            if (state == OnlineSignInState.SignedIn && _state != OnlineSignInState.SignedIn)
            {
                _reportedAchievements.Clear();
            }

            ServiceLog.Info("Online (" + Platform + "): " + _state + " -> " + state + ".");
            _state = state;
            _playerName = name;
            if (_statePosted)
            {
                return;
            }

            _statePosted = true;
            ServiceRunner.Post(() =>
            {
                _statePosted = false;
                Action handler = StateChanged;
                if (handler != null)
                {
                    handler();
                }
            });
        }

        /// <summary>
        /// A sign-in that never reports back must not leave the UI at "Signing in..." forever: after
        /// <paramref name="seconds"/> of app time still signing in, the state becomes signed out (a late answer still
        /// updates it).
        /// </summary>
        protected void StartSignInWatchdog(float seconds = 60f)
        {
            // Only the latest attempt's watchdog counts (an earlier one must not cut a later attempt short).
            int generation = ++_signInWatchdogGeneration;
            ServiceRunner.Delay(seconds, () =>
            {
                if (generation == _signInWatchdogGeneration && _state == OnlineSignInState.SigningIn)
                {
                    ServiceLog.Warn("Online (" + Platform + "): no sign-in answer in time.");
                    SetState(OnlineSignInState.SignedOut);
                }
            }, true);
        }

        /// <summary>Runs a platform call; exceptions are logged and turned into false.</summary>
        protected static bool Guard(Func<bool> call, string what)
        {
            try
            {
                return call();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Online: " + what + " failed", e);
                return false;
            }
        }

        /// <summary>Runs a platform call; exceptions are logged.</summary>
        protected static void Guard(Action call, string what)
        {
            try
            {
                call();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Online: " + what + " failed", e);
            }
        }

        /// <summary>Invokes a completion callback on a later frame (never synchronously), swallowing its exceptions.</summary>
        protected static void Complete<T>(Action<T> callback, T value)
        {
            if (callback == null)
            {
                return;
            }

            ServiceRunner.Post(() => callback(value));
        }
    }
}
