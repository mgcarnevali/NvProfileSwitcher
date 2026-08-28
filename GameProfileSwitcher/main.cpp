#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>
#include <commdlg.h>
#include <shlobj.h>
#include <tlhelp32.h>
#include <shlwapi.h>
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

struct GameProfile {
    std::wstring name=L"New Profile";
    std::wstring exePath;
    int vibrance=50;
    double brightness=.50, contrast=.50, gamma=1.00;
    bool enabled=true;
};
struct Settings {
    GameProfile desktop{L"Desktop / Normal",L"",50,.50,.50,1.00,true};
    std::vector<GameProfile> profiles;
    bool startWindows=false, startMinimized=false;
};

constexpr COLORREF C_BACK=RGB(22,24,28), C_PANEL=RGB(30,33,38), C_PANEL2=RGB(36,40,46), C_BORDER=RGB(55,60,68);
constexpr COLORREF C_TEXT=RGB(235,238,242), C_MUTED=RGB(155,163,174), C_ACCENT=RGB(70,190,120), C_DANGER=RGB(220,84,84);
constexpr UINT WM_TRAY=WM_APP+1;
enum {IDC_LIST=1001,IDC_NAME,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_VIB,IDC_BRI,IDC_CON,IDC_GAM,IDC_SAVE,IDC_APPLY,IDC_ADD,IDC_REMOVE,IDC_RESTORE,IDC_STARTWIN,IDC_STARTMIN,IDC_VALVIB,IDC_VALBRI,IDC_VALCON,IDC_VALGAM,IDC_LBL_NAME,IDC_LBL_EXE,IDC_LBL_ENABLED,IDC_LBL_VIB,IDC_LBL_BRI,IDC_LBL_CON,IDC_LBL_GAM};
enum {ID_TRAY_OPEN=2001,ID_TRAY_RESTORE,ID_TRAY_PAUSE,ID_TRAY_EXIT};

HINSTANCE gInst{}; HWND gWnd{}; HFONT gFont{},gFontBold{},gFontTitle{}; HBRUSH gBackBrush{},gPanelBrush{},gPanel2Brush{}; HICON gIcon{};
Settings gSettings; int gSelected=-1; bool gPaused=false,gReallyExit=false; std::wstring gActive=L"Desktop / Normal", gStatus=L"Not initialized"; bool gStatusOk=false;
NOTIFYICONDATAW gNid{}; HMENU gTrayMenu{};

using NvQueryInterface=void* (__cdecl*)(unsigned int);
using NvInit=int (__cdecl*)(); using NvUnload=int (__cdecl*)(); using NvEnumDisplay=int (__cdecl*)(int,void**);
struct DVCINFOEX { unsigned int version; int currentLevel,minLevel,maxLevel,defaultLevel; };
using NvGetDVC=int (__cdecl*)(void*,unsigned int,DVCINFOEX*); using NvSetDVC=int (__cdecl*)(void*,unsigned int,DVCINFOEX*);
HMODULE gNv{}; NvUnload pUnload{}; NvGetDVC pGetDvc{}; NvSetDVC pSetDvc{}; void* gDisplay{};

std::wstring AppDataFile(){ wchar_t p[MAX_PATH]{}; SHGetFolderPathW(nullptr,CSIDL_APPDATA,nullptr,SHGFP_TYPE_CURRENT,p); std::wstring d=std::wstring(p)+L"\\GameProfileSwitcher"; CreateDirectoryW(d.c_str(),nullptr); return d+L"\\profiles.json"; }
std::string W2U(const std::wstring&s){ if(s.empty())return{}; int n=WideCharToMultiByte(CP_UTF8,0,s.c_str(),-1,nullptr,0,nullptr,nullptr); std::string r(n,0); WideCharToMultiByte(CP_UTF8,0,s.c_str(),-1,r.data(),n,nullptr,nullptr); r.pop_back(); return r; }
std::wstring U2W(const std::string&s){ if(s.empty())return{}; int n=MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,nullptr,0); std::wstring r(n,0); MultiByteToWideChar(CP_UTF8,0,s.c_str(),-1,r.data(),n); r.pop_back(); return r; }
std::string Escape(const std::wstring&w){ std::string s=W2U(w),o; for(char c:s){ if(c=='\\'||c=='\"')o+='\\'; o+=c;} return o; }
std::wstring Unescape(std::string s){ std::string o; for(size_t i=0;i<s.size();++i){ if(s[i]=='\\'&&i+1<s.size()){ char n=s[++i]; if(n=='n')o+='\n'; else if(n=='r')o+='\r'; else if(n=='t')o+='\t'; else o+=n;} else o+=s[i]; } return U2W(o); }
std::string ReadAll(const std::wstring&p){ std::ifstream f(p,std::ios::binary); if(!f)return{}; return {std::istreambuf_iterator<char>(f),{}}; }
std::string FieldS(const std::string&o,const char*k,const char*d=""){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\""); std::smatch m; return std::regex_search(o,m,r)?m[1].str():d; }
double FieldN(const std::string&o,const char*k,double d){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)"); std::smatch m; return std::regex_search(o,m,r)?std::stod(m[1].str()):d; }
bool FieldB(const std::string&o,const char*k,bool d){ std::regex r(std::string("\\\"")+k+"\\\"\\s*:\\s*(true|false)"); std::smatch m; return std::regex_search(o,m,r)?m[1].str()=="true":d; }
GameProfile ParseProfile(const std::string&o){ GameProfile p; p.name=Unescape(FieldS(o,"Name","New Profile")); p.exePath=Unescape(FieldS(o,"ExePath")); p.vibrance=(int)FieldN(o,"DigitalVibrance",50); p.brightness=FieldN(o,"Brightness",.5); p.contrast=FieldN(o,"Contrast",.5); p.gamma=FieldN(o,"Gamma",1.0); p.enabled=FieldB(o,"Enabled",true); return p; }
void Save(){ std::ofstream f(AppDataFile(),std::ios::binary|std::ios::trunc); auto dump=[&](const GameProfile&p,int ind){ std::string sp(ind,' '); f<<sp<<"{\n"<<sp<<"  \"Name\": \""<<Escape(p.name)<<"\",\n"<<sp<<"  \"ExePath\": \""<<Escape(p.exePath)<<"\",\n"<<sp<<"  \"DigitalVibrance\": "<<p.vibrance<<",\n"<<sp<<"  \"Brightness\": "<<p.brightness<<",\n"<<sp<<"  \"Contrast\": "<<p.contrast<<",\n"<<sp<<"  \"Gamma\": "<<p.gamma<<",\n"<<sp<<"  \"Enabled\": "<<(p.enabled?"true":"false")<<"\n"<<sp<<"}";}; f<<"{\n  \"DesktopProfile\": "; dump(gSettings.desktop,2); f<<",\n  \"Profiles\": [\n"; for(size_t i=0;i<gSettings.profiles.size();++i){ dump(gSettings.profiles[i],4); if(i+1<gSettings.profiles.size())f<<","; f<<"\n";} f<<"  ],\n  \"StartWithWindows\": "<<(gSettings.startWindows?"true":"false")<<",\n  \"StartMinimized\": "<<(gSettings.startMinimized?"true":"false")<<"\n}\n"; }
void Load(){ std::string s=ReadAll(AppDataFile()); if(s.empty()){ gSettings.profiles.push_back({L"Escape from Tarkov",L"",75,.50,.60,1.40,true}); Save(); return;} gSettings.startWindows=FieldB(s,"StartWithWindows",false); gSettings.startMinimized=FieldB(s,"StartMinimized",false); size_t dp=s.find("\"DesktopProfile\""); if(dp!=std::string::npos){ size_t a=s.find('{',dp), b=s.find('}',a); if(a!=std::string::npos&&b!=std::string::npos)gSettings.desktop=ParseProfile(s.substr(a,b-a+1)); } size_t pr=s.find("\"Profiles\""); if(pr!=std::string::npos){ size_t a=s.find('[',pr), b=s.find(']',a); if(a!=std::string::npos&&b!=std::string::npos){ size_t pos=a; while((pos=s.find('{',pos))!=std::string::npos&&pos<b){ size_t e=s.find('}',pos); if(e==std::string::npos||e>b)break; gSettings.profiles.push_back(ParseProfile(s.substr(pos,e-pos+1))); pos=e+1; } } } }

bool InitNv(){ gNv=LoadLibraryW(L"nvapi64.dll"); if(!gNv){gStatus=L"NVIDIA driver / NVAPI not found";return false;} auto q=(NvQueryInterface)GetProcAddress(gNv,"nvapi_QueryInterface"); if(!q){gStatus=L"nvapi_QueryInterface not found";return false;} auto init=(NvInit)q(0x0150E828); pUnload=(NvUnload)q(0xD22BDD7E); auto en=(NvEnumDisplay)q(0x9ABDD40D); pGetDvc=(NvGetDVC)q(0x0E45002D); pSetDvc=(NvSetDVC)q(0x4A82C2B1); if(!init||!en||!pGetDvc||!pSetDvc||init()!=0||en(0,&gDisplay)!=0){gStatus=L"Could not initialize NVIDIA display";return false;} gStatus=L"Ready"; return true; }
void SetGamma(double bri,double con,double gam){ WORD ramp[3][256]{}; for(int i=0;i<256;i++){ double v=i/255.0; v=pow(v,1.0/gam); v=((v-.5)*(con/.5))+.5; v+=bri-.5; v=std::clamp(v,0.0,1.0); WORD x=(WORD)llround(v*65535.0); ramp[0][i]=ramp[1][i]=ramp[2][i]=x;} HDC dc=GetDC(nullptr); SetDeviceGammaRamp(dc,ramp); ReleaseDC(nullptr,dc); }
bool Apply(const GameProfile&p){ if(!gDisplay||!pSetDvc||!pGetDvc){gStatus=L"NVIDIA driver / NVAPI not initialized";gStatusOk=false;InvalidateRect(gWnd,nullptr,FALSE);return false;} DVCINFOEX d{}; d.version=sizeof(d)|0x10000; if(pGetDvc(gDisplay,0,&d)!=0){gStatus=L"Could not read Digital Vibrance";gStatusOk=false;return false;} d.currentLevel=std::clamp(p.vibrance,d.minLevel,d.maxLevel); if(pSetDvc(gDisplay,0,&d)!=0){gStatus=L"Could not set Digital Vibrance";gStatusOk=false;return false;} SetGamma(p.brightness,p.contrast,p.gamma); gStatus=L"Applied: "+p.name; gStatusOk=true; InvalidateRect(gWnd,nullptr,FALSE); return true; }

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
    DWORD len=(DWORD)std::size(path);
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
void SetStartup(bool on){ HKEY k; if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,nullptr,0,KEY_SET_VALUE,nullptr,&k,nullptr)==ERROR_SUCCESS){ if(on){ wchar_t p[MAX_PATH]; GetModuleFileNameW(nullptr,p,MAX_PATH); std::wstring v=L"\""+std::wstring(p)+L"\" --minimized"; RegSetValueExW(k,L"GameProfileSwitcher",0,REG_SZ,(BYTE*)v.c_str(),(DWORD)((v.size()+1)*sizeof(wchar_t))); }else RegDeleteValueW(k,L"GameProfileSwitcher"); RegCloseKey(k);} }

HWND H(int id){return GetDlgItem(gWnd,id);} void Txt(int id,const std::wstring&s){SetWindowTextW(H(id),s.c_str());} std::wstring GetTxt(int id){int n=GetWindowTextLengthW(H(id));std::wstring s(n+1,0);GetWindowTextW(H(id),s.data(),n+1);s.resize(n);return s;}
HWND Add(const wchar_t*cls,const wchar_t*txt,DWORD style,int x,int y,int w,int h,int id){ HWND c=CreateWindowExW(0,cls,txt,WS_CHILD|WS_VISIBLE|style,x,y,w,h,gWnd,(HMENU)(INT_PTR)id,gInst,nullptr); SendMessageW(c,WM_SETFONT,(WPARAM)gFont,TRUE); return c; }
void RefreshList(){ HWND l=H(IDC_LIST); SendMessageW(l,LB_RESETCONTENT,0,0); SendMessageW(l,LB_ADDSTRING,0,(LPARAM)gSettings.desktop.name.c_str()); for(auto&p:gSettings.profiles)SendMessageW(l,LB_ADDSTRING,0,(LPARAM)p.name.c_str()); int maxSel=(int)gSettings.profiles.size(); gSelected=std::clamp(gSelected,0,maxSel); SendMessageW(l,LB_SETCURSEL,gSelected,0); }
void UpdateSliderLabels(){ Txt(IDC_VALVIB,std::to_wstring((int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0))+L"%"); Txt(IDC_VALBRI,std::to_wstring((int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0))+L"%"); Txt(IDC_VALCON,std::to_wstring((int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0))+L"%"); int gp=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0); wchar_t b[32]; swprintf_s(b,L"%.2f",gp/100.0); Txt(IDC_VALGAM,b); }
bool IsDesktopSelected(){return gSelected==0;}
GameProfile* SelectedProfile(){ if(gSelected==0)return &gSettings.desktop; int i=gSelected-1; return (i>=0&&i<(int)gSettings.profiles.size())?&gSettings.profiles[i]:nullptr; }

void SetDesktopUi(bool desktop){
    int rightX=298;
    RECT r; GetClientRect(gWnd,&r);
    int rightW=r.right-318;

    int showGame = desktop ? SW_HIDE : SW_SHOW;
    for(int id:{IDC_LBL_NAME,IDC_NAME,IDC_LBL_EXE,IDC_EXE,IDC_BROWSE,IDC_ENABLED,IDC_LBL_ENABLED})
        ShowWindow(H(id),showGame);

    ShowWindow(H(IDC_REMOVE), desktop ? SW_HIDE : SW_SHOW);
    SetWindowTextW(H(IDC_SAVE), desktop ? L"Save Desktop" : L"Save profile");

    const int yVib = desktop ? 150 : 282;
    const int yBri = desktop ? 215 : 337;
    const int yCon = desktop ? 280 : 392;
    const int yGam = desktop ? 345 : 447;
    const int yButtons = desktop ? 430 : 530;

    struct SPos { int lbl, track, val, y; };
    for(auto sp : std::vector<SPos>{
        {IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,yVib},
        {IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,yBri},
        {IDC_LBL_CON,IDC_CON,IDC_VALCON,yCon},
        {IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,yGam}
    }){
        MoveWindow(H(sp.lbl),rightX,sp.y,160,22,TRUE);
        MoveWindow(H(sp.track),rightX,sp.y+26,rightW-10,28,TRUE);
        MoveWindow(H(sp.val),rightX+rightW-70,sp.y,60,22,TRUE);
    }

    MoveWindow(H(IDC_SAVE),rightX,yButtons,105,36,TRUE);

    InvalidateRect(gWnd,nullptr,TRUE);
}
void LoadSelected(){ int i=(int)SendMessageW(H(IDC_LIST),LB_GETCURSEL,0,0); if(i<0||i>(int)gSettings.profiles.size())return; gSelected=i; auto*p=SelectedProfile(); if(!p)return; bool desktop=IsDesktopSelected(); SetDesktopUi(desktop); Txt(IDC_NAME,p->name);Txt(IDC_EXE,desktop?L"":p->exePath);SendMessageW(H(IDC_ENABLED),BM_SETCHECK,desktop?BST_UNCHECKED:(p->enabled?BST_CHECKED:BST_UNCHECKED),0);SendMessageW(H(IDC_VIB),TBM_SETPOS,TRUE,p->vibrance);SendMessageW(H(IDC_BRI),TBM_SETPOS,TRUE,(LPARAM)llround(p->brightness*100));SendMessageW(H(IDC_CON),TBM_SETPOS,TRUE,(LPARAM)llround(p->contrast*100));SendMessageW(H(IDC_GAM),TBM_SETPOS,TRUE,(LPARAM)llround(p->gamma*100));UpdateSliderLabels(); }
void SaveSelected(){ auto*p=SelectedProfile(); if(!p)return; if(!IsDesktopSelected()){p->name=GetTxt(IDC_NAME);p->exePath=GetTxt(IDC_EXE);p->enabled=SendMessageW(H(IDC_ENABLED),BM_GETCHECK,0,0)==BST_CHECKED;}p->vibrance=(int)SendMessageW(H(IDC_VIB),TBM_GETPOS,0,0);p->brightness=(int)SendMessageW(H(IDC_BRI),TBM_GETPOS,0,0)/100.0;p->contrast=(int)SendMessageW(H(IDC_CON),TBM_GETPOS,0,0)/100.0;p->gamma=(int)SendMessageW(H(IDC_GAM),TBM_GETPOS,0,0)/100.0;Save();RefreshList(); }

void DrawLabel(HDC dc,const wchar_t*t,int x,int y,COLORREF c,HFONT f=nullptr){ SetBkMode(dc,TRANSPARENT);SetTextColor(dc,c);SelectObject(dc,f?f:gFont);TextOutW(dc,x,y,t,(int)wcslen(t)); }
void Fill(HDC dc,int x,int y,int w,int h,COLORREF c){HBRUSH b=CreateSolidBrush(c);RECT r{x,y,x+w,y+h};FillRect(dc,&r,b);DeleteObject(b);} 
void Paint(HWND w){PAINTSTRUCT ps;HDC dc=BeginPaint(w,&ps);RECT rc;GetClientRect(w,&rc);FillRect(dc,&rc,gBackBrush);Fill(dc,20,78,245,rc.bottom-118,C_PANEL);Fill(dc,278,78,rc.right-298,rc.bottom-118,C_PANEL);DrawLabel(dc,L"Game Profile Switcher",28,24,C_TEXT,gFontTitle);DrawLabel(dc,L"Automatic display color profiles for your games",28,52,C_MUTED);Fill(dc,rc.right-90,20,60,30,C_PANEL2);DrawLabel(dc,L"v0.3.12",rc.right-79,27,C_ACCENT);DrawLabel(dc,L"GAME PROFILES",35,92,C_MUTED,gFontBold);DrawLabel(dc,L"PROFILE SETTINGS",300,92,C_MUTED,gFontBold); SIZE z{};SelectObject(dc,gFont);GetTextExtentPoint32W(dc,gStatus.c_str(),(int)gStatus.size(),&z);DrawLabel(dc,gStatus.c_str(),rc.right-z.cx-28,rc.bottom-28,gStatusOk?C_MUTED:C_DANGER);EndPaint(w,&ps);} 

void BuildControls(){ RECT r;GetClientRect(gWnd,&r); int rightX=298,rightW=r.right-318;
 HWND list=Add(L"LISTBOX",L"",LBS_NOTIFY|LBS_OWNERDRAWFIXED|WS_VSCROLL,32,120,221,r.bottom-260,IDC_LIST); SendMessageW(list,LB_SETITEMHEIGHT,0,48);

 Add(L"STATIC",L"Profile name",0,rightX,122,160,22,IDC_LBL_NAME);
 Add(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,rightX,145,rightW,26,IDC_NAME);
 Add(L"STATIC",L"Game executable",0,rightX,182,160,22,IDC_LBL_EXE);
 Add(L"EDIT",L"",WS_BORDER|ES_AUTOHSCROLL,rightX,205,rightW-100,26,IDC_EXE);
 Add(L"BUTTON",L"Browse...",BS_PUSHBUTTON,rightX+rightW-90,202,90,32,IDC_BROWSE);
 Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX,245,20,24,IDC_ENABLED);
 Add(L"STATIC",L"Enable automatic profile",0,rightX+24,247,200,22,IDC_LBL_ENABLED);

 auto slider=[&](const wchar_t*t,int lid,int id,int vid,int y,int min,int max){
     Add(L"STATIC",t,0,rightX,y,160,22,lid);
     Add(TRACKBAR_CLASSW,L"",TBS_HORZ|TBS_NOTICKS,rightX,y+26,rightW-10,28,id);
     SendMessageW(H(id),TBM_SETRANGE,TRUE,MAKELONG(min,max));
     Add(L"STATIC",L"",SS_RIGHT,rightX+rightW-70,y,60,22,vid);
 };
 slider(L"Digital Vibrance",IDC_LBL_VIB,IDC_VIB,IDC_VALVIB,282,0,100);
 slider(L"Brightness",IDC_LBL_BRI,IDC_BRI,IDC_VALBRI,337,0,100);
 slider(L"Contrast",IDC_LBL_CON,IDC_CON,IDC_VALCON,392,0,100);
 slider(L"Gamma",IDC_LBL_GAM,IDC_GAM,IDC_VALGAM,447,50,300);

 Add(L"BUTTON",L"Save profile",BS_PUSHBUTTON,rightX,530,105,36,IDC_SAVE);
 Add(L"BUTTON",L"+  Add game",BS_PUSHBUTTON,32,r.bottom-165,112,36,IDC_ADD);
 Add(L"BUTTON",L"Remove",BS_PUSHBUTTON,154,r.bottom-165,99,36,IDC_REMOVE);
 Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX,590,20,24,IDC_STARTWIN);
 Add(L"STATIC",L"Start with Windows",0,rightX+24,592,135,22,0);
 Add(L"BUTTON",L"",BS_AUTOCHECKBOX,rightX+165,590,20,24,IDC_STARTMIN);
 Add(L"STATIC",L"Start minimized to tray",0,rightX+189,592,170,22,0);

 SendMessageW(H(IDC_STARTWIN),BM_SETCHECK,gSettings.startWindows?BST_CHECKED:BST_UNCHECKED,0);
 SendMessageW(H(IDC_STARTMIN),BM_SETCHECK,gSettings.startMinimized?BST_CHECKED:BST_UNCHECKED,0);
}
void ResizeControls(){
    RECT r;GetClientRect(gWnd,&r); int rightX=298,rightW=r.right-318;
    MoveWindow(H(IDC_LIST),32,120,221,(int)std::max<LONG>(250L,r.bottom-260),TRUE);
    MoveWindow(H(IDC_NAME),rightX,145,rightW,26,TRUE);
    MoveWindow(H(IDC_EXE),rightX,205,rightW-100,26,TRUE);
    MoveWindow(H(IDC_BROWSE),rightX+rightW-90,202,90,32,TRUE);
    MoveWindow(H(IDC_ADD),32,r.bottom-165,112,36,TRUE);
    MoveWindow(H(IDC_REMOVE),154,r.bottom-165,99,36,TRUE);
    SetDesktopUi(IsDesktopSelected());
}

void ShowMain(){ShowWindow(gWnd,SW_SHOW);SetForegroundWindow(gWnd);} void RestoreDesktop(){Apply(gSettings.desktop);gActive=gSettings.desktop.name;InvalidateRect(gWnd,nullptr,FALSE);} void UpdateTrayPause(){ModifyMenuW(gTrayMenu,ID_TRAY_PAUSE,MF_BYCOMMAND|MF_STRING,ID_TRAY_PAUSE,gPaused?L"Resume automatic switching":L"Pause automatic switching");}
LRESULT CALLBACK Proc(HWND w,UINT m,WPARAM wp,LPARAM lp){switch(m){case WM_CREATE:gWnd=w;BuildControls();RefreshList();LoadSelected();SetTimer(w,1,750,nullptr);return 0;case WM_SIZE:ResizeControls();InvalidateRect(w,nullptr,TRUE);return 0;case WM_PAINT:Paint(w);return 0;case WM_CTLCOLORSTATIC:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL);SetBkMode(dc,TRANSPARENT);return (LRESULT)gPanelBrush;}case WM_CTLCOLOREDIT:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL2);return (LRESULT)gPanel2Brush;}case WM_CTLCOLORBTN:{HDC dc=(HDC)wp;SetTextColor(dc,C_TEXT);SetBkColor(dc,C_PANEL);return (LRESULT)gPanelBrush;}case WM_DRAWITEM:{auto*d=(DRAWITEMSTRUCT*)lp;if(d->CtlID==IDC_LIST&&d->itemID!=(UINT)-1){FillRect(d->hDC,&d->rcItem,(d->itemState&ODS_SELECTED)?gPanel2Brush:gPanelBrush);bool desktop=d->itemID==0;GameProfile*p=desktop?&gSettings.desktop:&gSettings.profiles[d->itemID-1];SHFILEINFOW fi{};HICON ic=nullptr;bool destroyIcon=false;if(desktop){ic=gIcon;}else if(!p->exePath.empty()&&PathFileExistsW(p->exePath.c_str())&&SHGetFileInfoW(p->exePath.c_str(),0,&fi,sizeof(fi),SHGFI_ICON|SHGFI_SMALLICON)){ic=fi.hIcon;destroyIcon=true;}int x=d->rcItem.left+8,y=d->rcItem.top+8;if(ic){DrawIconEx(d->hDC,x,y,ic,24,24,0,nullptr,DI_NORMAL);if(destroyIcon)DestroyIcon(ic);}else{HBRUSH b=CreateSolidBrush(C_ACCENT);HGDIOBJ old=SelectObject(d->hDC,b);Ellipse(d->hDC,x,y,x+28,y+28);SelectObject(d->hDC,old);DeleteObject(b);if(!p->name.empty()){wchar_t c[2]{p->name[0],0};DrawLabel(d->hDC,c,x+9,y+5,RGB(255,255,255));}}DrawLabel(d->hDC,p->name.c_str(),x+38,d->rcItem.top+6,C_TEXT);std::wstring sub=desktop?L"Default display profile":ProcessName(p->exePath);DrawLabel(d->hDC,sub.c_str(),x+38,d->rcItem.top+25,C_MUTED);return TRUE;}break;}case WM_HSCROLL:UpdateSliderLabels();return 0;case WM_TIMER:CheckProcesses();return 0;case WM_COMMAND:{int id=LOWORD(wp);if(id==IDC_LIST&&HIWORD(wp)==LBN_SELCHANGE){LoadSelected();return 0;}switch(id){case IDC_BROWSE:{OPENFILENAMEW o{sizeof(o)};wchar_t f[MAX_PATH]{};o.hwndOwner=w;o.lpstrFilter=L"Executables (*.exe)\0*.exe\0All files\0*.*\0";o.lpstrFile=f;o.nMaxFile=MAX_PATH;o.Flags=OFN_FILEMUSTEXIST;if(GetOpenFileNameW(&o))Txt(IDC_EXE,f);break;}case IDC_SAVE:SaveSelected();break;case IDC_ADD:gSettings.profiles.push_back({});gSelected=(int)gSettings.profiles.size();Save();RefreshList();LoadSelected();break;case IDC_REMOVE:if(gSelected>0&&gSelected<=(int)gSettings.profiles.size()){gSettings.profiles.erase(gSettings.profiles.begin()+(gSelected-1));gSelected=std::max<int>(0,gSelected-1);Save();RefreshList();LoadSelected();}break;case IDC_STARTWIN:gSettings.startWindows=SendMessageW(H(IDC_STARTWIN),BM_GETCHECK,0,0)==BST_CHECKED;SetStartup(gSettings.startWindows);Save();break;case IDC_STARTMIN:gSettings.startMinimized=SendMessageW(H(IDC_STARTMIN),BM_GETCHECK,0,0)==BST_CHECKED;Save();break;case ID_TRAY_OPEN:ShowMain();break;case ID_TRAY_RESTORE:RestoreDesktop();break;case ID_TRAY_PAUSE:gPaused=!gPaused;if(gPaused)RestoreDesktop();UpdateTrayPause();break;case ID_TRAY_EXIT:gReallyExit=true;DestroyWindow(w);break;}return 0;}case WM_CLOSE:if(!gReallyExit){ShowWindow(w,SW_HIDE);return 0;}break;case WM_TRAY:if(lp==WM_LBUTTONDBLCLK){ShowMain();return 0;}if(lp==WM_RBUTTONUP||lp==WM_CONTEXTMENU){POINT p;GetCursorPos(&p);SetForegroundWindow(w);TrackPopupMenu(gTrayMenu,TPM_RIGHTBUTTON,p.x,p.y,0,w,nullptr);return 0;}break;case WM_DESTROY:KillTimer(w,1);Shell_NotifyIconW(NIM_DELETE,&gNid);if(pUnload)pUnload();if(gNv)FreeLibrary(gNv);PostQuitMessage(0);return 0;}return DefWindowProcW(w,m,wp,lp);} 

int WINAPI wWinMain(HINSTANCE h,HINSTANCE,LPWSTR cmd,int){gInst=h;INITCOMMONCONTROLSEX ic{sizeof(ic),ICC_BAR_CLASSES|ICC_STANDARD_CLASSES};InitCommonControlsEx(&ic);Load();gBackBrush=CreateSolidBrush(C_BACK);gPanelBrush=CreateSolidBrush(C_PANEL);gPanel2Brush=CreateSolidBrush(C_PANEL2);gFont=CreateFontW(-15,0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");gFontBold=CreateFontW(-15,0,0,0,FW_SEMIBOLD,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");gFontTitle=CreateFontW(-24,0,0,0,FW_BOLD,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Segoe UI");gIcon=LoadIconW(h,MAKEINTRESOURCEW(IDI_APPICON));WNDCLASSEXW wc{sizeof(wc)};wc.style=CS_HREDRAW|CS_VREDRAW;wc.lpfnWndProc=Proc;wc.hInstance=h;wc.hIcon=gIcon;wc.hIconSm=gIcon;wc.hCursor=LoadCursor(nullptr,IDC_ARROW);wc.hbrBackground=gBackBrush;wc.lpszClassName=L"GameProfileSwitcherNative";RegisterClassExW(&wc);gWnd=CreateWindowExW(0,wc.lpszClassName,L"Game Profile Switcher v0.3.12",WS_OVERLAPPEDWINDOW,CW_USEDEFAULT,CW_USEDEFAULT,900,775,nullptr,nullptr,h,nullptr);SetWindowLongPtrW(gWnd,GWLP_USERDATA,0);gTrayMenu=CreatePopupMenu();AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_OPEN,L"Open Game Profile Switcher");AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_RESTORE,L"Restore Desktop");AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_PAUSE,L"Pause automatic switching");AppendMenuW(gTrayMenu,MF_SEPARATOR,0,nullptr);AppendMenuW(gTrayMenu,MF_STRING,ID_TRAY_EXIT,L"Exit");gNid.cbSize=sizeof(gNid);gNid.hWnd=gWnd;gNid.uID=1;gNid.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;gNid.uCallbackMessage=WM_TRAY;gNid.hIcon=gIcon;wcscpy_s(gNid.szTip,L"Game Profile Switcher");Shell_NotifyIconW(NIM_ADD,&gNid);gStatusOk=InitNv();if(gStatusOk)Apply(gSettings.desktop);gActive=gSettings.desktop.name;bool min=(wcsstr(cmd,L"--minimized")!=nullptr)||gSettings.startMinimized;ShowWindow(gWnd,min?SW_HIDE:SW_SHOW);UpdateWindow(gWnd);MSG msg;while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}DeleteObject(gFont);DeleteObject(gFontBold);DeleteObject(gFontTitle);DeleteObject(gBackBrush);DeleteObject(gPanelBrush);DeleteObject(gPanel2Brush);return 0;}
