using System;
using BeamRunner.Core.Meta;
using BeamRunner.Services;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// Loads and persists the <see cref="PlayerProfile"/> as JSON through an <see cref="ISaveStorage"/>.
    /// Saves are debounced (changes within a short window are coalesced) and flushed immediately at safe
    /// points: end of run, purchases, app pause/quit.
    /// </summary>
    public sealed class ProfileStore
    {
        private const float SaveDelay = 1.25f;

        private readonly ISaveStorage _storage;
        private bool _dirty;
        private float _dirtyTimer;

        public ProfileStore(ISaveStorage storage)
        {
            _storage = storage;
        }

        public ProfileService Service { get; private set; }

        /// <summary>True if the last load found a save but it could not be parsed (a fresh profile was used).</summary>
        public bool RecoveredFromCorruption { get; private set; }

        /// <summary>Loads the saved profile (or creates a new one) and wires change tracking.</summary>
        public ProfileService Load()
        {
            PlayerProfile profile = null;
            string json = null;
            try
            {
                json = _storage != null ? _storage.Load() : null;
            }
            catch (Exception e)
            {
                FieldLog.Warn("Save load failed: " + e.Message);
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    profile = JsonUtility.FromJson<PlayerProfile>(json);
                }
                catch (Exception e)
                {
                    FieldLog.Warn("Save parse failed, starting fresh: " + e.Message);
                    RecoveredFromCorruption = true;
                }
            }

            if (profile == null)
            {
                profile = PlayerProfile.CreateNew();
            }

            try
            {
                profile.Sanitize();
                DeviceIdentity.Ensure(profile);
                Service = new ProfileService(profile);
            }
            catch (Exception e)
            {
                // A save that parses but can't be repaired must not throw out of boot on every launch.
                FieldLog.Warn("Save could not be repaired, starting fresh: " + e.Message);
                RecoveredFromCorruption = true;
                Service = new ProfileService(PlayerProfile.CreateNew());
            }

            Service.Changed += MarkDirty;
            return Service;
        }

        /// <summary>
        /// Save-storage validator: true only for documents that <see cref="JsonUtility"/> turns into a profile.
        /// A structurally plausible but unparsable file is then treated as damaged, so the storage falls back to
        /// its backup copy (and never rotates the broken file over that backup on the next save).
        /// </summary>
        public static bool IsUsableSave(string json)
        {
            if (!JsonSanity.LooksLikeJsonObject(json))
            {
                return false;
            }

            try
            {
                return JsonUtility.FromJson<PlayerProfile>(json) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void MarkDirty()
        {
            _dirty = true;
            _dirtyTimer = SaveDelay;
        }

        /// <summary>Call every frame with unscaled delta time; performs debounced saves.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!_dirty)
            {
                return;
            }

            _dirtyTimer -= unscaledDeltaTime;
            if (_dirtyTimer <= 0f)
            {
                SaveNow();
            }
        }

        /// <summary>Writes immediately if anything changed (or always when <paramref name="force"/>).</summary>
        public void SaveNow(bool force = false)
        {
            if ((!_dirty && !force) || Service == null || _storage == null)
            {
                return;
            }

            _dirty = false;
            try
            {
                string json = Serialize();
                if (!_storage.Save(json))
                {
                    FieldLog.Warn("Save write failed; will retry on next change.");
                    _dirty = true;
                    _dirtyTimer = SaveDelay * 4f;
                }
            }
            catch (Exception e)
            {
                FieldLog.Warn("Save serialisation failed: " + e.Message);
            }
        }

        /// <summary>
        /// The profile as a JSON document (what is saved and uploaded). Books any unexplained gem change on this
        /// device's ledger entry first (<see cref="WalletLedger.Reconcile"/>), so the document is always mergeable.
        /// </summary>
        public string Serialize()
        {
            PlayerProfile profile = Service.Profile;
            if (profile.Wallet != null && string.IsNullOrEmpty(profile.Wallet.DeviceId))
            {
                DeviceIdentity.Ensure(profile);
            }

            WalletLedger.Reconcile(profile);
            return JsonUtility.ToJson(profile);
        }

        /// <summary>Deletes the save and starts over with a fresh profile (keeps purchases via ProfileService).</summary>
        public void ResetProgress()
        {
            Service.ResetProgress();
            SaveNow(true);
        }
    }
}
