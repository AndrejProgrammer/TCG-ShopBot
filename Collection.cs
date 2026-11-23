using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Etappe 2 + 3: Sammlung, Pack-Kauf, Opener-Arbeiter, Boost, Kartenablage, Arbeiter, Nachbestellen
    public partial class Plugin
    {
        private ConfigEntry<bool> _colEnabled;
        private ConfigEntry<float> _colReserve;
        private ConfigEntry<int> _colFocusSets;
        private ConfigEntry<int> _colBufferCases;
        private ConfigEntry<int> _colCasesPerBuy;
        private ConfigEntry<float> _colFills;
        private ConfigEntry<float> _colBudgetPerExtraSet, _colBudgetPerExtraFill, _colMaxFills;
        private ConfigEntry<int> _colMaxFocusSets, _colPacksPerMissing;
        private ConfigEntry<bool> _colEfficiency;
        private ConfigEntry<string> _colPacksBoughtCfg;
        private readonly Dictionary<string, int> _setPacksBought = new Dictionary<string, int>();
        private float _cappedLogAt;
        private void CountSetPacks(string set, int packs)
        {
            _setPacksBought[set] = (_setPacksBought.TryGetValue(set, out var n) ? n : 0) + packs;
            _colPacksBoughtCfg.Value = string.Join("|", _setPacksBought.Select(kv => kv.Key.Replace("|", "/").Replace("=", "-") + "=" + kv.Value));
        }
        private ConfigEntry<bool> _colPokemonOnly;
        private ConfigEntry<float> _dayBuyLimit, _dayBuyLimitPercent;
        // Wirksames Tageslimit: fester Wert, aber mindestens X % vom Geld über der Reserve (wenn eingestellt). 0 = kein Limit.
        // Tageslimit: fester Wert, aber mindestens X % vom Geld. Bei "ganzes Geld nutzen" mindestens 15 %, wenn kein Prozentwert eingestellt ist.
        private float DayLimit
        {
            get
            {
                if (_dayBuyLimit.Value <= 0f) return 0f;
                float pct = _dayBuyLimitPercent.Value > 0f ? _dayBuyLimitPercent.Value : (_useAllMoney != null && _useAllMoney.Value ? 15f : 0f);
                return Mathf.Max(_dayBuyLimit.Value, pct > 0f ? (float)(Math.Max(0.0, Budget) * pct / 100.0) : 0f);
            }
        }
        private ConfigEntry<int> _maxStockPacks;
        private float _daySpent; private int _daySpentDay = -1;

        // Einkaufs-Tageslimit (Packs, Nachbestellungen, Bewertungen). Rechnungen, Löhne, Bonus und Grading zählen nicht.
        private float DayLeft()
        {
            if (_daySpentDay != CPlayerData.m_CurrentDay) { _daySpentDay = CPlayerData.m_CurrentDay; _daySpent = 0f; }
            return DayLimit <= 0f ? float.MaxValue : Mathf.Max(0f, DayLimit - _daySpent);
        }
        private void AddDaySpent(float v) { DayLeft(); _daySpent += v; }
        private ConfigEntry<bool> _billsEnabled;
        private ConfigEntry<bool> _bonusEnabled;
        private ConfigEntry<bool> _expEnabled, _expWarehouse;
        private ConfigEntry<int> _expMaxShop, _expMaxWarehouse;
        private ConfigEntry<float> _expMoneyFactor;
        private ConfigEntry<bool> _reviewEnabled;
        private ConfigEntry<int> _reviewCases;
        private ConfigEntry<float> _reviewMinMarkup;
        private ConfigEntry<bool> _restockOnlyTcg;
        private ConfigEntry<int> _colMaxBoxesOnFloor;
        private ConfigEntry<bool> _colSetWorkerPacks;
        private ConfigEntry<bool> _colClaimBoost;

        private ConfigEntry<bool> _bulkEnabled;
        private ConfigEntry<float> _bulkMaxValue;
        private ConfigEntry<int> _bulkMaxPerRun;

        private ConfigEntry<bool> _workersAuto;
        private ConfigEntry<bool> _gradedSell, _gradedKeepTen;

        private ConfigEntry<bool> _restockEnabled;
        private ConfigEntry<int> _restockMaxPerMinute;

        private float _colTimer = 52f, _bulkTimer = 285f, _workerTimer = 50f, _restockTimer = 45f;
        private bool _ctWarned;

        private void BindCollectionConfig()
        {
            _billsEnabled = Config.Bind("0. Rechnungen", "Rechnungen automatisch bezahlen", true, "Miete, Strom und Löhne werden sofort bezahlt, sobald sie da sind (vor allen Einkäufen).");
            _bonusEnabled = Config.Bind("9. Arbeiter", "Jeden Tag Bonus an alle Arbeiter", true, "Wie der Bonus-Knopf: 3x pro Tag und Arbeiter (kostet je 1 Tageslohn). Jeder Bonus = +1 Level auf alle Aufgaben und doppelte Erfahrung.");
            _expEnabled = Config.Bind("14. Erweiterungen", "Laden-Erweiterungen kaufen", true, "Kauft die nächste Laden-Erweiterung, sobald Level und Geld reichen.");
            _expWarehouse = Config.Bind("14. Erweiterungen", "Lager (Shop B) und Lager-Erweiterungen kaufen", true, "Schaltet das Lager frei und erweitert es.");
            _expMaxShop = Config.Bind("14. Erweiterungen", "Max. Laden-Erweiterungen", 30, "Bis zu dieser Anzahl (Spiel-Maximum 30).");
            _expMaxWarehouse = Config.Bind("14. Erweiterungen", "Max. Lager-Erweiterungen", 14, "Bis zu dieser Anzahl (Spiel-Maximum 14).");
            _expMoneyFactor = Config.Bind("14. Erweiterungen", "Nur kaufen wenn Budget mindestens X mal Preis", 5f, "Schutz: Erweiterung nur, wenn das Geld über der Reserve mindestens so oft den Preis abdeckt.");
            _reviewEnabled = Config.Bind("13. Bewertungen", "Auf Kundenbewertungen reagieren", true, "Vermisste Packs/Displays sofort nachbestellen, bei 'zu teuer' den Aufschlag für diesen Artikel senken.");
            _reviewCases = Config.Bind("13. Bewertungen", "Kartons pro vermisstem Artikel", 3, "So viele Kartons werden bestellt, wenn Kunden einen Artikel vermissen.");
            _reviewMinMarkup = Config.Bind("13. Bewertungen", "Minimaler Aufschlag (%)", 10f, "Tiefer wird der Aufschlag bei Preis-Beschwerden nie gesenkt.");
            _colEnabled = Config.Bind("7. Sammlung", "Sammlung automatisch vervollständigen", true, "Kauft Packs von unfertigen Sets (Collection Tracker) und lässt sie in den Openern öffnen.");
            _colReserve = Config.Bind("7. Sammlung", "Sicherheitsreserve ($)", 20000f, "So viel Geld bleibt immer übrig (Miete/Löhne). Alles darüber darf für Packs ausgegeben werden.");
            _useAllMoney = Config.Bind("7. Sammlung", "Ganzes Geld nutzen (nur Rechnungen zurückhalten)", true, "An: der Bot darf alles ausgeben, hält aber offene Rechnungen + die nächsten Tagesrechnungen (x1,5) zurück – so geht das Konto nie ins Minus. Aus: feste Sicherheitsreserve."); 
            _colFocusSets = Config.Bind("7. Sammlung", "Ziel-Sets gleichzeitig", 5, "An so vielen Sets wird gleichzeitig gearbeitet.");
            _colEfficiency = Config.Bind("7. Sammlung", "Sets nach Effizienz wählen", true, "An: Sets, bei denen pro Dollar die meisten neuen Karten zu erwarten sind (viele fehlende Karten, günstige Packs). Aus: fast fertige Sets zuerst.");
            _colPacksPerMissing = Config.Bind("7. Sammlung", "Fast fertige Sets: max. Packs pro fehlender Karte", 1500, "Fehlen nur noch 1-3 Karten, kauft der Bot höchstens so viele Packs pro fehlender Karte – danach wird das Set pausiert (Karten von Kunden ankaufen). 0 = kein Deckel.");
            _colPacksBoughtCfg = Config.Bind("7. Sammlung", "Packs gekauft pro Set (intern)", "", "Zähler für den Deckel oben. Leeren = zurücksetzen.");
            foreach (var part in (_colPacksBoughtCfg.Value ?? "").Split('|'))
            {
                int eq = part.LastIndexOf('=');
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), out var n)) _setPacksBought[part.Substring(0, eq)] = n;
            }
            _colFills = Config.Bind("7. Sammlung", "Vorrat in Opener-Füllungen", 2f, "Vorrat = Kapazität ALLER Pack-Opener x diesen Wert (z. B. 11 Automaten x 448 Packs x 2 = 9856 Packs), verteilt auf die Ziel-Sets. Passt sich automatisch an, wenn neue Automaten dazukommen.");
            _colBudgetPerExtraSet = Config.Bind("7. Sammlung", "Budget pro zusätzlichem Ziel-Set ($)", 50000f, "Je so viel Geld über der Reserve wird ein Ziel-Set mehr gleichzeitig bespielt. 0 = aus.");
            _colPokemonOnly = Config.Bind("7. Sammlung", "Nur Pokémon-Sets", true, "Nur Sets aus Pokémon-Shops (Pokemon Shop, Pokemon Pocket Shop) – keine Tetramon/Grundspiel-Sets.");
            _dayBuyLimit = Config.Bind("7. Sammlung", "Einkaufslimit pro Tag ($)", 60000f, "Höchstens so viel gibt der Bot pro Spieltag für Packs/Displays aus (Sammlung + Nachbestellen + Bewertungen). 0 = kein Limit.");
            _dayBuyLimitPercent = Config.Bind("7. Sammlung", "Tageslimit mindestens Prozent vom Geld", 0f, "Das Tageslimit wächst mit: mindestens so viel Prozent vom Geld über der Reserve. 0 = aus (nur fester Wert).");
            _maxStockPacks = Config.Bind("7. Sammlung", "Max. Packs im Vorrat gesamt", 15000, "Obergrenze für den Pack-Vorrat aller Ziel-Sets zusammen – egal wie viel Geld da ist.");
            _colMaxFocusSets = Config.Bind("7. Sammlung", "Max. Ziel-Sets gleichzeitig", 10, "Obergrenze für die Anzahl gleichzeitiger Ziel-Sets.");
            _colBudgetPerExtraFill = Config.Bind("7. Sammlung", "Budget pro zusätzlicher Opener-Füllung ($)", 150000f, "Je so viel Geld über der Reserve wird der Vorrat um eine Opener-Füllung erhöht. 0 = aus.");
            _colMaxFills = Config.Bind("7. Sammlung", "Max. Opener-Füllungen Vorrat", 4f, "Obergrenze für den Vorrat.");
            _colCasesPerBuy = Config.Bind("7. Sammlung", "Max. Kartons pro Bestellung", 40, "Höchstens so viele Kartons pro Set und Bestellung.");
            _restockOnlyTcg = Config.Bind("10. Nachbestellen", "Nur Packs und Displays", true, "Nur Booster-Packs und Booster-Boxen/Displays nachbestellen – keine Figuren, Spiele, Hüllen, Matten usw.");
            _colMaxBoxesOnFloor = Config.Bind("7. Sammlung", "Max. volle Kartons im Laden insgesamt", 600, "Über dieser Anzahl voller Kartons (Laden + Lager) wird nichts mehr bestellt.");
            _colSetWorkerPacks = Config.Bind("7. Sammlung", "Opener-Arbeiter auf Ziel-Sets einstellen", true, "Arbeiter mit Aufgabe 'Opener befüllen' nehmen nur Packs der Ziel-Sets.");
            _colClaimBoost = Config.Bind("7. Sammlung", "Pack-Automaten-Boost abholen", true, "Holt die nächste Boost-Stufe im Collection Tracker automatisch ab.");

            _bulkEnabled = Config.Bind("8. Kartenablage", "Doppelte in Kartenablage legen", true, "Doppelte Karten in die Kartenablage (Card Storage) legen, damit Arbeiter sie in die Vitrinen stellen. 1 Stück bleibt immer im Album.");
            _bulkMaxValue = Config.Bind("8. Kartenablage", "Nur Karten unter ($)", 100f, "Wertvollere Doppelte bleiben im Album.");
            _bulkMaxPerRun = Config.Bind("8. Kartenablage", "Max. Karten pro Durchgang", 2000, "Begrenzung pro Durchgang (alle 5 Minuten).");
            _gradedSell = Config.Bind("8. Kartenablage", "Gegradete Karten zuerst verkaufen", true, "Gegradete Karten kommen zuerst in die Kartenablage -> Arbeiter stellen sie in die Vitrinen (Marktpreis +20 %). Normale Doppelte erst, wenn keine gegradeten mehr warten.");
            _gradedKeepTen = Config.Bind("8. Kartenablage", "Grade 10 (PSA 10): nur Duplikate verkaufen", true, "Von jeder Karte mit Grade 10 bleibt immer eine im Album.");

            _workersAuto = Config.Bind("9. Arbeiter", "Arbeiter automatisch einteilen", true, "Einsatzplan nach Stärken (Haupt- + Nebenrolle), jeden Morgen und wenn sich die Belegschaft ändert.");
            BindWorkerPlanConfig();

            _restockEnabled = Config.Bind("10. Nachbestellen", "Produkte nachbestellen", true, "Bestellt Produkte nach, wenn Regal + Lager fast leer sind.");
            _restockMaxPerMinute = Config.Bind("10. Nachbestellen", "Max. Bestellungen pro Minute", 3, "Begrenzung, damit nicht zu viele Kartons kommen.");
        }

        // Wird aus dem Haupt-Takt (1x pro Sekunde) aufgerufen
        private void HandleEconomy()
        {
            if (LoadingNextDay()) return;

            // Zuerst die Rechnungen – dann Arbeiter-Bonus – erst danach wird eingekauft
            HandleBills();
            HandleWorkerBonus();
            HandleReviews();
            HandleExpansions();
            HandleLicenses();
            HandleSelfStocking();
            HandleHiring();
            HandleSorting();
            HandlePackEmergency();
            DayReportTick();

            if (_colEnabled.Value && (_colTimer += 1f) >= 60f) { _colTimer = 0f; SafeRun("Sammlung", CollectionTick); }
            if (_bulkEnabled.Value && (_bulkTimer += 1f) >= 300f) { _bulkTimer = 0f; SafeRun("Kartenablage", BulkTick); }
            if (_workersAuto.Value && (_workerTimer += 1f) >= 60f) { _workerTimer = 0f; SafeRun("Arbeiter", WorkerTick); }
            if (_restockEnabled.Value && (_restockTimer += 1f) >= 60f) { _restockTimer = 0f; SafeRun("Nachbestellen", RestockTick); }
            HandleUpgrades();
            HandleGrading();
        }

        // ================= Rechnungen =================
        private float _billTimer = 999f;

        private void HandleBills()
        {
            if (!_billsEnabled.Value || (_billTimer += 1f) < 15f) return;
            _billTimer = 0f;
            SafeRun("Rechnungen", () =>
            {
                var screen = S<PhoneManager>()?.m_RentBillScreen;
                if (screen == null) return;
                var parts = new List<string>();
                void Pay(EBillType type, string name, Action<bool> payFn)
                {
                    var bill = CPlayerData.GetBill(type);
                    if (bill == null || bill.amountToPay <= 0f) return;
                    if (CPlayerData.m_CoinAmountDouble < bill.amountToPay) return;
                    float amount = bill.amountToPay;
                    payFn(false);
                    var after = CPlayerData.GetBill(type);
                    if (after == null || after.amountToPay <= 0f) { parts.Add($"{name} {GameInstance.GetPriceString(amount)}"); NoteBillPaid(amount); }
                }
                Pay(EBillType.Rent, "Miete", screen.OnPressPayRentBill);
                Pay(EBillType.Electric, "Strom", screen.OnPressPayElectricBill);
                Pay(EBillType.Employee, "Löhne", screen.OnPressPaySalaryBill);
                if (parts.Count > 0) Note("Rechnungen bezahlt: " + string.Join(", ", parts));
            });
        }

        // ================= Kundenbewertungen auswerten =================
        // "Gibt's hier kein XXX?"  -> Artikel fehlt  -> sofort nachbestellen
        // "XXX ist zu teuer" (1-2 Sterne) -> Aufschlag für genau diesen Artikel senken (nie unter Minimum)
        private float _reviewTimer;
        private readonly HashSet<string> _seenReviews = new HashSet<string>();
        private bool _reviewsPrimed;
        internal readonly Dictionary<EItemType, float> _itemMarkup = new Dictionary<EItemType, float>(); // % pro Artikel
        private int _markupDay = -1;

        private void HandleReviews()
        {
            if (!_reviewEnabled.Value || (_reviewTimer += 1f) < 30f) return;
            _reviewTimer = 0f;
            SafeRun("Bewertungen", ReviewTick);
        }

        private void ReviewTick()
        {
            var reviews = CPlayerData.m_CustomerReviewDataList;
            if (reviews == null) return;

            // Neuer Tag: gesenkte Aufschläge langsam wieder Richtung Standard
            if (_markupDay != CPlayerData.m_CurrentDay)
            {
                _markupDay = CPlayerData.m_CurrentDay;
                foreach (var k in _itemMarkup.Keys.ToList())
                {
                    _itemMarkup[k] = Mathf.Min(_priceItemPercent.Value, _itemMarkup[k] + 2.5f);
                    if (_itemMarkup[k] >= _priceItemPercent.Value) _itemMarkup.Remove(k);
                }
            }

            var missing = new Dictionary<EItemType, int>();
            var tooExpensive = new Dictionary<EItemType, int>();
            foreach (var r in reviews)
            {
                if (r == null) continue;
                string key = $"{r.day}|{r.hour}|{r.minute}|{r.customerName}|{r.customerReviewType}|{r.itemType}|{r.textSOIndex}";
                if (!_seenReviews.Add(key)) continue;
                if (r.day < CPlayerData.m_CurrentDay - 1) continue; // nur aktuelle

                if (r.customerReviewType == ECustomerReviewType.ItemVariety && r.textSOGoodBadLevel == 0 && r.itemType != EItemType.None)
                    missing[r.itemType] = (missing.TryGetValue(r.itemType, out var m) ? m : 0) + 1;
                else if (r.customerReviewType == ECustomerReviewType.ItemPrice && r.starLevel <= 2 && r.itemType != EItemType.None)
                    tooExpensive[r.itemType] = (tooExpensive.TryGetValue(r.itemType, out var e) ? e : 0) + 1;
            }

            // Fehlende Artikel nachbestellen
            bool needShelfHelp = false;
            UpdateShelfHelper(false); // abgelaufene Aushilfe zurücksetzen
            foreach (var kv in missing.OrderByDescending(k => k.Value))
            {
                var type = kv.Key;
                var data = InventoryBase.GetItemData(type);
                string name = data?.GetName() ?? type.ToString();
                if (_restockOnlyTcg.Value && data?.category != EItemCategory.TCG)
                {
                    Logger.LogInfo($"[Bewertungen] {kv.Value}x vermisst: {name} (kein Pack/Display – nicht bestellt)");
                    continue;
                }
                if (RecentlyOrdered(type)) continue;
                int idx = BestRestockIndex(type);
                if (idx < 0) { Logger.LogInfo($"[Bewertungen] {name} vermisst, aber keine Lizenz – kann nicht bestellt werden"); continue; }

                // Liegt schon im Lager (und ist kein Ziel-Set-Pack, das die Opener verbrauchen)? -> nicht kaufen, nur einräumen lassen
                if (!_focusPackTypes.Contains(type))
                {
                    var stockNow = CountItemsInBoxes(out _);
                    CaseCost(idx, out int perCaseNow);
                    if (stockNow.TryGetValue(type, out var inStock) && inStock >= Mathf.Max(1, perCaseNow / 2))
                    {
                        needShelfHelp = true;
                        Logger.LogInfo($"[Bewertungen] {name} vermisst, liegt aber im Lager ({inStock} Stück) – wird eingeräumt statt neu gekauft");
                        continue;
                    }
                }
                float cost = CaseCost(idx, out int perCase);
                int cases = Mathf.Clamp(_reviewCases.Value, 1, Mathf.Max(1, (int)(Budget / (cost * 1.1f))));
                cases = Mathf.Min(cases, (int)(DayLeft() / Mathf.Max(0.01f, cost)));
                if (Budget < cost * 1.1f || cases < 1) break;
                if (BuyRestockIndex(idx, type, cases))
                {
                    MarkOrdered(type); MarkSpent(cost * cases);
                    _statSpent += cost * cases;
                    AddDaySpent(cost * cases);
                    Note($"Kunden vermissen {name} ({kv.Value}x) – {cases} Kartons bestellt (~{GameInstance.GetPriceString(cost * cases)})");
                }
            }

            if (needShelfHelp) UpdateShelfHelper(true);

            // Zu teure Artikel: Aufschlag für diesen Artikel um 5 % senken (Minimum einstellbar)
            foreach (var kv in tooExpensive)
            {
                float cur = _itemMarkup.TryGetValue(kv.Key, out var v) ? v : _priceItemPercent.Value;
                float next = Mathf.Max(_reviewMinMarkup.Value, cur - 5f * kv.Value);
                if (next < cur)
                {
                    _itemMarkup[kv.Key] = next;
                    _priceTimer = 999f; // Preise sofort neu setzen
                    Note($"Kunden finden {InventoryBase.GetItemData(kv.Key)?.GetName() ?? kv.Key.ToString()} zu teuer – Aufschlag {cur:0}% -> {next:0}%");
                }
            }
        }

        // ================= Laden-/Lager-Erweiterungen =================
        // Kauft die nächste Erweiterung, wenn: Level reicht, Maximum nicht erreicht und genug Geld da ist
        // (Budget über der Reserve >= Preis x Faktor – Packs/Sammlung haben weiter genug Geld).
        private float _expTimer = 40f;

        private void HandleExpansions()
        {
            if (!_expEnabled.Value || (_expTimer += 1f) < 60f) return;
            _expTimer = 0f;
            SafeRun("Erweiterungen", ExpansionTick);
        }

        private void ExpansionTick()
        {
            var urm = S<UnlockRoomManager>();
            if (urm == null || LightManager.GetHasDayEnded()) return;
            int level = CPlayerData.m_ShopLevel + 1;
            float factor = Mathf.Max(1f, _expMoneyFactor.Value);

            // 1) Laden-Erweiterung
            int idx = CPlayerData.m_UnlockRoomCount;
            int maxShop = Mathf.Min(_expMaxShop.Value, urm.m_LockedRoomBlockerList?.Count ?? 0, 30);
            if (idx < maxShop)
            {
                int req = Mathf.Clamp(idx * 2 + 1, 2, 60) + idx / 4 * 2;
                float cost = CPlayerData.GetUnlockShopRoomCost(idx);
                if (level >= req && Budget >= cost * factor)
                {
                    BuyExpansion(cost, isWarehouse: false, idx);
                    Note($"Laden-Erweiterung {idx + 1} gekauft ({GameInstance.GetPriceString(cost)}) – mehr Fläche für Regale/Opener");
                    return; // max. 1 pro Runde
                }
            }

            // 2) Lager freischalten
            if (_expWarehouse.Value && !CPlayerData.m_IsWarehouseRoomUnlocked)
            {
                float price = urm.m_ShopB_UnlockPrice;
                if (level >= urm.m_ShopB_UnlockLevelRequired && Budget >= price * factor)
                {
                    PriceChangeManager.AddTransaction(0f - price, ETransactionType.ShopExpansion, 1, -1);
                    Pay(price);
                    urm.SetUnlockWarehouseRoom(true);
                    AchievementManager.OnShopLotBUnlocked();
                    CEventManager.QueueEvent(new CEventPlayer_AddShopExp(Mathf.Clamp(Mathf.RoundToInt(price / 100f), 5, 100)));
                    CPlayerData.m_GameReportDataCollect.upgradeCost -= price;
                    CPlayerData.m_GameReportDataCollectPermanent.upgradeCost -= price;
                    StartCoroutine(SaveShelvesSoon());
                    Note($"Lager (Shop B) freigeschaltet ({GameInstance.GetPriceString(price)})");
                    return;
                }
            }

            // 3) Lager-Erweiterung
            if (_expWarehouse.Value && CPlayerData.m_IsWarehouseRoomUnlocked)
            {
                int widx = CPlayerData.m_UnlockWarehouseRoomCount;
                int maxWh = Mathf.Min(_expMaxWarehouse.Value, urm.m_LockedWarehouseRoomBlockerList?.Count ?? 0, 14);
                if (widx < maxWh)
                {
                    int req = 20 + widx * 5 + widx / 4 * 10;
                    float cost = CPlayerData.GetUnlockWarehouseRoomCost(widx);
                    if (level >= req && Budget >= cost * factor)
                    {
                        BuyExpansion(cost, isWarehouse: true, widx);
                        Note($"Lager-Erweiterung {widx + 1} gekauft ({GameInstance.GetPriceString(cost)}) – mehr Platz für Kartons");
                    }
                }
            }
        }

        // Wie ExpansionShopUIScreen.EvaluateCartCheckout im Spiel
        private void BuyExpansion(float cost, bool isWarehouse, int index)
        {
            var urm = S<UnlockRoomManager>();
            PriceChangeManager.AddTransaction(0f - cost, ETransactionType.ShopExpansion, isWarehouse ? 0 : 1, index);
            Pay(cost);
            if (isWarehouse) urm.StartUnlockNextWarehouseRoom(); else urm.StartUnlockNextRoom();
            CEventManager.QueueEvent(new CEventPlayer_AddShopExp(Mathf.Clamp(Mathf.RoundToInt(cost / 100f), 5, 100)));
            CPlayerData.m_GameReportDataCollect.upgradeCost -= cost;
            CPlayerData.m_GameReportDataCollectPermanent.upgradeCost -= cost;
            _statSpent += cost;
            StartCoroutine(SaveShelvesSoon());
        }

        private System.Collections.IEnumerator SaveShelvesSoon()
        {
            yield return new WaitForSeconds(1f);
            try { S<ShelfManager>()?.SaveInteractableObjectData(); } catch { }
        }

        // ================= Aushilfe Regale =================
        // Vermissen Kunden Artikel, die im Lager liegen, räumt für 10 Min. ein Opener-Arbeiter zusätzlich Regale ein.
        private Worker _shelfHelper;
        private EWorkerTask _shelfHelperOldMain, _shelfHelperOldSide;
        private float _shelfHelperUntil;

        private void UpdateShelfHelper(bool need)
        {
            float now = Time.unscaledTime;
            if (_shelfHelper != null && (!need && now >= _shelfHelperUntil || !_shelfHelper.IsActive() || LightManager.GetHasDayEnded()))
            {
                if (_shelfHelper.IsActive())
                {
                    _shelfHelper.SetTask(_shelfHelperOldMain);
                    _shelfHelper.SetSecondaryTask(_shelfHelperOldSide);
                    Note($"{WorkerName(_shelfHelper)} zurück an {TaskName(_shelfHelperOldMain)}");
                }
                _shelfHelper = null;
            }
            if (!need) return;
            if (_shelfHelper != null) { _shelfHelperUntil = now + 600f; return; }

            var list = WorkerManager.GetWorkerList();
            if (list == null) return;
            var openers = list.Where(w => w != null && w.IsActive() && w.m_PrimaryTask == EWorkerTask.RefillCardOpener && !_borrowed.ContainsKey(w))
                              .OrderBy(w => w.GetWorkerData()?.restockSpeed ?? 99f).ToList();
            if (openers.Count < 2) return; // der beste Opener-Befüller bleibt immer an den Openern
            var pick = openers[openers.Count - 1];
            _shelfHelper = pick;
            _shelfHelperOldMain = pick.m_PrimaryTask;
            _shelfHelperOldSide = pick.m_SecondaryTask;
            _shelfHelperUntil = now + 600f;
            pick.SetTask(EWorkerTask.RestockShelf);
            pick.SetSecondaryTask(EWorkerTask.RefillCardOpener);
            Note($"Kunden vermissen Ware aus dem Lager – {WorkerName(pick)} räumt 10 Min. Regale ein");
        }

        // ================= Arbeiter-Bonus (Geld) =================
        // Wie der "Bonus geben"-Knopf: kostet 1 Tageslohn, max. 3x pro Tag und Arbeiter.
        // Effekt: +1 Level auf alle Aufgaben (je 7 % schneller) und doppelte Erfahrung (levelt dauerhaft schneller).
        private float _bonusTimer;

        private void HandleWorkerBonus()
        {
            if (!_bonusEnabled.Value || (_bonusTimer += 1f) < 20f) return;
            _bonusTimer = 0f;
            SafeRun("Bonus", () =>
            {
                var list = WorkerManager.GetWorkerList();
                if (list == null || LightManager.GetHasDayEnded()) return;
                var given = new List<string>();
                foreach (var w in list)
                {
                    if (w == null || !w.IsActive() || w.m_PrimaryTask == EWorkerTask.Fired || w.m_PrimaryTask == EWorkerTask.GoBackHome) continue;
                    var data = w.GetWorkerData();
                    if (data == null) continue;
                    float cost = data.costPerDay;
                    int n = 0;
                    while (w.GetBonusBoostedCount() < 3 && Budget >= cost && cost > 0f)
                    {
                        PriceChangeManager.AddTransaction(0f - cost, ETransactionType.WorkerSalary, 0);
                        Pay(cost);
                        CPlayerData.m_GameReportDataCollect.employeeCost -= cost;
                        CPlayerData.m_GameReportDataCollectPermanent.employeeCost -= cost;
                        w.GiveSalaryBonus();
                        _statSpent += cost;
                        n++;
                        if (n >= 3) break;
                    }
                    if (n > 0) given.Add($"{WorkerName(w)} +{n}");
                }
                if (given.Count > 0) Note("Bonus gezahlt: " + string.Join(", ", given));
            });
        }

        private void SafeRun(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Logger.LogError($"[{what}] {e}"); }
        }

        private static bool LoadingNextDay()
        {
            var r = S<EndOfDayReportScreen>();
            return r != null && (Traverse.Create(r).Field("m_IsLoadingNextDay").GetValue<bool>() || Traverse.Create(r).Field("m_IsActive").GetValue<bool>());
        }

        // Geld über der Reserve. Abbuchungen laufen über ein Spiel-Ereignis und kommen erst im nächsten Bild an –
        // darum werden eigene, gerade erst ausgegebene Beträge (letzte 3 s) schon abgezogen. Sonst kauft der Bot mit altem Kontostand.
        private double Budget => CPlayerData.m_CoinAmountDouble - Reserve - PendingSpend();

        // Rücklage: "ganzes Geld nutzen" = nur so viel, wie die offenen + die nächsten Tagesrechnungen kosten (nie ins Minus).
        // Sonst die feste Sicherheitsreserve.
        private ConfigEntry<bool> _useAllMoney;
        private float _billsEstimate = 5000f, _billsToday; private int _billsDay = -1;
        private void NoteBillPaid(float amount)
        {
            if (_billsDay != CPlayerData.m_CurrentDay) { _billsDay = CPlayerData.m_CurrentDay; _billsToday = 0f; }
            _billsToday += amount;
            _billsEstimate = Mathf.Max(_billsToday, _billsEstimate * 0.9f);     // folgt steigenden Rechnungen sofort, sinkenden langsam
        }
        private float OpenBills()
        {
            float s = 0f;
            foreach (var t in new[] { EBillType.Rent, EBillType.Electric, EBillType.Employee })
                try { var b = CPlayerData.GetBill(t); if (b != null && b.amountToPay > 0f) s += b.amountToPay; } catch { }
            return s;
        }
        private float Reserve => _useAllMoney != null && _useAllMoney.Value
            ? OpenBills() + _billsEstimate * 1.5f                                  // offene Rechnungen + nächster Tag mit Puffer
            : _colReserve.Value;
        private readonly List<KeyValuePair<float, float>> _pending = new List<KeyValuePair<float, float>>();
        private float PendingSpend()
        {
            float now = Time.unscaledTime;
            _pending.RemoveAll(p => now - p.Key > 3f);
            float s = 0f; foreach (var p in _pending) s += p.Value; return s;
        }
        private void MarkSpent(float v) { if (v > 0f) _pending.Add(new KeyValuePair<float, float>(Time.unscaledTime, v)); }
        private void Pay(float v) { CEventManager.QueueEvent(new CEventPlayer_ReduceCoin(v)); MarkSpent(v); }

        // ================= Collection-Tracker-Zugriff (per Reflection) =================
        private static Type _ctShopType, _ctOrderType;

        private bool CtReady()
        {
            if (_ctShopType == null) _ctShopType = AccessTools.TypeByName("CollectionTracker.CTRestockShop");
            if (_ctOrderType == null) _ctOrderType = AccessTools.TypeByName("CollectionTracker.CTSetOrdering");
            if (_ctShopType == null || _ctOrderType == null)
            {
                if (!_ctWarned) { _ctWarned = true; Logger.LogWarning("Collection Tracker nicht gefunden – Sammel-Funktionen aus."); ModMissing("Collection Tracker", "Sammlung / Pack-Kauf"); }
                return false;
            }
            return true;
        }

        private object CtScreen() => AccessTools.Property(_ctShopType, "Screen")?.GetValue(null, null);

        private struct SetInfo
        {
            public ECardExpansionType Type; public string Name; public int Have, Total; public bool Complete; public string Shop;
        }

        private List<SetInfo> GetSets()
        {
            var result = new List<SetInfo>();
            var screen = CtScreen();
            if (screen == null) return result;
            var rows = AccessTools.Method(screen.GetType(), "TabletAllExpansions")?.Invoke(screen, null) as IEnumerable;
            if (rows == null) return result;
            foreach (var row in rows)
            {
                var t = Traverse.Create(row);
                result.Add(new SetInfo
                {
                    Type = t.Field("Type").GetValue<ECardExpansionType>(),
                    Name = t.Field("Name").GetValue<string>(),
                    Have = t.Field("Have").GetValue<int>(),
                    Total = t.Field("Total").GetValue<int>(),
                    Complete = t.Field("State").GetValue()?.ToString() == "Complete",
                    Shop = (t.Field("Shop").GetValue<string>() ?? "") + " " + (t.Field("ShopId").GetValue<string>() ?? "")
                });
            }
            return result;
        }

        private int GetBiggestBox(ECardExpansionType exp)
        {
            var m = AccessTools.Method(_ctOrderType, "TryGetBiggestUnlockedBox");
            if (m == null) return -1;
            var args = new object[] { exp, -1, null, null };
            bool ok = (bool)m.Invoke(null, args);
            return ok ? (int)args[1] : -1;
        }

        // Pack-Sorten pro Set ändern sich im Spiel nicht -> einmal ermitteln und merken
        private readonly Dictionary<ECardExpansionType, List<EItemType>> _packTypeCache = new Dictionary<ECardExpansionType, List<EItemType>>();

        private List<EItemType> GetSetPackTypes(ECardExpansionType exp)
        {
            if (_packTypeCache.TryGetValue(exp, out var cached)) return cached;
            var list = new List<EItemType>();
            if (AccessTools.Method(_ctOrderType, "GetSetBoxIndices")?.Invoke(null, new object[] { exp }) is List<int> idx)
                foreach (var i in idx)
                {
                    var rd = InventoryBase.GetRestockData(i);
                    if (rd != null && !list.Contains(rd.itemType)) list.Add(rd.itemType);
                }
            if (list.Count > 0) _packTypeCache[exp] = list;
            return list;
        }

        private bool PurchaseSet(ECardExpansionType exp, int cases, out string msg)
        {
            msg = "";
            var res = AccessTools.Method(_ctOrderType, "PurchaseSetInstantly")?.Invoke(null, new object[] { exp, cases });
            if (res == null) return false;
            var t = Traverse.Create(res);
            msg = t.Field("Message").GetValue<string>();
            return t.Field("Status").GetValue()?.ToString() == "Added";
        }

        // ================= Lager zählen =================
        private static Dictionary<EItemType, int> CountItemsInBoxes(out int boxCount)
        {
            var d = new Dictionary<EItemType, int>();
            boxCount = 0;
            var rm = S<RestockManager>();
            if (rm == null) return d;
            foreach (var box in rm.m_ItemPackagingBoxList)
            {
                if (box == null || !box.gameObject.activeInHierarchy || box.m_ItemCompartment == null) continue;
                if (box.m_IsStored) continue;                         // eingelagerte zählen unten über die Lager-Liste
                int n = box.m_ItemCompartment.GetItemCount();
                if (n <= 0) continue;
                boxCount++;
                var type = box.m_ItemCompartment.GetItemType();
                d[type] = (d.TryGetValue(type, out var c) ? c : 0) + n;
            }
            // Kartons im Lagerregal: das Spiel speichert sie nur als Einträge (StoredBoxRecord: itemType, amount)
            try
            {
                var sm = S<ShelfManager>();
                if (sm != null)
                    foreach (var ws in sm.m_WarehouseShelfList)
                    {
                        if (ws == null) continue;
                        var comps = Traverse.Create(ws).Field("m_ItemCompartmentList").GetValue<List<ShelfCompartment>>();
                        if (comps == null) continue;
                        foreach (var comp in comps)
                        {
                            if (comp == null) continue;
                            var recs = Traverse.Create(comp).Field("m_StoredBoxRecordList").GetValue() as IList;
                            if (recs == null) continue;
                            foreach (var r in recs)
                            {
                                var tr = Traverse.Create(r);
                                var type = tr.Field("itemType").GetValue<EItemType>();
                                int n = tr.Field("amount").GetValue<int>();
                                if (n <= 0 || type == EItemType.None) continue;
                                d[type] = (d.TryGetValue(type, out var c) ? c : 0) + n;
                            }
                        }
                    }
            }
            catch { }
            return d;
        }

        // ================= Notfall-Nachkauf: Opener-Arbeiter dürfen nie ohne Packs dastehen =================
        private ConfigEntry<bool> _emergencyEnabled;
        private ConfigEntry<float> _emergencyFills, _emergencyExtraLimit;
        private float _emergencyTimer, _emergencyUntil = -1f, _emergencySpent; private int _emergencyDay = -1;
        private bool EmergencyActive => Time.unscaledTime < _emergencyUntil;

        private void BindEmergencyConfig()
        {
            _emergencyEnabled = Config.Bind("7. Sammlung", "Notfall-Nachkauf wenn Packs ausgehen", true,
                "Prüft alle 10 s den Pack-Vorrat unfertiger Sets (Boden + Lager + unterwegs). Fällt er unter die eingestellten Opener-Füllungen, wird sofort viel nachbestellt – auch über das Tageslimit hinaus (bis zum Notfall-Limit).");
            _emergencyFills = Config.Bind("7. Sammlung", "Notfall ab weniger als X Opener-Füllungen", 1f,
                "1 = sobald weniger Packs da sind, als alle Opener zusammen fassen.");
            _emergencyExtraLimit = Config.Bind("7. Sammlung", "Notfall zusätzliches Limit pro Tag ($)", 100000f,
                "So viel darf der Notfall-Nachkauf pro Tag zusätzlich zum normalen Tageslimit ausgeben.");
        }

        private float EmergencyLeft()
        {
            if (_emergencyDay != CPlayerData.m_CurrentDay) { _emergencyDay = CPlayerData.m_CurrentDay; _emergencySpent = 0f; }
            return Mathf.Max(0f, _emergencyExtraLimit.Value - _emergencySpent);
        }

        // Wie viel darf jetzt ausgegeben werden: Tageslimit, im Notfall zusätzlich das Notfall-Limit
        private float BuyLeft() => EmergencyActive ? DayLeft() + EmergencyLeft() : DayLeft();

        private void AddBuySpent(float v)
        {
            float normal = Mathf.Min(v, DayLeft());
            AddDaySpent(v);
            if (EmergencyActive && v > normal) { EmergencyLeft(); _emergencySpent += v - normal; }
        }

        private void HandlePackEmergency()
        {
            if (!_emergencyEnabled.Value || !_colEnabled.Value || (_emergencyTimer += 1f) < 10f) return;
            _emergencyTimer = 0f;
            if (EmergencyActive || _openerPackTypes.Count == 0 || LightManager.GetHasDayEnded()) return;
            SafeRun("Notfall-Nachkauf", () =>
            {
                int cap = 0;
                var ops = ShelfManager.GetAutoPackOpenerList();
                if (ops != null) foreach (var o in ops) if (o != null) cap += Mathf.Max(0, o.m_MaxPackCount);
                if (cap <= 0) return;
                var stock = CountItemsInBoxes(out _);
                var coming = CountItemsInDelivery();
                int have = _openerPackTypes.Sum(t => (stock.TryGetValue(t, out var a) ? a : 0) + (coming.TryGetValue(t, out var b) ? b : 0));
                int need = Mathf.RoundToInt(cap * Mathf.Max(0.1f, _emergencyFills.Value));
                if (have >= need) return;
                _emergencyUntil = Time.unscaledTime + 30f;
                Note($"Notfall-Nachkauf: nur noch {have} Packs für die Opener (Ziel mind. {need}) – bestelle sofort");
                _colTimer = 60f;                                       // Sammlung gleich im nächsten Takt
            });
        }

        // Bestellungen, die noch unterwegs sind: nicht sofort nochmal bestellen
        private readonly Dictionary<EItemType, float> _orderedAt = new Dictionary<EItemType, float>();
        private const float OrderCooldown = 600f; // 10 Minuten Echtzeit

        private bool RecentlyOrdered(EItemType t) =>
            _orderedAt.TryGetValue(t, out var at) && Time.unscaledTime - at < OrderCooldown;

        private void MarkOrdered(EItemType t) => _orderedAt[t] = Time.unscaledTime;

        // Kartons, die das Spiel noch ausliefern muss (Warteschlange)
        private static Dictionary<EItemType, int> CountItemsInDelivery()
        {
            var d = new Dictionary<EItemType, int>();
            try
            {
                var idx = CPlayerData.m_SpawnBoxRestockIndexWaitingList;
                var cnt = CPlayerData.m_SpawnBoxItemCountWaitingList;
                for (int i = 0; idx != null && cnt != null && i < idx.Count && i < cnt.Count; i++)
                {
                    var rd = InventoryBase.GetRestockData(idx[i]);
                    if (rd == null) continue;
                    int n = cnt[i] * RestockManager.GetMaxItemCountInBox(rd.itemType, rd.isBigBox);
                    d[rd.itemType] = (d.TryGetValue(rd.itemType, out var c) ? c : 0) + n;
                }
            }
            catch { }
            return d;
        }

        private static float CaseCost(int restockIndex, out int perCase)
        {
            var rd = InventoryBase.GetRestockData(restockIndex);
            perCase = RestockManager.GetMaxItemCountInBox(rd.itemType, rd.isBigBox);
            return CPlayerData.GetItemCost(rd.itemType) * perCase;
        }

        // ================= Sammlung =================
        private readonly HashSet<EItemType> _focusPackTypes = new HashSet<EItemType>();
        private readonly HashSet<EItemType> _openerPackTypes = new HashSet<EItemType>();
        private ConfigEntry<float> _cashTurbo;
        private string _focusText = "-";

        private void CollectionTick()
        {
            if (!CtReady()) return;
            var screen = CtScreen();
            if (screen == null) return;

            // 1) Boost abholen
            if (_colClaimBoost.Value)
            {
                var claim = AccessTools.Method(screen.GetType(), "TabletClaimReward");
                if (claim != null && (bool)claim.Invoke(screen, new object[] { "PackMachineBoost" }))
                    Note("Pack-Automaten-Boost: nächste Stufe abgeholt! (schneller + mehr Kapazität)");
            }

            // 2) Ziel-Sets: unfertig + kaufbar, fast fertige zuerst
            var candidates = new List<KeyValuePair<SetInfo, int>>();
            var sets = GetSets();
            int incomplete = 0, errors = 0;
            var noBox = new List<string>();
            foreach (var s in sets)
            {
                if (s.Complete || s.Total <= 0 || s.Have >= s.Total) continue;
                if (_colPokemonOnly.Value && s.Shop.IndexOf("pok", StringComparison.OrdinalIgnoreCase) < 0) continue; // nur Pokémon-Sets
                incomplete++;
                int box;
                try { box = GetBiggestBox(s.Type); } catch { errors++; continue; }
                if (box < 0) { noBox.Add(s.Name); continue; }
                candidates.Add(new KeyValuePair<SetInfo, int>(s, box));
            }
            Logger.LogInfo($"[Sammlung] {sets.Count} Sets gelesen, {incomplete} unfertig, {candidates.Count} kaufbar, {errors} Fehler" +
                           (noBox.Count > 0 ? $" | ohne kaufbare Packs: {string.Join(", ", noBox.Take(8))}{(noBox.Count > 8 ? " ..." : "")}" : "") +
                           $" | Budget: {GameInstance.GetPriceString((float)Budget)}");
            // Kaufstrategie:
            //  1) wenigste fehlende Karten zuerst (schnell fertig -> Boost-Stufen)
            //  2) bei Gleichstand: günstigerer Preis pro Pack
            //  3) Anzahl Ziel-Sets wächst mit dem Budget (je $X über der Reserve ein Set mehr)
            float PackPrice(int restockIdx) { float c = CaseCost(restockIdx, out int per); return per > 0 ? c / per : 999f; }
            int extra = _colBudgetPerExtraSet.Value > 0 ? (int)(Budget / _colBudgetPerExtraSet.Value) : 0;
            int focusCount = Mathf.Clamp(_colFocusSets.Value + extra, 1, Mathf.Max(1, _colMaxFocusSets.Value));
            // Fast fertige Sets (nur noch 1–3 seltene Karten) nicht endlos weiterkaufen: Deckel pro fehlender Karte
            var allCandidates = candidates.ToList();   // Opener öffnen auch Packs pausierter Sets (schon gekauft)
            var capped = new List<string>();
            candidates = candidates.Where(c =>
            {
                int miss = c.Key.Total - c.Key.Have;
                if (miss > 3 || _colPacksPerMissing.Value <= 0) return true;
                int boughtPacks = _setPacksBought.TryGetValue(c.Key.Name, out var bp) ? bp : 0;
                if (boughtPacks < _colPacksPerMissing.Value * miss) return true;
                capped.Add($"{c.Key.Name} ({miss} fehlen, {boughtPacks} Packs gekauft)");
                return false;
            }).ToList();
            if (capped.Count > 0 && Time.unscaledTime >= _cappedLogAt)
            {
                _cappedLogAt = Time.unscaledTime + 1800f;
                Note($"Sammlung: fast fertige Sets pausiert (fehlende Karten lieber von Kunden ankaufen): {string.Join(", ", capped.Take(5))}");
            }

            // Effizienz: Anteil fehlender Karten pro Dollar Packpreis = wie viele NEUE Karten bringt ein Dollar ungefähr
            //  (Alternative: fast fertige Sets zuerst – für die Boost-Stufen des Collection Trackers)
            List<KeyValuePair<SetInfo, int>> focus;
            if (_colEfficiency.Value)
                focus = candidates.OrderByDescending(c => (c.Key.Total - c.Key.Have) / (float)Mathf.Max(1, c.Key.Total) / Mathf.Max(0.01f, PackPrice(c.Value)))
                                  .Take(focusCount).ToList();
            else
                focus = candidates.OrderBy(c => c.Key.Total - c.Key.Have)
                                  .ThenBy(c => PackPrice(c.Value))
                                  .Take(focusCount).ToList();

            _focusPackTypes.Clear();
            foreach (var f in focus) foreach (var t in GetSetPackTypes(f.Key.Type)) _focusPackTypes.Add(t);
            // Opener-Arbeiter: Packs ALLER unfertigen (kaufbaren) Pokémon-Sets – nie von fertigen Sets
            _openerPackTypes.Clear();
            foreach (var c in allCandidates) foreach (var t in GetSetPackTypes(c.Key.Type)) _openerPackTypes.Add(t);
            _focusText = focus.Count == 0 ? "-" : string.Join(", ", focus.Select(f => $"{f.Key.Name} {f.Key.Have}/{f.Key.Total}"));
            _dashFocus = focus.Select(f => (f.Key.Name, f.Key.Have, f.Key.Total)).ToList();
            _dashSetsTotal = sets.Count;
            _dashSetsDone = sets.Count(s => s.Complete || (s.Total > 0 && s.Have >= s.Total));
            _dashSetsBuyable = candidates.Count;
            _setsWithoutPacks = noBox.ToList();   // für Lizenzen: diese Sets brauchen zuerst freigeschaltete Packs

            // 3) Opener-Arbeiter auf Ziel-Packs einstellen
            if (_colSetWorkerPacks.Value && _openerPackTypes.Count > 0) ApplyWorkerPackTypes();

            // 4) Nachkaufen
            var stock = CountItemsInBoxes(out int boxes);
            var coming = CountItemsInDelivery();

            // Vorrat aus Opener-Kapazität berechnen: alle Automaten x Packs pro Automat x Füllungen
            int openerCapacity = 0;
            var openerList = ShelfManager.GetAutoPackOpenerList();
            if (openerList != null) foreach (var o in openerList) if (o != null) openerCapacity += Mathf.Max(0, o.m_MaxPackCount);
            // Viel Geld übrig -> größerer Vorrat (+1 Füllung je $X Budget, max. eingestellt)
            float fills = Mathf.Max(0.5f, _colFills.Value);
            if (_colBudgetPerExtraFill.Value > 0) fills = Mathf.Min(_colMaxFills.Value, fills + (float)(Budget / _colBudgetPerExtraFill.Value));
            int totalTarget = Mathf.Max(64 * 2, Mathf.RoundToInt(openerCapacity * fills));
            if (_maxStockPacks.Value > 0) totalTarget = Mathf.Min(totalTarget, Mathf.Max(_maxStockPacks.Value, openerCapacity));   // nie weniger als eine volle Opener-Füllung
            int targetPerSet = Mathf.Max(64, totalTarget / Mathf.Max(1, focus.Count));
            _dashStockTarget = totalTarget;
            _dashStockHave = focus.Sum(f => GetSetPackTypes(f.Key.Type).Sum(t => (stock.TryGetValue(t, out var c) ? c : 0) + (coming.TryGetValue(t, out var d) ? d : 0)));
            Logger.LogInfo($"[Sammlung] Opener: {openerList?.Count ?? 0} Stück, {openerCapacity} Packs Kapazität -> Ziel-Vorrat {totalTarget} Packs ({targetPerSet} pro Set)");

            foreach (var f in focus)
            {
                if (boxes >= _colMaxBoxesOnFloor.Value) { Note($"Sammlung: {boxes} volle Kartons im Laden (Limit {_colMaxBoxesOnFloor.Value}) – warte"); break; }
                float cost = CaseCost(f.Value, out int perCase);
                var setTypes = GetSetPackTypes(f.Key.Type);
                // Lieferung noch unterwegs -> nicht nochmal; im Notfall nur warten, wenn wirklich etwas von diesem Set unterwegs ist
                if (EmergencyActive ? setTypes.Any(t => coming.TryGetValue(t, out var cm) && cm > 0) : setTypes.Any(RecentlyOrdered)) continue;
                int have = setTypes.Sum(t => (stock.TryGetValue(t, out var c) ? c : 0) + (coming.TryGetValue(t, out var d) ? d : 0));
                if (have >= targetPerSet)
                {
                    Logger.LogInfo($"[Sammlung] {f.Key.Name}: genug Vorrat ({have}/{targetPerSet} Packs)");
                    continue;
                }
                // Große Mengen: auf den berechneten Vorrat auffüllen, max. X Kartons pro Bestellung, so viel das Budget hergibt
                int missingCases = Mathf.CeilToInt((targetPerSet - have) / (float)Mathf.Max(1, perCase));
                int cases = Mathf.Min(Mathf.Max(1, _colCasesPerBuy.Value), Mathf.Max(1, missingCases));
                cases = Mathf.Min(cases, (int)(Budget / (cost * 1.1f)));
                cases = Mathf.Min(cases, (int)(BuyLeft() / Mathf.Max(0.01f, cost)));
                if (cases < 1 && BuyLeft() < cost) { Note($"Tageslimit erreicht ({GameInstance.GetPriceString(DayLimit)}) – morgen geht es weiter"); break; }
                if (cases < 1) { Note($"Sammlung: Budget reicht nicht für {f.Key.Name} ({GameInstance.GetPriceString(cost)})"); break; }

                if (PurchaseSet(f.Key.Type, cases, out var msg))
                {
                    foreach (var st in setTypes) MarkOrdered(st);
                    MarkSpent(cost * cases);
                    boxes += cases;
                    _statSpent += cost * cases;
                    AddBuySpent(cost * cases);
                    _statPackCases += cases;
                    CountSetPacks(f.Key.Name, cases * perCase);
                    Note($"Gekauft: {cases} Kartons {f.Key.Name} ({cases * perCase} Packs, ~{GameInstance.GetPriceString(cost * cases)}) – Set {f.Key.Have}/{f.Key.Total}");
                }
                else Logger.LogInfo($"Kauf {f.Key.Name} nicht möglich: {msg}");
            }
        }

        private void ApplyWorkerPackTypes()
        {
            var workers = WorkerManager.GetWorkerList();
            if (workers == null) return;
            foreach (var w in workers)
            {
                if (w == null || !w.IsActive()) continue;
                if (w.m_PrimaryTask != EWorkerTask.RefillCardOpener && w.m_SecondaryTask != EWorkerTask.RefillCardOpener) continue;
                var types = Traverse.Create(w).Field("m_CardPackItemTypeList").GetValue<List<EItemType>>();
                var enabled = w.GetCardPackItemTypeEnabledList();
                if (types == null || enabled == null) continue;
                bool changed = false;
                for (int i = 0; i < types.Count && i < enabled.Count; i++)
                {
                    bool want = _openerPackTypes.Contains(types[i]);
                    if (enabled[i] != want) { w.SetCardPackItemTypeEnabled(i, want); changed = true; }
                }
                if (changed) Note($"{WorkerName(w)} befüllt Opener jetzt nur mit Ziel-Sets: {_focusText}");
            }
        }

        // ================= Kartenablage (Doppelte) =================
        private void BulkTick()
        {
            if (!CtReady()) return;
            var shelves = ShelfManager.GetCardStorageShelfList();
            if (shelves == null || shelves.Count == 0) return;
            var valid = shelves.Where(s => s != null && s.IsValidObject() && s.CanWorkerTake() && !s.IsEditingBulkBox()).ToList();
            if (valid.Count == 0) return;
            var target = valid.FirstOrDefault(s => s.GetTotalCardAmount() < s.GetBoxTotalCardCountMax());

            // 1) Gegradete Karten zuerst verkaufen (PSA 10 nur Duplikate – eine bleibt immer)
            if (_gradedSell.Value)
            {
                int need = SellableGradedPoints();
                if (need > 0)
                {
                    // Ablage voll? Billigste normale Doppelte zurück ins Album, damit die gegradeten Platz haben
                    var gt = target ?? valid[0];
                    int free = gt.GetBoxTotalCardCountMax() - gt.GetTotalCardAmount();
                    if (free < need)
                    {
                        int back = ReturnCheapCardsToAlbum(gt, Mathf.Min(need - free, 5000), out float backValue);
                        if (back > 0) Note($"Kartenablage voll: {back} billige Doppelte (~{GameInstance.GetPriceString(backValue)}) zurück ins Album – Platz für gegradete Karten");
                    }
                    target = gt;
                }
            }
            if (target == null) return;

            int room = target.GetBoxTotalCardCountMax() - target.GetTotalCardAmount();
            int budget = Mathf.Min(room, _bulkMaxPerRun.Value);
            var list = target.GetCompactCardDataAmountList();

            if (_gradedSell.Value)
            {
                int g = MoveGradedToStorage(list, Mathf.Max(0, target.GetBoxTotalCardCountMax() - target.GetTotalCardAmount()), out float gValue);
                if (g > 0)
                {
                    target.SetCompactCardDataAmountList(list);
                    S<InteractionPlayerController>()?.m_CollectionBinderFlipAnimCtrl?.SetCanUpdateSort(true);
                    _statBulk += g;
                    Note($"Kartenablage: {g} gegradete Karten zum Verkauf eingelegt (Wert ~{GameInstance.GetPriceString(gValue)})");
                }
                // Solange noch gegradete Karten in der Ablage warten, keine normalen Karten dazu -> Vitrinen zeigen zuerst gegradete
                if (list.Any(e => e.gradedCardIndex > 0)) return;
            }

            // Normale Doppelte: verteilt über mehrere Bilder (ein Set pro Bild), damit nichts ruckelt
            if (!_bulkRunning) StartCoroutine(BulkRawRoutine(target, list, budget));
        }

        private bool _bulkRunning;

        private System.Collections.IEnumerator BulkRawRoutine(InteractableCardStorageShelf target, List<CompactCardDataAmount> list, int budget)
        {
            _bulkRunning = true;
            int moved = 0; float value = 0f;
            List<SetInfo> sets;
            try { sets = GetSets(); } catch { _bulkRunning = false; yield break; }
            float bulkLimit = _gradeEnabled.Value ? Mathf.Min(_bulkMaxValue.Value, _gradeMinValue.Value) : _bulkMaxValue.Value;

            foreach (var set in sets)
            {
                if (budget <= 0 || target == null) break;
                try
                {
                    foreach (bool dim in set.Type == ECardExpansionType.Ghost ? new[] { false, true } : new[] { false })
                    {
                        var collected = CPlayerData.GetCardCollectedList(set.Type, dim);
                        int count = collected != null ? collected.Count : set.Total;
                        for (int i = 0; i < count && budget > 0; i++)
                        {
                            CardData cd;
                            try { cd = CPlayerData.GetCardData(i, set.Type, dim); } catch { continue; }
                            if (cd == null || cd.cardGrade > 0) continue;
                            int amt = CPlayerData.GetCardAmount(cd);
                            if (amt <= 1) continue;
                            float price = CPlayerData.GetCardMarketPrice(cd);
                            if (price <= 0f || price >= bulkLimit) continue;
                            int n = Mathf.Min(amt - 1, budget);
                            CPlayerData.ReduceCard(cd, n);
                            AddToCompact(list, cd, n);
                            budget -= n; moved += n; value += price * n;
                        }
                    }
                }
                catch (Exception e) { Logger.LogWarning($"[Kartenablage] {set.Name}: {e.Message}"); }
                yield return null; // nächstes Set im nächsten Bild
            }

            if (moved > 0 && target != null)
            {
                target.SetCompactCardDataAmountList(list);
                S<InteractionPlayerController>()?.m_CollectionBinderFlipAnimCtrl?.SetCanUpdateSort(true);
                _statBulk += moved;
                Note($"Kartenablage: {moved} doppelte Karten (Wert ~{GameInstance.GetPriceString(value)}) eingelegt");
            }
            _bulkRunning = false;
        }

        // Gegradete Karten aus dem Album in die Kartenablage (wie im Spiel-Menü: RemoveGradedCard + Eintrag mit Grade/Index)
        // Platzbedarf (Ablage zählt gegradete mit ihrer Note) aller gegradeten Karten, die verkauft werden dürfen
        private int SellableGradedPoints()
        {
            var inv = CPlayerData.m_GradedCardInventoryList;
            if (inv == null || inv.Count == 0) return 0;
            string Key(CompactCardDataAmount c) => $"{c.expansionType}|{c.cardSaveIndex}|{c.isDestiny}";
            var tens = new Dictionary<string, int>();
            foreach (var c in inv) if (c.amount >= 10) tens[Key(c)] = (tens.TryGetValue(Key(c), out var n) ? n : 0) + 1;
            int pts = 0;
            foreach (var c in inv)
            {
                if (c.amount <= 0) continue;
                if (c.amount >= 10 && _gradedKeepTen.Value) { if (tens[Key(c)] <= 1) continue; tens[Key(c)]--; }
                pts += c.amount;
            }
            return pts;
        }

        // Billigste normale Karten aus der Ablage zurück ins Album (sie bleiben Doppelte und können später wieder rein)
        private int ReturnCheapCardsToAlbum(InteractableCardStorageShelf shelf, int points, out float value)
        {
            value = 0f;
            var list = shelf.GetCompactCardDataAmountList();
            if (list == null || points <= 0) return 0;
            var normal = new List<(CompactCardDataAmount e, CardData cd, float price)>();
            foreach (var e in list)
            {
                if (e == null || e.gradedCardIndex > 0 || e.amount <= 0) continue;
                CardData cd;
                try { cd = CPlayerData.GetCardData(e.cardSaveIndex, e.expansionType, e.isDestiny); } catch { continue; }
                if (cd == null) continue;
                normal.Add((e, cd, CPlayerData.GetCardMarketPrice(cd)));
            }
            int moved = 0;
            foreach (var n in normal.OrderBy(x => x.price))
            {
                if (points <= 0) break;
                int take = Mathf.Min(n.e.amount, points);
                CPlayerData.AddCard(n.cd, take);
                n.e.amount -= take;
                points -= take; moved += take; value += n.price * take;
            }
            list.RemoveAll(e => e != null && e.gradedCardIndex <= 0 && e.amount <= 0);
            shelf.SetCompactCardDataAmountList(list);
            S<InteractionPlayerController>()?.m_CollectionBinderFlipAnimCtrl?.SetCanUpdateSort(true);
            return moved;
        }

        private int MoveGradedToStorage(List<CompactCardDataAmount> storage, int room, out float value)
        {
            value = 0f;
            var inv = CPlayerData.m_GradedCardInventoryList;
            if (inv == null || inv.Count == 0) return 0;

            // Wie viele Grade-10-Kopien gibt es pro Karte?
            string Key(CompactCardDataAmount c) => $"{c.expansionType}|{c.cardSaveIndex}|{c.isDestiny}";
            var tens = new Dictionary<string, int>();
            foreach (var c in inv) if (c.amount >= 10) tens[Key(c)] = (tens.TryGetValue(Key(c), out var n) ? n : 0) + 1;

            int moved = 0;
            foreach (var c in inv.ToList())
            {
                if (c.amount <= 0) continue;
                if (c.amount > room) continue; // passt nicht mehr (gegradete zählen mit ihrer Note) – vielleicht eine niedrigere Note
                if (c.amount >= 10 && _gradedKeepTen.Value)
                {
                    // PSA 10: nur verkaufen, wenn danach noch mindestens eine übrig ist
                    if (tens[Key(c)] <= 1) continue;
                    tens[Key(c)]--;
                }
                var cd = CPlayerData.GetGradedCardData(c);
                float price = CPlayerData.GetCardMarketPrice(cd);
                CPlayerData.RemoveGradedCard(cd);
                storage.Add(new CompactCardDataAmount
                {
                    expansionType = c.expansionType, cardSaveIndex = c.cardSaveIndex, isDestiny = c.isDestiny,
                    gradedCardIndex = c.gradedCardIndex, amount = c.amount
                });
                value += price; moved++;
                room -= c.amount;
            }
            return moved;
        }

        private static void AddToCompact(List<CompactCardDataAmount> list, CardData cd, int n)
        {
            int idx = CPlayerData.GetCardSaveIndex(cd);
            foreach (var e in list)
            {
                if (e.gradedCardIndex <= 0 && e.cardSaveIndex == idx && e.expansionType == cd.expansionType && e.isDestiny == cd.isDestiny)
                { e.amount += n; return; }
            }
            list.Add(new CompactCardDataAmount { cardSaveIndex = idx, expansionType = cd.expansionType, isDestiny = cd.isDestiny, amount = n });
        }

        // ================= Arbeiter einteilen (Einsatzplan nach Stärken) =================
        private string _lastPlanKey;
        private ConfigEntry<int> _planCashiers, _planDisplays, _planShelves;

        private void BindWorkerPlanConfig()
        {
            _planCashiers = Config.Bind("9. Arbeiter", "Anzahl Kassierer (0 bedeutet so viele wie Kassen)", 0, "Haupt- UND Nebenrolle Kasse. Die mit der besten Kassen-Geschwindigkeit (inkl. Level).");
            _planDisplays = Config.Bind("9. Arbeiter", "Vitrinen-Befüller (Bot füllt selbst)", 0, "Arbeiter für Vitrinen. 0 = der Bot füllt die Vitrinen selbst.");
            _planShelves = Config.Bind("9. Arbeiter", "Regal-Einräumer (Bot räumt selbst ein)", 0, "Arbeiter für Regale. 0 = der Bot räumt selbst ein. Alle übrigen Arbeiter befüllen die Pack-Opener (Haupt- UND Nebenrolle).");
            _cashTurbo = Config.Bind("5. Kasse", "Kassen-Turbo (Scan-Tempo x)", 2f, "Kassierer scannen so viel schneller (1 = normal). Wirkt wie eine zweite Person an jeder Kasse.");
        }

        private static string TaskName(EWorkerTask t)
        {
            switch (t)
            {
                case EWorkerTask.ManCounter: return "Kasse";
                case EWorkerTask.RefillCardOpener: return "Opener";
                case EWorkerTask.RestockShelf: return "Regale";
                case EWorkerTask.RestockCardDisplay: return "Vitrinen";
                case EWorkerTask.SetPrice: return "Preise";
                case EWorkerTask.RefillCleanser: return "Reiniger";
                default: return t.ToString();
            }
        }

        private void WorkerTick()
        {
            var all = WorkerManager.GetWorkerList();
            if (all == null || _borrowed.Count > 0 || LightManager.GetHasDayEnded()) return;
            var ws = all.Where(w => w != null && w.IsActive()
                                    && w.m_PrimaryTask != EWorkerTask.Fired && w.m_PrimaryTask != EWorkerTask.GoBackHome).ToList();
            if (ws.Count == 0) return;

            // Nur neu planen, wenn ein neuer Tag ist oder sich die Belegschaft ändert (deine Änderungen tagsüber bleiben)
            string key = CPlayerData.m_CurrentDay + "|" + _autoCheckout.Value + "|" + string.Join(",", ws.Select(w => w.m_WorkerIndex).OrderBy(i => i));
            if (key == _lastPlanKey) return;
            _lastPlanKey = key;

            // Echte Geschwindigkeit pro Aufgabe = Grundwert x Level-Bonus (jede Stufe 7 % schneller). Kleiner = schneller.
            float Lvl(Worker w, EWorkerTask task)
            {
                int l = (w.m_TaskLevel != null && w.m_TaskLevel.Count > (int)task) ? w.m_TaskLevel[(int)task] : 0;
                return Mathf.Max(0.1f, 1f - l * 0.07f);
            }
            float Checkout(Worker w) => (w.GetWorkerData()?.checkoutSpeed ?? 99f) * Lvl(w, EWorkerTask.ManCounter);
            float Restock(Worker w, EWorkerTask task) => (w.GetWorkerData()?.restockSpeed ?? 99f) * Lvl(w, task);
            float maxC = ws.Max(Checkout), minC = ws.Min(Checkout);
            float maxR = ws.Max(w => Restock(w, EWorkerTask.RefillCardOpener)), minR = ws.Min(w => Restock(w, EWorkerTask.RefillCardOpener));
            float CS(Worker w) => maxC > minC ? (maxC - Checkout(w)) / (maxC - minC) : 0.5f;                                          // 0..1 Kasse
            float RS(Worker w) => maxR > minR ? (maxR - Restock(w, EWorkerTask.RefillCardOpener)) / (maxR - minR) : 0.5f;           // 0..1 Einräumen

            var free = new List<Worker>(ws);
            var plan = new Dictionary<Worker, (EWorkerTask main, EWorkerTask side)>();
            bool hasOpeners = (ShelfManager.GetAutoPackOpenerList()?.Count ?? 0) > 0;
            bool hasStorage = (ShelfManager.GetCardStorageShelfList()?.Count ?? 0) > 0;
            int counters = S<ShelfManager>()?.m_CashierCounterList?.Count(c => c != null) ?? 1;

            // 1) Kassierer: so viele wie Kassen (oder eingestellt), Haupt- + Nebenrolle Kasse
            //    Bot-Kasse an -> keine Kassierer, der Bot kassiert selbst an allen Kassen
            int wantCash = _planCashiers.Value > 0 ? _planCashiers.Value : Mathf.Max(1, counters);
            int nCash = _autoCheckout.Value ? 0 : Mathf.Clamp(wantCash, ws.Count >= 2 ? 1 : 0, Mathf.Max(0, free.Count - 1));
            foreach (var w in free.OrderByDescending(w => CS(w) - 0.3f * RS(w)).Take(nCash).ToList())
            { plan[w] = (EWorkerTask.ManCounter, EWorkerTask.ManCounter); free.Remove(w); }

            // 2) Vitrinen und Regale bekommen je feste Anzahl – der REST (die schnellsten Einräumer) geht an die Opener
            int nDisp = hasStorage ? Mathf.Min(_planDisplays.Value, free.Count) : 0;
            int nShelf = Mathf.Min(_planShelves.Value, Mathf.Max(0, free.Count - nDisp));
            int nOpen = hasOpeners ? Mathf.Max(0, free.Count - nDisp - nShelf) : 0;
            if (hasOpeners && nOpen == 0 && free.Count > 0) { nOpen = 1; if (nShelf > 0) nShelf--; else if (nDisp > 0) nDisp--; }

            // Opener zuerst: die Schnellsten (mit Level im Opener-Befüllen)
            foreach (var w in free.OrderBy(w => Restock(w, EWorkerTask.RefillCardOpener)).Take(nOpen).ToList())
            { plan[w] = (EWorkerTask.RefillCardOpener, EWorkerTask.RefillCardOpener); free.Remove(w); }

            foreach (var w in free.OrderBy(w => Restock(w, EWorkerTask.RestockCardDisplay)).Take(nDisp).ToList())
            { plan[w] = (EWorkerTask.RestockCardDisplay, hasOpeners ? EWorkerTask.RefillCardOpener : EWorkerTask.RestockShelf); free.Remove(w); }

            foreach (var w in free)
                plan[w] = (EWorkerTask.RestockShelf, hasOpeners ? EWorkerTask.RefillCardOpener : EWorkerTask.RestockCardDisplay);

            // Anwenden (nur wo nötig) + speichern, damit es auch am nächsten Tag gilt
            var lines = new List<string>();
            foreach (var kv in plan.OrderBy(k => k.Value.main))
            {
                var w = kv.Key; var (main, side) = kv.Value;
                bool changed = w.m_PrimaryTask != main || w.m_SecondaryTask != side;
                if (w.m_PrimaryTask != main) w.SetTask(main);
                if (w.m_SecondaryTask != side) w.SetSecondaryTask(side);
                try { w.SetLastTask(main); } catch { }
                lines.Add($"{WorkerName(w)}: {TaskName(main)} / {TaskName(side)}{(changed ? " *" : "")}");
            }
            _dashPlan = plan.OrderBy(k => k.Value.main).Select(k => (WorkerName(k.Key), TaskName(k.Value.main), TaskName(k.Value.side))).ToList();
            Note($"Einsatzplan Tag {CPlayerData.m_CurrentDay + 1}: " + string.Join(" | ", lines));
            Logger.LogInfo("[Arbeiter] Einsatzplan (Haupt / Neben, * = geändert):\n  " + string.Join("\n  ", lines));
        }

        // ================= Produkte nachbestellen =================
        private void RestockTick()
        {
            if (!CtReady()) return;
            var shelves = ShelfManager.GetShelfList();
            if (shelves == null) return;

            var onShelf = new Dictionary<EItemType, int>();
            foreach (var shelf in shelves)
            {
                if (shelf == null || shelf.m_ItemNotForSale) continue;
                foreach (var comp in shelf.GetItemCompartmentList())
                {
                    if (comp == null) continue;
                    var t = comp.GetItemType();
                    if (t == EItemType.None) continue;
                    onShelf[t] = (onShelf.TryGetValue(t, out var c) ? c : 0) + comp.GetItemCount();
                }
            }

            var inBoxes = CountItemsInBoxes(out int boxes);
            var inDelivery = CountItemsInDelivery();
            int orders = 0;
            foreach (var kv in onShelf.OrderBy(k => k.Value))
            {
                if (orders >= _restockMaxPerMinute.Value || boxes >= _colMaxBoxesOnFloor.Value) break;
                var type = kv.Key;
                if (_restockOnlyTcg.Value && InventoryBase.GetItemData(type)?.category != EItemCategory.TCG) continue; // keine Figuren/Spiele/Zubehör
                int idx = BestRestockIndex(type);
                if (idx < 0) continue;
                float cost = CaseCost(idx, out int perCase);
                if (RecentlyOrdered(type)) continue;              // Lieferung noch unterwegs
                int total = kv.Value + (inBoxes.TryGetValue(type, out var b) ? b : 0) + (inDelivery.TryGetValue(type, out var dl) ? dl : 0);
                if (total >= perCase / 2) continue;               // noch genug da
                if (Budget < cost * 1.1f || DayLeft() < cost) break;

                if (BuyRestockIndex(idx, type, 1))
                {
                    MarkOrdered(type); MarkSpent(cost);
                    orders++; boxes++;
                    _statSpent += cost;
                    AddDaySpent(cost);
                    Note($"Nachbestellt: {InventoryBase.GetItemData(type)?.GetName() ?? type.ToString()} (1 Karton, ~{GameInstance.GetPriceString(cost)})");
                }
            }
        }

        private static int BestRestockIndex(EItemType type)
        {
            var list = InventoryBase.GetRestockDataIndexList(type);
            if (list == null) return -1;
            int best = -1; int bestCount = -1;
            foreach (var i in list)
            {
                if (!CPlayerData.GetIsItemLicenseUnlocked(i)) continue;
                var rd = InventoryBase.GetRestockData(i);
                int n = RestockManager.GetMaxItemCountInBox(rd.itemType, rd.isBigBox);
                if (n > bestCount) { bestCount = n; best = i; }
            }
            return best;
        }

        // Kauft wie "Sofort kaufen" im Collection Tracker: richtiger Shop (EPL oder Großhändler), Warenkorb sichern
        private bool BuyRestockIndex(int restockIndex, EItemType type, int count)
        {
            RestockItemScreen screen = null;
            string appId = EplShopAppId(type);
            if (!string.IsNullOrEmpty(appId))
                screen = AccessTools.Method(_ctOrderType, "ResolveCustomShopScreen")?.Invoke(null, new object[] { appId }) as RestockItemScreen;
            if (screen == null) screen = S<PhoneManager>()?.m_RestockItemScreen;
            if (screen == null || screen.m_RestockItemCheckoutScreen == null) return false;

            var cart = Traverse.Create(screen).Field("m_CartItemList").GetValue<Dictionary<int, int>>();
            var backup = cart != null ? new Dictionary<int, int>(cart) : null;
            bool ok = false;
            try
            {
                cart?.Clear();
                screen.AddToCartForCheckout(restockIndex, count);
                screen.m_RestockItemCheckoutScreen.OnPressConfirmCheckout();
                ok = cart == null || cart.Count == 0;
            }
            finally
            {
                if (cart != null)
                {
                    cart.Clear();
                    foreach (var kv in backup) cart[kv.Key] = kv.Value;
                    try { screen.m_RestockItemCheckoutScreen.UpdateData(screen, cart, false); } catch { }
                }
            }
            return ok;
        }

        private static bool _eplSearched;
        private static Type _eplType;

        private static string EplShopAppId(EItemType type)
        {
            try
            {
                // EnhancedPrefabLoader.API.Epl.Api.Assets.Shops.GetShopInfoFor(itemType) – Typ einmal suchen, ohne Warnungs-Spam
                if (!_eplSearched) { _eplSearched = true; _eplType = AccessTools.TypeByName("EnhancedPrefabLoader.API.Epl"); }
                if (_eplType == null) return null;
                object Prop(object o, string name)
                {
                    if (o == null) return null;
                    var t = o as Type ?? o.GetType();
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | (o is Type ? BindingFlags.Static : BindingFlags.Instance);
                    var p = t.GetProperty(name, flags);
                    if (p == null && !(o is Type))
                        foreach (var itf in t.GetInterfaces()) { p = itf.GetProperty(name); if (p != null) break; }
                    return p?.GetValue(o is Type ? null : o, null);
                }
                var shops = Prop(Prop(Prop(_eplType, "Api"), "Assets"), "Shops");
                if (shops == null) return null;
                var m = shops.GetType().GetMethod("GetShopInfoFor", new[] { typeof(EItemType) });
                if (m == null) foreach (var itf in shops.GetType().GetInterfaces()) { m = itf.GetMethod("GetShopInfoFor", new[] { typeof(EItemType) }); if (m != null) break; }
                var info = m?.Invoke(shops, new object[] { type });
                if (info == null) return null;
                return Traverse.Create(info).Field("PhoneAppId").GetValue<string>()
                       ?? Traverse.Create(info).Property("PhoneAppId").GetValue<string>();
            }
            catch { return null; }
        }
    }
}
