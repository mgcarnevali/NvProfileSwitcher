# NvProfileSwitcher

A Windows utility that automatically applies per-application NVIDIA
display color settings and restores the Windows profile when no
configured application is active.

This document defines the terminology and behavioral rules used by
NvProfileSwitcher. It is intended as shared context for contributors,
maintainers, and coding agents.

## Language

### Profiles

**Profile:** A named set of display color values: brightness, contrast,
gamma, digital vibrance, and hue. Values are stored independently for
each physical display. This is not an NVIDIA driver 3D profile.

Avoid: game profile, preset, scheme, color profile (ambiguous with ICC
profiles).

**Windows profile:** The special profile representing normal desktop
colors. It cannot be deleted and is restored whenever no enabled
application profile matches the foreground application.

Avoid: desktop profile, default profile, baseline.

**Application profile:** A user-created profile associated with an
executable. When enabled and its executable owns the foreground window,
its saved values are applied to the corresponding displays.

Avoid: game profile, game preset.

**Selected profile:** The profile currently highlighted in the UI for
viewing or editing. Selecting a profile does not activate it.

**Active profile:** The profile currently applied because of automatic
application switching. The active profile and the selected profile are
independent concepts.

### Switching

**Executable match:** An enabled application profile matches when the
configured executable name is the foreground process.

**Automatic switching:** The active profile follows the foreground
executable. If an enabled application profile matches, that profile is
applied. Otherwise, all connected displays return to their saved Windows
profile values.

Selecting an application profile in the UI is an editing action only. It
must not activate that profile.

### Editing

**Live preview:** Unsaved slider values temporarily applied to the
selected physical display while editing. A live preview does not make
the selected profile active.

Pending or active preview changes must be discarded and the real
foreground state re-applied when appropriate, including when switching
profiles, changing displays, minimizing the application, or saving an
application profile.

**Reset:** Restores the sliders for the selected display to
NVIDIA-neutral values and previews those values. Reset does not mean
"restore Windows profile."

NVIDIA-neutral values are:

-   Brightness: 100
-   Contrast: 100
-   Gamma: 1.00
-   Digital Vibrance: 50
-   Hue: 0

**Save profile:** Persists the values currently shown for the selected
display. Saving an application profile must then re-evaluate the real
foreground process so a live preview cannot remain physically active by
mistake.

## Profile initialization

A new physical display that has never been seen before receives
NVIDIA-neutral values for its Windows profile.

A new application profile inherits the saved Windows profile values for
each currently connected physical display. It does not start from
NVIDIA-neutral values.

When a newly detected physical display is added to an existing
application profile, its initial values are copied from that display's
Windows profile.

If a Windows profile cannot be found for a display during
application-profile initialization, NVIDIA-neutral values are used only
as a fallback.

Existing saved values for a known physical display must never be
replaced merely because its Windows DISPLAY number changed or because it
was disconnected and later reconnected.

## Displays

**Display:** A physical monitor driven by the NVIDIA GPU. Profiles store
one independent set of values for each display.

**Primary display:** The display Windows currently marks as primary. It
is shown first in the display selector and is selected by default when
opening a profile.

**MonitorId:** The stable physical identity used by NvProfileSwitcher to
associate saved values with a display. It is derived from the display
hardware identifier and EDID serial number.

`MonitorId` is the persisted identity. Windows names such as
`\\.\DISPLAY1`, `\\.\DISPLAY2`, and `\\.\DISPLAY3` are only current
routing names and must not be treated as physical identity.

Two different physical displays may use the same Windows DISPLAY number
at different times. They must remain separate profiles when their
`MonitorId` values differ.

A disconnected display remains stored in the configuration. Reconnecting
the same `MonitorId` restores its existing saved values.

## Display topology

NvProfileSwitcher detects display topology changes while running.

When a display is connected or disconnected:

1.  Any live preview is discarded.
2.  Active NVIDIA displays are re-enumerated.
3.  Newly seen physical displays receive a Windows profile if needed.
4.  Connected displays are ensured in every application profile.
5.  Configuration is saved.
6.  The display selector is refreshed.
7.  The real foreground application state is re-evaluated and applied.

Disconnected display profiles are intentionally retained.

## Configuration

Profiles are stored in `profiles.json`.

The top-level profile collections are:

-   `Windows Profiles`
-   `Application Profiles`

Application profiles store their per-display values under
`Display Profiles`.

The current configuration format uses stable `MonitorId` values. There
is no legacy DISPLAY-number migration or backward-compatibility path for
older development formats.

## Behavioral invariants

Changes should preserve these rules:

-   Selecting a profile is not the same as activating it.
-   Live preview is temporary and must never silently become the real
    active state.
-   The Windows profile is the source of initial values for application
    profiles.
-   NVIDIA-neutral values are used for Reset, first-time Windows
    displays, and fallback initialization.
-   Stable `MonitorId` identifies physical displays; DISPLAY numbers do
    not.
-   Existing values for a known `MonitorId` are preserved across driver
    updates, display-number changes, disconnects, and reconnects.
-   Newly connected displays are added without deleting disconnected
    display data.
-   Automatic switching is based on the actual foreground executable.
