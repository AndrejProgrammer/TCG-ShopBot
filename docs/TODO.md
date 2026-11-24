# ShopBot – To-do (Stand 24.11.2026, v0.6.0)

Reihenfolge = Empfehlung. ⭐ = wichtig vor dem Public-Schalten.

## Vor dem Veröffentlichen
- [x] **v0.6.0 im Spiel getestet (24.11.):** Dashboard auf Deutsch ✓, 115 Einstellungen 1:1 übernommen ✓, alle Seiten sehen gut aus ✓, Feilschen bis Runde 5 ✓, Lizenz gekauft ✓, keine Fehler im Log ✓. Sidebar-Fußzeile war links abgeschnitten → behoben (Abstand).
- [ ] ⭐ **Fehler: Tageslimit-Zähler wird beim Neustart zurückgesetzt** (`_daySpent` nur im Speicher). Neustart am selben Spieltag → Bot darf nochmal das volle Limit ausgeben (gesehen: „Einkauf heute $0 / $60.502“ bei „Kosten heute $177.118“). Lösung: `_daySpent`, `_daySpentDay`, `_emergencySpent`, `_emergencyDay` in der Config (intern) speichern oder beim Start aus `CPlayerData.m_GameReportDataCollect.supplyCost` des Tages ableiten.
- [ ] **Tagesbericht** zählt erst ab Spielstart statt ab Tagesbeginn (Einnahmen „$0“ nach Neustart) – Tageswerte speichern oder aus dem Spiel-Tagesbericht nehmen.
- [ ] **Übersicht direkt nach dem Start:** „Pack-Vorrat Ziel-Sets 0 / 0“ bis zur ersten Runde → „wird berechnet …“ anzeigen.
- [ ] 8 verwaiste deutsche Einträge aus uralten Versionen in der Config aufräumen (z. B. „Anzahl Kassierer“, „Gleichzeitige Ziel-Sets“) – stören nicht.
- [ ] ⭐ **Testinstallation auf frischem Spielstand ohne alte Config** – wie erlebt ein neuer Nutzer den Bot? (Standardwerte sind auf späten Spielstand abgestimmt.)

## Bot passt sich dem Spielverlauf an (ersetzt den starren Assistenten)
- [ ] ⭐ **Schritt 1 – Messen & anzeigen:** Ø Einnahmen/Kosten pro Tag (letzte 3 Tage), Level, Erweiterungen, Opener/Kassen/Vitrinen/Arbeiter, Lizenzen, Sammlungsfortschritt. Daraus **Spielphase** erkennen und im Dashboard zeigen („Phase: Sammeln (erkannt)“) – noch ohne Verhalten zu ändern, ein paar Spieltage beobachten.
  - 🌱 Start (Level < 10, wenig Geld, keine Opener): Preise, Kasse, Nachbestellen, Rechnungen – keine Sammlungs-Packs, kein Graden.
  - 🏗️ Aufbau (wenige Opener, mittlere Einnahmen): Geld zuerst in Erweiterungen + Lizenzen, Packs maßvoll.
  - 📦 Sammeln (viele Opener, hohe Einnahmen): Packs zuerst, Notfall-Nachkauf aktiv.
  - 🏆 Endspiel (Sammlung > 90 %): fehlende Karten von Kunden, PSA-10-Jagd, Graden voll.
  - Wechsel erst, wenn die Werte einen ganzen Spieltag stabil sind (kein Hin-und-her).
- [ ] **Schritt 2 – Auto-Werte:** Grenzen aus Einnahmen statt fester Zahlen (Tageslimit = % der Ø-Tageseinnahmen je Phase, Reserve = kommende Rechnungen + Puffer, Lizenzen/Erweiterungen nur bei Amortisation in X Tagen).
- [ ] **Lernphase** bei Einstieg mitten im Spiel: erster Spieltag vorsichtig, nur messen; ab Tag 2 mit echten Zahlen.
- [ ] **Kontrolle:** jede Einstellung „Auto“ (Standard, zeigt aktuellen Wert + Grund) oder „Manuell“ (wird nie überschrieben); Phase im Dashboard festlegbar; Phasenwechsel als Meldung im Verlauf.
- [ ] Beim ersten Start trotzdem kurz: Sprache + Liste fehlender Mods (was dadurch aus ist).
- [ ] **Sprache automatisch erkennen** – nur bei Neuinstallation: Windows auf Deutsch → Deutsch, sonst Englisch.

## Installation
- [ ] ⭐ **Installer `Install-ShopBot.bat`:** Spielordner über Steam-Bibliotheken finden (`libraryfolders.vdf`), BepInEx 5 prüfen (sonst Link), DLL kopieren, Windows-Sperre aufheben (`Unblock-File`), „fertig“ melden. In die Release-ZIP packen.
- [ ] **Nexus-Mods-Seite** vorbereiten (Text EN/DE, Anforderungen, Installationsschritte) – Hochladen macht der User. ZIP-Aufbau passt schon für Vortex.
- [ ] **Thunderstore/r2modman** – erst prüfen, ob es eine Community für das Spiel gibt.
- [ ] **Screenshots / GIF** vom Dashboard in die README (User macht Bilder im Spiel).

## Stabilität & Support
- [ ] ⭐ **Spiel-Update-Schutz:** Fehlt nach einem Spiel-/Mod-Update ein Feld/eine Methode (Reflection), Funktion sauber abschalten und im Dashboard „Funktion X nicht kompatibel“ zeigen. Startzeile im Log: „aktiv: Kasse ✓, Graden ✓, Upgrades ✗ …“.
- [ ] **„Getestet mit“** in der README: Spielversion + Mod-Versionen (siehe „Mod-Stand“ in ENTWICKLUNG.md).
- [ ] **Update-Hinweis (opt-in, Standard aus):** neueste GitHub-Release abfragen, im Dashboard „Version x.y.z verfügbar“.

## Veröffentlichen (erst nach OK vom User)
- [ ] Repo `AndrejProgrammer/TCG-ShopBot` auf public stellen
- [ ] GitHub-Release v0.6.0 mit `dist\ShopBot-0.6.0.zip` (`powershell -File tools\release.ps1`)

## Sonstiges / Ideen
- [ ] Weitere Sprachen (Französisch, Spanisch, Russisch …) – erst später, User wollte vorerst nur DE/EN.
- [ ] Hinweis: Plugin-ID `ratta.shopbot` (Windows-Benutzername) bleibt – Ändern würde alle Einstellungen zurücksetzen.
