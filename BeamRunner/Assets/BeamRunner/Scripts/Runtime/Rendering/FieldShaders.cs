using System.Collections.Generic;
using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Finds the BeamRunner shaders by name (they live under Resources, so they are always in the build) and falls
    /// back to built-in shaders if one is missing or unsupported on the device. Never returns null.
    /// </summary>
    /// <remarks>
    /// Opaque shaders fall back to unlit colour shaders. The transparent ones (Additive, Hologram) fall back only to
    /// <c>Sprites/Default</c> (alpha-blended: white × vertex colour × texture alpha, so glows become soft sprites; it is
    /// in the project's Always Included Shaders); an opaque fallback would turn every glow, beam and particle into an
    /// opaque rectangle, so without one <see cref="CanDraw"/> is false and <see cref="FieldMaterials"/>' guards keep
    /// those renderers from drawing.
    /// </remarks>
    public static class FieldShaders
    {
        public const string GridName = "BeamRunner/Grid";
        public const string SkyName = "BeamRunner/Sky";
        public const string NeonSolidName = "BeamRunner/NeonSolid";
        public const string AdditiveName = "BeamRunner/Additive";
        public const string HologramName = "BeamRunner/Hologram";

        private const string ErrorShaderName = "Hidden/InternalErrorShader";

        private static readonly string[] OpaqueFallbackNames =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
            "Sprites/Default",
        };

        private static readonly string[] TransparentFallbackNames =
        {
            "Sprites/Default",
        };

        private static readonly Dictionary<string, Shader> Cache = new Dictionary<string, Shader>(8);
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static bool _warned;

        public static Shader Grid
        {
            get { return Get(GridName); }
        }

        public static Shader Sky
        {
            get { return Get(SkyName); }
        }

        public static Shader NeonSolid
        {
            get { return Get(NeonSolidName); }
        }

        public static Shader Additive
        {
            get { return Get(AdditiveName); }
        }

        public static Shader Hologram
        {
            get { return Get(HologramName); }
        }

        /// <summary>True if <paramref name="name"/> resolved to a fallback instead of the real shader.</summary>
        public static bool IsFallback(string name)
        {
            Get(name);
            return Missing.Contains(name);
        }

        /// <summary>True for the shaders that blend over the scene (their fallback must blend too).</summary>
        public static bool IsTransparent(string name)
        {
            return name == AdditiveName || name == HologramName;
        }

        /// <summary>
        /// False when <paramref name="name"/> is missing and its fallback would draw something wrong: the error shader, or
        /// an opaque shader in place of a transparent one. Renderers using it should not draw at all.
        /// </summary>
        public static bool CanDraw(string name)
        {
            Shader shader = Get(name);
            if (!Missing.Contains(name))
            {
                return true;
            }

            return shader != null && shader.name != ErrorShaderName;
        }

        /// <summary>Returns the named shader, or the first supported fallback (logging one warning in total).</summary>
        public static Shader Get(string name)
        {
            Shader shader;
            if (Cache.TryGetValue(name, out shader) && shader != null)
            {
                return shader;
            }

            shader = Shader.Find(name);
            if (shader == null || !shader.isSupported)
            {
                Missing.Add(name);
                shader = FindFallback(IsTransparent(name) ? TransparentFallbackNames : OpaqueFallbackNames);
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[BeamRunner] Shader '" + name + "' is missing or unsupported; using '" +
                                     (shader != null ? shader.name : "none") + "'. Visuals will be degraded.");
                }
            }
            else
            {
                Missing.Remove(name);
            }

            Cache[name] = shader;
            return shader;
        }

        private static Shader FindFallback(string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Shader s = Shader.Find(names[i]);
                if (s != null && s.isSupported)
                {
                    return s;
                }
            }

            // Always present in every player; guarantees a non-null result.
            return Shader.Find(ErrorShaderName);
        }
    }
}
