#include "switching_core.h"

#include <cwctype>

namespace nvps {

std::wstring NormalizeExecutableName(std::wstring_view path){
    const auto separator=path.find_last_of(L"\\/");
    if(separator!=std::wstring_view::npos)
        path.remove_prefix(separator+1);

    const auto extension=path.find_last_of(L'.');
    if(extension!=std::wstring_view::npos)
        path=path.substr(0,extension);

    std::wstring normalized(path);
    for(auto& ch:normalized)
        ch=(wchar_t)std::towlower(ch);
    return normalized;
}

std::optional<std::size_t> FindMatchingProfile(
    const std::vector<ProfileDescriptor>& profiles,
    std::wstring_view foregroundExecutable){
    const std::wstring foreground=NormalizeExecutableName(foregroundExecutable);
    if(foreground.empty()) return std::nullopt;

    for(std::size_t i=0;i<profiles.size();++i){
        const auto& profile=profiles[i];
        if(!profile.enabled||profile.executablePath.empty()) continue;
        if(NormalizeExecutableName(profile.executablePath)==foreground)
            return i;
    }
    return std::nullopt;
}

SwitchTarget SelectSwitchTarget(
    const std::vector<ProfileDescriptor>& profiles,
    std::wstring_view foregroundExecutable,
    bool windowsOverride){
    SwitchTarget target;
    if(windowsOverride) return target;
    target.profileIndex=FindMatchingProfile(profiles,foregroundExecutable);
    if(target.profileIndex)
        target.activeName=profiles[*target.profileIndex].name;
    return target;
}

std::optional<SwitchTarget> SelectProfileOverrideTarget(
    const std::vector<ProfileDescriptor>& profiles,
    std::size_t profileIndex){
    if(profileIndex>=profiles.size()||!profiles[profileIndex].enabled)
        return std::nullopt;
    SwitchTarget target;
    target.profileIndex=profileIndex;
    target.activeName=profiles[profileIndex].name;
    return target;
}

std::optional<std::size_t> ToggleProfileOverride(
    const std::vector<ProfileDescriptor>& profiles,
    std::optional<std::size_t> currentProfileIndex,
    std::size_t pressedProfileIndex){
    if(pressedProfileIndex>=profiles.size()||!profiles[pressedProfileIndex].enabled)
        return currentProfileIndex;
    if(currentProfileIndex&&*currentProfileIndex==pressedProfileIndex)
        return std::nullopt;
    return pressedProfileIndex;
}

} // namespace nvps
