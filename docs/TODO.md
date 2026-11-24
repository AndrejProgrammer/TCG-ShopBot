# ShopBot – To-do (Stand 24.11.2026, v0.6.0)

Reihenfolge = Empfehlung. ⭐ = wichtig vor dem Public-Schalten.

## Vor dem Veröffentlichen
- [ ] ⭐ **v0.6.0 im Spiel testen:** Dashboard auf Deutsch? Alte Einstellungen übernommen (Log: „settings carried over“)? Animationen flüssig? Sprach-Menü öffnet/schließt? Testbericht (`tools\testbericht.ps1`) mitlaufen lassen.
- [ ] ⭐ **Testinstallation auf frischem Spielstand ohne alte Config** – wie erlebt ein neuer Nutzer den Bot? (Standardwerte sind auf späten Spielstand abgestimmt.)

## Erster Start für neue Nutzer
- [ ] ⭐ **Einrichtungs-Assistent beim ersten Start:** Sprache, Profil („Neuer Spielstand / wenig Geld“, „Mittendrin“, „Viel Geld – Sammlung voll machen“) → passende Werte (Tageslimit, Reserve, Notfall-Nachkauf, Graden), Liste fehlender Mods mit Hinweis, was dadurch aus ist.
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
