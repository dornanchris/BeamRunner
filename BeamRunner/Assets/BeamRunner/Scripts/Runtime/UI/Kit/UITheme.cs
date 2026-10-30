using System;
using BeamRunner.Core;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Design tokens for the UI kit, tuned for a 1080×1920 reference canvas (1 unit ≈ 1 px on a 1080p-wide phone).
    /// Colours are sRGB. <see cref="Accent"/>/<see cref="AccentAlt"/> follow the current level palette via <see cref="SetAccent(ColorRGB, ColorRGB, bool)"/>.
    /// </summary>
    public static class UITheme
    {
        // ------------------------------------------------------------------ surfaces
        public static readonly Color Background = Hex(0x07030F);
        public static readonly Color BackgroundAlt = Hex(0x140A2B);
        /// <summary>Dark translucent glass for panels and cards.</summary>
        public static readonly Color PanelGlass = new Color(0.050f, 0.030f, 0.110f, 0.82f);
        /// <summary>Lighter glass for nested surfaces (rows, inputs, chips).</summary>
        public static readonly Color PanelGlassLight = new Color(0.120f, 0.085f, 0.230f, 0.62f);
        /// <summary>Neutral hairline border (non-accent).</summary>
        public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.13f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color Dimmer = new Color(0.010f, 0.000f, 0.035f, 0.76f);
        public static readonly Color Shadow = new Color(0f, 0f, 0.02f, 0.62f);

        // ------------------------------------------------------------------ text
        public static readonly Color TextPrimary = Hex(0xF5F3FF);
        public static readonly Color TextSecondary = Hex(0xC4BEE6);
        public static readonly Color TextMuted = Hex(0x7F78A8);
        /// <summary>Dark text for use on bright accent fills.</summary>
        public static readonly Color TextOnAccent = Hex(0x0B0616);

        // ------------------------------------------------------------------ fixed accents
        public static readonly Color AccentCyan = Hex(0x00E5FF);
        public static readonly Color AccentMagenta = Hex(0xFF2BD6);
        public static readonly Color AccentYellow = Hex(0xFFE45C);
        public static readonly Color AccentPurple = Hex(0x9D4DFF);
        public static readonly Color Success = Hex(0x39FFA0);
        public static readonly Color Danger = Hex(0xFF3B5C);
        public static readonly Color DangerAlt = Hex(0xFF7A45);
        public static readonly Color Warning = Hex(0xFFB020);
        /// <summary>Currency (gems).</summary>
        public static readonly Color GemColor = Hex(0x7CF7FF);
        public static readonly Color Gold = Hex(0xFFD24D);

        // ------------------------------------------------------------------ spacing scale (units)
        public const float SpaceXxs = 4f;
        public const float SpaceXs = 8f;
        public const float SpaceSm = 16f;
        public const float SpaceMd = 24f;
        public const float SpaceLg = 32f;
        public const float SpaceXl = 48f;
        public const float SpaceXxl = 64f;
        public const float SpaceXxxl = 96f;

        /// <summary>Default padding between screen content and the safe-area edge.</summary>
        public const float ScreenPadding = 48f;

        /// <summary>Maximum width of a content column (landscape/tablet layouts centre a column of this width).</summary>
        public const float ContentMaxWidth = 1000f;

        // ------------------------------------------------------------------ radius scale
        public const float RadiusSm = 12f;
        public const float RadiusMd = 20f;
        public const float RadiusLg = 32f;
        public const float RadiusXl = 48f;

        // ------------------------------------------------------------------ type scale (font sizes)
        public const int TextCaption = 30;
        public const int TextSmall = 36;
        public const int TextBody = 42;
        public const int TextLead = 48;
        public const int TextH3 = 56;
        public const int TextH2 = 72;
        public const int TextH1 = 96;
        public const int TextDisplay = 132;
        public const int TextHero = 180;

        // ------------------------------------------------------------------ component metrics
        public const float ButtonHeightLarge = 152f;
        public const float ButtonHeight = 128f;
        public const float ButtonHeightSmall = 96f;
        public const float IconButtonSize = 132f;
        public const float IconSize = 64f;
        public const float IconSizeSmall = 44f;
        /// <summary>Minimum comfortable touch target (≈ 48 dp on a 1080p phone).</summary>
        public const float TouchTarget = 120f;
        public const float BorderWidth = 3f;
        public const float GlowSize = 28f;
        public const float RowHeight = 112f;

        // ------------------------------------------------------------------ motion
        public const float DurationFast = 0.12f;
        public const float DurationNormal = 0.26f;
        public const float DurationSlow = 0.45f;

        /// <summary>When true, decorative motion (flicker, breathing, shakes) is reduced. Settings can set this.</summary>
        public static bool ReducedMotion;

        // ------------------------------------------------------------------ dynamic accent
        private static Color _accent = AccentCyan;
        private static Color _accentAlt = AccentMagenta;
        private static int _accentTween;

        /// <summary>Primary accent (frames, highlights, active states).</summary>
        public static Color Accent
        {
            get { return _accent; }
        }

        /// <summary>Secondary accent (second gradient stop, secondary highlights).</summary>
        public static Color AccentAlt
        {
            get { return _accentAlt; }
        }

        /// <summary>Raised whenever <see cref="Accent"/>/<see cref="AccentAlt"/> change (every step while animating).</summary>
        public static event Action AccentChanged;

        /// <summary>Makes the UI follow a level palette (e.g. <c>ZoneThemes.ForLevel(n).Primary/.Secondary</c>).</summary>
        public static void SetAccent(ColorRGB primary, ColorRGB secondary, bool animate = true)
        {
            SetAccent(ToColor(primary), ToColor(secondary), animate);
        }

        public static void SetAccent(Color primary, Color secondary, bool animate = true)
        {
            primary = Clamp01(primary);
            secondary = Clamp01(secondary);
            primary.a = 1f;
            secondary.a = 1f;
            UITween.KillId(_accentTween);
            _accentTween = 0;
            if (!animate || !Application.isPlaying)
            {
                _accent = primary;
                _accentAlt = secondary;
                RaiseAccentChanged();
                return;
            }

            Color fromA = _accent;
            Color fromB = _accentAlt;
            _accentTween = UITween.Value(0f, 1f, 0.8f, t =>
            {
                _accent = Color.Lerp(fromA, primary, t);
                _accentAlt = Color.Lerp(fromB, secondary, t);
                RaiseAccentChanged();
            }).SetEase(UIEase.InOutSine).Id;
        }

        /// <summary>Restores the default cyan / magenta accents.</summary>
        public static void ResetAccent(bool animate = false)
        {
            SetAccent(AccentCyan, AccentMagenta, animate);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Colour from 0xRRGGBB.</summary>
        public static Color Hex(uint rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }

        public static Color ToColor(ColorRGB c)
        {
            return new Color(c.R, c.G, c.B, c.A);
        }

        /// <summary>Same colour with a different alpha.</summary>
        public static Color Alpha(Color c, float alpha)
        {
            c.a = alpha;
            return c;
        }

        /// <summary>Mixes towards white by <paramref name="amount"/> (0..1), keeping alpha.</summary>
        public static Color Lighten(Color c, float amount)
        {
            float a = c.a;
            Color r = Color.Lerp(c, Color.white, amount);
            r.a = a;
            return r;
        }

        /// <summary>Mixes towards black by <paramref name="amount"/> (0..1), keeping alpha.</summary>
        public static Color Darken(Color c, float amount)
        {
            float a = c.a;
            Color r = Color.Lerp(c, Color.black, amount);
            r.a = a;
            return r;
        }

        private static Color Clamp01(Color c)
        {
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
        }

        private static void RaiseAccentChanged()
        {
            Action handler = AccentChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _accent = AccentCyan;
            _accentAlt = AccentMagenta;
            _accentTween = 0;
            AccentChanged = null;
            ReducedMotion = false;
        }
    }
}
