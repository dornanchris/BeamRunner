using System;
using System.Collections.Generic;

namespace BeamRunner.Core.Localization
{
    /// <summary>Writing system of a language: decides which font set the UI uses.</summary>
    public enum LocScript
    {
        /// <summary>Audiowide + Rajdhani (Latin incl. Turkish/Polish/French/German/Spanish/Portuguese/Italian).</summary>
        Latin = 0,

        /// <summary>Russo One + Tektur (Cyrillic).</summary>
        Cyrillic = 1,

        /// <summary>Noto Sans JP.</summary>
        Japanese = 2,

        /// <summary>Noto Sans KR.</summary>
        Korean = 3,

        /// <summary>Noto Sans SC.</summary>
        SimplifiedChinese = 4,
    }

    /// <summary>A language the game can show: its code, picker name, script and number format.</summary>
    public sealed class LanguageInfo
    {
        /// <summary>BCP 47 code, also the string table's file name ("en", "pt-BR", "zh-Hans", "qps-ploc").</summary>
        public readonly string Code;

        /// <summary>
        /// Name in the language itself, in the case the picker shows it ("DEUTSCH", "日本語"). Rendered with the language's
        /// own font, so its glyphs are part of that font's subset.
        /// </summary>
        public readonly string DisplayName;

        /// <summary>English name (logs, docs).</summary>
        public readonly string EnglishName;

        public readonly LocScript Script;

        /// <summary>Thousands separator ("," "." or a no-break space U+00A0).</summary>
        public readonly string GroupSeparator;

        public readonly string DecimalSeparator;

        /// <summary>CLDR minimumGroupingDigits: 2 = four-digit numbers stay ungrouped (es "1234", but "12.345").</summary>
        public readonly int MinGroupingDigits;

        /// <summary>Development-only pseudo-localization (generated from English, never shipped as a file).</summary>
        public readonly bool IsPseudo;

        public LanguageInfo(string code, string displayName, string englishName, LocScript script, string groupSeparator,
            string decimalSeparator, int minGroupingDigits = 1, bool isPseudo = false)
        {
            Code = code;
            DisplayName = displayName;
            EnglishName = englishName;
            Script = script;
            GroupSeparator = groupSeparator;
            DecimalSeparator = decimalSeparator;
            MinGroupingDigits = Math.Max(1, minGroupingDigits);
            IsPseudo = isPseudo;
        }

        public override string ToString()
        {
            return Code;
        }
    }

    /// <summary>
    /// The shipped languages (English is the source; the others are machine-translated, see the tables' "_meta"), the
    /// pseudo-localization for development builds, and the mapping from the device language. Number formats follow CLDR
    /// (fr uses U+00A0 instead of CLDR's narrow no-break space U+202F, which the game fonts lack).
    /// </summary>
    public static class Languages
    {
        public const string EnglishCode = "en";
        public const string PseudoCode = "qps-ploc";

        private const string Nbsp = " ";

        /// <summary>English: the source language and the fallback for every missing string.</summary>
        public static readonly LanguageInfo English = new LanguageInfo(EnglishCode, "ENGLISH", "English", LocScript.Latin, ",", ".");

        /// <summary>Accented, ~35 % longer English for spotting truncation and unlocalized text (Editor/development builds).</summary>
        public static readonly LanguageInfo Pseudo = new LanguageInfo(PseudoCode, "[PŠÉÚÐÖ]", "Pseudo-localization", LocScript.Latin,
            ",", ".", 1, true);

        private static readonly LanguageInfo[] ShippedList =
        {
            English,
            new LanguageInfo("es", "ESPAÑOL", "Spanish", LocScript.Latin, ".", ",", 2),
            new LanguageInfo("pt-BR", "PORTUGUÊS (BRASIL)", "Portuguese (Brazil)", LocScript.Latin, ".", ","),
            new LanguageInfo("fr", "FRANÇAIS", "French", LocScript.Latin, Nbsp, ","),
            new LanguageInfo("de", "DEUTSCH", "German", LocScript.Latin, ".", ","),
            new LanguageInfo("it", "ITALIANO", "Italian", LocScript.Latin, ".", ","),
            new LanguageInfo("ru", "РУССКИЙ", "Russian", LocScript.Cyrillic, Nbsp, ","),
            new LanguageInfo("tr", "TÜRKÇE", "Turkish", LocScript.Latin, ".", ","),
            new LanguageInfo("pl", "POLSKI", "Polish", LocScript.Latin, Nbsp, ",", 2),
            new LanguageInfo("ja", "日本語", "Japanese", LocScript.Japanese, ",", "."),
            new LanguageInfo("ko", "한국어", "Korean", LocScript.Korean, ",", "."),
            new LanguageInfo("zh-Hans", "简体中文", "Chinese (Simplified)", LocScript.SimplifiedChinese, ",", "."),
        };

        /// <summary>Every shipped language, English first (the order of the language picker).</summary>
        public static IReadOnlyList<LanguageInfo> Shipped
        {
            get { return ShippedList; }
        }

        /// <summary>A shipped language (or the pseudo-localization) by code, case-insensitive; null when unknown.</summary>
        public static LanguageInfo Find(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            for (int i = 0; i < ShippedList.Length; i++)
            {
                if (string.Equals(ShippedList[i].Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return ShippedList[i];
                }
            }

            return string.Equals(code, PseudoCode, StringComparison.OrdinalIgnoreCase) ? Pseudo : null;
        }

        /// <summary>
        /// The shipped language for a device language, given as the name of Unity's <c>SystemLanguage</c> value
        /// ("German", "ChineseSimplified", …). Every Chinese variant maps to Simplified Chinese (the only Chinese
        /// shipped; Traditional readers can switch to English in Settings), Portuguese to Brazilian Portuguese, anything
        /// unsupported to English.
        /// </summary>
        public static LanguageInfo FromSystemLanguage(string systemLanguage)
        {
            switch (systemLanguage)
            {
                case "Spanish":
                    return Find("es");
                case "Portuguese":
                    return Find("pt-BR");
                case "French":
                    return Find("fr");
                case "German":
                    return Find("de");
                case "Italian":
                    return Find("it");
                case "Russian":
                    return Find("ru");
                case "Turkish":
                    return Find("tr");
                case "Polish":
                    return Find("pl");
                case "Japanese":
                    return Find("ja");
                case "Korean":
                    return Find("ko");
                case "Chinese":
                case "ChineseSimplified":
                case "ChineseTraditional":
                    return Find("zh-Hans");
                default:
                    return English;
            }
        }

        /// <summary>"pt" for "pt-BR", "zh" for "zh-Hans", lower-case; "" for null.</summary>
        public static string BaseLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }

            int dash = code.IndexOf('-');
            string lang = dash > 0 ? code.Substring(0, dash) : code;
            return lang.ToLowerInvariant(); // l10n-ok: a language code, never shown
        }
    }
}
