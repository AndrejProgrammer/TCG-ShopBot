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
        private ConfigEntry<bool> _licEnabled, _licPokemonOnly, _stockSelf, _cardsSelf;
        private ConfigEntry<float> _licMoneyFactor;
        private ConfigEntry<string> _licBought;   // dauerhaft: schon gekaufte Lizenzen (Artikel-NAMEN – Typ-Nummern der Mods ändern sich pro Start)
        private HashSet<string> _licBoughtSet;
        private ConfigEntry<int> _stockPerSecond, _cardsPerSecond;
        private float _licTimer = 30f;
        private List<string> _setsWithoutPacks = new List<string>();

        // Opener selbst befüllen / Arbeiter einstellen / Opener-Turbo
        private ConfigEntry<bool> _openerSelf, _hireEnabled;
        private ConfigEntry<int> _openerPerSecond, _hireMax;
        private ConfigEntry<float> _hireMoneyFactor, _openerTurbo;
        private float _openerAcc, _hireTimer = 40f;
        private int _statSelfOpener, _statHired;
        private readonly Dictionary<InteractableAutoPackOpener, float> _openerBaseTime = new Dictionary<InteractableAutoPackOpener, float>();
        private readonly Dictionary<InteractableAutoPackOpener, float> _openerSetTime = new Dictionary<InteractableAutoPackOpener, float>();

        private void BindStoreConfig()
        {
            _licEnabled = Config.Bind("15. Lizenzen", "Neue Produkte freischalten", true, "Kauft Lizenzen für neue Packs/Displays, sobald Level und Geld reichen (günstigste zuerst).");
            _licPokemonOnly = Config.Bind("15. Lizenzen", "Nur Pokémon-Produkte", true, "Nur Lizenzen aus den Pokémon-Shops.");
            _licMoneyFactor = Config.Bind("15. Lizenzen", "Nur kaufen wenn Budget mindestens X mal Preis", 3f, "Schutz, damit genug Geld für Packs bleibt.");
            _licBought = Config.Bind("15. Lizenzen", "Schon gekauft (intern)", "", "Vom Bot gekaufte Lizenzen – werden nie erneut gekauft, auch wenn das Spiel sie zurücksetzt.");
            _licBoughtSet = new HashSet<string>();
            foreach (var part in (_licBought.Value ?? "").Split('|'))
            {
                var p = part.Trim();
                if (p.Length > 0 && !int.TryParse(p, out _)) _licBoughtSet.Add(p);   // alte Zahlen-Einträge (v0.1) sind wertlos
            }
            _openerSelf = Config.Bind("2. Pack-Opener", "Bot befüllt Opener selbst", true, "Legt Packs unfertiger Pokémon-Sets aus Kartons direkt in freie Pack-Opener (Ziel-Sets zuerst). Opener stehen so nie leer.");
            _openerPerSecond = Config.Bind("2. Pack-Opener", "Packs pro Sekunde in Opener", 40, "Wie schnell der Bot die Opener befüllt.");
            _openerTurbo = Config.Bind("2. Pack-Opener", "Opener-Turbo (Tempo x)", 1f, "Opener öffnen Packs so viel schneller (1 = normal, Spielwert).");
            _hireEnabled = Config.Bind("9. Arbeiter", "Neue Arbeiter automatisch einstellen", true, "Stellt freie Arbeiter ein, sobald Level und Geld reichen (Einsatzplan teilt sie danach ein).");
            _hireMax = Config.Bind("9. Arbeiter", "Max. Arbeiter (0 bedeutet alle)", 0, "Höchstens so viele Arbeiter insgesamt.");
            _hireMoneyFactor = Config.Bind("9. Arbeiter", "Einstellen nur wenn Budget mindestens X mal Einstellgebühr", 10f, "Schutz, damit genug Geld für Packs bleibt.");
            _stockSelf = Config.Bind("16. Selbst einräumen", "Bot räumt Regale selbst ein", true, "Legt Ware aus Kartons direkt in Regalfächer, die für diese Ware vorgesehen sind (zusätzlich zu den Arbeitern).");
            _stockPerSecond = Config.Bind("16. Selbst einräumen", "Artikel pro Sekunde", 6, "Wie schnell der Bot einräumt.");
            _cardsSelf = Config.Bind("16. Selbst einräumen", "Bot füllt leere Vitrinen-Plätze", true, "Nimmt Karten aus der Kartenablage und legt sie in leere Kartenfächer.");
            _cardsPerSecond = Config.Bind("16. Selbst einräumen", "Karten pro Sekunde", 2, "Wie schnell der Bot Vitrinen füllt.");
        }

        // ---------------- Lizenzen ----------------
        private void HandleLicenses()
        {
            if (!_licEnabled.Value || (_licTimer += 1f) < 60f) return;
            _licTimer = 0f;
            SafeRun("Lizenzen", LicenseTick);
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
                    if (_licRebuyWarned.Add(nm)) Logger.LogWarning($"[Lizenzen] {nm} ist nach dem Kauf wieder gesperrt – kaufe nicht erneut");
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
            int bought = 0;
            foreach (var c in order)
            {
                if (Budget < c.rd.licensePrice * Mathf.Max(1f, _licMoneyFactor.Value)) continue;
                BuyLicense(c.idx, c.rd);
                bought++;
                if (bought >= 25) break;                                    // pro Minute höchstens 25
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
            Note($"Neue Lizenz: {InventoryBase.GetItemData(r.itemType)?.GetName() ?? r.itemType.ToString()} freigeschaltet ({GameInstance.GetPriceString(price)})");
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
            SafeRun("Einstellen", HireTick);
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
            Note($"Neuer Arbeiter eingestellt: {wm.m_WorkerDataList[best].GetName()} ({GameInstance.GetPriceString(bestCost)}, Lohn {GameInstance.GetPriceString(wm.m_WorkerDataList[best].costPerDay)}/Tag) – jetzt {count + 1} Arbeiter");
        }

        // ---------------- Selbst einräumen ----------------
        private float _stockAcc, _cardAcc;

        private void HandleSelfStocking()
        {
            if (_stockSelf.Value) { _stockAcc += _stockPerSecond.Value; SafeRun("Einräumen", StockShelves); }
            if (_cardsSelf.Value) { _cardAcc += _cardsPerSecond.Value; SafeRun("Vitrinen", StockCards); }
            if (_openerSelf.Value) { _openerAcc = Mathf.Min(_openerAcc + _openerPerSecond.Value, _openerPerSecond.Value * 2f); SafeRun("Opener befüllen", StockOpeners); }
            SafeRun("Opener-Turbo", ApplyOpenerTurbo);
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
                onFloor++;
                var tb = Traverse.Create(b);
                if (tb.Field("m_IsBeingHold").GetValue<bool>() || tb.Field("m_IsLerpingToPos").GetValue<bool>() || tb.Field("m_IsTogglingOpenClose").GetValue<bool>()) continue;
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

            int moved = 0, bi = 0;
            foreach (var o in free)
            {
                while (budget > 0 && bi < boxes.Count && !o.GetIsProcessing() && o.HasEnoughSlot())
                {
                    var box = boxes[bi];
                    if (box == null || RealItemCount(box) <= 0) { bi++; continue; }
                    int before = o.GetStoredItemList().Count;
                    o.DispenseItemFromBox(box, isPlayer: false);
                    if (o.GetStoredItemList().Count <= before) { bi++; continue; }   // Karton passt nicht (z. B. kein Pack) -> nächster
                    budget--; moved++;
                }
                if (budget <= 0 || bi >= boxes.Count) break;
            }
            _openerAcc -= moved;
            if (moved > 0)
            {
                _statSelfOpener += moved;
                if (_statSelfOpener % 500 < moved) Note($"Opener selbst befüllt: {_statSelfOpener} Packs insgesamt");
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

            // verfügbare Kartons nach Ware (nicht getragen, nicht unterwegs)
            var boxes = new Dictionary<EItemType, List<InteractablePackagingBox_Item>>();
            foreach (var b in rm.m_ItemPackagingBoxList)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null) continue;
                if (b.m_ItemCompartment.GetItemCount() <= 0) continue;
                var tb = Traverse.Create(b);
                if (tb.Field("m_IsBeingHold").GetValue<bool>() || tb.Field("m_IsLerpingToPos").GetValue<bool>()) continue;
                if (b.m_IsStored) continue;                                   // Kartons im Lagerregal holen die Arbeiter
                if (tb.Field("m_IsTogglingOpenClose").GetValue<bool>()) continue;
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
                        if (PullFromStorage(x => x == t ? 0 : -1, 1, shelf.transform.position) > 0) { floorTypes.Add(t); sorts++; }
                        else floorTypes.Add(t);                                                // nichts im Lager -> nicht nochmal suchen
                    }
                }
            }
            if (boxes.Count == 0) return;

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
            if (moved > 0) { _statSelfStocked += moved; if (_statSelfStocked % 50 < moved) Note($"Selbst eingeräumt: {_statSelfStocked} Artikel insgesamt"); }
        }

        private void StockCards()
        {
            int budget = Mathf.FloorToInt(_cardAcc);
            _cardAcc -= budget;
            if (budget <= 0) return;

            var storages = ShelfManager.GetCardStorageShelfList();
            var cardShelves = ShelfManager.GetCardShelfList();
            if (storages == null || cardShelves == null) return;
            var storage = storages.FirstOrDefault(s => s != null && s.IsValidObject() && s.CanWorkerTake() && !s.IsEditingBulkBox() && s.GetTotalCardAmount() > 0);
            if (storage == null) return;

            int placed = 0;
            foreach (var cs in cardShelves)
            {
                if (budget <= 0) break;
                if (cs == null || !cs.IsValidObject()) continue;
                foreach (var comp in cs.GetCardCompartmentList())
                {
                    if (budget <= 0) break;
                    if (comp == null || comp.m_ItemNotForSale || comp.m_StoredCardList.Count > 0 || comp.IsSettingPrice()) continue;
                    var card = TakeCardGradedFirst(storage);               // wie der Arbeiter "Vitrinen befüllen", aber gegradete zuerst
                    if (card == null) return;
                    card.transform.position = storage.transform.position + Vector3.up;
                    comp.SetCardOnShelf(card);
                    budget--; placed++;
                }
            }
            if (placed > 0)
            {
                _statSelfCards += placed;
                _vitrinesFullSince = -1f;
                _priceTimer = Mathf.Max(_priceTimer, 15f); // neue Karten gleich bepreisen
                if (_statSelfCards % 20 < placed) Note($"Vitrinen selbst befüllt: {_statSelfCards} Karten insgesamt");
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
                    Note($"Vitrinen voll: {waiting} Karten warten in der Kartenablage – mehr Karten-Vitrinen aufstellen, dann verkauft sich mehr");
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
