# How Keyboard Toggler operates

[← Go to the README](../README.md)

## How Keyboard Toggler finds the laptop keyboard

Windows puts each device into a *container*. All the internal devices of the computer are in the
container of the computer. Each device that you connect (USB, Bluetooth, dock) has a different
container. Keyboard Toggler identifies the keyboards in the container of the computer as laptop
keyboards. This method is correct for PS/2, I2C and internal USB laptop keyboards.

If Windows cannot give data about a keyboard, Keyboard Toggler uses the usual PS/2 hardware IDs to
identify the laptop keyboard. You can also change the selection in the tray menu (**Keyboards…**).

Keyboard Toggler identifies an external keyboard by its full hardware ID. Thus, Keyboard Toggler
also identifies Bluetooth keyboards.

## Problems with the usual methods

Many laptops use a **PS/2 keyboard** (`ACPI\MSFT0001`, `i8042prt` driver) as the laptop keyboard.
Windows identifies this keyboard as a very important system device. Thus, you cannot deactivate it
with the usual methods:

| Method | Result on this type of laptop |
|---|---|
| Deactivate the device (Device Manager / SetupAPI) | Windows does not let you do this: *"Cannot disable critical system device."* |
| Uninstall or remove the device | You must start Windows again after each change |
| Low-level keyboard hook + Raw Input | The hook must make a decision about each key first. Raw Input identifies the keyboard of the key, but it gives this data *after* that decision. For a key that the hook stops, Raw Input gives no data |

## The Keyboard Toggler method

Keyboard Toggler uses the [Interception](https://github.com/oblitum/Interception) filter driver.
When you deactivate the laptop keyboard, the app tells the driver to send the keystrokes of **only
that one device** to the app. The app discards these keystrokes. Other keyboards do not go through
the app. Thus, if the app does not operate correctly, your external keyboard continues to operate.

When the app stops, the driver automatically lets all keystrokes go through again. Keyboard Toggler
does not change the condition of a device. Thus, each device stays in its initial condition.

While the laptop keyboard is off, the app continues to monitor the keystrokes of that keyboard for
the shortcut. Thus, you can type the shortcut on the laptop keyboard to activate that keyboard again.

The app operates without administrator rights. Setup must have administrator rights only one time,
when it installs the Interception driver.

## File and registry locations

| Item | Location |
|---|---|
| Keyboard Toggler | `C:\Program Files\KeyboardToggler` |
| Shortcut, keyboard selection and log | `%LOCALAPPDATA%\KeyboardToggler` (`shortcut.txt`, `laptop-keyboards.txt`, `log.txt`) |
| Start when you sign in | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
