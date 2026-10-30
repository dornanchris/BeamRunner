using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BeamRunner.Core.Localization
{
    /// <summary>
    /// Named-argument formatting ("{n} M AHEAD") and locale-aware numbers. Placeholders are <c>{name}</c> with
    /// name = [A-Za-z_][A-Za-z0-9_]*; anything else in braces is literal text. Integer arguments are formatted with the
    /// language's digit grouping, strings are inserted as they are. Never uses <see cref="CultureInfo.CurrentCulture"/>,
    /// so the device culture cannot change what the game shows (or writes: saves/JSON never go through here).
    /// </summary>
    public static class LocFormat
    {
        /// <summary>Replaces every <c>{name}</c> of <paramref name="pattern"/> with the matching argument (unknown ones stay).</summary>
        public static string Apply(string pattern, LanguageInfo language, (string name, object value)[] args)
        {
            if (string.IsNullOrEmpty(pattern) || args == null || args.Length == 0 || pattern.IndexOf('{') < 0)
            {
                return pattern ?? string.Empty;
            }

            var sb = new StringBuilder(pattern.Length + 16);
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                if (c == '{')
                {
                    int end;
                    string name = ReadName(pattern, i, out end);
                    if (name != null)
                    {
                        int index = Find(args, name);
                        if (index >= 0)
                        {
                            sb.Append(ValueText(args[index].value, language));
                            i = end + 1;
                            continue;
                        }
                    }
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>The distinct placeholder names of <paramref name="pattern"/>, in order of first use.</summary>
        public static List<string> Placeholders(string pattern)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(pattern))
            {
                return names;
            }

            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != '{')
                {
                    continue;
                }

                int end;
                string name = ReadName(pattern, i, out end);
                if (name != null && !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        /// <summary>
        /// <paramref name="value"/> with the language's thousands separator (CLDR grouping, minimumGroupingDigits):
        /// en 12,345 · de 12.345 · fr/ru 12 345 · es 1234 but 12.345.
        /// </summary>
        public static string Integer(long value, LanguageInfo language)
        {
            LanguageInfo lang = language ?? Languages.English;
            bool negative = value < 0;
            ulong magnitude = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
            string digits = magnitude.ToString(CultureInfo.InvariantCulture);
            string sep = lang.GroupSeparator ?? string.Empty;
            bool group = sep.Length > 0 && digits.Length > 3 && digits.Length >= 3 + lang.MinGroupingDigits;
            if (!group)
            {
                return negative ? "-" + digits : digits;
            }

            var sb = new StringBuilder(digits.Length + digits.Length / 3 * sep.Length + 1);
            if (negative)
            {
                sb.Append('-');
            }

            int head = digits.Length % 3;
            if (head == 0)
            {
                head = 3;
            }

            sb.Append(digits, 0, head);
            for (int i = head; i < digits.Length; i += 3)
            {
                sb.Append(sep).Append(digits, i, 3);
            }

            return sb.ToString();
        }

        /// <summary>
        /// A decimal number with exactly <paramref name="decimals"/> fraction digits (rounded half away from zero), the
        /// language's decimal separator and grouped integer part: en 1.5 · de 1,5 · fr 1 234,5.
        /// </summary>
        public static string Decimal(double value, int decimals, LanguageInfo language)
        {
            LanguageInfo lang = language ?? Languages.English;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0.0;
            }

            decimals = Math.Max(0, Math.Min(6, decimals));
            double scale = Math.Pow(10.0, decimals);
            double rounded = Math.Round(Math.Abs(value) * scale, MidpointRounding.AwayFromZero);
            if (rounded > 9.0e17)
            {
                rounded = 9.0e17;
            }

            long scaled = (long)rounded;
            long whole = decimals == 0 ? scaled : scaled / (long)scale;
            long frac = decimals == 0 ? 0 : scaled % (long)scale;
            bool negative = value < 0 && scaled != 0;
            string text = Integer(whole, lang);
            if (decimals > 0)
            {
                text += lang.DecimalSeparator + frac.ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0');
            }

            return negative ? "-" + text : text;
        }

        private static string ValueText(object value, LanguageInfo language)
        {
            if (value == null)
            {
                return string.Empty;
            }

            string s = value as string;
            if (s != null)
            {
                return s;
            }

            if (PluralRules.IsIntegral(value))
            {
                return Integer(PluralRules.ToLong(value), language);
            }

            if (value is double d)
            {
                return Integer((long)Math.Floor(d), language);
            }

            if (value is float f)
            {
                return Integer((long)Math.Floor(f), language);
            }

            IFormattable formattable = value as IFormattable;
            return formattable != null ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
        }

        private static int Find((string name, object value)[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i].name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The placeholder name starting at the '{' at <paramref name="open"/>, or null if it is not one.</summary>
        internal static string ReadName(string s, int open, out int close)
        {
            close = -1;
            int i = open + 1;
            if (i >= s.Length || !IsNameStart(s[i]))
            {
                return null;
            }

            int start = i;
            while (i < s.Length && IsNamePart(s[i]))
            {
                i++;
            }

            if (i >= s.Length || s[i] != '}')
            {
                return null;
            }

            close = i;
            return s.Substring(start, i - start);
        }

        private static bool IsNameStart(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        private static bool IsNamePart(char c)
        {
            return IsNameStart(c) || (c >= '0' && c <= '9');
        }
    }
}
