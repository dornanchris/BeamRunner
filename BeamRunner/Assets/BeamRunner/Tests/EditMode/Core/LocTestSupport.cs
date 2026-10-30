using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using BeamRunner.Core.Localization;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>
    /// Reads the shipped string tables (Resources/BeamRunner/Localization/*.json) straight from the project, for the
    /// Core tests: Unity runs them from the project folder, `dotnet test` (tools/unity-compile-check) from a build folder
    /// elsewhere, so the folder is found from this source file's path, the working directory or the test directory.
    /// </summary>
    internal static class LocTestSupport
    {
        private const string Relative = "Resources/BeamRunner/Localization";
        private static readonly Dictionary<string, StringTable> s_tables = new Dictionary<string, StringTable>();
        private static string s_folder;

        /// <summary>The Localization folder (fails the test when it cannot be found).</summary>
        public static string Folder
        {
            get
            {
                if (s_folder == null)
                {
                    s_folder = Find();
                }

                Assert.IsNotNull(s_folder, "Resources/BeamRunner/Localization not found from the test's location.");
                return s_folder;
            }
        }

        public static string ReadJson(string code)
        {
            return File.ReadAllText(Path.Combine(Folder, code + ".json"));
        }

        /// <summary>A shipped table, parsed once per test run.</summary>
        public static StringTable Table(string code)
        {
            StringTable table;
            if (!s_tables.TryGetValue(code, out table))
            {
                table = StringTable.FromJson(code, ReadJson(code));
                s_tables[code] = table;
            }

            return table;
        }

        /// <summary>Installs a localizer with the shipped English table as the current one (what the game does at boot).</summary>
        public static Localizer InstallEnglish()
        {
            var localizer = new Localizer(Table(Languages.EnglishCode));
            Loc.Install(localizer);
            return localizer;
        }

        /// <summary>Installs English and switches to <paramref name="code"/> (its shipped table).</summary>
        public static Localizer Install(string code)
        {
            Localizer localizer = InstallEnglish();
            LanguageInfo lang = Languages.Find(code);
            Assert.IsNotNull(lang, code);
            localizer.SetLanguage(lang, lang.IsPseudo || lang == Languages.English ? null : Table(code));
            return localizer;
        }

        private static string Find([CallerFilePath] string sourcePath = "")
        {
            var starts = new List<string>();
            if (!string.IsNullOrEmpty(sourcePath))
            {
                string p = sourcePath;
                if (!Path.IsPathRooted(p))
                {
                    p = Path.Combine(Directory.GetCurrentDirectory(), p);
                }

                starts.Add(Path.GetDirectoryName(p));
            }

            starts.Add(Directory.GetCurrentDirectory());
            try
            {
                starts.Add(TestContext.CurrentContext.TestDirectory);
            }
            catch (Exception)
            {
                // no NUnit context
            }

            foreach (string start in starts)
            {
                string dir = start;
                for (int up = 0; up < 8 && !string.IsNullOrEmpty(dir); up++)
                {
                    foreach (string candidate in new[]
                             {
                                 Path.Combine(dir, Relative),
                                 Path.Combine(dir, "BeamRunner", Relative),
                                 Path.Combine(dir, "Assets", "BeamRunner", Relative),
                                 Path.Combine(dir, "BeamRunner", "Assets", "BeamRunner", Relative),
                             })
                    {
                        if (File.Exists(Path.Combine(candidate, "en.json")))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }

            return null;
        }
    }
}
