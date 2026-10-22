using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyboardToggler
{
    // The toggle shortcut: modifier flags as RegisterHotKey takes them, plus one virtual key.
    sealed class Hotkey
    {
        public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;
        public static readonly Hotkey Default = new Hotkey(Ctrl | Alt | Shift, (uint)Keys.K);
        static readonly string File_ = Path.Combine(Log.Dir, "shortcut.txt");

        public readonly uint Mods, Vk;
        public Hotkey(uint mods, uint vk) { Mods = mods; Vk = vk; }

        // Needs Ctrl, Alt or Win (Shift alone would fire while typing) and a key that isn't itself a modifier.
        public bool IsValid
        {
            get { return (Mods & (Ctrl | Alt | Win)) != 0 && Vk != 0 && !IsModifierKey((Keys)Vk); }
        }

        public static bool IsModifierKey(Keys k)
        {
            switch (k)
            {
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.LWin: case Keys.RWin:
                    return true;
            }
            return false;
        }

        public override bool Equals(object o) { var h = o as Hotkey; return h != null && h.Mods == Mods && h.Vk == Vk; }
        public override int GetHashCode() { return (int)(Mods << 16 ^ Vk); }

        public override string ToString()
        {
            return Mods == 0 ? KeyName(Vk) : ModsText(Mods) + " + " + KeyName(Vk);
        }

        public static string ModsText(uint mods)
        {
            var parts = new List<string>();
            if ((mods & Win) != 0) parts.Add("Win");
            if ((mods & Ctrl) != 0) parts.Add("Ctrl");
            if ((mods & Alt) != 0) parts.Add("Alt");
            if ((mods & Shift) != 0) parts.Add("Shift");
            return string.Join(" + ", parts);
        }

        // Scan code the keyboard sends for the key (what the driver sees), and whether it's an E0 "extended" key.
        public ushort ScanCode { get { return (ushort)(MapVirtualKey(Vk, 4 /*MAPVK_VK_TO_VSC_EX*/) & 0xFF); } }
        public bool Extended { get { return IsExtended(Vk); } }

        // MapVirtualKey doesn't flag every E0 key (e.g. the arrows come back as the numpad keys), so list the standard ones.
        static readonly HashSet<Keys> ExtendedKeys = new HashSet<Keys> {
            Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Insert, Keys.Delete, Keys.Home, Keys.End,
            Keys.PageUp, Keys.PageDown, Keys.Divide, Keys.NumLock, Keys.PrintScreen, Keys.Apps,
            Keys.RControlKey, Keys.RMenu, Keys.LWin, Keys.RWin };

        static bool IsExtended(uint vk) { return ExtendedKeys.Contains((Keys)vk) || (MapVirtualKey(vk, 4) & 0xFF00) == 0xE000; }

        static string KeyName(uint vk)
        {
            uint sc = MapVirtualKey(vk, 4);
            int lParam = (int)((sc & 0xFF) << 16) | (IsExtended(vk) ? 1 << 24 : 0);
            var sb = new StringBuilder(64);
            if (sc != 0 && GetKeyNameText(lParam, sb, sb.Capacity) > 0)
            {
                string s = sb.ToString();
                return s.Length == 1 ? s.ToUpperInvariant() : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
            }
            return ((Keys)vk).ToString();
        }

        public static Hotkey Load()
        {
            try
            {
                var p = File.ReadAllText(File_).Trim().Split(',');
                var h = new Hotkey(uint.Parse(p[0]), uint.Parse(p[1]));
                if (h.IsValid) return h;
            }
            catch { }
            return Default;
        }

        public void Save()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(File_, Mods + "," + Vk);
        }

        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetKeyNameText(int lParam, StringBuilder name, int size);
    }
}
