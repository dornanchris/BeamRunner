using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Localization
{
    /// <summary>
    /// One language's strings: key → text, or key → plural forms (CLDR category → text). Loaded from a JSON object:
    /// <code>
    /// {
    ///   "_meta": { "language": "de", "machineTranslated": true, … },   // keys starting with "_" are metadata
    ///   "menu.play": "SPIELEN",
    ///   "mission.gems_run": { "one": "…{n}…", "other": "…{n}…" }
    /// }
    /// </code>
    /// Strings are stored in their final case (no runtime upper/lower-casing: Turkish dotted I, German ß).
    /// </summary>
    public sealed class StringTable
    {
        private readonly Dictionary<string, string> _simple = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _plural = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, string> _meta = new Dictionary<string, string>(StringComparer.Ordinal);

        public StringTable(string language)
        {
            Language = language ?? string.Empty;
        }

        /// <summary>Language code of the table.</summary>
        public string Language { get; }

        /// <summary>Every key, in file order.</summary>
        public IReadOnlyList<string> Keys
        {
            get { return _keys; }
        }

        public int Count
        {
            get { return _keys.Count; }
        }

        /// <summary>Values of the "_meta" object, as strings ("true" for booleans).</summary>
        public IReadOnlyDictionary<string, string> Meta
        {
            get { return _meta; }
        }

        public bool Contains(string key)
        {
            return key != null && (_simple.ContainsKey(key) || _plural.ContainsKey(key));
        }

        public bool IsPlural(string key)
        {
            return key != null && _plural.ContainsKey(key);
        }

        /// <summary>A simple string, or the "other" form of a plural entry.</summary>
        public bool TryGet(string key, out string text)
        {
            if (key == null)
            {
                text = null;
                return false;
            }

            if (_simple.TryGetValue(key, out text))
            {
                return true;
            }

            string[] forms;
            if (_plural.TryGetValue(key, out forms))
            {
                text = forms[(int)PluralCategory.Other];
                return text != null;
            }

            return false;
        }

        /// <summary>The form for <paramref name="category"/> (falling back to "other"); a simple string for any category.</summary>
        public bool TryGet(string key, PluralCategory category, out string text)
        {
            if (key == null)
            {
                text = null;
                return false;
            }

            if (_simple.TryGetValue(key, out text))
            {
                return true;
            }

            string[] forms;
            if (_plural.TryGetValue(key, out forms))
            {
                text = forms[(int)category] ?? forms[(int)PluralCategory.Other];
                return text != null;
            }

            return false;
        }

        /// <summary>The plural forms of <paramref name="key"/> (indexed by <see cref="PluralCategory"/>; null = absent), or null.</summary>
        public string[] GetForms(string key)
        {
            string[] forms;
            return key != null && _plural.TryGetValue(key, out forms) ? (string[])forms.Clone() : null;
        }

        /// <summary>Adds or replaces a simple string.</summary>
        public void Set(string key, string text)
        {
            if (string.IsNullOrEmpty(key) || text == null)
            {
                return;
            }

            if (!Contains(key))
            {
                _keys.Add(key);
            }

            _plural.Remove(key);
            _simple[key] = text;
        }

        /// <summary>Adds or replaces a plural entry (<paramref name="forms"/> indexed by <see cref="PluralCategory"/>).</summary>
        public void SetPlural(string key, string[] forms)
        {
            if (string.IsNullOrEmpty(key) || forms == null || forms.Length != 6 || forms[(int)PluralCategory.Other] == null)
            {
                return;
            }

            if (!Contains(key))
            {
                _keys.Add(key);
            }

            _simple.Remove(key);
            _plural[key] = (string[])forms.Clone();
        }

        /// <summary>
        /// Parses a table. Throws <see cref="FormatException"/> for malformed JSON, non-string values, plural objects with
        /// unknown categories or without "other".
        /// </summary>
        public static StringTable FromJson(string language, string json)
        {
            object root = LocJson.Parse(json);
            var obj = root as LocJson.JsonObject;
            if (obj == null)
            {
                throw new FormatException("String table " + language + ": the root must be a JSON object.");
            }

            var table = new StringTable(language);
            for (int k = 0; k < obj.Keys.Count; k++)
            {
                string key = obj.Keys[k];
                object value;
                obj.TryGet(key, out value);
                if (key.Length > 0 && key[0] == '_')
                {
                    if (key == "_meta")
                    {
                        ReadMeta(table, value as LocJson.JsonObject);
                    }

                    continue;
                }

                string text = value as string;
                if (text != null)
                {
                    table.Set(key, text);
                    continue;
                }

                var forms = value as LocJson.JsonObject;
                if (forms == null)
                {
                    throw new FormatException("String table " + language + ": \"" + key + "\" must be a string or a plural object.");
                }

                var array = new string[6];
                for (int f = 0; f < forms.Keys.Count; f++)
                {
                    string cat = forms.Keys[f];
                    PluralCategory category;
                    if (!PluralRules.TryParse(cat, out category))
                    {
                        throw new FormatException("String table " + language + ": \"" + key + "\" has an unknown plural category \"" +
                                                  cat + "\".");
                    }

                    object formValue;
                    forms.TryGet(cat, out formValue);
                    string form = formValue as string;
                    if (form == null)
                    {
                        throw new FormatException("String table " + language + ": \"" + key + "." + cat + "\" must be a string.");
                    }

                    array[(int)category] = form;
                }

                if (array[(int)PluralCategory.Other] == null)
                {
                    throw new FormatException("String table " + language + ": plural \"" + key + "\" needs an \"other\" form.");
                }

                table.SetPlural(key, array);
            }

            return table;
        }

        /// <summary>Like <see cref="FromJson"/> but never throws: null and the reason on failure.</summary>
        public static StringTable TryFromJson(string language, string json, out string error)
        {
            try
            {
                error = null;
                return FromJson(language, json);
            }
            catch (FormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        private static void ReadMeta(StringTable table, LocJson.JsonObject meta)
        {
            if (meta == null)
            {
                return;
            }

            for (int i = 0; i < meta.Keys.Count; i++)
            {
                object v;
                meta.TryGet(meta.Keys[i], out v);
                if (v is string s)
                {
                    table._meta[meta.Keys[i]] = s;
                }
                else if (v is bool b)
                {
                    table._meta[meta.Keys[i]] = b ? "true" : "false";
                }
            }
        }
    }
}
