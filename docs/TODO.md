# ShopBot – To-do (Stand 24.11.2026, v0.6.0)

Reihenfolge = Empfehlung. ⭐ = wichtig vor dem Public-Schalten.

## Vor dem Veröffentlichen
- [ ] ⭐ **0.7.0 im Spiel prüfen:** Log „[Compat] … all … found“ (sonst: welche Funktion aus?), Spielphase + „Auto: …“-Zeile auf der Übersicht plausibel (erwartet: Sammeln, Tageslimit ~70 % der Einnahmen erst ab dem 2. gemessenen Tag), Einsatzplan nach Bedarf, Regale voll, keine Fehler. Danach Installer an einer echten Release-ZIP testen. Außerdem aus 0.6.3/0.6.4: Einsatzplan nach Bedarf („Einsatzplan … (Arbeit für Arbeiter: …)“ im Verlauf), arbeiten die Arbeiter? Spielphase auf der Übersicht plausibel (erwartet beim User: Sammeln)? Regale bleiben voll, keine Fehler.
- [x] **v0.6.0 im Spiel getestet (24.11.):** Dashboard auf Deutsch ✓, 115 Einstellungen 1:1 übernommen ✓, alle Seiten sehen gut aus ✓, Feilschen bis Runde 5 ✓, Lizenz gekauft ✓, keine Fehler im Log ✓. Sidebar-Fußzeile war links abgeschnitten → behoben (Abstand).
- [x] (0.6.1) **Fehler: Tageslimit-Zähler wird beim Neustart zurückgesetzt** (`_daySpent` nur im Speicher). Neustart am selben Spieltag → Bot darf nochmal das volle Limit ausgeben (gesehen: „Einkauf heute $0 / $60.502“ bei „Kosten heute $177.118“). Lösung: `_daySpent`, `_daySpentDay`, `_emergencySpent`, `_emergencyDay` in der Config (intern) speichern oder beim Start aus `CPlayerData.m_GameReportDataCollect.supplyCost` des Tages ableiten.
- [x] (0.6.1) **Tagesbericht** zählt erst ab Spielstart statt ab Tagesbeginn (Einnahmen „$0“ nach Neustart) – Tageswerte speichern oder aus dem Spiel-Tagesbericht nehmen.
- [x] (0.6.1) **Übersicht direkt nach dem Start:** „Pack-Vorrat Ziel-Sets 0 / 0“ bis zur ersten Runde → „wird berechnet …“ anzeigen.
- [x] (0.6.1) 8 verwaiste deutsche Einträge aus uralten Versionen in der Config aufräumen (z. B. „Anzahl Kassierer“, „Gleichzeitige Ziel-Sets“) – stören nicht.
- [ ] ⭐ **Testinstallation auf frischem Spielstand ohne alte Config** – wie erlebt ein neuer Nutzer den Bot? (Standardwerte sind auf späten Spielstand abgestimmt.)

- [x] (0.6.3, getestet 24.11.) Regale: leere Fächer 188 → 66 in 3 Min., 4.500–6.600 Artikel/Min., Neubelegung 100 %, Nachbestellen nach Lücke, Kartenablagen alle 4, keine Fehler. Ursprünglich geplant: Log „Reordered: …“ erscheint, beschriftete Regale werden voll; leere Regale ohne Etikett bekommen Ware (keine Opener-Packs!); Kartenablage-Meldung „… (N storages)“ mit N > 1; Vitrinen bekommen Karten aus allen Ablagen.
- [x] (getestet 24.11.) **0.6.1 im Spiel prüfen:** Neustart am selben Tag → Log „already spent today (restored after restart)“, Dashboard „Einkauf heute“ nicht $0; Tagesbericht/Tempo nach Neustart weiter da; Log „unused old entries removed“.

- [ ] **Beobachten:** „Regale vor Openern“ – lässt sich ein beschriftetes Fach nie befüllen, bekommen die Opener diese Pack-Sorte nicht mehr (keine Absicherung eingebaut). In der Regal-Übersicht prüfen.
- [ ] **Beobachten:** Nachbestellen für große Regallücken kostet anfangs einige 10.000 $ – ggf. Menge pro Bestellung/Tag begrenzen (User gefragt, noch offen).

## Berater – wirtschaften wie ein guter Spieler (Konzept 24.11.)
Jede Ausgabe = Investition mit Kosten (einmalig + laufend, z. B. Miete/Lohn) und Nutzen/Tag aus Messwerten → Amortisation → Bestes zuerst. Engpässe statt starrer Phasen.
- [x] (0.7.1) **Schritt 1 – Messen & Berater-Seite:** Einnahmen/Fixkosten/Gewinn pro Tag, Rücklage, Engpässe, Tipps für den Spieler, nächste Investitionen. Im Spiel prüfen, ob die Zahlen stimmen (Fixkosten = gemessene Rechnungen).
- [x] (0.7.1) **Schritt 2 – Grading-Stufe nach Gewinn** (schnellste Stufe ≤ 15 % Gewinn/Tag, die sich füllen lässt). Prüfen: welche Stufe wählt er, wie viele Tage?
- [ ] **Schritt 3 – Erweiterungen und Arbeiter nach Amortisation** statt „Budget ≥ 5× Preis“: Erweiterung nur bei Engpass „Regalfläche“ und Mietplus ≤ ~10 % Gewinn/Tag (Mietplus nach einer Erweiterung messen); Arbeiter einstellen nur, wenn über Tage Arbeit übrig bleibt.
- [x] (0.7.2, noch nicht im Spiel getestet) **Schritt 4 – Budget-Töpfe** statt Tageslimit/Faktoren: Gewinn → Rücklage → Investitionen (Engpass, beste Amortisation) → Packs.
- [ ] **Schritt 5 – Packs selbst öffnen am Anfang** (ohne Opener) – prüfen, ob das Spiel das per Code zulässt.

## Bot passt sich dem Spielverlauf an (ersetzt den starren Assistenten)
- [x] (0.6.4, noch nicht im Spiel getestet) ⭐ **Schritt 1 – Messen & anzeigen:** Ø Einnahmen/Kosten pro Tag (letzte 3 Tage), Level, Erweiterungen, Opener/Kassen/Vitrinen/Arbeiter, Lizenzen, Sammlungsfortschritt. Daraus **Spielphase** erkennen und im Dashboard zeigen („Phase: Sammeln (erkannt)“) – noch ohne Verhalten zu ändern, ein paar Spieltage beobachten.
  - 🌱 Start (Level < 10, wenig Geld, keine Opener): Preise, Kasse, Nachbestellen, Rechnungen – keine Sammlungs-Packs, kein Graden.
  - 🏗️ Aufbau (wenige Opener, mittlere Einnahmen): Geld zuerst in Erweiterungen + Lizenzen, Packs maßvoll.
  - 📦 Sammeln (viele Opener, hohe Einnahmen): Packs zuerst, Notfall-Nachkauf aktiv.
  - 🏆 Endspiel (Sammlung > 90 %): fehlende Karten von Kunden, PSA-10-Jagd, Graden voll.
  - Wechsel erst, wenn die Werte einen ganzen Spieltag stabil sind (kein Hin-und-her).
- [x] (0.7.0, noch nicht im Spiel getestet) **Schritt 2 – Auto-Werte:** Grenzen aus Einnahmen statt fester Zahlen (Tageslimit = % der Ø-Tageseinnahmen je Phase, Reserve = kommende Rechnungen + Puffer, Lizenzen/Erweiterungen nur bei Amortisation in X Tagen).
- [x] (0.7.0) **Lernphase** bei Einstieg mitten im Spiel: erster Spieltag vorsichtig, nur messen; ab Tag 2 mit echten Zahlen.
- [~] (0.7.0: ein Hauptschalter „Einstellungen an die Spielphase anpassen“ statt Auto/Manuell pro Einstellung) **Kontrolle:** jede Einstellung „Auto“ (Standard, zeigt aktuellen Wert + Grund) oder „Manuell“ (wird nie überschrieben); Phase im Dashboard festlegbar; Phasenwechsel als Meldung im Verlauf.
- [ ] Beim ersten Start trotzdem kurz: Sprache + Liste fehlender Mods (was dadurch aus ist).
- [x] (0.7.0) **Sprache automatisch erkennen** – nur bei Neuinstallation: Windows auf Deutsch → Deutsch, sonst Englisch.

## Installation
- [x] (0.7.0) ⭐ **Installer `Install-ShopBot.bat`:** Spielordner über Steam-Bibliotheken finden (`libraryfolders.vdf`), BepInEx 5 prüfen (sonst Link), DLL kopieren, Windows-Sperre aufheben (`Unblock-File`), „fertig“ melden. In die Release-ZIP packen.
- [ ] **Nexus-Mods-Seite** vorbereiten (Text EN/DE, Anforderungen, Installationsschritte) – Hochladen macht der User. ZIP-Aufbau passt schon für Vortex.
- [ ] **Thunderstore/r2modman** – erst prüfen, ob es eine Community für das Spiel gibt.
- [ ] **Screenshots / GIF** vom Dashboard in die README (User macht Bilder im Spiel).

## Stabilität & Support
- [x] (0.7.0) ⭐ **Spiel-Update-Schutz:** Fehlt nach einem Spiel-/Mod-Update ein Feld/eine Methode (Reflection), Funktion sauber abschalten und im Dashboard „Funktion X nicht kompatibel“ zeigen. Startzeile im Log: „aktiv: Kasse ✓, Graden ✓, Upgrades ✗ …“.
- [x] (0.7.0) **„Getestet mit“** in der README: Spielversion + Mod-Versionen (siehe „Mod-Stand“ in ENTWICKLUNG.md).
- [ ] **Update-Hinweis (opt-in, Standard aus):** neueste GitHub-Release abfragen, im Dashboard „Version x.y.z verfügbar“.

## Veröffentlichen (erst nach OK vom User)
- [x] (24.11.) Repo `AndrejProgrammer/TCG-ShopBot` ist öffentlich (Hauptzweig gegen Force-Push/Löschen geschützt, Wiki aus)
- [x] (24.11.) GitHub-Release **v0.7.0 als Pre-Release** mit `ShopBot-0.7.0.zip` (inkl. Installer)
- [x] (24.11.) **v0.7.2 als normales Release (Latest)** veröffentlicht – Budget-Töpfe, Berater, Grading-Stufe im Spiel geprüft. Ältere Pre-Releases (0.4.2–0.7.0) bestehen noch.

## Sonstiges / Ideen
- [ ] Weitere Sprachen (Französisch, Spanisch, Russisch …) – erst später, User wollte vorerst nur DE/EN.
- [ ] Hinweis: Plugin-ID `ratta.shopbot` (Windows-Benutzername) bleibt – Ändern würde alle Einstellungen zurücksetzen.
