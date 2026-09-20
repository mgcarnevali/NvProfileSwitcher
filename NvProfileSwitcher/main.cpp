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
#include <utility>
#include "resource.h"
#include "switching_core.h"
#include "hotkey_core.h"
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
    WORD hotkey=0;
    std::vector<DisplayProfileValues> displayProfiles;
};
struct Settings {
    ApplicationProfile desktop{L"Windows",L"",true,0,{}}; // template metadata for Windows profiles
    std::vector<ApplicationProfile> desktopProfiles;
    std::vector<ApplicationProfile> profiles;
    bool startWindows=false, startMinimized=false, minimizeToTray=false, checkUpdates=true;
    WORD showHideHotkey=0, windowsOverrideHotkey=0, resumeAutomaticHotkey=0;
};

constexpr COLORREF C_BACK=RGB(10,13,16), C_PANEL=RGB(18,22,26), C_PANEL2=RGB(24,29,34), C_FIELD=RGB(20,24,28), C_BORDER=RGB(45,52,59);
constexpr COLORREF C_TEXT=RGB(241,244,247), C_MUTED=RGB(151,161,171), C_ACCENT=RGB(82,214,39), C_ACCENT2=RGB(43,164,22), C_ACCENT_DARK=RGB(24,50,28), C_DANGER=RGB(232,75,75);
constexpr COLORREF C_TRACK=RGB(61,67,73), C_WINBLUE=RGB(0,120,215);
constexpr UINT WM_TRAY=WM_APP+1;
constexpr UINT WM_UPDATE_AVAILABLE=WM_APP+2;
constexpr UINT WM_SHOW_EXISTING_INSTANCE=WM_APP+3;
constexpr UINT WM_SHOW_APP_MESSAGE=WM_APP+4;
constexpr wchar_t INSTANCE_MUTEX_NAME[]=L"Local\\NvProfileSwitcher_SingleInstance";
constexpr wchar_t APP_VERSION[]=NVPS_VERSION_WSTR;
constexpr wchar_t APP_URL[]=L"https://github.com/mgcarnevali/NvProfileSwitcher";
constexpr wchar_t SUPPORT_URL[]=L"https://ko-fi.com/mgcarnevali";
constexpr wchar_t UPDATE_HOST[]=L"api.github.com";
constexpr wchar_t UPDATE_PATH[]=L"/repos/mgcarnevali/NvProfileSwitcher/releases/latest";
constexpr int DIALOG_MARGIN=22;
constexpr int DIALOG_LINE_GAP=12;
constexpr int DIALOG_SECTION_GAP=16;
constexpr int DIALOG_BUTTON_GAP=12;
constexpr int DIALOG_BUTTON_WIDTH=100;
constexpr int DIALOG_BUTTON_HEIGHT=36;
enum {IDC_LIST=1001,IDC_NAME,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_DISPLAY,IDC_LBL_DISPLAY,IDC_VIB,IDC_HUE,IDC_BRI,IDC_CON,IDC_GAM,IDC_SAVE,IDC_ADD=1015,IDC_REMOVE,IDC_STARTWIN=1018,IDC_STARTMIN,IDC_VALVIB,IDC_VALHUE,IDC_VALBRI,IDC_VALCON,IDC_VALGAM,IDC_LBL_NAME,IDC_LBL_EXE,IDC_LBL_ENABLED,IDC_LBL_VIB,IDC_LBL_HUE,IDC_LBL_BRI,IDC_LBL_CON,IDC_LBL_GAM,IDC_DEFAULTS,IDC_MINTRAY,IDC_CHECKUPDATES,IDC_FOOT_GITHUB,IDC_FOOT_SUPPORT,IDC_FOOT_ABOUT,IDC_APP_SETTINGS_TITLE,IDC_HOTKEY_SHOW_LABEL,IDC_HOTKEY_SHOW,IDC_HOTKEY_SHOW_CLEAR,IDC_HOTKEY_OVERRIDE_LABEL,IDC_HOTKEY_OVERRIDE,IDC_HOTKEY_OVERRIDE_CLEAR,IDC_PROFILE_HOTKEY_LABEL,IDC_PROFILE_HOTKEY,IDC_PROFILE_HOTKEY_CLEAR,IDC_HOTKEY_RESUME_LABEL,IDC_HOTKEY_RESUME,IDC_HOTKEY_RESUME_CLEAR,IDC_CHECKUPDATES_LABEL,IDC_RUNNING_APPS,IDC_PROFILE_UP,IDC_PROFILE_DOWN,IDC_IMPORT_PROFILES,IDC_EXPORT_PROFILES,IDC_MANAGE_DISPLAYS};
enum {ID_TRAY_OPEN=2001,ID_TRAY_CHECK_UPDATE,ID_TRAY_ABOUT,ID_TRAY_EXIT};
enum {ID_HOTKEY_SHOW_HIDE=3001,ID_HOTKEY_WINDOWS_OVERRIDE,ID_HOTKEY_RESUME_AUTOMATIC,ID_HOTKEY_TEST};
enum {IDC_RUNNING_LIST=5101,IDC_RUNNING_REFRESH,IDC_RUNNING_SELECT,IDC_RUNNING_HEADER,IDC_RUNNING_EMPTY};
enum {IDC_MANAGE_LIST=5201,IDC_MANAGE_REMOVE,IDC_MANAGE_CLOSE,IDC_MANAGE_HEADER,IDC_MANAGE_EMPTY};
constexpr int ID_HOTKEY_PROFILE_BASE=4000;


constexpr int MAIN_BASE_CLIENT_WIDTH=1344;
constexpr int MAIN_BASE_CLIENT_HEIGHT=891;
constexpr int MAIN_SAFE_MARGIN=12;
double gUiScale=1.0;
// WM_DPICHANGED can arrive as part of the same monitor transition as
// WM_DISPLAYCHANGE/WM_DEVICECHANGE. Keep its target rect and let the
// existing display-settle timer perform the one responsive resize.
bool gPendingResponsiveRect=false;
RECT gResponsiveSuggestedRect{};
bool gApplyingResponsiveLayout=false;

int Ui(int value){
    return static_cast<int>(std::lround(static_cast<double>(value)*gUiScale));
}
RECT LogicalClientRect(HWND){
    return RECT{0,0,MAIN_BASE_CLIENT_WIDTH,MAIN_BASE_CLIENT_HEIGHT};
}
void MoveUi(HWND hwnd,int x,int y,int width,int height,BOOL repaint=TRUE){
    if(hwnd) MoveWindow(hwnd,Ui(x),Ui(y),Ui(width),Ui(height),repaint);
}

struct ResponsiveDialogState {
    double uiScale=1.0;
    HFONT font{};
    HFONT fontBold{};
};

struct AppMessageData {
    std::wstring title;
    std::wstring text;
    bool deleteOnClose=false;
    bool confirm=false;
    bool result=false;
    ResponsiveDialogState responsive{};
};

HINSTANCE gInst{}; HWND gWnd{}; HFONT gFont{},gFontBold{},gFontPanelTitle{},gFontTitle{},gFontSmall{},gFontHeaderButton{},gIconFont{}; HFONT gBaseFont{},gBaseFontBold{},gBaseFontPanelTitle{},gBaseFontTitle{},gBaseFontSmall{},gBaseFontHeaderButton{},gBaseIconFont{}; HBRUSH gBackBrush{},gPanelBrush{},gPanel2Brush{},gFieldBrush{}; HICON gIcon{};
ULONG_PTR gGdiPlusToken{}; Gdiplus::Image* gHeaderImage{};
Gdiplus::Image *gSliderBrightness{},*gSliderContrast{},*gSliderGamma{},*gSliderVibrance{},*gSliderHue{},*gNvidiaDriverIcon{};
enum class OverrideMode { Automatic, Windows, Profile };
Settings gSettings; int gSelected=-1; std::wstring gActive=L"Windows", gStatus=L"Not initialized", gDriverVersion=L"--"; bool gStatusOk=false;
OverrideMode gOverrideMode=OverrideMode::Automatic;
size_t gOverrideProfileIndex=0;
bool gUpdatingHotkeyControls=false;
NOTIFYICONDATAW gNid{}; HMENU gTrayMenu{};
HWND gFooterHover{};
HWND gMainButtonHover{};
HWND gFocusedHotkey{};
HWND gProfileTooltip{};
HWND gExeTooltip{};
bool gExeTooltipVisible=false;
HWND gResetTooltip{};
bool gResetTooltipVisible=false;
HWND gImportTooltip{};
bool gImportTooltipVisible=false;
HWND gExportTooltip{};
bool gExportTooltipVisible=false;
#if NVPS_DEV_BUILD
HWND gUpdateCheckTooltip{};
bool gUpdateCheckTooltipVisible=false;
#endif
int gProfileTooltipItem=-1;
int gProfileHoverItem=-1;
int gProfilePressedItem=-1;
std::wstring gProfileTooltipText;

void InvalidateFooter(){
    if(!gWnd)return;
    RECT client{};
    GetClientRect(gWnd,&client);
    RECT footer{0,std::max<LONG>(0,client.bottom-56),client.right,client.bottom};
    InvalidateRect(gWnd,&footer,FALSE);
}
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
    std::wstring displayName;
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
    return d+L"\\config.json";
}
std::string W2U(const std::wstring&s){ if(s.empty())return{}; int n=WideCharToMultiByte(CP_UTF8,0,s.c_str(),-1,nullptr,0,nullptr,nullptr); std::string r(n,0); WideCharToMultiByte(CP_UTF8,0,s.c_str(),-1,r.data(),n,nullptr,nullptr); r.pop_back(); return r; }
std::wstring U2W(const std::string&s){ if(s.empty())return{}; int n=MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,nullptr,0); std::wstring r(n,0); MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,r.data(),n); r.pop_back(); return r; }
std::string Escape(const std::wstring&w){ std::string s=W2U(w),o; for(char c:s){ if(c=='\\'||c=='\"')o+='\\'; o+=c;} return o; }
std::wstring Unescape(std::string s){ std::string o; for(size_t i=0;i<s.size();++i){ if(s[i]=='\\'&&i+1<s.size()){ char n=s[++i]; if(n=='n')o+='\n'; else if(n=='r')o+='\r'; else if(n=='t')o+='\t'; else o+=n;} else o+=s[i]; } return U2W(o); }
std::string ReadAll(const std::wstring&p){ std::ifstream f(p,std::ios::binary); if(!f)return{}; return {std::istreambuf_iterator<char>(f),{}}; }
std::string FieldS(const std::string&o,const char*k,const char*d=""){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\""); std::smatch m; return std::regex_search(o,m,r)?m[1].str():d; }
double FieldN(const std::string&o,const char*k,double d){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)"); std::smatch m; return std::regex_search(o,m,r)?std::stod(m[1].str()):d; }
bool FieldB(const std::string&o,const char*k,bool d){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*(true|false)"); std::smatch m; return std::regex_search(o,m,r)?m[1].str()=="true":d; }

bool HasStringField(const std::string&o,const char*k){ std::regex r(std::string("\"")+k+"\"\\s*:\\s*\"(?:\\\\.|[^\"])*\""); return std::regex_search(o,r); }
bool HasNumberField(const std::string&o,const char*k){ std::regex r(std::string("\"")+k+"\"\\s*:\\s*-?[0-9]+(?:\\.[0-9]+)?"); return std::regex_search(o,r); }
bool HasBoolField(const std::string&o,const char*k){ std::regex r(std::string("\"")+k+"\"\\s*:\\s*(?:true|false)"); return std::regex_search(o,r); }


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

bool ValidateDisplayProfileJson(const std::string& o){
    return HasStringField(o,"DisplayName") && HasStringField(o,"MonitorId") &&
           HasNumberField(o,"Brightness") && HasNumberField(o,"Contrast") &&
           HasNumberField(o,"Gamma") && HasNumberField(o,"DigitalVibrance") && HasNumberField(o,"Hue");
}

bool ValidateConfigurationJson(const std::string& s){
    size_t first=s.find_first_not_of(" \t\r\n");
    size_t last=s.find_last_not_of(" \t\r\n");
    if(first==std::string::npos || s[first]!='{' || s[last]!='}' || FindMatchingJson(s,first,'{','}')!=last) return false;

    if(!HasBoolField(s,"StartWithWindows") || !HasBoolField(s,"StartMinimized") ||
       !HasBoolField(s,"MinimizeToTray") || !HasBoolField(s,"CheckForUpdates") ||
       !HasNumberField(s,"ShowHideHotkey") || !HasNumberField(s,"WindowsOverrideHotkey") ||
       !HasNumberField(s,"ResumeAutomaticHotkey")) return false;

    size_t wp=s.find("\"Windows Profiles\"");
    size_t wa=wp==std::string::npos?std::string::npos:s.find('[',wp);
    size_t wb=FindMatchingJson(s,wa,'[',']');
    if(wp==std::string::npos || wa==std::string::npos || wb==std::string::npos) return false;
    for(const auto& obj:JsonObjectsInArray(s,wa,wb))
        if(!HasStringField(obj,"Name") || !ValidateDisplayProfileJson(obj)) return false;

    size_t ap=s.find("\"Application Profiles\"");
    size_t aa=ap==std::string::npos?std::string::npos:s.find('[',ap);
    size_t ab=FindMatchingJson(s,aa,'[',']');
    if(ap==std::string::npos || aa==std::string::npos || ab==std::string::npos) return false;
    for(const auto& obj:JsonObjectsInArray(s,aa,ab)){
        if(!HasStringField(obj,"Name") || !HasStringField(obj,"ExePath") ||
           !HasBoolField(obj,"Enabled") || !HasNumberField(obj,"Hotkey")) return false;
        size_t dp=obj.find("\"Display Profiles\"");
        size_t da=dp==std::string::npos?std::string::npos:obj.find('[',dp);
        size_t db=FindMatchingJson(obj,da,'[',']');
        if(dp==std::string::npos || da==std::string::npos || db==std::string::npos) return false;
        for(const auto& displayObj:JsonObjectsInArray(obj,da,db))
            if(!ValidateDisplayProfileJson(displayObj)) return false;
    }
    return true;
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
    p.hotkey=(WORD)FieldN(o,"Hotkey",0);

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
        std::wstring windowsProfileName=L"Display";
        for(const auto& d:gDisplays){
            if(SameMonitorId(d.monitorId,v.monitorId)){
                windowsProfileName=WindowsProfileJsonName(d.gdiName);
                break;
            }
        }
        f<<"    {\n"
         <<"      \"Name\": \""<<Escape(windowsProfileName)<<"\",\n"
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
         <<"      \"Hotkey\": "<<p.hotkey<<",\n"
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
     <<",\n  \"MinimizeToTray\": "<<(gSettings.minimizeToTray?"true":"false")<<",\n  \"CheckForUpdates\": "<<(gSettings.checkUpdates?"true":"false")
     <<",\n  \"ShowHideHotkey\": "<<gSettings.showHideHotkey
     <<",\n  \"WindowsOverrideHotkey\": "<<gSettings.windowsOverrideHotkey
     <<",\n  \"ResumeAutomaticHotkey\": "<<gSettings.resumeAutomaticHotkey<<"\n}\n";
}
void Load(){
    std::string s=ReadAll(AppDataFile());
    if(s.empty()){Save();return;}
    gSettings.startWindows=FieldB(s,"StartWithWindows",false);
    gSettings.startMinimized=FieldB(s,"StartMinimized",false);
    gSettings.minimizeToTray=FieldB(s,"MinimizeToTray",false);
    gSettings.checkUpdates=FieldB(s,"CheckForUpdates",true);
    gSettings.showHideHotkey=(WORD)FieldN(s,"ShowHideHotkey",0);
    gSettings.windowsOverrideHotkey=(WORD)FieldN(s,"WindowsOverrideHotkey",0);
    gSettings.resumeAutomaticHotkey=(WORD)FieldN(s,"ResumeAutomaticHotkey",0);

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

        gDisplays.push_back({gdi,friendly,label,handle,id,primary,StableMonitorIdForGdi(gdi)});
    }

    if(gDisplays.empty() && gDisplay && gDisplayId){
        gDisplays.push_back({L"",L"Primary NVIDIA display",L"Primary NVIDIA display",gDisplay,gDisplayId,true,L""});
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
    return EnsureDesktopProfile(gDisplays[ds].displayName,gDisplays[ds].monitorId);
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
            EnsureApplicationValuesForDisplay(p,d.displayName,d.monitorId);
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
            InvalidateFooter();
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
        InvalidateFooter();
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

void LoadSelected();

std::vector<nvps::ProfileDescriptor> SwitchingProfiles(){
    std::vector<nvps::ProfileDescriptor> profiles;
    profiles.reserve(gSettings.profiles.size());
    for(const auto& p:gSettings.profiles)
        profiles.push_back({p.name,p.exePath,p.enabled});
    return profiles;
}

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
        name=nvps::NormalizeExecutableName(path);
    }
    CloseHandle(hp);
    return name;
}
void CheckProcesses(){
    if(gOverrideMode==OverrideMode::Profile){
        const auto profiles=SwitchingProfiles();
        if(const auto target=nvps::SelectProfileOverrideTarget(profiles,gOverrideProfileIndex)){
            const auto& profile=gSettings.profiles[*target->profileIndex];
            if(gActive!=profile.name){
                ApplyApplicationProfile(profile);
                gActive=profile.name;
                InvalidateFooter();
            }
            return;
        }
        gOverrideMode=OverrideMode::Automatic;
    }
    std::wstring fgName=ForegroundProcessName();
    const auto target=nvps::SelectSwitchTarget(SwitchingProfiles(),fgName,
        gOverrideMode==OverrideMode::Windows);
    if(target.activeName!=gActive){
        if(target.profileIndex){
            ApplyApplicationProfile(gSettings.profiles[*target.profileIndex]);
        }else{
            // Restore every configured Windows display so each monitor returns
            // to its own saved desktop values.
            RestoreAllDesktopProfiles();
        }
        gActive=target.activeName;
        InvalidateFooter();
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
        EnsureDesktopProfile(d.displayName,d.monitorId);

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
HWND Add(const wchar_t*cls,const wchar_t*txt,DWORD style,int x,int y,int w,int h,int id){ HWND c=CreateWindowExW(0,cls,txt,WS_CHILD|WS_VISIBLE|style,Ui(x),Ui(y),Ui(w),Ui(h),gWnd,(HMENU)(INT_PTR)id,gInst,nullptr); SendMessageW(c,WM_SETFONT,(WPARAM)gFont,TRUE); return c; }

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

double AdaptiveUiScaleForDpi(UINT dpi);
int DialogUi(const ResponsiveDialogState* state,int value);
void RecreateResponsiveDialogFonts(ResponsiveDialogState* state);
void DestroyResponsiveDialogFonts(ResponsiveDialogState* state);
void ApplyResponsiveDialogWindow(HWND w,ResponsiveDialogState* state,UINT dpi,
    int baseClientWidth,int baseClientHeight,const RECT* suggested);

constexpr int APP_MESSAGE_BASE_CLIENT_WIDTH=472;
constexpr int APP_MESSAGE_BASE_CLIENT_HEIGHT=140;
constexpr int APP_MESSAGE_TEXT_WIDTH=374;

int DialogUi(const AppMessageData* data,int value){
    return DialogUi(data?&data->responsive:nullptr,value);
}

void LayoutAppMessageDialog(HWND w,AppMessageData* data){
    if(!w||!data)return;
    RECT client{};GetClientRect(w,&client);

    const int margin=DialogUi(data,DIALOG_MARGIN);
    const int iconSize=DialogUi(data,40);
    const int iconGap=DialogUi(data,14);
    const int messageX=margin+iconSize+iconGap;
    const int messageW=std::max(1,static_cast<int>(client.right)-messageX-margin);
    const int buttonW=DialogUi(data,DIALOG_BUTTON_WIDTH);
    const int buttonH=DialogUi(data,DIALOG_BUTTON_HEIGHT);
    const int buttonGap=DialogUi(data,DIALOG_BUTTON_GAP);
    const int buttonY=client.bottom-margin-buttonH;

    HWND icon=GetDlgItem(w,5401);
    HWND message=GetDlgItem(w,5402);
    MoveWindow(icon,margin,margin,iconSize,iconSize,TRUE);
    MoveWindow(message,messageX,margin,messageW,
        std::max(1,buttonY-margin-DialogUi(data,20)),TRUE);

    if(data->confirm){
        const int noX=client.right-margin-buttonW;
        const int yesX=noX-buttonGap-buttonW;
        MoveWindow(GetDlgItem(w,IDYES),yesX,buttonY,buttonW,buttonH,TRUE);
        MoveWindow(GetDlgItem(w,IDNO),noX,buttonY,buttonW,buttonH,TRUE);
    }else{
        MoveWindow(GetDlgItem(w,IDOK),client.right-margin-buttonW,buttonY,
            buttonW,buttonH,TRUE);
    }

    SendMessageW(message,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    if(HWND yes=GetDlgItem(w,IDYES))SendMessageW(yes,WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    if(HWND no=GetDlgItem(w,IDNO))SendMessageW(no,WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    if(HWND ok=GetDlgItem(w,IDOK))SendMessageW(ok,WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
}

void AutosizeAppMessageDialog(HWND w,AppMessageData* data){
    if(!w||!data)return;
    const int margin=DialogUi(data,DIALOG_MARGIN);
    const int iconSize=DialogUi(data,40);
    const int iconGap=DialogUi(data,14);
    const int messageX=margin+iconSize+iconGap;
    const int messageW=std::max(1,static_cast<int>(std::lround(APP_MESSAGE_TEXT_WIDTH*data->responsive.uiScale)));

    HDC dc=GetDC(w);
    RECT measured{0,0,messageW,0};
    HFONT old=(HFONT)SelectObject(dc,data->responsive.font);
    DrawTextW(dc,data->text.c_str(),-1,&measured,DT_CALCRECT|DT_WORDBREAK|DT_NOPREFIX);
    SelectObject(dc,old);
    ReleaseDC(w,dc);

    const int contentH=std::max(iconSize,std::max(1,static_cast<int>(measured.bottom-measured.top)));
    const int desiredClientH=margin+contentH+DialogUi(data,20)+
        DialogUi(data,DIALOG_BUTTON_HEIGHT)+margin;

    RECT client{};GetClientRect(w,&client);
    const int currentClientH=client.bottom-client.top;
    if(currentClientH==desiredClientH)return;

    RECT wr{};GetWindowRect(w,&wr);
    SetWindowPos(w,nullptr,0,0,wr.right-wr.left,
        (wr.bottom-wr.top)+(desiredClientH-currentClientH),
        SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
}

void ApplyAppMessageResponsiveLayout(HWND w,AppMessageData* data,UINT dpi,const RECT* suggested=nullptr){
    if(!w||!data)return;
    ApplyResponsiveDialogWindow(w,&data->responsive,dpi,
        APP_MESSAGE_BASE_CLIENT_WIDTH,APP_MESSAGE_BASE_CLIENT_HEIGHT,suggested);
    LayoutAppMessageDialog(w,data);
    AutosizeAppMessageDialog(w,data);
    LayoutAppMessageDialog(w,data);
    InvalidateRect(w,nullptr,TRUE);
}

LRESULT CALLBACK AppMessageProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    auto* data=(AppMessageData*)GetWindowLongPtrW(w,GWLP_USERDATA);
    switch(m){
    case WM_CREATE:{
        auto* cs=(CREATESTRUCTW*)lp;
        data=(AppMessageData*)cs->lpCreateParams;
        SetWindowLongPtrW(w,GWLP_USERDATA,(LONG_PTR)data);
        if(!data)return -1;

        data->responsive.uiScale=AdaptiveUiScaleForDpi(GetDpiForWindow(w));
        RecreateResponsiveDialogFonts(&data->responsive);

        HWND icon=CreateWindowExW(0,L"STATIC",nullptr,WS_CHILD|WS_VISIBLE|SS_ICON,
            0,0,1,1,w,(HMENU)5401,gInst,nullptr);
        SendMessageW(icon,STM_SETICON,(WPARAM)gIcon,0);
        CreateWindowExW(0,L"STATIC",data->text.c_str(),WS_CHILD|WS_VISIBLE|SS_LEFT,
            0,0,1,1,w,(HMENU)5402,gInst,nullptr);

        if(data->confirm){
            HWND yes=CreateWindowExW(0,L"BUTTON",L"Yes",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
                0,0,1,1,w,(HMENU)IDYES,gInst,nullptr);
            HWND no=CreateWindowExW(0,L"BUTTON",L"No",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
                0,0,1,1,w,(HMENU)IDNO,gInst,nullptr);
            StyleMainButton(yes);StyleMainButton(no);
            SetFocus(no);
        }else{
            HWND ok=CreateWindowExW(0,L"BUTTON",L"OK",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
                0,0,1,1,w,(HMENU)IDOK,gInst,nullptr);
            StyleMainButton(ok);
            SetFocus(ok);
        }
        ApplyAppMessageResponsiveLayout(w,data,GetDpiForWindow(w));
        return 0;
    }
    case WM_SIZE:
        if(data)LayoutAppMessageDialog(w,data);
        return 0;
    case WM_DPICHANGED:
        if(data){
            const UINT dpi=HIWORD(wp)?HIWORD(wp):GetDpiForWindow(w);
            const RECT* suggested=reinterpret_cast<const RECT*>(lp);
            ApplyAppMessageResponsiveLayout(w,data,dpi,suggested);
        }
        return 0;
    case WM_CTLCOLORSTATIC:{
        HDC dc=(HDC)wp;
        SetTextColor(dc,C_TEXT);
        SetBkColor(dc,C_BACK);
        SetBkMode(dc,TRANSPARENT);
        return (LRESULT)gBackBrush;
    }
    case WM_DRAWITEM:{
        auto* d=(DRAWITEMSTRUCT*)lp;
        if(d->CtlID==IDOK||d->CtlID==IDYES||d->CtlID==IDNO){
            const bool down=(d->itemState&ODS_SELECTED)!=0;
            RECT r=d->rcItem;
            FillRound(d->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,7);
            SetBkMode(d->hDC,TRANSPARENT);
            SetTextColor(d->hDC,C_TEXT);
            HFONT old=(HFONT)SelectObject(d->hDC,data&&data->responsive.fontBold?data->responsive.fontBold:gFontBold);
            const wchar_t* label=d->CtlID==IDYES?L"Yes":d->CtlID==IDNO?L"No":L"OK";
            DrawTextW(d->hDC,label,-1,&r,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            SelectObject(d->hDC,old);
            return TRUE;
        }
        break;
    }
    case WM_COMMAND:
        if(LOWORD(wp)==IDYES&&data&&data->confirm){data->result=true;DestroyWindow(w);return 0;}
        if(LOWORD(wp)==IDNO&&data&&data->confirm){data->result=false;DestroyWindow(w);return 0;}
        if(LOWORD(wp)==IDOK||LOWORD(wp)==IDCANCEL){DestroyWindow(w);return 0;}
        break;
    case WM_CLOSE:
        if(data&&data->confirm)data->result=false;
        DestroyWindow(w);
        return 0;
    case WM_DESTROY:
        if(data)DestroyResponsiveDialogFonts(&data->responsive);
        if(data&&data->deleteOnClose) delete data;
        SetWindowLongPtrW(w,GWLP_USERDATA,0);
        return 0;
    }
    return DefWindowProcW(w,m,wp,lp);
}

HWND CreateAppMessageWindow(AppMessageData* data,HWND owner){
    static bool registered=false;
    if(!registered){
        WNDCLASSEXW wc{sizeof(wc)};
        wc.lpfnWndProc=AppMessageProc;
        wc.hInstance=gInst;
        wc.hIcon=gIcon;
        wc.hIconSm=gIcon;
        wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hbrBackground=gBackBrush;
        wc.lpszClassName=L"NvProfileSwitcherMessage";
        if(!RegisterClassExW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)
            return nullptr;
        registered=true;
    }

    HMONITOR monitor=owner&&IsWindowVisible(owner)
        ?MonitorFromWindow(owner,MONITOR_DEFAULTTONEAREST)
        :MonitorFromPoint(POINT{0,0},MONITOR_DEFAULTTOPRIMARY);
    UINT dpi=96;
    if(owner&&IsWindowVisible(owner))dpi=GetDpiForWindow(owner);
    else if(HDC dc=GetDC(nullptr)){
        dpi=(UINT)GetDeviceCaps(dc,LOGPIXELSX);
        ReleaseDC(nullptr,dc);
    }
    const double scale=AdaptiveUiScaleForDpi(dpi);

    RECT windowRect{0,0,
        static_cast<LONG>(std::lround(APP_MESSAGE_BASE_CLIENT_WIDTH*scale)),
        static_cast<LONG>(std::lround(APP_MESSAGE_BASE_CLIENT_HEIGHT*scale))};
    if(!AdjustWindowRectExForDpi(&windowRect,WS_CAPTION|WS_SYSMENU,FALSE,WS_EX_DLGMODALFRAME,dpi))
        AdjustWindowRectEx(&windowRect,WS_CAPTION|WS_SYSMENU,FALSE,WS_EX_DLGMODALFRAME);
    const int ww=windowRect.right-windowRect.left;
    const int wh=windowRect.bottom-windowRect.top;

    MONITORINFO mi{sizeof(mi)};
    GetMonitorInfoW(monitor,&mi);
    RECT target=mi.rcWork;
    if(owner&&IsWindowVisible(owner))GetWindowRect(owner,&target);
    const int x=target.left+((target.right-target.left)-ww)/2;
    const int y=target.top+((target.bottom-target.top)-wh)/2;

    HWND dialog=CreateWindowExW(WS_EX_DLGMODALFRAME,
        L"NvProfileSwitcherMessage",data->title.c_str(),WS_CAPTION|WS_SYSMENU,
        x,y,ww,wh,owner,nullptr,gInst,data);
    if(!dialog)return nullptr;

    BOOL darkTitle=TRUE;
    DwmSetWindowAttribute(dialog,20,&darkTitle,sizeof(darkTitle));

    // WM_CREATE autosizes the final height. Re-center once using that final size.
    RECT wr{};GetWindowRect(dialog,&wr);
    const int finalW=wr.right-wr.left,finalH=wr.bottom-wr.top;
    const int finalX=target.left+((target.right-target.left)-finalW)/2;
    const int finalY=target.top+((target.bottom-target.top)-finalH)/2;
    SetWindowPos(dialog,HWND_TOP,finalX,finalY,0,0,SWP_NOSIZE|SWP_SHOWWINDOW);
    UpdateWindow(dialog);
    SetForegroundWindow(dialog);
    return dialog;
}

void ShowAppMessage(const std::wstring& title,const std::wstring& text){
    AppMessageData data{title,text,false,false,false};
    HWND owner=gWnd;
    HWND previousFocus=GetFocus();
    const bool disableOwner=owner&&IsWindowEnabled(owner);
    if(disableOwner)EnableWindow(owner,FALSE);
    HWND dialog=CreateAppMessageWindow(&data,owner);
    if(!dialog){
        if(disableOwner)EnableWindow(owner,TRUE);
        return;
    }

    MSG msg{};
    while(IsWindow(dialog)&&GetMessageW(&msg,nullptr,0,0)>0){
        if(msg.message==WM_KEYDOWN&&(msg.wParam==VK_RETURN||msg.wParam==VK_ESCAPE)
            &&(msg.hwnd==dialog||IsChild(dialog,msg.hwnd))){
            DestroyWindow(dialog);
            continue;
        }
        if(!IsDialogMessageW(dialog,&msg)){
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    if(disableOwner){
        EnableWindow(owner,TRUE);
        if(previousFocus&&IsWindow(previousFocus))SetFocus(previousFocus);
        else SetFocus(owner);
        SetForegroundWindow(owner);
    }
}


bool ShowAppConfirm(HWND owner,const std::wstring& title,const std::wstring& text){
    AppMessageData data{title,text,false,true,false};
    HWND previousFocus=GetFocus();
    const bool disableOwner=owner&&IsWindowEnabled(owner);
    if(disableOwner)EnableWindow(owner,FALSE);
    HWND dialog=CreateAppMessageWindow(&data,owner);
    if(!dialog){if(disableOwner)EnableWindow(owner,TRUE);return false;}
    MSG msg{};
    while(IsWindow(dialog)&&GetMessageW(&msg,nullptr,0,0)>0){
        if(!IsDialogMessageW(dialog,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}
    }
    if(disableOwner){
        EnableWindow(owner,TRUE);
        if(previousFocus&&IsWindow(previousFocus))SetFocus(previousFocus);else SetFocus(owner);
        SetForegroundWindow(owner);
    }
    return data.result;
}

void QueueAppMessage(const std::wstring& title,const std::wstring& text){
    auto* data=new AppMessageData{title,text,true,false,false};
    if(!PostMessageW(gWnd,WM_SHOW_APP_MESSAGE,0,(LPARAM)data))delete data;
}

std::wstring HotkeyDisplayText(WORD hotkey){
    if(!hotkey) return L"None";
    std::wstring text;
    const BYTE flags=HIBYTE(hotkey);
    if(flags&HOTKEYF_CONTROL) text+=L"CTRL + ";
    if(flags&HOTKEYF_ALT) text+=L"ALT + ";
    if(flags&HOTKEYF_SHIFT) text+=L"SHIFT + ";
    const UINT vk=LOBYTE(hotkey);
    UINT scan=MapVirtualKeyW(vk,MAPVK_VK_TO_VSC)<<16;
    if(flags&HOTKEYF_EXT) scan|=1u<<24;
    wchar_t keyName[64]{};
    if(GetKeyNameTextW((LONG)scan,keyName,(int)(sizeof(keyName)/sizeof(keyName[0])))) text+=keyName;
    return text;
}

void RegisterConfiguredHotkeys();
void UnregisterConfiguredHotkeys();

LRESULT CALLBACK HotkeyFieldSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                         UINT_PTR subclassId,DWORD_PTR refData){
    constexpr UINT WM_POSITION_HOTKEY_CARET=WM_APP+5;
    switch(msg){
    case WM_POSITION_HOTKEY_CARET:
        if(GetFocus()==hwnd){
            SetCaretPos(8,2);
            InvalidateRect(hwnd,nullptr,FALSE);
        }
        return 0;
    case WM_ERASEBKGND:
        return 1;
    case WM_NCPAINT:
        // The app paints the rounded outer frame; suppress the native hotkey
        // control's light non-client outline so only that frame is visible.
        return 0;
    case WM_SETFOCUS:{
        gFocusedHotkey=hwnd;
        UnregisterConfiguredHotkeys();
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        InvalidateRect(hwnd,nullptr,FALSE);
        PostMessageW(hwnd,WM_POSITION_HOTKEY_CARET,0,0);
        return result;
    }
    case WM_LBUTTONUP:{
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        PostMessageW(hwnd,WM_POSITION_HOTKEY_CARET,0,0);
        return result;
    }
    case WM_KILLFOCUS:{
        if(gFocusedHotkey==hwnd)gFocusedHotkey=nullptr;
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        UnregisterConfiguredHotkeys();
        RegisterConfiguredHotkeys();
        InvalidateRect(hwnd,nullptr,FALSE);
        return result;
    }
    case WM_PAINT:{
        PAINTSTRUCT ps{};
        HDC dc=BeginPaint(hwnd,&ps);
        RECT r{};
        GetClientRect(hwnd,&r);
        FillRect(dc,&r,gFieldBrush);
        const WORD hotkey=(WORD)SendMessageW(hwnd,HKM_GETHOTKEY,0,0);
        const bool empty=!LOBYTE(hotkey);
        const bool focused=gFocusedHotkey==hwnd;
        const std::wstring text=empty&&focused?L"":HotkeyDisplayText(hotkey);
        RECT tr=r; tr.left+=8; tr.right-=6;
        SetBkMode(dc,TRANSPARENT);
        SetTextColor(dc,text==L"None"?C_MUTED:C_TEXT);
        HFONT oldFont=(HFONT)SelectObject(dc,gFont);
        DrawTextW(dc,text.c_str(),-1,&tr,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
        SelectObject(dc,oldFont);
        EndPaint(hwnd,&ps);
        if(focused)SetCaretPos(8,2);
        return 0;
    }
    case WM_NCDESTROY:
        if(gFocusedHotkey==hwnd)gFocusedHotkey=nullptr;
        RemoveWindowSubclass(hwnd,HotkeyFieldSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void RemoveNativeHotkeyFrame(HWND hwnd){
    SetWindowLongPtrW(hwnd,GWL_STYLE,GetWindowLongPtrW(hwnd,GWL_STYLE)&~WS_BORDER);
    SetWindowLongPtrW(hwnd,GWL_EXSTYLE,GetWindowLongPtrW(hwnd,GWL_EXSTYLE)&~WS_EX_CLIENTEDGE);
    SetWindowPos(hwnd,nullptr,0,0,0,0,
                 SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
}

UINT HotkeyModifiers(WORD hotkey){
    const BYTE flags=HIBYTE(hotkey); UINT modifiers=MOD_NOREPEAT;
    if(flags&HOTKEYF_ALT) modifiers|=MOD_ALT;
    if(flags&HOTKEYF_CONTROL) modifiers|=MOD_CONTROL;
    if(flags&HOTKEYF_SHIFT) modifiers|=MOD_SHIFT;
    return modifiers;
}

bool IsCtrlAltHotkey(WORD hotkey){
    return nvps::IsCtrlAltHotkey(hotkey);
}

bool NeedsHotkeyModifier(WORD hotkey){
    return nvps::NeedsHotkeyModifier(hotkey);
}

bool RegisterStoredHotkey(int registrationId,WORD hotkey){
    if(!hotkey) return true;
    if(IsCtrlAltHotkey(hotkey)) return false;
    return RegisterHotKey(gWnd,registrationId,HotkeyModifiers(hotkey),LOBYTE(hotkey))!=FALSE;
}

void SetHotkeyControl(int controlId,WORD hotkey){
    gUpdatingHotkeyControls=true; SendMessageW(H(controlId),HKM_SETHOTKEY,hotkey,0); gUpdatingHotkeyControls=false;
}

std::wstring HotkeyOwnerName(WORD hotkey,const WORD* excluded){
    if(!hotkey)return{};
    if(&gSettings.showHideHotkey!=excluded&&nvps::HotkeysConflict(gSettings.showHideHotkey,hotkey))
        return L"Show / hide window";
    if(&gSettings.windowsOverrideHotkey!=excluded&&nvps::HotkeysConflict(gSettings.windowsOverrideHotkey,hotkey))
        return L"Windows override";
    if(&gSettings.resumeAutomaticHotkey!=excluded&&nvps::HotkeysConflict(gSettings.resumeAutomaticHotkey,hotkey))
        return L"Resume automatic switching";
    for(const auto& profile:gSettings.profiles){
        if(&profile.hotkey!=excluded&&nvps::HotkeysConflict(profile.hotkey,hotkey))
            return profile.name;
    }
    return{};
}

bool UpdateConfiguredHotkey(int controlId,WORD& stored){
    if(gUpdatingHotkeyControls) return true;
    const WORD requested=(WORD)SendMessageW(H(controlId),HKM_GETHOTKEY,0,0);
    if(!LOBYTE(requested)) return true;
    if(requested==stored){SetFocus(gWnd);return true;}
    UnregisterConfiguredHotkeys();
    if(NeedsHotkeyModifier(requested)){
        SetHotkeyControl(controlId,stored);
        ShowAppMessage(L"NvProfileSwitcher",L"Shortcuts must use Ctrl or Alt with another key. Function keys can be used alone.\n\nUse Ctrl + Shift, Alt + Shift, or a function key instead.");
        return false;
    }
    if(IsCtrlAltHotkey(requested)){
        SetHotkeyControl(controlId,stored);
        ShowAppMessage(L"NvProfileSwitcher",L"Ctrl + Alt shortcuts are not supported because Windows may treat Right Alt (AltGr) as Ctrl + Alt.\n\nUse Ctrl + Shift, Alt + Shift, or a function key instead.");
        return false;
    }
    const std::wstring owner=HotkeyOwnerName(requested,&stored);
    if(!owner.empty()){
        SetHotkeyControl(controlId,stored);
        std::wstring message=L"That shortcut is already assigned to ";
        message+=owner;
        message+=L".\n\nChoose a different combination.";
        ShowAppMessage(L"NvProfileSwitcher",message);
        SetFocus(H(controlId));
        return false;
    }
    if(requested&&!RegisterStoredHotkey(ID_HOTKEY_TEST,requested)){
        SetHotkeyControl(controlId,stored);
        ShowAppMessage(L"NvProfileSwitcher",L"That shortcut is already being used by another application.");
        return false;
    }
    UnregisterHotKey(gWnd,ID_HOTKEY_TEST);
    stored=requested; Save(); SetFocus(gWnd); return true;
}

void ClearConfiguredHotkey(int controlId,WORD& stored){
    UnregisterConfiguredHotkeys();
    stored=0;
    SetHotkeyControl(controlId,0);
    Save();
    RegisterConfiguredHotkeys();
    InvalidateRect(H(IDC_LIST),nullptr,FALSE);
}

void UnregisterConfiguredHotkeys(){
    UnregisterHotKey(gWnd,ID_HOTKEY_SHOW_HIDE);
    UnregisterHotKey(gWnd,ID_HOTKEY_WINDOWS_OVERRIDE);
    UnregisterHotKey(gWnd,ID_HOTKEY_RESUME_AUTOMATIC);
    UnregisterHotKey(gWnd,ID_HOTKEY_TEST);
    for(size_t i=0;i<gSettings.profiles.size();++i)
        UnregisterHotKey(gWnd,ID_HOTKEY_PROFILE_BASE+(int)i);
}

void RegisterConfiguredHotkeys(){
    bool unavailable=false;
    if(!RegisterStoredHotkey(ID_HOTKEY_SHOW_HIDE,gSettings.showHideHotkey)) unavailable=true;
    if(!RegisterStoredHotkey(ID_HOTKEY_WINDOWS_OVERRIDE,gSettings.windowsOverrideHotkey)) unavailable=true;
    if(!RegisterStoredHotkey(ID_HOTKEY_RESUME_AUTOMATIC,gSettings.resumeAutomaticHotkey)) unavailable=true;
    for(size_t i=0;i<gSettings.profiles.size();++i){
        if(!gSettings.profiles[i].enabled)continue;
        if(ID_HOTKEY_PROFILE_BASE+(int)i>0xBFFF){unavailable=true;break;}
        if(!RegisterStoredHotkey(ID_HOTKEY_PROFILE_BASE+(int)i,gSettings.profiles[i].hotkey))
            unavailable=true;
    }
    if(unavailable) ShowAppMessage(L"NvProfileSwitcher",L"One or more saved shortcuts could not be enabled.\n\nChoose a supported, unused combination in Hotkey settings.");
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
#if NVPS_DEV_BUILD
        const bool devUpdateCheck=GetDlgCtrlID(hwnd)==IDC_CHECKUPDATES;
#else
        const bool devUpdateCheck=false;
#endif

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

            const Gdiplus::Color top=devUpdateCheck?Gdiplus::Color(255,31,36,41)
                :(checked?Gdiplus::Color(255,82,196,76):Gdiplus::Color(255,37,44,51));
            const Gdiplus::Color bottom=devUpdateCheck?Gdiplus::Color(255,20,24,28)
                :(checked?Gdiplus::Color(255,55,164,60):Gdiplus::Color(255,23,29,34));
            Gdiplus::LinearGradientBrush fill(
                Gdiplus::PointF(box.X,box.Y),
                Gdiplus::PointF(box.X,box.GetBottom()),top,bottom);
            g.FillPath(&fill,&boxPath);

            Gdiplus::Pen border(devUpdateCheck?Gdiplus::Color(255,48,55,62)
                :(checked?Gdiplus::Color(255,43,145,49):Gdiplus::Color(255,59,69,78)),1.0f);
            g.DrawPath(&border,&boxPath);

            Gdiplus::Pen highlight(devUpdateCheck?Gdiplus::Color(55,70,77,84)
                :(checked?Gdiplus::Color(145,132,231,124):Gdiplus::Color(90,79,89,98)),0.8f);
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
            FillRound(dc,box,devUpdateCheck?RGB(24,29,34):(checked?RGB(63,177,67):RGB(25,31,36)),
                devUpdateCheck?RGB(48,55,62):(checked?RGB(43,145,49):RGB(59,69,78)),4);
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

void PaintFlatComboPopupBorder(HWND hwnd){
    HDC dc=GetWindowDC(hwnd);
    if(!dc) return;

    RECT r{};
    GetWindowRect(hwnd,&r);
    OffsetRect(&r,-r.left,-r.top);
    HBRUSH border=CreateSolidBrush(C_BORDER);
    FrameRect(dc,&r,border);
    DeleteObject(border);
    ReleaseDC(hwnd,dc);
}

LRESULT CALLBACK FlatComboPopupSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                            UINT_PTR subclassId,DWORD_PTR refData){
    switch(msg){
    case WM_NCPAINT:
    case WM_NCACTIVATE:{
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        PaintFlatComboPopupBorder(hwnd);
        return result;
    }
    case WM_MOUSEMOVE:{
        LRESULT result=DefSubclassProc(hwnd,msg,wp,lp);
        // The native popup changes its frame color while the pointer is over it.
        // Reapply the application border so hover and inactive states match.
        PaintFlatComboPopupBorder(hwnd);
        return result;
    }
    case WM_NCDESTROY:
        RemoveWindowSubclass(hwnd,FlatComboPopupSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

void StyleFlatCombo(HWND hwnd){
    if(!hwnd) return;
    SetWindowTheme(hwnd,L"",L"");
    SetWindowSubclass(hwnd,FlatComboSubclassProc,1,0);

    COMBOBOXINFO info{sizeof(info)};
    if(GetComboBoxInfo(hwnd,&info) && info.hwndList){
        SetWindowTheme(info.hwndList,L"",L"");
        SetWindowSubclass(info.hwndList,FlatComboPopupSubclassProc,1,0);
    }
    InvalidateRect(hwnd,nullptr,TRUE);
}

void RefreshList(){ HWND l=H(IDC_LIST); SendMessageW(l,LB_RESETCONTENT,0,0); SendMessageW(l,LB_ADDSTRING,0,(LPARAM)gSettings.desktop.name.c_str()); for(auto&p:gSettings.profiles)SendMessageW(l,LB_ADDSTRING,0,(LPARAM)p.name.c_str()); int maxSel=(int)gSettings.profiles.size(); gSelected=std::clamp(gSelected,0,maxSel); SendMessageW(l,LB_SETCURSEL,gSelected,0); }

void UpdateProfileMoveButtons(){
    EnableWindow(H(IDC_PROFILE_UP),gSelected>1);
    EnableWindow(H(IDC_PROFILE_DOWN),gSelected>0&&gSelected<(int)gSettings.profiles.size());
}

bool MoveSelectedProfile(int direction){
    if(gSelected<=0)return false;
    const int from=gSelected-1;
    const int to=from+direction;
    if(to<0||to>=(int)gSettings.profiles.size())return false;

    DiscardPreview();
    UnregisterConfiguredHotkeys();
    std::swap(gSettings.profiles[from],gSettings.profiles[to]);

    if(gOverrideMode==OverrideMode::Profile){
        if(gOverrideProfileIndex==(size_t)from)gOverrideProfileIndex=(size_t)to;
        else if(gOverrideProfileIndex==(size_t)to)gOverrideProfileIndex=(size_t)from;
    }

    gSelected=to+1;
    Save();
    RegisterConfiguredHotkeys();
    RefreshList();
    LoadSelected();
    UpdateProfileMoveButtons();
    return true;
}

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

POINT TooltipPositionForRect(const RECT& visibleRect,int tipW,int tipH){
    int x=static_cast<int>(visibleRect.left);
    int y=static_cast<int>(visibleRect.bottom)+TOOLTIP_GAP;
    HMONITOR monitor=MonitorFromRect(&visibleRect,MONITOR_DEFAULTTONEAREST);
    MONITORINFO info{sizeof(info)};
    if(GetMonitorInfoW(monitor,&info)){
        const int workLeft=static_cast<int>(info.rcWork.left);
        const int workTop=static_cast<int>(info.rcWork.top);
        const int workRight=static_cast<int>(info.rcWork.right);
        const int workBottom=static_cast<int>(info.rcWork.bottom);
        x=std::clamp(x,workLeft,std::max(workLeft,workRight-tipW));
        y=std::clamp(y,workTop,std::max(workTop,workBottom-tipH));
    }
    return POINT{static_cast<LONG>(x),static_cast<LONG>(y)};
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

    TOOLINFOW ti{sizeof(ti)};
    ti.hwnd=list;
    ti.uId=1;
    ti.lpszText=(LPWSTR)gProfileTooltipText.c_str();
    SendMessageW(gProfileTooltip,TTM_UPDATETIPTEXTW,0,(LPARAM)&ti);

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
        RECT visibleRect{};
        SendMessageW(list,LB_GETITEMRECT,item,(LPARAM)&visibleRect);
        visibleRect.left+=5;
        visibleRect.right-=5;
        visibleRect.top+=5;
        visibleRect.bottom-=5;
        MapWindowPoints(list,nullptr,(POINT*)&visibleRect,2);
        POINT position=TooltipPositionForRect(visibleRect,tipW,tipH);
        SendMessageW(gProfileTooltip,TTM_TRACKPOSITION,0,
            MAKELPARAM(position.x,position.y));
        SendMessageW(gProfileTooltip,TTM_TRACKACTIVATE,TRUE,(LPARAM)&ti);
        SetWindowPos(gProfileTooltip,HWND_TOPMOST,position.x,position.y,
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
        DrawTextW(dc,text,-1,&tr,DT_LEFT|DT_NOPREFIX);

        EndPaint(hwnd,&ps);
        return 0;
    }
    case WM_NCDESTROY:
        RemoveWindowSubclass(hwnd,ProfileTooltipSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

#if NVPS_DEV_BUILD
void HideUpdateCheckTooltip(){
    if(gUpdateCheckTooltip && gUpdateCheckTooltipVisible){
        ShowWindow(gUpdateCheckTooltip,SW_HIDE);
        gUpdateCheckTooltipVisible=false;
    }
    if(gWnd) KillTimer(gWnd,3);
}

void ShowUpdateCheckTooltip(){
    if(!gUpdateCheckTooltip) return;
    HWND checkbox=H(IDC_CHECKUPDATES);
    if(!checkbox) return;

    static const wchar_t* text=L"Update checks are disabled in development builds.";
    RECT checkboxRect{};
    GetWindowRect(checkbox,&checkboxRect);

    HDC dc=GetDC(gUpdateCheckTooltip);
    if(!dc) return;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    SIZE textSize{};
    GetTextExtentPoint32W(dc,text,(int)wcslen(text),&textSize);
    SelectObject(dc,old);
    ReleaseDC(gUpdateCheckTooltip,dc);

    const int tipW=textSize.cx+16;
    const int tipH=textSize.cy+10;
    RECT visibleRect=checkboxRect;
    InflateRect(&visibleRect,-3,-3);
    POINT position=TooltipPositionForRect(visibleRect,tipW,tipH);

    SetWindowPos(gUpdateCheckTooltip,HWND_TOPMOST,position.x,position.y,tipW,tipH,
        SWP_NOACTIVATE|SWP_SHOWWINDOW);
    RedrawWindow(gUpdateCheckTooltip,nullptr,nullptr,
        RDW_INVALIDATE|RDW_ERASE|RDW_FRAME|RDW_UPDATENOW);
    gUpdateCheckTooltipVisible=true;
    SetTimer(gWnd,3,3000,nullptr);
}
#endif


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

    HMONITOR mon=MonitorFromWindow(edit,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};
    const bool haveMonitorInfo=GetMonitorInfoW(mon,&mi)!=FALSE;

    const int workWidth=haveMonitorInfo?(int)(mi.rcWork.right-mi.rcWork.left):700;
    const int maxTipW=std::min(700,std::max(120,workWidth-16));
    const int maxTextW=maxTipW-16;

    HDC dc=GetDC(gExeTooltip);
    if(!dc) return;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    auto textWidth=[&](const std::wstring& text)->int{
        SIZE size{};
        GetTextExtentPoint32W(dc,text.c_str(),(int)text.size(),&size);
        return static_cast<int>(size.cx);
    };

    std::wstring wrapped;
    std::wstring line;
    size_t partStart=0;
    for(size_t i=0;i<=path.size();++i){
        if(i<path.size()&&path[i]!=L'\\'&&path[i]!=L'/') continue;
        const size_t partEnd=i<path.size()?i+1:i;
        std::wstring part=path.substr(partStart,partEnd-partStart);
        std::wstring candidate=line+part;
        if(!line.empty()&&textWidth(candidate)>maxTextW){
            if(!wrapped.empty()) wrapped+=L'\n';
            wrapped+=line;
            line=part;
        }else{
            line=candidate;
        }
        partStart=partEnd;
    }
    if(!line.empty()){
        if(!wrapped.empty()) wrapped+=L'\n';
        wrapped+=line;
    }

    int lineCount=1;
    int widestLine=0;
    size_t lineStart=0;
    for(size_t i=0;i<=wrapped.size();++i){
        if(i<wrapped.size()&&wrapped[i]!=L'\n') continue;
        widestLine=std::max(widestLine,textWidth(wrapped.substr(lineStart,i-lineStart)));
        if(i<wrapped.size()) ++lineCount;
        lineStart=i+1;
    }
    SIZE lineSize{};
    GetTextExtentPoint32W(dc,L"Ag",2,&lineSize);
    SelectObject(dc,old);
    ReleaseDC(gExeTooltip,dc);

    const int tipW=std::min(maxTipW,widestLine+16);
    const int tipH=lineSize.cy*lineCount+10;
    RECT visibleRect=er;
    RECT labelRect{};
    HWND label=H(IDC_LBL_EXE);
    if(label&&GetWindowRect(label,&labelRect)) visibleRect.left=labelRect.left;
    visibleRect.bottom+=7;
    POINT position=TooltipPositionForRect(visibleRect,tipW,tipH);

    SetWindowTextW(gExeTooltip,wrapped.c_str());
    SetWindowPos(gExeTooltip,HWND_TOPMOST,position.x,position.y,tipW,tipH,
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

    static const wchar_t* text=L"Reset to NVIDIA defaults.";
    HDC dc=GetDC(gResetTooltip);
    if(!dc) return;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    SIZE sz{};
    GetTextExtentPoint32W(dc,text,(int)wcslen(text),&sz);
    SelectObject(dc,old);
    ReleaseDC(gResetTooltip,dc);

    const int tipW=sz.cx+16;
    const int tipH=sz.cy+10;

    POINT position=TooltipPositionForRect(rr,tipW,tipH);

    SetWindowPos(gResetTooltip,HWND_TOPMOST,position.x,position.y,tipW,tipH,
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

void HideConfigurationTooltip(HWND tooltip,bool& visible){
    if(tooltip&&visible){
        ShowWindow(tooltip,SW_HIDE);
        visible=false;
    }
}

void UpdateConfigurationTooltip(HWND button,HWND tooltip,bool& visible,const wchar_t* text){
    if(!button||!tooltip||!text) return;
    RECT buttonRect{};
    GetWindowRect(button,&buttonRect);
    HDC dc=GetDC(tooltip);
    if(!dc) return;
    HFONT old=(HFONT)SelectObject(dc,gFont);
    SIZE sz{};
    GetTextExtentPoint32W(dc,text,(int)wcslen(text),&sz);
    SelectObject(dc,old);
    ReleaseDC(tooltip,dc);
    const int tipW=sz.cx+16;
    const int tipH=sz.cy+10;
    POINT position=TooltipPositionForRect(buttonRect,tipW,tipH);
    SetWindowPos(tooltip,HWND_TOPMOST,position.x,position.y,tipW,tipH,SWP_NOACTIVATE|SWP_SHOWWINDOW);
    RedrawWindow(tooltip,nullptr,nullptr,RDW_INVALIDATE|RDW_ERASE|RDW_FRAME|RDW_UPDATENOW);
    visible=true;
}

LRESULT CALLBACK ConfigurationButtonTooltipSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                                         UINT_PTR subclassId,DWORD_PTR refData){
    const bool isImport=refData==1;
    HWND tooltip=isImport?gImportTooltip:gExportTooltip;
    bool& visible=isImport?gImportTooltipVisible:gExportTooltipVisible;
    const wchar_t* text=isImport
        ?L"Import a complete NvProfileSwitcher configuration from a JSON file."
        :L"Export the complete NvProfileSwitcher configuration to a JSON file.";
    switch(msg){
    case WM_MOUSEMOVE:{
        UpdateConfigurationTooltip(hwnd,tooltip,visible,text);
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_MOUSELEAVE:
        HideConfigurationTooltip(tooltip,visible);
        break;
    case WM_NCDESTROY:
        HideConfigurationTooltip(tooltip,visible);
        RemoveWindowSubclass(hwnd,ConfigurationButtonTooltipSubclassProc,subclassId);
        break;
    }
    return DefSubclassProc(hwnd,msg,wp,lp);
}

LRESULT CALLBACK ProfileListSubclassProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp,
                                        UINT_PTR subclassId,DWORD_PTR refData){
    auto itemAtPoint=[&](LPARAM point)->int{
        LRESULT hit=SendMessageW(hwnd,LB_ITEMFROMPOINT,0,point);
        if(HIWORD(hit)) return -1;
        int item=LOWORD(hit);
        int count=(int)SendMessageW(hwnd,LB_GETCOUNT,0,0);
        return item>=0&&item<count?item:-1;
    };
    auto invalidateItem=[&](int item){
        if(item<0) return;
        RECT itemRect{};
        if(SendMessageW(hwnd,LB_GETITEMRECT,item,(LPARAM)&itemRect)!=LB_ERR)
            InvalidateRect(hwnd,&itemRect,FALSE);
    };

    switch(msg){
    case WM_MOUSEMOVE:{
        POINT pt{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
        int item=itemAtPoint(lp);
        if(item!=gProfileHoverItem){
            int old=gProfileHoverItem;
            gProfileHoverItem=item;
            invalidateItem(old);
            invalidateItem(item);
        }
        UpdateProfileTooltip(pt);
        TRACKMOUSEEVENT tme{sizeof(tme),TME_LEAVE,hwnd,0};
        TrackMouseEvent(&tme);
        break;
    }
    case WM_LBUTTONDOWN:{
        int old=gProfilePressedItem;
        gProfilePressedItem=itemAtPoint(lp);
        invalidateItem(old);
        invalidateItem(gProfilePressedItem);
        break;
    }
    case WM_LBUTTONUP:
    case WM_CAPTURECHANGED:{
        int old=gProfilePressedItem;
        gProfilePressedItem=-1;
        invalidateItem(old);
        break;
    }
    case WM_MOUSELEAVE:{
        int old=gProfileHoverItem;
        gProfileHoverItem=-1;
        invalidateItem(old);
        HideProfileTooltip();
        break;
    }
    case WM_NCDESTROY:
        gProfileHoverItem=-1;
        gProfilePressedItem=-1;
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
        v.displayName=gDisplays[ds].displayName;
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
    if(gOverrideMode==OverrideMode::Profile&&gOverrideProfileIndex<gSettings.profiles.size()){
        const auto& profile=gSettings.profiles[gOverrideProfileIndex];
        ApplyApplicationProfile(profile);
        gActive=profile.name;
        return;
    }
    std::wstring fgName=ForegroundProcessName();
    const auto target=nvps::SelectSwitchTarget(SwitchingProfiles(),fgName,
        gOverrideMode==OverrideMode::Windows);
    if(target.profileIndex){
        ApplyApplicationProfile(gSettings.profiles[*target.profileIndex]);
        gActive=target.activeName;
        return;
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
        v.displayName=gDisplays[ds].displayName;
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
    RECT r{0,0,MAIN_BASE_CLIENT_WIDTH,MAIN_BASE_CLIENT_HEIGHT};

    const int margin=18;
    const int leftW=360;
    const int gap=14;
    const int settingsW=330;
    const int centerPanelX=margin+leftW+gap;
    const int centerPanelW=r.right-(margin*2)-leftW-settingsW-(gap*2);
    const int rightX=centerPanelX+22;
    const int rightW=centerPanelW-44;
    const int footerH=56;
    const int panelBottom=r.bottom-footerH-14;

    int showApplication=desktop?SW_HIDE:SW_SHOW;
    for(int id:{IDC_LBL_NAME,IDC_NAME,IDC_LBL_EXE,IDC_EXE,IDC_BROWSE,IDC_RUNNING_APPS,IDC_ENABLED,IDC_LBL_ENABLED,
                IDC_PROFILE_HOTKEY_LABEL,IDC_PROFILE_HOTKEY,IDC_PROFILE_HOTKEY_CLEAR})
        ShowWindow(H(id),showApplication);
    ShowWindow(H(IDC_REMOVE),desktop?SW_HIDE:SW_SHOW);

    // Both views use the same vertical rhythm from Display down. Application
    // profiles only start the block lower to make room for their extra fields.
    const int yDisplay=desktop?154:358;
    const int yBri=yDisplay+86;
    const int yCon=yBri+64;
    const int yGam=yCon+64;
    const int yVib=yGam+64;
    const int yHue=yVib+64;
    const int ySave=panelBottom-19-38;

    const int profileHotkeyX=rightX+rightW-288;
    MoveUi(H(IDC_PROFILE_HOTKEY_LABEL),profileHotkeyX,272,220,22,TRUE);
    MoveUi(H(IDC_PROFILE_HOTKEY),profileHotkeyX+2,306,206,22,TRUE);
    MoveUi(H(IDC_PROFILE_HOTKEY_CLEAR),profileHotkeyX+220,300,68,34,TRUE);

    MoveUi(H(IDC_LBL_DISPLAY),rightX+31,yDisplay,150,22,TRUE);
    MoveUi(H(IDC_DISPLAY),rightX,yDisplay+24,rightW,34,TRUE);

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
        MoveUi(H(sp.lbl),labelX,sp.y-2,labelW,22,TRUE);
        MoveUi(H(sp.track),trackX,sp.y-4,trackW,28,TRUE);
        MoveUi(H(sp.val),valueX,sp.y-5,valueW,28,TRUE);
    }

    MoveUi(H(IDC_DEFAULTS),rightX,ySave,132,38,TRUE);
    MoveUi(H(IDC_SAVE),rightX+rightW-160,ySave,160,38,TRUE);

    const int appX=centerPanelX+centerPanelW+gap+22;
    MoveUi(H(IDC_HOTKEY_SHOW_LABEL),appX,150,286,22,TRUE);
    MoveUi(H(IDC_HOTKEY_SHOW),appX+2,182,206,22,TRUE);
    MoveUi(H(IDC_HOTKEY_SHOW_CLEAR),appX+218,176,68,34,TRUE);
    MoveUi(H(IDC_HOTKEY_OVERRIDE_LABEL),appX,234,286,22,TRUE);
    MoveUi(H(IDC_HOTKEY_OVERRIDE),appX+2,266,206,22,TRUE);
    MoveUi(H(IDC_HOTKEY_OVERRIDE_CLEAR),appX+218,260,68,34,TRUE);
    MoveUi(H(IDC_HOTKEY_RESUME_LABEL),appX,318,286,22,TRUE);
    MoveUi(H(IDC_HOTKEY_RESUME),appX+2,350,206,22,TRUE);
    MoveUi(H(IDC_HOTKEY_RESUME_CLEAR),appX+218,344,68,34,TRUE);
    MoveUi(H(IDC_APP_SETTINGS_TITLE),appX,579,250,24,TRUE);
    MoveUi(H(IDC_STARTWIN),appX,617,22,22,TRUE);
    MoveUi(GetWindow(H(IDC_STARTWIN),GW_HWNDNEXT),appX+27,617,220,22,TRUE);
    MoveUi(H(IDC_STARTMIN),appX,645,22,22,TRUE);
    MoveUi(GetWindow(H(IDC_STARTMIN),GW_HWNDNEXT),appX+27,645,220,22,TRUE);
    MoveUi(H(IDC_MINTRAY),appX,673,22,22,TRUE);
    MoveUi(GetWindow(H(IDC_MINTRAY),GW_HWNDNEXT),appX+27,673,220,22,TRUE);
    MoveUi(H(IDC_CHECKUPDATES),appX,701,22,22,TRUE);
    MoveUi(GetWindow(H(IDC_CHECKUPDATES),GW_HWNDNEXT),appX+27,701,220,22,TRUE);
    MoveUi(H(IDC_MANAGE_DISPLAYS),appX,ySave,138,38,TRUE);
    MoveUi(H(IDC_EXPORT_PROFILES),appX+148,ySave,138,38,TRUE);

    InvalidateRect(gWnd,nullptr,TRUE);
}
void LoadSelected(){
    int i=(int)SendMessageW(H(IDC_LIST),LB_GETCURSEL,0,0);
    if(i<0||i>(int)gSettings.profiles.size())return;
    gSelected=i;
    UpdateProfileMoveButtons();
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
            p=EnsureDesktopProfile(gDisplays[primary].displayName,gDisplays[primary].monitorId);
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
        SetHotkeyControl(IDC_PROFILE_HOTKEY,0);
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
    SetHotkeyControl(IDC_PROFILE_HOTKEY,p->hotkey);

    if(ds>=0&&ds<(int)gDisplays.size())
        LoadValuesToSliders(*EnsureApplicationValuesForDisplay(*p,gDisplays[ds].displayName,gDisplays[ds].monitorId));
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
        if(ds>=0&&ds<(int)gDisplays.size()){v.displayName=gDisplays[ds].displayName;v.monitorId=gDisplays[ds].monitorId;}
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
    const size_t profileIndex=(size_t)(gSelected-1);
    const bool wasEnabled=p->enabled;
    p->name=GetTxt(IDC_NAME);
    p->exePath=GetTxt(IDC_EXE);
    p->enabled=SendMessageW(H(IDC_ENABLED),BM_GETCHECK,0,0)==BST_CHECKED;

    if(p->enabled!=wasEnabled){
        UnregisterConfiguredHotkeys();
        if(!p->enabled&&gOverrideMode==OverrideMode::Profile&&gOverrideProfileIndex==profileIndex)
            gOverrideMode=OverrideMode::Automatic;
        RegisterConfiguredHotkeys();
    }

    if(ds>=0&&ds<(int)gDisplays.size()){
        auto* v=EnsureApplicationValuesForDisplay(*p,gDisplays[ds].displayName,gDisplays[ds].monitorId);
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
    g.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);
    Gdiplus::Color color(255,GetRValue(c),GetGValue(c),GetBValue(c));
    Gdiplus::Pen pen(color,1.45f);
    pen.SetStartCap(Gdiplus::LineCapRound);
    pen.SetEndCap(Gdiplus::LineCapRound);
    pen.SetLineJoin(Gdiplus::LineJoinRound);

    // Lid and raised handle.
    g.DrawLine(&pen,x+1.5f,y+5.0f,x+15.5f,y+5.0f);
    Gdiplus::GraphicsPath handle;
    handle.StartFigure();
    handle.AddLine(x+5.8f,y+4.8f,x+6.5f,y+2.7f);
    handle.AddLine(x+6.5f,y+2.7f,x+10.5f,y+2.7f);
    handle.AddLine(x+10.5f,y+2.7f,x+11.2f,y+4.8f);
    g.DrawPath(&pen,&handle);

    // Wider body with subtly rounded lower corners.
    Gdiplus::GraphicsPath body;
    body.StartFigure();
    body.AddLine(x+3.0f,y+7.0f,x+14.0f,y+7.0f);
    body.AddLine(x+14.0f,y+7.0f,x+13.2f,y+15.8f);
    body.AddBezier(x+13.2f,y+15.8f,x+13.1f,y+16.9f,
                   x+12.4f,y+17.4f,x+11.3f,y+17.4f);
    body.AddLine(x+11.3f,y+17.4f,x+5.7f,y+17.4f);
    body.AddBezier(x+5.7f,y+17.4f,x+4.6f,y+17.4f,
                   x+3.9f,y+16.9f,x+3.8f,y+15.8f);
    body.CloseFigure();
    Gdiplus::SolidBrush bodyFill(Gdiplus::Color(24,GetRValue(c),GetGValue(c),GetBValue(c)));
    g.FillPath(&bodyFill,&body);
    g.DrawPath(&pen,&body);

    // Two clean inner slots, matching the mockup proportions.
    Gdiplus::Pen slotPen(Gdiplus::Color(220,GetRValue(c),GetGValue(c),GetBValue(c)),1.1f);
    slotPen.SetStartCap(Gdiplus::LineCapRound);
    slotPen.SetEndCap(Gdiplus::LineCapRound);
    g.DrawLine(&slotPen,x+7.0f,y+9.3f,x+7.3f,y+14.8f);
    g.DrawLine(&slotPen,x+10.7f,y+9.3f,x+10.4f,y+14.8f);
}

void DrawFolderIcon(HDC dc,int x,int y,COLORREF c){
    Gdiplus::Graphics g(dc);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    g.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHalf);

    Gdiplus::Color color(255,GetRValue(c),GetGValue(c),GetBValue(c));
    Gdiplus::Color rearColor(190,GetRValue(c),GetGValue(c),GetBValue(c));
    Gdiplus::Pen rearPen(rearColor,1.15f);
    rearPen.SetStartCap(Gdiplus::LineCapRound);
    rearPen.SetEndCap(Gdiplus::LineCapRound);
    rearPen.SetLineJoin(Gdiplus::LineJoinRound);

    // Rear folder body and tab.
    Gdiplus::GraphicsPath rear;
    rear.StartFigure();
    rear.AddLine((Gdiplus::REAL)x+2.0f,(Gdiplus::REAL)y+15.5f,
                 (Gdiplus::REAL)x+2.0f,(Gdiplus::REAL)y+4.5f);
    rear.AddLine((Gdiplus::REAL)x+2.0f,(Gdiplus::REAL)y+4.5f,
                 (Gdiplus::REAL)x+7.0f,(Gdiplus::REAL)y+4.5f);
    rear.AddLine((Gdiplus::REAL)x+7.0f,(Gdiplus::REAL)y+4.5f,
                 (Gdiplus::REAL)x+9.2f,(Gdiplus::REAL)y+6.7f);
    rear.AddLine((Gdiplus::REAL)x+9.2f,(Gdiplus::REAL)y+6.7f,
                 (Gdiplus::REAL)x+17.5f,(Gdiplus::REAL)y+6.7f);
    rear.AddLine((Gdiplus::REAL)x+17.5f,(Gdiplus::REAL)y+6.7f,
                 (Gdiplus::REAL)x+18.2f,(Gdiplus::REAL)y+9.0f);
    g.DrawPath(&rearPen,&rear);

    // Open front flap, which gives the mockup icon its depth.
    Gdiplus::GraphicsPath front;
    front.StartFigure();
    front.AddLine((Gdiplus::REAL)x+1.8f,(Gdiplus::REAL)y+9.0f,
                  (Gdiplus::REAL)x+19.0f,(Gdiplus::REAL)y+9.0f);
    front.AddLine((Gdiplus::REAL)x+19.0f,(Gdiplus::REAL)y+9.0f,
                  (Gdiplus::REAL)x+16.2f,(Gdiplus::REAL)y+16.2f);
    front.AddLine((Gdiplus::REAL)x+16.2f,(Gdiplus::REAL)y+16.2f,
                  (Gdiplus::REAL)x+1.0f,(Gdiplus::REAL)y+16.2f);
    front.CloseFigure();

    Gdiplus::SolidBrush frontFill(Gdiplus::Color(28,GetRValue(c),GetGValue(c),GetBValue(c)));
    Gdiplus::Pen frontPen(color,1.35f);
    frontPen.SetStartCap(Gdiplus::LineCapRound);
    frontPen.SetEndCap(Gdiplus::LineCapRound);
    frontPen.SetLineJoin(Gdiplus::LineJoinRound);
    g.FillPath(&frontFill,&front);
    g.DrawPath(&frontPen,&front);
}

void DrawRunningAppsIcon(HDC dc,int x,int y,COLORREF c){
    HPEN pen=CreatePen(PS_SOLID,1,c);
    HGDIOBJ oldPen=SelectObject(dc,pen);
    HGDIOBJ oldBrush=SelectObject(dc,GetStockObject(NULL_BRUSH));
    RoundRect(dc,x+1,y+2,x+16,y+14,3,3);
    MoveToEx(dc,x+5,y+17,nullptr);LineTo(dc,x+18,y+17);
    RoundRect(dc,x+5,y+6,x+20,y+18,3,3);
    SelectObject(dc,oldBrush);
    SelectObject(dc,oldPen);
    DeleteObject(pen);
}


void DrawProfileHeaderButton(const DRAWITEMSTRUCT* d){
    const int id=(int)d->CtlID;
    const bool down=(d->itemState&ODS_SELECTED)!=0;
    const bool disabled=(d->itemState&ODS_DISABLED)!=0;
    const bool hover=d->hwndItem==gMainButtonHover;

    RECT r=d->rcItem;
    const bool moveButton=id==IDC_PROFILE_UP||id==IDC_PROFILE_DOWN;
    const COLORREF textColor=disabled?C_MUTED:RGB(230,233,236);
    const COLORREF iconColor=disabled?(moveButton?RGB(58,66,72):C_MUTED)
        :(id==IDC_REMOVE?C_DANGER:(moveButton?RGB(238,241,244):RGB(218,222,226)));

    HBRUSH headerBackground=CreateSolidBrush(C_PANEL2);
    FillRect(d->hDC,&r,headerBackground);
    DeleteObject(headerBackground);
    DrawMainButtonSurface(d->hDC,r,false,hover,down,disabled);

    if(moveButton){
        const int pressOffset=down?1:0;
        const int cx=(r.left+r.right)/2+pressOffset;
        const int cy=(r.top+r.bottom)/2+pressOffset;
        HPEN pen=CreatePen(PS_SOLID,1,iconColor);
        HGDIOBJ oldPen=SelectObject(d->hDC,pen);
        if(id==IDC_PROFILE_UP){
            MoveToEx(d->hDC,cx-4,cy+2,nullptr);
            LineTo(d->hDC,cx,cy-2);
            LineTo(d->hDC,cx+4,cy+2);
        }else{
            MoveToEx(d->hDC,cx-4,cy-2,nullptr);
            LineTo(d->hDC,cx,cy+2);
            LineTo(d->hDC,cx+4,cy-2);
        }
        SelectObject(d->hDC,oldPen);
        DeleteObject(pen);
        return;
    }

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
        DrawRemoveButtonIcon(d->hDC,contentLeft,cy-10,iconColor);
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
    if(id==IDC_SAVE||id==IDC_RUNNING_SELECT){
        textColor=disabled?C_MUTED:C_TEXT;
        icon=disabled?C_MUTED:C_TEXT;
    }else if(id==IDC_DEFAULTS){
        textColor=disabled?C_MUTED:C_TEXT;
        icon=C_MUTED;
    }else if(id==IDC_BROWSE||id==IDC_RUNNING_APPS){
        icon=C_TEXT;
    }

    RECT r=d->rcItem;
    DrawMainButtonSurface(d->hDC,r,id==IDC_SAVE||id==IDC_RUNNING_SELECT,hover,down,disabled);

    wchar_t caption[128]{};
    GetWindowTextW(d->hwndItem,caption,128);
    SIZE sz{};
    HFONT buttonFont=(id==IDC_SAVE||id==IDC_DEFAULTS||id==IDC_RUNNING_SELECT)?gFontBold:gFont;
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
        if(id==IDC_BROWSE||id==IDC_RUNNING_APPS){ iconW=20; gap=7; }

        int total=iconW+gap+sz.cx;
        int contentX=r.left+((r.right-r.left)-total)/2+pressOffset;

        if(id==IDC_BROWSE) DrawFolderIcon(d->hDC,contentX,cy-10,icon);
        else if(id==IDC_RUNNING_APPS) DrawRunningAppsIcon(d->hDC,contentX,cy-10,icon);

        int textY=cy-sz.cy/2;
        if(id==IDC_BROWSE||id==IDC_RUNNING_APPS) textY-=1;
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

    const int targetH=Ui(58);
    const UINT iw=gHeaderImage->GetWidth();
    const UINT ih=gHeaderImage->GetHeight();
    if(!iw||!ih) return;
    const int targetW=(int)llround((double)iw*targetH/(double)ih);
    graphics.DrawImage(gHeaderImage,Gdiplus::Rect(Ui(27),Ui(9),targetW,targetH));
}


void DrawSliderIconPhysical(HDC dc,Gdiplus::Image* image,int x,int y,int iconBox){
    if(!image) return;
    const UINT sourceW=image->GetWidth();
    const UINT sourceH=image->GetHeight();
    if(!sourceW||!sourceH||iconBox<=0) return;

    const double scale=std::min((double)iconBox/sourceW,(double)iconBox/sourceH);
    const int targetW=std::max(1,(int)std::lround(sourceW*scale));
    const int targetH=std::max(1,(int)std::lround(sourceH*scale));
    const int targetX=x+(iconBox-targetW)/2;
    const int targetY=y+(iconBox-targetH)/2;

    int saved=SaveDC(dc);
    SetMapMode(dc,MM_TEXT);
    SetWindowOrgEx(dc,0,0,nullptr);
    SetViewportOrgEx(dc,0,0,nullptr);
    Gdiplus::Graphics graphics(dc);
    graphics.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
    graphics.SetPixelOffsetMode(Gdiplus::PixelOffsetModeHighQuality);
    graphics.DrawImage(image,Gdiplus::Rect(targetX,targetY,targetW,targetH));
    RestoreDC(dc,saved);
}


void DrawLabel(HDC dc,const wchar_t*t,int x,int y,COLORREF c,HFONT f=nullptr){ SetBkMode(dc,TRANSPARENT);SetTextColor(dc,c);SelectObject(dc,f?f:gFont);TextOutW(dc,x,y,t,(int)wcslen(t)); }
void Fill(HDC dc,int x,int y,int w,int h,COLORREF c){HBRUSH b=CreateSolidBrush(c);RECT r{x,y,x+w,y+h};FillRect(dc,&r,b);DeleteObject(b);} 
void DrawSortIndicator(HDC dc,int x,int centerY,int unit,COLORREF color,bool active,bool ascending){
    // Three centered horizontal bars, matching the compact sort/filter-style glyph
    // used by GitHub. Keep every bar on the same horizontal center line.
    const int lineH=std::max(2,unit*2);
    const int step=std::max(5,unit*5);
    const int w1=unit*14,w2=unit*10,w3=unit*4;
    const int topY=centerY-step-lineH/2;
    const int midY=centerY-lineH/2;
    const int bottomY=centerY+step-lineH/2;

    if(active&&ascending){
        Fill(dc,x+(w1-w3)/2,topY,w3,lineH,color);
        Fill(dc,x+(w1-w2)/2,midY,w2,lineH,color);
        Fill(dc,x,bottomY,w1,lineH,color);
    }else{
        Fill(dc,x,topY,w1,lineH,color);
        Fill(dc,x+(w1-w2)/2,midY,w2,lineH,color);
        Fill(dc,x+(w1-w3)/2,bottomY,w3,lineH,color);
    }
}



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

    HPEN framePen=CreatePen(PS_SOLID,std::max(1,Ui(1)),frame);
    HBRUSH frameBrush=CreateSolidBrush(frame);
    HBRUSH screenBrush=CreateSolidBrush(screen);

    HGDIOBJ oldPen=SelectObject(dc,framePen);
    HGDIOBJ oldBrush=SelectObject(dc,frameBrush);

    // Prototype proportions: smaller/thinner bezel and a larger white screen.
    // Draw the bezel as a solid 1 px shell so GDI Rectangle's inclusive edge
    // doesn't visually turn it into a ~2 px border.
    SelectObject(dc,frameBrush);
    PatBlt(dc,x,y,Ui(22),std::max(1,Ui(1)),PATCOPY);          // top
    PatBlt(dc,x,y+Ui(1),std::max(1,Ui(1)),Ui(14),PATCOPY);        // left
    PatBlt(dc,x+Ui(21),y+Ui(1),std::max(1,Ui(1)),Ui(14),PATCOPY);     // right
    PatBlt(dc,x,y+Ui(15),Ui(22),std::max(1,Ui(1)),PATCOPY);       // bottom

    // White screen.
    SelectObject(dc,screenBrush);
    PatBlt(dc,x+Ui(1),y+Ui(1),Ui(20),Ui(14),PATCOPY);

    // Very thin stand/base, matching the reference.
    SelectObject(dc,frameBrush);
    PatBlt(dc,x+Ui(10),y+Ui(16),Ui(2),Ui(4),PATCOPY);
    PatBlt(dc,x+Ui(6),y+Ui(20),Ui(10),std::max(1,Ui(1)),PATCOPY);

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
    g.DrawImage(gNvidiaDriverIcon,Gdiplus::Rect(x,y,Ui(16),Ui(16)));
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
    PAINTSTRUCT ps{}; HDC dc=BeginPaint(w,&ps);
    RECT physical{}; GetClientRect(w,&physical);
    FillRect(dc,&physical,gBackBrush);
    RECT rc{0,0,MAIN_BASE_CLIENT_WIDTH,MAIN_BASE_CLIENT_HEIGHT};
    auto SR=[](const RECT& r){ return RECT{Ui(r.left),Ui(r.top),Ui(r.right),Ui(r.bottom)}; };
    auto FR=[&](const RECT& r,COLORREF fill,COLORREF border,int radius){ RECT q=SR(r); FillRound(dc,q,fill,border,std::max(1,Ui(radius))); };
    auto FL=[&](int x,int y,int ww,int hh,COLORREF c){ Fill(dc,Ui(x),Ui(y),std::max(1,Ui(ww)),std::max(1,Ui(hh)),c); };
    auto DL=[&](const wchar_t* t,int x,int y,COLORREF c,HFONT f){ DrawLabel(dc,t,Ui(x),Ui(y),c,f); };

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
    FR(left,C_PANEL,C_BORDER,10);
    FR(center,C_PANEL,C_BORDER,10);
    FR(settings,C_PANEL,C_BORDER,10);

    DrawHeaderImage(dc);

    std::wstring headerVersion;
#if NVPS_DEV_BUILD
    headerVersion=APP_VERSION;
#else
    headerVersion=L"v";
    headerVersion+=APP_VERSION;
#endif
    RECT headerVersionRect=SR(RECT{rc.right-240,48,rc.right-27,68});
    SetBkMode(dc,TRANSPARENT);
    SetTextColor(dc,C_MUTED);
    SelectObject(dc,gFontSmall);
    DrawTextW(dc,headerVersion.c_str(),-1,&headerVersionRect,
        DT_RIGHT|DT_BOTTOM|DT_SINGLELINE|DT_NOPREFIX);

    FL(0,78,rc.right,1,C_BORDER);

    // Panel header fill is clipped to the rounded panel and stops exactly
    // at the separator. No rounded header overlay and no repaint workaround.
    const int separatorY=136;
    auto PaintPanelHeader=[&](const RECT& panel){
        int saved=SaveDC(dc);
        RECT q=SR(panel);
        HRGN clip=CreateRoundRectRgn(q.left+Ui(1),q.top+Ui(1),q.right,q.bottom,Ui(10),Ui(10));
        SelectClipRgn(dc,clip);
        FL(panel.left+1,panel.top+1,
             panel.right-panel.left-2,separatorY-panel.top-1,C_PANEL2);
        SelectClipRgn(dc,nullptr);
        DeleteObject(clip);
        RestoreDC(dc,saved);
    };
    PaintPanelHeader(left);
    PaintPanelHeader(center);
    PaintPanelHeader(settings);

    DL(L"Profiles",left.left+14,99,C_TEXT,gFontPanelTitle);
    DL(L"Profile Settings",center.left+14,99,C_TEXT,gFontPanelTitle);
    DL(L"Hotkeys",settings.left+14,99,C_TEXT,gFontPanelTitle);

    FL(left.left+1,separatorY,leftW-2,1,C_BORDER);
    FL(center.left+1,separatorY,centerW-2,1,C_BORDER);
    FL(settings.left+1,separatorY,settingsW-2,1,C_BORDER);
    FL(settings.left+22,565,settingsW-44,1,C_BORDER);

    // Match the application text fields: the app paints the complete rounded
    // frame and the native hotkey control sits borderless inside it.
    RECT showHotkeyFrame{settingsX+22,176,settingsX+232,210};
    RECT overrideHotkeyFrame{settingsX+22,260,settingsX+232,294};
    RECT resumeHotkeyFrame{settingsX+22,344,settingsX+232,378};
    FR(showHotkeyFrame,C_FIELD,C_BORDER,8);
    FR(overrideHotkeyFrame,C_FIELD,C_BORDER,8);
    FR(resumeHotkeyFrame,C_FIELD,C_BORDER,8);

    const bool desktop=IsDesktopSelected();
    const int displayY=desktop?154:358;

    DrawDisplayPrototypeIcon(dc,Ui(centerX+22),Ui(displayY));

    // Rounded frames for application text fields. The EDIT controls themselves
    // are borderless and inset, avoiding clipped corners or double borders.
    if(!desktop){
        const int browseW=135;
        const int fieldGap=10;
        RECT nameFrame{rightX+118,146,rightX+rightW,182};
        FR(nameFrame,C_FIELD,C_BORDER,8);

        RECT exeFrame{rightX,222,rightX+rightW-browseW-fieldGap,258};
        FR(exeFrame,C_FIELD,C_BORDER,8);

        const int profileHotkeyX=rightX+rightW-288;
        RECT profileHotkeyFrame{profileHotkeyX,300,profileHotkeyX+210,334};
        FR(profileHotkeyFrame,C_FIELD,C_BORDER,8);
    }

    const int iconX=centerX+22;
    const int iconBri=displayY+86;
    const int iconCon=iconBri+64;
    const int iconGam=iconCon+64;
    const int iconVib=iconGam+64;
    const int iconHue=iconVib+64;
    DrawSliderIconPhysical(dc,gSliderBrightness,Ui(iconX),Ui(iconBri-2),Ui(22));
    DrawSliderIconPhysical(dc,gSliderContrast,Ui(iconX),Ui(iconCon-2),Ui(22));
    DrawSliderIconPhysical(dc,gSliderGamma,Ui(iconX),Ui(iconGam-2),Ui(22));
    DrawSliderIconPhysical(dc,gSliderVibrance,Ui(iconX),Ui(iconVib-2),Ui(22));
    DrawSliderIconPhysical(dc,gSliderHue,Ui(iconX),Ui(iconHue-2),Ui(22));

    const int footerTop=rc.bottom-footerH;
    FL(0,footerTop,rc.right,1,C_BORDER);
    const int footerY=footerTop+19;

    const int dotX=Ui(38), dotY=Ui(footerY+5);
    HBRUSH statusBrush=CreateSolidBrush(gStatusOk?C_ACCENT:C_DANGER);
    HGDIOBJ oldBrush=SelectObject(dc,statusBrush);
    Ellipse(dc,dotX,dotY,dotX+Ui(8),dotY+Ui(8));
    SelectObject(dc,oldBrush);
    DeleteObject(statusBrush);

    DrawLabel(dc,L"NVIDIA API",Ui(54),Ui(footerY),C_MUTED,gFont);
    SIZE apiLabel{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,L"NVIDIA API",10,&apiLabel);
    const wchar_t* apiState=gStatusOk?L"Available":L"Unavailable";
    COLORREF apiColor=gStatusOk?C_ACCENT:C_DANGER;
    int apiStateX=Ui(54)+apiLabel.cx+Ui(8);
    DrawLabel(dc,apiState,apiStateX,Ui(footerY),apiColor,gFont);

    SIZE stateSize{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,apiState,(int)wcslen(apiState),&stateSize);
    int dividerX=apiStateX+stateSize.cx+Ui(18);
    Fill(dc,dividerX,Ui(footerY),std::max(1,Ui(1)),Ui(17),C_BORDER);

    int driverIconX=dividerX+Ui(16);
    DrawDriverIcon(dc,driverIconX,Ui(footerY+1),C_MUTED);
    int driverTextX=driverIconX+Ui(23);
    DrawLabel(dc,L"Driver",driverTextX,Ui(footerY),C_MUTED,gFont);
    SIZE driverLabel{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,L"Driver",6,&driverLabel);
    int driverVersionX=driverTextX+driverLabel.cx+Ui(8);
    DrawLabel(dc,gDriverVersion.c_str(),driverVersionX,Ui(footerY),C_TEXT,gFont);

    SIZE driverVersionSize{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,gDriverVersion.c_str(),(int)gDriverVersion.size(),&driverVersionSize);
    int activeDividerX=driverVersionX+driverVersionSize.cx+Ui(18);
    Fill(dc,activeDividerX,Ui(footerY),std::max(1,Ui(1)),Ui(17),C_BORDER);
    constexpr wchar_t activeProfileLabel[]=L"Active profile:";
    DrawLabel(dc,activeProfileLabel,activeDividerX+Ui(16),Ui(footerY),C_MUTED,gFont);
    SIZE activeLabel{}; SelectObject(dc,gFont);
    GetTextExtentPoint32W(dc,activeProfileLabel,
        (int)(sizeof(activeProfileLabel)/sizeof(activeProfileLabel[0])-1),&activeLabel);
    std::wstring displayedActive=gActive;
    if(gOverrideMode!=OverrideMode::Automatic)displayedActive+=L" (override)";
    DrawLabel(dc,displayedActive.c_str(),activeDividerX+Ui(16)+activeLabel.cx+Ui(8),Ui(footerY),C_TEXT,gFontBold);


    EndPaint(w,&ps);
}
void BuildControls(){
    RECT r{0,0,MAIN_BASE_CLIENT_WIDTH,MAIN_BASE_CLIENT_HEIGHT};
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
    SendMessageW(list,LB_SETITEMHEIGHT,0,Ui(70));

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

    HWND addProfile=Add(L"BUTTON",L"Add profile",BS_OWNERDRAW,120,95,112,32,IDC_ADD);
    HWND removeProfile=Add(L"BUTTON",L"Remove",BS_OWNERDRAW,236,95,92,32,IDC_REMOVE);
    HWND moveUp=Add(L"BUTTON",L"",BS_OWNERDRAW,332,95,32,15,IDC_PROFILE_UP);
    HWND moveDown=Add(L"BUTTON",L"",BS_OWNERDRAW,332,112,32,15,IDC_PROFILE_DOWN);
    StyleMainButton(addProfile);
    StyleMainButton(removeProfile);
    StyleMainButton(moveUp);
    StyleMainButton(moveDown);
    UpdateProfileMoveButtons();

    Add(L"STATIC",L"Profile name",0,rightX,152,110,22,IDC_LBL_NAME);
    HWND eName=Add(L"EDIT",L"",ES_AUTOHSCROLL,rightX+120,153,rightW-122,22,IDC_NAME);
    SetWindowTheme(eName,L"DarkMode_Explorer",nullptr);
    SendMessageW(eName,EM_SETMARGINS,EC_LEFTMARGIN|EC_RIGHTMARGIN,MAKELPARAM(8,8));

    Add(L"STATIC",L"Executable",0,rightX,194,120,22,IDC_LBL_EXE);
    const int browseW=135;
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

    HWND runningApps=Add(L"BUTTON",L"Running apps...",BS_OWNERDRAW,
        rightX+rightW-browseW,209,browseW,28,IDC_RUNNING_APPS);
    StyleMainButton(runningApps);
    HWND browse=Add(L"BUTTON",L"Browse...",BS_OWNERDRAW,
        rightX+rightW-browseW,243,browseW,28,IDC_BROWSE);
    StyleMainButton(browse);

    HWND enabled=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX,272,22,22,IDC_ENABLED);
    StyleFlatCheckbox(enabled);
    Add(L"STATIC",L"Enable this profile",SS_CENTERIMAGE,rightX+27,272,205,22,IDC_LBL_ENABLED);

    const int profileHotkeyX=rightX+rightW-288;
    Add(L"STATIC",L"Profile hotkey",0,profileHotkeyX,272,220,22,IDC_PROFILE_HOTKEY_LABEL);
    HWND profileHotkey=Add(HOTKEY_CLASSW,L"",WS_TABSTOP,profileHotkeyX+2,306,206,22,IDC_PROFILE_HOTKEY);
    SetWindowSubclass(profileHotkey,HotkeyFieldSubclassProc,1,0);
    RemoveNativeHotkeyFrame(profileHotkey);
    SendMessageW(profileHotkey,HKM_SETRULES,0,0);
    HWND clearProfile=Add(L"BUTTON",L"Clear",BS_OWNERDRAW,profileHotkeyX+220,300,68,34,IDC_PROFILE_HOTKEY_CLEAR);
    StyleMainButton(clearProfile);

    Add(L"STATIC",L"Display",0,rightX+31,358,150,22,IDC_LBL_DISPLAY);
    HWND display=Add(L"COMBOBOX",L"",CBS_DROPDOWNLIST|CBS_OWNERDRAWFIXED|CBS_HASSTRINGS|WS_VSCROLL,
        rightX,382,rightW,240,IDC_DISPLAY);
    SendMessageW(display,CB_SETITEMHEIGHT,0,Ui(28));
    SendMessageW(display,CB_SETITEMHEIGHT,(WPARAM)-1,Ui(26));
    StyleFlatCombo(display);

    const int labelX=rightX+30;
    const int labelW=154;
    const int trackX=rightX+190;
    const int valueW=76;
    const int valueX=rightX+rightW-valueW;
    const int trackW=valueX-trackX-16;

    auto slider=[&](const wchar_t*t,int lid,int id,int vid,int y,int mn,int mx){
        Add(L"STATIC",t,SS_CENTERIMAGE,labelX,y-2,labelW,22,lid);
        HWND tr=Add(TRACKBAR_CLASSW,L"",TBS_HORZ|TBS_NOTICKS,trackX,y-4,trackW,28,id);
        SendMessageW(tr,TBM_SETRANGE,TRUE,MAKELONG(mn,mx));
        Add(L"STATIC",L"",SS_OWNERDRAW,valueX,y-5,valueW,28,vid);
    };

    slider(L"Brightness",IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,444,80,120);
    slider(L"Contrast",IDC_LBL_CON,IDC_CON,IDC_VALCON,508,80,120);
    slider(L"Gamma",IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,572,30,280);
    slider(L"Digital Vibrance (%)",IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,636,0,100);
    slider(L"Hue (\x00B0)",IDC_LBL_HUE,IDC_HUE,IDC_VALHUE,700,0,359);

    const int bottomButtonY=panelBottom-19-38;
    HWND reset=Add(L"BUTTON",L"Reset",BS_OWNERDRAW,rightX,bottomButtonY,132,38,IDC_DEFAULTS);
    StyleMainButton(reset);
    gResetTooltip=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST,L"STATIC",
        L"Reset to NVIDIA defaults.",WS_POPUP,0,0,0,0,gWnd,nullptr,gInst,nullptr);
    if(gResetTooltip){
        SendMessageW(gResetTooltip,WM_SETFONT,(WPARAM)gFont,FALSE);
        SetWindowSubclass(gResetTooltip,ProfileTooltipSubclassProc,2,0);
        SetWindowSubclass(H(IDC_DEFAULTS),ResetButtonSubclassProc,1,0);
    }
    HWND saveProfile=Add(L"BUTTON",L"Save profile",BS_OWNERDRAW,
        rightX+rightW-160,bottomButtonY,160,38,IDC_SAVE);
    StyleMainButton(saveProfile);

    const int appX=centerPanelX+centerPanelW+gap+22;

    Add(L"STATIC",L"Show / hide window",0,appX,150,286,22,IDC_HOTKEY_SHOW_LABEL);
    HWND showHotkey=Add(HOTKEY_CLASSW,L"",WS_TABSTOP,appX+2,182,206,22,IDC_HOTKEY_SHOW);
    SetWindowSubclass(showHotkey,HotkeyFieldSubclassProc,1,0);
    RemoveNativeHotkeyFrame(showHotkey);
    SendMessageW(showHotkey,HKM_SETRULES,0,0);
    HWND clearShow=Add(L"BUTTON",L"Clear",BS_OWNERDRAW,appX+218,176,68,34,IDC_HOTKEY_SHOW_CLEAR);
    StyleMainButton(clearShow);
    Add(L"STATIC",L"Windows override",0,appX,234,286,22,IDC_HOTKEY_OVERRIDE_LABEL);
    HWND overrideHotkey=Add(HOTKEY_CLASSW,L"",WS_TABSTOP,appX+2,266,206,22,IDC_HOTKEY_OVERRIDE);
    SetWindowSubclass(overrideHotkey,HotkeyFieldSubclassProc,1,0);
    RemoveNativeHotkeyFrame(overrideHotkey);
    SendMessageW(overrideHotkey,HKM_SETRULES,0,0);
    HWND clearOverride=Add(L"BUTTON",L"Clear",BS_OWNERDRAW,appX+218,260,68,34,IDC_HOTKEY_OVERRIDE_CLEAR);
    StyleMainButton(clearOverride);
    Add(L"STATIC",L"Resume automatic switching",0,appX,318,286,22,IDC_HOTKEY_RESUME_LABEL);
    HWND resumeHotkey=Add(HOTKEY_CLASSW,L"",WS_TABSTOP,appX+2,350,206,22,IDC_HOTKEY_RESUME);
    SetWindowSubclass(resumeHotkey,HotkeyFieldSubclassProc,1,0);
    RemoveNativeHotkeyFrame(resumeHotkey);
    SendMessageW(resumeHotkey,HKM_SETRULES,0,0);
    HWND clearResume=Add(L"BUTTON",L"Clear",BS_OWNERDRAW,appX+218,344,68,34,IDC_HOTKEY_RESUME_CLEAR);
    StyleMainButton(clearResume);

    HWND appSettingsTitle=Add(L"STATIC",L"Application Settings",0,appX,579,250,24,IDC_APP_SETTINGS_TITLE);
    SendMessageW(appSettingsTitle,WM_SETFONT,(WPARAM)gFontBold,TRUE);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,617,22,22,IDC_STARTWIN); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Start with Windows",SS_CENTERIMAGE,appX+27,617,220,22,0);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,645,22,22,IDC_STARTMIN); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Start minimized",SS_CENTERIMAGE,appX+27,645,220,22,0);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,673,22,22,IDC_MINTRAY); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Minimize to tray",SS_CENTERIMAGE,appX+27,673,220,22,0);
    { HWND cb=Add(L"BUTTON",L"",BS_AUTOCHECKBOX,appX,701,22,22,IDC_CHECKUPDATES); StyleFlatCheckbox(cb); }
    Add(L"STATIC",L"Check for updates",SS_CENTERIMAGE,appX+27,701,220,22,IDC_CHECKUPDATES_LABEL);
    HWND manageDisplays=Add(L"BUTTON",L"Manage displays...",BS_OWNERDRAW,appX,bottomButtonY,138,38,IDC_MANAGE_DISPLAYS);
    StyleMainButton(manageDisplays);
    HWND exportProfiles=Add(L"BUTTON",L"Configuration...",BS_OWNERDRAW,appX+148,bottomButtonY,138,38,IDC_EXPORT_PROFILES);
    StyleMainButton(exportProfiles);
    gExportTooltip=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST,L"STATIC",
        L"Import or export the complete NvProfileSwitcher configuration.",WS_POPUP,0,0,0,0,gWnd,nullptr,gInst,nullptr);
    if(gExportTooltip){
        SendMessageW(gExportTooltip,WM_SETFONT,(WPARAM)gFont,FALSE);
        SetWindowSubclass(gExportTooltip,ProfileTooltipSubclassProc,6,0);
        SetWindowSubclass(exportProfiles,ConfigurationButtonTooltipSubclassProc,1,2);
    }
#if NVPS_DEV_BUILD
    gUpdateCheckTooltip=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST,L"STATIC",
        L"Update checks are disabled in development builds.",WS_POPUP,
        0,0,0,0,gWnd,nullptr,gInst,nullptr);
    if(gUpdateCheckTooltip){
        SendMessageW(gUpdateCheckTooltip,WM_SETFONT,(WPARAM)gFont,FALSE);
        SetWindowSubclass(gUpdateCheckTooltip,ProfileTooltipSubclassProc,4,0);
    }
#endif
    SetHotkeyControl(IDC_HOTKEY_SHOW,gSettings.showHideHotkey);
    SetHotkeyControl(IDC_HOTKEY_OVERRIDE,gSettings.windowsOverrideHotkey);
    SetHotkeyControl(IDC_HOTKEY_RESUME,gSettings.resumeAutomaticHotkey);

    SendMessageW(H(IDC_STARTWIN),BM_SETCHECK,gSettings.startWindows?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_STARTMIN),BM_SETCHECK,gSettings.startMinimized?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_MINTRAY),BM_SETCHECK,gSettings.minimizeToTray?BST_CHECKED:BST_UNCHECKED,0);
#if NVPS_DEV_BUILD
    SendMessageW(H(IDC_CHECKUPDATES),BM_SETCHECK,BST_UNCHECKED,0);
#else
    SendMessageW(H(IDC_CHECKUPDATES),BM_SETCHECK,gSettings.checkUpdates?BST_CHECKED:BST_UNCHECKED,0);
#endif

    HWND footGitHub=Add(L"BUTTON",L"GitHub",BS_OWNERDRAW,r.right-284,r.bottom-43,66,24,IDC_FOOT_GITHUB);
    HWND footSupport=Add(L"BUTTON",L"Support me",BS_OWNERDRAW,r.right-212,r.bottom-43,98,24,IDC_FOOT_SUPPORT);
    HWND footAbout=Add(L"BUTTON",L"About",BS_OWNERDRAW,r.right-108,r.bottom-43,64,24,IDC_FOOT_ABOUT);
    SetWindowSubclass(footGitHub,FooterLinkSubclassProc,1,0);
    SetWindowSubclass(footSupport,FooterLinkSubclassProc,1,0);
    SetWindowSubclass(footAbout,FooterLinkSubclassProc,1,0);
}

void ResizeControls(){
    RECT r{0,0,MAIN_BASE_CLIENT_WIDTH,MAIN_BASE_CLIENT_HEIGHT};
    const int margin=18, gap=14, footerH=56;
    const int leftW=360, settingsW=330;
    const int centerPanelX=margin+leftW+gap;
    const int centerPanelW=r.right-(margin*2)-leftW-settingsW-(gap*2);
    const int rightX=centerPanelX+22;
    const int rightW=centerPanelW-44;
    const int panelBottom=r.bottom-footerH-14;

    MoveUi(H(IDC_LIST),margin+10,144,leftW-20,(int)std::max(300,panelBottom-144-18),TRUE);
    MoveUi(H(IDC_ADD),120,95,112,32,TRUE);
    MoveUi(H(IDC_REMOVE),236,95,92,32,TRUE);
    MoveUi(H(IDC_PROFILE_UP),332,95,32,15,TRUE);
    MoveUi(H(IDC_PROFILE_DOWN),332,112,32,15,TRUE);

    MoveUi(H(IDC_LBL_NAME),rightX,152,110,22,TRUE);
    MoveUi(H(IDC_NAME),rightX+120,153,rightW-122,22,TRUE);

    const int browseW=135;
    const int fieldGap=10;
    MoveUi(H(IDC_LBL_EXE),rightX,194,120,22,TRUE);
    MoveUi(H(IDC_EXE),rightX+2,229,rightW-browseW-fieldGap-4,22,TRUE);
    MoveUi(H(IDC_RUNNING_APPS),rightX+rightW-browseW,209,browseW,28,TRUE);
    MoveUi(H(IDC_BROWSE),rightX+rightW-browseW,243,browseW,28,TRUE);
    MoveUi(H(IDC_ENABLED),rightX,272,22,22,TRUE);
    MoveUi(H(IDC_LBL_ENABLED),rightX+27,272,205,22,TRUE);
    const int profileHotkeyX=rightX+rightW-288;
    MoveUi(H(IDC_PROFILE_HOTKEY_LABEL),profileHotkeyX,272,220,22,TRUE);
    MoveUi(H(IDC_PROFILE_HOTKEY),profileHotkeyX+2,306,206,22,TRUE);
    MoveUi(H(IDC_PROFILE_HOTKEY_CLEAR),profileHotkeyX+220,300,68,34,TRUE);

    MoveUi(H(IDC_FOOT_GITHUB),r.right-284,r.bottom-43,66,24,TRUE);
    MoveUi(H(IDC_FOOT_SUPPORT),r.right-212,r.bottom-43,98,24,TRUE);
    MoveUi(H(IDC_FOOT_ABOUT),r.right-108,r.bottom-43,64,24,TRUE);

    SetDesktopUi(IsDesktopSelected());
}


HFONT ScaledFontFromBase(HFONT base,double scale){
    if(!base||std::abs(scale-1.0)<0.001) return base;
    LOGFONTW lf{};
    if(!GetObjectW(base,sizeof(lf),&lf)) return base;
    lf.lfHeight=(LONG)std::lround((double)lf.lfHeight*scale);
    if(lf.lfHeight==0) lf.lfHeight=-1;
    return CreateFontIndirectW(&lf);
}
void RecreateScaledUiFonts(){
    auto replace=[&](HFONT& current,HFONT base){
        if(current&&current!=base) DeleteObject(current);
        current=ScaledFontFromBase(base,gUiScale);
    };
    replace(gFont,gBaseFont);
    replace(gFontBold,gBaseFontBold);
    replace(gFontPanelTitle,gBaseFontPanelTitle);
    replace(gFontTitle,gBaseFontTitle);
    replace(gFontSmall,gBaseFontSmall);
    replace(gFontHeaderButton,gBaseFontHeaderButton);
    replace(gIconFont,gBaseIconFont);

    if(gWnd){
        EnumChildWindows(gWnd,[](HWND child,LPARAM){
            SendMessageW(child,WM_SETFONT,(WPARAM)gFont,TRUE);
            return TRUE;
        },0);
        if(HWND title=H(IDC_APP_SETTINGS_TITLE))
            SendMessageW(title,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        if(gProfileTooltip) SendMessageW(gProfileTooltip,WM_SETFONT,(WPARAM)gFont,TRUE);
        if(gExeTooltip) SendMessageW(gExeTooltip,WM_SETFONT,(WPARAM)gFont,TRUE);
        if(gResetTooltip) SendMessageW(gResetTooltip,WM_SETFONT,(WPARAM)gFont,TRUE);
        if(gImportTooltip) SendMessageW(gImportTooltip,WM_SETFONT,(WPARAM)gFont,TRUE);
        if(gExportTooltip) SendMessageW(gExportTooltip,WM_SETFONT,(WPARAM)gFont,TRUE);
    }
}

SIZE MainWindowSizeForScale(HWND hwnd,double scale){
    const DWORD style=(DWORD)GetWindowLongPtrW(hwnd,GWL_STYLE);
    const DWORD exStyle=(DWORD)GetWindowLongPtrW(hwnd,GWL_EXSTYLE);
    const UINT dpi=GetDpiForWindow(hwnd);
    RECT r{0,0,
        static_cast<LONG>(std::lround(MAIN_BASE_CLIENT_WIDTH*scale)),
        static_cast<LONG>(std::lround(MAIN_BASE_CLIENT_HEIGHT*scale))};
    if(!AdjustWindowRectExForDpi(&r,style,FALSE,exStyle,dpi))
        AdjustWindowRectEx(&r,style,FALSE,exStyle);
    return SIZE{r.right-r.left,r.bottom-r.top};
}

double CalculateMainWindowScale(HWND hwnd,HMONITOR monitor){
    MONITORINFO mi{sizeof(mi)};
    if(!monitor||!GetMonitorInfoW(monitor,&mi)) return 1.0;

    // GetMonitorInfo and MainWindowSizeForScale both use physical pixels for
    // this DPI-aware process, so keep the fit calculation in the same space.
    const int workW=(mi.rcWork.right-mi.rcWork.left)-MAIN_SAFE_MARGIN*2;
    const int workH=(mi.rcWork.bottom-mi.rcWork.top)-MAIN_SAFE_MARGIN*2;
    if(workW<=0||workH<=0) return 1.0;

    // Keep the 100% desktop layout unchanged, but grow it gently on high-DPI
    // displays so it remains physically comfortable to read. The work-area
    // fit below still has the final say if the preferred size does not fit.
    UINT dpi=GetDpiForWindow(hwnd);
    if(dpi==0) dpi=96;
    const double dpiScale=static_cast<double>(dpi)/96.0;
    const double adaptiveScale=dpiScale<=2.0
        ? 1.0+(dpiScale-1.0)*0.20
        : 1.20+(dpiScale-2.0)*0.10;
    const double preferredScale=std::clamp(adaptiveScale,1.0,1.30);

    double lo=0.25,hi=preferredScale;
    SIZE preferred=MainWindowSizeForScale(hwnd,preferredScale);
    if(preferred.cx<=workW&&preferred.cy<=workH) return preferredScale;

    for(int i=0;i<24;++i){
        const double mid=(lo+hi)*0.5;
        SIZE size=MainWindowSizeForScale(hwnd,mid);
        if(size.cx<=workW&&size.cy<=workH) lo=mid;
        else hi=mid;
    }
    return lo;
}

void ApplyResponsiveLayout(HWND hwnd,HMONITOR monitor,const RECT* suggested=nullptr,bool center=false){
    MONITORINFO mi{sizeof(mi)};
    if(!monitor||!GetMonitorInfoW(monitor,&mi)) return;

    // A per-monitor DPI transition updates fonts, the top-level window and many
    // child controls in one pass. Suppress intermediate paints so Windows does
    // not synchronously redraw every step while the window is being dragged
    // across monitors. The DPI change itself remains immediate.
    SendMessageW(hwnd,WM_SETREDRAW,FALSE,0);
    gApplyingResponsiveLayout=true;

    gUiScale=CalculateMainWindowScale(hwnd,monitor);
    RecreateScaledUiFonts();
    SIZE size=MainWindowSizeForScale(hwnd,gUiScale);

    const int left=(int)mi.rcWork.left+MAIN_SAFE_MARGIN;
    const int top=(int)mi.rcWork.top+MAIN_SAFE_MARGIN;
    const int right=(int)mi.rcWork.right-MAIN_SAFE_MARGIN;
    const int bottom=(int)mi.rcWork.bottom-MAIN_SAFE_MARGIN;

    int x=left+(right-left-size.cx)/2;
    int y=top+(bottom-top-size.cy)/2;
    if(!center&&suggested){
        x=(int)suggested->left;
        y=(int)suggested->top;
    }
    const int maxX=std::max<int>(left,static_cast<int>(right-size.cx));
    const int maxY=std::max<int>(top,static_cast<int>(bottom-size.cy));
    x=std::clamp<int>(x,left,maxX);
    y=std::clamp<int>(y,top,maxY);

    // Size the top-level window from the client area we actually need.  The
    // non-client metrics returned by Windows can be DPI-virtualized depending
    // on the manifest/current monitor, so verify the resulting client size and
    // correct the outer size by the measured delta instead of assuming it.
    const int desiredClientW=Ui(MAIN_BASE_CLIENT_WIDTH);
    const int desiredClientH=Ui(MAIN_BASE_CLIENT_HEIGHT);

    SetWindowPos(hwnd,nullptr,x,y,size.cx,size.cy,SWP_NOZORDER|SWP_NOACTIVATE);

    for(int pass=0;pass<3;++pass){
        RECT client{};
        RECT window{};
        GetClientRect(hwnd,&client);
        GetWindowRect(hwnd,&window);

        const int actualClientW=client.right-client.left;
        const int actualClientH=client.bottom-client.top;
        const int deltaW=desiredClientW-actualClientW;
        const int deltaH=desiredClientH-actualClientH;
        if(std::abs(deltaW)<=1&&std::abs(deltaH)<=1) break;

        const int outerW=(window.right-window.left)+deltaW;
        const int outerH=(window.bottom-window.top)+deltaH;
        SetWindowPos(hwnd,nullptr,0,0,outerW,outerH,
            SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
    }

    // The correction above may change the final outer dimensions by a few
    // pixels. Keep the finished window inside the selected monitor work area.
    RECT finalWindow{};
    GetWindowRect(hwnd,&finalWindow);
    const int finalW=finalWindow.right-finalWindow.left;
    const int finalH=finalWindow.bottom-finalWindow.top;
    const int finalMaxX=std::max(left,right-finalW);
    const int finalMaxY=std::max(top,bottom-finalH);
    const int finalX=std::clamp<int>(static_cast<int>(finalWindow.left),left,finalMaxX);
    const int finalY=std::clamp<int>(static_cast<int>(finalWindow.top),top,finalMaxY);
    if(finalX!=finalWindow.left||finalY!=finalWindow.top)
        SetWindowPos(hwnd,nullptr,finalX,finalY,0,0,
            SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE);

    ResizeControls();
    gApplyingResponsiveLayout=false;

    // Paint the completed layout once instead of repainting every intermediate
    // MoveWindow/WM_SETFONT operation during the DPI transition.
    SendMessageW(hwnd,WM_SETREDRAW,TRUE,0);
    RedrawWindow(hwnd,nullptr,nullptr,
        RDW_INVALIDATE|RDW_ERASE|RDW_ALLCHILDREN|RDW_UPDATENOW);
}



struct UpdateInfo{
    std::wstring version;
    std::wstring url;
    ResponsiveDialogState responsive{};
    HFONT titleFont{};
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
#if NVPS_DEV_BUILD
    if(manual)
        QueueAppMessage(L"Check for updates",L"Update checks are disabled in development builds.");
    return 0;
#else
    UpdateInfo info;
    if(GetLatestRelease(info)){
        if(IsVersionNewer(info.version,APP_VERSION)){
            auto* result=new UpdateInfo(std::move(info));
            if(!PostMessageW(gWnd,WM_UPDATE_AVAILABLE,0,(LPARAM)result))
                delete result;
        }else if(manual){
            std::wstring msg=L"NvProfileSwitcher is up to date.\n\nCurrent version: ";
            msg+=APP_VERSION;
            QueueAppMessage(L"Check for updates",msg);
        }
    }else if(manual){
        QueueAppMessage(L"Check for updates",L"Could not check for updates.\n\nPlease try again later.");
    }
    return 0;
#endif
}

constexpr int UPDATE_BASE_CLIENT_WIDTH=470;
constexpr int UPDATE_BASE_CLIENT_HEIGHT=175;
enum {IDC_UPDATE_TITLE=5501,IDC_UPDATE_CURRENT,IDC_UPDATE_BODY,IDC_UPDATE_DOWNLOAD=3101};

int DialogUi(const UpdateInfo* info,int value){
    return DialogUi(info?&info->responsive:nullptr,value);
}

void RecreateUpdateTitleFont(UpdateInfo* info){
    if(!info)return;
    if(info->titleFont)DeleteObject(info->titleFont);
    info->titleFont=CreateFontW(-DialogUi(info,20),0,0,0,FW_SEMIBOLD,0,0,0,
        DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
}

void LayoutUpdateDialog(HWND w,UpdateInfo* info){
    if(!w||!info)return;
    RECT client{};GetClientRect(w,&client);

    const int margin=DialogUi(info,DIALOG_MARGIN);
    const int lineGap=DialogUi(info,DIALOG_LINE_GAP);
    const int sectionGap=DialogUi(info,DIALOG_SECTION_GAP);
    const int buttonGap=DialogUi(info,DIALOG_BUTTON_GAP);
    const int buttonW=DialogUi(info,DIALOG_BUTTON_WIDTH);
    const int buttonH=DialogUi(info,DIALOG_BUTTON_HEIGHT);
    const int contentW=std::max(1,static_cast<int>(client.right)-margin*2);

    std::wstring heading=L"NvProfileSwitcher ";
    heading+=info->version;
    heading+=L" is available";
    std::wstring current=L"You are currently running version ";
    current+=APP_VERSION;
    current+=L".";
    const std::wstring body=L"A newer version is available on GitHub.";

    auto measureHeight=[&](const std::wstring& text,HFONT font){
        HDC dc=GetDC(w);
        RECT r{0,0,contentW,0};
        HFONT old=(HFONT)SelectObject(dc,font);
        DrawTextW(dc,text.c_str(),-1,&r,DT_CALCRECT|DT_WORDBREAK|DT_NOPREFIX);
        SelectObject(dc,old);
        ReleaseDC(w,dc);
        return std::max(1,static_cast<int>(r.bottom-r.top));
    };

    const int titleH=measureHeight(heading,info->titleFont);
    const int currentH=measureHeight(current,info->responsive.font);
    const int bodyH=measureHeight(body,info->responsive.font);
    const int titleY=margin;
    const int currentY=titleY+titleH+lineGap;
    const int bodyY=currentY+currentH+DialogUi(info,6);
    const int buttonY=bodyY+bodyH+sectionGap;

    MoveWindow(GetDlgItem(w,IDC_UPDATE_TITLE),margin,titleY,contentW,titleH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_UPDATE_CURRENT),margin,currentY,contentW,currentH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_UPDATE_BODY),margin,bodyY,contentW,bodyH,TRUE);

    const int buttonsWidth=buttonW*2+buttonGap;
    const int buttonsX=client.right-margin-buttonsWidth;
    MoveWindow(GetDlgItem(w,IDC_UPDATE_DOWNLOAD),buttonsX,buttonY,buttonW,buttonH,TRUE);
    MoveWindow(GetDlgItem(w,IDCANCEL),buttonsX+buttonW+buttonGap,buttonY,buttonW,buttonH,TRUE);

    SendMessageW(GetDlgItem(w,IDC_UPDATE_TITLE),WM_SETFONT,(WPARAM)info->titleFont,TRUE);
    SendMessageW(GetDlgItem(w,IDC_UPDATE_CURRENT),WM_SETFONT,(WPARAM)info->responsive.font,TRUE);
    SendMessageW(GetDlgItem(w,IDC_UPDATE_BODY),WM_SETFONT,(WPARAM)info->responsive.font,TRUE);
    SendMessageW(GetDlgItem(w,IDC_UPDATE_DOWNLOAD),WM_SETFONT,(WPARAM)info->responsive.fontBold,TRUE);
    SendMessageW(GetDlgItem(w,IDCANCEL),WM_SETFONT,(WPARAM)info->responsive.fontBold,TRUE);
}

void ApplyUpdateResponsiveLayout(HWND w,UpdateInfo* info,UINT dpi,const RECT* suggested=nullptr){
    if(!w||!info)return;
    ApplyResponsiveDialogWindow(w,&info->responsive,dpi,
        UPDATE_BASE_CLIENT_WIDTH,UPDATE_BASE_CLIENT_HEIGHT,suggested);
    RecreateUpdateTitleFont(info);
    LayoutUpdateDialog(w,info);
    InvalidateRect(w,nullptr,TRUE);
}

LRESULT CALLBACK UpdateProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    auto* info=(UpdateInfo*)GetWindowLongPtrW(w,GWLP_USERDATA);
    switch(m){
    case WM_CREATE:{
        auto* cs=(CREATESTRUCTW*)lp;
        info=(UpdateInfo*)cs->lpCreateParams;
        SetWindowLongPtrW(w,GWLP_USERDATA,(LONG_PTR)info);

        info->responsive.uiScale=AdaptiveUiScaleForDpi(GetDpiForWindow(w));
        RecreateResponsiveDialogFonts(&info->responsive);
        RecreateUpdateTitleFont(info);

        std::wstring heading=L"NvProfileSwitcher ";
        heading+=info->version;
        heading+=L" is available";
        std::wstring current=L"You are currently running version ";
        current+=APP_VERSION;
        current+=L".";

        CreateWindowExW(0,L"STATIC",heading.c_str(),WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_UPDATE_TITLE,gInst,nullptr);
        CreateWindowExW(0,L"STATIC",current.c_str(),WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_UPDATE_CURRENT,gInst,nullptr);
        CreateWindowExW(0,L"STATIC",L"A newer version is available on GitHub.",WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_UPDATE_BODY,gInst,nullptr);
        CreateWindowExW(0,L"BUTTON",L"Download",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)IDC_UPDATE_DOWNLOAD,gInst,nullptr);
        HWND later=CreateWindowExW(0,L"BUTTON",L"Later",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)IDCANCEL,gInst,nullptr);

        ApplyUpdateResponsiveLayout(w,info,GetDpiForWindow(w));
        SetFocus(later);
        return 0;
    }
    case WM_SIZE:
        if(info)LayoutUpdateDialog(w,info);
        return 0;
    case WM_DPICHANGED:{
        if(!info)break;
        const UINT dpi=HIWORD(wp)?HIWORD(wp):GetDpiForWindow(w);
        const RECT* suggested=reinterpret_cast<const RECT*>(lp);
        ApplyUpdateResponsiveLayout(w,info,dpi,suggested);
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
        if(d->CtlID==IDC_UPDATE_DOWNLOAD||d->CtlID==IDCANCEL){
            const bool down=(d->itemState&ODS_SELECTED)!=0;
            RECT r=d->rcItem;
            FillRound(d->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,7);
            const wchar_t* text=d->CtlID==IDC_UPDATE_DOWNLOAD?L"Download":L"Later";
            HFONT font=info&&info->responsive.fontBold?info->responsive.fontBold:gFontBold;
            SIZE z{};
            SelectObject(d->hDC,font);
            GetTextExtentPoint32W(d->hDC,text,(int)wcslen(text),&z);
            DrawLabel(d->hDC,text,r.left+(r.right-r.left-z.cx)/2,
                r.top+(r.bottom-r.top-z.cy)/2,C_TEXT,font);
            return TRUE;
        }
        break;
    }
    case WM_COMMAND:
        if(LOWORD(wp)==IDC_UPDATE_DOWNLOAD){
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
    case WM_DESTROY:
        if(info){
            if(info->titleFont)DeleteObject(info->titleFont);
            info->titleFont=nullptr;
            DestroyResponsiveDialogFonts(&info->responsive);
            delete info;
        }
        SetWindowLongPtrW(w,GWLP_USERDATA,0);
        return 0;
    }
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

    HWND owner=gWnd;
    HWND previousFocus=GetFocus();
    const bool disableOwner=owner&&IsWindowEnabled(owner);
    if(disableOwner)EnableWindow(owner,FALSE);

    HMONITOR monitor=MonitorFromWindow(owner?owner:gWnd,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};
    GetMonitorInfoW(monitor,&mi);
    const UINT dpi=GetDpiForWindow(owner?owner:gWnd);
    const double scale=AdaptiveUiScaleForDpi(dpi);

    RECT outer{0,0,
        static_cast<LONG>(std::lround(UPDATE_BASE_CLIENT_WIDTH*scale)),
        static_cast<LONG>(std::lround(UPDATE_BASE_CLIENT_HEIGHT*scale))};
    const DWORD style=WS_CAPTION|WS_SYSMENU;
    const DWORD exStyle=WS_EX_DLGMODALFRAME;
    if(!AdjustWindowRectExForDpi(&outer,style,FALSE,exStyle,dpi))
        AdjustWindowRectEx(&outer,style,FALSE,exStyle);
    const int ww=outer.right-outer.left;
    const int wh=outer.bottom-outer.top;
    const int x=mi.rcWork.left+(mi.rcWork.right-mi.rcWork.left-ww)/2;
    const int y=mi.rcWork.top+(mi.rcWork.bottom-mi.rcWork.top-wh)/2;

    HWND a=CreateWindowExW(exStyle,L"NvProfileSwitcherUpdate",
        L"NvProfileSwitcher Update",style,
        x,y,ww,wh,owner,nullptr,gInst,info);
    if(!a){
        delete info;
        if(disableOwner)EnableWindow(owner,TRUE);
        return;
    }

    BOOL darkTitle=TRUE;
    DwmSetWindowAttribute(a,20,&darkTitle,sizeof(darkTitle));

    ShowWindow(a,SW_SHOW);
    UpdateWindow(a);
    SetForegroundWindow(a);

    MSG msg{};
    while(IsWindow(a)&&GetMessageW(&msg,nullptr,0,0)>0){
        if(msg.message==WM_KEYDOWN&&msg.wParam==VK_ESCAPE
            &&(msg.hwnd==a||IsChild(a,msg.hwnd))){
            DestroyWindow(a);
            continue;
        }
        if(!IsDialogMessageW(a,&msg)){
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    if(disableOwner){
        EnableWindow(owner,TRUE);
        if(previousFocus&&IsWindow(previousFocus))SetFocus(previousFocus);
        else SetFocus(owner);
        SetForegroundWindow(owner);
    }
}


struct RunningAppEntry{
    std::wstring name;
    std::wstring executable;
    std::wstring path;
    HICON icon{};
};

struct RunningAppsDialogData{
    std::vector<RunningAppEntry> apps;
    std::wstring selectedPath;
    HWND list{};
    HWND emptyMessage{};
    HIMAGELIST images{};
    ResponsiveDialogState responsive{};
    int sortColumn=0;
    bool sortAscending=true;
};

std::wstring FileNameFromPath(const std::wstring& path){
    const wchar_t* name=PathFindFileNameW(path.c_str());
    return name?name:L"";
}

bool EqualPathInsensitive(const std::wstring& a,const std::wstring& b){
    return CompareStringOrdinal(a.c_str(),-1,b.c_str(),-1,TRUE)==CSTR_EQUAL;
}

BOOL CALLBACK CollectRunningAppWindow(HWND hwnd,LPARAM param){
    auto* apps=reinterpret_cast<std::vector<RunningAppEntry>*>(param);
    if(!apps||!IsWindowVisible(hwnd)||GetWindow(hwnd,GW_OWNER))return TRUE;

    int titleLength=GetWindowTextLengthW(hwnd);
    if(titleLength<=0)return TRUE;
    std::wstring title((size_t)titleLength+1,L'\0');
    GetWindowTextW(hwnd,title.data(),titleLength+1);
    title.resize(wcslen(title.c_str()));
    if(title.empty())return TRUE;

    DWORD processId=0;
    GetWindowThreadProcessId(hwnd,&processId);
    if(!processId||processId==GetCurrentProcessId())return TRUE;

    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,processId);
    if(!process)return TRUE;
    std::wstring path(32768,L'\0');
    DWORD pathLength=(DWORD)path.size();
    const BOOL queried=QueryFullProcessImageNameW(process,0,path.data(),&pathLength);
    CloseHandle(process);
    if(!queried||!pathLength)return TRUE;
    path.resize(pathLength);

    for(const auto& app:*apps)
        if(EqualPathInsensitive(app.path,path))return TRUE;

    apps->push_back({std::move(title),FileNameFromPath(path),std::move(path)});
    return TRUE;
}

std::vector<RunningAppEntry> EnumerateRunningApps(){
    std::vector<RunningAppEntry> apps;
    EnumWindows(CollectRunningAppWindow,(LPARAM)&apps);
    std::sort(apps.begin(),apps.end(),[](const RunningAppEntry& a,const RunningAppEntry& b){
        return CompareStringOrdinal(a.name.c_str(),-1,b.name.c_str(),-1,TRUE)==CSTR_LESS_THAN;
    });
    return apps;
}

void SortRunningApps(RunningAppsDialogData* data){
    if(!data||data->sortColumn<0||data->sortColumn>2)return;
    const int column=data->sortColumn;
    const bool ascending=data->sortAscending;
    std::stable_sort(data->apps.begin(),data->apps.end(),[column,ascending](const RunningAppEntry& a,const RunningAppEntry& b){
        const std::wstring* left=&a.name; const std::wstring* right=&b.name;
        if(column==1){left=&a.executable;right=&b.executable;} else if(column==2){left=&a.path;right=&b.path;}
        const int cmp=CompareStringOrdinal(left->c_str(),-1,right->c_str(),-1,TRUE);
        return ascending?cmp==CSTR_LESS_THAN:cmp==CSTR_GREATER_THAN;
    });
}

void PopulateRunningAppsList(RunningAppsDialogData* data){
    if(!data||!data->list)return;
    ListView_DeleteAllItems(data->list);
    for(auto& app:data->apps)if(app.icon)DestroyIcon(app.icon);
    if(data->images)ImageList_RemoveAll(data->images);
    data->apps=EnumerateRunningApps();
    SortRunningApps(data);
    if(data->emptyMessage)ShowWindow(data->emptyMessage,data->apps.empty()?SW_SHOW:SW_HIDE);

    for(size_t i=0;i<data->apps.size();++i){
        SHFILEINFOW info{};
        if(SHGetFileInfoW(data->apps[i].path.c_str(),0,&info,sizeof(info),SHGFI_ICON|SHGFI_SMALLICON))
            data->apps[i].icon=info.hIcon;
        LVITEMW item{};
        item.mask=LVIF_TEXT|LVIF_PARAM;
        item.iItem=(int)i;
        item.lParam=(LPARAM)i;
        item.pszText=(LPWSTR)data->apps[i].name.c_str();
        const int row=ListView_InsertItem(data->list,&item);
        ListView_SetItemText(data->list,row,1,(LPWSTR)data->apps[i].executable.c_str());
        ListView_SetItemText(data->list,row,2,(LPWSTR)data->apps[i].path.c_str());
    }
}


struct SavedDisplayInfo {
    std::wstring monitorId;
    std::wstring name;
    bool connected=false;
};
double AdaptiveUiScaleForDpi(UINT dpi){
    if(dpi==0) dpi=96;
    const double dpiScale=static_cast<double>(dpi)/96.0;
    const double adaptiveScale=dpiScale<=2.0
        ? 1.0+(dpiScale-1.0)*0.20
        : 1.20+(dpiScale-2.0)*0.10;
    return std::clamp(adaptiveScale,1.0,1.30);
}

int DialogUi(const ResponsiveDialogState* state,int value){
    const double scale=state?state->uiScale:1.0;
    return static_cast<int>(std::lround(static_cast<double>(value)*scale));
}

void RecreateResponsiveDialogFonts(ResponsiveDialogState* state){
    if(!state)return;
    if(state->font&&state->font!=gBaseFont)DeleteObject(state->font);
    if(state->fontBold&&state->fontBold!=gBaseFontBold)DeleteObject(state->fontBold);
    state->font=ScaledFontFromBase(gBaseFont,state->uiScale);
    state->fontBold=ScaledFontFromBase(gBaseFontBold,state->uiScale);
}

void DestroyResponsiveDialogFonts(ResponsiveDialogState* state){
    if(!state)return;
    if(state->font&&state->font!=gBaseFont)DeleteObject(state->font);
    if(state->fontBold&&state->fontBold!=gBaseFontBold)DeleteObject(state->fontBold);
    state->font=nullptr;
    state->fontBold=nullptr;
}

void ApplyResponsiveDialogWindow(HWND w,ResponsiveDialogState* state,UINT dpi,
    int baseClientWidth,int baseClientHeight,const RECT* suggested=nullptr){
    if(!w||!state)return;
    state->uiScale=AdaptiveUiScaleForDpi(dpi);
    RecreateResponsiveDialogFonts(state);

    const DWORD style=(DWORD)GetWindowLongPtrW(w,GWL_STYLE);
    const DWORD exStyle=(DWORD)GetWindowLongPtrW(w,GWL_EXSTYLE);
    RECT outer{0,0,
        static_cast<LONG>(std::lround(baseClientWidth*state->uiScale)),
        static_cast<LONG>(std::lround(baseClientHeight*state->uiScale))};
    if(!AdjustWindowRectExForDpi(&outer,style,FALSE,exStyle,dpi))
        AdjustWindowRectEx(&outer,style,FALSE,exStyle);

    RECT current{};GetWindowRect(w,&current);
    const int x=suggested?suggested->left:current.left;
    const int y=suggested?suggested->top:current.top;
    SetWindowPos(w,nullptr,x,y,outer.right-outer.left,outer.bottom-outer.top,
        SWP_NOZORDER|SWP_NOACTIVATE);
}


struct AboutDialogData {
    ResponsiveDialogState responsive{};
    HFONT titleFont{};
};

constexpr int ABOUT_BASE_CLIENT_WIDTH=470;
constexpr int ABOUT_BASE_CLIENT_HEIGHT=215;
enum {IDC_ABOUT_ICON=5301,IDC_ABOUT_NAME,IDC_ABOUT_VERSION,IDC_ABOUT_DESCRIPTION,IDC_ABOUT_COPYRIGHT};

int DialogUi(const AboutDialogData* data,int value){
    return DialogUi(data?&data->responsive:nullptr,value);
}

void RecreateAboutTitleFont(AboutDialogData* data){
    if(!data)return;
    if(data->titleFont)DeleteObject(data->titleFont);
    data->titleFont=CreateFontW(-DialogUi(data,21),0,0,0,FW_SEMIBOLD,0,0,0,
        DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
}

void LayoutAboutDialog(HWND w,AboutDialogData* data){
    if(!w||!data)return;
    RECT client{};GetClientRect(w,&client);

    const int margin=DialogUi(data,DIALOG_MARGIN);
    const int iconSize=DialogUi(data,40);
    const int iconGap=DialogUi(data,14);
    const int lineGap=DialogUi(data,DIALOG_LINE_GAP);
    const int sectionGap=DialogUi(data,DIALOG_SECTION_GAP);
    const int buttonGap=DialogUi(data,DIALOG_BUTTON_GAP);
    const int buttonW=DialogUi(data,DIALOG_BUTTON_WIDTH);
    const int buttonH=DialogUi(data,DIALOG_BUTTON_HEIGHT);
    const int contentW=std::max(1,static_cast<int>(client.right)-margin*2);
    const int headerTextX=margin+iconSize+iconGap;
    const int headerTextW=std::max(1,contentW-iconSize-iconGap);

    HDC dc=GetDC(w);
    auto measureHeight=[&](HWND control,HFONT font,int width){
        wchar_t text[512]{};GetWindowTextW(control,text,512);
        RECT measured{0,0,width,0};
        HFONT old=(HFONT)SelectObject(dc,font);
        DrawTextW(dc,text,-1,&measured,DT_CALCRECT|DT_WORDBREAK|DT_NOPREFIX);
        SelectObject(dc,old);
        return std::max(1,static_cast<int>(measured.bottom-measured.top));
    };

    const int nameH=measureHeight(GetDlgItem(w,IDC_ABOUT_NAME),data->titleFont,headerTextW);
    const int versionH=measureHeight(GetDlgItem(w,IDC_ABOUT_VERSION),data->responsive.font,headerTextW);
    const int headerH=std::max(iconSize,nameH+DialogUi(data,2)+versionH);
    const int descriptionH=measureHeight(GetDlgItem(w,IDC_ABOUT_DESCRIPTION),data->responsive.font,contentW);
    const int copyrightH=measureHeight(GetDlgItem(w,IDC_ABOUT_COPYRIGHT),data->responsive.font,contentW);
    ReleaseDC(w,dc);

    const int descriptionY=margin+headerH+sectionGap;
    const int copyrightY=descriptionY+descriptionH+lineGap;
    const int buttonY=client.bottom-margin-buttonH;
    const int buttonsWidth=buttonW*3+buttonGap*2;
    const int buttonsX=client.right-margin-buttonsWidth;

    MoveWindow(GetDlgItem(w,IDC_ABOUT_ICON),margin,margin,iconSize,iconSize,TRUE);
    MoveWindow(GetDlgItem(w,IDC_ABOUT_NAME),headerTextX,margin,headerTextW,nameH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_ABOUT_VERSION),headerTextX,margin+nameH+DialogUi(data,2),headerTextW,versionH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_ABOUT_DESCRIPTION),margin,descriptionY,contentW,descriptionH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_ABOUT_COPYRIGHT),margin,copyrightY,contentW,copyrightH,TRUE);
    MoveWindow(GetDlgItem(w,3001),buttonsX,buttonY,buttonW,buttonH,TRUE);
    MoveWindow(GetDlgItem(w,3002),buttonsX+buttonW+buttonGap,buttonY,buttonW,buttonH,TRUE);
    MoveWindow(GetDlgItem(w,IDCANCEL),buttonsX+(buttonW+buttonGap)*2,buttonY,buttonW,buttonH,TRUE);

    SendMessageW(GetDlgItem(w,IDC_ABOUT_NAME),WM_SETFONT,(WPARAM)data->titleFont,TRUE);
    SendMessageW(GetDlgItem(w,IDC_ABOUT_VERSION),WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    SendMessageW(GetDlgItem(w,IDC_ABOUT_DESCRIPTION),WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    SendMessageW(GetDlgItem(w,IDC_ABOUT_COPYRIGHT),WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    SendMessageW(GetDlgItem(w,3001),WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    SendMessageW(GetDlgItem(w,3002),WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    SendMessageW(GetDlgItem(w,IDCANCEL),WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    InvalidateRect(w,nullptr,TRUE);
}

int MeasureAboutContentBottom(HWND w,AboutDialogData* data){
    if(!w||!data)return 0;
    RECT r{};
    HWND copyrightControl=GetDlgItem(w,IDC_ABOUT_COPYRIGHT);
    if(!copyrightControl||!GetWindowRect(copyrightControl,&r))return 0;
    POINT bottomLeft{r.left,r.bottom};
    ScreenToClient(w,&bottomLeft);
    return bottomLeft.y;
}

void AutosizeAboutDialog(HWND w,AboutDialogData* data){
    if(!w||!data)return;

    const int margin=DialogUi(data,DIALOG_MARGIN);
    const int contentButtonGap=DialogUi(data,20);
    const int buttonH=DialogUi(data,DIALOG_BUTTON_HEIGHT);
    const int contentBottom=MeasureAboutContentBottom(w,data);
    if(contentBottom<=0)return;

    // Same vertical rule used by AppMessageProc:
    // content -> 20 logical px -> buttons -> 22 logical px bottom margin.
    const int desiredClientH=contentBottom+contentButtonGap+buttonH+margin;

    RECT client{};GetClientRect(w,&client);
    const int currentClientH=client.bottom-client.top;
    if(currentClientH==desiredClientH)return;

    RECT wr{};GetWindowRect(w,&wr);
    const int outerW=wr.right-wr.left;
    const int outerH=(wr.bottom-wr.top)+(desiredClientH-currentClientH);
    SetWindowPos(w,nullptr,0,0,outerW,outerH,
        SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
}

void ApplyAboutResponsiveLayout(HWND w,AboutDialogData* data,UINT dpi,const RECT* suggested=nullptr){
    if(!w||!data)return;
    ApplyResponsiveDialogWindow(w,&data->responsive,dpi,
        ABOUT_BASE_CLIENT_WIDTH,ABOUT_BASE_CLIENT_HEIGHT,suggested);
    RecreateAboutTitleFont(data);
    LayoutAboutDialog(w,data);
    AutosizeAboutDialog(w,data);
    LayoutAboutDialog(w,data);
}

LRESULT CALLBACK AboutProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    auto* data=(AboutDialogData*)GetWindowLongPtrW(w,GWLP_USERDATA);
    switch(m){
    case WM_CREATE:{
        data=(AboutDialogData*)((CREATESTRUCTW*)lp)->lpCreateParams;
        SetWindowLongPtrW(w,GWLP_USERDATA,(LONG_PTR)data);
        data->responsive.uiScale=AdaptiveUiScaleForDpi(GetDpiForWindow(w));
        RecreateResponsiveDialogFonts(&data->responsive);
        RecreateAboutTitleFont(data);

        const wchar_t* appName=L"NvProfileSwitcher";
        const wchar_t* description=L"Automatic per-application NVIDIA display color profiles for Windows";
        const wchar_t* copyrightText=L"Copyright \x00A9 2026 Maximiliano Carnevali";
        std::wstring versionText=L"Version ";versionText+=APP_VERSION;

        HWND icon=CreateWindowExW(0,L"STATIC",nullptr,WS_CHILD|WS_VISIBLE|SS_ICON,
            0,0,1,1,w,(HMENU)IDC_ABOUT_ICON,gInst,nullptr);
        SendMessageW(icon,STM_SETICON,(WPARAM)gIcon,0);
        CreateWindowExW(0,L"STATIC",appName,WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_ABOUT_NAME,gInst,nullptr);
        CreateWindowExW(0,L"STATIC",versionText.c_str(),WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_ABOUT_VERSION,gInst,nullptr);
        CreateWindowExW(0,L"STATIC",description,WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_ABOUT_DESCRIPTION,gInst,nullptr);
        CreateWindowExW(0,L"STATIC",copyrightText,WS_CHILD|WS_VISIBLE,
            0,0,1,1,w,(HMENU)IDC_ABOUT_COPYRIGHT,gInst,nullptr);

        HWND github=CreateWindowExW(0,L"BUTTON",L"GitHub",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)3001,gInst,nullptr);
        HWND support=CreateWindowExW(0,L"BUTTON",L"Support",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)3002,gInst,nullptr);
        HWND close=CreateWindowExW(0,L"BUTTON",L"Close",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)IDCANCEL,gInst,nullptr);
        StyleMainButton(github);StyleMainButton(support);StyleMainButton(close);
        LayoutAboutDialog(w,data);
        AutosizeAboutDialog(w,data);
        LayoutAboutDialog(w,data);
        return 0;
    }
    case WM_SIZE:
        if(data)LayoutAboutDialog(w,data);
        return 0;
    case WM_DPICHANGED:{
        if(!data)return 0;
        const UINT dpi=HIWORD(wp)?HIWORD(wp):GetDpiForWindow(w);
        const RECT* suggested=reinterpret_cast<const RECT*>(lp);
        ApplyAboutResponsiveLayout(w,data,dpi,suggested);
        return 0;
    }
    case WM_CTLCOLORSTATIC:{
        HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_BACK);SetBkMode(dc,TRANSPARENT);
        return (LRESULT)gBackBrush;
    }
    case WM_DRAWITEM:{
        auto* d=(DRAWITEMSTRUCT*)lp;
        if(d->CtlID==3001||d->CtlID==3002||d->CtlID==IDCANCEL){
            const bool down=(d->itemState&ODS_SELECTED)!=0;
            RECT r=d->rcItem;
            FillRound(d->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,7);
            const wchar_t* text=d->CtlID==3001?L"GitHub":(d->CtlID==3002?L"Support":L"Close");
            HFONT font=(data&&data->responsive.fontBold)?data->responsive.fontBold:gFontBold;
            HFONT old=(HFONT)SelectObject(d->hDC,font);
            SetBkMode(d->hDC,TRANSPARENT);SetTextColor(d->hDC,C_TEXT);
            DrawTextW(d->hDC,text,-1,&r,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            SelectObject(d->hDC,old);
            return TRUE;
        }
        break;
    }
    case WM_COMMAND:
        if(LOWORD(wp)==3001){ShellExecuteW(w,L"open",APP_URL,nullptr,nullptr,SW_SHOWNORMAL);return 0;}
        if(LOWORD(wp)==3002){ShellExecuteW(w,L"open",SUPPORT_URL,nullptr,nullptr,SW_SHOWNORMAL);return 0;}
        if(LOWORD(wp)==IDCANCEL){DestroyWindow(w);return 0;}
        break;
    case WM_CLOSE:DestroyWindow(w);return 0;
    case WM_DESTROY:
        if(data&&data->titleFont){DeleteObject(data->titleFont);data->titleFont=nullptr;}
        if(data)DestroyResponsiveDialogFonts(&data->responsive);
        return 0;
    }
    return DefWindowProcW(w,m,wp,lp);
}

void ShowAbout(){
    static bool registered=false;
    if(!registered){
        WNDCLASSEXW wc{sizeof(wc)};wc.lpfnWndProc=AboutProc;wc.hInstance=gInst;
        wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherAbout";
        if(!RegisterClassExW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return;
        registered=true;
    }

    HWND existing=FindWindowW(L"NvProfileSwitcherAbout",nullptr);
    if(existing){SetForegroundWindow(existing);return;}

    HWND owner=gWnd;
    HWND previousFocus=GetFocus();
    const bool disableOwner=owner&&IsWindowEnabled(owner);
    if(disableOwner)EnableWindow(owner,FALSE);

    RECT ownerRect{};
    if(owner&&IsWindowVisible(owner))GetWindowRect(owner,&ownerRect);
    else SystemParametersInfoW(SPI_GETWORKAREA,0,&ownerRect,0);
    HMONITOR ownerMonitor=owner?MonitorFromWindow(owner,MONITOR_DEFAULTTONEAREST):MonitorFromRect(&ownerRect,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};GetMonitorInfoW(ownerMonitor,&mi);

    AboutDialogData data{};
    const int initialX=ownerRect.left+(ownerRect.right-ownerRect.left-488)/2;
    const int initialY=ownerRect.top+(ownerRect.bottom-ownerRect.top-254)/2;
    HWND a=CreateWindowExW(WS_EX_DLGMODALFRAME,L"NvProfileSwitcherAbout",L"About NvProfileSwitcher",
        WS_CAPTION|WS_SYSMENU,initialX,initialY,488,254,owner,nullptr,gInst,&data);
    if(!a){if(disableOwner)EnableWindow(owner,TRUE);return;}

    BOOL darkTitle=TRUE;DwmSetWindowAttribute(a,20,&darkTitle,sizeof(darkTitle));
    ApplyAboutResponsiveLayout(a,&data,GetDpiForWindow(a),nullptr);

    RECT wr{};GetWindowRect(a,&wr);
    const int ww=wr.right-wr.left,wh=wr.bottom-wr.top;
    const int x=mi.rcWork.left+(mi.rcWork.right-mi.rcWork.left-ww)/2;
    const int y=mi.rcWork.top+(mi.rcWork.bottom-mi.rcWork.top-wh)/2;
    SetWindowPos(a,HWND_TOP,x,y,0,0,SWP_NOSIZE|SWP_SHOWWINDOW);
    SetForegroundWindow(a);

    MSG msg{};
    while(IsWindow(a)&&GetMessageW(&msg,nullptr,0,0)>0){
        if(!IsDialogMessageW(a,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}
    }
    if(disableOwner){
        EnableWindow(owner,TRUE);
        if(previousFocus&&IsWindow(previousFocus))SetFocus(previousFocus);else SetFocus(owner);
        SetForegroundWindow(owner);
    }
}

struct ManageDisplaysDialogData {
    HWND list{};
    HWND emptyMessage{};
    ResponsiveDialogState responsive{};
    std::vector<SavedDisplayInfo> displays;
    int sortColumn=-1;
    bool sortAscending=true;
};

constexpr int MANAGE_BASE_CLIENT_WIDTH=524;
constexpr int MANAGE_BASE_CLIENT_HEIGHT=261;

int DialogUi(const ManageDisplaysDialogData* data,int value){
    return DialogUi(data?&data->responsive:nullptr,value);
}

void LayoutManageDisplaysDialog(HWND w,ManageDisplaysDialogData* data){
    if(!w||!data)return;
    RECT client{};GetClientRect(w,&client);
    const int margin=DialogUi(data,DIALOG_MARGIN);
    const int headerH=DialogUi(data,32);
    const int buttonH=DialogUi(data,DIALOG_BUTTON_HEIGHT);
    const int buttonW=DialogUi(data,DIALOG_BUTTON_WIDTH);
    const int buttonY=client.bottom-margin-buttonH;
    const int contentW=std::max(1,static_cast<int>(client.right)-margin*2);
    const int listY=margin+headerH;
    const int listH=std::max(1,buttonY-margin-listY);

    MoveWindow(GetDlgItem(w,IDC_MANAGE_HEADER),margin,margin,contentW,headerH,TRUE);
    MoveWindow(data->list,margin,listY,contentW,listH,TRUE);
    MoveWindow(data->emptyMessage,margin+1,listY+1,std::max(1,contentW-2),std::max(1,listH-2),TRUE);
    MoveWindow(GetDlgItem(w,IDC_MANAGE_REMOVE),margin,buttonY,buttonW,buttonH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_MANAGE_CLOSE),client.right-margin-buttonW,buttonY,buttonW,buttonH,TRUE);

    const int displayColW=(contentW*70)/100;
    ListView_SetColumnWidth(data->list,0,displayColW);
    ListView_SetColumnWidth(data->list,1,contentW-displayColW);

    SendMessageW(data->list,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    SendMessageW(data->emptyMessage,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    SendMessageW(GetDlgItem(w,IDC_MANAGE_REMOVE),WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    SendMessageW(GetDlgItem(w,IDC_MANAGE_CLOSE),WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    InvalidateRect(w,nullptr,TRUE);
}

void ApplyManageDisplaysResponsiveLayout(HWND w,ManageDisplaysDialogData* data,UINT dpi,const RECT* suggested=nullptr){
    if(!w||!data)return;
    ApplyResponsiveDialogWindow(w,&data->responsive,dpi,
        MANAGE_BASE_CLIENT_WIDTH,MANAGE_BASE_CLIENT_HEIGHT,suggested);
    LayoutManageDisplaysDialog(w,data);
}

std::vector<SavedDisplayInfo> GetSavedDisplays(){
    std::vector<SavedDisplayInfo> savedDisplays;
    auto add=[&](const DisplayProfileValues& values){
        if(values.monitorId.empty())return;
        auto it=std::find_if(savedDisplays.begin(),savedDisplays.end(),
            [&](const SavedDisplayInfo& d){return SameMonitorId(d.monitorId,values.monitorId);});
        if(it==savedDisplays.end()){
            SavedDisplayInfo info{};
            info.monitorId=values.monitorId;
            info.name=values.displayName;
            savedDisplays.push_back(std::move(info));
        }else if(it->name==it->monitorId&&!values.displayName.empty()){
            it->name=values.displayName;
        }
    };

    for(const auto& profile:gSettings.desktopProfiles)
        for(const auto& values:profile.displayProfiles)add(values);
    for(const auto& profile:gSettings.profiles)
        for(const auto& values:profile.displayProfiles)add(values);

    std::vector<SavedDisplayInfo> result;
    result.reserve(savedDisplays.size());

    // Connected displays follow the exact same order as the display combo,
    // because the combo is populated directly from gDisplays.
    for(const auto& active:gDisplays){
        auto it=std::find_if(savedDisplays.begin(),savedDisplays.end(),
            [&](const SavedDisplayInfo& d){return SameMonitorId(d.monitorId,active.monitorId);});
        if(it==savedDisplays.end())continue;

        SavedDisplayInfo info=*it;
        info.connected=true;
        if(!active.label.empty())info.name=active.label;
        result.push_back(std::move(info));
    }

    // Saved displays that are no longer connected are appended afterwards,
    // preserving their existing saved order.
    for(const auto& saved:savedDisplays){
        bool alreadyAdded=std::any_of(result.begin(),result.end(),
            [&](const SavedDisplayInfo& d){return SameMonitorId(d.monitorId,saved.monitorId);});
        if(!alreadyAdded)result.push_back(saved);
    }

    return result;
}

void RemoveSavedDisplay(const std::wstring& monitorId){
    auto eraseFrom=[](ApplicationProfile& profile,const std::wstring& id){
        profile.displayProfiles.erase(std::remove_if(profile.displayProfiles.begin(),profile.displayProfiles.end(),
            [&](const DisplayProfileValues& values){return values.monitorId==id;}),profile.displayProfiles.end());
    };
    for(auto& profile:gSettings.desktopProfiles)eraseFrom(profile,monitorId);
    for(auto& profile:gSettings.profiles)eraseFrom(profile,monitorId);
    Save();
}

void SortManageDisplays(ManageDisplaysDialogData* data){
    if(!data||data->sortColumn<0||data->sortColumn>1)return;
    const int column=data->sortColumn; const bool ascending=data->sortAscending;
    std::stable_sort(data->displays.begin(),data->displays.end(),[column,ascending](const SavedDisplayInfo& a,const SavedDisplayInfo& b){
        int cmp=0;
        if(column==0)cmp=CompareStringOrdinal(a.name.c_str(),-1,b.name.c_str(),-1,TRUE);
        else{const wchar_t* left=a.connected?L"Connected":L"Disconnected"; const wchar_t* right=b.connected?L"Connected":L"Disconnected"; cmp=CompareStringOrdinal(left,-1,right,-1,TRUE);}
        return ascending?cmp==CSTR_LESS_THAN:cmp==CSTR_GREATER_THAN;
    });
}

void PopulateManageDisplaysList(ManageDisplaysDialogData* data){
    if(!data||!data->list)return;
    data->displays=GetSavedDisplays();
    SortManageDisplays(data);
    ListView_DeleteAllItems(data->list);
    for(size_t i=0;i<data->displays.size();++i){
        const auto& display=data->displays[i];
        LVITEMW item{};item.mask=LVIF_TEXT|LVIF_PARAM;item.iItem=(int)i;item.lParam=(LPARAM)i;item.pszText=(LPWSTR)display.name.c_str();
        int row=ListView_InsertItem(data->list,&item);
        ListView_SetItemText(data->list,row,1,(LPWSTR)(display.connected?L"Connected":L"Disconnected"));
    }
    ShowWindow(data->emptyMessage,data->displays.empty()?SW_SHOW:SW_HIDE);
    ShowWindow(data->list,data->displays.empty()?SW_HIDE:SW_SHOW);
    HWND remove=GetDlgItem(GetParent(data->list),IDC_MANAGE_REMOVE);
    if(remove)EnableWindow(remove,FALSE);
}

void TrimManageDisplaysUnusedListSpace(HWND w,ManageDisplaysDialogData* data){
    if(!w||!data||!data->list||data->displays.empty())return;

    RECT listClient{};
    GetClientRect(data->list,&listClient);

    RECT lastRow{};
    if(!ListView_GetItemRect(data->list,(int)data->displays.size()-1,&lastRow,LVIR_BOUNDS))
        return;

    const int bottomPadding=DialogUi(data,10);
    const int wantedListH=lastRow.bottom+bottomPadding;
    const int currentListH=listClient.bottom-listClient.top;
    const int trim=currentListH-wantedListH;

    // Only remove genuine unused space; never grow the list or alter row metrics.
    if(trim<=0)return;

    RECT wr{};
    GetWindowRect(w,&wr);
    SetWindowPos(w,nullptr,0,0,
        wr.right-wr.left,
        (wr.bottom-wr.top)-trim,
        SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);

    LayoutManageDisplaysDialog(w,data);
}

void RecreateManageDisplaysListForDpi(HWND w,ManageDisplaysDialogData* data){
    if(!w||!data)return;

    std::wstring selectedId;
    if(data->list){
        const int selected=ListView_GetNextItem(data->list,-1,LVNI_SELECTED);
        if(selected>=0&&selected<(int)data->displays.size())
            selectedId=data->displays[(size_t)selected].monitorId;
        DestroyWindow(data->list);
        data->list=nullptr;
    }

    data->list=CreateWindowExW(0,WC_LISTVIEWW,L"",
        WS_CHILD|WS_VISIBLE|WS_TABSTOP|LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS|LVS_NOCOLUMNHEADER|LVS_OWNERDRAWFIXED,
        0,0,1,1,w,(HMENU)IDC_MANAGE_LIST,gInst,nullptr);
    SetWindowTheme(data->list,L"",L"");
    ListView_SetExtendedListViewStyle(data->list,LVS_EX_FULLROWSELECT|LVS_EX_DOUBLEBUFFER);
    ListView_SetBkColor(data->list,C_FIELD);
    ListView_SetTextBkColor(data->list,C_FIELD);
    ListView_SetTextColor(data->list,C_TEXT);
    SendMessageW(data->list,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);

    LVCOLUMNW col{LVCF_WIDTH};
    col.cx=1;
    ListView_InsertColumn(data->list,0,&col);
    ListView_InsertColumn(data->list,1,&col);

    PopulateManageDisplaysList(data);
    LayoutManageDisplaysDialog(w,data);
    TrimManageDisplaysUnusedListSpace(w,data);

    if(!selectedId.empty()){
        for(size_t i=0;i<data->displays.size();++i){
            if(data->displays[i].monitorId==selectedId){
                ListView_SetItemState(data->list,(int)i,LVIS_SELECTED|LVIS_FOCUSED,LVIS_SELECTED|LVIS_FOCUSED);
                ListView_EnsureVisible(data->list,(int)i,FALSE);
                break;
            }
        }
    }
    SetFocus(data->list);
}

LRESULT CALLBACK ManageDisplaysDialogProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    auto* data=(ManageDisplaysDialogData*)GetWindowLongPtrW(w,GWLP_USERDATA);
    switch(m){
    case WM_CREATE:{
        data=(ManageDisplaysDialogData*)((CREATESTRUCTW*)lp)->lpCreateParams;
        SetWindowLongPtrW(w,GWLP_USERDATA,(LONG_PTR)data);
        data->responsive.uiScale=AdaptiveUiScaleForDpi(GetDpiForWindow(w));
        RecreateResponsiveDialogFonts(&data->responsive);

        CreateWindowExW(0,L"STATIC",L"",WS_CHILD|WS_VISIBLE|SS_OWNERDRAW|SS_NOTIFY,0,0,1,1,w,(HMENU)IDC_MANAGE_HEADER,gInst,nullptr);
        data->list=CreateWindowExW(0,WC_LISTVIEWW,L"",WS_CHILD|WS_VISIBLE|WS_TABSTOP|LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS|LVS_NOCOLUMNHEADER|LVS_OWNERDRAWFIXED,
            0,0,1,1,w,(HMENU)IDC_MANAGE_LIST,gInst,nullptr);
        SetWindowTheme(data->list,L"",L"");
        ListView_SetExtendedListViewStyle(data->list,LVS_EX_FULLROWSELECT|LVS_EX_DOUBLEBUFFER);
        ListView_SetBkColor(data->list,C_FIELD);ListView_SetTextBkColor(data->list,C_FIELD);ListView_SetTextColor(data->list,C_TEXT);
        LVCOLUMNW col{LVCF_WIDTH};col.cx=1;ListView_InsertColumn(data->list,0,&col);ListView_InsertColumn(data->list,1,&col);
        data->emptyMessage=CreateWindowExW(0,L"STATIC",L"No saved displays found.",WS_CHILD|SS_CENTER|SS_CENTERIMAGE,
            0,0,1,1,w,(HMENU)IDC_MANAGE_EMPTY,gInst,nullptr);
        HWND remove=CreateWindowExW(0,L"BUTTON",L"Remove",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,0,0,1,1,w,(HMENU)IDC_MANAGE_REMOVE,gInst,nullptr);
        HWND close=CreateWindowExW(0,L"BUTTON",L"Close",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,0,0,1,1,w,(HMENU)IDC_MANAGE_CLOSE,gInst,nullptr);
        StyleMainButton(remove);StyleMainButton(close);
        LayoutManageDisplaysDialog(w,data);
        PopulateManageDisplaysList(data);

        // WM_CREATE happens before the initial WM_SIZE.
        // Trim after that first layout so the direct-open path
        // matches the already-correct DPI transition path.
        PostMessageW(w,WM_APP+20,0,0);
        SetFocus(data->list);return 0;
    }
    case WM_APP+20:
        if(data){
            LayoutManageDisplaysDialog(w,data);
            TrimManageDisplaysUnusedListSpace(w,data);
        }
        return 0;
    case WM_DPICHANGED:{
        const UINT dpi=HIWORD(wp)?HIWORD(wp):GetDpiForWindow(w);
        const RECT* suggested=reinterpret_cast<const RECT*>(lp);
        ApplyManageDisplaysResponsiveLayout(w,data,dpi,suggested);

        // A report ListView with LVS_OWNERDRAWFIXED keeps internal row/scroll
        // metrics from the DPI at which the control was created. Recreate only
        // this child control so a DPI transition converges to the same layout
        // as opening the dialog directly on that monitor.
        RecreateManageDisplaysListForDpi(w,data);
        return 0;
    }
    case WM_SIZE:
        if(data)LayoutManageDisplaysDialog(w,data);
        return 0;
    case WM_NOTIFY:
        if(((NMHDR*)lp)->idFrom==IDC_MANAGE_LIST&&((NMHDR*)lp)->code==LVN_ITEMCHANGED&&data){
            int row=ListView_GetNextItem(data->list,-1,LVNI_SELECTED);
            bool canRemove=row>=0&&row<(int)data->displays.size()&&!data->displays[(size_t)row].connected;
            EnableWindow(GetDlgItem(w,IDC_MANAGE_REMOVE),canRemove);return 0;
        }
        break;
    case WM_COMMAND:
        if(LOWORD(wp)==IDC_MANAGE_HEADER&&HIWORD(wp)==STN_CLICKED&&data){
            HWND header=GetDlgItem(w,IDC_MANAGE_HEADER); POINT pt{};GetCursorPos(&pt);ScreenToClient(header,&pt); RECT r{};GetClientRect(header,&r);
            const int column=pt.x<((r.right-r.left)*70)/100?0:1;
            if(data->sortColumn==column)data->sortAscending=!data->sortAscending; else{data->sortColumn=column;data->sortAscending=true;}
            SortManageDisplays(data); InvalidateRect(header,nullptr,TRUE); ListView_DeleteAllItems(data->list);
            for(size_t i=0;i<data->displays.size();++i){auto& display=data->displays[i];LVITEMW item{};item.mask=LVIF_TEXT|LVIF_PARAM;item.iItem=(int)i;item.lParam=(LPARAM)i;item.pszText=(LPWSTR)display.name.c_str();const int row=ListView_InsertItem(data->list,&item);ListView_SetItemText(data->list,row,1,(LPWSTR)(display.connected?L"Connected":L"Disconnected"));}
            EnableWindow(GetDlgItem(w,IDC_MANAGE_REMOVE),FALSE);SetFocus(data->list);return 0;
        }
        if(LOWORD(wp)==IDC_MANAGE_CLOSE){DestroyWindow(w);return 0;}
        if(LOWORD(wp)==IDC_MANAGE_REMOVE&&data){
            int row=ListView_GetNextItem(data->list,-1,LVNI_SELECTED);
            if(row>=0&&row<(int)data->displays.size()&&!data->displays[(size_t)row].connected){
                const auto display=data->displays[(size_t)row];
                std::wstring msg=L"Remove this display?\n\n"+display.name+L"\n\nIts saved settings will be permanently removed from all profiles.";
                if(ShowAppConfirm(w,L"Remove display",msg)){
                    RemoveSavedDisplay(display.monitorId);PopulateManageDisplaysList(data);
                }
            }
            return 0;
        }
        break;
    case WM_DRAWITEM:{
        auto* draw=(DRAWITEMSTRUCT*)lp;
        if(draw->CtlID==IDC_MANAGE_LIST&&draw->itemID!=(UINT)-1&&data&&draw->itemID<data->displays.size()){
            const auto& display=data->displays[draw->itemID];
            RECT r=draw->rcItem;
            if(draw->itemState&ODS_SELECTED){HBRUSH selectedBrush=CreateSolidBrush(C_ACCENT_DARK);FillRect(draw->hDC,&r,selectedBrush);DeleteObject(selectedBrush);}
            else FillRect(draw->hDC,&r,gFieldBrush);
            SetBkMode(draw->hDC,TRANSPARENT);SetTextColor(draw->hDC,C_TEXT);
            HFONT oldFont=(HFONT)SelectObject(draw->hDC,data->responsive.font?data->responsive.font:gFont);
            const int divider=r.left+((r.right-r.left)*70)/100;
            const int pad=DialogUi(data,10);
            const int innerGap=DialogUi(data,8);
            RECT nameRect{r.left+pad,r.top,divider-innerGap,r.bottom};
            RECT statusRect{divider+pad,r.top,r.right-innerGap,r.bottom};
            DrawTextW(draw->hDC,display.name.c_str(),-1,&nameRect,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
            const wchar_t* status=display.connected?L"Connected":L"Disconnected";
            DrawTextW(draw->hDC,status,-1,&statusRect,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            SelectObject(draw->hDC,oldFont);return TRUE;
        }
        if(draw->CtlID==IDC_MANAGE_HEADER&&data){
            RECT r=draw->rcItem;FillRect(draw->hDC,&r,gPanel2Brush);SetBkMode(draw->hDC,TRANSPARENT);SetTextColor(draw->hDC,C_TEXT);
            HFONT oldFont=(HFONT)SelectObject(draw->hDC,data->responsive.fontBold?data->responsive.fontBold:gFontBold);
            const int divider=r.left+((r.right-r.left)*70)/100;
            const int pad=DialogUi(data,10);
            const int innerGap=DialogUi(data,8);
            RECT a{r.left+pad,r.top,divider-innerGap,r.bottom};RECT b{divider+pad,r.top,r.right-innerGap,r.bottom};
            DrawTextW(draw->hDC,L"Display",-1,&a,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);DrawTextW(draw->hDC,L"Status",-1,&b,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            const int glyphUnit=std::max(1,DialogUi(data,1));
            const int glyphWidth=glyphUnit*14;
            const int glyphRightPad=DialogUi(data,10);
            const int glyphY=(r.top+r.bottom)/2;
            DrawSortIndicator(draw->hDC,divider-glyphRightPad-glyphWidth,glyphY,glyphUnit,data->sortColumn==0?C_TEXT:C_MUTED,data->sortColumn==0,data->sortAscending);
            DrawSortIndicator(draw->hDC,r.right-glyphRightPad-glyphWidth,glyphY,glyphUnit,data->sortColumn==1?C_TEXT:C_MUTED,data->sortColumn==1,data->sortAscending);
            Fill(draw->hDC,divider,(int)r.top+DialogUi(data,6),1,std::max(1,(int)(r.bottom-r.top)-DialogUi(data,12)),C_BORDER);
            Fill(draw->hDC,r.left,r.bottom-1,r.right-r.left,1,C_BORDER);SelectObject(draw->hDC,oldFont);return TRUE;
        }
        if((draw->CtlID==IDC_MANAGE_REMOVE||draw->CtlID==IDC_MANAGE_CLOSE)&&data){
            const bool down=(draw->itemState&ODS_SELECTED)!=0;const bool disabled=(draw->itemState&ODS_DISABLED)!=0;RECT r=draw->rcItem;
            FillRound(draw->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,DialogUi(data,7));
            SetBkMode(draw->hDC,TRANSPARENT);SetTextColor(draw->hDC,disabled?C_MUTED:C_TEXT);
            HFONT oldFont=(HFONT)SelectObject(draw->hDC,data->responsive.fontBold?data->responsive.fontBold:gFontBold);
            wchar_t label[128]{};GetWindowTextW(draw->hwndItem,label,128);DrawTextW(draw->hDC,label,-1,&r,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            SelectObject(draw->hDC,oldFont);return TRUE;
        }
        break;
    }
    case WM_CTLCOLORSTATIC:{HDC dc=(HDC)wp;SetTextColor(dc,C_MUTED);SetBkMode(dc,TRANSPARENT);return (LRESULT)gBackBrush;}
    case WM_PAINT:{
        PAINTSTRUCT ps{};HDC dc=BeginPaint(w,&ps);RECT client{};GetClientRect(w,&client);
        const int margin=DialogUi(data,DIALOG_MARGIN);const int buttonY=client.bottom-margin-DialogUi(data,DIALOG_BUTTON_HEIGHT);
        RECT border{margin,margin,client.right-margin,buttonY-margin};HBRUSH b=CreateSolidBrush(C_BORDER);FrameRect(dc,&border,b);DeleteObject(b);EndPaint(w,&ps);return 0;
    }
    case WM_DESTROY:
        if(data)DestroyResponsiveDialogFonts(&data->responsive);
        return 0;
    case WM_CLOSE:DestroyWindow(w);return 0;
    }
    return DefWindowProcW(w,m,wp,lp);
}

void ShowManageDisplaysDialog(HWND owner){
    static bool registered=false;
    if(!registered){WNDCLASSEXW wc{sizeof(wc)};wc.lpfnWndProc=ManageDisplaysDialogProc;wc.hInstance=gInst;wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherManageDisplays";if(!RegisterClassExW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return;registered=true;}
    ManageDisplaysDialogData data{};HWND previousFocus=GetFocus();const bool disableOwner=owner&&IsWindowEnabled(owner);if(disableOwner)EnableWindow(owner,FALSE);
    RECT ownerRect{};GetWindowRect(owner,&ownerRect);
    HMONITOR ownerMonitor=MonitorFromWindow(owner,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};GetMonitorInfoW(ownerMonitor,&mi);
    const int initialX=ownerRect.left+(ownerRect.right-ownerRect.left-540)/2;
    const int initialY=ownerRect.top+(ownerRect.bottom-ownerRect.top-300)/2;
    HWND dialog=CreateWindowExW(WS_EX_DLGMODALFRAME,L"NvProfileSwitcherManageDisplays",L"Manage displays",WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU,
        initialX,initialY,540,300,owner,nullptr,gInst,&data);
    if(!dialog){if(disableOwner)EnableWindow(owner,TRUE);return;}
    BOOL darkTitle=TRUE;DwmSetWindowAttribute(dialog,20,&darkTitle,sizeof(darkTitle));
    ApplyManageDisplaysResponsiveLayout(dialog,&data,GetDpiForWindow(dialog),nullptr);
    RECT wr{};GetWindowRect(dialog,&wr);
    const int ww=wr.right-wr.left,wh=wr.bottom-wr.top;
    const int x=mi.rcWork.left+(mi.rcWork.right-mi.rcWork.left-ww)/2;
    const int y=mi.rcWork.top+(mi.rcWork.bottom-mi.rcWork.top-wh)/2;
    SetWindowPos(dialog,HWND_TOP,x,y,0,0,SWP_NOSIZE|SWP_SHOWWINDOW);
    MSG msg{};while(IsWindow(dialog)&&GetMessageW(&msg,nullptr,0,0)>0){if(!IsDialogMessageW(dialog,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}}
    if(disableOwner){EnableWindow(owner,TRUE);if(previousFocus&&IsWindow(previousFocus))SetFocus(previousFocus);else SetFocus(owner);SetForegroundWindow(owner);}
}

constexpr int RUNNING_BASE_CLIENT_WIDTH=704;
constexpr int RUNNING_BASE_CLIENT_HEIGHT=351;

int DialogUi(const RunningAppsDialogData* data,int value){
    return DialogUi(data?&data->responsive:nullptr,value);
}

void InsertRunningAppsRows(RunningAppsDialogData* data){
    if(!data||!data->list)return;
    ListView_DeleteAllItems(data->list);
    for(size_t i=0;i<data->apps.size();++i){
        LVITEMW item{};
        item.mask=LVIF_TEXT|LVIF_PARAM;
        item.iItem=(int)i;
        item.lParam=(LPARAM)i;
        item.pszText=(LPWSTR)data->apps[i].name.c_str();
        const int row=ListView_InsertItem(data->list,&item);
        ListView_SetItemText(data->list,row,1,(LPWSTR)data->apps[i].executable.c_str());
        ListView_SetItemText(data->list,row,2,(LPWSTR)data->apps[i].path.c_str());
    }
}

void LayoutRunningAppsDialog(HWND w,RunningAppsDialogData* data){
    if(!w||!data)return;
    RECT client{};GetClientRect(w,&client);
    const int margin=DialogUi(data,DIALOG_MARGIN);
    const int headerH=DialogUi(data,32);
    const int buttonH=DialogUi(data,DIALOG_BUTTON_HEIGHT);
    const int buttonW=DialogUi(data,DIALOG_BUTTON_WIDTH);
    const int buttonGap=DialogUi(data,DIALOG_BUTTON_GAP);
    const int buttonY=client.bottom-margin-buttonH;
    const int contentW=std::max(1,static_cast<int>(client.right)-margin*2);
    const int listY=margin+headerH;
    const int listH=std::max(1,buttonY-margin-listY);

    MoveWindow(GetDlgItem(w,IDC_RUNNING_HEADER),margin,margin,contentW,headerH,TRUE);
    MoveWindow(data->list,margin,listY,contentW,listH,TRUE);
    MoveWindow(data->emptyMessage,margin+1,listY+1,std::max(1,contentW-2),std::max(1,listH-2),TRUE);
    MoveWindow(GetDlgItem(w,IDC_RUNNING_REFRESH),margin,buttonY,buttonW,buttonH,TRUE);
    MoveWindow(GetDlgItem(w,IDC_RUNNING_SELECT),client.right-margin-buttonW*2-buttonGap,buttonY,buttonW,buttonH,TRUE);
    MoveWindow(GetDlgItem(w,IDCANCEL),client.right-margin-buttonW,buttonY,buttonW,buttonH,TRUE);

    const int appCol=DialogUi(data,230);
    const int exeCol=DialogUi(data,145);
    ListView_SetColumnWidth(data->list,0,appCol);
    ListView_SetColumnWidth(data->list,1,exeCol);
    ListView_SetColumnWidth(data->list,2,std::max(1,contentW-appCol-exeCol));

    SendMessageW(data->list,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    SendMessageW(data->emptyMessage,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);
    const int buttonIds[]={IDC_RUNNING_REFRESH,IDC_RUNNING_SELECT,IDCANCEL};
    for(const int id:buttonIds)
        SendMessageW(GetDlgItem(w,id),WM_SETFONT,(WPARAM)data->responsive.fontBold,TRUE);
    InvalidateRect(w,nullptr,TRUE);
}

void ApplyRunningAppsResponsiveLayout(HWND w,RunningAppsDialogData* data,UINT dpi,const RECT* suggested=nullptr){
    if(!w||!data)return;
    ApplyResponsiveDialogWindow(w,&data->responsive,dpi,
        RUNNING_BASE_CLIENT_WIDTH,RUNNING_BASE_CLIENT_HEIGHT,suggested);
    LayoutRunningAppsDialog(w,data);
}

void RecreateRunningAppsListForDpi(HWND w,RunningAppsDialogData* data){
    if(!w||!data)return;

    std::wstring selectedPath;
    if(data->list){
        const int selected=ListView_GetNextItem(data->list,-1,LVNI_SELECTED);
        if(selected>=0&&selected<(int)data->apps.size())
            selectedPath=data->apps[(size_t)selected].path;
        DestroyWindow(data->list);
        data->list=nullptr;
    }

    data->list=CreateWindowExW(0,WC_LISTVIEWW,L"",
        WS_CHILD|WS_VISIBLE|WS_TABSTOP|LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS|LVS_NOCOLUMNHEADER|LVS_OWNERDRAWFIXED,
        0,0,1,1,w,(HMENU)IDC_RUNNING_LIST,gInst,nullptr);
    SetWindowTheme(data->list,L"",L"");
    ListView_SetExtendedListViewStyle(data->list,LVS_EX_FULLROWSELECT|LVS_EX_DOUBLEBUFFER);
    ListView_SetBkColor(data->list,C_FIELD);
    ListView_SetTextBkColor(data->list,C_FIELD);
    ListView_SetTextColor(data->list,C_TEXT);
    SendMessageW(data->list,WM_SETFONT,(WPARAM)data->responsive.font,TRUE);

    LVCOLUMNW column{LVCF_WIDTH|LVCF_SUBITEM};
    column.cx=1;column.iSubItem=0;ListView_InsertColumn(data->list,0,&column);
    column.iSubItem=1;ListView_InsertColumn(data->list,1,&column);
    column.iSubItem=2;ListView_InsertColumn(data->list,2,&column);

    InsertRunningAppsRows(data);
    LayoutRunningAppsDialog(w,data);

    if(!selectedPath.empty()){
        for(size_t i=0;i<data->apps.size();++i){
            if(EqualPathInsensitive(data->apps[i].path,selectedPath)){
                ListView_SetItemState(data->list,(int)i,LVIS_SELECTED|LVIS_FOCUSED,LVIS_SELECTED|LVIS_FOCUSED);
                ListView_EnsureVisible(data->list,(int)i,FALSE);
                break;
            }
        }
    }
    SetFocus(data->list);
}

LRESULT CALLBACK RunningAppsDialogProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    auto* data=(RunningAppsDialogData*)GetWindowLongPtrW(w,GWLP_USERDATA);
    switch(m){
    case WM_CREATE:{
        data=(RunningAppsDialogData*)((CREATESTRUCTW*)lp)->lpCreateParams;
        SetWindowLongPtrW(w,GWLP_USERDATA,(LONG_PTR)data);
        data->responsive.uiScale=AdaptiveUiScaleForDpi(GetDpiForWindow(w));
        RecreateResponsiveDialogFonts(&data->responsive);

        CreateWindowExW(0,L"STATIC",L"",WS_CHILD|WS_VISIBLE|SS_OWNERDRAW|SS_NOTIFY,
            0,0,1,1,w,(HMENU)IDC_RUNNING_HEADER,gInst,nullptr);
        data->list=CreateWindowExW(0,WC_LISTVIEWW,L"",
            WS_CHILD|WS_VISIBLE|WS_TABSTOP|LVS_REPORT|LVS_SINGLESEL|LVS_SHOWSELALWAYS|LVS_NOCOLUMNHEADER|LVS_OWNERDRAWFIXED,
            0,0,1,1,w,(HMENU)IDC_RUNNING_LIST,gInst,nullptr);
        SetWindowTheme(data->list,L"",L"");
        ListView_SetExtendedListViewStyle(data->list,LVS_EX_FULLROWSELECT|LVS_EX_DOUBLEBUFFER);
        ListView_SetBkColor(data->list,C_FIELD);
        ListView_SetTextBkColor(data->list,C_FIELD);
        ListView_SetTextColor(data->list,C_TEXT);

        data->images=ImageList_Create(20,28,ILC_COLOR32|ILC_MASK,16,16);
        ListView_SetImageList(data->list,data->images,LVSIL_SMALL);

        data->emptyMessage=CreateWindowExW(0,L"STATIC",L"No running applications found.",
            WS_CHILD|SS_CENTER|SS_CENTERIMAGE,0,0,1,1,w,(HMENU)IDC_RUNNING_EMPTY,gInst,nullptr);

        LVCOLUMNW column{LVCF_WIDTH|LVCF_SUBITEM};
        column.cx=1;column.iSubItem=0;ListView_InsertColumn(data->list,0,&column);
        column.iSubItem=1;ListView_InsertColumn(data->list,1,&column);
        column.iSubItem=2;ListView_InsertColumn(data->list,2,&column);

        HWND refresh=CreateWindowExW(0,L"BUTTON",L"Refresh",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)IDC_RUNNING_REFRESH,gInst,nullptr);
        HWND cancel=CreateWindowExW(0,L"BUTTON",L"Cancel",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)IDCANCEL,gInst,nullptr);
        HWND select=CreateWindowExW(0,L"BUTTON",L"Select",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            0,0,1,1,w,(HMENU)IDC_RUNNING_SELECT,gInst,nullptr);
        for(HWND button:{refresh,cancel,select})StyleMainButton(button);

        LayoutRunningAppsDialog(w,data);
        PopulateRunningAppsList(data);
        SetFocus(data->list);
        return 0;
    }
    case WM_SIZE:
        if(data)LayoutRunningAppsDialog(w,data);
        return 0;
    case WM_DPICHANGED:{
        if(!data)return 0;
        const UINT dpi=HIWORD(wp)?HIWORD(wp):GetDpiForWindow(w);
        const RECT* suggested=reinterpret_cast<const RECT*>(lp);
        ApplyRunningAppsResponsiveLayout(w,data,dpi,suggested);
        RecreateRunningAppsListForDpi(w,data);
        return 0;
    }
    case WM_NOTIFY:
        if(((NMHDR*)lp)->idFrom==IDC_RUNNING_LIST&&((NMHDR*)lp)->code==NM_DBLCLK){
            SendMessageW(w,WM_COMMAND,IDC_RUNNING_SELECT,0);return 0;
        }
        break;
    case WM_COMMAND:
        if(LOWORD(wp)==IDC_RUNNING_HEADER&&HIWORD(wp)==STN_CLICKED&&data){
            HWND header=GetDlgItem(w,IDC_RUNNING_HEADER);POINT pt{};GetCursorPos(&pt);ScreenToClient(header,&pt);
            const int firstDivider=DialogUi(data,230);const int secondDivider=DialogUi(data,375);const int column=pt.x<firstDivider?0:(pt.x<secondDivider?1:2);
            if(data->sortColumn==column)data->sortAscending=!data->sortAscending;else{data->sortColumn=column;data->sortAscending=true;}
            SortRunningApps(data);InvalidateRect(header,nullptr,TRUE);InsertRunningAppsRows(data);SetFocus(data->list);return 0;
        }
        if(LOWORD(wp)==IDC_RUNNING_REFRESH){PopulateRunningAppsList(data);return 0;}
        if(LOWORD(wp)==IDC_RUNNING_SELECT){
            int row=ListView_GetNextItem(data->list,-1,LVNI_SELECTED);
            if(row>=0&&row<(int)data->apps.size()){
                LVITEMW item{};item.mask=LVIF_PARAM;item.iItem=row;
                if(ListView_GetItem(data->list,&item))data->selectedPath=data->apps[(size_t)item.lParam].path;
                DestroyWindow(w);
            }
            return 0;
        }
        if(LOWORD(wp)==IDCANCEL){DestroyWindow(w);return 0;}
        break;
    case WM_DRAWITEM:{
        auto* draw=(DRAWITEMSTRUCT*)lp;
        if(draw->CtlID==IDC_RUNNING_LIST&&draw->itemID!=(UINT)-1&&data&&draw->itemID<data->apps.size()){
            const auto& app=data->apps[draw->itemID];
            RECT r=draw->rcItem;
            if(draw->itemState&ODS_SELECTED){
                HBRUSH selectedBrush=CreateSolidBrush(C_ACCENT_DARK);
                FillRect(draw->hDC,&r,selectedBrush);
                DeleteObject(selectedBrush);
            }else FillRect(draw->hDC,&r,gFieldBrush);
            const int iconSize=DialogUi(data,16);
            const int iconX=r.left+DialogUi(data,6);
            if(app.icon)
                DrawIconEx(draw->hDC,iconX,r.top+(r.bottom-r.top-iconSize)/2,
                    app.icon,iconSize,iconSize,0,nullptr,DI_NORMAL);
            SetBkMode(draw->hDC,TRANSPARENT);SetTextColor(draw->hDC,C_TEXT);
            HFONT old=(HFONT)SelectObject(draw->hDC,data->responsive.font);
            RECT appRect{r.left+DialogUi(data,30),r.top,r.left+DialogUi(data,225),r.bottom};
            RECT exeRect{r.left+DialogUi(data,238),r.top,r.left+DialogUi(data,370),r.bottom};
            RECT pathRect{r.left+DialogUi(data,383),r.top,r.right-DialogUi(data,8),r.bottom};
            DrawTextW(draw->hDC,app.name.c_str(),-1,&appRect,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
            DrawTextW(draw->hDC,app.executable.c_str(),-1,&exeRect,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
            DrawTextW(draw->hDC,app.path.c_str(),-1,&pathRect,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
            SelectObject(draw->hDC,old);
            return TRUE;
        }
        if(draw->CtlID==IDC_RUNNING_HEADER&&data){
            RECT r=draw->rcItem;
            FillRect(draw->hDC,&r,gPanel2Brush);
            SetBkMode(draw->hDC,TRANSPARENT);SetTextColor(draw->hDC,C_TEXT);
            HFONT old=(HFONT)SelectObject(draw->hDC,data->responsive.fontBold);
            RECT app{r.left+DialogUi(data,10),r.top,r.left+DialogUi(data,230),r.bottom};
            RECT exe{r.left+DialogUi(data,238),r.top,r.left+DialogUi(data,375),r.bottom};
            RECT path{r.left+DialogUi(data,383),r.top,r.right-DialogUi(data,8),r.bottom};
            DrawTextW(draw->hDC,L"Application",-1,&app,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            DrawTextW(draw->hDC,L"Executable",-1,&exe,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            DrawTextW(draw->hDC,L"Path",-1,&path,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            const int glyphUnit=std::max(1,DialogUi(data,1));
            const int glyphWidth=glyphUnit*14;
            const int glyphRightPad=DialogUi(data,10);
            const int glyphY=(r.top+r.bottom)/2;
            const int appRight=r.left+DialogUi(data,230);
            const int exeRight=r.left+DialogUi(data,375);
            DrawSortIndicator(draw->hDC,appRight-glyphRightPad-glyphWidth,glyphY,glyphUnit,data->sortColumn==0?C_TEXT:C_MUTED,data->sortColumn==0,data->sortAscending);
            DrawSortIndicator(draw->hDC,exeRight-glyphRightPad-glyphWidth,glyphY,glyphUnit,data->sortColumn==1?C_TEXT:C_MUTED,data->sortColumn==1,data->sortAscending);
            DrawSortIndicator(draw->hDC,r.right-glyphRightPad-glyphWidth,glyphY,glyphUnit,data->sortColumn==2?C_TEXT:C_MUTED,data->sortColumn==2,data->sortAscending);
            const int dividerH=std::max(1,static_cast<int>(r.bottom-r.top)-DialogUi(data,12));
            Fill(draw->hDC,r.left+DialogUi(data,230),r.top+DialogUi(data,6),1,dividerH,C_BORDER);
            Fill(draw->hDC,r.left+DialogUi(data,375),r.top+DialogUi(data,6),1,dividerH,C_BORDER);
            Fill(draw->hDC,r.left,r.bottom-1,r.right-r.left,1,C_BORDER);
            SelectObject(draw->hDC,old);
            return TRUE;
        }
        if(draw->CtlID==IDC_RUNNING_REFRESH||draw->CtlID==IDC_RUNNING_SELECT||draw->CtlID==IDCANCEL){
            const bool down=(draw->itemState&ODS_SELECTED)!=0;
            const bool disabled=(draw->itemState&ODS_DISABLED)!=0;
            RECT r=draw->rcItem;
            FillRound(draw->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,7);
            SetBkMode(draw->hDC,TRANSPARENT);
            SetTextColor(draw->hDC,disabled?C_MUTED:C_TEXT);
            HFONT font=(data&&data->responsive.fontBold)?data->responsive.fontBold:gFontBold;
            HFONT old=(HFONT)SelectObject(draw->hDC,font);
            wchar_t label[128]{};GetWindowTextW(draw->hwndItem,label,128);
            DrawTextW(draw->hDC,label,-1,&r,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            SelectObject(draw->hDC,old);
            return TRUE;
        }
        break;
    }
    case WM_CTLCOLORSTATIC:{
        HDC dc=(HDC)wp;SetTextColor(dc,GetDlgCtrlID((HWND)lp)==IDC_RUNNING_EMPTY?C_MUTED:C_TEXT);
        SetBkColor(dc,GetDlgCtrlID((HWND)lp)==IDC_RUNNING_EMPTY?C_FIELD:C_BACK);SetBkMode(dc,TRANSPARENT);
        return (LRESULT)(GetDlgCtrlID((HWND)lp)==IDC_RUNNING_EMPTY?gFieldBrush:gBackBrush);
    }
    case WM_PAINT:{
        PAINTSTRUCT ps{};HDC dc=BeginPaint(w,&ps);
        RECT client{};GetClientRect(w,&client);
        const int margin=DialogUi(data,DIALOG_MARGIN);
        const int buttonY=client.bottom-margin-DialogUi(data,DIALOG_BUTTON_HEIGHT);
        RECT border{margin,margin,client.right-margin,buttonY-margin};
        HBRUSH brush=CreateSolidBrush(C_BORDER);FrameRect(dc,&border,brush);DeleteObject(brush);
        EndPaint(w,&ps);return 0;
    }
    case WM_CLOSE:DestroyWindow(w);return 0;
    case WM_DESTROY:
        if(data)for(auto& app:data->apps)if(app.icon)DestroyIcon(app.icon);
        if(data&&data->images){ImageList_Destroy(data->images);data->images=nullptr;}
        if(data)DestroyResponsiveDialogFonts(&data->responsive);
        return 0;
    }
    return DefWindowProcW(w,m,wp,lp);
}

bool SelectRunningApplication(HWND owner,std::wstring& selectedPath){
    static bool registered=false;
    if(!registered){
        WNDCLASSEXW wc{sizeof(wc)};wc.lpfnWndProc=RunningAppsDialogProc;wc.hInstance=gInst;
        wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherRunningApps";
        if(!RegisterClassExW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return false;
        registered=true;
    }

    RunningAppsDialogData data{};
    HWND previousFocus=GetFocus();
    const bool disableOwner=owner&&IsWindowEnabled(owner);
    if(disableOwner)EnableWindow(owner,FALSE);

    RECT ownerRect{};
    if(owner&&IsWindowVisible(owner))GetWindowRect(owner,&ownerRect);
    else SystemParametersInfoW(SPI_GETWORKAREA,0,&ownerRect,0);
    HMONITOR ownerMonitor=owner?MonitorFromWindow(owner,MONITOR_DEFAULTTONEAREST):MonitorFromRect(&ownerRect,MONITOR_DEFAULTTONEAREST);
    MONITORINFO mi{sizeof(mi)};GetMonitorInfoW(ownerMonitor,&mi);

    const int initialX=ownerRect.left+(ownerRect.right-ownerRect.left-720)/2;
    const int initialY=ownerRect.top+(ownerRect.bottom-ownerRect.top-390)/2;
    HWND dialog=CreateWindowExW(WS_EX_DLGMODALFRAME,L"NvProfileSwitcherRunningApps",
        L"Running applications",WS_CAPTION|WS_SYSMENU,initialX,initialY,720,390,owner,nullptr,gInst,&data);
    if(!dialog){if(disableOwner)EnableWindow(owner,TRUE);return false;}

    BOOL darkTitle=TRUE;DwmSetWindowAttribute(dialog,20,&darkTitle,sizeof(darkTitle));
    ApplyRunningAppsResponsiveLayout(dialog,&data,GetDpiForWindow(dialog),nullptr);

    RECT wr{};GetWindowRect(dialog,&wr);
    const int ww=wr.right-wr.left,wh=wr.bottom-wr.top;
    const int x=mi.rcWork.left+(mi.rcWork.right-mi.rcWork.left-ww)/2;
    const int y=mi.rcWork.top+(mi.rcWork.bottom-mi.rcWork.top-wh)/2;
    SetWindowPos(dialog,HWND_TOP,x,y,0,0,SWP_NOSIZE|SWP_SHOWWINDOW);
    SetForegroundWindow(dialog);

    MSG msg{};
    while(IsWindow(dialog)&&GetMessageW(&msg,nullptr,0,0)>0){
        if(!IsDialogMessageW(dialog,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}
    }
    if(disableOwner){EnableWindow(owner,TRUE);if(previousFocus&&IsWindow(previousFocus))SetFocus(previousFocus);else SetFocus(owner);SetForegroundWindow(owner);}
    selectedPath=std::move(data.selectedPath);
    return !selectedPath.empty();
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
}
void ToggleMainVisibility(){
    if(IsWindowVisible(gWnd)){HideExecutableTooltip();DiscardPreview();ShowWindow(gWnd,SW_HIDE);SetTrayIconVisible(true);}
    else ShowMain();
}
void ToggleWindowsOverride(){
    DiscardPreview();
    gOverrideMode=gOverrideMode==OverrideMode::Windows
        ?OverrideMode::Automatic:OverrideMode::Windows;
    gActive.clear();
    CheckProcesses();
}
void ResumeAutomaticSwitching(){
    DiscardPreview();
    gOverrideMode=OverrideMode::Automatic;
    gActive.clear();
    CheckProcesses();
}
void ToggleProfileOverride(size_t profileIndex){
    const auto profiles=SwitchingProfiles();
    const std::optional<size_t> current=gOverrideMode==OverrideMode::Profile
        ?std::optional<size_t>(gOverrideProfileIndex):std::nullopt;
    const auto next=nvps::ToggleProfileOverride(profiles,current,profileIndex);
    if(next==current&&profileIndex>=profiles.size())return;
    if(next==current&&profileIndex<profiles.size()&&!profiles[profileIndex].enabled)return;
    DiscardPreview();
    if(next){
        gOverrideMode=OverrideMode::Profile;
        gOverrideProfileIndex=*next;
    }else gOverrideMode=OverrideMode::Automatic;
    gActive.clear();
    CheckProcesses();
}
void RestoreDesktop(){RestoreAllDesktopProfiles();gActive=L"Windows";InvalidateFooter();}
bool ExportConfiguration(HWND owner){
    Save();
    wchar_t path[MAX_PATH]=L"NvProfileSwitcher-Config.json";
    OPENFILENAMEW ofn{sizeof(ofn)};
    ofn.hwndOwner=owner;
    ofn.lpstrFilter=L"JSON files (*.json)\0*.json\0All files\0*.*\0";
    ofn.lpstrFile=path;
    ofn.nMaxFile=MAX_PATH;
    ofn.lpstrDefExt=L"json";
    ofn.lpstrTitle=L"Export configuration";
    ofn.Flags=OFN_OVERWRITEPROMPT|OFN_PATHMUSTEXIST;
    if(!GetSaveFileNameW(&ofn)) return false;
    if(CopyFileW(AppDataFile().c_str(),path,FALSE)) return true;
    ShowAppMessage(L"Export configuration",L"The configuration could not be exported.");
    return false;
}

bool ImportConfiguration(HWND owner){
    wchar_t path[MAX_PATH]{};
    OPENFILENAMEW ofn{sizeof(ofn)};
    ofn.hwndOwner=owner;
    ofn.lpstrFilter=L"JSON files (*.json)\0*.json\0All files\0*.*\0";
    ofn.lpstrFile=path;
    ofn.nMaxFile=MAX_PATH;
    ofn.lpstrDefExt=L"json";
    ofn.lpstrTitle=L"Import configuration";
    ofn.Flags=OFN_FILEMUSTEXIST|OFN_PATHMUSTEXIST;
    if(!GetOpenFileNameW(&ofn)) return false;

    std::string json=ReadAll(path);
    if(!ValidateConfigurationJson(json)){
        ShowAppMessage(L"Invalid configuration file",L"The selected file is not a valid NvProfileSwitcher configuration.");
        return false;
    }

    UnregisterConfiguredHotkeys();
    DiscardPreview();
    if(!CopyFileW(path,AppDataFile().c_str(),FALSE)){
        RegisterConfiguredHotkeys();
        ShowAppMessage(L"Import configuration",L"The configuration could not be imported.");
        return false;
    }

    gSettings=Settings{};
    Load();
    gSettings.desktop.name=L"Windows";
    for(const auto& d:gDisplays) EnsureDesktopProfile(d.displayName,d.monitorId);
    EnsureAllApplicationDisplayProfiles();
    SetStartup(gSettings.startWindows);
    SetHotkeyControl(IDC_HOTKEY_SHOW,gSettings.showHideHotkey);
    SetHotkeyControl(IDC_HOTKEY_OVERRIDE,gSettings.windowsOverrideHotkey);
    SetHotkeyControl(IDC_HOTKEY_RESUME,gSettings.resumeAutomaticHotkey);
    SendMessageW(H(IDC_STARTWIN),BM_SETCHECK,gSettings.startWindows?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_STARTMIN),BM_SETCHECK,gSettings.startMinimized?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_MINTRAY),BM_SETCHECK,gSettings.minimizeToTray?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_CHECKUPDATES),BM_SETCHECK,gSettings.checkUpdates?BST_CHECKED:BST_UNCHECKED,0);
    gOverrideMode=OverrideMode::Automatic;
    gOverrideProfileIndex=0;
    gSelected=0;
    RegisterConfiguredHotkeys();
    RefreshList();
    LoadSelected();
    gActive.clear();
    CheckProcesses();
    return true;
}


enum {IDC_CONFIG_IMPORT=5301,IDC_CONFIG_EXPORT};

LRESULT CALLBACK ConfigurationPopupProc(HWND w,UINT m,WPARAM wp,LPARAM lp){
    switch(m){
    case WM_CREATE:{
        RECT c{};GetClientRect(w,&c);
        const int margin=8,gap=6,buttonH=38;
        HWND importButton=CreateWindowExW(0,L"BUTTON",L"Import",
            WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            margin,margin,c.right-margin*2,buttonH,w,(HMENU)IDC_CONFIG_IMPORT,gInst,nullptr);
        HWND exportButton=CreateWindowExW(0,L"BUTTON",L"Export",
            WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,
            margin,margin+buttonH+gap,c.right-margin*2,buttonH,w,(HMENU)IDC_CONFIG_EXPORT,gInst,nullptr);
        SendMessageW(importButton,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        SendMessageW(exportButton,WM_SETFONT,(WPARAM)gFontBold,TRUE);
        SetFocus(importButton);
        return 0;
    }
    case WM_DRAWITEM:{
        auto* d=(DRAWITEMSTRUCT*)lp;
        if(d->CtlID==IDC_CONFIG_IMPORT||d->CtlID==IDC_CONFIG_EXPORT){
            const bool down=(d->itemState&ODS_SELECTED)!=0;
            RECT r=d->rcItem;
            FillRound(d->hDC,r,down?C_ACCENT_DARK:C_PANEL2,C_BORDER,7);
            SetBkMode(d->hDC,TRANSPARENT);SetTextColor(d->hDC,C_TEXT);
            HFONT old=(HFONT)SelectObject(d->hDC,gFontBold);
            const wchar_t* label=d->CtlID==IDC_CONFIG_IMPORT?L"Import":L"Export";
            DrawTextW(d->hDC,label,-1,&r,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
            SelectObject(d->hDC,old);
            return TRUE;
        }
        break;
    }
    case WM_COMMAND:
        if(LOWORD(wp)==IDC_CONFIG_IMPORT){DestroyWindow(w);ImportConfiguration(gWnd);return 0;}
        if(LOWORD(wp)==IDC_CONFIG_EXPORT){DestroyWindow(w);ExportConfiguration(gWnd);return 0;}
        break;
    case WM_ACTIVATE:
        if(LOWORD(wp)==WA_INACTIVE){DestroyWindow(w);return 0;}
        break;
    case WM_KEYDOWN:
        if(wp==VK_ESCAPE){DestroyWindow(w);return 0;}
        break;
    case WM_PAINT:{
        PAINTSTRUCT ps{};HDC dc=BeginPaint(w,&ps);RECT r{};GetClientRect(w,&r);
        FillRect(dc,&r,gBackBrush);HBRUSH border=CreateSolidBrush(C_BORDER);FrameRect(dc,&r,border);DeleteObject(border);
        EndPaint(w,&ps);return 0;
    }
    }
    return DefWindowProcW(w,m,wp,lp);
}

void ShowConfigurationPopup(HWND owner){
    static bool registered=false;
    if(!registered){
        WNDCLASSEXW wc{sizeof(wc)};
        wc.lpfnWndProc=ConfigurationPopupProc;wc.hInstance=gInst;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);
        wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherConfigurationPopup";
        if(!RegisterClassExW(&wc)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return;
        registered=true;
    }
    HWND anchor=GetDlgItem(owner,IDC_EXPORT_PROFILES);
    if(!anchor)return;
    RECT br{};GetWindowRect(anchor,&br);
    const int width=230,height=98;
    int x=br.right-width;
    int y=br.top-height-6;
    HWND popup=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_TOPMOST,L"NvProfileSwitcherConfigurationPopup",L"",
        WS_POPUP,x,y,width,height,owner,nullptr,gInst,nullptr);
    if(!popup)return;
    ShowWindow(popup,SW_SHOWNORMAL);UpdateWindow(popup);SetForegroundWindow(popup);
}

LRESULT CALLBACK Proc(HWND w,UINT m,WPARAM wp,LPARAM lp){switch(m){case WM_SHOW_EXISTING_INSTANCE:ShowMain();return 0;case WM_UPDATE_AVAILABLE:ShowUpdateAvailable((UpdateInfo*)lp);return 0;case WM_SHOW_APP_MESSAGE:{auto* data=(AppMessageData*)lp;if(data){std::wstring title=data->title,text=data->text;delete data;ShowAppMessage(title,text);}return 0;}case WM_CREATE:gWnd=w;BuildControls();RegisterConfiguredHotkeys();RefreshList();LoadSelected();SetTimer(w,1,250,nullptr);return 0;case WM_DPICHANGED:{
    // Windows has already selected the new DPI for this HWND. Apply the
    // responsive size immediately while that DPI is authoritative. Deferring
    // this message left the top-level HWND at the previous monitor's physical
    // size while only the child/layout scale changed, producing the large
    // unused background seen on high-DPI displays.
    RECT target=*reinterpret_cast<RECT*>(lp);
    HMONITOR monitor=MonitorFromRect(&target,MONITOR_DEFAULTTONEAREST);
    gPendingResponsiveRect=false;
    KillTimer(w,2);
    ApplyResponsiveLayout(w,monitor,&target,false);
    return 0;
}case WM_EXITSIZEMOVE:return 0;case WM_HOTKEY:if(wp==ID_HOTKEY_SHOW_HIDE){ToggleMainVisibility();return 0;}if(wp==ID_HOTKEY_WINDOWS_OVERRIDE){ToggleWindowsOverride();return 0;}if(wp==ID_HOTKEY_RESUME_AUTOMATIC){ResumeAutomaticSwitching();return 0;}if(wp>=ID_HOTKEY_PROFILE_BASE&&wp<ID_HOTKEY_PROFILE_BASE+(WPARAM)gSettings.profiles.size()){ToggleProfileOverride((size_t)(wp-ID_HOTKEY_PROFILE_BASE));return 0;}break;case WM_ACTIVATE:
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
    // ApplyResponsiveLayout can issue several SetWindowPos calls while correcting
    // the outer/client size. Those calls synchronously generate WM_SIZE; doing a
    // full child layout for every correction is redundant and causes the visible
    // hitch when crossing monitors with different DPI.
    if(gApplyingResponsiveLayout)return 0;
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
case WM_CTLCOLORSTATIC:{HDC dc=(HDC)wp;
#if NVPS_DEV_BUILD
    SetTextColor(dc,GetDlgCtrlID((HWND)lp)==IDC_CHECKUPDATES_LABEL?C_MUTED:C_TEXT);
#else
    SetTextColor(dc,C_TEXT);
#endif
    SetBkColor(dc,C_PANEL);SetBkMode(dc,TRANSPARENT);return (LRESULT)gPanelBrush;}case WM_CTLCOLOREDIT:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_FIELD);return (LRESULT)gFieldBrush;}case WM_CTLCOLORBTN:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL);return (LRESULT)gPanelBrush;}case WM_DRAWITEM:{
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

    if(d->CtlID==IDC_ADD||d->CtlID==IDC_REMOVE||d->CtlID==IDC_PROFILE_UP||d->CtlID==IDC_PROFILE_DOWN){
        DrawProfileHeaderButton(d);return TRUE;
    }
    if(d->CtlID==IDC_SAVE||d->CtlID==IDC_DEFAULTS||d->CtlID==IDC_BROWSE||d->CtlID==IDC_RUNNING_APPS||d->CtlID==IDC_HOTKEY_SHOW_CLEAR||d->CtlID==IDC_HOTKEY_OVERRIDE_CLEAR||d->CtlID==IDC_HOTKEY_RESUME_CLEAR||d->CtlID==IDC_PROFILE_HOTKEY_CLEAR||d->CtlID==IDC_EXPORT_PROFILES||d->CtlID==IDC_MANAGE_DISPLAYS){
        DrawOwnerButton(d);return TRUE;
    }
    if(d->CtlID==IDC_FOOT_GITHUB||d->CtlID==IDC_FOOT_SUPPORT||d->CtlID==IDC_FOOT_ABOUT){
        DrawFooterLink(d);return TRUE;
    }

    if(d->CtlID==IDC_LIST&&d->itemID!=(UINT)-1){
        const bool selected=(d->itemState&ODS_SELECTED)!=0;
        const bool hovered=(int)d->itemID==gProfileHoverItem;
        const bool pressed=(int)d->itemID==gProfilePressedItem;

        RECT row=d->rcItem;
        row.left+=5;
        row.right-=5;
        row.top+=5;
        row.bottom-=5;

        if(selected){
            FillRound(d->hDC,row,pressed?RGB(11,28,16):RGB(15,34,20),
                pressed?C_ACCENT2:C_ACCENT,9);
        }else if(pressed){
            FillRound(d->hDC,row,RGB(20,25,30),RGB(67,77,86),9);
        }else if(hovered){
            FillRound(d->hDC,row,RGB(25,31,36),RGB(54,63,71),9);
        }else{
            FillRound(d->hDC,row,C_PANEL,C_PANEL,9);
        }

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

        SetBkMode(d->hDC,TRANSPARENT);
        SetTextColor(d->hDC,C_TEXT);
        SelectObject(d->hDC,gFontBold);
        const wchar_t* title=desktop?L"Windows":p->name.c_str();
        if(!desktop&&p->hotkey){
            RECT titleRect{x+58,d->rcItem.top+9,d->rcItem.right-14,d->rcItem.top+34};
            DrawTextW(d->hDC,title,-1,&titleRect,
                DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
            std::wstring shortcut=HotkeyDisplayText(p->hotkey);
            RECT shortcutRect{x+58,d->rcItem.top+34,d->rcItem.right-14,d->rcItem.bottom-7};
            SetTextColor(d->hDC,C_MUTED);
            SelectObject(d->hDC,gFontSmall);
            DrawTextW(d->hDC,shortcut.c_str(),-1,&shortcutRect,
                DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
        }else{
            RECT titleRect{x+58,d->rcItem.top,d->rcItem.right-14,d->rcItem.bottom};
            DrawTextW(d->hDC,title,-1,&titleRect,
                DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS|DT_NOPREFIX);
        }

        if(!selected){
            Fill(d->hDC,d->rcItem.left+12,d->rcItem.bottom-1,
                 d->rcItem.right-d->rcItem.left-24,1,C_BORDER);
        }
        return TRUE;
    }
    break;
}case WM_HSCROLL:UpdateSliderLabels();if((HWND)lp)InvalidateRect((HWND)lp,nullptr,FALSE);RequestPreview();return 0;
case WM_DISPLAYCHANGE:
    // Display topology may still be changing. Restart the settle timer instead
    // of applying the layout from this notification.
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

        RECT targetRect{};
        const RECT* suggested=nullptr;
        HMONITOR monitor=nullptr;

        if(gPendingResponsiveRect){
            targetRect=gResponsiveSuggestedRect;
            suggested=&targetRect;
            monitor=MonitorFromRect(&targetRect,MONITOR_DEFAULTTONEAREST);
        }else{
            // Preserve the current position for topology-only changes. Passing
            // the current rect prevents ApplyResponsiveLayout from recentering
            // the window just because a display/device notification fired.
            GetWindowRect(w,&targetRect);
            suggested=&targetRect;
            monitor=MonitorFromRect(&targetRect,MONITOR_DEFAULTTONEAREST);
        }

        gPendingResponsiveRect=false;
        ApplyResponsiveLayout(w,monitor,suggested,false);
        return 0;
    }
#if NVPS_DEV_BUILD
    if(wp==3){
        HideUpdateCheckTooltip();
        return 0;
    }
#endif
    return 0;
case WM_COMMAND:{int id=LOWORD(wp);if(id==IDC_PROFILE_UP){MoveSelectedProfile(-1);return 0;}if(id==IDC_PROFILE_DOWN){MoveSelectedProfile(1);return 0;}if(id==IDC_HOTKEY_SHOW&&HIWORD(wp)==EN_CHANGE){UpdateConfiguredHotkey(IDC_HOTKEY_SHOW,gSettings.showHideHotkey);return 0;}if(id==IDC_HOTKEY_OVERRIDE&&HIWORD(wp)==EN_CHANGE){UpdateConfiguredHotkey(IDC_HOTKEY_OVERRIDE,gSettings.windowsOverrideHotkey);return 0;}if(id==IDC_HOTKEY_RESUME&&HIWORD(wp)==EN_CHANGE){UpdateConfiguredHotkey(IDC_HOTKEY_RESUME,gSettings.resumeAutomaticHotkey);return 0;}if(id==IDC_PROFILE_HOTKEY&&HIWORD(wp)==EN_CHANGE&&!IsDesktopSelected()){if(auto*p=SelectedProfile()){UpdateConfiguredHotkey(IDC_PROFILE_HOTKEY,p->hotkey);InvalidateRect(H(IDC_LIST),nullptr,TRUE);}return 0;}if(id==IDC_LIST&&HIWORD(wp)==LBN_SELCHANGE){HideExecutableTooltip();DiscardPreview();LoadSelected();return 0;}if(id==IDC_DISPLAY&&HIWORD(wp)==CBN_SELCHANGE){DiscardPreview();int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);if(ds>=0&&ds<(int)gDisplays.size()){if(IsDesktopSelected()){auto*p=EnsureDesktopProfile(gDisplays[ds].displayName,gDisplays[ds].monitorId);LoadValuesToSliders(ValuesFromFlatProfile(*p));}else{auto*p=SelectedProfile();if(p){LoadValuesToSliders(*EnsureApplicationValuesForDisplay(*p,gDisplays[ds].displayName,gDisplays[ds].monitorId));}}}return 0;}if(id==IDC_RUNNING_APPS){std::wstring path;if(SelectRunningApplication(w,path)){Txt(IDC_EXE,path);InvalidateRect(H(IDC_EXE),nullptr,TRUE);auto* p=SelectedProfile();if(p&&!IsDesktopSelected()){p->exePath=path;InvalidateRect(H(IDC_LIST),nullptr,TRUE);}}return 0;}switch(id){case IDC_HOTKEY_SHOW_CLEAR:ClearConfiguredHotkey(IDC_HOTKEY_SHOW,gSettings.showHideHotkey);break;case IDC_HOTKEY_OVERRIDE_CLEAR:ClearConfiguredHotkey(IDC_HOTKEY_OVERRIDE,gSettings.windowsOverrideHotkey);break;case IDC_HOTKEY_RESUME_CLEAR:ClearConfiguredHotkey(IDC_HOTKEY_RESUME,gSettings.resumeAutomaticHotkey);break;case IDC_PROFILE_HOTKEY_CLEAR:if(!IsDesktopSelected()){if(auto*p=SelectedProfile()){ClearConfiguredHotkey(IDC_PROFILE_HOTKEY,p->hotkey);InvalidateRect(H(IDC_LIST),nullptr,TRUE);}}break;case IDC_BROWSE:{OPENFILENAMEW o{sizeof(o)};wchar_t f[MAX_PATH]{};o.hwndOwner=w;o.lpstrFilter=L"Executables (*.exe)\0*.exe\0All files\0*.*\0";o.lpstrFile=f;o.nMaxFile=MAX_PATH;o.Flags=OFN_FILEMUSTEXIST;if(GetOpenFileNameW(&o)){Txt(IDC_EXE,f);InvalidateRect(H(IDC_EXE),nullptr,TRUE);auto* p=SelectedProfile();if(p&&!IsDesktopSelected()){p->exePath=f;InvalidateRect(H(IDC_LIST),nullptr,TRUE);}}break;}case IDC_MANAGE_DISPLAYS:ShowManageDisplaysDialog(w);break;case IDC_EXPORT_PROFILES:ShowConfigurationPopup(w);break;case IDC_DEFAULTS:ResetSlidersToDefaults();break;case IDC_SAVE:SaveSelected();break;case IDC_ADD:{ApplicationProfile np{};if(!gDisplays.empty()){for(const auto&d:gDisplays)np.displayProfiles.push_back(ApplicationDefaultsForDisplay(d.displayName,d.monitorId));}gSettings.profiles.push_back(np);gSelected=(int)gSettings.profiles.size();Save();RefreshList();LoadSelected();break;}case IDC_REMOVE:if(gSelected>0&&gSelected<=(int)gSettings.profiles.size()){size_t removed=(size_t)(gSelected-1);UnregisterConfiguredHotkeys();if(gOverrideMode==OverrideMode::Profile){if(gOverrideProfileIndex==removed)gOverrideMode=OverrideMode::Automatic;else if(gOverrideProfileIndex>removed)--gOverrideProfileIndex;}gSettings.profiles.erase(gSettings.profiles.begin()+removed);RegisterConfiguredHotkeys();gSelected=gSettings.profiles.empty()?0:std::min(gSelected,(int)gSettings.profiles.size());Save();RefreshList();LoadSelected();gActive.clear();CheckProcesses();}break;case IDC_STARTWIN:gSettings.startWindows=SendMessageW(H(IDC_STARTWIN),BM_GETCHECK,0,0)==BST_CHECKED;SetStartup(gSettings.startWindows);Save();break;case IDC_STARTMIN:gSettings.startMinimized=SendMessageW(H(IDC_STARTMIN),BM_GETCHECK,0,0)==BST_CHECKED;Save();break;case IDC_MINTRAY:
    gSettings.minimizeToTray=SendMessageW(H(IDC_MINTRAY),BM_GETCHECK,0,0)==BST_CHECKED;
    if(!gSettings.minimizeToTray)
        SetTrayIconVisible(false);
    Save();
    break;case IDC_CHECKUPDATES:
#if NVPS_DEV_BUILD
    SendMessageW(H(IDC_CHECKUPDATES),BM_SETCHECK,BST_UNCHECKED,0);
    ShowUpdateCheckTooltip();
#else
    gSettings.checkUpdates=SendMessageW(H(IDC_CHECKUPDATES),BM_GETCHECK,0,0)==BST_CHECKED;Save();
#endif
    break;case IDC_FOOT_GITHUB:ShellExecuteW(w,L"open",APP_URL,nullptr,nullptr,SW_SHOWNORMAL);break;
case IDC_FOOT_SUPPORT:ShellExecuteW(w,L"open",SUPPORT_URL,nullptr,nullptr,SW_SHOWNORMAL);break;
case IDC_FOOT_ABOUT:ShowAbout();break;
case ID_TRAY_OPEN:ShowMain();break;case ID_TRAY_CHECK_UPDATE:{if(HANDLE h=CreateThread(nullptr,0,UpdateCheckThread,(LPVOID)1,0,nullptr))CloseHandle(h);break;}case ID_TRAY_ABOUT:ShowAbout();break;case ID_TRAY_EXIT:DestroyWindow(w);break;}return 0;}case WM_CLOSE:
    DestroyWindow(w);
    return 0;case WM_TRAY:if(lp==WM_LBUTTONDBLCLK){ShowMain();return 0;}if(lp==WM_RBUTTONUP||lp==WM_CONTEXTMENU){POINT p;GetCursorPos(&p);SetForegroundWindow(w);TrackPopupMenu(gTrayMenu,TPM_RIGHTBUTTON,p.x,p.y,0,w,nullptr);return 0;}break;case WM_DESTROY:UnregisterConfiguredHotkeys();KillTimer(w,1);KillTimer(w,2);SetTrayIconVisible(false);if(pUnload)pUnload();if(gNv)FreeLibrary(gNv);PostQuitMessage(0);return 0;}return DefWindowProcW(w,m,wp,lp);}

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
INITCOMMONCONTROLSEX ic{sizeof(ic),ICC_BAR_CLASSES|ICC_STANDARD_CLASSES|ICC_HOTKEY_CLASS};InitCommonControlsEx(&ic);Load();gSettings.desktop.name=L"Windows";gBackBrush=CreateSolidBrush(C_BACK);gPanelBrush=CreateSolidBrush(C_PANEL);gPanel2Brush=CreateSolidBrush(C_PANEL2);gFieldBrush=CreateSolidBrush(C_FIELD);
RECT startupWork{};
SystemParametersInfoW(SPI_GETWORKAREA,0,&startupWork,0);
const int startupWorkW=startupWork.right-startupWork.left;
const int startupWorkH=startupWork.bottom-startupWork.top;
// Keep startup in design coordinates. The authoritative fit is calculated
// once the real HWND exists, using that window's actual monitor/non-client metrics.
gUiScale=1.0;

const wchar_t* uiFamily=FontFamilyAvailable(L"Bahnschrift")?L"Bahnschrift":L"Segoe UI";
gBaseFont=CreateUiFont(-15,FW_NORMAL,uiFamily);
gBaseFontBold=CreateUiFont(-15,FW_SEMIBOLD,uiFamily);
gBaseFontPanelTitle=CreateUiFont(-18,FW_SEMIBOLD,uiFamily);
gBaseFontTitle=CreateUiFont(-24,FW_BOLD,uiFamily);
gBaseFontSmall=CreateUiFont(-13,FW_NORMAL,uiFamily);
gBaseFontHeaderButton=CreateUiFont(-14,FW_SEMIBOLD,uiFamily);
gBaseIconFont=CreateFontW(-18,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe MDL2 Assets");
gFont=ScaledFontFromBase(gBaseFont,gUiScale);
gFontBold=ScaledFontFromBase(gBaseFontBold,gUiScale);
gFontPanelTitle=ScaledFontFromBase(gBaseFontPanelTitle,gUiScale);
gFontTitle=ScaledFontFromBase(gBaseFontTitle,gUiScale);
gFontSmall=ScaledFontFromBase(gBaseFontSmall,gUiScale);
gFontHeaderButton=ScaledFontFromBase(gBaseFontHeaderButton,gUiScale);
gIconFont=ScaledFontFromBase(gBaseIconFont,gUiScale);gIcon=LoadIconW(h,MAKEINTRESOURCEW(IDI_APPICON_TRANSPARENT));WNDCLASSEXW wc{sizeof(wc)};wc.style=CS_HREDRAW|CS_VREDRAW;wc.lpfnWndProc=Proc;wc.hInstance=h;wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherNative";RegisterClassExW(&wc);
DWORD mainStyle=WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX;
RECT initialClient{0,0,Ui(MAIN_BASE_CLIENT_WIDTH),Ui(MAIN_BASE_CLIENT_HEIGHT)};
AdjustWindowRectEx(&initialClient,mainStyle,FALSE,0);
const int initialW=initialClient.right-initialClient.left;
const int initialH=initialClient.bottom-initialClient.top;
const int initialX=startupWork.left+(startupWorkW-initialW)/2;
const int initialY=startupWork.top+(startupWorkH-initialH)/2;
gWnd=CreateWindowExW(0,wc.lpszClassName,L"NvProfileSwitcher",mainStyle,initialX,initialY,initialW,initialH,nullptr,nullptr,h,nullptr);
ApplyResponsiveLayout(gWnd,MonitorFromWindow(gWnd,MONITOR_DEFAULTTONEAREST),nullptr,true);
BOOL darkTitle=TRUE;DwmSetWindowAttribute(gWnd,20,&darkTitle,sizeof(darkTitle));

SetWindowLongPtrW(gWnd,GWLP_USERDATA,0);gTrayMenu=CreatePopupMenu();AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_OPEN,L"Open NvProfileSwitcher");AppendMenuW(gTrayMenu,MF_SEPARATOR,0,nullptr);AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_CHECK_UPDATE,L"Check for updates");AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_ABOUT,L"About NvProfileSwitcher");AppendMenuW(gTrayMenu,MF_SEPARATOR,0,nullptr);AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_EXIT,L"Exit");gNid.cbSize=sizeof(gNid);gNid.hWnd=gWnd;gNid.uID=1;gNid.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;gNid.uCallbackMessage=WM_TRAY;gNid.hIcon=gIcon;wcscpy_s(gNid.szTip,L"NvProfileSwitcher");gStatusOk=InitNv();if(gStatusOk){for(const auto&d:gDisplays)EnsureDesktopProfile(d.displayName,d.monitorId);EnsureAllApplicationDisplayProfiles();Save();if(auto* p=SelectedProfile())RefreshDisplayCombo(*p);RestoreAllDesktopProfiles();LoadSelected();}gActive=L"Windows";bool min=(wcsstr(cmd,L"--minimized")!=nullptr);
if(min) SetTrayIconVisible(true);
ShowWindow(gWnd,min?SW_HIDE:SW_SHOW);
UpdateWindow(gWnd);
#if !NVPS_DEV_BUILD
if(gSettings.checkUpdates){if(HANDLE h=CreateThread(nullptr,0,UpdateCheckThread,nullptr,0,nullptr))CloseHandle(h);}
#endif
MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}DeleteObject(gFont);DeleteObject(gFontBold);DeleteObject(gFontPanelTitle);DeleteObject(gFontTitle);DeleteObject(gFontSmall);DeleteObject(gFontHeaderButton);DeleteObject(gIconFont);DeleteObject(gBackBrush);DeleteObject(gPanelBrush);DeleteObject(gPanel2Brush);DeleteObject(gFieldBrush);
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
