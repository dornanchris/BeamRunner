using System;
using System.Collections.Generic;

namespace BeamRunner.Services
{
    /// <summary>
    /// No online services (nothing configured, unsupported platform, automated tests). Unavailable forever: every UI
    /// hides, every call is a no-op and callbacks report failure on a later frame.
    /// </summary>
    public sealed class NullOnlineService : IOnlineService
    {
        /// <inheritdoc />
        public event Action StateChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public OnlinePlatform Platform
        {
            get { return OnlinePlatform.None; }
        }

        /// <inheritdoc />
        public string PlatformName
        {
            get { return string.Empty; }
        }

        /// <inheritdoc />
        public bool IsAvailable
        {
            get { return false; }
        }

        /// <inheritdoc />
        public OnlineSignInState SignInState
        {
            get { return OnlineSignInState.Unavailable; }
        }

        /// <inheritdoc />
        public bool IsSignedIn
        {
            get { return false; }
        }

        /// <inheritdoc />
        public string PlayerName
        {
            get { return string.Empty; }
        }

        /// <inheritdoc />
        public bool CanSignInManually
        {
            get { return false; }
        }

        /// <inheritdoc />
        public bool UsesAccount
        {
            get { return false; }
        }

        /// <inheritdoc />
        public bool HasAchievements
        {
            get { return false; }
        }

        /// <inheritdoc />
        public ICloudSave CloudSave
        {
            get { return null; }
        }

        /// <inheritdoc />
        public void Start()
        {
        }

        /// <inheritdoc />
        public void SignIn(Action<bool> onDone)
        {
            if (onDone != null)
            {
                ServiceRunner.Post(() => onDone(false));
            }
        }

        /// <inheritdoc />
        public bool HasLeaderboard(LeaderboardKind kind)
        {
            return false;
        }

        /// <inheritdoc />
        public void SubmitScore(LeaderboardKind kind, long score, long context)
        {
        }

        /// <inheritdoc />
        public void UnlockAchievements(IList<string> gameAchievementIds, bool catchUp)
        {
        }

        /// <inheritdoc />
        public bool ShowLeaderboards()
        {
            return false;
        }

        /// <inheritdoc />
        public bool ShowAchievements()
        {
            return false;
        }
    }
}
