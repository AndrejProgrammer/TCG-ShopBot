# ShopBot installer – finds the game, checks BepInEx, copies the mod and unblocks it.
# Started by Install-ShopBot.bat (double-click). Optional: -GameDir "C:\...\TCG Card Shop Simulator"
param([string]$GameDir = "")
$ErrorActionPreference = "Stop"
$de = (Get-Culture).TwoLetterISOLanguageName -eq "de"
function Say($en, $deText) { if ($de) { Write-Host $deText } else { Write-Host $en } }
function Ok($en, $deText) { if ($de) { Write-Host "  [OK] $deText" -ForegroundColor Green } else { Write-Host "  [OK] $en" -ForegroundColor Green } }
function Warn($en, $deText) { if ($de) { Write-Host "  [!]  $deText" -ForegroundColor Yellow } else { Write-Host "  [!]  $en" -ForegroundColor Yellow } }
function Fail($en, $deText) { if ($de) { Write-Host "  [X]  $deText" -ForegroundColor Red } else { Write-Host "  [X]  $en" -ForegroundColor Red } }

Write-Host ""
Write-Host "  ShopBot installer" -ForegroundColor Cyan
Write-Host ""

# The mod files sit next to this script (release ZIP: BepInEx\plugins\ShopBot\...)
$src = Join-Path $PSScriptRoot "BepInEx\plugins\ShopBot"
if (-not (Test-Path "$src\ShopBot.dll")) {
  Fail "ShopBot.dll not found next to the installer. Extract the whole ZIP first, then start Install-ShopBot.bat from the extracted folder." `
       "ShopBot.dll liegt nicht neben dem Installer. Erst die ganze ZIP entpacken, dann Install-ShopBot.bat aus dem entpackten Ordner starten."
  exit 1
}

# ---- 1) Find the game folder ----
$gameName = "TCG Card Shop Simulator"
function IsGame($p) { return $p -and (Test-Path (Join-Path $p "Card Shop Simulator.exe")) }
if (-not (IsGame $GameDir)) {
  $cands = New-Object System.Collections.Generic.List[string]
  $steam = $null
  foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
    try { $v = Get-ItemProperty $key -ErrorAction Stop; if ($v.SteamPath) { $steam = $v.SteamPath } elseif ($v.InstallPath) { $steam = $v.InstallPath } } catch { }
    if ($steam) { break }
  }
  if ($steam) {
    $steam = $steam -replace "/", "\"
    $cands.Add((Join-Path $steam "steamapps\common\$gameName"))
    # all Steam libraries (also on other drives)
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
      foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
        $lib = $m.Groups[1].Value -replace "\\\\", "\"
        $cands.Add((Join-Path $lib "steamapps\common\$gameName"))
      }
    }
  }
  foreach ($d in "C", "D", "E", "F", "G") {
    $cands.Add("${d}:\Program Files (x86)\Steam\steamapps\common\$gameName")
    $cands.Add("${d}:\SteamLibrary\steamapps\common\$gameName")
    $cands.Add("${d}:\steam\steamapps\common\$gameName")
  }
  $GameDir = $cands | Where-Object { IsGame $_ } | Select-Object -First 1
}
if (-not (IsGame $GameDir)) {
  Warn "Game folder not found automatically." "Spielordner nicht automatisch gefunden."
  Say "Steam -> Library -> right-click TCG Card Shop Simulator -> Manage -> Browse local files, copy the path and paste it here:" `
      "Steam -> Bibliothek -> Rechtsklick auf TCG Card Shop Simulator -> Verwalten -> Lokale Dateien durchsuchen, Pfad kopieren und hier einfügen:"
  $GameDir = (Read-Host "  Path / Pfad").Trim('"', ' ')
  if (-not (IsGame $GameDir)) { Fail "No 'Card Shop Simulator.exe' in that folder." "In diesem Ordner liegt keine 'Card Shop Simulator.exe'."; exit 1 }
}
Ok "Game: $GameDir" "Spiel: $GameDir"

# ---- 2) Game must be closed ----
if (Get-Process -Name "Card Shop Simulator" -ErrorAction SilentlyContinue) {
  Warn "The game is running – please close it, then press Enter." "Das Spiel läuft – bitte beenden, dann Enter drücken."
  Read-Host | Out-Null
}

# ---- 3) BepInEx 5 ----
$bep = Join-Path $GameDir "BepInEx"
if (-not (Test-Path (Join-Path $bep "core\BepInEx.dll"))) {
  if (Test-Path (Join-Path $bep "core\BepInEx.Core.dll")) {
    Fail "BepInEx 6 found – ShopBot needs BepInEx 5.4.x." "BepInEx 6 gefunden – ShopBot braucht BepInEx 5.4.x."
  } else {
    Fail "BepInEx is not installed." "BepInEx ist nicht installiert."
  }
  Say "Download BepInEx 5.4.x (win_x64), extract it into the game folder, start the game once, then run this installer again." `
      "BepInEx 5.4.x (win_x64) herunterladen, in den Spielordner entpacken, das Spiel einmal starten, dann diesen Installer nochmal starten."
  Say "(Using the Real TCG Overhaul mod manager? It installs BepInEx for you.)" "(Mit dem Real-TCG-Overhaul-Mod-Manager ist BepInEx schon dabei.)"
  Start-Process "https://github.com/BepInEx/BepInEx/releases"
  exit 1
}
Ok "BepInEx 5 found" "BepInEx 5 gefunden"

# ---- 4) Copy + unblock ----
$dst = Join-Path $bep "plugins\ShopBot"
$update = Test-Path (Join-Path $dst "ShopBot.dll")
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item -Path (Join-Path $src "*") -Destination $dst -Recurse -Force
Get-ChildItem $dst -Recurse -File | Unblock-File
if ($update) { Ok "ShopBot updated (your settings are kept)" "ShopBot aktualisiert (deine Einstellungen bleiben)" }
else { Ok "ShopBot installed: $dst" "ShopBot installiert: $dst" }

# ---- 5) Recommended mods ----
Write-Host ""
Say "Recommended mods (without them that feature is off):" "Empfohlene Mods (ohne sie ist die jeweilige Funktion aus):"
$plugins = Get-ChildItem (Join-Path $bep "plugins") -Recurse -Filter *.dll -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
$mods = @(
  @{ n = "Collection Tracker";   p = "CollectionTracker"; en = "collection, buying packs";       d = "Sammlung, Packs kaufen" },
  @{ n = "CardGrade Overhaul";   p = "CardGrade";         en = "grading at PSA";                 d = "Graden bei PSA" },
  @{ n = "Upgrades";             p = "Upgrades";          en = "collection point upgrades";      d = "Sammelpunkte-Upgrades" },
  @{ n = "ConfigurationManager"; p = "ConfigurationManager"; en = "settings with F1";            d = "Einstellungen mit F1" }
)
foreach ($m in $mods) {
  if ($plugins | Where-Object { $_ -like "*$($m.p)*" }) { Ok "$($m.n)" "$($m.n)" }
  else { Warn "$($m.n) missing – $($m.en)" "$($m.n) fehlt – $($m.d)" }
}

Write-Host ""
Say "Done! Start the game, load your save – after 10 seconds the bot starts. F9 = dashboard, F8 = bot on/off." `
    "Fertig! Spiel starten, Spielstand laden – nach 10 Sekunden legt der Bot los. F9 = Dashboard, F8 = Bot an/aus."
Say "Tip: back up your save first (%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator)." `
    "Tipp: Vorher den Spielstand sichern (%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator)."
