using System;

namespace BeamRunner.Core
{
    /// <summary>
    /// Engine-independent colour (sRGB, 0..1 components, values above 1 allowed for HDR intensity).
    /// The Unity layer converts with <c>new Color(c.R, c.G, c.B, c.A)</c>.
    /// </summary>
    [Serializable]
    public struct ColorRGB : IEquatable<ColorRGB>
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public ColorRGB(float r, float g, float b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Parses "#RRGGBB" or "#RRGGBBAA" (the leading # is optional).</summary>
        public static ColorRGB Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return new ColorRGB(1f, 1f, 1f);
            }

            string h = hex[0] == '#' ? hex.Substring(1) : hex;
            if (h.Length != 6 && h.Length != 8)
            {
                throw new FormatException("Colour must be #RRGGBB or #RRGGBBAA: " + hex);
            }

            float r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
            float g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
            float b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
            float a = h.Length == 8 ? Convert.ToInt32(h.Substring(6, 2), 16) / 255f : 1f;
            return new ColorRGB(r, g, b, a);
        }

        public ColorRGB WithAlpha(float a)
        {
            return new ColorRGB(R, G, B, a);
        }

        /// <summary>Multiplies RGB (not alpha) — used to push neon colours into HDR for bloom.</summary>
        public ColorRGB Scaled(float intensity)
        {
            return new ColorRGB(R * intensity, G * intensity, B * intensity, A);
        }

        public static ColorRGB Lerp(ColorRGB a, ColorRGB b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new ColorRGB(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t,
                a.A + (b.A - a.A) * t);
        }

        public string ToHex()
        {
            int r = Clamp255(R);
            int g = Clamp255(G);
            int b = Clamp255(B);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        private static int Clamp255(float v)
        {
            int i = (int)Math.Round(v * 255f);
            return i < 0 ? 0 : (i > 255 ? 255 : i);
        }

        public bool Equals(ColorRGB other)
        {
            return R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
        }

        public override bool Equals(object obj)
        {
            return obj is ColorRGB other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = R.GetHashCode();
                h = (h * 397) ^ G.GetHashCode();
                h = (h * 397) ^ B.GetHashCode();
                return (h * 397) ^ A.GetHashCode();
            }
        }

        public override string ToString()
        {
            return ToHex();
        }
    }
}
