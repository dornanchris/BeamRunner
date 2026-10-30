using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// <c>BeamRunner/Validate Project</c>: read-only health check of everything the game needs at build time
    /// (scene, pipeline, shaders, fonts, ship model, monetization config, player settings). Findings go to the
    /// Console and a summary dialog. CI entry point: <see cref="ValidateBatch"/>.
    /// </summary>
    public static class FieldProjectValidator
    {
        [MenuItem(FieldEditorConstants.MenuRoot + "Validate Project", false, 40)]
        private static void ValidateMenu()
        {
            FieldValidationReport report = Run();
            report.LogToConsole();
            EditorUtility.DisplayDialog("BeamRunner — Validate Project", report.ToDialogText(18), "OK");
        }

        /// <summary>
        /// Batch-mode entry: <c>-batchmode -quit -executeMethod BeamRunner.EditorTools.FieldProjectValidator.ValidateBatch</c>.
        /// Exits with 1 when any error was found.
        /// </summary>
        public static void ValidateBatch()
        {
            FieldValidationReport report = Run();
            report.LogToConsole();
            EditorApplication.Exit(report.Errors > 0 ? 1 : 0);
        }

        /// <summary>Runs every check and returns the findings. Never throws.</summary>
        public static FieldValidationReport Run()
        {
            var report = new FieldValidationReport();
            var checks = new Action<FieldValidationReport>[]
            {
                CheckScene, CheckRendering, CheckShaders, CheckFonts, CheckMonetization, CheckPlayer,
                FieldPlatformFeatures.Check, // reminders (notifications), Play in-app review, crash reporting
                FieldOnlineValidation.Check, // Google Play Games / Game Center ids, Play Games setup, cloud save
            };
            foreach (Action<FieldValidationReport> check in checks)
            {
                try
                {
                    check(report);
                }
                catch (Exception e)
                {
                    report.Error(check.Method.Name + " threw " + e.GetType().Name + ": " + e.Message);
                }
            }

            return report;
        }

        // ------------------------------------------------------------------------------------------

        private static void CheckScene(FieldValidationReport r)
        {
            string path = FieldEditorConstants.MainScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                r.Error("Main scene missing at " + path + " (BeamRunner/Rebuild Main Scene).");
                return;
            }

            string text = File.ReadAllText(path);
            if (text.IndexOf(FieldEditorConstants.GameBootstrapScriptGuid, StringComparison.Ordinal) < 0)
            {
                r.Error("Main scene has no GameBootstrap component (BeamRunner/Rebuild Main Scene).");
            }
            else
            {
                r.Ok("Main scene contains GameBootstrap");
            }

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int firstEnabled = Array.FindIndex(scenes, s => s.enabled);
            if (firstEnabled < 0 || !string.Equals(scenes[firstEnabled].path, path, StringComparison.Ordinal))
            {
                r.Error("Main scene is not the first enabled scene in Build Settings (BeamRunner/Setup Project).");
            }
            else
            {
                r.Ok("Main scene is scene 0 in Build Settings");
            }

            int extra = 0;
            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.enabled && !string.Equals(s.path, path, StringComparison.Ordinal))
                {
                    extra++;
                }
            }

            if (extra > 0)
            {
                r.Warn(extra + " other enabled scene(s) in Build Settings will be included in builds.");
            }
        }

        private static void CheckRendering(FieldValidationReport r)
        {
            RenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline;
            if (FieldProjectSetup.IsUrp(rp))
            {
                r.Ok("URP is the default render pipeline (" + rp.name + ")");
            }
            else
            {
                r.Error("URP is not the default render pipeline" + (rp == null ? " (Built-in in use)" : " (" + rp.name + ")") +
                        ": no bloom/post-processing. Run BeamRunner/Setup Project.");
            }

            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                RenderPipelineAsset level = QualitySettings.GetRenderPipelineAssetAt(i);
                if (level != null && !FieldProjectSetup.IsUrp(level))
                {
                    r.Warn("Quality level '" + names[i] + "' overrides the pipeline with " + level.name + ".");
                }
            }

            if (PlayerSettings.colorSpace == ColorSpace.Linear)
            {
                r.Ok("Linear colour space");
            }
            else
            {
                r.Warn("Colour space is Gamma; the neon palette and bloom are tuned for Linear.");
            }
        }

        private static void CheckShaders(FieldValidationReport r)
        {
            string folder = FieldEditorConstants.ShaderFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                r.Error("Shader folder missing: " + folder);
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Shader", new[] { folder });
            if (guids.Length == 0)
            {
                r.Error("No shaders found in " + folder + ".");
            }

            int clean = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                {
                    r.Error("Shader could not be loaded: " + path);
                    continue;
                }

                if (ShaderUtil.ShaderHasError(shader))
                {
                    r.Error("Shader '" + shader.name + "' (" + Path.GetFileName(path) + ") has compile errors: " +
                            FirstShaderErrors(shader, 3));
                    continue;
                }

                if (!shader.isSupported)
                {
                    r.Warn("Shader '" + shader.name + "' is not supported by the current graphics API.");
                    continue;
                }

                clean++;
            }

            if (clean > 0)
            {
                r.Ok(clean + " shader(s) in " + folder + " import without errors");
            }

            foreach (string name in FieldEditorConstants.RequiredShaderNames)
            {
                if (Shader.Find(name) == null)
                {
                    r.Error("Shader '" + name + "' not found; the renderer falls back to built-in shaders.");
                }
            }
        }

        private static string FirstShaderErrors(Shader shader, int max)
        {
            var parts = new List<string>();
            foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(shader))
            {
                if (m.severity != ShaderCompilerMessageSeverity.Error)
                {
                    continue;
                }

                string where = string.IsNullOrEmpty(m.file) ? "" : Path.GetFileName(m.file) + ":" + m.line + " ";
                parts.Add(where + m.message);
                if (parts.Count >= max)
                {
                    break;
                }
            }

            return parts.Count == 0 ? "see the shader inspector" : string.Join(" | ", parts);
        }

        private static void CheckFonts(FieldValidationReport r)
        {
            int found = 0;
            foreach (string file in FieldEditorConstants.RequiredFonts)
            {
                string path = FieldEditorConstants.FontFolder + "/" + file;
                if (AssetDatabase.LoadAssetAtPath<Font>(path) == null)
                {
                    r.Error("Font missing: " + path + " (UI falls back to LegacyRuntime.ttf).");
                }
                else
                {
                    found++;
                }
            }

            if (found == FieldEditorConstants.RequiredFonts.Length)
            {
                r.Ok("Fonts present (" + string.Join(", ", FieldEditorConstants.RequiredFonts) + ")");
            }

            if (!File.Exists(FieldEditorConstants.FontFolder + "/OFL.txt"))
            {
                r.Warn("OFL.txt (font licence) is missing from " + FieldEditorConstants.FontFolder + ".");
            }
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>Settings asset written by the LevelPlay (Ads Mediation) package.</summary>
        private const string LevelPlayFolder = "Assets/LevelPlay";

        /// <summary>Where Unity unpacks registry packages (relative to the project folder).</summary>
        private const string PackageCacheFolder = "Library/PackageCache";

        /// <summary>Unity Ads' Android artifact / iOS pod as named in EDM4U descriptors.</summary>
        private static readonly System.Text.RegularExpressions.Regex UnityAdsDependency =
            new System.Text.RegularExpressions.Regex("unity-?ads", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private const string LevelPlayMediationSettingsPath = "Assets/LevelPlay/Resources/LevelPlayMediationSettings.asset";

        [Serializable]
        private sealed class MonetizationMirror
        {
            public string levelPlayAppKeyAndroid = "";
            public string levelPlayAppKeyIos = "";
            public string rewardedAdUnitAndroid = "";
            public string rewardedAdUnitIos = "";
            public string interstitialAdUnitAndroid = "";
            public string interstitialAdUnitIos = "";
            public bool testMode = true;
            public bool allowIncompleteReleaseBuild;
            public string privacyPolicyUrl = "";
            public string termsUrl = "";
            public string supportEmail = "";
            public string iosAppId = "";
            public ProductMirror[] products = Array.Empty<ProductMirror>();
        }

        [Serializable]
        private sealed class ProductMirror
        {
            public string id = "";
        }

        private static void CheckMonetization(FieldValidationReport r)
        {
            string path = FieldEditorConstants.MonetizationConfigPath;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null)
            {
                r.Warn("Monetization config missing (" + path + "): built-in defaults are used and ads stay off.");
                return;
            }

            MonetizationMirror config;
            try
            {
                config = JsonUtility.FromJson<MonetizationMirror>(asset.text);
            }
            catch (Exception e)
            {
                r.Error("monetization.json does not parse (" + e.Message + "); the game will use built-in defaults.");
                return;
            }

            if (config == null)
            {
                r.Error("monetization.json is empty.");
                return;
            }

            r.Ok("monetization.json parses");

            if (asset.text.IndexOf("\"androidGameId\"", StringComparison.Ordinal) >= 0 ||
                asset.text.IndexOf("\"iosGameId\"", StringComparison.Ordinal) >= 0)
            {
                r.Warn("monetization.json still has Unity Ads keys (androidGameId/iosGameId/placements); they are " +
                       "ignored — ads now use LevelPlay (levelPlayAppKey*, rewardedAdUnit*, interstitialAdUnit*).");
            }

            bool android = CheckLevelPlayPlatform(r, "Android", config.levelPlayAppKeyAndroid,
                config.rewardedAdUnitAndroid, config.interstitialAdUnitAndroid);
            bool ios = CheckLevelPlayPlatform(r, "iOS", config.levelPlayAppKeyIos, config.rewardedAdUnitIos,
                config.interstitialAdUnitIos);
            if (android || ios)
            {
                CheckLevelPlaySettings(r);
            }

            if (config.allowIncompleteReleaseBuild)
            {
                r.Warn(FieldMonetizationBuildGuard.OptOutKey + " is true in monetization.json: release builds may ship " +
                       "without ads or a privacy policy. Set it back to false before the store release.");
            }

            if (config.testMode)
            {
                r.Ok("testMode on: development builds open the LevelPlay Test Suite after init (release builds ignore it)");
            }

            if (IsPlaceholder(config.privacyPolicyUrl))
            {
                r.Warn("Privacy policy URL is a placeholder ('" + config.privacyPolicyUrl +
                       "'); both stores require a real one when ads/IAP are enabled.");
            }

            if (IsPlaceholder(config.termsUrl) || IsPlaceholder(config.supportEmail))
            {
                r.Warn("Terms URL / support e-mail are still placeholders.");
            }

            if (string.IsNullOrWhiteSpace(config.iosAppId))
            {
                r.Warn("iosAppId is empty: 'Rate' / store links on iOS are hidden until it is set.");
            }

            ProductMirror[] products = config.products ?? Array.Empty<ProductMirror>();
            if (products.Length == 0)
            {
                r.Warn("No IAP products in monetization.json (the built-in catalogue is used).");
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool productsOk = true;
            foreach (ProductMirror p in products)
            {
                string id = p == null ? "" : (p.id ?? "").Trim();
                if (id.Length == 0)
                {
                    r.Error("An IAP product in monetization.json has no id.");
                    productsOk = false;
                }
                else if (!seen.Add(id))
                {
                    r.Error("Duplicate IAP product id '" + id + "' in monetization.json.");
                    productsOk = false;
                }
            }

            if (productsOk)
            {
                r.Ok(products.Length + " IAP product id(s) valid — create them with identical ids in both stores");
            }
        }

        /// <summary>Returns true when LevelPlay is configured for the platform.</summary>
        private static bool CheckLevelPlayPlatform(FieldValidationReport r, string platform, string appKey,
            string rewardedUnit, string interstitialUnit)
        {
            bool hasKey = !string.IsNullOrWhiteSpace(appKey);
            bool hasRewarded = !string.IsNullOrWhiteSpace(rewardedUnit);
            bool hasInterstitial = !string.IsNullOrWhiteSpace(interstitialUnit);
            if (!hasKey)
            {
                r.Warn("LevelPlay app key for " + platform + " is empty in monetization.json: no ads on " + platform +
                       " (release builds are blocked until it is set; see FieldMonetizationBuildGuard).");
                return false;
            }

            if (!hasRewarded && !hasInterstitial)
            {
                r.Warn("LevelPlay app key for " + platform + " is set but both ad unit ids are empty: no ads on " +
                       platform + " (release builds are blocked until one is set).");
                return false;
            }

            if (!hasRewarded || !hasInterstitial)
            {
                r.Warn("LevelPlay " + platform + ": the " + (hasRewarded ? "interstitial" : "rewarded") +
                       " ad unit id is empty; that format is disabled on " + platform + ".");
            }
            else
            {
                r.Ok("LevelPlay " + platform + ": app key + rewarded + interstitial ad units set");
            }

            return true;
        }

        /// <summary>
        /// Reads the LevelPlay settings asset (serialized field names as used by the LevelPlay helper's setup
        /// checklist). Missing asset/fields are reported, never fatal.
        /// </summary>
        private static void CheckLevelPlaySettings(FieldValidationReport r)
        {
            var settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LevelPlayMediationSettingsPath);
            if (settings == null)
            {
                r.Warn("LevelPlay settings asset not found (" + LevelPlayMediationSettingsPath + "): open Ads Mediation ▸ " +
                       "Network Manager once, install the Unity Ads adapter and resolve native dependencies.");
                return;
            }

            CheckUnityAdsAdapter(r);

            var so = new SerializedObject(settings);
            SerializedProperty autoInit = so.FindProperty("EnableIronsourceSDKInitAPI");
            if (autoInit != null && autoInit.boolValue)
            {
                r.Warn("LevelPlay auto-initialisation (EnableIronsourceSDKInitAPI) is ON: the SDK may start before the " +
                       "consent dialog / ATT. Turn it off in the Ads Mediation settings; the game initialises LevelPlay itself.");
            }

            SerializedProperty adapterDebug = so.FindProperty("EnableAdapterDebug");
            SerializedProperty integrationHelper = so.FindProperty("EnableIntegrationHelper");
            if ((adapterDebug != null && adapterDebug.boolValue) || (integrationHelper != null && integrationHelper.boolValue))
            {
                r.Warn("LevelPlay EnableAdapterDebug / EnableIntegrationHelper is on; turn both off for the store release.");
            }

            SerializedProperty adId = so.FindProperty("DeclareAD_IDPermission");
            if (adId != null && !adId.boolValue)
            {
                r.Warn("LevelPlay DeclareAD_IDPermission is off: make sure com.google.android.gms.permission.AD_ID is " +
                       "merged (play-services-ads-identifier after the Android Resolver) or turn it on.");
            }
        }

        /// <summary>
        /// Unity Ads must be among LevelPlay's native dependencies, or LevelPlay cannot fill from it (since the
        /// ironSource Ads network stopped serving direct demand on 30 Apr 2026 it is the main demand source). Recent
        /// LevelPlay SDKs bundle it ("LevelPlay SDK" row of the Network Manager), older ones install it as an adapter,
        /// so this looks for the Unity Ads artifact / pod (<c>unity-ads</c>, <c>unityads-adapter</c>,
        /// <c>…UnityAds…</c>) inside the External Dependency Manager descriptors under Assets/LevelPlay and the
        /// LevelPlay package, not for a file name.
        /// </summary>
        private static void CheckUnityAdsAdapter(FieldValidationReport r)
        {
            var roots = new List<string>();
            if (Directory.Exists(LevelPlayFolder))
            {
                roots.Add(LevelPlayFolder);
            }

            if (Directory.Exists(PackageCacheFolder))
            {
                roots.AddRange(Directory.GetDirectories(PackageCacheFolder, "com.unity.services.levelplay*"));
            }

            foreach (string root in roots)
            {
                foreach (string file in Directory.GetFiles(root, "*Dependencies.xml", SearchOption.AllDirectories))
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (UnityAdsDependency.IsMatch(text))
                    {
                        r.Ok("Unity Ads is among LevelPlay's native dependencies (" + Path.GetFileName(file) + ")");
                        return;
                    }
                }
            }

            r.Warn("Unity Ads not found in LevelPlay's native dependencies (no *Dependencies.xml under " + LevelPlayFolder +
                   " or the LevelPlay package names it): LevelPlay may not fill from Unity Ads. Open Ads Mediation ▸ " +
                   "Network Manager and install/update Unity Ads (LevelPlay SDK section or Networks list), then commit " +
                   "the files it writes under " + LevelPlayFolder + ".");
        }

        /// <summary>Empty or still an example.com placeholder.</summary>
        internal static bool IsPlaceholder(string value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   value.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------------------------------

        private static void CheckPlayer(FieldValidationReport r)
        {
            NamedBuildTarget[] targets = { NamedBuildTarget.Android, NamedBuildTarget.iOS };
            foreach (NamedBuildTarget t in targets)
            {
                string id = PlayerSettings.GetApplicationIdentifier(t);
                if (id != FieldEditorConstants.BundleId)
                {
                    r.Warn(t.TargetName + " bundle id is '" + id + "', expected '" + FieldEditorConstants.BundleId + "'.");
                }
            }

            if (PlayerSettings.productName != FieldEditorConstants.ProductName ||
                PlayerSettings.companyName != FieldEditorConstants.CompanyName)
            {
                r.Warn("Company/product name differ from '" + FieldEditorConstants.CompanyName + "' / '" +
                       FieldEditorConstants.ProductName + "'.");
            }
            else
            {
                r.Ok("Identity: " + FieldEditorConstants.ProductName + " (" + FieldEditorConstants.BundleId + ") v" +
                     PlayerSettings.bundleVersion);
            }

            bool portrait = PlayerSettings.defaultInterfaceOrientation == UIOrientation.Portrait ||
                            (PlayerSettings.defaultInterfaceOrientation == UIOrientation.AutoRotation &&
                             PlayerSettings.allowedAutorotateToPortrait &&
                             !PlayerSettings.allowedAutorotateToLandscapeLeft &&
                             !PlayerSettings.allowedAutorotateToLandscapeRight);
            if (portrait)
            {
                r.Ok("Portrait orientation");
            }
            else
            {
                r.Warn("Orientation is not portrait-only; the UI is laid out for portrait.");
            }

            Texture2D[] icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (icons == null || icons.Length == 0 || icons[0] == null)
            {
                r.Warn("No default app icon set (BeamRunner/Setup Project assigns " + FieldEditorConstants.AppIconPath + ").");
            }
            else
            {
                r.Ok("Default app icon: " + icons[0].name);
            }

            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP ||
                (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
            {
                r.Error("Android must use IL2CPP with ARM64 (Google Play 64-bit requirement). Run Setup Project.");
            }
            else
            {
                r.Ok("Android: IL2CPP + ARM64");
            }

            if (!PlayerSettings.Android.useCustomKeystore)
            {
                r.Warn("Android release keystore not configured: builds are debug-signed and cannot be uploaded to Google Play.");
            }

            CheckPrivacyManifest(r);
        }

        /// <summary>The iOS privacy manifest must exist and be imported for iOS so Unity merges it into the build.</summary>
        private static void CheckPrivacyManifest(FieldValidationReport r)
        {
            string path = FieldIosPrivacyManifest.SourceAssetPath;
            if (!File.Exists(path))
            {
                r.Error("iOS privacy manifest missing: " + path + " (required-reason API declarations for App Store uploads).");
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null || !importer.GetCompatibleWithPlatform(BuildTarget.iOS))
            {
                r.Error(path + " is not enabled for iOS in its Plugin Inspector: Unity will not merge it into the Xcode project.");
            }
            else
            {
                r.Ok("iOS privacy manifest: " + path);
            }
        }
    }
}
