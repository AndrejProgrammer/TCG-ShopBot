using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace ShopBot
{
    // Until v0.5.4 the config used German names. On the first start of v0.6 the old values are carried over
    // to the new English names, so nobody loses their settings.
    public partial class Plugin
    {
        private static readonly string[,] ConfigRenames =
        {
            { "5. Kasse", "Bot kassiert an allen freien Kassen", "5. Checkout", "Bot serves all free registers" },
            { "5. Kasse", "Bot-Kasse Artikel pro Sekunde", "5. Checkout", "Bot checkout items per second" },
            { "0. Rechnungen", "Rechnungen automatisch bezahlen", "0. Bills", "Pay bills automatically" },
            { "9. Arbeiter", "Jeden Tag Bonus an alle Arbeiter", "9. Workers", "Daily bonus for all workers" },
            { "14. Erweiterungen", "Laden-Erweiterungen kaufen", "14. Expansions", "Buy shop expansions" },
            { "14. Erweiterungen", "Lager (Shop B) und Lager-Erweiterungen kaufen", "14. Expansions", "Buy warehouse (Shop B) and warehouse expansions" },
            { "14. Erweiterungen", "Max. Laden-Erweiterungen", "14. Expansions", "Max shop expansions" },
            { "14. Erweiterungen", "Max. Lager-Erweiterungen", "14. Expansions", "Max warehouse expansions" },
            { "14. Erweiterungen", "Nur kaufen wenn Budget mindestens X mal Preis", "14. Expansions", "Only buy if budget is at least X times the price" },
            { "13. Bewertungen", "Auf Kundenbewertungen reagieren", "13. Reviews", "React to customer reviews" },
            { "13. Bewertungen", "Kartons pro vermisstem Artikel", "13. Reviews", "Boxes per missing item" },
            { "13. Bewertungen", "Minimaler Aufschlag (%)", "13. Reviews", "Minimum markup (%)" },
            { "7. Sammlung", "Sammlung automatisch vervollständigen", "7. Collection", "Complete the collection automatically" },
            { "7. Sammlung", "Sicherheitsreserve ($)", "7. Collection", "Safety reserve ($)" },
            { "7. Sammlung", "Ganzes Geld nutzen (nur Rechnungen zurückhalten)", "7. Collection", "Use all money (only hold back bills)" },
            { "7. Sammlung", "Ziel-Sets gleichzeitig", "7. Collection", "Target sets at once" },
            { "7. Sammlung", "Sets nach Effizienz wählen", "7. Collection", "Choose sets by efficiency" },
            { "7. Sammlung", "Fast fertige Sets: max. Packs pro fehlender Karte", "7. Collection", "Nearly finished sets: max packs per missing card" },
            { "7. Sammlung", "Packs gekauft pro Set (intern)", "7. Collection", "Packs bought per set (internal)" },
            { "7. Sammlung", "Vorrat in Opener-Füllungen", "7. Collection", "Stock in opener fills" },
            { "7. Sammlung", "Budget pro zusätzlichem Ziel-Set ($)", "7. Collection", "Budget per extra target set ($)" },
            { "7. Sammlung", "Nur Pokémon-Sets", "7. Collection", "Pokémon sets only" },
            { "7. Sammlung", "Einkaufslimit pro Tag ($)", "7. Collection", "Daily purchase limit ($)" },
            { "7. Sammlung", "Tageslimit mindestens Prozent vom Geld", "7. Collection", "Daily limit at least percent of money" },
            { "7. Sammlung", "Max. Packs im Vorrat gesamt", "7. Collection", "Max packs in stock overall" },
            { "7. Sammlung", "Max. Ziel-Sets gleichzeitig", "7. Collection", "Max target sets at once" },
            { "7. Sammlung", "Budget pro zusätzlicher Opener-Füllung ($)", "7. Collection", "Budget per extra opener fill ($)" },
            { "7. Sammlung", "Max. Opener-Füllungen Vorrat", "7. Collection", "Max opener fills in stock" },
            { "7. Sammlung", "Max. Kartons pro Bestellung", "7. Collection", "Max boxes per order" },
            { "10. Nachbestellen", "Nur Packs und Displays", "10. Restocking", "Packs and displays only" },
            { "7. Sammlung", "Max. volle Kartons im Laden insgesamt", "7. Collection", "Max full boxes in the shop overall" },
            { "7. Sammlung", "Opener-Arbeiter auf Ziel-Sets einstellen", "7. Collection", "Set opener workers to target sets" },
            { "7. Sammlung", "Pack-Automaten-Boost abholen", "7. Collection", "Claim pack machine boost" },
            { "8. Kartenablage", "Doppelte in Kartenablage legen", "8. Card storage", "Put duplicates into card storage" },
            { "8. Kartenablage", "Nur Karten unter ($)", "8. Card storage", "Only cards below ($)" },
            { "8. Kartenablage", "Max. Karten pro Durchgang", "8. Card storage", "Max cards per run" },
            { "8. Kartenablage", "Gegradete Karten zuerst verkaufen", "8. Card storage", "Sell graded cards first" },
            { "8. Kartenablage", "Grade 10 (PSA 10): nur Duplikate verkaufen", "8. Card storage", "Grade 10 (PSA 10): only sell duplicates" },
            { "9. Arbeiter", "Arbeiter automatisch einteilen", "9. Workers", "Assign workers automatically" },
            { "10. Nachbestellen", "Produkte nachbestellen", "10. Restocking", "Reorder products" },
            { "10. Nachbestellen", "Max. Bestellungen pro Minute", "10. Restocking", "Max orders per minute" },
            { "7. Sammlung", "Notfall-Nachkauf wenn Packs ausgehen", "7. Collection", "Emergency restock when packs run out" },
            { "7. Sammlung", "Notfall ab weniger als X Opener-Füllungen", "7. Collection", "Emergency below X opener fills" },
            { "7. Sammlung", "Notfall zusätzliches Limit pro Tag ($)", "7. Collection", "Emergency extra limit per day ($)" },
            { "7. Sammlung", "Packs zuerst (Lizenzen/Erweiterungen warten bei knappem Vorrat)", "7. Collection", "Packs first (licenses/expansions wait when stock is low)" },
            { "9. Arbeiter", "Anzahl Kassierer (0 bedeutet so viele wie Kassen)", "9. Workers", "Number of cashiers (0 means as many as registers)" },
            { "9. Arbeiter", "Vitrinen-Befüller (Bot füllt selbst)", "9. Workers", "Display fillers (bot fills them itself)" },
            { "9. Arbeiter", "Regal-Einräumer (Bot räumt selbst ein)", "9. Workers", "Shelf stockers (bot stocks itself)" },
            { "5. Kasse", "Kassen-Turbo (Scan-Tempo x)", "5. Checkout", "Checkout turbo (scan speed x)" },
            { "1. Allgemein", "Hotkey Dashboard", "1. General", "Dashboard hotkey" },
            { "1. Allgemein", "Dashboard-Größe", "1. General", "Dashboard size" },
            { "12. Graden", "Karten automatisch graden", "12. Grading", "Grade cards automatically" },
            { "12. Graden", "Nur Karten ab Wert ($)", "12. Grading", "Only cards from value ($)" },
            { "12. Graden", "1 Stück jeder Karte im Album lassen", "12. Grading", "Keep 1 copy of every card in the album" },
            { "12. Graden", "Nur volle Einsendungen schicken", "12. Grading", "Only send full submissions" },
            { "12. Graden", "PSA-10-Sammlung (alle Karten als PSA 10)", "12. Grading", "PSA 10 collection (all cards as PSA 10)" },
            { "1. Allgemein", "Bot aktiv", "1. General", "Bot active" },
            { "1. Allgemein", "Hotkey An/Aus", "1. General", "Hotkey on/off" },
            { "1. Allgemein", "Status anzeigen", "1. General", "Show status" },
            { "1. Allgemein", "Startpause nach dem Laden (Sekunden)", "1. General", "Start delay after loading (seconds)" },
            { "1. Allgemein", "Fenster nur wenn ich X Sek. nichts drücke", "1. General", "Windows only if I press nothing for X sec" },
            { "2. Pack-Opener", "Fertige Opener einsammeln", "2. Pack opener", "Collect finished openers" },
            { "2. Pack-Opener", "Ergebnis anzeigen (Sekunden)", "2. Pack opener", "Show result (seconds)" },
            { "2. Pack-Opener", "Halbvolle Opener starten", "2. Pack opener", "Start half-full openers" },
            { "2. Pack-Opener", "Grading-Pakete automatisch öffnen", "2. Pack opener", "Open grading packages automatically" },
            { "2. Pack-Opener", "Wartezeit halbvoll (Sekunden)", "2. Pack opener", "Wait time half-full (seconds)" },
            { "3. Kunden-Angebote", "Angebote bearbeiten", "3. Customer offers", "Handle offers" },
            { "3. Kunden-Angebote", "Ankauf Startgebot (% Marktpreis)", "3. Customer offers", "Buy start offer (% market price)" },
            { "3. Kunden-Angebote", "Ankauf Maximum (% Marktpreis)", "3. Customer offers", "Buy maximum (% market price)" },
            { "3. Kunden-Angebote", "Ankauf Schritt (% Marktpreis)", "3. Customer offers", "Buy step (% market price)" },
            { "3. Kunden-Angebote", "Tausch bearbeiten", "3. Customer offers", "Handle swaps" },
            { "3. Kunden-Angebote", "Tausch Mindestgewinn (%)", "3. Customer offers", "Swap minimum gain (%)" },
            { "3. Kunden-Angebote", "Pause zwischen Schritten (Sekunden)", "3. Customer offers", "Pause between steps (seconds)" },
            { "3. Kunden-Angebote", "Ankauf Limit normale Karten (% Marktpreis)", "3. Customer offers", "Buy limit normal cards (% market price)" },
            { "3. Kunden-Angebote", "Neue Karte: bis X Prozent Marktpreis (Set mit Packs)", "3. Customer offers", "New card: up to X percent market price (set with packs)" },
            { "3. Kunden-Angebote", "Neue Karte: bis X Prozent Marktpreis (Set pausiert oder ohne Packs)", "3. Customer offers", "New card: up to X percent market price (set paused or without packs)" },
            { "3. Kunden-Angebote", "Neue Karte: höchstens X Prozent vom Geld", "3. Customer offers", "New card: at most X percent of money" },
            { "3. Kunden-Angebote", "Neue Karte: immer den Wunschpreis zahlen", "3. Customer offers", "New card: always pay the asking price" },
            { "3. Kunden-Angebote", "Doppelte: Startgebot (Prozent Marktpreis)", "3. Customer offers", "Duplicates: start offer (percent market price)" },
            { "3. Kunden-Angebote", "Doppelte: Startgebot billige Karten (Prozent Marktpreis)", "3. Customer offers", "Duplicates: start offer cheap cards (percent market price)" },
            { "3. Kunden-Angebote", "Doppelte: billig heißt unter ($)", "3. Customer offers", "Duplicates: cheap means below ($)" },
            { "3. Kunden-Angebote", "Karten unter diesem Wert immer kaufen ($)", "3. Customer offers", "Always buy cards below this value ($)" },
            { "3. Kunden-Angebote", "Tausch: kleiner Verlust erlaubt ($)", "3. Customer offers", "Swap: small loss allowed ($)" },
            { "4. Tagesende", "Tag automatisch beenden", "4. End of day", "End day automatically" },
            { "4. Tagesende", "Wartezeit vor Tagesende (Sekunden)", "4. End of day", "Wait before end of day (seconds)" },
            { "5. Kasse", "Arbeiter an Kasse holen", "5. Checkout", "Call a worker to the register" },
            { "5. Kasse", "Ab so vielen wartenden Kunden", "5. Checkout", "From this many waiting customers" },
            { "5. Kasse", "Arbeiter zurückgeben nach (Sekunden)", "5. Checkout", "Return worker after (seconds)" },
            { "6. Preise", "Preise automatisch setzen", "6. Prices", "Set prices automatically" },
            { "6. Preise", "Aufschlag Produkte (%)", "6. Prices", "Markup products (%)" },
            { "6. Preise", "Aufschlag Einzelkarten + gegradete (%)", "6. Prices", "Markup single cards + graded (%)" },
            { "6. Preise", "Prüfintervall (Sekunden)", "6. Prices", "Check interval (seconds)" },
            { "6. Preise", "Preise runden", "6. Prices", "Round prices" },
            { "5. Kasse", "Kassierer bleiben bis zum letzten Kunden", "5. Checkout", "Cashiers stay until the last customer" },
            { "17. Einsortieren", "Herumliegende Kartons ins Lagerregal", "17. Sorting", "Put loose boxes on the storage shelf" },
            { "17. Einsortieren", "Packs für Opener am Boden lassen", "17. Sorting", "Leave packs for openers on the floor" },
            { "17. Einsortieren", "Leere Kartons wegwerfen", "17. Sorting", "Throw away empty boxes" },
            { "17. Einsortieren", "Kartons pro Sekunde", "17. Sorting", "Boxes per second" },
            { "17. Einsortieren", "Kartons aus dem Lagerregal holen", "17. Sorting", "Take boxes from the storage shelf" },
            { "17. Einsortieren", "Kartons aus dem Lager pro Runde (Opener)", "17. Sorting", "Boxes from storage per round (openers)" },
            { "15. Lizenzen", "Neue Produkte freischalten", "15. Licenses", "Unlock new products" },
            { "15. Lizenzen", "Nur Pokémon-Produkte", "15. Licenses", "Pokémon products only" },
            { "15. Lizenzen", "Nur kaufen wenn Budget mindestens X mal Preis", "15. Licenses", "Only buy if budget is at least X times the price" },
            { "15. Lizenzen", "Schon gekauft (intern)", "15. Licenses", "Already bought (internal)" },
            { "2. Pack-Opener", "Bot befüllt Opener selbst", "2. Pack opener", "Bot fills openers itself" },
            { "2. Pack-Opener", "Packs pro Sekunde in Opener", "2. Pack opener", "Packs per second into openers" },
            { "2. Pack-Opener", "Bei knappem Vorrat gleichmäßig verteilen", "2. Pack opener", "Spread evenly when stock is low" },
            { "2. Pack-Opener", "Mindestportion pro Opener (Packs)", "2. Pack opener", "Minimum portion per opener (packs)" },
            { "2. Pack-Opener", "Opener-Turbo (Tempo x)", "2. Pack opener", "Opener turbo (speed x)" },
            { "9. Arbeiter", "Neue Arbeiter automatisch einstellen", "9. Workers", "Hire new workers automatically" },
            { "9. Arbeiter", "Max. Arbeiter (0 bedeutet alle)", "9. Workers", "Max workers (0 means all)" },
            { "9. Arbeiter", "Einstellen nur wenn Budget mindestens X mal Einstellgebühr", "9. Workers", "Only hire if budget is at least X times the hiring fee" },
            { "16. Selbst einräumen", "Bot räumt Regale selbst ein", "16. Self-stocking", "Bot stocks shelves itself" },
            { "16. Selbst einräumen", "Artikel pro Sekunde", "16. Self-stocking", "Items per second" },
            { "16. Selbst einräumen", "Bot füllt leere Vitrinen-Plätze", "16. Self-stocking", "Bot fills empty display slots" },
            { "16. Selbst einräumen", "Karten pro Sekunde", "16. Self-stocking", "Cards per second" },
            { "11. Upgrades", "Upgrades automatisch kaufen", "11. Upgrades", "Buy upgrades automatically" },
            { "11. Upgrades", "Sammelpunkte-Reserve", "11. Upgrades", "Collection points reserve" },
        };

        private void MigrateConfig()
        {
            try
            {
                var orphans = Traverse.Create(Config).Property("OrphanedEntries").GetValue<Dictionary<ConfigDefinition, string>>();
                if (orphans == null || orphans.Count == 0) return;
                int moved = 0;
                for (int i = 0; i < ConfigRenames.GetLength(0); i++)
                {
                    var oldDef = new ConfigDefinition(ConfigRenames[i, 0], ConfigRenames[i, 1]);
                    if (!orphans.TryGetValue(oldDef, out var value)) continue;
                    var newDef = new ConfigDefinition(ConfigRenames[i, 2], ConfigRenames[i, 3]);
                    if (Config.ContainsKey(newDef)) { Config[newDef].SetSerializedValue(value); moved++; }
                    orphans.Remove(oldDef);
                }
                // leftovers from very old versions in the old German sections: unused, remove them
                var oldSections = new HashSet<string>();
                for (int i = 0; i < ConfigRenames.GetLength(0); i++) oldSections.Add(ConfigRenames[i, 0]);
                int removed = 0;
                foreach (var def in new List<ConfigDefinition>(orphans.Keys))
                    if (oldSections.Contains(def.Section)) { orphans.Remove(def); removed++; }
                if (removed > 0 && moved == 0) { Config.Save(); Logger.LogInfo($"Config: {removed} unused old entries removed"); }

                if (moved > 0)
                {
                    // whoever used the German versions before gets the German dashboard
                    if (_language != null && !orphans.ContainsKey(new ConfigDefinition("1. General", "Language"))) _language.Value = "de";
                    Config.Save();
                    Logger.LogInfo($"Config: {moved} settings carried over from the old German names");
                }
            }
            catch (System.Exception e) { Logger.LogWarning("Config migration: " + e.Message); }
        }
    }
}
