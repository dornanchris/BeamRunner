using System;

namespace BeamRunner.UI
{
    /// <summary>
    /// Engine-free icon artwork. Every icon is drawn on a 24×24 design grid (content inside ~2.5..21.5, Y down)
    /// with a common stroke weight, then rasterised with <see cref="SdfCanvas"/> into white coverage.
    /// </summary>
    public static class IconPainter
    {
        /// <summary>Design grid size.</summary>
        public const float Grid = 24f;

        /// <summary>Standard stroke weight in design units.</summary>
        public const float Stroke = 2.3f;

        /// <summary>Coverage (0..1, row-major, row 0 = top) of an icon at <paramref name="size"/>×<paramref name="size"/> pixels.</summary>
        public static float[] Paint(IconId id, int size)
        {
            var c = new SdfCanvas(size, size);
            c.SetDesignSize(Grid);
            Draw(c, id);
            return c.ToCoverage();
        }

        /// <summary>Draws the icon into an existing canvas (design size must be 24).</summary>
        public static void Draw(SdfCanvas c, IconId id)
        {
            OpticalOffset(id, out float ox, out float oy);
            c.SetOffset(ox, oy);
            DrawShapes(c, id);
            c.SetOffset(0f, 0f);
        }

        /// <summary>Per-icon nudges (design units) so each glyph reads as centred.</summary>
        private static void OpticalOffset(IconId id, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;
            switch (id)
            {
                case IconId.Music:
                    dx = -1.5f;
                    dy = 0.5f;
                    break;
                case IconId.Cart:
                    dx = 0.4f;
                    break;
                case IconId.SoundOn:
                    dx = 0.45f;
                    break;
                case IconId.SoundOff:
                    dx = 0.25f;
                    break;
                case IconId.Hangar:
                    dy = 0.45f;
                    break;
                case IconId.Trophy:
                    dy = 0.35f;
                    break;
                case IconId.Gem:
                    dy = -0.3f;
                    break;
                case IconId.Lock:
                    dy = -0.45f;
                    break;
                case IconId.Speed:
                    dy = 0.45f;
                    break;
                case IconId.Calendar:
                    dy = 0.2f;
                    break;
                case IconId.Gamepad:
                    dy = -0.7f;
                    break;
                case IconId.PhoneTilt:
                    dy = -0.6f;
                    break;
                case IconId.Magnet:
                    dy = 0.3f;
                    break;
                case IconId.Gift:
                    dy = 0.4f;
                    break;
                case IconId.Check:
                    dy = -0.3f;
                    break;
                case IconId.ChevronRight:
                    dx = -0.4f;
                    break;
                case IconId.ChevronLeft:
                    dx = 0.4f;
                    break;
                case IconId.ChevronUp:
                    dy = 0.4f;
                    break;
                case IconId.ChevronDown:
                    dy = -0.4f;
                    break;
                case IconId.Hand:
                    dy = 0.3f;
                    break;
            }
        }

        private static void DrawShapes(SdfCanvas c, IconId id)
        {
            const float s = Stroke;
            switch (id)
            {
                case IconId.None:
                    break;

                case IconId.Play:
                    c.Triangle(7.4f, 5.6f, 7.4f, 18.4f, 18.6f, 12f, SdfOp.Union, 1.3f);
                    break;

                case IconId.Pause:
                    c.Rect(6.2f, 4.8f, 10.4f, 19.2f, 1.4f);
                    c.Rect(13.6f, 4.8f, 17.8f, 19.2f, 1.4f);
                    break;

                case IconId.Settings:
                    DrawGear(c);
                    break;

                case IconId.Back:
                    c.Segment(19f, 12f, 5.6f, 12f, s);
                    c.Polyline(new[] { 11.6f, 5.4f, 5f, 12f, 11.6f, 18.6f }, s);
                    break;

                case IconId.Close:
                    c.Segment(6.2f, 6.2f, 17.8f, 17.8f, s * 1.08f);
                    c.Segment(17.8f, 6.2f, 6.2f, 17.8f, s * 1.08f);
                    break;

                case IconId.Home:
                    c.Polyline(new[] { 3.2f, 11.6f, 12f, 3.9f, 20.8f, 11.6f }, s);
                    c.Polyline(new[] { 6.1f, 9.6f, 6.1f, 20.2f, 17.9f, 20.2f, 17.9f, 9.6f }, s);
                    c.Rect(10.2f, 14.2f, 13.8f, 20.2f, 0.7f);
                    break;

                case IconId.Retry:
                    CircularArrow(c, 12f, 12.4f, 7.4f, -50f, 300f, s, 3.0f);
                    break;

                case IconId.Restore:
                    CircularArrow(c, 12f, 12.4f, 7.6f, -130f, -300f, s, 3.0f);
                    c.Polyline(new[] { 12f, 8.2f, 12f, 12.6f, 14.9f, 14.4f }, s * 0.85f);
                    break;

                case IconId.Trophy:
                    c.Rect(6.9f, 3.6f, 17.1f, 9f, 0.9f);
                    c.Circle(12f, 8.6f, 5.1f);
                    c.Arc(6.9f, 7.9f, 2.9f, 90f, 180f, 1.9f);
                    c.Arc(17.1f, 7.9f, 2.9f, -90f, 180f, 1.9f);
                    c.Rect(10.7f, 12.5f, 13.3f, 17.4f);
                    c.Rect(7.2f, 17.2f, 16.8f, 20.4f, 1f);
                    break;

                case IconId.Hangar:
                    DrawShip(c);
                    break;

                case IconId.Missions:
                    c.Ring(12f, 12f, 8.7f, s);
                    c.Ring(12f, 12f, 4.6f, s);
                    c.Circle(12f, 12f, 1.5f);
                    break;

                case IconId.Stats:
                    c.Rect(4.4f, 12.2f, 8.1f, 20f, 1.1f);
                    c.Rect(10.15f, 4.4f, 13.85f, 20f, 1.1f);
                    c.Rect(15.9f, 8.6f, 19.6f, 20f, 1.1f);
                    break;

                case IconId.Gem:
                    DrawGem(c);
                    break;

                case IconId.Lock:
                    c.Arc(12f, 9.4f, 4.4f, 180f, 180f, s);
                    c.Segment(7.6f, 9.4f, 7.6f, 11.5f, s);
                    c.Segment(16.4f, 9.4f, 16.4f, 11.5f, s);
                    c.Rect(4.8f, 10.6f, 19.2f, 21f, 2.2f);
                    c.Circle(12f, 14.7f, 1.8f, SdfOp.Subtract);
                    c.Rect(11.15f, 15f, 12.85f, 18.4f, 0.6f, SdfOp.Subtract);
                    break;

                case IconId.Check:
                    c.Polyline(new[] { 4.8f, 12.6f, 9.6f, 17.4f, 19.2f, 7.4f }, s * 1.12f);
                    break;

                case IconId.Star:
                    c.Polygon(StarPoints(12f, 12.7f, 9.1f, 3.9f, 5), SdfOp.Union, 1.05f);
                    break;

                case IconId.SoundOn:
                    Speaker(c, -0.6f);
                    c.Arc(11.6f, 12f, 4.2f, -48f, 96f, s * 0.9f);
                    c.Arc(11.6f, 12f, 8.3f, -52f, 104f, s * 0.9f);
                    break;

                case IconId.SoundOff:
                    Speaker(c, -0.6f);
                    c.Segment(15.6f, 9.4f, 20.6f, 14.6f, s * 0.95f);
                    c.Segment(20.6f, 9.4f, 15.6f, 14.6f, s * 0.95f);
                    break;

                case IconId.Music:
                    c.Ellipse(8f, 17.6f, 3.05f, 2.3f, -22f);
                    c.Ellipse(17.6f, 15.6f, 3.05f, 2.3f, -22f);
                    c.Segment(10.55f, 17.2f, 10.55f, 6.6f, 1.9f);
                    c.Segment(20.15f, 15.2f, 20.15f, 4.6f, 1.9f);
                    c.Polygon(new[] { 9.6f, 5.4f, 21.1f, 3.1f, 21.1f, 6.5f, 9.6f, 8.8f }, SdfOp.Union, 0.3f);
                    break;

                case IconId.Vibration:
                    c.Rect(8.4f, 3.6f, 15.6f, 20.4f, 1.9f, SdfOp.Union, s * 0.9f);
                    c.Segment(11f, 17.3f, 13f, 17.3f, 1.4f);
                    c.Segment(5f, 8.4f, 5f, 15.6f, s * 0.85f);
                    c.Segment(2.1f, 10.2f, 2.1f, 13.8f, s * 0.85f);
                    c.Segment(19f, 8.4f, 19f, 15.6f, s * 0.85f);
                    c.Segment(21.9f, 10.2f, 21.9f, 13.8f, s * 0.85f);
                    break;

                case IconId.Gift:
                    c.Rect(3.4f, 7.6f, 20.6f, 11.6f, 1.1f);
                    c.Rect(4.9f, 12.8f, 19.1f, 20.8f, 1.3f);
                    c.Rect(11f, 6f, 13f, 22f, 0f, SdfOp.Subtract);
                    c.Ellipse(9f, 5.1f, 2.9f, 1.75f, 28f, SdfOp.Union, 1.7f);
                    c.Ellipse(15f, 5.1f, 2.9f, 1.75f, -28f, SdfOp.Union, 1.7f);
                    break;

                case IconId.Ad:
                    c.Rect(2.6f, 5f, 21.4f, 19f, 3f, SdfOp.Union, s * 0.95f);
                    c.Triangle(10.1f, 9.1f, 10.1f, 14.9f, 15.3f, 12f, SdfOp.Union, 0.7f);
                    break;

                case IconId.Cart:
                    c.Polyline(new[] { 2.4f, 4.4f, 5.2f, 4.4f, 7.9f, 15.4f, 18.6f, 15.4f }, s * 0.95f);
                    c.Polygon(new[] { 6.4f, 7.7f, 20.6f, 7.7f, 18.9f, 12.9f, 7.6f, 12.9f }, SdfOp.Union, 0.5f);
                    c.Circle(9.1f, 19.3f, 1.75f);
                    c.Circle(17.3f, 19.3f, 1.75f);
                    break;

                case IconId.Shield:
                    {
                        float[] outer = ShieldPoints(12f, 12f, 1f);
                        c.Polygon(outer, SdfOp.Union, 0.6f);
                        c.Polygon(ShieldPoints(12f, 12.1f, 0.62f), SdfOp.Subtract, 0f, 1.25f);
                    }

                    break;

                case IconId.Magnet:
                    c.Arc(12f, 11.4f, 5.9f, 0f, 180f, 4.8f);
                    c.Rect(3.7f, 3.6f, 8.5f, 11.6f, 0.8f);
                    c.Rect(15.5f, 3.6f, 20.3f, 11.6f, 0.8f);
                    c.Rect(2f, 6.7f, 22f, 7.8f, 0f, SdfOp.Subtract);
                    break;

                case IconId.Bolt:
                    c.Polygon(new[] { 14.1f, 2.6f, 5.6f, 13.5f, 11.2f, 13.5f, 9.9f, 21.4f, 18.4f, 10.3f, 12.8f, 10.3f }, SdfOp.Union, 0.75f);
                    break;

                case IconId.Info:
                    c.Ring(12f, 12f, 9.2f, s);
                    c.Circle(12f, 7.7f, 1.5f);
                    c.Segment(12f, 11f, 12f, 16.8f, s * 1.05f);
                    break;

                case IconId.Crown:
                    c.Polygon(new[] { 3.6f, 8.4f, 8.2f, 12.3f, 12f, 5.6f, 15.8f, 12.3f, 20.4f, 8.4f, 18.6f, 17.2f, 5.4f, 17.2f }, SdfOp.Union, 0.7f);
                    c.Circle(3.4f, 7.4f, 1.55f);
                    c.Circle(12f, 4.5f, 1.55f);
                    c.Circle(20.6f, 7.4f, 1.55f);
                    c.Rect(5f, 18.6f, 19f, 20.8f, 0.9f);
                    break;

                case IconId.ChevronRight:
                    c.Polyline(new[] { 9.2f, 4.8f, 16.4f, 12f, 9.2f, 19.2f }, s * 1.12f);
                    break;

                case IconId.ChevronLeft:
                    c.Polyline(new[] { 14.8f, 4.8f, 7.6f, 12f, 14.8f, 19.2f }, s * 1.12f);
                    break;

                case IconId.ChevronUp:
                    c.Polyline(new[] { 4.8f, 14.8f, 12f, 7.6f, 19.2f, 14.8f }, s * 1.12f);
                    break;

                case IconId.ChevronDown:
                    c.Polyline(new[] { 4.8f, 9.2f, 12f, 16.4f, 19.2f, 9.2f }, s * 1.12f);
                    break;

                case IconId.Globe:
                    c.Ring(12f, 12f, 9.2f, s);
                    c.Ellipse(12f, 12f, 4.1f, 9.2f, 0f, SdfOp.Union, s * 0.85f);
                    c.Segment(3.4f, 12f, 20.6f, 12f, s * 0.85f);
                    c.Segment(5.3f, 7.3f, 18.7f, 7.3f, s * 0.7f);
                    c.Segment(5.3f, 16.7f, 18.7f, 16.7f, s * 0.7f);
                    break;

                case IconId.Plus:
                    c.Segment(12f, 4.8f, 12f, 19.2f, s * 1.08f);
                    c.Segment(4.8f, 12f, 19.2f, 12f, s * 1.08f);
                    break;

                case IconId.Minus:
                    c.Segment(4.8f, 12f, 19.2f, 12f, s * 1.08f);
                    break;

                case IconId.Speed:
                    c.Arc(12f, 13.6f, 8.6f, 150f, 240f, s);
                    c.Segment(12f, 13.6f, 16.4f, 8.2f, s * 0.95f);
                    c.Circle(12f, 13.6f, 2.3f);
                    c.Segment(12f, 5.9f, 12f, 7.3f, 1.6f);
                    c.Segment(5.3f, 9.8f, 6.5f, 10.5f, 1.6f);
                    c.Segment(18.7f, 9.8f, 17.5f, 10.5f, 1.6f);
                    break;

                case IconId.Heart:
                    DrawHeart(c, 12f, 12.2f, 16.4f);
                    break;

                case IconId.Fuel:
                    c.Circle(12f, 14.6f, 6.2f);
                    c.Triangle(12f, 2.6f, 6.3f, 12.4f, 17.7f, 12.4f, SdfOp.Union, 0.5f);
                    c.Arc(12f, 14.6f, 3.4f, 100f, 80f, 1.4f, SdfOp.Subtract);
                    break;

                case IconId.Calendar:
                    c.Rect(3.4f, 5f, 20.6f, 20.6f, 2.2f, SdfOp.Union, s * 0.9f);
                    c.Rect(3.4f, 5f, 20.6f, 9.8f, 2.2f);
                    c.Rect(3.4f, 7.5f, 20.6f, 9.8f, 0f);
                    c.Segment(8f, 2.6f, 8f, 6.6f, 4.4f, SdfOp.Subtract);
                    c.Segment(16f, 2.6f, 16f, 6.6f, 4.4f, SdfOp.Subtract);
                    c.Segment(8f, 2.6f, 8f, 6.6f, 2f);
                    c.Segment(16f, 2.6f, 16f, 6.6f, 2f);
                    for (int row = 0; row < 2; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            float x = 7.1f + col * 4.9f;
                            float y = 13.2f + row * 3.9f;
                            c.Rect(x - 1.25f, y - 1.2f, x + 1.25f, y + 1.2f, 0.45f);
                        }
                    }

                    break;

                case IconId.Gamepad:
                    DrawGamepad(c);
                    break;

                case IconId.Hand:
                    DrawHand(c);
                    break;

                case IconId.PhoneTilt:
                    c.Box(12f, 14.6f, 3.9f, 6.5f, 1.6f, -18f, SdfOp.Union, s * 0.9f);
                    c.Box(12f, 14.6f, 1.1f, 0.42f, 0.4f, -18f);
                    ArrowArc(c, 12f, 16.4f, 12.6f, -128f, 76f, 1.9f, 2.5f, true, true);
                    break;

                case IconId.Warning:
                    c.Polygon(new[] { 12f, 4.4f, 20.6f, 19.3f, 3.4f, 19.3f }, SdfOp.Union, 1.1f, s * 0.95f);
                    c.Segment(12f, 9.7f, 12f, 13.8f, s);
                    c.Circle(12f, 16.6f, 1.3f);
                    break;

                default:
                    c.Circle(12f, 12f, 3f);
                    break;
            }
        }

        // ------------------------------------------------------------------ compound shapes

        private static void DrawGear(SdfCanvas c)
        {
            const float cx = 12f;
            const float cy = 12f;
            c.Circle(cx, cy, 6.7f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                float r = 8.1f;
                float tx = cx + MathF.Cos(a * SdfCanvas.Deg) * r;
                float ty = cy + MathF.Sin(a * SdfCanvas.Deg) * r;
                c.Box(tx, ty, 2.05f, 1.75f, 0.7f, a);
            }

            c.Circle(cx, cy, 3.1f, SdfOp.Subtract);
        }

        private static void DrawShip(SdfCanvas c)
        {
            float[] ship =
            {
                12f, 2.8f,
                13.7f, 8.6f,
                15.1f, 11.2f,
                21f, 16.6f,
                21f, 18.9f,
                15.3f, 17.3f,
                14.3f, 20.2f,
                9.7f, 20.2f,
                8.7f, 17.3f,
                3f, 18.9f,
                3f, 16.6f,
                8.9f, 11.2f,
                10.3f, 8.6f,
            };
            c.Polygon(ship, SdfOp.Union, 0.55f);
            c.Ellipse(12f, 10.4f, 1.15f, 2.6f, 0f, SdfOp.Subtract);
            c.Segment(10.3f, 16.2f, 13.7f, 16.2f, 0.9f, SdfOp.Subtract);
        }

        private static void DrawGem(SdfCanvas c)
        {
            float[] gem =
            {
                7.6f, 4.3f,
                16.4f, 4.3f,
                20.9f, 9.6f,
                12f, 20.6f,
                3.1f, 9.6f,
            };
            c.Polygon(gem, SdfOp.Union, 0.7f);
            const float w = 0.95f;
            c.Segment(2f, 9.6f, 22f, 9.6f, w, SdfOp.Subtract);
            c.Segment(12f, 4.3f, 9.2f, 9.6f, w, SdfOp.Subtract);
            c.Segment(12f, 4.3f, 14.8f, 9.6f, w, SdfOp.Subtract);
            c.Segment(9.2f, 9.6f, 11.75f, 18.9f, w, SdfOp.Subtract);
            c.Segment(14.8f, 9.6f, 12.25f, 18.9f, w, SdfOp.Subtract);
        }

        private static void DrawHeart(SdfCanvas c, float cx, float cy, float width)
        {
            // Inigo Quilez' exact heart: spans x in [-0.604, 0.604], y (up) in [0, 1.104].
            float k = width / 1.2071f;
            float bottom = cy + 1.1036f * k * 0.5f;
            float half = width * 0.5f;
            c.Custom(cx - half, bottom - 1.1036f * k, cx + half, bottom, (x, y) =>
            {
                float px = MathF.Abs(x - cx) / k;
                float py = (bottom - y) / k;
                float d;
                if (py + px > 1f)
                {
                    float dx = px - 0.25f;
                    float dy = py - 0.75f;
                    d = MathF.Sqrt(dx * dx + dy * dy) - 0.35355339f;
                }
                else
                {
                    float ax = px;
                    float ay = py - 1f;
                    float m = 0.5f * MathF.Max(px + py, 0f);
                    float bx = px - m;
                    float by = py - m;
                    d = MathF.Sqrt(MathF.Min(ax * ax + ay * ay, bx * bx + by * by)) * MathF.Sign(px - py);
                }

                return d * k;
            });
        }

        private static void DrawGamepad(SdfCanvas c)
        {
            c.Rect(2.8f, 6.8f, 21.2f, 15.6f, 4.4f);
            c.Circle(6.4f, 15.4f, 3.6f);
            c.Circle(17.6f, 15.4f, 3.6f);
            // D-pad
            c.Rect(5.2f, 10.4f, 9.6f, 12.2f, 0.4f, SdfOp.Subtract);
            c.Rect(6.5f, 9.1f, 8.3f, 13.5f, 0.4f, SdfOp.Subtract);
            // Face buttons
            c.Circle(15.6f, 10.1f, 1.25f, SdfOp.Subtract);
            c.Circle(18.2f, 12.6f, 1.25f, SdfOp.Subtract);
        }

        private static void DrawHand(SdfCanvas c)
        {
            // Index finger up, other fingers curled, thumb out to the left.
            const float o = 0.5f;
            c.Segment(11f + o, 5.8f, 11f + o, 14f, 3.3f);
            c.Segment(14.65f + o, 10.3f, 14.65f + o, 14f, 3f);
            c.Segment(17.95f + o, 11.7f, 17.95f + o, 14.4f, 2.8f);
            c.Rect(9.35f + o, 12.9f, 19.35f + o, 20.8f, 3.1f);
            c.Segment(10.4f + o, 17.2f, 6.5f + o, 13.6f, 3.1f);
            // Drag hint.
            c.Polyline(new[] { 6.4f + o, 2.6f, 3.6f + o, 5.4f, 6.4f + o, 8.2f }, 1.8f);
            c.Polyline(new[] { 15.6f + o, 2.6f, 18.4f + o, 5.4f, 15.6f + o, 8.2f }, 1.8f);
        }

        private static void Speaker(SdfCanvas c, float dx)
        {
            c.Polygon(new[]
            {
                3.6f + dx, 9.1f,
                7.4f + dx, 9.1f,
                12.2f + dx, 5.2f,
                12.2f + dx, 18.8f,
                7.4f + dx, 14.9f,
                3.6f + dx, 14.9f,
            }, SdfOp.Union, 0.8f);
        }

        /// <summary>Arc with a filled arrow head at its end (sweep sign = direction).</summary>
        private static void CircularArrow(SdfCanvas c, float cx, float cy, float r, float startDeg, float sweepDeg, float width, float headScale = 3.2f)
        {
            ArrowArc(c, cx, cy, r, startDeg, sweepDeg, width, headScale, false, true);
        }

        /// <summary>Arc with optional arrow heads at either end; heads follow the arc and point outward.</summary>
        private static void ArrowArc(SdfCanvas c, float cx, float cy, float r, float startDeg, float sweepDeg, float width, float headScale, bool headAtStart, bool headAtEnd)
        {
            float dir = MathF.Sign(sweepDeg);
            float headLen = headScale * 1.1f;
            // Angular length of the head; the arc body stops at the head's base.
            float headDeg = headLen / r / SdfCanvas.Deg;
            float endDeg = startDeg + sweepDeg;
            float bodyStart = startDeg + (headAtStart ? dir * headDeg * 0.45f : 0f);
            float bodyEnd = endDeg - (headAtEnd ? dir * headDeg * 0.45f : 0f);
            c.Arc(cx, cy, r, bodyStart, bodyEnd - bodyStart, width);
            if (headAtEnd)
            {
                ArrowHead(c, cx, cy, r, bodyEnd, bodyEnd + dir * headDeg, headScale);
            }

            if (headAtStart)
            {
                ArrowHead(c, cx, cy, r, bodyStart, bodyStart - dir * headDeg, headScale);
            }
        }

        /// <summary>Filled arrow head whose base centre and tip both sit on the circle.</summary>
        private static void ArrowHead(SdfCanvas c, float cx, float cy, float r, float baseDeg, float tipDeg, float headScale)
        {
            float ab = baseDeg * SdfCanvas.Deg;
            float at = tipDeg * SdfCanvas.Deg;
            float bx = cx + MathF.Cos(ab) * r;
            float by = cy + MathF.Sin(ab) * r;
            float tx = cx + MathF.Cos(at) * r;
            float ty = cy + MathF.Sin(at) * r;
            float dx = tx - bx;
            float dy = ty - by;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f)
            {
                return;
            }

            dx /= len;
            dy /= len;
            float nx = -dy;
            float ny = dx;
            float hw = headScale * 0.9f;
            c.Triangle(bx + nx * hw, by + ny * hw, bx - nx * hw, by - ny * hw, tx, ty, SdfOp.Union, 0.45f);
        }

        private static float[] StarPoints(float cx, float cy, float outer, float inner, int points)
        {
            var p = new float[points * 4];
            for (int i = 0; i < points * 2; i++)
            {
                float a = (-90f + i * 180f / points) * SdfCanvas.Deg;
                float r = (i & 1) == 0 ? outer : inner;
                p[i * 2] = cx + MathF.Cos(a) * r;
                p[i * 2 + 1] = cy + MathF.Sin(a) * r;
            }

            return p;
        }

        private static float[] ShieldPoints(float cx, float cy, float scale)
        {
            // Right half: top centre → shoulder → side → quadratic curve down to the tip; mirrored for the left half.
            const int curveSteps = 10;
            var right = new float[(3 + curveSteps) * 2];
            int n = 0;
            void Add(float x, float y)
            {
                right[n++] = x;
                right[n++] = y;
            }

            Add(0f, -9.2f);
            Add(7.6f, -6.4f);
            Add(7.6f, -1.2f);
            float p0x = 7.6f;
            float p0y = -1.2f;
            float p1x = 7.3f;
            float p1y = 5.6f;
            float p2x = 0f;
            float p2y = 9.6f;
            for (int i = 1; i <= curveSteps; i++)
            {
                float t = i / (float)curveSteps;
                float u = 1f - t;
                Add(u * u * p0x + 2f * u * t * p1x + t * t * p2x, u * u * p0y + 2f * u * t * p1y + t * t * p2y);
            }

            int half = n / 2;
            // Full polygon: right half (top → tip) then the left half mirrored (tip excluded → top excluded).
            var pts = new float[(half + half - 2) * 2];
            int k = 0;
            for (int i = 0; i < half; i++)
            {
                pts[k++] = cx + right[i * 2] * scale;
                pts[k++] = cy + right[i * 2 + 1] * scale;
            }

            for (int i = half - 2; i >= 1; i--)
            {
                pts[k++] = cx - right[i * 2] * scale;
                pts[k++] = cy + right[i * 2 + 1] * scale;
            }

            return pts;
        }
    }
}
