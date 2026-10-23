// Keyboard Toggler installer / uninstaller. The app files are embedded as resources.
//   KeyboardToggler-Setup.exe              -> install wizard
//   Uninstall.exe /uninstall               -> remove app + Interception driver (copied into the install folder)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Keyboard Toggler Setup")]
[assembly: AssemblyProduct("Keyboard Toggler")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]

namespace KeyboardTogglerSetup
{
    static class Setup
    {
        public const string AppName = "Keyboard Toggler", Version = "2.2.0";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyboardToggler";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KeyboardToggler");
        public static readonly string AppExe = Path.Combine(Dir, "KeyboardToggler.exe");
        public static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Keyboard Toggler.lnk");
        static readonly string[] Payload = { "KeyboardToggler.exe", "interception.dll", "install-interception.exe", "Interception-LGPL-3.0.txt" };

        public const string ArmMessage = "Keyboard Toggler can't be installed on Windows on Arm.\n\n" +
            "The keyboard driver it relies on (Interception) only exists for Intel and AMD (x64) PCs. It can't load on this PC, " +
            "so after the restart every keyboard and mouse would stop working.";

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        // Windows on Arm runs this x64 setup emulated, and the driver's installer would then register its x64 keyboard and
        // mouse filters, which an Arm64 Windows can't load.
        public static bool IsArm64()
        {
            try
            {
                ushort process, native;
                return IsWow64Process2(GetCurrentProcess(), out process, out native) && native == 0xAA64; // IMAGE_FILE_MACHINE_ARM64
            }
            catch (EntryPointNotFoundException) { return false; }   // Windows 10 before 1709, which can't run x64 apps on Arm anyway
        }

        // Windows tools are started by full path: given a bare name, Windows looks in the setup's own folder
        // (e.g. Downloads) first, so a planted schtasks.exe there would run with admin rights.
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

        // Runs install-interception.exe with /install or /uninstall; returns its output.
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

        // Returns true when Windows must restart before the driver is active.
        public static bool Install(bool startWithWindows, Action<string> progress)
        {
            if (IsArm64()) throw new Exception(ArmMessage);

            progress("Closing Keyboard Toggler if it's running...");
            StopApp();

            progress("Removing leftovers from older versions...");
            RemoveV1();

            progress("Copying files...");
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
                progress("Installing the Interception keyboard driver...");
                string output = RunDriverTool(Path.Combine(Dir, "install-interception.exe"), "/install");
                if (!DriverInstalled()) throw new Exception("The Interception driver didn't install:\n\n" + output);
                restart = true;
            }

            progress("Creating Start menu shortcut...");
            CreateShortcut(Shortcut, AppExe, "Switch the laptop keyboard off while using an external keyboard");

            progress("Registering with Windows...");
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

            progress("Setting up start at sign-in...");
            SetStartup(startWithWindows);
            return restart;
        }

        public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        // Same as other apps: a Run-key entry plus its Task Manager "Startup apps" state
        // (StartupApproved; first byte 02 = enabled, 03 = disabled), so it can be switched in Task Manager.
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

        // v1.x (NSIS, device-disable) left: two scheduled tasks, ProgramData state/logs, extra files and shortcuts.
        // If v1 ever left a keyboard disabled, its state file lists it; switch those back on first.
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

            DeleteLogonTask(); // v1's elevated sign-in task had the same name
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

        // Start the app as the signed-in user (not elevated): explorer.exe launches it with normal rights.
        public static void LaunchApp()
        {
            Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + AppExe + "\"");
        }

        public static void RestartWindows() { Process.Start(new ProcessStartInfo(SystemTool("shutdown.exe"), "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); }

        public static Icon AppIcon() { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
    }

    sealed class InstallForm : Form
    {
        readonly Label body = new Label { AutoSize = false };
        readonly CheckBox startup = new CheckBox { Text = "Start Keyboard Toggler when I sign in", Checked = true, AutoSize = true };
        readonly CheckBox launch = new CheckBox { Text = "Open Keyboard Toggler now", Checked = true, AutoSize = true, Visible = false };
        readonly ProgressBar bar = new ProgressBar { Style = ProgressBarStyle.Marquee, Visible = false, Height = 16 };
        readonly Button primary = new Button { Text = "Install", Width = 110, Height = 30 };
        readonly Button secondary = new Button { Text = "Cancel", Width = 110, Height = 30 };
        enum Stage { Welcome, Working, DoneRestart, Done }
        Stage stage = Stage.Welcome;

        public InstallForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Keyboard Toggler " + Setup.Version + " Setup";
            Icon = Setup.AppIcon();
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 380);
            BackColor = SystemColors.Window;

            var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.FromArgb(46, 160, 67) };
            var logo = new PictureBox { Image = new Icon(Icon, 48, 48).ToBitmap(), SizeMode = PictureBoxSizeMode.CenterImage, Bounds = new Rectangle(20, 18, 48, 48), BackColor = Color.White };
            var title = new Label { Text = "Keyboard Toggler", ForeColor = Color.White, Font = new Font(Font.FontFamily, 16f, FontStyle.Bold), AutoSize = true, Location = new Point(84, 16) };
            var subtitle = new Label { Text = "Switch your laptop keyboard off while you use an external one.", ForeColor = Color.White, AutoSize = true, Location = new Point(86, 50) };
            header.Controls.AddRange(new Control[] { logo, title, subtitle });

            body.Bounds = new Rectangle(24, 104, 512, 150);
            startup.Location = new Point(24, 262);
            launch.Location = new Point(24, 262);
            bar.Bounds = new Rectangle(24, 266, 512, 16);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = SystemColors.Control };
            secondary.Location = new Point(ClientSize.Width - 24 - secondary.Width, 13);
            primary.Location = new Point(secondary.Left - 10 - primary.Width, 13);
            footer.Controls.AddRange(new Control[] { primary, secondary });

            Controls.AddRange(new Control[] { body, startup, launch, bar, footer, header });
            AcceptButton = primary;
            primary.Click += (s, e) => OnPrimary();
            secondary.Click += (s, e) => OnSecondary();

            bool upgrade = File.Exists(Setup.AppExe);
            body.Text = (upgrade ? "Keyboard Toggler is already installed. Setup will update it.\n\n" : "") +
                "Left-click the tray icon to switch the built-in keyboard off or on. Your external keyboard always keeps working.\n\n" +
                "Setup installs to " + Setup.Dir + " and adds the Interception keyboard driver (free, LGPL) " +
                "that lets it block only the laptop keyboard." +
                (Setup.DriverInstalled() ? "" : "\n\nWindows needs one restart after installing the driver.");
            if (Setup.IsArm64())
            {
                body.Text = Setup.ArmMessage;
                startup.Visible = false;
                primary.Visible = false;
                secondary.Text = "Close";
                AcceptButton = secondary;
            }
            ResumeLayout(false);
        }

        void OnPrimary()
        {
            switch (stage)
            {
                case Stage.Welcome: StartInstall(); break;
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

        void StartInstall()
        {
            stage = Stage.Working;
            startup.Visible = false; bar.Visible = true;
            primary.Enabled = secondary.Enabled = false;
            bool start = startup.Checked;
            var t = new Thread(() =>
            {
                try
                {
                    bool restart = Setup.Install(start, msg => BeginInvoke((Action)(() => body.Text = msg)));
                    BeginInvoke((Action)(() => Finish(restart)));
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() =>
                    {
                        stage = Stage.Welcome; bar.Visible = false;
                        body.Text = "Setup couldn't finish:\n\n" + ex.Message;
                        primary.Text = "Try again"; primary.Enabled = secondary.Enabled = true;
                    }));
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Finish(bool restart)
        {
            bar.Visible = false;
            primary.Enabled = secondary.Enabled = true;
            if (restart)
            {
                stage = Stage.DoneRestart;
                body.Text = "Keyboard Toggler is installed.\n\nRestart Windows to load the keyboard driver. " +
                    "After you sign in, Keyboard Toggler appears in the tray (click the ^ arrow next to the clock if you don't see it).\n\n" +
                    "You can also open it from the Start menu.";
                primary.Text = "Restart now"; secondary.Text = "Restart later";
            }
            else
            {
                stage = Stage.Done;
                body.Text = "Keyboard Toggler is installed.\n\nIt lives in the tray: click the ^ arrow next to the clock if you don't see it. " +
                    "You can also open it from the Start menu.";
                launch.Visible = true;
                primary.Text = "Finish"; secondary.Text = "Close";
            }
        }
    }

    static class Uninstaller
    {
        public static void Run(string[] args)
        {
            bool quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
            string self = Application.ExecutablePath;

            // Can't delete the install folder while running from it: hand off to a temp copy.
            if (self.StartsWith(Setup.Dir, StringComparison.OrdinalIgnoreCase))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "KeyboardToggler-Uninstall.exe");
                File.Copy(self, tmp, true);
                Process.Start(tmp, string.Join(" ", args));
                return;
            }

            if (!quiet && MessageBox.Show("Remove Keyboard Toggler and its Interception keyboard driver?", "Uninstall Keyboard Toggler",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
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

            // Delete this temp copy once we've exited.
            // cmd looks for "ping" in its working folder first, so run it from System32.
            Process.Start(new ProcessStartInfo(Setup.SystemTool("cmd.exe"), "/c ping -n 3 127.0.0.1 >nul & del \"" + self + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory });

            if (quiet) return;
            if (driverRemoved)
            {
                if (MessageBox.Show("Keyboard Toggler has been removed.\n\nRestart Windows now to finish removing the keyboard driver?",
                        "Uninstall Keyboard Toggler", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Setup.RestartWindows();
            }
            else MessageBox.Show("Keyboard Toggler has been removed.", "Uninstall Keyboard Toggler", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
