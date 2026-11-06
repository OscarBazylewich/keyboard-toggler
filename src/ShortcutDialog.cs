using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // The "Change the shortcut" window. tryUse tries to use the new shortcut.
    // tryUse gives false if Windows or a different app uses that shortcut.
    sealed class ShortcutDialog : FluentForm
    {
        public Hotkey Result { get; private set; }
        readonly Func<Hotkey, bool> tryUse;
        Hotkey candidate;

        readonly Keycaps keycaps = new Keycaps();
        readonly GlyphIcon warning = new GlyphIcon { Symbol = Glyph.Warning, Role = TextRole.Critical, Points = 10.5f, Visible = false };
        readonly TextBlock status = new TextBlock();
        readonly FluentButton save = new FluentButton { Text = "Save", Accent = true, Enabled = false, TabStop = false };
        readonly Rectangle statusArea;   // The status area is below the keys. Its height is two lines.
        readonly int lineHeight;

        const TextFormatFlags StatusFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public ShortcutDialog(Hotkey current, Func<Hotkey, bool> tryUse)
        {
            this.tryUse = tryUse;
            Text = "Change the shortcut";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowIcon = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;

            int width = Theme.S(440), pad = Theme.S(24), avail = width - 2 * pad;
            var content = new ThemedPanel { Surface = Surface.Layer, Location = Point.Empty, Width = width };
            var heading = new TextBlock { Font = Theme.Subtitle, Text = "Type a new shortcut", Location = new Point(pad, pad), Width = avail };
            heading.FitHeight();
            var intro = new TextBlock
            {
                Role = TextRole.Secondary,
                Text = "Use Ctrl, Alt or Win plus one more key. The shortcut deactivates or activates the laptop keyboard. You can also type it on the laptop keyboard when that keyboard is off.",
                Location = new Point(pad, heading.Bottom + Theme.S(8)),
                Width = avail,
            };
            intro.FitHeight();
            keycaps.Bounds = new Rectangle(pad, intro.Bottom + Theme.S(24), avail, Theme.S(40));
            // The status area has space for two lines. Thus the window size does not change when a message uses two lines.
            lineHeight = TextRenderer.MeasureText(" ", Theme.Body, Size.Empty, StatusFlags).Height;
            statusArea = new Rectangle(pad, keycaps.Bottom + Theme.S(12), avail, 2 * lineHeight);
            content.Height = statusArea.Bottom + pad;
            content.Controls.AddRange(new Control[] { heading, intro, keycaps, warning, status });

            // The buttons do not get the focus (TabStop is false). Thus Space, Enter and other keys go to the shortcut.
            // These keys do not push a button.
            var footer = new ThemedPanel { Surface = Surface.Background, TopDivider = true, Location = new Point(0, content.Height), Size = new Size(width, Theme.S(80)) };
            var reset = new FluentButton { Text = "Set to standard", TabStop = false };
            var cancel = new FluentButton { Text = "Cancel", DialogResult = DialogResult.Cancel, TabStop = false };
            // The maximum width of each button is 1/3 of the row. Save and Cancel have the same width.
            // The "Set to standard" button is as wide as its text. There is a wider space between it and Save.
            int gap = Theme.S(8), apart = Theme.S(24), height = Theme.S(32), third = (avail - apart - gap) / 3;
            int resetW = reset.PreferredWidth(third), bw = Math.Min(third, (avail - resetW - apart - gap) / 2);
            reset.Bounds = new Rectangle(pad, pad, resetW, height);
            cancel.Bounds = new Rectangle(width - pad - bw, pad, bw, height);
            save.Bounds = new Rectangle(cancel.Left - gap - bw, pad, bw, height);
            reset.Click += (s, e) => Pick(Hotkey.Default);
            save.Click += (s, e) => Commit();
            footer.Controls.AddRange(new Control[] { reset, save, cancel });

            Controls.Add(content);
            Controls.Add(footer);
            ClientSize = new Size(width, content.Height + footer.Height);

            keycaps.SetKeys(current.KeyLabels(), false, current.ToString());
            SetStatus("This is your shortcut", false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        void Pick(Hotkey h)
        {
            candidate = h;
            keycaps.SetKeys(h.KeyLabels(), false, h.ToString());
            SetStatus("Click Save to use this shortcut", false);
            save.Enabled = true;
        }

        void Commit()
        {
            if (candidate == null) return;
            if (!tryUse(candidate))
            {
                SetStatus(candidate + " is not available because Windows or a different app uses it. Try a different shortcut.", true);
                save.Enabled = false;
                return;
            }
            Result = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }

        // Shows the text below the keys. Usual text has the Secondary role. Error text has the Critical role and a
        // warning glyph before it. The glyph and the text are at the center as one block. If the text uses two lines,
        // the text width is the minimum width for two lines. Thus the two lines have almost the same length.
        void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.Role = error ? TextRole.Critical : TextRole.Secondary;
            warning.Visible = error;
            int lead = error ? Theme.S(24) : 0;   // 16 px for the glyph and 8 px for the space
            int max = statusArea.Width - lead, textH = MeasureStatus(text, max).Height;
            int lo = 1, hi = max;   // Find the minimum width that keeps this text height.
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (MeasureStatus(text, mid).Height <= textH) hi = mid; else lo = mid + 1;
            }
            int x = statusArea.X + (max - lo) / 2;
            warning.Bounds = new Rectangle(x, statusArea.Y, Theme.S(16), lineHeight);
            status.Bounds = new Rectangle(x + lead, statusArea.Y, lo, textH);
        }

        static Size MeasureStatus(string text, int width)
        {
            return TextRenderer.MeasureText(text, Theme.Body, new Size(width, int.MaxValue), StatusFlags);
        }

        // Examines one key that you push. ProcessCmdKey sends all keys to this method.
        // winDown shows if you hold a Win key. keyData cannot show this.
        internal void HandleKey(Keys keyData, bool winDown)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape && (keyData & Keys.Modifiers) == 0) { Close(); return; }

            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Hotkey.Ctrl;
            if ((keyData & Keys.Alt) != 0) mods |= Hotkey.Alt;
            if ((keyData & Keys.Shift) != 0) mods |= Hotkey.Shift;
            if (winDown) mods |= Hotkey.Win;

            if (Hotkey.IsModifierKey(key))
            {
                // Show the modifier keys while you hold them. Do not accept only modifier keys.
                string held = Hotkey.ModsText(mods);
                keycaps.SetKeys(held.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries), true, held.Length == 0 ? "..." : held + " + ...");
                SetStatus((mods & (Hotkey.Ctrl | Hotkey.Alt | Hotkey.Win)) != 0 ? "Push one more key" : "Also push Ctrl, Alt or Win", false);
                save.Enabled = false;   // A new key combination started. You cannot click Save until you complete it.
                candidate = null;
                return;
            }

            var h = new Hotkey(mods, (uint)key);
            if (!h.IsValid)
            {
                keycaps.SetKeys(h.KeyLabels(), false, h.ToString());
                SetStatus("Add Ctrl, Alt or Win to prevent accidental operation", true);
                save.Enabled = false;
                candidate = null;
                return;
            }
            Pick(h);
        }

        // Gets all keys before the dialog can use them. This includes Alt key combinations, Tab, Enter and Space.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (msg.Msg != 0x0100 && msg.Msg != 0x0104) return base.ProcessCmdKey(ref msg, keyData);   // WM_KEYDOWN / WM_SYSKEYDOWN
            HandleKey(keyData, (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0);   // VK_LWIN / VK_RWIN
            return true;
        }

        // If you push and release only Alt or F10, Windows puts the window in menu mode. In menu mode, the window
        // does not get the next key. This window has no menu. Thus WndProc does not send SC_KEYMENU to the base class.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0112 /*WM_SYSCOMMAND*/ && ((int)m.WParam & 0xFFF0) == 0xF100 /*SC_KEYMENU*/ && m.LParam == IntPtr.Zero) return;
            base.WndProc(ref m);
        }

        [DllImport("user32.dll")] static extern short GetKeyState(int vk);

        // Shows the shortcut as keyboard keys with a "+" between them. While you hold only modifier keys, an empty
        // key follows them. This empty key has a border with a DashPattern. It shows the position of the next key.
        sealed class Keycaps : Control
        {
            string[] labels = new string[0];
            bool waiting;

            const TextFormatFlags Centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            public Keycaps()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                AccessibleRole = AccessibleRole.StaticText;
            }

            public void SetKeys(string[] keys, bool waitingForKey, string spoken)
            {
                labels = keys;
                waiting = waitingForKey;
                AccessibleName = spoken;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Color back = Parent != null ? Parent.BackColor : Theme.Layer;
                g.Clear(back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int n = labels.Length + (waiting ? 1 : 0);
                if (n == 0) return;

                const TextFormatFlags measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                var widths = new int[n];
                int keysW = 0;
                for (int i = 0; i < n; i++)
                {
                    int textW = i < labels.Length ? TextRenderer.MeasureText(g, labels[i], Theme.BodyStrong, Size.Empty, measure).Width : 0;
                    keysW += widths[i] = Math.Max(Theme.S(40), textW + Theme.S(24));
                }
                int arm = Theme.S(3), stroke = Math.Max(1, Theme.S(1)), plusW = 2 * arm + stroke + Theme.S(16);
                int plusesW = (n - 1) * plusW;
                if (keysW + plusesW > Width)   // The keys are too wide (for example, four modifier keys and a long key name). Make all keys smaller. Long labels get an ellipsis.
                {
                    int room = Width - plusesW, natural = keysW;
                    keysW = 0;
                    for (int i = 0; i < n; i++) keysW += widths[i] = widths[i] * room / natural;
                }

                int faceH = Height - Theme.S(2), x = (Width - keysW - plusesW) / 2;
                for (int i = 0; i < n; i++)
                {
                    if (i < labels.Length) DrawKey(g, new Rectangle(x, 0, widths[i], Height), faceH, labels[i], back);
                    else DrawEmptyKey(g, new Rectangle(x, 0, widths[i], faceH));
                    x += widths[i];
                    if (i == n - 1) break;
                    // Make the "+" from two rectangles at the center of the key faces. A text "+" is too low, and its edges show different colors.
                    int cx = x + (plusW - stroke) / 2, cy = (faceH - stroke) / 2;
                    using (var b = new SolidBrush(Theme.TextSecondary))
                    {
                        g.FillRectangle(b, cx - arm, cy, 2 * arm + stroke, stroke);
                        g.FillRectangle(b, cx, cy - arm, stroke, 2 * arm + stroke);
                    }
                    x += plusW;
                }
            }

            // Shows a key. The face has a border like a button. Below the face, an edge in EdgeColor gives the key some depth.
            static void DrawKey(Graphics g, Rectangle r, int faceH, string label, Color back)
            {
                float radius = Theme.SF(6);
                using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f), radius))
                using (var b = new SolidBrush(EdgeColor(back))) g.FillPath(b, path);
                using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, faceH - 1f), radius))
                {
                    using (var b = new SolidBrush(Theme.ControlFill(back, false, false))) g.FillPath(b, path);
                    using (var p = new Pen(Theme.ControlBorder(back))) g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, label, Theme.BodyStrong, new Rectangle(r.X + Theme.S(4), r.Y, r.Width - Theme.S(8), faceH), Theme.Text,
                    Centered | TextFormatFlags.EndEllipsis);
            }

            // Shows the empty key for the next key. This key has only a border with a DashPattern.
            static void DrawEmptyKey(Graphics g, Rectangle r)
            {
                using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f), Theme.SF(6)))
                using (var p = new Pen(Theme.ControlStrongBorder) { DashPattern = new[] { Theme.SF(4), Theme.SF(3) } })
                    g.DrawPath(p, path);
            }

            // Gives the color of the edge below the key face. This color is a mixture of the background color and black.
            // In high contrast mode, the edge has the color of the key border.
            static Color EdgeColor(Color back)
            {
                if (Theme.HighContrast) return Theme.ControlBorder(back);
                return Theme.Blend(Color.Black, back, Theme.Dark ? 0.35f : 0.2f);
            }
        }
    }
}
