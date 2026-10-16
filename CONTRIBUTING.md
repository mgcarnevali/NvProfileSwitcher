# Contributing to NvProfileSwitcher

Thanks for considering contributing to NvProfileSwitcher.

Bug reports, feature requests, documentation improvements, and code contributions are welcome.

## Ways to contribute

- Report bugs through [GitHub Issues](https://github.com/mgcarnevali/NvProfileSwitcher/issues).
- Suggest new features or improvements.
- Improve documentation.
- Submit fixes or improvements through pull requests.

## Development

NvProfileSwitcher v3 is a Windows application written in **C#**, using **.NET** and **Avalonia UI**. It interacts with NVIDIA display settings through native interfaces and is distributed as a self-contained **NativeAOT** executable.

### Requirements

- Windows 10 or Windows 11 (x64) for running and testing the application.
- A compatible .NET SDK for the version specified by the project files.
- A supported C# development environment, such as Visual Studio or Visual Studio Code.
- An NVIDIA GPU and driver for testing display color functionality.
- The Windows native build prerequisites required by .NET NativeAOT when producing release executables.

You can inspect the code and run tests without NVIDIA hardware, but hardware-dependent behavior must be verified on a compatible Windows system.

## Building and testing

The application source is in `src/NvProfileSwitcher/`, with automated tests in `tests/`.

Use the .NET SDK version targeted by the project to restore, build, and test the solution or individual projects. The repository's GitHub Actions workflows are the reference for the complete build and test commands.

- `.github/workflows/ci.yml` validates changes through builds and automated tests, including UI tests where supported.
- `.github/workflows/release.yml` supports manual validation builds and release preparation. Official distribution builds use NativeAOT and include the Windows installer and portable package.
- Release creation is controlled by the project maintainer. Draft releases are reviewed before publication.

Do not commit generated build outputs or temporary files.

## Pull requests

1. Fork the repository.
2. Create a branch from `main`.
3. Make your changes, following the existing project structure and conventions.
4. Build the affected projects and run relevant automated tests.
5. Test affected display-color functionality on Windows with an NVIDIA GPU whenever possible; mention any hardware testing limitations in your pull request.
6. Open a pull request describing what changed, why, and how it was tested.

Please keep pull requests focused on a specific change whenever possible. Avoid unrelated UI or behavior changes.

## Reporting bugs

When reporting a bug, please include:

- Windows version.
- NVIDIA GPU model.
- NVIDIA driver version.
- NvProfileSwitcher version.
- A description of the problem.
- Steps to reproduce it.
- Expected and actual behavior.
- Any relevant screenshots or error messages.

Do not include personal or sensitive information in logs, screenshots, or configuration files.

## Code style

- Follow the existing C#/.NET and Avalonia conventions used by the project.
- Preserve existing application behavior unless a change is explicitly intended.
- Avoid introducing unnecessary external dependencies.
- Keep user-facing behavior simple and predictable.
- Add or update relevant tests when changing functionality.
- Write code comments and user-facing strings in English.

## License

By contributing to NvProfileSwitcher, you agree that your contributions will be released under the [GNU General Public License v3.0](LICENSE).
