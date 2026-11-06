using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // This class makes the tray icon. The keyboard glyph has the same text color as the Windows 11 tray icons:
    // white in Dark mode and almost black in Light mode. The status badge is a green check mark when the laptop
    // keyboard is on, and a red bar when it is off. The code makes the icon for the accurate pixel dimension
    // of the tray at the DPI of the display.
    static class TrayIcon
    {
        // This method gives a new icon. The caller must use Dispose() on it.
        // Make a new icon when the taskbar changes to Dark mode or to Light mode.
        public static Icon Create(bool off, bool darkTaskbar)
        {
            Size tray = SystemInformation.SmallIconSize;   // 16 px at 100% scale, 20 px at 125%, 24 px at 150%, 32 px at 200%
            Color color = darkTaskbar ? Color.White : Color.FromArgb(0xE4, 0, 0, 0);   // The primary text color of Windows in each mode
            using (var bmp = KeyboardGlyph.Render(Math.Min(tray.Width, tray.Height), color, off ? GlyphBadge.Off : GlyphBadge.On))
            {
                IntPtr h = ToHicon(bmp);
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        // Bitmap.GetHicon() keeps premultiplied colors. Windows premultiplies these colors again when it shows the icon.
        // Thus, the anti-aliased edges of a white glyph become gray. This method makes the icon from a straight-alpha DIB.
        static IntPtr ToHicon(Bitmap bmp)
        {
            int n = bmp.Width;
            var argb = new int[n * n];
            var data = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < n; y++) Marshal.Copy(data.Scan0 + y * data.Stride, argb, y * n, n);
            bmp.UnlockBits(data);

            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = n, biHeight = -n, biPlanes = 1, biBitCount = 32 };
            IntPtr bits, color = CreateDIBSection(IntPtr.Zero, ref header, 0, out bits, IntPtr.Zero, 0);
            int stride = (n + 15) / 16 * 2;                       // Each row of the 1 bpp mask is aligned to 16 bits
            var maskBits = new byte[stride * n];                  // 1 = you can see through the pixel (for code that does not use alpha)
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
