using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// <c>BeamRunner/Setup Project</c>: an idempotent fixer that brings Player/Graphics/Build settings to the
    /// values the game ships with (main scene, URP, Linear colour, identity, portrait, icons, Android IL2CPP/ARM64).
    /// Running it on an already-configured project changes nothing.
    /// </summary>
    public static class FieldProjectSetup
    {
        private const string MenuPath = FieldEditorConstants.MenuRoot + "Setup Project";

        [MenuItem(MenuPath, false, 1)]
        private static void SetupProjectMenu()
        {
            var changes = new List<string>();
            var notes = new List<string>();
            Apply(changes, notes);

            var sb = new StringBuilder();
            if (changes.Count == 0)
            {
                sb.AppendLine("Everything was already configured. No changes made.");
            }
            else
            {
                sb.AppendLine(changes.Count + " setting(s) updated:");
                foreach (string c in changes)
                {
                    sb.AppendLine("• " + c);
                }
            }

            if (notes.Count > 0)
            {
                sb.AppendLine();
                foreach (string n in notes)
                {
                    sb.AppendLine("⚠ " + n);
                }
            }

            string report = sb.ToString().TrimEnd();
            Debug.Log("[BeamRunner] Setup Project\n" + report);
            EditorUtility.DisplayDialog("BeamRunner — Setup Project", report, "OK");
        }

        /// <summary>
        /// Applies every setting. <paramref name="changes"/> receives a line per modified setting,
        /// <paramref name="notes"/> receives things that could not be fixed automatically.
        /// </summary>
        public static void Apply(List<string> changes, List<string> notes)
        {
            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            if (notes == null)
            {
                throw new ArgumentNullException(nameof(notes));
            }

            EnsureMainSceneInBuild(changes, notes);
            EnsureUrp(changes, notes);
            EnsureColorSpace(changes);
            EnsureIdentity(changes);
            EnsureOrientation(changes);
            EnsureIcons(changes, notes);
            EnsureAndroid(changes);
            EnsureIos(changes);
            EnsureSplash(changes);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------
        // Build settings
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Makes the main scene the first, enabled entry in Build Settings and drops entries whose scene file
        /// no longer exists. Returns true if Build Settings changed.
        /// </summary>
        public static bool EnsureMainSceneFirstInBuild(out string problem)
        {
            problem = null;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(FieldEditorConstants.MainScenePath) == null)
            {
                problem = "Main scene not found at " + FieldEditorConstants.MainScenePath +
                          " (use BeamRunner/Rebuild Main Scene).";
                return false;
            }

            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            var scenes = new List<EditorBuildSettingsScene>(current.Length + 1);
            bool dropped = false;
            foreach (EditorBuildSettingsScene s in current)
            {
                if (string.IsNullOrEmpty(s.path) || !File.Exists(s.path))
                {
                    dropped = true;
                    continue;
                }

                if (string.Equals(s.path, FieldEditorConstants.MainScenePath, StringComparison.Ordinal))
                {
                    continue;
                }

                scenes.Add(s);
            }

            bool alreadyFirst = current.Length > 0 &&
                                string.Equals(current[0].path, FieldEditorConstants.MainScenePath, StringComparison.Ordinal) &&
                                current[0].enabled;
            if (alreadyFirst && !dropped && scenes.Count == current.Length - 1)
            {
                return false;
            }

            scenes.Insert(0, new EditorBuildSettingsScene(FieldEditorConstants.MainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }

        private static void EnsureMainSceneInBuild(List<string> changes, List<string> notes)
        {
            if (EnsureMainSceneFirstInBuild(out string problem))
            {
                changes.Add("Build Settings: " + FieldEditorConstants.MainScenePath + " is scene 0");
            }

            if (problem != null)
            {
                notes.Add(problem);
            }

            int others = EditorBuildSettings.scenes.Length - 1;
            if (others > 0)
            {
                notes.Add(others + " other scene(s) are listed in Build Settings; the game only needs Main.");
            }
        }

        // ------------------------------------------------------------------------------------------
        // Graphics
        // ------------------------------------------------------------------------------------------

        /// <summary>True when <paramref name="asset"/> is a URP pipeline asset (checked by type name).</summary>
        public static bool IsUrp(RenderPipelineAsset asset)
        {
            return asset != null && asset.GetType().Name == FieldEditorConstants.UrpAssetTypeName;
        }

        private static void EnsureUrp(List<string> changes, List<string> notes)
        {
            var urp = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(FieldEditorConstants.UrpAssetPath);
            if (urp == null)
            {
                notes.Add("URP asset not found at " + FieldEditorConstants.UrpAssetPath +
                          "; the game falls back to the Built-in pipeline (no bloom).");
                return;
            }

            if (GraphicsSettings.defaultRenderPipeline != urp)
            {
                GraphicsSettings.defaultRenderPipeline = urp;
                changes.Add("Graphics: default render pipeline = " + urp.name);
            }

            // A quality level may override the pipeline; clear overrides that are not URP.
            int levels = QualitySettings.names.Length;
            int active = QualitySettings.GetQualityLevel();
            bool touched = false;
            for (int i = 0; i < levels; i++)
            {
                RenderPipelineAsset levelAsset = QualitySettings.GetRenderPipelineAssetAt(i);
                if (levelAsset == null || IsUrp(levelAsset))
                {
                    continue;
                }

                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = null;
                touched = true;
                changes.Add("Quality level '" + QualitySettings.names[i] + "': removed non-URP pipeline override");
            }

            if (touched)
            {
                QualitySettings.SetQualityLevel(active, false);
            }
        }

        private static void EnsureColorSpace(List<string> changes)
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                changes.Add("Player: colour space = Linear");
            }
        }

        // ------------------------------------------------------------------------------------------
        // Identity
        // ------------------------------------------------------------------------------------------

        private static void EnsureIdentity(List<string> changes)
        {
            if (PlayerSettings.companyName != FieldEditorConstants.CompanyName)
            {
                PlayerSettings.companyName = FieldEditorConstants.CompanyName;
                changes.Add("Player: company = " + FieldEditorConstants.CompanyName);
            }

            if (PlayerSettings.productName != FieldEditorConstants.ProductName)
            {
                PlayerSettings.productName = FieldEditorConstants.ProductName;
                changes.Add("Player: product = " + FieldEditorConstants.ProductName);
            }

            // Without this flag the Player Settings inspector regenerates the id from company + product.
            if (SetPlayerSettingsBool("overrideDefaultApplicationIdentifier", true))
            {
                changes.Add("Player: 'Override Default Package Name / Bundle Identifier' enabled");
            }

            NamedBuildTarget[] targets = { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone };
            foreach (NamedBuildTarget target in targets)
            {
                if (PlayerSettings.GetApplicationIdentifier(target) != FieldEditorConstants.BundleId)
                {
                    PlayerSettings.SetApplicationIdentifier(target, FieldEditorConstants.BundleId);
                    changes.Add("Player: " + target.TargetName + " bundle id = " + FieldEditorConstants.BundleId);
                }
            }

            // Versions: only replace Unity's untouched defaults so bumped release numbers are never reset.
            string version = PlayerSettings.bundleVersion;
            if (string.IsNullOrEmpty(version) || version == "0.1" || version == "1.0")
            {
                PlayerSettings.bundleVersion = FieldEditorConstants.InitialVersion;
                changes.Add("Player: version = " + FieldEditorConstants.InitialVersion);
            }

            if (PlayerSettings.Android.bundleVersionCode < 1)
            {
                PlayerSettings.Android.bundleVersionCode = 1;
                changes.Add("Android: bundle version code = 1");
            }

            string iosBuild = PlayerSettings.iOS.buildNumber;
            if (string.IsNullOrEmpty(iosBuild) || iosBuild == "0")
            {
                PlayerSettings.iOS.buildNumber = "1";
                changes.Add("iOS: build number = 1");
            }
        }

        /// <summary>
        /// Sets a serialized PlayerSettings bool that has no public API. Returns true if it changed.
        /// </summary>
        private static bool SetPlayerSettingsBool(string propertyName, bool value)
        {
            Object[] objects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            foreach (Object o in objects)
            {
                if (!(o is PlayerSettings))
                {
                    continue;
                }

                var so = new SerializedObject(o);
                SerializedProperty prop = so.FindProperty(propertyName);
                if (prop == null || prop.propertyType != SerializedPropertyType.Boolean || prop.boolValue == value)
                {
                    return false;
                }

                prop.boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------------------------------
        // Orientation, icons, splash
        // ------------------------------------------------------------------------------------------

        private static void EnsureOrientation(List<string> changes)
        {
            bool changed = false;
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                changed = true;
            }

            if (!PlayerSettings.allowedAutorotateToPortrait)
            {
                PlayerSettings.allowedAutorotateToPortrait = true;
                changed = true;
            }

            if (PlayerSettings.allowedAutorotateToPortraitUpsideDown)
            {
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                changed = true;
            }

            if (PlayerSettings.allowedAutorotateToLandscapeLeft)
            {
                PlayerSettings.allowedAutorotateToLandscapeLeft = false;
                changed = true;
            }

            if (PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                PlayerSettings.allowedAutorotateToLandscapeRight = false;
                changed = true;
            }

            if (changed)
            {
                changes.Add("Player: orientation = Portrait only");
            }
        }

        private static void EnsureIcons(List<string> changes, List<string> notes)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(FieldEditorConstants.AppIconPath);
            if (icon == null)
            {
                notes.Add("App icon not found at " + FieldEditorConstants.AppIconPath + ".");
                return;
            }

            Texture2D[] current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (current == null || current.Length == 0 || current[0] != icon)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                changes.Add("Player: default icon = " + Path.GetFileName(FieldEditorConstants.AppIconPath));
            }

            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(FieldEditorConstants.AdaptiveIconBackgroundPath);
            var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(FieldEditorConstants.AdaptiveIconForegroundPath);
            if (background == null || foreground == null)
            {
                notes.Add("Android adaptive icon layers missing; Android uses the legacy (square) icon.");
                return;
            }

            PlatformIconKind adaptive = FindIconKind(NamedBuildTarget.Android, "Adaptive");
            if (adaptive == null)
            {
                notes.Add("Android Build Support is not installed, so adaptive icons could not be verified " +
                          "(they are pre-set in ProjectSettings.asset).");
                return;
            }

            PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, adaptive);
            bool changed = false;
            foreach (PlatformIcon platformIcon in icons)
            {
                Texture2D[] layers = platformIcon.GetTextures();
                if (layers == null || layers.Length < 2 || layers[0] != background || layers[1] != foreground)
                {
                    platformIcon.SetTextures(background, foreground);
                    changed = true;
                }
            }

            if (changed)
            {
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, adaptive, icons);
                changes.Add("Android: adaptive icon layers (background + foreground)");
            }
        }

        private static PlatformIconKind FindIconKind(NamedBuildTarget target, string kindName)
        {
            PlatformIconKind[] kinds = PlayerSettings.GetSupportedIconKinds(target);
            if (kinds == null)
            {
                return null;
            }

            foreach (PlatformIconKind kind in kinds)
            {
                if (kind != null && string.Equals(kind.ToString(), kindName, StringComparison.OrdinalIgnoreCase))
                {
                    return kind;
                }
            }

            return null;
        }

        private static void EnsureSplash(List<string> changes)
        {
            // Unity 6 allows disabling the Unity splash on every licence tier.
            if (PlayerSettings.SplashScreen.show)
            {
                PlayerSettings.SplashScreen.show = false;
                changes.Add("Player: Unity splash screen disabled");
            }

            var dark = new Color(0.03137255f, 0.011764706f, 0.07058824f, 1f);
            if (!Approximately(PlayerSettings.SplashScreen.backgroundColor, dark))
            {
                PlayerSettings.SplashScreen.backgroundColor = dark;
                changes.Add("Player: splash/launch background = deep purple");
            }
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f &&
                   Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;
        }

        // ------------------------------------------------------------------------------------------
        // Platforms
        // ------------------------------------------------------------------------------------------

        private static void EnsureAndroid(List<string> changes)
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                changes.Add("Android: scripting backend = IL2CPP");
            }

            AndroidArchitecture arch = PlayerSettings.Android.targetArchitectures;
            if ((arch & AndroidArchitecture.ARM64) == 0)
            {
                PlayerSettings.Android.targetArchitectures = arch | AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
                changes.Add("Android: target architectures include ARM64 (+ ARMv7)");
            }

            // Google Play: new apps and updates must target Android 16 (API 36) since 31 Aug 2026. Pin it rather
            // than "Automatic (highest installed)" so a machine with an older SDK can't silently build an
            // unpublishable bundle. (Cast: the enum member name is version-specific.)
            const int RequiredTargetApi = 36;
            if ((int)PlayerSettings.Android.targetSdkVersion != RequiredTargetApi)
            {
                PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)RequiredTargetApi;
                changes.Add("Android: target API level = " + RequiredTargetApi + " (Google Play requirement)");
            }

            // Unity 6.3 supports API 23+, but the Google Play Games plugin 2.2.x (play-services-games-v2 22.x; its bridge
            // AAR declares minSdkVersion 24) needs API 24 (Android 7.0): below it the Gradle manifest merge fails.
            const int MinimumApi = 24;
            if ((int)PlayerSettings.Android.minSdkVersion < MinimumApi)
            {
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinimumApi;
                changes.Add("Android: minimum API level = " + MinimumApi);
            }

            // Android 16 with target API 36 no longer dispatches KEYCODE_BACK: back arrives only through
            // OnBackInvokedCallback, which Unity uses (and forwards to the game as the Escape key) only when
            // predictive back support is on. Without it the back button would neither pause runs nor close menus.
            if (!PlayerSettings.Android.predictiveBackSupport)
            {
                PlayerSettings.Android.predictiveBackSupport = true;
                changes.Add("Android: predictive back support on (back button on Android 16)");
            }

            // Portrait-only phone game: don't advertise Android TV support.
            if (PlayerSettings.Android.androidTVCompatibility)
            {
                PlayerSettings.Android.androidTVCompatibility = false;
                changes.Add("Android: Android TV compatibility off");
            }
        }

        private static void EnsureIos(List<string> changes)
        {
            // Unity 6.3 supports iOS 15+; builds must be made with Xcode 26 / the iOS 26 SDK (App Store rule since
            // 28 Apr 2026) — that's an Xcode requirement, the deployment target can stay at 15.0.
            const string MinimumIos = "15.0";
            System.Version current;
            if (!System.Version.TryParse(PlayerSettings.iOS.targetOSVersionString, out current) ||
                current < new System.Version(MinimumIos))
            {
                PlayerSettings.iOS.targetOSVersionString = MinimumIos;
                changes.Add("iOS: minimum iOS version = " + MinimumIos);
            }

            if (!PlayerSettings.statusBarHidden)
            {
                PlayerSettings.statusBarHidden = true;
                changes.Add("iOS: status bar hidden");
            }
        }
    }
}
