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
                Application.ThreadException += (s, e) => Log.Write("UI exception: " + e.Exception);
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
        // Per-keyboard overrides of the built-in detection: "+<hardware id>" = switch it off too, "-<hardware id>" = never.
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
            Log.Write("started");
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
                blocker.HotkeyPressed += () => win.BeginInvoke((Action)(() => TurnOn("shortcut")));
            }
            catch (Exception ex) { Log.Write("blocker unavailable: " + ex.Message); blocker = null; }

            win = new MsgWindow(this);
            bool shortcutOk = win.RegisterShortcut(shortcut);
            if (!shortcutOk) Log.Write("shortcut " + shortcut + " is taken by another app");

            // Windows' own menu, so it looks and behaves like the rest of Windows 11 (rounded corners, dark mode, DPI).
            toggleItem = new MenuItem("", (s, e) => Toggle()) { DefaultItem = true };   // bold: what a left-click does
            startupItem = new MenuItem("Start with Windows", (s, e) => SetStartup(!startupItem.Checked));
            if (blocker == null) menu.MenuItems.Add(new MenuItem("The keyboard driver isn't running") { Enabled = false });
            menu.MenuItems.Add(toggleItem);
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Keyboards\u2026", (s, e) => ShowKeyboards());
            menu.MenuItems.Add("Change shortcut\u2026", (s, e) => ChangeShortcut());
            menu.MenuItems.Add(startupItem);
            menu.MenuItems.Add("Open log folder", (s, e) => OpenLogFolder());
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Exit", (s, e) => ExitThread());
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
            SystemEvents.SessionEnding += (s, e) => TurnOn("sign-out/shutdown");

            if (blocker != null && !shortcutOk)
                Balloon("Shortcut unavailable",
                    shortcut + " is already used by another app. Right-click the tray icon and choose \"Change shortcut\".", ToolTipIcon.Warning);
            if (blocker == null)
                Balloon("Interception driver not found",
                    "Keyboard Toggler needs the Interception driver. Reinstall Keyboard Toggler and restart Windows.", ToolTipIcon.Error);
        }

        bool Off { get { return blocker != null && blocker.Blocking; } }

        void Toggle() { if (Off) TurnOn("tray click"); else TurnOff(); }

        // The toggle shortcut, typed on a keyboard that isn't blocked (the blocked one only ever turns back on).
        internal void OnShortcut() { if (Off) TurnOn("shortcut"); else TurnOff(); }

        void ChangeShortcut()
        {
            win.UnregisterShortcut(); // so pressing the current shortcut in the dialog doesn't toggle
            using (var dlg = new ShortcutDialog(shortcut, win.RegisterShortcut))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                {
                    shortcut = dlg.Result;
                    if (blocker != null) blocker.Hotkey = shortcut;
                    try { shortcut.Save(); } catch (Exception ex) { Log.Write("saving shortcut: " + ex.Message); }
                    Log.Write("shortcut set to " + shortcut);
                }
                else if (!win.RegisterShortcut(shortcut)) Log.Write("shortcut " + shortcut + " is taken by another app");
            }
            UpdateUi();
        }

        void TurnOff()
        {
            if (blocker == null)
            {
                Notice.Show(null, NoticeKind.Error, "The keyboard driver isn't running",
                    "Keyboard Toggler needs the Interception driver to switch the laptop keyboard off. Reinstall Keyboard Toggler, then restart Windows.");
                return;
            }
            if (!ExternalPresent())
            {
                if (learned.Count == 0)
                    Notice.Show(null, NoticeKind.Info, "Type on your external keyboard first",
                        "Press any key on your external keyboard once, then try again. That's how Keyboard Toggler knows which keyboard is yours.");
                else
                    Notice.Show(null, NoticeKind.Info, "No external keyboard connected",
                        "The laptop keyboard stays on until one of your external keyboards is connected. If it's a new keyboard, press a key on it once, then try again.");
                return;
            }
            int n;
            var present = Devices.PresentKeyboards();
            try { n = blocker.Start(id => IsTarget(id, present)); }
            catch (Exception ex)
            {
                Log.Write("start failed: " + ex);
                Notice.Show(null, NoticeKind.Error, "Couldn't switch off the laptop keyboard", ex.Message);
                return;
            }
            if (n == 0)
            {
                Log.Write("no laptop keyboard among: " + string.Join(" | ", blocker.Keyboards().Values));
                if (Notice.Show(null, NoticeKind.Warning, "Couldn't find the laptop keyboard",
                        "Choose your laptop's keyboard in the Keyboards window, then try again.", "Choose keyboard", "Cancel") == 0)
                    ShowKeyboards();
                return;
            }
            Log.Write("laptop keyboard OFF");
            UpdateUi();

            prompt = new KeepOffPrompt(15);
            prompt.FormClosed += (s, e) =>
            {
                var p = (KeepOffPrompt)s;
                if (p == prompt) prompt = null;
                if (!p.Kept) TurnOn(p.TimedOut ? "no confirmation within 15 s" : "chose turn back on");
            };
            prompt.Show();
        }

        internal void TurnOn(string why)
        {
            if (!Off) return;
            blocker.Stop();
            Log.Write("laptop keyboard ON (" + why + ")");
            if (prompt != null) { var p = prompt; prompt = null; p.Kept = true; p.Close(); }
            UpdateUi();
            if (why != "tray click" && why != "chose turn back on" && why != "app exit" && why != "shortcut")
                Balloon("Laptop keyboard is back on", "Reason: " + why, ToolTipIcon.Info);
        }

        void UpdateUi()
        {
            bool off = Off;
            if (off != shownOff && keyboardsWindow != null && !keyboardsWindow.IsDisposed) keyboardsWindow.Reload();   // its "off right now" note
            shownOff = off;
            tray.Icon = off ? iconOff : iconOn;
            tray.Text = off ? "Laptop keyboard is off\nClick to turn it back on" : "Laptop keyboard is on\nClick to turn it off";
            toggleItem.Text = (off ? "Turn laptop keyboard on" : "Turn laptop keyboard off") + "\t" + shortcut;   // tab: right-aligned shortcut
        }

        // Tray icons in the Windows 11 style: white on a dark taskbar, black on a light one.
        void MakeIcons()
        {
            Icon oldOn = iconOn, oldOff = iconOff;
            iconOn = TrayIcon.Create(false, Theme.DarkTaskbar);
            iconOff = TrayIcon.Create(true, Theme.DarkTaskbar);
            tray.Icon = Off ? iconOff : iconOn;
            if (oldOn != null) oldOn.Dispose();
            if (oldOff != null) oldOff.Dispose();
        }

        // Light/dark mode or accent colour changed (may be raised on another thread).
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

        // ---- which keyboards are "the laptop keyboard" ----

        // Built in = Windows places it in the computer's own container (PS/2, I2C and internal-USB keyboards alike).
        // Keyboards Windows can't describe fall back to the classic PS/2 IDs.
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
            Log.Write((on ? "switch off with laptop keyboard: " : "never switch off: ") + hardwareId);
            ChoicesChanged();
        }

        // Saves the keyboard choices and, if the laptop keyboard is off, applies them right away:
        // re-filter with the new choice, or turn back on if nothing is left to block.
        void ChoicesChanged()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllLines(ChoiceFile, forceOn.Select(h => "+" + h).Concat(forceOff.Select(h => "-" + h)).ToArray());
            }
            catch (Exception ex) { Log.Write("saving keyboard choice: " + ex.Message); }
            rawDevices.Clear();
            if (!Off) return;
            blocker.Stop();
            var present = Devices.PresentKeyboards();
            if (blocker.Start(id => IsTarget(id, present)) == 0)
            {
                Log.Write("laptop keyboard ON (no keyboard selected)");
                if (prompt != null) { var p = prompt; prompt = null; p.Kept = true; p.Close(); }
                UpdateUi();
            }
            else CheckExternal();
        }

        // ---- the Keyboards window (IKeyboardHost) ----

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

        // All at once, so the result doesn't depend on the order the choices are undone in.
        void IKeyboardHost.ResetToAutomatic()
        {
            forceOn.Clear(); forceOff.Clear();
            Log.Write("keyboard choices reset to automatic");
            ChoicesChanged();
        }

        string IKeyboardHost.Details() { return DetectedKeyboards(); }

        bool IKeyboardHost.KeyboardIsOff { get { return Off; } }

        // ---- external keyboard tracking ----

        // Keyboards are remembered by hardware ID without the firmware revision, so a firmware update doesn't forget them.
        static string LearnKey(string hardwareId) { return Revision.Replace(hardwareId, "").ToUpperInvariant(); }

        // Learned entries from 2.0.x are just "VID_xxxx&PID_yyyy"; they still match by containment.
        bool IsLearned(string hardwareId)
        {
            string key = LearnKey(hardwareId);
            foreach (string l in learned)
                if (key == l.ToUpperInvariant() || key.Contains(l.ToUpperInvariant())) return true;
            return false;
        }

        // Called for every raw keystroke; remembers which non-laptop keyboards are actually used for typing.
        internal void OnRawKey(string deviceName)
        {
            // Cached per device, so a keystroke doesn't rescan the device tree; cleared on device changes.
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
                Log.Write("learned external keyboard " + LearnKey(d.HardwareId) + " (" + d.Name + ")");
                try { Directory.CreateDirectory(Log.Dir); File.WriteAllLines(LearnedFile, learned.ToArray()); } catch { }
            }
        }

        // An external keyboard you've typed on is connected (and isn't one of the keyboards being switched off).
        bool ExternalPresent()
        {
            var present = Devices.PresentKeyboards();
            return present.Any(d => !IsTarget(d.HardwareId, present) && IsLearned(d.HardwareId));
        }

        internal void CheckExternal()
        {
            if (Off && !ExternalPresent()) TurnOn("external keyboard disconnected");
        }

        internal void OnDeviceChange()
        {
            rawDevices.Clear();
            CheckExternal();
            if (keyboardsWindow != null && !keyboardsWindow.IsDisposed) keyboardsWindow.Reload();
        }

        // The plain-text report behind "Copy details" (and logged when the Keyboards window opens).
        string DetectedKeyboards()
        {
            var present = Devices.PresentKeyboards();
            var sb = new StringBuilder();
            sb.AppendLine("Connected keyboards:");
            foreach (var d in present)
                sb.AppendLine(string.Format("  {0} {1}{2}\r\n      {3}",
                    IsTarget(d.HardwareId, present) ? "[laptop keyboard]" : IsLearned(d.HardwareId) ? "[external, typed on]" : "[external]",
                    d.Name, d.Internal ? " (built in)" : "", d.HardwareId));
            sb.AppendLine();
            sb.AppendLine("Through the Interception driver:");
            if (blocker == null) sb.AppendLine("  (driver not available)");
            else foreach (var kv in blocker.Keyboards()) sb.AppendLine("  #" + kv.Key + "  " + kv.Value);
            return sb.ToString();
        }

        // ---- startup ----

        // Same mechanism as other apps: a Run-key entry plus its Task Manager "Startup apps" state
        // (StartupApproved; first byte 02 = enabled, 03 = disabled), so Task Manager can switch it too.
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
            catch (Exception ex) { Log.Write("startup setting: " + ex.Message); }
        }

        protected override void ExitThreadCore()
        {
            TurnOn("app exit");
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

    // Windows' own menus only follow dark mode when the app opts in, which uxtheme exposes by ordinal only
    // (Windows 10 1903 and later). Does nothing elsewhere.
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
                SetPreferredAppMode(Theme.HighContrast ? 0 : Theme.Dark ? 2 : 3);   // default / force dark / force light
                FlushMenuThemes();
            }
            catch (EntryPointNotFoundException) { }
        }
    }

    // Hidden window: raw keyboard input (to learn external keyboards), device-change notices, and the
    // toggle shortcut as a system-wide hotkey (the keyboards that aren't being blocked).
    sealed class MsgWindow : NativeWindow
    {
        readonly TrayApp app;
        public MsgWindow(TrayApp app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Caption = "KeyboardTogglerMsg" });
            var d = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = 0x100 /*INPUTSINK*/, Target = Handle } };
            if (!Native.RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)))) Log.Write("raw input registration failed");
        }

        // Replaces the registered shortcut; false if Windows or another app already owns that combination.
        public bool RegisterShortcut(Hotkey h)
        {
            Native.UnregisterHotKey(Handle, 1);
            return h != null && Native.RegisterHotKey(Handle, 1, h.Mods | 0x4000 /*MOD_NOREPEAT*/, h.Vk);
        }

        public void UnregisterShortcut() { Native.UnregisterHotKey(Handle, 1); }

        readonly Queue<Action> pending = new Queue<Action>();

        // Thread-safe: runs the action on the UI thread.
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
                    catch (Exception ex) { Log.Write("raw input: " + ex.Message); }
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
