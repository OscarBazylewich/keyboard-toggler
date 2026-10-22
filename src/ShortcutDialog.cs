using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardToggler
{
    // "Press the new shortcut" window. tryUse registers a candidate and returns false if another app already has it.
    sealed class ShortcutDialog : Form
    {
        public Hotkey Result { get; private set; }
        readonly Func<Hotkey, bool> tryUse;
        Hotkey candidate;
        readonly Label combo = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window };
        readonly Label hint = new Label { AutoSize = false };
        readonly Button save = new Button { Text = "Save", Width = 90, Height = 28, Enabled = false };

        public ShortcutDialog(Hotkey current, Func<Hotkey, bool> tryUse)
        {
            this.tryUse = tryUse;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Keyboard Toggler - shortcut";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            KeyPreview = true;
            ClientSize = new Size(400, 200);

            var intro = new Label
            {
                Text = "Press the shortcut that switches the laptop keyboard off and on.\nIt needs Ctrl, Alt or Win, plus another key.",
                Bounds = new Rectangle(16, 14, 368, 40)
            };
            combo.Bounds = new Rectangle(16, 60, 368, 40);
            combo.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
            combo.Text = current.ToString();
            hint.Bounds = new Rectangle(16, 106, 368, 36);
            hint.ForeColor = SystemColors.GrayText;
            hint.Text = "Current shortcut. Press a new one to change it.";

            var reset = new Button { Text = "Reset to default", Width = 120, Height = 28, Location = new Point(16, 156) };
            var cancel = new Button { Text = "Cancel", Width = 90, Height = 28, Location = new Point(294, 156), DialogResult = DialogResult.Cancel };
            save.Location = new Point(198, 156);
            reset.Click += (s, e) => Pick(Hotkey.Default);
            save.Click += (s, e) => Commit();
            CancelButton = cancel;

            // Buttons never take focus, so Space/Enter/Esc etc. can be captured as keys instead of pressing them.
            foreach (var b in new[] { reset, save, cancel }) b.TabStop = false;
            Controls.AddRange(new Control[] { intro, combo, hint, reset, save, cancel });
            ResumeLayout(false);
        }

        void Pick(Hotkey h)
        {
            candidate = h;
            combo.Text = h.ToString();
            hint.ForeColor = SystemColors.GrayText;
            hint.Text = "Click Save to use this shortcut.";
            save.Enabled = true;
        }

        void Commit()
        {
            if (candidate == null) return;
            if (!tryUse(candidate))
            {
                hint.ForeColor = Color.Firebrick;
                hint.Text = candidate + " is already used by Windows or another app. Try a different one.";
                save.Enabled = false;
                return;
            }
            Result = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }

        // Sees every key, including Alt combinations, Tab and Enter, before the dialog would act on them.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (msg.Msg != 0x0100 && msg.Msg != 0x0104) return base.ProcessCmdKey(ref msg, keyData); // WM_KEYDOWN / WM_SYSKEYDOWN
            if (key == Keys.Escape && (keyData & Keys.Modifiers) == 0) { Close(); return true; }

            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Hotkey.Ctrl;
            if ((keyData & Keys.Alt) != 0) mods |= Hotkey.Alt;
            if ((keyData & Keys.Shift) != 0) mods |= Hotkey.Shift;
            if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) mods |= Hotkey.Win;

            if (Hotkey.IsModifierKey(key) || key == Keys.Menu || key == Keys.ShiftKey || key == Keys.ControlKey)
            {
                // Show the modifiers while they're held, but don't accept them on their own.
                combo.Text = (mods == 0 ? "" : Hotkey.ModsText(mods) + " + ") + "...";
                return true;
            }

            var h = new Hotkey(mods, (uint)key);
            if (!h.IsValid)
            {
                combo.Text = h.ToString();
                hint.ForeColor = Color.Firebrick;
                hint.Text = "Add Ctrl, Alt or Win so the shortcut can't be typed by accident.";
                save.Enabled = false;
                candidate = null;
                return true;
            }
            Pick(h);
            return true;
        }

        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    }
}
