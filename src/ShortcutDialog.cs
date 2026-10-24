using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // "Change shortcut" window. tryUse registers a candidate and returns false if Windows or another app already has it.
    sealed class ShortcutDialog : FluentForm
    {
        public Hotkey Result { get; private set; }
        readonly Func<Hotkey, bool> tryUse;
        Hotkey candidate;

        readonly Keycaps keycaps = new Keycaps();
        readonly GlyphIcon warning = new GlyphIcon { Symbol = Glyph.Warning, Role = TextRole.Critical, Points = 10.5f, Visible = false };
        readonly TextBlock status = new TextBlock();
        readonly FluentButton save = new FluentButton { Text = "Save", Accent = true, Enabled = false, TabStop = false };
        readonly Rectangle statusArea;   // under the keys, two lines high
        readonly int lineHeight;

        const TextFormatFlags StatusFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public ShortcutDialog(Hotkey current, Func<Hotkey, bool> tryUse)
        {
            this.tryUse = tryUse;
            Text = "Change shortcut";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowIcon = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;

            int width = Theme.S(440), pad = Theme.S(24), avail = width - 2 * pad;
            var content = new ThemedPanel { Surface = Surface.Layer, Location = Point.Empty, Width = width };
            var heading = new TextBlock { Font = Theme.Subtitle, Text = "Press a new shortcut", Location = new Point(pad, pad), Width = avail };
            heading.FitHeight();
            var intro = new TextBlock
            {
                Role = TextRole.Secondary,
                Text = "Use Ctrl, Alt or Win plus another key. It switches the laptop keyboard off and on, even when you type it on the switched-off keyboard.",
                Location = new Point(pad, heading.Bottom + Theme.S(8)),
                Width = avail,
            };
            intro.FitHeight();
            keycaps.Bounds = new Rectangle(pad, intro.Bottom + Theme.S(24), avail, Theme.S(40));
            // Two lines of room, so the window keeps its size when a message needs a second line.
            lineHeight = TextRenderer.MeasureText(" ", Theme.Body, Size.Empty, StatusFlags).Height;
            statusArea = new Rectangle(pad, keycaps.Bottom + Theme.S(12), avail, 2 * lineHeight);
            content.Height = statusArea.Bottom + pad;
            content.Controls.AddRange(new Control[] { heading, intro, keycaps, warning, status });

            // Buttons never take the focus (TabStop off), so Space, Enter etc. are captured as keys instead of pressing them.
            var footer = new ThemedPanel { Surface = Surface.Background, TopDivider = true, Location = new Point(0, content.Height), Size = new Size(width, Theme.S(80)) };
            var reset = new FluentButton { Text = "Reset to default", TabStop = false };
            var cancel = new FluentButton { Text = "Cancel", DialogResult = DialogResult.Cancel, TabStop = false };
            // Up to a third of the row each; Save and Cancel equal, Reset as wide as its text needs and set apart by a wider gap.
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
            SetStatus("Current shortcut", false);
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
            SetStatus("Press Save to use this shortcut", false);
            save.Enabled = true;
        }

        void Commit()
        {
            if (candidate == null) return;
            if (!tryUse(candidate))
            {
                SetStatus(candidate + " is already used by Windows or another app. Try a different one.", true);
                save.Enabled = false;
                return;
            }
            Result = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }

        // Shows text under the keys: secondary text, or for an error critical text after a warning glyph. The glyph
        // and text are centred as one block; text that needs two lines wraps at the narrowest width that still
        // fits it in two, so both lines come out about as long.
        void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.Role = error ? TextRole.Critical : TextRole.Secondary;
            warning.Visible = error;
            int lead = error ? Theme.S(24) : 0;   // 16 px glyph, 8 px gap
            int max = statusArea.Width - lead, textH = MeasureStatus(text, max).Height;
            int lo = 1, hi = max;   // find the narrowest width that keeps the text this high
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

        // Handles one key press. ProcessCmdKey sends every key here; winDown says whether a Win key is held,
        // which keyData can't carry.
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
                // Show the modifiers while they're held, but don't accept them on their own.
                string held = Hotkey.ModsText(mods);
                keycaps.SetKeys(held.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries), true, held.Length == 0 ? "..." : held + " + ...");
                SetStatus((mods & (Hotkey.Ctrl | Hotkey.Alt | Hotkey.Win)) != 0 ? "Now press another key" : "Add Ctrl, Alt or Win as well", false);
                save.Enabled = false;   // a new combination has started; Save waits for it to be finished
                candidate = null;
                return;
            }

            var h = new Hotkey(mods, (uint)key);
            if (!h.IsValid)
            {
                keycaps.SetKeys(h.KeyLabels(), false, h.ToString());
                SetStatus("Add Ctrl, Alt or Win so it can't be typed by accident", true);
                save.Enabled = false;
                candidate = null;
                return;
            }
            Pick(h);
        }

        // Sees every key, including Alt combinations, Tab, Enter and Space, before the dialog would act on them.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (msg.Msg != 0x0100 && msg.Msg != 0x0104) return base.ProcessCmdKey(ref msg, keyData);   // WM_KEYDOWN / WM_SYSKEYDOWN
            HandleKey(keyData, (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0);   // VK_LWIN / VK_RWIN
            return true;
        }

        // Releasing Alt or F10 on its own would put the window in menu mode and swallow the next key; it has no menu.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0112 /*WM_SYSCOMMAND*/ && ((int)m.WParam & 0xFFF0) == 0xF100 /*SC_KEYMENU*/ && m.LParam == IntPtr.Zero) return;
            base.WndProc(ref m);
        }

        [DllImport("user32.dll")] static extern short GetKeyState(int vk);

        // The shortcut drawn as keyboard keys with a "+" between them. While only modifiers are held, an empty
        // dashed key follows them, waiting for the other key.
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
                if (keysW + plusesW > Width)   // all four modifiers and a long key name: squeeze the keys, long labels get an ellipsis
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
                    // "+" drawn as a crisp cross, centred on the key faces (a text "+" sits low and gets colour fringes).
                    int cx = x + (plusW - stroke) / 2, cy = (faceH - stroke) / 2;
                    using (var b = new SolidBrush(Theme.TextSecondary))
                    {
                        g.FillRectangle(b, cx - arm, cy, 2 * arm + stroke, stroke);
                        g.FillRectangle(b, cx, cy - arm, stroke, 2 * arm + stroke);
                    }
                    x += plusW;
                }
            }

            // A key: its face, outlined like a button, sits on a darker lower edge that gives it some depth.
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

            // The key still to come: just a dashed outline.
            static void DrawEmptyKey(Graphics g, Rectangle r)
            {
                using (var path = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f), Theme.SF(6)))
                using (var p = new Pen(Theme.ControlStrongBorder) { DashPattern = new[] { Theme.SF(4), Theme.SF(3) } })
                    g.DrawPath(p, path);
            }

            // A shade darker than the key's outline; in high contrast the outline colour itself.
            static Color EdgeColor(Color back)
            {
                if (Theme.HighContrast) return Theme.ControlBorder(back);
                return Theme.Blend(Color.Black, back, Theme.Dark ? 0.35f : 0.2f);
            }
        }
    }
}
