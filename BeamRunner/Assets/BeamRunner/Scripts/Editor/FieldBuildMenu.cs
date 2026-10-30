using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// <c>BeamRunner/Build/…</c>: one-click store builds into <c>Builds/</c> (gitignored), with a pre-flight
    /// validation and a result dialog. CI entry point: <see cref="BuildFromCommandLine"/>.
    /// </summary>
    public static class FieldBuildMenu
    {
        private const string BuildMenu = FieldEditorConstants.MenuRoot + "Build/";

        /// <summary>Kinds of build the menu can produce.</summary>
        public enum Kind
        {
            /// <summary>Google Play release: Android App Bundle (.aab).</summary>
            AndroidAppBundle,

            /// <summary>Side-loadable development APK (debuggable, profiler-ready).</summary>
            AndroidDevApk,

            /// <summary>iOS Xcode project (archive and sign it in Xcode).</summary>
            IosXcode,
        }

        [MenuItem(BuildMenu + "Android App Bundle", false, 80)]
        private static void BuildAab()
        {
            Build(Kind.AndroidAppBundle, true);
        }

        [MenuItem(BuildMenu + "Android APK (dev)", false, 81)]
        private static void BuildDevApk()
        {
            Build(Kind.AndroidDevApk, true);
        }

        [MenuItem(BuildMenu + "iOS Xcode Project", false, 82)]
        private static void BuildIos()
        {
            Build(Kind.IosXcode, true);
        }

        /// <summary>
        /// Batch entry: <c>-batchmode -quit -executeMethod BeamRunner.EditorTools.FieldBuildMenu.BuildFromCommandLine
        /// -fieldBuild aab|apk|ios</c>. Exits with 1 on failure.
        /// <para>
        /// Also the <c>buildMethod</c> of the GameCI workflow (.github/workflows/unity-build.yml, docs/CI.md), so it
        /// understands the arguments GameCI's unity-builder passes to a custom build method:
        /// <c>-customBuildPath</c> (output path instead of <c>Builds/…</c>); <c>-buildTarget</c> (used when
        /// <c>-fieldBuild</c> is absent: iOS → Xcode project, Android → AAB, or the dev APK with
        /// <c>-androidExportType androidPackage</c>); <c>-buildVersion</c> (bundleVersion, <c>none</c>/empty = keep);
        /// <c>-androidVersionCode</c> (&gt; 0 = Android bundleVersionCode); <c>-androidKeystoreName</c>,
        /// <c>-androidKeystorePass</c>, <c>-androidKeyaliasName</c>, <c>-androidKeyaliasPass</c> (release signing, the
        /// keystore path relative to the project folder; each falls back to the <c>ANDROID_KEYSTORE_NAME</c>,
        /// <c>ANDROID_KEYSTORE_PASS</c>, <c>ANDROID_KEYALIAS_NAME</c>, <c>ANDROID_KEYALIAS_PASS</c> environment
        /// variable; without a keystore Unity signs with the debug key). Other GameCI arguments are ignored. The
        /// Player Settings changed for the build are restored afterwards.
        /// </para>
        /// <para>
        /// Local testing: <c>-iosSdk simulator</c> builds the Xcode project for the iOS Simulator instead of devices;
        /// <c>-fieldDevelopment</c> makes a development build (LevelPlay Test Suite with <c>testMode</c>, debug
        /// logging). Neither is for store uploads.
        /// </para>
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string fieldBuild = ArgValue(args, "fieldBuild");
            string buildTarget = ArgValue(args, "buildTarget") ?? ArgValue(args, "customBuildTarget");
            string exportType = ArgValue(args, "androidExportType");

            Kind kind;
            if (!string.IsNullOrEmpty(fieldBuild))
            {
                string arg = fieldBuild.ToLowerInvariant();
                kind = arg == "apk" ? Kind.AndroidDevApk : arg == "ios" ? Kind.IosXcode : Kind.AndroidAppBundle;
            }
            else if (string.Equals(buildTarget, "iOS", StringComparison.OrdinalIgnoreCase))
            {
                kind = Kind.IosXcode;
            }
            else if (string.Equals(exportType, "androidStudioProject", StringComparison.OrdinalIgnoreCase))
            {
                Fail(false, "-androidExportType androidStudioProject is not supported; use androidAppBundle or androidPackage.");
                EditorApplication.Exit(1);
                return;
            }
            else
            {
                kind = string.Equals(exportType, "androidPackage", StringComparison.OrdinalIgnoreCase)
                    ? Kind.AndroidDevApk
                    : Kind.AndroidAppBundle;
            }

            string kindTarget = kind == Kind.IosXcode ? "iOS" : "Android";
            if (!string.IsNullOrEmpty(buildTarget) && !string.Equals(buildTarget, kindTarget, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[BeamRunner] Build: -buildTarget " + buildTarget + " does not match the requested " +
                                 Label(kind) + "; building " + kindTarget + ".");
            }

            // Player Settings overrides (restored in finally, so a local command-line build never edits the project).
            string oldVersion = PlayerSettings.bundleVersion;
            int oldVersionCode = PlayerSettings.Android.bundleVersionCode;
            bool oldCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string oldKeystoreName = PlayerSettings.Android.keystoreName;
            string oldKeystorePass = PlayerSettings.Android.keystorePass;
            string oldKeyaliasName = PlayerSettings.Android.keyaliasName;
            string oldKeyaliasPass = PlayerSettings.Android.keyaliasPass;
            iOSSdkVersion oldIosSdk = PlayerSettings.iOS.sdkVersion;
            AppleMobileArchitectureSimulator oldSimulatorArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;
            bool ok = false;
            try
            {
                bool simulator = string.Equals(ArgValue(args, "iosSdk"), "simulator", StringComparison.OrdinalIgnoreCase);
                if (kind == Kind.IosXcode && simulator)
                {
                    // Apple Silicon simulators don't launch the x86_64 default.
                    PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
                    PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
                }

                bool development = Array.Exists(args,
                    a => string.Equals(a, "-fieldDevelopment", StringComparison.OrdinalIgnoreCase));

                string version = ArgValue(args, "buildVersion");
                if (!string.IsNullOrEmpty(version) && !string.Equals(version, "none", StringComparison.OrdinalIgnoreCase))
                {
                    PlayerSettings.bundleVersion = version;
                }

                if (int.TryParse(ArgValue(args, "androidVersionCode"), out int versionCode) && versionCode > 0)
                {
                    PlayerSettings.Android.bundleVersionCode = versionCode;
                }

                string keystore = ArgOrEnv(args, "androidKeystoreName", "ANDROID_KEYSTORE_NAME");
                bool customKeystore = kind != Kind.IosXcode && !string.IsNullOrEmpty(keystore);
                if (customKeystore)
                {
                    if (!File.Exists(keystore))
                    {
                        Fail(false, "Android keystore not found: " + Path.GetFullPath(keystore) +
                                    " (-androidKeystoreName is relative to the project folder).");
                        return;
                    }

                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = keystore;
                    PlayerSettings.Android.keystorePass = ArgOrEnv(args, "androidKeystorePass", "ANDROID_KEYSTORE_PASS") ?? string.Empty;
                    PlayerSettings.Android.keyaliasName = ArgOrEnv(args, "androidKeyaliasName", "ANDROID_KEYALIAS_NAME") ?? string.Empty;
                    PlayerSettings.Android.keyaliasPass = ArgOrEnv(args, "androidKeyaliasPass", "ANDROID_KEYALIAS_PASS") ?? string.Empty;
                }

                Debug.Log("[BeamRunner] Command-line build: " + Label(kind) + ", version " + PlayerSettings.bundleVersion +
                          (kind == Kind.IosXcode ? string.Empty : " (" + PlayerSettings.Android.bundleVersionCode + ")") +
                          (kind == Kind.IosXcode ? string.Empty : customKeystore ? ", release keystore" : ", debug keystore") +
                          (kind == Kind.IosXcode && simulator ? ", iOS Simulator SDK" : string.Empty) +
                          (development ? ", development build" : string.Empty));

                BuildReport report = Build(kind, false, ArgValue(args, "customBuildPath"), development);
                ok = report != null && report.summary.result == BuildResult.Succeeded;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                PlayerSettings.bundleVersion = oldVersion;
                PlayerSettings.Android.bundleVersionCode = oldVersionCode;
                PlayerSettings.Android.useCustomKeystore = oldCustomKeystore;
                PlayerSettings.Android.keystoreName = oldKeystoreName;
                PlayerSettings.Android.keystorePass = oldKeystorePass;
                PlayerSettings.Android.keyaliasName = oldKeyaliasName;
                PlayerSettings.Android.keyaliasPass = oldKeyaliasPass;
                PlayerSettings.iOS.sdkVersion = oldIosSdk;
                PlayerSettings.iOS.simulatorSdkArchitecture = oldSimulatorArchitecture;
                // The build already wrote the overrides to ProjectSettings.asset; write the restored values back.
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        /// <summary>Value after the last <c>-name</c> argument (case-insensitive), or null when absent.</summary>
        private static string ArgValue(string[] args, string name)
        {
            string value = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-" + name, StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i + 1];
                }
            }

            return value;
        }

        /// <summary><see cref="ArgValue"/> when non-empty, else the environment variable (null when neither is set).</summary>
        private static string ArgOrEnv(string[] args, string name, string environmentVariable)
        {
            string value = ArgValue(args, name);
            if (string.IsNullOrEmpty(value))
            {
                value = Environment.GetEnvironmentVariable(environmentVariable);
            }

            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>Builds <paramref name="kind"/>; returns the report (null when cancelled or not possible).</summary>
        public static BuildReport Build(Kind kind, bool interactive)
        {
            return Build(kind, interactive, null);
        }

        /// <summary>
        /// Builds <paramref name="kind"/> to <paramref name="location"/> (null/empty = <see cref="OutputPath"/>);
        /// returns the report (null when cancelled or not possible).
        /// </summary>
        public static BuildReport Build(Kind kind, bool interactive, string location)
        {
            return Build(kind, interactive, location, false);
        }

        /// <summary>
        /// As <see cref="Build(Kind, bool, string)"/>; <paramref name="development"/> = development build
        /// (command-line testing only).
        /// </summary>
        private static BuildReport Build(Kind kind, bool interactive, string location, bool development)
        {
            BuildTarget target = kind == Kind.IosXcode ? BuildTarget.iOS : BuildTarget.Android;
            BuildTargetGroup group = kind == Kind.IosXcode ? BuildTargetGroup.iOS : BuildTargetGroup.Android;
            string platformName = kind == Kind.IosXcode ? "iOS" : "Android";

            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                Fail(interactive, platformName + " Build Support is not installed. Add the module in Unity Hub " +
                                  "(Installs → " + Application.unityVersion + " → Add modules) and try again.");
                return null;
            }

            // BuildPlayer runs in the editor domain that is loaded now. If it was compiled for another platform, every
            // platform-guarded post-build step is silently skipped (#if UNITY_IOS: Unity IAP's StoreKit capability and
            // Xcode patches, the ATT usage string, SKAdNetwork ids, the privacy-manifest check). Unity's own Build
            // window switches the platform first and builds after the script reload; do the same.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                string current = EditorUserBuildSettings.activeBuildTarget.ToString();
                if (!interactive || Application.isBatchMode)
                {
                    Fail(interactive, "The active platform is " + current + ", not " + platformName + ". Start Unity with " +
                                      "-buildTarget " + platformName + " (GameCI does) or switch the platform first.");
                    return null;
                }

                if (EditorUtility.DisplayDialog("BeamRunner — " + Label(kind),
                        "The active platform is " + current + ". " + platformName + " builds need the editor scripts " +
                        "compiled for " + platformName + " (platform post-build steps are skipped otherwise).\n\n" +
                        "Switch the platform now? When Unity has finished reimporting and recompiling, choose " +
                        Label(kind) + " again.", "Switch Platform", "Cancel"))
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
                    Debug.Log("[BeamRunner] Build: switched the active platform to " + platformName +
                              "; run the build again after the script reload.");
                }

                return null;
            }

            if (interactive && !Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return null;
            }

            FieldProjectSetup.EnsureMainSceneFirstInBuild(out string sceneProblem);
            if (sceneProblem != null)
            {
                Fail(interactive, sceneProblem);
                return null;
            }

            // Pre-flight: errors block, release warnings are shown for confirmation.
            FieldValidationReport validation = FieldProjectValidator.Run();
            if (validation.Errors > 0)
            {
                validation.LogToConsole();
                Fail(interactive, "Validation found " + validation.Errors + " error(s); fix them first.\n\n" +
                                  validation.ToDialogText(12));
                return null;
            }

            bool release = kind != Kind.AndroidDevApk;
            if (release)
            {
                // Same rule as FieldMonetizationBuildGuard (which also covers File ▸ Build), checked up front so the
                // dialog names the problem instead of a failed build.
                List<string> monetization = FieldMonetizationBuildGuard.ReleaseProblems(target, out bool allowIncomplete);
                if (monetization.Count > 0 && !allowIncomplete)
                {
                    Fail(interactive, "This release build would reach the store unmonetized:\n\n- " +
                                      string.Join("\n- ", monetization) + "\n\nFill in " +
                                      FieldEditorConstants.MonetizationConfigPath + ", or set \"" +
                                      FieldMonetizationBuildGuard.OptOutKey + "\": true there for a test-track build.");
                    return null;
                }
            }

            if (interactive && release && validation.Warnings > 0 &&
                !EditorUtility.DisplayDialog("BeamRunner — " + Label(kind),
                    "Release checklist items are still open:\n\n" + validation.ToDialogText(10) + "\n\nBuild anyway?",
                    "Build", "Cancel"))
            {
                return null;
            }

            if (string.IsNullOrEmpty(location))
            {
                location = OutputPath(kind);
            }

            string directory = kind == Kind.IosXcode ? location : Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            BuildOptions options = BuildOptions.None;
            if (kind == Kind.AndroidDevApk || development)
            {
                options |= BuildOptions.Development | BuildOptions.AllowDebugging;
            }

            bool previousAppBundle = EditorUserBuildSettings.buildAppBundle;
            if (target == BuildTarget.Android)
            {
                EditorUserBuildSettings.buildAppBundle = kind == Kind.AndroidAppBundle;
            }

            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { FieldEditorConstants.MainScenePath },
                locationPathName = location,
                target = target,
                targetGroup = group,
                options = options,
            };

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(buildOptions);
            }
            finally
            {
                if (target == BuildTarget.Android)
                {
                    EditorUserBuildSettings.buildAppBundle = previousAppBundle;
                }
            }

            ReportResult(kind, report, location, interactive);
            return report;
        }

        private static string Label(Kind kind)
        {
            switch (kind)
            {
                case Kind.AndroidAppBundle:
                    return "Android App Bundle";
                case Kind.AndroidDevApk:
                    return "Android APK (dev)";
                default:
                    return "iOS Xcode Project";
            }
        }

        /// <summary>Output path (relative to the project folder) for a build kind.</summary>
        public static string OutputPath(Kind kind)
        {
            string version = PlayerSettings.bundleVersion;
            switch (kind)
            {
                case Kind.AndroidAppBundle:
                    return FieldEditorConstants.BuildRoot + "/Android/BeamRunner-" + version + "-" +
                           PlayerSettings.Android.bundleVersionCode + ".aab";
                case Kind.AndroidDevApk:
                    return FieldEditorConstants.BuildRoot + "/Android/BeamRunner-" + version + "-dev.apk";
                default:
                    return FieldEditorConstants.BuildRoot + "/iOS/Xcode";
            }
        }

        private static void ReportResult(Kind kind, BuildReport report, string location, bool interactive)
        {
            BuildSummary summary = report.summary;
            var sb = new StringBuilder();
            sb.AppendLine(Label(kind) + ": " + summary.result);
            sb.AppendLine("Output: " + Path.GetFullPath(location));
            if (summary.result == BuildResult.Succeeded)
            {
                sb.AppendLine("Size: " + FormatBytes(summary.totalSize) + "   Time: " +
                              summary.totalTime.ToString(@"hh\:mm\:ss"));
                if (kind == Kind.AndroidAppBundle && !PlayerSettings.Android.useCustomKeystore)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ Signed with the debug keystore: set a release keystore (Player Settings → " +
                                  "Publishing Settings) before uploading to Google Play.");
                }

                if (kind == Kind.IosXcode)
                {
                    sb.AppendLine();
                    sb.AppendLine("On a Mac: run `pod install` in that folder if the Pods folder is missing, then open " +
                                  "Unity-iPhone.xcworkspace (not the .xcodeproj) in Xcode, choose your team, then Product → Archive.");
                }
            }
            else
            {
                sb.AppendLine("Errors: " + summary.totalErrors + ", warnings: " + summary.totalWarnings +
                              " (see the Console).");
            }

            string text = sb.ToString().TrimEnd();
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[BeamRunner] " + text);
            }
            else
            {
                Debug.LogError("[BeamRunner] " + text);
            }

            if (!interactive || Application.isBatchMode)
            {
                return;
            }

            if (summary.result == BuildResult.Succeeded)
            {
                if (EditorUtility.DisplayDialog("BeamRunner — Build", text, "Show in folder", "OK"))
                {
                    EditorUtility.RevealInFinder(location);
                }
            }
            else
            {
                EditorUtility.DisplayDialog("BeamRunner — Build", text, "OK");
            }
        }

        private static void Fail(bool interactive, string message)
        {
            Debug.LogError("[BeamRunner] Build: " + message);
            if (interactive && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("BeamRunner — Build", message, "OK");
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1024UL * 1024UL)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            }

            return (bytes / 1024.0).ToString("0") + " KB";
        }
    }
}
