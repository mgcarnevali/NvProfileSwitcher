#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>
#include <commdlg.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <shlwapi.h>
#include <uxtheme.h>
#include <dwmapi.h>
#include <string>
#include <vector>
#include <fstream>
#include <sstream>
#include <regex>
#include <algorithm>
#include <cmath>
#include "resource.h"

#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "uxtheme.lib")
#pragma comment(lib, "dwmapi.lib")

struct GameProfile {
    std::wstring name=L"New Profile";
    std::wstring exePath;
    std::wstring displayName; // empty = primary NVIDIA display
    int vibrance=50;
    double brightness=100.0, contrast=100.0, gamma=1.00;
    bool enabled=true;
};
struct Settings {
    GameProfile desktop{L"Windows",L"",L"",50,100.0,100.0,1.00,true};
    std::vector<GameProfile> profiles;
    bool startWindows=false, startMinimized=false;
};

constexpr COLORREF C_BACK=RGB(10,13,16), C_PANEL=RGB(18,22,26), C_PANEL2=RGB(24,29,34), C_FIELD=RGB(20,24,28), C_BORDER=RGB(45,52,59);
constexpr COLORREF C_TEXT=RGB(241,244,247), C_MUTED=RGB(151,161,171), C_ACCENT=RGB(82,214,39), C_ACCENT2=RGB(43,164,22), C_ACCENT_DARK=RGB(24,50,28), C_DANGER=RGB(232,75,75);
constexpr COLORREF C_TRACK=RGB(61,67,73), C_WINBLUE=RGB(0,120,215);
constexpr UINT WM_TRAY=WM_APP+1;
enum {IDC_LIST=1001,IDC_NAME,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_DISPLAY,IDC_LBL_DISPLAY,IDC_VIB,IDC_BRI,IDC_CON,IDC_GAM,IDC_SAVE,IDC_APPLY,IDC_ADD,IDC_REMOVE,IDC_RESTORE,IDC_STARTWIN,IDC_STARTMIN,IDC_VALVIB,IDC_VALBRI,IDC_VALCON,IDC_VALGAM,IDC_LBL_NAME,IDC_LBL_EXE,IDC_LBL_ENABLED,IDC_LBL_VIB,IDC_LBL_BRI,IDC_LBL_CON,IDC_LBL_GAM};
enum {ID_TRAY_OPEN=2001,ID_TRAY_RESTORE,ID_TRAY_PAUSE,ID_TRAY_EXIT};

HINSTANCE gInst{}; HWND gWnd{}; HFONT gFont{},gFontBold{},gFontTitle{},gIconFont{}; HBRUSH gBackBrush{},gPanelBrush{},gPanel2Brush{},gFieldBrush{}; HICON gIcon{};
Settings gSettings; int gSelected=-1; bool gPaused=false,gReallyExit=false; std::wstring gActive=L"Windows", gStatus=L"Not initialized"; bool gStatusOk=false;
NOTIFYICONDATAW gNid{}; HMENU gTrayMenu{};

using NvQueryInterface=void* (__cdecl*)(unsigned int);
using NvInit=int (__cdecl*)(); using NvUnload=int (__cdecl*)(); using NvEnumDisplay=int (__cdecl*)(int,void**);
struct DVCINFOEX { unsigned int version; int currentLevel,minLevel,maxLevel,defaultLevel; };
using NvGetDVC=int (__cdecl*)(void*,unsigned int,DVCINFOEX*); using NvSetDVC=int (__cdecl*)(void*,unsigned int,DVCINFOEX*);
#pragma pack(push,1)
struct NV_GAMMA_CORRECTION_EX {
    unsigned int version;
    float gammaRamp[1024*3];
    unsigned int unknown;
};
#pragma pack(pop)
using NvGetPrimaryDisplayId=int (__cdecl*)(unsigned int*);
using NvSetTargetGamma=int (__cdecl*)(unsigned int,NV_GAMMA_CORRECTION_EX*);
using NvGetAssociatedDisplayHandle=int (__cdecl*)(const char*,void**);
using NvGetDisplayIdByName=int (__cdecl*)(const char*,unsigned int*);

struct DisplayTarget {
    std::wstring gdiName;
    std::wstring label;
    void* handle{};
    unsigned int displayId{};
    bool primary{};
};

HMODULE gNv{}; NvUnload pUnload{}; NvGetDVC pGetDvc{}; NvSetDVC pSetDvc{};
NvGetPrimaryDisplayId pGetPrimaryDisplayId{}; NvSetTargetGamma pSetTargetGamma{};
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
GameProfile ParseProfile(const std::string&o){
    GameProfile p;
    p.name=Unescape(FieldS(o,"Name","New Profile"));
    p.exePath=Unescape(FieldS(o,"ExePath"));
    p.displayName=Unescape(FieldS(o,"DisplayName",""));
    p.vibrance=(int)FieldN(o,"DigitalVibrance",50);
    p.brightness=FieldN(o,"Brightness",100.0);
    p.contrast=FieldN(o,"Contrast",100.0);
    p.gamma=FieldN(o,"Gamma",1.0);
    p.enabled=FieldB(o,"Enabled",true);

    // v0.3.14 and older stored Brightness/Contrast normalized as 0..1.
    // Convert those existing profiles once in memory to NVIDIA App's 80..120 scale.
    if(p.brightness>=0.0 && p.brightness<=1.0) p.brightness=80.0+p.brightness*40.0;
    if(p.contrast>=0.0 && p.contrast<=1.0) p.contrast=80.0+p.contrast*40.0;
    return p;
}
void Save(){ std::ofstream f(AppDataFile(),std::ios::binary|std::ios::trunc); auto dump=[&](const GameProfile&p,int ind){ std::string sp(ind,' '); f<<sp<<"{\n"<<sp<<"  \"Name\": \""<<Escape(p.name)<<"\",\n"<<sp<<"  \"ExePath\": \""<<Escape(p.exePath)<<"\",\n"<<sp<<"  \"DisplayName\": \""<<Escape(p.displayName)<<"\",\n"<<sp<<"  \"DigitalVibrance\": "<<p.vibrance<<",\n"<<sp<<"  \"Brightness\": "<<p.brightness<<",\n"<<sp<<"  \"Contrast\": "<<p.contrast<<",\n"<<sp<<"  \"Gamma\": "<<p.gamma<<",\n"<<sp<<"  \"Enabled\": "<<(p.enabled?"true":"false")<<"\n"<<sp<<"}";}; f<<"{\n  \"DesktopProfile\": "; dump(gSettings.desktop,2); f<<",\n  \"Profiles\": [\n"; for(size_t i=0;i<gSettings.profiles.size();++i){ dump(gSettings.profiles[i],4); if(i+1<gSettings.profiles.size())f<<","; f<<"\n";} f<<"  ],\n  \"StartWithWindows\": "<<(gSettings.startWindows?"true":"false")<<",\n  \"StartMinimized\": "<<(gSettings.startMinimized?"true":"false")<<"\n}\n"; }
void Load(){ std::string s=ReadAll(AppDataFile()); if(s.empty()){ gSettings.profiles.push_back({L"Escape from Tarkov",L"",L"",90,100.0,105.0,1.40,true}); Save(); return;} gSettings.startWindows=FieldB(s,"StartWithWindows",false); gSettings.startMinimized=FieldB(s,"StartMinimized",false); size_t dp=s.find("\"DesktopProfile\""); if(dp!=std::string::npos){ size_t a=s.find('{',dp), b=s.find('}',a); if(a!=std::string::npos&&b!=std::string::npos)gSettings.desktop=ParseProfile(s.substr(a,b-a+1)); } gSettings.desktop.name=L"Windows";
size_t pr=s.find("\"Profiles\""); if(pr!=std::string::npos){ size_t a=s.find('[',pr), b=s.find(']',a); if(a!=std::string::npos&&b!=std::string::npos){ size_t pos=a; while((pos=s.find('{',pos))!=std::string::npos&&pos<b){ size_t e=s.find('}',pos); if(e==std::string::npos||e>b)break; gSettings.profiles.push_back(ParseProfile(s.substr(pos,e-pos+1))); pos=e+1; } } } }


std::string NvDisplayNameA(const std::wstring& gdi){
    std::wstring n=gdi;
    // NvAPI docs commonly use "\\DISPLAY1", while Win32 returns "\\.\DISPLAY1".
    if(n.rfind(L"\\\\.\\",0)==0) n=L"\\\\"+n.substr(4);
    return W2U(n);
}

int WindowsDisplayNumber(const std::wstring& gdiName){
    UINT32 pathCount=0, modeCount=0;
    if(GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,&pathCount,&modeCount)!=ERROR_SUCCESS) return -1;

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
    if(QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,&pathCount,paths.data(),&modeCount,modes.data(),nullptr)!=ERROR_SUCCESS) return -1;

    for(UINT32 i=0;i<pathCount;i++){
        DISPLAYCONFIG_SOURCE_DEVICE_NAME src{};
        src.header.type=DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        src.header.size=sizeof(src);
        src.header.adapterId=paths[i].sourceInfo.adapterId;
        src.header.id=paths[i].sourceInfo.id;
        if(DisplayConfigGetDeviceInfo(&src.header)==ERROR_SUCCESS){
            if(_wcsicmp(src.viewGdiDeviceName,gdiName.c_str())==0)
                return (int)paths[i].sourceInfo.id+1;
        }
    }
    return -1;
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
        int winNum=WindowsDisplayNumber(gdi);
        std::wstring label=friendly;
        if(winNum>0) label+=L"  —  Display "+std::to_wstring(winNum);
        else label+=L"  —  "+gdi;
        if(primary) label+=L" (Primary)";
        gDisplays.push_back({gdi,label,handle,id,primary});
    }

    if(gDisplays.empty() && gDisplay && gDisplayId){
        gDisplays.push_back({L"",L"Primary NVIDIA display",gDisplay,gDisplayId,true});
    }
}

DisplayTarget* TargetForProfile(const GameProfile& p){
    if(!p.displayName.empty()){
        for(auto& d:gDisplays)
            if(_wcsicmp(d.gdiName.c_str(),p.displayName.c_str())==0) return &d;
    }
    for(auto& d:gDisplays) if(d.primary) return &d;
    return gDisplays.empty()?nullptr:&gDisplays.front();
}

void RefreshDisplayCombo(const GameProfile& p){
    HWND c=GetDlgItem(gWnd,IDC_DISPLAY);
    if(!c) return;
    SendMessageW(c,CB_RESETCONTENT,0,0);
    int selected=-1, primary=-1;
    for(size_t i=0;i<gDisplays.size();++i){
        SendMessageW(c,CB_ADDSTRING,0,(LPARAM)gDisplays[i].label.c_str());
        if(gDisplays[i].primary) primary=(int)i;
        if(!p.displayName.empty() && _wcsicmp(gDisplays[i].gdiName.c_str(),p.displayName.c_str())==0)
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
    pGetPrimaryDisplayId=(NvGetPrimaryDisplayId)q(0x1E9D8A31);
    pSetTargetGamma=(NvSetTargetGamma)q(0x7082A053);
    pGetAssociatedDisplayHandle=(NvGetAssociatedDisplayHandle)q(0x35C29134);
    pGetDisplayIdByName=(NvGetDisplayIdByName)q(0xAE457190);
    if(!init||!en||!pGetDvc||!pSetDvc||!pGetPrimaryDisplayId||!pSetTargetGamma||init()!=0||en(0,&gDisplay)!=0||pGetPrimaryDisplayId(&gDisplayId)!=0){
        gStatus=L"Could not initialize NVIDIA display";
        return false;
    }
    EnumerateNvDisplays();
    gStatus=L"Ready";
    return true;
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

// NVIDIA display-pipeline LUT, matching the native curve used by
// NvColorProfiles 1.2.3 / NvAPI_DISP_SetTargetGammaCorrection.
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

bool Apply(const GameProfile&p){
    if(!pSetDvc||!pGetDvc||!pSetTargetGamma){
        gStatus=L"NVIDIA driver / NVAPI not initialized";gStatusOk=false;InvalidateRect(gWnd,nullptr,FALSE);return false;
    }
    DisplayTarget* t=TargetForProfile(p);
    if(!t || !t->handle || !t->displayId){
        gStatus=L"Selected NVIDIA display is not available";gStatusOk=false;InvalidateRect(gWnd,nullptr,FALSE);return false;
    }
    DVCINFOEX d{};
    d.version=(unsigned int)(sizeof(d)|(1u<<16));
    if(pGetDvc(t->handle,0,&d)!=0){gStatus=L"Could not read Digital Vibrance";gStatusOk=false;InvalidateRect(gWnd,nullptr,FALSE);return false;}
    d.currentLevel=std::clamp(DvcRawFromPercent(p.vibrance,d),d.minLevel,d.maxLevel);
    if(pSetDvc(t->handle,0,&d)!=0){gStatus=L"Could not set Digital Vibrance";gStatusOk=false;InvalidateRect(gWnd,nullptr,FALSE);return false;}
    if(!SetNvGamma(t->displayId,p.brightness,p.contrast,p.gamma)){gStatus=L"Could not set NVIDIA color LUT";gStatusOk=false;InvalidateRect(gWnd,nullptr,FALSE);return false;}
    gStatus=L"Ready";gStatusOk=true;InvalidateRect(gWnd,nullptr,FALSE);return true;
}

std::wstring ProcessName(const std::wstring&p){ const wchar_t* n=PathFindFileNameW(p.c_str()); std::wstring s=n?n:L""; auto dot=s.find_last_of(L'.'); if(dot!=std::wstring::npos)s.resize(dot); return s; }
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
    if(gPaused)return;
    std::wstring fgName=ForegroundProcessName();
    GameProfile* hit=nullptr;
    for(auto& p:gSettings.profiles){
        if(!p.enabled||p.exePath.empty())continue;
        if(_wcsicmp(ProcessName(p.exePath).c_str(),fgName.c_str())==0){
            hit=&p;
            break;
        }
    }
    std::wstring next=hit?hit->name:gSettings.desktop.name;
    if(next!=gActive){
        if(hit)Apply(*hit);
        else Apply(gSettings.desktop);
        gActive=next;
        InvalidateRect(gWnd,nullptr,FALSE);
    }
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
void RefreshList(){ HWND l=H(IDC_LIST); SendMessageW(l,LB_RESETCONTENT,0,0); SendMessageW(l,LB_ADDSTRING,0,(LPARAM)gSettings.desktop.name.c_str()); for(auto&p:gSettings.profiles)SendMessageW(l,LB_ADDSTRING,0,(LPARAM)p.name.c_str()); int maxSel=(int)gSettings.profiles.size(); gSelected=std::clamp(gSelected,0,maxSel); SendMessageW(l,LB_SETCURSEL,gSelected,0); }
void UpdateSliderLabels(){
    Txt(IDC_VALVIB,std::to_wstring((int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0))+L"%");
    Txt(IDC_VALBRI,std::to_wstring((int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0)));
    Txt(IDC_VALCON,std::to_wstring((int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0)));
    int gp=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0);
    wchar_t b[32]; swprintf_s(b,L"%.2f",gp/100.0); Txt(IDC_VALGAM,b);
}
bool IsDesktopSelected(){return gSelected==0;}
GameProfile* SelectedProfile(){ if(gSelected==0)return &gSettings.desktop; int i=gSelected-1; return (i>=0&&i<(int)gSettings.profiles.size())?&gSettings.profiles[i]:nullptr; }

void SetDesktopUi(bool desktop){
    RECT r; GetClientRect(gWnd,&r);
    const int leftW=285, margin=18, gap=14;
    int rightX=margin+leftW+gap+22;
    int rightW=r.right-rightX-margin-22;

    int showGame=desktop?SW_HIDE:SW_SHOW;
    for(int id:{IDC_LBL_NAME,IDC_NAME,IDC_LBL_EXE,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_LBL_ENABLED})
        ShowWindow(H(id),showGame);
    ShowWindow(H(IDC_REMOVE),desktop?SW_HIDE:SW_SHOW);

    const int yDisplay=desktop?132:278;
    const int yVib=desktop?205:350;
    const int yBri=desktop?275:420;
    const int yCon=desktop?345:490;
    const int yGam=desktop?415:560;
    const int ySave=desktop?505:640;

    MoveWindow(H(IDC_LBL_DISPLAY),rightX,yDisplay,160,22,TRUE);
    MoveWindow(H(IDC_DISPLAY),rightX,yDisplay+25,rightW,32,TRUE);

    struct SPos{int lbl,track,val,y;};
    for(auto sp:std::vector<SPos>{
        {IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,yVib},
        {IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,yBri},
        {IDC_LBL_CON,IDC_CON,IDC_VALCON,yCon},
        {IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,yGam}
    }){
        MoveWindow(H(sp.lbl),rightX,sp.y,190,22,TRUE);
        MoveWindow(H(sp.track),rightX,sp.y+27,rightW-92,28,TRUE);
        MoveWindow(H(sp.val),rightX+rightW-76,sp.y-2,76,28,TRUE);
    }

    MoveWindow(H(IDC_SAVE),rightX,ySave,150,38,TRUE);

    // Global startup options stay at the bottom of the right panel.
    MoveWindow(H(IDC_STARTWIN),rightX,r.bottom-66,20,22,TRUE);
    MoveWindow(GetWindow(H(IDC_STARTWIN),GW_HWNDNEXT),rightX+24,r.bottom-65,135,22,TRUE);
    MoveWindow(H(IDC_STARTMIN),rightX+175,r.bottom-66,20,22,TRUE);
    MoveWindow(GetWindow(H(IDC_STARTMIN),GW_HWNDNEXT),rightX+199,r.bottom-65,175,22,TRUE);

    InvalidateRect(gWnd,nullptr,TRUE);
}
void LoadSelected(){ int i=(int)SendMessageW(H(IDC_LIST),LB_GETCURSEL,0,0); if(i<0||i>(int)gSettings.profiles.size())return; gSelected=i; auto*p=SelectedProfile(); if(!p)return; bool desktop=IsDesktopSelected(); SetDesktopUi(desktop); RefreshDisplayCombo(*p); Txt(IDC_NAME,p->name);Txt(IDC_EXE,desktop?L"":p->exePath);SendMessageW(H(IDC_ENABLED),BM_SETCHECK,desktop?BST_UNCHECKED:(p->enabled?BST_CHECKED:BST_UNCHECKED),0);SendMessageW(H(IDC_VIB),TBM_SETPOS,TRUE,p->vibrance);SendMessageW(H(IDC_BRI),TBM_SETPOS,TRUE,(LPARAM)llround(p->brightness));SendMessageW(H(IDC_CON),TBM_SETPOS,TRUE,(LPARAM)llround(p->contrast));SendMessageW(H(IDC_GAM),TBM_SETPOS,TRUE,(LPARAM)llround(p->gamma*100));UpdateSliderLabels(); }
void SaveSelected(){ auto*p=SelectedProfile(); if(!p)return; bool desktop=IsDesktopSelected(); if(!desktop){p->name=GetTxt(IDC_NAME);p->exePath=GetTxt(IDC_EXE);p->enabled=SendMessageW(H(IDC_ENABLED),BM_GETCHECK,0,0)==BST_CHECKED;}int ds=(int)SendMessageW(H(IDC_DISPLAY),CB_GETCURSEL,0,0);if(ds>=0&&ds<(int)gDisplays.size())p->displayName=gDisplays[ds].gdiName;p->vibrance=(int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0);p->brightness=(double)(int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0);p->contrast=(double)(int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0);p->gamma=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0)/100.0;Save();RefreshList(); if(desktop){Apply(gSettings.desktop);gActive=gSettings.desktop.name;}else{std::wstring fg=ForegroundProcessName();if(!p->exePath.empty()&&_wcsicmp(ProcessName(p->exePath).c_str(),fg.c_str())==0){Apply(*p);gActive=p->name;}} }

HICON LoadExeIcon(const std::wstring& path){
    if(path.empty() || !PathFileExistsW(path.c_str())) return nullptr;

    // Ask Windows for a 48x48 resource first. This avoids stretching a 16/32 px
    // small icon and keeps game icons much sharper in the profile list.
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

void DrawGlyphIcon(HDC dc,const wchar_t* glyph,int x,int y,COLORREF c){
    HFONT oldFont=(HFONT)SelectObject(dc,gIconFont);
    SetBkMode(dc,TRANSPARENT);
    SetTextColor(dc,c);
    TextOutW(dc,x,y,glyph,1);
    SelectObject(dc,oldFont);
}

void DrawSaveIcon(HDC dc,int x,int y,COLORREF c){
    HPEN p=CreatePen(PS_SOLID,2,c);HGDIOBJ old=SelectObject(dc,p);
    Rectangle(dc,x+2,y+2,x+18,y+19);
    Rectangle(dc,x+5,y+3,x+14,y+9);
    Rectangle(dc,x+6,y+13,x+14,y+19);
    SelectObject(dc,old);DeleteObject(p);
}
void DrawFolderIcon(HDC dc,int x,int y,COLORREF c){
    HPEN p=CreatePen(PS_SOLID,2,c);HGDIOBJ old=SelectObject(dc,p);
    MoveToEx(dc,x+1,y+6,nullptr);LineTo(dc,x+7,y+6);
    LineTo(dc,x+9,y+9);LineTo(dc,x+20,y+9);LineTo(dc,x+20,y+20);
    LineTo(dc,x+1,y+20);LineTo(dc,x+1,y+6);
    SelectObject(dc,old);DeleteObject(p);
}

void DrawOwnerButton(const DRAWITEMSTRUCT* d){
    int id=(int)d->CtlID;
    bool down=(d->itemState&ODS_SELECTED)!=0;
    bool disabled=(d->itemState&ODS_DISABLED)!=0;

    COLORREF fill=C_PANEL2, border=C_BORDER, textColor=disabled?C_MUTED:C_TEXT, icon=C_MUTED;
    if(id==IDC_SAVE){
        fill=down?RGB(42,130,23):RGB(39,151,22);
        border=RGB(77,205,44); icon=C_TEXT;
    }else if(id==IDC_ADD){
        fill=down?RGB(26,62,29):RGB(22,48,27);
        border=RGB(46,117,46); icon=C_ACCENT;
    }else if(id==IDC_REMOVE){
        icon=C_DANGER;
    }else if(id==IDC_BROWSE){
        icon=C_TEXT;
    }

    RECT r=d->rcItem;
    FillRound(d->hDC,r,fill,border,8);

    wchar_t caption[128]{};
    GetWindowTextW(d->hwndItem,caption,128);
    SIZE sz{};SelectObject(d->hDC,gFont);GetTextExtentPoint32W(d->hDC,caption,(int)wcslen(caption),&sz);

    int iconW=(id==IDC_ADD||id==IDC_REMOVE)?19:20;
    int gap=(id==IDC_SAVE)?7:6;
    int total=iconW+gap+sz.cx;
    int start=r.left+((r.right-r.left)-total)/2;
    int cy=(r.top+r.bottom)/2;

    if(id==IDC_SAVE) DrawSaveIcon(d->hDC,start,cy-10,icon);
    else if(id==IDC_ADD) DrawGlyphIcon(d->hDC,L"\xE710",start+1,cy-9,icon);      // Add
    else if(id==IDC_REMOVE) DrawGlyphIcon(d->hDC,L"\xE74D",start+1,cy-9,icon); // Delete
    else if(id==IDC_BROWSE) DrawFolderIcon(d->hDC,start,cy-10,icon);

    SetBkMode(d->hDC,TRANSPARENT);SetTextColor(d->hDC,textColor);SelectObject(d->hDC,gFont);
    TextOutW(d->hDC,start+iconW+gap,cy-sz.cy/2,caption,(int)wcslen(caption));
}

void DrawValueBox(const DRAWITEMSTRUCT* d){
    RECT r=d->rcItem;
    FillRound(d->hDC,r,C_FIELD,C_BORDER,7);
    wchar_t text[64]{};GetWindowTextW(d->hwndItem,text,64);
    RECT tr=r;tr.right-=10;
    SetBkMode(d->hDC,TRANSPARENT);SetTextColor(d->hDC,C_TEXT);SelectObject(d->hDC,gFont);
    DrawTextW(d->hDC,text,-1,&tr,DT_RIGHT|DT_VCENTER|DT_SINGLELINE);
}

LRESULT CustomDrawSlider(NMCUSTOMDRAW* cd){
    HWND h=cd->hdr.hwndFrom;
    if(cd->dwDrawStage!=CDDS_PREPAINT) return CDRF_DODEFAULT;

    RECT r{};GetClientRect(h,&r);
    HDC dc=cd->hdc;
    HBRUSH panelBrush=CreateSolidBrush(C_PANEL);
    FillRect(dc,&r,panelBrush);
    DeleteObject(panelBrush);

    int minv=(int)SendMessageW(h,TBM_GETRANGEMIN,0,0);
    int maxv=(int)SendMessageW(h,TBM_GETRANGEMAX,0,0);
    int pos=(int)SendMessageW(h,TBM_GETPOS,0,0);
    double t=maxv==minv?0.0:(double)(pos-minv)/(double)(maxv-minv);

    int x1=8,x2=(int)std::max<LONG>(9L,r.right-8),cy=(int)((r.top+r.bottom)/2);
    int active=x1+(int)llround((x2-x1)*t);

    RECT bg{x1,cy-2,x2,cy+2};FillRound(dc,bg,C_TRACK,C_TRACK,4);
    if(active>x1){
        RECT fg{x1,cy-2,active,cy+2};FillRound(dc,fg,C_ACCENT2,C_ACCENT2,4);
    }

    int rad=7;
    HBRUSH b=CreateSolidBrush(C_ACCENT);
    HPEN p=CreatePen(PS_SOLID,1,RGB(111,235,71));
    HGDIOBJ ob=SelectObject(dc,b),op=SelectObject(dc,p);
    Ellipse(dc,active-rad,cy-rad,active+rad+1,cy+rad+1);
    SelectObject(dc,ob);SelectObject(dc,op);DeleteObject(b);DeleteObject(p);
    return CDRF_SKIPDEFAULT;
}

void DrawLabel(HDC dc,const wchar_t*t,int x,int y,COLORREF c,HFONT f=nullptr){ SetBkMode(dc,TRANSPARENT);SetTextColor(dc,c);SelectObject(dc,f?f:gFont);TextOutW(dc,x,y,t,(int)wcslen(t)); }
void Fill(HDC dc,int x,int y,int w,int h,COLORREF c){HBRUSH b=CreateSolidBrush(c);RECT r{x,y,x+w,y+h};FillRect(dc,&r,b);DeleteObject(b);} 
void Paint(HWND w){
    PAINTSTRUCT ps;HDC dc=BeginPaint(w,&ps);RECT rc;GetClientRect(w,&rc);
    FillRect(dc,&rc,gBackBrush);

    const int margin=18, top=76, leftW=285, gap=14;
    int rightX=margin+leftW+gap;
    RECT left{margin,top,margin+leftW,rc.bottom-24};
    RECT right{rightX,top,rc.right-margin,rc.bottom-24};
    FillRound(dc,left,C_PANEL,C_BORDER,10);
    FillRound(dc,right,C_PANEL,C_BORDER,10);

    DrawLabel(dc,L"NvProfileSwitcher",28,22,C_TEXT,gFontTitle);
    DrawLabel(dc,L"Automatic display color profiles for your games",28,49,C_MUTED);

    RECT ver{rc.right-92,19,rc.right-28,49};
    FillRound(dc,ver,C_PANEL2,C_BORDER,7);
    DrawLabel(dc,L"v0.6.0",rc.right-80,27,C_ACCENT,gFontBold);

    DrawLabel(dc,L"GAME PROFILES",38,94,C_MUTED,gFontBold);
    DrawLabel(dc,L"PROFILE SETTINGS",rightX+22,94,C_MUTED,gFontBold);

    if(!gStatusOk && !gStatus.empty()){
        SIZE z{};SelectObject(dc,gFont);
        GetTextExtentPoint32W(dc,gStatus.c_str(),(int)gStatus.size(),&z);
        DrawLabel(dc,gStatus.c_str(),rc.right-z.cx-28,rc.bottom-22,C_DANGER);
    }
    EndPaint(w,&ps);
}
void BuildControls(){
    RECT r;GetClientRect(gWnd,&r);
    const int margin=18, top=76, leftW=285, gap=14;
    int rightPanelX=margin+leftW+gap;
    int rightX=rightPanelX+22;
    int rightW=r.right-rightX-margin-22;

    HWND list=Add(L"LISTBOX",L"",LBS_NOTIFY|LBS_OWNERDRAWFIXED|WS_VSCROLL,34,124,leftW-32,r.bottom-290,IDC_LIST);
    SendMessageW(list,LB_SETITEMHEIGHT,0,66);

    Add(L"STATIC",L"Profile name",0,rightX,122,160,22,IDC_LBL_NAME);
    HWND eName=Add(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,rightX,146,rightW,28,IDC_NAME);SetWindowTheme(eName,L"DarkMode_Explorer",nullptr);
    Add(L"STATIC",L"Game executable",0,rightX,185,160,22,IDC_LBL_EXE);
    HWND eExe=Add(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,rightX,209,rightW-110,28,IDC_EXE);SetWindowTheme(eExe,L"DarkMode_Explorer",nullptr);
    Add(L"BUTTON",L"Browse...",BS_OWNERDRAW,rightX+rightW-100,204,100,36,IDC_BROWSE);
    Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX,247,20,22,IDC_ENABLED);
    Add(L"STATIC",L"Enable automatic profile",0,rightX+25,248,205,22,IDC_LBL_ENABLED);

    Add(L"STATIC",L"Display",0,rightX,278,160,22,IDC_LBL_DISPLAY);
    HWND display=Add(L"COMBOBOX",L"",CBS_DROPDOWNLIST|CBS_OWNERDRAWFIXED|CBS_HASSTRINGS|WS_VSCROLL,rightX,303,rightW,240,IDC_DISPLAY);
    SendMessageW(display,CB_SETITEMHEIGHT,0,28);
    SetWindowTheme(display,L"DarkMode_Explorer",nullptr);

    auto slider=[&](const wchar_t*t,int lid,int id,int vid,int y,int mn,int mx){
        Add(L"STATIC",t,0,rightX,y,190,22,lid);
        HWND tr=Add(TRACKBAR_CLASSW,L"",TBS_HORZ|TBS_NOTICKS,rightX,y+27,rightW-92,28,id);
        SendMessageW(tr,TBM_SETRANGE,TRUE,MAKELONG(mn,mx));
        Add(L"STATIC",L"",SS_OWNERDRAW,rightX+rightW-76,y-2,76,28,vid);
    };
    slider(L"Digital Vibrance (%)",IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,350,0,100);
    slider(L"Brightness",IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,420,80,120);
    slider(L"Contrast",IDC_LBL_CON,IDC_CON,IDC_VALCON,490,80,120);
    slider(L"Gamma",IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,560,30,280);

    Add(L"BUTTON",L"Save profile",BS_OWNERDRAW,rightX,640,150,38,IDC_SAVE);
    Add(L"BUTTON",L"Add game",BS_OWNERDRAW,34,r.bottom-172,118,38,IDC_ADD);
    Add(L"BUTTON",L"Remove",BS_OWNERDRAW,164,r.bottom-172,104,38,IDC_REMOVE);

    Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX,r.bottom-66,20,22,IDC_STARTWIN);
    Add(L"STATIC",L"Start with Windows",0,rightX+24,r.bottom-65,135,22,0);
    Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX+175,r.bottom-66,20,22,IDC_STARTMIN);
    Add(L"STATIC",L"Start minimized to tray",0,rightX+199,r.bottom-65,175,22,0);

    SendMessageW(H(IDC_STARTWIN),BM_SETCHECK,gSettings.startWindows?BST_CHECKED:BST_UNCHECKED,0);
    SendMessageW(H(IDC_STARTMIN),BM_SETCHECK,gSettings.startMinimized?BST_CHECKED:BST_UNCHECKED,0);
}

void ResizeControls(){
    RECT r;GetClientRect(gWnd,&r);
    const int margin=18, leftW=285, gap=14;
    int rightPanelX=margin+leftW+gap;
    int rightX=rightPanelX+22;
    int rightW=r.right-rightX-margin-22;

    MoveWindow(H(IDC_LIST),34,124,leftW-32,(int)std::max<LONG>(280L,r.bottom-290),TRUE);
    MoveWindow(H(IDC_NAME),rightX,146,rightW,28,TRUE);
    MoveWindow(H(IDC_EXE),rightX,209,rightW-110,28,TRUE);
    MoveWindow(H(IDC_BROWSE),rightX+rightW-100,204,100,36,TRUE);
    MoveWindow(H(IDC_ADD),34,r.bottom-172,120,38,TRUE);
    MoveWindow(H(IDC_REMOVE),164,r.bottom-172,104,38,TRUE);
    SetDesktopUi(IsDesktopSelected());
}

void ShowMain(){ShowWindow(gWnd,SW_SHOW);SetForegroundWindow(gWnd);} void RestoreDesktop(){Apply(gSettings.desktop);gActive=gSettings.desktop.name;InvalidateRect(gWnd,nullptr,FALSE);} void UpdateTrayPause(){ModifyMenuW(gTrayMenu,ID_TRAY_PAUSE,MF_BYCOMMAND|MF_STRING,ID_TRAY_PAUSE,gPaused?L"Resume automatic switching":L"Pause automatic switching");}
LRESULT CALLBACK Proc(HWND w,UINT m,WPARAM wp,LPARAM lp){switch(m){case WM_CREATE:gWnd=w;BuildControls();RefreshList();LoadSelected();SetTimer(w,1,250,nullptr);return 0;case WM_SIZE:
    if(wp==SIZE_MINIMIZED){
        ShowWindow(w,SW_HIDE);
        return 0;
    }
    ResizeControls();
    InvalidateRect(w,nullptr,TRUE);
    return 0;case WM_PAINT:Paint(w);return 0;
case WM_NOTIFY:{
    auto* hdr=(NMHDR*)lp;
    int id=GetDlgCtrlID(hdr->hwndFrom);
    if(hdr->code==NM_CUSTOMDRAW && (id==IDC_VIB||id==IDC_BRI||id==IDC_CON||id==IDC_GAM))
        return CustomDrawSlider((NMCUSTOMDRAW*)lp);
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

    if(d->CtlID==IDC_VALVIB||d->CtlID==IDC_VALBRI||d->CtlID==IDC_VALCON||d->CtlID==IDC_VALGAM){
        DrawValueBox(d);return TRUE;
    }

    if(d->CtlID==IDC_SAVE||d->CtlID==IDC_ADD||d->CtlID==IDC_REMOVE||d->CtlID==IDC_BROWSE){
        DrawOwnerButton(d);return TRUE;
    }

    if(d->CtlID==IDC_LIST&&d->itemID!=(UINT)-1){
        bool selected=(d->itemState&ODS_SELECTED)!=0;
        // Draw the row ourselves so selected items match the dark/green visual language.
        if(selected) Fill(d->hDC,d->rcItem.left,d->rcItem.top,d->rcItem.right-d->rcItem.left,d->rcItem.bottom-d->rcItem.top,C_ACCENT_DARK);
        else Fill(d->hDC,d->rcItem.left,d->rcItem.top,d->rcItem.right-d->rcItem.left,d->rcItem.bottom-d->rcItem.top,C_PANEL);
        if(selected) Fill(d->hDC,d->rcItem.left,d->rcItem.top,4,d->rcItem.bottom-d->rcItem.top,C_ACCENT);

        bool desktop=d->itemID==0;
        GameProfile*p=desktop?&gSettings.desktop:&gSettings.profiles[d->itemID-1];
        int x=d->rcItem.left+12,y=d->rcItem.top+11;

        if(desktop){
            DrawWindowsLogo(d->hDC,x+1,y+1,40);
        }else{
            HICON ic=LoadExeIcon(p->exePath);
            if(ic){DrawIconEx(d->hDC,x,y,ic,42,42,0,nullptr,DI_NORMAL);DestroyIcon(ic);}
            else{
                HBRUSH b=CreateSolidBrush(C_ACCENT);HGDIOBJ old=SelectObject(d->hDC,b);
                Ellipse(d->hDC,x+2,y+2,x+42,y+42);SelectObject(d->hDC,old);DeleteObject(b);
                if(!p->name.empty()){wchar_t c[2]{p->name[0],0};DrawLabel(d->hDC,c,x+16,y+12,RGB(255,255,255),gFontBold);}
            }
        }

        const wchar_t* title=desktop?L"Windows":p->name.c_str();
        DrawLabel(d->hDC,title,x+54,d->rcItem.top+11,C_TEXT,gFontBold);
        std::wstring sub=desktop?L"Default display profile":ProcessName(p->exePath);
        DrawLabel(d->hDC,sub.c_str(),x+54,d->rcItem.top+35,C_MUTED);
        Fill(d->hDC,d->rcItem.left+10,d->rcItem.bottom-1,d->rcItem.right-d->rcItem.left-20,1,C_BORDER);
        return TRUE;
    }
    break;
}case WM_HSCROLL:UpdateSliderLabels();if((HWND)lp)InvalidateRect((HWND)lp,nullptr,FALSE);return 0;case WM_TIMER:CheckProcesses();return 0;case WM_COMMAND:{int id=LOWORD(wp);if(id==IDC_LIST&&HIWORD(wp)==LBN_SELCHANGE){LoadSelected();return 0;}switch(id){case IDC_BROWSE:{OPENFILENAMEW o{sizeof(o)};wchar_t f[MAX_PATH]{};o.hwndOwner=w;o.lpstrFilter=L"Executables (*.exe)\0*.exe\0All files\0*.*\0";o.lpstrFile=f;o.nMaxFile=MAX_PATH;o.Flags=OFN_FILEMUSTEXIST;if(GetOpenFileNameW(&o)){Txt(IDC_EXE,f);auto* p=SelectedProfile();if(p&&!IsDesktopSelected()){p->exePath=f;InvalidateRect(H(IDC_LIST),nullptr,TRUE);}}break;}case IDC_SAVE:SaveSelected();break;case IDC_ADD:gSettings.profiles.push_back({});gSelected=(int)gSettings.profiles.size();Save();RefreshList();LoadSelected();break;case IDC_REMOVE:if(gSelected>0&&gSelected<=(int)gSettings.profiles.size()){gSettings.profiles.erase(gSettings.profiles.begin()+(gSelected-1));gSelected=std::max<int>(0,gSelected-1);Save();RefreshList();LoadSelected();}break;case IDC_STARTWIN:gSettings.startWindows=SendMessageW(H(IDC_STARTWIN),BM_GETCHECK,0,0)==BST_CHECKED;SetStartup(gSettings.startWindows);Save();break;case IDC_STARTMIN:gSettings.startMinimized=SendMessageW(H(IDC_STARTMIN),BM_GETCHECK,0,0)==BST_CHECKED;Save();break;case ID_TRAY_OPEN:ShowMain();break;case ID_TRAY_RESTORE:RestoreDesktop();break;case ID_TRAY_PAUSE:gPaused=!gPaused;if(gPaused)RestoreDesktop();UpdateTrayPause();break;case ID_TRAY_EXIT:gReallyExit=true;DestroyWindow(w);break;}return 0;}case WM_CLOSE:if(!gReallyExit){ShowWindow(w,SW_HIDE);return 0;}break;case WM_TRAY:if(lp==WM_LBUTTONDBLCLK){ShowMain();return 0;}if(lp==WM_RBUTTONUP||lp==WM_CONTEXTMENU){POINT p;GetCursorPos(&p);SetForegroundWindow(w);TrackPopupMenu(gTrayMenu,TPM_RIGHTBUTTON,p.x,p.y,0,w,nullptr);return 0;}break;case WM_DESTROY:KillTimer(w,1);Shell_NotifyIconW(NIM_DELETE,&gNid);if(pUnload)pUnload();if(gNv)FreeLibrary(gNv);PostQuitMessage(0);return 0;}return DefWindowProcW(w,m,wp,lp);} 

int WINAPI wWinMain(HINSTANCE h,HINSTANCE,LPWSTR cmd,int){gInst=h;INITCOMMONCONTROLSEX ic{sizeof(ic),ICC_BAR_CLASSES|ICC_STANDARD_CLASSES};InitCommonControlsEx(&ic);Load();gSettings.desktop.name=L"Windows";gBackBrush=CreateSolidBrush(C_BACK);gPanelBrush=CreateSolidBrush(C_PANEL);gPanel2Brush=CreateSolidBrush(C_PANEL2);gFieldBrush=CreateSolidBrush(C_FIELD);gFont=CreateFontW(-15,0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");gFontBold=CreateFontW(-15,0,0,0,FW_SEMIBOLD,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");gFontTitle=CreateFontW(-24,0,0,0,FW_BOLD,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");
gIconFont=CreateFontW(-18,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe MDL2 Assets");gIcon=LoadIconW(h,MAKEINTRESOURCEW(IDI_APPICON));WNDCLASSEXW wc{sizeof(wc)};wc.style=CS_HREDRAW|CS_VREDRAW;wc.lpfnWndProc=Proc;wc.hInstance=h;wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hbrBackground=gBackBrush;wc.lpszClassName=L"NvProfileSwitcherNative";RegisterClassExW(&wc);gWnd=CreateWindowExW(0,wc.lpszClassName,L"NvProfileSwitcher v0.6.0",WS_OVERLAPPEDWINDOW,CW_USEDEFAULT,CW_USEDEFAULT,980,800,nullptr,nullptr,h,nullptr);
BOOL darkTitle=TRUE;DwmSetWindowAttribute(gWnd,20,&darkTitle,sizeof(darkTitle));
SetWindowLongPtrW(gWnd,GWLP_USERDATA,0);gTrayMenu=CreatePopupMenu();AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_OPEN,L"Open Game Profile Switcher");AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_RESTORE,L"Restore Windows profile");AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_PAUSE,L"Pause automatic switching");AppendMenuW(gTrayMenu,MF_SEPARATOR,0,nullptr);AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_EXIT,L"Exit");gNid.cbSize=sizeof(gNid);gNid.hWnd=gWnd;gNid.uID=1;gNid.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;gNid.uCallbackMessage=WM_TRAY;gNid.hIcon=gIcon;wcscpy_s(gNid.szTip,L"NvProfileSwitcher");Shell_NotifyIconW(NIM_ADD,&gNid);gStatusOk=InitNv();if(gStatusOk){if(auto* p=SelectedProfile())RefreshDisplayCombo(*p);Apply(gSettings.desktop);}gActive=gSettings.desktop.name;bool min=(wcsstr(cmd,L"--minimized")!=nullptr)||gSettings.startMinimized;ShowWindow(gWnd,min?SW_HIDE:SW_SHOW);UpdateWindow(gWnd);MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}DeleteObject(gFont);DeleteObject(gFontBold);DeleteObject(gFontTitle);DeleteObject(gIconFont);DeleteObject(gBackBrush);DeleteObject(gPanelBrush);DeleteObject(gPanel2Brush);DeleteObject(gFieldBrush);return 0;}
