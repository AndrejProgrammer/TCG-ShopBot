# Builds the release ZIP: dist\ShopBot-<version>.zip
# Contents (extract into the game folder):
#   BepInEx\plugins\ShopBot\ShopBot.dll
#   BepInEx\plugins\ShopBot\lang\en.txt       (template for translators; the DLL already contains en + de)
#   BepInEx\plugins\ShopBot\README.md, README.de.md, LICENSE, CHANGELOG.md
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# Version from the BepInPlugin attribute
$m = Select-String -Path "$root\Plugin.cs" -Pattern 'BepInPlugin\("[^"]+", "[^"]+", "([^"]+)"\)'
if (-not $m) { throw "Version not found in Plugin.cs" }
$version = $m.Matches[0].Groups[1].Value
Write-Host "ShopBot $version"

# Refresh translation keys and stop if German is incomplete
& node "$root\tools\i18n-keys.js"
if ($LASTEXITCODE -ne 0) { throw "i18n-keys.js failed" }

& dotnet build -c Release -p:CopyToGame=false --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$dll = "$root\bin\Release\netstandard2.1\ShopBot.dll"
$stage = "$root\dist\stage"
$plug = "$stage\BepInEx\plugins\ShopBot"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$plug\lang" | Out-Null
Copy-Item $dll $plug
Copy-Item "$root\lang\en.txt" "$plug\lang\en.txt.template"
Copy-Item "$root\README.md", "$root\README.de.md", "$root\LICENSE", "$root\CHANGELOG.md" $plug

$zip = "$root\dist\ShopBot-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# ZIP with forward slashes (Compress-Archive in PowerShell 5 writes backslashes, which some tools/mod managers misread)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [System.IO.File]::Open($zip, [System.IO.FileMode]::CreateNew)
$archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
try {
  Get-ChildItem $stage -Recurse -File | ForEach-Object {
    $name = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $archive.Dispose(); $fs.Dispose() }
Remove-Item $stage -Recurse -Force
$size = [math]::Round((Get-Item $zip).Length / 1KB)
Write-Host "-> $zip ($size KB)"
