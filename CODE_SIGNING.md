# Code signing policy

**Planned signing attribution (not yet active):** Free code signing provided by SignPath.io, certificate by SignPath Foundation.
The project has not yet been approved for SignPath Foundation signing. This attribution describes the intended service, not a claim that current releases are signed.

NvProfileSwitcher is an open-source Windows utility distributed through its
[official GitHub repository](https://github.com/mgcarnevali/NvProfileSwitcher).

The project intends to use [SignPath.io](https://signpath.io/) and the
[SignPath Foundation](https://signpath.org/) for signing official Windows
release binaries, subject to approval and configuration of the signing service.
**Do not assume a download is digitally signed:** check the signature on the
individual file. The current GitHub Actions release workflow does not, by
itself, establish that code signing has taken place.

## Project

NvProfileSwitcher automatically switches NVIDIA display color profiles based
on the foreground application. Its source code is available under the
[GNU General Public License v3.0](LICENSE).

## Team roles

### Committer and reviewer

**Maximiliano Carnevali** maintains the source code, reviews contributions,
maintains the repository and build workflows, and prepares releases.

### Release approver

**Maximiliano Carnevali** reviews and approves official releases.

NvProfileSwitcher is currently maintained by a single developer.

## Privacy policy

NvProfileSwitcher does not intentionally collect or transmit telemetry, analytics,
profile configurations, or display settings. Its optional update checker contacts
GitHub to retrieve release information and download updates at the user's request.
Automatic update checks can be disabled in the application settings.
No other user information is intentionally transferred to networked systems.

## System changes and removal

The installer writes application files, registers the application for Windows
uninstallation, and creates Start Menu shortcuts. A desktop shortcut is optional.
The portable edition runs without installation. NVIDIA color settings are changed
only as directed by the user's profile configuration and controls.
The installed edition can be removed through Windows Installed apps.

## Build and release process

Official release candidates are built from the public repository using GitHub
Actions on Windows runners. The release workflow validates the requested
semantic version, builds and tests the application, creates the portable and
installer packages, and verifies their SHA-256 checksum files.

The workflow can create a **draft GitHub Release**, which is reviewed and
published manually. The release tag, when created by the workflow, points to
the source commit used for the build. Draft releases are not public releases.

Building a release candidate and publishing it do **not** imply that its files
have been digitally signed. If signing is enabled in the future, its status
and verification instructions should be documented before describing releases
as signed.

## Privacy and network communication

NvProfileSwitcher does not intentionally collect or transmit telemetry,
analytics, profile configurations, or display settings. Its built-in update
checker communicates with GitHub to check for releases and obtain update files.
Users can disable automatic update checks and check manually instead.

## Signing policy

Code signing, when available, is reserved for official NvProfileSwitcher
release binaries built from the public source code through the approved
release process.

Development builds, locally compiled binaries, modified builds, and
third-party redistributions are not official signed releases of the project.

Official releases are distributed through
[GitHub Releases](https://github.com/mgcarnevali/NvProfileSwitcher/releases).
