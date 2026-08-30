# Contributing to NvProfileSwitcher

Thanks for considering contributing to NvProfileSwitcher.

Bug reports, feature requests and code contributions are welcome.

## Ways to contribute

- Report bugs through GitHub Issues.
- Suggest new features or improvements.
- Submit fixes or improvements through pull requests.

## Development

NvProfileSwitcher is a native Windows application written in C++.

### Requirements

- Windows 10 or Windows 11
- Visual Studio Build Tools with the MSVC x64 toolchain
- Windows SDK
- NVIDIA GPU and driver for testing NVIDIA color functionality

The application is built as a native x64 executable using C++20 and the
static C/C++ runtime (`/MT`).

No .NET SDK or runtime is required.

## Building

The easiest way to build NvProfileSwitcher is through the GitHub Actions
workflow included in the repository.

Development builds from `main` are automatically versioned as:

`dev-<commit>`

Release builds are generated from version tags such as:

`v1.0.0`

## Pull requests

1. Fork the repository.
2. Create a branch from `main`.
3. Make your changes.
4. Make sure the application builds successfully.
5. Test the affected functionality on Windows with an NVIDIA GPU.
6. Open a pull request describing what changed and why.

Please keep pull requests focused on a specific change whenever possible.

## Reporting bugs

When reporting a bug, please include:

- Windows version
- NVIDIA GPU
- NVIDIA driver version
- NvProfileSwitcher version
- A description of the problem
- Steps to reproduce it
- Any relevant screenshots or error messages

Do not include personal or sensitive information in logs, screenshots or
configuration files.

## Code style

- Keep the application lightweight and dependency-free where practical.
- Follow the existing C++/Win32 style used by the project.
- Avoid introducing unnecessary external dependencies.
- Keep user-facing behavior simple and predictable.
- Comments and user-facing strings should be written in English.

## License

By contributing to NvProfileSwitcher, you agree that your contributions will
be released under the [MIT License](LICENSE).
