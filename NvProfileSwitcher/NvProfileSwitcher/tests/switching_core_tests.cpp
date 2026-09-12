#include "../switching_core.h"

#include <iostream>
#include <string>
#include <vector>

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

    target=SelectSwitchTarget(profiles,L"EscapeFromTarkov.exe",false);
    Expect(target.profileIndex==0&&target.activeName==L"Escape From Tarkov",
           "removing Windows override restores automatic selection");

    auto duplicateProfiles=profiles;
    duplicateProfiles.insert(duplicateProfiles.begin(),
        {L"First match",L"E:\\Duplicate\\EscapeFromTarkov.exe",true});
    target=SelectSwitchTarget(duplicateProfiles,L"EscapeFromTarkov.exe");
    Expect(target.profileIndex==0&&target.activeName==L"First match",
           "preserves first-match precedence");

    if(failures){
        std::cerr<<failures<<" switching core test(s) failed.\n";
        return 1;
    }
    std::cout<<"All switching core tests passed.\n";
    return 0;
}
