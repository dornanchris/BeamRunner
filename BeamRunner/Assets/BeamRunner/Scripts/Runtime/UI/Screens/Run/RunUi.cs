using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Small shared helpers for the run-flow screens (HUD, pause, revive, results): allocation-light number
    /// formatting, cached short strings, full-bleed dimmers and raycast hygiene.
    /// </summary>
    internal static class RunUi
    {
        /// <summary>
        /// Multiplier prefix. Plain ASCII "x" to match the meta screens (the bundled TTFs do contain U+00D7, U+00B7 and
        /// U+2014, verified with fontTools, but have no glyphs for the diamond or lightning bolt: use IconId.Gem / Bolt).
        /// </summary>
        public const string Times = "x";

        /// <summary>Middle dot separator.</summary>
        public const string Dot = " \u00B7 ";

        /// <summary>Em dash.</summary>
        public const string Dash = " \u2014 ";

        /// <summary>How far full-bleed images reach past a safe-area layer (covers notches / rounded corners).</summary>
        public const float Overscan = 600f;

        private static readonly char[] Buffer = new char[96];
        private static readonly Dictionary<string, string[]> PatternCache = new Dictionary<string, string[]>();
        private static LanguageInfo s_patternLanguage;
        private static readonly string[] MultiplierCache = new string[33];
        private static readonly string[] SmallNumberCache = new string[100];

        /// <summary>
        /// Formats <paramref name="value"/> with the current language's thousands separator ("12,345", "12.345",
        /// "12 345"; CLDR minimum grouping) plus an optional suffix. Builds into a reused buffer, so the only allocation is
        /// the returned string.
        /// </summary>
        public static string Grouped(long value, string suffix = null)
        {
            return Grouped(value, null, suffix);
        }

        /// <summary><see cref="Grouped(long, string)"/> with a prefix too.</summary>
        public static string Grouped(long value, string prefix, string suffix)
        {
            LanguageInfo lang = Loc.Language;
            string sep = lang.GroupSeparator ?? string.Empty;
            char separator = sep.Length > 0 ? sep[0] : '\0';
            int suffixLength = suffix != null ? Mathf.Min(suffix.Length, 32) : 0;
            int prefixLength = prefix != null ? Mathf.Min(prefix.Length, 40) : 0;
            int pos = Buffer.Length - suffixLength;
            for (int i = 0; i < suffixLength; i++)
            {
                Buffer[pos + i] = suffix[i];
            }

            bool negative = value < 0;
            ulong v = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
            int totalDigits = 1;
            for (ulong t = v / 10UL; t != 0UL; t /= 10UL)
            {
                totalDigits++;
            }

            bool group = separator != '\0' && totalDigits >= 3 + lang.MinGroupingDigits;
            int digits = 0;
            do
            {
                if (group && digits > 0 && digits % 3 == 0)
                {
                    Buffer[--pos] = separator;
                }

                Buffer[--pos] = (char)('0' + (int)(v % 10UL));
                v /= 10UL;
                digits++;
            }
            while (v != 0UL);

            if (negative)
            {
                Buffer[--pos] = '-';
            }

            for (int i = prefixLength - 1; i >= 0; i--)
            {
                Buffer[--pos] = prefix[i];
            }

            return new string(Buffer, pos, Buffer.Length - pos);
        }

        /// <summary>
        /// A number inside a simple one-number string-table pattern (e.g. "hud.distance" = "{n} M"), built without per-call
        /// formatting work: the pattern is split once per language at its <c>{n}</c>. For HUD texts that change every frame.
        /// </summary>
        public static string Formatted(string key, long value)
        {
            string[] parts = Pattern(key);
            return Grouped(value, parts[0], parts[1]);
        }

        /// <summary>
        /// The text before and after <c>{n}</c> in a one-number string-table pattern (cached per language), for callers
        /// that add a cached prefix of their own: <c>Grouped(value, myPrefix + before, after)</c> built once per change of
        /// the prefix.
        /// </summary>
        public static void Pattern(string key, out string before, out string after)
        {
            string[] parts = Pattern(key);
            before = parts[0];
            after = parts[1];
        }

        private static string[] Pattern(string key)
        {
            if (!ReferenceEquals(s_patternLanguage, Loc.Language))
            {
                PatternCache.Clear();
                s_patternLanguage = Loc.Language;
            }

            string[] parts;
            if (!PatternCache.TryGetValue(key, out parts))
            {
                string pattern = Loc.T(key) ?? string.Empty;
                int at = pattern.IndexOf("{n}", System.StringComparison.Ordinal);
                parts = at >= 0
                    ? new[] { pattern.Substring(0, at), pattern.Substring(at + 3) }
                    : new[] { string.Empty, " " + pattern };
                PatternCache[key] = parts;
            }

            return parts;
        }

        /// <summary>"×3" (cached).</summary>
        public static string Multiplier(int m)
        {
            if (m < 0 || m >= MultiplierCache.Length)
            {
                return Times + m;
            }

            string s = MultiplierCache[m];
            if (s == null)
            {
                s = Times + m;
                MultiplierCache[m] = s;
            }

            return s;
        }

        /// <summary>Non-negative integer below 100 as a cached string.</summary>
        public static string Small(int n)
        {
            if (n < 0 || n >= SmallNumberCache.Length)
            {
                return n.ToString();
            }

            string s = SmallNumberCache[n];
            if (s == null)
            {
                s = n.ToString();
                SmallNumberCache[n] = s;
            }

            return s;
        }

        /// <summary>Seconds as "m:ss" (or "h:mm:ss" past an hour).</summary>
        public static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int h = total / 3600;
            int m = (total / 60) % 60;
            int s = total % 60;
            if (h > 0)
            {
                return h + ":" + m.ToString("00") + ":" + s.ToString("00");
            }

            return m + ":" + s.ToString("00");
        }

        /// <summary>"34 / 40" style progress text for a mission.</summary>
        public static string MissionProgress(Mission m, long progress)
        {
            if (m == null)
            {
                return string.Empty;
            }

            long target = m.Target;
            if (progress > target)
            {
                progress = target;
            }

            return Grouped(progress) + " / " + Grouped(target);
        }

        /// <summary>
        /// Full-bleed tinted image behind a screen's content: stretched past the (safe-area) parent by
        /// <see cref="Overscan"/> so it also covers notches. Moved to the back of <paramref name="parent"/>.
        /// </summary>
        public static Image FullBleed(Transform parent, Color color, bool raycast, string name = "Dim")
        {
            Image img = UIFactory.CreateImage(parent, null, color, name, raycast);
            UIFactory.Stretch(img.rectTransform, -Overscan, -Overscan, -Overscan, -Overscan);
            img.transform.SetAsFirstSibling();
            UIFactory.IgnoreLayout(img);
            return img;
        }

        /// <summary>
        /// Turns off raycasts on every graphic under <paramref name="root"/> that is not part of a
        /// <see cref="Selectable"/> — gameplay overlays must never swallow steering touches.
        /// </summary>
        public static void StripDecorRaycasts(Transform root)
        {
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic g = graphics[i];
                if (g.raycastTarget && g.GetComponentInParent<Selectable>(true) == null)
                {
                    g.raycastTarget = false;
                }
            }
        }

        /// <summary>Adds (or returns) a CanvasGroup on <paramref name="c"/>.</summary>
        public static CanvasGroup Group(Component c)
        {
            CanvasGroup g = c.GetComponent<CanvasGroup>();
            if (g == null)
            {
                g = c.gameObject.AddComponent<CanvasGroup>();
            }

            return g;
        }

        /// <summary>Non-interactive CanvasGroup (never blocks raycasts).</summary>
        public static CanvasGroup PassiveGroup(Component c)
        {
            CanvasGroup g = Group(c);
            g.blocksRaycasts = false;
            g.interactable = false;
            return g;
        }

        /// <summary>Upper-case caption label (muted, bold, small).</summary>
        public static Text Caption(Transform parent, string text, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null)
        {
            return UIFactory.LabelLine(parent, text, UITheme.TextCaption, color ?? UITheme.TextMuted, align, UIFont.Bold);
        }

        /// <summary>Rich-text colour tag for <paramref name="c"/>.</summary>
        public static string ColorTag(Color c)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">";
        }
    }
}
