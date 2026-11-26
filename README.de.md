# ShopBot – Automatisierungs-Mod für TCG Card Shop Simulator (BepInEx)

[![Download](https://img.shields.io/github/v/release/AndrejProgrammer/TCG-ShopBot?label=download&style=for-the-badge&color=ff9933)](https://github.com/AndrejProgrammer/TCG-ShopBot/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/AndrejProgrammer/TCG-ShopBot/total?style=for-the-badge&color=4a90e2)](https://github.com/AndrejProgrammer/TCG-ShopBot/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-45d17a?style=for-the-badge)](LICENSE)

**ShopBot** ist ein kostenloser **Mod für TCG Card Shop Simulator** (BepInEx-Plugin), der deinen Kartenladen weitgehend allein führt: **automatisch kassieren** an allen Kassen, **Regale und Vitrinen automatisch auffüllen**, **Pack-Opener befüllen**, **Preise setzen**, Packs für deine **Pokémon-Sammlung** kaufen, **PSA-Graden**, Arbeiter, Lizenzen und Erweiterungen – optimiert für das **Real TCG Overhaul**-Modpack (Pokémon).
Ziel: **alle Pokémon-Karten im Collection Tracker besitzen** (später alle als PSA 10) – während der Laden Geld verdient.

> Version **0.7.4** · Spiel: Steam-Standard-Branch (Unity 6) · BepInEx 5.4.x · Dashboard auf **Englisch** und **Deutsch**
> 📖 **Neu hier? → [Schritt-für-Schritt-Anleitung](docs/ANLEITUNG.md)** · 🇬🇧 [English version](README.md)

---

## Was der Bot alles macht

| Bereich | Automatisch |
|---|---|
| 💰 **Kasse** | Kassiert an **allen freien Kassen gleichzeitig** (scannen, Geld nehmen, Wechselgeld) – du brauchst keine Kassierer |
| 👷 **Arbeiter** | Alle Arbeiter befüllen die **Pack-Opener** (nur Packs unfertiger Pokémon-Sets), Einsatzplan nach Stärken, täglich 3× Bonus, stellt neue Arbeiter ein |
| 📦 **Packs & Sammlung** | Liest den Collection Tracker, kauft Packs der Sets mit den **meisten neuen Karten pro Dollar**, holt Kartons selbst aus dem Lager, **Notfall-Nachkauf**, wenn die Opener leerlaufen |
| ⚙️ **Pack-Opener** | Befüllt Opener selbst, **verteilt bei knappem Vorrat gleichmäßig**, damit alle Opener laufen, startet halbvolle, sammelt fertige ein. **Noch kein Opener?** Dann öffnet der Bot Sammlungs-Packs selbst, wie eine Maschine |
| 🏷️ **Preise** | Marktpreis **+20 %**, gerundet (Produkte, Einzelkarten, gegradete); auf Wunsch angepasst daran, wie schnell sich ein Produkt verkauft |
| 🔔 **Einblendungen** | Kurze Hinweise im Bild: Set fertig, neue Lizenz, Grading-Paket geöffnet, neue Spielphase … |
| 🛒 **Regale & Vitrinen** | Räumt Ware selbst ein, legt Karten in leere Vitrinen-Plätze (gegradete zuerst) |
| 🗑️ **Ordnung** | Volle Kartons ins Lagerregal, leere Kartons in den Mülleimer |
| 🏅 **Graden** | Schickt Karten zu PSA (Standard, volle 52er-Einsendungen, teuerste zuerst), **öffnet zurückgekommene Pakete** und verkauft gegradete Karten (eine PSA 10 jeder Karte bleibt) |
| 🧭 **Spielphase** | Erkennt, wo du stehst (Start, Aufbau, Sammeln, Endspiel), und passt Sammlungs-Einkauf, Graden, Notfall-Nachkauf und Tageslimit daran an – oder nutzt deine eigenen Werte |
| 🔓 **Freischalten** | Lizenzen (Packs fehlender Sets zuerst), Laden-/Lager-Erweiterungen, Upgrades – aber **Packs zuerst**, wenn den Openern Packs fehlen |
| 🤝 **Kunden-Angebote** | Ankauf & Tausch: **neue Karten immer zum Wunschpreis**, Doppelte nur unter Marktpreis mit klugem Feilschen, nie mehr als verlangt, nie die letzte Karte abgeben |
| 📅 **Tagesablauf** | Rechnungen sofort bezahlen, Tag um 21 Uhr beenden, wenn der Laden leer ist |
| 🛡️ **Geld** | Darf alles ausgeben, hält aber immer die nächsten Rechnungen zurück – **nie ins Minus** |

Alles ist einzeln abschaltbar und einstellbar – im **Dashboard (F9)** oder im **Configuration Manager (F1)**.

---

## Schnellstart

1. **BepInEx 5** im Spiel installieren (ist beim Real-TCG-Overhaul-Modpack schon dabei).
2. Unter [**Releases**](../../releases) die neueste `ShopBot-x.y.z.zip` herunterladen und irgendwo entpacken.
3. **`Install-ShopBot.bat`** doppelklicken – findet das Spiel über Steam, prüft BepInEx, kopiert den Mod, entsperrt ihn und zeigt fehlende empfohlene Mods.
   *(Von Hand: den Ordner `BepInEx` aus der ZIP in den Spielordner kopieren.)*
4. Spiel starten, Spielstand laden – nach 10 Sekunden legt der Bot los. **F9** öffnet das Dashboard.
5. Sprache umstellen: im Dashboard oben rechts auf **EN ▼** klicken → **Deutsch**.

👉 Ausführlich mit Spielordner finden, Einstellungen und Fehlersuche: **[docs/ANLEITUNG.md](docs/ANLEITUNG.md)**

## Tasten

| Taste | Funktion |
|---|---|
| **F9** | Dashboard öffnen/schließen (auch ESC) |
| **F8** | Bot an/aus |
| **F1** | Configuration Manager (alle Einstellungen, Abschnitt „ShopBot“) – braucht das Plugin *ConfigurationManager* |

## Sprache

Das Dashboard gibt es auf **Englisch** (Standard) und **Deutsch** – umschalten oben rechts im Dashboard oder auf der Seite **Einstellungen**. Wer von einer älteren deutschen Version (bis 0.5.4) aktualisiert, bekommt automatisch Deutsch.
Die Log-Datei (`BepInEx\LogOutput.log`) bleibt immer Englisch, damit Fehlerberichte für alle lesbar sind. Auch die Einträge in der Config-Datei sind jetzt englisch – deine alten Einstellungen werden beim ersten Start automatisch übernommen.

**Eigene Sprache:** [`lang/en.txt`](lang/en.txt) kopieren, rechts von jedem `=` übersetzen und als `<Kürzel>.txt` (z. B. `fr.txt`) nach `BepInEx\plugins\ShopBot\lang\` legen. Platzhalter wie `{0}` oder `{1:N0}` beibehalten.

## Getestet mit

| | Version |
|---|---|
| TCG Card Shop Simulator | 1.07 (Steam-Standard-Branch, Unity 6) |
| BepInEx | 5.4.x (x64) |
| Real TCG Overhaul Mod Manager | 2.1.2 (35 Mods) |
| Enhanced Prefab Loader | 6.9.0 |
| Collection Tracker | 3.1.4 |
| CardGrade Overhaul | 1.2.7 |

Nach einem Spiel- oder Mod-Update prüft sich der Bot beim Start selbst: Fehlt etwas, das er braucht, wird nur diese Funktion abgeschaltet und im Dashboard angezeigt – der Rest läuft weiter. Im Log steht `[Compat] …`.

## Voraussetzungen

| Pflicht | |
|---|---|
| TCG Card Shop Simulator (Steam) | |
| [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases) (x64) | im Modpack enthalten |

| Empfohlen (sonst ist die jeweilige Funktion aus) | Wofür |
|---|---|
| **Real TCG Overhaul** (Nexus Mods, Mod 577) inkl. Enhanced Prefab Loader | Pokémon-Produkte und -Sets |
| **Collection Tracker** | Sammlung lesen, Packs „sofort kaufen“, Opener-Boost |
| **CardGrade Overhaul** | Graden über PSA, Grading-Pakete öffnen |
| **Upgrades** | Sammelpunkte-Upgrades |
| **ConfigurationManager** | Einstellungen mit F1 |

## Häufige Fragen

<details><summary><b>Der Bot macht nichts.</b></summary>

- Wurde der Spielstand geladen? Im Hauptmenü tut er nichts. Nach dem Laden wartet er 10 s.
- Ist er an? **F8** drücken – oben links steht der Status.
- Liegt die DLL wirklich unter `…\TCG Card Shop Simulator\BepInEx\plugins\ShopBot\ShopBot.dll`?
- Log prüfen: `BepInEx\LogOutput.log` muss `Loading [ShopBot …]` enthalten.
</details>

<details><summary><b>Fenster gehen auf, während ich spiele.</b></summary>

Fenster-Aktionen (Kunden-Angebote, Grading-Pakete) macht der Bot nur, wenn du **8 Sekunden nichts drückst** (einstellbar unter *Einstellungen*). Grading-Pakete, die länger als 60 s liegen, öffnet er auch zwischendurch (Fenster ca. 2 s).
</details>

<details><summary><b>Er gibt zu viel Geld aus.</b></summary>

Dashboard → **Sammlung → Grenzen**: Tageslimit senken. Oder „Ganzes Geld nutzen“ ausschalten und eine feste **Sicherheitsreserve** setzen. Den **Notfall-Nachkauf** kannst du dort auch abschalten oder sein Limit ändern.
</details>

<details><summary><b>Meinen Openern gehen ständig die Packs aus.</b></summary>

Die Opener sind der Motor der Sammlung – aber jedes Pack kostet Geld. Der Bot steckt bei knappem Vorrat das Geld zuerst in Packs (keine Erweiterungen, keine verzichtbaren Lizenzen) und verteilt die Packs so, dass alle Opener weiterlaufen. Im Dashboard siehst du den Verbrauch pro Minute und wie lange der Vorrat noch reicht. Sinkt er trotzdem weiter, reichen deine Einnahmen noch nicht für so viele Opener: weniger Opener aufstellen oder das Tageslimit erhöhen.
</details>

<details><summary><b>Ich will selbst kassieren / Kassierer einsetzen.</b></summary>

Dashboard → **Laden & Preise → Tagesablauf**: „Bot kassiert an allen freien Kassen“ aus. Dann teilt der Einsatzplan wieder Kassierer ein. Kassen, an denen du selbst stehst, lässt der Bot immer in Ruhe.
</details>

<details><summary><b>Wie deinstalliere ich ihn?</b></summary>

Ordner `BepInEx\plugins\ShopBot` löschen. Optional `BepInEx\config\ratta.shopbot.cfg` löschen. Der Spielstand bleibt normal spielbar.
</details>

## ⚠️ Hinweise

- **Mach vor dem ersten Start ein Backup deines Spielstands** (`%USERPROFILE%\AppData\LocalLow\OPNeonGames\Card Shop Simulator`).
- Der Bot greift tief ins Spiel ein (Geld ausgeben, Arbeiter umstellen, Karten verkaufen). Stell im Dashboard ein, was er darf.
- Inoffizielles Fan-Projekt, nicht verbunden mit OPNeon Games, Nexus-Mod-Autoren, PSA oder The Pokémon Company.

## Selbst bauen (für Entwickler)

```
git clone https://github.com/AndrejProgrammer/TCG-ShopBot.git
cd TCG-ShopBot
copy GameDir.local.props.example GameDir.local.props   # Pfad zum Spielordner eintragen
dotnet build -c Release
```
Braucht das **.NET SDK 8**. Nach dem Bauen wird die DLL automatisch in `BepInEx\plugins\ShopBot` kopiert (abschalten mit `-p:CopyToGame=false`).
Release-ZIP: `powershell -File tools\release.ps1` → `dist\ShopBot-x.y.z.zip`.
Details, Erkenntnisse und Fallstricke: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md) · Änderungen: [CHANGELOG.md](CHANGELOG.md)

## Lizenz

[MIT](LICENSE) – frei nutzbar, veränderbar und weitergebbar; der Copyright-Hinweis muss drinbleiben.
