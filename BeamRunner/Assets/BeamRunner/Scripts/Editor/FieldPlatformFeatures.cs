using System;
using System.IO;
using BeamRunner.Services;
using Sentry.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// Editor side of reminders (Mobile Notifications 2.5.0), the Play in-app review dependency and crash reporting
    /// (Sentry for Unity 4.10.3): the <c>BeamRunner ▸ Crash Reporting ▸ Set Up Sentry Options</c> menu, the
    /// <c>Validate Project</c> checks (<see cref="Check"/>) and a build guard (<see cref="FieldPlatformFeaturesBuildGuard"/>).
    /// </summary>
    public static class FieldPlatformFeatures
    {
        /// <summary>Settings file of com.unity.mobile.notifications (EditorJsonUtility JSON, committed).</summary>
        public const string NotificationSettingsPath = "ProjectSettings/NotificationsSettings.asset";

        /// <summary>White-on-transparent source of the Android small notification icon.</summary>
        public const string NotificationIconPath = "Assets/BeamRunner/Art/Notification/NotificationSmall.png";

        /// <summary>External Dependency Manager declaration of the Play In-App Review library.</summary>
        public const string PlayReviewDependenciesPath = "Assets/BeamRunner/Scripts/Editor/Android/FieldPlayReviewDependencies.xml";

        /// <summary>Sentry's CLI settings (auth token for the debug-symbol upload; kept out of git).</summary>
        public const string SentryCliOptionsPath = "Assets/Plugins/Sentry/SentryCliOptions.asset";

        private const string RequestAuthOnLaunchKey = "UnityNotificationRequestAuthorizationOnAppLaunch";
        private const string PushCapabilityKey = "UnityAddRemoteNotificationCapability";

        // ------------------------------------------------------------------ Sentry setup

        [MenuItem(FieldEditorConstants.MenuRoot + "Crash Reporting/Set Up Sentry Options", false, 60)]
        private static void SetUpSentryMenu()
        {
            string summary = SetUpSentry();
            Debug.Log("[BeamRunner] " + summary);
            EditorUtility.DisplayDialog("BeamRunner — Crash Reporting", summary, "OK");
        }

        /// <summary>
        /// Creates (or repairs) Sentry's options asset with the game's privacy settings and links it to
        /// <see cref="FieldSentryOptionsConfiguration"/>, which injects the DSN from <c>monetization.json</c> and keeps the
        /// SDK off until the player agreed. Idempotent. Returns a summary.
        /// </summary>
        public static string SetUpSentry()
        {
            EnsureFolder("Assets/Resources/Sentry");
            var configuration = AssetDatabase.LoadAssetAtPath<FieldSentryOptionsConfiguration>(FieldSentryOptionsConfiguration.AssetPath);
            bool createdConfiguration = false;
            if (configuration == null)
            {
                configuration = ScriptableObject.CreateInstance<FieldSentryOptionsConfiguration>();
                AssetDatabase.CreateAsset(configuration, FieldSentryOptionsConfiguration.AssetPath);
                createdConfiguration = true;
            }

            string optionsPath = ScriptableSentryUnityOptions.GetConfigPath();
            var options = AssetDatabase.LoadAssetAtPath<ScriptableSentryUnityOptions>(optionsPath);
            bool createdOptions = false;
            if (options == null)
            {
                options = ScriptableObject.CreateInstance<ScriptableSentryUnityOptions>();
                options.Enabled = true;
                options.Dsn = string.Empty; // injected at runtime/build time from monetization.json
                AssetDatabase.CreateAsset(options, optionsPath);
                createdOptions = true;
            }

            options.OptionsConfiguration = configuration;
            options.CaptureInEditor = false;
            options.SendDefaultPii = false;
            options.IsEnvironmentUser = false;
            options.AttachScreenshot = false;
            options.AttachViewHierarchy = false;
            options.TracesSampleRate = 0;
            options.IosNativeSupportEnabled = true;
            options.AndroidNativeSupportEnabled = true;
            options.Il2CppLineNumberSupportEnabled = true;
            EditorUtility.SetDirty(options);
            AssetDatabase.SaveAssets();

            return (createdOptions ? "Created " : "Updated ") + optionsPath + " (options configuration: " +
                   FieldSentryOptionsConfiguration.AssetPath + (createdConfiguration ? ", created" : "") + "). " +
                   "Set crashReportingDsn in monetization.json; for symbolicated IL2CPP/native crashes enter an auth " +
                   "token, org and project in Tools ▸ Sentry ▸ Debug Symbols (stored in " + SentryCliOptionsPath +
                   ", which is git-ignored).";
        }

        /// <summary>
        /// True when Sentry could start by itself at launch without the game's consent gate: an options asset exists but
        /// is not linked to <see cref="FieldSentryOptionsConfiguration"/>.
        /// </summary>
        public static bool SentryBypassesConsent(out string reason)
        {
            reason = null;
            var options = AssetDatabase.LoadAssetAtPath<ScriptableSentryUnityOptions>(ScriptableSentryUnityOptions.GetConfigPath());
            if (options == null || !options.Enabled)
            {
                return false;
            }

            if (options.OptionsConfiguration is FieldSentryOptionsConfiguration)
            {
                return false;
            }

            reason = ScriptableSentryUnityOptions.GetConfigPath() + " is enabled but its Options Configuration is not " +
                     "FieldSentryOptionsConfiguration: Sentry would start at launch before the player's consent. Run " +
                     "BeamRunner ▸ Crash Reporting ▸ Set Up Sentry Options.";
            return true;
        }

        // ------------------------------------------------------------------ validation

        /// <summary><c>Validate Project</c> checks for reminders, the review dependency and crash reporting.</summary>
        public static void Check(FieldValidationReport r)
        {
            CheckNotifications(r);
            CheckPlayReview(r);
            CheckCrashReporting(r);
        }

        [Serializable]
        private sealed class NotificationSettingsFile
        {
            public NotificationSettingsBody MonoBehaviour;
        }

        [Serializable]
        private sealed class NotificationSettingsBody
        {
            public KeyValues m_iOSNotificationSettingsValues;
            public KeyValues m_AndroidNotificationSettingsValues;
            public Drawable[] DrawableResources;
        }

        [Serializable]
        private sealed class KeyValues
        {
            public string[] m_Keys;
            public string[] m_Values;

            public string Get(string key)
            {
                if (m_Keys == null || m_Values == null)
                {
                    return null;
                }

                int i = Array.IndexOf(m_Keys, key);
                return i >= 0 && i < m_Values.Length ? m_Values[i] : null;
            }
        }

        [Serializable]
        private sealed class Drawable
        {
            public string Id;
            public int Type;
            public AssetRef Asset;
        }

        [Serializable]
        private sealed class AssetRef
        {
            public long fileID;
            public string guid;
            public int type;
        }

        /// <summary>
        /// Reads the notification settings: iOS must NOT request authorization at launch (the game asks after its own
        /// explanation); Android needs the small icon.
        /// </summary>
        public static bool ReadNotificationSettings(out bool requestAuthOnLaunch, out bool pushCapability, out bool hasSmallIcon,
            out string error)
        {
            requestAuthOnLaunch = true; // package default
            pushCapability = false;
            hasSmallIcon = false;
            error = null;
            if (!File.Exists(NotificationSettingsPath))
            {
                error = NotificationSettingsPath + " is missing (the package would use its defaults, which ask for the iOS " +
                        "permission at launch).";
                return false;
            }

            NotificationSettingsFile file;
            try
            {
                file = JsonUtility.FromJson<NotificationSettingsFile>(File.ReadAllText(NotificationSettingsPath));
            }
            catch (Exception e)
            {
                error = NotificationSettingsPath + " does not parse: " + e.Message;
                return false;
            }

            if (file == null || file.MonoBehaviour == null)
            {
                error = NotificationSettingsPath + " has an unexpected format.";
                return false;
            }

            string auth = file.MonoBehaviour.m_iOSNotificationSettingsValues?.Get(RequestAuthOnLaunchKey);
            requestAuthOnLaunch = auth == null || !string.Equals(auth, "False", StringComparison.OrdinalIgnoreCase);
            string push = file.MonoBehaviour.m_iOSNotificationSettingsValues?.Get(PushCapabilityKey);
            pushCapability = push != null && string.Equals(push, "True", StringComparison.OrdinalIgnoreCase);

            string iconGuid = AssetDatabase.AssetPathToGUID(NotificationIconPath);
            Drawable[] drawables = file.MonoBehaviour.DrawableResources ?? Array.Empty<Drawable>();
            foreach (Drawable d in drawables)
            {
                if (d != null && d.Id == LocalReminders.SmallIconId && d.Type == 0 && d.Asset != null &&
                    !string.IsNullOrEmpty(iconGuid) && d.Asset.guid == iconGuid)
                {
                    hasSmallIcon = true;
                }
            }

            return true;
        }

        private static void CheckNotifications(FieldValidationReport r)
        {
            bool requestAuthOnLaunch, pushCapability, hasSmallIcon;
            string error;
            if (!ReadNotificationSettings(out requestAuthOnLaunch, out pushCapability, out hasSmallIcon, out error))
            {
                r.Error(error);
                return;
            }

            if (requestAuthOnLaunch)
            {
                r.Error("Mobile Notifications: iOS 'Request Authorization on App Launch' is on — the permission prompt would " +
                        "appear at first launch. Turn it off (Project Settings ▸ Mobile Notifications ▸ iOS).");
            }
            else
            {
                r.Ok("Mobile Notifications: iOS permission is only requested in game, after the explanation");
            }

            if (pushCapability)
            {
                r.Warn("Mobile Notifications: the iOS push capability is on, but the game only uses local notifications.");
            }

            if (!hasSmallIcon)
            {
                r.Warn("Mobile Notifications: Android small icon '" + LocalReminders.SmallIconId + "' (" + NotificationIconPath +
                       ", Small) is not registered: reminders would show the app icon as a white square.");
            }
            else
            {
                var importer = AssetImporter.GetAtPath(NotificationIconPath) as TextureImporter;
                if (importer == null || !importer.isReadable)
                {
                    r.Warn(NotificationIconPath + " must have Read/Write enabled (the package reads its pixels at build time).");
                }
                else
                {
                    r.Ok("Mobile Notifications: Android small icon registered (" + LocalReminders.SmallIconId + ")");
                }
            }
        }

        private static void CheckPlayReview(FieldValidationReport r)
        {
            if (!File.Exists(PlayReviewDependenciesPath))
            {
                r.Warn("Play In-App Review dependency declaration missing (" + PlayReviewDependenciesPath + "): Android " +
                       "review prompts will do nothing.");
                return;
            }

            r.Ok("Play In-App Review (com.google.android.play:review) declared for the Android Resolver — run " +
                 "Assets ▸ External Dependency Manager ▸ Android Resolver ▸ Resolve");
        }

        private static void CheckCrashReporting(FieldValidationReport r)
        {
            string reason;
            if (SentryBypassesConsent(out reason))
            {
                r.Error(reason);
                return;
            }

            bool hasDsn = !string.IsNullOrEmpty(MonetizationConfig.Current.CrashReportingDsn);
            bool hasOptions = AssetDatabase.LoadAssetAtPath<ScriptableSentryUnityOptions>(ScriptableSentryUnityOptions.GetConfigPath()) != null;
            if (!hasDsn)
            {
                r.Ok("Crash reporting off (crashReportingDsn is empty in monetization.json)");
                return;
            }

            if (!hasOptions)
            {
                r.Warn("crashReportingDsn is set but Sentry's options asset is missing: only C# exceptions are reported (no " +
                       "native crashes, no symbol upload). Run BeamRunner ▸ Crash Reporting ▸ Set Up Sentry Options.");
            }
            else
            {
                r.Ok("Crash reporting: Sentry options linked to the consent gate (FieldSentryOptionsConfiguration)");
            }

            if (!File.Exists(SentryCliOptionsPath))
            {
                r.Warn("Sentry debug-symbol upload not configured (" + SentryCliOptionsPath + "): IL2CPP/native stack traces " +
                       "will not be symbolicated. Tools ▸ Sentry ▸ Debug Symbols: auth token, org, project.");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Fails Android/iOS builds that would break a privacy promise: Sentry able to start before consent, or the iOS
    /// notification permission requested at launch.
    /// </summary>
    public sealed class FieldPlatformFeaturesBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildTarget target = report.summary.platform;
            if (target != BuildTarget.Android && target != BuildTarget.iOS)
            {
                return;
            }

            string reason;
            if (FieldPlatformFeatures.SentryBypassesConsent(out reason))
            {
                throw new BuildFailedException("[BeamRunner] " + reason);
            }

            if (target == BuildTarget.iOS)
            {
                bool requestAuthOnLaunch, pushCapability, hasSmallIcon;
                string error;
                if (FieldPlatformFeatures.ReadNotificationSettings(out requestAuthOnLaunch, out pushCapability, out hasSmallIcon, out error) &&
                    requestAuthOnLaunch)
                {
                    throw new BuildFailedException("[BeamRunner] Mobile Notifications would ask for the iOS notification " +
                                                   "permission at launch. Turn off 'Request Authorization on App Launch'.");
                }
            }
        }
    }
}
