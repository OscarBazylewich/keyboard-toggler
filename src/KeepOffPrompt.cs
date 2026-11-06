using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // Keyboard Toggler shows this card immediately after it deactivates the laptop keyboard. The card has the same shape
    // as a Windows 11 notification and shows in the screen corner near the notification area. If you do not select a
    // button before the time limit, Keyboard Toggler activates the laptop keyboard again. This is a safety function.
    // The focus starts on "Keep it off". Enter or Space keeps the laptop keyboard off. Esc activates it again.
    sealed class KeepOffPrompt : FluentForm
    {
        public bool Kept;                                       // Kept is true if you selected "Keep it off".
        public bool TimedOut { get { return timedOut; } }       // TimedOut is true if you did not select a button before the time limit.

        readonly int seconds;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 30 };     // The Tick event occurs almost 30 times each second. Thus, the bar becomes shorter in small steps. If the interval is 33 ms, Windows increases it to 47 ms.
        readonly TextBlock body;
        readonly FluentProgress bar;
        readonly Rectangle glyph;
        bool timedOut, outline;
        int secondsShown;

        public KeepOffPrompt(int seconds)
        {
            this.seconds = Math.Max(1, seconds);
            Text = "Keyboard Toggler";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Layer;

            int width = Theme.S(360), pad = Theme.S(16), icon = Theme.S(32), indent = pad + icon + Theme.S(12);
            var title = new TextBlock { Font = Theme.BodyStrong, Text = "The laptop keyboard is off", Location = new Point(indent, pad), Width = width - indent - pad };
            title.FitHeight();
            secondsShown = this.seconds;
            body = new TextBlock { Role = TextRole.Secondary, Text = Countdown(secondsShown), Location = new Point(indent, title.Bottom + Theme.S(4)), Width = title.Width };
            body.FitHeight();
            glyph = new Rectangle(pad, pad + (body.Bottom - pad - icon) / 2, icon, icon);   // The symbol is vertically in the middle of the text.

            bar = new FluentProgress { Value = 1f, Location = new Point(pad, body.Bottom + Theme.S(12)), Width = width - 2 * pad };

            int gap = Theme.S(8), half = (width - 2 * pad - gap) / 2, top = bar.Bottom + Theme.S(16);
            var keep = new FluentButton { Text = "Keep it off", Accent = true, Bounds = new Rectangle(pad, top, half, Theme.S(32)), TabIndex = 0 };
            var back = new FluentButton { Text = "Activate it", Bounds = new Rectangle(pad + half + gap, top, width - 2 * pad - half - gap, Theme.S(32)), TabIndex = 1 };
            keep.Click += (s, e) => { Kept = true; Close(); };
            back.Click += (s, e) => { Kept = false; Close(); };

            Controls.AddRange(new Control[] { title, body, bar, keep, back });
            AcceptButton = keep;    // Enter
            CancelButton = back;    // Esc
            ActiveControl = keep;   // Enter and Space operate the button that has the focus. Thus, the focus must be on "Keep it off".
            ClientSize = new Size(width, keep.Bottom + pad);
            Location = Corner(Screen.PrimaryScreen.Bounds, Screen.PrimaryScreen.WorkingArea, Size);

            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        protected override bool IsPopup { get { return true; } }

        // The text always has two lines, with the line break in the same position. Thus, the text does not move when the
        // number becomes shorter.
        static string Countdown(int left) { return "It will be on again in " + left + " s\nunless you keep it off."; }

        // The card is 12 px from the screen corner near the notification area. The notification area is at
        // the right end of a taskbar at the bottom or top, and at the bottom end of a taskbar on a side.
        internal static Point Corner(Rectangle screen, Rectangle work, Size size)
        {
            int margin = Theme.S(12);
            int x = work.Right - size.Width - margin, y = work.Bottom - size.Height - margin;   // taskbar at the bottom or on the right, or Windows does not show the taskbar
            if (work.Bottom >= screen.Bottom)
            {
                if (work.Top > screen.Top) y = work.Top + margin;               // taskbar at the top
                else if (work.Left > screen.Left) x = work.Left + margin;       // taskbar on the left
            }
            return new Point(x, y);
        }

        void Tick()
        {
            if (IsDisposed) return;
            double left = seconds - clock.Elapsed.TotalSeconds;
            if (left <= 0)
            {
                timer.Stop();
                timedOut = true;
                Close();
                return;
            }
            bar.Value = (float)(left / seconds);
            int whole = (int)Math.Ceiling(left);
            if (whole != secondsShown)
            {
                secondsShown = whole;
                body.Text = Countdown(whole);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);   // Windows 11 shows the card with rounded corners and a line in the theme color along the edges.
            // Windows 10 cannot do this. Thus, Keyboard Toggler adds a line along the edges of the card on Windows 10.
            // Without this line, it is not easy to see the card on a window behind it that has almost the same color.
            int round = 2;   // DWMWCP_ROUND
            try { outline = DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4) != 0; }
            catch (DllNotFoundException) { outline = true; }
            catch (EntryPointNotFoundException) { outline = true; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();   // The card gets the focus. Then Enter and Space operate immediately.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            KeyboardGlyph.Draw(e.Graphics, glyph, Theme.Text, GlyphBadge.Off);   // This is the same symbol that the tray icon shows when the laptop keyboard is off.
            if (outline)
                using (var p = new Pen(Theme.Divider)) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void ApplyTheme()
        {
            base.ApplyTheme();
            if (!IsDisposed) BackColor = Theme.Layer;   // The card has only the Theme.Layer color, not the Theme.Background color.
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();   // A button, Esc, the time limit or the app closed the card. Stop the timer. Then no Tick event closes the card again.
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
