using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardToggler.Ui
{
    // A control that needs more than a repaint when the theme changes (e.g. a new BackColor).
    interface IThemed { void ApplyTheme(); }

    // Base window: themed background, Segoe UI Variable, a frame that matches the theme, and no WinForms
    // auto-scaling (layout code scales its pixel sizes with Theme.S; fonts scale with the DPI by themselves).
    class FluentForm : Form
    {
        public FluentForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Theme.Changed += ThemeChanged;
        }

        // Borderless popups get Windows 11 rounded corners and a themed outline.
        protected virtual bool IsPopup { get { return false; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyFrame(Handle, IsPopup);
        }

        void ThemeChanged()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((Action)ApplyTheme); } catch (InvalidOperationException) { }
        }

        // Re-colours the window after a theme change. Override to update anything else, then call base.
        protected virtual void ApplyTheme()
        {
            if (IsDisposed) return;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            ApplyTheme(Controls);
            Theme.ApplyFrame(Handle, IsPopup);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37 /*NOSIZE|NOMOVE|NOZORDER|NOACTIVATE|FRAMECHANGED*/);
            Invalidate(true);
        }

        static void ApplyTheme(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                var t = c as IThemed;
                if (t != null) t.ApplyTheme();
                ApplyTheme(c.Controls);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Theme.Changed -= ThemeChanged;
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    enum Surface { Background, Layer }

    // A panel in a theme surface colour, optionally with a 1 px divider along its top (dialog footers).
    class ThemedPanel : Panel, IThemed
    {
        Surface surface;
        bool topDivider;

        public Surface Surface { get { return surface; } set { surface = value; ApplyTheme(); } }
        public bool TopDivider { get { return topDivider; } set { topDivider = value; Invalidate(); } }

        public ThemedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = surface == Surface.Layer ? Theme.Layer : Theme.Background;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (topDivider) using (var p = new Pen(Theme.Divider)) e.Graphics.DrawLine(p, 0, 0, Width, 0);
        }
    }

    enum TextRole { Primary, Secondary, Disabled, Accent, Critical, Caution, Success, OnAccent }

    // Themed text. Wraps by default, never treats '&' as a mnemonic, and paints over its parent's colour.
    class TextBlock : Control
    {
        TextRole role;
        bool wrap = true;
        TextFormatFlags align;

        public TextRole Role { get { return role; } set { role = value; Invalidate(); } }
        public bool Wrap { get { return wrap; } set { wrap = value; Invalidate(); } }
        public TextFormatFlags Align { get { return align; } set { align = value; Invalidate(); } }   // e.g. TextFormatFlags.HorizontalCenter

        public TextBlock()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = Theme.Body;
        }

        public static Color ColorOf(TextRole role)
        {
            switch (role)
            {
                case TextRole.Secondary: return Theme.TextSecondary;
                case TextRole.Disabled: return Theme.TextDisabled;
                case TextRole.Accent: return Theme.AccentText;
                case TextRole.Critical: return Theme.Critical;
                case TextRole.Caution: return Theme.Caution;
                case TextRole.Success: return Theme.Success;
                case TextRole.OnAccent: return Theme.OnAccent;
                default: return Theme.Text;
            }
        }

        TextFormatFlags Flags
        {
            // NoPadding: GDI's padding grows with the font size, which would push headings right of body text.
            get { return (wrap ? TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis) | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | align; }
        }

        // Height that shows all of the text at the current width.
        public int MeasureHeight()
        {
            return TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(Math.Max(1, Width), int.MaxValue), Flags).Height;
        }

        public void FitHeight() { Height = MeasureHeight(); }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ColorOf(role), Flags);
        }
    }

    // An icon-font glyph in a theme colour (e.g. Glyph.Warning in TextRole.Caution).
    class GlyphIcon : Control
    {
        string glyph = Ui.Glyph.Info;
        TextRole role;
        float points = 15f;

        public string Symbol { get { return glyph; } set { glyph = value; Invalidate(); } }
        public TextRole Role { get { return role; } set { role = value; Invalidate(); } }
        public float Points { get { return points; } set { points = value; Invalidate(); } }

        public GlyphIcon()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            TextRenderer.DrawText(e.Graphics, glyph, Theme.IconFont(points), ClientRectangle, TextBlock.ColorOf(role),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    // Windows 11 push button. Accent = the main action, filled with the accent colour. Works as a form's
    // AcceptButton/CancelButton; Space and Enter click it when it has the focus.
    class FluentButton : Control, IButtonControl
    {
        bool hover, pressed, accent, isDefault;
        string glyph;

        public bool Accent { get { return accent; } set { accent = value; Invalidate(); } }
        public string GlyphText { get { return glyph; } set { glyph = value; Invalidate(); } }   // optional icon-font glyph before the text
        public DialogResult DialogResult { get; set; }

        public FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            Font = Theme.Body;
            Size = new Size(Theme.S(120), Theme.S(32));
        }

        // Width that fits the text (and glyph) with Windows 11 padding, but at least minWidth.
        public int PreferredWidth(int minWidth)
        {
            int w = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + Theme.S(24);
            if (!string.IsNullOrEmpty(glyph)) w += Theme.S(24);
            return Math.Max(minWidth, w);
        }

        public void NotifyDefault(bool value) { if (isDefault != value) { isDefault = value; Invalidate(); } }

        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            var form = FindForm();
            if (form != null && DialogResult != DialogResult.None) form.DialogResult = DialogResult;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); if (TabStop && CanFocus && !Focused) Focus(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { if (pressed) { pressed = false; Invalidate(); } base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && pressed) { pressed = false; Invalidate(); PerformClick(); }
            base.OnKeyUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : Theme.Background;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Theme.SF(4);
            Color fill, text, border = Color.Empty;
            if (accent)
            {
                fill = !Enabled ? Theme.AccentDisabled : pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                text = Enabled ? Theme.OnAccent : Theme.OnAccentDisabled;
            }
            else
            {
                fill = Theme.ControlFill(back, Enabled && hover, Enabled && pressed);
                border = Theme.ControlBorder(back);
                text = !Enabled ? Theme.TextDisabled : pressed ? Theme.TextSecondary : Theme.Text;
            }
            using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            if (Focused && ShowFocusCues)
                using (var path = Theme.RoundRect(new RectangleF(1f, 1f, Width - 2f, Height - 2f), radius))
                using (var p = new Pen(Theme.Text, Theme.SF(2))) g.DrawPath(p, path);

            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            if (string.IsNullOrEmpty(glyph))
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, flags | TextFormatFlags.HorizontalCenter);
                return;
            }
            Font iconFont = Theme.IconFont(Font.SizeInPoints);
            var measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;   // no EndEllipsis: with no width limit it measures just "..."
            int glyphW = TextRenderer.MeasureText(glyph, iconFont, Size.Empty, measure | TextFormatFlags.NoPadding).Width;
            int textW = TextRenderer.MeasureText(Text, Font, Size.Empty, measure).Width;
            int gap = Theme.S(8), x = Math.Max(Theme.S(8), (Width - glyphW - gap - textW) / 2);
            TextRenderer.DrawText(g, glyph, iconFont, new Rectangle(x, 0, glyphW, Height), text, flags | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphW + gap, 0, Width - x - glyphW - gap, Height), text, flags);
        }
    }

    // Windows 11 toggle switch with its label on the left (settings-row style). Click or Space toggles it.
    class FluentToggle : Control
    {
        bool on, hover;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public FluentToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            Font = Theme.Body;
            Size = new Size(Theme.S(240), Theme.S(32));
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyUp(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && TabStop && CanFocus && !Focused) Focus();   // so Space next acts on this control
            base.OnMouseDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : Theme.Layer;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float tw = Theme.SF(40), th = Theme.SF(20), pad = Theme.SF(3);
            var track = new RectangleF(Width - tw - pad, (Height - th) / 2f, tw, th);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, (int)(track.X - Theme.S(12)), Height), Enabled ? Theme.Text : Theme.TextDisabled,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            float knob = Theme.SF(hover && Enabled ? 14 : 12);
            using (var path = Theme.RoundRect(track, th / 2))
            {
                if (on)
                {
                    using (var b = new SolidBrush(!Enabled ? Theme.AccentDisabled : hover ? Theme.AccentHover : Theme.Accent)) g.FillPath(b, path);
                }
                else
                {
                    using (var b = new SolidBrush(Theme.ControlFill(back, Enabled && hover, false))) g.FillPath(b, path);
                    using (var p = new Pen(Enabled ? Theme.ControlStrongBorder : Theme.TextDisabled)) g.DrawPath(p, path);
                }
            }
            float cx = on ? track.Right - th / 2 : track.X + th / 2, cy = track.Y + th / 2;
            Color knobColor = on ? (Enabled ? Theme.OnAccent : Theme.OnAccentDisabled) : (Enabled ? Theme.TextSecondary : Theme.TextDisabled);
            using (var b = new SolidBrush(knobColor)) g.FillEllipse(b, cx - knob / 2, cy - knob / 2, knob, knob);

            if (Focused && ShowFocusCues)
            {
                var ring = RectangleF.Inflate(track, Theme.SF(2.5f), Theme.SF(2.5f));
                using (var path = Theme.RoundRect(ring, ring.Height / 2))
                using (var p = new Pen(Theme.Text, Theme.SF(1.5f))) g.DrawPath(p, path);
            }
        }
    }

    // Windows 11 check box with its label on the right. Click or Space toggles it.
    class FluentCheckBox : Control
    {
        bool on, hover;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public FluentCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            Font = Theme.Body;
            Size = new Size(Theme.S(240), Theme.S(32));
        }

        protected override void OnClick(EventArgs e) { if (Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; base.OnKeyUp(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && TabStop && CanFocus && !Focused) Focus();   // so Space next acts on this control
            base.OnMouseDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : Theme.Layer;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Theme.SF(20);
            var box = new RectangleF(Theme.SF(2), (Height - s) / 2f, s, s);
            using (var path = Theme.RoundRect(box, Theme.SF(4)))
            {
                if (on)
                {
                    using (var b = new SolidBrush(!Enabled ? Theme.AccentDisabled : hover ? Theme.AccentHover : Theme.Accent)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, Glyph.Checkmark, Theme.IconFont(9f), Rectangle.Round(box), Enabled ? Theme.OnAccent : Theme.OnAccentDisabled,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
                else
                {
                    using (var b = new SolidBrush(Theme.ControlFill(back, Enabled && hover, false))) g.FillPath(b, path);
                    using (var p = new Pen(Enabled ? Theme.ControlStrongBorder : Theme.TextDisabled)) g.DrawPath(p, path);
                }
            }
            if (Focused && ShowFocusCues)
                using (var path = Theme.RoundRect(RectangleF.Inflate(box, Theme.SF(2), Theme.SF(2)), Theme.SF(6)))
                using (var p = new Pen(Theme.Text, Theme.SF(1.5f))) g.DrawPath(p, path);

            int tx = (int)(box.Right + Theme.S(8));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, 0, Width - tx, Height), Enabled ? Theme.Text : Theme.TextDisabled,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // Windows 11 progress bar: a 1 px track with a 3 px rounded accent bar. Indeterminate = a moving segment.
    class FluentProgress : Control
    {
        float value;
        bool indeterminate, showTrack = true;
        readonly Timer timer = new Timer { Interval = 16 };
        readonly DateTime start = DateTime.Now;

        public float Value { get { return value; } set { this.value = Math.Max(0f, Math.Min(1f, value)); Invalidate(); } }
        public bool Indeterminate { get { return indeterminate; } set { indeterminate = value; timer.Enabled = value && Visible; Invalidate(); } }
        public bool ShowTrack { get { return showTrack; } set { showTrack = value; Invalidate(); } }

        public FluentProgress()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Height = Theme.S(4);
            timer.Tick += (s, e) => Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e) { timer.Enabled = indeterminate && Visible; base.OnVisibleChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Layer);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float bar = Theme.SF(3), y = (Height - bar) / 2f;
            if (showTrack)
                using (var b = new SolidBrush(Theme.ControlStrongBorder))
                    g.FillRectangle(b, 0, (Height - 1) / 2f, Width, Math.Max(1f, Theme.SF(1)));
            float x, w;
            if (indeterminate)
            {
                double t = ((DateTime.Now - start).TotalMilliseconds % 1800) / 1800.0;
                t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;   // ease in-out
                w = Width * 0.4f;
                x = (float)(-w + (Width + w) * t);
            }
            else { x = 0; w = Width * value; }
            if (w <= 0) return;
            g.SetClip(new RectangleF(0, 0, Width, Height));
            using (var path = Theme.RoundRect(new RectangleF(x, y, w, bar), bar / 2))
            using (var b = new SolidBrush(Enabled ? Theme.Accent : Theme.AccentDisabled)) g.FillPath(b, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // A 1 px themed separator line.
    class DividerLine : Control
    {
        public DividerLine()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Height = 1;
        }

        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Theme.Divider); }
    }
}
