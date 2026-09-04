# Changelog

All notable changes to NvProfileSwitcher will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project follows [Semantic Versioning](https://semver.org/).

## 1.2.0 — 2026-09-04

### Added

- Added a completely redesigned interface with a new branded application header.
- Added dedicated visual sections for **Profiles**, **Profile Settings**, and **Application Settings**.
- Added custom icons for Brightness, Contrast, Gamma, Digital Vibrance, and Hue controls.
- Added new visual indicators and action icons throughout the interface.
- Added a dedicated placeholder icon for profiles without an assigned executable.
- Added application version information to the footer, including development build identification.
- Added direct **GitHub**, **Support me**, and **About** links to the application footer.
- Added hover and hand-cursor behavior for footer links.

### Changed

- Redesigned the profile list and profile settings presentation.
- Redesigned slider controls with improved spacing, alignment, and visual feedback.
- Redesigned the display selector and application settings area.
- Redesigned the status footer to integrate NVIDIA API status, NVIDIA driver information, application version, and navigation links.
- Improved typography, spacing, button sizing, and overall visual hierarchy.
- Improved executable icon loading for sharper game icons in the profile list.
- Improved system tray behavior so the tray icon is only present when the application is actually minimized to the tray.
- Restoring the application now removes the tray icon and returns the application to the taskbar.
- Disabling **Minimize to tray** now removes an existing tray icon immediately.
- Reorganized UI assets and embedded resources for a cleaner project structure.
- Updated the GitHub Actions build workflow and development/release version handling.

### Fixed

- Improved cleanup of UI and image resources during application shutdown.

## 1.1.3 — 2026-09-02

### Added

- Added NVIDIA API status and NVIDIA driver version information to the application footer.

### Changed

- Added dark styling to the profile list scrollbar to better match the application theme.
- Refined UI spacing and alignment.

## 1.1.2 — 2026-09-02

### Added

- Added a **Check for updates** setting to enable or disable automatic update checks at startup.
- Manual update checks from the system tray remain available when automatic update checks are disabled.

### Changed

- Improved the main window layout and spacing.
- Updated the window sizing behavior to keep the interface at a consistent fixed size.
- Improved game profile layout to accommodate profile name, executable selection and automatic profile controls.
- Improved application settings layout.

## 1.1.1 — 2026-09-01

### Added

- Added independent per-monitor profiles for both Windows and games.
- Added single-instance support. Launching NvProfileSwitcher again now restores the existing window instead of opening a second instance.
- Added a **Minimize to tray** option, allowing the minimize button to either use the Windows taskbar or system tray.

### Changed

- The primary display is now selected by default when opening a profile.
- Improved startup, minimize and system tray behavior.
- Existing profiles are automatically migrated to the new per-monitor profile format.

### Fixed

- Fixed NVIDIA display mapping so color settings are applied to the correct monitor.
- Fixed saved profile values not always corresponding to the selected display.
- Fixed manual launches being minimized to the system tray when NvProfileSwitcher was configured to start minimized with Windows.

## 1.1.0 — 2026-08-30

### Added

- Added a manual **Check for updates** option to the system tray menu.
- Added feedback when NvProfileSwitcher is already up to date or the update check fails.

### Changed

- Simplified the game profile list by removing redundant secondary text.
- Reduced profile row height for a cleaner and more compact interface.
- Vertically centered profile names and icons in the profile list.

## 1.0.0 — 2026-08-30

First public stable release.

### Added

- Automatic per-game NVIDIA display color profile switching.
- Automatic restoration of Windows color profiles when leaving a configured game.
- Brightness, contrast, gamma, Digital Vibrance and hue controls.
- Multi-monitor support with monitor model detection.
- Game profiles matched by executable.
- Individual enable/disable control for game profiles.
- Start with Windows support.
- Start minimized to the Windows system tray.
- System tray controls for opening, viewing application information and exiting.
- Built-in GitHub update checker.
- Portable native x64 Windows executable with no .NET runtime required.
- Automatic development and release versioning through GitHub Actions.
