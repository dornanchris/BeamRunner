using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// <c>BeamRunner/Reset Save Data</c> (deletes the profile file, its backups and all PlayerPrefs after a
    /// confirmation) and <c>BeamRunner/Open Save Folder</c>.
    /// </summary>
    public static class FieldSaveDataTools
    {
        [MenuItem(FieldEditorConstants.MenuRoot + "Reset Save Data", false, 60)]
        private static void ResetSaveData()
        {
            string folder = Application.persistentDataPath;
            if (!EditorUtility.DisplayDialog("BeamRunner — Reset Save Data",
                    "Delete the Editor's saved profile (" + FieldEditorConstants.SaveFileName + "*) in\n" + folder +
                    "\n\nand ALL PlayerPrefs for this project (settings, consent, ad pacing)?\n\nThis cannot be undone.",
                    "Delete", "Cancel"))
            {
                return;
            }

            int deleted = 0;
            int failed = 0;
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder, FieldEditorConstants.SaveFileName + "*"))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Debug.LogWarning("[BeamRunner] Could not delete " + file + ": " + e.Message);
                    }
                }
            }

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            string message = "Deleted " + deleted + " save file(s) and all PlayerPrefs." +
                             (failed > 0 ? "\n" + failed + " file(s) could not be deleted (see Console)." : "") +
                             (EditorApplication.isPlaying ? "\n\nThe running game still holds its profile in memory; " +
                                                            "stop Play Mode before playing again." : "");
            Debug.Log("[BeamRunner] " + message);
            EditorUtility.DisplayDialog("BeamRunner", message, "OK");
        }

        [MenuItem(FieldEditorConstants.MenuRoot + "Open Save Folder", false, 61)]
        private static void OpenSaveFolder()
        {
            string folder = Application.persistentDataPath;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string profile = Path.Combine(folder, FieldEditorConstants.SaveFileName);
            if (File.Exists(profile))
            {
                // Opens the save folder with the profile selected.
                EditorUtility.RevealInFinder(profile);
            }
            else
            {
                // RevealInFinder on a folder would open its parent; open the folder itself instead.
                Application.OpenURL(new Uri(folder).AbsoluteUri);
            }
        }
    }
}
