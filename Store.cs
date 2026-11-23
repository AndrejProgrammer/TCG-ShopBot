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
        private readonly Dictionary<int, int> _licRestoreTries = new Dictionary<int, int>();
        private readonly HashSet<int> _licRestoreGaveUp = new HashSet<int>();
        private int _licRestoreDay = -1;
        private List<string> _setsWithoutPacks = new List<string>();
        // Lizenzen, die sich während einer Sitzung immer wieder sperren (Mod) -> von dieser Art nichts Neues mehr kaufen
        private ConfigEntry<string> _licUnstable;
        private HashSet<string> _licUnstableSet;
        private readonly Dictionary<string, int> _licRestoredSession = new Dictionary<string, int>();
        private readonly HashSet<string> _licBoughtSession = new HashSet<string>();

        // Art einer Lizenz = die letzten zwei Wörter, z. B. "Booster Box" oder "Booster Packs"
        private static string LicKind(string name)
        {
            var w = (name ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return w.Length >= 2 ? (w[w.Length - 2] + " " + w[w.Length - 1]).ToLowerInvariant() : (name ?? "").ToLowerInvariant();
        }
        private int UnstableOfKind(string kind) => _licUnstableSet.Count(n => LicKind(n) == kind);

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
            _licUnstable = Config.Bind("15. Lizenzen", "Sperren sich immer wieder (intern)", "", "Lizenzen, die eine Mod nach dem Kauf immer wieder sperrt. Ab 2 derselben Art (z. B. Booster Box) kauft der Bot von dieser Art nichts Neues mehr. Leeren = neu lernen.");
            _licUnstableSet = new HashSet<string>((_licUnstable.Value ?? "").Split('|').Select(p => p.Trim()).Where(p => p.Length > 0));
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

        private void LicenseTick()
        {
            var unlocked = CPlayerData.m_IsItemLicenseUnlocked;
            if (unlocked == null) return;
            int level = CPlayerData.m_ShopLevel + 1;

            // Das Spiel/die Mods setzen Lizenzen beim Laden zurück (Index-Verschiebung). Schon bezahlte -> kostenlos wieder frei.
            var restored = new List<string>();
            if (_licRestoreDay != CPlayerData.m_CurrentDay) { _licRestoreDay = CPlayerData.m_CurrentDay; _licRestoreTries.Clear(); }
            for (int i = 0; i < unlocked.Count; i++)
            {
                if (unlocked[i]) continue;
                RestockData rd;
                try { rd = InventoryBase.GetRestockData(i); } catch { continue; }
                if (rd == null || rd.itemType == EItemType.None) continue;
                string nm = LicName(rd.itemType);
                if (!_licBoughtSet.Contains(nm)) continue;
                // Sperrt etwas (Mod) diese Lizenz immer wieder? Nach 2 Versuchen in Ruhe lassen – sie gilt trotzdem als gekauft.
                _licRestoreTries.TryGetValue(i, out int tries);
                if (tries >= 2)
                {
                    if (_licRestoreGaveUp.Add(i)) Logger.LogInfo($"[Lizenzen] {nm} (Eintrag {i}) wird immer wieder gesperrt – lasse ihn in Ruhe");
                    continue;
                }
                _licRestoreTries[i] = tries + 1;
                CPlayerData.SetUnlockItemLicense(i);
                restored.Add(nm);
                // Einmal beim Laden zurücksetzen ist normal. Erneut gesperrt (oder gleich nach dem Kauf) = instabil.
                _licRestoredSession.TryGetValue(nm, out int rc);
                _licRestoredSession[nm] = ++rc;
                if ((rc >= 2 || _licBoughtSession.Contains(nm)) && _licUnstableSet.Add(nm))
                {
                    _licUnstable.Value = string.Join("|", _licUnstableSet);
                    string kind = LicKind(nm);
                    if (UnstableOfKind(kind) == 2)
                        Note($"Lizenzen: '{kind}' sperrt eine Mod immer wieder – davon kauft der Bot keine neuen mehr");
                }
            }
            if (restored.Count > 0)
            {
                GameInstance.m_IsItemLicenseUnlocked = true;
                Note($"Lizenzen wiederhergestellt (schon bezahlt, kostenlos): {string.Join(", ", restored.Take(6))}{(restored.Count > 6 ? $" +{restored.Count - 6}" : "")}");
            }

            var cands = new List<(int idx, RestockData rd)>();
            for (int i = 0; i < unlocked.Count; i++)
            {
                if (unlocked[i]) continue;
                RestockData rd;
                try { rd = InventoryBase.GetRestockData(i); } catch { continue; }
                if (rd == null || rd.itemType == EItemType.None || rd.licensePrice <= 0f) continue;
                if (level < rd.licenseShopLevelRequired) continue;
                if (_licBoughtSet.Contains(LicName(rd.itemType))) continue;  // schon einmal gekauft
                if (UnstableOfKind(LicKind(LicName(rd.itemType))) >= 2) continue;   // diese Art sperrt eine Mod immer wieder
                bool sameTypeUnlocked = false;                              // anderer Eintrag desselben Artikels schon frei?
                for (int j = 0; j < unlocked.Count && !sameTypeUnlocked; j++)
                {
                    if (j == i || !unlocked[j]) continue;
                    try { var o = InventoryBase.GetRestockData(j); if (o != null && o.itemType == rd.itemType) sameTypeUnlocked = true; } catch { }
                }
                if (sameTypeUnlocked) continue;
                var data = InventoryBase.GetItemData(rd.itemType);
                if (data == null || data.category != EItemCategory.TCG) continue;      // nur Packs/Displays
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
            foreach (var b in rm.m_ItemPackagingBoxList)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null || b.m_IsStored) continue;
                if (b.m_ItemCompartment.GetItemCount() <= 0 || !b.CanWorkerTakeBox()) continue;
                if (!_openerPackTypes.Contains(b.m_ItemCompartment.GetItemType())) continue;
                var tb = Traverse.Create(b);
                if (tb.Field("m_IsBeingHold").GetValue<bool>() || tb.Field("m_IsLerpingToPos").GetValue<bool>() || tb.Field("m_IsTogglingOpenClose").GetValue<bool>()) continue;
                if (RealItemCount(b) <= 0)
                {
                    if (!tb.Field("m_IsBoxOpened").GetValue<bool>()) { try { b.SetOpenCloseBox(true, false); } catch { } }
                    continue;                                              // Artikel entstehen erst beim Öffnen -> nächste Runde
                }
                boxes.Add(b);
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
                _priceTimer = Mathf.Max(_priceTimer, 15f); // neue Karten gleich bepreisen
                if (_statSelfCards % 20 < placed) Note($"Vitrinen selbst befüllt: {_statSelfCards} Karten insgesamt");
            }
        }

        private int _statSelfStocked, _statSelfCards;

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
