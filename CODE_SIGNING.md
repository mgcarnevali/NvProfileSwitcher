# Code signing policy

Free code signing for NvProfileSwitcher is provided by
[SignPath.io](https://signpath.io/), with a certificate provided by the
[SignPath Foundation](https://signpath.org/).

## Project

**NvProfileSwitcher** is an open-source Windows utility that automatically
switches NVIDIA display color profiles based on the application currently in
the foreground.

Source code:

[NvProfileSwitcher on GitHub](https://github.com/mgcarnevali/NvProfileSwitcher)

NvProfileSwitcher is licensed under the MIT License.

## Team roles

### Committer and reviewer

**Maximiliano Carnevali**

Responsible for:

- Source code development
- Code review
- Repository maintenance
- Build workflow maintenance
- Release preparation

### Approver

**Maximiliano Carnevali**

Responsible for reviewing and approving official signed releases.

NvProfileSwitcher is currently maintained by a single developer.

## Build and release process

Official NvProfileSwitcher releases are built from the public GitHub repository
using GitHub Actions and GitHub-hosted Windows runners.

Release builds are created from version tags using semantic versioning.

Development builds are identified using the source commit hash.

Signed release artifacts must originate from the official repository and its
configured GitHub Actions workflow.

## Privacy and network communication

NvProfileSwitcher does not collect, store, or transmit personal information,
usage statistics, telemetry, or analytics.

The application may access the network only to perform its built-in update
check against GitHub.

Automatic update checks can be disabled by the user. Manual update checks are
also available.

No profile configuration or display settings are transmitted over the network.

## Signing policy

Code signing is intended only for official NvProfileSwitcher release binaries
built from the public source code in this repository.

Development builds, locally compiled binaries, modified builds, and third-party
redistributions are not considered official signed releases.

Official signed releases are published through the project's GitHub Releases
page.
