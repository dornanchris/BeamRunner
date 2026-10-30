using System;

namespace BeamRunner.UI
{
    /// <summary>How a primitive is combined with what is already on an <see cref="SdfCanvas"/>.</summary>
    public enum SdfOp
    {
        Union = 0,
        Subtract = 1,
        Intersect = 2,
    }

    /// <summary>
    /// Engine-free signed-distance-field rasteriser used to draw the UI kit's icons and sprites.
    /// Primitives are described in "design units" (see <see cref="SetDesignSize"/>) with the origin at the
    /// top-left and Y pointing down (like SVG); distances are stored in pixels (negative = inside) and
    /// converted to anti-aliased coverage with a one-pixel ramp. Angles are in degrees, 0 = +X and
    /// positive = clockwise on screen (because Y points down).
    /// </summary>
    public sealed class SdfCanvas
    {
        private const float Far = 1e5f;

        /// <summary>Pixels outside a primitive's bounds + this margin are left untouched (they cannot change coverage).</summary>
        private const float CullMargin = 3f;

        private readonly float[] _d;
        private float _scale = 1f;
        private float _offsetX;
        private float _offsetY;

        public delegate float DistanceFunc(float x, float y);

        public SdfCanvas(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            Width = width;
            Height = height;
            _d = new float[width * height];
            Clear();
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Pixels per design unit.</summary>
        public float Scale
        {
            get { return _scale; }
        }

        /// <summary>Maps <paramref name="designWidth"/> design units onto the canvas width.</summary>
        public void SetDesignSize(float designWidth)
        {
            _scale = Width / designWidth;
        }

        /// <summary>Translates every subsequent primitive by (dx, dy) design units (optical centring).</summary>
        public void SetOffset(float dx, float dy)
        {
            _offsetX = dx;
            _offsetY = dy;
        }

        public void Clear()
        {
            for (int i = 0; i < _d.Length; i++)
            {
                _d[i] = Far;
            }
        }

        /// <summary>Signed distance (pixels) at a pixel.</summary>
        public float DistanceAt(int x, int y)
        {
            return _d[y * Width + x];
        }

        /// <summary>Anti-aliased coverage 0..1 for every pixel, row-major, row 0 = top.</summary>
        public float[] ToCoverage()
        {
            var a = new float[_d.Length];
            WriteCoverage(a, 1f);
            return a;
        }

        /// <summary>Writes coverage (scaled by <paramref name="opacity"/>) into <paramref name="dst"/> using max-blend.</summary>
        public void WriteCoverage(float[] dst, float opacity)
        {
            for (int i = 0; i < _d.Length; i++)
            {
                float c = Coverage(_d[i]) * opacity;
                if (c > dst[i])
                {
                    dst[i] = c;
                }
            }
        }

        public static float Coverage(float distancePx)
        {
            float c = 0.5f - distancePx;
            return c < 0f ? 0f : (c > 1f ? 1f : c);
        }

        // ------------------------------------------------------------------ primitives

        public void Circle(float cx, float cy, float r, SdfOp op = SdfOp.Union, float stroke = 0f)
        {
            float ext = r + stroke * 0.5f;
            Apply(cx - ext, cy - ext, cx + ext, cy + ext, op, stroke, (x, y) =>
            {
                float dx = x - cx;
                float dy = y - cy;
                return MathF.Sqrt(dx * dx + dy * dy) - r;
            });
        }

        /// <summary>Approximate ellipse (exact on the axes, very close elsewhere for moderate ratios).</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, float angleDeg = 0f, SdfOp op = SdfOp.Union, float stroke = 0f)
        {
            float ext = MathF.Max(rx, ry) + stroke * 0.5f;
            float cs = MathF.Cos(-angleDeg * Deg);
            float sn = MathF.Sin(-angleDeg * Deg);
            float k = MathF.Min(rx, ry);
            Apply(cx - ext, cy - ext, cx + ext, cy + ext, op, stroke, (x, y) =>
            {
                float px = x - cx;
                float py = y - cy;
                float qx = px * cs - py * sn;
                float qy = px * sn + py * cs;
                float ex = qx / rx;
                float ey = qy / ry;
                float len = MathF.Sqrt(ex * ex + ey * ey);
                if (len < 1e-6f)
                {
                    return -k;
                }

                // First-order correction: divide by gradient magnitude.
                float gx = ex / (rx * len);
                float gy = ey / (ry * len);
                float g = MathF.Sqrt(gx * gx + gy * gy);
                return (len - 1f) / MathF.Max(g, 1e-6f);
            });
        }

        /// <summary>Rounded box centred at (cx, cy), optionally rotated.</summary>
        public void Box(float cx, float cy, float halfW, float halfH, float radius = 0f, float angleDeg = 0f, SdfOp op = SdfOp.Union, float stroke = 0f)
        {
            float ext = MathF.Sqrt(halfW * halfW + halfH * halfH) + stroke * 0.5f;
            float cs = MathF.Cos(-angleDeg * Deg);
            float sn = MathF.Sin(-angleDeg * Deg);
            float r = MathF.Min(radius, MathF.Min(halfW, halfH));
            Apply(cx - ext, cy - ext, cx + ext, cy + ext, op, stroke, (x, y) =>
            {
                float px = x - cx;
                float py = y - cy;
                float qx = MathF.Abs(px * cs - py * sn) - halfW + r;
                float qy = MathF.Abs(px * sn + py * cs) - halfH + r;
                return RoundBoxTail(qx, qy, r);
            });
        }

        /// <summary>Rounded rectangle from corner (x0, y0) to (x1, y1).</summary>
        public void Rect(float x0, float y0, float x1, float y1, float radius = 0f, SdfOp op = SdfOp.Union, float stroke = 0f)
        {
            Box((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, MathF.Abs(x1 - x0) * 0.5f, MathF.Abs(y1 - y0) * 0.5f, radius, 0f, op, stroke);
        }

        /// <summary>Capsule (line with round caps) of total <paramref name="width"/>.</summary>
        public void Segment(float ax, float ay, float bx, float by, float width, SdfOp op = SdfOp.Union)
        {
            float hw = width * 0.5f;
            Apply(MathF.Min(ax, bx) - hw, MathF.Min(ay, by) - hw, MathF.Max(ax, bx) + hw, MathF.Max(ay, by) + hw, op, 0f,
                (x, y) => SegmentDistance(x, y, ax, ay, bx, by) - hw);
        }

        /// <summary>Connected capsules through the points (x0, y0, x1, y1, ...), drawn as one shape.</summary>
        public void Polyline(float[] pts, float width, bool closed = false, SdfOp op = SdfOp.Union)
        {
            int n = pts.Length / 2;
            if (n < 2)
            {
                return;
            }

            float hw = width * 0.5f;
            Bounds(pts, out float minX, out float minY, out float maxX, out float maxY);
            Apply(minX - hw, minY - hw, maxX + hw, maxY + hw, op, 0f, (x, y) =>
            {
                float best = Far;
                int segs = closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    int j = (i + 1) % n;
                    float d = SegmentDistance(x, y, pts[i * 2], pts[i * 2 + 1], pts[j * 2], pts[j * 2 + 1]);
                    if (d < best)
                    {
                        best = d;
                    }
                }

                return best - hw;
            });
        }

        /// <summary>
        /// Filled polygon (any simple polygon, either winding). <paramref name="round"/> inflates the shape by that
        /// amount with rounded corners (shrink the input points to compensate).
        /// </summary>
        public void Polygon(float[] pts, SdfOp op = SdfOp.Union, float round = 0f, float stroke = 0f)
        {
            if (pts.Length < 6)
            {
                return;
            }

            float grow = round + stroke * 0.5f;
            Bounds(pts, out float minX, out float minY, out float maxX, out float maxY);
            Apply(minX - grow, minY - grow, maxX + grow, maxY + grow, op, stroke, (x, y) => PolygonDistance(x, y, pts) - round);
        }

        /// <summary>
        /// Arc stroke with round caps: centre, radius, start angle and sweep (degrees, positive = clockwise on screen).
        /// </summary>
        public void Arc(float cx, float cy, float r, float startDeg, float sweepDeg, float width, SdfOp op = SdfOp.Union)
        {
            float hw = width * 0.5f;
            if (sweepDeg < 0f)
            {
                startDeg += sweepDeg;
                sweepDeg = -sweepDeg;
            }

            if (sweepDeg >= 360f)
            {
                Circle(cx, cy, r, op, width);
                return;
            }

            float a0 = startDeg * Deg;
            float sweep = sweepDeg * Deg;
            float e0x = cx + MathF.Cos(a0) * r;
            float e0y = cy + MathF.Sin(a0) * r;
            float e1x = cx + MathF.Cos(a0 + sweep) * r;
            float e1y = cy + MathF.Sin(a0 + sweep) * r;
            float ext = r + hw;
            Apply(cx - ext, cy - ext, cx + ext, cy + ext, op, 0f, (x, y) =>
            {
                float px = x - cx;
                float py = y - cy;
                float ang = MathF.Atan2(py, px) - a0;
                ang %= TwoPi;
                if (ang < 0f)
                {
                    ang += TwoPi;
                }

                if (ang <= sweep)
                {
                    return MathF.Abs(MathF.Sqrt(px * px + py * py) - r) - hw;
                }

                float d0x = x - e0x;
                float d0y = y - e0y;
                float d1x = x - e1x;
                float d1y = y - e1y;
                return MathF.Sqrt(MathF.Min(d0x * d0x + d0y * d0y, d1x * d1x + d1y * d1y)) - hw;
            });
        }

        /// <summary>Full ring of the given stroke width.</summary>
        public void Ring(float cx, float cy, float r, float width, SdfOp op = SdfOp.Union)
        {
            Circle(cx, cy, r, op, width);
        }

        /// <summary>Filled triangle with optional corner rounding (inflates by <paramref name="round"/>).</summary>
        public void Triangle(float ax, float ay, float bx, float by, float cx, float cy, SdfOp op = SdfOp.Union, float round = 0f)
        {
            Polygon(new[] { ax, ay, bx, by, cx, cy }, op, round);
        }

        /// <summary>Arbitrary distance function over a bounding box (design units).</summary>
        public void Custom(float x0, float y0, float x1, float y1, DistanceFunc fn, SdfOp op = SdfOp.Union, float stroke = 0f)
        {
            Apply(x0, y0, x1, y1, op, stroke, fn);
        }

        // ------------------------------------------------------------------ helpers

        public const float Deg = MathF.PI / 180f;
        private const float TwoPi = MathF.PI * 2f;

        private void Apply(float x0, float y0, float x1, float y1, SdfOp op, float stroke, DistanceFunc fn)
        {
            float hs = stroke * 0.5f;
            float ox = _offsetX;
            float oy = _offsetY;
            x0 += ox;
            x1 += ox;
            y0 += oy;
            y1 += oy;
            int px0;
            int py0;
            int px1;
            int py1;
            if (op == SdfOp.Intersect)
            {
                px0 = 0;
                py0 = 0;
                px1 = Width - 1;
                py1 = Height - 1;
            }
            else
            {
                px0 = Math.Max(0, (int)MathF.Floor(x0 * _scale - CullMargin));
                py0 = Math.Max(0, (int)MathF.Floor(y0 * _scale - CullMargin));
                px1 = Math.Min(Width - 1, (int)MathF.Ceiling(x1 * _scale + CullMargin));
                py1 = Math.Min(Height - 1, (int)MathF.Ceiling(y1 * _scale + CullMargin));
            }

            float inv = 1f / _scale;
            for (int py = py0; py <= py1; py++)
            {
                float uy = (py + 0.5f) * inv - oy;
                int row = py * Width;
                for (int px = px0; px <= px1; px++)
                {
                    float ux = (px + 0.5f) * inv - ox;
                    float d = fn(ux, uy);
                    if (hs > 0f)
                    {
                        d = MathF.Abs(d) - hs;
                    }

                    d *= _scale;
                    int i = row + px;
                    float cur = _d[i];
                    switch (op)
                    {
                        case SdfOp.Union:
                            _d[i] = d < cur ? d : cur;
                            break;
                        case SdfOp.Subtract:
                            _d[i] = -d > cur ? -d : cur;
                            break;
                        default:
                            _d[i] = d > cur ? d : cur;
                            break;
                    }
                }
            }
        }

        private static float RoundBoxTail(float qx, float qy, float r)
        {
            float ox = qx > 0f ? qx : 0f;
            float oy = qy > 0f ? qy : 0f;
            float outside = MathF.Sqrt(ox * ox + oy * oy);
            float inside = MathF.Min(MathF.Max(qx, qy), 0f);
            return outside + inside - r;
        }

        public static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float pax = px - ax;
            float pay = py - ay;
            float bax = bx - ax;
            float bay = by - ay;
            float len2 = bax * bax + bay * bay;
            float h = len2 > 1e-12f ? (pax * bax + pay * bay) / len2 : 0f;
            h = h < 0f ? 0f : (h > 1f ? 1f : h);
            float dx = pax - bax * h;
            float dy = pay - bay * h;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Exact signed distance to a simple polygon (negative inside).</summary>
        public static float PolygonDistance(float px, float py, float[] v)
        {
            int n = v.Length / 2;
            float d = (px - v[0]) * (px - v[0]) + (py - v[1]) * (py - v[1]);
            float s = 1f;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                float vix = v[i * 2];
                float viy = v[i * 2 + 1];
                float vjx = v[j * 2];
                float vjy = v[j * 2 + 1];
                float ex = vjx - vix;
                float ey = vjy - viy;
                float wx = px - vix;
                float wy = py - viy;
                float ee = ex * ex + ey * ey;
                float t = ee > 1e-12f ? (wx * ex + wy * ey) / ee : 0f;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                float bx = wx - ex * t;
                float by = wy - ey * t;
                float bb = bx * bx + by * by;
                if (bb < d)
                {
                    d = bb;
                }

                bool c1 = py >= viy;
                bool c2 = py < vjy;
                bool c3 = ex * wy > ey * wx;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                {
                    s = -s;
                }
            }

            return s * MathF.Sqrt(d);
        }

        private static void Bounds(float[] pts, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue;
            minY = float.MaxValue;
            maxX = float.MinValue;
            maxY = float.MinValue;
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                minX = MathF.Min(minX, pts[i]);
                maxX = MathF.Max(maxX, pts[i]);
                minY = MathF.Min(minY, pts[i + 1]);
                maxY = MathF.Max(maxY, pts[i + 1]);
            }
        }
    }
}
