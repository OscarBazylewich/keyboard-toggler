using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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

    sealed class TrayApp : ApplicationContext
    {
        static readonly string LearnedFile = Path.Combine(Log.Dir, "external-keyboards.txt");
        // Per-keyboard overrides of the built-in detection: "+<hardware id>" = switch it off too, "-<hardware id>" = never.
        static readonly string ChoiceFile = Path.Combine(Log.Dir, "laptop-keyboards.txt");
        static readonly Regex Revision = new Regex(@"&REV_[^&\\]*", RegexOptions.IgnoreCase);

        readonly NotifyIcon tray = new NotifyIcon();
        readonly Icon iconOn = MakeIcon(false), iconOff = MakeIcon(true);
        readonly MsgWindow win;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly HashSet<string> learned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> forceOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> forceOff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, KeyboardDevice> rawDevices = new Dictionary<string, KeyboardDevice>(StringComparer.OrdinalIgnoreCase);
        Blocker blocker;
        KeepOffPrompt prompt;
        ToolStripMenuItem statusItem, toggleItem, startupItem, laptopItem, shortcutItem;
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

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem { Enabled = false };
            toggleItem = new ToolStripMenuItem("", null, (s, e) => Toggle());
            startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => SetStartup(!startupItem.Checked));
            menu.Items.Add(statusItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(new ToolStripSeparator());
            laptopItem = new ToolStripMenuItem("Laptop keyboard");
            laptopItem.DropDownItems.Add("(loading)");
            menu.Items.Add(laptopItem);
            shortcutItem = new ToolStripMenuItem("", null, (s, e) => ChangeShortcut());
            menu.Items.Add(shortcutItem);
            menu.Items.Add(startupItem);
            menu.Items.Add("Show detected keyboards", null, (s, e) => ShowKeyboards());
            menu.Items.Add("Open log folder", null, (s, e) => { Directory.CreateDirectory(Log.Dir); Process.Start("explorer.exe", Log.Dir); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            menu.Opening += (s, e) => { startupItem.Checked = IsStartup(); FillLaptopMenu(); };

            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            tray.Visible = true;
            UpdateUi();

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
                MessageBox.Show("The Interception driver isn't available, so the laptop keyboard can't be switched off.\n\n" +
                    "Reinstall Keyboard Toggler, then restart Windows.", "Keyboard Toggler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!ExternalPresent())
            {
                MessageBox.Show(learned.Count == 0
                    ? "No external keyboard detected yet.\n\nPress any key on your external keyboard once, then try again."
                    : "None of your external keyboards is connected, so the laptop keyboard stays on.\n\n(If this is a new keyboard, press a key on it once, then try again.)",
                    "Keyboard Toggler", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int n;
            try { n = blocker.Start(IsTarget); }
            catch (Exception ex) { Log.Write("start failed: " + ex); MessageBox.Show("Couldn't switch off the laptop keyboard:\n" + ex.Message, "Keyboard Toggler"); return; }
            if (n == 0)
            {
                Log.Write("no laptop keyboard among: " + string.Join(" | ", blocker.Keyboards().Values));
                MessageBox.Show("Couldn't find the laptop's built-in keyboard.\n\n" +
                    "Right-click the tray icon, open \"Laptop keyboard\" and tick your laptop's keyboard, then try again.",
                    "Keyboard Toggler", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            tray.Icon = off ? iconOff : iconOn;
            tray.Text = off ? "Laptop keyboard: OFF (click to turn on)" : "Laptop keyboard: ON (click to turn off)";
            statusItem.Text = blocker == null ? "Interception driver not available" : off ? "Laptop keyboard is OFF" : "Laptop keyboard is ON";
            toggleItem.Text = (off ? "Turn laptop keyboard on" : "Turn laptop keyboard off");
            toggleItem.ShortcutKeyDisplayString = shortcut.ToString();
            shortcutItem.Text = "Change shortcut...";
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
            try
            {
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllLines(ChoiceFile, forceOn.Select(h => "+" + h).Concat(forceOff.Select(h => "-" + h)).ToArray());
            }
            catch (Exception ex) { Log.Write("saving keyboard choice: " + ex.Message); }
            Log.Write((on ? "switch off with laptop keyboard: " : "never switch off: ") + hardwareId);
            rawDevices.Clear();

            if (Off)
            {
                // Apply right away: re-filter with the new choice, or turn back on if nothing is left to block.
                blocker.Stop();
                if (blocker.Start(IsTarget) == 0)
                {
                    Log.Write("laptop keyboard ON (no keyboard selected)");
                    if (prompt != null) { var p = prompt; prompt = null; p.Kept = true; p.Close(); }
                    UpdateUi();
                }
                else CheckExternal();
            }
        }

        void FillLaptopMenu()
        {
            laptopItem.DropDownItems.Clear();
            laptopItem.DropDownItems.Add(new ToolStripMenuItem("Keyboards to switch off:") { Enabled = false });
            var present = Devices.PresentKeyboards();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in present.OrderBy(d => d.Internal ? 0 : 1).ThenBy(d => d.Name))
            {
                if (!seen.Add(d.HardwareId)) continue;
                string id = d.HardwareId;
                var item = new ToolStripMenuItem(d.Name + (d.Internal ? "  (built in)" : "") + "   " + Revision.Replace(id, ""));
                item.Checked = IsTarget(id, present);
                item.Click += (s, e) => SetTarget(id, !item.Checked);
                laptopItem.DropDownItems.Add(item);
            }
            if (forceOn.Count + forceOff.Count > 0)
            {
                laptopItem.DropDownItems.Add(new ToolStripSeparator());
                laptopItem.DropDownItems.Add("Reset to automatic", null, (s, e) =>
                {
                    foreach (var h in forceOn.Concat(forceOff).ToList()) SetTarget(h, AutoBuiltIn(h, Devices.PresentKeyboards()));
                });
            }
        }

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
        }

        void ShowKeyboards()
        {
            var present = Devices.PresentKeyboards();
            var sb = new StringBuilder();
            sb.AppendLine("Connected keyboards:");
            foreach (var d in present)
                sb.AppendLine(string.Format("  {0} {1}{2}\n      {3}",
                    IsTarget(d.HardwareId, present) ? "[laptop keyboard]" : IsLearned(d.HardwareId) ? "[external, typed on]" : "[external]",
                    d.Name, d.Internal ? " (built in)" : "", d.HardwareId));
            sb.AppendLine();
            sb.AppendLine("Through the Interception driver:");
            if (blocker == null) sb.AppendLine("  (driver not available)");
            else foreach (var kv in blocker.Keyboards()) sb.AppendLine("  #" + kv.Key + "  " + kv.Value);
            Log.Write(sb.ToString());
            MessageBox.Show(sb.ToString(), "Keyboard Toggler - keyboards");
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

        // ---- icons ----

        static Icon MakeIcon(bool off)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var body = off ? Color.FromArgb(140, 140, 140) : Color.FromArgb(46, 160, 67);
                    using (var b = new SolidBrush(body)) g.FillRectangle(b, 1, 7, 30, 19);
                    using (var w = new SolidBrush(Color.White))
                    {
                        for (int r = 0; r < 2; r++) for (int c = 0; c < 6; c++) g.FillRectangle(w, 4 + c * 4, 10 + r * 5, 3, 3);
                        g.FillRectangle(w, 8, 20, 16, 3);
                    }
                    if (off) using (var p = new Pen(Color.FromArgb(220, 30, 30), 4)) g.DrawLine(p, 3, 29, 29, 3);
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }

        protected override void ExitThreadCore()
        {
            TurnOn("app exit");
            tray.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                poll.Dispose(); tray.Dispose(); win.DestroyHandle();
                if (blocker != null) blocker.Dispose();
            }
            base.Dispose(disposing);
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
