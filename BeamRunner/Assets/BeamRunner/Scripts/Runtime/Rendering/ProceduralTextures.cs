using UnityEngine;

namespace BeamRunner.Rendering
{
    /// <summary>
    /// Small procedural textures for the additive shaders, generated once and cached. RGB is white and the
    /// shape lives in alpha (the Additive shader multiplies rgb by alpha), so any tint can be applied.
    /// Textures are created linear (no sRGB conversion of the masks) and never saved.
    /// </summary>
    public static class ProceduralTextures
    {
        private static Texture2D _softDot;
        private static Texture2D _streak;
        private static Texture2D _ring;
        private static Texture2D _sparkle;
        private static Texture2D _noise;
        private static Texture2D _beam;
        private static Texture2D _trail;

        /// <summary>Soft radial glow with a bright core (engine glows, halos, flashes, particles).</summary>
        public static Texture2D SoftDot()
        {
            if (_softDot == null)
            {
                const int size = 64;
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float r = Radius(x, y, size);
                        float f = Mathf.Clamp01(1f - r);
                        float a = f * f * 0.7f + Mathf.Pow(f, 10f) * 0.6f;
                        px[y * size + x] = White(a);
                    }
                }

                _softDot = Build("BeamRunner SoftDot", size, size, px, true, TextureWrapMode.Clamp);
            }

            return _softDot;
        }

        /// <summary>
        /// Horizontal streak: u runs tail (0) → head (1) with a bright head; v is the soft cross-section.
        /// Used for speed lines, engine flares and stretched sparks.
        /// </summary>
        public static Texture2D Streak()
        {
            if (_streak == null)
            {
                const int w = 128;
                const int h = 16;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h - 0.5f;
                    float across = Mathf.Exp(-(v * v) / (2f * 0.16f * 0.16f));
                    across *= Mathf.Clamp01((0.5f - Mathf.Abs(v)) * 8f);
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w;
                        float along = Mathf.Pow(u, 1.6f) * Mathf.Clamp01((1f - u) / 0.06f);
                        px[y * w + x] = White(along * across);
                    }
                }

                _streak = Build("BeamRunner Streak", w, h, px, true, TextureWrapMode.Clamp);
            }

            return _streak;
        }

        /// <summary>Thin glowing ring (shockwaves, pickup halos).</summary>
        public static Texture2D Ring()
        {
            if (_ring == null)
            {
                const int size = 128;
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float r = Radius(x, y, size);
                        float d = r - 0.78f;
                        float a = Mathf.Exp(-(d * d) / (2f * 0.035f * 0.035f)) + 0.35f * Mathf.Exp(-(d * d) / (2f * 0.12f * 0.12f));
                        a *= Mathf.Clamp01((1f - r) * 12f);
                        px[y * size + x] = White(a);
                    }
                }

                _ring = Build("BeamRunner Ring", size, size, px, true, TextureWrapMode.Clamp);
            }

            return _ring;
        }

        /// <summary>Four-pointed star glint.</summary>
        public static Texture2D Sparkle()
        {
            if (_sparkle == null)
            {
                const int size = 64;
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float fx = Mathf.Abs((x + 0.5f) / size - 0.5f) * 2f;
                        float fy = Mathf.Abs((y + 0.5f) / size - 0.5f) * 2f;
                        float r = Mathf.Sqrt(fx * fx + fy * fy);
                        float rayH = Mathf.Exp(-fy / 0.035f) * Mathf.Clamp01(1f - fx);
                        float rayV = Mathf.Exp(-fx / 0.035f) * Mathf.Clamp01(1f - fy);
                        float core = Mathf.Exp(-(r * r) / (2f * 0.1f * 0.1f));
                        float a = Mathf.Max(Mathf.Max(rayH, rayV) * 0.9f, core);
                        a *= Mathf.Clamp01((1f - r) * 4f);
                        px[y * size + x] = White(a);
                    }
                }

                _sparkle = Build("BeamRunner Sparkle", size, size, px, true, TextureWrapMode.Clamp);
            }

            return _sparkle;
        }

        /// <summary>Tileable smooth value noise (grey in rgb and alpha).</summary>
        public static Texture2D Noise()
        {
            if (_noise == null)
            {
                const int size = 64;
                const int cells = 8;
                var lattice = new float[cells * cells];
                uint seed = 0x9E3779B9u;
                for (int i = 0; i < lattice.Length; i++)
                {
                    seed ^= seed << 13;
                    seed ^= seed >> 17;
                    seed ^= seed << 5;
                    lattice[i] = (seed & 0xFFFF) / 65535f;
                }

                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float n = 0f;
                        float amp = 0.65f;
                        float total = 0f;
                        for (int octave = 0; octave < 2; octave++)
                        {
                            int c = cells >> octave;
                            float gx = (float)x / size * c;
                            float gy = (float)y / size * c;
                            int x0 = Mathf.FloorToInt(gx);
                            int y0 = Mathf.FloorToInt(gy);
                            float tx = Smooth(gx - x0);
                            float ty = Smooth(gy - y0);
                            float a00 = Lattice(lattice, cells, x0 % c, y0 % c);
                            float a10 = Lattice(lattice, cells, (x0 + 1) % c, y0 % c);
                            float a01 = Lattice(lattice, cells, x0 % c, (y0 + 1) % c);
                            float a11 = Lattice(lattice, cells, (x0 + 1) % c, (y0 + 1) % c);
                            n += amp * Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), ty);
                            total += amp;
                            amp *= 0.5f;
                        }

                        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(n / total) * 255f);
                        px[y * size + x] = new Color32(b, b, b, b);
                    }
                }

                _noise = Build("BeamRunner Noise", size, size, px, true, TextureWrapMode.Repeat);
            }

            return _noise;
        }

        /// <summary>Soft line cross-section along v (bright core, soft glow); constant along u. Frames and highlights.</summary>
        public static Texture2D Beam()
        {
            if (_beam == null)
            {
                const int w = 8;
                const int h = 64;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h - 0.5f;
                    float a = Mathf.Exp(-(v * v) / (2f * 0.05f * 0.05f)) + 0.45f * Mathf.Exp(-(v * v) / (2f * 0.18f * 0.18f));
                    a *= Mathf.Clamp01((0.5f - Mathf.Abs(v)) * 10f);
                    for (int x = 0; x < w; x++)
                    {
                        px[y * w + x] = White(a);
                    }
                }

                _beam = Build("BeamRunner Beam", w, h, px, true, TextureWrapMode.Clamp);
            }

            return _beam;
        }

        /// <summary>Ribbon cross-section along u (hot core, soft falloff to the edges); constant along v.</summary>
        public static Texture2D Trail()
        {
            if (_trail == null)
            {
                const int w = 64;
                const int h = 4;
                var px = new Color32[w * h];
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w - 0.5f;
                    float a = Mathf.Exp(-(u * u) / (2f * 0.07f * 0.07f)) * 0.8f + Mathf.Exp(-(u * u) / (2f * 0.2f * 0.2f)) * 0.35f;
                    a *= Mathf.Clamp01((0.5f - Mathf.Abs(u)) * 12f);
                    for (int y = 0; y < h; y++)
                    {
                        px[y * w + x] = White(a);
                    }
                }

                _trail = Build("BeamRunner Trail", w, h, px, true, TextureWrapMode.Clamp);
            }

            return _trail;
        }

        // ------------------------------------------------------------------ helpers

        private static float Radius(int x, int y, int size)
        {
            float fx = (x + 0.5f) / size - 0.5f;
            float fy = (y + 0.5f) / size - 0.5f;
            return Mathf.Sqrt(fx * fx + fy * fy) * 2f;
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float Lattice(float[] lattice, int stride, int x, int y)
        {
            return lattice[y * stride + x];
        }

        private static Color32 White(float alpha)
        {
            return new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }

        private static Texture2D Build(string name, int width, int height, Color32[] pixels, bool mips, TextureWrapMode wrap)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mips, true);
            tex.name = name;
            tex.wrapMode = wrap;
            tex.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            tex.SetPixels32(pixels);
            tex.Apply(mips, true);
            return RenderCache.Track(tex);
        }
    }
}
