using System;
using System.Collections.Generic;
using System.IO;
using BeamRunner.Core.Meta;
using UnityEditor;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// <c>Validate Project</c> checks for the online services (<c>online.json</c>): missing Google Play Games / Game
    /// Center leaderboard and achievement ids, the Play Games plugin's Android setup (app id, manifest), the native
    /// dependency resolver, and what the iOS build will add (capabilities).
    /// </summary>
    public static class FieldOnlineValidation
    {
        /// <summary>Asset path of the online configuration.</summary>
        public const string OnlineConfigPath = "Assets/BeamRunner/Resources/BeamRunner/Config/online.json";

        /// <summary>Where the Play Games plugin's Android setup stores the app id.</summary>
        public const string PlayGamesSettingsPath = "Assets/GooglePlayGames/Resources/PlayGamesSettings.asset";

        /// <summary>Android library manifest written by the Play Games plugin's Android setup.</summary>
        public const string PlayGamesManifestPath = "Assets/Plugins/Android/GooglePlayGamesManifest.androidlib/AndroidManifest.xml";

        [Serializable]
        private sealed class OnlineMirror
        {
            public bool googlePlayGames = true;
            public bool gameCenter = true;
            public bool cloudSaveAndroid = false;
            public bool cloudSaveIos = false;
            public bool simulateInEditor = false;
            public string leaderboardBestScoreAndroid = "";
            public string leaderboardBestScoreIos = "";
            public string leaderboardDailyRunAndroid = "";
            public string leaderboardDailyRunIos = "";
            public AchievementMirror[] achievements = Array.Empty<AchievementMirror>();
        }

        [Serializable]
        private sealed class AchievementMirror
        {
            public string id = "";
            public string android = "";
            public string ios = "";
        }

        /// <summary>Minimum Android API of the vendored Google Play Games plugin (its bridge AAR's minSdkVersion).</summary>
        public const int PlayGamesMinimumApi = 24;

        /// <summary>Runs the online checks.</summary>
        public static void Check(FieldValidationReport r)
        {
            if (Directory.Exists("Assets/GooglePlayGames") && (int)UnityEditor.PlayerSettings.Android.minSdkVersion < PlayGamesMinimumApi)
            {
                r.Error("Android minimum API is " + (int)UnityEditor.PlayerSettings.Android.minSdkVersion + " but the Google Play Games plugin " +
                        "needs " + PlayGamesMinimumApi + " (the Gradle manifest merge fails): run BeamRunner ▸ Setup Project.");
            }

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(OnlineConfigPath);
            if (asset == null)
            {
                r.Warn("online.json missing (" + OnlineConfigPath + "): Google Play Games, Game Center and cloud save are off.");
                return;
            }

            OnlineMirror config;
            try
            {
                config = JsonUtility.FromJson<OnlineMirror>(asset.text);
            }
            catch (Exception e)
            {
                r.Error("online.json does not parse (" + e.Message + "); online services stay off.");
                return;
            }

            if (config == null)
            {
                r.Error("online.json is empty.");
                return;
            }

            r.Ok("online.json parses");
            CheckAchievementList(r, config);
            bool android = CheckPlatform(r, "Android (Google Play Games)", config.googlePlayGames, config.leaderboardBestScoreAndroid,
                config.leaderboardDailyRunAndroid, config.cloudSaveAndroid, config, a => a.android);
            bool ios = CheckPlatform(r, "iOS (Game Center)", config.gameCenter, config.leaderboardBestScoreIos,
                config.leaderboardDailyRunIos, config.cloudSaveIos, config, a => a.ios);

            if (android)
            {
                CheckPlayGamesSetup(r);
            }

            if (ios && config.cloudSaveIos)
            {
                r.Ok("iOS cloud save on: the iOS build adds the iCloud (key-value storage) capability — enable iCloud for the " +
                     "App ID (automatic signing does it) and test with two devices on one Apple Account");
            }

            if (config.simulateInEditor)
            {
                r.Ok("simulateInEditor on: the Editor and development builds simulate sign-in and a file-backed cloud save");
            }
        }

        private static void CheckAchievementList(FieldValidationReport r, OnlineMirror config)
        {
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (AchievementMirror a in config.achievements ?? Array.Empty<AchievementMirror>())
            {
                string id = a != null ? (a.id ?? "").Trim() : "";
                if (id.Length == 0)
                {
                    r.Error("online.json: an achievements entry has no \"id\".");
                    continue;
                }

                if (!listed.Add(id))
                {
                    r.Warn("online.json: achievement '" + id + "' is listed twice (the first entry wins).");
                }

                if (AchievementCatalog.Get(id) == null)
                {
                    r.Warn("online.json: '" + id + "' is not a game achievement (AchievementCatalog); it is never unlocked.");
                }
            }

            var missing = new List<string>();
            foreach (AchievementDefinition def in AchievementCatalog.All)
            {
                if (!listed.Contains(def.Id))
                {
                    missing.Add(def.Id);
                }
            }

            if (missing.Count > 0)
            {
                r.Warn("online.json has no entry for " + missing.Count + " game achievement(s): " + string.Join(", ", missing) +
                       ". Add them (with platform ids) or they never unlock online.");
            }
        }

        /// <summary>Returns true when the platform has any online feature configured.</summary>
        private static bool CheckPlatform(FieldValidationReport r, string name, bool enabled, string best, string daily, bool cloud,
            OnlineMirror config, Func<AchievementMirror, string> platformId)
        {
            if (!enabled)
            {
                r.Ok(name + ": switched off in online.json");
                return false;
            }

            bool hasBest = !string.IsNullOrWhiteSpace(best);
            bool hasDaily = !string.IsNullOrWhiteSpace(daily);
            int mapped = 0;
            int total = 0;
            foreach (AchievementMirror a in config.achievements ?? Array.Empty<AchievementMirror>())
            {
                if (a == null || string.IsNullOrWhiteSpace(a.id))
                {
                    continue;
                }

                total++;
                if (!string.IsNullOrWhiteSpace(platformId(a)))
                {
                    mapped++;
                }
            }

            if (!hasBest && !hasDaily && mapped == 0 && !cloud)
            {
                r.Warn(name + ": no leaderboard or achievement ids in online.json and cloud save off — sign-in, " +
                       "leaderboards, achievements and cloud save are hidden on this platform.");
                return false;
            }

            if (!hasBest)
            {
                r.Warn(name + ": the best-score leaderboard id is empty (no all-time leaderboard).");
            }

            if (!hasDaily)
            {
                r.Warn(name + ": the Daily Run leaderboard id is empty (Daily Run scores are not submitted).");
            }

            if (mapped < total)
            {
                r.Warn(name + ": " + (total - mapped) + " of " + total + " achievement ids are empty in online.json " +
                       "(those achievements never unlock online).");
            }
            else if (total > 0)
            {
                r.Ok(name + ": all " + total + " achievement ids set");
            }

            if (hasBest && hasDaily)
            {
                r.Ok(name + ": both leaderboard ids set");
            }

            return true;
        }

        private static void CheckPlayGamesSetup(FieldValidationReport r)
        {
            string appId = ReadPlayGamesAppId();
            if (string.IsNullOrEmpty(appId))
            {
                r.Warn("Google Play Games is configured in online.json but the plugin's Android setup was not run (no app id in " +
                       PlayGamesSettingsPath + "): Window ▸ Google Play Games ▸ Setup ▸ Android Setup, paste the resources " +
                       "XML from Play Console (Play Games Services ▸ Setup and management ▸ Configuration ▸ Get resources). " +
                       "Until then Play Games stays off.");
            }
            else
            {
                r.Ok("Google Play Games app id set (" + appId + ")");
                string manifest = File.Exists(PlayGamesManifestPath) ? File.ReadAllText(PlayGamesManifestPath) : "";
                if (manifest.IndexOf("com.google.android.gms.games.APP_ID", StringComparison.Ordinal) < 0)
                {
                    r.Error("The Play Games app id is set but " + PlayGamesManifestPath + " has no APP_ID meta-data: run the " +
                            "plugin's Android Setup again (it regenerates the manifest).");
                }
            }

            bool resolver = Directory.Exists("Assets/ExternalDependencyManager") || Directory.Exists("Assets/MobileDependencyResolver") ||
                            Directory.Exists("Assets/Mobile Dependency Resolver") ||
                            Type.GetType("Google.JarResolver.PlayServicesSupport, Google.JarResolver") != null;
            if (!resolver)
            {
                r.Warn("No External Dependency Manager / Mobile Dependency Resolver found: play-services-games-v2 (declared in " +
                       "GooglePlayGamesPluginDependencies.xml) is not resolved and Play Games will fail at run time. Install " +
                       "the resolver LevelPlay offers, then Assets ▸ External Dependency Manager ▸ Android Resolver ▸ Resolve.");
            }
            else if (Directory.Exists("Assets/ExternalDependencyManager") &&
                     (Directory.Exists("Assets/MobileDependencyResolver") || Directory.Exists("Assets/Mobile Dependency Resolver")))
            {
                r.Warn("Two dependency resolvers are installed (Assets/ExternalDependencyManager and the Mobile Dependency " +
                       "Resolver): keep one, delete the other.");
            }
        }

        private static string ReadPlayGamesAppId()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PlayGamesSettingsPath);
            if (settings == null)
            {
                return null;
            }

            var so = new SerializedObject(settings);
            SerializedProperty appId = so.FindProperty("mAppId");
            return appId != null ? (appId.stringValue ?? "").Trim() : null;
        }
    }
}
