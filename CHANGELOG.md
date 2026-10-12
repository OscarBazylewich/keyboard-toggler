# Changelog

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
