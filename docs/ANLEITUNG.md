# ShopBot – Schritt-für-Schritt-Anleitung

Diese Anleitung führt dich von null bis zum laufenden Bot. Dauer: ca. 10 Minuten.
🇬🇧 [English guide](INSTALL.md)

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

**Am einfachsten:** Unter **Releases** die neueste **`ShopBot-x.y.z.zip`** herunterladen, irgendwo entpacken (z. B. Downloads) und **`Install-ShopBot.bat`** doppelklicken. Der Installer findet das Spiel über Steam (auch auf anderen Laufwerken), prüft BepInEx, kopiert den Mod, entsperrt ihn und zeigt, welche empfohlenen Mods fehlen. Fertig – weiter mit Schritt 6.

**Von Hand:**
1. Auf der Repo-Seite rechts auf **Releases** klicken und die neueste **`ShopBot-x.y.z.zip`** herunterladen.
2. ZIP öffnen. Darin ist der Ordner `BepInEx\plugins\ShopBot\ShopBot.dll`.
3. Den Ordner **`BepInEx` aus der ZIP in den Spielordner ziehen** und „Dateien ersetzen/zusammenführen“ bestätigen.
4. Prüfen: Es gibt jetzt
   `…\TCG Card Shop Simulator\BepInEx\plugins\ShopBot\ShopBot.dll`

> Windows blockiert manchmal heruntergeladene DLLs: Rechtsklick auf `ShopBot.dll` → **Eigenschaften** → unten **„Zulassen“** anhaken → OK.

## 6. Erster Start

1. Spiel starten und **Spielstand laden**.
2. Oben links erscheint eine kleine Statuszeile („Shop loaded – bot starts in 10 s“, auf Deutsch, sobald du die Sprache umgestellt hast).
3. Nach 10 Sekunden legt der Bot los: Preise anpassen, Rechnungen bezahlen, Einsatzplan, Einkauf …
4. **F9** drücken → das Dashboard zeigt, was gerade passiert.
5. **Sprache:** Das Dashboard startet auf Englisch. Oben rechts auf **EN ▼** klicken → **Deutsch**. (Wer von einer älteren deutschen Version aktualisiert, hat automatisch Deutsch.)

**Hat es geklappt?** Öffne `BepInEx\LogOutput.log` (im Spielordner) mit dem Editor. Dort muss stehen:
```
[Info   :   BepInEx] Loading [ShopBot 0.7.1]
[Info   :   ShopBot] ShopBot 0.7.1 loaded. F8 = on/off, F9 = dashboard. Language: Deutsch
```
Die Log-Datei ist immer Englisch – so können auch andere bei Fehlern helfen.

## 7. Das Dashboard (F9)

| Seite | Was du siehst / einstellst |
|---|---|
| **Übersicht** | Geld, Umsatz & Kosten heute, Einkauf vs. Tageslimit, Sets, Grading, Opener, Pack-Vorrat mit Verbrauch und Reichweite, Schnellaktionen, letzte Aktionen, Tagesbericht |
| **Sammlung** | Ziel (alle Karten, Tempo, Resttage), Ziel-Sets mit Fortschritt, alle Pokémon-Sets, Vorrat, Tageslimit, Notfall-Nachkauf, „Packs zuerst“ |
| **Laden & Preise** | Aufschläge, Einkauf, Bewertungen, Tagesablauf (**Bot-Kasse**), Erweiterungen, Selbst einräumen, Lizenzen, Kartons einsortieren |
| **Kunden** | Ankauf-Limits, Feilschen, Tausch-Regeln |
| **Karten** | Graden (PSA), Verkauf über Kartenablage, Pack-Opener (gleichmäßig verteilen, Mindestportion, Turbo) |
| **Arbeiter** | Einsatzplan live (Rolle, Tempo, Bonus), Anzahl Kassierer/Einräumer, Einstellen |
| **Verlauf** | Alle Bot-Aktionen, filterbar |
| **Einstellungen** | Sprache, Dashboard-Größe, Statuszeile, Tastenkürzel, Zeiten |

Alle Werte werden automatisch gespeichert (beim Schließen des Dashboards, beim Beenden, alle 30 s). Dieselben Einstellungen findest du auch mit **F1** im Abschnitt **ShopBot** – oder als Datei `BepInEx\config\ratta.shopbot.cfg`.

## 8. Empfohlene Einstellungen

Die Standardwerte passen für die meisten. Je nach Spielstand:

| Situation | Einstellung |
|---|---|
| **Neuer Spielstand / wenig Geld** | *Sammlung → Einkaufslimit pro Tag* auf $5.000–$20.000; *Notfall-Nachkauf* aus |
| **Viel Geld, Sammlung schnell füllen** | Tageslimit hoch oder „Limit wächst mit: % vom Geld“ auf 10 % |
| **Du willst selbst kassieren** | *Laden & Preise → Bot kassiert an allen freien Kassen* aus |
| **Nur bestimmte Dinge automatisieren** | Einzelne Schalter aus – z. B. nur Kasse + Preise |
| **Später: alle Karten als PSA 10** | *Karten → PSA-10-Sammlung* an |

**Wichtig für den vollen Ablauf:**
- Stell genug **Pack-Opener** auf – das ist der Motor der Sammlung. Aber nur so viele, wie deine Einnahmen mit Packs füttern können (das Dashboard zeigt, wie lange der Pack-Vorrat reicht).
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
| „Collection Tracker not found“ im Log | Mod fehlt → Sammel-Funktionen sind aus. Mod installieren. |
| Bot kauft keine Packs | Tageslimit erreicht? Geld knapp? Lizenz für das Set fehlt? → Dashboard *Sammlung* |
| Opener laufen oft leer | Karte „Pack-Vorrat“: Verbrauch pro Minute und Reichweite. Weniger Opener oder ein höheres Tageslimit helfen. |
| Grading-Pakete bleiben liegen | Log nach `[Grading package] not opened:` durchsuchen – dort steht der Grund |
| Kunden stauen sich an der Kasse | Dashboard → Bot-Kasse „Artikel pro Sekunde“ erhöhen |
| Etwas ging schief | **F8** (Bot aus), Spielstand-Backup zurückspielen, Log-Datei mitschicken |

**Fehler melden:** Bitte `BepInEx\LogOutput.log` (direkt nach dem Problem, vor dem nächsten Spielstart) und eine kurze Beschreibung mitschicken – im Repo unter **Issues**.

## 11. Update & Deinstallation

**Update:** Neue ZIP herunterladen und wie in Schritt 5 drüberkopieren (Dateien ersetzen). Deine Einstellungen bleiben erhalten – auch die aus älteren Versionen mit deutschen Namen in der Config-Datei.

**Deinstallation:** Ordner `BepInEx\plugins\ShopBot` löschen (optional auch `BepInEx\config\ratta.shopbot.cfg`). Dein Spielstand bleibt normal spielbar.
