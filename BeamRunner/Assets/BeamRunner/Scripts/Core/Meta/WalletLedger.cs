using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// Per-device gem counters that make the wallet mergeable across devices without duplicating or losing gems
    /// (a PN-counter): every device only ever increases its own <see cref="WalletEntry.Earned"/> /
    /// <see cref="WalletEntry.Spent"/>, a merge keeps the larger value of each device's counters, and the balance is
    /// Σ earned − Σ spent. Spending gems on one device and merging with a snapshot that still has them is therefore
    /// exact — no "free" ship.
    /// </summary>
    [Serializable]
    public sealed class WalletLedger
    {
        /// <summary>
        /// This device's key in <see cref="Entries"/>. Device-local: a merge never takes it from the other snapshot, and
        /// the Unity layer rotates it when the save was copied to another device (see <c>ProfileStore</c>).
        /// </summary>
        public string DeviceId = string.Empty;

        /// <summary>One entry per device that ever changed the balance.</summary>
        public List<WalletEntry> Entries = new List<WalletEntry>();

        /// <summary>Σ earned − Σ spent (not clamped).</summary>
        public static long Balance(WalletLedger ledger)
        {
            if (ledger == null || ledger.Entries == null)
            {
                return 0;
            }

            long balance = 0;
            for (int i = 0; i < ledger.Entries.Count; i++)
            {
                WalletEntry e = ledger.Entries[i];
                if (e != null)
                {
                    balance += e.Earned - e.Spent;
                }
            }

            return balance;
        }

        /// <summary>
        /// Books the difference between <see cref="PlayerProfile.Gems"/> and the ledger balance on this device's
        /// entry (earned when positive, spent when negative), so the ledger explains the wallet again. Call before
        /// saving and before merging. No-op without a <see cref="WalletLedger.DeviceId"/>.
        /// </summary>
        public static void Reconcile(PlayerProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            WalletLedger ledger = profile.Wallet;
            if (ledger == null || string.IsNullOrEmpty(ledger.DeviceId))
            {
                return;
            }

            if (ledger.Entries == null)
            {
                ledger.Entries = new List<WalletEntry>();
            }

            long delta = profile.Gems - Balance(ledger);
            if (delta == 0)
            {
                return;
            }

            WalletEntry own = Find(ledger, ledger.DeviceId);
            if (own == null)
            {
                own = new WalletEntry { Device = ledger.DeviceId };
                ledger.Entries.Add(own);
            }

            if (delta > 0)
            {
                own.Earned += delta;
            }
            else
            {
                own.Spent += -delta;
            }
        }

        /// <summary>
        /// Merges <paramref name="other"/>'s entries into <paramref name="target"/> (per device: the larger earned and the
        /// larger spent). Commutative, associative and idempotent. <see cref="WalletLedger.DeviceId"/> is untouched.
        /// </summary>
        public static void MergeInto(WalletLedger target, WalletLedger other)
        {
            if (target == null || other == null || other.Entries == null)
            {
                return;
            }

            if (target.Entries == null)
            {
                target.Entries = new List<WalletEntry>();
            }

            for (int i = 0; i < other.Entries.Count; i++)
            {
                WalletEntry e = other.Entries[i];
                if (e == null || string.IsNullOrEmpty(e.Device))
                {
                    continue;
                }

                WalletEntry mine = Find(target, e.Device);
                if (mine == null)
                {
                    target.Entries.Add(new WalletEntry { Device = e.Device, Earned = e.Earned, Spent = e.Spent });
                }
                else
                {
                    mine.Earned = Math.Max(mine.Earned, e.Earned);
                    mine.Spent = Math.Max(mine.Spent, e.Spent);
                }
            }

            Normalize(target);
        }

        /// <summary>
        /// Repairs a loaded ledger: drops null / nameless entries, clamps negative counters, folds duplicate devices
        /// (larger counters win) and sorts the entries by device id (deterministic output).
        /// </summary>
        public static void Normalize(WalletLedger ledger)
        {
            if (ledger == null)
            {
                return;
            }

            ledger.DeviceId = ledger.DeviceId ?? string.Empty;
            List<WalletEntry> entries = ledger.Entries ?? new List<WalletEntry>();
            var byDevice = new Dictionary<string, WalletEntry>(StringComparer.Ordinal);
            var clean = new List<WalletEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                WalletEntry e = entries[i];
                if (e == null || string.IsNullOrEmpty(e.Device))
                {
                    continue;
                }

                long earned = Math.Max(0, e.Earned);
                long spent = Math.Max(0, e.Spent);
                WalletEntry existing;
                if (byDevice.TryGetValue(e.Device, out existing))
                {
                    existing.Earned = Math.Max(existing.Earned, earned);
                    existing.Spent = Math.Max(existing.Spent, spent);
                    continue;
                }

                var copy = new WalletEntry { Device = e.Device, Earned = earned, Spent = spent };
                byDevice.Add(copy.Device, copy);
                clean.Add(copy);
            }

            clean.Sort((a, b) => string.CompareOrdinal(a.Device, b.Device));
            ledger.Entries = clean;
        }

        private static WalletEntry Find(WalletLedger ledger, string device)
        {
            for (int i = 0; i < ledger.Entries.Count; i++)
            {
                WalletEntry e = ledger.Entries[i];
                if (e != null && string.Equals(e.Device, device, StringComparison.Ordinal))
                {
                    return e;
                }
            }

            return null;
        }
    }

    /// <summary>Gems one device has earned (or bought) and spent in total, both only ever growing.</summary>
    [Serializable]
    public sealed class WalletEntry
    {
        public string Device = string.Empty;
        public long Earned;
        public long Spent;
    }
}
