# Änderungen

Ausführliche Entwickler-Notizen: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)

## 0.6.2 – 25.11.2026
- **Fix – shop shelves were never restocked:** reordering goods for the shelves (and goods customers miss) no longer counts against the pack daily limit – pack buying used it up within minutes. The many pack boxes for the openers no longer count toward the box limit either. Only the safety reserve applies.
- **Fix – boxes in front of the entrance were never used:** delivered boxes can keep a "being carried/moved/opened" flag forever, so the bot skipped them (seen: 176 boxes, all "busy"). A flagged box that has not moved for 15 s is now treated as free – for shelves, openers and sorting.
- **Fix – empty shelf slots with a label stayed empty:** the bot asked the game which goods are inside the slot – for an empty slot that is "none", so it stopped. Only half-full slots were topped up. Now the first item goes in the game's own way, the rest as before.
- **Shelf status** once a minute in the log (`[Shelves] …`) and in the dashboard: empty slots with/without label, items stocked, goods without any box.
- **Empty shelves without a label get filled:** the bot picks goods itself – boxes on the floor whose goods have the fewest shelf slots, otherwise goods from the storage shelf that are on no shelf yet. Booster boxes/displays first, then packs; never packs meant for the openers. The first item is placed the game's own way, so the game checks the fit and labels the slot. Can be switched off.
- **All card storages are used:** duplicates are spread over every card storage with room (not only the first one); a single graded card no longer blocks the duplicates. Graded cards go into the storage with the most room. Runs every 2 minutes instead of 5.
- **Display cases take cards from all card storages** – the ones with graded cards first.
- Selling rules unchanged: normal cards only as duplicates (one copy always stays, sets stay complete), graded cards first, exactly one PSA 10 of every card stays.

## 0.6.1 – 25.11.2026
- **Fix – daily limit after a restart:** "spent today" is now kept when you restart the game on the same game day (saved by the bot, with the game's own day report as fallback). Before, the bot could spend the daily limit twice. The emergency limit is kept too.
- **Daily report survives a restart:** today's numbers and the last 7 days are saved – the pace ("new cards per day") no longer starts from zero.
- Dashboard: pack stock shows "calculated after the first round" instead of "0 / 0" right after loading; sidebar footer no longer cut off.
- Old unused German config entries from very old versions are removed automatically; internal entries are hidden in the Configuration Manager.

## 0.6.0 – 24.11.2026 – first public version
- **English & German:** the dashboard, status line and history are available in English (default) and German – switch with the language button at the top right or on the new **Settings** page. More languages: drop a translated `lang/<code>.txt` into `BepInEx/plugins/ShopBot/lang/`. Old history entries switch language too.
- **New dashboard design:** smooth open/close and page animations, gliding bars and switches, hover effects, smooth scrolling, sliding sidebar marker, cards with a subtle edge, gradient header.
- **Settings page:** language, dashboard size, status line, hotkeys, timing.
- **English log and config:** the log file is always English (readable bug reports). Config names are English now – **your old settings are carried over automatically** on the first start; whoever used a German version gets the German dashboard.
- Dashboard controls for "Packs first", emergency restock, even spreading and the minimum portion per opener.
- Release ZIP (`tools/release.ps1`), MIT license, English guide (`docs/INSTALL.md`), issue templates.

## 0.5.4 – 24.11.2026
- **Opener ständig befüllt:** Ist der Pack-Vorrat knapp (weniger als eine Opener-Füllung), verteilt der Bot die vorhandenen Packs gleichmäßig auf alle freien Opener und startet sie sofort – alle laufen, statt dass wenige voll und der Rest leer ist (abschaltbar, Mindestportion 64 Packs).
- **Packs zuerst:** Bei knappem Vorrat wartet der Bot mit Erweiterungen und Lizenzen, die nur „nice to have“ sind (z. B. Booster Boxes). Lizenzen für Sets ganz ohne kaufbare Packs gibt es weiter. Abschaltbar.
- **Dashboard:** Pack-Vorrat zeigt Verbrauch pro Minute und wie lange der Vorrat noch reicht.
- Meldung „Kartons passen nicht ins Lagerregal“ nennt jetzt die Artikel.
- Testbericht prüft die Funktionen ab 0.5.

## 0.5.3 – 23.11.2026
- **Feilschen bei doppelten Karten:** Start bei 60 % Marktpreis (Karten unter $10: 50 %), dann in gleichmäßigen Schritten bis 95 % – so viele Schritte, wie der Kunde noch Ablehnungen übrig hat (liest das Spiel aus). Jedes Gebot ist höher als das letzte (sonst sinkt die Annahme-Chance stark). Hat der Kunde keine Ablehnung mehr übrig, bietet der Bot gleich sein Maximum. Senkt der Kunde seinen Wunschpreis unter das nächste Gebot, zahlt der Bot den Wunschpreis. Aussichtslose Angebote (Wunschpreis mehr als doppelt so hoch wie unser Maximum) lehnt er sofort ab.

## 0.5.2 – 23.11.2026
- **Neue Karten: immer genau den Wunschpreis zahlen** (nie mehr, unabhängig vom Marktpreis) – nur die Grenze von 10 % des Geldes pro Karte gilt. Abschaltbar.
- Gebote werden auf den Cent **auf**gerundet statt 1 Cent über dem Wunschpreis.

## 0.5.1 – 23.11.2026
- **Kunden-Ankauf neu gedacht:** Der Bot bietet nie mehr als der Wunschpreis – er prüft nur, ob sich der Preis lohnt. Doppelte bis 95 % Marktpreis (Weiterverkauf +20 %). **Neue Karten** bis 200 % Marktpreis, bei Sets, die **pausiert** sind oder **keine kaufbaren Packs** haben, bis 500 % (der Kunde ist dort die einzige Quelle). Höchstens 10 % vom Geld pro Karte. Liegt der Wunschpreis weit darüber, lehnt er sofort ab, statt den Kunden mit Geboten ungeduldig zu machen.
- **Tausch:** Für eine neue Karte gibt der Bot Doppelte bis zu diesem Wert her (nie die letzte Karte).
- **Ziel-Übersicht** (Dashboard → Sammlung): gesammelte / fehlende Pokémon-Karten, neue Karten pro Tag, geschätzte Spieltage bis alles da ist, Sets ohne kaufbare Packs, pausierte Sets.
- **Englische Anleitung:** [README.en.md](README.en.md).

## 0.5.0 – 23.11.2026
- **Kartons aus dem Lagerregal holen:** Fehlen Packs für freie Opener oder Ware für halb leere Regalfächer, holt der Bot Kartons selbst aus dem Lager (wie ein Klick aufs Lagerfach) – alle Opener laufen voll, auch mit wenigen Arbeitern.
- **Klügere Set-Auswahl:** Sets mit den meisten neuen Karten pro Dollar zuerst (abschaltbar). Fehlen nur noch 1–3 Karten, kauft der Bot höchstens 1.500 Packs pro fehlender Karte und pausiert das Set dann (die Karten lieber von Kunden ankaufen).
- **Lizenzen richtig gelesen:** Mod-Artikel (Enhanced Prefab Loader) speichern ihre Lizenz extra – der Bot las die Liste direkt und hielt freigeschaltete Booster Boxes für gesperrt. Jetzt über die Spielfunktion → keine falschen „wieder gesperrt“-Meldungen, Booster-Box-Lizenzen werden wieder normal gekauft.
- **Vitrinen voll:** Hinweis im Dashboard und Verlauf, wenn Karten in der Ablage warten, aber kein Vitrinen-Platz frei ist.
- **Tagesbericht** im Dashboard (Einnahmen Bot-Kasse, Bot-Ausgaben, Kunden, Packs – heute + letzte Tage).
- **Hinweis bei fehlenden Mods** (Collection Tracker, CardGrade Overhaul, Upgrades) im Dashboard.
- **Tageslimit wächst mit** (mind. 15 % vom Geld), wenn „Ganzes Geld nutzen“ an ist.


## 0.4.2 – 23.11.2026
- **Geld nie im Minus:** eigene Ausgaben werden sofort vom Budget abgezogen (vorher konnte der Bot innerhalb eines Einkaufs mehrfach mit altem Kontostand kaufen).
- **Ganzes Geld nutzen:** statt fester Reserve werden nur offene + nächste Rechnungen zurückgehalten (abschaltbar).
- **Notfall-Nachkauf**, wenn die Opener-Packs ausgehen (auch über das Tageslimit, eigenes Notfall-Limit).
- Vorrat immer mindestens eine volle Opener-Füllung; Kartons im Lagerregal werden mitgezählt.
- Lizenzen: alle verfügbaren auf einmal, Packs fehlender Sets zuerst; Lizenzarten, die eine Mod immer wieder sperrt, werden nicht mehr gekauft.
- Grading-Pakete: Öffnungsfenster wird zuverlässig gefunden; Grund fürs Nicht-Öffnen steht im Log.
- Tagesende wartet auch an der Bot-Kasse bis zu 5 Min. auf die letzten Kunden.

## 0.4.0
- **Bot-Kasse:** kassiert an allen freien Kassen gleichzeitig → alle Arbeiter befüllen Opener.
- Lizenzen nach bester Marge, optionale PSA-10-Sammlung.

## 0.3.0
- Kartons einsortieren (Lagerregal) und leere Kartons wegwerfen.
- Grading-Pakete mit CardGrade Overhaul (mehrere Runden) korrekt öffnen.
- Volle Kartenablage: billige Doppelte zurück ins Album, damit gegradete Karten verkauft werden; Vitrinen bekommen gegradete zuerst.
- Bereits bezahlte Lizenzen werden kostenlos wiederhergestellt.

## 0.2.0
- Bot befüllt Pack-Opener selbst, stellt neue Arbeiter ein.
- Optional: Tageslimit wächst mit dem Geld, Opener-Turbo.

## 0.1.0
- Erste Version: Preise, Kunden-Angebote, Opener, Tagesende, Rechnungen, Sammlung/Pack-Kauf, Graden, Arbeiter-Einsatzplan, Upgrades, Erweiterungen, Lizenzen, Selbst einräumen, Dashboard.
