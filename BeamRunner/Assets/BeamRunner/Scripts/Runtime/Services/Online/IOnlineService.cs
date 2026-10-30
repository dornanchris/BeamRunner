using System;
using System.Collections.Generic;

namespace BeamRunner.Services
{
    /// <summary>Which online platform backs an <see cref="IOnlineService"/>.</summary>
    public enum OnlinePlatform
    {
        None = 0,
        GooglePlayGames,
        GameCenter,
        Simulated,
    }

    /// <summary>Sign-in state of the platform's game account.</summary>
    public enum OnlineSignInState
    {
        /// <summary>Not configured / not supported here: every online UI is hidden.</summary>
        Unavailable = 0,

        /// <summary>Configured but not started yet (before the first-launch consent flow finished).</summary>
        NotStarted,

        /// <summary>Signing in (automatic or interactive).</summary>
        SigningIn,

        /// <summary>Signed in.</summary>
        SignedIn,

        /// <summary>Not signed in (declined, cancelled, no account, offline). <see cref="IOnlineService.CanSignInManually"/> tells whether a button can retry.</summary>
        SignedOut,
    }

    /// <summary>The game's leaderboards.</summary>
    public enum LeaderboardKind
    {
        /// <summary>All-time best score of normal runs.</summary>
        BestScore = 0,

        /// <summary>Daily Run scores (one track per UTC day).</summary>
        DailyRun = 1,
    }

    /// <summary>
    /// Platform-neutral online services: sign-in, leaderboards, achievements (native UIs) and cloud save. Implementations:
    /// <c>PlayGamesOnlineService</c> (Android, Google Play Games plugin), <c>GameCenterOnlineService</c> (iOS, GameKit +
    /// iCloud key-value store), <see cref="SimulatedOnlineService"/> (Editor/development opt-in) and
    /// <see cref="NullOnlineService"/> (everything else, or nothing configured in <see cref="OnlineConfig"/>).
    /// Main thread only; every event and callback is raised on the main thread, never synchronously from the call that
    /// caused it. Nothing ever throws into the game.
    /// </summary>
    public interface IOnlineService
    {
        /// <summary>The backing platform (<see cref="OnlinePlatform.None"/> for the null service).</summary>
        OnlinePlatform Platform { get; }

        /// <summary>User-facing platform name ("Google Play Games", "Game Center"); empty when unavailable.</summary>
        string PlatformName { get; }

        /// <summary>Configured and supported here. False ⇒ hide every online UI.</summary>
        bool IsAvailable { get; }

        /// <summary>Current sign-in state.</summary>
        OnlineSignInState SignInState { get; }

        /// <summary>Shortcut for <c>SignInState == SignedIn</c>.</summary>
        bool IsSignedIn { get; }

        /// <summary>Display name of the signed-in player ("" otherwise).</summary>
        string PlayerName { get; }

        /// <summary>A "Sign in" button can start an interactive sign-in now.</summary>
        bool CanSignInManually { get; }

        /// <summary>
        /// Signing in matters here: leaderboards or achievements are configured, or cloud save is tied to the platform
        /// account (Google Play Games). False for an iOS setup with iCloud cloud save only.
        /// </summary>
        bool UsesAccount { get; }

        /// <summary>Raised on the main thread whenever the sign-in state or player changes.</summary>
        event Action StateChanged;

        /// <summary>
        /// Starts the automatic (silent) sign-in. Call once the first-launch consent flow is done; idempotent.
        /// </summary>
        void Start();

        /// <summary>Interactive sign-in; <paramref name="onDone"/> (optional) gets true when signed in afterwards.</summary>
        void SignIn(Action<bool> onDone);

        /// <summary>True when the leaderboard has an id here.</summary>
        bool HasLeaderboard(LeaderboardKind kind);

        /// <summary>True when at least one achievement is mapped here.</summary>
        bool HasAchievements { get; }

        /// <summary>
        /// Submits a score (fire and forget; ignored when not signed in or the leaderboard has no id). The platforms keep
        /// each player's best. <paramref name="context"/> is stored with the score where supported (Daily Run: the UTC
        /// day number).
        /// </summary>
        void SubmitScore(LeaderboardKind kind, long score, long context);

        /// <summary>
        /// Unlocks the platform achievements mapped to these game achievement ids (idempotent; unmapped ids are
        /// ignored; nothing happens while signed out). <paramref name="catchUp"/>: re-reporting achievements unlocked
        /// earlier (after sign-in / a cloud merge) — no completion banner where the platform lets us choose.
        /// </summary>
        void UnlockAchievements(IList<string> gameAchievementIds, bool catchUp);

        /// <summary>Opens the platform's leaderboards UI. False when that is not possible now.</summary>
        bool ShowLeaderboards();

        /// <summary>Opens the platform's achievements UI. False when that is not possible now.</summary>
        bool ShowAchievements();

        /// <summary>Cloud save backend, or null when cloud save is off / unsupported here.</summary>
        ICloudSave CloudSave { get; }
    }

    /// <summary>Outcome of <see cref="ICloudSave.Sync"/>.</summary>
    public enum CloudSyncStatus
    {
        /// <summary>Read (and written, if the merge asked for it).</summary>
        Success = 0,

        /// <summary>The backend cannot be used right now (not signed in, no iCloud account, cloud save disabled).</summary>
        Unavailable,

        /// <summary>A network or service error; try again later.</summary>
        Failed,

        /// <summary>No answer in time; try again later.</summary>
        TimedOut,

        /// <summary>Another sync is still running.</summary>
        Busy,

        /// <summary>The merge function declined to write (or threw); the cloud copy was left unchanged.</summary>
        Declined,
    }

    /// <summary>Result of one cloud sync.</summary>
    public struct CloudSyncResult
    {
        public CloudSyncStatus Status;

        /// <summary>A new document was written to the cloud.</summary>
        public bool Wrote;

        /// <summary>Diagnostic detail (logs only; never shown to players).</summary>
        public string Detail;

        public static CloudSyncResult Of(CloudSyncStatus status, string detail = null, bool wrote = false)
        {
            return new CloudSyncResult { Status = status, Detail = detail ?? string.Empty, Wrote = wrote };
        }
    }

    /// <summary>
    /// Implemented by cloud backends that learn about other devices' writes (the iCloud key-value store): the counter
    /// increases whenever newer data arrived, so the caller can sync again.
    /// </summary>
    public interface IExternalCloudChanges
    {
        /// <summary>Number of external changes seen so far (only compare it with an earlier value).</summary>
        int ExternalChangeCount { get; }
    }

    /// <summary>What a merge wants written to the cloud.</summary>
    public sealed class CloudDocument
    {
        /// <summary>The document text (the signed profile).</summary>
        public string Text;

        /// <summary>Total play time it represents (Saved Games metadata; its conflict policy keeps the longest).</summary>
        public double PlayedSeconds;

        /// <summary>Short description shown in platform save UIs.</summary>
        public string Description = string.Empty;
    }

    /// <summary>
    /// One cloud document per player account (the signed profile). A sync reads it, lets the caller merge, and writes
    /// the result back in one operation so no other write can slip in between on backends that support it (Saved
    /// Games open → commit). Offline-safe: failures never touch the local profile.
    /// </summary>
    public interface ICloudSave
    {
        /// <summary>The backend can be used now (e.g. signed in / an iCloud account is available).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Stable, pseudonymous key of the account the cloud copy belongs to (a hash of the platform player id), or ""
        /// when the backend cannot tell (iCloud key-value store). Used to notice an account switch before merging.
        /// </summary>
        string AccountKey { get; }

        /// <summary>
        /// Reads the cloud document and calls <paramref name="merge"/> once, on the main thread, with its text (null when
        /// the cloud is empty). <paramref name="merge"/> returns what to write, or null to leave the cloud unchanged
        /// (result <see cref="CloudSyncStatus.Declined"/>). <paramref name="onDone"/> is called exactly once, on the
        /// main thread, on a later frame. Only one sync runs at a time (<see cref="CloudSyncStatus.Busy"/>).
        /// </summary>
        void Sync(Func<string, CloudDocument> merge, Action<CloudSyncResult> onDone);
    }

    /// <summary>Helpers for online identifiers.</summary>
    public static class OnlineIds
    {
        /// <summary>
        /// A pseudonymous key for a platform player id: <paramref name="prefix"/> + the first 32 hex characters of its
        /// SHA-256. Empty for an empty id. The raw id is never stored.
        /// </summary>
        public static string AccountKey(string prefix, string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return string.Empty;
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("beamrunner/account/" + playerId));
                var sb = new System.Text.StringBuilder(prefix ?? string.Empty);
                for (int i = 0; i < 16; i++)
                {
                    sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }
    }
}
