using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using KeyboardToggler.Ui;

namespace KeyboardToggler
{
    // One connected keyboard, as the Keyboards window shows it.
    sealed class KeyboardInfo
    {
        public string Key;          // opaque id to pass back to the host
        public string Name;         // e.g. "Keychron Link" or "Standard PS/2 Keyboard"
        public string HardwareId;   // for display, e.g. HID\VID_3434&PID_D030&MI_02&Col01
        public bool BuiltIn;        // Windows says it's part of the computer
        public bool SwitchedOff;    // switched off together with the laptop keyboard (current choice)
        public bool TypedOn;        // the app has seen typing on it (it counts as your external keyboard)
    }

    // What the Keyboards window needs from the app.
    interface IKeyboardHost
    {
        List<KeyboardInfo> Keyboards();              // connected keyboards, built-in first
        void SetSwitchOff(string key, bool switchOff);
        bool HasManualChoices { get; }               // any choice differs from automatic detection
        void ResetToAutomatic();
        string Details();                            // plain-text report for bug reports ("Copy details")
        bool KeyboardIsOff { get; }                  // the laptop keyboard is switched off right now
    }

    // The Keyboards window: a card per connected keyboard, each with a "Switch off" toggle that chooses whether it
    // switches off together with the laptop keyboard. Modeless; the app keeps one instance and calls Reload() when
    // keyboards are plugged in or out or choices change. Changes apply as soon as a toggle flips, so Enter and Esc
    // both just close it.
    sealed class KeyboardsWindow : FluentForm
    {
        // Layout in px at 96 DPI (always scaled with Theme.S).
        const int WindowWidth = 520, Pad = 24, CardHeight = 68, CardGap = 4, VisibleCards = 5;
        const string CopyLabel = "Copy details", CopiedLabel = "Copied", CopyFailedLabel = "Couldn't copy";

        readonly IKeyboardHost host;
        readonly ThemedPanel header = new ThemedPanel { Surface = Surface.Background };
        readonly TextBlock intro = new TextBlock { Role = TextRole.Secondary,
            Text = "Choose which keyboards switch off together with the laptop keyboard. Leave your external keyboard on." };
        readonly GlyphIcon noteIcon = new GlyphIcon { Symbol = Glyph.Info, Role = TextRole.Accent, Points = 11f };
        readonly TextBlock note = new TextBlock { Text = "The laptop keyboard is off right now. Changes apply immediately." };
        readonly ScrollArea scroller = new ScrollArea();
        readonly ThemedPanel list = new ThemedPanel { Surface = Surface.Background };   // the cards; the only child of scroller
        readonly ThemedPanel footer = new ThemedPanel { Surface = Surface.Background, TopDivider = true };
        readonly FluentButton reset = new FluentButton { Text = "Reset to automatic" };
        readonly GlyphButton copy = new GlyphButton { Label = CopyLabel, Symbol = Glyph.Copy };
        readonly FluentButton close = new FluentButton { Text = "Close", Accent = true };
        readonly Timer copyTimer = new Timer { Interval = 2000 };
        readonly List<KeyboardCard> cards = new List<KeyboardCard>();
        readonly List<Control> retired = new List<Control>();   // removed cards, disposed once the event that removed them is over
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

            // The list spans the window so its scroll bar sits in the right margin, and the cards keep their width.
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

        // Shows the keyboards and choices as the host has them now, keeping the list's scroll position and the focus.
        public void Reload()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)Reload); } catch (InvalidOperationException) { }   // closing
                return;
            }
            if (reloading) { reloadAgain = true; return; }   // asked again mid-reload (e.g. by the host): once more afterwards
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

            updating = true;   // setting the toggles below mustn't count as the user flipping them
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

                // Cards already on screen are reused (matched by key), so the focus stays on the toggle it was on.
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

                // The list scrolls once it's taller than VisibleCards cards (or the screen); the end padding scrolls with it,
                // so a sixth card peeks out below the fifth.
                int contentH = y2 - gap + pad;
                list.Height = contentH;
                int maxH = Math.Min(Theme.S(VisibleCards * (CardHeight + CardGap) - CardGap + Pad), RoomOnScreen() - header.Height - footer.Height);
                int viewH = Math.Min(contentH, Math.Max(maxH, cardH + pad));
                scroller.Bounds = new Rectangle(0, header.Bottom, w, viewH);
                footer.Location = new Point(0, scroller.Bottom);
                reset.Enabled = host.HasManualChoices;
                ClientSize = new Size(w, footer.Bottom);

                scroller.PerformLayout();
                scroller.AutoScrollPosition = new Point(0, scrollY);   // clamped by WinForms if the list got shorter

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
            finally { Reload(); }   // shows what the host made of it (a refused change flips back)
        }

        void CopyDetails()
        {
            string text = host.Details();
            bool ok = false;
            if (!string.IsNullOrEmpty(text))
            {
                try { Clipboard.SetDataObject(text, true, 10, 100); ok = true; }
                catch (ExternalException) { }   // another app kept the clipboard open
            }
            copy.Label = ok ? CopiedLabel : CopyFailedLabel;
            copy.Symbol = ok ? Glyph.Checkmark : Glyph.Warning;
            copyTimer.Stop();
            copyTimer.Start();
        }

        // A removed card may be the one whose toggle is still raising its event, so it's disposed afterwards.
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

        // Client height the window can have on its screen.
        int RoomOnScreen()
        {
            Screen screen = IsHandleCreated && Visible ? Screen.FromHandle(Handle) : Screen.FromPoint(Cursor.Position);
            int frame = SizeFromClientSize(new Size(100, 100)).Height - 100;
            return screen.WorkingArea.Height - frame - Theme.S(48);
        }

        // After growing, the window moves up if its bottom went off the screen.
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

        // Width the text draws at. TextRenderer.MeasureText reports a line about half an em wider than that, even with
        // NoPadding; the surplus is the same for any text in a font, so it cancels out when the text is doubled.
        static int TextWidth(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s + s, f, Size.Empty, LineFlags).Width - TextRenderer.MeasureText(s, f, Size.Empty, LineFlags).Width;
        }

        // ---- the parts of the window ----

        // A Windows 11 settings card: Layer fill, 1 px Divider outline, 8 px corners, the keyboard pictogram on the left.
        class CardBase : Control, IThemed
        {
            protected bool GlyphOff;   // the pictogram gets the red "off" badge: the keyboard is switched off right now

            public CardBase()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                BackColor = Theme.Layer;   // what the controls on the card paint behind themselves
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

        // One keyboard: name with badges, hardware ID below, "Switch off" toggle on the right.
        sealed class KeyboardCard : CardBase
        {
            public readonly SwitchToggle Toggle = new SwitchToggle { Text = "Switch off" };
            readonly TextBlock name = new TextBlock { Font = Theme.BodyStrong, Wrap = false };
            readonly TextBlock id = new TextBlock { Font = Theme.Caption, Role = TextRole.Secondary, Wrap = false };
            readonly Badge builtIn = new Badge { Text = "Built in" };
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
                Toggle.AccessibleName = "Switch off " + name.Text;
                Toggle.AccessibleDescription = (hasBuiltIn ? "Built in. " : "") + (hasTypedOn ? "Your external keyboard. " : "") + id.Text;
                GlyphOff = keyboardIsOff && info.SwitchedOff;
                LayoutContent();
                Invalidate();
            }

            void LayoutContent()
            {
                int left = Theme.S(56), right = Theme.S(16);

                // FluentToggle draws its label with GDI padding (it needs the full padded width, or it gets an ellipsis)
                // and its track SF(3) inside its right edge, which lines up with the card's padding.
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

                // Badges follow the name. The name matters most, so when they don't fit beside it they move down in
                // front of the hardware ID, which is cut short instead (it's in "Copy details" in full).
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
                if (idW < Theme.S(48)) idW = 0;   // a few characters and an ellipsis would say nothing
                id.Bounds = new Rectangle(idLeft, line2 + (line2H - idH) / 2, idW, idH);
                builtIn.Visible = hasBuiltIn;
                typedOn.Visible = hasTypedOn;
            }
        }

        // Shown instead of the cards when there are none.
        sealed class EmptyCard : CardBase
        {
            readonly TextBlock text = new TextBlock { Role = TextRole.Secondary, Wrap = false, Text = "No keyboards found." };

            public EmptyCard() { Controls.Add(text); }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                int left = Theme.S(56), h = LineHeight(text.Font);
                text.Bounds = new Rectangle(left, (Height - h) / 2, Math.Max(0, Width - left - Theme.S(16)), h);
            }
        }

        // A small rounded tag after a keyboard's name, e.g. "Built in".
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

            // The subtle control fill. In light mode that fill is white on a white card, so there it's a faint tint
            // of the text colour instead (Windows 11's subtle secondary fill).
            static Color Fill(Color back)
            {
                return Theme.Dark ? Theme.ControlFill(back, false, false) : Theme.Blend(Theme.Text, back, 0.06f);
            }
        }

        // A FluentButton with an icon-font glyph before its label. (FluentButton.GlyphText measures the label with
        // EndEllipsis and no width, which comes back as just the ellipsis, so longer labels like "Copy details" get
        // cut short.) FluentButton still paints the fill, border and focus ring; its Text stays empty and this draws
        // the glyph and label, centred together.
        sealed class GlyphButton : FluentButton
        {
            string label = "", symbol = "";
            bool pressed;   // mirrors FluentButton's own pressed state, for the label colour

            public string Label { get { return label; } set { label = value ?? ""; AccessibleName = label; Invalidate(); } }
            public string Symbol { get { return symbol; } set { symbol = value ?? ""; Invalidate(); } }

            // Width that fits the glyph and the given label, with the same side padding FluentButton ends up with.
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

        // A FluentToggle that takes the focus when clicked, and tells screen readers it's a check button and whether it's on.
        sealed class SwitchToggle : FluentToggle
        {
            public SwitchToggle()
            {
                CheckedChanged += (s, e) => { if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); };
            }

            // FluentToggle keeps the focus where it was on a click, so Space would then press the focused button (Close).
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

                public override string DefaultAction { get { return "Toggle"; } }

                public override void DoDefaultAction()
                {
                    var t = (FluentToggle)Owner;
                    if (t.Enabled) t.Checked = !t.Checked;
                }
            }
        }

        // The scrolling part of the window. Its scroll bar follows the theme (uxtheme's dark Explorer style in dark mode).
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

            // Tabbing to a toggle scrolls its whole card into view (and the end padding after the last card). A click
            // scrolls nothing, so the toggle can't move away from under the pointer before the click lands.
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
