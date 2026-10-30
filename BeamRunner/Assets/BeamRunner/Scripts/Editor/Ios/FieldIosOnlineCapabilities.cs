#if UNITY_IOS
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
#endif

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// iOS post-build step for the online services (<c>Resources/BeamRunner/Config/online.json</c>):
    /// <list type="bullet">
    /// <item>links <c>GameKit.framework</c> (weak) into UnityFramework, where <c>Assets/Plugins/iOS/FieldGameCenter.m</c>
    /// is compiled (its plugin settings declare the same dependency; this is the safety net);</item>
    /// <item>Game Center ids configured → the <c>com.apple.developer.game-center</c> entitlement;</item>
    /// <item><c>cloudSaveIos</c> → the iCloud key-value store entitlement
    /// (<c>com.apple.developer.ubiquity-kvstore-identifier</c> = <c>$(TeamIdentifierPrefix)$(CFBundleIdentifier)</c>,
    /// plus the empty container list Xcode writes for a key-value-only iCloud capability).</item>
    /// </list>
    /// With the shipped (empty) configuration nothing but the framework link is added, so signing needs no new
    /// capability. Entitlements go into the main target's existing entitlements file when another plugin already set
    /// <c>CODE_SIGN_ENTITLEMENTS</c>, otherwise into <c>Unity-iPhone/BeamRunner.entitlements</c>. Existing keys are
    /// never removed. <c>ProjectCapabilityManager.AddGameCenter</c> is deliberately not used: it also adds the legacy
    /// <c>gamekit</c> UIRequiredDeviceCapabilities entry (App Store Connect rejects adding required capabilities in an
    /// app update).
    /// </summary>
    public static class FieldIosOnlineCapabilities
    {
        /// <summary>Entitlements file created when the project has none (relative to the Xcode project folder).</summary>
        public const string DefaultEntitlementsPath = "Unity-iPhone/BeamRunner.entitlements";

        /// <summary>Game Center entitlement key.</summary>
        public const string GameCenterKey = "com.apple.developer.game-center";

        /// <summary>iCloud key-value store entitlement key.</summary>
        public const string KeyValueStoreKey = "com.apple.developer.ubiquity-kvstore-identifier";

        /// <summary>Default value Xcode writes for the key-value store.</summary>
        public const string KeyValueStoreValue = "$(TeamIdentifierPrefix)$(CFBundleIdentifier)";

        /// <summary>iCloud container list (empty for a key-value-only iCloud capability).</summary>
        public const string ContainersKey = "com.apple.developer.icloud-container-identifiers";

        /// <summary>Runs after <c>FieldIosTrackingPlist</c> (100) and <c>FieldIosPrivacyManifest</c> (110).</summary>
        public const int CallbackOrder = 120;

        /// <summary>Asset path of the online configuration.</summary>
        public const string OnlineConfigPath = "Assets/BeamRunner/Resources/BeamRunner/Config/online.json";

#if UNITY_IOS
        private const string Tag = "[BeamRunner] iOS online services: ";

        [Serializable]
        private sealed class OnlineMirror
        {
            public bool gameCenter = true;
            public bool cloudSaveIos = false;
            public string leaderboardBestScoreIos = "";
            public string leaderboardDailyRunIos = "";
            public AchievementMirror[] achievements = Array.Empty<AchievementMirror>();
        }

        [Serializable]
        private sealed class AchievementMirror
        {
            public string ios = "";
        }

        /// <summary>Unity post-build callback (iOS target only).</summary>
        [PostProcessBuild(CallbackOrder)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            try
            {
                bool gameCenter;
                bool cloud;
                ReadConfig(out gameCenter, out cloud);

                string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
                var project = new PBXProject();
                project.ReadFromString(File.ReadAllText(pbxPath));
                string mainTarget = project.GetUnityMainTargetGuid();
                string frameworkTarget = project.GetUnityFrameworkTargetGuid();

                bool projectChanged = false;
                if (!project.ContainsFramework(frameworkTarget, "GameKit.framework"))
                {
                    project.AddFrameworkToProject(frameworkTarget, "GameKit.framework", true);
                    projectChanged = true;
                }

                if (gameCenter || cloud)
                {
                    string entitlements = project.GetBuildPropertyForAnyConfig(mainTarget, "CODE_SIGN_ENTITLEMENTS");
                    bool created = string.IsNullOrEmpty(entitlements);
                    if (created)
                    {
                        entitlements = DefaultEntitlementsPath;
                    }

                    WriteEntitlements(Path.Combine(pathToBuiltProject, entitlements), gameCenter, cloud);
                    if (created)
                    {
                        project.AddFile(entitlements, entitlements);
                        project.SetBuildProperty(mainTarget, "CODE_SIGN_ENTITLEMENTS", entitlements);
                        projectChanged = true;
                    }

                    Debug.Log(Tag + (gameCenter ? "Game Center " : "") + (cloud ? "iCloud key-value store " : "") +
                              "entitlement(s) written to " + entitlements + ". Enable the same capabilities for the App ID " +
                              "(automatic signing does this) before archiving.");
                }

                if (projectChanged)
                {
                    File.WriteAllText(pbxPath, project.WriteToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "post-processing failed (" + e.GetType().Name + ": " + e.Message + "). Add GameKit.framework " +
                               "and the Game Center / iCloud key-value entitlements in Xcode by hand.");
            }
        }

        private static void ReadConfig(out bool gameCenter, out bool cloud)
        {
            gameCenter = false;
            cloud = false;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(OnlineConfigPath);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return;
            }

            OnlineMirror config = JsonUtility.FromJson<OnlineMirror>(asset.text);
            if (config == null)
            {
                return;
            }

            bool ids = !string.IsNullOrWhiteSpace(config.leaderboardBestScoreIos) ||
                       !string.IsNullOrWhiteSpace(config.leaderboardDailyRunIos);
            if (config.achievements != null)
            {
                foreach (AchievementMirror a in config.achievements)
                {
                    ids |= a != null && !string.IsNullOrWhiteSpace(a.ios);
                }
            }

            gameCenter = config.gameCenter && ids;
            cloud = config.gameCenter && config.cloudSaveIos;
        }

        private static void WriteEntitlements(string path, bool gameCenter, bool cloud)
        {
            var plist = new PlistDocument();
            if (File.Exists(path))
            {
                plist.ReadFromString(File.ReadAllText(path));
            }

            PlistElementDict root = plist.root;
            if (gameCenter)
            {
                root.SetBoolean(GameCenterKey, true);
            }

            if (cloud)
            {
                if (root.values == null || !root.values.ContainsKey(KeyValueStoreKey))
                {
                    root.SetString(KeyValueStoreKey, KeyValueStoreValue);
                }

                if (root.values == null || !root.values.ContainsKey(ContainersKey))
                {
                    root.CreateArray(ContainersKey);
                }
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, plist.WriteToString());
        }
#endif
    }
}
