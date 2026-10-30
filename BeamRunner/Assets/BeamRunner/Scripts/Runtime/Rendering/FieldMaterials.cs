using System.Collections.Generic;
using BeamRunner.Core;
using BeamRunner.Core.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Cached material factory. Materials are shared: anything that differs per instance (obstacle palette slot,
    /// glow, flash, intensity) goes through a <see cref="MaterialPropertyBlock"/> (see the helpers below).
    /// Colours are sRGB; Material/MaterialPropertyBlock.SetColor converts them in linear projects.
    /// </summary>
    /// <remarks>
    /// GPU instancing is deliberately not enabled: the materials are created at runtime, so with the default
    /// "Strip Unused" instancing-variant setting the instanced variants would not exist in player builds.
    /// Draw counts are low (tens of renderers) and the shaders are cheap to switch between.
    /// </remarks>
    public static class FieldMaterials
    {
        private static Material _floor;
        private static Material _ceiling;
        private static Material _sky;
        private static Material _obstacle;
        private static Material _obstacleDithered;
        private static Material _hologram;
        private static Material _gem;
        private static readonly Dictionary<long, Material> ObstacleCache = new Dictionary<long, Material>();
        private static readonly Dictionary<long, Material> ShipCache = new Dictionary<long, Material>();
        private static readonly Dictionary<long, Material> HologramCache = new Dictionary<long, Material>();
        private static readonly Dictionary<int, Material> AdditiveCache = new Dictionary<int, Material>();

        /// <summary>The floor under the play field: opaque dark plane with the full neon grid (Grid shader).</summary>
        public static Material Floor()
        {
            if (_floor == null)
            {
                _floor = Create(FieldShaders.Grid, "BeamRunner Floor");
                SetGridDefaults(_floor);
                // The floor lies far below the void: a dim grid, so the rails above it stay the brightest lines.
                _floor.SetFloat(ShaderIds.GridIntensity, 0.4f);
                _floor.SetFloat(ShaderIds.RailIntensity, 0.3f);
                _floor.SetFloat(ShaderIds.StreakIntensity, 0.35f);
                _floor.SetFloat(ShaderIds.BaseAmount, 1f);
                _floor.SetFloat(ShaderIds.SrcBlend, (float)BlendMode.One);
                _floor.SetFloat(ShaderIds.DstBlend, (float)BlendMode.Zero);
                _floor.SetFloat(ShaderIds.ZWrite, 1f);
                _floor.renderQueue = (int)RenderQueue.Geometry;
                SetFallbackColor(_floor, new Color(0.05f, 0.03f, 0.12f));
            }

            return _floor;
        }

        /// <summary>
        /// The ceiling above the play field: the same grid drawn additively with no body (the sky shows through),
        /// dimmer, without speed streaks. Transparent queue.
        /// </summary>
        public static Material Ceiling()
        {
            if (_ceiling == null)
            {
                _ceiling = Create(FieldShaders.Grid, "BeamRunner Ceiling");
                SetGridDefaults(_ceiling);
                _ceiling.SetFloat(ShaderIds.BaseAmount, 0f);
                _ceiling.SetFloat(ShaderIds.GridIntensity, 0.35f);
                _ceiling.SetFloat(ShaderIds.RailIntensity, 0.45f);
                _ceiling.SetFloat(ShaderIds.PulseIntensity, 0.4f);
                _ceiling.SetFloat(ShaderIds.StreakIntensity, 0f);
                _ceiling.SetFloat(ShaderIds.SrcBlend, (float)BlendMode.One);
                _ceiling.SetFloat(ShaderIds.DstBlend, (float)BlendMode.One);
                _ceiling.SetFloat(ShaderIds.ZWrite, 0f);
                _ceiling.renderQueue = (int)RenderQueue.Transparent;
                SetFallbackColor(_ceiling, new Color(0.05f, 0.03f, 0.12f));
            }

            return _ceiling;
        }

        /// <summary>The procedural sky (gradient, horizon band, stars, sun), drawn after the opaque geometry.</summary>
        public static Material Sky(float radius)
        {
            if (_sky == null)
            {
                _sky = Create(FieldShaders.Sky, "BeamRunner Sky");
                SetFallbackColor(_sky, new Color(0.05f, 0.03f, 0.12f));
            }

            _sky.SetFloat(ShaderIds.Radius, radius);
            return _sky;
        }

        private static void SetGridDefaults(Material m)
        {
            m.SetFloat(ShaderIds.GridIntensity, 1f);
            m.SetFloat(ShaderIds.RailIntensity, 1f);
            m.SetFloat(ShaderIds.PulseIntensity, 1f);
            m.SetFloat(ShaderIds.StreakIntensity, 1f);
            m.SetFloat(ShaderIds.CellSize, FieldSpec.CellSize);
            m.SetFloat(ShaderIds.HalfExtent, FieldSpec.HalfExtent);
        }

        /// <summary>
        /// Shared palette-driven obstacle material (NeonSolid). Choose the palette slot, glow and flash per
        /// renderer with <see cref="SetPalette"/> / a property block; defaults to the level primary colour.
        /// </summary>
        public static Material Obstacle()
        {
            if (_obstacle == null)
            {
                _obstacle = Create(FieldShaders.NeonSolid, "BeamRunner Obstacle");
                _obstacle.SetColor(ShaderIds.BaseColor, new Color(0.03f, 0.025f, 0.055f, 1f));
                _obstacle.SetVector(ShaderIds.Palette, ShaderIds.PalettePrimary);
                _obstacle.SetFloat(ShaderIds.EdgeWidth, 0.075f);
                _obstacle.SetFloat(ShaderIds.EdgeIntensity, 3.2f);
                _obstacle.SetFloat(ShaderIds.RimIntensity, 0.55f);
                _obstacle.SetFloat(ShaderIds.BodyGlow, 0.09f);
                SetFallbackColor(_obstacle, FieldGlobals.PrimaryColor);
            }

            return _obstacle;
        }

        /// <summary>
        /// <see cref="Obstacle()"/> with the FIELD_DITHER variant: a screen-space dithered fade by the per-renderer
        /// <c>_Fade</c> (property block) and by the distance to the camera. Swap a renderer to it only while it fades out
        /// (an obstacle the craft has passed, before the chase camera reaches it), then back: the discard in this
        /// variant costs early depth rejection on mobile GPUs.
        /// </summary>
        public static Material ObstacleDithered()
        {
            if (_obstacleDithered == null)
            {
                _obstacleDithered = Create(FieldShaders.NeonSolid, "BeamRunner Obstacle Dithered");
                _obstacleDithered.CopyPropertiesFromMaterial(Obstacle());
                _obstacleDithered.EnableKeyword("FIELD_DITHER");
            }

            return _obstacleDithered;
        }

        /// <summary>Obstacle material with a fixed edge colour (not palette-driven).</summary>
        public static Material Obstacle(Color edge, float glow)
        {
            long key = ColorKey(edge) * 1000L + Mathf.Clamp(Mathf.RoundToInt(glow * 100f), 0, 999);
            Material m;
            if (ObstacleCache.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            m = Create(FieldShaders.NeonSolid, "BeamRunner Obstacle " + ColorUtility.ToHtmlStringRGB(edge));
            m.SetColor(ShaderIds.BaseColor, new Color(0.03f, 0.025f, 0.055f, 1f));
            m.SetColor(ShaderIds.EdgeColor, edge);
            m.SetColor(ShaderIds.RimColor, edge);
            m.SetVector(ShaderIds.Palette, ShaderIds.PaletteCustom);
            m.SetFloat(ShaderIds.EdgeWidth, 0.075f);
            m.SetFloat(ShaderIds.EdgeIntensity, 3.2f * Mathf.Max(0f, glow));
            m.SetFloat(ShaderIds.RimIntensity, 0.55f);
            m.SetFloat(ShaderIds.BodyGlow, 0.09f);
            SetFallbackColor(m, edge);
            ObstacleCache[key] = m;
            return m;
        }

        /// <summary>Obstacle material from a Core colour.</summary>
        public static Material Obstacle(ColorRGB edge, float glow)
        {
            return Obstacle(FieldGlobals.ToColor(edge), glow);
        }

        /// <summary>Additive material (particles, glows, ribbons) for a texture; tint/intensity via property block.</summary>
        public static Material Additive(Texture tex)
        {
            if (tex == null)
            {
                tex = Texture2D.whiteTexture;
            }

            int key = tex.GetInstanceID();
            Material m;
            if (AdditiveCache.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            m = Create(FieldShaders.Additive, "BeamRunner Additive " + tex.name);
            m.SetTexture(ShaderIds.MainTex, tex);
            m.SetTexture(ShaderIds.BaseMap, tex);
            AdditiveCache[key] = m;
            return m;
        }

        /// <summary>Soft glow sprite material.</summary>
        public static Material Glow()
        {
            return Additive(ProceduralTextures.SoftDot());
        }

        /// <summary>Soft line material (Additive with the beam profile across v): play-area frame, hole highlights.</summary>
        public static Material Beam()
        {
            return Additive(ProceduralTextures.Beam());
        }

        /// <summary>Procedural ship hull: dark body with neon edges and rim in the glow colour.</summary>
        public static Material Ship(Color body, Color glow)
        {
            long key = ColorKey(body) * 16777216L + ColorKey(glow);
            Material m;
            if (ShipCache.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            m = Create(FieldShaders.NeonSolid, "BeamRunner Ship " + ColorUtility.ToHtmlStringRGB(glow));
            // Livery bodies are authored as mid tones; keep the hull dark enough for the edges to pop.
            m.SetColor(ShaderIds.BaseColor, Color.Lerp(new Color(0.02f, 0.02f, 0.035f), body, 0.55f));
            m.SetColor(ShaderIds.EdgeColor, glow);
            m.SetColor(ShaderIds.RimColor, glow);
            m.SetVector(ShaderIds.Palette, ShaderIds.PaletteCustom);
            m.SetFloat(ShaderIds.EdgeWidth, 0.014f);
            m.SetFloat(ShaderIds.EdgeIntensity, 3.4f);
            m.SetFloat(ShaderIds.RimIntensity, 1.1f);
            m.SetFloat(ShaderIds.BodyGlow, 0.07f);
            SetFallbackColor(m, glow);
            ShipCache[key] = m;
            return m;
        }

        /// <summary>Shared palette-driven hologram; colour via <see cref="SetPalette"/>.</summary>
        public static Material Hologram()
        {
            if (_hologram == null)
            {
                _hologram = Create(FieldShaders.Hologram, "BeamRunner Hologram");
                _hologram.SetVector(ShaderIds.Palette, ShaderIds.PalettePrimary);
                _hologram.SetFloat(ShaderIds.Cull, (float)CullMode.Back);
                SetFallbackColor(_hologram, FieldGlobals.PrimaryColor);
            }

            return _hologram;
        }

        /// <summary>Hologram with a fixed colour (shield bubble, halos).</summary>
        public static Material Hologram(Color color)
        {
            return Hologram(color, CullMode.Back);
        }

        /// <summary>Hologram with a fixed colour and cull mode (use <see cref="CullMode.Off"/> for flat or open shapes).</summary>
        public static Material Hologram(Color color, CullMode cull)
        {
            long key = ColorKey(color) * 4L + (int)cull;
            Material m;
            if (HologramCache.TryGetValue(key, out m) && m != null)
            {
                return m;
            }

            m = Create(FieldShaders.Hologram, "BeamRunner Hologram " + ColorUtility.ToHtmlStringRGB(color));
            // The alpha-blended fallback (Sprites/Default) would draw the shape solid at alpha 1: keep it see-through.
            m.SetColor(ShaderIds.Color, FieldShaders.IsFallback(FieldShaders.HologramName) ? new Color(color.r, color.g, color.b, 0.3f) : color);
            m.SetVector(ShaderIds.Palette, ShaderIds.PaletteCustom);
            m.SetFloat(ShaderIds.Cull, (float)cull);
            HologramCache[key] = m;
            return m;
        }

        /// <summary>Gem pickup: bright faceted crystal.</summary>
        public static Material Gem()
        {
            if (_gem == null)
            {
                _gem = Create(FieldShaders.NeonSolid, "BeamRunner Gem");
                _gem.SetColor(ShaderIds.BaseColor, new Color(0.08f, 0.1f, 0.16f, 1f));
                _gem.SetColor(ShaderIds.EdgeColor, new Color(0.75f, 1f, 1f, 1f));
                _gem.SetColor(ShaderIds.RimColor, new Color(0.75f, 1f, 1f, 1f));
                _gem.SetVector(ShaderIds.Palette, new Vector4(0f, 0f, 0.55f, 0.45f));
                _gem.SetFloat(ShaderIds.EdgeWidth, 0.016f);
                _gem.SetFloat(ShaderIds.EdgeIntensity, 4f);
                _gem.SetFloat(ShaderIds.RimIntensity, 1.4f);
                _gem.SetFloat(ShaderIds.BodyGlow, 0.45f);
                SetFallbackColor(_gem, new Color(0.75f, 1f, 1f));
            }

            return _gem;
        }

        // ------------------------------------------------------------------ property block helpers

        /// <summary>Sets the palette weights on a block (see <see cref="ShaderIds.PaletteSlot"/>).</summary>
        public static void SetPalette(MaterialPropertyBlock block, Vector4 weights)
        {
            block.SetVector(ShaderIds.Palette, weights);
        }

        /// <summary>Fixed custom colour on a block for NeonSolid (edge + rim) or Hologram/Additive (colour + tint).</summary>
        public static void SetCustomColor(MaterialPropertyBlock block, Color srgb)
        {
            block.SetVector(ShaderIds.Palette, ShaderIds.PaletteCustom);
            block.SetColor(ShaderIds.EdgeColor, srgb);
            block.SetColor(ShaderIds.RimColor, srgb);
            block.SetColor(ShaderIds.Color, srgb);
            block.SetColor(ShaderIds.TintColor, srgb);
        }

        /// <summary>
        /// For a renderer with an additive material: stops it drawing when BeamRunner/Additive and its transparent fallback
        /// are both unavailable (an opaque fallback would cover the scene with rectangles). Call once at creation;
        /// <see cref="Renderer.forceRenderingOff"/> survives later <c>enabled</c> toggles.
        /// </summary>
        public static void GuardAdditive(Renderer renderer)
        {
            if (renderer != null)
            {
                renderer.forceRenderingOff = !FieldShaders.CanDraw(FieldShaders.AdditiveName);
            }
        }

        /// <summary>Like <see cref="GuardAdditive"/>, for hologram renderers (shield bubble, Daily Run ghost).</summary>
        public static void GuardHologram(Renderer renderer)
        {
            if (renderer != null)
            {
                renderer.forceRenderingOff = !FieldShaders.CanDraw(FieldShaders.HologramName);
            }
        }

        /// <summary>Applies a block to a renderer (null-safe).</summary>
        public static void Apply(Renderer renderer, MaterialPropertyBlock block)
        {
            if (renderer != null)
            {
                renderer.SetPropertyBlock(block);
            }
        }

        // ------------------------------------------------------------------ internals

        private static Material Create(Shader shader, string name)
        {
            var m = new Material(shader);
            m.name = name;
            return RenderCache.Track(m);
        }

        /// <summary>Makes built-in fallback shaders (which read _Color / _BaseColor) show something sensible.</summary>
        private static void SetFallbackColor(Material m, Color c)
        {
            if (m.HasProperty(ShaderIds.Color) && m.shader != null && !m.shader.name.StartsWith("BeamRunner/"))
            {
                m.SetColor(ShaderIds.Color, c);
            }

            if (m.HasProperty(ShaderIds.BaseColor) && m.shader != null && !m.shader.name.StartsWith("BeamRunner/"))
            {
                m.SetColor(ShaderIds.BaseColor, c);
            }
        }

        private static long ColorKey(Color c)
        {
            Color32 c32 = c;
            return ((long)c32.r << 16) | ((long)c32.g << 8) | c32.b;
        }
    }
}
