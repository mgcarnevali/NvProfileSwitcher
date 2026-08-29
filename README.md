# NvProfileSwitcher

Lightweight native Windows utility that automatically applies per-game NVIDIA display color profiles based on the application currently in the foreground.

When a configured game becomes active, NvProfileSwitcher applies its display profile. When you switch back to Windows or another application, the Windows profile is automatically restored.

## Features

- Per-game profiles based on executable (`.exe`).
- Automatic foreground application detection.
- Automatically restores the Windows profile when leaving a configured game.
- NVIDIA Digital Vibrance control through NVAPI.
- Brightness, Contrast and Gamma control through the NVIDIA display pipeline.
- Per-profile monitor selection.
- Multi-monitor support.
- Individual game executable icons.
- Windows profile for normal desktop use.
- Start with Windows option.
- Start minimized to system tray option.
- Minimize to tray.
- Pause/resume automatic profile switching from the tray.
- Manual Windows profile restore from the tray.
- Lightweight native C++ application.
- No .NET runtime required.
- No installation required.

## Requirements

- Windows 10 or Windows 11.
- NVIDIA GPU.
- NVIDIA display driver with NVAPI support.

## How it works

NvProfileSwitcher monitors the foreground application.

If the foreground executable matches an enabled game profile, that profile is applied to the selected display.

When the foreground application no longer matches a configured game, the Windows profile is automatically restored.

This means switching between a game and the desktop, browser, Discord, or any other application automatically switches between the game and Windows display profiles.

## Profile settings

Each profile can configure:

- Display
- Digital Vibrance
- Brightness
- Contrast
- Gamma

Profiles can be enabled or disabled individually.

## Usage

1. Launch `NvProfileSwitcher.exe`.
2. Configure the **Windows** profile with your normal desktop color settings.
3. Click **Add game**.
4. Browse to the game's executable.
5. Select the display where the game runs.
6. Configure Digital Vibrance, Brightness, Contrast and Gamma.
7. Click **Save profile**.
8. Launch the game.

NvProfileSwitcher will automatically apply the game profile when the game is in the foreground and restore the Windows profile when you switch away from it.

> If you use another application that controls NVIDIA Digital Vibrance or gamma settings, such as vibranceGUI, close it before using NvProfileSwitcher to avoid both applications changing the same display settings.

## System tray

NvProfileSwitcher can run entirely from the Windows system tray.

The tray menu provides:

- Open NvProfileSwitcher
- Restore Windows profile
- Pause automatic switching
- Resume automatic switching
- Exit

Minimizing the main window sends NvProfileSwitcher to the system tray.

## Configuration

Profiles and application settings are stored in:

`%APPDATA%\NvProfileSwitcher\profiles.json`

The configuration file is created automatically.

## Build

NvProfileSwitcher is built as a native x64 Windows application using MSVC.

The GitHub Actions workflow automatically builds the executable on pushes to the `main` branch.

The native build uses:

- C++20
- MSVC x64
- Static C/C++ runtime (`/MT`)
- Windows Win32 API
- NVIDIA NVAPI

No .NET SDK or runtime is required.

## Technical details

Digital Vibrance is controlled directly through NVIDIA NVAPI.

Brightness, Contrast and Gamma are applied through NVIDIA's display pipeline using a gamma correction LUT.

NvProfileSwitcher communicates directly with `nvapi64.dll` and does not require the NVIDIA App to be open.

The NVIDIA App sliders may not visually update when NvProfileSwitcher changes display values because NvProfileSwitcher applies the settings directly through NVAPI rather than controlling the NVIDIA App interface.

Foreground application detection uses lightweight polling to determine which executable currently owns the foreground window.

## License

License information will be added before the first public release.
