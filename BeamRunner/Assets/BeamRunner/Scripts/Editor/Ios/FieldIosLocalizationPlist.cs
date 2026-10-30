#if UNITY_IOS
using System;
using System.IO;
using System.Text;
using BeamRunner.Core.Localization;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
#endif

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// iOS post-build step for the game's languages (Core/Localization/Languages.cs):
    /// <list type="bullet">
    /// <item><c>CFBundleDevelopmentRegion</c> = en and <c>CFBundleLocalizations</c> = every shipped language, so iOS
    /// knows the app is localized and shows its own texts (purchase sheet, permission prompts, Game Center banners) in
    /// the player's language instead of English.</item>
    /// <item><c>&lt;lang&gt;.lproj/InfoPlist.strings</c> with the translated App Tracking Transparency prompt
    /// (<c>NSUserTrackingUsageDescription</c>, string key <c>ios.tracking_usage</c> of the string tables), added to the
    /// app target as folder references. Info.plist itself keeps the English text from
    /// <see cref="FieldIosTrackingPlist"/>.</item>
    /// </list>
    /// A language whose InfoPlist.strings another plugin already wrote (or whose .lproj is already in the Xcode project) is
    /// left alone apart from appending the tracking text when that file lacks it.
    /// </summary>
    public static class FieldIosLocalizationPlist
    {
        /// <summary>String-table key of the ATT prompt text (checked by tools/check-strings.py like every key).</summary>
        public const string TrackingTextKey = "ios.tracking_usage";

        /// <summary>Project folder of the string tables (Resources/BeamRunner/Localization).</summary>
        public const string TableFolder = "Assets/BeamRunner/Resources/BeamRunner/Localization";

        /// <summary>After <see cref="FieldIosTrackingPlist"/>, which writes the English prompt text.</summary>
        public const int CallbackOrder = FieldIosTrackingPlist.CallbackOrder + 1;

        /// <summary>The .lproj name of a language code (every shipped code is already an Apple locale id).</summary>
        public static string LprojName(string code)
        {
            return code;
        }

#if UNITY_IOS
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
                WriteInfoPlist(pathToBuiltProject);
                WriteInfoPlistStrings(pathToBuiltProject);
            }
            catch (Exception e)
            {
                Debug.LogError("[BeamRunner] iOS: writing the localization settings failed: " + e.Message);
            }
        }

        private static void WriteInfoPlist(string buildPath)
        {
            string plistPath = Path.Combine(buildPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogError("[BeamRunner] iOS: Info.plist not found at " + plistPath + "; CFBundleLocalizations not added.");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromString(File.ReadAllText(plistPath));
            PlistElementDict root = plist.root;
            if (root == null)
            {
                Debug.LogError("[BeamRunner] iOS: Info.plist could not be parsed; CFBundleLocalizations not added.");
                return;
            }

            root.SetString("CFBundleDevelopmentRegion", LprojName(Languages.EnglishCode));
            PlistElementArray list = root.CreateArray("CFBundleLocalizations");
            foreach (LanguageInfo language in Languages.Shipped)
            {
                list.AddString(LprojName(language.Code));
            }

            File.WriteAllText(plistPath, plist.WriteToString());
            Debug.Log("[BeamRunner] iOS: CFBundleLocalizations = " + Languages.Shipped.Count + " languages.");
        }

        private static void WriteInfoPlistStrings(string buildPath)
        {
            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            if (!File.Exists(projectPath))
            {
                Debug.LogError("[BeamRunner] iOS: Xcode project not found at " + projectPath + "; InfoPlist.strings not added.");
                return;
            }

            var project = new PBXProject();
            project.ReadFromString(File.ReadAllText(projectPath));
            string mainTarget = project.GetUnityMainTargetGuid();
            var encoding = new UTF8Encoding(false);
            int added = 0;
            foreach (LanguageInfo language in Languages.Shipped)
            {
                string text = TrackingText(language.Code);
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogWarning("[BeamRunner] iOS: no " + TrackingTextKey + " for " + language.Code + "; iOS shows the English ATT text.");
                    continue;
                }

                string folder = LprojName(language.Code) + ".lproj";
                string file = Path.Combine(buildPath, folder, "InfoPlist.strings");
                bool inProject = project.FindFileGuidByProjectPath(folder) != null ||
                                 project.FindFileGuidByProjectPath(folder + "/InfoPlist.strings") != null;
                string line = StringsEntry(FieldIosTrackingPlist.UsageDescriptionKey, text);
                if (File.Exists(file))
                {
                    string existing = File.ReadAllText(file);
                    if (existing.IndexOf("\"" + FieldIosTrackingPlist.UsageDescriptionKey + "\"", StringComparison.Ordinal) < 0)
                    {
                        File.WriteAllText(file, existing.TrimEnd() + "\n" + line, encoding);
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllText(file, line, encoding);
                }

                if (!inProject)
                {
                    string guid = project.AddFolderReference(folder, folder);
                    project.AddFileToBuild(mainTarget, guid);
                    added++;
                }
            }

            File.WriteAllText(projectPath, project.WriteToString());
            Debug.Log("[BeamRunner] iOS: InfoPlist.strings written (" + added + " .lproj folder(s) added to the app target).");
        }

        /// <summary>The ATT text of <paramref name="code"/> from its string table, or null.</summary>
        private static string TrackingText(string code)
        {
            string path = Path.Combine(TableFolder, code + ".json");
            if (!File.Exists(path))
            {
                return null;
            }

            string error;
            StringTable table = StringTable.TryFromJson(code, File.ReadAllText(path), out error);
            string text;
            if (table == null || !table.TryGet(TrackingTextKey, out text))
            {
                return null;
            }

            return text;
        }

        /// <summary>One <c>"key" = "value";</c> line of a .strings file (quotes, backslashes and newlines escaped).</summary>
        private static string StringsEntry(string key, string value)
        {
            var sb = new StringBuilder(value.Length + key.Length + 16);
            sb.Append('"').Append(key).Append("\" = \"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            sb.Append("\";\n");
            return sb.ToString();
        }
#endif
    }
}
