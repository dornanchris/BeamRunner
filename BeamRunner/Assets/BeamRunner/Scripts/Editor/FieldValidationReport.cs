using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>Result of <see cref="FieldProjectValidator.Run"/>: an ordered list of passed checks, warnings and errors.</summary>
    public sealed class FieldValidationReport
    {
        /// <summary>Severity of a single finding.</summary>
        public enum Severity
        {
            /// <summary>Check passed.</summary>
            Ok,

            /// <summary>Works, but needs attention before release.</summary>
            Warning,

            /// <summary>Broken: the game will misbehave or a build will fail.</summary>
            Error,
        }

        /// <summary>One finding.</summary>
        public readonly struct Entry
        {
            /// <summary>Creates a finding.</summary>
            public Entry(Severity severity, string message)
            {
                Level = severity;
                Message = message;
            }

            /// <summary>Severity.</summary>
            public Severity Level { get; }

            /// <summary>Human-readable message.</summary>
            public string Message { get; }
        }

        private readonly List<Entry> _entries = new List<Entry>();

        /// <summary>All findings in check order.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Number of errors.</summary>
        public int Errors { get; private set; }

        /// <summary>Number of warnings.</summary>
        public int Warnings { get; private set; }

        /// <summary>Number of passed checks.</summary>
        public int Passed { get; private set; }

        /// <summary>Records a passed check.</summary>
        public void Ok(string message)
        {
            _entries.Add(new Entry(Severity.Ok, message));
            Passed++;
        }

        /// <summary>Records a warning.</summary>
        public void Warn(string message)
        {
            _entries.Add(new Entry(Severity.Warning, message));
            Warnings++;
        }

        /// <summary>Records an error.</summary>
        public void Error(string message)
        {
            _entries.Add(new Entry(Severity.Error, message));
            Errors++;
        }

        /// <summary>One-line summary.</summary>
        public string Summary =>
            Errors + " error(s), " + Warnings + " warning(s), " + Passed + " check(s) passed";

        /// <summary>Writes every finding to the Console (errors/warnings with the matching log type).</summary>
        public void LogToConsole()
        {
            var ok = new StringBuilder();
            foreach (Entry e in _entries)
            {
                switch (e.Level)
                {
                    case Severity.Error:
                        Debug.LogError("[BeamRunner] Validate: " + e.Message);
                        break;
                    case Severity.Warning:
                        Debug.LogWarning("[BeamRunner] Validate: " + e.Message);
                        break;
                    default:
                        ok.AppendLine("  ✔ " + e.Message);
                        break;
                }
            }

            Debug.Log("[BeamRunner] Validate Project: " + Summary + "\n" + ok);
        }

        /// <summary>Errors and warnings (at most <paramref name="maxLines"/>) formatted for a dialog.</summary>
        public string ToDialogText(int maxLines)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Summary);
            int lines = 0;
            foreach (Severity level in new[] { Severity.Error, Severity.Warning })
            {
                foreach (Entry e in _entries)
                {
                    if (e.Level != level)
                    {
                        continue;
                    }

                    if (lines == 0)
                    {
                        sb.AppendLine();
                    }

                    if (lines >= maxLines)
                    {
                        sb.AppendLine("… (see the Console for the full list)");
                        return sb.ToString().TrimEnd();
                    }

                    sb.AppendLine((level == Severity.Error ? "✖ " : "⚠ ") + e.Message);
                    lines++;
                }
            }

            if (lines == 0)
            {
                sb.AppendLine();
                sb.AppendLine("All checks passed.");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
