using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // The data for one connected keyboard, as the Keyboards window shows it.
    sealed class KeyboardInfo
    {
        public string Key;          // ID that the window sends back to the host. The window does not read it.
        public string Name;         // for example, "Keychron Link" or "Standard PS/2 Keyboard"
        public string HardwareId;   // the window shows it, for example HID\VID_3434&PID_D030&MI_02&Col01
        public bool BuiltIn;        // Windows identifies the keyboard as a part of the computer
        public bool SwitchedOff;    // Keyboard Toggler deactivates it with the laptop keyboard (the selection at this time)
        public bool TypedOn;        // the app saw that you typed on it (thus it is your external keyboard)
    }

    // The data and functions that the Keyboards window gets from the app.
    interface IKeyboardHost
    {
        List<KeyboardInfo> Keyboards();              // the connected keyboards, internal keyboards first
        void SetSwitchOff(string key, bool switchOff);
        bool HasManualChoices { get; }               // true if a selection is different from the automatic selection
        void ResetToAutomatic();
        string Details();                            // text report to help find problems ("Copy report")
        bool KeyboardIsOff { get; }                  // true if the laptop keyboard is off at this time
    }

    // The Keyboards window. It shows one card for each connected keyboard. Each card has a "Deactivate" toggle.
    // Use this toggle to select if Keyboard Toggler deactivates that keyboard with the laptop keyboard.
    // The window is modeless. The app keeps one instance and uses Reload() when you connect or disconnect
    // a keyboard, or when a selection changes. Each change has an effect when the toggle changes.
    // Thus Enter and Esc only close the window.
    sealed class KeyboardsWindow : FluentForm
    {
        // Dimensions in px at 96 DPI (always use Theme.S on them).
        const int WindowWidth = 520, Pad = 24, CardHeight = 68, CardGap = 4, VisibleCards = 5;
        const string CopyLabel = "Copy report", CopiedLabel = "Copied", CopyFailedLabel = "Not copied";

        readonly IKeyboardHost host;
        readonly ThemedPanel header = new ThemedPanel { Surface = Surface.Background };
        readonly TextBlock intro = new TextBlock { Role = TextRole.Secondary,
            Text = "Select the keyboards that Keyboard Toggler deactivates with the laptop keyboard. Do not select your external keyboard." };
        readonly GlyphIcon noteIcon = new GlyphIcon { Symbol = Glyph.Info, Role = TextRole.Accent, Points = 11f };
        readonly TextBlock note = new TextBlock { Text = "The laptop keyboard is off at this time. Each change has an effect immediately." };
        readonly ScrollArea scroller = new ScrollArea();
        readonly ThemedPanel list = new ThemedPanel { Surface = Surface.Background };   // the cards. This is the only child control of scroller.
        readonly ThemedPanel footer = new ThemedPanel { Surface = Surface.Background, TopDivider = true };
        readonly FluentButton reset = new FluentButton { Text = "Set to automatic" };
        readonly GlyphButton copy = new GlyphButton { Label = CopyLabel, Symbol = Glyph.Copy };
        readonly FluentButton close = new FluentButton { Text = "Close", Accent = true };
        readonly Timer copyTimer = new Timer { Interval = 2000 };
        readonly List<KeyboardCard> cards = new List<KeyboardCard>();
        readonly List<Control> retired = new List<Control>();   // removed cards. DisposeRetired() disposes them after the end of the event in which the window removed them.
        EmptyCard empty;
        bool updating, reloading, reloadAgain;

        public KeyboardsWindow(IKeyboardHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            this.host = host;
            Text = "Keyboards";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;

            int w = Theme.S(WindowWidth), pad = Theme.S(Pad);
            header.Width = w;
            intro.Bounds = new Rectangle(pad, pad, w - 2 * pad, 0);
            intro.FitHeight();
            header.Controls.AddRange(new Control[] { intro, noteIcon, note });

            // The list has the full width of the window. Thus its scroll bar is at the right edge of the window, and the cards keep their width.
            list.Width = w - pad;
            scroller.Controls.Add(list);

            int bh = Theme.S(32), min = Theme.S(96);
            footer.Size = new Size(w, pad + bh + pad);
            int closeW = close.PreferredWidth(min);
            int copyW = Math.Max(min, Math.Max(copy.WidthFor(CopyLabel), Math.Max(copy.WidthFor(CopiedLabel), copy.WidthFor(CopyFailedLabel))));
            reset.Bounds = new Rectangle(pad, pad, reset.PreferredWidth(min), bh);
            close.Bounds = new Rectangle(w - pad - closeW, pad, closeW, bh);
            copy.Bounds = new Rectangle(close.Left - Theme.S(8) - copyW, pad, copyW, bh);
            reset.TabIndex = 0; copy.TabIndex = 1; close.TabIndex = 2;
            footer.Controls.AddRange(new Control[] { reset, copy, close });

            header.TabIndex = 0; scroller.TabIndex = 1; footer.TabIndex = 2;
            Controls.AddRange(new Control[] { header, scroller, footer });

            reset.Click += (s, e) => { try { this.host.ResetToAutomatic(); } finally { Reload(); } };
            copy.Click += (s, e) => CopyDetails();
            close.Click += (s, e) => Close();
            copyTimer.Tick += (s, e) => { copyTimer.Stop(); copy.Label = CopyLabel; copy.Symbol = Glyph.Copy; };
            AcceptButton = close;
            CancelButton = close;
            ActiveControl = close;

            Reload();
        }

        // Shows the keyboards and the selections that the host has at this time.
        // The scroll position of the list and the focus stay the same.
        public void Reload()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)Reload); } catch (InvalidOperationException) { }   // the window closes
                return;
            }
            if (reloading) { reloadAgain = true; return; }   // if the host (for example) uses Reload() during a reload, do the reload one more time after this reload
            reloading = true;
            try
            {
                do { reloadAgain = false; Rebuild(); } while (reloadAgain && !IsDisposed);
            }
            finally { reloading = false; }
        }

        void Rebuild()
        {
            List<KeyboardInfo> items = host.Keyboards() ?? new List<KeyboardInfo>();
            items = items.FindAll(i => i != null);
            bool off = host.KeyboardIsOff;
            int w = Theme.S(WindowWidth), pad = Theme.S(Pad), cardW = w - 2 * pad, cardH = Theme.S(CardHeight), gap = Theme.S(CardGap);

            int scrollY = -scroller.AutoScrollPosition.Y;
            KeyboardCard focused = CardOf(ActiveControl);
            int focusedIndex = cards.IndexOf(focused);

            updating = true;   // Toggled() must ignore the toggle changes that follow. You do not make these changes. This code makes them.
            try
            {
                // The note shows only while the laptop keyboard is off.
                noteIcon.Visible = note.Visible = off;
                int headerBottom = intro.Bottom;
                if (off)
                {
                    int iconW = Theme.S(16), x = pad + iconW + Theme.S(8), y = intro.Bottom + Theme.S(8);
                    note.Bounds = new Rectangle(x, y, w - pad - x, 0);
                    note.FitHeight();
                    noteIcon.Bounds = new Rectangle(pad, y, iconW, LineHeight(note.Font));
                    headerBottom = note.Bottom;
                }
                header.Height = headerBottom + Theme.S(16);

                // This uses the cards on the screen again (it finds them by key). Thus the focus stays on the same toggle.
                var old = new List<KeyboardCard>(cards);
                cards.Clear();
                int y2 = 0;
                foreach (KeyboardInfo info in items)
                {
                    KeyboardCard card = TakeCard(old, info.Key);
                    if (card == null)
                    {
                        card = new KeyboardCard();
                        KeyboardCard c = card;
                        card.Toggle.CheckedChanged += (s, e) => Toggled(c);
                        list.Controls.Add(card);
                    }
                    card.TabIndex = cards.Count;
                    card.Bounds = new Rectangle(pad, y2, cardW, cardH);
                    card.Bind(info, off);
                    cards.Add(card);
                    y2 += cardH + gap;
                }
                bool focusGone = false;
                foreach (KeyboardCard gone in old)
                {
                    focusGone |= gone == focused;
                    list.Controls.Remove(gone);
                    Retire(gone);
                }
                if (cards.Count == 0)
                {
                    if (empty == null) { empty = new EmptyCard(); list.Controls.Add(empty); }
                    empty.Bounds = new Rectangle(pad, 0, cardW, cardH);
                    y2 = cardH + gap;
                }
                else if (empty != null) { list.Controls.Remove(empty); Retire(empty); empty = null; }

                // The list scrolls when its height is more than the height of VisibleCards cards (or of the screen). The end
                // padding scrolls with the list. Thus you can see a part of card 6 below card 5.
                int contentH = y2 - gap + pad;
                list.Height = contentH;
                int maxH = Math.Min(Theme.S(VisibleCards * (CardHeight + CardGap) - CardGap + Pad), RoomOnScreen() - header.Height - footer.Height);
                int viewH = Math.Min(contentH, Math.Max(maxH, cardH + pad));
                scroller.Bounds = new Rectangle(0, header.Bottom, w, viewH);
                footer.Location = new Point(0, scroller.Bottom);
                reset.Enabled = host.HasManualChoices;
                ClientSize = new Size(w, footer.Bottom);

                scroller.PerformLayout();
                scroller.AutoScrollPosition = new Point(0, scrollY);   // if the list is shorter, WinForms decreases this value as necessary

                if (focusGone)
                    ActiveControl = cards.Count == 0 ? (Control)close : cards[Math.Min(focusedIndex, cards.Count - 1)].Toggle;
                KeepOnScreen();
            }
            finally { updating = false; }
        }

        void Toggled(KeyboardCard card)
        {
            if (updating) return;
            try { if (card.Key != null) host.SetSwitchOff(card.Key, card.Toggle.Checked); }
            finally { Reload(); }   // shows the result from the host (if the host does not accept a change, the toggle goes back)
        }

        void CopyDetails()
        {
            string text = host.Details();
            bool ok = false;
            if (!string.IsNullOrEmpty(text))
            {
                try { Clipboard.SetDataObject(text, true, 10, 100); ok = true; }
                catch (ExternalException) { }   // a different app kept the clipboard open
            }
            copy.Label = ok ? CopiedLabel : CopyFailedLabel;
            copy.Symbol = ok ? Glyph.Checkmark : Glyph.Warning;
            copyTimer.Stop();
            copyTimer.Start();
        }

        // The toggle event of a removed card can continue at this time. Thus DisposeRetired() disposes the card after the end of that event.
        void Retire(Control c)
        {
            if (!IsHandleCreated) { c.Dispose(); return; }
            retired.Add(c);
            if (retired.Count == 1) BeginInvoke((Action)DisposeRetired);
        }

        void DisposeRetired()
        {
            var done = new List<Control>(retired);
            retired.Clear();
            foreach (Control c in done) c.Dispose();
        }

        // The maximum client height that the window can have on its screen.
        int RoomOnScreen()
        {
            Screen screen = IsHandleCreated && Visible ? Screen.FromHandle(Handle) : Screen.FromPoint(Cursor.Position);
            int frame = SizeFromClientSize(new Size(100, 100)).Height - 100;
            return screen.WorkingArea.Height - frame - Theme.S(48);
        }

        // If the height of the window increases and its bottom edge goes below the bottom of the screen area, this moves the window up.
        void KeepOnScreen()
        {
            if (!IsHandleCreated || !Visible || WindowState != FormWindowState.Normal) return;
            Rectangle area = Screen.FromHandle(Handle).WorkingArea;
            if (Bottom > area.Bottom && Top > area.Top) Top = Math.Max(area.Top, area.Bottom - Height);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                copyTimer.Dispose();
                DisposeRetired();
            }
            base.Dispose(disposing);
        }

        static KeyboardCard CardOf(Control c)
        {
            while (c != null && !(c is KeyboardCard)) c = c.Parent;
            return (KeyboardCard)c;
        }

        static KeyboardCard TakeCard(List<KeyboardCard> from, string key)
        {
            for (int i = 0; i < from.Count; i++)
                if (string.Equals(from[i].Key, key, StringComparison.Ordinal))
                {
                    KeyboardCard c = from[i];
                    from.RemoveAt(i);
                    return c;
                }
            return null;
        }

        const TextFormatFlags LineFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        static int LineHeight(Font f) { return TextRenderer.MeasureText("Ag", f, Size.Empty, LineFlags).Height; }

        // The width of the text on the screen. TextRenderer.MeasureText gives a width that is approximately 0.5 em too large.
        // This also occurs with NoPadding. This error is the same for all text in a font. Thus the width of two copies of the text, minus the
        // width of one copy, is the correct width.
        static int TextWidth(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s + s, f, Size.Empty, LineFlags).Width - TextRenderer.MeasureText(s, f, Size.Empty, LineFlags).Width;
        }

        // ---- the parts of the window ----

        // A Windows 11 settings card. It has the Layer color, a 1 px edge of the Divider color, 8 px corners and the keyboard
        // icon on the left side.
        class CardBase : Control, IThemed
        {
            protected bool GlyphOff;   // true: the icon shows the red "off" badge, because the keyboard is off at this time

            public CardBase()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                BackColor = Theme.Layer;   // the controls on the card show this color behind them
            }

            public void ApplyTheme() { BackColor = Theme.Layer; Invalidate(true); }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Theme.SF(8)))
                {
                    using (var b = new SolidBrush(Theme.Layer)) g.FillPath(b, path);
                    using (var p = new Pen(Theme.Divider)) g.DrawPath(p, path);
                }
                float s = Theme.SF(24);
                KeyboardGlyph.Draw(g, new RectangleF(Theme.SF(16), (Height - s) / 2f, s, s), Theme.TextSecondary, GlyphOff ? GlyphBadge.Off : GlyphBadge.None);
            }
        }

        // The card for one keyboard. It shows the name with badges, the hardware ID below the name, and the "Deactivate"
        // toggle on the right side.
        sealed class KeyboardCard : CardBase
        {
            public readonly SwitchToggle Toggle = new SwitchToggle { Text = "Deactivate" };
            readonly TextBlock name = new TextBlock { Font = Theme.BodyStrong, Wrap = false };
            readonly TextBlock id = new TextBlock { Font = Theme.Caption, Role = TextRole.Secondary, Wrap = false };
            readonly Badge builtIn = new Badge { Text = "Internal" };
            readonly Badge typedOn = new Badge { Text = "Your external keyboard" };
            bool hasBuiltIn, hasTypedOn;

            public string Key { get; private set; }

            public KeyboardCard() { Controls.AddRange(new Control[] { name, id, builtIn, typedOn, Toggle }); }

            public void Bind(KeyboardInfo info, bool keyboardIsOff)
            {
                Key = info.Key;
                name.Text = string.IsNullOrEmpty(info.Name) ? "Keyboard" : info.Name;
                id.Text = info.HardwareId ?? "";
                hasBuiltIn = info.BuiltIn;
                hasTypedOn = info.TypedOn;
                Toggle.Checked = info.SwitchedOff;
                Toggle.AccessibleName = "Deactivate " + name.Text;
                Toggle.AccessibleDescription = (hasBuiltIn ? "Internal. " : "") + (hasTypedOn ? "Your external keyboard. " : "") + id.Text;
                GlyphOff = keyboardIsOff && info.SwitchedOff;
                LayoutContent();
                Invalidate();
            }

            void LayoutContent()
            {
                int left = Theme.S(56), right = Theme.S(16);

                // FluentToggle shows its label with GDI padding. If the label does not have the full width with padding, it
                // gets an ellipsis. FluentToggle puts its track SF(3) to the left of its right edge. This aligns the track
                // with the padding of the card.
                int toggleW = TextRenderer.MeasureText(Toggle.Text, Toggle.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width
                    + Theme.S(12) + Theme.S(43) + 2;
                Toggle.Bounds = new Rectangle(Width - right + Theme.S(3) - toggleW, (Height - Theme.S(32)) / 2, toggleW, Theme.S(32));
                int textRight = Toggle.Left - Theme.S(16), avail = Math.Max(0, textRight - left);

                var badges = new List<Badge>();
                if (hasBuiltIn) badges.Add(builtIn);
                if (hasTypedOn) badges.Add(typedOn);
                int badgeGap = Theme.S(4), badgesW = 0, badgeH = 0;
                foreach (Badge b in badges)
                {
                    b.Size = b.Measure();
                    badgesW += b.Width;
                    badgeH = Math.Max(badgeH, b.Height);
                }
                if (badges.Count > 1) badgesW += (badges.Count - 1) * badgeGap;

                // The badges follow the name. The name is the most important text. If the badges do not have space
                // adjacent to the name, they move down to the left side of the hardware ID. Then the hardware ID becomes
                // shorter (the full ID is in the "Copy report" text).
                int nameGap = Theme.S(8), nameW = Math.Min(TextWidth(name.Text, name.Font) + 1, avail);
                bool badgesBelow = badges.Count > 0 && nameW + nameGap + badgesW > avail;
                int nameH = LineHeight(name.Font), idH = LineHeight(id.Font);
                int line2H = badgesBelow ? Math.Max(idH, badgeH) : idH;
                int top = (Height - nameH - line2H) / 2, line2 = top + nameH;
                name.Bounds = new Rectangle(left, top, nameW, nameH);

                int x = badgesBelow ? left : name.Right + nameGap;
                int by = badgesBelow ? line2 + (line2H - badgeH) / 2 : top + (nameH - badgeH) / 2;
                foreach (Badge b in badges) { b.Location = new Point(x, by); x += b.Width + badgeGap; }
                int idLeft = badgesBelow ? x - badgeGap + nameGap : left, idW = textRight - idLeft;
                if (idW < Theme.S(48)) idW = 0;   // a small number of characters and an ellipsis do not give data
                id.Bounds = new Rectangle(idLeft, line2 + (line2H - idH) / 2, idW, idH);
                builtIn.Visible = hasBuiltIn;
                typedOn.Visible = hasTypedOn;
            }
        }

        // The window shows this card when there are no keyboard cards.
        sealed class EmptyCard : CardBase
        {
            readonly TextBlock text = new TextBlock { Role = TextRole.Secondary, Wrap = false, Text = "Keyboard Toggler cannot find a keyboard." };

            public EmptyCard() { Controls.Add(text); }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                int left = Theme.S(56), h = LineHeight(text.Font);
                text.Bounds = new Rectangle(left, (Height - h) / 2, Math.Max(0, Width - left - Theme.S(16)), h);
            }
        }

        // A small badge after the name of a keyboard, for example "Internal". The badge has a curve at each corner.
        sealed class Badge : Control
        {
            public Badge()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Font = Theme.Caption;
            }

            public Size Measure() { return new Size(TextWidth(Text, Font) + Theme.S(16), LineHeight(Font) + Theme.S(4)); }

            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Color back = Parent != null ? Parent.BackColor : Theme.Layer;
                g.Clear(back);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (Theme.HighContrast)
                {
                    using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Theme.SF(4)))
                    using (var p = new Pen(Theme.Text)) g.DrawPath(p, path);
                }
                else
                {
                    using (var path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), Theme.SF(4)))
                    using (var b = new SolidBrush(Fill(back))) g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, Theme.TextSecondary,
                    LineFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // The color of the badge. In dark mode, it is the control color of Windows 11 (Theme.ControlFill). In light mode,
            // that color is white on a white card. Thus in light mode, this mixes 6% of the text color with the card color
            // (SubtleFillColorSecondary of Windows 11).
            static Color Fill(Color back)
            {
                return Theme.Dark ? Theme.ControlFill(back, false, false) : Theme.Blend(Theme.Text, back, 0.06f);
            }
        }

        // A FluentButton with an icon-font glyph on the left side of its label. FluentButton.GlyphText measures the label
        // with EndEllipsis and a width of 0. The result is only the ellipsis. Thus FluentButton makes long labels such as
        // "Copy report" too short. FluentButton shows the button color, the edge and the focus ring. Its Text stays
        // empty. This class shows the glyph and the label together in the center.
        sealed class GlyphButton : FluentButton
        {
            string label = "", symbol = "";
            bool pressed;   // a copy of the pressed state of FluentButton, for the label color

            public string Label { get { return label; } set { label = value ?? ""; AccessibleName = label; Invalidate(); } }
            public string Symbol { get { return symbol; } set { symbol = value ?? ""; Invalidate(); } }

            // The width for the glyph and the given label, with the same side padding as FluentButton.
            public int WidthFor(string text) { return Theme.S(16 + 16 + 8 + 16) + TextWidth(text, Font); }

            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); }
            protected override void OnMouseLeave(EventArgs e) { pressed = false; base.OnMouseLeave(e); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); }
            protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = false; base.OnKeyUp(e); }
            protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Color color = Accent ? (Enabled ? Theme.OnAccent : Theme.OnAccentDisabled)
                    : !Enabled ? Theme.TextDisabled : pressed ? Theme.TextSecondary : Theme.Text;
                int glyphW = Theme.S(16), gap = Theme.S(8);
                int x = Math.Max(Theme.S(12), (Width - glyphW - gap - TextWidth(label, Font)) / 2);
                var flags = LineFlags | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(e.Graphics, symbol, Theme.IconFont(Font.SizeInPoints), new Rectangle(x, 0, glyphW, Height), color,
                    flags | TextFormatFlags.HorizontalCenter);
                x += glyphW + gap;
                TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(x, 0, Math.Max(0, Width - x - Theme.S(8)), Height), color,
                    flags | TextFormatFlags.EndEllipsis);
            }
        }

        // A FluentToggle that gets the focus when you click it. It also tells screen readers that it is a check button,
        // and if it is on.
        sealed class SwitchToggle : FluentToggle
        {
            public SwitchToggle()
            {
                CheckedChanged += (s, e) => { if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); };
            }

            // On a click, FluentToggle does not move the focus. Then, if you push Space, the button that has the focus (Close)
            // operates.
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && CanFocus) Focus();
                base.OnMouseDown(e);
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new SwitchAccessible(this); }

            sealed class SwitchAccessible : ControlAccessibleObject
            {
                public SwitchAccessible(SwitchToggle owner) : base(owner) { }

                public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }

                public override AccessibleStates State
                {
                    get
                    {
                        AccessibleStates s = base.State;
                        if (((FluentToggle)Owner).Checked) s |= AccessibleStates.Checked;
                        return s;
                    }
                }

                public override string DefaultAction { get { return "Change"; } }

                public override void DoDefaultAction()
                {
                    var t = (FluentToggle)Owner;
                    if (t.Enabled) t.Checked = !t.Checked;
                }
            }
        }

        // The part of the window that scrolls. Its scroll bar uses the theme (the DarkMode_Explorer theme of uxtheme in dark mode).
        sealed class ScrollArea : Panel, IThemed
        {
            public ScrollArea()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                AutoScroll = true;
                BackColor = Theme.Background;
            }

            public void ApplyTheme()
            {
                BackColor = Theme.Background;
                if (IsHandleCreated) ThemeScrollBar();
                Invalidate(true);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ThemeScrollBar();
            }

            void ThemeScrollBar()
            {
                try
                {
                    SetWindowTheme(Handle, Theme.Dark && !Theme.HighContrast ? "DarkMode_Explorer" : "Explorer", null);
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37 /*NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED*/);
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }

            // When you use Tab to go to a toggle, this scrolls its full card into view (and the end padding after the last
            // card). A click does not scroll the list. Thus the toggle cannot move away from the pointer before the click.
            protected override Point ScrollToControl(Control active)
            {
                Point at = DisplayRectangle.Location;
                if (Control.MouseButtons != MouseButtons.None || active == null) return at;
                Control card = active;
                while (card.Parent != null && card.Parent.Parent != this) card = card.Parent;
                if (card.Parent == null) return base.ScrollToControl(active);
                Rectangle r = RectangleToClient(card.Parent.RectangleToScreen(card.Bounds));
                int below = card.Parent.Height - card.Bottom;
                if (below > Theme.S(Pad)) below = 0;
                if (r.Top < 0) at.Y -= r.Top;
                else if (r.Bottom + below > ClientSize.Height) at.Y -= r.Bottom + below - ClientSize.Height;
                return at;
            }

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);
            [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        }
    }
}
