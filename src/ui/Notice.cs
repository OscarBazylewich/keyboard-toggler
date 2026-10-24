using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardToggler.Ui
{
    enum NoticeKind { Info, Warning, Error, Question }

    // Windows 11-style message dialog, used instead of MessageBox so every dialog shares the app's look.
    sealed class Notice : FluentForm
    {
        int choice = -1;

        // Shows the dialog and returns the index of the button clicked (0 = the first, accent button), or -1 if
        // it was closed with the title bar's close button. Enter clicks the first button, Esc the last one.
        public static int Show(IWin32Window owner, NoticeKind kind, string title, string text, params string[] buttons)
        {
            using (var n = new Notice(kind, title, text, buttons == null || buttons.Length == 0 ? new[] { "OK" } : buttons))
            {
                n.ShowDialog(owner);
                return n.choice;
            }
        }

        Notice(NoticeKind kind, string title, string text, string[] buttons)
        {
            Text = "Keyboard Toggler";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;

            int width = Theme.S(440), pad = Theme.S(24), iconW = Theme.S(28), indent = pad + iconW + Theme.S(12);
            var content = new ThemedPanel { Surface = Surface.Layer, Location = Point.Empty, Width = width };
            var icon = new GlyphIcon
            {
                Symbol = kind == NoticeKind.Error ? Glyph.Error : kind == NoticeKind.Warning ? Glyph.Warning : Glyph.Info,
                Role = kind == NoticeKind.Error ? TextRole.Critical : kind == NoticeKind.Warning ? TextRole.Caution : TextRole.Accent,
                Points = 16f,
                Bounds = new Rectangle(pad, pad, iconW, Theme.S(28)),
            };
            var heading = new TextBlock { Font = Theme.Subtitle, Text = title, Location = new Point(indent, pad), Width = width - indent - pad };
            heading.FitHeight();
            var body = new TextBlock { Text = text, Location = new Point(indent, heading.Bottom + Theme.S(8)), Width = width - indent - pad };
            body.FitHeight();
            content.Height = body.Bottom + pad;
            content.Controls.AddRange(new Control[] { icon, heading, body });

            var footer = new ThemedPanel { Surface = Surface.Background, TopDivider = true, Location = new Point(0, content.Height), Size = new Size(width, Theme.S(80)) };
            int gap = Theme.S(8), n = buttons.Length, avail = width - 2 * pad;
            int bw = n == 1 ? (avail - gap) / 2 : (avail - (n - 1) * gap) / n;
            int x = width - pad - (n * bw + (n - 1) * gap);
            var made = new FluentButton[n];
            for (int i = 0; i < n; i++)
            {
                int index = i;
                made[i] = new FluentButton { Text = buttons[i], Accent = i == 0, Bounds = new Rectangle(x + i * (bw + gap), pad, bw, Theme.S(32)), TabIndex = i };
                made[i].Click += (s, e) => { choice = index; DialogResult = DialogResult.OK; };
            }
            footer.Controls.AddRange(made);
            AcceptButton = made[0];
            CancelButton = made[n - 1];

            Controls.Add(content);
            Controls.Add(footer);
            ClientSize = new Size(width, content.Height + footer.Height);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }
    }
}
