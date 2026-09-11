#pragma once

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace nvps {

struct ProfileDescriptor {
    std::wstring name;
    std::wstring executablePath;
    bool enabled=true;
};

struct SwitchTarget {
    std::optional<std::size_t> profileIndex;
    std::wstring activeName=L"Windows";

    bool IsDesktop() const noexcept { return !profileIndex.has_value(); }
};

std::wstring NormalizeExecutableName(std::wstring_view path);

std::optional<std::size_t> FindMatchingProfile(
    const std::vector<ProfileDescriptor>& profiles,
    std::wstring_view foregroundExecutable);

SwitchTarget SelectSwitchTarget(
    const std::vector<ProfileDescriptor>& profiles,
    std::wstring_view foregroundExecutable,
    bool windowsOverride=false);

} // namespace nvps
