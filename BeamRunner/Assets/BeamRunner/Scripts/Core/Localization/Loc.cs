using System;

namespace BeamRunner.Core.Localization
{
    /// <summary>
    /// The game's string lookup (Core and Unity layer): <c>Loc.T("menu.play")</c>,
    /// <c>Loc.T("hud.ghost_ahead", ("n", 42))</c>, <c>Loc.N(score)</c>. Every user-facing text goes through here
    /// (tools/check-strings.py enforces it); keys live in <c>Resources/BeamRunner/Localization/&lt;code&gt;.json</c>.
    /// <para>Until the Unity layer installs the loaded tables (<see cref="Install"/>), lookups return their key, which is
    /// what Core tests without tables see. <see cref="LanguageChanged"/> fires after <see cref="SetLanguage"/>; the UI
    /// rebuilds itself on it. Main thread only.</para>
    /// </summary>
    public static class Loc
    {
        private static Localizer s_current = new Localizer(null);

        /// <summary>Raised after the language changed (the UI rebuilds its screens).</summary>
        public static event Action LanguageChanged;

        /// <summary>The active localizer (never null).</summary>
        public static Localizer Current
        {
            get { return s_current; }
        }

        /// <summary>The current language (English until one is installed).</summary>
        public static LanguageInfo Language
        {
            get { return s_current.Language; }
        }

        /// <summary>Replaces the localizer (e.g. once the English table is loaded). Does not raise <see cref="LanguageChanged"/>.</summary>
        public static void Install(Localizer localizer)
        {
            s_current = localizer ?? new Localizer(null);
        }

        /// <summary>Switches the language of the current localizer and raises <see cref="LanguageChanged"/> if it changed.</summary>
        public static void SetLanguage(LanguageInfo language, StringTable table)
        {
            LanguageInfo before = s_current.Language;
            s_current.SetLanguage(language, table);
            if (!ReferenceEquals(before, s_current.Language))
            {
                RaiseLanguageChanged();
            }
        }

        /// <summary>Raises <see cref="LanguageChanged"/> (handlers' exceptions are swallowed so every subscriber runs).</summary>
        public static void RaiseLanguageChanged()
        {
            Action handler = LanguageChanged;
            if (handler == null)
            {
                return;
            }

            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action)list[i])();
                }
                catch (Exception)
                {
                    // One broken listener must not stop the others (the Unity layer logs through its own handlers).
                }
            }
        }

        /// <summary>The text for <paramref name="key"/> in the current language (fallback: English, then the key).</summary>
        public static string T(string key)
        {
            return s_current.Get(key);
        }

        /// <summary>
        /// The text for <paramref name="key"/> with named arguments, e.g. <c>Loc.T("hud.ghost_ahead", ("n", 42))</c>.
        /// Integers are grouped per language; a plural entry chooses its form from the argument <c>n</c>.
        /// </summary>
        public static string T(string key, params (string name, object value)[] args)
        {
            return s_current.Format(key, args);
        }

        /// <summary>An integer with the current language's thousands separator (12,345 · 12.345 · 12 345).</summary>
        public static string N(long value)
        {
            return s_current.Number(value);
        }

        /// <summary>The whole part of <paramref name="value"/> (floored, never negative) with digit grouping.</summary>
        public static string N(double value)
        {
            return s_current.Number((long)Math.Floor(Math.Max(0.0, value)));
        }

        /// <summary><paramref name="value"/> with <paramref name="decimals"/> fraction digits (1.5 · 1,5).</summary>
        public static string N(double value, int decimals)
        {
            return s_current.Number(value, decimals);
        }

        /// <summary>Back to an empty English localizer and no listeners (static reset for tests / domain reloads).</summary>
        public static void Reset()
        {
            s_current = new Localizer(null);
            LanguageChanged = null;
        }
    }
}
