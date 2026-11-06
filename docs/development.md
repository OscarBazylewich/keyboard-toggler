# Make the installer from the source code

[← Go to the README](../README.md)

It is not necessary to install an SDK. `build.cmd` uses the C# compiler that Windows supplies
(.NET Framework 4.x). To make the installer, start this file:

```bat
build.cmd
```

The installer is `dist\KeyboardToggler-Setup.exe`. When you push to `main`,
[GitHub Actions](../.github/workflows/build.yml) also makes the installer. It attaches the installer to the workflow run as an artifact.

## Source code folders and files

```
src/          the tray app (KeyboardToggler.exe)
  Interception.cs   Interception driver P/Invoke and the Blocker that deactivates the laptop keyboard
  Devices.cs        the keyboards in the Windows device tree (names, hardware IDs, internal or not)
  Hotkey.cs         the shortcut: saved value, display names, scan codes
  TrayApp.cs        tray menu, safety functions, keyboard selection, "Start when you sign in", log
  TrayIcon.cs       the tray icon (its color agrees with the light mode or dark mode of the taskbar)
  KeepOffPrompt.cs  the "The laptop keyboard is off" card (15 seconds, button "Keep it off")
  ShortcutDialog.cs the "Change the shortcut" window
  KeyboardsWindow.cs the "Keyboards" window (keyboards to deactivate, "Copy report" for bug reports)
  Native.cs         Win32 API functions for raw input and hotkeys
  app.manifest      DPI awareness and Windows 10 and 11 compatibility
  ui/           the Windows 11 UI items that the app and the installer use: theme (light mode or
                dark mode, accent color, fonts), buttons, toggle switches, dialogs, and the keyboard
                pictogram that the icons use
setup/        the installer and the uninstaller (one exe file that contains the app files as resources)
tools/        the icon generator that build.cmd uses
vendor/       Interception 1.0.1 binary files (not changed, with checksums in vendor/README.md)
docs/         documentation and the images of the README
```

## Procedure for a new release

1. Change the version number in all the files that follow:
   - `src/AssemblyInfo.cs` (2 lines)
   - `src/app.manifest`
   - `setup/Setup.cs` (3 lines)
   - `setup/setup.manifest`
2. In `CHANGELOG.md`, change the name of the `Unreleased` section to the new version number and the date of the release.
3. Start `build.cmd`.
4. Do a test of the installer.
5. Commit the changes.
6. Push the commit.
7. Tag that commit `vX.Y.Z`.
8. Push the tag.
9. Make a GitHub release from the tag.
10. Attach `dist\KeyboardToggler-Setup.exe` to the release.
11. Calculate the SHA-256 of the installer with `Get-FileHash dist\KeyboardToggler-Setup.exe`.
12. Put the SHA-256 in the release notes.
