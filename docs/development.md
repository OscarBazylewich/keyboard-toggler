# Building from source

[← Back to the README](../README.md)

No SDK needed: the build uses the C# compiler that ships with Windows (.NET Framework 4.x).

```bat
build.cmd
```

The result is `dist\KeyboardToggler-Setup.exe`. Every push to `main` is also built by
[GitHub Actions](../.github/workflows/build.yml), and the installer is attached to the run as an artifact.

## Source layout

```
src/          the tray app (KeyboardToggler.exe)
  Interception.cs   driver P/Invoke + the Blocker that filters the built-in keyboard
  Devices.cs        keyboard list from Windows' device tree (names, hardware IDs, built-in or not)
  Hotkey.cs         the toggle shortcut: storage, display names, scan codes
  ShortcutDialog.cs the "Change shortcut" window
  TrayApp.cs        tray icon, menu, safety nets, keyboard choice, startup setting, logging
  KeepOffPrompt.cs  the 15-second "Keep it off?" prompt
  Native.cs         Raw Input / hotkey Win32 calls
setup/        the installer and uninstaller (one exe; the app files are embedded as resources)
tools/        build-time icon generator
vendor/       Interception 1.0.1 binaries (unmodified; checksums in vendor/README.md)
docs/         user documentation and README images
```

## Releasing

1. Bump the version in `src/AssemblyInfo.cs` and add an entry to `CHANGELOG.md`.
2. Run `build.cmd` and test the installer.
3. Tag `vX.Y.Z`, create a GitHub release, attach `dist\KeyboardToggler-Setup.exe` and put its
   SHA-256 in the release notes (`Get-FileHash dist\KeyboardToggler-Setup.exe`).
