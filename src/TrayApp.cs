using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (new Mutex(true, @"Local\KeyboardToggler.v2", out first))
            {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI error: " + e.Exception);
                using (var app = new TrayApp()) Application.Run(app);
            }
        }
    }

    static class Log
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardToggler");
        static readonly object gate = new object();
        public static void Write(string s)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Dir);
                    string path = Path.Combine(Dir, "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + s + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    sealed class TrayApp : ApplicationContext, IKeyboardHost
    {
        static readonly string LearnedFile = Path.Combine(Log.Dir, "external-keyboards.txt");
        // This file keeps the manual selections that override the automatic selection.
        // "+<hardware id>" = deactivate this keyboard also. "-<hardware id>" = do not deactivate this keyboard.
        static readonly string ChoiceFile = Path.Combine(Log.Dir, "laptop-keyboards.txt");
        static readonly Regex Revision = new Regex(@"&REV_[^&\\]*", RegexOptions.IgnoreCase);

        readonly NotifyIcon tray = new NotifyIcon();
        Icon iconOn, iconOff;
        readonly MsgWindow win;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly HashSet<string> learned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> forceOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> forceOff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, KeyboardDevice> rawDevices = new Dictionary<string, KeyboardDevice>(StringComparer.OrdinalIgnoreCase);
        Blocker blocker;
        KeepOffPrompt prompt;
        KeyboardsWindow keyboardsWindow;
        bool shownOff;
        readonly ContextMenu menu = new ContextMenu();
        MenuItem toggleItem, startupItem;
        Hotkey shortcut = Hotkey.Load();

        public TrayApp()
        {
            Log.Write("Keyboard Toggler started");
            try { foreach (var l in File.ReadAllLines(LearnedFile)) if (l.Trim() != "") learned.Add(l.Trim()); } catch { }
            try
            {
                foreach (var l in File.ReadAllLines(ChoiceFile))
                {
                    string s = l.Trim();
                    if (s.Length < 2) continue;
                    if (s[0] == '+') forceOn.Add(s.Substring(1)); else if (s[0] == '-') forceOff.Add(s.Substring(1));
                }
            }
            catch { }

            try
            {
                blocker = new Blocker { Hotkey = shortcut };
                blocker.HotkeyPressed += () => win.BeginInvoke((Action)(() => TurnOn("you typed the shortcut")));
            }
            catch (Exception ex) { Log.Write("cannot open the Interception driver: " + ex.Message); blocker = null; }

            win = new MsgWindow(this);
            bool shortcutOk = win.RegisterShortcut(shortcut);
            if (!shortcutOk) Log.Write("the shortcut " + shortcut + " is not available because a different app uses it");

            // This is a Windows menu. Thus, it agrees with the other Windows 11 menus (rounded corners, dark mode, DPI).
            toggleItem = new MenuItem("", (s, e) => Toggle()) { DefaultItem = true };   // If you click the tray icon, Keyboard Toggler does the same step as this bold item.
            startupItem = new MenuItem("Start when you sign in", (s, e) => SetStartup(!startupItem.Checked));
            if (blocker == null) menu.MenuItems.Add(new MenuItem("The Interception driver is not available") { Enabled = false });
            menu.MenuItems.Add(toggleItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Keyboards\u2026", (s, e) => ShowKeyboards());
            menu.MenuItems.Add("Change the shortcut\u2026", (s, e) => ChangeShortcut());
            menu.MenuItems.Add(startupItem);
            menu.MenuItems.Add("Open the log folder", (s, e) => OpenLogFolder());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Stop Keyboard Toggler", (s, e) => ExitThread());
            menu.Popup += (s, e) => { startupItem.Checked = IsStartup(); UpdateUi(); };

            tray.ContextMenu = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            DarkMenus.Apply();
            MakeIcons();
            tray.Visible = true;
            UpdateUi();
            Theme.Changed += ThemeChanged;

            poll.Tick += (s, e) => CheckExternal();
            poll.Start();
            SystemEvents.SessionEnding += (s, e) => TurnOn("Windows sign-out or shutdown");

            if (blocker != null && !shortcutOk)
                Balloon("The shortcut is not available",
                    shortcut + " is not available because a different app uses it. Right-click the tray icon. Then select \"Change the shortcut\".", ToolTipIcon.Warning);
            if (blocker == null)
                Balloon("The Interception driver is not available",
                    "The Interception driver is necessary for Keyboard Toggler. Install Keyboard Toggler again. Then start Windows again.", ToolTipIcon.Error);
        }

        bool Off { get { return blocker != null && blocker.Blocking; } }

        void Toggle() { if (Off) TurnOn("you clicked the tray icon"); else TurnOff(); }

        // MsgWindow starts this procedure when you type the shortcut on a keyboard that is on.
        // If you type the shortcut on the laptop keyboard while it is off, Blocker.HotkeyPressed activates the laptop keyboard.
        internal void OnShortcut() { if (Off) TurnOn("you typed the shortcut"); else TurnOff(); }

        void ChangeShortcut()
        {
            win.UnregisterShortcut(); // Thus, if you type your shortcut in the dialog, the laptop keyboard does not change.
            using (var dlg = new ShortcutDialog(shortcut, win.RegisterShortcut))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    shortcut = dlg.Result;
                    if (blocker != null) blocker.Hotkey = shortcut;
                    try { shortcut.Save(); } catch (Exception ex) { Log.Write("cannot save the shortcut: " + ex.Message); }
                    Log.Write("new shortcut: " + shortcut);
                }
                else if (!win.RegisterShortcut(shortcut)) Log.Write("the shortcut " + shortcut + " is not available because a different app uses it");
            }
            UpdateUi();
        }

        void TurnOff()
        {
            if (blocker == null)
            {
                Notice.Show(null, NoticeKind.Error, "The Interception driver is not available",
                    "The Interception driver is necessary to deactivate the laptop keyboard. Install Keyboard Toggler again. Then start Windows again.");
                return;
            }
            if (!ExternalPresent())
            {
                if (learned.Count == 0)
                    Notice.Show(null, NoticeKind.Info, "Type on your external keyboard first",
                        "Push a key on your external keyboard one time. Then try again. This lets Keyboard Toggler know which keyboard is your external keyboard.");
                else
                    Notice.Show(null, NoticeKind.Info, "No external keyboard is connected",
                        "The laptop keyboard stays on until you connect one of your external keyboards. If the keyboard is new, push a key on it one time. Then try again.");
                return;
            }
            int n;
            var present = Devices.PresentKeyboards();
            try { n = blocker.Start(id => IsTarget(id, present)); }
            catch (Exception ex)
            {
                Log.Write("cannot deactivate the laptop keyboard: " + ex);
                Notice.Show(null, NoticeKind.Error, "Keyboard Toggler cannot deactivate the laptop keyboard", ex.Message);
                return;
            }
            if (n == 0)
            {
                Log.Write("cannot find the laptop keyboard. Interception keyboards: " + string.Join(" | ", blocker.Keyboards().Values));
                if (Notice.Show(null, NoticeKind.Warning, "Keyboard Toggler cannot find the laptop keyboard",
                        "Select the laptop keyboard in the Keyboards window. Then try again.", "Select keyboard", "Cancel") == 0)
                    ShowKeyboards();
                return;
            }
            Log.Write("the laptop keyboard is OFF");
            UpdateUi();

            prompt = new KeepOffPrompt(15);
            prompt.FormClosed += (s, e) =>
            {
                var p = (KeepOffPrompt)s;
                if (p == prompt) prompt = null;
                if (!p.Kept) TurnOn(p.TimedOut ? "you did not select \"Keep it off\" in 15 s" : "you selected \"Activate it\"");
            };
            prompt.Show();
        }

        internal void TurnOn(string why)
        {
            if (!Off) return;
            blocker.Stop();
            Log.Write("the laptop keyboard is ON (cause: " + why + ")");
            if (prompt != null) { var p = prompt; prompt = null; p.Kept = true; p.Close(); }
            UpdateUi();
            if (why != "you clicked the tray icon" && why != "you selected \"Activate it\"" && why != "Keyboard Toggler stopped" && why != "you typed the shortcut")
                Balloon("The laptop keyboard is on again", "Cause: " + why, ToolTipIcon.Info);
        }

        void UpdateUi()
        {
            bool off = Off;
            if (off != shownOff && keyboardsWindow != null && !keyboardsWindow.IsDisposed) keyboardsWindow.Reload();   // The window shows a note when the laptop keyboard is off.
            shownOff = off;
            tray.Icon = off ? iconOff : iconOn;
            tray.Text = off ? "The laptop keyboard is off\nClick to activate it" : "The laptop keyboard is on\nClick to deactivate it";
            toggleItem.Text = (off ? "Activate the laptop keyboard" : "Deactivate the laptop keyboard") + "\t" + shortcut;   // \t puts the shortcut on the right side of the menu.
        }

        // This procedure makes the tray icons for Windows 11. The icons are white in dark mode and black in light mode.
        void MakeIcons()
        {
            Icon oldOn = iconOn, oldOff = iconOff;
            iconOn = TrayIcon.Create(false, Theme.DarkTaskbar);
            iconOff = TrayIcon.Create(true, Theme.DarkTaskbar);
            tray.Icon = Off ? iconOff : iconOn;
            if (oldOn != null) oldOn.Dispose();
            if (oldOff != null) oldOff.Dispose();
        }

        // Windows changed to light mode or dark mode, or the accent color changed. Theme.Changed can occur on a different thread.
        void ThemeChanged()
        {
            win.BeginInvoke(() => { DarkMenus.Apply(); MakeIcons(); UpdateUi(); });
        }

        void OpenLogFolder()
        {
            Directory.CreateDirectory(Log.Dir);
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + Log.Dir + "\"");
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            tray.BalloonTipTitle = title; tray.BalloonTipText = text; tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(5000);
        }

        // ---- The keyboards that are "the laptop keyboard" ----

        // Internal = Windows puts the keyboard in the device container of the PC (PS/2, I2C and internal USB keyboards).
        // If Windows does not give data about a keyboard, Keyboard Toggler compares its hardware ID with the PS/2 IDs.
        static bool AutoBuiltIn(string hardwareId, List<KeyboardDevice> present)
        {
            bool known = false;
            foreach (var d in present)
                if (string.Equals(d.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
                {
                    if (d.Internal) return true;
                    known = true;
                }
            return !known && Blocker.IsPs2KeyboardId(hardwareId);
        }

        bool IsTarget(string hardwareId, List<KeyboardDevice> present)
        {
            if (forceOn.Contains(hardwareId)) return true;
            if (forceOff.Contains(hardwareId)) return false;
            return AutoBuiltIn(hardwareId, present);
        }

        bool IsTarget(string hardwareId) { return IsTarget(hardwareId, Devices.PresentKeyboards()); }

        void SetTarget(string hardwareId, bool on)
        {
            forceOn.Remove(hardwareId); forceOff.Remove(hardwareId);
            if (on != AutoBuiltIn(hardwareId, Devices.PresentKeyboards())) (on ? forceOn : forceOff).Add(hardwareId);
            Log.Write((on ? "deactivate with the laptop keyboard: " : "do not deactivate: ") + hardwareId);
            ChoicesChanged();
        }

        // This procedure saves the keyboard selection. If the laptop keyboard is off, the selection has an effect immediately.
        // Blocker deactivates the selected keyboards again. If no keyboard is selected, the laptop keyboard is on again.
        void ChoicesChanged()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllLines(ChoiceFile, forceOn.Select(h => "+" + h).Concat(forceOff.Select(h => "-" + h)).ToArray());
            }
            catch (Exception ex) { Log.Write("cannot save the keyboard selection: " + ex.Message); }
            rawDevices.Clear();
            if (!Off) return;
            blocker.Stop();
            var present = Devices.PresentKeyboards();
            if (blocker.Start(id => IsTarget(id, present)) == 0)
            {
                Log.Write("the laptop keyboard is ON (cause: no keyboard is selected)");
                if (prompt != null) { var p = prompt; prompt = null; p.Kept = true; p.Close(); }
                UpdateUi();
            }
            else CheckExternal();
        }

        // ---- The Keyboards window (IKeyboardHost) ----

        void ShowKeyboards()
        {
            if (keyboardsWindow != null && !keyboardsWindow.IsDisposed) { keyboardsWindow.Activate(); return; }
            Log.Write(DetectedKeyboards());
            keyboardsWindow = new KeyboardsWindow(this);
            keyboardsWindow.FormClosed += (s, e) => keyboardsWindow = null;
            keyboardsWindow.Show();
            keyboardsWindow.Activate();
        }

        List<KeyboardInfo> IKeyboardHost.Keyboards()
        {
            var present = Devices.PresentKeyboards();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<KeyboardInfo>();
            foreach (var d in present.OrderBy(d => d.Internal ? 0 : 1).ThenBy(d => d.Name))
            {
                if (!seen.Add(d.HardwareId)) continue;
                bool target = IsTarget(d.HardwareId, present);
                list.Add(new KeyboardInfo
                {
                    Key = d.HardwareId, Name = d.Name, HardwareId = Revision.Replace(d.HardwareId, ""),
                    BuiltIn = d.Internal, SwitchedOff = target, TypedOn = !target && IsLearned(d.HardwareId),
                });
            }
            return list;
        }

        void IKeyboardHost.SetSwitchOff(string key, bool switchOff) { SetTarget(key, switchOff); }

        bool IKeyboardHost.HasManualChoices { get { return forceOn.Count + forceOff.Count > 0; } }

        // This procedure removes all manual selections at the same time. Thus, the sequence of the removal has no effect on the result.
        void IKeyboardHost.ResetToAutomatic()
        {
            forceOn.Clear(); forceOff.Clear();
            Log.Write("you set the keyboard selection to automatic");
            ChoicesChanged();
        }

        string IKeyboardHost.Details() { return DetectedKeyboards(); }

        bool IKeyboardHost.KeyboardIsOff { get { return Off; } }

        // ---- External keyboards that you typed on ----

        // Keyboard Toggler records the hardware ID without the firmware revision (REV_). Thus, Keyboard Toggler can find the keyboard after a firmware update.
        static string LearnKey(string hardwareId) { return Revision.Replace(hardwareId, "").ToUpperInvariant(); }

        // Entries from version 2.0.x have only "VID_xxxx&PID_yyyy". If a hardware ID contains the entry, IsLearned gives true.
        bool IsLearned(string hardwareId)
        {
            string key = LearnKey(hardwareId);
            foreach (string l in learned)
                if (key == l.ToUpperInvariant() || key.Contains(l.ToUpperInvariant())) return true;
            return false;
        }

        // MsgWindow starts this procedure for each WM_INPUT message. It records each external keyboard that you type on.
        internal void OnRawKey(string deviceName)
        {
            // rawDevices keeps the result for each device. Thus, Keyboard Toggler does not examine the device tree each time that you push a key.
            // OnDeviceChange and ChoicesChanged remove all entries from rawDevices.
            KeyboardDevice d;
            if (!rawDevices.TryGetValue(deviceName, out d))
            {
                d = Devices.Describe(Devices.InstanceIdFromInterfacePath(deviceName));
                if (d != null && IsTarget(d.HardwareId)) d = null;
                rawDevices[deviceName] = d;
            }
            if (d == null) return;
            if (!IsLearned(d.HardwareId) && learned.Add(LearnKey(d.HardwareId)))
            {
                Log.Write("Keyboard Toggler recorded a new external keyboard: " + LearnKey(d.HardwareId) + " (" + d.Name + ")");
                try { Directory.CreateDirectory(Log.Dir); File.WriteAllLines(LearnedFile, learned.ToArray()); } catch { }
            }
        }

        // This procedure gives true if a keyboard that you typed on is connected, and Keyboard Toggler does not deactivate that keyboard.
        bool ExternalPresent()
        {
            var present = Devices.PresentKeyboards();
            return present.Any(d => !IsTarget(d.HardwareId, present) && IsLearned(d.HardwareId));
        }

        internal void CheckExternal()
        {
            if (Off && !ExternalPresent()) TurnOn("no external keyboard is connected");
        }

        internal void OnDeviceChange()
        {
            rawDevices.Clear();
            CheckExternal();
            if (keyboardsWindow != null && !keyboardsWindow.IsDisposed) keyboardsWindow.Reload();
        }

        // This procedure makes the plain-text report for "Copy report". Keyboard Toggler also writes it to the log when the Keyboards window opens.
        string DetectedKeyboards()
        {
            var present = Devices.PresentKeyboards();
            var sb = new StringBuilder();
            sb.AppendLine("Connected keyboards:");
            foreach (var d in present)
                sb.AppendLine(string.Format("  {0} {1}{2}\r\n      {3}",
                    IsTarget(d.HardwareId, present) ? "[laptop keyboard]" : IsLearned(d.HardwareId) ? "[your external keyboard]" : "[external]",
                    d.Name, d.Internal ? " (internal)" : "", d.HardwareId));
            sb.AppendLine();
            sb.AppendLine("Keyboards in the Interception driver:");
            if (blocker == null) sb.AppendLine("  (the Interception driver is not available)");
            else foreach (var kv in blocker.Keyboards()) sb.AppendLine("  #" + kv.Key + "  " + kv.Value);
            return sb.ToString();
        }

        // ---- Start when you sign in ----

        // Other apps use the same procedure: an entry in the Run key, plus a StartupApproved value for "Startup apps" in Task Manager.
        // Byte 0 of that value is 02 (Enabled) or 03 (Disabled). Thus, you can also activate or deactivate this entry in Task Manager.
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string RunName = "KeyboardToggler";

        static bool IsStartup()
        {
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
                if (run == null || run.GetValue(RunName) == null) return false;
            using (var ok = Registry.CurrentUser.OpenSubKey(ApprovedKey))
            {
                var state = ok == null ? null : ok.GetValue(RunName) as byte[];
                return state == null || state.Length == 0 || (state[0] & 1) == 0;
            }
        }

        static void SetStartup(bool on)
        {
            try
            {
                using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                    run.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                var state = new byte[12];
                state[0] = (byte)(on ? 2 : 3);
                if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(state, 4);
                using (var ok = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                    ok.SetValue(RunName, state, RegistryValueKind.Binary);
            }
            catch (Exception ex) { Log.Write("cannot change \"Start when you sign in\": " + ex.Message); }
        }

        protected override void ExitThreadCore()
        {
            TurnOn("Keyboard Toggler stopped");
            if (keyboardsWindow != null && !keyboardsWindow.IsDisposed) keyboardsWindow.Close();
            tray.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= ThemeChanged;
                poll.Dispose(); tray.Dispose(); menu.Dispose(); win.DestroyHandle();
                if (iconOn != null) iconOn.Dispose();
                if (iconOff != null) iconOff.Dispose();
                if (blocker != null) blocker.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Windows menus use dark mode only if the app uses SetPreferredAppMode. uxtheme.dll gives this function only by ordinal
    // (Windows 10 version 1903 and subsequent versions). On previous versions, Apply has no effect.
    static class DarkMenus
    {
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();

        public static void Apply()
        {
            var v = Environment.OSVersion.Version;
            if (v.Major < 10 || v.Build < 18362) return;
            try
            {
                SetPreferredAppMode(Theme.HighContrast ? 0 : Theme.Dark ? 2 : 3);   // 0 = Default, 2 = ForceDark, 3 = ForceLight
                FlushMenuThemes();
            }
            catch (EntryPointNotFoundException) { }
        }
    }

    // MsgWindow is a window that you cannot see. It receives these messages:
    // - WM_INPUT (raw input), to record external keyboards
    // - WM_DEVICECHANGE, when you connect or disconnect a device
    // - WM_HOTKEY, when you type the shortcut on a keyboard that is on
    sealed class MsgWindow : NativeWindow
    {
        readonly TrayApp app;
        public MsgWindow(TrayApp app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Caption = "KeyboardTogglerMsg" });
            var d = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = 0x100 /*INPUTSINK*/, Target = Handle } };
            if (!Native.RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)))) Log.Write("error in the raw input registration");
        }

        // This procedure replaces the shortcut for RegisterHotKey. It gives false if Windows or a different app uses the same keys.
        public bool RegisterShortcut(Hotkey h)
        {
            Native.UnregisterHotKey(Handle, 1);
            return h != null && Native.RegisterHotKey(Handle, 1, h.Mods | 0x4000 /*MOD_NOREPEAT*/, h.Vk);
        }

        public void UnregisterShortcut() { Native.UnregisterHotKey(Handle, 1); }

        readonly Queue<Action> pending = new Queue<Action>();

        // You can use this from a different thread. MsgWindow does the Action on the UI thread.
        public void BeginInvoke(Action a)
        {
            lock (pending) pending.Enqueue(a);
            Native.PostMessage(Handle, 0x8001, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x00FF: // WM_INPUT
                    try
                    {
                        IntPtr dev = Native.RawInputDevice(m.LParam);
                        if (dev != IntPtr.Zero) app.OnRawKey(Native.DeviceName(dev));
                    }
                    catch (Exception ex) { Log.Write("raw input error: " + ex.Message); }
                    break;
                case 0x0312: // WM_HOTKEY
                    Run(() => app.OnShortcut());
                    break;
                case 0x0219: // WM_DEVICECHANGE
                    app.OnDeviceChange();
                    break;
                case 0x8001:
                    for (;;)
                    {
                        Action a;
                        lock (pending) { if (pending.Count == 0) break; a = pending.Dequeue(); }
                        Run(a);
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        static void Run(Action a) { try { a(); } catch (Exception ex) { Log.Write("error: " + ex); } }
    }
}
