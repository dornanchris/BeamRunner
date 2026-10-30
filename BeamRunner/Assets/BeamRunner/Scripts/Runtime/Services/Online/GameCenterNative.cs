#if UNITY_IOS
using System.Runtime.InteropServices;

namespace BeamRunner.Services
{
    /// <summary>
    /// P/Invoke surface of <c>Assets/Plugins/iOS/FieldGameCenter.m</c> (GameKit + iCloud key-value store). iOS players
    /// only; in the Editor (iOS build target) every call is a harmless no-op. Booleans cross the boundary as int and
    /// returned strings are malloc'ed by the native side (freed by the marshaller). Main thread only.
    /// </summary>
    internal static class GameCenterNative
    {
        /// <summary>Native auth states (FieldGCState* in FieldGameCenter.m).</summary>
        public const int StateIdle = 0;
        public const int StateAuthenticating = 1;
        public const int StateAuthenticated = 2;
        public const int StateSignedOut = 3;
        public const int StateLoginAvailable = 4;

#if !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FieldGC_Authenticate();

        [DllImport("__Internal")]
        private static extern int FieldGC_AuthState();

        [DllImport("__Internal")]
        private static extern int FieldGC_AuthChangeCount();

        [DllImport("__Internal")]
        private static extern long FieldGC_LastErrorCode();

        [DllImport("__Internal")]
        private static extern int FieldGC_PresentLogin();

        [DllImport("__Internal")]
        private static extern string FieldGC_PlayerName();

        [DllImport("__Internal")]
        private static extern void FieldGC_SubmitScore(string leaderboardId, long score, long context);

        [DllImport("__Internal")]
        private static extern void FieldGC_ReportAchievement(string achievementId, int showBanner);

        [DllImport("__Internal")]
        private static extern int FieldGC_ShowDashboard(int state);

        [DllImport("__Internal")]
        private static extern int FieldKvs_Synchronize();

        [DllImport("__Internal")]
        private static extern string FieldKvs_GetString(string key);

        [DllImport("__Internal")]
        private static extern int FieldKvs_SetString(string key, string value);

        [DllImport("__Internal")]
        private static extern int FieldKvs_ChangeCount();

        [DllImport("__Internal")]
        private static extern int FieldKvs_AccountChangeCount();

        public static void Authenticate() { FieldGC_Authenticate(); }
        public static int AuthState() { return FieldGC_AuthState(); }
        public static int AuthChangeCount() { return FieldGC_AuthChangeCount(); }
        public static long LastErrorCode() { return FieldGC_LastErrorCode(); }
        public static bool PresentLogin() { return FieldGC_PresentLogin() != 0; }
        public static string PlayerName() { return FieldGC_PlayerName() ?? string.Empty; }
        public static void SubmitScore(string leaderboardId, long score, long context) { FieldGC_SubmitScore(leaderboardId, score, context); }
        public static void ReportAchievement(string achievementId, bool showBanner) { FieldGC_ReportAchievement(achievementId, showBanner ? 1 : 0); }
        public static bool ShowLeaderboards() { return FieldGC_ShowDashboard(0) != 0; }
        public static bool ShowAchievements() { return FieldGC_ShowDashboard(1) != 0; }
        public static bool KvsSynchronize() { return FieldKvs_Synchronize() != 0; }
        public static string KvsGetString(string key) { return FieldKvs_GetString(key); }
        public static bool KvsSetString(string key, string value) { return FieldKvs_SetString(key, value) != 0; }
        public static int KvsChangeCount() { return FieldKvs_ChangeCount(); }
        public static int KvsAccountChangeCount() { return FieldKvs_AccountChangeCount(); }
#else
        public static void Authenticate() { }
        public static int AuthState() { return StateSignedOut; }
        public static int AuthChangeCount() { return 0; }
        public static long LastErrorCode() { return 0; }
        public static bool PresentLogin() { return false; }
        public static string PlayerName() { return string.Empty; }
        public static void SubmitScore(string leaderboardId, long score, long context) { }
        public static void ReportAchievement(string achievementId, bool showBanner) { }
        public static bool ShowLeaderboards() { return false; }
        public static bool ShowAchievements() { return false; }
        public static bool KvsSynchronize() { return false; }
        public static string KvsGetString(string key) { return null; }
        public static bool KvsSetString(string key, string value) { return false; }
        public static int KvsChangeCount() { return 0; }
        public static int KvsAccountChangeCount() { return 0; }
#endif
    }
}
#endif
