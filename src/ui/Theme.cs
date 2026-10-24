using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyboardToggler.Ui
{
    // Windows 11-style colours, fonts and window frames, shared by the app and the installer.
    // Follows the system light/dark setting, accent colour and high-contrast mode. On Windows 10 it
    // falls back to Segoe UI and square corners.
    static class Theme
    {
        public static bool Dark { get; private set; }          // apps use the dark theme
        public static bool DarkTaskbar { get; private set; }   // the taskbar (and so the tray) is dark
        public static bool HighContrast { get; private set; }

        public static Color Background { get; private set; }   // window base, dialog footers
        public static Color Layer { get; private set; }        // content areas, cards, popups
        public static Color Divider { get; private set; }      // 1 px separators and card outlines

        public static Color Text { get; private set; }
        public static Color TextSecondary { get; private set; }
        public static Color TextDisabled { get; private set; }

        public static Color ControlStrongBorder { get; private set; }   // toggle and checkbox outlines, off-state knob

        public static Color Accent { get; private set; }        // accent fill: main buttons, toggles, progress
        public static Color AccentHover { get; private set; }
        public static Color AccentPressed { get; private set; }
        public static Color AccentDisabled { get; private set; }
        public static Color OnAccent { get; private set; }      // text and glyphs on the accent fill
        public static Color OnAccentDisabled { get; private set; }
        public static Color AccentText { get; private set; }    // accent-coloured text on Layer

        public static Color Critical { get; private set; }
        public static Color Caution { get; private set; }
        public static Color Success { get; private set; }

        public static Font Caption { get; private set; }        // 12 px
        public static Font Body { get; private set; }           // 14 px
        public static Font BodyStrong { get; private set; }     // 14 px semibold
        public static Font Subtitle { get; private set; }       // 20 px semibold
        public static Font Title { get; private set; }          // 28 px semibold
        public static string IconFontFamily { get; private set; }

        // System DPI / 96. Fonts are in points and scale by themselves; sizes in pixels go through S().
        public static float Scale { get; private set; }

        // Raised after the light/dark mode, accent colour or high-contrast mode changes. May be raised on any thread.
        public static event Action Changed;

        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        static bool? forcedDark;
        static string signature;
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        static Theme()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            bool variable = HasFont("Segoe UI Variable Text");
            string text = variable ? "Segoe UI Variable Text" : "Segoe UI";
            string strong = variable ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";
            string display = variable ? "Segoe UI Variable Display Semib" : "Segoe UI Semibold";   // GDI cuts family names at 31 characters
            Caption = new Font(text, 9f);
            Body = new Font(text, 10.5f);
            BodyStrong = new Font(strong, 10.5f);
            Subtitle = new Font(display, 15f);
            Title = new Font(display, 21f);
            IconFontFamily = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            Load();
            SystemEvents.UserPreferenceChanged += (s, e) => Reload();
        }

        public static int S(float px) { return (int)Math.Round(px * Scale); }
        public static float SF(float px) { return px * Scale; }

        // For screenshots and tests: force light or dark (null = follow Windows again).
        public static void Force(bool? dark) { forcedDark = dark; signature = null; Reload(); }

        static void Reload()
        {
            string before = signature;
            Load();
            if (before == signature) return;
            var h = Changed;
            if (h != null) h();
        }

        static void Load()
        {
            HighContrast = SystemInformation.HighContrast;
            Dark = forcedDark.HasValue ? forcedDark.Value : ReadDword("AppsUseLightTheme", 1) == 0;
            DarkTaskbar = ReadDword("SystemUsesLightTheme", 0) == 0;
            Color[] p = ReadAccentPalette();   // [0] lightest ... [6] darkest

            if (HighContrast)
            {
                Background = Layer = SystemColors.Window;
                Divider = Text = TextSecondary = ControlStrongBorder = SystemColors.WindowText;
                TextDisabled = SystemColors.GrayText;
                Accent = AccentHover = AccentPressed = SystemColors.Highlight;
                OnAccent = SystemColors.HighlightText;
                AccentDisabled = SystemColors.Control;
                OnAccentDisabled = SystemColors.GrayText;
                AccentText = SystemColors.HotTrack;
                Critical = Caution = Success = SystemColors.WindowText;
            }
            else if (Dark)
            {
                Background = Rgb(0x202020); Layer = Rgb(0x2B2B2B); Divider = Rgb(0x3A3A3A);
                Text = Rgb(0xFFFFFF); TextSecondary = Rgb(0xCFCFCF); TextDisabled = Rgb(0x787878);
                ControlStrongBorder = Rgb(0x9A9A9A);
                Accent = p[1]; OnAccent = Rgb(0x000000); AccentText = p[0];
                AccentDisabled = Rgb(0x434343); OnAccentDisabled = Rgb(0xA7A7A7);
                Critical = Rgb(0xFF99A4); Caution = Rgb(0xFCE100); Success = Rgb(0x6CCB5F);
            }
            else
            {
                Background = Rgb(0xF3F3F3); Layer = Rgb(0xFFFFFF); Divider = Rgb(0xE5E5E5);
                Text = Rgb(0x1A1A1A); TextSecondary = Rgb(0x5F5F5F); TextDisabled = Rgb(0xA0A0A0);
                ControlStrongBorder = Rgb(0x8A8A8A);
                Accent = p[4]; OnAccent = Rgb(0xFFFFFF); AccentText = p[5];
                AccentDisabled = Rgb(0xC5C5C5); OnAccentDisabled = Rgb(0xFFFFFF);
                Critical = Rgb(0xC42B1C); Caution = Rgb(0x9D5D00); Success = Rgb(0x0F7B0F);
            }
            if (!HighContrast)
            {
                AccentHover = Blend(Accent, Layer, 0.9f);
                AccentPressed = Blend(Accent, Layer, 0.8f);
            }
            signature = HighContrast + "|" + Dark + "|" + DarkTaskbar + "|" + Accent.ToArgb();
        }

        // Fill of a standard control (button, keycap...) drawn on the given background, Windows 11 style.
        public static Color ControlFill(Color back, bool hover, bool pressed)
        {
            if (HighContrast) return SystemColors.ButtonFace;
            if (Dark) return Blend(Color.White, back, pressed ? 0.0326f : hover ? 0.0837f : 0.0605f);
            return pressed ? Blend(Rgb(0xF9F9F9), back, 0.3f) : hover ? Blend(Rgb(0xF9F9F9), back, 0.5f) : Blend(Color.White, back, 0.7f);
        }

        // Outline of a standard control drawn on the given background.
        public static Color ControlBorder(Color back)
        {
            if (HighContrast) return SystemColors.ControlText;
            return Dark ? Blend(Color.White, back, 0.09f) : Blend(Color.Black, back, 0.12f);
        }

        public static Font IconFont(float points)
        {
            lock (iconFonts)
            {
                Font f;
                if (!iconFonts.TryGetValue(points, out f)) iconFonts[points] = f = new Font(IconFontFamily, points);
                return f;
            }
        }

        public static Color Blend(Color top, Color back, float alpha)
        {
            return Color.FromArgb(
                (int)Math.Round(top.R * alpha + back.R * (1 - alpha)),
                (int)Math.Round(top.G * alpha + back.G * (1 - alpha)),
                (int)Math.Round(top.B * alpha + back.B * (1 - alpha)));
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Dark or light title bar to match the theme; rounded corners and a theme-coloured outline for popups
        // (Windows 11 rounds normal windows by itself). Does nothing where Windows doesn't support it.
        public static void ApplyFrame(IntPtr hwnd, bool popup)
        {
            try
            {
                int dark = Dark && !HighContrast ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE (19 before 20H1)
                if (popup)
                {
                    int round = 2;                                                     // DWMWCP_ROUND
                    DwmSetWindowAttribute(hwnd, 33, ref round, 4);                     // DWMWA_WINDOW_CORNER_PREFERENCE
                    int border = ColorTranslator.ToWin32(Divider);
                    DwmSetWindowAttribute(hwnd, 34, ref border, 4);                    // DWMWA_BORDER_COLOR
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        static int ReadDword(string name, int fallback)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    object v = k == null ? null : k.GetValue(name);
                    return v is int ? (int)v : fallback;
                }
            }
            catch { return fallback; }
        }

        static Color[] ReadAccentPalette()
        {
            // Windows' default blue, lightest to darkest.
            var c = new[] { Rgb(0x99EBFF), Rgb(0x4CC2FF), Rgb(0x0091F8), Rgb(0x0078D4), Rgb(0x0067C0), Rgb(0x003E92), Rgb(0x001A68) };
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var b = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (b != null && b.Length >= 28)
                        for (int i = 0; i < 7; i++) c[i] = Color.FromArgb(b[i * 4], b[i * 4 + 1], b[i * 4 + 2]);
                }
            }
            catch { }
            return c;
        }

        static bool HasFont(string family)
        {
            try { using (new FontFamily(family)) return true; }
            catch (ArgumentException) { return false; }
        }

        static Color Rgb(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    // Icon font characters (the same in Segoe Fluent Icons and Segoe MDL2 Assets).
    static class Glyph
    {
        public const string Keyboard = "\uE765", Info = "\uE946", Warning = "\uE7BA", Error = "\uEA39", Checkmark = "\uE73E",
            ChevronRight = "\uE76C", ChevronDown = "\uE70D", Copy = "\uE8C8", Cancel = "\uE711", Shield = "\uEA18",
            Refresh = "\uE72C", Settings = "\uE713", Power = "\uE7E8", Lock = "\uE72E", Accept = "\uE8FB";
    }
}
