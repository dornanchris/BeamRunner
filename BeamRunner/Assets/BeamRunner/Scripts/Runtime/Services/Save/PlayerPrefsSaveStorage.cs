using System;
using BeamRunner.Core.Meta;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// <see cref="ISaveStorage"/> on top of <see cref="PlayerPrefs"/>, used on WebGL where files in
    /// <c>persistentDataPath</c> are not reliably flushed to the browser's IndexedDB. Keeps the previous good
    /// document under a backup key. PlayerPrefs on WebGL is limited to about 1 MB in total, far above a profile.
    /// With a signing key, documents are signed like <see cref="FileSaveStorage"/> does (same recovery policy; the
    /// migration marker is the <c>*.signed</c> key).
    /// </summary>
    public sealed class PlayerPrefsSaveStorage : ISaveStorage
    {
        /// <summary>Default PlayerPrefs key of the profile document.</summary>
        public const string DefaultKey = "BeamRunner.Profile";

        private readonly string _key;
        private readonly string _backupKey;
        private readonly string _markerKey;
        private readonly Func<string, bool> _validator;
        private readonly byte[] _signingKey;
        private string _installId;

        /// <summary>
        /// Creates storage under <paramref name="key"/>. <paramref name="validator"/> decides whether a document
        /// is usable; when null, <see cref="JsonSanity.LooksLikeJsonObject"/> is used.
        /// </summary>
        public PlayerPrefsSaveStorage(string key = DefaultKey, Func<string, bool> validator = null)
            : this(key, validator, null)
        {
        }

        /// <summary>As above; with a <paramref name="signingKey"/> documents are signed and verified.</summary>
        public PlayerPrefsSaveStorage(string key, Func<string, bool> validator, byte[] signingKey)
        {
            _key = string.IsNullOrEmpty(key) ? DefaultKey : key;
            _backupKey = _key + ".bak";
            _markerKey = _key + ".signed";
            _validator = validator ?? JsonSanity.LooksLikeJsonObject;
            _signingKey = signingKey != null && signingKey.Length > 0 ? (byte[])signingKey.Clone() : null;
        }

        /// <inheritdoc />
        public string Load()
        {
            try
            {
                if (_signingKey != null)
                {
                    return LoadSigned();
                }

                string primary = PlayerPrefs.GetString(_key, "");
                if (IsValid(primary))
                {
                    return primary;
                }

                string backup = PlayerPrefs.GetString(_backupKey, "");
                if (IsValid(backup))
                {
                    ServiceLog.Warn("Recovered the profile from the backup key.");
                    return backup;
                }

                if (!string.IsNullOrEmpty(primary))
                {
                    ServiceLog.Error("Save data is damaged and no valid backup exists; starting a fresh profile.");
                }
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Loading the profile from PlayerPrefs failed", e);
            }

            return null;
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

                string previous = PlayerPrefs.GetString(_key, "");
                if (IsKeepable(previous) && !string.Equals(previous, document, StringComparison.Ordinal))
                {
                    PlayerPrefs.SetString(_backupKey, previous);
                }

                PlayerPrefs.SetString(_key, document);
                if (_signingKey != null)
                {
                    PlayerPrefs.SetInt(_markerKey, 1);
                }

                PlayerPrefs.Save();
                return true;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Saving the profile to PlayerPrefs failed", e);
                return false;
            }
        }

        /// <inheritdoc />
        public void Delete()
        {
            try
            {
                PlayerPrefs.DeleteKey(_key);
                PlayerPrefs.DeleteKey(_backupKey);
                PlayerPrefs.DeleteKey(_markerKey);
                _installId = null;
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Deleting the profile from PlayerPrefs failed", e);
            }
        }

        private string LoadSigned()
        {
            string[] copies = { PlayerPrefs.GetString(_key, ""), PlayerPrefs.GetString(_backupKey, "") };
            bool legacyAllowed = PlayerPrefs.GetInt(_markerKey, 0) == 0;
            SaveChoice choice = SaveRecovery.Choose(copies, _signingKey, IsValid, legacyAllowed);
            if (choice.Kind == SaveDocumentKind.Signed && SaveEnvelope.IsValidInstallId(choice.InstallId))
            {
                _installId = choice.InstallId;
            }

            if (choice.SawTampered || choice.RejectedLegacy)
            {
                ServiceLog.Warn("Save: an edited or unsigned copy was ignored.");
            }

            if (choice.Index == 1)
            {
                ServiceLog.Warn("Recovered the profile from the backup key.");
            }
            else if (choice.Index < 0 && !string.IsNullOrEmpty(copies[0]))
            {
                ServiceLog.Error("Save data is unusable (damaged or edited) and no valid copy exists; starting a fresh profile.");
            }

            return choice.Payload;
        }

        /// <summary>Whether the current document is worth keeping as the backup.</summary>
        private bool IsKeepable(string text)
        {
            if (_signingKey == null)
            {
                return IsValid(text);
            }

            string payload;
            string iid;
            SaveDocumentKind kind = SaveEnvelope.Open(text, _signingKey, IsValid, out payload, out iid);
            return kind == SaveDocumentKind.Signed ||
                   (kind == SaveDocumentKind.Legacy && PlayerPrefs.GetInt(_markerKey, 0) == 0);
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
            catch (Exception)
            {
                return false;
            }
        }
    }
}
