# OLED Burn-In Saver 🖥️🖤

An ultra-lightweight, native Windows utility designed to protect OLED and QD-OLED displays from burn-in by monitoring keystroke inactivity and blanking selected displays into pitch-black (`#000000`) or putting screens into hardware power-off standby.

---

## ⚡ Why OLED Burn-In Saver?

When standard Windows power management puts monitors to sleep:
* ⚠️ Windows plays device disconnect chimes.
* ⚠️ Open windows get shuffled and moved onto remaining displays.
* ⚠️ High refresh rate / HDR / DisplayPort monitors take 3–10 seconds to handshake and turn back on.
* ⚠️ Audio routed through DisplayPort/HDMI gets cut off.

**OLED Burn-In Saver gives you the best of both worlds:**
* 🌟 **Mode 1: Pure OLED Blackout Overlay (`#000000`)**:
  * Self-emissive OLED subpixels turn off completely (0 nits, zero power per pixel, zero burn-in risk).
  * **Instant 0 ms Resume**: Moving the mouse or touching any key instantly restores your display with zero delay and zero window rearrangement.
  * **Per-Display Target**: Choose which specific monitors go dark (e.g., blank only your OLED monitor, leaving LCD screens active).
  * Uses two non-activating black panels per display to cover every pixel without presenting a single full-screen window to Windows' automatic Do Not Disturb rule. Existing Do Not Disturb settings and app-specific notification settings still apply.
* ⚡ **Mode 2: Hardware Screen Power Off (Standby / No Signal)**:
  * Uses native Windows DPMS (`SC_MONITORPOWER`) to cut video signal to all monitors, putting them into deep hardware power-saving standby.
  * PC, running apps, and background downloads stay active. Audio through HDMI/DisplayPort may stop when the display powers off; use Overlay mode to keep that audio connection active.
  * Resumes automatically on any mouse movement or keystroke.

---

## 🔒 100% Privacy (Zero Key Logging)
Uses passive Windows Raw Input (`RIDEV_INPUTSINK`) and event timestamping purely to measure idle duration. Key codes, letters, and typed characters are **never** inspected, captured, or logged to disk or memory.

---

## 🚀 Quick Start

1. Double-click [**`OLEDSaver.exe`**](file:///C:/Users/mrajput/OneDrive/Vibe%20Coded%20Apps/OLED%20Burn-In%20Saver/OLEDSaver.exe) to launch.
2. Choose your timeout (e.g., **5 minutes** or use preset buttons: 1m, 2m, 5m, 10m, 15m, 30m).
3. Choose your **Idle Action Mode** (Pure Blackout Overlay or Hardware Screen Power Off).
4. Click **`⏻ Blackout Now`** / **`⏻ Turn Off Now`** or press **`Ctrl + Alt + B`** to test immediately!

---

## ⚙️ Features

| Feature | Description |
| :--- | :--- |
| **Keystroke Timer** | Monitors keyboard activity. If no key is registered within your configured minutes, your chosen action triggers. |
| **Dual Inactivity Modes** | Choose between **Keyboard Inactivity Only** (ideal for reading/watching) or **Keyboard + Mouse Inactivity**. |
| **Two Idle Protection Modes** | **Pure OLED Blackout Overlay** (instant 0ms wake, per-monitor) OR **Hardware Screen Power Off** (cuts video signal to all displays). |
| **Per-Display Selection** | Choose any individual display or combination in Overlay mode. Non-selected displays stay untouched. |
| **Instant Wake-Up** | Moving the mouse (configurable sensitivity), clicking, or pressing any key immediately dismisses the action. |
| **Global Hotkey** | Press **`Ctrl + Alt + B`** anywhere in Windows to trigger the action immediately. |
| **System Tray** | Minimizes to the notification area with a live countdown tooltip and right-click quick menu. |
| **Windows Startup** | One-click toggle to launch silently in the background when Windows boots up. |
| **Media Playback Detection** | Pauses the countdown after 5 seconds of sustained audible playback from supported browsers and media players. Brief notification sounds and silent audio sessions do not reset the timer; short silent gaps during confirmed playback are tolerated for up to 2 seconds. |

---

## 🛠️ Building from Source

No external SDKs or runtime downloads required! Compiles natively with the built-in Windows C# compiler (`csc.exe`):

```cmd
build.bat
```

Run the settings regression tests in PowerShell (temporary files only; real
settings and Windows startup entries are not changed):

```powershell
.\Tests\Run-Tests.ps1
```

---

## 📁 Settings Location
Settings are saved automatically for the current Windows user on this PC in:
`%LOCALAPPDATA%\OLEDSaver\settings.json`

This is Windows' standard per-user, non-roaming application-data folder (normally
`C:\Users\<username>\AppData\Local`). The app and its required files can stay in
OneDrive; settings are not stored beside the EXE or synchronized between PCs.
Each PC keeps its own checkbox selections, timeout, display choices, and other
preferences. Start with Windows is also configured separately for each Windows
user on each PC.

On the first launch after this update, existing settings from
`%APPDATA%\OLEDSaver\settings.json` are copied to the local folder if no local
settings file exists. The old file is left untouched and ignored thereafter.
