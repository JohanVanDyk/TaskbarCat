# Builds the shippable drop: one self-contained exe plus the assets folder, zipped.
#
# Run from Windows or from WSL via:  powershell.exe -File tools/publish.ps1
#
#   -FrameworkDependent   much smaller, but needs the .NET 8 Desktop Runtime on the target
#   -Output <dir>         defaults to dist/

[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [string]$Output = "dist"
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root 'src\TaskbarCat.App\TaskbarCat.App.csproj'
$out  = Join-Path $root $Output
$stage = Join-Path $out 'TaskbarCat'

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# The icon is generated art, but assets/app.ico is committed, so a machine without Pillow can
# still cut a release — it just ships the checked-in icon. Only a hard failure is worth a word.
if (Get-Command python -ErrorAction SilentlyContinue) {
    # ErrorActionPreference=Stop promotes anything a native tool writes to stderr into a
    # terminating error, so this one call runs with it relaxed.
    try {
        $ErrorActionPreference = 'Continue'
        & python (Join-Path $root 'tools\make_icon.py') 2>&1 | Out-Null
    } finally { $ErrorActionPreference = 'Stop' }
    if ($LASTEXITCODE -ne 0) { Write-Host 'note: icon not regenerated (needs Pillow) - using committed assets/app.ico' -ForegroundColor DarkYellow }
}

$args = @(
    'publish', $proj,
    '-c', 'Release',
    '-r', 'win-x64',
    '-p:PublishSingleFile=true',
    '-o', $stage
)
if ($FrameworkDependent) { $args += '--self-contained:false' }

Write-Host "publishing -> $stage" -ForegroundColor Cyan
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# The bundler leaves these next to the exe; they are debug scaffolding, not part of the drop.
Get-ChildItem $stage -Filter '*.xml' | Remove-Item -Force -ErrorAction SilentlyContinue

$exe = Join-Path $stage 'TaskbarCat.exe'
if (-not (Test-Path $exe)) { throw "expected $exe" }
if (-not (Test-Path (Join-Path $stage 'assets\sprites.json'))) { throw 'assets missing from publish output' }

$version = (Get-Item $exe).VersionInfo.FileVersion
$zip = Join-Path $out "TaskbarCat-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "`nexe  $exe" -ForegroundColor Green
Write-Host "zip  $zip  (${mb} MB)" -ForegroundColor Green
# --selftest is the switch that arms the harness; the others only configure it. Passing
# only --selftest-seconds starts a normal, permanent cat, which is a confusing way to
# discover that from a smoke test.
Write-Host "`nSmoke test:  & '$exe' --selftest --selftest-seconds=6 --selftest-out=$root\artifacts\selftest.txt" -ForegroundColor DarkGray
