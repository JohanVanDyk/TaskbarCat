# Enumerates real top-level windows via EnumWindows.
# Get-Process MainWindowTitle uses a heuristic that skips tool windows, so it cannot
# see the cat by design. This walks the actual window list instead.

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Enum32 {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@

$results = New-Object System.Collections.ArrayList
$cb = [Enum32+EnumProc]{
    param($hwnd, $lp)
    $sb = New-Object System.Text.StringBuilder 512
    [void][Enum32]::GetWindowTextW($hwnd, $sb, 512)
    $title = $sb.ToString()
    $cb2 = New-Object System.Text.StringBuilder 512
    [void][Enum32]::GetClassNameW($hwnd, $cb2, 512)
    $cls = $cb2.ToString()
    $procId = 0
    [void][Enum32]::GetWindowThreadProcessId($hwnd, [ref]$procId)
    if ($title -match 'Taskbar' -or $cls -match 'Taskbar' -or $cls -match 'HwndWrapper') {
        [void]$results.Add([PSCustomObject]@{
            Hwnd = $hwnd; Title = $title; Class = $cls
            Visible = [Enum32]::IsWindowVisible($hwnd); Pid = $procId
        })
    }
    return $true
}
[void][Enum32]::EnumWindows($cb, [IntPtr]::Zero)
$results | Format-Table -AutoSize | Out-String -Width 200
