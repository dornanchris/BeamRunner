using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Online services configuration (Google Play Games / Game Center ids, cloud save switches) loaded from
    /// <c>Resources/BeamRunner/Config/online.json</c>, editable without code — like <see cref="MonetizationConfig"/>.
    /// Every id defaults to empty and <b>an empty id hides that feature</b>: with the shipped file nothing online is used
    /// and the game behaves exactly as without these services. Missing keys keep their defaults; a missing or corrupt
    /// file yields the defaults. Cached; use <see cref="Current"/>.
    /// </summary>
    [Serializable]
    public sealed class OnlineConfig
    {
        /// <summary>Resources path of the JSON file (without extension).</summary>
        public const string ResourcePath = "BeamRunner/Config/online";

        [SerializeField] private bool googlePlayGames = true;
        [SerializeField] private bool gameCenter = true;
        [SerializeField] private bool cloudSaveAndroid;
        [SerializeField] private bool cloudSaveIos;
        [SerializeField] private bool simulateInEditor;
        [SerializeField] private string leaderboardBestScoreAndroid = "";
        [SerializeField] private string leaderboardBestScoreIos = "";
        [SerializeField] private string leaderboardDailyRunAndroid = "";
        [SerializeField] private string leaderboardDailyRunIos = "";
        [SerializeField] private OnlineAchievementId[] achievements = Array.Empty<OnlineAchievementId>();

        private static OnlineConfig s_current;

        [NonSerialized] private bool _loadedFromFile;

        /// <summary>The cached configuration (loaded on first access, never null).</summary>
        public static OnlineConfig Current
        {
            get
            {
                if (s_current == null)
                {
                    s_current = Load();
                }

                return s_current;
            }
        }

        /// <summary>Master switch for the Android online services (Google Play Games Services and its Saved Games).</summary>
        public bool GooglePlayGames => googlePlayGames;

        /// <summary>Master switch for the iOS online services (Game Center and the iCloud cloud save).</summary>
        public bool GameCenter => gameCenter;

        /// <summary>
        /// Android cloud save through Play Games Saved Games (enable "Saved Games" in the Play Console's Play Games
        /// Services configuration first). Off by default.
        /// </summary>
        public bool CloudSaveAndroid => cloudSaveAndroid;

        /// <summary>
        /// iOS cloud save through the iCloud key-value store. Off by default because it makes the iOS build add the
        /// iCloud capability (key-value storage), which the App ID / provisioning profile must allow.
        /// </summary>
        public bool CloudSaveIos => cloudSaveIos;

        /// <summary>
        /// Editor / development builds: simulate sign-in, leaderboards, achievements and a file-backed cloud save
        /// (<see cref="SimulatedOnlineService"/>). Off by default so the Editor behaves like an unconfigured release.
        /// </summary>
        public bool SimulateInEditor => simulateInEditor;

        /// <summary>True when the values came from the JSON file rather than built-in defaults.</summary>
        public bool LoadedFromFile => _loadedFromFile;

        /// <summary>All achievement mappings (never null; entries have a non-empty game id).</summary>
        public IReadOnlyList<OnlineAchievementId> Achievements => achievements ?? Array.Empty<OnlineAchievementId>();

        /// <summary>Leaderboard id of <paramref name="kind"/> for a platform ("" = no such leaderboard).</summary>
        public string LeaderboardId(LeaderboardKind kind, OnlinePlatform platform)
        {
            bool ios = platform == OnlinePlatform.GameCenter;
            switch (kind)
            {
                case LeaderboardKind.DailyRun:
                    return ios ? leaderboardDailyRunIos ?? "" : leaderboardDailyRunAndroid ?? "";
                default:
                    return ios ? leaderboardBestScoreIos ?? "" : leaderboardBestScoreAndroid ?? "";
            }
        }

        /// <summary>Platform achievement id of the game achievement <paramref name="gameId"/> ("" = not mapped).</summary>
        public string AchievementId(string gameId, OnlinePlatform platform)
        {
            if (string.IsNullOrEmpty(gameId) || achievements == null)
            {
                return "";
            }

            for (int i = 0; i < achievements.Length; i++)
            {
                OnlineAchievementId a = achievements[i];
                if (a != null && string.Equals(a.Id, gameId, StringComparison.Ordinal))
                {
                    return a.For(platform);
                }
            }

            return "";
        }

        /// <summary>True when at least one achievement is mapped for the platform.</summary>
        public bool HasAnyAchievement(OnlinePlatform platform)
        {
            if (achievements == null)
            {
                return false;
            }

            for (int i = 0; i < achievements.Length; i++)
            {
                if (achievements[i] != null && achievements[i].For(platform).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the platform has anything to do: a leaderboard id, an achievement id or cloud save switched on,
        /// and its master switch on.
        /// </summary>
        public bool HasAnyFeature(OnlinePlatform platform)
        {
            switch (platform)
            {
                case OnlinePlatform.GooglePlayGames:
                    if (!googlePlayGames)
                    {
                        return false;
                    }

                    break;
                case OnlinePlatform.GameCenter:
                    if (!gameCenter)
                    {
                        return false;
                    }

                    break;
                case OnlinePlatform.Simulated:
                    return true;
                default:
                    return false;
            }

            return CloudSave(platform) || LeaderboardId(LeaderboardKind.BestScore, platform).Length > 0 ||
                   LeaderboardId(LeaderboardKind.DailyRun, platform).Length > 0 || HasAnyAchievement(platform);
        }

        /// <summary>Cloud save switch of a platform.</summary>
        public bool CloudSave(OnlinePlatform platform)
        {
            switch (platform)
            {
                case OnlinePlatform.GooglePlayGames:
                    return cloudSaveAndroid;
                case OnlinePlatform.GameCenter:
                    return cloudSaveIos;
                case OnlinePlatform.Simulated:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Loads the configuration from Resources (bypassing the cache). Never throws and never returns null.</summary>
        public static OnlineConfig Load()
        {
            var config = new OnlineConfig();
            try
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset == null)
                {
                    ServiceLog.Info("online.json not found in Resources; online services off.");
                }
                else
                {
                    string json = asset.text;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        JsonUtility.FromJsonOverwrite(json, config);
                        config._loadedFromFile = true;
                    }

                    Resources.UnloadAsset(asset);
                }
            }
            catch (Exception e)
            {
                ServiceLog.Warn("online.json could not be parsed (" + e.Message + "); online services off.");
                config = new OnlineConfig();
            }

            config.Sanitize();
            return config;
        }

        /// <summary>Drops the cached instance so the next <see cref="Current"/> re-reads the file.</summary>
        public static void ClearCache()
        {
            s_current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
        }

        private void Sanitize()
        {
            leaderboardBestScoreAndroid = (leaderboardBestScoreAndroid ?? "").Trim();
            leaderboardBestScoreIos = (leaderboardBestScoreIos ?? "").Trim();
            leaderboardDailyRunAndroid = (leaderboardDailyRunAndroid ?? "").Trim();
            leaderboardDailyRunIos = (leaderboardDailyRunIos ?? "").Trim();
            var clean = new List<OnlineAchievementId>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (achievements != null)
            {
                for (int i = 0; i < achievements.Length; i++)
                {
                    OnlineAchievementId a = achievements[i];
                    if (a == null)
                    {
                        continue;
                    }

                    a.Sanitize();
                    if (a.Id.Length > 0 && seen.Add(a.Id))
                    {
                        clean.Add(a);
                    }
                }
            }

            achievements = clean.ToArray();
        }
    }

    /// <summary>One game achievement (<c>AchievementCatalog</c> id) mapped to its platform ids.</summary>
    [Serializable]
    public sealed class OnlineAchievementId
    {
        [SerializeField] private string id = "";
        [SerializeField] private string android = "";
        [SerializeField] private string ios = "";

        /// <summary>Game achievement id (e.g. <c>first_flight</c>).</summary>
        public string Id => id ?? "";

        /// <summary>The platform's id ("" = not mapped).</summary>
        public string For(OnlinePlatform platform)
        {
            switch (platform)
            {
                case OnlinePlatform.GooglePlayGames:
                    return android ?? "";
                case OnlinePlatform.GameCenter:
                    return ios ?? "";
                case OnlinePlatform.Simulated:
                    return Id;
                default:
                    return "";
            }
        }

        internal void Sanitize()
        {
            id = (id ?? "").Trim();
            android = (android ?? "").Trim();
            ios = (ios ?? "").Trim();
        }
    }
}
