# How Keyboard Toggler works

[← Back to the README](../README.md)

## Finding the built-in keyboard

Windows groups devices into *containers*: everything built into the computer shares the computer's
own container, while each plugged-in device (USB, Bluetooth, dock) gets its own. Keyboard Toggler
treats keyboards in the computer's container as built in, which covers PS/2, I2C and internal-USB
laptop keyboards alike. Keyboards Windows can't describe fall back to the classic PS/2 IDs, and you
can override the choice from the tray menu (**Laptop keyboard**).

External keyboards are recognised by their full hardware ID, so Bluetooth keyboards count too.

## Why the usual ways don't work

Many laptops connect the built-in keyboard as a **PS/2 keyboard** (`ACPI\MSFT0001`, `i8042prt`
driver). Windows treats it as a critical system device, so it can't be switched off the usual ways:

| Approach | Result on this kind of laptop |
|---|---|
| Disable the device (Device Manager / SetupAPI) | Refused: *"Cannot disable critical system device."* |
| Uninstall/remove the device | Needs a restart for every toggle |
| Low-level keyboard hook + Raw Input | Can't tell which keyboard a key came from in time: Raw Input arrives *after* the hook has to decide, and not at all for blocked keys |

## What it does instead

Keyboard Toggler uses the [Interception](https://github.com/oblitum/Interception) filter driver.
When you switch the laptop keyboard off, the app asks the driver to route **only that one device**
through the app, which drops its keystrokes. Other keyboards never pass through the app at all, so
even if it hangs, your external keyboard keeps working. When the app stops, the driver goes back to
plain pass-through by itself, so there is no device state to restore.

While the laptop keyboard is off, the app still watches its keystrokes for the toggle shortcut, so
typing the shortcut on the switched-off keyboard turns it back on.

The app runs without admin rights. The installer needs admin rights once, to install the driver.

## Where things are stored

| What | Where |
|---|---|
| App | `C:\Program Files\KeyboardToggler` |
| Settings and log | `%LOCALAPPDATA%\KeyboardToggler` (`shortcut.txt`, `laptop-keyboards.txt`, `log.txt`) |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
