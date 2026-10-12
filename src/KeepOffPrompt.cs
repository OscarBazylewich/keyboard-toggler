using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardToggler
{
    // Shown once, right after switching off. If nobody answers within the timeout, the keyboard comes back on.
    sealed class KeepOffPrompt : Form
    {
        public bool Kept, TimedOut;
        int left;
        readonly Label text = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0) };
        readonly Timer timer = new Timer { Interval = 1000 };

        public KeepOffPrompt(int seconds)
        {
            left = seconds;
            Text = "Keyboard Toggler";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            TopMost = true; ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(380, 120);
            Font = SystemFonts.MessageBoxFont;

            var keep = new Button { Text = "Keep it off", AutoSize = true, DialogResult = DialogResult.OK };
            var back = new Button { Text = "Turn back on", AutoSize = true };
            keep.Click += (s, e) => { Kept = true; Close(); };
            back.Click += (s, e) => Close();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 42, Padding = new Padding(8) };
            buttons.Controls.Add(back); buttons.Controls.Add(keep);
            Controls.Add(text); Controls.Add(buttons);
            AcceptButton = keep;

            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

            timer.Tick += (s, e) => { left--; UpdateText(); if (left <= 0) { TimedOut = true; Close(); } };
            UpdateText();
            timer.Start();
        }

        void UpdateText() { text.Text = "The laptop keyboard is off. Keep it off?\n\nTurning back on automatically in " + left + " s."; }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Dispose(); base.OnFormClosed(e); }
    }
}
