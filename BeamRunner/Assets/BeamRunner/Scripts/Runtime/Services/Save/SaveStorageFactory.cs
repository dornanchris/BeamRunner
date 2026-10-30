using System;
using BeamRunner.Core.Meta;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif

namespace BeamRunner.Services
{
    /// <summary>Picks the right <see cref="ISaveStorage"/> for the running platform.</summary>
    public static class SaveStorageFactory
    {
        /// <summary>
        /// WebGL → <see cref="PlayerPrefsSaveStorage"/>; everything else → <see cref="FileSaveStorage"/> in
        /// <c>persistentDataPath</c>. <paramref name="validator"/> (optional) should return true only for
        /// documents that deserialize into a usable profile; it lets the storage fall back to the backup copy
        /// when the main copy is syntactically valid but semantically broken. Documents are signed
        /// (<see cref="SaveEnvelope"/> with <see cref="SaveIntegrityKey"/>); the validator judges the profile inside.
        /// </summary>
        public static ISaveStorage Create(Func<string, bool> validator = null)
        {
            byte[] key = SaveIntegrityKey.Get();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Automated tests keep the profile in their own temp folder: the player's real save is never touched.
            string testDirectory = TestLaunchOptions.SaveDirectory;
            if (!string.IsNullOrEmpty(testDirectory))
            {
                ServiceLog.Info("Save: test launch options, profile in " + testDirectory);
                return new FileSaveStorage(testDirectory, FileSaveStorage.DefaultFileName, validator, key);
            }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            return new PlayerPrefsSaveStorage(PlayerPrefsSaveStorage.DefaultKey, validator, key);
#else
            return new FileSaveStorage(FileStorageDirectory(), FileSaveStorage.DefaultFileName, validator, key);
#endif
        }

        private static string FileStorageDirectory()
        {
            try
            {
                return UnityEngine.Application.persistentDataPath;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("persistentDataPath unavailable", e);
                return ".";
            }
        }
    }
}
