using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyboardToggler
{
    sealed class KeyboardDevice
    {
        public string InstanceId;   // e.g. ACPI\MSFT0001\0 or HID\VID_046D&PID_C54D&MI_01&COL01\9&...
        public string HardwareId;   // first hardware ID, which is also what the Interception driver reports
        public string Name;         // most descriptive name found on the device or its parents
        public bool Internal;       // part of the computer itself (built-in), as opposed to plugged in
    }

    // Keyboard-class devices from the PnP tree (cfgmgr32).
    static class Devices
    {
        // Windows puts every built-in device in the computer's own "container"; plugged-in devices
        // (USB, Bluetooth, docks) get a container of their own. This works for PS/2, I2C and internal USB keyboards.
        static readonly Guid ComputerContainer = new Guid("00000000-0000-0000-ffff-ffffffffffff");
        const string KeyboardClass = "{4d36e96b-e325-11ce-bfc1-08002be10318}";

        [StructLayout(LayoutKind.Sequential)]
        struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(string g, uint p) { fmtid = new Guid(g); pid = p; } }

        static readonly DEVPROPKEY HardwareIds = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 3);
        static readonly DEVPROPKEY DeviceDesc = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
        static readonly DEVPROPKEY ContainerId = new DEVPROPKEY("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
        static readonly DEVPROPKEY BusReportedDesc = new DEVPROPKEY("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_ID_List_SizeW(out uint len, string filter, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_ID_ListW(string filter, char[] buf, uint len, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern uint CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[] buf, ref uint size, uint flags);

        const uint CM_GETIDLIST_FILTER_CLASS = 0x200, CM_GETIDLIST_FILTER_PRESENT = 0x100;

        public static List<KeyboardDevice> PresentKeyboards()
        {
            var result = new List<KeyboardDevice>();
            uint flags = CM_GETIDLIST_FILTER_CLASS | CM_GETIDLIST_FILTER_PRESENT, len;
            if (CM_Get_Device_ID_List_SizeW(out len, KeyboardClass, flags) != 0 || len == 0) return result;
            var buf = new char[len];
            if (CM_Get_Device_ID_ListW(KeyboardClass, buf, len, flags) != 0) return result;
            foreach (string id in new string(buf).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = Describe(id);
                if (d != null) result.Add(d);
            }
            return result;
        }

        public static KeyboardDevice Describe(string instanceId)
        {
            uint node;
            if (CM_Locate_DevNodeW(out node, instanceId, 0) != 0) return null;
            var ids = GetStrings(node, HardwareIds);
            byte[] container = Get(node, ContainerId);
            return new KeyboardDevice
            {
                InstanceId = instanceId,
                HardwareId = ids.Length > 0 ? ids[0] : instanceId,
                Name = BestName(node),
                Internal = container != null && container.Length == 16 && new Guid(container) == ComputerContainer
                           && !instanceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)
                           && !instanceId.StartsWith("SWD\\", StringComparison.OrdinalIgnoreCase),
            };
        }

        // \\?\HID#VID_3434&PID_D030&MI_02&Col01#8&282de2e0&0&0000#{884b96c3-...}  ->  HID\VID_3434&PID_D030&MI_02&Col01\8&282de2e0&0&0000
        public static string InstanceIdFromInterfacePath(string path)
        {
            string s = path;
            if (s.StartsWith(@"\\?\") || s.StartsWith(@"\??\")) s = s.Substring(4);
            int guid = s.LastIndexOf("#{", StringComparison.Ordinal);
            if (guid >= 0) s = s.Substring(0, guid);
            return s.Replace('#', '\\');
        }

        static string BestName(uint node)
        {
            // HID keyboards are all called "HID Keyboard Device"; the USB/Bluetooth parent usually has the product name.
            uint n = node;
            for (int i = 0; i < 4; i++)
            {
                string bus = GetString(n, BusReportedDesc);
                if (!string.IsNullOrEmpty(bus)) return bus;
                uint parent;
                if (CM_Get_Parent(out parent, n, 0) != 0) break;
                n = parent;
            }
            return GetString(node, DeviceDesc) ?? "Keyboard";
        }

        static byte[] Get(uint node, DEVPROPKEY key)
        {
            uint type, size = 0;
            CM_Get_DevNode_PropertyW(node, ref key, out type, null, ref size, 0);
            if (size == 0) return null;
            var buf = new byte[size];
            return CM_Get_DevNode_PropertyW(node, ref key, out type, buf, ref size, 0) == 0 ? buf : null;
        }

        static string GetString(uint node, DEVPROPKEY key)
        {
            var b = Get(node, key);
            return b == null ? null : Encoding.Unicode.GetString(b).TrimEnd('\0');
        }

        static string[] GetStrings(uint node, DEVPROPKEY key)
        {
            var b = Get(node, key);
            return b == null ? new string[0] : Encoding.Unicode.GetString(b).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
