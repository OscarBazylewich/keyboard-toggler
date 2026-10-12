// Build-time tool: writes app.ico (PNG-compressed entries, 16-256 px) with the green keyboard used in the tray.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static byte[] Render(int size)
    {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 32f;
                g.ScaleTransform(s, s);
                using (var path = new GraphicsPath())
                {
                    path.AddArc(1, 7, 6, 6, 180, 90); path.AddArc(25, 7, 6, 6, 270, 90);
                    path.AddArc(25, 20, 6, 6, 0, 90); path.AddArc(1, 20, 6, 6, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(46, 160, 67))) g.FillPath(b, path);
                }
                using (var w = new SolidBrush(Color.White))
                {
                    for (int r = 0; r < 2; r++) for (int c = 0; c < 6; c++) g.FillRectangle(w, 4.5f + c * 4, 10 + r * 4.5f, 2.8f, 2.8f);
                    g.FillRectangle(w, 8, 20, 16, 2.8f);
                }
            }
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }

    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes) images.Add(Render(s));
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
}
