# Builds the portable Snapzy for Windows package.
$ErrorActionPreference = "Stop"
$version = "1.2.3"
$root = $PSScriptRoot
$out = Join-Path $root "publish\Snapzy"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish (Join-Path $root "src\Snapzy.App") -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Bundle ffmpeg (pinned copy; source documented in README)
New-Item -ItemType Directory -Force (Join-Path $out "ffmpeg") | Out-Null
Copy-Item "C:\Windows\system32\ffmpeg.exe" (Join-Path $out "ffmpeg\ffmpeg.exe")

# Verify markers + smoke essentials
if (-not (Test-Path (Join-Path $out "Snapzy.exe"))) { throw "Snapzy.exe missing" }
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
