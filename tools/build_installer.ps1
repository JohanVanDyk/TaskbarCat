# Builds the shippable installer: publishes the app, then compiles installer\TaskbarCat.iss.
#
#   powershell -File tools\build_installer.ps1
#   powershell -File tools\build_installer.ps1 -SkipPublish     # reuse dist\TaskbarCat
#
# Signing (optional, and see README for why it matters):
#   powershell -File tools\build_installer.ps1 -SignTool "C:\path\signtool.exe" -CertThumbprint ABC123...
# Signs the app exe BEFORE packaging and the setup exe after, which is the order that matters:
# signing setup alone leaves the thing SmartScreen actually watches — the installed app —
# unsigned.

[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [string]$SignTool,
    [string]$CertThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root 'installer\TaskbarCat.iss'
$payload = Join-Path $root 'dist\TaskbarCat'
$appExe = Join-Path $payload 'TaskbarCat.exe'

function Find-ISCC {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "ISCC.exe not found. Install it with:  winget install -e --id JRSoftware.InnoSetup"
}

function Invoke-Sign([string]$Path) {
    if (-not $SignTool -or -not $CertThumbprint) { return }
    Write-Host "signing $Path" -ForegroundColor Cyan
    & $SignTool sign /fd SHA256 /sha1 $CertThumbprint /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed on $Path ($LASTEXITCODE)" }
}

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }
}

if (-not (Test-Path $appExe)) { throw "no payload at $appExe - run tools\publish.ps1 first" }
if (-not (Test-Path (Join-Path $payload 'assets\sprites.json'))) { throw "assets missing from $payload" }

# The app first: the installer embeds it, so signing after packaging would be too late.
Invoke-Sign $appExe

$iscc = Find-ISCC
Write-Host "compiling $iss" -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$version = (Get-Item $appExe).VersionInfo.FileVersion
$setup = Join-Path $root "dist\TaskbarCat-Setup-$version.exe"
if (-not (Test-Path $setup)) {
    # ISCC names the file from GetVersionNumbersString, which can trim differently.
    $setup = (Get-ChildItem (Join-Path $root 'dist') -Filter 'TaskbarCat-Setup-*.exe' |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

Invoke-Sign $setup

$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`ninstaller  $setup  (${mb} MB)" -ForegroundColor Green
if (-not $SignTool) {
    Write-Host "UNSIGNED - recipients will get a SmartScreen warning. See README." -ForegroundColor DarkYellow
}
