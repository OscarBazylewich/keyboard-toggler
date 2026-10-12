using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyboardToggler
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, wParam; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICELIST { public IntPtr Device; public uint Type; }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, out RAWINPUTHEADER data, ref uint size, uint hsize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, StringBuilder data, ref uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] list, ref uint n, uint size);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        // Device handle of a WM_INPUT message (zero for injected input).
        public static IntPtr RawInputDevice(IntPtr hRawInput)
        {
            RAWINPUTHEADER h;
            uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            return GetRawInputData(hRawInput, 0x10000005 /*RID_HEADER*/, out h, ref size, size) == uint.MaxValue ? IntPtr.Zero : h.Device;
        }

        public static string DeviceName(IntPtr dev)
        {
            uint size = 0;
            GetRawInputDeviceInfo(dev, 0x20000007 /*RIDI_DEVICENAME*/, null, ref size);
            var sb = new StringBuilder((int)size + 1);
            GetRawInputDeviceInfo(dev, 0x20000007, sb, ref size);
            return sb.ToString();
        }

        public static List<string> KeyboardDeviceNames()
        {
            var names = new List<string>();
            uint n = 0, sz = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            GetRawInputDeviceList(null, ref n, sz);
            var list = new RAWINPUTDEVICELIST[n + 8];
            n = (uint)list.Length;
            uint got = GetRawInputDeviceList(list, ref n, sz);
            if (got == uint.MaxValue) return names;
            for (int i = 0; i < got; i++)
                if (list[i].Type == 1 /*RIM_TYPEKEYBOARD*/) names.Add(DeviceName(list[i].Device));
            return names;
        }
    }
}
