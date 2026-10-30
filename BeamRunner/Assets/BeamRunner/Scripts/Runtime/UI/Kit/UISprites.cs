using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Procedurally generated, cached UI sprites (white, tinted via <c>Image.color</c>). Sprites use 100 pixels per unit,
    /// so with the kit's canvas (referencePixelsPerUnit 100) one texture pixel = one canvas unit at
    /// <c>pixelsPerUnitMultiplier</c> 1. Nine-sliced shapes should be applied with <see cref="ApplyRounded"/>,
    /// <see cref="ApplyOutline"/> or <see cref="ApplyFrame"/>, which set the image type and multiplier correctly.
    /// </summary>
    public static class UISprites
    {
        /// <summary>Radii for which rounded-rect textures are generated; other radii reuse the next bucket up via pixelsPerUnitMultiplier.</summary>
        private static readonly int[] RadiusBuckets = { 2, 4, 6, 8, 12, 16, 20, 24, 28, 32, 40, 48, 56, 64, 76, 88, 100, 128 };

        private static readonly Dictionary<long, Sprite> Cache = new Dictionary<long, Sprite>(64);
        private static readonly List<Object> Owned = new List<Object>(128);
        private static bool _hooked;

        // ------------------------------------------------------------------ keys
        private const int KRounded = 1;
        private const int KOutline = 2;
        private const int KFrame = 3;
        private const int KSoft = 4;
        private const int KCircle = 5;
        private const int KRing = 6;
        private const int KGlow = 7;
        private const int KRadial = 8;
        private const int KVignette = 9;
        private const int KGradV = 10;
        private const int KGradH = 11;
        private const int KHazard = 12;
        private const int KScan = 13;
        private const int KNoise = 14;
        private const int KChevrons = 15;
        private const int KFadeLine = 16;
        private const int KIcon = 17;

        // ------------------------------------------------------------------ shapes

        /// <summary>Filled rounded rect with an exact pixel radius (9-sliced).</summary>
        public static Sprite RoundedRect(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 256);
            long key = Key(KRounded, radius);
            if (TryGet(key, out Sprite s))
            {
                return s;
            }

            return Store(key, SpritePainter.RoundedRect(radius), "Rounded" + radius);
        }

        /// <summary>Rounded outline, stroke inside the edge (9-sliced).</summary>
        public static Sprite RoundedOutline(int radius, int stroke)
        {
            radius = Mathf.Clamp(radius, 1, 256);
            stroke = Mathf.Clamp(stroke, 1, 64);
            long key = Key(KOutline, radius, stroke);
            if (TryGet(key, out Sprite s))
            {
                return s;
            }

            return Store(key, SpritePainter.RoundedOutline(radius, stroke), "Outline" + radius + "_" + stroke);
        }

        /// <summary>Neon frame: crisp stroke + soft outer glow (9-sliced). The image must overhang the framed rect by <paramref name="glow"/>.</summary>
        public static Sprite NeonFrame(int radius, int stroke, int glow)
        {
            radius = Mathf.Clamp(radius, 1, 256);
            stroke = Mathf.Clamp(stroke, 1, 64);
            glow = Mathf.Clamp(glow, 2, 128);
            long key = Key(KFrame, radius, stroke, glow);
            if (TryGet(key, out Sprite s))
            {
                return s;
            }

            return Store(key, SpritePainter.NeonFrame(radius, stroke, glow), "Frame" + radius + "_" + stroke + "_" + glow);
        }

        /// <summary>Blurred rounded rect for drop shadows and glow plates (9-sliced). Overhangs the shape by <paramref name="blur"/>.</summary>
        public static Sprite SoftRect(int radius, int blur)
        {
            radius = Mathf.Clamp(radius, 1, 256);
            blur = Mathf.Clamp(blur, 2, 128);
            long key = Key(KSoft, radius, blur);
            if (TryGet(key, out Sprite s))
            {
                return s;
            }

            return Store(key, SpritePainter.SoftRect(radius, blur), "Soft" + radius + "_" + blur);
        }

        /// <summary>Anti-aliased filled circle (128 px, mipmapped).</summary>
        public static Sprite Circle
        {
            get
            {
                long key = Key(KCircle, 128);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Circle(128), "Circle");
            }
        }

        /// <summary>Ring (256 px texture) with the given stroke in texture pixels.</summary>
        public static Sprite Ring(int thicknessPx = 20)
        {
            thicknessPx = Mathf.Clamp(thicknessPx, 2, 120);
            long key = Key(KRing, thicknessPx);
            if (TryGet(key, out Sprite s))
            {
                return s;
            }

            return Store(key, SpritePainter.Ring(256, thicknessPx), "Ring" + thicknessPx);
        }

        /// <summary>Soft radial glow (Gaussian-like, 128 px, mipmapped).</summary>
        public static Sprite Glow
        {
            get
            {
                long key = Key(KGlow, 128);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Glow(128), "Glow");
            }
        }

        /// <summary>Linear radial falloff (1 centre → 0 edge).</summary>
        public static Sprite RadialGradient
        {
            get
            {
                long key = Key(KRadial, 128);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.RadialGradient(128), "Radial");
            }
        }

        /// <summary>Vignette (transparent centre, opaque corners) — stretch full screen and tint black.</summary>
        public static Sprite Vignette
        {
            get
            {
                long key = Key(KVignette, 256);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Vignette(256), "Vignette");
            }
        }

        /// <summary>Vertical gradient: opaque top → transparent bottom.</summary>
        public static Sprite GradientVertical
        {
            get
            {
                long key = Key(KGradV, 128);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.LinearGradient(128, true), "GradV");
            }
        }

        /// <summary>Horizontal gradient: opaque left → transparent right.</summary>
        public static Sprite GradientHorizontal
        {
            get
            {
                long key = Key(KGradH, 128);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.LinearGradient(128, false), "GradH");
            }
        }

        /// <summary>Seamless diagonal hazard stripes (use <c>Image.Type.Tiled</c>).</summary>
        public static Sprite Hazard
        {
            get
            {
                long key = Key(KHazard, 64);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Hazard(64, 0.5f), "Hazard");
            }
        }

        /// <summary>Horizontal scanlines with a 4-unit period (tile).</summary>
        public static Sprite Scanlines
        {
            get
            {
                long key = Key(KScan, 64);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Scanlines(64, 4), "Scanlines");
            }
        }

        /// <summary>Film grain noise (tile).</summary>
        public static Sprite Noise
        {
            get
            {
                long key = Key(KNoise, 128);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Noise(128, 7), "Noise");
            }
        }

        /// <summary>Right-pointing chevrons, one per 64-unit tile.</summary>
        public static Sprite Chevrons
        {
            get
            {
                long key = Key(KChevrons, 64);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.Chevrons(64), "Chevrons");
            }
        }

        /// <summary>Horizontal hairline whose ends fade out (dividers).</summary>
        public static Sprite FadeLine
        {
            get
            {
                long key = Key(KFadeLine, 256);
                return TryGet(key, out Sprite s) ? s : Store(key, SpritePainter.FadeLine(256), "FadeLine");
            }
        }

        // ------------------------------------------------------------------ image helpers

        /// <summary>Nearest generated radius ≥ <paramref name="radius"/> (the largest bucket for huge radii).</summary>
        public static int BucketRadius(float radius)
        {
            for (int i = 0; i < RadiusBuckets.Length; i++)
            {
                if (RadiusBuckets[i] >= radius - 0.01f)
                {
                    return RadiusBuckets[i];
                }
            }

            return RadiusBuckets[RadiusBuckets.Length - 1];
        }

        /// <summary>Makes <paramref name="image"/> a filled rounded rect with <paramref name="radius"/> canvas units.</summary>
        public static void ApplyRounded(Image image, float radius)
        {
            if (image == null)
            {
                return;
            }

            radius = Mathf.Max(0.5f, radius);
            int bucket = BucketRadius(radius);
            Sprite s = RoundedRect(bucket);
            if (image.sprite != s)
            {
                image.sprite = s;
            }

            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            float mult = bucket / radius;
            if (!Mathf.Approximately(image.pixelsPerUnitMultiplier, mult))
            {
                image.pixelsPerUnitMultiplier = mult;
            }
        }

        /// <summary>Makes <paramref name="image"/> a rounded outline (exact radius / stroke in units, multiplier 1).</summary>
        public static void ApplyOutline(Image image, float radius, float stroke)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = RoundedOutline(Mathf.RoundToInt(radius), Mathf.Max(1, Mathf.RoundToInt(stroke)));
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>
        /// Makes <paramref name="image"/> a neon frame and stretches it over its parent, overhanging by the glow size so
        /// the stroke sits exactly on the parent's edge.
        /// </summary>
        public static void ApplyFrame(Image image, float radius, float stroke, float glow)
        {
            if (image == null)
            {
                return;
            }

            int g = Mathf.Max(2, Mathf.RoundToInt(glow));
            image.sprite = NeonFrame(Mathf.RoundToInt(radius), Mathf.Max(1, Mathf.RoundToInt(stroke)), g);
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-g, -g);
            rt.offsetMax = new Vector2(g, g);
        }

        /// <summary>Makes <paramref name="image"/> a soft (blurred) rounded rect overhanging its parent by <paramref name="blur"/>.</summary>
        public static void ApplySoft(Image image, float radius, float blur, Vector2 offset = default)
        {
            if (image == null)
            {
                return;
            }

            int b = Mathf.Max(2, Mathf.RoundToInt(blur));
            image.sprite = SoftRect(Mathf.RoundToInt(radius), b);
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
            RectTransform rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-b, -b) + offset;
            rt.offsetMax = new Vector2(b, b) + offset;
        }

        /// <summary>Makes <paramref name="image"/> a tiled pattern (hazard, scanlines, noise, chevrons). <paramref name="tileScale"/> scales the tile.</summary>
        public static void ApplyTiled(Image image, Sprite pattern, float tileScale = 1f)
        {
            if (image == null)
            {
                return;
            }

            image.sprite = pattern;
            image.type = Image.Type.Tiled;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.05f, tileScale);
        }

        /// <summary>
        /// Icon sprite (see <see cref="UIIcons"/>). Internal cache shared with the sprite store. Uses the pixels painted by
        /// <see cref="UIIcons.BeginPrewarm"/>'s worker when they are ready, else paints the icon now.
        /// </summary>
        internal static Sprite IconSprite(IconId id, int size)
        {
            long key = Key(KIcon, (int)id, size);
            if (TryGet(key, out Sprite s))
            {
                return s;
            }

            Color32[] px = size == UIIcons.Size ? UIIcons.TakePainted(id) : null;
            if (px == null)
            {
                px = ToPixels(IconPainter.Paint(id, size), size, size);
            }

            s = CreateSprite(px, size, size, true, false, 0, "Icon_" + id);
            Cache[key] = s;
            return s;
        }

        /// <summary>True when the icon sprite is already generated.</summary>
        internal static bool HasIconSprite(IconId id, int size)
        {
            return TryGet(Key(KIcon, (int)id, size), out _);
        }

        /// <summary>Destroys every generated texture and sprite (called automatically on quit).</summary>
        public static void ReleaseAll()
        {
            for (int i = 0; i < Owned.Count; i++)
            {
                if (Owned[i] != null)
                {
                    Object.Destroy(Owned[i]);
                }
            }

            Owned.Clear();
            Cache.Clear();
        }

        // ------------------------------------------------------------------ internals

        private static long Key(int kind, int a, int b = 0, int c = 0)
        {
            return ((long)kind << 48) | ((long)(a & 0xFFFF) << 32) | ((long)(b & 0xFFFF) << 16) | (long)(c & 0xFFFF);
        }

        private static bool TryGet(long key, out Sprite sprite)
        {
            if (Cache.TryGetValue(key, out sprite) && sprite != null)
            {
                return true;
            }

            sprite = null;
            return false;
        }

        private static Sprite Store(long key, SpriteBitmap bmp, string name)
        {
            Sprite s = CreateSprite(bmp, name);
            Cache[key] = s;
            return s;
        }

        /// <summary>Uploads a <see cref="SpriteBitmap"/> (row 0 = top) into a white RGBA texture and wraps it in a sprite.</summary>
        public static Sprite CreateSprite(SpriteBitmap bmp, string name)
        {
            return CreateSprite(ToPixels(bmp.Alpha, bmp.Width, bmp.Height), bmp.Width, bmp.Height, bmp.Mips, bmp.Tiled, bmp.Border, name);
        }

        /// <summary>
        /// Coverage (0..1, row 0 = top) → white RGBA32 pixels in texture order (row 0 = bottom). Engine-free, so it is safe
        /// on worker threads (the icon prewarm converts there).
        /// </summary>
        internal static Color32[] ToPixels(float[] alpha, int w, int h)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    float v = alpha[src + x];
                    byte b = (byte)(v <= 0f ? 0 : (v >= 1f ? 255 : (int)(v * 255f + 0.5f)));
                    px[dst + x] = new Color32(255, 255, 255, b);
                }
            }

            return px;
        }

        private static Sprite CreateSprite(Color32[] px, int w, int h, bool mips, bool tiled, int border, string name)
        {
            if (!_hooked)
            {
                _hooked = true;
                Application.quitting += ReleaseAll;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mips);
            tex.name = "FieldUI_" + name;
            tex.hideFlags = HideFlags.DontSave;
            tex.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            tex.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            tex.anisoLevel = 1;
            if (mips)
            {
                tex.mipMapBias = -0.35f;
            }

            tex.SetPixels32(px);
            tex.Apply(mips, true);

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.name = "FieldUI_" + name;
            sprite.hideFlags = HideFlags.DontSave;
            Owned.Add(tex);
            Owned.Add(sprite);
            return sprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            Owned.Clear();
            _hooked = false;
        }
    }
}
