# ShopBot – Schritt-für-Schritt-Anleitung

Diese Anleitung führt dich von null bis zum laufenden Bot. Dauer: ca. 10 Minuten.

**Inhalt**
1. [Spielordner finden](#1-spielordner-finden)
2. [Spielstand sichern](#2-spielstand-sichern)
3. [BepInEx installieren (falls noch nicht vorhanden)](#3-bepinex-installieren)
4. [Empfohlene Mods](#4-empfohlene-mods)
5. [ShopBot installieren](#5-shopbot-installieren)
6. [Erster Start](#6-erster-start)
7. [Das Dashboard (F9)](#7-das-dashboard-f9)
8. [Empfohlene Einstellungen](#8-empfohlene-einstellungen)
9. [So spielst du mit dem Bot](#9-so-spielst-du-mit-dem-bot)
10. [Fehlersuche](#10-fehlersuche)
11. [Update & Deinstallation](#11-update--deinstallation)

---

## 1. Spielordner finden

1. **Steam** öffnen → **Bibliothek**.
2. Rechtsklick auf **TCG Card Shop Simulator** → **Verwalten** → **Lokale Dateien durchsuchen**.
3. Es öffnet sich der Spielordner, z. B.
   `C:\Program Files (x86)\Steam\steamapps\common\TCG Card Shop Simulator`

Darin liegt `Card Shop Simulator.exe`. **Diesen Ordner brauchst du gleich.**

## 2. Spielstand sichern

Der Bot gibt Geld aus, stellt Arbeiter um und verkauft Karten. Sichere deinen Spielstand vorher:

1. `Windows-Taste + R` drücken, eingeben: `%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator` → **OK**.
2. Den ganzen Ordner kopieren und an einen sicheren Ort einfügen (z. B. Desktop → „Spielstand-Backup“).

## 3. BepInEx installieren

> **Nutzt du das Real-TCG-Overhaul-Modpack / den Mod-Manager?** Dann ist BepInEx schon da → weiter bei [Schritt 4](#4-empfohlene-mods).
> Prüfen: Gibt es im Spielordner einen Ordner **`BepInEx`** mit Unterordner **`plugins`**? Dann ist es installiert.

1. Auf [github.com/BepInEx/BepInEx/releases](https://github.com/BepInEx/BepInEx/releases) die neueste **BepInEx 5.4.x** für **win_x64** herunterladen (z. B. `BepInEx_win_x64_5.4.23.x.zip`). **Nicht** BepInEx 6.
2. Die ZIP **in den Spielordner** entpacken. Danach liegen dort u. a. `winhttp.dll`, `doorstop_config.ini` und der Ordner `BepInEx`.
3. Spiel **einmal starten** bis zum Hauptmenü und wieder beenden. Jetzt gibt es `BepInEx\plugins` und `BepInEx\config`.

## 4. Empfohlene Mods

ShopBot läuft auch ohne sie – dann sind die jeweiligen Funktionen einfach aus. Für das volle Paket:

| Mod | Wofür im Bot |
|---|---|
| **Real TCG Overhaul** (Nexus Mods, Mod 577) mit *Enhanced Prefab Loader* | Pokémon-Produkte, -Sets und Pokémon-Filter |
| **Collection Tracker** | Sammlung lesen, Packs kaufen, Opener-Boost |
| **CardGrade Overhaul** | Graden bei PSA, Pakete öffnen |
| **Upgrades** | Sammelpunkte-Upgrades |
| **ConfigurationManager** (BepInEx) | Einstellungen mit **F1** |

Am einfachsten über den **Real TCG Overhaul Mod Manager** installieren – der bringt alles mit.

## 5. ShopBot installieren

1. Auf der Repo-Seite rechts auf **Releases** klicken und die neueste **`ShopBot-x.y.z.zip`** herunterladen.
2. ZIP öffnen. Darin ist der Ordner `BepInEx\plugins\ShopBot\ShopBot.dll`.
3. Den Ordner **`BepInEx` aus der ZIP in den Spielordner ziehen** und „Dateien ersetzen/zusammenführen“ bestätigen.
4. Prüfen: Es gibt jetzt
   `…\TCG Card Shop Simulator\BepInEx\plugins\ShopBot\ShopBot.dll`

> Windows blockiert manchmal heruntergeladene DLLs: Rechtsklick auf `ShopBot.dll` → **Eigenschaften** → unten **„Zulassen“** anhaken → OK.

## 6. Erster Start

1. Spiel starten und **Spielstand laden**.
2. Oben links erscheint eine kleine Statuszeile („Laden geladen – Bot startet in 10 Sek.“).
3. Nach 10 Sekunden legt der Bot los: Preise anpassen, Rechnungen bezahlen, Einsatzplan, Einkauf …
4. **F9** drücken → das Dashboard zeigt, was gerade passiert.

**Hat es geklappt?** Öffne `BepInEx\LogOutput.log` (im Spielordner) mit dem Editor. Dort muss stehen:
```
[Info   :   BepInEx] Loading [ShopBot 0.5.1]
[Info   :   ShopBot] ShopBot geladen. F8 = An/Aus.
```

## 7. Das Dashboard (F9)

| Seite | Was du siehst / einstellst |
|---|---|
| **Übersicht** | Geld, Umsatz & Kosten heute, Einkauf vs. Tageslimit, Sets, Grading, Opener, Vorrat, Schnellaktionen, letzte Aktionen |
| **Sammlung** | Ziel-Sets mit Fortschritt, alle Pokémon-Sets, Vorrat, Tageslimit, Notfall-Nachkauf |
| **Laden & Preise** | Aufschläge, Einkauf, Bewertungen, Tagesablauf (**Bot-Kasse**), Erweiterungen, Selbst einräumen, Lizenzen, Kartons einsortieren |
| **Kunden** | Ankauf-Limits, Tausch-Regeln |
| **Karten** | Graden (PSA), Verkauf über Kartenablage, Pack-Opener |
| **Arbeiter** | Einsatzplan live (Rolle, Tempo, Bonus), Anzahl Kassierer/Einräumer, Einstellen |
| **Verlauf** | Alle Bot-Aktionen, filterbar |

Alle Werte werden automatisch gespeichert (beim Schließen des Dashboards, beim Beenden, alle 30 s). Dieselben Einstellungen findest du auch mit **F1** im Abschnitt **ShopBot** – oder als Datei `BepInEx\config\ratta.shopbot.cfg`.

## 8. Empfohlene Einstellungen

Die Standardwerte passen für die meisten. Je nach Spielstand:

| Situation | Einstellung |
|---|---|
| **Neuer Spielstand / wenig Geld** | *Sammlung → Tageslimit* auf $5.000–$20.000; *Notfall-Nachkauf* aus |
| **Viel Geld, Sammlung schnell füllen** | Tageslimit hoch oder „Limit wächst mit: % vom Geld“ auf 10 % |
| **Du willst selbst kassieren** | *Laden & Preise → Bot kassiert an allen freien Kassen* aus |
| **Nur bestimmte Dinge automatisieren** | Einzelne Schalter aus – z. B. nur Kasse + Preise |
| **Später: alle Karten als PSA 10** | *Karten → PSA-10-Sammlung* an |

**Wichtig für den vollen Ablauf:**
- Stell genug **Pack-Opener** auf – das ist der Motor der Sammlung.
- Stell einen **Mülleimer** und **Lagerregale** auf (für Kartons).
- Stell eine **Kartenablage (Card Storage)** und **Karten-Vitrinen** auf – darüber verkauft der Bot doppelte und gegradete Karten.

## 9. So spielst du mit dem Bot

- Du kannst ganz normal mitspielen. **Fenster** (Kunden-Angebote, Grading-Pakete) öffnet der Bot nur, wenn du **8 Sekunden nichts drückst**.
- Kassen, an denen **du** stehst, lässt er in Ruhe.
- Abends ab 21 Uhr beendet er den Tag, sobald die letzten Kunden bezahlt haben.
- **F8** schaltet alles sofort aus und wieder an.

## 10. Fehlersuche

| Problem | Lösung |
|---|---|
| Keine Statuszeile, F9 tut nichts | DLL am falschen Ort? (siehe Schritt 5) · BepInEx installiert? · DLL „zugelassen“? · Log prüfen |
| `Loading [ShopBot]` fehlt im Log | BepInEx lädt nicht: BepInEx **5** (nicht 6), x64, im Spielordner entpackt? |
| „Collection Tracker nicht gefunden“ im Log | Mod fehlt → Sammel-Funktionen sind aus. Mod installieren. |
| Bot kauft keine Packs | Tageslimit erreicht? Geld knapp? Lizenz für das Set fehlt? → Dashboard *Sammlung* |
| Grading-Pakete bleiben liegen | Log nach `[Grading-Paket] nicht geöffnet:` durchsuchen – dort steht der Grund |
| Kunden stauen sich an der Kasse | Dashboard → Bot-Kasse „Artikel pro Sekunde“ erhöhen |
| Etwas ging schief | **F8** (Bot aus), Spielstand-Backup zurückspielen, Log-Datei mitschicken |

**Fehler melden:** Bitte `BepInEx\LogOutput.log` (direkt nach dem Problem, vor dem nächsten Spielstart) und eine kurze Beschreibung mitschicken – im Repo unter **Issues**.

## 11. Update & Deinstallation

**Update:** Neue ZIP herunterladen und wie in Schritt 5 drüberkopieren (Dateien ersetzen). Deine Einstellungen bleiben erhalten.

**Deinstallation:** Ordner `BepInEx\plugins\ShopBot` löschen (optional auch `BepInEx\config\ratta.shopbot.cfg`). Dein Spielstand bleibt normal spielbar.
