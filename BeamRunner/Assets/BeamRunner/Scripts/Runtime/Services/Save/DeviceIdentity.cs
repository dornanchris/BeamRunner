using System;
using System.IO;
using BeamRunner.Core.Meta;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif

namespace BeamRunner.Services
{
    /// <summary>
    /// Keeps <see cref="WalletLedger.DeviceId"/> (the key of this device's gem counters) unique per device. The id is
    /// stored in the profile and mirrored in a small file in <see cref="Application.temporaryCachePath"/>, which device
    /// backups, iCloud/Google restores and device-to-device transfers do not copy: when the profile arrives on a device
    /// without that file (a restored or copied save), the device gets a fresh id and the old entry stays frozen in the
    /// ledger — the balance is unchanged and two devices never share (and overwrite) one counter. A spurious rotation
    /// (the OS purged the cache) is harmless for the same reason. No hardware identifiers are used.
    /// </summary>
    public static class DeviceIdentity
    {
        private const string MarkerFileName = "beamrunner_device.id";

        /// <summary>
        /// Makes sure <paramref name="profile"/> has this device's id: the cached id when the cache file exists,
        /// otherwise a new random one. Returns true when the id changed. Never throws.
        /// </summary>
        public static bool Ensure(PlayerProfile profile)
        {
            if (profile == null)
            {
                return false;
            }

            if (profile.Wallet == null)
            {
                profile.Wallet = new WalletLedger();
            }

            string cached = ReadMarker();
            string current = profile.Wallet.DeviceId ?? string.Empty;
            string wanted = SaveEnvelope.IsValidInstallId(cached) ? cached : Guid.NewGuid().ToString("N");
            if (!string.Equals(cached, wanted, StringComparison.Ordinal))
            {
                WriteMarker(wanted);
            }

            if (string.Equals(current, wanted, StringComparison.Ordinal))
            {
                return false;
            }

            // Book this device's unexplained balance on the OLD key first, so rotating never moves gems around.
            WalletLedger.Reconcile(profile);
            profile.Wallet.DeviceId = wanted;
            if (current.Length > 0)
            {
                ServiceLog.Info("Save: device id rotated (restored or copied save, or the cache was cleared).");
            }

            return true;
        }

        private static string MarkerPath()
        {
            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string testDirectory = TestLaunchOptions.SaveDirectory;
                if (!string.IsNullOrEmpty(testDirectory))
                {
                    return Path.Combine(testDirectory, MarkerFileName);
                }
#endif
                string dir = Application.temporaryCachePath;
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, MarkerFileName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadMarker()
        {
            try
            {
                string path = MarkerPath();
                return path != null && File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Could not read the device id cache: " + e.Message);
                return null;
            }
        }

        private static void WriteMarker(string id)
        {
            try
            {
                string path = MarkerPath();
                if (path == null)
                {
                    return;
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, id);
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Could not write the device id cache: " + e.Message);
            }
        }
    }
}
