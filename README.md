# OLED Blackout Saver 🖥️🖤

An ultra-lightweight, native Windows utility designed to protect OLED and QD-OLED displays from burn-in by monitoring keystroke inactivity and blanking selected displays into pitch-black (`#000000`), exactly like Twinkle Tray's blackout feature.

---

## ⚡ Why OLED Blackout Saver?

When standard Windows power management puts monitors to sleep:
* ⚠️ Windows plays device disconnect chimes.
* ⚠️ Open windows get shuffled and moved onto remaining displays.
* ⚠️ High refresh rate / HDR / DisplayPort monitors take 3–10 seconds to handshake and turn back on.
* ⚠️ Audio routed through DisplayPort/HDMI gets cut off.

**OLED Blackout Saver provides the ideal OLED solution:**
* 🌟 **True OLED Pitch Black (`#000000`)**: Self-emissive OLED pixels turn off completely (0 nits, zero power consumption per pixel, zero burn-in risk).
* ⚡ **Instant Resume (0 ms)**: Monitors stay logically connected. Moving the mouse or touching any key instantly restores your screen with zero delay or window rearrangement.
* 🔒 **100% Privacy (Zero Key Logging)**: Uses passive Windows Raw Input / event timestamping. Key codes and typed characters are **never** inspected, logged, or recorded.
* 🪶 **Super Lightweight**: Compiled native Windows executable (`~50 KB`), ~15–25 MB RAM, and 0.0% CPU when idle. Zero Electron bloatware.
* 🖥️ **Multi-Monitor Configurable**: Choose which displays go dark — blank only your OLED monitor, only the primary display, or all displays.

---

## 🚀 Quick Start

1. Double-click **`OLEDSaver.exe`** to launch.
2. Choose your timeout (e.g., **5 minutes** or use preset buttons: 1m, 2m, 5m, 10m, 15m, 30m).
3. Select which displays you want to blank under **Target Displays to Blackout** (e.g. check your OLED monitor).
4. Click **`⏻ Blackout Now`** or press **`Ctrl + Alt + B`** to test how smoothly it blacks out and wakes up!

---

## ⚙️ Features

| Feature | Description |
| :--- | :--- |
| **Keystroke Timer** | Monitors keyboard activity. If no key is registered within your configured minutes, selected screens go dark. |
| **Dual Inactivity Modes** | Choose between **Keyboard Inactivity Only** (ideal for reading/watching) or **Keyboard + Mouse Inactivity**. |
| **Per-Display Selection** | Choose any individual display or combination to black out. Non-selected displays stay untouched. |
| **Instant Wake-Up** | Moving the mouse (configurable sensitivity), clicking, or pressing any key immediately dismisses the blackout. |
| **Global Hotkey** | Press **`Ctrl + Alt + B`** anywhere in Windows to blackout selected screens immediately. |
| **System Tray** | Minimizes to the notification area with a live countdown tooltip and right-click quick menu. |
| **Windows Startup** | One-click toggle to launch silently in the background when Windows boots up. |

---

## 🛠️ Building from Source

No SDKs or dependencies are required! The app compiles directly using the built-in Windows .NET Framework C# compiler (`csc.exe`):

```cmd
build.bat
```

---

## 📁 Settings Location
Settings are saved automatically in:
`%APPDATA%\OLEDSaver\settings.json`
