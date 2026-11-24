# ShopBot Testbericht – läuft eigenständig im Hintergrund.
# Wartet auf den Spielstart, liest das BepInEx-Log und schreibt jede Minute TESTBERICHT.md (neben diesem Ordner).
# Endet, wenn das Spiel beendet wird oder nach 3 Stunden.
# Aufruf: powershell -ExecutionPolicy Bypass -File tools\testbericht.ps1 [-game "C:\...\TCG Card Shop Simulator"]
param([string]$game = "")

if (-not $game) {
  # Spielordner automatisch suchen (Standard-Steam-Pfade)
  $cands = @("C:\Program Files (x86)\Steam\steamapps\common\TCG Card Shop Simulator",
             "D:\steam\steamapps\common\TCG Card Shop Simulator",
             "D:\SteamLibrary\steamapps\common\TCG Card Shop Simulator",
             "E:\SteamLibrary\steamapps\common\TCG Card Shop Simulator")
  $game = $cands | Where-Object { Test-Path "$_\BepInEx" } | Select-Object -First 1
  if (-not $game) { Write-Host "Spielordner nicht gefunden – bitte mit -game angeben."; exit 1 }
}
$log    = "$game\BepInEx\LogOutput.log"
$player = "$env:USERPROFILE\AppData\LocalLow\OPNeonGames\Card Shop Simulator\Player.log"
$out    = Join-Path (Split-Path $PSScriptRoot -Parent) "TESTBERICHT.md"
$start  = Get-Date

$checks = @(
  @{ n="Bot startet";                       p="ShopBot geladen";                                   test="neu" },
  @{ n="Rechnungen bezahlen";               p="Rechnungen bezahlt";                                test="alt" },
  @{ n="Bonus an Arbeiter";                 p="Bonus gezahlt";                                     test="alt" },
  @{ n="Einsatzplan";                       p="Einsatzplan Tag";                                   test="alt" },
  @{ n="Sammlung: Vorrat berechnet";        p="\[Sammlung\] Opener";                               test="alt" },
  @{ n="Sammlung: Packs gekauft";           p="Gekauft: \d+ Kartons";                              test="alt" },
  @{ n="Tageslimit greift";                 p="Tageslimit erreicht";                               test="neu" },
  @{ n="Selbst einräumen (Regale)";         p="Selbst eingeräumt";                                 test="neu" },
  @{ n="Selbst einräumen (Vitrinen)";       p="Vitrinen selbst befüllt";                           test="alt" },
  @{ n="Opener selbst befüllt (v0.2)";      p="Opener selbst befüllt";                             test="neu" },
  @{ n="Arbeiter eingestellt (v0.2)";       p="Neuer Arbeiter eingestellt";                        test="neu" },
  @{ n="Bot-Kasse (v0.4)";                  p="Bot-Kasse:";                                        test="neu" },
  @{ n="Einsortieren (v0.3)";               p="Einsortiert:";                                      test="neu" },
  @{ n="Lizenzen freischalten";             p="Neue Lizenz";                                       test="neu" },
  @{ n="Erweiterungen kaufen";              p="Laden-Erweiterung|Lager-Erweiterung|Lager \(Shop B\)"; test="neu" },
  @{ n="Bewertungen: nachbestellt";         p="Kunden vermissen";                                  test="alt" },
  @{ n="Bewertungen: Lager statt Kauf";     p="liegt aber im Lager";                               test="neu" },
  @{ n="Aushilfe Regale";                   p="Regale ein\b|räumt 10 Min";                         test="neu" },
  @{ n="Preis bei Beschwerde gesenkt";      p="zu teuer – Aufschlag";                              test="alt" },
  @{ n="Nachbestellen Packs/Displays";      p="Nachbestellt:";                                     test="alt" },
  @{ n="Kunden: Ankauf";                    p="Gekauft für";                                       test="alt" },
  @{ n="Kunden: Tausch angenommen";         p="Tausch angenommen";                                 test="alt" },
  @{ n="Graden (PSA)";                      p="Graden: \d+ Karten";                                test="alt" },
  @{ n="Grading-Pakete öffnen";             p="Grading-Paket geöffnet";                            test="neu" },
  @{ n="Kartenablage";                      p="Kartenablage:";                                     test="alt" },
  @{ n="Opener einsammeln/starten";         p="Opener eingesammelt|Opener gestartet";              test="alt" },
  @{ n="Upgrades (Sammelpunkte)";           p="Upgrade gekauft";                                   test="alt" },
  @{ n="Tagesende";                         p="Tag \d+ beendet";                                   test="alt" },
  @{ n="Notfall-Nachkauf (v0.4.2)";         p="Notfall-Nachkauf";                                  test="alt" },
  @{ n="Lager-Kartons holen (v0.5.0)";      p="Aus dem Lager geholt";                              test="neu" },
  @{ n="Fast fertige Sets pausiert (v0.5.0)"; p="fast fertige Sets pausiert";                      test="neu" },
  @{ n="Hinweis Vitrinen voll (v0.5.0)";    p="Vitrinen voll:";                                    test="neu" },
  @{ n="Ankauf neue Karte (v0.5.2)";        p="Gekauft für.*neue Karte";                           test="neu" },
  @{ n="Feilschen Doppelte (v0.5.3)";       p="Gekauft für.*Runde [2-9]";                          test="neu" },
  @{ n="Ankauf abgelehnt (v0.5.1)";         p="Ankauf abgelehnt";                                  test="neu" },
  @{ n="Kunde gegangen beim Feilschen";     p="Kunde ist gegangen";                                test="neu" },
  @{ n="Tausch abgelehnt (v0.5.1)";         p="Tausch abgelehnt";                                  test="neu" },
  @{ n="Packs zuerst: Lizenzen warten (v0.5.4)"; p="\[Lizenzen\] \d+ warten";                      test="neu" },
  @{ n="Packs zuerst: Erweiterungen warten (v0.5.4)"; p="\[Erweiterungen\] warten";                test="neu" }
)

function Write-Report($final) {
  $lines = @()
  if (Test-Path $log) { $all = Get-Content $log -Encoding UTF8 -ErrorAction SilentlyContinue } else { $all = @() }
  $idx = -1
  for ($i = $all.Count - 1; $i -ge 0; $i--) { if ($all[$i] -match "ShopBot geladen") { $idx = $i; break } }
  $sess = if ($idx -ge 0) { $all[$idx..($all.Count-1)] } else { @() }
  $bot  = $sess | Where-Object { $_ -match "\[ShopBot\]|ShopBot\]" }

  $errs = $sess | Where-Object { $_ -match "Error.*ShopBot|ShopBot.*Exception|Fehler im Bot-Takt|Dashboard:|\[(Sammlung|Kartenablage|Graden|Bewertungen|Rechnungen|Bonus|Lizenzen|Einräumen|Vitrinen|Erweiterungen|Upgrades|Arbeiter|Nachbestellen)\] System\." }
  $unity = @()
  if (Test-Path $player) { $unity = Select-String -Path $player -Encoding UTF8 -Pattern "GUI Error|Mismatched LayoutGroup|Getting control|tooltip data is NULL|NullReferenceException.*ShopBot" -ErrorAction SilentlyContinue | ForEach-Object { $_.Line } }

  $now = Get-Date -Format "dd.MM.yyyy HH:mm"
  $lines += "# ShopBot Testbericht"
  $lines += ""
  $lines += "Automatisch erstellt von ``tools\testbericht.ps1`` · Stand: **$now** " + $(if ($final) { "(abgeschlossen)" } else { "(läuft noch, aktualisiert jede Minute)" })
  $lines += ""
  if ($idx -lt 0) { $lines += "_Noch kein Spielstart mit ShopBot erkannt._"; Set-Content -Path $out -Value $lines -Encoding UTF8; return }
  $lines += "Bot-Meldungen in dieser Sitzung: **$($bot.Count)** · Fehler: **$($errs.Count)** · Unity-GUI-Fehler: **$($unity.Count)**"
  $lines += ""
  $lines += "| Funktion | Typ | Status | Anzahl | Letzte Meldung |"
  $lines += "|---|---|---|---|---|"
  foreach ($c in $checks) {
    $hits = @($bot | Where-Object { $_ -match $c.p })
    $status = if ($hits.Count -gt 0) { "✅ funktioniert" } else { "⏳ noch nicht gesehen" }
    $last = if ($hits.Count -gt 0) { ($hits[-1] -replace '^\[Info\s*:\s*ShopBot\]\s*','' -replace '\|','/') } else { "" }
    if ($last.Length -gt 120) { $last = $last.Substring(0,120) + "…" }
    $typ = if ($c.test -eq "neu") { "**neu**" } else { "bekannt" }
    $lines += "| $($c.n) | $typ | $status | $($hits.Count) | $last |"
  }
  $lines += ""
  $lines += "## Fehler"
  if ($errs.Count -eq 0 -and $unity.Count -eq 0) { $lines += "Keine Fehler gefunden. ✅" }
  else {
    foreach ($e in ($errs | Select-Object -Last 15)) { $lines += "- ``" + ($e.Substring(0,[Math]::Min(220,$e.Length))) + "``" }
    foreach ($u in ($unity | Select-Object -Last 10)) { $lines += "- Unity: ``" + ($u.Substring(0,[Math]::Min(220,$u.Length))) + "``" }
  }
  $lines += ""
  $lines += "## Noch zu prüfen (von Hand / beim nächsten Mal)"
  $lines += "- Neue Funktionen mit ⏳ oben: entweder noch nicht ausgelöst (z. B. Lizenz/Erweiterung braucht Level+Geld, Tageslimit erst nach `$60.000`) oder fehlerhaft → im Log suchen."
  $lines += "- Dashboard (F9): runde Balken, Rollen-Pillen, Seite Laden & Preise → Abschnitte 'Erweiterungen' und 'Selbst einräumen'."
  $lines += "- Fliegen beim Selbst-Einräumen Artikel sichtbar korrekt ins Regal? Werden 'Kunden vermissen …' seltener?"
  $lines += "- Kartenablage-Füllstand (war 16.583/10.000) – sinkt er?"
  $lines += "- Dashboard (F9) → Sammlung: Ziel-Übersicht (fehlende Karten, neue pro Tag, Tage bis fertig) und Tagesbericht plausibel?"
  $lines += "- Feilschen: steigen die Gebote pro Runde, wird nie über dem Wunschpreis gezahlt?"
  $lines += ""
  $lines += "## Letzte 25 Bot-Meldungen"
  foreach ($b in ($bot | Select-Object -Last 25)) { $lines += "- " + ($b -replace '^\[Info\s*:\s*ShopBot\]\s*','') }
  Set-Content -Path $out -Value $lines -Encoding UTF8
}

# 1) auf Spielstart warten (max. 2 Std.)
while (-not (Get-Process -Name "Card Shop Simulator" -ErrorAction SilentlyContinue)) {
  if ((Get-Date) - $start -gt [TimeSpan]::FromHours(2)) { exit }
  Start-Sleep -Seconds 10
}
# 2) auf ShopBot-Meldung im NEUEN Log warten
while ($true) {
  if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $start -and (Select-String -Path $log -Pattern "ShopBot geladen" -Quiet)) { break }
  if (-not (Get-Process -Name "Card Shop Simulator" -ErrorAction SilentlyContinue)) { Write-Report $true; exit }
  Start-Sleep -Seconds 10
}
# 3) jede Minute Bericht schreiben, bis das Spiel beendet wird (max. 3 Std.)
while ((Get-Process -Name "Card Shop Simulator" -ErrorAction SilentlyContinue) -and ((Get-Date) - $start -lt [TimeSpan]::FromHours(3))) {
  Write-Report $false
  Start-Sleep -Seconds 60
}
Write-Report $true
