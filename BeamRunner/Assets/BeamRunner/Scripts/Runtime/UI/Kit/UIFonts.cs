using BeamRunner.Core.Localization;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>Font roles used by the UI kit.</summary>
    public enum UIFont
    {
        /// <summary>Body copy, descriptions (Latin: Rajdhani SemiBold).</summary>
        Body = 0,

        /// <summary>Labels, numbers, emphasis (Latin: Rajdhani Bold).</summary>
        Bold = 1,

        /// <summary>Display titles, buttons, big numbers (Latin: Audiowide).</summary>
        Title = 2,
    }

    /// <summary>
    /// Bundled OFL fonts (Resources/BeamRunner/Fonts), one set per writing system, chosen by the current language's
    /// <see cref="LocScript"/> (<see cref="Loc.Language"/>): Latin = Audiowide / Rajdhani SemiBold / Rajdhani Bold;
    /// Cyrillic = Neo Title Cyrillic (Russo One subset) / Tektur condensed; Japanese, Korean, Simplified Chinese = Noto
    /// Sans JP/KR/SC Bold (titles, bold) and Medium (body). The non-Latin fonts are subsets holding exactly the glyphs
    /// of their languages' string tables (tools/build-fonts.py). Fonts load lazily, so only the current script's set is
    /// in memory. Falls back to Unity's built-in LegacyRuntime font when a font is missing, so text always renders.
    /// <para>The language only changes through a UI rebuild (GameUI), so a label keeps the font it was built with.</para>
    /// </summary>
    public static class UIFonts
    {
        public const string ResourceFolder = "BeamRunner/Fonts/";
        public const string TitleName = "Audiowide-Regular";
        public const string BodyName = "Rajdhani-SemiBold";
        public const string BoldName = "Rajdhani-Bold";

        // [script, role] with role = UIFont (Body, Bold, Title).
        private static readonly string[,] Names =
        {
            { BodyName, BoldName, TitleName },
            { "Tektur-Condensed-Medium", "Tektur-Condensed-SemiBold", "FieldTitleCyrillic-Regular" },
            { "NotoSansJP-Medium", "NotoSansJP-Bold", "NotoSansJP-Bold" },
            { "NotoSansKR-Medium", "NotoSansKR-Bold", "NotoSansKR-Bold" },
            { "NotoSansSC-Medium", "NotoSansSC-Bold", "NotoSansSC-Bold" },
        };

        private const int ScriptCount = 5;
        private const int RoleCount = 3;

        private static readonly Font[,] s_cache = new Font[ScriptCount, RoleCount];
        private static Font _fallback;

        /// <summary>The writing system of the current language.</summary>
        public static LocScript Script
        {
            get { return Loc.Language.Script; }
        }

        /// <summary>Display face of the current script (Latin: Audiowide).</summary>
        public static Font Title
        {
            get { return Get(UIFont.Title); }
        }

        /// <summary>Body face of the current script (Latin: Rajdhani SemiBold).</summary>
        public static Font Body
        {
            get { return Get(UIFont.Body); }
        }

        /// <summary>Bold face of the current script (Latin: Rajdhani Bold).</summary>
        public static Font Bold
        {
            get { return Get(UIFont.Bold); }
        }

        /// <summary>Unity's built-in runtime font.</summary>
        public static Font Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    try
                    {
                        _fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                    catch (System.Exception)
                    {
                        _fallback = null;
                    }
                }

                return _fallback;
            }
        }

        /// <summary>The <paramref name="font"/> role in the current language's script.</summary>
        public static Font Get(UIFont font)
        {
            return For(Script, font);
        }

        /// <summary>
        /// The <paramref name="font"/> role of <paramref name="script"/> — e.g. to show each language's own name in the
        /// language picker, or the Latin brand logo whatever the language.
        /// </summary>
        public static Font For(LocScript script, UIFont font)
        {
            int s = (int)script;
            int r = (int)font;
            if (s < 0 || s >= ScriptCount)
            {
                s = 0;
            }

            if (r < 0 || r >= RoleCount)
            {
                r = 0;
            }

            Font f = s_cache[s, r];
            if (f == null)
            {
                f = Load(Names[s, r]);
                if (f == null && s != 0)
                {
                    f = For(LocScript.Latin, (UIFont)r);
                }

                s_cache[s, r] = f != null ? f : Fallback;
                f = s_cache[s, r];
            }

            return f;
        }

        /// <summary>Resource name (without extension) of a script's font role (tests, tools).</summary>
        public static string FileName(LocScript script, UIFont font)
        {
            return Names[Mathf.Clamp((int)script, 0, ScriptCount - 1), Mathf.Clamp((int)font, 0, RoleCount - 1)];
        }

        private static Font Load(string fileName)
        {
            try
            {
                return Resources.Load<Font>(ResourceFolder + fileName);
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
