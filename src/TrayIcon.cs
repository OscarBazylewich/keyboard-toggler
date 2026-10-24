using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // The notification-area icon in the Windows 11 style: the keyboard glyph in the taskbar's text colour, like
    // the system's own tray icons (white on a dark taskbar, near-black on a light one), with a status badge:
    // a green check while the laptop keyboard is on, a red bar while it's switched off. Drawn for the tray's
    // exact pixel size at the current DPI.
    static class TrayIcon
    {
        // A new icon; the caller disposes it. Make new ones when the taskbar switches between light and dark.
        public static Icon Create(bool off, bool darkTaskbar)
        {
            Size tray = SystemInformation.SmallIconSize;   // 16 px at 100% scaling, 20 at 125%, 24 at 150%, 32 at 200%
            Color color = darkTaskbar ? Color.White : Color.FromArgb(0xE4, 0, 0, 0);   // Windows' primary text colour on each
            using (var bmp = KeyboardGlyph.Render(Math.Min(tray.Width, tray.Height), color, off ? GlyphBadge.Off : GlyphBadge.On))
            {
                IntPtr h = ToHicon(bmp);
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        // Bitmap.GetHicon() stores premultiplied colours, which Windows premultiplies again when it draws the
        // icon: anti-aliased edges of a white glyph come out dark. So build the icon from a straight-alpha DIB.
        static IntPtr ToHicon(Bitmap bmp)
        {
            int n = bmp.Width;
            var argb = new int[n * n];
            var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < n; y++) Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * n, n);
            bmp.UnlockBits(data);

            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = n, biHeight = -n, biPlanes = 1, biBitCount = 32 };
            IntPtr bits, color = CreateDIBSection(IntPtr.Zero, ref header, 0, out bits, IntPtr.Zero, 0);
            int stride = (n + 15) / 16 * 2;                       // the 1 bpp mask has 16-bit aligned rows
            var maskBits = new byte[stride * n];                  // 1 = transparent, for anything that ignores alpha
            for (int i = 0; i < argb.Length; i++)
                if ((argb[i] >> 24) == 0) maskBits[i / n * stride + i % n / 8] |= (byte)(0x80 >> (i % n % 8));
            IntPtr mask = CreateBitmap(n, n, 1, 1, maskBits);
            try
            {
                if (color == IntPtr.Zero || mask == IntPtr.Zero) throw new Win32Exception();
                Marshal.Copy(argb, 0, bits, argb.Length);
                var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
                IntPtr h = CreateIconIndirect(ref info);
                if (h == IntPtr.Zero) throw new Win32Exception();
                return h;
            }
            finally { DeleteObject(color); DeleteObject(mask); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

        [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }
}
