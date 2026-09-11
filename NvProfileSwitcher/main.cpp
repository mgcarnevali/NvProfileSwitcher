/*
 * NvProfileSwitcher
 * Copyright (C) 2026 Maximiliano Carnevali
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * See the LICENSE file for the full license text.
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shellapi.h>
#include <commdlg.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <uxtheme.h>
#include <dwmapi.h>
#include <dbt.h>
#include <gdiplus.h>
#include <objidl.h>
#include <winhttp.h>
#include <string>
#include <vector>
#include <fstream>
#include <regex>
#include <algorithm>
#include <cmath>
#include "resource.h"
#include "version.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "uxtheme.lib")
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "gdiplus.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "winhttp.lib")

struct DisplayProfileValues {
    std::wstring displayName;
    std::wstring monitorId;
    int vibrance=50;
    int hue=0;
    double brightness=100.0, contrast=100.0, gamma=1.00;
};

struct ApplicationProfile {
    std::wstring name=L"New Profile";
    std::wstring exePath;
    bool enabled=true;
    std::vector<DisplayProfileValues> displayProfiles;
};
struct Settings {
    ApplicationProfile desktop{L"Windows",L"",true,{}}; // template metadata for Windows profiles
    std::vector<ApplicationProfile> desktopProfiles;
    std::vector<ApplicationProfile> profiles;
    bool startWindows=false, startMinimized=false, minimizeToTray=false, checkUpdates=true;
};

constexpr COLORREF C_BACK=RGB(10,13,16), C_PANEL=RGB(18,22,26), C_PANEL2=RGB(24,29,34), C_FIELD=RGB(20,24,28), C_BORDER=RGB(45,52,59);
constexpr COLORREF C_TEXT=RGB(241,244,247), C_MUTED=RGB(151,161,171), C_ACCENT=RGB(82,214,39), C_ACCENT2=RGB(43,164,22), C_ACCENT_DARK=RGB(24,50,28), C_DANGER=RGB(232,75,75);
constexpr COLORREF C_TRACK=RGB(61,67,73), C_WINBLUE=RGB(0,120,215);
constexpr UINT WM_TRAY=WM_APP+1;
constexpr UINT WM_UPDATE_AVAILABLE=WM_APP+2;
constexpr UINT WM_SHOW_EXISTING_INSTANCE=WM_APP+3;
constexpr wchar_t INSTANCE_MUTEX_NAME[]=L"Local\\NvProfileSwitcher_SingleInstance";
constexpr wchar_t APP_VERSION[]=NVPS_VERSION_WSTR;
constexpr wchar_t APP_URL[]=L"https://github.com/mgcarnevali/NvProfileSwitcher";
constexpr wchar_t SUPPORT_URL[]=L"https://ko-fi.com/mgcarnevali";
constexpr wchar_t UPDATE_HOST[]=L"api.github.com";
constexpr wchar_t UPDATE_PATH[]=L"/repos/mgcarnevali/NvProfileSwitcher/releases/latest";
enum {IDC_LIST=1001,IDC_NAME,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_DISPLAY,IDC_LBL_DISPLAY,IDC_VIB,IDC_HUE,IDC_BRI,IDC_CON,IDC_GAM,IDC_SAVE,IDC_ADD=1015,IDC_REMOVE,IDC_STARTWIN=1018,IDC_STARTMIN,IDC_VALVIB,IDC_VALHUE,IDC_VALBRI,IDC_VALCON,IDC_VALGAM,IDC_LBL_NAME,IDC_LBL_EXE,IDC_LBL_ENABLED,IDC_LBL_VIB,IDC_LBL_HUE,IDC_LBL_BRI,IDC_LBL_CON,IDC_LBL_GAM,IDC_DEFAULTS,IDC_MINTRAY,IDC_CHECKUPDATES,IDC_FOOT_GITHUB,IDC_FOOT_SUPPORT,IDC_FOOT_ABOUT};
enum {ID_TRAY_OPEN=2001,ID_TRAY_CHECK_UPDATE,ID_TRAY_ABOUT,ID_TRAY_EXIT};

HINSTANCE gInst{}; HWND gWnd{}; HFONT gFont{},gFontBold{},gFontPanelTitle{},gFontTitle{},gFontSmall{},gFontHeaderButton{},gIconFont{}; HBRUSH gBackBrush{},gPanelBrush{},gPanel2Brush{},gFieldBrush{}; HICON gIcon{};
ULONG_PTR gGdiPlusToken{}; Gdiplus::Image* gHeaderImage{};
Gdiplus::Image *gSliderBrightness{},*gSliderContrast{},*gSliderGamma{},*gSliderVibrance{},*gSliderHue{},*gNvidiaDriverIcon{};
Settings gSettings; int gSelected=-1; std::wstring gActive=L"Windows", gStatus=L"Not initialized", gDriverVersion=L"--"; bool gStatusOk=false;
NOTIFYICONDATAW gNid{}; HMENU gTrayMenu{};
HWND gFooterHover{};
HWND gMainButtonHover{};
HWND gProfileTooltip{};
HWND gExeTooltip{};
bool gExeTooltipVisible=false;
HWND gResetTooltip{};
bool gResetTooltipVisible=false;
int gProfileTooltipItem=-1;
std::wstring gProfileTooltipText;
constexpr int TOOLTIP_GAP=3;

// Live preview state. Slider changes are applied asynchronously so NVAPI calls
// never block the UI thread. A generation counter invalidates stale previews.
CRITICAL_SECTION gNvApplyLock{};
CRITICAL_SECTION gPreviewLock{};
HANDLE gPreviewEvent{};
HANDLE gPreviewThread{};
volatile LONG gPreviewStop=0;
unsigned long long gPreviewGeneration=0;
bool gPreviewPending=false;
bool gPreviewDirty=false;
DisplayProfileValues gPreviewValues{};

using NvQueryInterface=void* (__cdecl*)(unsigned int);
using NvInit=int (__cdecl*)(); using NvUnload=int (__cdecl*)(); using NvEnumDisplay=int (__cdecl*)(int,void**);
struct DVCINFOEX { unsigned int version; int currentLevel,minLevel,maxLevel,defaultLevel; };
using NvGetDVC=int (__cdecl*)(void*,unsigned int,DVCINFOEX*); using NvSetDVC=int (__cdecl*)(void*,unsigned int,DVCINFOEX*);
struct HUEINFO { unsigned int version; unsigned int currentAngle; unsigned int defaultAngle; };
using NvGetHUE=int (__cdecl*)(void*,unsigned int,HUEINFO*);
using NvSetHUE=int (__cdecl*)(void*,unsigned int,unsigned int);
#pragma pack(push,1)
struct NV_GAMMA_CORRECTION_EX {
    unsigned int version;
    float gammaRamp[1024*3];
    unsigned int unknown;
};
#pragma pack(pop)
using NvGetPrimaryDisplayId=int (__cdecl*)(unsigned int*); using NvGetDriverAndBranchVersion=int (__cdecl*)(unsigned int*,char*);
using NvSetTargetGamma=int (__cdecl*)(unsigned int,NV_GAMMA_CORRECTION_EX*);
using NvGetAssociatedDisplayHandle=int (__cdecl*)(const char*,void**);
using NvGetDisplayIdByName=int (__cdecl*)(const char*,unsigned int*);

struct DisplayTarget {
    std::wstring gdiName;
    std::wstring label;
    void* handle{};
    unsigned int displayId{};
    bool primary{};
    std::wstring monitorId;
};

HMODULE gNv{}; NvUnload pUnload{}; NvGetDVC pGetDvc{}; NvSetDVC pSetDvc{}; NvGetHUE pGetHue{}; NvSetHUE pSetHue{};
NvGetPrimaryDisplayId pGetPrimaryDisplayId{}; NvSetTargetGamma pSetTargetGamma{}; NvGetDriverAndBranchVersion pGetDriverVersion{};
NvGetAssociatedDisplayHandle pGetAssociatedDisplayHandle{}; NvGetDisplayIdByName pGetDisplayIdByName{};
void* gDisplay{}; unsigned int gDisplayId{};
std::vector<DisplayTarget> gDisplays;

std::wstring AppDataFile(){
    wchar_t p[MAX_PATH]{};
    SHGetFolderPathW(nullptr,CSIDL_APPDATA,nullptr,SHGFP_TYPE_CURRENT,p);
    std::wstring d=std::wstring(p)+L"\\NvProfileSwitcher";
    CreateDirectoryW(d.c_str(),nullptr);
    return d+L"\\profiles.json";
}
std::string W2U(const std::wstring&s){ if(s.empty())return{}; int n=WideCharToMultiByte(CP_UTF8,0,s.c_str(),-1,nullptr,0,nullptr,nullptr); std::string r(n,0); WideCharToMultiByte(CP_UTF8,0,s.c_str(),-1,r.data(),n,nullptr,nullptr); r.pop_back(); return r; }
std::wstring U2W(const std::string&s){ if(s.empty())return{}; int n=MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,nullptr,0); std::wstring r(n,0); MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,r.data(),n); r.pop_back(); return r; }
std::string Escape(const std::wstring&w){ std::string s=W2U(w),o; for(char c:s){ if(c=='\\'||c=='\"')o+='\\'; o+=c;} return o; }
std::wstring Unescape(std::string s){ std::string o; for(size_t i=0;i<s.size();++i){ if(s[i]=='\\'&&i+1<s.size()){ char n=s[++i]; if(n=='n')o+='\n'; else if(n=='r')o+='\r'; else if(n=='t')o+='\t'; else o+=n;} else o+=s[i]; } return U2W(o); }
std::string ReadAll(const std::wstring&p){ std::ifstream f(p,std::ios::binary); if(!f)return{}; return {std::istreambuf_iterator<char>(f),{}}; }
std::string FieldS(const std::string&o,const char*k,const char*d=""){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\""); std::smatch m; return std::regex_search(o,m,r)?m[1].str():d; }
double FieldN(const std::string&o,const char*k,double d){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)"); std::smatch m; return std::regex_search(o,m,r)?std::stod(m[1].str()):d; }
bool FieldB(const std::string&o,const char*k,bool d){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*(true|false)"); std::smatch m; return std::regex_search(o,m,r)?m[1].str()=="true":d; }

size_t FindMatchingJson(const std::string& s,size_t openPos,char openCh,char closeCh){
    if(openPos==std::string::npos||openPos>=s.size()||s[openPos]!=openCh) return std::string::npos;
    int depth=0;
    bool inString=false, escaped=false;
    for(size_t i=openPos;i<s.size();++i){
        char c=s[i];
        if(inString){
            if(escaped) escaped=false;
            else if(c=='\\') escaped=true;
            else if(c=='"') inString=false;
            continue;
        }
        if(c=='"'){ inString=true; continue; }
        if(c==openCh) ++depth;
        else if(c==closeCh && --depth==0) return i;
    }
    return std::string::npos;
}

std::vector<std::string> JsonObjectsInArray(const std::string& s,size_t arrayOpen,size_t arrayClose){
    std::vector<std::string> out;
    if(arrayOpen==std::string::npos||arrayClose==std::string::npos||arrayOpen>=arrayClose) return out;
    size_t pos=arrayOpen+1;
    while(pos<arrayClose){
        size_t b=s.find('{',pos);
        if(b==std::string::npos||b>=arrayClose) break;
        size_t e=FindMatchingJson(s,b,'{','}');
        if(e==std::string::npos||e>arrayClose) break;
        out.push_back(s.substr(b,e-b+1));
        pos=e+1;
    }
    return out;
}

DisplayProfileValues ParseDisplayValues(const std::string& o){
    DisplayProfileValues v;
    v.displayName=Unescape(FieldS(o,"DisplayName",""));
    v.monitorId=Unescape(FieldS(o,"MonitorId",""));
    v.vibrance=(int)FieldN(o,"DigitalVibrance",50);
    v.hue=(int)FieldN(o,"Hue",0);
    v.brightness=FieldN(o,"Brightness",100.0);
    v.contrast=FieldN(o,"Contrast",100.0);
    v.gamma=FieldN(o,"Gamma",1.0);
    return v;
}

ApplicationProfile ParseProfile(const std::string&o){
    ApplicationProfile p;
    p.name=Unescape(FieldS(o,"Name","New Profile"));
    p.exePath=Unescape(FieldS(o,"ExePath"));
    p.enabled=FieldB(o,"Enabled",true);

    size_t dp=o.find("\"Display Profiles\"");
    if(dp!=std::string::npos){
        size_t a=o.find('[',dp);
        size_t b=FindMatchingJson(o,a,'[',']');
        if(a!=std::string::npos&&b!=std::string::npos){
            for(const auto& obj:JsonObjectsInArray(o,a,b)){
                DisplayProfileValues v=ParseDisplayValues(obj);
                if(!v.monitorId.empty()) p.displayProfiles.push_back(v);
            }
        }
    }
    return p;
}
bool SameMonitorId(const std::wstring&a,const std::wstring&b){
    return !a.empty()&&!b.empty()&&_wcsicmp(a.c_str(),b.c_str())==0;
}
ApplicationProfile* DesktopProfileForMonitor(const std::wstring&id){
    for(auto& p:gSettings.desktopProfiles)
        if(!p.displayProfiles.empty()&&SameMonitorId(p.displayProfiles.front().monitorId,id)) return &p;
    return nullptr;
}
const ApplicationProfile* DesktopProfileForMonitorConst(const std::wstring&id){
    for(const auto& p:gSettings.desktopProfiles)
        if(!p.displayProfiles.empty()&&SameMonitorId(p.displayProfiles.front().monitorId,id)) return &p;
    return nullptr;
}


ApplicationProfile DesktopTemplate(){
    ApplicationProfile p=gSettings.desktop;p.name=L"Windows";p.exePath=L"";p.enabled=true;return p;
}
DisplayProfileValues DefaultValuesForDisplay(const std::wstring&displayName,const std::wstring&monitorId=L""){
    DisplayProfileValues v;
    v.displayName=displayName;
    v.monitorId=monitorId;
    return v; // NVIDIA-neutral defaults: DV 50, Hue 0, Brightness/Contrast 100, Gamma 1.00
}
DisplayProfileValues* ApplicationValuesForMonitor(ApplicationProfile&p,const std::wstring&id){
    for(auto&v:p.displayProfiles)if(SameMonitorId(v.monitorId,id))return &v;
    return nullptr;
}

DisplayProfileValues ApplicationDefaultsForDisplay(const std::wstring&displayName,const std::wstring&monitorId=L""){
    if(!monitorId.empty()){
        if(const ApplicationProfile* desktop=DesktopProfileForMonitorConst(monitorId)){
            if(!desktop->displayProfiles.empty()){
                DisplayProfileValues v=desktop->displayProfiles.front();
                v.displayName=displayName;
                v.monitorId=monitorId;
                return v;
            }
        }
    }
    return DefaultValuesForDisplay(displayName,monitorId);
}

DisplayProfileValues* EnsureApplicationValuesForDisplay(ApplicationProfile&p,const std::wstring&displayName,const std::wstring&monitorId=L""){
    if(!monitorId.empty())if(auto*v=ApplicationValuesForMonitor(p,monitorId)){v->displayName=displayName;return v;}
    p.displayProfiles.push_back(ApplicationDefaultsForDisplay(displayName,monitorId));
    return &p.displayProfiles.back();
}
std::wstring WindowsProfileJsonName(const std::wstring& displayName){
    size_t pos=displayName.rfind(L"DISPLAY");
    if(pos!=std::wstring::npos){
        std::wstring number=displayName.substr(pos+7);
        if(!number.empty()) return L"Display "+number;
    }
    return L"Display";
}
void Save(){
    std::ofstream f(AppDataFile(),std::ios::binary|std::ios::trunc);

    auto dumpDisplay=[&](const DisplayProfileValues&v,int ind){
        std::string sp(ind,' ');
        f<<sp<<"{\n"
         <<sp<<"  \"DisplayName\": \""<<Escape(v.displayName)<<"\",\n"
         <<sp<<"  \"MonitorId\": \""<<Escape(v.monitorId)<<"\",\n"
         <<sp<<"  \"Brightness\": "<<v.brightness<<",\n"
         <<sp<<"  \"Contrast\": "<<v.contrast<<",\n"
         <<sp<<"  \"Gamma\": "<<v.gamma<<",\n"
         <<sp<<"  \"DigitalVibrance\": "<<v.vibrance<<",\n"
         <<sp<<"  \"Hue\": "<<v.hue<<"\n"
         <<sp<<"}";
    };

    f<<"{\n  \"Windows Profiles\": [\n";
    for(size_t i=0;i<gSettings.desktopProfiles.size();++i){
        const ApplicationProfile& p=gSettings.desktopProfiles[i];
        if(p.displayProfiles.empty()) continue;
        const auto& v=p.displayProfiles.front();
        f<<"    {\n"
         <<"      \"Name\": \""<<Escape(WindowsProfileJsonName(v.displayName))<<"\",\n"
         <<"      \"DisplayName\": \""<<Escape(v.displayName)<<"\",\n"
         <<"      \"MonitorId\": \""<<Escape(v.monitorId)<<"\",\n"
         <<"      \"Brightness\": "<<v.brightness<<",\n"
         <<"      \"Contrast\": "<<v.contrast<<",\n"
         <<"      \"Gamma\": "<<v.gamma<<",\n"
         <<"      \"DigitalVibrance\": "<<v.vibrance<<",\n"
         <<"      \"Hue\": "<<v.hue<<"\n"
         <<"    }";
        if(i+1<gSettings.desktopProfiles.size())f<<",";
        f<<"\n";
    }

    f<<"  ],\n  \"Application Profiles\": [\n";
    for(size_t i=0;i<gSettings.profiles.size();++i){
        const ApplicationProfile& p=gSettings.profiles[i];
        f<<"    {\n"
         <<"      \"Name\": \""<<Escape(p.name)<<"\",\n"
         <<"      \"ExePath\": \""<<Escape(p.exePath)<<"\",\n"
         <<"      \"Enabled\": "<<(p.enabled?"true":"false")<<",\n"
         <<"      \"Display Profiles\": [\n";
        for(size_t j=0;j<p.displayProfiles.size();++j){
            dumpDisplay(p.displayProfiles[j],8);
            if(j+1<p.displayProfiles.size())f<<",";
            f<<"\n";
        }
        f<<"      ]\n"
         <<"    }";
        if(i+1<gSettings.profiles.size())f<<",";
        f<<"\n";
    }

    f<<"  ],\n  \"StartWithWindows\": "<<(gSettings.startWindows?"true":"false")
     <<",\n  \"StartMinimized\": "<<(gSettings.startMinimized?"true":"false")
     <<",\n  \"MinimizeToTray\": "<<(gSettings.minimizeToTray?"true":"false")<<",\n  \"CheckForUpdates\": "<<(gSettings.checkUpdates?"true":"false")<<"\n}\n";
}
void Load(){
    std::string s=ReadAll(AppDataFile());
    if(s.empty()){Save();return;}
    gSettings.startWindows=FieldB(s,"StartWithWindows",false);
    gSettings.startMinimized=FieldB(s,"StartMinimized",false);
    gSettings.minimizeToTray=FieldB(s,"MinimizeToTray",false);
    gSettings.checkUpdates=FieldB(s,"CheckForUpdates",true);

    size_t wp=s.find("\"Windows Profiles\"");
    if(wp!=std::string::npos){
        size_t a=s.find('[',wp), b=FindMatchingJson(s,a,'[',']');
        if(a!=std::string::npos&&b!=std::string::npos){
            for(const auto& obj:JsonObjectsInArray(s,a,b)){
                DisplayProfileValues v=ParseDisplayValues(obj);
                if(v.monitorId.empty()) continue;
                ApplicationProfile p=DesktopTemplate();
                p.displayProfiles.push_back(v);
                gSettings.desktopProfiles.push_back(p);
            }
        }
    }

    size_t pr=s.find("\"Application Profiles\"");
    if(pr!=std::string::npos){
        size_t a=s.find('[',pr), b=FindMatchingJson(s,a,'[',']');
        if(a!=std::string::npos&&b!=std::string::npos){
            for(const auto& obj:JsonObjectsInArray(s,a,b))
                gSettings.profiles.push_back(ParseProfile(obj));
        }
    }
}
std::string NvDisplayNameA(const std::wstring& gdi){
    std::wstring n=gdi;
    // NvAPI docs commonly use "\\DISPLAY1", while Win32 returns "\\.\DISPLAY1".
    if(n.rfind(L"\\\\.\\",0)==0) n=L"\\\\"+n.substr(4);
    return W2U(n);
}


std::wstring EdidSerialFromRegistry(const std::wstring& hardware,const std::wstring& instance){
    std::wstring path=L"SYSTEM\\CurrentControlSet\\Enum\\DISPLAY\\"+hardware+L"\\"+instance+L"\\Device Parameters";
    HKEY k{};
    if(RegOpenKeyExW(HKEY_LOCAL_MACHINE,path.c_str(),0,KEY_READ,&k)!=ERROR_SUCCESS)return L"";

    DWORD type=0,size=0;
    if(RegQueryValueExW(k,L"EDID",nullptr,&type,nullptr,&size)!=ERROR_SUCCESS ||
       type!=REG_BINARY || size<128){
        RegCloseKey(k);
        return L"";
    }

    std::vector<BYTE> edid(size);
    if(RegQueryValueExW(k,L"EDID",nullptr,&type,edid.data(),&size)!=ERROR_SUCCESS){
        RegCloseKey(k);
        return L"";
    }
    RegCloseKey(k);

    // Preferred form: EDID monitor serial-number descriptor (tag 0xFF).
    // This is what most PC monitors expose as a readable serial string.
    for(size_t off=54;off+18<=edid.size() && off<126;off+=18){
        if(edid[off]==0 && edid[off+1]==0 && edid[off+2]==0 && edid[off+3]==0xFF){
            std::string serial;
            for(size_t i=off+5;i<off+18;i++){
                char c=(char)edid[i];
                if(c==0 || c=='\r' || c=='\n') break;
                serial.push_back(c);
            }
            while(!serial.empty() && (serial.back()==' ' || serial.back()=='\t'))
                serial.pop_back();

            if(!serial.empty())
                return U2W(serial);
        }
    }

    // Generic EDID fallback used by many TVs and some monitors:
    // bytes 12..15 contain the 32-bit numeric serial number, little-endian.
    // Do not use zero / all-ones values because vendors commonly use those
    // to indicate that no numeric serial is available.
    if(edid.size()>=16){
        unsigned int numericSerial=
            (unsigned int)edid[12] |
            ((unsigned int)edid[13]<<8) |
            ((unsigned int)edid[14]<<16) |
            ((unsigned int)edid[15]<<24);

        if(numericSerial!=0 && numericSerial!=0xFFFFFFFFu)
            return std::to_wstring(numericSerial);
    }

    return L"";
}
std::wstring StableMonitorIdForGdi(const std::wstring&gdi){
    UINT32 pathCount=0,modeCount=0;
    LONG e=GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&pathCount,&modeCount);
    if(e!=ERROR_SUCCESS)return L"";
    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
    e=QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&pathCount,paths.data(),&modeCount,modes.data(),nullptr);
    if(e!=ERROR_SUCCESS)return L"";
    paths.resize(pathCount);
    for(const auto&p:paths){
        DISPLAYCONFIG_SOURCE_DEVICE_NAME src{};
        src.header.type=DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        src.header.size=sizeof(src);
        src.header.adapterId=p.sourceInfo.adapterId;
        src.header.id=p.sourceInfo.id;
        if(DisplayConfigGetDeviceInfo(&src.header)!=ERROR_SUCCESS)continue;
        if(_wcsicmp(src.viewGdiDeviceName,gdi.c_str())!=0)continue;

        DISPLAYCONFIG_TARGET_DEVICE_NAME tgt{};
        tgt.header.type=DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        tgt.header.size=sizeof(tgt);
        tgt.header.adapterId=p.targetInfo.adapterId;
        tgt.header.id=p.targetInfo.id;
        if(DisplayConfigGetDeviceInfo(&tgt.header)!=ERROR_SUCCESS)return L"";

        // Example: \\?\DISPLAY#AUS275B#5&2987d1c0&3&UID4355#{...}
        std::wstring path=tgt.monitorDevicePath;
        size_t p1=path.find(L'#');
        size_t p2=p1==std::wstring::npos?std::wstring::npos:path.find(L'#',p1+1);
        size_t p3=p2==std::wstring::npos?std::wstring::npos:path.find(L'#',p2+1);
        if(p1==std::wstring::npos||p2==std::wstring::npos||p3==std::wstring::npos)return L"";
        std::wstring hardware=path.substr(p1+1,p2-p1-1);
        std::wstring instance=path.substr(p2+1,p3-p2-1);
        std::wstring serial=EdidSerialFromRegistry(hardware,instance);
        if(serial.empty())return L"";
        return hardware+L"-"+serial;
    }
    return L"";
}
DisplayTarget* TargetForMonitorId(const std::wstring&id){
    for(auto&d:gDisplays)if(SameMonitorId(d.monitorId,id))return &d;
    return nullptr;
}

void EnumerateNvDisplays(){
    gDisplays.clear();
    if(!pGetAssociatedDisplayHandle||!pGetDisplayIdByName) return;

    for(DWORD i=0;;++i){
        DISPLAY_DEVICEW dd{};
        dd.cb=sizeof(dd);
        if(!EnumDisplayDevicesW(nullptr,i,&dd,0)) break;
        if(!(dd.StateFlags&DISPLAY_DEVICE_ACTIVE) || (dd.StateFlags&DISPLAY_DEVICE_MIRRORING_DRIVER)) continue;

        std::wstring gdi=dd.DeviceName;
        std::string nvName=NvDisplayNameA(gdi);
        std::string gdiUtf8=W2U(gdi);

        void* handle=nullptr;
        unsigned int id=0;
        int hs=pGetAssociatedDisplayHandle(nvName.c_str(),&handle);
        if(hs!=0) hs=pGetAssociatedDisplayHandle(gdiUtf8.c_str(),&handle);
        int is=pGetDisplayIdByName(nvName.c_str(),&id);
        if(is!=0) is=pGetDisplayIdByName(gdiUtf8.c_str(),&id);
        if(hs!=0 || is!=0 || !handle || !id) continue;

        DISPLAY_DEVICEW mon{};
        mon.cb=sizeof(mon);
        std::wstring friendly;
        if(EnumDisplayDevicesW(dd.DeviceName,0,&mon,0) && mon.DeviceString[0])
            friendly=mon.DeviceString;
        if(friendly.empty() && dd.DeviceString[0]) friendly=dd.DeviceString;
        if(friendly.empty()) friendly=L"NVIDIA display";

        bool primary=(dd.StateFlags&DISPLAY_DEVICE_PRIMARY_DEVICE)!=0;

        std::wstring label=friendly;
        if(primary) label+=L" (Primary)";

        gDisplays.push_back({gdi,label,handle,id,primary,StableMonitorIdForGdi(gdi)});
    }

    if(gDisplays.empty() && gDisplay && gDisplayId){
        gDisplays.push_back({L"",L"Primary NVIDIA display",gDisplay,gDisplayId,true,L""});
    }

    // Keep the Windows primary display at the top of the combo box while
    // preserving the relative order of all other displays.
    std::stable_sort(gDisplays.begin(),gDisplays.end(),
        [](const DisplayTarget& a,const DisplayTarget& b){
            return a.primary && !b.primary;
        });

}

bool Apply(const ApplicationProfile& p,bool updateUi=true);
void DiscardPreview();

ApplicationProfile* EnsureDesktopProfile(const std::wstring&displayName,const std::wstring&monitorId=L""){
    if(!monitorId.empty())if(auto*p=DesktopProfileForMonitor(monitorId)){
        if(!p->displayProfiles.empty())p->displayProfiles.front().displayName=displayName;
        return p;
    }
    ApplicationProfile p=DesktopTemplate();
    p.displayProfiles.push_back(DefaultValuesForDisplay(displayName,monitorId));
    gSettings.desktopProfiles.push_back(p);
    return &gSettings.desktopProfiles.back();
}
ApplicationProfile* CurrentDesktopProfile(){
    if(gDisplays.empty())return &gSettings.desktop;
    int ds=(int)SendMessageW(GetDlgItem(gWnd,IDC_DISPLAY),CB_GETCURSEL,0,0);
    if(ds<0||ds>=(int)gDisplays.size()){for(size_t i=0;i<gDisplays.size();++i)if(gDisplays[i].primary){ds=(int)i;break;}if(ds<0)ds=0;}
    return EnsureDesktopProfile(gDisplays[ds].gdiName,gDisplays[ds].monitorId);
}
void RestoreAllDesktopProfiles(){
    for(const auto&d:gDisplays){
        const ApplicationProfile*p=!d.monitorId.empty()?DesktopProfileForMonitorConst(d.monitorId):nullptr;
        if(p)Apply(*p);
    }
}
void EnsureAllApplicationDisplayProfiles(){
    if(gDisplays.empty())return;
    for(auto&p:gSettings.profiles)
        for(const auto&d:gDisplays)
            EnsureApplicationValuesForDisplay(p,d.gdiName,d.monitorId);
}

void ApplyApplicationProfile(const ApplicationProfile& p){
    for(const auto& v:p.displayProfiles){
        DisplayTarget* t=TargetForMonitorId(v.monitorId);
        if(!t) continue;
        ApplicationProfile one=p;
        one.displayProfiles.clear();
        one.displayProfiles.push_back(v);
        Apply(one);
    }
}

DisplayTarget* TargetForProfile(const ApplicationProfile& p){
    if(!p.displayProfiles.empty()&&!p.displayProfiles.front().monitorId.empty())
        return TargetForMonitorId(p.displayProfiles.front().monitorId);
    return nullptr;
}

void RefreshDisplayCombo(const ApplicationProfile& p){
    HWND c=GetDlgItem(gWnd,IDC_DISPLAY);
    if(!c) return;
    SendMessageW(c,CB_RESETCONTENT,0,0);
    int selected=-1, primary=-1;
    for(size_t i=0;i<gDisplays.size();++i){
        SendMessageW(c,CB_ADDSTRING,0,(LPARAM)gDisplays[i].label.c_str());
        if(gDisplays[i].primary) primary=(int)i;
        if(!p.displayProfiles.empty()&&SameMonitorId(gDisplays[i].monitorId,p.displayProfiles.front().monitorId))
            selected=(int)i;
    }
    if(selected<0) selected=primary>=0?primary:(gDisplays.empty()?-1:0);
    if(selected>=0) SendMessageW(c,CB_SETCURSEL,selected,0);
}

bool InitNv(){
    gNv=LoadLibraryW(L"nvapi64.dll");
    if(!gNv){gStatus=L"NVIDIA driver / NVAPI not found";return false;}
    auto q=(NvQueryInterface)GetProcAddress(gNv,"nvapi_QueryInterface");
    if(!q){gStatus=L"nvapi_QueryInterface not found";return false;}
    auto init=(NvInit)q(0x0150E828);
    pUnload=(NvUnload)q(0xD22BDD7E);
    auto en=(NvEnumDisplay)q(0x9ABDD40D);
    pGetDvc=(NvGetDVC)q(0x0E45002D);
    pSetDvc=(NvSetDVC)q(0x4A82C2B1);
    pGetHue=(NvGetHUE)q(0x95B64341);
    pSetHue=(NvSetHUE)q(0xF5A0F22C);
    pGetPrimaryDisplayId=(NvGetPrimaryDisplayId)q(0x1E9D8A31);
    pSetTargetGamma=(NvSetTargetGamma)q(0x7082A053);
    pGetAssociatedDisplayHandle=(NvGetAssociatedDisplayHandle)q(0x35C29134);
    pGetDisplayIdByName=(NvGetDisplayIdByName)q(0xAE457190);
    pGetDriverVersion=(NvGetDriverAndBranchVersion)q(0x2926AAAD);
    if(!init||!en||!pGetDvc||!pSetDvc||!pGetHue||!pSetHue||!pGetPrimaryDisplayId||!pSetTargetGamma||init()!=0||en(0,&gDisplay)!=0||pGetPrimaryDisplayId(&gDisplayId)!=0){
        gStatus=L"Could not initialize NVIDIA display";
        return false;
    }
    EnumerateNvDisplays();
    if(pGetDriverVersion){
        unsigned int version=0;
        char branch[64]{};
        if(pGetDriverVersion(&version,branch)==0 && version>0){
            unsigned int major=version/100;
            unsigned int minor=version%100;
            wchar_t buf[32]{};
            swprintf_s(buf,L"%u.%02u",major,minor);
            gDriverVersion=buf;
        }
    }
    gStatus=L"Ready";
    return true;
}

void RefreshDriverVersion(){
    if(!pGetDriverVersion) return;

    unsigned int version=0;
    char branch[64]{};
    if(pGetDriverVersion(&version,branch)==0 && version>0){
        unsigned int major=version/100;
        unsigned int minor=version%100;
        wchar_t buf[32]{};
        swprintf_s(buf,L"%u.%02u",major,minor);

        if(gDriverVersion!=buf){
            gDriverVersion=buf;
            InvalidateRect(gWnd,nullptr,FALSE);
        }
    }
}

// Maps the NVIDIA App style 0..100 slider (50 = neutral) onto the actual
// DVC range reported by the driver. This mirrors NvAPIWrapper's normalized
// semantics without depending on NvAPIWrapper itself.
int DvcRawFromPercent(int percent,const DVCINFOEX& d){
    percent=std::clamp(percent,0,100);
    if(percent>=50){
        double t=(percent-50)/50.0;
        return (int)llround(d.defaultLevel+t*(d.maxLevel-d.defaultLevel));
    }
    double t=(50-percent)/50.0;
    return (int)llround(d.defaultLevel-t*(d.defaultLevel-d.minLevel));
}

bool SetNvGamma(unsigned int displayId,double bri,double con,double gam){
    if(!pSetTargetGamma||!displayId)return false;
    NV_GAMMA_CORRECTION_EX data{};
    data.version=(unsigned int)(sizeof(data)|(1u<<16));
    data.unknown=1;

    // UI/storage values intentionally match NVIDIA App:
    // Brightness 80..120, Contrast 80..120, Gamma 0.30..2.80.
    double brightnessRaw=std::clamp(bri,80.0,120.0);
    double contrastRaw=std::clamp(con,80.0,120.0);
    double gammaRaw=std::clamp(gam,0.3,2.8)*100.0;
    double contrastNorm=(contrastRaw-100.0)/100.0;
    double brightnessShift=(brightnessRaw-100.0)/100.0;
    double gammaInv=1.0/(gammaRaw/100.0);

    for(int i=0;i<1024;i++){
        double x=i/1023.0;
        double v;
        if(contrastNorm<=0.0) v=(contrastNorm+1.0)*(x-0.5);
        else v=(x-0.5)/std::max(1.0-contrastNorm,1e-6);
        v+=brightnessShift+0.5;
        v=std::clamp(v,0.0,1.0);
        v=pow(v,gammaInv);
        v=std::clamp(v,0.0,1.0);
        float f=(float)v;
        data.gammaRamp[i*3+0]=f;
        data.gammaRamp[i*3+1]=f;
        data.gammaRamp[i*3+2]=f;
    }
    return pSetTargetGamma(displayId,&data)==0;
}

bool ApplyUnlocked(const ApplicationProfile&p,bool updateUi){
    auto setStatus=[&](const wchar_t* status,bool ok){
        if(!updateUi) return;
        gStatus=status;
        gStatusOk=ok;
        InvalidateRect(gWnd,nullptr,FALSE);
    };

    if(!pSetDvc||!pGetDvc||!pSetHue||!pSetTargetGamma){
        setStatus(L"NVIDIA driver / NVAPI not initialized",false);
        return false;
    }
    if(p.displayProfiles.empty()){
        setStatus(L"Profile has no display values",false);
        return false;
    }
    const auto& v=p.displayProfiles.front();
    DisplayTarget* t=TargetForProfile(p);
    if(!t || !t->handle || !t->displayId){
        setStatus(L"Selected NVIDIA display is not available",false);
        return false;
    }
    DVCINFOEX d{};
    d.version=(unsigned int)(sizeof(d)|(1u<<16));
    if(pGetDvc(t->handle,0,&d)!=0){
        setStatus(L"Could not read Digital Vibrance",false);
        return false;
    }
    d.currentLevel=std::clamp(DvcRawFromPercent(v.vibrance,d),d.minLevel,d.maxLevel);
    if(pSetDvc(t->handle,0,&d)!=0){
        setStatus(L"Could not set Digital Vibrance",false);
        return false;
    }
    unsigned int hue=(unsigned int)(((v.hue%360)+360)%360);
    if(pSetHue(t->handle,0,hue)!=0){
        setStatus(L"Could not set Hue",false);
        return false;
    }
    if(!SetNvGamma(t->displayId,v.brightness,v.contrast,v.gamma)){
        setStatus(L"Could not set NVIDIA color LUT",false);
        return false;
    }
    setStatus(L"Ready",true);
    return true;
}

bool Apply(const ApplicationProfile&p,bool updateUi){
    EnterCriticalSection(&gNvApplyLock);
    bool ok=ApplyUnlocked(p,updateUi);
    LeaveCriticalSection(&gNvApplyLock);
    return ok;
}

std::wstring ProcessName(const std::wstring&p){ const wchar_t* n=PathFindFileNameW(p.c_str()); std::wstring s=n?n:L""; auto dot=s.find_last_of(L'.'); if(dot!=std::wstring::npos)s.resize(dot); return s; }
void LoadSelected();

std::wstring ForegroundProcessName(){
    HWND fg=GetForegroundWindow();
    if(!fg)return{};
    DWORD pid=0;
    GetWindowThreadProcessId(fg,&pid);
    if(!pid)return{};
    HANDLE hp=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
    if(!hp)return{};
    wchar_t path[32768]{};
    DWORD len=(DWORD)(sizeof(path)/sizeof(path[0]));
    std::wstring name;
    if(QueryFullProcessImageNameW(hp,0,path,&len)){
        name=ProcessName(path);
    }
    CloseHandle(hp);
    return name;
}
void CheckProcesses(){
    std::wstring fgName=ForegroundProcessName();
    ApplicationProfile* hit=nullptr;
    for(auto& p:gSettings.profiles){
        if(!p.enabled||p.exePath.empty())continue;
        if(_wcsicmp(ProcessName(p.exePath).c_str(),fgName.c_str())==0){
            hit=&p;
            break;
        }
    }
    std::wstring next=hit?hit->name:L"Windows";
    if(next!=gActive){
        if(hit){
            ApplyApplicationProfile(*hit);
        }else{
            // Restore every configured Windows display so each monitor returns
            // to its own saved desktop values.
            RestoreAllDesktopProfiles();
        }
        gActive=next;
        InvalidateRect(gWnd,nullptr,FALSE);
    }
}

void RefreshDisplayTopology(){
    DiscardPreview();

    // Windows can emit several display/device notifications while an HDMI/DP
    // switch or hot-plug is still settling. This function is called only after
    // the short debounce timer expires.
    EnumerateNvDisplays();

    // Persist every currently connected physical monitor immediately.
    // Stable MonitorId remains the identity; DISPLAYx is only the current route.
    for(const auto& d:gDisplays)
        EnsureDesktopProfile(d.gdiName,d.monitorId);

    // Add the newly connected monitor to every existing application profile as well.
    // Disconnected monitor profiles are intentionally kept in the JSON.
    EnsureAllApplicationDisplayProfiles();
    Save();

    // Rebuild the display selector/sliders using the new topology.
    LoadSelected();

    // Force the currently relevant Windows/application profile to be re-applied to
    // the refreshed set of displays, including a newly connected monitor.
    gActive.clear();
    CheckProcesses();
}
void SetStartup(bool on){
    HKEY k;
    if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,nullptr,0,KEY_SET_VALUE,nullptr,&k,nullptr)==ERROR_SUCCESS){
        if(on){
            wchar_t p[MAX_PATH]; GetModuleFileNameW(nullptr,p,MAX_PATH);
            std::wstring v=L"\""+std::wstring(p)+L"\" --minimized";
            RegSetValueExW(k,L"NvProfileSwitcher",0,REG_SZ,(BYTE*)v.c_str(),(DWORD)((v.size()+1)*sizeof(wchar_t)));
        }else{
            RegDeleteValueW(k,L"NvProfileSwitcher");
        }
        RegCloseKey(k);
    }
}

HWND H(int id){return GetDlgItem(gWnd,id);} void Txt(int id,const std::wstring&s){SetWindowTextW(H(id),s.c_str());} std::wstring GetTxt(int id){int n=GetWindowTextLengthW(H(id));std::wstring s(n+1,0);GetWindowTextW(H(id),s.data(),n+1);s.resize(n);return s;}
HWND Add(const wchar_t*cls,const wchar_t*txt,DWORD style,int x,int y,int w,int h,int id){ HWND c=CreateWindowExW(0,cls,txt,WS_CHILD|WS_VISIBLE|style,x,y,w,h,gWnd,(HMENU)(INT_PTR)id,gInst,nullptr); SendMessageW(c,WM_SETFONT,(WPARAM)gFont,TRUE); return c; }

void FillRound(HDC dc,const RECT&r,COLORREF fill,COLORREF border,int radius);

void AddRoundedRectPath(Gdiplus::GraphicsPath& path,const Gdiplus::RectF& r,
                        Gdiplus::REAL radius){
    const Gdiplus::REAL d=radius*2.0f;
    path.StartFigure();
    path.AddArc(r.X,r.Y,d,d,180.0f,90.0f);
    path.AddArc(r.GetRight()-d,r.Y,d,d,270.0f,90.0f);
    path.AddArc(r.GetRight()-d,r.GetBottom()-d,d,d,0.0f,90.0f);
    path.AddArc(r.X,r.GetBottom()-d,d,d,90.0f,90.0f);
    path.CloseFigure();
}

void DrawMainButtonSurface(HDC dc,const RECT& r,bool accent,bool hover,bool down,
                           bool disabled){
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    g.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);

    const Gdiplus::REAL width=(Gdiplus::REAL)(r.right-r.left)-1.0f;
    const Gdiplus::REAL height=(Gdiplus::REAL)(r.bottom-r.top)-1.0f;
    Gdiplus::RectF bounds((Gdiplus::REAL)r.left+0.5f,(Gdiplus::REAL)r.top+0.5f,
                          width,height);
    Gdiplus::GraphicsPath path;
    AddRoundedRectPath(path,bounds,6.0f);

    Gdiplus::Color top;
    Gdiplus::Color bottom;
    Gdiplus::Color outline;
    if(accent){
        top=Gdiplus::Color(255,hover?48:39,hover?132:112,hover?54:45);
        bottom=Gdiplus::Color(255,hover?25:21,hover?91:78,hover?31:27);
        outline=Gdiplus::Color(255,hover?91:73,hover?218:188,hover?72:60);
    }else{
        top=Gdiplus::Color(255,hover?42:34,hover?49:40,hover?56:46);
        bottom=Gdiplus::Color(255,hover?29:24,hover?35:29,hover?41:34);
        outline=Gdiplus::Color(255,hover?83:64,hover?94:73,hover?104:82);
    }
    if(down) std::swap(top,bottom);
    if(disabled){
        top=Gdiplus::Color(255,28,33,38);
        bottom=Gdiplus::Color(255,22,27,31);
        outline=Gdiplus::Color(255,49,56,63);
    }

    Gdiplus::LinearGradientBrush fill(
        Gdiplus::PointF(bounds.X,bounds.Y),
        Gdiplus::PointF(bounds.X,bounds.GetBottom()),top,bottom);
    g.FillPath(&fill,&path);

    Gdiplus::Pen border(outline,1.0f);
    g.DrawPath(&border,&path);

    Gdiplus::Pen topEdge(
        accent?Gdiplus::Color(95,126,226,111):Gdiplus::Color(85,105,116,126),
        0.8f);
    topEdge.SetStartCap(Gdiplus::LineCapRound);
    topEdge.SetEndCap(Gdiplus::LineCapRound);
    g.DrawLine(&topEdge,bounds.X+6.0f,bounds.Y+1.2f,
               bounds.GetRight()-6.0f,bounds.Y+1.2f);
}

LRESULT CALLBACK MainButtonHoverSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                             UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_MOUSEMOVE:{
        if(gMainButtonHover!=hwnd){
            HWND old=gMainButtonHover;
            gMainButtonHover=hwnd;
            if(old) InvalidateRect(old,nullptr,FALSE);
            InvalidateRect(hwnd,nullptr,FALSE);
        }
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_MOUSELEAVE:
        if(gMainButtonHover==hwnd){
            gMainButtonHover=nullptr;
            InvalidateRect(hwnd,nullptr,FALSE);
        }
        break;
    case WM_NCDESTROY:
        if(gMainButtonHover==hwnd) gMainButtonHover=nullptr;
        RemoveWindowSubclass(hwnd,MainButtonHoverSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void StyleMainButton(HWND hwnd){
    if(hwnd) SetWindowSubclass(hwnd,MainButtonHoverSubclassProc,2,0);
}

LRESULT CALLBACK FlatCheckboxSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                          UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case BM_SETCHECK:{
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        InvalidateRect(hwnd,nullptr,TRUE);
        return result;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:{
        PAINTSTRUCT ps{};
        HDC dc=BeginPaint(hwnd,&ps);
        RECT r{};
        GetClientRect(hwnd,&r);

        HBRUSH bg=CreateSolidBrush(C_PANEL);
        FillRect(dc,&r,bg);
        DeleteObject(bg);

        const bool checked=SendMessageW(hwnd,BM_GETCHECK,0,0)==BST_CHECKED;

        // Keep the proven 16x16 geometry, but render it with antialiasing and
        // restrained depth so it matches the rest of the mockup-style UI.
        if(gGdiPlusToken){
            Gdiplus::Graphics g(dc);
            g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
            g.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);

            Gdiplus::GraphicsPath shadowPath;
            AddRoundedRectPath(shadowPath,Gdiplus::RectF(3.5f,4.3f,15.0f,15.0f),3.2f);
            Gdiplus::SolidBrush shadow(Gdiplus::Color(80,0,0,0));
            g.FillPath(&shadow,&shadowPath);

            Gdiplus::RectF box(3.5f,3.5f,15.0f,15.0f);
            Gdiplus::GraphicsPath boxPath;
            AddRoundedRectPath(boxPath,box,3.2f);

            const Gdiplus::Color top=checked?Gdiplus::Color(255,82,196,76)
                                              :Gdiplus::Color(255,37,44,51);
            const Gdiplus::Color bottom=checked?Gdiplus::Color(255,55,164,60)
                                                 :Gdiplus::Color(255,23,29,34);
            Gdiplus::LinearGradientBrush fill(
                Gdiplus::PointF(box.X,box.Y),
                Gdiplus::PointF(box.X,box.GetBottom()),top,bottom);
            g.FillPath(&fill,&boxPath);

            Gdiplus::Pen border(
                checked?Gdiplus::Color(255,43,145,49):Gdiplus::Color(255,59,69,78),
                1.0f);
            g.DrawPath(&border,&boxPath);

            Gdiplus::Pen highlight(
                checked?Gdiplus::Color(145,132,231,124):Gdiplus::Color(90,79,89,98),
                0.8f);
            highlight.SetStartCap(Gdiplus::LineCapRound);
            highlight.SetEndCap(Gdiplus::LineCapRound);
            g.DrawLine(&highlight,6.0f,4.6f,16.0f,4.6f);

            if(checked){
                Gdiplus::Pen check(Gdiplus::Color(255,12,48,17),1.65f);
                check.SetStartCap(Gdiplus::LineCapRound);
                check.SetEndCap(Gdiplus::LineCapRound);
                check.SetLineJoin(Gdiplus::LineJoinRound);
                Gdiplus::PointF points[]{
                    {6.5f,11.0f},{9.5f,14.0f},{16.0f,7.5f}
                };
                g.DrawLines(&check,points,3);
            }
        }else{
            RECT box{3,3,19,19};
            FillRound(dc,box,checked?RGB(63,177,67):RGB(25,31,36),
                checked?RGB(43,145,49):RGB(59,69,78),4);
        }

        EndPaint(hwnd,&ps);
        return 0;
    }
    case WM_NCDESTROY:
        RemoveWindowSubclass(hwnd,FlatCheckboxSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void StyleFlatCheckbox(HWND hwnd){
    if(!hwnd) return;
    SetWindowTheme(hwnd,L"",L"");
    SetWindowSubclass(hwnd,FlatCheckboxSubclassProc,1,0);
    InvalidateRect(hwnd,nullptr,TRUE);
}

void DrawComboArrow(HDC dc,const RECT& r){
    HPEN pen=CreatePen(PS_SOLID,2,C_MUTED);
    HGDIOBJ old=SelectObject(dc,pen);
    const int cx=r.right-18;
    const int cy=(r.top+r.bottom)/2;
    MoveToEx(dc,cx-4,cy-2,nullptr);
    LineTo(dc,cx,cy+2);
    LineTo(dc,cx+4,cy-2);
    SelectObject(dc,old);
    DeleteObject(pen);
}

LRESULT CALLBACK FlatComboSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                       UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:{
        PAINTSTRUCT ps{};
        HDC dc=BeginPaint(hwnd,&ps);
        RECT r{};
        GetClientRect(hwnd,&r);

        FillRound(dc,r,C_FIELD,C_BORDER,8);

        int sel=(int)SendMessageW(hwnd,CB_GETCURSEL,0,0);
        if(sel>=0){
            wchar_t txt[256]{};
            SendMessageW(hwnd,CB_GETLBTEXT,sel,(LPARAM)txt);
            RECT tr=r;
            tr.left+=12;
            tr.right-=38;
            tr.top+=1;
            SetBkMode(dc,TRANSPARENT);
            SetTextColor(dc,C_TEXT);
            SelectObject(dc,gFont);
            DrawTextW(dc,txt,-1,&tr,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
        }
        DrawComboArrow(dc,r);

        EndPaint(hwnd,&ps);
        return 0;
    }
    case WM_NCDESTROY:
        RemoveWindowSubclass(hwnd,FlatComboSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void StyleFlatCombo(HWND hwnd){
    if(!hwnd) return;
    SetWindowTheme(hwnd,L"",L"");
    SetWindowSubclass(hwnd,FlatComboSubclassProc,1,0);
    InvalidateRect(hwnd,nullptr,TRUE);
}

void RefreshList(){ HWND l=H(IDC_LIST); SendMessageW(l,LB_RESETCONTENT,0,0); SendMessageW(l,LB_ADDSTRING,0,(LPARAM)gSettings.desktop.name.c_str()); for(auto&p:gSettings.profiles)SendMessageW(l,LB_ADDSTRING,0,(LPARAM)p.name.c_str()); int maxSel=(int)gSettings.profiles.size(); gSelected=std::clamp(gSelected,0,maxSel); SendMessageW(l,LB_SETCURSEL,gSelected,0); }

bool ProfileTitleIsTruncated(int item){
    HWND list=H(IDC_LIST);
    if(!list || item<0 || item>(int)gSettings.profiles.size()) return false;

    const wchar_t* title=item==0?L"Windows":gSettings.profiles[item-1].name.c_str();
    RECT itemRect{};
    if(SendMessageW(list,LB_GETITEMRECT,item,(LPARAM)&itemRect)==LB_ERR) return false;

    HDC dc=GetDC(list);
    if(!dc) return false;
    HFONT oldFont=(HFONT)SelectObject(dc,gFontBold);
    SIZE titleSize{};
    GetTextExtentPoint32W(dc,title,(int)wcslen(title),&titleSize);
    SelectObject(dc,oldFont);
    ReleaseDC(list,dc);

    const int textLeft=itemRect.left+16+58;
    const int textRight=itemRect.right-8;
    return titleSize.cx>(textRight-textLeft);
}

void HideProfileTooltip(){
    if(gProfileTooltip){
        TOOLINFOW ti{sizeof(ti)};
        ti.hwnd=H(IDC_LIST);
        ti.uId=1;
        SendMessageW(gProfileTooltip,TTM_TRACKACTIVATE,FALSE,(LPARAM)&ti);
    }
    gProfileTooltipItem=-1;
}

void UpdateProfileTooltip(POINT clientPt){
    HWND list=H(IDC_LIST);
    if(!list || !gProfileTooltip) return;

    LRESULT hit=SendMessageW(list,LB_ITEMFROMPOINT,0,MAKELPARAM(clientPt.x,clientPt.y));
    int item=LOWORD(hit);
    BOOL outside=HIWORD(hit);

    if(outside || item<0 || item>(int)gSettings.profiles.size() || !ProfileTitleIsTruncated(item)){
        HideProfileTooltip();
        return;
    }
    if(item==gProfileTooltipItem) return;

    gProfileTooltipText=item==0?L"Windows":gSettings.profiles[item-1].name;

    RECT itemRect{};
    SendMessageW(list,LB_GETITEMRECT,item,(LPARAM)&itemRect);
    POINT screenPt{itemRect.left+66,itemRect.bottom+TOOLTIP_GAP};
    ClientToScreen(list,&screenPt);

    TOOLINFOW ti{sizeof(ti)};
    ti.hwnd=list;
    ti.uId=1;
    ti.lpszText=(LPWSTR)gProfileTooltipText.c_str();
    SendMessageW(gProfileTooltip,TTM_UPDATETIPTEXTW,0,(LPARAM)&ti);
    SendMessageW(gProfileTooltip,TTM_TRACKPOSITION,0,MAKELPARAM(screenPt.x,screenPt.y));
    SendMessageW(gProfileTooltip,TTM_TRACKACTIVATE,TRUE,(LPARAM)&ti);

    // The tooltip is custom-painted, so explicitly size the popup to the
    // complete profile name instead of relying on the native tooltip layout.
    HDC tipDc=GetDC(gProfileTooltip);
    if(tipDc){
        HFONT oldFont=(HFONT)SelectObject(tipDc,gFont);
        SIZE textSize{};
        GetTextExtentPoint32W(tipDc,gProfileTooltipText.c_str(),
            (int)gProfileTooltipText.size(),&textSize);
        SelectObject(tipDc,oldFont);
        ReleaseDC(gProfileTooltip,tipDc);

        const int tipW=std::min(500,(int)textSize.cx+16);
        const int tipH=textSize.cy+10;
        SetWindowPos(gProfileTooltip,HWND_TOPMOST,screenPt.x,screenPt.y,
            tipW,tipH,SWP_NOACTIVATE|SWP_SHOWWINDOW);
    }

    gProfileTooltipItem=item;
}

LRESULT CALLBACK ProfileTooltipSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                           UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_WINDOWPOSCHANGED:
    case WM_SIZE:{
        RECT r{};
        GetClientRect(hwnd,&r);
        if(r.right>0 && r.bottom>0){
            HRGN region=CreateRoundRectRgn(0,0,r.right+1,r.bottom+1,5,5);
            SetWindowRgn(hwnd,region,TRUE); // Windows owns the region after success.
        }
        break;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:{
        PAINTSTRUCT ps{};
        HDC dc=BeginPaint(hwnd,&ps);
        RECT r{};
        GetClientRect(hwnd,&r);

        HBRUSH bg=CreateSolidBrush(C_PANEL2);
        HPEN border=CreatePen(PS_SOLID,1,C_BORDER);
        HGDIOBJ oldBrush=SelectObject(dc,bg);
        HGDIOBJ oldPen=SelectObject(dc,border);
        RoundRect(dc,r.left,r.top,r.right,r.bottom,5,5);
        SelectObject(dc,oldBrush);
        SelectObject(dc,oldPen);
        DeleteObject(bg);
        DeleteObject(border);

        wchar_t text[512]{};
        GetWindowTextW(hwnd,text,(int)(sizeof(text)/sizeof(text[0])));
        RECT tr=r;
        tr.left+=8; tr.right-=8; tr.top+=5; tr.bottom-=5;
        SetBkMode(dc,TRANSPARENT);
        SetTextColor(dc,C_TEXT);
        SelectObject(dc,gFont);
        DrawTextW(dc,text,-1,&tr,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);

        EndPaint(hwnd,&ps);
        return 0;
    }
    case WM_NCDESTROY:
        RemoveWindowSubclass(hwnd,ProfileTooltipSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}


bool ExecutablePathIsTruncated(){
    HWND edit=H(IDC_EXE);
    if(!edit || !IsWindowVisible(edit)) return false;

    std::wstring path=GetTxt(IDC_EXE);
    if(path.empty()) return false;

    RECT r{};
    GetClientRect(edit,&r);

    HDC dc=GetDC(edit);
    if(!dc) return false;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    SIZE sz{};
    GetTextExtentPoint32W(dc,path.c_str(),(int)path.size(),&sz);
    SelectObject(dc,old);
    ReleaseDC(edit,dc);

    const int available=(r.right-r.left)-16;
    return sz.cx>available;
}

void HideExecutableTooltip(){
    if(gExeTooltip && gExeTooltipVisible){
        ShowWindow(gExeTooltip,SW_HIDE);
        gExeTooltipVisible=false;
    }
}

void UpdateExecutableTooltip(){
    HWND edit=H(IDC_EXE);
    if(!edit || !gExeTooltip || !ExecutablePathIsTruncated()){
        HideExecutableTooltip();
        return;
    }

    std::wstring path=GetTxt(IDC_EXE);
    if(path.empty()){
        HideExecutableTooltip();
        return;
    }

    RECT er{};
    GetWindowRect(edit,&er);

    HDC dc=GetDC(gExeTooltip);
    if(!dc) return;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    SIZE sz{};
    GetTextExtentPoint32W(dc,path.c_str(),(int)path.size(),&sz);
    SelectObject(dc,old);
    ReleaseDC(gExeTooltip,dc);

    const int tipW=std::min(700,(int)sz.cx+16);
    const int tipH=sz.cy+10;
    int x=er.left;
    // The EDIT's rendered lower edge extends beyond the rectangle used to
    // position this popup. Add 7 px so its visible gap matches the other
    // tooltips.
    int y=er.bottom+TOOLTIP_GAP+7;

    HMONITOR mon=MonitorFromWindow(edit,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};
    if(GetMonitorInfoW(mon,&mi)){
        if(x+tipW>mi.rcWork.right) x=std::max((int)mi.rcWork.left,(int)mi.rcWork.right-tipW);
        if(x<mi.rcWork.left) x=mi.rcWork.left;
        y=std::clamp(y,(int)mi.rcWork.top,(int)mi.rcWork.bottom-tipH);
    }

    SetWindowTextW(gExeTooltip,path.c_str());
    SetWindowPos(gExeTooltip,HWND_TOPMOST,x,y,tipW,tipH,
        SWP_NOACTIVATE|SWP_SHOWWINDOW);
    // Unlike the fixed Reset tooltip, this popup receives its text while it is
    // still 0x0. Force the shared tooltip painter to redraw the complete
    // background, border and text after the final size has been applied.
    RedrawWindow(gExeTooltip,nullptr,nullptr,
        RDW_INVALIDATE|RDW_ERASE|RDW_FRAME|RDW_UPDATENOW);
    gExeTooltipVisible=true;
}

LRESULT CALLBACK ExecutableEditSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                            UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_MOUSEMOVE:{
        UpdateExecutableTooltip();
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_MOUSELEAVE:
        HideExecutableTooltip();
        break;

    case WM_SETFOCUS:
        HideExecutableTooltip();
        InvalidateRect(hwnd,nullptr,TRUE);
        break;

    case WM_KILLFOCUS:
    case WM_SETTEXT:
        HideExecutableTooltip();
        InvalidateRect(hwnd,nullptr,TRUE);
        break;

    case WM_ERASEBKGND:
        if(GetFocus()!=hwnd) return 1;
        break;

    case WM_PAINT:
        if(GetFocus()!=hwnd){
            PAINTSTRUCT ps{};
            HDC dc=BeginPaint(hwnd,&ps);
            RECT r{};
            GetClientRect(hwnd,&r);

            HBRUSH bg=CreateSolidBrush(C_FIELD);
            FillRect(dc,&r,bg);
            DeleteObject(bg);

            std::wstring path=GetTxt(IDC_EXE);
            RECT tr=r;
            tr.left+=8;
            tr.right-=8;

            SetBkMode(dc,TRANSPARENT);
            SetTextColor(dc,C_TEXT);
            SelectObject(dc,gFont);
            DrawTextW(dc,path.c_str(),-1,&tr,
                DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);

            EndPaint(hwnd,&ps);
            return 0;
        }
        break;

    case WM_NCDESTROY:
        HideExecutableTooltip();
        RemoveWindowSubclass(hwnd,ExecutableEditSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void HideResetTooltip(){
    if(gResetTooltip && gResetTooltipVisible){
        ShowWindow(gResetTooltip,SW_HIDE);
        gResetTooltipVisible=false;
    }
}

void UpdateResetTooltip(){
    if(!gResetTooltip) return;
    HWND reset=H(IDC_DEFAULTS);
    if(!reset) return;

    RECT rr{};
    GetWindowRect(reset,&rr);

    static const wchar_t* text=L"Reset to NVIDIA defaults";
    HDC dc=GetDC(gResetTooltip);
    if(!dc) return;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    SIZE sz{};
    GetTextExtentPoint32W(dc,text,(int)wcslen(text),&sz);
    SelectObject(dc,old);
    ReleaseDC(gResetTooltip,dc);

    const int tipW=sz.cx+16;
    const int tipH=sz.cy+10;

    // Match the profile-name and executable tooltip placement.
    int x=rr.left;
    int y=rr.bottom+TOOLTIP_GAP;

    HMONITOR mon=MonitorFromWindow(reset,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};
    if(GetMonitorInfoW(mon,&mi)){
        if(x<mi.rcWork.left) x=rr.left;
        y=std::clamp(y,(int)mi.rcWork.top,(int)mi.rcWork.bottom-tipH);
    }

    SetWindowPos(gResetTooltip,HWND_TOPMOST,x,y,tipW,tipH,
        SWP_NOACTIVATE|SWP_SHOWWINDOW);
    gResetTooltipVisible=true;
}

LRESULT CALLBACK ResetButtonSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                        UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_MOUSEMOVE:{
        UpdateResetTooltip();
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_MOUSELEAVE:
        HideResetTooltip();
        break;
    case WM_NCDESTROY:
        HideResetTooltip();
        RemoveWindowSubclass(hwnd,ResetButtonSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

LRESULT CALLBACK ProfileListSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                        UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_MOUSEMOVE:{
        POINT pt{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
        UpdateProfileTooltip(pt);
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_MOUSELEAVE:
        HideProfileTooltip();
        break;
    case WM_NCDESTROY:
        HideProfileTooltip();
        RemoveWindowSubclass(hwnd,ProfileListSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void UpdateSliderLabels(){
    Txt(IDC_VALVIB,std::to_wstring((int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0))+L"%");
    Txt(IDC_VALHUE,std::to_wstring((int)SendMessageW(H(IDC_HUE),TBM_GETPOS,0,0))+L"\x00B0");
    Txt(IDC_VALBRI,std::to_wstring((int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0)));
    Txt(IDC_VALCON,std::to_wstring((int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0)));
    int gp=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0);
    wchar_t b[32]; swprintf_s(b,L"%.2f",gp/100.0); Txt(IDC_VALGAM,b);
}
void RedrawAllSliders(){
    const int ids[]={IDC_VIB,IDC_HUE,IDC_BRI,IDC_CON,IDC_GAM};
    for(int id:ids){
        HWND h=H(id);
        if(h) RedrawWindow(h,nullptr,nullptr,RDW_INVALIDATE|RDW_ERASE|RDW_UPDATENOW|RDW_ALLCHILDREN);
    }
}

void LoadValuesToSliders(const DisplayProfileValues& v){
    SendMessageW(H(IDC_VIB),TBM_SETPOS,TRUE,v.vibrance);
    SendMessageW(H(IDC_HUE),TBM_SETPOS,TRUE,v.hue);
    SendMessageW(H(IDC_BRI),TBM_SETPOS,TRUE,(LPARAM)llround(v.brightness));
    SendMessageW(H(IDC_CON),TBM_SETPOS,TRUE,(LPARAM)llround(v.contrast));
    SendMessageW(H(IDC_GAM),TBM_SETPOS,TRUE,(LPARAM)llround(v.gamma*100));
    UpdateSliderLabels();
    RedrawAllSliders();
}

DisplayProfileValues SliderValuesForSelectedDisplay(){
    DisplayProfileValues v;
    int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);
    if(ds>=0&&ds<(int)gDisplays.size()){
        v.displayName=gDisplays[ds].gdiName;
        v.monitorId=gDisplays[ds].monitorId;
    }
    v.vibrance=(int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0);
    v.hue=(int)SendMessageW(H(IDC_HUE),TBM_GETPOS,0,0);
    v.brightness=(double)(int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0);
    v.contrast=(double)(int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0);
    v.gamma=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0)/100.0;
    return v;
}

void CancelPendingPreview(){
    EnterCriticalSection(&gPreviewLock);
    ++gPreviewGeneration;
    gPreviewPending=false;
    LeaveCriticalSection(&gPreviewLock);
}

void ReapplyRealColors(){
    std::wstring fgName=ForegroundProcessName();

    for(const auto& p:gSettings.profiles){
        if(!p.enabled||p.exePath.empty()) continue;
        if(_wcsicmp(ProcessName(p.exePath).c_str(),fgName.c_str())==0){
            ApplyApplicationProfile(p);
            gActive=p.name;
            return;
        }
    }

    RestoreAllDesktopProfiles();
    gActive=L"Windows";
}

void DiscardPreview(){
    bool dirty=false;
    EnterCriticalSection(&gPreviewLock);
    dirty=gPreviewDirty;
    gPreviewDirty=false;
    ++gPreviewGeneration;
    gPreviewPending=false;
    LeaveCriticalSection(&gPreviewLock);

    if(dirty) ReapplyRealColors();
}

DWORD WINAPI PreviewThreadProc(LPVOID){
    while(WaitForSingleObject(gPreviewEvent,INFINITE)==WAIT_OBJECT_0){
        if(InterlockedCompareExchange(&gPreviewStop,0,0)!=0) break;

        for(;;){
            DisplayProfileValues values;
            unsigned long long generation=0;
            bool have=false;

            EnterCriticalSection(&gPreviewLock);
            if(gPreviewPending){
                values=gPreviewValues;
                generation=gPreviewGeneration;
                gPreviewPending=false;
                have=true;
            }
            LeaveCriticalSection(&gPreviewLock);

            if(!have) break;

            ApplicationProfile preview;
            preview.name=L"Preview";
            preview.displayProfiles.push_back(values);

            // Serialize the generation check with the actual NVAPI apply.
            // This prevents a stale preview that was already queued from
            // applying after DiscardPreview() has restored the real profile.
            EnterCriticalSection(&gNvApplyLock);
            EnterCriticalSection(&gPreviewLock);
            bool current=(generation==gPreviewGeneration);
            LeaveCriticalSection(&gPreviewLock);

            if(current)
                ApplyUnlocked(preview,false);

            LeaveCriticalSection(&gNvApplyLock);
        }
    }
    return 0;
}

void RequestPreview(){
    DisplayProfileValues values=SliderValuesForSelectedDisplay();
    if(values.monitorId.empty()) return;

    EnterCriticalSection(&gPreviewLock);
    ++gPreviewGeneration;
    gPreviewValues=values;
    gPreviewPending=true;
    gPreviewDirty=true;
    LeaveCriticalSection(&gPreviewLock);

    if(gPreviewEvent) SetEvent(gPreviewEvent);
}

void ResetSlidersToDefaults(){
    int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);
    DisplayProfileValues v;
    if(ds>=0&&ds<(int)gDisplays.size()){
        v.displayName=gDisplays[ds].gdiName;
        v.monitorId=gDisplays[ds].monitorId;
    }
    LoadValuesToSliders(v);
    RequestPreview();
}

DisplayProfileValues ValuesFromFlatProfile(const ApplicationProfile& p){
    return p.displayProfiles.empty()?DisplayProfileValues{}:p.displayProfiles.front();
}
bool IsDesktopSelected(){return gSelected==0;}
ApplicationProfile* SelectedProfile(){ if(gSelected==0)return CurrentDesktopProfile(); int i=gSelected-1; return (i>=0&&i<(int)gSettings.profiles.size())?&gSettings.profiles[i]:nullptr; }

void SetDesktopUi(bool desktop){
    RECT r{}; GetClientRect(gWnd,&r);

    const int margin=18;
    const int leftW=360;
    const int gap=14;
    const int settingsW=330;
    const int centerPanelX=margin+leftW+gap;
    const int centerPanelW=r.right-(margin*2)-leftW-settingsW-(gap*2);
    const int rightX=centerPanelX+22;
    const int rightW=centerPanelW-44;

    int showApplication=desktop?SW_HIDE:SW_SHOW;
    for(int id:{IDC_LBL_NAME,IDC_NAME,IDC_LBL_EXE,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_LBL_ENABLED})
        ShowWindow(H(id),showApplication);
    ShowWindow(H(IDC_REMOVE),desktop?SW_HIDE:SW_SHOW);

    const int yDisplay=desktop?154:320;
    const int yBri=desktop?230:406;
    const int yCon=desktop?294:474;
    const int yGam=desktop?358:542;
    const int yVib=desktop?422:610;
    const int yHue=desktop?486:678;
    const int ySave=desktop?550:742;

    MoveWindow(H(IDC_LBL_DISPLAY),rightX+31,yDisplay,150,22,TRUE);
    MoveWindow(H(IDC_DISPLAY),rightX,yDisplay+24,rightW,34,TRUE);

    const int labelX=rightX+30;
    const int labelW=154;
    const int trackX=rightX+190;
    const int valueW=76;
    const int valueX=rightX+rightW-valueW;
    const int trackW=valueX-trackX-16;

    struct SPos{int lbl,track,val,y;};
    for(auto sp:std::vector<SPos>{
        {IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,yBri},
        {IDC_LBL_CON,IDC_CON,IDC_VALCON,yCon},
        {IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,yGam},
        {IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,yVib},
        {IDC_LBL_HUE,IDC_HUE,IDC_VALHUE,yHue}
    }){
        MoveWindow(H(sp.lbl),labelX,sp.y-2,labelW,22,TRUE);
        MoveWindow(H(sp.track),trackX,sp.y-4,trackW,28,TRUE);
        MoveWindow(H(sp.val),valueX,sp.y-5,valueW,28,TRUE);
    }

    MoveWindow(H(IDC_DEFAULTS),rightX,ySave,132,38,TRUE);
    MoveWindow(H(IDC_SAVE),rightX+rightW-160,ySave,160,38,TRUE);

    const int appX=centerPanelX+centerPanelW+gap+22;
    MoveWindow(H(IDC_STARTWIN),appX,150,22,22,TRUE);
    MoveWindow(GetWindow(H(IDC_STARTWIN),GW_HWNDNEXT),appX+27,150,220,22,TRUE);
    MoveWindow(H(IDC_STARTMIN),appX,178,22,22,TRUE);
    MoveWindow(GetWindow(H(IDC_STARTMIN),GW_HWNDNEXT),appX+27,178,220,22,TRUE);
    MoveWindow(H(IDC_MINTRAY),appX,206,22,22,TRUE);
    MoveWindow(GetWindow(H(IDC_MINTRAY),GW_HWNDNEXT),appX+27,206,220,22,TRUE);
    MoveWindow(H(IDC_CHECKUPDATES),appX,234,22,22,TRUE);
    MoveWindow(GetWindow(H(IDC_CHECKUPDATES),GW_HWNDNEXT),appX+27,234,220,22,TRUE);

    InvalidateRect(gWnd,nullptr,TRUE);
}
void LoadSelected(){
    int i=(int)SendMessageW(H(IDC_LIST),LB_GETCURSEL,0,0);
    if(i<0||i>(int)gSettings.profiles.size())return;
    gSelected=i;
    bool desktop=IsDesktopSelected();
    SetDesktopUi(desktop);

    ApplicationProfile* p=nullptr;
    if(desktop){
        int primary=-1;
        for(size_t di=0;di<gDisplays.size();++di){
            if(gDisplays[di].primary){ primary=(int)di; break; }
        }
        if(primary<0 && !gDisplays.empty()) primary=0;

        if(primary>=0){
            p=EnsureDesktopProfile(gDisplays[primary].gdiName,gDisplays[primary].monitorId);
            RefreshDisplayCombo(*p);
            SendMessageW(H(IDC_DISPLAY),CB_SETCURSEL,primary,0);
        }else{
            p=&gSettings.desktop;
            RefreshDisplayCombo(*p);
        }

        Txt(IDC_NAME,p->name);
        Txt(IDC_EXE,L"");
        InvalidateRect(H(IDC_EXE),nullptr,TRUE);
        SendMessageW(H(IDC_ENABLED),BM_SETCHECK,BST_UNCHECKED,0);
        LoadValuesToSliders(ValuesFromFlatProfile(*p));
        return;
    }

    p=SelectedProfile();
    if(!p)return;
    RefreshDisplayCombo(*p);

    int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);
    if(ds<0||ds>=(int)gDisplays.size()){
        for(size_t di=0;di<gDisplays.size();++di)
            if(gDisplays[di].primary){ ds=(int)di; break; }
        if(ds<0&&!gDisplays.empty()) ds=0;
    }

    Txt(IDC_NAME,p->name);
    Txt(IDC_EXE,p->exePath);
    InvalidateRect(H(IDC_EXE),nullptr,TRUE);
    SendMessageW(H(IDC_ENABLED),BM_SETCHECK,p->enabled?BST_CHECKED:BST_UNCHECKED,0);

    if(ds>=0&&ds<(int)gDisplays.size())
        LoadValuesToSliders(*EnsureApplicationValuesForDisplay(*p,gDisplays[ds].gdiName,gDisplays[ds].monitorId));
    else
        LoadValuesToSliders(ValuesFromFlatProfile(*p));
}
void SaveSelected(){
    CancelPendingPreview();
    EnterCriticalSection(&gPreviewLock);
    gPreviewDirty=false;
    LeaveCriticalSection(&gPreviewLock);

    bool desktop=IsDesktopSelected();
    int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);

    if(desktop){
        ApplicationProfile* p=CurrentDesktopProfile();
        if(!p)return;
        p->name=L"Windows";
        if(p->displayProfiles.empty())return;
        auto& v=p->displayProfiles.front();
        if(ds>=0&&ds<(int)gDisplays.size()){v.displayName=gDisplays[ds].gdiName;v.monitorId=gDisplays[ds].monitorId;}
        v.vibrance=(int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0);
        v.hue=(int)SendMessageW(H(IDC_HUE),TBM_GETPOS,0,0);
        v.brightness=(double)(int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0);
        v.contrast=(double)(int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0);
        v.gamma=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0)/100.0;
        Save();
        RefreshList();
        Apply(*p);
        gActive=L"Windows";
        return;
    }

    ApplicationProfile* p=SelectedProfile();
    if(!p)return;
    p->name=GetTxt(IDC_NAME);
    p->exePath=GetTxt(IDC_EXE);
    p->enabled=SendMessageW(H(IDC_ENABLED),BM_GETCHECK,0,0)==BST_CHECKED;

    if(ds>=0&&ds<(int)gDisplays.size()){
        auto* v=EnsureApplicationValuesForDisplay(*p,gDisplays[ds].gdiName,gDisplays[ds].monitorId);
        v->vibrance=(int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0);
        v->hue=(int)SendMessageW(H(IDC_HUE),TBM_GETPOS,0,0);
        v->brightness=(double)(int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0);
        v->contrast=(double)(int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0);
        v->gamma=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0)/100.0;

    }

    Save();
    RefreshList();

    // Saving an application profile can happen while its live preview is
    // physically active on the display. Re-evaluate the real foreground state
    // unconditionally so the monitor never remains stuck on the edited profile.
    // Clearing gActive forces CheckProcesses() to re-apply the correct saved
    // profile even when its logical state was already "Windows".
    gActive.clear();
    CheckProcesses();
}
HICON LoadExeIcon(const std::wstring& path){
    if(path.empty() || !PathFileExistsW(path.c_str())) return nullptr;

    // Ask Windows for a 48x48 resource first. This avoids stretching a 16/32 px
    // small icon and keeps application icons much sharper in the profile list.
    HICON hi=nullptr;
    UINT iconId=0;
    UINT got=PrivateExtractIconsW(path.c_str(),0,48,48,&hi,&iconId,1,LR_DEFAULTCOLOR);
    if(got>0 && hi) return hi;

    HICON hLarge=nullptr, hSmall=nullptr;
    UINT count=ExtractIconExW(path.c_str(),0,&hLarge,&hSmall,1);
    if(count>0){
        if(hLarge){
            if(hSmall) DestroyIcon(hSmall);
            return hLarge;
        }
        if(hSmall) return hSmall;
    }

    SHFILEINFOW fi{};
    if(SHGetFileInfoW(path.c_str(),0,&fi,sizeof(fi),SHGFI_ICON|SHGFI_LARGEICON) && fi.hIcon)
        return fi.hIcon;

    return nullptr;
}


void DrawWindowsLogo(HDC dc,int x,int y,int size){
    int gap=2, half=(size-gap)/2;
    HBRUSH b=CreateSolidBrush(C_WINBLUE);
    RECT r1{x,y,x+half,y+half};
    RECT r2{x+half+gap,y,x+size,y+half};
    RECT r3{x,y+half+gap,x+half,y+size};
    RECT r4{x+half+gap,y+half+gap,x+size,y+size};
    FillRect(dc,&r1,b);FillRect(dc,&r2,b);FillRect(dc,&r3,b);FillRect(dc,&r4,b);
    DeleteObject(b);
}

void FillRound(HDC dc,const RECT& r,COLORREF fill,COLORREF border,int radius=8){
    HBRUSH b=CreateSolidBrush(fill);
    HPEN p=CreatePen(PS_SOLID,1,border);
    HGDIOBJ ob=SelectObject(dc,b), op=SelectObject(dc,p);
    RoundRect(dc,r.left,r.top,r.right,r.bottom,radius,radius);
    SelectObject(dc,ob);SelectObject(dc,op);
    DeleteObject(b);DeleteObject(p);
}

void DrawAddButtonIcon(HDC dc,int x,int y,COLORREF c){
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    Gdiplus::Color color(255,GetRValue(c),GetGValue(c),GetBValue(c));
    Gdiplus::Pen pen(color,1.35f);
    pen.SetStartCap(Gdiplus::LineCapRound);
    pen.SetEndCap(Gdiplus::LineCapRound);
    g.DrawLine(&pen,(Gdiplus::REAL)x+7,(Gdiplus::REAL)y+2.5f,(Gdiplus::REAL)x+7,(Gdiplus::REAL)y+15.5f);
    g.DrawLine(&pen,(Gdiplus::REAL)x+0.5f,(Gdiplus::REAL)y+9,(Gdiplus::REAL)x+13.5f,(Gdiplus::REAL)y+9);
}
void DrawRemoveButtonIcon(HDC dc,int x,int y,COLORREF c){
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    Gdiplus::Color color(255,GetRValue(c),GetGValue(c),GetBValue(c));
    Gdiplus::Pen pen(color,1.15f);
    pen.SetStartCap(Gdiplus::LineCapRound);
    pen.SetEndCap(Gdiplus::LineCapRound);
    pen.SetLineJoin(Gdiplus::LineJoinRound);
    g.DrawLine(&pen,x+1.5f,y+4.5f,x+14.5f,y+4.5f);
    g.DrawLine(&pen,x+5.0f,y+2.0f,x+11.0f,y+2.0f);
    g.DrawLine(&pen,x+3.0f,y+6.0f,x+4.0f,y+17.0f);
    g.DrawLine(&pen,x+4.0f,y+17.0f,x+12.0f,y+17.0f);
    g.DrawLine(&pen,x+12.0f,y+17.0f,x+13.0f,y+6.0f);
    g.DrawLine(&pen,x+6.5f,y+8.0f,x+6.8f,y+14.5f);
    g.DrawLine(&pen,x+9.5f,y+8.0f,x+9.2f,y+14.5f);
}

void DrawFolderIcon(HDC dc,int x,int y,COLORREF c){
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);

    Gdiplus::Color color(255,GetRValue(c),GetGValue(c),GetBValue(c));
    Gdiplus::Pen pen(color,1.35f);
    pen.SetStartCap(Gdiplus::LineCapRound);
    pen.SetEndCap(Gdiplus::LineCapRound);
    pen.SetLineJoin(Gdiplus::LineJoinRound);

    Gdiplus::GraphicsPath path;
    path.StartFigure();
    path.AddLine((Gdiplus::REAL)x+1.5f,(Gdiplus::REAL)y+5.0f,
                 (Gdiplus::REAL)x+6.8f,(Gdiplus::REAL)y+5.0f);
    path.AddLine((Gdiplus::REAL)x+6.8f,(Gdiplus::REAL)y+5.0f,
                 (Gdiplus::REAL)x+9.2f,(Gdiplus::REAL)y+7.4f);
    path.AddLine((Gdiplus::REAL)x+9.2f,(Gdiplus::REAL)y+7.4f,
                 (Gdiplus::REAL)x+18.0f,(Gdiplus::REAL)y+7.4f);
    path.AddLine((Gdiplus::REAL)x+18.0f,(Gdiplus::REAL)y+7.4f,
                 (Gdiplus::REAL)x+19.0f,(Gdiplus::REAL)y+16.5f);
    path.AddLine((Gdiplus::REAL)x+19.0f,(Gdiplus::REAL)y+16.5f,
                 (Gdiplus::REAL)x+1.5f,(Gdiplus::REAL)y+16.5f);
    path.CloseFigure();
    g.DrawPath(&pen,&path);
}


void DrawProfileHeaderButton(const DRAWITEMSTRUCT* d){
    const int id=(int)d->CtlID;
    const bool down=(d->itemState&ODS_SELECTED)!=0;
    const bool disabled=(d->itemState&ODS_DISABLED)!=0;
    const bool hover=d->hwndItem==gMainButtonHover;

    RECT r=d->rcItem;
    const COLORREF textColor=disabled?C_MUTED:RGB(230,233,236);
    const COLORREF iconColor=disabled?C_MUTED:(id==IDC_REMOVE?C_DANGER:RGB(218,222,226));

    DrawMainButtonSurface(d->hDC,r,false,hover,down,disabled);

    wchar_t caption[64]{};
    GetWindowTextW(d->hwndItem,caption,64);

    HFONT oldFont=(HFONT)SelectObject(d->hDC,gFont);
    SetBkMode(d->hDC,TRANSPARENT);
    SetTextColor(d->hDC,textColor);

    SIZE textSize{};
    GetTextExtentPoint32W(d->hDC,caption,(int)wcslen(caption),&textSize);

    // Fixed visual metrics. The complete icon + gap + text block is centered.
    const int iconVisualW=16;
    const int gap=6;
    const int totalW=iconVisualW+gap+textSize.cx;
    const int pressOffset=down?1:0;
    const int contentLeft=r.left+((r.right-r.left)-totalW)/2+pressOffset;
    const int cy=(r.top+r.bottom)/2+pressOffset;

    if(id==IDC_ADD){
        // Smaller 12 px plus, centered inside the same 16 px visual slot.
        // The complete icon + gap + caption block remains centered as one unit.
        HPEN pen=CreatePen(PS_SOLID,1,iconColor);
        HGDIOBJ oldPen=SelectObject(d->hDC,pen);
        MoveToEx(d->hDC,contentLeft+8,cy-6,nullptr);
        LineTo(d->hDC,contentLeft+8,cy+6);
        MoveToEx(d->hDC,contentLeft+2,cy,nullptr);
        LineTo(d->hDC,contentLeft+14,cy);
        SelectObject(d->hDC,oldPen);
        DeleteObject(pen);
    }else{
        // Compact trash can, drawn to the same 16 px visual box.
        HPEN pen=CreatePen(PS_SOLID,1,iconColor);
        HGDIOBJ oldPen=SelectObject(d->hDC,pen);
        MoveToEx(d->hDC,contentLeft+3,cy-5,nullptr);
        LineTo(d->hDC,contentLeft+13,cy-5);
        MoveToEx(d->hDC,contentLeft+5,cy-8,nullptr);
        LineTo(d->hDC,contentLeft+11,cy-8);
        Rectangle(d->hDC,contentLeft+4,cy-3,contentLeft+13,cy+8);
        MoveToEx(d->hDC,contentLeft+7,cy-1,nullptr);
        LineTo(d->hDC,contentLeft+7,cy+6);
        MoveToEx(d->hDC,contentLeft+10,cy-1,nullptr);
        LineTo(d->hDC,contentLeft+10,cy+6);
        SelectObject(d->hDC,oldPen);
        DeleteObject(pen);
    }

    RECT tr{
        contentLeft+iconVisualW+gap,
        r.top+pressOffset,
        r.right-2,
        r.bottom+pressOffset
    };
    DrawTextW(d->hDC,caption,-1,&tr,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);

    SelectObject(d->hDC,oldFont);
}

void DrawOwnerButton(const DRAWITEMSTRUCT* d){
    int id=(int)d->CtlID;
    bool down=(d->itemState&ODS_SELECTED)!=0;
    bool disabled=(d->itemState&ODS_DISABLED)!=0;
    bool hover=d->hwndItem==gMainButtonHover;

    COLORREF textColor=disabled?C_MUTED:C_TEXT, icon=C_MUTED;
    if(id==IDC_SAVE){
        textColor=disabled?C_MUTED:C_TEXT;
        icon=disabled?C_MUTED:C_TEXT;
    }else if(id==IDC_DEFAULTS){
        textColor=disabled?C_MUTED:C_TEXT;
        icon=C_MUTED;
    }else if(id==IDC_BROWSE){
        icon=C_TEXT;
    }

    RECT r=d->rcItem;
    DrawMainButtonSurface(d->hDC,r,id==IDC_SAVE,hover,down,disabled);

    wchar_t caption[128]{};
    GetWindowTextW(d->hwndItem,caption,128);
    SIZE sz{};
    HFONT buttonFont=(id==IDC_SAVE||id==IDC_DEFAULTS)?gFontBold:gFont;
    SelectObject(d->hDC,buttonFont);
    GetTextExtentPoint32W(d->hDC,caption,(int)wcslen(caption),&sz);

    const int pressOffset=down?1:0;
    const int cy=(r.top+r.bottom)/2+pressOffset;

    SetBkMode(d->hDC,TRANSPARENT);
    SetTextColor(d->hDC,textColor);
    SelectObject(d->hDC,buttonFont);

    {
        int iconW=0;
        int gap=0;
        if(id==IDC_BROWSE){ iconW=20; gap=7; }

        int total=iconW+gap+sz.cx;
        int contentX=r.left+((r.right-r.left)-total)/2+pressOffset;

        if(id==IDC_BROWSE){
            DrawFolderIcon(d->hDC,contentX+1,cy-11,RGB(65,72,79));
            DrawFolderIcon(d->hDC,contentX,cy-12,icon);
        }

        int textY=cy-sz.cy/2;
        if(id==IDC_BROWSE) textY-=1;
        TextOutW(d->hDC,contentX+iconW+gap,textY,caption,(int)wcslen(caption));
    }
}

void DrawValueBox(const DRAWITEMSTRUCT* d){
    RECT r=d->rcItem;
    FillRound(d->hDC,r,C_FIELD,C_BORDER,7);
    wchar_t text[64]{};
    GetWindowTextW(d->hwndItem,text,64);

    RECT tr=r;
    tr.left+=8;
    tr.right-=8;
    SetBkMode(d->hDC,TRANSPARENT);
    SetTextColor(d->hDC,C_TEXT);
    SelectObject(d->hDC,gFont);
    DrawTextW(d->hDC,text,-1,&tr,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
}

LRESULT CustomDrawSlider(NMCUSTOMDRAW* cd){
    HWND h=cd->hdr.hwndFrom;
    if(cd->dwDrawStage!=CDDS_PREPAINT) return CDRF_DODEFAULT;

    RECT r{};
    GetClientRect(h,&r);
    HDC dc=cd->hdc;

    HBRUSH panelBrush=CreateSolidBrush(C_PANEL);
    FillRect(dc,&r,panelBrush);
    DeleteObject(panelBrush);

    int minv=(int)SendMessageW(h,TBM_GETRANGEMIN,0,0);
    int maxv=(int)SendMessageW(h,TBM_GETRANGEMAX,0,0);
    int pos=(int)SendMessageW(h,TBM_GETPOS,0,0);
    double t=maxv==minv?0.0:(double)(pos-minv)/(double)(maxv-minv);

    const int x1=8;
    const int x2=(int)std::max<LONG>(9L,r.right-8);
    const int cy=(r.top+r.bottom)/2;
    const int active=x1+(int)llround((x2-x1)*t);

    RECT bg{x1,cy-3,x2,cy+3};
    FillRound(dc,bg,RGB(63,69,75),RGB(63,69,75),6);

    if(active>x1){
        RECT fg{x1,cy-3,active,cy+3};
        FillRound(dc,fg,C_ACCENT2,C_ACCENT2,6);
    }

    const int rad=8;
    HBRUSH knob=CreateSolidBrush(RGB(211,216,220));
    HPEN outline=CreatePen(PS_SOLID,1,RGB(168,176,182));
    HGDIOBJ oldBrush=SelectObject(dc,knob);
    HGDIOBJ oldPen=SelectObject(dc,outline);
    Ellipse(dc,active-rad,cy-rad,active+rad+1,cy+rad+1);
    SelectObject(dc,oldBrush);
    SelectObject(dc,oldPen);
    DeleteObject(knob);
    DeleteObject(outline);

    return CDRF_SKIPDEFAULT;
}


Gdiplus::Image* LoadEmbeddedPng(int resourceId){
    HRSRC res=FindResourceW(gInst,MAKEINTRESOURCEW(resourceId),RT_RCDATA);
    if(!res) return nullptr;
    HGLOBAL data=LoadResource(gInst,res);
    if(!data) return nullptr;
    DWORD size=SizeofResource(gInst,res);
    const void* src=LockResource(data);
    if(!src||!size) return nullptr;

    HGLOBAL copy=GlobalAlloc(GMEM_MOVEABLE,size);
    if(!copy) return nullptr;
    void* dst=GlobalLock(copy);
    if(!dst){GlobalFree(copy);return nullptr;}
    memcpy(dst,src,size);
    GlobalUnlock(copy);

    IStream* stream=nullptr;
    if(CreateStreamOnHGlobal(copy,TRUE,&stream)!=S_OK){
        GlobalFree(copy);return nullptr;
    }
    auto* image=Gdiplus::Image::FromStream(stream,FALSE);
    stream->Release();
    if(!image||image->GetLastStatus()!=Gdiplus::Ok){
        delete image;return nullptr;
    }
    return image;
}

void LoadSliderIcons(){
    gSliderBrightness=LoadEmbeddedPng(IDR_SLIDER_BRIGHTNESS);
    gSliderContrast=LoadEmbeddedPng(IDR_SLIDER_CONTRAST);
    gSliderGamma=LoadEmbeddedPng(IDR_SLIDER_GAMMA);
    gSliderVibrance=LoadEmbeddedPng(IDR_SLIDER_VIBRANCE);
    gSliderHue=LoadEmbeddedPng(IDR_SLIDER_HUE);
    gNvidiaDriverIcon=LoadEmbeddedPng(IDR_NVIDIA_PNG);
}

bool LoadHeaderImage(){
    HRSRC res=FindResourceW(gInst,MAKEINTRESOURCEW(IDR_HEADER_PNG),RT_RCDATA);
    if(!res) return false;
    HGLOBAL data=LoadResource(gInst,res);
    if(!data) return false;
    DWORD size=SizeofResource(gInst,res);
    const void* src=LockResource(data);
    if(!src||!size) return false;

    HGLOBAL copy=GlobalAlloc(GMEM_MOVEABLE,size);
    if(!copy) return false;
    void* dst=GlobalLock(copy);
    if(!dst){ GlobalFree(copy); return false; }
    memcpy(dst,src,size);
    GlobalUnlock(copy);

    IStream* stream=nullptr;
    if(CreateStreamOnHGlobal(copy,TRUE,&stream)!=S_OK){
        GlobalFree(copy);
        return false;
    }

    auto* image=Gdiplus::Image::FromStream(stream,FALSE);
    stream->Release();
    if(!image || image->GetLastStatus()!=Gdiplus::Ok){
        delete image;
        return false;
    }
    gHeaderImage=image;
    return true;
}

void DrawHeaderImage(HDC dc){
    if(!gHeaderImage) return;
    Gdiplus::Graphics graphics(dc);
    graphics.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
    graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHighQuality);
    graphics.SetCompositingMode(Gdiplus::CompositingModeSourceOver);
    graphics.SetCompositingQuality(Gdiplus::CompositingQualityHighQuality);

    const int targetH=58;
    const UINT iw=gHeaderImage->GetWidth();
    const UINT ih=gHeaderImage->GetHeight();
    if(!iw||!ih) return;
    const int targetW=(int)llround((double)iw*targetH/(double)ih);
    graphics.DrawImage(gHeaderImage,Gdiplus::Rect(27,9,targetW,targetH));
}


void DrawSliderIcon(HDC dc,Gdiplus::Image* image,int x,int y){
    if(!image) return;
    Gdiplus::Graphics graphics(dc);
    graphics.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
    graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHighQuality);
    graphics.SetCompositingMode(Gdiplus::CompositingModeSourceOver);
    graphics.SetCompositingQuality(Gdiplus::CompositingQualityHighQuality);
    graphics.DrawImage(image,Gdiplus::Rect(x,y,22,22));
}


void DrawLabel(HDC dc,const wchar_t*t,int x,int y,COLORREF c,HFONT f=nullptr){ SetBkMode(dc,TRANSPARENT);SetTextColor(dc,c);SelectObject(dc,f?f:gFont);TextOutW(dc,x,y,t,(int)wcslen(t)); }
void Fill(HDC dc,int x,int y,int w,int h,COLORREF c){HBRUSH b=CreateSolidBrush(c);RECT r{x,y,x+w,y+h};FillRect(dc,&r,b);DeleteObject(b);} 


void DrawProfilesPrototypeIcon(HDC dc,int x,int y){
    // Prototype: two overlapping WHITE cards with a thin green outline.
    // The old version incorrectly filled the front card green.
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);

    Gdiplus::Color accent(255,GetRValue(C_ACCENT),GetGValue(C_ACCENT),GetBValue(C_ACCENT));
    Gdiplus::Color white(255,245,245,245);
    Gdiplus::Pen outline(accent,1.0f);
    Gdiplus::SolidBrush fill(white);

    // Rear card: slightly up/left.
    g.FillRectangle(&fill,(Gdiplus::REAL)x+1,(Gdiplus::REAL)y+1,15.0f,10.0f);
    g.DrawRectangle(&outline,(Gdiplus::REAL)x+1,(Gdiplus::REAL)y+1,15.0f,10.0f);

    // Front card: offset down/right, also white like the prototype.
    g.FillRectangle(&fill,(Gdiplus::REAL)x+7,(Gdiplus::REAL)y+6,16.0f,11.0f);
    g.DrawRectangle(&outline,(Gdiplus::REAL)x+7,(Gdiplus::REAL)y+6,16.0f,11.0f);
}

void DrawApplicationSettingsGear(HDC dc,int x,int y){
    // Thin prototype-style green gear for the Application Settings module.
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);

    Gdiplus::Color accent(255,GetRValue(C_ACCENT),GetGValue(C_ACCENT),GetBValue(C_ACCENT));
    Gdiplus::Pen pen(accent,1.35f);

    const Gdiplus::REAL cx=(Gdiplus::REAL)x+9.0f;
    const Gdiplus::REAL cy=(Gdiplus::REAL)y+9.0f;

    g.DrawEllipse(&pen,cx-5.0f,cy-5.0f,10.0f,10.0f);
    g.DrawEllipse(&pen,cx-1.8f,cy-1.8f,3.6f,3.6f);

    // Eight short teeth, kept light so the icon matches the other section glyphs.
    for(int i=0;i<8;i++){
        const double a=(3.14159265358979323846/4.0)*i;
        const Gdiplus::REAL x1=cx+(Gdiplus::REAL)(6.0*cos(a));
        const Gdiplus::REAL y1=cy+(Gdiplus::REAL)(6.0*sin(a));
        const Gdiplus::REAL x2=cx+(Gdiplus::REAL)(8.0*cos(a));
        const Gdiplus::REAL y2=cy+(Gdiplus::REAL)(8.0*sin(a));
        g.DrawLine(&pen,x1,y1,x2,y2);
    }
}

void DrawProfileSettingsPrototypeIcon(HDC dc,int x,int y){
    // Prototype: very thin anti-aliased lines with small round adjustment knobs.
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);

    Gdiplus::Color accent(255,GetRValue(C_ACCENT),GetGValue(C_ACCENT),GetBValue(C_ACCENT));
    Gdiplus::Pen line(accent,1.25f);
    Gdiplus::SolidBrush knob(accent);

    const Gdiplus::REAL ys[3]={(Gdiplus::REAL)y+2.5f,(Gdiplus::REAL)y+8.5f,(Gdiplus::REAL)y+14.5f};
    const Gdiplus::REAL xs[3]={(Gdiplus::REAL)x+7.0f,(Gdiplus::REAL)x+15.0f,(Gdiplus::REAL)x+11.0f};

    for(int i=0;i<3;i++){
        g.DrawLine(&line,(Gdiplus::REAL)x,ys[i],(Gdiplus::REAL)x+22.0f,ys[i]);
        g.FillEllipse(&knob,xs[i]-2.25f,ys[i]-2.25f,4.5f,4.5f);
    }
}

void DrawDisplayPrototypeIcon(HDC dc,int x,int y){
    // Match prototype: gray bezel/frame with a solid white screen,
    // then a gray stem and base underneath.
    const COLORREF frame=RGB(145,151,154);
    const COLORREF screen=RGB(245,245,245);

    HPEN framePen=CreatePen(PS_SOLID,1,frame);
    HBRUSH frameBrush=CreateSolidBrush(frame);
    HBRUSH screenBrush=CreateSolidBrush(screen);

    HGDIOBJ oldPen=SelectObject(dc,framePen);
    HGDIOBJ oldBrush=SelectObject(dc,frameBrush);

    // Prototype proportions: smaller/thinner bezel and a larger white screen.
    // Draw the bezel as a solid 1 px shell so GDI Rectangle's inclusive edge
    // doesn't visually turn it into a ~2 px border.
    SelectObject(dc,frameBrush);
    PatBlt(dc,x,y,22,1,PATCOPY);          // top
    PatBlt(dc,x,y+1,1,14,PATCOPY);        // left
    PatBlt(dc,x+21,y+1,1,14,PATCOPY);     // right
    PatBlt(dc,x,y+15,22,1,PATCOPY);       // bottom

    // White screen.
    SelectObject(dc,screenBrush);
    PatBlt(dc,x+1,y+1,20,14,PATCOPY);

    // Very thin stand/base, matching the reference.
    SelectObject(dc,frameBrush);
    PatBlt(dc,x+10,y+16,2,4,PATCOPY);
    PatBlt(dc,x+6,y+20,10,1,PATCOPY);

    SelectObject(dc,oldBrush);
    SelectObject(dc,oldPen);
    DeleteObject(screenBrush);
    DeleteObject(frameBrush);
    DeleteObject(framePen);
}

void DrawDriverIcon(HDC dc,int x,int y,COLORREF){
    if(!gNvidiaDriverIcon) return;
    Gdiplus::Graphics g(dc);
    g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
    g.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHighQuality);
    g.DrawImage(gNvidiaDriverIcon,Gdiplus::Rect(x,y,16,16));
}

LRESULT CALLBACK FooterLinkSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                       UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_MOUSEMOVE:{
        if(gFooterHover!=hwnd){
            HWND oldHover=gFooterHover;
            gFooterHover=hwnd;
            if(oldHover) InvalidateRect(oldHover,nullptr,TRUE);
            InvalidateRect(hwnd,nullptr,TRUE);
        }
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_MOUSELEAVE:
        if(gFooterHover==hwnd){
            gFooterHover=nullptr;
            InvalidateRect(hwnd,nullptr,TRUE);
        }
        break;
    case WM_NCDESTROY:
        if(gFooterHover==hwnd) gFooterHover=nullptr;
        RemoveWindowSubclass(hwnd,FooterLinkSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void DrawFooterLink(const DRAWITEMSTRUCT* d){
    const wchar_t* text=d->CtlID==IDC_FOOT_GITHUB?L"GitHub":
                       d->CtlID==IDC_FOOT_SUPPORT?L"Support me":L"About";
    bool down=(d->itemState&ODS_SELECTED)!=0;
    RECT r=d->rcItem;

    HBRUSH bg=CreateSolidBrush(C_BACK);
    FillRect(d->hDC,&r,bg);
    DeleteObject(bg);

    HFONT oldFont=(HFONT)SelectObject(d->hDC,gFont);
    SetBkMode(d->hDC,TRANSPARENT);
    SetTextColor(d->hDC,((down||(d->hwndItem==gFooterHover))?C_ACCENT:C_MUTED));

    SIZE s{};
    GetTextExtentPoint32W(d->hDC,text,(int)wcslen(text),&s);
    TextOutW(d->hDC,
             r.left+((r.right-r.left)-s.cx)/2,
             r.top+((r.bottom-r.top)-s.cy)/2,
             text,(int)wcslen(text));
    SelectObject(d->hDC,oldFont);
}

void Paint(HWND w){
    PAINTSTRUCT ps{}; HDC dc=BeginPaint(w,&ps); RECT rc{}; GetClientRect(w,&rc);
    FillRect(dc,&rc,gBackBrush);

    const int margin=18, top=88, gap=14, footerH=56;
    const int leftW=360, settingsW=330;
    const int panelBottom=rc.bottom-footerH-14;
    const int centerX=margin+leftW+gap;
    const int centerW=rc.right-(margin*2)-leftW-settingsW-(gap*2);
    const int settingsX=centerX+centerW+gap;
    const int rightX=centerX+22;
    const int rightW=centerW-44;

    RECT left{margin,top,margin+leftW,panelBottom};
    RECT center{centerX,top,centerX+centerW,panelBottom};
    RECT settings{settingsX,top,rc.right-margin,panelBottom};
    FillRound(dc,left,C_PANEL,C_BORDER,10);
    FillRound(dc,center,C_PANEL,C_BORDER,10);
    FillRound(dc,settings,C_PANEL,C_BORDER,10);

    DrawHeaderImage(dc);
    Fill(dc,0,78,rc.right,1,C_BORDER);

    // Panel header fill is clipped to the rounded panel and stops exactly
    // at the separator. No rounded header overlay and no repaint workaround.
    const int separatorY=136;
    auto PaintPanelHeader=[&](const RECT& panel){
        int saved=SaveDC(dc);
        HRGN clip=CreateRoundRectRgn(panel.left+1,panel.top+1,panel.right,panel.bottom,18,18);
        SelectClipRgn(dc,clip);
        Fill(dc,panel.left+1,panel.top+1,
             panel.right-panel.left-2,separatorY-panel.top-1,C_PANEL2);
        SelectClipRgn(dc,nullptr);
        DeleteObject(clip);
        RestoreDC(dc,saved);
    };
    PaintPanelHeader(left);
    PaintPanelHeader(center);
    PaintPanelHeader(settings);

    DrawLabel(dc,L"Profiles",left.left+14,99,C_TEXT,gFontPanelTitle);
    DrawLabel(dc,L"Profile Settings",center.left+14,99,C_TEXT,gFontPanelTitle);
    DrawLabel(dc,L"Application Settings",settings.left+14,99,C_TEXT,gFontPanelTitle);

    Fill(dc,left.left+1,separatorY,leftW-2,1,C_BORDER);
    Fill(dc,center.left+1,separatorY,centerW-2,1,C_BORDER);
    Fill(dc,settings.left+1,separatorY,settingsW-2,1,C_BORDER);

    const bool desktop=IsDesktopSelected();
    const int displayY=desktop?154:320;

    DrawDisplayPrototypeIcon(dc,centerX+22,displayY);

    // Rounded frames for application text fields. The EDIT controls themselves
    // are borderless and inset, avoiding clipped corners or double borders.
    if(!desktop){
        const int browseW=150;
        const int fieldGap=10;
        RECT nameFrame{rightX+118,146,rightX+rightW,182};
        FillRound(dc,nameFrame,C_FIELD,C_BORDER,8);

        RECT exeFrame{rightX,222,rightX+rightW-browseW-fieldGap,258};
        FillRound(dc,exeFrame,C_FIELD,C_BORDER,8);
    }

    const int iconX=centerX+22;
    const int iconBri=desktop?230:406;
    const int iconCon=desktop?294:474;
    const int iconGam=desktop?358:542;
    const int iconVib=desktop?422:610;
    const int iconHue=desktop?486:678;
    DrawSliderIcon(dc,gSliderBrightness,iconX,iconBri-2);
    DrawSliderIcon(dc,gSliderContrast,iconX,iconCon-2);
    DrawSliderIcon(dc,gSliderGamma,iconX,iconGam-2);
    DrawSliderIcon(dc,gSliderVibrance,iconX,iconVib-2);
    DrawSliderIcon(dc,gSliderHue,iconX,iconHue-2);

    const int footerTop=rc.bottom-footerH;
    Fill(dc,0,footerTop,rc.right,1,C_BORDER);
    const int footerY=footerTop+19;

    const int dotX=38, dotY=footerY+5;
    HBRUSH statusBrush=CreateSolidBrush(gStatusOk?C_ACCENT:C_DANGER);
    HGDIOBJ oldBrush=SelectObject(dc,statusBrush);
    Ellipse(dc,dotX,dotY,dotX+8,dotY+8);
    SelectObject(dc,oldBrush);
    DeleteObject(statusBrush);

    DrawLabel(dc,L"NVIDIA API",54,footerY,C_MUTED,gFont);
    SIZE apiLabel{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,L"NVIDIA API",10,&apiLabel);
    const wchar_t* apiState=gStatusOk?L"Available":L"Unavailable";
    COLORREF apiColor=gStatusOk?C_ACCENT:C_DANGER;
    int apiStateX=54+apiLabel.cx+8;
    DrawLabel(dc,apiState,apiStateX,footerY,apiColor,gFont);

    SIZE stateSize{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,apiState,(int)wcslen(apiState),&stateSize);
    int dividerX=apiStateX+stateSize.cx+18;
    Fill(dc,dividerX,footerY,1,17,C_BORDER);

    int driverIconX=dividerX+16;
    DrawDriverIcon(dc,driverIconX,footerY+1,C_MUTED);
    int driverTextX=driverIconX+23;
    DrawLabel(dc,L"Driver",driverTextX,footerY,C_MUTED,gFont);
    SIZE driverLabel{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,L"Driver",6,&driverLabel);
    int driverVersionX=driverTextX+driverLabel.cx+8;
    DrawLabel(dc,gDriverVersion.c_str(),driverVersionX,footerY,C_TEXT,gFont);

    SIZE driverVersionSize{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,gDriverVersion.c_str(),(int)gDriverVersion.size(),&driverVersionSize);
    int versionDividerX=driverVersionX+driverVersionSize.cx+18;
    Fill(dc,versionDividerX,footerY,1,17,C_BORDER);

    std::wstring footerVersion;
#if NVPS_DEV_BUILD
    footerVersion=APP_VERSION;
#else
    footerVersion=L"v";
    footerVersion+=APP_VERSION;
#endif
    DrawLabel(dc,footerVersion.c_str(),versionDividerX+16,footerY,C_MUTED,gFont);

    SIZE verSize{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,footerVersion.c_str(),(int)footerVersion.size(),&verSize);
    int activeDividerX=versionDividerX+16+verSize.cx+18;
    Fill(dc,activeDividerX,footerY,1,17,C_BORDER);
    DrawLabel(dc,L"Active profile",activeDividerX+16,footerY,C_MUTED,gFont);
    SIZE activeLabel{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,L"Active profile",14,&activeLabel);
    DrawLabel(dc,gActive.c_str(),activeDividerX+16+activeLabel.cx+8,footerY,C_TEXT,gFontBold);

    EndPaint(w,&ps);
}
void BuildControls(){
    RECT r{}; GetClientRect(gWnd,&r);
    const int margin=18, top=88, gap=14, footerH=56;
    const int leftW=360, settingsW=330;
    const int centerPanelX=margin+leftW+gap;
    const int centerPanelW=r.right-(margin*2)-leftW-settingsW-(gap*2);
    const int rightX=centerPanelX+22;
    const int rightW=centerPanelW-44;
    const int panelBottom=r.bottom-footerH-14;

    HWND list=Add(L"LISTBOX",L"",LBS_NOTIFY|LBS_OWNERDRAWFIXED|WS_VSCROLL,
        margin+10,144,leftW-20,panelBottom-144-18,IDC_LIST);
    SetWindowTheme(list,L"DarkMode_Explorer",nullptr);
    SendMessageW(list,LB_SETITEMHEIGHT,0,70);

    gProfileTooltip=CreateWindowExW(WS_EX_TOPMOST,TOOLTIPS_CLASSW,nullptr,
        WS_POPUP|TTS_ALWAYSTIP|TTS_NOPREFIX,
        CW_USEDEFAULT,CW_USEDEFAULT,CW_USEDEFAULT,CW_USEDEFAULT,
        gWnd,nullptr,gInst,nullptr);
    if(gProfileTooltip){
        SetWindowTheme(gProfileTooltip,L"",L"");
        SendMessageW(gProfileTooltip,WM_SETFONT,(WPARAM)gFont,FALSE);
        SendMessageW(gProfileTooltip,TTM_SETTIPBKCOLOR,(WPARAM)C_PANEL2,0);
        SendMessageW(gProfileTooltip,TTM_SETTIPTEXTCOLOR,(WPARAM)C_TEXT,0);
        SendMessageW(gProfileTooltip,TTM_SETMAXTIPWIDTH,0,500);
        RECT tipMargin{7,5,7,5};
        SendMessageW(gProfileTooltip,TTM_SETMARGIN,0,(LPARAM)&tipMargin);
        SetWindowSubclass(gProfileTooltip,ProfileTooltipSubclassProc,1,0);
        TOOLINFOW ti{sizeof(ti)};
        ti.uFlags=TTF_TRACK|TTF_ABSOLUTE;
        ti.hwnd=list;
        ti.uId=1;
        ti.lpszText=(LPWSTR)L"";
        SendMessageW(gProfileTooltip,TTM_ADDTOOLW,0,(LPARAM)&ti);
        SetWindowSubclass(list,ProfileListSubclassProc,1,0);
    }

    HWND addProfile=Add(L"BUTTON",L"Add profile",BS_OWNERDRAW,158,95,112,32,IDC_ADD);
    HWND removeProfile=Add(L"BUTTON",L"Remove",BS_OWNERDRAW,274,95,92,32,IDC_REMOVE);
    StyleMainButton(addProfile);
    StyleMainButton(removeProfile);

    Add(L"STATIC",L"Profile name",0,rightX,152,110,22,IDC_LBL_NAME);
    HWND eName=Add(L"EDIT",L"",ES_AUTOHSCROLL,rightX+120,153,rightW-122,22,IDC_NAME);
    SetWindowTheme(eName,L"DarkMode_Explorer",nullptr);
    SendMessageW(eName,EM_SETMARGINS,EC_LEFTMARGIN|EC_RIGHTMARGIN,MAKELPARAM(8,8));

    Add(L"STATIC",L"Executable",0,rightX,194,120,22,IDC_LBL_EXE);
    const int browseW=150;
    const int fieldGap=10;
    HWND eExe=Add(L"EDIT",L"",ES_AUTOHSCROLL,rightX+2,229,rightW-browseW-fieldGap-4,22,IDC_EXE);
    SetWindowTheme(eExe,L"DarkMode_Explorer",nullptr);
    SendMessageW(eExe,EM_SETMARGINS,EC_LEFTMARGIN|EC_RIGHTMARGIN,MAKELPARAM(8,8));
    SetWindowSubclass(eExe,ExecutableEditSubclassProc,1,0);

    gExeTooltip=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST,L"STATIC",L"",
        WS_POPUP,0,0,0,0,gWnd,nullptr,gInst,nullptr);
    if(gExeTooltip){
        SendMessageW(gExeTooltip,WM_SETFONT,(WPARAM)gFont,FALSE);
        SetWindowSubclass(gExeTooltip,ProfileTooltipSubclassProc,3,0);
    }

    HWND browse=Add(L"BUTTON",L"Browse...",BS_OWNERDRAW,
        rightX+rightW-browseW,222,browseW,36,IDC_BROWSE);
    StyleMainButton(browse);

    HWND enabled=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX,272,22,22,IDC_ENABLED);
    StyleFlatCheckbox(enabled);
    Add(L"STATIC",L"Enable this profile",SS_CENTERIMAGE,rightX+27,272,205,22,IDC_LBL_ENABLED);

    Add(L"STATIC",L"Display",0,rightX+31,320,150,22,IDC_LBL_DISPLAY);
    HWND display=Add(L"COMBOBOX",L"",CBS_DROPDOWNLIST|CBS_OWNERDRAWFIXED|CBS_HASSTRINGS|WS_VSCROLL,
        rightX,344,rightW,240,IDC_DISPLAY);
    SendMessageW(display,CB_SETITEMHEIGHT,0,28);
    SendMessageW(display,CB_SETITEMHEIGHT,(WPARAM)-1,26);
    StyleFlatCombo(display);

    const int labelX=rightX+30;
    const int labelW=154;
    const int trackX=rightX+190;
    const int valueW=76;
    const int valueX=rightX+rightW-valueW;
    const int trackW=valueX-trackX-16;

    auto slider=[&](const wchar_t*t,int lid,int id,int vid,int y,int mn,int mx){
        Add(L"STATIC",t,0,labelX,y-2,labelW,22,lid);
        HWND tr=Add(TRACKBAR_CLASSW,L"",TBS_HORZ|TBS_NOTICKS,trackX,y-4,trackW,28,id);
        SendMessageW(tr,TBM_SETRANGE,TRUE,MAKELONG(mn,mx));
        Add(L"STATIC",L"",SS_OWNERDRAW,valueX,y-5,valueW,28,vid);
    };

    slider(L"Brightness",IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,420,80,120);
    slider(L"Contrast",IDC_LBL_CON,IDC_CON,IDC_VALCON,488,80,120);
    slider(L"Gamma",IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,556,30,280);
    slider(L"Digital Vibrance (%)",IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,624,0,100);
    slider(L"Hue (\x00B0)",IDC_LBL_HUE,IDC_HUE,IDC_VALHUE,692,0,359);

    HWND reset=Add(L"BUTTON",L"Reset",BS_OWNERDRAW,rightX,756,132,38,IDC_DEFAULTS);
    StyleMainButton(reset);
    gResetTooltip=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST,L"STATIC",
        L"Reset to NVIDIA defaults",WS_POPUP,0,0,0,0,gWnd,nullptr,gInst,nullptr);
    if(gResetTooltip){
        SendMessageW(gResetTooltip,WM_SETFONT,(WPARAM)gFont,FALSE);
        SetWindowSubclass(gResetTooltip,ProfileTooltipSubclassProc,2,0);
        SetWindowSubclass(H(IDC_DEFAULTS),ResetButtonSubclassProc,1,0);
    }
    HWND saveProfile=Add(L"BUTTON",L"Save profile",BS_OWNERDRAW,
        rightX+rightW-160,756,160,38,IDC_SAVE);
    StyleMainButton(saveProfile);

    const int appX=centerPanelX+centerPanelW+gap+22;

    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,150,22,22,IDC_STARTWIN); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Start with Windows",SS_CENTERIMAGE,appX+27,150,220,22,0);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,178,22,22,IDC_STARTMIN); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Start minimized",SS_CENTERIMAGE,appX+27,178,220,22,0);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,206,22,22,IDC_MINTRAY); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Minimize to tray",SS_CENTERIMAGE,appX+27,206,220,22,0);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,234,22,22,IDC_CHECKUPDATES); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Check for updates",SS_CENTERIMAGE,appX+27,234,220,22,0);

    SendMessageW(H(IDC_STARTWIN),BM_SETCHECK,gSettings.startWindows?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_STARTMIN),BM_SETCHECK,gSettings.startMinimized?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_MINTRAY),BM_SETCHECK,gSettings.minimizeToTray?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_CHECKUPDATES),BM_SETCHECK,gSettings.checkUpdates?BST_CHECKED:BST_UNCHECKED,0);

    HWND footGitHub=Add(L"BUTTON",L"GitHub",BS_OWNERDRAW,r.right-284,r.bottom-43,66,24,IDC_FOOT_GITHUB);
    HWND footSupport=Add(L"BUTTON",L"Support me",BS_OWNERDRAW,r.right-212,r.bottom-43,98,24,IDC_FOOT_SUPPORT);
    HWND footAbout=Add(L"BUTTON",L"About",BS_OWNERDRAW,r.right-108,r.bottom-43,64,24,IDC_FOOT_ABOUT);
    SetWindowSubclass(footGitHub,FooterLinkSubclassProc,1,0);
    SetWindowSubclass(footSupport,FooterLinkSubclassProc,1,0);
    SetWindowSubclass(footAbout,FooterLinkSubclassProc,1,0);
}

void ResizeControls(){
    RECT r{}; GetClientRect(gWnd,&r);
    const int margin=18, gap=14, footerH=56;
    const int leftW=360, settingsW=330;
    const int centerPanelX=margin+leftW+gap;
    const int centerPanelW=r.right-(margin*2)-leftW-settingsW-(gap*2);
    const int rightX=centerPanelX+22;
    const int rightW=centerPanelW-44;
    const int panelBottom=r.bottom-footerH-14;

    MoveWindow(H(IDC_LIST),margin+10,144,leftW-20,(int)std::max(300,panelBottom-144-18),TRUE);
    MoveWindow(H(IDC_ADD),158,95,112,32,TRUE);
    MoveWindow(H(IDC_REMOVE),274,95,92,32,TRUE);

    MoveWindow(H(IDC_LBL_NAME),rightX,152,110,22,TRUE);
    MoveWindow(H(IDC_NAME),rightX+120,153,rightW-122,22,TRUE);

    const int browseW=150;
    const int fieldGap=10;
    MoveWindow(H(IDC_LBL_EXE),rightX,194,120,22,TRUE);
    MoveWindow(H(IDC_EXE),rightX+2,229,rightW-browseW-fieldGap-4,22,TRUE);
    MoveWindow(H(IDC_BROWSE),rightX+rightW-browseW,222,browseW,36,TRUE);
    MoveWindow(H(IDC_ENABLED),rightX,272,22,22,TRUE);
    MoveWindow(H(IDC_LBL_ENABLED),rightX+27,272,205,22,TRUE);

    MoveWindow(H(IDC_FOOT_GITHUB),r.right-284,r.bottom-43,66,24,TRUE);
    MoveWindow(H(IDC_FOOT_SUPPORT),r.right-212,r.bottom-43,98,24,TRUE);
    MoveWindow(H(IDC_FOOT_ABOUT),r.right-108,r.bottom-43,64,24,TRUE);

    SetDesktopUi(IsDesktopSelected());
}


struct UpdateInfo{
    std::wstring version;
    std::wstring url;
};

std::wstring Utf8ToWide(const std::string& text){
    if(text.empty())return L"";
    int n=MultiByteToWideChar(CP_UTF8,0,text.data(),(int)text.size(),nullptr,0);
    if(n<=0)return L"";
    std::wstring out(n,0);
    MultiByteToWideChar(CP_UTF8,0,text.data(),(int)text.size(),out.data(),n);
    return out;
}

std::string JsonStringValue(const std::string& json,const std::string& key){
    std::string token="\""+key+"\"";
    size_t p=json.find(token);
    if(p==std::string::npos)return {};
    p=json.find(':',p+token.size());
    if(p==std::string::npos)return {};
    p=json.find('"',p+1);
    if(p==std::string::npos)return {};
    ++p;
    std::string out;
    bool esc=false;
    for(;p<json.size();++p){
        char c=json[p];
        if(esc){
            switch(c){
                case '"': out.push_back('"'); break;
                case '\\': out.push_back('\\'); break;
                case '/': out.push_back('/'); break;
                case 'b': out.push_back('\b'); break;
                case 'f': out.push_back('\f'); break;
                case 'n': out.push_back('\n'); break;
                case 'r': out.push_back('\r'); break;
                case 't': out.push_back('\t'); break;
                default: out.push_back(c); break;
            }
            esc=false;
        }else if(c=='\\'){
            esc=true;
        }else if(c=='"'){
            break;
        }else{
            out.push_back(c);
        }
    }
    return out;
}

std::vector<int> ParseVersionParts(std::wstring v){
    if(!v.empty()&&(v[0]==L'v'||v[0]==L'V'))v.erase(v.begin());
    size_t dash=v.find_first_of(L"-+");
    if(dash!=std::wstring::npos)v.resize(dash);
    std::vector<int> parts;
    size_t start=0;
    while(start<=v.size()){
        size_t dot=v.find(L'.',start);
        std::wstring part=v.substr(start,dot==std::wstring::npos?v.size()-start:dot-start);
        if(part.empty())return {};
        for(wchar_t c:part)if(c<L'0'||c>L'9')return {};
        parts.push_back(_wtoi(part.c_str()));
        if(dot==std::wstring::npos)break;
        start=dot+1;
    }
    return parts;
}

bool IsVersionNewer(const std::wstring& remote,const std::wstring& local){
    auto a=ParseVersionParts(remote);
    auto b=ParseVersionParts(local);
    if(a.empty()||b.empty())return false;
    size_t n=std::max(a.size(),b.size());
    a.resize(n,0); b.resize(n,0);
    for(size_t i=0;i<n;++i){
        if(a[i]>b[i])return true;
        if(a[i]<b[i])return false;
    }
    return false;
}

bool GetLatestRelease(UpdateInfo& info){
    HINTERNET session=WinHttpOpen(L"NvProfileSwitcher Update Checker",
        WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,WINHTTP_NO_PROXY_NAME,WINHTTP_NO_PROXY_BYPASS,0);
    if(!session)return false;

    WinHttpSetTimeouts(session,4000,4000,4000,6000);

    HINTERNET connect=WinHttpConnect(session,UPDATE_HOST,INTERNET_DEFAULT_HTTPS_PORT,0);
    if(!connect){WinHttpCloseHandle(session);return false;}

    HINTERNET request=WinHttpOpenRequest(connect,L"GET",UPDATE_PATH,nullptr,
        WINHTTP_NO_REFERER,WINHTTP_DEFAULT_ACCEPT_TYPES,WINHTTP_FLAG_SECURE);
    if(!request){
        WinHttpCloseHandle(connect);
        WinHttpCloseHandle(session);
        return false;
    }

    const wchar_t* headers=
        L"Accept: application/vnd.github+json\r\n"
        L"X-GitHub-Api-Version: 2022-11-28\r\n"
        L"User-Agent: NvProfileSwitcher\r\n";

    BOOL ok=WinHttpAddRequestHeaders(request,headers,(DWORD)-1L,WINHTTP_ADDREQ_FLAG_ADD|WINHTTP_ADDREQ_FLAG_REPLACE)
        && WinHttpSendRequest(request,WINHTTP_NO_ADDITIONAL_HEADERS,0,WINHTTP_NO_REQUEST_DATA,0,0,0)
        && WinHttpReceiveResponse(request,nullptr);

    DWORD status=0,statusSize=sizeof(status);
    if(ok)ok=WinHttpQueryHeaders(request,WINHTTP_QUERY_STATUS_CODE|WINHTTP_QUERY_FLAG_NUMBER,
        WINHTTP_HEADER_NAME_BY_INDEX,&status,&statusSize,WINHTTP_NO_HEADER_INDEX) && status==200;

    std::string body;
    if(ok){
        for(;;){
            DWORD avail=0;
            if(!WinHttpQueryDataAvailable(request,&avail) || avail==0)break;
            size_t old=body.size();
            body.resize(old+avail);
            DWORD read=0;
            if(!WinHttpReadData(request,body.data()+old,avail,&read)){ok=FALSE;break;}
            body.resize(old+read);
            if(read==0)break;
        }
    }

    WinHttpCloseHandle(request);
    WinHttpCloseHandle(connect);
    WinHttpCloseHandle(session);

    if(!ok||body.empty())return false;

    std::string tag=JsonStringValue(body,"tag_name");
    std::string url=JsonStringValue(body,"html_url");
    if(tag.empty()||url.empty())return false;

    info.version=Utf8ToWide(tag);
    if(!info.version.empty()&&(info.version[0]==L'v'||info.version[0]==L'V'))
        info.version.erase(info.version.begin());
    info.url=Utf8ToWide(url);
    return !info.version.empty()&&!info.url.empty();
}

DWORD WINAPI UpdateCheckThread(LPVOID param){
    bool manual=param!=nullptr;
    UpdateInfo info;
    if(GetLatestRelease(info)){
        if(IsVersionNewer(info.version,APP_VERSION)){
            auto* result=new UpdateInfo(std::move(info));
            if(!PostMessageW(gWnd,WM_UPDATE_AVAILABLE,0,(LPARAM)result))
                delete result;
        }else if(manual){
            std::wstring msg=L"NvProfileSwitcher is up to date.\n\nCurrent version: ";
            msg+=APP_VERSION;
            MessageBoxW(gWnd,msg.c_str(),L"Check for updates",MB_OK|MB_ICONINFORMATION);
        }
    }else if(manual){
        MessageBoxW(gWnd,L"Could not check for updates.\n\nPlease try again later.",
            L"Check for updates",MB_OK|MB_ICONWARNING);
    }
    return 0;
}

LRESULT CALLBACK UpdateProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    auto* info=(UpdateInfo*)GetWindowLongPtrW(w,GWLP_USERDATA);
    switch(m){
    case WM_CREATE:{
        auto* cs=(CREATESTRUCTW*)lp;
        info=(UpdateInfo*)cs->lpCreateParams;
        SetWindowLongPtrW(w,GWLP_USERDATA,(LONG_PTR)info);

        HFONT title=CreateFontW(-20,0,0,0,FW_SEMIBOLD,0,0,0,DEFAULT_CHARSET,0,0,
            CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
        SetPropW(w,L"UpdateTitleFont",title);

        std::wstring heading=L"NvProfileSwitcher ";
        heading+=info->version;
        heading+=L" is available";

        HWND hTitle=CreateWindowExW(0,L"STATIC",heading.c_str(),WS_CHILD|WS_VISIBLE,
            22,20,420,28,w,nullptr,gInst,nullptr);
        SendMessageW(hTitle,WM_SETFONT,(WPARAM)title,TRUE);

        std::wstring current=L"You are currently running version ";
        current+=APP_VERSION;
        current+=L".";
        HWND hCurrent=CreateWindowExW(0,L"STATIC",current.c_str(),WS_CHILD|WS_VISIBLE,
            22,60,420,22,w,nullptr,gInst,nullptr);
        SendMessageW(hCurrent,WM_SETFONT,(WPARAM)gFont,TRUE);

        HWND hText=CreateWindowExW(0,L"STATIC",
            L"A newer version is available on GitHub.",
            WS_CHILD|WS_VISIBLE,22,88,420,22,w,nullptr,gInst,nullptr);
        SendMessageW(hText,WM_SETFONT,(WPARAM)gFont,TRUE);

        HWND download=CreateWindowExW(0,L"BUTTON",L"Download",WS_CHILD|WS_VISIBLE|BS_OWNERDRAW,
            238,130,100,36,w,(HMENU)3101,gInst,nullptr);
        SendMessageW(download,WM_SETFONT,(WPARAM)gFontBold,TRUE);

        HWND later=CreateWindowExW(0,L"BUTTON",L"Later",WS_CHILD|WS_VISIBLE|BS_OWNERDRAW,
            350,130,100,36,w,(HMENU)IDCANCEL,gInst,nullptr);
        SendMessageW(later,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        return 0;
    }
    case WM_CTLCOLORSTATIC:{
        HDC dc=(HDC)wp;
        SetTextColor(dc,C_TEXT);
        SetBkColor(dc,C_BACK);
        SetBkMode(dc,TRANSPARENT);
        return (LRESULT)gBackBrush;
    }
    case WM_DRAWITEM:{
        auto* d=(DRAWITEMSTRUCT*)lp;
        if(d->CtlID==3101||d->CtlID==IDCANCEL){
            bool down=(d->itemState&ODS_SELECTED)!=0;
            RECT r=d->rcItem;
            COLORREF fill=d->CtlID==3101?(down?C_ACCENT2:C_ACCENT):(down?C_ACCENT_DARK:C_PANEL2);
            COLORREF border=d->CtlID==3101?C_ACCENT:C_BORDER;
            FillRound(d->hDC,r,fill,border,7);
            const wchar_t* text=d->CtlID==3101?L"Download":L"Later";
            SIZE z{};
            SelectObject(d->hDC,gFontBold);
            GetTextExtentPoint32W(d->hDC,text,(int)wcslen(text),&z);
            DrawLabel(d->hDC,text,r.left+(r.right-r.left-z.cx)/2,
                r.top+(r.bottom-r.top-z.cy)/2,d->CtlID==3101?RGB(8,15,8):C_TEXT,gFontBold);
            return TRUE;
        }
        break;
    }
    case WM_COMMAND:
        if(LOWORD(wp)==3101){
            if(info&&!info->url.empty())
                ShellExecuteW(w,L"open",info->url.c_str(),nullptr,nullptr,SW_SHOWNORMAL);
            DestroyWindow(w);
            return 0;
        }
        if(LOWORD(wp)==IDCANCEL){
            DestroyWindow(w);
            return 0;
        }
        break;
    case WM_CLOSE:
        DestroyWindow(w);
        return 0;
    case WM_DESTROY:{
        HFONT f=(HFONT)RemovePropW(w,L"UpdateTitleFont");
        if(f)DeleteObject(f);
        delete info;
        SetWindowLongPtrW(w,GWLP_USERDATA,0);
        return 0;
    }}
    return DefWindowProcW(w,m,wp,lp);
}

void ShowUpdateAvailable(UpdateInfo* info){
    static bool registered=false;
    if(!registered){
        WNDCLASSEXW wc{sizeof(wc)};
        wc.lpfnWndProc=UpdateProc;
        wc.hInstance=gInst;
        wc.hIcon=gIcon;
        wc.hIconSm=gIcon;
        wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hbrBackground=gBackBrush;
        wc.lpszClassName=L"NvProfileSwitcherUpdate";
        RegisterClassExW(&wc);
        registered=true;
    }

    HWND a=CreateWindowExW(WS_EX_DLGMODALFRAME|WS_EX_TOPMOST,L"NvProfileSwitcherUpdate",
        L"NvProfileSwitcher Update",WS_CAPTION|WS_SYSMENU,
        0,0,488,214,nullptr,nullptr,gInst,info);
    if(!a){delete info;return;}

    BOOL darkTitle=TRUE;
    DwmSetWindowAttribute(a,20,&darkTitle,sizeof(darkTitle));

    RECT wr{},work{};
    GetWindowRect(a,&wr);
    SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);
    int ww=wr.right-wr.left, wh=wr.bottom-wr.top;
    int x=work.left+((work.right-work.left)-ww)/2;
    int y=work.top+((work.bottom-work.top)-wh)/2;

    ShowWindow(a,SW_SHOW);
    SetWindowPos(a,HWND_TOPMOST,x,y,0,0,SWP_NOSIZE|SWP_SHOWWINDOW);
    UpdateWindow(a);
    SetForegroundWindow(a);
}

LRESULT CALLBACK AboutProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    switch(m){
    case WM_CREATE:{
        HFONT title=CreateFontW(-21,0,0,0,FW_SEMIBOLD,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
        SetPropW(w,L"AboutTitleFont",title);

        HWND icon=CreateWindowExW(0,L"STATIC",nullptr,WS_CHILD|WS_VISIBLE|SS_ICON,22,22,40,40,w,nullptr,gInst,nullptr);
        SendMessageW(icon,STM_SETICON,(WPARAM)gIcon,0);

        HWND name=CreateWindowExW(0,L"STATIC",L"NvProfileSwitcher",WS_CHILD|WS_VISIBLE,76,19,260,30,w,nullptr,gInst,nullptr);
        SendMessageW(name,WM_SETFONT,(WPARAM)title,TRUE);

        std::wstring ver=L"Version ";
        ver+=APP_VERSION;
        HWND version=CreateWindowExW(0,L"STATIC",ver.c_str(),WS_CHILD|WS_VISIBLE,76,48,260,22,w,nullptr,gInst,nullptr);
        SendMessageW(version,WM_SETFONT,(WPARAM)gFont,TRUE);

        HWND desc=CreateWindowExW(0,L"STATIC",L"Automatic per-app NVIDIA display color profiles for Windows",
            WS_CHILD|WS_VISIBLE,22,84,430,22,w,nullptr,gInst,nullptr);
        SendMessageW(desc,WM_SETFONT,(WPARAM)gFont,TRUE);

        HWND copy=CreateWindowExW(0,L"STATIC",L"Copyright \x00A9 2026 Maximiliano Carnevali",
            WS_CHILD|WS_VISIBLE,22,118,350,22,w,nullptr,gInst,nullptr);
        SendMessageW(copy,WM_SETFONT,(WPARAM)gFont,TRUE);

        HWND github=CreateWindowExW(0,L"BUTTON",L"GitHub",WS_CHILD|WS_VISIBLE|BS_OWNERDRAW,126,158,100,36,w,(HMENU)3001,gInst,nullptr);
        SendMessageW(github,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        HWND support=CreateWindowExW(0,L"BUTTON",L"Support",WS_CHILD|WS_VISIBLE|BS_OWNERDRAW,238,158,100,36,w,(HMENU)3002,gInst,nullptr);
        SendMessageW(support,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        HWND close=CreateWindowExW(0,L"BUTTON",L"Close",WS_CHILD|WS_VISIBLE|BS_OWNERDRAW,350,158,100,36,w,(HMENU)IDCANCEL,gInst,nullptr);
        SendMessageW(close,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        return 0;
    }
    case WM_CTLCOLORSTATIC:{
        HDC dc=(HDC)wp;
        SetTextColor(dc,C_TEXT);
        SetBkColor(dc,C_BACK);
        SetBkMode(dc,TRANSPARENT);
        return (LRESULT)gBackBrush;
    }
    case WM_DRAWITEM:{
        auto* d=(DRAWITEMSTRUCT*)lp;
        if(d->CtlID==3001 || d->CtlID==3002 || d->CtlID==IDCANCEL){
            bool down=(d->itemState&ODS_SELECTED)!=0;
            RECT r=d->rcItem;
            FillRound(d->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,7);
            const wchar_t* text=d->CtlID==3001?L"GitHub":(d->CtlID==3002?L"Support":L"Close");
            SIZE z{};
            SelectObject(d->hDC,gFontBold);
            GetTextExtentPoint32W(d->hDC,text,(int)wcslen(text),&z);
            DrawLabel(d->hDC,text,r.left+(r.right-r.left-z.cx)/2,r.top+(r.bottom-r.top-z.cy)/2,C_TEXT,gFontBold);
            return TRUE;
        }
        break;
    }
    case WM_COMMAND:
        if(LOWORD(wp)==3001){
            ShellExecuteW(w,L"open",APP_URL,nullptr,nullptr,SW_SHOWNORMAL);
            return 0;
        }
        if(LOWORD(wp)==3002){
            ShellExecuteW(w,L"open",SUPPORT_URL,nullptr,nullptr,SW_SHOWNORMAL);
            return 0;
        }
        if(LOWORD(wp)==IDCANCEL){
            DestroyWindow(w);
            return 0;
        }
        break;
    case WM_CLOSE:
        DestroyWindow(w);
        return 0;
    case WM_DESTROY:{
        HFONT f=(HFONT)RemovePropW(w,L"AboutTitleFont");
        if(f)DeleteObject(f);
        return 0;
    }}
    return DefWindowProcW(w,m,wp,lp);
}

void ShowAbout(){
    static bool registered=false;
    if(!registered){
        WNDCLASSEXW wc{sizeof(wc)};
        wc.lpfnWndProc=AboutProc;
        wc.hInstance=gInst;
        wc.hIcon=gIcon;
        wc.hIconSm=gIcon;
        wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hbrBackground=gBackBrush;
        wc.lpszClassName=L"NvProfileSwitcherAbout";
        RegisterClassExW(&wc);
        registered=true;
    }

    HWND existing=FindWindowW(L"NvProfileSwitcherAbout",nullptr);
    if(existing){
        SetForegroundWindow(existing);
        return;
    }

    HWND a=CreateWindowExW(WS_EX_DLGMODALFRAME|WS_EX_TOPMOST,L"NvProfileSwitcherAbout",L"About NvProfileSwitcher",
        WS_CAPTION|WS_SYSMENU,0,0,488,242,nullptr,nullptr,gInst,nullptr);
    if(!a)return;

    BOOL darkTitle=TRUE;
    DwmSetWindowAttribute(a,20,&darkTitle,sizeof(darkTitle));

    RECT wr{},work{};
    GetWindowRect(a,&wr);
    SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);
    int ww=wr.right-wr.left, wh=wr.bottom-wr.top;
    int x=work.left+((work.right-work.left)-ww)/2;
    int y=work.top+((work.bottom-work.top)-wh)/2;

    ShowWindow(a,SW_SHOW);
    SetWindowPos(a,HWND_TOPMOST,x,y,0,0,SWP_NOSIZE|SWP_SHOWWINDOW);
    UpdateWindow(a);
    SetForegroundWindow(a);
}

bool gTrayIconVisible=false;

void SetTrayIconVisible(bool visible){
    if(visible==gTrayIconVisible) return;

    if(visible){
        if(Shell_NotifyIconW(NIM_ADD,&gNid))
            gTrayIconVisible=true;
    }else{
        Shell_NotifyIconW(NIM_DELETE,&gNid);
        gTrayIconVisible=false;
    }
}

void ShowMain(){
    if(IsIconic(gWnd))
        ShowWindow(gWnd,SW_RESTORE);
    else
        ShowWindow(gWnd,SW_SHOW);

    // Restored/visible window lives only in the taskbar.
    SetTrayIconVisible(false);

    SetForegroundWindow(gWnd);
    BringWindowToTop(gWnd);
} void RestoreDesktop(){RestoreAllDesktopProfiles();gActive=L"Windows";InvalidateRect(gWnd,nullptr,FALSE);}
LRESULT CALLBACK Proc(HWND w,UINT m,WPARAM wp,LPARAM lp){switch(m){case WM_SHOW_EXISTING_INSTANCE:ShowMain();return 0;case WM_UPDATE_AVAILABLE:ShowUpdateAvailable((UpdateInfo*)lp);return 0;case WM_CREATE:gWnd=w;BuildControls();RefreshList();LoadSelected();SetTimer(w,1,250,nullptr);return 0;case WM_ACTIVATE:
    if(LOWORD(wp)!=WA_INACTIVE) RefreshDriverVersion();
    return 0;case WM_SIZE:
    if(wp==SIZE_MINIMIZED){
        HideExecutableTooltip();
        DiscardPreview();
        LoadSelected();
        if(gSettings.minimizeToTray){
            SetTrayIconVisible(true);
            ShowWindow(w,SW_HIDE);
        }else{
            SetTrayIconVisible(false);
        }
        return 0;
    }
    ResizeControls();
    InvalidateRect(w,nullptr,TRUE);
    return 0;case WM_PAINT:Paint(w);return 0;
case WM_NOTIFY:{
    auto* hdr=(NMHDR*)lp;
    int id=GetDlgCtrlID(hdr->hwndFrom);
    if(hdr->code==NM_CUSTOMDRAW && (id==IDC_VIB||id==IDC_HUE||id==IDC_BRI||id==IDC_CON||id==IDC_GAM))
        return CustomDrawSlider((NMCUSTOMDRAW*)lp);
    break;
}
case WM_SETCURSOR:{
    HWND target=(HWND)wp;
    int cid=GetDlgCtrlID(target);
    if(cid==IDC_FOOT_GITHUB||cid==IDC_FOOT_SUPPORT||cid==IDC_FOOT_ABOUT){
        SetCursor(LoadCursor(nullptr,IDC_HAND));
        return TRUE;
    }
    break;
}
case WM_CTLCOLORLISTBOX:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL);return (LRESULT)gPanelBrush;}
case WM_CTLCOLORSTATIC:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL);SetBkMode(dc,TRANSPARENT);return (LRESULT)gPanelBrush;}case WM_CTLCOLOREDIT:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_FIELD);return (LRESULT)gFieldBrush;}case WM_CTLCOLORBTN:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL);return (LRESULT)gPanelBrush;}case WM_DRAWITEM:{
    auto*d=(DRAWITEMSTRUCT*)lp;

    if(d->CtlID==IDC_DISPLAY){
        Fill(d->hDC,d->rcItem.left,d->rcItem.top,d->rcItem.right-d->rcItem.left,d->rcItem.bottom-d->rcItem.top,
             (d->itemState&ODS_SELECTED)?C_ACCENT_DARK:C_FIELD);
        if(d->itemID!=(UINT)-1){
            wchar_t txt[256]{};
            SendMessageW(d->hwndItem,CB_GETLBTEXT,d->itemID,(LPARAM)txt);
            RECT tr=d->rcItem;tr.left+=10;tr.right-=6;
            SetBkMode(d->hDC,TRANSPARENT);SetTextColor(d->hDC,C_TEXT);SelectObject(d->hDC,gFont);
            DrawTextW(d->hDC,txt,-1,&tr,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS);
        }
        return TRUE;
    }

    if(d->CtlID==IDC_VALVIB||d->CtlID==IDC_VALHUE||d->CtlID==IDC_VALBRI||d->CtlID==IDC_VALCON||d->CtlID==IDC_VALGAM){
        DrawValueBox(d);return TRUE;
    }

    if(d->CtlID==IDC_ADD||d->CtlID==IDC_REMOVE){
        DrawProfileHeaderButton(d);return TRUE;
    }
    if(d->CtlID==IDC_SAVE||d->CtlID==IDC_DEFAULTS||d->CtlID==IDC_BROWSE){
        DrawOwnerButton(d);return TRUE;
    }
    if(d->CtlID==IDC_FOOT_GITHUB||d->CtlID==IDC_FOOT_SUPPORT||d->CtlID==IDC_FOOT_ABOUT){
        DrawFooterLink(d);return TRUE;
    }

    if(d->CtlID==IDC_LIST&&d->itemID!=(UINT)-1){
        const bool selected=(d->itemState&ODS_SELECTED)!=0;

        RECT row=d->rcItem;
        row.left+=5;
        row.right-=5;
        row.top+=5;
        row.bottom-=5;

        if(selected)
            FillRound(d->hDC,row,RGB(15,34,20),C_ACCENT,9);
        else
            FillRound(d->hDC,row,C_PANEL,C_PANEL,9);

        bool desktop=d->itemID==0;
        ApplicationProfile* p=desktop?&gSettings.desktop:&gSettings.profiles[d->itemID-1];

        const int iconSize=44;
        const int x=d->rcItem.left+16;
        const int y=d->rcItem.top+(d->rcItem.bottom-d->rcItem.top-iconSize)/2;

        if(desktop){
            DrawWindowsLogo(d->hDC,x+1,y,iconSize);
        }else{
            HICON ic=LoadExeIcon(p->exePath);
            if(ic){
                DrawIconEx(d->hDC,x,y,ic,iconSize,iconSize,0,nullptr,DI_NORMAL);
                DestroyIcon(ic);
            }else{
                Gdiplus::Graphics g(d->hDC);
                g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
                Gdiplus::Color accent(255,GetRValue(C_ACCENT),GetGValue(C_ACCENT),GetBValue(C_ACCENT));
                Gdiplus::Pen pen(accent,2.4f);
                pen.SetStartCap(Gdiplus::LineCapRound);
                pen.SetEndCap(Gdiplus::LineCapRound);

                const Gdiplus::REAL inset=3.0f;
                g.DrawEllipse(&pen,
                    (Gdiplus::REAL)x+inset,(Gdiplus::REAL)y+inset,
                    (Gdiplus::REAL)iconSize-inset*2,(Gdiplus::REAL)iconSize-inset*2);

                const Gdiplus::REAL cx=(Gdiplus::REAL)x+iconSize/2.0f;
                const Gdiplus::REAL cy=(Gdiplus::REAL)y+iconSize/2.0f;
                const Gdiplus::REAL arm=8.0f;
                g.DrawLine(&pen,cx-arm,cy,cx+arm,cy);
                g.DrawLine(&pen,cx,cy-arm,cx,cy+arm);
            }
        }

        const wchar_t* title=desktop?L"Windows":p->name.c_str();
        RECT titleRect{x+58,d->rcItem.top,d->rcItem.right-14,d->rcItem.bottom};
        SetBkMode(d->hDC,TRANSPARENT);
        SetTextColor(d->hDC,C_TEXT);
        SelectObject(d->hDC,gFontBold);
        DrawTextW(d->hDC,title,-1,&titleRect,
            DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);

        if(!selected){
            Fill(d->hDC,d->rcItem.left+12,d->rcItem.bottom-1,
                 d->rcItem.right-d->rcItem.left-24,1,C_BORDER);
        }
        return TRUE;
    }
    break;
}case WM_HSCROLL:UpdateSliderLabels();if((HWND)lp)InvalidateRect((HWND)lp,nullptr,FALSE);RequestPreview();return 0;
case WM_DISPLAYCHANGE:
    KillTimer(w,2);
    SetTimer(w,2,750,nullptr);
    return 0;
case WM_DEVICECHANGE:
    if(wp==DBT_DEVNODES_CHANGED || wp==DBT_DEVICEARRIVAL || wp==DBT_DEVICEREMOVECOMPLETE){
        KillTimer(w,2);
        SetTimer(w,2,750,nullptr);
    }
    return 0;
case WM_TIMER:
    if(wp==1){
        CheckProcesses();
        return 0;
    }
    if(wp==2){
        KillTimer(w,2);
        RefreshDisplayTopology();
        return 0;
    }
    return 0;
case WM_COMMAND:{int id=LOWORD(wp);if(id==IDC_LIST&&HIWORD(wp)==LBN_SELCHANGE){HideExecutableTooltip();DiscardPreview();LoadSelected();return 0;}if(id==IDC_DISPLAY&&HIWORD(wp)==CBN_SELCHANGE){DiscardPreview();int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);if(ds>=0&&ds<(int)gDisplays.size()){if(IsDesktopSelected()){auto*p=EnsureDesktopProfile(gDisplays[ds].gdiName,gDisplays[ds].monitorId);LoadValuesToSliders(ValuesFromFlatProfile(*p));}else{auto*p=SelectedProfile();if(p){LoadValuesToSliders(*EnsureApplicationValuesForDisplay(*p,gDisplays[ds].gdiName,gDisplays[ds].monitorId));}}}return 0;}switch(id){case IDC_BROWSE:{OPENFILENAMEW o{sizeof(o)};wchar_t f[MAX_PATH]{};o.hwndOwner=w;o.lpstrFilter=L"Executables (*.exe)\0*.exe\0All files\0*.*\0";o.lpstrFile=f;o.nMaxFile=MAX_PATH;o.Flags=OFN_FILEMUSTEXIST;if(GetOpenFileNameW(&o)){Txt(IDC_EXE,f);InvalidateRect(H(IDC_EXE),nullptr,TRUE);auto* p=SelectedProfile();if(p&&!IsDesktopSelected()){p->exePath=f;InvalidateRect(H(IDC_LIST),nullptr,TRUE);}}break;}case IDC_DEFAULTS:ResetSlidersToDefaults();break;case IDC_SAVE:SaveSelected();break;case IDC_ADD:{ApplicationProfile np{};if(!gDisplays.empty()){for(const auto&d:gDisplays)np.displayProfiles.push_back(ApplicationDefaultsForDisplay(d.gdiName,d.monitorId));}gSettings.profiles.push_back(np);gSelected=(int)gSettings.profiles.size();Save();RefreshList();LoadSelected();break;}case IDC_REMOVE:if(gSelected>0&&gSelected<=(int)gSettings.profiles.size()){gSettings.profiles.erase(gSettings.profiles.begin()+(gSelected-1));gSelected=std::max<int>(0,gSelected-1);Save();RefreshList();LoadSelected();}break;case IDC_STARTWIN:gSettings.startWindows=SendMessageW(H(IDC_STARTWIN),BM_GETCHECK,0,0)==BST_CHECKED;SetStartup(gSettings.startWindows);Save();break;case IDC_STARTMIN:gSettings.startMinimized=SendMessageW(H(IDC_STARTMIN),BM_GETCHECK,0,0)==BST_CHECKED;Save();break;case IDC_MINTRAY:
    gSettings.minimizeToTray=SendMessageW(H(IDC_MINTRAY),BM_GETCHECK,0,0)==BST_CHECKED;
    if(!gSettings.minimizeToTray)
        SetTrayIconVisible(false);
    Save();
    break;case IDC_CHECKUPDATES:gSettings.checkUpdates=SendMessageW(H(IDC_CHECKUPDATES),BM_GETCHECK,0,0)==BST_CHECKED;Save();break;case IDC_FOOT_GITHUB:ShellExecuteW(w,L"open",APP_URL,nullptr,nullptr,SW_SHOWNORMAL);break;
case IDC_FOOT_SUPPORT:ShellExecuteW(w,L"open",SUPPORT_URL,nullptr,nullptr,SW_SHOWNORMAL);break;
case IDC_FOOT_ABOUT:ShowAbout();break;
case ID_TRAY_OPEN:ShowMain();break;case ID_TRAY_CHECK_UPDATE:{if(HANDLE h=CreateThread(nullptr,0,UpdateCheckThread,(LPVOID)1,0,nullptr))CloseHandle(h);break;}case ID_TRAY_ABOUT:ShowAbout();break;case ID_TRAY_EXIT:DestroyWindow(w);break;}return 0;}case WM_CLOSE:
    DestroyWindow(w);
    return 0;case WM_TRAY:if(lp==WM_LBUTTONDBLCLK){ShowMain();return 0;}if(lp==WM_RBUTTONUP||lp==WM_CONTEXTMENU){POINT p;GetCursorPos(&p);SetForegroundWindow(w);TrackPopupMenu(gTrayMenu,TPM_RIGHTBUTTON,p.x,p.y,0,w,nullptr);return 0;}break;case WM_DESTROY:KillTimer(w,1);KillTimer(w,2);SetTrayIconVisible(false);if(pUnload)pUnload();if(gNv)FreeLibrary(gNv);PostQuitMessage(0);return 0;}return DefWindowProcW(w,m,wp,lp);} 

int CALLBACK DetectFontFamily(const LOGFONTW*,const TEXTMETRICW*,DWORD,LPARAM data){
    *reinterpret_cast<bool*>(data)=true;
    return 0;
}

bool FontFamilyAvailable(const wchar_t* family){
    HDC dc=GetDC(nullptr);
    if(!dc) return false;
    LOGFONTW lf{};
    lf.lfCharSet=DEFAULT_CHARSET;
    wcsncpy_s(lf.lfFaceName,family,_TRUNCATE);
    bool found=false;
    EnumFontFamiliesExW(dc,&lf,(FONTENUMPROCW)DetectFontFamily,(LPARAM)&found,0);
    ReleaseDC(nullptr,dc);
    return found;
}

HFONT CreateUiFont(int height,int weight,const wchar_t* family){
    return CreateFontW(height,0,0,0,weight,FALSE,FALSE,FALSE,DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,
        DEFAULT_PITCH|FF_DONTCARE,family);
}

int WINAPI wWinMain(HINSTANCE h,HINSTANCE,LPWSTR cmd,int){
HANDLE instanceMutex=CreateMutexW(nullptr,TRUE,INSTANCE_MUTEX_NAME);
if(instanceMutex && GetLastError()==ERROR_ALREADY_EXISTS){
    HWND existing=FindWindowW(L"NvProfileSwitcherNative",nullptr);
    if(existing){
        PostMessageW(existing,WM_SHOW_EXISTING_INSTANCE,0,0);
    }
    CloseHandle(instanceMutex);
    return 0;
}
gInst=h;
InitializeCriticalSection(&gNvApplyLock);
InitializeCriticalSection(&gPreviewLock);
gPreviewEvent=CreateEventW(nullptr,FALSE,FALSE,nullptr);
if(gPreviewEvent)
    gPreviewThread=CreateThread(nullptr,0,PreviewThreadProc,nullptr,0,nullptr);

Gdiplus::GdiplusStartupInput gdiplusInput;
if(Gdiplus::GdiplusStartup(&gGdiPlusToken,&gdiplusInput,nullptr)!=Gdiplus::Ok)
    gGdiPlusToken=0;
if(gGdiPlusToken){LoadHeaderImage();LoadSliderIcons();}
INITCOMMONCONTROLSEX ic{sizeof(ic),ICC_BAR_CLASSES|ICC_STANDARD_CLASSES};InitCommonControlsEx(&ic);Load();gSettings.desktop.name=L"Windows";gBackBrush=CreateSolidBrush(C_BACK);gPanelBrush=CreateSolidBrush(C_PANEL);gPanel2Brush=CreateSolidBrush(C_PANEL2);gFieldBrush=CreateSolidBrush(C_FIELD);
const wchar_t* uiFamily=FontFamilyAvailable(L"Bahnschrift")?L"Bahnschrift":L"Segoe UI";
gFont=CreateUiFont(-15,FW_NORMAL,uiFamily);
gFontBold=CreateUiFont(-15,FW_SEMIBOLD,uiFamily);
gFontPanelTitle=CreateUiFont(-18,FW_SEMIBOLD,uiFamily);
gFontTitle=CreateUiFont(-24,FW_BOLD,uiFamily);
gFontSmall=CreateUiFont(-13,FW_NORMAL,uiFamily);
gFontHeaderButton=CreateUiFont(-14,FW_SEMIBOLD,uiFamily);
gIconFont=CreateFontW(-18,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe MDL2 Assets");gIcon=LoadIconW(h,MAKEINTRESOURCEW(IDI_APPICON));WNDCLASSEXW wc{sizeof(wc)};wc.style=CS_HREDRAW|CS_VREDRAW;wc.lpfnWndProc=Proc;wc.hInstance=h;wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherNative";RegisterClassExW(&wc);
gWnd=CreateWindowExW(0,wc.lpszClassName,L"NvProfileSwitcher",WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,CW_USEDEFAULT,CW_USEDEFAULT,1360,930,nullptr,nullptr,h,nullptr);
BOOL darkTitle=TRUE;DwmSetWindowAttribute(gWnd,20,&darkTitle,sizeof(darkTitle));

// Center the main window on the primary monitor.
RECT mainWr{},mainWork{};
GetWindowRect(gWnd,&mainWr);
SystemParametersInfoW(SPI_GETWORKAREA,0,&mainWork,0);
int mainW=mainWr.right-mainWr.left, mainH=mainWr.bottom-mainWr.top;
int mainX=mainWork.left+((mainWork.right-mainWork.left)-mainW)/2;
int mainY=mainWork.top+((mainWork.bottom-mainWork.top)-mainH)/2;
SetWindowPos(gWnd,nullptr,mainX,mainY,0,0,SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE);
SetWindowLongPtrW(gWnd,GWLP_USERDATA,0);gTrayMenu=CreatePopupMenu();AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_OPEN,L"Open NvProfileSwitcher");AppendMenuW(gTrayMenu,MF_SEPARATOR,0,nullptr);AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_CHECK_UPDATE,L"Check for updates");AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_ABOUT,L"About NvProfileSwitcher");AppendMenuW(gTrayMenu,MF_SEPARATOR,0,nullptr);AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_EXIT,L"Exit");gNid.cbSize=sizeof(gNid);gNid.hWnd=gWnd;gNid.uID=1;gNid.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;gNid.uCallbackMessage=WM_TRAY;gNid.hIcon=gIcon;wcscpy_s(gNid.szTip,L"NvProfileSwitcher");gStatusOk=InitNv();if(gStatusOk){for(const auto&d:gDisplays)EnsureDesktopProfile(d.gdiName,d.monitorId);EnsureAllApplicationDisplayProfiles();Save();if(auto* p=SelectedProfile())RefreshDisplayCombo(*p);RestoreAllDesktopProfiles();LoadSelected();}gActive=L"Windows";bool min=(wcsstr(cmd,L"--minimized")!=nullptr);
if(min) SetTrayIconVisible(true);
ShowWindow(gWnd,min?SW_HIDE:SW_SHOW);
UpdateWindow(gWnd);if(gSettings.checkUpdates){if(HANDLE h=CreateThread(nullptr,0,UpdateCheckThread,nullptr,0,nullptr))CloseHandle(h);}MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}DeleteObject(gFont);DeleteObject(gFontBold);DeleteObject(gFontPanelTitle);DeleteObject(gFontTitle);DeleteObject(gFontSmall);DeleteObject(gFontHeaderButton);DeleteObject(gIconFont);DeleteObject(gBackBrush);DeleteObject(gPanelBrush);DeleteObject(gPanel2Brush);DeleteObject(gFieldBrush);
if(gHeaderImage){delete gHeaderImage;gHeaderImage=nullptr;}
for(auto** image:{&gSliderBrightness,&gSliderContrast,&gSliderGamma,&gSliderVibrance,&gSliderHue,&gNvidiaDriverIcon}){
    if(*image){delete *image;*image=nullptr;}
}
if(gGdiPlusToken){Gdiplus::GdiplusShutdown(gGdiPlusToken);gGdiPlusToken=0;}
InterlockedExchange(&gPreviewStop,1);
if(gPreviewEvent)SetEvent(gPreviewEvent);
if(gPreviewThread){WaitForSingleObject(gPreviewThread,2000);CloseHandle(gPreviewThread);gPreviewThread=nullptr;}
if(gPreviewEvent){CloseHandle(gPreviewEvent);gPreviewEvent=nullptr;}
DeleteCriticalSection(&gPreviewLock);
DeleteCriticalSection(&gNvApplyLock);
if(instanceMutex)CloseHandle(instanceMutex);return 0;}
