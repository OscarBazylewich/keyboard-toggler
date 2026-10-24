// Build-time tool: writes the app icon (.ico with PNG-compressed entries, 16-256 px) in the Windows 11 style:
// the white keyboard glyph (src\ui\KeyboardGlyph.cs, the same one as the tray icon) on a green rounded tile.
// Every size is drawn for its own pixel grid; the small ones stay flat, the larger ones get a little depth.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using KeyboardToggler.Ui;

static class MakeIcon
{
    static readonly Color Top = Color.FromArgb(0x34, 0xB3, 0x56), Bottom = Color.FromArgb(0x1F, 0x8A, 0x3B);   // brand green #2EA043 in between

    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = Render(s))
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
        using (var w = new BinaryWriter(File.Create(args[0])))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    // One icon image: tile geometry snapped to whole pixels, then the glyph at a size its layout is drawn for.
    static Bitmap Render(int n)
    {
        bool small = n <= 24, depth = !small;
        int pad = n <= 16 ? 0 : small ? 1 : Round(n / 16f);                   // 16 px: full bleed ... 256 px: 16
        float tile = n - 2 * pad, radius = Round(tile * 0.17f * 2) / 2f;
        int glyph = Round(tile * (small ? 0.75f : 0.68f));                     // a bigger glyph when tiny
        if ((n - glyph) % 2 != 0) glyph++;                                     // centred on whole pixels

        var px = new float[n * n * 4];   // premultiplied B, G, R, A in 0..1
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cov = Coverage(x, y, pad, tile, radius);
                float b = 0, g = 0, r = 0, a = 0;
                if (depth)
                {
                    // Soft shadow under the tile: a blurred, slightly lowered copy of its shape.
                    float blur = n / 32f, drop = n / 64f;
                    float sd = RoundRect(x + 0.5f, y + 0.5f - drop, pad, pad, n - pad, n - pad, radius);
                    a = 0.28f * Smooth(blur, -blur, sd);   // black, so the colour channels stay 0
                }
                if (cov > 0)
                {
                    // Gradient from the top-left light to the bottom-right dark, mostly vertical.
                    float t = ((y + 0.5f - pad) * 0.8f + (x + 0.5f - pad) * 0.2f) / tile;
                    Color c = Lerp(Top, Bottom, Math.Max(0, Math.Min(1, t)));
                    float cr = c.R / 255f, cg = c.G / 255f, cb = c.B / 255f;
                    if (depth)
                    {
                        // A faint light rim along the top edge, fading out down the sides.
                        float inside = -RoundRect(x + 0.5f, y + 0.5f, pad, pad, n - pad, n - pad, radius);
                        float rim = Math.Max(1, n / 48f);
                        float up = Math.Max(0, 1 - (y + 0.5f - pad) / (tile * 0.5f));
                        float k = 0.35f * up * Smooth(rim, rim * 0.5f, inside);
                        cr += (1 - cr) * k; cg += (1 - cg) * k; cb += (1 - cb) * k;
                    }
                    r = cr * cov + r * (1 - cov); g = cg * cov + g * (1 - cov); b = cb * cov + b * (1 - cov);
                    a = cov + a * (1 - cov);
                }
                int i = (y * n + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
            }

        int at = (n - glyph) / 2;
        if (depth)
            using (var shade = KeyboardGlyph.Render(glyph, Color.FromArgb(70, 0x0B, 0x4A, 0x1C)))
                Over(px, n, shade, at, at + Math.Max(1, Round(n / 64f)));     // the glyph's shadow, a touch lower
        using (var white = KeyboardGlyph.Render(glyph, Color.White))
            Over(px, n, white, at, at);

        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[n * n * 4];
        for (int i = 0; i < n * n; i++)
        {
            float a = px[i * 4 + 3];
            for (int ch = 0; ch < 3; ch++) bytes[i * 4 + ch] = a > 0 ? Byte(px[i * 4 + ch] / a) : (byte)0;   // un-premultiply
            bytes[i * 4 + 3] = Byte(a);
        }
        for (int y = 0; y < n; y++) Marshal.Copy(bytes, y * n * 4, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), n * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    // Fraction of pixel (x, y) inside the tile (4 x 4 samples).
    static float Coverage(int x, int y, int pad, float tile, float radius)
    {
        int hits = 0;
        for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
                if (RoundRect(x + (sx + 0.5f) / 4, y + (sy + 0.5f) / 4, pad, pad, pad + tile, pad + tile, radius) < 0) hits++;
        return hits / 16f;
    }

    // Draws a (non-premultiplied) bitmap over the premultiplied buffer at (ox, oy).
    static void Over(float[] px, int n, Bitmap src, int ox, int oy)
    {
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int tx = ox + x, ty = oy + y;
                if (tx < 0 || ty < 0 || tx >= n || ty >= n) continue;
                Color c = src.GetPixel(x, y);
                float a = c.A / 255f;
                if (a == 0) continue;
                int i = (ty * n + tx) * 4;
                px[i] = c.B / 255f * a + px[i] * (1 - a);
                px[i + 1] = c.G / 255f * a + px[i + 1] * (1 - a);
                px[i + 2] = c.R / 255f * a + px[i + 2] * (1 - a);
                px[i + 3] = a + px[i + 3] * (1 - a);
            }
    }

    static float RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        float hx = (x1 - x0) / 2, hy = (y1 - y0) / 2;
        float qx = Math.Abs(x - (x0 + x1) / 2) - hx + r, qy = Math.Abs(y - (y0 + y1) / 2) - hy + r;
        float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    // 0 at v = from, 1 at v = to, smooth in between.
    static float Smooth(float from, float to, float v)
    {
        float t = Math.Max(0, Math.Min(1, (v - from) / (to - from)));
        return t * t * (3 - 2 * t);
    }

    static Color Lerp(Color a, Color b, float t)
    {
        return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    static byte Byte(float v) { return (byte)Round(Math.Max(0, Math.Min(1, v)) * 255); }

    static int Round(float v) { return (int)Math.Floor(v + 0.5f); }
}
