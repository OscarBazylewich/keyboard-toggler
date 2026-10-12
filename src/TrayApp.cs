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
        static readonly Regex VidPid = new Regex(@"VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", RegexOptions.IgnoreCase);

        readonly NotifyIcon tray = new NotifyIcon();
        readonly Icon iconOn = MakeIcon(false), iconOff = MakeIcon(true);
        readonly MsgWindow win;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly HashSet<string> learned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Blocker blocker;
        KeepOffPrompt prompt;
        ToolStripMenuItem statusItem, toggleItem, startupItem;

        public TrayApp()
        {
            Log.Write("started");
            try { foreach (var l in File.ReadAllLines(LearnedFile)) if (l.Trim() != "") learned.Add(l.Trim()); } catch { }

            try { blocker = new Blocker(); blocker.EmergencyCombo += () => win.BeginInvoke((Action)(() => TurnOn("Ctrl+Alt+Shift+K"))); }
            catch (Exception ex) { Log.Write("blocker unavailable: " + ex.Message); blocker = null; }

            win = new MsgWindow(this);

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem { Enabled = false };
            toggleItem = new ToolStripMenuItem("", null, (s, e) => Toggle());
            startupItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => SetStartup(!startupItem.Checked));
            menu.Items.Add(statusItem);
            menu.Items.Add(toggleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add("Show detected keyboards", null, (s, e) => ShowKeyboards());
            menu.Items.Add("Open log folder", null, (s, e) => { Directory.CreateDirectory(Log.Dir); Process.Start("explorer.exe", Log.Dir); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            menu.Opening += (s, e) => startupItem.Checked = IsStartup();

            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            tray.Visible = true;
            UpdateUi();

            poll.Tick += (s, e) => CheckExternal();
            poll.Start();
            SystemEvents.SessionEnding += (s, e) => TurnOn("sign-out/shutdown");

            if (blocker == null)
                Balloon("Interception driver not found",
                    "Keyboard Toggler needs the Interception driver. Run install.cmd as administrator and restart Windows.", ToolTipIcon.Error);
        }

        bool Off { get { return blocker != null && blocker.Blocking; } }

        void Toggle() { if (Off) TurnOn("tray click"); else TurnOff(); }

        void TurnOff()
        {
            if (blocker == null)
            {
                MessageBox.Show("The Interception driver isn't available, so the laptop keyboard can't be switched off.\n\n" +
                    "Run install.cmd as administrator, then restart Windows.", "Keyboard Toggler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            try { n = blocker.Start(); }
            catch (Exception ex) { Log.Write("start failed: " + ex); MessageBox.Show("Couldn't switch off the laptop keyboard:\n" + ex.Message, "Keyboard Toggler"); return; }
            if (n == 0)
            {
                Log.Write("no built-in keyboard among: " + string.Join(" | ", blocker.Keyboards().Values));
                MessageBox.Show("Couldn't find the built-in keyboard through the Interception driver.\nUse \"Show detected keyboards\" and send me the list.", "Keyboard Toggler");
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
            if (why != "tray click" && why != "chose turn back on" && why != "app exit")
                Balloon("Laptop keyboard is back on", "Reason: " + why, ToolTipIcon.Info);
        }

        void UpdateUi()
        {
            bool off = Off;
            tray.Icon = off ? iconOff : iconOn;
            tray.Text = off ? "Laptop keyboard: OFF (click to turn on)" : "Laptop keyboard: ON (click to turn off)";
            statusItem.Text = blocker == null ? "Interception driver not available" : off ? "Laptop keyboard is OFF" : "Laptop keyboard is ON";
            toggleItem.Text = off ? "Turn laptop keyboard on" : "Turn laptop keyboard off";
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            tray.BalloonTipTitle = title; tray.BalloonTipText = text; tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(5000);
        }

        // ---- external keyboard tracking ----

        // Called for every raw keystroke; remembers which non-built-in keyboards are actually used for typing.
        internal void OnRawKey(string deviceName)
        {
            if (Blocker.IsBuiltInId(deviceName.Replace('#', '\\').TrimStart('\\', '?'))) return;
            var m = VidPid.Match(deviceName);
            if (!m.Success) return;
            string id = m.Value.ToUpperInvariant();
            if (learned.Add(id))
            {
                Log.Write("learned external keyboard " + id);
                try { Directory.CreateDirectory(Log.Dir); File.WriteAllLines(LearnedFile, learned.ToArray()); } catch { }
            }
        }

        static HashSet<string> PresentKeyboards()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in Native.KeyboardDeviceNames())
            {
                var m = VidPid.Match(name);
                if (m.Success) set.Add(m.Value.ToUpperInvariant());
            }
            return set;
        }

        bool ExternalPresent() { return learned.Overlaps(PresentKeyboards()); }

        internal void CheckExternal()
        {
            if (Off && !ExternalPresent()) TurnOn("external keyboard disconnected");
        }

        void ShowKeyboards()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Through Interception driver:");
            if (blocker == null) sb.AppendLine("  (driver not available)");
            else foreach (var kv in blocker.Keyboards())
                sb.AppendLine("  #" + kv.Key + (Blocker.IsBuiltInId(kv.Value) ? "  [BUILT-IN]  " : "  ") + kv.Value);
            sb.AppendLine();
            sb.AppendLine("External keyboards you've typed on: " + (learned.Count == 0 ? "(none yet)" : string.Join(", ", learned)));
            sb.AppendLine("Currently connected: " + string.Join(", ", PresentKeyboards()));
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
    // Ctrl+Alt+Shift+K hotkey for the external keyboard.
    sealed class MsgWindow : NativeWindow
    {
        readonly TrayApp app;
        public MsgWindow(TrayApp app)
        {
            this.app = app;
            CreateHandle(new CreateParams { Caption = "KeyboardTogglerMsg" });
            var d = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 6, Flags = 0x100 /*INPUTSINK*/, Target = Handle } };
            if (!Native.RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)))) Log.Write("raw input registration failed");
            if (!Native.RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4 | 0x4000 /*ALT|CTRL|SHIFT|NOREPEAT*/, (uint)Keys.K)) Log.Write("hotkey registration failed");
        }

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
                    Run(() => app.TurnOn("Ctrl+Alt+Shift+K"));
                    break;
                case 0x0219: // WM_DEVICECHANGE
                    app.CheckExternal();
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
