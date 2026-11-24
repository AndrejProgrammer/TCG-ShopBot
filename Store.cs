using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // 1) Lizenzen für neue Pokémon-Produkte freischalten (wie "Lizenz kaufen" im Bestell-Menü)
    // 2) Bot räumt selbst ein: Ware aus Kartons in Regale, Karten aus der Kartenablage in leere Vitrinen-Plätze
    public partial class Plugin
    {
        private ConfigEntry<bool> _licEnabled, _licPokemonOnly, _stockSelf, _cardsSelf, _stockEmptySlots, _relabelEmpty;
        private ConfigEntry<float> _licMoneyFactor;
        private ConfigEntry<string> _licBought;   // dauerhaft: schon gekaufte Lizenzen (Artikel-NAMEN – Typ-Nummern der Mods ändern sich pro Start)
        private HashSet<string> _licBoughtSet;
        private ConfigEntry<int> _stockPerSecond, _cardsPerSecond;
        private float _licTimer = 30f;
        private List<string> _setsWithoutPacks = new List<string>();

        // Opener selbst befüllen / Arbeiter einstellen / Opener-Turbo
        private ConfigEntry<bool> _openerSelf, _hireEnabled, _openerSpread;
        private ConfigEntry<int> _openerPerSecond, _hireMax, _openerMinBatch;
        private ConfigEntry<float> _hireMoneyFactor, _openerTurbo;
        private float _openerAcc, _hireTimer = 40f;
        private int _statSelfOpener, _statHired;
        private readonly Dictionary<InteractableAutoPackOpener, float> _openerBaseTime = new Dictionary<InteractableAutoPackOpener, float>();
        private readonly Dictionary<InteractableAutoPackOpener, float> _openerSetTime = new Dictionary<InteractableAutoPackOpener, float>();

        private void BindStoreConfig()
        {
            _licEnabled = Config.Bind("15. Licenses", "Unlock new products", true, "Buys licenses for new packs/displays as soon as level and money allow (cheapest first).");
            _licPokemonOnly = Config.Bind("15. Licenses", "Pokémon products only", true, "Only licenses from the Pokémon shops.");
            _licMoneyFactor = Config.Bind("15. Licenses", "Only buy if budget is at least X times the price", 3f, "Safety so enough money stays for packs.");
            _licBought = Config.Bind("15. Licenses", "Already bought (internal)", "", "Licenses bought by the bot – never bought again, even if the game resets them.");
            _licBoughtSet = new HashSet<string>();
            foreach (var part in (_licBought.Value ?? "").Split('|'))
            {
                var p = part.Trim();
                if (p.Length > 0 && !int.TryParse(p, out _)) _licBoughtSet.Add(p);   // alte Zahlen-Einträge (v0.1) sind wertlos
            }
            _openerSelf = Config.Bind("2. Pack opener", "Bot fills openers itself", true, "Puts packs of unfinished Pokémon sets from boxes straight into free pack openers (target sets first). Openers are never empty.");
            _openerPerSecond = Config.Bind("2. Pack opener", "Packs per second into openers", 40, "How fast the bot fills the openers.");
            _openerSpread = Config.Bind("2. Pack opener", "Spread evenly when stock is low", true,
                "If the packs on the floor are not enough for all free openers, each gets an equal portion and starts right away – all openers run instead of a few full ones.");
            _openerMinBatch = Config.Bind("2. Pack opener", "Minimum portion per opener (packs)", 64,
                "Smallest portion when spreading evenly.");
            _openerTurbo = Config.Bind("2. Pack opener", "Opener turbo (speed x)", 1f, "Openers open packs this much faster (1 = normal, game value).");
            _hireEnabled = Config.Bind("9. Workers", "Hire new workers automatically", true, "Hires free workers as soon as level and money allow (the work plan assigns them afterwards).");
            _hireMax = Config.Bind("9. Workers", "Max workers (0 means all)", 0, "At most this many workers in total.");
            _hireMoneyFactor = Config.Bind("9. Workers", "Only hire if budget is at least X times the hiring fee", 10f, "Safety so enough money stays for packs.");
            _stockSelf = Config.Bind("16. Self-stocking", "Bot stocks shelves itself", true, "Puts goods from boxes straight into shelf slots meant for those goods (in addition to the workers).");
            _stockEmptySlots = Config.Bind("16. Self-stocking", "Fill empty shelf slots without a label", true,
                "Empty shelf slots that are not assigned to any goods yet get goods the bot chooses: booster boxes/displays first, then packs (also of the new sets).");
            _relabelEmpty = Config.Bind("16. Self-stocking", "Relabel empty slots whose goods are gone", true,
                "An empty labeled slot whose goods are nowhere left (no box, nothing on the way) gets goods that are here – booster boxes/displays first, then packs of the new sets.");
            _stockPerSecond = Config.Bind("16. Self-stocking", "Items per second", 6, "How fast the bot stocks.");
            _cardsSelf = Config.Bind("16. Self-stocking", "Bot fills empty display slots", true, "Takes cards from the card storage and puts them into empty card slots.");
            _cardsPerSecond = Config.Bind("16. Self-stocking", "Cards per second", 2, "How fast the bot fills display cases.");
        }

        // ---------------- Lizenzen ----------------
        private void HandleLicenses()
        {
            if (!_licEnabled.Value || (_licTimer += 1f) < 60f) return;
            _licTimer = 0f;
            SafeRun("Licenses", LicenseTick);
        }

        // WICHTIG: Lizenzen nur über CPlayerData.GetIsItemLicenseUnlocked lesen und InventoryBase.GetRestockData holen.
        // Der Enhanced Prefab Loader leitet diese Zugriffe im Spielcode für Mod-Artikel auf seinen eigenen Speicher um.
        // Die Liste CPlayerData.m_IsItemLicenseUnlocked DIREKT zu lesen zeigt Mod-Artikel immer als "gesperrt"
        // (Ursache der angeblich "immer wieder gesperrten" Booster-Box-Lizenzen bis v0.4.2).
        private static List<(int idx, RestockData rd)> AllRestockEntries()
        {
            var list = new List<(int, RestockData)>();
            int misses = 0;
            for (int i = 0; i < 5000 && misses < 25; i++)
            {
                RestockData rd = null;
                try { rd = InventoryBase.GetRestockData(i); } catch { }
                if (rd == null) { misses++; continue; }
                misses = 0;
                list.Add((i, rd));
            }
            return list;
        }

        private static bool LicenseUnlocked(int idx)
        {
            try { return CPlayerData.GetIsItemLicenseUnlocked(idx); } catch { return true; }
        }

        private readonly HashSet<string> _licBoughtSession = new HashSet<string>();

        private void LicenseTick()
        {
            int level = CPlayerData.m_ShopLevel + 1;
            var entries = AllRestockEntries();
            if (entries.Count == 0) return;
            var unlockedTypes = new HashSet<EItemType>(entries.Where(e => LicenseUnlocked(e.idx)).Select(e => e.rd.itemType));

            var cands = new List<(int idx, RestockData rd)>();
            foreach (var (i, rd) in entries)
            {
                if (rd.itemType == EItemType.None || rd.licensePrice <= 0f) continue;
                if (LicenseUnlocked(i)) continue;
                if (level < rd.licenseShopLevelRequired) continue;
                if (unlockedTypes.Contains(rd.itemType)) continue;                 // derselbe Artikel ist unter anderem Eintrag schon frei
                string nm = LicName(rd.itemType);
                if (_licBoughtSession.Contains(nm))                                // Sicherheit: nie zweimal in einer Sitzung bezahlen
                {
                    if (_licRebuyWarned.Add(nm)) Logger.LogWarning($"[Licenses] {nm} is locked again after buying – not buying again");
                    continue;
                }
                var data = InventoryBase.GetItemData(rd.itemType);
                if (data == null || data.category != EItemCategory.TCG) continue;   // nur Packs/Displays
                if (_licPokemonOnly.Value)
                {
                    string app = EplShopAppId(rd.itemType) ?? "";
                    if (app.IndexOf("pok", StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                cands.Add((i, rd));
            }
            if (cands.Count == 0) return;

            // Reihenfolge: 1) Packs von Sets, für die die Sammlung noch keine kaufbaren Packs hat
            //              2) übrige Packs  3) Rest – jeweils beste Marge, dann günstigste Lizenz
            int Prio((int idx, RestockData rd) c)
            {
                string nm = LicName(c.rd.itemType);
                bool pack = nm.IndexOf("pack", StringComparison.OrdinalIgnoreCase) >= 0;
                bool needed = _setsWithoutPacks.Any(s => nm.StartsWith(s, StringComparison.OrdinalIgnoreCase));
                return needed && pack ? 0 : needed ? 1 : pack ? 2 : 3;
            }
            var order = cands.OrderBy(Prio).ThenByDescending(c => LicenseMargin(c.rd.itemType)).ThenBy(c => c.rd.licensePrice).ToList();

            // Alle kaufen, die das Budget hergibt (Schutzfaktor gilt für jede einzelne)
            int bought = 0, waiting = 0;
            foreach (var c in order)
            {
                // Packs zuerst: bei knappem Vorrat nur Lizenzen für Sets, die noch gar keine kaufbaren Packs haben
                if (PacksFirstBlocks && Prio(c) != 0) { waiting++; continue; }
                if (Budget < c.rd.licensePrice * Mathf.Max(1f, _licMoneyFactor.Value)) continue;
                BuyLicense(c.idx, c.rd);
                bought++;
                if (bought >= 25) break;                                    // pro Minute höchstens 25
            }
            if (waiting > 0 && Time.unscaledTime >= _starveLogLic)
            {
                _starveLogLic = Time.unscaledTime + 600f;
                Logger.LogInfo($"[Licenses] {waiting} waiting: pack stock low ({_packHave}/{_packNeed}) – money goes to packs first");
            }
        }
        private readonly HashSet<string> _licRebuyWarned = new HashSet<string>();

        private void BuyLicense(int index, RestockData r)
        {
            float price = r.licensePrice;
            PriceChangeManager.AddTransaction(0f - price, ETransactionType.PayRestockLicense, index);
            Pay(price);
            CPlayerData.SetUnlockItemLicense(index);
            _licBoughtSet.Add(LicName(r.itemType));
            _licBoughtSession.Add(LicName(r.itemType));
            _licBought.Value = string.Join("|", _licBoughtSet);
            try { Config.Save(); } catch { }
            CPlayerData.m_GameReportDataCollect.upgradeCost -= price;
            CPlayerData.m_GameReportDataCollectPermanent.upgradeCost -= price;
            try { AchievementManager.OnItemLicenseUnlocked(r.itemType); } catch { }
            GameInstance.m_IsItemLicenseUnlocked = true;
            _statSpent += price;
            _packTypeCache.Clear(); // neue Packs können zu Sets gehören
            NoteF($"New license: {InventoryBase.GetItemData(r.itemType)?.GetName() ?? r.itemType.ToString()} unlocked ({GameInstance.GetPriceString(price)})");
        }

        private float LicenseMargin(EItemType t)
        {
            try
            {
                float cost = CPlayerData.GetItemCost(t);
                float market = CPlayerData.GetItemMarketPrice(t);
                if (cost <= 0f || market <= 0f) return 0f;
                return market * (1f + _priceItemPercent.Value / 100f) / cost;
            }
            catch { return 0f; }
        }

        private static string LicName(EItemType t)
        {
            string n = null;
            try { n = InventoryBase.GetItemData(t)?.GetName(); } catch { }
            return string.IsNullOrEmpty(n) ? t.ToString() : n.Replace("|", "/").Trim();
        }

        // ---------------- Arbeiter einstellen (wie "Einstellen" im Telefon) ----------------
        private void HandleHiring()
        {
            if (!_hireEnabled.Value || (_hireTimer += 1f) < 60f) return;
            _hireTimer = 0f;
            SafeRun("Hiring", HireTick);
        }

        private void HireTick()
        {
            var wm = S<WorkerManager>();
            var hired = CPlayerData.m_IsWorkerHired;
            if (wm == null || wm.m_WorkerDataList == null || hired == null) return;
            if (LightManager.GetHasDayEnded()) return;                       // abends nicht – kommen morgens sowieso
            var gm = S<CGameManager>();
            if (gm != null && gm.m_IsPrologue) return;

            int count = hired.Count(h => h);
            if (_hireMax.Value > 0 && count >= _hireMax.Value) return;
            int level = CPlayerData.m_ShopLevel + 1;
            int best = -1; float bestCost = float.MaxValue;
            for (int i = 0; i < wm.m_WorkerDataList.Count && i < hired.Count; i++)
            {
                if (hired[i]) continue;
                var wd = wm.m_WorkerDataList[i];
                if (wd == null || level < wd.shopLevelRequired) continue;
                if (wd.hiringCost < bestCost) { best = i; bestCost = wd.hiringCost; }
            }
            if (best < 0) return;
            if (Budget < bestCost * Mathf.Max(1f, _hireMoneyFactor.Value)) return;

            // exakt wie HireWorkerPanelUI.OnPressHireButton
            PriceChangeManager.AddTransaction(0f - bestCost, ETransactionType.HireWorker, best);
            Pay(bestCost);
            CPlayerData.SetIsWorkerHired(best, isHired: true);
            wm.ActivateWorker(best, resetTask: true);
            CPlayerData.m_GameReportDataCollect.employeeCost -= bestCost;
            CPlayerData.m_GameReportDataCollectPermanent.employeeCost -= bestCost;
            try { AchievementManager.OnStaffHired(count + 1); } catch { }
            _statSpent += bestCost;
            _statHired++;
            _lastPlanKey = null; _workerTimer = 55f;                         // gleich in den Einsatzplan aufnehmen
            NoteF($"New worker hired: {wm.m_WorkerDataList[best].GetName()} ({GameInstance.GetPriceString(bestCost)}, wage {GameInstance.GetPriceString(wm.m_WorkerDataList[best].costPerDay)}/day) – now {count + 1} workers");
        }

        // ---------------- Selbst einräumen ----------------
        private float _stockAcc, _cardAcc;

        private void HandleSelfStocking()
        {
            if (_stockSelf.Value) { _stockAcc += _stockPerSecond.Value; SafeRun("Stocking", StockShelves); }
            if (_cardsSelf.Value) { _cardAcc += _cardsPerSecond.Value; SafeRun("Displays", StockCards); }
            if (_openerSelf.Value) { _openerAcc = Mathf.Min(_openerAcc + _openerPerSecond.Value, _openerPerSecond.Value * 2f); SafeRun("Fill openers", StockOpeners); }
            SafeRun("Opener turbo", ApplyOpenerTurbo);
        }

        // Opener-Turbo: Öffnungszeit pro Pack = Spielwert / Turbo (Spielwert pro Automat gemerkt, 1 = zurück auf normal)
        private void ApplyOpenerTurbo()
        {
            var list = ShelfManager.GetAutoPackOpenerList();
            if (list == null) return;
            float turbo = Mathf.Clamp(_openerTurbo.Value, 1f, 10f);
            foreach (var o in list)
            {
                if (o == null) continue;
                // Spielwert merken. Hat jemand anderes (z. B. Collection-Tracker-Boost) die Zeit geändert -> das ist der neue Spielwert.
                if (!_openerBaseTime.TryGetValue(o, out var baseTime) || !_openerSetTime.TryGetValue(o, out var setTime)
                    || Mathf.Abs(o.m_PackOpenTime - setTime) > 0.001f)
                {
                    baseTime = o.m_PackOpenTime;
                    _openerBaseTime[o] = baseTime;
                }
                float want = baseTime / turbo;
                if (Mathf.Abs(o.m_PackOpenTime - want) > 0.001f) o.m_PackOpenTime = want;
                _openerSetTime[o] = want;
            }
        }

        // Bot befüllt freie Opener direkt aus Kartons (wie ein Arbeiter: InteractableAutoPackOpener.DispenseItemFromBox)
        private void StockOpeners()
        {
            int budget = Mathf.FloorToInt(_openerAcc);
            if (budget <= 0 || _openerPackTypes.Count == 0) return;

            var openers = ShelfManager.GetAutoPackOpenerList();
            var rm = S<RestockManager>();
            if (openers == null || rm == null) return;
            var free = new List<InteractableAutoPackOpener>();
            foreach (var o in openers)
            {
                if (o == null || !o.gameObject.activeInHierarchy || !o.IsValidObject()) continue;
                var boxed = Traverse.Create(o).Field("m_IsBoxedUp");
                if (boxed.FieldExists() && boxed.GetValue<bool>()) continue;
                if (o.GetIsProcessing() || !o.HasEnoughSlot()) continue;
                free.Add(o);
            }
            if (free.Count == 0) return;

            // Kartons mit Packs unfertiger Sets (auf dem Boden, nicht getragen) – Ziel-Sets zuerst, angebrochene zuerst
            var boxes = new List<InteractablePackagingBox_Item>();
            int onFloor = 0;                                               // auch noch ungeöffnete/getragene mitzählen
            foreach (var b in rm.m_ItemPackagingBoxList)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null || b.m_IsStored) continue;
                if (b.m_ItemCompartment.GetItemCount() <= 0 || !b.CanWorkerTakeBox()) continue;
                if (!_openerPackTypes.Contains(b.m_ItemCompartment.GetItemType())) continue;
                // shelves first: while labeled shelf slots of these packs still miss items, the box stays for the shelves
                if (_stockSelf.Value && _shelfNeed.TryGetValue(b.m_ItemCompartment.GetItemType(), out var need) && need > 0) continue;
                onFloor++;
                var tb = Traverse.Create(b);
                if (BoxBusy(b)) continue;
                if (RealItemCount(b) <= 0)
                {
                    if (!tb.Field("m_IsBoxOpened").GetValue<bool>()) { try { b.SetOpenCloseBox(true, false); } catch { } }
                    continue;                                              // Artikel entstehen erst beim Öffnen -> nächste Runde
                }
                boxes.Add(b);
            }
            // Keine Packs mehr am Boden, aber Opener frei -> Kartons aus dem Lagerregal neben den Opener holen (Ziel-Sets zuerst)
            if (onFloor < Mathf.Max(1, free.Count / 2) && _pullFromStorage.Value && Time.unscaledTime >= _pullOpenerAt)
            {
                _pullOpenerAt = Time.unscaledTime + 2f;
                var at = free[0].m_StandLoc != null ? free[0].m_StandLoc.position : free[0].transform.position;
                PullFromStorage(t => !_openerPackTypes.Contains(t) ? -1 : _focusPackTypes.Contains(t) ? 0 : 1,
                                Mathf.Clamp(free.Count, 1, _pullPerTick.Value), at);
            }
            if (boxes.Count == 0) return;
            boxes = boxes.OrderBy(b => _focusPackTypes.Contains(b.m_ItemCompartment.GetItemType()) ? 0 : 1)
                         .ThenBy(b => RealItemCount(b)).ToList();

            // Knapper Vorrat: Packs am Boden reichen nicht, um alle freien Opener zu füllen ->
            // jeder Opener bekommt eine gleich große Portion und startet gleich (alle laufen, keiner wartet leer)
            int share = int.MaxValue;
            if (_openerSpread.Value)
            {
                int avail = boxes.Sum(b => RealItemCount(b));
                int freeRoom = free.Sum(o => Mathf.Max(0, o.m_MaxPackCount - o.GetStoredItemList().Count));
                if (avail < freeRoom)
                    share = Mathf.Max(Mathf.Max(1, _openerMinBatch.Value), Mathf.CeilToInt(avail / (float)free.Count));
            }

            int moved = 0, bi = 0;
            var toStart = new List<InteractableAutoPackOpener>();
            foreach (var o in free)
            {
                while (budget > 0 && bi < boxes.Count && o.GetStoredItemList().Count < share && !o.GetIsProcessing() && o.HasEnoughSlot())
                {
                    var box = boxes[bi];
                    if (box == null || RealItemCount(box) <= 0) { bi++; continue; }
                    int before = o.GetStoredItemList().Count;
                    o.DispenseItemFromBox(box, isPlayer: false);
                    if (o.GetStoredItemList().Count <= before) { bi++; continue; }   // Karton passt nicht (z. B. kein Pack) -> nächster
                    budget--; moved++;
                }
                // Portion voll (oder keine Packs mehr, aber mind. Mindestportion drin) -> sofort starten statt 45 s auf "halbvoll" zu warten
                int inside = o.GetStoredItemList().Count;
                if (share != int.MaxValue && !o.GetIsProcessing() && inside > 0 &&
                    (inside >= share || !o.HasEnoughSlot() || (bi >= boxes.Count && inside >= _openerMinBatch.Value)))
                    toStart.Add(o);
                if (budget <= 0 || bi >= boxes.Count) break;
            }
            foreach (var o in toStart)
            {
                try { if (!o.GetIsProcessing()) { o.OnMouseButtonUp(); _openerIdle.Remove(o); _statStarted++; } } catch { }
            }
            _openerAcc -= moved;
            if (moved > 0)
            {
                _statSelfOpener += moved;
                if (_statSelfOpener % 500 < moved) NoteF($"Openers filled by the bot: {_statSelfOpener} packs in total");
            }
        }

        private void StockShelves()
        {
            int budget = Mathf.FloorToInt(_stockAcc);
            _stockAcc -= budget;
            if (budget <= 0) return;

            var rm = S<RestockManager>();
            var shelves = ShelfManager.GetShelfList();
            if (rm == null || shelves == null) return;

            // how many items the labeled slots of each goods type still miss – the openers leave these boxes for the shelves
            _shelfNeed.Clear();
            foreach (var shelf in shelves)
            {
                if (shelf == null || shelf.m_ItemNotForSale || !shelf.IsValidObject()) continue;
                foreach (var comp in shelf.GetItemCompartmentList())
                {
                    if (comp == null || comp.m_ItemNotForSale || !comp.m_CanPutItem) continue;
                    var t = comp.GetItemType();
                    if (t == EItemType.None) continue;
                    int miss = comp.GetMaxItemCount() - comp.GetItemCount();
                    if (miss > 0) _shelfNeed[t] = (_shelfNeed.TryGetValue(t, out var n) ? n : 0) + miss;
                }
            }

            // verfügbare Kartons nach Ware (nicht getragen, nicht unterwegs)
            var boxes = new Dictionary<EItemType, List<InteractablePackagingBox_Item>>();
            foreach (var b in rm.m_ItemPackagingBoxList)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null) continue;
                if (b.m_ItemCompartment.GetItemCount() <= 0) continue;
                var tb = Traverse.Create(b);
                if (BoxBusy(b)) continue;
                if (b.m_IsStored) continue;                                   // Kartons im Lagerregal holen die Arbeiter
                // Artikel existieren erst nach dem ersten Öffnen – wie ein Arbeiter den Karton öffnen und nächste Runde nutzen
                if (RealItemCount(b) <= 0)
                {
                    if (!tb.Field("m_IsBoxOpened").GetValue<bool>()) { try { b.SetOpenCloseBox(true, false); } catch { } }
                    continue;
                }
                var type = b.m_ItemCompartment.GetItemType();
                if (!boxes.TryGetValue(type, out var l)) boxes[type] = l = new List<InteractablePackagingBox_Item>();
                l.Add(b);
            }

            // Regalfach braucht Ware, aber kein Karton davon am Boden -> einen Karton aus dem Lagerregal holen (max. 2 Sorten pro Runde)
            if (_pullFromStorage.Value && Time.unscaledTime >= _pullShelfAt)
            {
                _pullShelfAt = Time.unscaledTime + 3f;
                var floorTypes = new HashSet<EItemType>();
                foreach (var b in rm.m_ItemPackagingBoxList)
                    if (b != null && b.gameObject.activeInHierarchy && !b.m_IsStored && b.m_ItemCompartment != null && b.m_ItemCompartment.GetItemCount() > 0)
                        floorTypes.Add(b.m_ItemCompartment.GetItemType());
                int sorts = 0;
                foreach (var shelf in shelves)
                {
                    if (sorts >= 2) break;
                    if (shelf == null || shelf.m_ItemNotForSale || !shelf.IsValidObject()) continue;
                    foreach (var comp in shelf.GetItemCompartmentList())
                    {
                        if (sorts >= 2) break;
                        if (comp == null || !comp.m_CanPutItem || comp.m_ItemNotForSale) continue;
                        var t = comp.GetItemType();
                        if (t == EItemType.None || floorTypes.Contains(t) || !comp.HasEnoughSlot()) continue;
                        if (comp.GetItemCount() > comp.GetMaxItemCount() / 2) continue;      // erst wenn das Fach halb leer ist
                        if ((S<ShelfManager>()?.m_WarehouseShelfList?.Count ?? 0) == 0) break;   // no storage shelf
                        _shelfPullTries++;
                        if (PullFromStorage(x => x == t ? 0 : -1, 1, shelf.transform.position) > 0) { floorTypes.Add(t); sorts++; _shelfPulled++; }
                        else floorTypes.Add(t);                                                // nichts im Lager -> nicht nochmal suchen
                    }
                }
            }
            int moved = 0;
            foreach (var shelf in shelves)
            {
                if (budget <= 0) break;
                if (shelf == null || shelf.m_ItemNotForSale || !shelf.IsValidObject()) continue;
                foreach (var comp in shelf.GetItemCompartmentList())
                {
                    if (budget <= 0) break;
                    if (comp == null || !comp.m_CanPutItem || comp.m_ItemNotForSale) continue;
                    var type = comp.GetItemType();
                    if (type == EItemType.None) continue;                  // nur Fächer, die schon für eine Ware vorgesehen sind
                    if (!boxes.TryGetValue(type, out var list) || list.Count == 0) continue;
                    while (budget > 0 && comp.HasEnoughSlot() && list.Count > 0)
                    {
                        var box = list[0];
                        if (box.m_ItemCompartment.GetItemCount() <= 0 || RealItemCount(box) <= 0) { list.RemoveAt(0); continue; }
                        // Empty slot (only the label is set): CheckItemType looks at the items inside and finds none -> it used to stop here,
                        // so empty labeled slots were never filled. First item the game's own way (it checks the fit), the rest below.
                        if (comp.GetItemCount() == 0)
                        {
                            _dispTry++;
                            try { box.DispenseItem(false, comp); } catch (Exception e) { DispWhy(e.Message); }
                            if (comp.GetItemCount() == 0) break;            // does not fit / did not work
                            _dispOk++;
                            budget--; moved++;
                            continue;
                        }
                        if (comp.CheckItemType(type) != type) break;
                        // wie InteractablePackagingBox_Item.DispenseItem: Artikel aus dem Karton ins Fach legen
                        var item = box.m_ItemCompartment.GetFirstItem();
                        if (item == null) { list.RemoveAt(0); continue; }
                        item.LerpToTransform(comp.GetEmptySlotTransform(), comp.GetEmptySlotParent());
                        comp.AddItem(item, false);
                        box.m_ItemCompartment.RemoveItem(item);
                        budget--; moved++;
                    }
                }
            }
            int intoEmpty = 0;
            if (_stockEmptySlots.Value && budget > 0) { intoEmpty = FillEmptySlots(shelves, boxes, ref budget); moved += intoEmpty; }
            _shelfStatMoved += moved; _shelfStatEmpty += intoEmpty;
            if (Time.unscaledTime >= _shelfStatAt) { _shelfStatAt = Time.unscaledTime + 60f; LogShelfStatus(shelves, boxes); }
            if (moved > 0) { _statSelfStocked += moved; if (_statSelfStocked % 50 < moved) NoteF($"Stocked by the bot: {_statSelfStocked} items in total"); }
        }

        private float _emptySlotPullAt, _shelfStatAt = 30f;
        private readonly Dictionary<EItemType, int> _shelfNeed = new Dictionary<EItemType, int>();
        private int _shelfStatMoved, _shelfStatEmpty, _shelfPullTries, _shelfPulled, _dispTry, _dispOk, _relabelTry, _relabelOk;
        internal string _dashShelfStatus = "";
        private string _dispWhy = "";
        private void DispWhy(string m) { if (_dispWhy.Length == 0) _dispWhy = m; }

        // Once a minute: how the shelves look (so you can see in the log whether stocking works)
        private void LogShelfStatus(List<Shelf> shelves, Dictionary<EItemType, List<InteractablePackagingBox_Item>> boxes)
        {
            int slots = 0, emptyLabeled = 0, emptyUnlabeled = 0, onFloor = 0, onlyStorage = 0;
            // independent survey of all loose boxes (the boxes list above is changed while stocking)
            var survey = new Dictionary<EItemType, (int total, int closed, int noItems, int busy, int usable)>();
            var rmS = S<RestockManager>();
            if (rmS != null)
                foreach (var b in rmS.m_ItemPackagingBoxList)
                {
                    if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null || b.m_IsStored) continue;
                    if (b.m_ItemCompartment.GetItemCount() <= 0) continue;
                    var tb = Traverse.Create(b);
                    var bt = b.m_ItemCompartment.GetItemType();
                    survey.TryGetValue(bt, out var v);
                    v.total++;
                    bool closed = !tb.Field("m_IsBoxOpened").GetValue<bool>();
                    bool busy = BoxBusy(b);
                    bool noItems = RealItemCount(b) <= 0;
                    if (closed) v.closed++; if (noItems) v.noItems++; if (busy) v.busy++;
                    if (!busy && !noItems) v.usable++;
                    survey[bt] = v;
                }
            int fTotal = 0, fClosed = 0, fNoItems = 0, fBusy = 0, fUsable = 0;
            var missing = new Dictionary<string, int>();
            var stock = CountItemsInBoxes(out _);
            foreach (var shelf in shelves)
            {
                if (shelf == null || shelf.m_ItemNotForSale || !shelf.IsValidObject()) continue;
                foreach (var comp in shelf.GetItemCompartmentList())
                {
                    if (comp == null || comp.m_ItemNotForSale) continue;
                    slots++;
                    if (comp.GetItemCount() > 0) continue;
                    var t = comp.GetItemType();
                    if (t == EItemType.None) { emptyUnlabeled++; continue; }
                    emptyLabeled++;
                    if (survey.TryGetValue(t, out var sv) && sv.total > 0)
                    {
                        onFloor++;
                        fTotal += sv.total; fClosed += sv.closed; fNoItems += sv.noItems; fBusy += sv.busy; fUsable += sv.usable;
                    }
                    else if (stock.TryGetValue(t, out var st) && st > 0) onlyStorage++;
                    if (!stock.TryGetValue(t, out var have) || have <= 0)
                    {
                        string n = t.ToString();
                        try { n = InventoryBase.GetItemData(t)?.GetName() ?? n; } catch { }
                        missing[n] = (missing.TryGetValue(n, out var c) ? c : 0) + 1;
                    }
                }
            }
            string miss = missing.Count == 0 ? "-" : string.Join(", ", missing.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} ({k.Value})"));
            Logger.LogInfo($"[Shelves] {slots} slots, empty: {emptyLabeled} labeled + {emptyUnlabeled} without label | last minute: {_shelfStatMoved} items stocked, {_shelfStatEmpty} into slots without label | empty labeled: {onFloor} have boxes on the floor, {onlyStorage} only in storage | those floor boxes: {fTotal} (closed {fClosed}, no items {fNoItems}, busy {fBusy}, usable {fUsable}) | relabeled: {_relabelOk}/{_relabelTry} | first item into empty slot: {_dispOk}/{_dispTry}{(_dispWhy.Length > 0 ? " (" + _dispWhy + ")" : "")} | busy flags: held {_busyHold}, moving {_busyLerp}, opening {_busyToggle}, stuck->free {_busyStuck} | storage pulls: {_shelfPulled}/{_shelfPullTries} | no box for: {miss}");
            _dashShelfStatus = F($"{slots} slots · empty: {emptyLabeled} labeled, {emptyUnlabeled} without label");
            _shelfStatMoved = 0; _shelfStatEmpty = 0; _shelfPullTries = 0; _shelfPulled = 0; _dispTry = 0; _dispOk = 0; _dispWhy = ""; _busyHold = _busyLerp = _busyToggle = _busyStuck = 0; _relabelTry = _relabelOk = 0;
        }
        private readonly Dictionary<EItemType, bool> _isBoxGoods = new Dictionary<EItemType, bool>();

        // Booster box / display (several packs in one product) – recognised by its name
        private bool IsBoxGoods(EItemType t)
        {
            if (_isBoxGoods.TryGetValue(t, out var b)) return b;
            string n = t.ToString();
            try { n += " " + (InventoryBase.GetItemData(t)?.GetName() ?? ""); } catch { }
            n = n.ToLowerInvariant();
            b = n.Contains("box") || n.Contains("display");
            _isBoxGoods[t] = b;
            return b;
        }

        // Empty shelf slots without a label: choose goods and put them in (the game assigns the slot to the goods)
        private int FillEmptySlots(List<Shelf> shelves, Dictionary<EItemType, List<InteractablePackagingBox_Item>> boxes, ref int budget)
        {
            // how many slots each kind of goods already has
            var slots = new Dictionary<EItemType, int>();
            var empty = new List<(Shelf shelf, ShelfCompartment comp, bool relabel)>();
            // goods that are still around (loose boxes, also busy ones) or on the way – their empty slots keep the label
            var around = new HashSet<EItemType>();
            var rmA = S<RestockManager>();
            if (rmA != null)
                foreach (var b in rmA.m_ItemPackagingBoxList)
                    if (b != null && b.gameObject.activeInHierarchy && b.m_ItemCompartment != null && b.m_ItemCompartment.GetItemCount() > 0)
                        around.Add(b.m_ItemCompartment.GetItemType());
            foreach (var kv in CountItemsInDelivery()) if (kv.Value > 0) around.Add(kv.Key);
            foreach (var shelf in shelves)
            {
                if (shelf == null || shelf.m_ItemNotForSale || !shelf.IsValidObject()) continue;
                foreach (var comp in shelf.GetItemCompartmentList())
                {
                    if (comp == null || comp.m_ItemNotForSale) continue;
                    var t = comp.GetItemType();
                    if (t != EItemType.None) slots[t] = (slots.TryGetValue(t, out var n) ? n : 0) + 1;
                    if (!comp.m_CanPutItem || comp.GetItemCount() != 0) continue;
                    if (t == EItemType.None) empty.Add((shelf, comp, false));
                    // labeled but its goods are gone (no box anywhere, nothing on the way) -> give it goods that are here
                    else if (_relabelEmpty.Value && !around.Contains(t)) empty.Add((shelf, comp, true));
                }
            }
            if (empty.Count == 0) return 0;

            // any goods – packs of the new sets too (thousands lie around for the openers; shelves only take a few hundred)
            bool ShelfGoods(EItemType t) => t != EItemType.None;
            int moved = 0;
            foreach (var (shelf, comp, relabel) in empty)
            {
                if (budget <= 0) break;
                // goods on the floor with the fewest shelf slots
                // booster boxes / displays first (they sell best), then packs; among them the goods with the fewest shelf slots
                var pick = boxes.Where(kv => ShelfGoods(kv.Key) && kv.Value.Count > 0)
                                .OrderBy(kv => IsBoxGoods(kv.Key) ? 0 : 1)
                                .ThenBy(kv => slots.TryGetValue(kv.Key, out var n) ? n : 0).Select(kv => (EItemType?)kv.Key).FirstOrDefault();
                // nullable: the first enum value is a real item (BasicCardPack), not "none"
                if (pick == null)
                {
                    // nothing suitable on the floor: fetch goods from the storage shelf that are on no shelf yet (one box every 3 s)
                    if (_pullFromStorage.Value && Time.unscaledTime >= _emptySlotPullAt)
                    {
                        _emptySlotPullAt = Time.unscaledTime + 3f;
                        PullFromStorage(x => ShelfGoods(x) && !slots.ContainsKey(x) ? (IsBoxGoods(x) ? 0 : 1) : -1, 1, shelf.transform.position);
                    }
                    break;
                }
                var goods = pick.Value;
                var list = boxes[goods];
                int before = moved;
                // first item the way the game does it (like the player placing it): the game checks the fit and labels the slot
                var first = list.FirstOrDefault(x => x != null && x.m_ItemCompartment.GetItemCount() > 0 && RealItemCount(x) > 0);
                if (first == null) { boxes.Remove(goods); continue; }
                if (relabel) _relabelTry++;
                try { first.DispenseItem(false, comp); } catch { }
                if (comp.GetItemType() != goods || comp.GetItemCount() == 0) { boxes.Remove(goods); continue; }   // does not fit here -> try other goods next time
                if (relabel) _relabelOk++;
                budget--; moved++;
                while (budget > 0 && comp.HasEnoughSlot() && list.Count > 0)
                {
                    var box = list[0];
                    if (box.m_ItemCompartment.GetItemCount() <= 0 || RealItemCount(box) <= 0) { list.RemoveAt(0); continue; }
                    if (comp.CheckItemType(goods) != goods) break;          // this slot does not take these goods
                    var item = box.m_ItemCompartment.GetFirstItem();
                    if (item == null) { list.RemoveAt(0); continue; }
                    item.LerpToTransform(comp.GetEmptySlotTransform(), comp.GetEmptySlotParent());
                    comp.AddItem(item, false);
                    box.m_ItemCompartment.RemoveItem(item);
                    budget--; moved++;
                }
                if (moved > before) slots[goods] = (slots.TryGetValue(goods, out var c) ? c : 0) + 1;
            }
            return moved;
        }

        private void StockCards()
        {
            int budget = Mathf.FloorToInt(_cardAcc);
            _cardAcc -= budget;
            if (budget <= 0) return;

            var storages = ShelfManager.GetCardStorageShelfList();
            var cardShelves = ShelfManager.GetCardShelfList();
            if (storages == null || cardShelves == null) return;
            // all card storages with cards – the ones with graded cards first, so the display cases show them first
            var queue = storages.Where(s => s != null && s.IsValidObject() && s.CanWorkerTake() && !s.IsEditingBulkBox() && s.GetTotalCardAmount() > 0)
                                .OrderByDescending(s => s.GetCompactCardDataAmountList()?.Any(e => e.gradedCardIndex > 0) ?? false).ToList();
            if (queue.Count == 0) return;
            int qi = 0;
            var storage = queue[0];
            bool outOfCards = false;

            int placed = 0;
            foreach (var cs in cardShelves)
            {
                if (budget <= 0 || outOfCards) break;
                if (cs == null || !cs.IsValidObject()) continue;
                foreach (var comp in cs.GetCardCompartmentList())
                {
                    if (budget <= 0) break;
                    if (comp == null || comp.m_ItemNotForSale || comp.m_StoredCardList.Count > 0 || comp.IsSettingPrice()) continue;
                    var card = TakeCardGradedFirst(storage);               // like the worker task "fill display cases", but graded first
                    while (card == null && ++qi < queue.Count) { storage = queue[qi]; card = TakeCardGradedFirst(storage); }   // storage empty -> next one
                    if (card == null) { outOfCards = true; break; }
                    card.transform.position = storage.transform.position + Vector3.up;
                    comp.SetCardOnShelf(card);
                    budget--; placed++;
                }
            }
            if (outOfCards && placed == 0) return;                   // storages empty – not a "display cases full" situation
            if (placed > 0)
            {
                _statSelfCards += placed;
                _vitrinesFullSince = -1f;
                _priceTimer = Mathf.Max(_priceTimer, 15f); // neue Karten gleich bepreisen
                if (_statSelfCards % 20 < placed) NoteF($"Display cases filled by the bot: {_statSelfCards} cards in total");
            }
            else if (budget > 0)
            {
                // Karten warten in der Ablage, aber kein freier Vitrinen-Platz -> Verkauf stockt
                if (_vitrinesFullSince < 0f) _vitrinesFullSince = Time.unscaledTime;
                int waiting = storages.Where(s => s != null && s.IsValidObject()).Sum(s => s.GetTotalCardAmount());
                _dashVitrinesFull = Time.unscaledTime - _vitrinesFullSince > 120f ? waiting : 0;
                if (_dashVitrinesFull > 0 && Time.unscaledTime >= _vitrinesFullLogAt)
                {
                    _vitrinesFullLogAt = Time.unscaledTime + 1800f;
                    NoteF($"Display cases full: {waiting} cards waiting in the card storage – place more card display cases to sell more");
                }
            }
        }
        private float _vitrinesFullSince = -1f, _vitrinesFullLogAt;
        private int _dashVitrinesFull;

        private int _statSelfStocked, _statSelfCards;
        private float _pullOpenerAt, _pullShelfAt;

        // GetRandomCard wählt zufällig unter allen Einträgen – bei Tausenden normalen Karten kämen gegradete fast nie dran.
        // Darum: liegt eine gegradete Karte in der Ablage, kurz nur diese "anbieten" und die Liste danach wiederherstellen.
        private static InteractableCard3d TakeCardGradedFirst(InteractableCardStorageShelf storage)
        {
            var full = storage.GetCompactCardDataAmountList();
            var graded = full?.FirstOrDefault(e => e != null && e.gradedCardIndex > 0);
            if (graded == null) return storage.GetRandomCard();
            storage.SetCompactCardDataAmountList(new List<CompactCardDataAmount> { graded });
            InteractableCard3d card = null;
            try { card = storage.GetRandomCard(); }
            finally
            {
                if (card != null) full.Remove(graded);
                storage.SetCompactCardDataAmountList(full);
            }
            return card;
        }

        // Tatsächlich vorhandene Artikel-Objekte im Karton (GetItemCount zählt auch noch nicht erzeugte)
        private static int RealItemCount(InteractablePackagingBox_Item b)
        {
            try { return Traverse.Create(b.m_ItemCompartment).Field("m_StoredItemList").GetValue<List<Item>>()?.Count ?? 0; }
            catch { return 0; }
        }
    }
}
