using System;

namespace BeamRunner.UI
{
    /// <summary>A white, alpha-only bitmap produced by <see cref="SpritePainter"/> (row 0 = top).</summary>
    public sealed class SpriteBitmap
    {
        public SpriteBitmap(int width, int height)
        {
            Width = width;
            Height = height;
            Alpha = new float[width * height];
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Coverage 0..1, row-major, row 0 = top.</summary>
        public float[] Alpha { get; }

        /// <summary>Uniform 9-slice border in pixels (0 = not sliced).</summary>
        public int Border { get; set; }

        /// <summary>True for seamless tiles (the texture should use Repeat wrapping).</summary>
        public bool Tiled { get; set; }

        /// <summary>True when the bitmap is typically displayed scaled (generate mipmaps).</summary>
        public bool Mips { get; set; }
    }

    /// <summary>
    /// Engine-free generators for the UI kit's procedural sprites. All shapes are white; the Unity layer tints
    /// them with <c>Image.color</c>. Nine-sliced shapes leave a 1 px transparent margin so every edge is
    /// anti-aliased inside the texture.
    /// </summary>
    public static class SpritePainter
    {
        /// <summary>Filled rounded rectangle, 9-sliced (border = radius + 2).</summary>
        public static SpriteBitmap RoundedRect(int radius)
        {
            radius = Math.Max(1, radius);
            int size = radius * 2 + 6;
            var bmp = new SpriteBitmap(size, size) { Border = radius + 2 };
            float inset = 1f;
            ForEach(bmp, (x, y) => SdfCanvas.Coverage(RoundRectDistance(x, y, size, size, inset, radius)));
            return bmp;
        }

        /// <summary>Rounded rectangle outline (stroke lies inside the shape's edge), 9-sliced.</summary>
        public static SpriteBitmap RoundedOutline(int radius, int stroke)
        {
            radius = Math.Max(1, radius);
            stroke = Math.Max(1, stroke);
            int border = Math.Max(radius, stroke) + 2;
            int size = border * 2 + 2;
            var bmp = new SpriteBitmap(size, size) { Border = border };
            ForEach(bmp, (x, y) =>
            {
                float d = RoundRectDistance(x, y, size, size, 1f, radius);
                float band = MathF.Max(d, -(d + stroke));
                return SdfCanvas.Coverage(band);
            });
            return bmp;
        }

        /// <summary>
        /// Neon frame: crisp inner stroke plus a soft outer glow (and a faint inner glow). The shape edge sits
        /// <paramref name="glow"/> + 1 px inside the texture, so the Image must extend <paramref name="glow"/> units
        /// beyond the framed rect on every side (at pixelsPerUnitMultiplier 1).
        /// </summary>
        public static SpriteBitmap NeonFrame(int radius, int stroke, int glow)
        {
            radius = Math.Max(1, radius);
            stroke = Math.Max(1, stroke);
            glow = Math.Max(2, glow);
            int border = glow + Math.Max(radius, stroke) + 2;
            int size = border * 2 + 2;
            var bmp = new SpriteBitmap(size, size) { Border = border };
            float inset = glow + 1f;
            float sigmaOut = glow / 2.6f;
            float sigmaIn = MathF.Max(1.5f, glow / 3.5f);
            ForEach(bmp, (x, y) =>
            {
                float d = RoundRectDistance(x, y, size, size, inset, radius);
                float band = MathF.Max(d, -(d + stroke));
                float core = SdfCanvas.Coverage(band);
                float outer = d > 0f ? 0.62f * MathF.Exp(-0.5f * (d / sigmaOut) * (d / sigmaOut)) : 0f;
                float innerD = -(d + stroke);
                float inner = innerD > 0f ? 0.22f * MathF.Exp(-0.5f * (innerD / sigmaIn) * (innerD / sigmaIn)) : 0f;
                // Glow tails fade to exactly zero at the texture edge.
                float edge = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                float fade = MathF.Min(1f, edge / 2f);
                float a = 1f - (1f - core) * (1f - outer * fade) * (1f - inner);
                return a;
            });
            return bmp;
        }

        /// <summary>Rounded rectangle with a blurred edge (drop shadows, glow plates). Extends <paramref name="blur"/> px outside the shape.</summary>
        public static SpriteBitmap SoftRect(int radius, int blur)
        {
            radius = Math.Max(1, radius);
            blur = Math.Max(2, blur);
            int border = blur + radius + 2;
            int size = border * 2 + 2;
            var bmp = new SpriteBitmap(size, size) { Border = border };
            float inset = blur + 1f;
            ForEach(bmp, (x, y) =>
            {
                float d = RoundRectDistance(x, y, size, size, inset, radius);
                // Smooth falloff centred on the shape edge (half the blur inside, half outside).
                float t = (d + blur * 0.5f) / (blur * 1.5f);
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                float s = 1f - t * t * (3f - 2f * t);
                return s * s;
            });
            return bmp;
        }

        /// <summary>Anti-aliased filled circle (1 px margin).</summary>
        public static SpriteBitmap Circle(int size)
        {
            var bmp = new SpriteBitmap(size, size) { Mips = true };
            float c = size * 0.5f;
            float r = c - 1f;
            ForEach(bmp, (x, y) =>
            {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;
                return SdfCanvas.Coverage(MathF.Sqrt(dx * dx + dy * dy) - r);
            });
            return bmp;
        }

        /// <summary>Anti-aliased ring; <paramref name="thickness"/> in pixels, outer edge 1 px from the texture edge.</summary>
        public static SpriteBitmap Ring(int size, int thickness)
        {
            var bmp = new SpriteBitmap(size, size) { Mips = true };
            float c = size * 0.5f;
            float r = c - 1f - thickness * 0.5f;
            float hw = thickness * 0.5f;
            ForEach(bmp, (x, y) =>
            {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;
                return SdfCanvas.Coverage(MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - r) - hw);
            });
            return bmp;
        }

        /// <summary>Soft radial glow (Gaussian-like, exactly zero at the edge).</summary>
        public static SpriteBitmap Glow(int size)
        {
            var bmp = new SpriteBitmap(size, size) { Mips = true };
            float c = size * 0.5f;
            float e = MathF.Exp(-4.5f);
            ForEach(bmp, (x, y) =>
            {
                float dx = (x + 0.5f - c) / c;
                float dy = (y + 0.5f - c) / c;
                float r2 = dx * dx + dy * dy;
                if (r2 >= 1f)
                {
                    return 0f;
                }

                return (MathF.Exp(-4.5f * r2) - e) / (1f - e);
            });
            return bmp;
        }

        /// <summary>Linear radial falloff: 1 at the centre, 0 at the edge (smoothed).</summary>
        public static SpriteBitmap RadialGradient(int size)
        {
            var bmp = new SpriteBitmap(size, size) { Mips = true };
            float c = size * 0.5f;
            ForEach(bmp, (x, y) =>
            {
                float dx = (x + 0.5f - c) / c;
                float dy = (y + 0.5f - c) / c;
                float t = 1f - MathF.Sqrt(dx * dx + dy * dy);
                t = t < 0f ? 0f : t;
                return t * t * (3f - 2f * t);
            });
            return bmp;
        }

        /// <summary>Vignette: transparent centre, opaque corners (stretch over the screen, tint black).</summary>
        public static SpriteBitmap Vignette(int size)
        {
            var bmp = new SpriteBitmap(size, size) { Mips = false };
            float c = size * 0.5f;
            ForEach(bmp, (x, y) =>
            {
                float dx = (x + 0.5f - c) / c;
                float dy = (y + 0.5f - c) / c;
                float r = MathF.Sqrt(dx * dx + dy * dy) / 1.41421356f;
                float t = (r - 0.35f) / 0.65f;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                return t * t * (3f - 2f * t);
            });
            return bmp;
        }

        /// <summary>Linear gradient: alpha 1 at the top (or left) → 0 at the bottom (or right).</summary>
        public static SpriteBitmap LinearGradient(int length, bool vertical)
        {
            int w = vertical ? 4 : length;
            int h = vertical ? length : 4;
            var bmp = new SpriteBitmap(w, h);
            ForEach(bmp, (x, y) =>
            {
                float t = vertical ? (y + 0.5f) / h : (x + 0.5f) / w;
                return 1f - t;
            });
            return bmp;
        }

        /// <summary>Horizontal hairline texture whose two ends fade to transparent (constant vertically).</summary>
        public static SpriteBitmap FadeLine(int length)
        {
            var bmp = new SpriteBitmap(length, 4);
            ForEach(bmp, (x, y) =>
            {
                float u = (x + 0.5f) / length;
                float e = MathF.Min(u, 1f - u) / 0.32f;
                e = e > 1f ? 1f : e;
                return e * e * (3f - 2f * e);
            });
            return bmp;
        }

        /// <summary>Seamless 45° hazard stripes (tile). Stripes cover <paramref name="duty"/> of each period.</summary>
        public static SpriteBitmap Hazard(int size, float duty = 0.5f)
        {
            var bmp = new SpriteBitmap(size, size) { Tiled = true };
            float period = size * 0.5f;
            float half = period * duty * 0.5f;
            ForEach(bmp, (x, y) =>
            {
                float u = (x + y + 1f) % period;
                // Distance (in pixels, perpendicular to the stripes) from the stripe centre.
                float d = MathF.Abs(u - period * 0.5f);
                return SdfCanvas.Coverage((d - half) / 1.41421356f);
            });
            return bmp;
        }

        /// <summary>Horizontal scanlines (tile), with the given period in pixels.</summary>
        public static SpriteBitmap Scanlines(int size, int period)
        {
            period = Math.Max(2, period);
            var bmp = new SpriteBitmap(size, size) { Tiled = true };
            ForEach(bmp, (x, y) =>
            {
                float phase = (y % period + 0.5f) / period;
                float v = 0.5f + 0.5f * MathF.Cos(phase * MathF.PI * 2f);
                return v * v;
            });
            return bmp;
        }

        /// <summary>Deterministic grain (tile).</summary>
        public static SpriteBitmap Noise(int size, int seed)
        {
            var bmp = new SpriteBitmap(size, size) { Tiled = true };
            ForEach(bmp, (x, y) =>
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            });
            return bmp;
        }

        /// <summary>Row of right-pointing chevrons (tile; one chevron per tile).</summary>
        public static SpriteBitmap Chevrons(int size)
        {
            var c = new SdfCanvas(size, size);
            c.SetDesignSize(1f);
            c.Polyline(new[] { 0.3f, 0.14f, 0.66f, 0.5f, 0.3f, 0.86f }, 0.16f);
            var bmp = new SpriteBitmap(size, size) { Tiled = true };
            c.WriteCoverage(bmp.Alpha, 1f);
            return bmp;
        }

        /// <summary>Signed distance (pixels) from pixel (x, y) to a rounded rect inset from the bitmap edges.</summary>
        public static float RoundRectDistance(int x, int y, int width, int height, float inset, float radius)
        {
            float px = x + 0.5f;
            float py = y + 0.5f;
            float hw = width * 0.5f - inset;
            float hh = height * 0.5f - inset;
            float r = MathF.Min(radius, MathF.Min(hw, hh));
            float qx = MathF.Abs(px - width * 0.5f) - hw + r;
            float qy = MathF.Abs(py - height * 0.5f) - hh + r;
            float ox = qx > 0f ? qx : 0f;
            float oy = qy > 0f ? qy : 0f;
            return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0f) - r;
        }

        private static void ForEach(SpriteBitmap bmp, Func<int, int, float> fn)
        {
            int w = bmp.Width;
            float[] a = bmp.Alpha;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float v = fn(x, y);
                    a[y * w + x] = v < 0f ? 0f : (v > 1f ? 1f : v);
                }
            }
        }
    }
}
