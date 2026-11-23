# ShopBot – Dein Laden läuft von selbst

**ShopBot** ist ein BepInEx-Plugin für **TCG Card Shop Simulator**, das deinen Laden weitgehend automatisch führt – optimiert für das **Real TCG Overhaul**-Modpack (Pokémon).
Ziel: **alle Pokémon-Karten im Collection Tracker besitzen** (später alle als PSA 10) – während der Laden Geld verdient.

> Version **0.5.2** · Spiel: Steam-Standard-Branch (Unity 6) · BepInEx 5.4.x
> 📖 **Neu hier? → [Schritt-für-Schritt-Anleitung](docs/ANLEITUNG.md)** · 🇬🇧 [English version](README.en.md)

---

## Was der Bot alles macht

| Bereich | Automatisch |
|---|---|
| 💰 **Kasse** | Kassiert an **allen freien Kassen gleichzeitig** (scannen, Geld nehmen, Wechselgeld) – du brauchst keine Kassierer |
| 👷 **Arbeiter** | Alle Arbeiter befüllen die **Pack-Opener** (nur Packs unfertiger Pokémon-Sets), Einsatzplan nach Stärken, täglich 3× Bonus, stellt neue Arbeiter ein |
| 📦 **Packs & Sammlung** | Liest den Collection Tracker, kauft Packs der Sets mit den **meisten neuen Karten pro Dollar**, holt Kartons selbst aus dem Lager, **Notfall-Nachkauf**, wenn die Opener leerlaufen |
| ⚙️ **Pack-Opener** | Befüllt Opener selbst, startet halbvolle, sammelt fertige ein |
| 🏷️ **Preise** | Marktpreis **+20 %**, gerundet (Produkte, Einzelkarten, gegradete) |
| 🛒 **Regale & Vitrinen** | Räumt Ware selbst ein, legt Karten in leere Vitrinen-Plätze (gegradete zuerst) |
| 🗑️ **Ordnung** | Volle Kartons ins Lagerregal, leere Kartons in den Mülleimer |
| 🏅 **Graden** | Schickt Karten zu PSA (Standard, volle 52er-Einsendungen, teuerste zuerst), **öffnet zurückgekommene Pakete** und verkauft gegradete Karten (eine PSA 10 jeder Karte bleibt) |
| 🔓 **Freischalten** | Lizenzen (Packs fehlender Sets zuerst), Laden-/Lager-Erweiterungen, Upgrades |
| 🤝 **Kunden-Angebote** | Ankauf & Tausch: **neue Karten immer zum Wunschpreis**, Doppelte nur unter Marktpreis, nie mehr als verlangt, nie die letzte Karte abgeben |
| 📅 **Tagesablauf** | Rechnungen sofort bezahlen, Tag um 21 Uhr beenden, wenn der Laden leer ist |
| 🛡️ **Geld** | Darf alles ausgeben, hält aber immer die nächsten Rechnungen zurück – **nie ins Minus** |

Alles ist einzeln abschaltbar und einstellbar – im **Dashboard (F9)** oder im **Configuration Manager (F1)**.

---

## Schnellstart

1. **BepInEx 5** im Spiel installieren (ist beim Real-TCG-Overhaul-Modpack schon dabei).
2. Unter [**Releases**](../../releases) die neueste `ShopBot-x.y.z.zip` herunterladen.
3. Den Inhalt in den **Spielordner** entpacken → es entsteht `BepInEx\plugins\ShopBot\ShopBot.dll`.
4. Spiel starten, Spielstand laden – nach 10 Sekunden legt der Bot los.

👉 Ausführlich mit Bildern-Beschreibung, Spielordner finden, Fehlersuche: **[docs/ANLEITUNG.md](docs/ANLEITUNG.md)**

## Tasten

| Taste | Funktion |
|---|---|
| **F9** | Dashboard öffnen/schließen (auch ESC) |
| **F8** | Bot an/aus |
| **F1** | Configuration Manager (alle Einstellungen, Abschnitt „ShopBot“) – braucht das Plugin *ConfigurationManager* |

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

Fenster-Aktionen (Kunden-Angebote, Grading-Pakete) macht der Bot nur, wenn du **8 Sekunden nichts drückst** (einstellbar). Grading-Pakete, die länger als 60 s liegen, öffnet er auch zwischendurch (Fenster ca. 2 s).
</details>

<details><summary><b>Er gibt zu viel Geld aus.</b></summary>

Dashboard → **Sammlung → Grenzen**: Tageslimit senken. Oder in F1 „Ganzes Geld nutzen“ ausschalten und eine feste **Sicherheitsreserve** setzen. Den **Notfall-Nachkauf** kannst du dort auch abschalten oder sein Limit ändern.
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
git clone <repo-url>
cd ShopBot
copy GameDir.local.props.example GameDir.local.props   # Pfad zum Spielordner eintragen
dotnet build -c Release
```
Braucht das **.NET SDK 8**. Nach dem Bauen wird die DLL automatisch in `BepInEx\plugins\ShopBot` kopiert. Details, Erkenntnisse und Fallstricke: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md) · Änderungen: [CHANGELOG.md](CHANGELOG.md)
