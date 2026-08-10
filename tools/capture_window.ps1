# Captures a single window's pixels to PNG.
#
# Why not CopyFromScreen: the cat is a layered (AllowsTransparency) window, which plain
# BitBlt skips, and CopyPixelOperation.CaptureBlt returns an all-black frame under DWM.
# PrintWindow with PW_RENDERFULLCONTENT (0x2) asks the window to render itself, and is
# the only one of the three that works here.
#
# Why EnumWindows rather than FindWindow: PowerShell marshals a $null string argument as
# an empty string, so FindWindow($null, $title) filters on class name "" and finds nothing.
#
# Usage: powershell.exe -NoProfile -File tools\capture_window.ps1 -Title TaskbarCat -Out out.png

param(
    [string]$Title = "TaskbarCat",
    [string]$Out = "C:\dev\TaskbarCat\artifacts\window.png"
)

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Dpi {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
}
public static class Cap {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

# MUST come before any window query. A DPI-unaware caller gets VIRTUALISED coordinates:
# on a 125% display GetWindowRect silently returns rect * 0.8, so a 200x160 window reads
# back as 160x128 and the capture is a downscaled lie. -4 = PER_MONITOR_AWARE_V2.
[void][Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4))

$found = [IntPtr]::Zero
$cb = [Cap+EnumProc]{
    param($hwnd, $lp)
    if (-not [Cap]::IsWindowVisible($hwnd)) { return $true }
    $sb = New-Object System.Text.StringBuilder 512
    [void][Cap]::GetWindowTextW($hwnd, $sb, 512)
    if ($sb.ToString() -eq $Title) { $script:found = $hwnd; return $false }
    return $true
}
[void][Cap]::EnumWindows($cb, [IntPtr]::Zero)

if ($found -eq [IntPtr]::Zero) { Write-Output "NOT_FOUND"; exit 2 }

$r = New-Object Cap+RECT
[void][Cap]::GetWindowRect($found, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Output "hwnd=$found rect=$($r.Left),$($r.Top),$($r.Right),$($r.Bottom) size=${w}x${h}"

if ($w -le 0 -or $h -le 0) { Write-Output "EMPTY_RECT"; exit 3 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Cap]::PrintWindow($found, $hdc, 0x2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "printwindow=$ok saved=$Out"
