using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // Shown right after the laptop keyboard is switched off: a Windows 11-style notification card in the corner by the
    // notification area. If nobody answers within the timeout, the keyboard comes back on by itself (a safety net).
    // The focus starts on "Keep it off", so Enter or Space keeps it off; Esc turns it back on.
    sealed class KeepOffPrompt : FluentForm
    {
        public bool Kept;                                       // closed with the keyboard kept off
        public bool TimedOut { get { return timedOut; } }       // closed because nobody answered in time

        readonly int seconds;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 30 };     // ~30 updates a second, so the bar shrinks smoothly (33 ms would round up to 47 ms)
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
            var title = new TextBlock { Font = Theme.BodyStrong, Text = "Laptop keyboard is off", Location = new Point(indent, pad), Width = width - indent - pad };
            title.FitHeight();
            secondsShown = this.seconds;
            body = new TextBlock { Role = TextRole.Secondary, Text = Countdown(secondsShown), Location = new Point(indent, title.Bottom + Theme.S(4)), Width = title.Width };
            body.FitHeight();
            glyph = new Rectangle(pad, pad + (body.Bottom - pad - icon) / 2, icon, icon);   // centred on the text

            bar = new FluentProgress { Value = 1f, Location = new Point(pad, body.Bottom + Theme.S(12)), Width = width - 2 * pad };

            int gap = Theme.S(8), half = (width - 2 * pad - gap) / 2, top = bar.Bottom + Theme.S(16);
            var keep = new FluentButton { Text = "Keep it off", Accent = true, Bounds = new Rectangle(pad, top, half, Theme.S(32)), TabIndex = 0 };
            var back = new FluentButton { Text = "Turn back on", Bounds = new Rectangle(pad + half + gap, top, width - 2 * pad - half - gap, Theme.S(32)), TabIndex = 1 };
            keep.Click += (s, e) => { Kept = true; Close(); };
            back.Click += (s, e) => { Kept = false; Close(); };

            Controls.AddRange(new Control[] { title, body, bar, keep, back });
            AcceptButton = keep;    // Enter
            CancelButton = back;    // Esc
            ActiveControl = keep;   // the focused button is the one Enter and Space click, so it must be "Keep it off"
            ClientSize = new Size(width, keep.Bottom + pad);
            Location = Corner(Screen.PrimaryScreen.Bounds, Screen.PrimaryScreen.WorkingArea, Size);

            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        protected override bool IsPopup { get { return true; } }

        // Always two lines, broken in the same place, so the text doesn't re-wrap as the number gets shorter.
        static string Countdown(int left) { return "It turns back on in " + left + " s\nunless you keep it off."; }

        // Where the card goes: 12 px in from the screen corner next to the notification area, which sits at the right
        // end of a bottom or top taskbar and at the bottom end of a side taskbar.
        internal static Point Corner(Rectangle screen, Rectangle work, Size size)
        {
            int margin = Theme.S(12);
            int x = work.Right - size.Width - margin, y = work.Bottom - size.Height - margin;   // taskbar at the bottom or right, or hidden
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
            base.OnHandleCreated(e);   // Windows 11 rounds the corners and draws a themed outline
            // Windows 10 can do neither, so draw a square outline there: otherwise a light card melts into a light window behind it.
            int round = 2;   // DWMWCP_ROUND
            try { outline = DwmSetWindowAttribute(Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4) != 0; }
            catch (DllNotFoundException) { outline = true; }
            catch (EntryPointNotFoundException) { outline = true; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();   // take the focus, so Enter and Space work straight away
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            KeyboardGlyph.Draw(e.Graphics, glyph, Theme.Text, GlyphBadge.Off);   // the tray icon's "off" look
            if (outline)
                using (var p = new Pen(Theme.Divider)) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void ApplyTheme()
        {
            base.ApplyTheme();
            if (!IsDisposed) BackColor = Theme.Layer;   // the whole card is on the layer colour, not the window background
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();   // closed by a button, Esc, the timeout or the app: no more ticks, so no second close
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
