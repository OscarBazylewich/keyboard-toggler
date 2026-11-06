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

    // Blocker deactivates only the laptop keyboard. The Interception driver sends the strokes of only that
    // keyboard through this process. The strokes of all other keyboards go directly to Windows.
    // Thus, a problem in this process cannot stop the external keyboard. If this process stops,
    // the driver sends all strokes directly to Windows again.
    sealed class Blocker : IDisposable
    {
        IntPtr ctx;
        Thread thread;
        volatile bool running;
        readonly HashSet<int> builtIn = new HashSet<int>();
        readonly Ic.Predicate isBuiltIn, anyKeyboard;
        KeyGate keys;
        volatile Hotkey hotkey = Hotkey.Default;

        public event Action HotkeyPressed;   // This event occurs when you type the shortcut on a deactivated keyboard. It occurs on a worker thread.
        public bool Blocking { get { return running; } }
        public Hotkey Hotkey { set { hotkey = value; } }

        public Blocker()
        {
            ctx = Ic.interception_create_context();
            if (ctx == IntPtr.Zero)
                throw new InvalidOperationException("The Interception driver is not available. Install Keyboard Toggler again. Then start Windows again.");
            isBuiltIn = d => builtIn.Contains(d) ? 1 : 0;
            anyKeyboard = d => d >= 1 && d <= Ic.MaxKeyboard ? 1 : 0;
        }

        // Use this method when Windows gives no data about a keyboard. It gives true for the usual hardware IDs of a PS/2 laptop keyboard.
        public static bool IsPs2KeyboardId(string hwid)
        {
            string s = hwid.ToUpperInvariant();
            return s.StartsWith("ACPI\\") || s.Contains("PNP0303") || s.Contains("MSFT0001");
        }

        // This method gives the first hardware ID for each Interception device number that has a keyboard. Windows shows the same ID for the device.
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

        // This method deactivates each keyboard when isTarget gives true for its hardware ID. The return value is the quantity of these keyboards.
        public int Start(Func<string, bool> isTarget)
        {
            if (running) return builtIn.Count;
            builtIn.Clear();
            foreach (var kv in Keyboards())
                if (isTarget(kv.Value)) builtIn.Add(kv.Key);
            if (builtIn.Count == 0) return 0;
            // Record the keys that are down at this time (for example, the keys of the shortcut that deactivates this keyboard).
            // Windows got the key-down of these keys. Thus, Windows must also get their key-up.
            var held = KeyGate.KeysDownNow();
            if (held.Count > 0) Log.Write("keys that you held down when Keyboard Toggler deactivated the laptop keyboard: " + KeyGate.Names(held.Values));
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

    // KeyGate selects which strokes from a deactivated keyboard go to Windows. Windows gets only the key-ups of the keys
    // that were down when Blocker deactivated the keyboard (for example, Ctrl+Alt of the shortcut that deactivated it).
    // Windows got the key-down of these keys. Thus, Windows must also get their key-up. If Windows does not get the key-up,
    // Windows keeps the key down. KeyGate also finds the shortcut in the strokes. Thus, you can always activate
    // the deactivated keyboard again with the shortcut.
    sealed class KeyGate
    {
        // The ID of a key is its scan code. For an E0 key, the ID is the scan code plus 0x100.
        readonly Dictionary<int, int> heldAtStart;   // key ID -> virtual key, for the keys that were down in Windows when Blocker deactivated the keyboard
        readonly Func<int, bool> downInWindows;      // virtual key -> gives true if the key is down in Windows at this time
        readonly Action<string> log;
        bool ctrl, alt, shift, win;
        int shortcutKey = -1;                         // the ID of the shortcut key while that key is down

        public KeyGate(Dictionary<int, int> heldAtStart, Func<int, bool> downInWindows, Action<string> log)
        {
            this.heldAtStart = heldAtStart;
            this.downInWindows = downInWindows;
            this.log = log;
        }

        // This method gives the keys that are down in Windows at this time.
        public static Dictionary<int, int> KeysDownNow()
        {
            var held = new Dictionary<int, int>();
            for (int vk = 0x08; vk <= 0xFE; vk++)   // the start value 0x08 does not include the mouse buttons
            {
                if (vk >= 0x10 && vk <= 0x12) continue;   // do not examine Shift, Ctrl and Alt here. Examine their left and right keys (0xA0-0xA5)
                if ((Native.GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                uint sc = Native.MapVirtualKey((uint)vk, 4 /*MAPVK_VK_TO_VSC_EX*/);
                if ((sc & 0xFF) == 0 || (sc & 0xFF00) == 0xE100) continue;   // the key has no scan code, or the key is Pause. Pause does not stay down.
                int code = (int)(sc & 0xFF);
                bool modifier = (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;
                if (modifier) held[code | ((sc & 0xFF00) == 0xE000 ? 0x100 : 0)] = vk;
                else { held[code] = vk; held[code | 0x100] = vk; }   // MapVirtualKey does not identify all E0 keys (for example, the arrow keys). Thus, record the two IDs.
            }
            return held;
        }

        // The return value is true if Windows must get the stroke. The shortcut parameter is true if this stroke completes the shortcut.
        public bool Handle(ushort code, ushort state, Hotkey hk, out bool shortcut)
        {
            shortcut = false;
            bool down = (state & Ic.KeyUp) == 0, e0 = (state & Ic.KeyE0) != 0, e1 = (state & Ic.KeyE1) != 0;
            int id = code | (e0 ? 0x100 : 0), vk = 0;
            bool pass = false;

            bool heldBefore = !e1 && heldAtStart.TryGetValue(id, out vk);
            if (heldBefore)
            {
                if (!down) { pass = true; Forget(vk); log("sent the key-up to Windows for " + Names(new[] { vk })); }   // send the key-up to Windows
                else if (!downInWindows(vk)) { Forget(vk); heldBefore = false; }     // the key is not down in Windows at this time. Thus, this stroke is a new key-down.
            }

            if (code == 0x1D) ctrl = down;
            else if (code == 0x38) alt = down;
            else if (code == 0x2A || code == 0x36) shift = down;
            else if (e0 && (code == 0x5B || code == 0x5C)) win = down;
            else if (down)
            {
                // If a key was down before Blocker deactivated the keyboard, its key-down is auto-repeat and not new. Thus, KeyGate does not use it for the shortcut.
                if (!heldBefore && code == hk.ScanCode && e0 == hk.Extended
                    && ctrl == ((hk.Mods & Hotkey.Ctrl) != 0) && alt == ((hk.Mods & Hotkey.Alt) != 0)
                    && shift == ((hk.Mods & Hotkey.Shift) != 0) && win == ((hk.Mods & Hotkey.Win) != 0))
                    shortcutKey = id;
            }
            else if (id == shortcutKey)
            {
                // Complete the shortcut at the key-up. If KeyGate completes it at the key-down, the key can stay down
                // in the active app (with auto-repeat) when the keyboard is on again.
                shortcutKey = -1;
                shortcut = true;
            }
            return pass;
        }

        // This method gives the key names for the log, for example "F9, LControlKey, LMenu".
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
