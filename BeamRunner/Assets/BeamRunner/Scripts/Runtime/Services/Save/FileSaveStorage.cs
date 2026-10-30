using System;
using System.IO;
using System.Text;
using BeamRunner.Core.Meta;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Stores the profile in <c>Application.persistentDataPath/beamrunner_profile.json</c> with crash-safe
    /// writes:
    /// <list type="number">
    /// <item>the new document is written to <c>*.tmp</c> and flushed to disk;</item>
    /// <item>the current file, if valid, becomes <c>*.bak</c> (a damaged current file is discarded instead,
    /// so a good backup is never overwritten by garbage);</item>
    /// <item><c>*.tmp</c> is renamed over the main file (an atomic rename on every supported OS).</item>
    /// </list>
    /// <see cref="Load"/> tries the main file, then a completed-but-unrenamed <c>*.tmp</c>, then <c>*.bak</c>,
    /// accepting only documents that pass the validator. All IO is wrapped; nothing throws.
    /// <para><b>Integrity</b> (when constructed with a signing key): every document is written as a signed
    /// <see cref="SaveEnvelope"/> and <see cref="Load"/> applies <see cref="SaveRecovery"/> — an edited copy is ignored in
    /// favour of the newest correctly signed one, a pre-signing (legacy) save is accepted until the first signed save
    /// (<c>*.signed</c> marker next to the file), and a rejected main file is moved to <c>*.rejected</c> (never
    /// deleted) the next time the profile is saved. Deterrence against casual editing, not security.</para>
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        /// <summary>Default profile file name inside <see cref="Application.persistentDataPath"/>.</summary>
        public const string DefaultFileName = "beamrunner_profile.json";

        private const long MaxFileBytes = 8L * 1024L * 1024L;

        private static readonly UTF8Encoding s_utf8NoBom = new UTF8Encoding(false);

        private readonly string _directory;
        private readonly string _path;
        private readonly string _tempPath;
        private readonly string _backupPath;
        private readonly string _markerPath;
        private readonly string _rejectedPath;
        private readonly Func<string, bool> _validator;
        private readonly byte[] _signingKey;
        private bool _primaryKnownGood;
        private string _installId;

        /// <summary>
        /// Creates storage for <paramref name="fileName"/> in <see cref="Application.persistentDataPath"/>.
        /// <paramref name="validator"/> decides whether a document is usable (for example "parses into a
        /// profile"); when null, <see cref="JsonSanity.LooksLikeJsonObject"/> is used.
        /// </summary>
        public FileSaveStorage(string fileName = DefaultFileName, Func<string, bool> validator = null)
            : this(SafePersistentDataPath(), fileName, validator)
        {
        }

        /// <summary>Creates unsigned storage for <paramref name="fileName"/> inside <paramref name="directory"/>.</summary>
        public FileSaveStorage(string directory, string fileName, Func<string, bool> validator)
            : this(directory, fileName, validator, null)
        {
        }

        /// <summary>
        /// Creates storage for <paramref name="fileName"/> inside <paramref name="directory"/>. With a
        /// <paramref name="signingKey"/> (normally <see cref="SaveIntegrityKey.Get"/>) documents are signed and verified
        /// (see the class remarks); <paramref name="validator"/> then judges the profile payload inside the envelope.
        /// </summary>
        public FileSaveStorage(string directory, string fileName, Func<string, bool> validator, byte[] signingKey)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = DefaultFileName;
            }

            _directory = string.IsNullOrEmpty(directory) ? "." : directory;
            _path = Path.Combine(_directory, fileName);
            _tempPath = _path + ".tmp";
            _backupPath = _path + ".bak";
            _markerPath = _path + ".signed";
            _rejectedPath = _path + ".rejected";
            _validator = validator ?? JsonSanity.LooksLikeJsonObject;
            _signingKey = signingKey != null && signingKey.Length > 0 ? (byte[])signingKey.Clone() : null;
        }

        /// <summary>Absolute path of the main save file.</summary>
        public string FilePath => _path;

        /// <summary>True when documents are signed and verified.</summary>
        public bool IsSigned => _signingKey != null;

        /// <summary>What the last <see cref="Load"/> found (diagnostics; <see cref="SaveLoadReport.None"/> before any load).</summary>
        public SaveLoadReport LastLoad { get; private set; } = SaveLoadReport.None;

        /// <inheritdoc />
        public string Load()
        {
            if (_signingKey != null)
            {
                return LoadSigned();
            }

            string primary = TryRead(_path);
            if (IsValid(primary))
            {
                _primaryKnownGood = true;
                return primary;
            }

            _primaryKnownGood = false;
            bool primaryExisted = primary != null;
            if (primaryExisted)
            {
                ServiceLog.Warn("Save file is damaged; trying recovery copies.");
            }

            // A fully written temp file means the last save was interrupted just before the final rename;
            // it is newer than the backup.
            string temp = TryRead(_tempPath);
            if (IsValid(temp))
            {
                ServiceLog.Warn("Recovered the profile from an interrupted save.");
                return temp;
            }

            string backup = TryRead(_backupPath);
            if (IsValid(backup))
            {
                ServiceLog.Warn("Recovered the profile from the backup copy.");
                return backup;
            }

            if (primaryExisted || backup != null)
            {
                ServiceLog.Error("Save data is damaged and no valid backup exists; starting a fresh profile.");
            }

            return null;
        }

        private string LoadSigned()
        {
            string[] copies = { TryRead(_path), TryRead(_tempPath), TryRead(_backupPath) };
            var kinds = new SaveDocumentKind[copies.Length];
            bool legacyAllowed = !MarkerExists();
            SaveChoice choice = SaveRecovery.Choose(copies, _signingKey, IsValid, legacyAllowed, kinds);
            LastLoad = new SaveLoadReport(choice, kinds);
            _primaryKnownGood = choice.Index == 0;
            if (choice.Kind == SaveDocumentKind.Signed && SaveEnvelope.IsValidInstallId(choice.InstallId))
            {
                _installId = choice.InstallId;
            }

            if (choice.SawTampered)
            {
                ServiceLog.Warn("Save: a copy with a mismatching signature was ignored (edited file?).");
            }

            if (choice.RejectedLegacy)
            {
                ServiceLog.Warn("Save: an unsigned copy was ignored (this save was already migrated to signed saves).");
            }

            switch (choice.Index)
            {
                case -1:
                    bool anything = copies[0] != null || copies[1] != null || copies[2] != null;
                    if (anything)
                    {
                        ServiceLog.Error("Save data is unusable (damaged or edited) and no valid copy exists; starting a " +
                                         "fresh profile. The old files are kept as " + Path.GetFileName(_rejectedPath) +
                                         " on the next save.");
                    }

                    return null;
                case 0:
                    if (choice.Kind == SaveDocumentKind.Legacy)
                    {
                        ServiceLog.Info("Save: migrating an unsigned save; it is signed on the next save.");
                    }

                    return choice.Payload;
                case 1:
                    ServiceLog.Warn("Recovered the profile from an interrupted save.");
                    return choice.Payload;
                default:
                    ServiceLog.Warn("Recovered the profile from the backup copy.");
                    return choice.Payload;
            }
        }

        /// <inheritdoc />
        public bool Save(string json)
        {
            if (!IsValid(json))
            {
                ServiceLog.Error("Refusing to save an invalid profile document.");
                return false;
            }

            try
            {
                string document = json;
                if (_signingKey != null)
                {
                    if (!SaveEnvelope.IsValidInstallId(_installId))
                    {
                        _installId = Guid.NewGuid().ToString("N");
                    }

                    document = SaveEnvelope.Wrap(json.Trim(), _installId, _signingKey);
                }

                if (!Directory.Exists(_directory))
                {
                    Directory.CreateDirectory(_directory);
                }

                byte[] bytes = s_utf8NoBom.GetBytes(document);
                using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(_path))
                {
                    bool primaryGood = _primaryKnownGood || IsLoadableCopy(TryRead(_path));
                    if (primaryGood)
                    {
                        DeleteIfExists(_backupPath);
                        File.Move(_path, _backupPath);
                    }
                    else
                    {
                        // Keep the existing backup. The unusable main file is moved aside, never deleted: a damaged
                        // or edited copy may still matter to the player or to support.
                        DeleteIfExists(_rejectedPath);
                        File.Move(_path, _rejectedPath);
                    }
                }

                File.Move(_tempPath, _path);
                _primaryKnownGood = true;
                if (_signingKey != null && !MarkerExists())
                {
                    WriteMarker();
                }

                return true;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Saving the profile failed", e);
                return false;
            }
        }

        /// <inheritdoc />
        public void Delete()
        {
            _primaryKnownGood = false;
            _installId = null;
            try
            {
                DeleteIfExists(_path);
                DeleteIfExists(_tempPath);
                DeleteIfExists(_backupPath);
                DeleteIfExists(_markerPath);
                DeleteIfExists(_rejectedPath);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Deleting the profile failed", e);
            }
        }

        private bool IsValid(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            try
            {
                return _validator(text);
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Save validator threw (" + e.Message + "); treating the document as invalid.");
                return false;
            }
        }

        /// <summary>
        /// Whether an existing copy is worth keeping as the backup: valid (unsigned storage), or signed / a legacy
        /// document that may still be migrated (signed storage).
        /// </summary>
        private bool IsLoadableCopy(string text)
        {
            if (_signingKey == null)
            {
                return IsValid(text);
            }

            string payload;
            string iid;
            SaveDocumentKind kind = SaveEnvelope.Open(text, _signingKey, IsValid, out payload, out iid);
            return kind == SaveDocumentKind.Signed || (kind == SaveDocumentKind.Legacy && !MarkerExists());
        }

        private bool MarkerExists()
        {
            try
            {
                return File.Exists(_markerPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void WriteMarker()
        {
            try
            {
                File.WriteAllText(_markerPath, "Saves in this folder are signed; unsigned copies are no longer loaded.\n",
                    s_utf8NoBom);
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Could not write the save integrity marker: " + e.Message);
            }
        }

        /// <summary>Returns the file content, or null when missing/unreadable/oversized.</summary>
        private static string TryRead(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return null;
                }

                if (info.Length > MaxFileBytes)
                {
                    ServiceLog.Warn("Ignoring oversized save file " + info.Name + " (" + info.Length + " bytes).");
                    return "";
                }

                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                ServiceLog.Warn("Could not read " + Path.GetFileName(path) + ": " + e.Message);
                return "";
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string SafePersistentDataPath()
        {
            try
            {
                return Application.persistentDataPath;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("persistentDataPath unavailable", e);
                return ".";
            }
        }
    }
}
