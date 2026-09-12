#include "../switching_core.h"

#include <iostream>
#include <string>
#include <vector>

int RunHotkeyCoreTests();

namespace {

int failures=0;

void Expect(bool condition,const char* testName){
    if(condition) return;
    std::cerr<<"FAILED: "<<testName<<'\n';
    ++failures;
}

std::vector<nvps::ProfileDescriptor> TestProfiles(){
    return {
        {L"Escape From Tarkov",L"D:\\Games\\EscapeFromTarkov.exe",true},
        {L"Tarkov Arena",L"D:\\Games\\EscapeFromTarkovArena.EXE",true},
        {L"Disabled",L"C:\\Tools\\Disabled.exe",false},
        {L"No executable",L"",true}
    };
}

} // namespace

int main(){
    using nvps::NormalizeExecutableName;
    using nvps::SelectSwitchTarget;

    Expect(NormalizeExecutableName(L"C:\\Windows\\System32\\notepad.exe")==L"notepad",
           "normalizes a Windows executable path");
    Expect(NormalizeExecutableName(L"C:/Games/TARKOV.EXE")==L"tarkov",
           "normalizes separators and case");
    Expect(NormalizeExecutableName(L"notepad")==L"notepad",
           "accepts a process name without path or extension");
    Expect(NormalizeExecutableName(L"C:\\Games.v2\\game.profile.exe")==L"game.profile",
           "preserves dots in the executable base name");
    Expect(NormalizeExecutableName(L"C:\\Games\\GAME.EXE.EXE")==L"game.exe",
           "removes only the final extension");
    Expect(NormalizeExecutableName(L"").empty(),
           "normalizes an empty executable name");

    const auto profiles=TestProfiles();

    auto target=SelectSwitchTarget(profiles,L"escapefromtarkov");
    Expect(target.profileIndex==0&&target.activeName==L"Escape From Tarkov",
           "selects an enabled matching profile");

    target=SelectSwitchTarget(profiles,L"D:\\Other\\ESCAPEFROMTARKOVARENA.exe");
    Expect(target.profileIndex==1&&target.activeName==L"Tarkov Arena",
           "matches case-insensitively by executable name");

    target=SelectSwitchTarget(profiles,L"disabled.exe");
    Expect(target.IsDesktop()&&target.activeName==L"Windows",
           "ignores disabled profiles");

    target=SelectSwitchTarget(profiles,L"unknown.exe");
    Expect(target.IsDesktop()&&target.activeName==L"Windows",
           "falls back to the Windows profile");

    target=SelectSwitchTarget(profiles,L"");
    Expect(target.IsDesktop(),"handles a missing foreground process");

    target=SelectSwitchTarget(profiles,L"EscapeFromTarkov.exe",true);
    Expect(target.IsDesktop()&&target.activeName==L"Windows",
           "Windows override wins while an application profile is active");

    target=SelectSwitchTarget(profiles,L"",true);
    Expect(target.IsDesktop()&&target.activeName==L"Windows",
           "Windows override remains on Windows without a foreground process");

    target=SelectSwitchTarget(profiles,L"unknown.exe",true);
    Expect(target.IsDesktop()&&target.activeName==L"Windows",
           "Windows override remains on Windows for an unknown process");

    target=SelectSwitchTarget(profiles,L"EscapeFromTarkov.exe",false);
    Expect(target.profileIndex==0&&target.activeName==L"Escape From Tarkov",
           "removing Windows override restores automatic selection");

    auto duplicateProfiles=profiles;
    duplicateProfiles.insert(duplicateProfiles.begin(),
        {L"First match",L"E:\\Duplicate\\EscapeFromTarkov.exe",true});
    target=SelectSwitchTarget(duplicateProfiles,L"EscapeFromTarkov.exe");
    Expect(target.profileIndex==0&&target.activeName==L"First match",
           "preserves first-match precedence");

    auto profileOverride=nvps::SelectProfileOverrideTarget(profiles,0);
    Expect(profileOverride&&profileOverride->profileIndex==0
               &&profileOverride->activeName==L"Escape From Tarkov",
           "profile override selects the requested enabled profile");

    profileOverride=nvps::SelectProfileOverrideTarget(profiles,1);
    Expect(profileOverride&&profileOverride->profileIndex==1,
           "profile override can switch to another enabled profile");

    profileOverride=nvps::SelectProfileOverrideTarget(profiles,2);
    Expect(!profileOverride,
           "a disabled profile cannot remain in override");

    profileOverride=nvps::SelectProfileOverrideTarget(profiles,profiles.size());
    Expect(!profileOverride,
           "a removed profile ends its stale override");

    auto changedProfiles=profiles;
    changedProfiles[0].enabled=false;
    profileOverride=nvps::SelectProfileOverrideTarget(changedProfiles,0);
    Expect(!profileOverride,
           "disabling the pinned profile ends its override");

    changedProfiles.erase(changedProfiles.begin());
    profileOverride=nvps::SelectProfileOverrideTarget(changedProfiles,0);
    Expect(profileOverride&&profileOverride->activeName==L"Tarkov Arena",
           "an adjusted override index resolves the remaining profile");

    auto toggledOverride=nvps::ToggleProfileOverride(profiles,std::nullopt,0);
    Expect(toggledOverride==0,
           "a profile hotkey starts an override");

    toggledOverride=nvps::ToggleProfileOverride(profiles,toggledOverride,0);
    Expect(!toggledOverride,
           "pressing the active profile hotkey resumes automatic switching");

    toggledOverride=nvps::ToggleProfileOverride(profiles,0,1);
    Expect(toggledOverride==1,
           "another profile hotkey switches the active override");

    toggledOverride=nvps::ToggleProfileOverride(profiles,0,2);
    Expect(toggledOverride==0,
           "a disabled profile hotkey cannot replace the active override");

    toggledOverride=nvps::ToggleProfileOverride(profiles,0,profiles.size());
    Expect(toggledOverride==0,
           "an unknown profile hotkey leaves the active override unchanged");

    failures+=RunHotkeyCoreTests();

    if(failures){
        std::cerr<<failures<<" switching core test(s) failed.\n";
        return 1;
    }
    std::cout<<"All switching core tests passed.\n";
    return 0;
}
