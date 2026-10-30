using System;

namespace BeamRunner.Core.Localization
{
    /// <summary>CLDR plural categories (the names used as keys of plural entries in the string tables).</summary>
    public enum PluralCategory
    {
        Zero = 0,
        One = 1,
        Two = 2,
        Few = 3,
        Many = 4,
        Other = 5,
    }

    /// <summary>
    /// CLDR cardinal plural rules (plurals.xml, CLDR 45) for the shipped languages, for integers (the game only counts
    /// whole things: operands i = |n|, v = 0). Rule families:
    /// <list type="bullet">
    /// <item>en, de: one = i is 1; other.</item>
    /// <item>it, es: one = n is 1 (it: i = 1 and v = 0 — the same for integers); many = i != 0 and i % 1000000 = 0 (exact
    /// millions, "1.000.000 de …"); other.</item>
    /// <item>fr, pt (Brazil): one = i is 0 or 1 (French and Brazilian Portuguese treat 0 as singular); many = exact
    /// millions; other.</item>
    /// <item>ru: one = i % 10 = 1 and i % 100 != 11; few = i % 10 = 2..4 and i % 100 != 12..14; many = every other integer
    /// (i % 10 = 0 or 5..9, or i % 100 = 11..14); other = fractions only.</item>
    /// <item>pl: one = i is 1; few = i % 10 = 2..4 and i % 100 != 12..14; many = every other integer (0, 5..21, 25..31,
    /// 111..114, …); other = fractions only.</item>
    /// <item>tr: one = n is 1; other. Turkish nouns stay singular after a number, so both forms read the same and the tables
    /// only need "other".</item>
    /// <item>ja, ko, zh: other only.</item>
    /// </list>
    /// A table entry may omit any category but "other": a missing category falls back to "other" (see
    /// <see cref="RequiredCategories"/> for what each language's tables must provide).
    /// </summary>
    public static class PluralRules
    {
        private static readonly PluralCategory[] OneOther = { PluralCategory.One, PluralCategory.Other };
        private static readonly PluralCategory[] OtherOnly = { PluralCategory.Other };
        private static readonly PluralCategory[] Slavic = { PluralCategory.One, PluralCategory.Few, PluralCategory.Many, PluralCategory.Other };

        /// <summary>Plural category of the integer <paramref name="n"/> in <paramref name="language"/> (a language code).</summary>
        public static PluralCategory Select(string language, long n)
        {
            ulong i = n < 0 ? (ulong)(-(n + 1)) + 1UL : (ulong)n;
            switch (Family(language))
            {
                case "en":
                    return i == 1 ? PluralCategory.One : PluralCategory.Other;
                case "es":
                    if (i == 1)
                    {
                        return PluralCategory.One;
                    }

                    return i != 0 && i % 1000000 == 0 ? PluralCategory.Many : PluralCategory.Other;
                case "fr":
                    if (i <= 1)
                    {
                        return PluralCategory.One;
                    }

                    return i % 1000000 == 0 ? PluralCategory.Many : PluralCategory.Other;
                case "ru":
                {
                    ulong m10 = i % 10;
                    ulong m100 = i % 100;
                    if (m10 == 1 && m100 != 11)
                    {
                        return PluralCategory.One;
                    }

                    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14))
                    {
                        return PluralCategory.Few;
                    }

                    return PluralCategory.Many;
                }

                case "pl":
                {
                    if (i == 1)
                    {
                        return PluralCategory.One;
                    }

                    ulong m10 = i % 10;
                    ulong m100 = i % 100;
                    if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14))
                    {
                        return PluralCategory.Few;
                    }

                    return PluralCategory.Many;
                }

                case "tr":
                    return i == 1 ? PluralCategory.One : PluralCategory.Other;
                default:
                    return PluralCategory.Other;
            }
        }

        /// <summary>
        /// Categories every plural entry of <paramref name="language"/> must define. Optional extras that fall back to
        /// "other" when absent: "many" (exact millions) for es/fr/it/pt-BR, "one" for tr.
        /// </summary>
        public static PluralCategory[] RequiredCategories(string language)
        {
            switch (Family(language))
            {
                case "en":
                case "es":
                case "fr":
                    return (PluralCategory[])OneOther.Clone();
                case "ru":
                case "pl":
                    return (PluralCategory[])Slavic.Clone();
                default:
                    return (PluralCategory[])OtherOnly.Clone();
            }
        }

        /// <summary>True when <paramref name="category"/> may appear in <paramref name="language"/>'s plural entries.</summary>
        public static bool IsAllowed(string language, PluralCategory category)
        {
            if (category == PluralCategory.Other)
            {
                return true;
            }

            switch (Family(language))
            {
                case "en":
                    return category == PluralCategory.One;
                case "es":
                case "fr":
                    return category == PluralCategory.One || category == PluralCategory.Many;
                case "ru":
                case "pl":
                    return category == PluralCategory.One || category == PluralCategory.Few || category == PluralCategory.Many;
                case "tr":
                    return category == PluralCategory.One;
                default:
                    return false;
            }
        }

        /// <summary>CLDR key of a category ("one", "few", …).</summary>
        public static string Key(PluralCategory category)
        {
            switch (category)
            {
                case PluralCategory.Zero:
                    return "zero";
                case PluralCategory.One:
                    return "one";
                case PluralCategory.Two:
                    return "two";
                case PluralCategory.Few:
                    return "few";
                case PluralCategory.Many:
                    return "many";
                default:
                    return "other";
            }
        }

        /// <summary>Parses a CLDR category key; false for anything else.</summary>
        public static bool TryParse(string key, out PluralCategory category)
        {
            switch (key)
            {
                case "zero":
                    category = PluralCategory.Zero;
                    return true;
                case "one":
                    category = PluralCategory.One;
                    return true;
                case "two":
                    category = PluralCategory.Two;
                    return true;
                case "few":
                    category = PluralCategory.Few;
                    return true;
                case "many":
                    category = PluralCategory.Many;
                    return true;
                case "other":
                    category = PluralCategory.Other;
                    return true;
                default:
                    category = PluralCategory.Other;
                    return false;
            }
        }

        /// <summary>
        /// The rule family of a language code: "en" (en, de), "es" (es, it: one = 1, many = millions), "fr" (fr, pt-BR:
        /// one = 0 or 1, many = millions), "ru", "pl", "tr", or "other" (ja, ko, zh and anything unknown).
        /// </summary>
        internal static string Family(string language)
        {
            string lang = Languages.BaseLanguage(language);
            switch (lang)
            {
                case "en":
                case "de":
                case "qps": // pseudo-localization follows English
                    return "en";
                case "es":
                case "it":
                    return "es";
                case "fr":
                case "pt":
                    return "fr";
                case "ru":
                    return "ru";
                case "pl":
                    return "pl";
                case "tr":
                    return "tr";
                default:
                    return "other";
            }
        }

        internal static bool IsIntegral(object value)
        {
            return value is int || value is long || value is short || value is byte || value is uint || value is ushort ||
                   value is sbyte || value is ulong;
        }

        internal static long ToLong(object value)
        {
            if (value is ulong u)
            {
                return u > long.MaxValue ? long.MaxValue : (long)u;
            }

            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
