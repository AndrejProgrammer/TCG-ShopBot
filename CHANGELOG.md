# Änderungen

Ausführliche Entwickler-Notizen: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)

## 0.7.3 – 25.11.2026 – invests wisely
- **Expansions by payback** (with budget pots): a shop expansion only when shelf space is the bottleneck and the extra rent is at most ~10 % of the profit per day; warehouse only when boxes recently did not fit on the storage shelves.
- **Hiring by payback** (with budget pots): only when work has been left over for 5 minutes in a row and the wage is at most ~5 % of the profit per day.
- **Opening packs by hand early on:** while there is no pack opener in the shop, the bot opens collection packs itself – with the same game call the pack opener machine uses (card odds and god packs stay the game's own) – 20 packs per minute, adjustable, can be switched off. Stops as soon as you place an opener.
- **Safeguard "shelves before openers":** if the need of a shelf slot does not go down for 3 minutes (slot cannot be filled), the openers get those boxes again.
- **Shelf restocking limit per day** (optional, default off).
- **Daily values per save:** spent today and the daily report are kept per save slot – two saves no longer mix their numbers.
- Advisor: concrete opener tip ("about N openers are enough for your income"), packs per day from the measured game clock.
- **Notifications:** short notes at the top of the screen for important events – set complete, new license, grading package opened, new game phase, expansion, new worker, a feature switched off. Can be switched off (Settings).
- **"Set complete!"** message when a set is finished, and **PSA 10 progress** on the Collection page (cards you own as PSA 10).
- **Prices by sales speed (optional, off by default):** every 15 minutes products that sell out fast get +2 % markup, products that do not sell at all −2 %, within 10–40 % (adjustable). Lowered markups after complaints still win. Learned markups are kept across restarts.

## 0.7.2 – 25.11.2026 – budget pots
- **Budget pots:** once a full day is measured (and "Adapt settings to the game phase" is on), the profit per day (income − rent, electricity, wages) is split: **reserve** (3 days of fixed costs, always kept) → **worthwhile investments** (licenses for sets without packs, expansions when shelf space is the bottleneck) → **packs**. Collect/Endgame: ~85 % of the profit goes to packs (instead of a fixed daily limit), Build-up 40 %, Start 10 %. A quarter of idle money on the account goes to packs on top. The emergency restock may add 30 % of the profit (instead of a fixed $100,000). Shown on the Advisor page as "Budget plan for today".
- Advisor: "Income so far today" until a full day is measured, "calculating …" right after loading, fixed costs kept across restarts, clearer texts.

## 0.7.1 – 25.11.2026 – the advisor
- **New dashboard page "Advisor":** the bot thinks like a good player. It shows income, fixed costs (rent, electricity, wages) and profit per day, the reserve, **what holds the shop back** (packs short, openers idle, empty shelf slots, shelf space, display cases full, grading slots busy, card storage full – biggest first), **tips for what only you can do** (more display cases, more shelves, more/fewer openers, fewer workers) and the **next investments** with a verdict.
- **Grading tier by profit:** for every PSA tier the bot reads price per card, cards per submission and days, and picks the fastest tier whose cost (all 8 slots) stays within ~15 % of the profit per day and that it can fill. Small shop = cheapest tier as before; big shop = faster tiers, more cards per day.
- The daily report also keeps the items sold per day (for the shelf turnover).

## 0.7.0 – 25.11.2026 – adapts to your game
- **Settings follow the game phase (step 2):** with "Adapt settings to the game phase" (on by default) the bot sets collection buying, grading, emergency restock, "packs first", the PSA 10 hunt, the daily purchase limit and the license/expansion safety factor to fit the phase:
  - Start: no collection packs, no grading, no emergency restock
  - Build-up: expansions and licenses first (from 2× the price), daily limit 30 % of the average shop income
  - Collect: packs first, emergency restock, daily limit 70 % of the average shop income
  - Endgame: packs first, PSA 10 hunt, daily limit 30 %
  The daily limit only follows once a whole day was measured (before that your own limit applies). The overview shows what auto sets right now; off = your own values.
- **Protection against game/mod updates:** at start the bot checks the game parts each feature needs; if one is missing, only that feature is switched off (log `[Compat]`, note on the dashboard). A feature that fails 10 times in a row is switched off for the session instead of flooding the log.
- **Installer:** `Install-ShopBot.bat` in the release ZIP – finds the game via Steam (all libraries), checks BepInEx 5, copies the mod, unblocks it, lists missing recommended mods. German or English depending on Windows.
- **First start:** on a fresh install the dashboard is German if Windows is German, otherwise English.
- README: "Tested with" (game and mod versions).
- Fix (found in the test): while the shop income is not measured yet, the phase is decided by the build-out (8+ openers = Collect) – before, a big shop was detected as "Build-up" with $0 income at the start of a day.

## 0.6.4 – 25.11.2026
- **Game phase detection (step 1 – shown only):** the bot measures shop level, expansions, pack openers, money, collection progress and the average shop income per day, and detects the phase: **Start**, **Build-up**, **Collect** or **Endgame**. Shown on the overview with the reason and what the bot will focus on in that phase. A phase change only counts after 5 minutes (no flickering) and is noted in the history. The behaviour does not change yet – that comes in step 2.
- The daily report now also keeps the total shop income per day (needed for the phase).

## 0.6.3 – 25.11.2026 – shelves really get filled
- **Fix – boxes in front of the entrance were never used:** delivered boxes keep an "opening" flag forever, so the bot skipped them (seen: 176 boxes, all "busy"). A box stuck in "opening" is now treated as free after 3 s, a carried/moved box that lies still after 15 s – for shelves, openers and sorting.
- **Fix – empty shelf slots with a label stayed empty:** the bot asked the game which goods are inside the slot – for an empty slot that is "none", so it stopped. Only half-full slots were topped up. Now the first item goes in the game's own way, the rest as before.
- **Empty labeled slots get new goods:** if the goods of an empty labeled slot are gone (no box anywhere, nothing on the way), the bot relabels it with goods that are here – booster boxes/displays first, then packs of the new sets. Packs for the openers may go onto the shelves too (only a few hundred fit). Can be switched off.
- **Smarter shelf restocking:** orders by the real gap (room in all labeled slots of a goods type minus what is on them), up to 3 boxes at once. Before, a few half-full slots counted as "enough" and completely empty slots of the same goods were never refilled.
- **Shelves first:** while labeled shelf slots of a pack type still miss items, the openers leave those boxes for the shelves (a few hundred items) – then all packs go to the openers again.
- **Shelf status** once a minute in the log (`[Shelves] …`) and in the dashboard: empty slots with/without label, items stocked, goods without any box.
- **Workers go where the work is:** every 2 minutes the bot measures the work left for workers (openers, shelves, display cases – minus what the bot does itself) and splits the workers by demand and strengths. The side task is the next task with work, so nobody stands around when the main task runs out (before: everyone had Opener/Opener and waited). Can be switched off.
- **Stuck box flags are reset in the game too**, so the workers can take those boxes again (before only the bot ignored the flag).
- Stocking speed in the dashboard up to 150 items per second (was 30) – the bot puts goods in directly, 120/s fills a shop in a few minutes.
- No more useless storage pulls when there is no storage shelf.
- Tested in the game: empty slots 188 → 66 within 3 minutes, 4,500–6,600 items stocked per minute, relabeling 100 % successful, no errors.

## 0.6.2 – 25.11.2026
- **Fix – shop shelves were never restocked:** reordering goods for the shelves (and goods customers miss) no longer counts against the pack daily limit – pack buying used it up within minutes. The many pack boxes for the openers no longer count toward the box limit either. Only the safety reserve applies.
- **Empty shelves without a label get filled:** the bot picks goods itself – boxes on the floor whose goods have the fewest shelf slots, otherwise goods from the storage shelf that are on no shelf yet. Booster boxes/displays first, then packs. The first item is placed the game's own way, so the game checks the fit and labels the slot. Can be switched off.
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
