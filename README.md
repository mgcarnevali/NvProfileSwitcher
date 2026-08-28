# Game Profile Switcher v0.1

Small Windows tray utility that automatically applies a color profile while a configured game EXE is running, then restores the Desktop profile when the game closes.

## v0.1 features

- Per-EXE profiles.
- Automatic switch when a game process appears/disappears.
- NVIDIA Digital Vibrance via NVAPI.
- Brightness, Contrast and Gamma via Windows hardware gamma ramp.
- Primary display only for this first version.
- Tray icon.
- Optional Start with Windows.
- Built-in sample profile for `EscapeFromTarkov.exe`.

## Build

1. Install the .NET 8 SDK on Windows.
2. Extract this folder.
3. Right-click `build.ps1` -> Run with PowerShell, or run:

   `powershell -ExecutionPolicy Bypass -File .\build.ps1`

4. The portable executable will be created in `publish\GameProfileSwitcher.exe`.

## First test

1. Close vibranceGUI so two programs do not fight over Digital Vibrance.
2. Open Game Profile Switcher.
3. Select the Tarkov profile.
4. Browse to your real `EscapeFromTarkov.exe` path.
5. Set the values you want.
6. Click `Apply now` to verify the look before using auto mode.
7. Restore Desktop to verify the normal profile.
8. Launch Tarkov. The app should switch automatically.
9. Close Tarkov. It should restore Desktop automatically.

## Important technical note

Digital Vibrance is changed through NVIDIA NVAPI. Brightness/Contrast/Gamma are implemented with the Windows hardware gamma-ramp mechanism (`SetDeviceGammaRamp` indirectly through WindowsDisplayAPI). This is programmatically controllable and useful for game profiles, but it is not guaranteed to be byte-for-byte identical to the sliders in every version of NVIDIA App.

The first milestone is to test whether the visual result on your specific NVIDIA driver/monitor matches what you want. If it does, the next versions can add multi-monitor selection, profile ordering, current-value capture, better UI, hotkeys, and Stream Deck hooks.

## Config file

Profiles are stored at:

`%APPDATA%\GameProfileSwitcher\profiles.json`

## Third-party libraries

- NvAPIWrapper.Net 0.8.1.101 (LGPL-3.0)
- WindowsDisplayAPI 1.3.0.13 (LGPL-3.0)
