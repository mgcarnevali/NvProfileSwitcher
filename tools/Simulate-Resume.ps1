<#
.SYNOPSIS
    Simulates the return from sleep or hibernation for a running NvProfileSwitcher, without
    suspending Windows.

.DESCRIPTION
    Windows broadcasts WM_POWERBROADCAST to every top-level window when it resumes. This script
    posts the same message, with the same resume notifications, to every top-level window of the
    NvProfileSwitcher process, visible or hidden in the tray, so the application runs exactly the
    recovery it runs after a real resume: the NVIDIA API is initialized again, the displays are
    refreshed with new handles and every display is written with what it should show.

    To see the recovery, change the colors of a display from outside first (for example, digital
    vibrance in the NVIDIA Control Panel); within about a second of running the script, each
    display shows its profile again, the live preview included.

.PARAMETER Notification
    Both (default) sends PBT_APMRESUMESUSPEND and PBT_APMRESUMEAUTOMATIC, as Windows does after a
    resume triggered by the user; Automatic sends only PBT_APMRESUMEAUTOMATIC.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tools\Simulate-Resume.ps1
#>
param(
    [ValidateSet("Both", "Automatic")]
    [string] $Notification = "Both"
)

$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class ResumeSimulator
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static List<IntPtr> TopLevelWindows(uint processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId)
                windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
"@

$WmPowerBroadcast = 0x0218
$PbtApmResumeSuspend = 0x0007
$PbtApmResumeAutomatic = 0x0012

$processes = @(Get-Process -Name "NvProfileSwitcher" -ErrorAction SilentlyContinue)
if ($processes.Count -eq 0) {
    throw "NvProfileSwitcher is not running."
}

$notifications = if ($Notification -eq "Both") { @($PbtApmResumeSuspend, $PbtApmResumeAutomatic) } else { @($PbtApmResumeAutomatic) }

foreach ($process in $processes) {
    $windows = [ResumeSimulator]::TopLevelWindows([uint32]$process.Id)
    foreach ($window in $windows) {
        foreach ($code in $notifications) {
            [void][ResumeSimulator]::PostMessage($window, $WmPowerBroadcast, [IntPtr]$code, [IntPtr]::Zero)
        }
    }

    Write-Host "Resume posted to $($windows.Count) window(s) of NvProfileSwitcher (PID $($process.Id))."
}
