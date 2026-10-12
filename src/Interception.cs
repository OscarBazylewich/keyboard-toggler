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
        public const ushort KeyUp = 0x01, KeyE0 = 0x02;

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
        bool ctrl, alt, shift;

        public event Action EmergencyCombo;   // Ctrl+Alt+Shift+K pressed on the built-in keyboard (raised on a worker thread)
        public bool Blocking { get { return running; } }

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
            ctrl = alt = shift = false;
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
                if (!running || !builtIn.Contains(dev)) { Ic.interception_send(ctx, dev, stroke, 1); continue; }

                // Swallow the key, but still watch for the emergency combo.
                ushort code = BitConverter.ToUInt16(stroke, 0), state = BitConverter.ToUInt16(stroke, 2);
                bool down = (state & Ic.KeyUp) == 0;
                switch (code)
                {
                    case 0x1D: ctrl = down; break;
                    case 0x38: alt = down; break;
                    case 0x2A: case 0x36: shift = down; break;
                    case 0x25:
                        if (down && ctrl && alt && shift)
                        {
                            var h = EmergencyCombo;
                            if (h != null) ThreadPool.QueueUserWorkItem(_ => h());
                        }
                        break;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            if (ctx != IntPtr.Zero) { Ic.interception_destroy_context(ctx); ctx = IntPtr.Zero; }
        }
    }
}
