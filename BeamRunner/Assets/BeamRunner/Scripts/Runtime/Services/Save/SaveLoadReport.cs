using BeamRunner.Core.Meta;

namespace BeamRunner.Services
{
    /// <summary>What a signed <see cref="ISaveStorage.Load"/> found (diagnostics, tests, support).</summary>
    public sealed class SaveLoadReport
    {
        /// <summary>Report of a storage that has not loaded yet (or is unsigned).</summary>
        public static readonly SaveLoadReport None = new SaveLoadReport(
            new SaveChoice { Index = -1, Kind = SaveDocumentKind.Missing }, new SaveDocumentKind[0]);

        private readonly SaveDocumentKind[] _copies;

        /// <summary>Creates a report from the recovery decision and the classification of each copy.</summary>
        public SaveLoadReport(SaveChoice choice, SaveDocumentKind[] copies)
        {
            ChosenIndex = choice.Index;
            ChosenKind = choice.Kind;
            SawTampered = choice.SawTampered;
            SawDamaged = choice.SawDamaged;
            RejectedLegacy = choice.RejectedLegacy;
            _copies = copies != null ? (SaveDocumentKind[])copies.Clone() : new SaveDocumentKind[0];
        }

        /// <summary>Index of the loaded copy (0 main, 1 interrupted-save temp file, 2 backup), −1 = none.</summary>
        public int ChosenIndex { get; private set; }

        /// <summary>Kind of the loaded copy (<see cref="SaveDocumentKind.Missing"/> when none).</summary>
        public SaveDocumentKind ChosenKind { get; private set; }

        /// <summary>Some copy had a mismatching signature (edited).</summary>
        public bool SawTampered { get; private set; }

        /// <summary>Some copy was unreadable.</summary>
        public bool SawDamaged { get; private set; }

        /// <summary>An unsigned copy was ignored because the save had already been migrated.</summary>
        public bool RejectedLegacy { get; private set; }

        /// <summary>A newer copy was ignored and an older one loaded instead (an edit was undone).</summary>
        public bool RestoredOlderCopy
        {
            get { return ChosenIndex > 0 && (SawTampered || RejectedLegacy); }
        }

        /// <summary>Classification of copy <paramref name="index"/> (0 main, 1 temp, 2 backup).</summary>
        public SaveDocumentKind Copy(int index)
        {
            return index >= 0 && index < _copies.Length ? _copies[index] : SaveDocumentKind.Missing;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var parts = new string[_copies.Length];
            for (int i = 0; i < _copies.Length; i++)
            {
                parts[i] = _copies[i].ToString();
            }

            return "loaded=" + ChosenIndex + "(" + ChosenKind + ") copies=[" + string.Join(",", parts) + "]";
        }
    }
}
