using System;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyboardToggler
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, wParam; }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, out RAWINPUTHEADER data, ref uint size, uint hsize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, StringBuilder data, ref uint size);
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
    }
}
