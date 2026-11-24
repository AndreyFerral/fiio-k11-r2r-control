param(
    [switch]$IncludeTests,
    [switch]$Release,
    [switch]$FrameworkDependent,
    [string]$OfficialDir = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $repoRoot 'dist'
$releaseDir = Join-Path $repoRoot 'release'
if ([string]::IsNullOrWhiteSpace($OfficialDir)) {
    $OfficialDir = Join-Path $repoRoot 'firmware\official\K11 R2R M79 Firmware'
}
$officialHex = Join-Path $OfficialDir 'k11R2R_app1_m79.hex'
$officialFlasher = Join-Path $OfficialDir 'GD32_MCU_HID_IAP_Programmer.exe'
$patcherPy = Join-Path $repoRoot 'firmware\patcher\patch_m79.py'
$trayProj = Join-Path $repoRoot 'tray\K11Control\K11Control.csproj'
$testsProj = Join-Path $repoRoot 'tray\K11Control.Tests\K11Control.Tests.csproj'

[xml]$projXml = Get-Content $trayProj
$appVersion = ($projXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($appVersion)) { $appVersion = '1.0.0' }

if ($IncludeTests) {
    Write-Host '=== Running Python patcher tests ==='
    python -m unittest discover -s (Join-Path $repoRoot 'firmware\patcher\tests') -v
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$objScratch = Join-Path $repoRoot 'tray\K11Control\obj'
New-Item -ItemType Directory -Path $objScratch -Force | Out-Null
Get-ChildItem $objScratch -Filter '_running_old_*.exe' -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { }
}
$scTemp = Join-Path $objScratch '_sc_test'
if (Test-Path $scTemp) { Remove-Item $scTemp -Recurse -Force -ErrorAction SilentlyContinue }

if (Test-Path $distDir) {
    $distExe = Join-Path $distDir 'K11Control.exe'
    $distRunningTmp = Join-Path $distDir '_running_K11Control.exe'
    foreach ($candidate in @($distExe, $distRunningTmp)) {
        if (Test-Path $candidate) {
            try {
                Remove-Item $candidate -Force -ErrorAction Stop
            }
            catch {
                # Same-volume NTFS rename lets us publish a fresh K11Control.exe cleanly while the old tray instance stays running.
                $stash = Join-Path $objScratch ("_running_old_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + ".exe")
                Move-Item -Path $candidate -Destination $stash -Force
                Write-Warning "Moved running K11Control.exe aside to $stash; restart K11Control.exe from dist\ to use the new build."
            }
        }
    }
    Get-ChildItem $distDir -Force | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$hasOfficialHex = Test-Path $officialHex
if ($hasOfficialHex) {
    Write-Host '=== Generating firmware HEX images into dist\ ==='
    python $patcherPy --input $officialHex --profile full --out-dir $distDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    python $patcherPy --input $officialHex --profile stage0-diag --out-dir $distDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    python $patcherPy --input $officialHex --profile stage0-diag --transparent-vol --out-dir $distDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    python $patcherPy --input $officialHex --profile no-lo --out-dir $distDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Remove .bin copies from dist\ to keep dist\ tidy for end-user flashing (keep all .hex files)
    Get-ChildItem $distDir -Filter '*.bin' | Remove-Item -Force

    Write-Host '=== Copying local programmer and rollback HEX into dist\ ==='
    Copy-Item $officialHex (Join-Path $distDir 'k11R2R_app1_m79_official.hex') -Force
    if (Test-Path $officialFlasher) {
        Copy-Item $officialFlasher (Join-Path $distDir 'GD32_MCU_HID_IAP_Programmer.exe') -Force
    }
}
elseif ($Release) {
    throw "Release packaging requires official k11R2R_app1_m79.hex at '$officialHex' (or pass -OfficialDir <path>)."
}
else {
    Write-Warning "Official firmware HEX not found at '$officialHex'. Skipping firmware HEX generation; building K11Control.exe only."
}

$useSelfContained = -not $FrameworkDependent
Write-Host ("=== Publishing K11Control v$appVersion into dist\ (self-contained=$useSelfContained) ===")
if ($useSelfContained) {
    dotnet publish $trayProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $distDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
else {
    dotnet publish $trayProj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $distDir 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Falling back to standard framework-dependent publish...'
        dotnet publish $trayProj -c Release -o $distDir
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

if ($IncludeTests) {
    Write-Host '=== Running K11Control.Tests ==='
    dotnet run --project $testsProj -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Release) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    $zipName = "FiiO-K11-R2R-Control-v$appVersion-win-x64.zip"
    $zipPath = Join-Path $releaseDir $zipName
    $stageDir = Join-Path $objScratch '_release_stage'
    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

    Copy-Item (Join-Path $distDir 'K11Control.exe') (Join-Path $stageDir 'K11Control.exe') -Force
    Copy-Item (Join-Path $distDir 'k11R2R_m79_full.hex') (Join-Path $stageDir 'k11R2R_m79_full.hex') -Force
    Copy-Item (Join-Path $distDir 'k11R2R_m79_no-lo.hex') (Join-Path $stageDir 'k11R2R_m79_no-lo.hex') -Force

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Remove-Item $stageDir -Recurse -Force
    Write-Host "`n=== Release archive ready: $zipPath ==="
    Get-Item $zipPath | Format-Table Name, Length
}

# Ask Windows Explorer to flush any stale cached generic icon for K11Control.exe
try {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WinShellIconRefresh {
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@ -ErrorAction SilentlyContinue
    $exePtr = [System.Runtime.InteropServices.Marshal]::StringToHGlobalUni((Join-Path $distDir 'K11Control.exe'))
    [WinShellIconRefresh]::SHChangeNotify(0x00002000, 0x0005, $exePtr, [IntPtr]::Zero) # SHCNE_UPDATEITEM | SHCNF_PATHW
    [WinShellIconRefresh]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero) # SHCNE_ASSOCCHANGED
    [System.Runtime.InteropServices.Marshal]::FreeHGlobal($exePtr)
}
catch { }

Write-Host "`n=== Bundle ready: $distDir ==="
Get-ChildItem $distDir | Format-Table Name, Length
