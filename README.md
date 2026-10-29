# Window Tile

A lightweight, zero-dependency background utility for **Windows 11** to instantly tile open application windows side by side using global keyboard shortcuts.

Optimized for multi-monitor setups and **ultrawide screens** (e.g. 3440 × 1440).

---

## ⌨️ Shortcuts

| Shortcut | Action | Layout |
| :--- | :--- | :--- |
| **`Win` + `Alt` + `Ctrl` + `2`** | Tile 2 windows side by side | `50% \| 50%` |
| **`Win` + `Alt` + `Ctrl` + `3`** | Tile 3 windows side by side | `33% \| 33% \| 33%` |
| **`Win` + `Alt` + `Ctrl` + `4`** | Tile 4 windows side by side | `25% \| 25% \| 25% \| 25%` |

*(Supports both standard top-row number keys `2`, `3`, `4` and the Numpad keys).*

---

## ✨ Features

* **⚡ Instant & Responsive:** Written in native C# with Win32 APIs, running quietly in the background with 0% CPU footprint and sub-millisecond response time.
* **🎨 Modern Colorful Tray Icon:** Custom 3-column vector tray icon with distinct accent colors:
  * Column 1: Windows Accent Blue (`#008CFF`)
  * Column 2: Warm Coral / Orange (`#F97316`)
  * Column 3: Emerald Green (`#10B981`)
  * High-contrast borders designed to look crisp on both dark and light Windows 11 themes.
* **📐 Pixel-Perfect Alignment (DWM Shadow Correction):** Automatically queries Windows 11 `DwmGetWindowAttribute` (`DWMWA_EXTENDED_FRAME_BOUNDS`) to compensate for invisible drop-shadow borders, preventing unwanted gaps or overlapping edges.
* **🖥️ Display & Taskbar Aware:** Reads the working area of the monitor currently hosting the focused window, ensuring the taskbar is never covered.
* **🛡️ Collision & Conflict Detection:** Checks every hotkey registration via `RegisterHotKey`. If a shortcut is already claimed by another program, a clear notification alerts you immediately.
* **⚙️ Built-In Autostart Toggle:** Right-click the system tray icon and click **`[✓] Start with Windows`** to toggle startup launch directly via Windows Registry (`HKCU Run`) — no batch files or admin rights needed.

---

## 🚀 Getting Started

### Option 1: Download Pre-built Binary
1. Download **`WindowTileHotkeys.exe`** from the [**Releases**](https://github.com/hschenke/window-tile/releases) page.
2. Double-click `WindowTileHotkeys.exe` to run.
3. Press `Win + Alt + Ctrl + 2`, `3`, or `4` anytime!

### Option 2: Build from Source
No IDE or external SDK download needed! Windows 10/11 already includes the .NET C# compiler:
1. Clone this repository:
   ```bash
   git clone https://github.com/hschenke/window-tile.git
   ```
2. Run `build.bat` (or compile via command line):
   ```cmd
   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /out:WindowTileHotkeys.exe /reference:System.Windows.Forms.dll,System.Drawing.dll WindowTileHotkeys.cs
   ```
3. Run `WindowTileHotkeys.exe`.

---

## 🖱️ Tray Menu Options

Right-click the **Window Tile** icon in the Windows taskbar tray (near the clock) to access:

* **Check Hotkey Status...**: Displays a dialog showing whether each shortcut is currently active or blocked.
* **Tile 2 / 3 / 4 Windows**: Trigger tiling manually from the menu.
* **Start with Windows**: Toggle automatic startup on system boot.
* **Exit**: Cleanly unregister all global hotkeys and close the application.

---

## 📜 AutoHotkey Alternative

If you already use [AutoHotkey v2](https://www.autohotkey.com/), a standalone script [`WindowTileHotkeys.ahk`](WindowTileHotkeys.ahk) is also included in the repository.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
