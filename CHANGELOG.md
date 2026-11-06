# Changelog

## Unreleased

- **Simplified Technical English.** The text in the app, in Setup and in the log uses ASD-STE100
  Simplified Technical English (STE). Thus, the text is easy to read. These labels have new names:
  - **Change shortcut…** → **Change the shortcut…**
  - **Start with Windows** → **Start when you sign in**
  - **Open log folder** → **Open the log folder**
  - **Exit** → **Stop Keyboard Toggler**
  - **Turn back on** (in the **Keep it off** prompt) → **Activate it**
  - **Switch off** (in the **Keyboards** window) → **Deactivate**
  - **Built in** (in the **Keyboards** window) → **Internal**
  - **Copy details** → **Copy report**
  - **Reset to automatic** → **Set to automatic**
  - **Reset to default** → **Set to standard**
  - **Restart now** (in Setup) → **Start Windows again**
  - **Finish** (in Setup) → **Complete**

## 2.3.0 — 2026-10-23

- **New UI.** Keyboard Toggler has a new UI that is equivalent to the Windows 11 UI. The UI uses
  your light mode or dark mode and your accent color. Text is easy to read on high-DPI screens.
  These items are new:
  - The tray icon is equivalent to the Windows 11 tray icons. It is white when the taskbar uses
    dark mode, and black when the taskbar uses light mode. A badge on the icon shows the condition
    of the laptop keyboard. Thus, you can quickly see the condition:
    - A green check mark shows that the laptop keyboard is on.
    - A red bar shows that the laptop keyboard is off.
  - The app icon is new.
  - The tray menu is a standard Windows menu. It has rounded corners, and it uses dark mode when
    Windows uses dark mode.
  - The **Keep it off** prompt is a notification card with a countdown bar. In this prompt, Esc
    activates the laptop keyboard again.
  - The new **Change shortcut** window shows your shortcut as keys.
  - Message windows and the installer have the same new UI.
- **Keyboards window** (tray menu → **Keyboards…**). This window replaces the "Laptop keyboard"
  submenu and the "Show detected keyboards" item. Each keyboard has a **Switch off** control. Set
  it to on to deactivate that keyboard with the laptop keyboard. Use **Copy details** when you
  write a problem report.
- **Keys do not stay down.** Before this version, a problem occurred when you typed the shortcut on
  the laptop keyboard to deactivate it. Keys that you held down at that time (for example, Ctrl and
  Alt of the shortcut) stayed down in Windows. They stayed down until you pushed them on the
  external keyboard. In this version, Windows gets the usual key-up for these keys.
- If you type the shortcut on the laptop keyboard while that keyboard is off, the shortcut has an
  effect when you release the key. Thus, if you hold the key down for a long time, the app that you
  use does not get the key.
- In the **Keep it off** prompt, Enter and Space keep the laptop keyboard off. In previous
  versions, these keys activated the laptop keyboard again immediately.
- **Reset to automatic** sets all keyboard selections to automatic in one step. In previous
  versions, a problem was possible when the laptop keyboard was off. Then, this button activated
  the laptop keyboard again before the end of the procedure.
- Setup does not install Keyboard Toggler on Windows on Arm. On these PCs, Windows cannot load the
  Interception driver. If you install the driver, no keyboard and no mouse will operate after you
  start Windows again.
- Setup uses the full path to start Windows tools. Thus, Setup does not start a file with
  administrator rights if the file:
  - Has the same name as a Windows tool, and
  - Is in the same folder as the installer (for example, Downloads).

## 2.2.0 — 2026-10-21

- **You can change the shortcut.** To change the shortcut, do these steps:
  1. Right-click the tray icon.
  2. Select **Change shortcut…**.
  3. Type the shortcut that you want (Ctrl, Alt or Win plus one more key).

  Keyboard Toggler does not let you use a shortcut that Windows or a different app uses.
  **Reset to default** changes the shortcut to Ctrl + Alt + Shift + K again.
- The shortcut **deactivates or activates** the laptop keyboard. In previous versions, the
  shortcut only activated the laptop keyboard again. When the laptop keyboard is off, you can also
  type the shortcut on the laptop keyboard to activate it again.
- The tray menu shows your shortcut near the "Turn laptop keyboard off" or "Turn laptop keyboard
  on" item.

## 2.1.0 — 2026-10-12

- Keyboard Toggler also operates on laptops where the laptop keyboard is not PS/2. To find the
  laptop keyboard, Keyboard Toggler uses the device containers of Windows. Thus, Keyboard Toggler
  also finds I2C keyboards and USB keyboards in the laptop. If Keyboard Toggler cannot find the
  laptop keyboard with this method, it uses the PS/2 IDs.
- The tray menu has a new **Laptop keyboard** submenu. This submenu:
  - Shows the connected keyboards by name.
  - Shows the keyboards that Keyboard Toggler deactivates.
  - Lets you select each keyboard or cancel its selection (for example, a different device for the
    Fn key).

  **Reset to automatic** cancels the selections that you made.
- Keyboard Toggler uses the full hardware ID to find external keyboards. Thus, Keyboard Toggler
  also finds Bluetooth keyboards as external keyboards. Also, Keyboard Toggler does not use a HID
  laptop keyboard as an external keyboard. In previous versions, this error was possible.
- **Show detected keyboards** shows the name and the type of each keyboard.

## 2.0.3 — 2026-10-11

This is the initial version that you can download.

- Keyboard Toggler uses the Interception driver (a filter driver) to deactivate only the laptop
  keyboard. The input from external keyboards does not go through Keyboard Toggler. When Keyboard
  Toggler stops, the usual input starts again immediately.
- When you click the tray icon, Keyboard Toggler deactivates or activates the laptop keyboard.
- Keyboard Toggler has these safety items:
  - The "Keep it off?" prompt. If you do not use this prompt in 15 s, Keyboard Toggler activates
    the laptop keyboard again.
  - The Ctrl+Alt+Shift+K shortcut activates the laptop keyboard again. You can type this shortcut
    on the laptop keyboard or on the external keyboard.
  - When you disconnect the external keyboard, Keyboard Toggler automatically activates the laptop
    keyboard again.
  - If no external keyboard is connected, Keyboard Toggler does not deactivate the laptop keyboard.
- The installer:
  - Adds Keyboard Toggler to the Start menu.
  - Adds Keyboard Toggler to Settings › Apps.
  - Adds an uninstaller.
  - Lets you set Keyboard Toggler to start when you sign in. Task Manager › Startup apps shows
    this item.
- Setup removes the remaining items of the 1.x version. That version was a test version, and it
  was not possible to download it. It deactivated the keyboard device in Windows.
