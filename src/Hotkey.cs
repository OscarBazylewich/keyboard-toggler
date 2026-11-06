using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyboardToggler
{
    // Hotkey is the shortcut that deactivates or activates the laptop keyboard.
    // It has the modifier values that RegisterHotKey uses and one virtual key.
    sealed class Hotkey
    {
        public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;
        public static readonly Hotkey Default = new Hotkey(Ctrl | Alt | Shift, (uint)Keys.K);
        static readonly string File_ = Path.Combine(Log.Dir, "shortcut.txt");

        public readonly uint Mods, Vk;
        public Hotkey(uint mods, uint vk) { Mods = mods; Vk = vk; }

        // The shortcut must have Ctrl, Alt or Win to prevent accidental operation when you type.
        // Shift is not sufficient. The shortcut must also have one key that is not a modifier key.
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

        // This method gives each key as one item, for example {"Ctrl", "Alt", "F9"}.
        // The names and the sequence are the same as in ToString().
        public string[] KeyLabels()
        {
            var keys = new List<string>(ModsText(Mods).Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries));
            keys.Add(KeyName(Vk));
            return keys.ToArray();
        }

        // ScanCode is the scan code that the keyboard sends for the key. The Interception driver receives this code.
        // If the key is an E0 "extended" key, the value of Extended is true.
        public ushort ScanCode { get { return (ushort)(MapVirtualKey(Vk, 4 /*MAPVK_VK_TO_VSC_EX*/) & 0xFF); } }
        public bool Extended { get { return IsExtended(Vk); } }

        // MapVirtualKey does not identify all E0 keys. For example, it gives the arrow keys as numpad keys.
        // Thus, ExtendedKeys contains the standard E0 keys.
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
