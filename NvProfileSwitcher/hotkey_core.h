#pragma once

#include <cstdint>

namespace nvps {

using HotkeyValue=std::uint16_t;

constexpr std::uint8_t HotkeyShift=0x01;
constexpr std::uint8_t HotkeyControl=0x02;
constexpr std::uint8_t HotkeyAlt=0x04;
constexpr std::uint8_t VirtualKeyF1=0x70;
constexpr std::uint8_t VirtualKeyF24=0x87;

std::uint8_t HotkeyKey(HotkeyValue hotkey) noexcept;
std::uint8_t HotkeyFlags(HotkeyValue hotkey) noexcept;
bool IsCtrlAltHotkey(HotkeyValue hotkey) noexcept;
bool NeedsHotkeyModifier(HotkeyValue hotkey) noexcept;
bool HotkeysConflict(HotkeyValue first,HotkeyValue second) noexcept;

} // namespace nvps
