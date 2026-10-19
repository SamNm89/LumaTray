# LumaTray ☀️

A tiny, fast, and native Windows tray app to control monitor brightness.

I built this because I got fed up with Twinkle Tray sucking up 200 MB of RAM just to adjust brightness. After a little research, I found out it's because it's built with Electron slop! So I built mine with native C#.

**LumaTray** is written in C# targeting native Windows APIs (.NET 4.8 framework). It uses ~1–2 MB of trimmed idle RAM (~24–30 MB commit), zero background CPU, and lives as a single **~76 KB** standalone `.exe`.

---

## ⚡ Quick Comparison

| | Twinkle Tray | **LumaTray** |
| :--- | :--- | :--- |
| **Engine** | Electron (Chromium + Node) | **Native Win32 + C# (.NET 4.8 in-box)** |
| **Idle RAM** | 120 – 200+ MB | **~1 – 2 MB** (working set) / ~24–30 MB commit |
| **Idle CPU** | Periodic background spikes | **0.0%** (sleeps until clicked) |
| **Size** | ~150 MB installer | **~76 KB** single `.exe` |
| **Startup** | 3 – 5 seconds | **Instant** (< 100 ms) |

---

## ✨ Features

- **Windows 11 Fluent Design**:
  - Compact flyout that pops up right above your taskbar.
  - Automatically matches your Windows **Dark/Light Theme** and your personal **Windows Accent Color**.
  - Dynamic height: expands naturally to fit up to 2 monitor blocks with zero scrollbars.
  - Closes automatically when clicking away or pressing <kbd>Esc</kbd>.
- **Hardware DDC/CI & Laptop Screen Support**:
  - External HDMI / DisplayPort monitors via physical DDC/CI (`VCP 0x10`).
  - Built-in laptop and Surface displays via WMI.
  - Reads actual monitor names directly from EDID (e.g. `MSG244H` instead of "Generic PnP Monitor").
- **Keyboard & Mouse Control**:
  - <kbd>←</kbd> / <kbd>→</kbd> : Nudge brightness down / up by 2% (or 5% with <kbd>Shift</kbd>).
  - <kbd>Tab</kbd> / <kbd>↑</kbd> / <kbd>↓</kbd> : Switch between monitors if you have multiple screens.
  - Mouse wheel: Adjust brightness directly (or smoothly scroll through 3+ displays without ugly scrollbars).
- **Global Hotkeys & Floating OSD**:
  - <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>↑</kbd> : Increase brightness (+5%)
  - <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>↓</kbd> : Decrease brightness (-5%)
  - Shows a sleek mini floating OSD pill at the bottom center of the screen (never steals focus from games or apps).
- **Link All Displays (🔗)**:
  - One-click toggle in the header to adjust all your monitors in lockstep.
- **Smart Auto-Updates & Notification Dot**:
  - Automatically checks GitHub releases in the background (toggleable).
  - Shows a tiny blue notification blob on the tray icon and an update button in the flyout header when a new version is available.
  - Seamless 1-click update: downloads, swaps, and restarts in 1 second.
- **Brightness Memory**:
  - Automatically remembers and restores your exact brightness levels across reboots and updates.
- **Start with Windows**:
  - Simple 1-click toggle directly in the tray right-click menu.

---

## 🚀 Download & Run

1. Grab `LumaTray.exe` from the **[Releases](https://github.com/SamNm89/LumaTray/releases)** page.
2. Put it anywhere you want and run it. That's it — no installer needed.

> ### 🛡️ A Quick Note on Windows SmartScreen
> Because this is a free open-source project and I don't pay \$300+/year for a commercial code-signing certificate, Windows SmartScreen may show a blue popup on first run saying *"Windows protected your PC"*.
> 
> Just click **"More info"** ➔ **"Run anyway"**.
> 
> If you prefer not to run pre-compiled binaries from the internet, you can inspect the source code right here and compile it yourself in 1 second! (See below).

---

## 🛠️ Building from Source

No heavy Visual Studio or SDK download needed. Windows comes with the .NET 4.8 compiler pre-installed.

Just double-click:
```cmd
build.bat
```
It compiles `LumaTray.exe` in under a second using built-in Windows tools.

---

## 📄 License

[MIT](LICENSE) © 2026 [SamNm89](https://github.com/SamNm89)
