using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KeyboardToggler.Ui
{
    // The status badge in the keyboard pictogram's bottom-right corner.
    enum GlyphBadge
    {
        None,
        On,    // a green check: the laptop keyboard works
        Off,   // a red bar, like a no-entry sign: the keyboard is switched off
    }

    // The keyboard pictogram used by the tray icon, the app icon and the windows, in the Windows 11 (Fluent)
    // style: a rounded outline, two staggered rows of keys and a space bar. A badge can sit in the bottom-right
    // corner, cut out of the keyboard with a thin transparent ring like the badges on Windows' own tray icons,
    // so the tray icon says "on" or "off" at a glance, by colour and by shape.
    //
    // It's laid out afresh for the exact pixel size it's drawn at: outline, keys, space bar and badge sit on
    // whole pixels and the keys keep an even rhythm, so it's crisp at tray sizes (16, 20, 24, 32 px...) and
    // clean when large. It's rasterised here (exact anti-aliasing) instead of with GDI+ paths.
    // System.Drawing only: tools\MakeIcon.cs uses it too.
    static class KeyboardGlyph
    {
        // Saturated enough to stand out on a dark or a light taskbar, dark enough to carry a white mark.
        static readonly Color OnColor = Color.FromArgb(0x1A, 0x9A, 0x45), OffColor = Color.FromArgb(0xD7, 0x30, 0x3A);

        // Draws the glyph centred in box (as a square, the smaller side of box). The ring around a badge is
        // transparent, so whatever is behind the glyph shows through it.
        public static void Draw(Graphics g, RectangleF box, Color color, GlyphBadge badge)
        {
            // Work in device pixels so the glyph lands on the pixel grid whatever the Graphics' transform.
            var p = new[] { new PointF(box.Left, box.Top), new PointF(box.Right, box.Bottom) };
            g.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, p);
            float w = Math.Abs(p[1].X - p[0].X), h = Math.Abs(p[1].Y - p[0].Y);
            int size = (int)Math.Round(Math.Min(w, h));
            if (size < 1) return;
            int x = (int)Math.Round(Math.Min(p[0].X, p[1].X) + (w - size) / 2);
            int y = (int)Math.Round(Math.Min(p[0].Y, p[1].Y) + (h - size) / 2);
            using (var bmp = Render(size, color, badge))
            {
                var state = g.Save();
                g.ResetTransform();
                g.PageUnit = GraphicsUnit.Pixel;
                g.PageScale = 1;
                g.CompositingMode = CompositingMode.SourceOver;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, new Rectangle(x, y, size, size), 0, 0, size, size, GraphicsUnit.Pixel);
                g.Restore(state);
            }
        }

        // The glyph on a transparent size x size bitmap with straight (not premultiplied) alpha, for icons.
        public static Bitmap Render(int size, Color color, GlyphBadge badge = GlyphBadge.None)
        {
            var layout = new Layout(size, badge != GlyphBadge.None);
            Color fill = badge == GlyphBadge.On ? OnColor : OffColor;
            Field mark = badge == GlyphBadge.On ? (Field)layout.Check : layout.Bar;
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[size];
                for (int py = 0; py < size; py++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        float a = 0, r = 0, g = 0, b = 0;   // premultiplied
                        Over(ref a, ref r, ref g, ref b, color, Cover(layout.Keyboard, px, py));
                        if (badge != GlyphBadge.None)
                        {
                            Over(ref a, ref r, ref g, ref b, fill, Cover(layout.Disc, px, py));
                            Over(ref a, ref r, ref g, ref b, Color.White, Cover(mark, px, py));
                        }
                        row[px] = a <= 0 ? 0 : Byte(a) << 24 | Byte(r / a) << 16 | Byte(g / a) << 8 | Byte(b / a);
                    }
                    Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)py * data.Stride), size);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        // Paints colour c at coverage over what's there (premultiplied, values 0-1).
        static void Over(ref float a, ref float r, ref float g, ref float b, Color c, float coverage)
        {
            float k = coverage * c.A / 255f;
            if (k <= 0) return;
            a = k + a * (1 - k);
            r = c.R / 255f * k + r * (1 - k);
            g = c.G / 255f * k + g * (1 - k);
            b = c.B / 255f * k + b * (1 - k);
        }

        static int Byte(float v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }

        // Signed distance to a shape in pixels (negative inside; a lower bound away from the edges).
        delegate float Field(float x, float y);

        // Fraction of pixel (px, py) inside the shape: exact box-filter anti-aliasing, supersampled where an
        // edge crosses the pixel.
        static float Cover(Field f, int px, int py)
        {
            float d = f(px + 0.5f, py + 0.5f);
            if (d >= 0.75f) return 0;      // the whole pixel is outside (0.75 > half its diagonal)
            if (d <= -0.75f) return 1;
            const int S = 8;
            int hits = 0;
            for (int sy = 0; sy < S; sy++)
                for (int sx = 0; sx < S; sx++)
                    if (f(px + (sx + 0.5f) / S, py + (sy + 0.5f) / S) < 0) hits++;
            return hits / (float)(S * S);
        }

        // The glyph's shapes in pixels for an n x n box. Proportions follow the Windows 11 keyboard icon
        // (on its 16 px grid: body 16 x 11, 1 px outline, keys on a 3 px pitch, space bar 10 x 1), with every
        // part snapped to whole pixels for this size.
        sealed class Layout
        {
            readonly float left, top, right, bottom, stroke, radius;   // body outline, outer edge
            readonly RectangleF[] keys;
            readonly float keyRadius;
            readonly RectangleF space;                                // space bar, round ends
            readonly bool badged, dropSpace;
            readonly float cx, cy, badgeRadius, cutRadius;            // the badge disc, and the transparent ring around it
            readonly RectangleF bar;                                  // "off": a bar across the badge
            readonly PointF tick0, tick1, tick2;                      // "on": a check mark
            readonly float tickHalf;

            public Layout(int n, bool badge)
            {
                float u = n / 16f;
                stroke = n < 24 ? 1 : n < 30 ? 1.5f : (float)Math.Floor(u * 2 + 0.25) / 2;   // then about n / 16 in half pixels
                int pitch = Math.Max(2, Round(3 * u));
                int key = Math.Max(1, Math.Min(Round(1.5f * u), pitch - 2));
                int spaceH = Math.Max(1, Round(stroke));
                int inset = Math.Max(2, Round(stroke + u));            // outline plus margin above and below the keys
                int bodyH = 2 * pitch + spaceH + 2 * inset;            // two rows of keys, then the space bar
                left = 0; right = n;
                // With a badge the keyboard moves up a little, so the badge covers less of it.
                top = Math.Max(0, (float)Math.Floor((n - bodyH) / 2f) - (badge ? Math.Max(1, n / 10) : 0));
                bottom = top + bodyH;
                radius = Math.Max(1, Round(4 * u) / 2f);

                // Row 1 sits a little left and row 2 a little right, like a real keyboard (and the Fluent glyph).
                float y1 = top + inset, mid = n / 2f, shift = Math.Max(0.25f, u / 4);
                var all = new RectangleF[7];
                for (int i = 0; i < 4; i++) all[i] = new RectangleF(Round(mid + (i - 1.5f) * pitch - shift - key / 2f), y1, key, key);
                int stagger = Round(mid - pitch + shift - key / 2f) - (int)all[0].X;
                if (stagger % pitch == 0) stagger--;                     // tiny sizes: keep row 2 between row 1's keys
                for (int i = 0; i < 3; i++) all[4 + i] = new RectangleF(all[0].X + stagger + i * pitch, y1 + pitch, key, key);
                keyRadius = key >= 4 ? key / 4f : 0;

                int spaceW = Round(10 * u);
                if ((n - spaceW) % 2 != 0) spaceW--;
                space = new RectangleF((n - spaceW) / 2, y1 + 2 * pitch, spaceW, spaceH);

                badged = badge;
                if (!badge) { keys = all; return; }

                // A disc half the size of the box in the corner, its edges on whole pixels, with a ring of about
                // 1/20 of the box (at least 1 px) cut out of the keyboard around it.
                int d = n / 4 * 2;
                badgeRadius = d / 2f;
                cx = cy = n - badgeRadius;
                float ring = Math.Max(1, n / 20f);
                cutRadius = badgeRadius + ring;

                // Keys are never cut in pieces: a key may reach into the ring as long as it keeps half the ring
                // away from the badge, otherwise it goes. The space bar goes too if only a stub of it would be left.
                var kept = new List<RectangleF>();
                foreach (var k in all)
                    if (Clearance(k) - badgeRadius >= ring / 2) kept.Add(k);
                keys = kept.ToArray();
                float dy = space.Top + space.Height / 2 - cy;
                float reach = Math.Abs(dy) < cutRadius ? cx - (float)Math.Sqrt(cutRadius * cutRadius - dy * dy) : space.Right;
                dropSpace = Math.Min(space.Right, reach) - space.Left < 2 * space.Height;

                // The marks, in white: a bar about half as wide as the badge and a fifth as tall (on whole
                // pixels), or a check with rounded ends.
                int w = Round(d * 0.56f), h = Math.Max(1, Round(d * 0.2f));
                if ((d - w) % 2 != 0) w--;
                if ((d - h) % 2 != 0) h = h > 1 ? h - 1 : h + 1;
                bar = new RectangleF(cx - w / 2f, cy - h / 2f, w, h);
                float x0 = cx - badgeRadius, y0 = cy - badgeRadius;
                tick0 = new PointF(x0 + d * 0.27f, y0 + d * 0.52f);
                tick1 = new PointF(x0 + d * 0.43f, y0 + d * 0.68f);
                tick2 = new PointF(x0 + d * 0.74f, y0 + d * 0.35f);
                tickHalf = Math.Max(0.75f, d * 0.075f);
            }

            static int Round(float v) { return (int)Math.Floor(v + 0.5f); }

            // Distance from the badge's centre to the nearest point of key k.
            float Clearance(RectangleF k)
            {
                float dx = Math.Max(0, Math.Max(k.Left - cx, cx - k.Right)), dy = Math.Max(0, Math.Max(k.Top - cy, cy - k.Bottom));
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            // The outline and the space bar, cut by the ring, then the keys whole.
            public float Keyboard(float x, float y)
            {
                float d = Math.Max(RoundRect(x, y, left, top, right, bottom, radius),
                                   -RoundRect(x, y, left + stroke, top + stroke, right - stroke, bottom - stroke, Math.Max(0, radius - stroke)));
                if (!dropSpace) d = Math.Min(d, RoundRect(x, y, space.Left, space.Top, space.Right, space.Bottom, space.Height / 2));
                if (badged) d = Math.Max(d, cutRadius - Distance(x, y, cx, cy));
                foreach (var k in keys) d = Math.Min(d, RoundRect(x, y, k.Left, k.Top, k.Right, k.Bottom, keyRadius));
                return d;
            }

            public float Disc(float x, float y) { return Distance(x, y, cx, cy) - badgeRadius; }

            public float Bar(float x, float y) { return RoundRect(x, y, bar.Left, bar.Top, bar.Right, bar.Bottom, bar.Height / 2); }

            public float Check(float x, float y) { return Math.Min(Segment(x, y, tick0, tick1), Segment(x, y, tick1, tick2)) - tickHalf; }

            static float Distance(float x, float y, float px, float py)
            {
                float dx = x - px, dy = y - py;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }

            // Distance to the segment a-b.
            static float Segment(float x, float y, PointF a, PointF b)
            {
                float vx = b.X - a.X, vy = b.Y - a.Y;
                float t = Math.Max(0, Math.Min(1, ((x - a.X) * vx + (y - a.Y) * vy) / (vx * vx + vy * vy)));
                return Distance(x, y, a.X + vx * t, a.Y + vy * t);
            }

            static float RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
            {
                float hx = (x1 - x0) / 2, hy = (y1 - y0) / 2;
                r = Math.Min(r, Math.Min(hx, hy));
                float qx = Math.Abs(x - (x0 + x1) / 2) - hx + r, qy = Math.Abs(y - (y0 + y1) / 2) - hy + r;
                float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
            }
        }
    }
}
