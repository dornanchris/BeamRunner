#if UNITY_IOS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
#endif

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// iOS post-build check for the game's privacy manifest (<see cref="SourceAssetPath"/>). Unity merges the
    /// <c>PrivacyInfo.xcprivacy</c> files of <c>Assets/Plugins</c> (iOS-enabled) and packages into
    /// <c>UnityFramework/PrivacyInfo.xcprivacy</c>. This step verifies that every required-reason declaration of the
    /// game's manifest ended up in the UnityFramework or the app-level manifest of the generated Xcode project; if
    /// not (e.g. the file's iOS import setting was switched off), it adds the game's manifest to the main app target
    /// so the upload still carries the declarations, and says so in the Console.
    /// </summary>
    public static class FieldIosPrivacyManifest
    {
        /// <summary>The game's own privacy manifest (project-relative).</summary>
        public const string SourceAssetPath = "Assets/Plugins/iOS/PrivacyInfo.xcprivacy";

        /// <summary>File name Apple looks for at the root of each bundle.</summary>
        public const string FileName = "PrivacyInfo.xcprivacy";

        /// <summary>Runs after Unity's own post-processing and <see cref="FieldIosTrackingPlist"/>.</summary>
        public const int CallbackOrder = 110;

#if UNITY_IOS
        private const string Tag = "[BeamRunner] iOS privacy manifest: ";

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
                if (!File.Exists(SourceAssetPath))
                {
                    Debug.LogError(Tag + SourceAssetPath + " is missing; the App Store may reject the upload for " +
                                   "undeclared required-reason APIs (PlayerPrefs, file timestamps, Stopwatch).");
                    return;
                }

                HashSet<string> required = ReadDeclarations(File.ReadAllText(SourceAssetPath));
                if (required == null)
                {
                    Debug.LogError(Tag + SourceAssetPath + " could not be parsed.");
                    return;
                }

                var present = new HashSet<string>(StringComparer.Ordinal);
                string[] bundleManifests =
                {
                    Path.Combine(pathToBuiltProject, "UnityFramework", FileName),
                    Path.Combine(pathToBuiltProject, FileName),
                };
                foreach (string manifest in bundleManifests)
                {
                    if (File.Exists(manifest))
                    {
                        HashSet<string> found = ReadDeclarations(File.ReadAllText(manifest));
                        if (found != null)
                        {
                            present.UnionWith(found);
                        }
                    }
                }

                var missing = new List<string>();
                foreach (string entry in required)
                {
                    if (!present.Contains(entry))
                    {
                        missing.Add(entry);
                    }
                }

                if (missing.Count == 0)
                {
                    Debug.Log(Tag + "all " + required.Count + " declarations are in the Xcode project.");
                    return;
                }

                string appManifest = Path.Combine(pathToBuiltProject, FileName);
                string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
                var project = new PBXProject();
                project.ReadFromFile(projectPath);
                if (File.Exists(appManifest) || project.ContainsFileByProjectPath(FileName))
                {
                    Debug.LogWarning(Tag + "the app already has a " + FileName + " that lacks " +
                                     string.Join(", ", missing) + " - add them to it before uploading.");
                    return;
                }

                File.Copy(SourceAssetPath, appManifest);
                string fileGuid = project.AddFile(FileName, FileName, PBXSourceTree.Source);
                project.AddFileToBuild(project.GetUnityMainTargetGuid(), fileGuid);
                project.WriteToFile(projectPath);
                Debug.LogWarning(Tag + "Unity did not merge " + SourceAssetPath + " (missing " +
                                 string.Join(", ", missing) + "); added it to the app target instead. Check that its " +
                                 "Plugin Inspector has iOS enabled.");
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + "check failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>"Category/Reason" pairs under <c>NSPrivacyAccessedAPITypes</c>, or null if unreadable.</summary>
        private static HashSet<string> ReadDeclarations(string xml)
        {
            try
            {
                var document = new PlistDocument();
                document.ReadFromString(xml);
                if (document.root == null)
                {
                    return null;
                }

                var result = new HashSet<string>(StringComparer.Ordinal);
                if (!document.root.values.TryGetValue("NSPrivacyAccessedAPITypes", out PlistElement types))
                {
                    return result;
                }

                foreach (PlistElement item in types.AsArray().values)
                {
                    PlistElementDict entry = item.AsDict();
                    if (!entry.values.TryGetValue("NSPrivacyAccessedAPIType", out PlistElement category) ||
                        !entry.values.TryGetValue("NSPrivacyAccessedAPITypeReasons", out PlistElement reasons))
                    {
                        continue;
                    }

                    foreach (PlistElement reason in reasons.AsArray().values)
                    {
                        result.Add(category.AsString() + "/" + reason.AsString());
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }
#endif
    }
}
