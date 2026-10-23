using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace KeyboardToggler
{
    // P/Invoke for interception.dll (https://github.com/oblitum/Interception, LGPL).
    static class Ic
    {
        public const int MaxKeyboard = 10;
        public const ushort FilterNone = 0x0000, FilterAll = 0xFFFF;
        public const ushort KeyUp = 0x01, KeyE0 = 0x02, KeyE1 = 0x04;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Predicate(int device);

        const string Dll = "interception.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr interception_create_context();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void interception_destroy_context(IntPtr ctx);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void interception_set_filter(IntPtr ctx, Predicate p, ushort filter);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int interception_wait_with_timeout(IntPtr ctx, uint ms);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int interception_receive(IntPtr ctx, int dev, byte[] stroke, uint n);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int interception_send(IntPtr ctx, int dev, byte[] stroke, uint n);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern uint interception_get_hardware_id(IntPtr ctx, int dev, byte[] buf, uint size);
    }

    // Blocks keys from the built-in keyboard only. The driver is told to route just that
    // device through this process; every other keyboard bypasses us completely, so a hang
    // here can never freeze the external keyboard. If the process dies, the driver falls
    // back to normal pass-through on its own.
    sealed class Blocker : IDisposable
    {
        IntPtr ctx;
        Thread thread;
        volatile bool running;
        readonly HashSet<int> builtIn = new HashSet<int>();
        readonly Ic.Predicate isBuiltIn, anyKeyboard;
        KeyGate keys;
        volatile Hotkey hotkey = Hotkey.Default;

        public event Action HotkeyPressed;   // the toggle shortcut typed on a blocked keyboard (raised on a worker thread)
        public bool Blocking { get { return running; } }
        public Hotkey Hotkey { set { hotkey = value; } }

        public Blocker()
        {
            ctx = Ic.interception_create_context();
            if (ctx == IntPtr.Zero)
                throw new InvalidOperationException("The Interception driver isn't available. Install it and restart Windows.");
            isBuiltIn = d => builtIn.Contains(d) ? 1 : 0;
            anyKeyboard = d => d >= 1 && d <= Ic.MaxKeyboard ? 1 : 0;
        }

        // Fallback for keyboards Windows can't describe: the classic PS/2 laptop keyboard IDs.
        public static bool IsPs2KeyboardId(string hwid)
        {
            string s = hwid.ToUpperInvariant();
            return s.StartsWith("ACPI\\") || s.Contains("PNP0303") || s.Contains("MSFT0001");
        }

        // Interception device number -> first hardware ID (the same ID Windows lists for the device), for every keyboard slot in use.
        public Dictionary<int, string> Keyboards()
        {
            var result = new Dictionary<int, string>();
            var buf = new byte[2048];
            for (int d = 1; d <= Ic.MaxKeyboard; d++)
            {
                uint n = Ic.interception_get_hardware_id(ctx, d, buf, (uint)buf.Length);
                if (n == 0 || n > buf.Length) continue;
                result[d] = Encoding.Unicode.GetString(buf, 0, (int)n).Split('\0')[0];
            }
            return result;
        }

        // Starts dropping keys from every keyboard whose hardware ID passes isTarget. Returns how many were found.
        public int Start(Func<string, bool> isTarget)
        {
            if (running) return builtIn.Count;
            builtIn.Clear();
            foreach (var kv in Keyboards())
                if (isTarget(kv.Value)) builtIn.Add(kv.Key);
            if (builtIn.Count == 0) return 0;
            // Note which keys are held right now (e.g. the shortcut that is switching this keyboard off): Windows has
            // already seen them go down, so it has to see them come back up.
            var held = KeyGate.KeysDownNow();
            if (held.Count > 0) Log.Write("keys still held while switching off: " + KeyGate.Names(held.Values));
            keys = new KeyGate(held, vk => (Native.GetAsyncKeyState(vk) & 0x8000) != 0, Log.Write);
            running = true;
            Ic.interception_set_filter(ctx, isBuiltIn, Ic.FilterAll);
            thread = new Thread(Loop) { IsBackground = true, Name = "Blocker", Priority = ThreadPriority.AboveNormal };
            thread.Start();
            return builtIn.Count;
        }

        public void Stop()
        {
            if (!running) return;
            Ic.interception_set_filter(ctx, anyKeyboard, Ic.FilterNone);
            running = false;
            if (thread != null) thread.Join(1000);
            thread = null;
        }

        void Loop()
        {
            var stroke = new byte[20];
            while (running)
            {
                int dev = Ic.interception_wait_with_timeout(ctx, 250);
                if (dev <= 0 || Ic.interception_receive(ctx, dev, stroke, 1) <= 0) continue;
                bool shortcut = false;
                if (!running || !builtIn.Contains(dev)
                    || keys.Handle(BitConverter.ToUInt16(stroke, 0), BitConverter.ToUInt16(stroke, 2), hotkey, out shortcut))
                    Ic.interception_send(ctx, dev, stroke, 1);
                if (shortcut)
                {
                    var h = HotkeyPressed;
                    if (h != null) ThreadPool.QueueUserWorkItem(_ => h());
                }
            }
        }

        public void Dispose()
        {
            Stop();
            if (ctx != IntPtr.Zero) { Ic.interception_destroy_context(ctx); ctx = IntPtr.Zero; }
        }
    }

    // Decides what happens to each stroke from a blocked keyboard. Strokes are swallowed, except the key-ups of keys
    // that were already held when blocking started: Windows saw those go down, so it must see them come up, or it
    // keeps them "held" (e.g. the Ctrl+Alt of the shortcut that just switched this keyboard off). It also watches
    // the strokes for the toggle shortcut, so the blocked keyboard can always switch itself back on.
    sealed class KeyGate
    {
        // Keys are identified by scan code, plus 0x100 for E0-prefixed keys.
        readonly Dictionary<int, int> heldAtStart;   // key -> virtual key, for keys Windows had down when blocking started
        readonly Func<int, bool> downInWindows;      // virtual key -> is Windows currently treating it as held
        readonly Action<string> log;
        bool ctrl, alt, shift, win;
        int shortcutKey = -1;                         // the shortcut's key while it's held down

        public KeyGate(Dictionary<int, int> heldAtStart, Func<int, bool> downInWindows, Action<string> log)
        {
            this.heldAtStart = heldAtStart;
            this.downInWindows = downInWindows;
            this.log = log;
        }

        // Keys Windows has down right now.
        public static Dictionary<int, int> KeysDownNow()
        {
            var held = new Dictionary<int, int>();
            for (int vk = 0x08; vk <= 0xFE; vk++)   // starting at 0x08 skips the mouse buttons
            {
                if (vk >= 0x10 && vk <= 0x12) continue;   // Shift/Ctrl/Alt: their left/right versions (0xA0-0xA5) are checked instead
                if ((Native.GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                uint sc = Native.MapVirtualKey((uint)vk, 4 /*MAPVK_VK_TO_VSC_EX*/);
                if ((sc & 0xFF) == 0 || (sc & 0xFF00) == 0xE100) continue;   // no scan code, or Pause (which never stays down)
                int code = (int)(sc & 0xFF);
                bool modifier = (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
                if (modifier) held[code | ((sc & 0xFF00) == 0xE000 ? 0x100 : 0)] = vk;
                else { held[code] = vk; held[code | 0x100] = vk; }   // MapVirtualKey doesn't flag every E0 key (e.g. arrows), so accept both
            }
            return held;
        }

        // Returns true if the stroke should reach Windows. shortcut: the toggle shortcut was just completed.
        public bool Handle(ushort code, ushort state, Hotkey hk, out bool shortcut)
        {
            shortcut = false;
            bool down = (state & Ic.KeyUp) == 0, e0 = (state & Ic.KeyE0) != 0, e1 = (state & Ic.KeyE1) != 0;
            int id = code | (e0 ? 0x100 : 0), vk = 0;
            bool pass = false;

            bool heldBefore = !e1 && heldAtStart.TryGetValue(id, out vk);
            if (heldBefore)
            {
                if (!down) { pass = true; Forget(vk); log("passed on release of " + Names(new[] { vk })); }   // let Windows see the release
                else if (!downInWindows(vk)) { Forget(vk); heldBefore = false; }     // released elsewhere since: a new press
            }

            if (code == 0x1D) ctrl = down;
            else if (code == 0x38) alt = down;
            else if (code == 0x2A || code == 0x36) shift = down;
            else if (e0 && (code == 0x5B || code == 0x5C)) win = down;
            else if (down)
            {
                // A key held since before blocking (and its auto-repeat) isn't a new press, so it can't trigger the shortcut.
                if (!heldBefore && code == hk.ScanCode && e0 == hk.Extended
                    && ctrl == ((hk.Mods & Hotkey.Ctrl) != 0) && alt == ((hk.Mods & Hotkey.Alt) != 0)
                    && shift == ((hk.Mods & Hotkey.Shift) != 0) && win == ((hk.Mods & Hotkey.Win) != 0))
                    shortcutKey = id;
            }
            else if (id == shortcutKey)
            {
                // Act on release, so the key isn't still held (and auto-repeating into the focused app) once the keyboard is back on.
                shortcutKey = -1;
                shortcut = true;
            }
            return pass;
        }

        // For the log, e.g. "F9, LControlKey, LMenu".
        public static string Names(IEnumerable<int> vks)
        {
            var names = new List<string>();
            foreach (int vk in new HashSet<int>(vks)) names.Add(((System.Windows.Forms.Keys)vk).ToString());
            return string.Join(", ", names);
        }

        void Forget(int vk)
        {
            var ids = new List<int>();
            foreach (var kv in heldAtStart) if (kv.Value == vk) ids.Add(kv.Key);
            foreach (int id in ids) heldAtStart.Remove(id);
        }
    }
}
