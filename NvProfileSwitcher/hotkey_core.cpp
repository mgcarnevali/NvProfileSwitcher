#include "hotkey_core.h"

namespace nvps {

std::uint8_t HotkeyKey(HotkeyValue hotkey) noexcept{
    return static_cast<std::uint8_t>(hotkey&0x00ff);
}

std::uint8_t HotkeyFlags(HotkeyValue hotkey) noexcept{
    return static_cast<std::uint8_t>((hotkey>>8)&0x00ff);
}

bool IsCtrlAltHotkey(HotkeyValue hotkey) noexcept{
    const auto flags=HotkeyFlags(hotkey);
    return (flags&(HotkeyControl|HotkeyAlt))==(HotkeyControl|HotkeyAlt)
        && !(flags&HotkeyShift);
}

bool NeedsHotkeyModifier(HotkeyValue hotkey) noexcept{
    const auto key=HotkeyKey(hotkey);
    if(!key) return false;
    const bool functionKey=key>=VirtualKeyF1&&key<=VirtualKeyF24;
    return !functionKey&&!(HotkeyFlags(hotkey)&(HotkeyControl|HotkeyAlt));
}

bool HotkeysConflict(HotkeyValue first,HotkeyValue second) noexcept{
    return HotkeyKey(first)!=0&&first==second;
}

} // namespace nvps
