using System;
using BeamRunner.Core;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Static setters for every global shader property of docs/ARCHITECTURE.md §4 and theme application.
    /// </summary>
    /// <remarks>
    /// <para><b>Colour spaces.</b> Colours are passed in as sRGB (like everything authored in
    /// <see cref="ZoneThemes"/>). <c>Shader.SetGlobalColor</c> does <i>not</i> convert sRGB → linear (in
    /// Unity 6000.0 it forwards the raw value to <c>SetGlobalVectorImpl</c>), so this class converts
    /// explicitly with <see cref="Color.linear"/> when the project uses linear colour space. Material and
    /// MaterialPropertyBlock <c>SetColor</c> calls <i>do</i> convert, so per-material colours elsewhere are
    /// passed as plain sRGB. All colours stay LDR (0..1); shaders apply HDR intensity themselves, which keeps
    /// gamma and linear projects consistent.</para>
    /// <para><b>No curved world.</b> The tunnel game bent the world ahead of the ship; BeamRunner draws everything where
    /// the collision is, because the player aims at gate openings from far away.</para>
    /// <para><b>Per-frame order.</b> <see cref="SetScroll"/>, <see cref="SetSpeed01"/>, <see cref="SetBeat"/> and
    /// <see cref="SetFlash"/> once per rendered frame, before rendering.</para>
    /// </remarks>
    public static class FieldGlobals
    {
        /// <summary>
        /// The floor's beat ring launches on every this-many beats: every other bar of 4/4, about every 4 s in a run
        /// (116 BPM). On every beat it was visual clutter.
        /// </summary>
        public const int BeatRingEvery = 8;

        /// <summary>Fog start (metres from the camera): everything nearer is fully clear.</summary>
        public const float DefaultFogStart = 40f;

        /// <summary>
        /// Fog end (metres from the camera). Content spawns <see cref="Core.Field.FieldSpec.SpawnAhead"/> (190 m) ahead of
        /// the craft, about 197 m from the chase camera, so it fades in out of the fog instead of popping in.
        /// </summary>
        public const float DefaultFogEnd = 190f;

        /// <summary>Strength of the glow where the floor meets the fog (the sky continues it at its horizon).</summary>
        public const float DefaultEndGlow = 0.8f;

        /// <summary>Strength of the sky's thin horizon band and haze (obstacles fade toward the same sky colour).</summary>
        public const float DefaultHorizonBand = 1f;

        /// <summary>Default fake key light: above, a little to the right and in front of the craft.</summary>
        public static readonly Vector3 DefaultLightDirection = new Vector3(0.35f, 0.8f, -0.45f);

        private static Color _colorA;
        private static Color _colorB;
        private static Color _colorC;
        private static Color _baseColor;
        private static Color _fogColor;
        private static Vector3 _lightDirection;
        private static float _flash;
        private static bool _reducedFlashing;
        private static int _quality = 2;
        private static double _lastScrollDistance;
        private static double _scrollDistance;
        private static int _scrollFrame = -1;
        private static float _scrollDelta;

        /// <summary>Current level primary colour (sRGB).</summary>
        public static Color PrimaryColor
        {
            get { return _colorA; }
        }

        /// <summary>Current level secondary colour (sRGB).</summary>
        public static Color SecondaryColor
        {
            get { return _colorB; }
        }

        /// <summary>Current level accent colour (sRGB).</summary>
        public static Color AccentColor
        {
            get { return _colorC; }
        }

        /// <summary>Current dark base colour of the floor and obstacle bodies (sRGB).</summary>
        public static Color BaseColor
        {
            get { return _baseColor; }
        }

        /// <summary>Current fog colour (sRGB) — also used as the camera clear colour.</summary>
        public static Color FogColor
        {
            get { return _fogColor; }
        }

        /// <summary>Track distance last passed to <see cref="SetScroll"/> (things that scroll with the grid read it).</summary>
        public static double ScrollDistance
        {
            get { return _scrollDistance; }
        }

        /// <summary>Metres travelled between the last two rendered frames (0 when standing still or after a jump).</summary>
        public static float ScrollDelta
        {
            get { return _scrollDelta; }
        }

        /// <summary>Current fake key light direction (unit, world space, pointing toward the light).</summary>
        public static Vector3 LightDirection
        {
            get { return _lightDirection; }
        }

        public static bool ReducedFlashing
        {
            get { return _reducedFlashing; }
        }

        /// <summary>Palette colour for a slot (0 = primary, 1 = secondary, 2 = accent), sRGB.</summary>
        public static Color PaletteColor(int slot)
        {
            switch (slot)
            {
                case 1:
                    return _colorB;
                case 2:
                    return _colorC;
                default:
                    return _colorA;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            ResetDefaults();
        }

        /// <summary>Sets every global to a sane default (level 1 palette, default fog and light).</summary>
        public static void ResetDefaults()
        {
            _scrollFrame = -1;
            _lastScrollDistance = 0.0;
            _scrollDistance = 0.0;
            ApplyTheme(ZoneThemes.ForLevel(1));
            SetFog(DefaultFogStart, DefaultFogEnd, DefaultEndGlow, DefaultHorizonBand);
            SetLightDirection(DefaultLightDirection);
            SetScroll(0.0);
            SetBeat(0f, 0.0);
            SetSpeed01(0f);
            SetFlash(0f);
            SetQuality(_quality);
        }

        // ------------------------------------------------------------------ colour helpers

        /// <summary>Converts a Core colour (sRGB) to a Unity colour (sRGB, unchanged values).</summary>
        public static Color ToColor(ColorRGB c)
        {
            return new Color(c.R, c.G, c.B, c.A);
        }

        /// <summary>Converts an sRGB colour to the values a shader global expects in the active colour space.</summary>
        public static Color ToShaderSpace(Color srgb)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;
        }

        private static void SetGlobalColorSrgb(int id, Color srgb)
        {
            Shader.SetGlobalColor(id, ToShaderSpace(srgb));
        }

        // ------------------------------------------------------------------ palette / theme

        public static void SetColorA(Color srgb)
        {
            _colorA = srgb;
            SetGlobalColorSrgb(ShaderIds.FieldColorA, srgb);
        }

        public static void SetColorB(Color srgb)
        {
            _colorB = srgb;
            SetGlobalColorSrgb(ShaderIds.FieldColorB, srgb);
        }

        public static void SetColorC(Color srgb)
        {
            _colorC = srgb;
            SetGlobalColorSrgb(ShaderIds.FieldColorC, srgb);
        }

        public static void SetBaseColor(Color srgb)
        {
            _baseColor = srgb;
            SetGlobalColorSrgb(ShaderIds.FieldBaseColor, srgb);
        }

        public static void SetFogColor(Color srgb)
        {
            _fogColor = srgb;
            SetGlobalColorSrgb(ShaderIds.FieldFogColor, srgb);
        }

        /// <summary>Applies a level palette instantly.</summary>
        public static void ApplyTheme(ZoneTheme theme)
        {
            if (theme == null)
            {
                return;
            }

            SetColorA(ToColor(theme.Primary));
            SetColorB(ToColor(theme.Secondary));
            SetColorC(ToColor(theme.Accent));
            SetBaseColor(ToColor(theme.Base));
            SetFogColor(ToColor(theme.Fog));
        }

        /// <summary>Cross-fades two level palettes (t = 0 → from, 1 → to). Call every frame during a transition.</summary>
        public static void LerpTheme(ZoneTheme from, ZoneTheme to, float t)
        {
            if (from == null || to == null)
            {
                ApplyTheme(to ?? from);
                return;
            }

            t = Mathf.Clamp01(t);
            SetColorA(ToColor(ColorRGB.Lerp(from.Primary, to.Primary, t)));
            SetColorB(ToColor(ColorRGB.Lerp(from.Secondary, to.Secondary, t)));
            SetColorC(ToColor(ColorRGB.Lerp(from.Accent, to.Accent, t)));
            SetBaseColor(ToColor(ColorRGB.Lerp(from.Base, to.Base, t)));
            SetFogColor(ToColor(ColorRGB.Lerp(from.Fog, to.Fog, t)));
        }

        // ------------------------------------------------------------------ fog, motion, pulses

        /// <summary>
        /// Radial fog from <paramref name="start"/> to <paramref name="end"/> metres from the camera; endGlow brightens
        /// the horizon where the floor meets the fog; horizonBand scales the sky's band and haze above the horizon.
        /// Opaque obstacles fade toward the same floor / sky colours (FieldFogOpaque in FieldCommon.cginc).
        /// </summary>
        public static void SetFog(float start, float end, float endGlow, float horizonBand = DefaultHorizonBand)
        {
            Shader.SetGlobalVector(ShaderIds.FieldFogParams, new Vector4(start, end, endGlow, horizonBand));
        }

        /// <summary>Direction toward the fake key light that shades NeonSolid bodies (normalised; zero falls back to up).</summary>
        public static void SetLightDirection(Vector3 towardLight)
        {
            _lightDirection = towardLight.sqrMagnitude > 1e-8f ? towardLight.normalized : Vector3.up;
            Shader.SetGlobalVector(ShaderIds.FieldLightDir,
                new Vector4(_lightDirection.x, _lightDirection.y, _lightDirection.z, 0f));
        }

        /// <summary>
        /// Scrolls the floor / ceiling grid to the track distance (sent as distance mod 1000). Also derives the
        /// per-frame travel used to motion-blur the grid's cross lines, so call it once per rendered frame.
        /// </summary>
        public static void SetScroll(double distance)
        {
            _scrollDistance = distance;
            double wrapped = distance % 1000.0;
            if (wrapped < 0.0)
            {
                wrapped += 1000.0;
            }

            int frame = Time.frameCount;
            if (frame != _scrollFrame)
            {
                double delta = distance - _lastScrollDistance;
                _scrollDelta = delta > 0.0 && delta < 25.0 ? (float)delta : 0f;
                _lastScrollDistance = distance;
                _scrollFrame = frame;
            }

            Shader.SetGlobalFloat(ShaderIds.FieldScroll, (float)wrapped);
            Shader.SetGlobalFloat(ShaderIds.FieldScrollDelta, _scrollDelta);
        }

        /// <summary>
        /// Music beat. <paramref name="beat01"/> (1 on each beat, decaying) throbs the grid on every beat; the ring of light
        /// that flies away over the floor launches only on every <see cref="BeatRingEvery"/>-th beat.
        /// <paramref name="beatTime"/> is beats elapsed since the arrangement started (beat 0 is a bar downbeat).
        /// </summary>
        public static void SetBeat(float beat01, double beatTime)
        {
            beat01 = Mathf.Clamp01(beat01);
            bool ringBeat = (long)Math.Floor(beatTime) % BeatRingEvery == 0;
            Shader.SetGlobalFloat(ShaderIds.FieldBeat, beat01);
            Shader.SetGlobalFloat(ShaderIds.FieldBeatRing, ringBeat ? beat01 : 0f);
        }

        /// <summary>Normalised speed (0..1, up to ~1.6 in Overdrive) driving streaks and speed lines.</summary>
        public static void SetSpeed01(float speed01)
        {
            Shader.SetGlobalFloat(ShaderIds.FieldSpeed01, Mathf.Clamp(speed01, 0f, 1.6f));
        }

        /// <summary>World flash 0..1. Scaled down when reduced flashing is on.</summary>
        public static void SetFlash(float flash01)
        {
            _flash = Mathf.Clamp01(flash01);
            Shader.SetGlobalFloat(ShaderIds.FieldFlash, _flash * (_reducedFlashing ? 0.2f : 1f));
        }

        /// <summary>Accessibility: tones down world flashes.</summary>
        public static void SetReducedFlashing(bool reduced)
        {
            _reducedFlashing = reduced;
            SetFlash(_flash);
        }

        /// <summary>Shader quality tier 0..2. Tier 0 enables the FIELD_LITE keyword (no lane pulses / speed streaks on the grid).</summary>
        public static void SetQuality(int level)
        {
            _quality = Mathf.Clamp(level, 0, 2);
            if (_quality == 0)
            {
                Shader.EnableKeyword("FIELD_LITE");
            }
            else
            {
                Shader.DisableKeyword("FIELD_LITE");
            }
        }
    }
}
