#include "../hotkey_core.h"

#include <iostream>

namespace {

int failures=0;

void Expect(bool condition,const char* testName){
    if(condition) return;
    std::cerr<<"FAILED: "<<testName<<'\n';
    ++failures;
}

constexpr nvps::HotkeyValue Hotkey(std::uint8_t key,std::uint8_t flags=0){
    return static_cast<nvps::HotkeyValue>(key)
        |(static_cast<nvps::HotkeyValue>(flags)<<8);
}

} // namespace

int RunHotkeyCoreTests(){
    using namespace nvps;

    Expect(!NeedsHotkeyModifier(0),"an empty hotkey is allowed");
    Expect(NeedsHotkeyModifier(Hotkey('J')),"a letter requires Ctrl or Alt");
    Expect(NeedsHotkeyModifier(Hotkey('J',HotkeyShift)),"Shift alone is insufficient");
    Expect(!NeedsHotkeyModifier(Hotkey('J',HotkeyControl)),"Ctrl plus a letter is allowed");
    Expect(!NeedsHotkeyModifier(Hotkey('J',HotkeyAlt)),"Alt plus a letter is allowed");
    Expect(!NeedsHotkeyModifier(Hotkey(VirtualKeyF1)),"F1 is allowed without a modifier");
    Expect(!NeedsHotkeyModifier(Hotkey(VirtualKeyF24)),"F24 is allowed without a modifier");
    Expect(NeedsHotkeyModifier(Hotkey(VirtualKeyF1-1)),"the key before F1 is not a function key");
    Expect(NeedsHotkeyModifier(Hotkey(VirtualKeyF24+1)),"the key after F24 is not a function key");

    Expect(IsCtrlAltHotkey(Hotkey('J',HotkeyControl|HotkeyAlt)),"Ctrl Alt is blocked for AltGr safety");
    Expect(!IsCtrlAltHotkey(Hotkey('J',HotkeyControl|HotkeyAlt|HotkeyShift)),"Ctrl Alt Shift remains supported");
    Expect(!IsCtrlAltHotkey(Hotkey('J',HotkeyControl)),"Ctrl alone is not treated as AltGr");
    Expect(!IsCtrlAltHotkey(Hotkey('J',HotkeyAlt)),"Alt alone is not treated as AltGr");

    const auto ctrlJ=Hotkey('J',HotkeyControl);
    Expect(HotkeysConflict(ctrlJ,ctrlJ),"identical assigned hotkeys conflict");
    Expect(!HotkeysConflict(ctrlJ,Hotkey('K',HotkeyControl)),"different keys do not conflict");
    Expect(!HotkeysConflict(ctrlJ,Hotkey('J',HotkeyAlt)),"different modifiers do not conflict");
    Expect(!HotkeysConflict(0,0),"two empty hotkeys do not conflict");
    Expect(!HotkeysConflict(0,ctrlJ),"an empty hotkey does not conflict");

    return failures;
}
