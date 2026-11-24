# ShopBot – Entwickler-Notizen & Änderungsverlauf

> Interne Notizen aus der Entwicklung (Erkenntnisse, Fallstricke, Testergebnisse). Für Spieler: siehe [README](../README.md) und [Anleitung](ANLEITUNG.md).

BepInEx-Plugin, das den Laden weitgehend selbst führt: Packs öffnen, Sets vervollständigen, Preise, Kunden-Angebote, Graden, Arbeiter, Einkauf, Erweiterungen – mit In-Game-Dashboard (F9).

Stand: **23.11.2026** · Version 0.4.0 · Spiel Unity 6 (Steam Standard-Branch), BepInEx 5.4.23.6

---

## Schnellstart

| Taste | Funktion |
|---|---|
| **F9** | Dashboard öffnen/schließen (auch ESC) |
| **F8** | Bot an/aus |
| **F1** | Configuration Manager → Abschnitt „ShopBot“ (alle Zahlenwerte) |

- Der Bot startet **10 s nach dem Laden** des Spielstands (einstellbar). Im Hauptmenü tut er nichts.
- Fenster-Aktionen (Kunden-Angebote, Grading-Pakete, Tagesende) nur, wenn du **8 s nichts drückst** – damit er dir nicht dazwischenfunkt.
- Einstellungen werden gesammelt gespeichert (beim Schließen des Dashboards, beim Beenden, alle 30 s).

---

## Dateien

| Pfad | Inhalt |
|---|---|
| `D:\Mods\ShopBot\` | Quellcode (dieses Projekt) |
| `Plugin.cs` | Grundgerüst, Takt, Kunden-Angebote (Ankauf/Tausch), Opener, Tagesende, Kasse, Preise, Grading-Pakete öffnen |
| `Collection.cs` | Sammlung/Pack-Kauf, Rechnungen, Bonus, Bewertungen, Erweiterungen, Kartenablage, Arbeiter-Einsatzplan, Nachbestellen, Tageslimit |
| `Grading.cs` | Auto-Graden über CardGrade Overhaul (PSA Standard) |
| `Upgrades.cs` | Upgrades der „Upgrades“-Mod mit Sammelpunkten |
| `Store.cs` | Lizenzen freischalten, Bot räumt Regale/Vitrinen selbst ein |
| `Dashboard.cs` | In-Game-Dashboard (IMGUI) |
| `ShopBot.csproj` | Build; kopiert die DLL nach dem Bauen automatisch ins Spiel |
| Spiel: `BepInEx\plugins\ShopBot\ShopBot.dll` | installierte DLL |
| Spiel: `BepInEx\config\ratta.shopbot.cfg` | Einstellungen |
| Spiel: `BepInEx\LogOutput.log` | Log (alle Bot-Aktionen mit `[ShopBot]`) |

**Bauen:** `dotnet build D:\Mods\ShopBot\ShopBot.csproj -c Release` (.NET SDK 8 installiert). Danach Spiel neu starten.
Decompiler für Recherche: `ilspycmd` 8.2 (mit `DOTNET_ROLL_FORWARD=Major`).

**Backups Spielstand:** `D:\Mods\RTCGO-Manager\Savegame-Backup-*` (mehrere, zuletzt vor Graded-Verkauf 19.11.).

---

## Was der Bot macht

### Laden
- **Preise:** Marktpreis **+20 %** (Produkte, Einzelkarten, gegradete), **gerundet** (ab $1 ganze Dollar, darunter 10 Cent). Alle 20 s geprüft.
- **Bewertungen:** „Gibt's hier kein XXX?“ → Pack/Display nachbestellen (3 Kartons). Liegt die Ware schon im Lager (und ist kein Ziel-Set-Pack) → nicht kaufen, stattdessen 10 Min. ein zweiter Regal-Einräumer. „Zu teuer“ → Aufschlag nur für diesen Artikel −5 % (min. 10 %), erholt sich täglich.
- **Nachbestellen:** nur Packs & Displays (Kategorie TCG), wenn Regal + Lager < ½ Karton. 10 Min. Sperre pro Artikel, Liefer-Warteschlange zählt mit.
- **Selbst einräumen:** Bot legt Ware aus Kartons in Regalfächer, die schon für diese Ware vorgesehen sind (6/s), und Karten aus der Kartenablage in leere Vitrinen-Plätze (2/s).
- **Rechnungen:** Miete, Strom, Löhne sofort bezahlt (vor allen Einkäufen).
- **Tagesende:** ab 21 Uhr, wartet auf leeren Laden (max. 90 s bzw. 5 Min. wenn Kassierer noch arbeiten).
- **Erweiterungen:** nächste Laden-Erweiterung (max. 30) / Lager freischalten / Lager-Erweiterung (max. 14), wenn Level reicht und Budget ≥ 5× Preis. 1 pro Minute.
- **Lizenzen:** günstigste neue Pokémon-Pack/Display-Lizenz, wenn Level reicht und Budget ≥ 3× Preis.

### Sammlung & Packs
- Liest den **Collection Tracker**. Ziel-Sets = **Pokémon-Sets mit den wenigsten fehlenden Karten**, bei Gleichstand günstigster Packpreis.
- Anzahl Ziel-Sets: 5 + 1 je $50.000 Budget (max. 10).
- **Vorrat = Kapazität aller Pack-Opener × Füllungen** (2 + 1 je $150.000 Budget, max. 4), gedeckelt auf **15.000 Packs**.
- Kauf über Collection-Tracker „Sofort kaufen“, bis 40 Kartons pro Bestellung, **Tageslimit $60.000** (Sammlung + Nachbestellen + Bewertungen).
- **Opener-Arbeiter** befüllen nur Packs der Ziel-Sets.
- **Pack-Automaten-Boost** (Collection Tracker Belohnung) wird automatisch abgeholt.
- **Opener:** fertige einsammeln (während du spielst blitzschnell), halbvolle nach 45 s starten.

### Kunden-Angebote
- **Ankauf:** Wunschpreis zahlen, wenn ≤ Limit; sonst max. 2 Gebote nahe am Limit. Limit **95 %** Marktpreis, **125 %** für Karten, die noch fehlen; unter **$2** immer Wunschpreis.
- **Tausch:** annehmen wenn Gewinn, bis $1 Verlust, oder neue Karte für die Sammlung (bis −20 %). **Nie die letzte Karte** einer Sorte abgeben.

### Karten
- **Graden:** PSA Standard (12 Tage, $20/Karte + $10), immer **volle Einsendungen à 52 Karten**, teuerste zuerst, nur Karten ab **$50**, 1 Stück jeder Karte bleibt im Album, alle 8 Plätze.
- **Grading-Pakete** automatisch öffnen (Karten ins Album, nicht in die Hand; Zertifikate bleiben korrekt).
- **Kartenablage:** zuerst **gegradete Karten verkaufen** (PSA 10 nur Duplikate), danach doppelte normale Karten unter min($100, Grading-Grenze $50). Kapazität wird geprüft (gegradete zählen mit ihrer Note).

### Arbeiter
- **Einsatzplan jeden Morgen** nach Tempo × Level pro Aufgabe (7 % pro Level): Kassierer = Anzahl Kassen (Haupt+Neben Kasse), 1 Vitrinen, 1 Regale, Rest **Opener** (wichtigste Aufgabe).
- **3× Bonus** pro Tag an alle (+1 Level je Bonus, doppelte Erfahrung).
- Kassen-Hilfe bei Schlange, Kassierer bleiben bis zum letzten Kunden.
- **Upgrades** (Sammelpunkte): Arbeiter bleiben länger → Arbeitstempo (gleichmäßig, Opener & Kasse zuerst) → mehr Kunden → … Nie Glückspacks/God-Pack.

---

## Dashboard (F9)

7 Seiten: **Übersicht** (Geld, Umsatz/Kosten heute, Einkauf heute vs. Limit, Sets, Grading, Opener, Vorrat, Schnellaktionen, letzte Aktionen) · **Sammlung** (Ziel-Sets, alle Pokémon-Sets, Regler, Grenzen) · **Laden & Preise** (Preise, Einkauf, Bewertungen, Tagesablauf, Erweiterungen, Selbst einräumen, Lizenzen) · **Kunden** · **Karten** · **Arbeiter** (live: Rolle, Tempo, Bonus) · **Verlauf** (filterbar).

Größe passt sich an die Bildschirmhöhe an (Einstellung „Dashboard-Größe“, 0 = automatisch).

---

## Wichtige Erkenntnisse / Fallstricke (für die Entwicklung)

- **Niemals `CSingleton<T>.Instance` aufrufen, bevor das Spiel geladen ist** – es legt sonst eine LEERE Kopie an (z. B. CGameManager ohne Tasten-Daten → „F – Action Name“-Tooltips, Hänger beim Laden). Immer `S<T>()` benutzen (nur lesen, wenn `IsSet`). Auch `GameInstance.GetPriceString` nicht im Hauptmenü (greift auf CGameManager zu) → im Dashboard `Money()`.
- **Config-Namen dürfen kein `= \n \t \ " ' [ ]` enthalten** – sonst stürzt das Plugin beim Start ab. Vor dem Build prüfen.
- PowerShell-Textersetzungen auf CRLF-Dateien schlagen oft still fehl → Edit-Tool nehmen bzw. Ergebnis prüfen.
- Mods hängen sich in dieselben Funktionen (EPL, CardGrade Overhaul, ShopOS, Collection Tracker) → möglichst **spieleigene Methoden** aufrufen statt nachbauen.
- Reflection-Ziele: `CollectionTracker.CTRestockShop/CTSetOrdering`, `CardGradeOverhaul.Grading/Companies`, `Upgrades.UpStore`, `EnhancedPrefabLoader.API.Epl`.

---

## Testergebnis 19.11. (1 Std. Spielzeit, automatischer Testbericht)

| Funktion | Ergebnis |
|---|---|
| Start, Rechnungen, Bonus, Einsatzplan, Tagesende, Opener, Upgrades | ✅ |
| Sammlung + Vorrat (13 Opener, 8.512 Kapazität → 15.000 Packs gedeckelt) | ✅ |
| Tageslimit $60.000 | ✅ greift |
| Lizenzen freischalten | ✅ 47 Lizenzen gekauft |
| Erweiterungen | ✅ Lager-Erweiterung 12 gekauft |
| Bewertungen: „liegt im Lager – einräumen statt kaufen“ + Aushilfe Regale | ✅ |
| Vitrinen selbst befüllen | ✅ 1.080 Karten |
| Kunden Ankauf / Tausch | ✅ 15 Ankäufe, Tausche mit neuen Karten |
| **Regale selbst einräumen** | ❌ 2.839× Fehler (`GetFirstItem` auf ungeöffnetem Karton) → **20.11. behoben**: Karton wird erst geöffnet (wie Arbeiter), Kartons im Lagerregal ausgelassen. ✅ **20.11. getestet: 210+ Artikel eingeräumt, 0 Fehler** |
| Graden / Grading-Pakete | ⏳ nicht vorgekommen (alle 8 Plätze belegt, Pakete brauchen 12 Tage) |
| Preis bei Beschwerde senken | ⏳ keine Preis-Beschwerde vorgekommen |
| Testbericht-Skript | Umlaute wurden falsch gelesen → behoben (Log als UTF-8) |

Testbericht starten (läuft eigenständig, schreibt `TESTBERICHT.md`):
`powershell -NoProfile -ExecutionPolicy Bypass -File D:\Mods\ShopBot\tools\testbericht.ps1`

## Offene Punkte / Ideen

- [x] **Regale selbst einräumen** nach dem Fix getestet – läuft (20.11.).
- [ ] Grading-Pakete öffnen testen, sobald Einsendungen zurückkommen.
- [ ] Dashboard-Optik nach Rundungs-Fix prüfen.
- [ ] Kartenablage war überfüllt (16.583/10.000) – beobachten, ob sie sich leert.
- [ ] Ideen: Tagesbericht (Einnahmen/Ausgaben pro Tag) im Dashboard, dynamisch mehr Vitrinen-Arbeiter bei voller Ablage, gegradete Karten mit eigenem Aufschlag, ShopBot-Version hochzählen.

## Mod-Stand (Real TCG Overhaul Mod Manager 2.1.1)

35 Mods aktiv, alle aktuell (19.11.): u. a. CardGradeOverhaul 1.2.7, RTCGO Holographics 1.0.8, EnhancedPrefabLoader 6.9.0, Collection Tracker 3.1.4, QOLShopOS 1.2.4, TextureReplacer 1.7.9, Purchase Overhaul 1.3.6, Pokemon-Pakete aus Mod 577 (inkl. Pack Opening Machines, Figurines 3.8), Master Set Displays, Custom Customers.

## Änderungen 20.11.

- **Arbeiter-Rollen neu:** 3 Kassierer (Kasse/Kasse), **alle anderen: Opener als Haupt- UND Nebenrolle**. Regale/Vitrinen = 0 Arbeiter, das macht der Bot selbst (Config „9. Arbeiter“, wieder erhöhbar).
- **Opener-Arbeiter füllen nur Packs aus unfertigen Pokémon-Sets** (alle kaufbaren Sets mit fehlenden Karten, nicht nur die Fokus-Sets). Packs fertiger Sets werden abgewählt.
- **Kassen-Turbo** (Config „5. Kasse“, Standard 2×): erhöht `InteractableCashierCounter.m_NPCScanItemSpeed` (Spielwert 1,5) → Kassierer scannen doppelt so schnell. Der Bot selbst kann nicht an 3 Kassen gleichzeitig stehen (Spiel erlaubt nur eine Spieler-Kasse).
- Sets kaufen bleibt die Hauptaufgabe des Budgets (unverändert: Fokus-Sets, großer Vorrat, Tageslimit).
- **Lizenz-Fix:** „Base Set Booster Box“ wurde bei jedem Spielstart erneut für $100 gekauft (Spiel/EPL setzt das Flag beim Laden zurück). Gekaufte Lizenzen merkt sich der Bot jetzt dauerhaft in der Config (`15. Lizenzen → Schon gekauft (intern)`), außerdem wird nicht gekauft, wenn derselbe Artikel unter anderem Eintrag schon frei ist.
- 20.11. getestet: Einsatzplan Tag 101 = 3× Kasse/Kasse, 6× Opener/Opener ✅

## Änderungen 23.11. (v0.2.0) – mehr Durchsatz

- **Bot befüllt Pack-Opener selbst** (Config „2. Pack-Opener“ → „Bot befüllt Opener selbst“, 40 Packs/s): nimmt Packs unfertiger Pokémon-Sets aus Kartons auf dem Boden (Ziel-Sets zuerst, angebrochene Kartons zuerst) und legt sie per Spielfunktion `DispenseItemFromBox` in freie Opener. Volle Opener starten automatisch. Kartons im Lagerregal holen weiterhin die Opener-Arbeiter. → Opener stehen nach dem Einsammeln nicht mehr leer herum.
- **Arbeiter automatisch einstellen** („9. Arbeiter“): günstigster freier Arbeiter, sobald Level reicht und Budget ≥ 10× Einstellgebühr; danach sofort neuer Einsatzplan (= weiterer Opener). Max. Anzahl einstellbar (0 = alle).
- **Lizenz-Fix v2:** gekaufte Lizenzen werden jetzt nach **Artikel-Namen** gemerkt (die Typ-Nummern der Mod-Artikel ändern sich bei jedem Start – darum wurde „Base Set Booster Box“ trotz Fix vom 20.11. weiter jedes Mal gekauft, Log 23.11.). Alte Zahlen-Einträge werden ignoriert → ein letzter Kauf, dann nie wieder.
- **Optional (Standard aus):**
  - „Tageslimit mindestens Prozent vom Geld“ – Limit wächst mit dem Kontostand (z. B. 10 % bei $1,09 Mio. ≈ $109.000/Tag statt $60.000).
  - „Opener-Turbo (Tempo x)“ – Opener öffnen Packs schneller (wie Kassen-Turbo). Übernimmt Änderungen durch den Collection-Tracker-Boost als neuen Grundwert.
- Dashboard: Seite **Karten → Pack-Opener** (neue Schalter + Zähler), **Arbeiter → Steuerung** (Einstellen), **Sammlung → Grenzen** (Limit-Prozent, wirksames Limit).
- Quell-Backup vor v0.2: `D:\Mods\ShopBot-Backups\src-2026-11-23-vor-v0.2\` (inkl. Config).

**Zu testen:** Log nach „Opener selbst befüllt“, „Neuer Arbeiter eingestellt“, und dass „Neue Lizenz: Base Set Booster Box“ nach dem nächsten Start nur noch **einmal** kommt.

## Änderungen 23.11. (v0.3.0) – Einsortieren & Grading-Pakete

- **Neu `Sorting.cs` – herumliegende Kartons einsortieren** (Config „17. Einsortieren“, 4 Kartons/s):
  - Volle Kartons am Boden → **Lagerregal** (wie Arbeiter: `DispenseItem` auf Lagerfach; erst Fächer mit gleicher Ware, dann leere).
  - **Bleiben liegen:** Ware, die im Laden ein Regalfach hat (der Bot räumt daraus selbst ein – aus dem Lagerregal kann nur ein Arbeiter etwas holen) und Packs unfertiger Sets bis zu einer Opener-Füllung (daraus befüllt der Bot die Opener). Der restliche Pack-Vorrat kommt ins Lager → die Opener-Arbeiter holen ihn dort.
  - **Leere Kartons → Mülleimer** (nur wenn einer im Laden steht).
  - Lager voll → Hinweis im Verlauf (alle 10 Min.).
- **Grading-Pakete öffnen repariert:** CardGrade Overhaul zeigt große Pakete (52 Karten) in mehreren Runden. Alle Karten sind schon beim Öffnen im Album – der Bot verwirft jetzt die restlichen Runden (`Reveal.RestToAlbum`), statt im Fenster hängen zu bleiben. Liegt ein Paket > 60 s, öffnet er es auch, während du spielst (Fenster ~2 s).
- **Gegradete Karten verkaufen repariert:** Die Kartenablage war voll (16.583/10.000) → es passten keine gegradeten Karten mehr hinein. Jetzt legt der Bot die **billigsten normalen Doppelten zurück ins Album**, bis Platz ist. Regel unverändert: alles verkaufen, **PSA 10 nur, wenn es davon mehr als eine gibt**.
- **Vitrinen: gegradete Karten zuerst.** Das Spiel wählt zufällig unter allen Einträgen der Ablage (gegradete kamen bei Tausenden normalen Karten fast nie dran).
- **Lizenz-Fix v3:** Log zeigt, dass beim Laden **viele** Lizenzen zurückgesetzt werden (Jungle, Fossil, Base Set 2 …), nicht nur eine. Schon bezahlte Lizenzen (nach Name gemerkt) werden jetzt **kostenlos wieder freigeschaltet** → Ware bleibt bestellbar, kein Doppelkauf. Nach dem Update kauft der Bot jede Lizenz ein letztes Mal (alte Config hatte nur Zahlen).

## Änderungen 23.11. (v0.4.0) – Bot-Kasse: alle Arbeiter an die Opener

- **Neu `Checkout.cs` – Bot kassiert an ALLEN freien Kassen gleichzeitig** (Config „5. Kasse“ → „Bot kassiert an allen freien Kassen“, 8 Artikel/s pro Kasse). Ablauf wie der Arbeiter im Spiel (`InteractableCashierCounter.Update`): Artikel + Karten scannen → Geld/Karte nehmen → Kartenzahlung bestätigen bzw. passendes Wechselgeld → abschließen (`OnPressSpaceBar`, Einnahmen und Statistik wie beim Arbeiter). Läuft jedes Bild, auch wenn Fenster offen sind. Kassen, an denen du oder ein Arbeiter steht, lässt der Bot in Ruhe. Kunden stellen sich auch an unbesetzten Kassen an (`ShelfManager.GetCashierCounter`, Fallback).
- **Einsatzplan:** Bot-Kasse an → **0 Kassierer, alle Arbeiter Opener / Opener** (nur Packs unfertiger Pokémon-Sets inkl. Pocket). Kassen-Hilfe (Arbeiter ausleihen) ist dann aus.
- **Lizenzen nach bester Marge:** (Marktpreis + Aufschlag) / Einkaufspreis, bei Gleichstand die günstigere Lizenz.
- **PSA-10-Sammlung** (Config „12. Graden“, Standard aus = Später-Ziel): füllt die Einsendungen zusätzlich mit billigen Doppelten auf, von denen es noch keine PSA 10 gibt. Wertvolle Karten (ab $50) haben weiter Vorrang, eine PSA 10 jeder Karte bleibt immer.
- **Zu testen:** Verlauf „Bot-Kasse: N Kunden abkassiert“, Kasse stimmt (Geld steigt), keine „[Kasse]“-Warnungen im Log; Einsatzplan zeigt nur noch Opener.

## Änderungen 23.11. (Live-Test, nach v0.4.0)

Live-Test mit Log-Mitlesen: Bot-Kasse 125 Kunden / $82.757 an einem Tag ohne Kassierer, alle 9 Arbeiter Opener, 9 Grading-Pakete (468 Karten) geöffnet, 432 gegradete Karten (~$1,17 Mio.) in den Verkauf.

- **Grading-Pakete:** Ursache gefunden, warum nie eins aufging – `GradedCardOpeningSequence` meldet sich erst beim ersten Öffnen als Singleton an (`CSingleton` lädt faul), `S<T>()` lieferte null. Jetzt `FindGradedSequence()` (angemeldete Instanz, sonst Objekt in der Szene). Prüfung „Fenster offen“ über `m_ScreenGrp.activeSelf` (das Spiel setzt `m_IsScreenActive` nie zurück). Gründe fürs Nicht-Öffnen stehen im Log (`[Grading-Paket] nicht geöffnet: …`, 1×/Min.).
- **Tagesende:** Bot-Kasse zählt als „Kasse arbeitet“ → bis 5 Min. auf die letzten Kunden warten (vorher 90 s).
- **Lizenzen:** Booster-Box-Lizenzen werden von einer Mod direkt nach dem Kauf wieder gesperrt. Wiederherstellen max. 2×/Tag pro Eintrag; Lizenzen, die sich in einer Sitzung erneut sperren, merkt sich der Bot („Sperren sich immer wieder (intern)“) – ab 2 derselben Art (letzte zwei Wörter, z. B. „booster box“) kauft er davon keine neuen mehr.
- **Leere Kartons:** nur geöffnete, die ≥ 5 s leer sind, kommen in den Müll.
- **v0.4.1 Lizenzen: alle auf einmal** (bis 25 pro Minute, solange Budget ≥ 3× Lizenzpreis). Reihenfolge: Packs von Sets, für die die Sammlung noch keine kaufbaren Packs hat (z. B. 1st Edition …) → übrige Packs → Rest, jeweils beste Marge. Booster-Box-Art wird übersprungen, sobald 2 davon als „sperren sich immer wieder“ erkannt sind.
- **Notfall-Nachkauf** (Config „7. Sammlung“): alle 10 s Pack-Vorrat unfertiger Sets prüfen (Boden + **Lagerregal** + unterwegs). Unter 1 Opener-Füllung → sofort Sammlung-Einkauf, Bestellsperre (10 Min.) nur noch, wenn vom Set wirklich etwas unterwegs ist, und zusätzlich zum Tageslimit bis **$100.000/Tag Notfall-Limit**. Neu: Kartons im Lagerregal werden mitgezählt (`ShelfCompartment.m_StoredBoxRecordList`) – vorher fehlten sie in der Vorrats-Rechnung.
- **Vorrat mind. 1 Opener-Füllung:** Ziel-Vorrat = max(„Max. Packs im Vorrat gesamt“, Kapazität aller Opener). Anlass: 23 Opener = 26.432 Packs Kapazität > alte Grenze 15.000 → Opener wären nie voll geworden.

## v0.4.2 (23.11.) – Geld: alles nutzen, nie ins Minus

- **Fehler behoben:** Abbuchungen laufen über `CEventPlayer_ReduceCoin` und kommen erst im nächsten Bild an. Der Bot kaufte innerhalb eines Durchgangs mehrere Sets mit demselben (alten) Kontostand → Konto ging ins Minus. Jetzt `Pay()`/`MarkSpent()`: eigene Ausgaben werden 3 s lang sofort vom Budget abgezogen (Packs, Nachbestellen, Lizenzen, Erweiterungen, Bonus, Einstellen).
- **„Ganzes Geld nutzen (nur Rechnungen zurückhalten)“** (Standard an, Wunsch des Users): statt $20.000 Reserve werden nur offene Rechnungen + 1,5× die letzten Tagesrechnungen (~$4.300/Tag) zurückgehalten. Dashboard zeigt „zurückgehalten“.

## v0.5.0 (23.11. abends)

- **EPL-Lizenz-Falle (wichtig):** Enhanced Prefab Loader (CollectionWeaver) leitet Zugriffe auf `CPlayerData.m_IsItemLicenseUnlocked`, `m_RestockDataList` u. a. **nur im Spielcode** auf seinen eigenen Speicher um (Mod-Artikel ab Index > Vanilla-Anzahl, gespeichert als `ItemSaveData.IsBigBoxUnlocked/IsSmallBoxUnlocked`). Plugins, die die Liste **direkt** lesen, sehen Mod-Lizenzen immer als gesperrt. → Immer `CPlayerData.GetIsItemLicenseUnlocked(i)` und `InventoryBase.GetRestockData(i)` benutzen (Spielmethoden = umgeleitet). Weitere umgeleitete Felder: `m_SetItemPriceList`, `m_GeneratedMarketPriceList`, `m_AverageItemCostList`, `m_CurrentTotalItemCountList`, `m_ItemDataList` … (Liste in `EnhancedPrefabLoader.Core.InterceptorHandlers`).
- **Kartons aus dem Lager:** `ShelfCompartment.TryPopLastStoredBoxRecord` + `StoredBoxVisualBatcher.RebuildImmediate` + `RestockManager.SpawnPackageBoxItemAtTransform` (wie `InteractableStorageCompartment.OnMouseButtonUp`). Lagerkartons sind nur Einträge (`StoredBoxRecord`), keine Objekte.
- **Graden:** PSA Standard bleibt – höchster Durchsatz pro Platz (52/12 Tage = 4,3/Tag vs. 24/7 = 3,4, 10/4 = 2,5, 4/2 = 2).
- **IMGUI:** keine Steuerelemente bedingt ein-/ausblenden, die sich durch einen Klick im selben Bild ändern (Layout/Repaint-Fehler).

## Kunden-Ankauf: Annahme-Formel des Spiels (CustomerTradeCardScreen.OnPressAccept)
- `r = Gebot/Wunsch - 1`, Chance = `100·(1+r)`; Abzug r<-0.5: 45–75, <-0.4: 35–65, <-0.3: 25–55, <-0.2: 15–35, <-0.1: 5–15.
- Gebot > Marktpreis +5–15, < Marktpreis −5–15. Je bisheriger Ablehnung −5–15; Gebot ≤ letztes Gebot zusätzlich −10–25 je Ablehnung. Gebot ≥ Wunsch − 0,01 → 100 %.
- Bei Ablehnung: mit Chance `Chance·rand(1..2)` (Wunsch > Markt: 1..10) **feilscht** der Kunde → Wunschpreis sinkt Richtung Gebot; sonst zählt `m_MaxDeclineCount` runter (Start 0–4), bei 0 geht er.
- Bot (v0.5.3): neue Karten = Wunschpreis; Doppelte = Leiter 60 % (billig 50 %) → 95 %, Schritte = verbleibende Ablehnungen + 1.

## v0.5.4 (24.11.) – Opener ständig befüllt

Testsitzung Tag 103/104: 0 Fehler. Lizenzen, Grading-Pakete (2× 52), Graden, Feilschen (Runde 2/3), Upgrades, Lager-Erweiterung, Tagesende erstmals im Log bestätigt.
Problem: Pack-Vorrat lag fast durchgehend unter 1 Opener-Füllung (25 Opener, 30.016 Kapazität, Vorrat 5–13k). Ursachen:
- ~$176k Lizenzen + $31,6k Lager-Erweiterung in einer Sitzung, während die Opener hungerten (Faktor-3-Prüfung beachtet den Pack-Bedarf nicht).
- `StockOpeners` füllte einen Opener ganz (bis ~1.200 Packs), bevor der nächste drankam; Teilfüllungen starteten erst nach 45 s.
- Einkommen ≈ Verbrauch: Packs kosten ~$9–15 (Space-Time Smackdown $8,83, Wisdom of Sea and Sky $14,54); Bot-Kasse ~$217k/Tag.

Änderungen:
- `UpdatePackSupply` (alle 10 s in `HandlePackEmergency`): Vorrat, Bedarf, Verbrauch/Min (geglättet, gekaufte Packs eingerechnet), `_packStarving` = Vorrat < Notfall-Schwelle.
- **Packs zuerst** (Config „7. Sammlung“): bei Knappheit keine Erweiterungen, Lizenzen nur Prio 0 (Packs für Sets ohne kaufbare Packs). Log `[Lizenzen] N warten` / `[Erweiterungen] warten` alle 10 Min.
- **Gleichmäßig verteilen** (Config „2. Pack-Opener“): reichen die Packs am Boden nicht für den freien Platz, bekommt jeder freie Opener `max(Mindestportion 64, Packs/freie Opener)` und wird sofort gestartet (`OnMouseButtonUp`).
- Dashboard: Pack-Vorrat zeigt Verbrauch/Min und Reichweite in Minuten.
- Einsortieren: „passen nicht mehr“ nennt jetzt die Artikel (+ groß/klein).

## v0.6.0 (24.11.) – Sprachen, neues Dashboard, öffentliche Version

- **Übersetzung (`Lang.cs`):** Englisch ist die Quellsprache im Code. UI: `T("…")`, mit Werten `F($"… {x}")` (FormattableString → Vorlage `{0}` wird übersetzt). Meldungen: `Note("…")` / `NoteF($"…")` – Log bekommt Englisch (`Lang.Source`), der Verlauf speichert Vorlage + Werte und übersetzt beim Anzeigen (Sprachwechsel wirkt auch rückwirkend). Teile, die erst beim Anzeigen übersetzt werden: `L("…")` (LStr), Listen: `new LList(items, sep)`.
- **Achtung C#:** `T(string)` und `T(FormattableString)` gleichnamig geht nicht – ein `$"…"` nimmt immer die string-Überladung. Deshalb getrennte Namen `T`/`F` und `Note`/`NoteF`.
- **Statische Felder** nie mit `T()` initialisieren (laufen vor dem Laden der Sprache) – dort englischen Text speichern und beim Anzeigen `T()`/`L()`.
- **Sprachdateien:** `lang/<code>.txt`, als EmbeddedResource `ShopBot.lang.<code>.txt` in der DLL; Dateien in `BepInEx/plugins/ShopBot/lang/` überschreiben/ergänzen. Format `Englisch = Übersetzung`, `=` im Text als `\=`, Zeilenumbruch `\n`, `@name = Sprachname`. `node tools/i18n-keys.js` sammelt alle Schlüssel (T/F/Note/NoteF/L + Zeilen mit `// i18n` bzw. `// i18n-last`) nach `lang/en.txt` und meldet fehlende pro Sprache (`--missing` hängt sie an).
- Interne Vergleichswerte (z. B. `TaskName()` → "Cashier", "Opener") sind englische Schlüssel, übersetzt wird erst beim Anzeigen.
- **Config englisch + Migration (`ConfigMigration.cs`):** Tabelle alt (deutsch) → neu. Beim Start werden Werte aus `ConfigFile.OrphanedEntries` (per Traverse) übernommen, alte Einträge entfernt, gespeichert; dann Sprache = de.
- **Dashboard-Animation:** Werte pro Control in `_anim` (Schlüssel `GUIUtility.GetControlID`), nur bei `EventType.Repaint` nachgeführt (sonst mehrfach pro Bild). Feste IDs ≥ 0x7F000000, damit sie nicht mit Unity-IDs kollidieren. Sanftes Scrollen: Mausrad-Event abfangen (`Use()`), Ziel in `_scrollTarget`, in `Update` interpolieren; Inhaltshöhe aus einem leeren Rect am Ende der ScrollView. Sprach-Popup: Klicks am Anfang der Fensterfunktion abfangen, sonst bekommt sie das darunterliegende Control.
- **Build:** `-p:CopyToGame=false` verhindert das Kopieren ins Spiel (läuft das Spiel, wird die DLL sonst schon überschrieben). Release: `tools/release.ps1` → `dist/ShopBot-<version>.zip`.

## v0.6.1–0.6.3 (25.11.) – Neustart-Werte, Regale, Kartenablagen

- **Tageswerte über Neustart (`DayState.cs`):** Ausgaben des Spieltags + Tagesbericht in internen Config-Einträgen („99. Internal“, in F1 versteckt über eine Klasse `ConfigurationManagerAttributes { Browsable = false }`). Rückfall: `CPlayerData.m_GameReportDataCollect.supplyCost` (wird mit dem Spielstand gespeichert). Beim Wechsel ins Hauptmenü `ResetDayStateRestore()`.
- **Regale – drei Fallen, alle im Spiel bestätigt (Regal-Übersicht `[Shelves]` im Log):**
  1. **Hängender Merker:** Gelieferte Kartons vor dem Eingang behalten `m_IsTogglingOpenClose = true` (tausende Treffer pro Minute) – der Bot hielt sie für „wird gerade geöffnet“ und nahm sie nie. `BoxBusy()` gibt sie frei, wenn sie still liegen: „öffnen“ nach 3 s, getragen/bewegt nach 15 s.
  2. **`ShelfCompartment.CheckItemType()` schaut auf den Inhalt**, nicht aufs Etikett: bei leerem Fach kommt nicht die Etikett-Ware zurück → Abbruch. Leere Fächer: erstes Stück mit `InteractablePackagingBox_Item.DispenseItem(false, comp)` (Spiel prüft Passform und setzt das Etikett), Rest manuell.
  3. **Nachbestellen** prüfte „Regal gesamt ≥ halber Karton“ – ein paar halb volle Fächer versteckten 26 leere. Jetzt Lücke = Σ `GetMaxItemCount()` der beschrifteten Fächer − Inhalt, bis 3 Kartons; zählt nicht gegen das Pack-Tageslimit (das war in Minuten verbraucht).
- **Etikett überschreiben geht:** `DispenseItem` in ein leeres Fach mit fremdem Etikett belegt es neu (Neubelegung 100 % erfolgreich).
- **Wichtig: `FirstOrDefault()` auf `EItemType`** liefert den ersten Enum-Wert `BasicCardPack`, nicht `None` → nullable verwenden.
- **Kein Lagerregal** beim User (Shop B frei, aber keine Regale) – Kartons liegen vor dem Eingang. `m_WarehouseShelfList` leer → kein Holen aus dem Lager versuchen.
- **Kartenablagen:** Doppelte über alle Ablagen mit Platz, gegradete in die mit dem meisten Platz; Vitrinen nehmen aus allen (gegradete zuerst). Verkaufsregeln: normale Karten nur Duplikate (1 bleibt), gegradete zuerst, genau 1 PSA 10 je Karte bleibt.
