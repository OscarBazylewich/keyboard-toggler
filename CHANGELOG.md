# Changelog

## Unreleased

- **No more stuck keys** after switching the laptop keyboard off with the shortcut typed on it. Keys
  that are still held at that moment (like the shortcut's Ctrl and Alt) are now released normally,
  instead of staying "pressed" in Windows until you tapped them on the external keyboard.
- The shortcut typed on the switched-off laptop keyboard now takes effect when you let go of the key,
  so holding it a little too long no longer types into the app you're using.
- In the **Keep it off?** prompt, Enter and Space now keep the keyboard off. They used to turn it
  straight back on.
- Setup refuses to install on Windows on Arm, where the keyboard driver can't load and every keyboard
  and mouse would stop working after the restart.
- Setup now starts Windows tools by their full path, so a file with the same name sitting next to the
  installer (for example in Downloads) can't run with admin rights.

## 2.2.0 — 2026-10-21

- **Custom shortcut.** Right-click the tray icon → **Change shortcut…** and press the combination you
  want (Ctrl, Alt or Win plus a key). Combinations already taken by Windows or another app are
  refused; **Reset to default** restores Ctrl + Alt + Shift + K.
- The shortcut now **toggles** the laptop keyboard off and on (it used to only turn it back on).
  While the laptop keyboard is off, typing the shortcut on it still turns it back on.
- The tray menu shows the current shortcut next to "Turn laptop keyboard off/on".

## 2.1.0 — 2026-10-12

- Supports laptops whose built-in keyboard isn't PS/2: built-in keyboards are now found through
  Windows' device containers, which also covers I2C and internal-USB keyboards. PS/2 IDs remain
  as a fallback.
- New **Laptop keyboard** tray submenu: lists connected keyboards by name, shows which are
  switched off, and lets you tick/untick them (e.g. a separate Fn-key device). **Reset to automatic**
  undoes manual choices.
- External keyboards are now recognised by full hardware ID, so Bluetooth keyboards count too,
  and a built-in HID keyboard can no longer be mistaken for an external one.
- **Show detected keyboards** lists keyboards by name with their role.

## 2.0.3 — 2026-10-11

First public release.

- Blocks only the built-in keyboard through the Interception filter driver; external keyboards
  are never routed through the app, and normal input resumes the moment the app exits.
- Tray icon: left-click to switch the laptop keyboard off/on.
- Safety: a "Keep it off?" prompt that turns the keyboard back on after 15 s with no answer,
  Ctrl+Alt+Shift+K on either keyboard, automatic turn-on when the external keyboard is unplugged,
  and a refusal to switch off when no external keyboard is connected.
- Installer with Start menu shortcut, Settings › Apps entry, uninstaller, and a "start at sign-in"
  option that shows up in Task Manager › Startup apps.
- Removes leftovers from the unreleased 1.x prototype (device-disable approach).
