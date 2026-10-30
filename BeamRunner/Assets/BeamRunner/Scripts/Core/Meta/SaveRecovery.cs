using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Meta
{
    /// <summary>The copy <see cref="SaveRecovery.Choose"/> picked (or none).</summary>
    public struct SaveChoice
    {
        /// <summary>Index of the chosen copy in the list passed in, or −1 when no copy may be loaded.</summary>
        public int Index;

        /// <summary><see cref="SaveDocumentKind.Signed"/> or <see cref="SaveDocumentKind.Legacy"/> when a copy was chosen.</summary>
        public SaveDocumentKind Kind;

        /// <summary>The profile JSON to load (null when nothing was chosen).</summary>
        public string Payload;

        /// <summary>Install id of the chosen signed copy (null for legacy / none).</summary>
        public string InstallId;

        /// <summary>True when some copy was rejected because its signature did not match (an edited file).</summary>
        public bool SawTampered;

        /// <summary>True when some copy existed but was unreadable.</summary>
        public bool SawDamaged;

        /// <summary>True when a legacy (unsigned) copy was ignored because this save had already been migrated.</summary>
        public bool RejectedLegacy;
    }

    /// <summary>
    /// Which save copy to load. The storage passes its copies newest-first (main file, interrupted-save temp file,
    /// backup). The policy (deterministic, never throws):
    /// <list type="number">
    /// <item>The first <see cref="SaveDocumentKind.Signed"/> copy wins. An edited main file therefore falls back to
    /// the signed backup, so editing the save is ineffective.</item>
    /// <item>Otherwise, while <paramref name="legacyAllowed"/> (this save lineage has never been written signed — the
    /// storage keeps a marker next to the save once it has), the first <see cref="SaveDocumentKind.Legacy"/> copy is
    /// accepted: the one-time migration of saves from builds before signing. The next save re-signs it. After the
    /// migration a legacy copy is still accepted when the evidence points at corruption rather than editing: some
    /// signed-looking copy is unreadable (<see cref="SaveDocumentKind.Damaged"/>) and no copy is
    /// <see cref="SaveDocumentKind.Tampered"/> (e.g. the first signed save was torn and only the pre-migration backup
    /// survives) — progress is never dropped because of a mismatch that corruption could have caused.</item>
    /// <item>Otherwise nothing is loaded (index −1). Tampered and unreadable copies are never deleted by the storage;
    /// it moves them aside (<c>*.rejected</c>) the next time it saves.</item>
    /// </list>
    /// Corruption never causes a signature rejection: a torn or garbage file is <see cref="SaveDocumentKind.Damaged"/>
    /// and simply skipped for the next copy, exactly as before signing existed.
    /// </summary>
    public static class SaveRecovery
    {
        /// <summary>
        /// Chooses the copy to load. <paramref name="kinds"/> (optional, same length as <paramref name="documents"/>)
        /// receives the classification of every copy for logging.
        /// </summary>
        public static SaveChoice Choose(IReadOnlyList<string> documents, byte[] appKey, Func<string, bool> payloadValidator,
            bool legacyAllowed, SaveDocumentKind[] kinds = null)
        {
            var choice = new SaveChoice { Index = -1, Kind = SaveDocumentKind.Missing };
            if (documents == null)
            {
                return choice;
            }

            int count = documents.Count;
            var payloads = new string[count];
            var ids = new string[count];
            var found = new SaveDocumentKind[count];
            for (int i = 0; i < count; i++)
            {
                string payload;
                string iid;
                found[i] = SaveEnvelope.Open(documents[i], appKey, payloadValidator, out payload, out iid);
                payloads[i] = payload;
                ids[i] = iid;
                if (kinds != null && i < kinds.Length)
                {
                    kinds[i] = found[i];
                }

                choice.SawTampered |= found[i] == SaveDocumentKind.Tampered;
                choice.SawDamaged |= found[i] == SaveDocumentKind.Damaged;
            }

            for (int i = 0; i < count; i++)
            {
                if (found[i] == SaveDocumentKind.Signed)
                {
                    choice.Index = i;
                    choice.Kind = SaveDocumentKind.Signed;
                    choice.Payload = payloads[i];
                    choice.InstallId = ids[i];
                    return choice;
                }
            }

            bool corruptedEnvelope = false;
            for (int i = 0; i < count; i++)
            {
                corruptedEnvelope |= found[i] == SaveDocumentKind.Damaged && SaveEnvelope.LooksLikeEnvelope(documents[i]);
            }

            bool acceptLegacy = legacyAllowed || (corruptedEnvelope && !choice.SawTampered);
            for (int i = 0; i < count; i++)
            {
                if (found[i] != SaveDocumentKind.Legacy)
                {
                    continue;
                }

                if (!acceptLegacy)
                {
                    choice.RejectedLegacy = true;
                    continue;
                }

                choice.Index = i;
                choice.Kind = SaveDocumentKind.Legacy;
                choice.Payload = payloads[i];
                return choice;
            }

            return choice;
        }
    }
}
