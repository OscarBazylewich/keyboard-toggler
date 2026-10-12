# Keyboard Toggler

A small Windows tray app that switches your **laptop's built-in keyboard off** while you use an
external one, so things resting on the laptop (or the external keyboard itself) can't press keys.
One click on the tray icon turns it off; another turns it back on. Your external keyboard,
touchpad and mouse keep working the whole time.

Built for and tested on an ASUS TUF A16 (FA607PV) with Windows 11.

## Download

Get **`KeyboardToggler-Setup.exe`** from the [latest release](../../releases/latest) and run it.

- Windows 10 or 11, 64-bit. Nothing else to install: the .NET Framework it uses is part of Windows,
  and the keyboard driver is bundled.
- **Restart once** after the first install so Windows loads the keyboard driver.
- The installer isn't code-signed, so Windows may show *"Windows protected your PC"*.
  Click **More info → Run anyway**. You can check the download against the SHA-256 in the release notes.

## Using it

| Do this | To |
|---|---|
| Left-click the tray icon | Switch the laptop keyboard off / on |
| Click **Keep it off** in the prompt | Confirm. If you don't answer within 15 s, it turns back on |
| **Ctrl + Alt + Shift + K** (either keyboard) | Turn the laptop keyboard back on |
| Right-click the tray icon | Status, start-with-Windows, detected keyboards, log folder, exit |

The tray icon shows the laptop keyboard's state:

| <img src="docs/tray-on.png" width="48" alt="Green keyboard icon"> | <img src="docs/tray-off.png" width="48" alt="Grey keyboard icon with a red slash"> |
|:---:|:---:|
| Laptop keyboard **on** | Laptop keyboard **off** |

On Windows 11, new tray icons start in the hidden overflow: click **^** next to the clock and drag
the icon onto the taskbar to keep it visible.

Before switching off the first time, press any key on your external keyboard once. That's how
the app learns which keyboard is your external one.

### Safety nets

The laptop keyboard comes back on automatically when:

- you don't confirm **Keep it off** within 15 seconds,
- your external keyboard is unplugged,
- you press **Ctrl + Alt + Shift + K**,
- the app exits, crashes or is killed, or you sign out or restart.

It also refuses to switch off when no external keyboard is connected.

## How it works

Many laptops connect the built-in keyboard as a **PS/2 keyboard** (`ACPI\MSFT0001`, `i8042prt`
driver). Windows treats it as a critical system device, so it can't be switched off the usual ways:

| Approach | Result on this kind of laptop |
|---|---|
| Disable the device (Device Manager / SetupAPI) | Refused: *"Cannot disable critical system device."* |
| Uninstall/remove the device | Needs a restart for every toggle |
| Low-level keyboard hook + Raw Input | Can't tell which keyboard a key came from in time: Raw Input arrives *after* the hook has to decide, and not at all for blocked keys |

So Keyboard Toggler uses the [Interception](https://github.com/oblitum/Interception) filter driver.
When you switch the laptop keyboard off, the app asks the driver to route **only that one device**
through the app, which drops its keystrokes. Other keyboards never pass through the app at all, so
even if it hangs, your external keyboard keeps working. When the app stops, the driver goes back to
plain pass-through by itself, so there is no device state to restore.

The app runs without admin rights. The installer needs admin rights once, to install the driver.

## Compatibility

- **Built-in keyboard:** the app looks for a PS/2-type built-in keyboard (`ACPI\…`, `PNP0303`,
  `MSFT0001`). Some newer laptops connect the keyboard over I2C/HID instead. Those aren't recognised
  yet, and the app will tell you it can't find the built-in keyboard (see
  **Show detected keyboards**).
- **Anti-cheat:** some game anti-cheat systems dislike input filter drivers. Valorant (Riot
  Vanguard) launched fine in testing; a full match hasn't been tested. If a game complains,
  uninstall Keyboard Toggler.
- **Interception limits:** the driver tracks up to 10 keyboards. After a very large number of
  plug/unplug cycles in one session, newly plugged keyboards can stop being seen until you restart.

## Uninstall

**Settings › Apps › Installed apps › Keyboard Toggler › Uninstall.** This removes the app, the
Interception driver, the Start menu shortcut, the startup entry and the app's data. Restart
afterwards to finish removing the driver.

## Building from source

No SDK needed: the build uses the C# compiler that ships with Windows (.NET Framework 4.x).

```bat
build.cmd
```

The result is `dist\KeyboardToggler-Setup.exe`. Every push to `main` is also built by
[GitHub Actions](.github/workflows/build.yml), and the installer is attached to the run as an artifact.

```
src/          the tray app (KeyboardToggler.exe)
  Interception.cs   driver P/Invoke + the Blocker that filters the built-in keyboard
  TrayApp.cs        tray icon, menu, safety nets, startup setting, logging
  KeepOffPrompt.cs  the 15-second "Keep it off?" prompt
  Native.cs         Raw Input / hotkey Win32 calls
setup/        the installer and uninstaller (one exe; the app files are embedded as resources)
tools/        build-time icon generator
vendor/       Interception 1.0.1 binaries (unmodified; checksums in vendor/README.md)
```

Logs are written to `%LOCALAPPDATA%\KeyboardToggler\log.txt` (right-click the tray icon →
**Open log folder**).

## License

Keyboard Toggler is released under the [MIT License](LICENSE).

The installer bundles [Interception](https://github.com/oblitum/Interception) by Francisco Lopes
da Silva, which is licensed separately: LGPL-3.0 for non-commercial use. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
