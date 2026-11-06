// Setup for Keyboard Toggler: it installs and removes the app. The app files are resources in this exe file.
//   KeyboardToggler-Setup.exe              -> the install wizard
//   Uninstall.exe /uninstall               -> removes the app and the Interception driver (Setup copies this exe into the install folder)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeyboardToggler.Ui;
using Microsoft.Win32;

[assembly: AssemblyTitle("Keyboard Toggler Setup")]
[assembly: AssemblyProduct("Keyboard Toggler")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]

namespace KeyboardTogglerSetup
{
    static class Setup
    {
        public const string AppName = "Keyboard Toggler", Version = "2.3.0";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyboardToggler";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KeyboardToggler");
        public static readonly string AppExe = Path.Combine(Dir, "KeyboardToggler.exe");
        public static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Keyboard Toggler.lnk");
        static readonly string[] Payload = { "KeyboardToggler.exe", "interception.dll", "install-interception.exe", "Interception-LGPL-3.0.txt" };

        public const string ArmMessage = "You cannot install Keyboard Toggler on Windows on Arm.\n\n" +
            "Keyboard Toggler uses the Interception driver. This driver is only for Intel and AMD (x64) PCs. Windows cannot load it on this PC. " +
            "If you install the driver, no keyboard and no mouse will operate after you start Windows again.";

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        // Windows on Arm operates this x64 Setup in emulation. If Setup installs the driver there, the driver installer adds
        // x64 keyboard and mouse filters. Arm64 Windows cannot load them.
        public static bool IsArm64()
        {
            try
            {
                ushort process, native;
                return IsWow64Process2(GetCurrentProcess(), out process, out native) && native == 0xAA64; // IMAGE_FILE_MACHINE_ARM64
            }
            catch (EntryPointNotFoundException) { return false; }   // Windows 10 before 1709. It cannot operate x64 apps on Arm.
        }

        // Setup starts Windows tools with their full path. If Setup gives only a name, Windows first examines the folder of Setup
        // (for example, Downloads). A dangerous schtasks.exe in that folder can then operate with administrator rights.
        public static string SystemTool(string name) { return Path.Combine(Environment.SystemDirectory, name); }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))) Uninstaller.Run(args);
            else Application.Run(new InstallForm());
        }

        public static bool DriverInstalled()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\keyboard"))
                return k != null;
        }

        public static void StopApp()
        {
            foreach (var p in Process.GetProcessesByName("KeyboardToggler"))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
        }

        // This method starts install-interception.exe with /install or /uninstall. It gives the output of the tool.
        public static string RunDriverTool(string exe, string arg)
        {
            var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return output.Trim();
            }
        }

        // This method gives true if it is necessary to start Windows again before the driver is active.
        public static bool Install(bool startWithWindows, Action<string> progress)
        {
            if (IsArm64()) throw new Exception(ArmMessage);

            progress("Setup stops Keyboard Toggler if it operates...");
            StopApp();

            progress("Setup removes the remaining files of previous versions...");
            RemoveV1();

            progress("Setup copies the files...");
            Directory.CreateDirectory(Dir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (string name in Payload)
                using (var src = asm.GetManifestResourceStream(name))
                using (var dst = File.Create(Path.Combine(Dir, name)))
                    src.CopyTo(dst);
            File.Copy(Application.ExecutablePath, Path.Combine(Dir, "Uninstall.exe"), true);
            foreach (string old in new[] { "install.cmd", "uninstall.cmd" })
                try { File.Delete(Path.Combine(Dir, old)); } catch { }

            bool restart = false;
            if (!DriverInstalled())
            {
                progress("Setup installs the Interception driver...");
                string output = RunDriverTool(Path.Combine(Dir, "install-interception.exe"), "/install");
                if (!DriverInstalled()) throw new Exception("Setup cannot install the Interception driver:\n\n" + output);
                restart = true;
            }

            progress("Setup adds Keyboard Toggler to the Start menu...");
            CreateShortcut(Shortcut, AppExe, "Deactivate the laptop keyboard while you use an external keyboard");

            progress("Setup adds Keyboard Toggler to Installed apps in Settings...");
            using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                string uninst = Path.Combine(Dir, "Uninstall.exe");
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Keyboard Toggler");
                k.SetValue("DisplayIcon", AppExe + ",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /quiet");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)(new DirectoryInfo(Dir).GetFiles().Sum(f => f.Length) / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            progress("Setup sets Keyboard Toggler to start when you sign in...");
            SetStartup(startWithWindows);
            return restart;
        }

        public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // Setup does the same as other apps. It writes a Run-key entry and its condition in "Startup apps" in Task Manager
        // (StartupApproved: first byte 02 = on, 03 = off). Thus, you can change it in Task Manager.
        static void SetStartup(bool on)
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                run.SetValue("KeyboardToggler", "\"" + AppExe + "\"");
            var state = new byte[12];
            state[0] = (byte)(on ? 2 : 3);
            if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(state, 4);
            using (var ok = Registry.CurrentUser.CreateSubKey(ApprovedKey))
                ok.SetValue("KeyboardToggler", state, RegistryValueKind.Binary);
        }

        public static void DeleteLogonTask() { string o; RunHidden(SystemTool("schtasks.exe"), "/delete /f /tn KeyboardToggler", out o); }

        // After v1.x (NSIS, device-disable), these items can stay on the PC: two scheduled tasks, files in ProgramData,
        // other files and Start menu .lnk files. If v1 deactivated a keyboard, state.json records it.
        // This method first activates those keyboards again.
        public static void RemoveV1()
        {
            string o;
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KeyboardToggler");
            try
            {
                string state = File.ReadAllText(Path.Combine(data, "state.json"));
                var ids = System.Text.RegularExpressions.Regex.Match(state, @"""DisabledIds""\s*:\s*\[(.*?)\]", System.Text.RegularExpressions.RegexOptions.Singleline);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(ids.Groups[1].Value, @"""((?:[^""\\]|\\.)*)"""))
                    RunHidden(SystemTool("pnputil.exe"), "/enable-device \"" + m.Groups[1].Value.Replace(@"\\", @"\") + "\"", out o);
            }
            catch { }

            DeleteLogonTask(); // The elevated sign-in task of v1 had the same name
            RunHidden(SystemTool("schtasks.exe"), "/delete /f /tn \"KeyboardToggler Restore\"", out o);
            try { Directory.Delete(data, true); } catch { }
            foreach (string f in new[] { "Restore-Keyboard.cmd", "tasks.ps1" })
                try { File.Delete(Path.Combine(Dir, f)); } catch { }
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            try { Directory.Delete(Path.Combine(programs, "Keyboard Toggler"), true); } catch { }
            try { foreach (string lnk in Directory.GetFiles(programs, "Restore*keyboard*.lnk")) File.Delete(lnk); } catch { }
        }

        static int RunHidden(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                output = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static void CreateShortcut(string path, string target, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        // This method starts the app as the signed-in user (not elevated). explorer.exe starts it without administrator rights.
        public static void LaunchApp()
        {
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + AppExe + "\"");
        }

        public static void RestartWindows() { Process.Start(new ProcessStartInfo(SystemTool("shutdown.exe"), "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); }

        public static Icon AppIcon() { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
    }

    // This class is the install wizard. It has one window, and the content of the window changes with the Stage. Install operates on a
    // background thread and sends the progress text to the window through BeginInvoke. You cannot close the window while Install operates.
    // The dimensions are for 96 DPI. Theme.S changes them for other DPI values.
    sealed class InstallForm : FluentForm
    {
        // Error: an exception occurred in Install ("Try again" starts it again). Arm: Windows on Arm. You cannot install Keyboard Toggler on it.
        enum Stage { Welcome, Working, DoneRestart, Done, Error, Arm }
        Stage stage = Stage.Welcome;
        bool upgrade, statusGlyph;
        FluentCheckBox option;   // the check box below the text, if the Stage has one
        int contentHeight;       // it can increase but it cannot decrease, so the buttons stay in the same position at each Stage

        const string SuccessGlyph = "\uE930";   // "Completed": a check mark in a circle. Glyph.Error is an X in a circle.

        readonly ThemedPanel content = new ThemedPanel { Surface = Surface.Layer, TabIndex = 0 };
        readonly ThemedPanel commands = new ThemedPanel { Surface = Surface.Background, TopDivider = true, TabIndex = 1 };
        readonly AppIconView logo = new AppIconView();
        readonly TextBlock title = new TextBlock { Font = Theme.Subtitle, Wrap = false, Text = Setup.AppName };
        readonly TextBlock version = new TextBlock { Font = Theme.Caption, Role = TextRole.Secondary, Wrap = false, Text = "Version " + Setup.Version };
        readonly GlyphIcon statusIcon = new GlyphIcon { Points = 12f };
        readonly TextBlock heading = new TextBlock { Font = Theme.BodyStrong };
        readonly List<TextBlock> paragraphs = new List<TextBlock>();
        readonly FluentCheckBox startup = new FluentCheckBox { Text = "Start Keyboard Toggler when you sign in", Checked = true };
        readonly FluentCheckBox launch = new FluentCheckBox { Text = "Open Keyboard Toggler", Checked = true };
        readonly FluentProgress bar = new FluentProgress();
        readonly FluentButton primary = new FluentButton { Accent = true, TabIndex = 0 };
        readonly FluentButton secondary = new FluentButton { TabIndex = 1 };

        public InstallForm()
        {
            Text = "Keyboard Toggler " + Setup.Version + " Setup";
            Icon = Setup.AppIcon();
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            content.Controls.AddRange(new Control[] { logo, title, version, statusIcon, heading, startup, launch, bar });
            commands.Controls.AddRange(new Control[] { primary, secondary });
            Controls.AddRange(new Control[] { content, commands });
            AcceptButton = primary;
            CancelButton = secondary;
            primary.Click += (s, e) => OnPrimary();
            secondary.Click += (s, e) => OnSecondary();

            bool installed = File.Exists(Setup.AppExe), restartNote = !Setup.DriverInstalled();
            if (Setup.IsArm64()) ShowArm();
            else ShowWelcome(installed, restartNote);
        }

        void OnPrimary()
        {
            switch (stage)
            {
                case Stage.Welcome: case Stage.Error: StartInstall(); break;
                case Stage.DoneRestart: Setup.RestartWindows(); Close(); break;
                case Stage.Done: if (launch.Checked) Setup.LaunchApp(); Close(); break;
            }
        }

        void OnSecondary() { if (stage != Stage.Working) Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (stage == Stage.Working) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateCloseBox();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ActiveControl = (Control)AcceptButton;   // focus starts on the primary button, the same as in a standard dialog
        }

        void StartInstall()
        {
            bool start = startup.Checked;
            ShowWorking();
            var t = new Thread(() =>
            {
                try
                {
                    bool restart = Setup.Install(start, msg => BeginInvoke((Action)(() => ShowProgress(msg))));
                    BeginInvoke((Action)(() => Finish(restart)));
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() => ShowError(ex.Message)));
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---- Stage methods (UI only) ----

        void ShowWelcome(bool upgrade, bool restartNote)
        {
            stage = Stage.Welcome;
            this.upgrade = upgrade;
            SetStatus(null, TextRole.Primary, "", TextRole.Primary);
            SetText(TextRole.Primary, (upgrade ? "Keyboard Toggler is installed on this PC. Setup will update it.\n\n" : "") +
                "Click the tray icon to deactivate or activate the laptop keyboard. Your external keyboard always stays on.\n\n" +
                "Setup installs Keyboard Toggler in " + Setup.Dir + " and adds the Interception driver (open source, LGPL). " +
                "This driver lets Keyboard Toggler deactivate only the laptop keyboard." +
                (restartNote ? "\n\nAfter Setup installs the driver, you must start Windows again." : ""));
            option = startup;
            SetCommands("Install", "Cancel");
        }

        void ShowWorking()
        {
            stage = Stage.Working;
            SetStatus(null, TextRole.Primary, upgrade ? "Setup updates Keyboard Toggler" : "Setup installs Keyboard Toggler", TextRole.Primary);
            SetText(TextRole.Secondary, "");
            option = null;
            primary.Enabled = secondary.Enabled = false;
            Relayout();
        }

        void ShowProgress(string message)
        {
            SetText(TextRole.Secondary, message);
            Relayout();
        }

        void Finish(bool restart)
        {
            SetStatus(SuccessGlyph, TextRole.Success, "Keyboard Toggler is installed", TextRole.Primary);
            if (restart)
            {
                stage = Stage.DoneRestart;
                SetText(TextRole.Primary, "Start Windows again to load the Interception driver. After you sign in, the Keyboard Toggler icon shows in the tray. " +
                    "If you do not see it, click the ^ arrow near the clock.\n\nYou can also open Keyboard Toggler from the Start menu.");
                option = null;
                SetCommands("Start Windows again", "Close");
            }
            else
            {
                stage = Stage.Done;
                SetText(TextRole.Primary, "Keyboard Toggler is in the tray. If you do not see its icon, click the ^ arrow near the clock. " +
                    "You can also open Keyboard Toggler from the Start menu.");
                option = launch;
                SetCommands("Complete", "Close");
            }
        }

        void ShowError(string message)
        {
            stage = Stage.Error;
            SetStatus(Glyph.Error, TextRole.Critical, "Setup cannot complete the installation", TextRole.Critical);
            SetText(TextRole.Primary, message);
            option = null;
            SetCommands("Try again", "Cancel");
        }

        void ShowArm()
        {
            stage = Stage.Arm;
            // The first paragraph of the message is the heading.
            string[] parts = Setup.ArmMessage.Split(new[] { "\n\n" }, 2, StringSplitOptions.None);
            SetStatus(Glyph.Warning, TextRole.Caution, parts[0].TrimEnd('.'), TextRole.Primary);
            SetText(TextRole.Primary, parts.Length > 1 ? parts[1] : "");
            option = null;
            SetCommands(null, "Close");
        }

        // ---- Parts of the UI ----

        // This method sets the heading below the app name. An optional glyph can be before the heading (null = no glyph, empty text = no heading).
        void SetStatus(string glyph, TextRole glyphRole, string text, TextRole textRole)
        {
            statusGlyph = glyph != null;
            if (glyph != null) { statusIcon.Symbol = glyph; statusIcon.Role = glyphRole; }
            heading.Text = text;
            heading.Role = textRole;
        }

        // This method sets the body text. Each "\n\n" starts a new paragraph.
        void SetText(TextRole role, string text)
        {
            string[] parts = text.Replace("\r\n", "\n").Trim('\n').Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) parts = new[] { "" };
            while (paragraphs.Count < parts.Length)
            {
                var p = new TextBlock();
                paragraphs.Add(p);
                content.Controls.Add(p);
            }
            while (paragraphs.Count > parts.Length)
            {
                var p = paragraphs[paragraphs.Count - 1];
                paragraphs.RemoveAt(paragraphs.Count - 1);
                content.Controls.Remove(p);
                p.Dispose();
            }
            for (int i = 0; i < parts.Length; i++)
            {
                paragraphs[i].Text = parts[i].Trim('\n');
                paragraphs[i].Role = role;
            }
        }

        // This method sets the buttons at the right side. first is the primary (accent) button. If first is null, second becomes the primary button.
        void SetCommands(string first, string second)
        {
            primary.Visible = first != null;
            if (first != null) primary.Text = first;
            secondary.Text = second;
            secondary.Accent = first == null;
            AcceptButton = first != null ? primary : secondary;
            primary.Enabled = secondary.Enabled = true;
            Relayout();
            if (IsHandleCreated) ActiveControl = first != null ? primary : secondary;
        }

        void Relayout()
        {
            int pad = Theme.S(24), width = Theme.S(520), inner = width - 2 * pad;
            content.SuspendLayout();

            // Header: the app icon, with the name and the version adjacent to it.
            logo.Bounds = new Rectangle(pad, pad, Theme.S(48), Theme.S(48));
            int tx = logo.Right + Theme.S(16);
            title.Width = version.Width = width - tx - pad;
            title.FitHeight();
            version.FitHeight();
            title.Location = new Point(tx, logo.Top + (logo.Height - title.Height - version.Height) / 2);
            version.Location = new Point(tx, title.Bottom);
            int y = logo.Bottom + pad;

            bool hasHeading = heading.Text.Length > 0;
            heading.Visible = hasHeading;
            statusIcon.Visible = hasHeading && statusGlyph;
            if (hasHeading)
            {
                int hx = statusGlyph ? pad + Theme.S(28) : pad;
                heading.SetBounds(hx, y, width - hx - pad, 1);
                heading.FitHeight();
                int line = TextRenderer.MeasureText("Ag", heading.Font, Size.Empty, TextFormatFlags.NoPadding).Height;
                statusIcon.Bounds = new Rectangle(pad, y, Theme.S(20), line);
                y = heading.Bottom + Theme.S(4);
            }
            foreach (var p in paragraphs)
            {
                p.SetBounds(pad, y, inner, 1);
                p.FitHeight();
                y = p.Bottom + Theme.S(12);
            }

            startup.Visible = option == startup;
            launch.Visible = option == launch;
            bar.Visible = stage == Stage.Working;
            bar.Indeterminate = stage == Stage.Working;
            if (option != null)
            {
                // The box is 2 px from the edge of the control. This gives space for the focus ring. Align the box with the text.
                int w = Theme.S(34) + TextRenderer.MeasureText(option.Text, option.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                option.Bounds = new Rectangle(pad - Theme.S(2), y, w, Theme.S(32));
                y = option.Bottom;
            }
            else if (stage == Stage.Working)
            {
                bar.Bounds = new Rectangle(pad, y, inner, Theme.S(4));
                y = bar.Bottom;
            }
            else y -= Theme.S(12);

            contentHeight = Math.Max(contentHeight, y + pad);
            content.Bounds = new Rectangle(0, 0, width, contentHeight);
            content.ResumeLayout(false);

            // Command area: buttons with equal width, aligned on the right side.
            int bw = Math.Max(Theme.S(120), Math.Max(primary.PreferredWidth(0), secondary.PreferredWidth(0))), bh = Theme.S(32);
            commands.Bounds = new Rectangle(0, contentHeight, width, pad + bh + pad);
            secondary.Bounds = new Rectangle(width - pad - bw, pad, bw, bh);
            primary.Bounds = new Rectangle(secondary.Left - Theme.S(8) - bw, pad, bw, bh);

            var size = new Size(width, commands.Bottom);
            if (ClientSize != size)
            {
                ClientSize = size;
                if (IsHandleCreated)
                {
                    // If the window goes below the bottom of the screen, move it up.
                    Rectangle area = Screen.FromHandle(Handle).WorkingArea;
                    if (Bottom > area.Bottom && Top >= area.Top) Top = Math.Max(area.Top, area.Bottom - Height);
                }
            }
            UpdateCloseBox();
        }

        // This method deactivates the close button on the title bar while Setup operates. At that time, you also cannot close the window, because OnFormClosing cancels it.
        void UpdateCloseBox()
        {
            if (IsHandleCreated) EnableMenuItem(GetSystemMenu(Handle, false), 0xF060 /*SC_CLOSE*/, stage == Stage.Working ? 1u /*MF_GRAYED*/ : 0u);
        }

        [DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
        [DllImport("user32.dll")] static extern int EnableMenuItem(IntPtr menu, uint item, uint flags);
    }

    // This control shows the icon of the app at the correct dimension. Windows selects the applicable image in the icon of the exe file
    // (Setup.AppIcon() gives only the 32 px system dimension). OnPaint first fills the area with the color of the parent,
    // the same as the controls in the UI kit.
    sealed class AppIconView : Control
    {
        Icon icon;
        int loaded;

        public AppIconView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            int size = Math.Min(Width, Height);
            if (size <= 0) return;
            if (icon == null || loaded != size)
            {
                if (icon != null) icon.Dispose();
                icon = Load(size);
                loaded = size;
            }
            e.Graphics.DrawIcon(icon, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
        }

        static Icon Load(int size)
        {
            try
            {
                IntPtr h;
                if (SHDefExtractIcon(Application.ExecutablePath, 0, 0, out h, IntPtr.Zero, (uint)size) == 0 && h != IntPtr.Zero)
                {
                    try { using (var shared = Icon.FromHandle(h)) return (Icon)shared.Clone(); }
                    finally { DestroyIcon(h); }
                }
            }
            catch (EntryPointNotFoundException) { }
            return Setup.AppIcon();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && icon != null) { icon.Dispose(); icon = null; }
            base.Dispose(disposing);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, IntPtr small, uint size);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }

    static class Uninstaller
    {
        public static void Run(string[] args)
        {
            bool quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
            string self = Application.ExecutablePath;

            // This exe cannot delete the install folder while it operates from that folder. Thus, it starts a temporary copy of the exe.
            if (self.StartsWith(Setup.Dir, StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "KeyboardToggler-Uninstall.exe");
                File.Copy(self, tmp, true);
                Process.Start(tmp, string.Join(" ", args));
                return;
            }

            if (!quiet && Notice.Show(null, NoticeKind.Question, "Do you want to remove Keyboard Toggler?",
                    "This removes Keyboard Toggler and the Interception driver.", "Remove", "Cancel") != 0)
                return;

            Setup.StopApp();
            using (var k = Registry.CurrentUser.OpenSubKey(Setup.RunKey, true)) if (k != null) k.DeleteValue("KeyboardToggler", false);
            using (var k = Registry.CurrentUser.OpenSubKey(Setup.ApprovedKey, true)) if (k != null) k.DeleteValue("KeyboardToggler", false);
            Setup.RemoveV1();

            bool driverRemoved = false;
            string tool = Path.Combine(Setup.Dir, "install-interception.exe");
            if (Setup.DriverInstalled() && File.Exists(tool))
            {
                Setup.RunDriverTool(tool, "/uninstall");
                driverRemoved = true;
            }

            try { File.Delete(Setup.Shortcut); } catch { }
            Registry.LocalMachine.DeleteSubKeyTree(Setup.UninstallKey, false);
            for (int i = 0; i < 5 && Directory.Exists(Setup.Dir); i++)
            {
                try { Directory.Delete(Setup.Dir, true); } catch { Thread.Sleep(500); }
            }
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardToggler"), true); } catch { }

            // Delete this temporary copy after this exe stops.
            // cmd first examines its working folder for "ping". Thus, start cmd in System32.
            Process.Start(new ProcessStartInfo(Setup.SystemTool("cmd.exe"), "/c ping -n 3 127.0.0.1 >nul & del \"" + self + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory });

            if (quiet) return;
            if (driverRemoved)
            {
                if (Notice.Show(null, NoticeKind.Info, "Keyboard Toggler is removed",
                        "Start Windows again to complete the removal of the Interception driver.", "Start Windows again", "Close") == 0)
                    Setup.RestartWindows();
            }
            else Notice.Show(null, NoticeKind.Info, "Keyboard Toggler is removed", "It is not necessary to start Windows again.", "OK");
        }
    }
}
