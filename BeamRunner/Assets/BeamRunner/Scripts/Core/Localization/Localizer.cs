using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Localization
{
    /// <summary>
    /// Resolves keys for one language with fallbacks: the language's table → English → the key itself. A key missing from
    /// the language (or from English) is reported once per language through <see cref="MissingKey"/> (the Unity layer
    /// logs it in the Editor and development builds only). Plural entries pick their form from the integer argument named
    /// <c>n</c> with <see cref="PluralRules"/>. Main thread only.
    /// </summary>
    public sealed class Localizer
    {
        private readonly StringTable _english;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
        private StringTable _table;
        private LanguageInfo _language;

        /// <summary>A localizer showing <paramref name="english"/> (null = empty: every lookup returns its key).</summary>
        public Localizer(StringTable english)
        {
            _english = english ?? new StringTable(Languages.EnglishCode);
            _table = _english;
            _language = Languages.English;
        }

        /// <summary>Called with (language code, key) the first time a key falls back. Exceptions are swallowed.</summary>
        public Action<string, string> MissingKey;

        public LanguageInfo Language
        {
            get { return _language; }
        }

        /// <summary>The current language's table (English when English is current).</summary>
        public StringTable Table
        {
            get { return _table; }
        }

        public StringTable English
        {
            get { return _english; }
        }

        /// <summary>
        /// Switches to <paramref name="language"/> with its <paramref name="table"/> (null → English strings, e.g. when the
        /// file failed to load). The pseudo-localization builds its table from English when <paramref name="table"/> is null.
        /// </summary>
        public void SetLanguage(LanguageInfo language, StringTable table)
        {
            _language = language ?? Languages.English;
            if (table == null && _language.IsPseudo)
            {
                table = PseudoLocalizer.BuildTable(_english);
            }

            _table = table ?? _english;
            _reported.Clear();
        }

        /// <summary>True when the current language (or English) defines <paramref name="key"/>.</summary>
        public bool Has(string key)
        {
            return _table.Contains(key) || _english.Contains(key);
        }

        /// <summary>The text of <paramref name="key"/> (plural entries: the "other" form, unformatted).</summary>
        public string Get(string key)
        {
            return Resolve(key, false, 0);
        }

        /// <summary>
        /// The text of <paramref name="key"/> with <paramref name="args"/> substituted (see <see cref="LocFormat"/>); a plural
        /// entry picks its form from the integer argument named <c>n</c> ("other" without one).
        /// </summary>
        public string Format(string key, (string name, object value)[] args)
        {
            bool counted = false;
            long count = 0;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].name == "n" && PluralRules.IsIntegral(args[i].value))
                    {
                        counted = true;
                        count = PluralRules.ToLong(args[i].value);
                        break;
                    }
                }
            }

            string pattern = Resolve(key, counted, count);
            return LocFormat.Apply(pattern, _language, args);
        }

        /// <summary><paramref name="value"/> with the current language's digit grouping.</summary>
        public string Number(long value)
        {
            return LocFormat.Integer(value, _language);
        }

        /// <summary><paramref name="value"/> with <paramref name="decimals"/> fraction digits in the current language.</summary>
        public string Number(double value, int decimals)
        {
            return LocFormat.Decimal(value, decimals, _language);
        }

        private string Resolve(string key, bool counted, long count)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string text;
            PluralCategory category = counted ? PluralRules.Select(_language.Code, count) : PluralCategory.Other;
            if (_table.TryGet(key, category, out text))
            {
                return text;
            }

            Report(key);
            if (!ReferenceEquals(_table, _english))
            {
                // The English fallback text follows English plural rules.
                PluralCategory english = counted ? PluralRules.Select(Languages.EnglishCode, count) : PluralCategory.Other;
                if (_english.TryGet(key, english, out text))
                {
                    return text;
                }
            }

            return key;
        }

        private void Report(string key)
        {
            Action<string, string> handler = MissingKey;
            if (handler == null || !_reported.Add(key))
            {
                return;
            }

            try
            {
                handler(_language.Code, key);
            }
            catch (Exception)
            {
                // Diagnostics must never break text.
            }
        }
    }
}
