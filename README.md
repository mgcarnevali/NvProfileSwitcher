<div align="center">

# NvProfileSwitcher

### Your display. Your colors. Your rules.

**Automatic NVIDIA color profiles for Windows — tailored to every game, app, and monitor.**

Set your favorite colors once. NvProfileSwitcher takes care of the rest.

[**Download**](https://github.com/mgcarnevali/NvProfileSwitcher/releases) · [**Report an issue**](https://github.com/mgcarnevali/NvProfileSwitcher/issues)

**Windows 10 / 11** · **NVIDIA GPU** · **Freeware**

</div>

<!-- BEFORE PUBLICATION: replace this comment with a REAL v3 hero screenshot.
     Suggested path: docs/screenshots/v3-overview.png
     Suggested markup: ![NvProfileSwitcher v3 main window](docs/screenshots/v3-overview.png)
     Do not use a mockup or a v2 screenshot as a v3 product image. -->

> [!NOTE]
> **NvProfileSwitcher v3 is in development.** The Downloads page currently lists the publicly available releases. This page previews the upcoming v3 experience; v3 downloads will appear there when released.

## Made for the way you play

Every game looks different. Your display settings should adapt automatically.

NvProfileSwitcher detects the application you're using and applies its saved color profile across your displays. Switch to another game, return to the desktop, or activate a profile manually — without repeatedly changing NVIDIA Control Panel settings.

## Features

| | |
|---|---|
| **Automatic profile switching** | Apply the right color profile when a configured application becomes active, and return to your Windows profile when it isn't. |
| **Five color controls** | Adjust **Digital Vibrance**, **Hue**, **Brightness**, **Contrast**, and **Gamma** for each display. |
| **Per-display profiles** | Save independent settings for each physical monitor, even in multi-monitor setups. |
| **Live preview** | See changes immediately while editing. Unsaved adjustments are not committed to the profile. |
| **Hotkeys and overrides** | Activate a profile manually, use Windows Override, and resume auto switching from the keyboard. |
| **Global Switching** | Turn automatic application-based switching on or off without deleting your profiles. |
| **Monitor awareness** | Detect display connections and disconnections and retain settings associated with known physical monitors. |
| **Recovery after sleep** | Reapply the appropriate profile when Windows resumes and displays become available again. |
| **Backup and restore** | Export and import your application configuration. |
| **Everyday convenience** | System tray integration, startup preferences, driver information, and update checking. |

### Color control, per monitor

Configure the color of each connected display independently. Digital Vibrance and Hue are applied through NVIDIA APIs; Brightness, Contrast, and Gamma use the Windows display gamma ramp.

Adjustments can be previewed before saving, and **Reset** lets you return the selected controls to their default values while editing.

### Automatic when you want it. Manual when you need it.

Create a profile for an application executable and NvProfileSwitcher can activate it automatically when that application comes to the foreground.

Prefer manual control? Assign a hotkey to a profile, temporarily activate your Windows profile, or use **Resume auto switching** to return to automatic detection.

### Built for multiple displays

NvProfileSwitcher keeps separate settings for physical monitors instead of relying solely on Windows display numbers. It also responds to monitor connections and disconnections, helping keep your saved profiles associated with the right displays.

## Take a closer look

<!-- Add REAL screenshots from the v3 Windows build before publication.
     Recommended images:
       docs/screenshots/v3-windows-profile.png
       docs/screenshots/v3-application-profile.png
       docs/screenshots/v3-hotkeys.png
       docs/screenshots/v3-settings.png
     Use 2-column image tables only after the files exist, e.g.:
     | Windows profile | Application profile |
     |---|---|
     | ![Windows profile](docs/screenshots/v3-windows-profile.png) | ![Application profile](docs/screenshots/v3-application-profile.png) |
-->

**Windows Profile** — Your default desktop colors, customized for each monitor.

**Application Profiles** — Independent color settings for your games and apps.

**Hotkeys** — Profile activation, Windows Override, and Resume auto switching.

**Settings** — Global Switching, startup behavior, backups, and other preferences.

## Get started in four steps

1. **Set up your Windows profile.** Choose your preferred default colors for each monitor.
2. **Add an application.** Create a profile and select the game's or application's executable.
3. **Fine-tune your colors.** Adjust each display and preview the result live.
4. **Save and enable the profile.** NvProfileSwitcher automatically applies it when the application becomes active.

You can also assign a profile hotkey to activate it manually.

## Download and install

Official builds are published on the [**GitHub Releases**](https://github.com/mgcarnevali/NvProfileSwitcher/releases) page.

| Windows installer | Portable ZIP |
|---|---|
| Standard installation and uninstallation experience. | Extract the archive and run `NvProfileSwitcher.exe` without installing. |

**System requirements:** Windows 10 or Windows 11 (64-bit), an NVIDIA GPU, and a compatible NVIDIA display driver.

The distributed v3 application is self-contained; no separate .NET runtime installation is required.

> [!IMPORTANT]
> Color-control availability and results depend on your NVIDIA GPU, driver, and display configuration.

## Configuration and updates

Profiles and application preferences are stored locally. Use **Export** and **Import** to back up or restore your configuration.

NvProfileSwitcher can check for official updates and provides information about the installed NVIDIA driver.

**Upgrading from v2?** Version 3 is a new application generation. Do not assume that configuration files from the original C++ version can be imported into v3.

## Feedback and support

Found a bug or have an idea? [**Open an issue**](https://github.com/mgcarnevali/NvProfileSwitcher/issues).

When reporting a display issue, please include your Windows version, GPU, NVIDIA driver version, and the steps to reproduce it.

## License and previous versions

**NvProfileSwitcher v3 is proprietary freeware.** It is free to use; its source code is not publicly distributed. The applicable terms will accompany the v3 release.

Earlier open-source versions retain their original license terms, including **GNU GPL v3.0** for v2. The release of v3 does not change the license rights granted for earlier versions.

Copyright © 2026 Maximiliano Carnevali. All rights reserved **for the proprietary v3 release**, subject to applicable third-party rights and licenses.

---

<div align="center">

**Made for NVIDIA display color enthusiasts.**

*NvProfileSwitcher is an independent project and is not affiliated with, endorsed by, or sponsored by NVIDIA Corporation. NVIDIA is a trademark of NVIDIA Corporation.*

</div>
