# Builds the portable Snapzy for Windows package.
$ErrorActionPreference = "Stop"
$version = "1.5.0"
$root = $PSScriptRoot
$out = Join-Path $root "publish\Snapzy"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root "src\Snapzy.App") -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Bundle ffmpeg (pinned copy; source documented in README)
New-Item -ItemType Directory -Force (Join-Path $out "ffmpeg") | Out-Null
Copy-Item "C:\Windows\system32\ffmpeg.exe" (Join-Path $out "ffmpeg\ffmpeg.exe")

# The exe must NOT carry the CET_COMPAT PE flag: CET shadow stacks fail-fast the
# process when the file dialog loads a CET-incompatible shell extension on
# Windows 11 (dotnet/wpf#10305). Guarded here so a csproj regression cannot ship.
function Test-CetCompat($exe) {
    $b = [IO.File]::ReadAllBytes($exe)
    $pe = [BitConverter]::ToInt32($b, 0x3C)
    $numSections = [BitConverter]::ToUInt16($b, $pe + 6)
    $optSize = [BitConverter]::ToUInt16($b, $pe + 20)
    $opt = $pe + 24
    $dbgRva = [BitConverter]::ToInt32($b, $opt + 112 + 48)
    $dbgSize = [BitConverter]::ToInt32($b, $opt + 112 + 52)
    if ($dbgRva -eq 0) { return $false }
    $sec = $opt + $optSize
    $dbgOff = -1
    for ($i = 0; $i -lt $numSections; $i++) {
        $s = $sec + $i * 40
        $va = [BitConverter]::ToInt32($b, $s + 12)
        $vsz = [BitConverter]::ToInt32($b, $s + 8)
        $raw = [BitConverter]::ToInt32($b, $s + 20)
        if ($dbgRva -ge $va -and $dbgRva -lt ($va + $vsz)) { $dbgOff = $raw + ($dbgRva - $va); break }
    }
    if ($dbgOff -lt 0) { return $false }
    for ($i = 0; $i -lt [math]::Floor($dbgSize / 28); $i++) {
        $e = $dbgOff + $i * 28
        if ([BitConverter]::ToInt32($b, $e + 12) -eq 20) {  # IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS
            $ptr = [BitConverter]::ToInt32($b, $e + 24)
            return (([BitConverter]::ToInt32($b, $ptr)) -band 1) -eq 1
        }
    }
    return $false
}

# Verify markers + smoke essentials
if (-not (Test-Path (Join-Path $out "Snapzy.exe"))) { throw "Snapzy.exe missing" }
if (Test-CetCompat (Join-Path $out "Snapzy.exe")) { throw "Snapzy.exe has CET_COMPAT set - CETCompat=false was lost (Win11 file dialog crash, dotnet/wpf#10305)" }
if (-not (Test-Path (Join-Path $out "zh-CN\Snapzy.Core.resources.dll"))) { throw "zh-CN satellite missing" }
if (-not (Test-Path (Join-Path $out "Assets\snapzy.ico"))) { throw "tray icon asset missing" }
& (Join-Path $out "ffmpeg\ffmpeg.exe") -version 2>&1 | Select-Object -First 1

$zip = Join-Path $root "publish\Snapzy-Windows-v$version-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $out -DestinationPath $zip
$sizeMB = [math]::Round((Get-Item $zip).Length / 1MB, 1)
"Zip: $zip ($sizeMB MB)"
$folderMB = [math]::Round((Get-ChildItem -Recurse $out | Measure-Object Length -Sum).Sum / 1MB, 1)
"Folder: $folderMB MB (budget: 180 MB)"
