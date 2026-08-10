# Verifies the alpha hit-test: clicks must pass THROUGH the cat's transparent pixels to
# whatever is behind, and land on the cat only where it is actually drawn.
#
# Uses WindowFromPoint rather than synthetic clicks: it asks the same question the mouse
# does (which window owns this pixel) without moving the cursor, so the auto-hide taskbar
# never pops up and interferes.
#
# Locates the cat and derives probe points from its CURRENT rect — the cat walks, so
# hard-coded coordinates go stale between runs.

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class HT {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[void][HT]::SetProcessDpiAwarenessContext([IntPtr](-4))

$cat = [IntPtr]::Zero
$cb = [HT+EnumProc]{
    param($hwnd, $lp)
    if (-not [HT]::IsWindowVisible($hwnd)) { return $true }
    $sb = New-Object System.Text.StringBuilder 512
    [void][HT]::GetWindowTextW($hwnd, $sb, 512)
    if ($sb.ToString() -eq 'TaskbarCat') { $script:cat = $hwnd; return $false }
    return $true
}
[void][HT]::EnumWindows($cb, [IntPtr]::Zero)

if ($cat -eq [IntPtr]::Zero) { Write-Output 'CAT_NOT_FOUND'; exit 2 }

$r = New-Object HT+RECT
[void][HT]::GetWindowRect($cat, [ref]$r)
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
Write-Output "cat hwnd=$cat rect=$($r.Left),$($r.Top) size=${w}x${h}"

function Probe([string]$label, [double]$fx, [double]$fy) {
    $p = New-Object HT+POINT
    $p.X = [int]($r.Left + $w * $fx)
    $p.Y = [int]($r.Top + $h * $fy)
    $hit = [HT]::WindowFromPoint($p)
    $root = if ($hit -eq [IntPtr]::Zero) { [IntPtr]::Zero } else { [HT]::GetAncestor($hit, 2) }
    $isCat = ($root -eq $cat)
    # Only report cat / not-cat: the window behind is whatever the user has open.
    $owner = if ($isCat) { 'CAT' } elseif ($root -eq [IntPtr]::Zero) { 'none' } else { 'behind' }
    "{0,-20} ({1},{2}) -> {3}" -f $label, $p.X, $p.Y, $owner
}

# Fractions of the window box. Corners are transparent in every cat pose; the
# lower-centre is where the body always is.
Probe 'opaque body'    0.50 0.78
Probe 'transparent TL' 0.05 0.05
Probe 'transparent TR' 0.95 0.05
Probe 'transparent BL' 0.03 0.97
