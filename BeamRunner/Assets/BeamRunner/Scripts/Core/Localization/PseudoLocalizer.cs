using System.Text;

namespace BeamRunner.Core.Localization
{
    /// <summary>
    /// Pseudo-localization ("qps-ploc"): English with every letter swapped for an accented look-alike and ~35 % padding
    /// inside brackets, e.g. "PLAY" → "[ÞĿÀŸ ~~]". Text that is still plain English on screen was never localized; a
    /// missing bracket means the label was truncated. Placeholders (<c>{n}</c>), rich-text tags (<c>&lt;color=…&gt;</c>)
    /// and escapes stay intact. Every substitute is a Latin-1/Latin Extended-A letter covered by Audiowide and Rajdhani
    /// (checked by tools/check-strings.py), so it renders with the Latin font set.
    /// </summary>
    public static class PseudoLocalizer
    {
        /// <summary>Target expansion (German/Russian/Polish run 30–40 % longer than English).</summary>
        public const float Expansion = 0.35f;

        // Index = letter - 'A' / letter - 'a'.
        private const string Upper = "ÀßÇÐÉFĜĦÎĴĶĿMÑÖÞQŔŠŢÛVŴXŸŽ";
        private const string Lower = "àbçðéfĝĥîĵķľmñöþqŕšţûvŵxÿž";

        /// <summary>The pseudo-localized form of <paramref name="text"/>.</summary>
        public static string Transform(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            var sb = new StringBuilder(text.Length * 2 + 4);
            sb.Append('[');
            int letters = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    int close;
                    if (LocFormat.ReadName(text, i, out close) != null)
                    {
                        sb.Append(text, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }
                else if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i && LooksLikeTag(text, i, close))
                    {
                        sb.Append(text, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }

                if (c >= 'A' && c <= 'Z')
                {
                    sb.Append(Upper[c - 'A']);
                    letters++;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    sb.Append(Lower[c - 'a']);
                    letters++;
                }
                else
                {
                    sb.Append(c);
                    if (c != ' ' && c != '\n')
                    {
                        letters++;
                    }
                }

                i++;
            }

            int pad = (int)System.Math.Ceiling(letters * Expansion);
            if (pad > 0)
            {
                sb.Append(' ');
                sb.Append('~', pad);
            }

            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>A pseudo table built from <paramref name="english"/> (every simple string and plural form).</summary>
        public static StringTable BuildTable(StringTable english)
        {
            var table = new StringTable(Languages.PseudoCode);
            if (english == null)
            {
                return table;
            }

            for (int k = 0; k < english.Keys.Count; k++)
            {
                string key = english.Keys[k];
                string[] forms = english.GetForms(key);
                if (forms != null)
                {
                    for (int f = 0; f < forms.Length; f++)
                    {
                        if (forms[f] != null)
                        {
                            forms[f] = Transform(forms[f]);
                        }
                    }

                    table.SetPlural(key, forms);
                }
                else
                {
                    string text;
                    if (english.TryGet(key, out text))
                    {
                        table.Set(key, Transform(text));
                    }
                }
            }

            return table;
        }

        private static bool LooksLikeTag(string s, int open, int close)
        {
            // <color=#RRGGBB>, </color>, <b>, </b>, <size=40>, <i>
            if (close - open < 2)
            {
                return false;
            }

            char first = s[open + 1];
            return first == '/' || first == 'b' || first == 'i' || first == 'c' || first == 's';
        }
    }
}
