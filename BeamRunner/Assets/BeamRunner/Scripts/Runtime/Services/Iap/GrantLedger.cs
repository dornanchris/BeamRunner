using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Remembers the transaction ids of consumables already granted (bounded, persisted in PlayerPrefs) so a
    /// transaction the store re-delivers — for example after a crash between granting and confirming — is
    /// never granted twice.
    /// </summary>
    internal sealed class GrantLedger
    {
        private const string PrefsKey = "BeamRunner.Iap.GrantedTransactions";
        private const int Capacity = 64;
        private const char Separator = '\n';

        private readonly List<string> _ids = new List<string>();

        public GrantLedger()
        {
            try
            {
                string stored = PlayerPrefs.GetString(PrefsKey, "");
                if (string.IsNullOrEmpty(stored))
                {
                    return;
                }

                string[] parts = stored.Split(Separator);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length > 0 && !_ids.Contains(parts[i]))
                    {
                        _ids.Add(parts[i]);
                    }
                }

                Trim();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Reading the IAP grant ledger failed", e);
            }
        }

        public bool Contains(string transactionId)
        {
            return !string.IsNullOrEmpty(transactionId) && _ids.Contains(Normalize(transactionId));
        }

        public void Add(string transactionId)
        {
            if (string.IsNullOrEmpty(transactionId))
            {
                return;
            }

            string id = Normalize(transactionId);
            if (_ids.Contains(id))
            {
                return;
            }

            _ids.Add(id);
            Trim();
            try
            {
                PlayerPrefs.SetString(PrefsKey, string.Join(Separator.ToString(), _ids));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Writing the IAP grant ledger failed", e);
            }
        }

        private void Trim()
        {
            while (_ids.Count > Capacity)
            {
                _ids.RemoveAt(0);
            }
        }

        private static string Normalize(string transactionId)
        {
            return transactionId.Replace(Separator, ' ').Trim();
        }
    }
}
