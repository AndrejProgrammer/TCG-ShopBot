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
        private readonly HashSet<ECardExpansionType> _hardSetTypes = new HashSet<ECardExpansionType>();
        private int _goalTotal, _goalHave = -1;
        private List<string> _goalNoPacks = new List<string>(), _goalPaused = new List<string>();
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
            RestoreDayState();
            if (_daySpentDay != CPlayerData.m_CurrentDay) { _daySpentDay = CPlayerData.m_CurrentDay; _daySpent = 0f; }
            return DayLimit <= 0f ? float.MaxValue : Mathf.Max(0f, DayLimit - _daySpent);
        }
        private void AddDaySpent(float v) { DayLeft(); _daySpent += v; SaveDayState(); }
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
            _billsEnabled = Config.Bind("0. Bills", "Pay bills automatically", true, "Rent, electricity and wages are paid as soon as they are due (before any purchases).");
            _bonusEnabled = Config.Bind("9. Workers", "Daily bonus for all workers", true, "Like the bonus button: 3x per day and worker (costs 1 day's wage each). Every bonus = +1 level on all tasks and double experience.");
            _expEnabled = Config.Bind("14. Expansions", "Buy shop expansions", true, "Buys the next shop expansion as soon as level and money allow.");
            _expWarehouse = Config.Bind("14. Expansions", "Buy warehouse (Shop B) and warehouse expansions", true, "Unlocks the warehouse and expands it.");
            _expMaxShop = Config.Bind("14. Expansions", "Max shop expansions", 30, "Up to this number (game maximum 30).");
            _expMaxWarehouse = Config.Bind("14. Expansions", "Max warehouse expansions", 14, "Up to this number (game maximum 14).");
            _expMoneyFactor = Config.Bind("14. Expansions", "Only buy if budget is at least X times the price", 5f, "Safety: expansion only if the money above the reserve covers the price at least this many times.");
            _reviewEnabled = Config.Bind("13. Reviews", "React to customer reviews", true, "Reorder missing packs/displays right away; lower the markup of an item when customers say 'too expensive'.");
            _reviewCases = Config.Bind("13. Reviews", "Boxes per missing item", 3, "This many boxes are ordered when customers miss an item.");
            _reviewMinMarkup = Config.Bind("13. Reviews", "Minimum markup (%)", 10f, "The markup is never lowered below this on price complaints.");
            _colEnabled = Config.Bind("7. Collection", "Complete the collection automatically", true, "Buys packs of unfinished sets (Collection Tracker) and lets the openers open them.");
            _colReserve = Config.Bind("7. Collection", "Safety reserve ($)", 20000f, "This much money is always kept (rent/wages). Everything above may be spent on packs.");
            _useAllMoney = Config.Bind("7. Collection", "Use all money (only hold back bills)", true, "On: the bot may spend everything but holds back open bills + the next day's bills (x1.5) – the account never goes negative. Off: fixed safety reserve."); 
            _colFocusSets = Config.Bind("7. Collection", "Target sets at once", 5, "This many sets are worked on at the same time.");
            _colEfficiency = Config.Bind("7. Collection", "Choose sets by efficiency", true, "On: sets with the most new cards per dollar (many missing cards, cheap packs). Off: nearly finished sets first.");
            _colPacksPerMissing = Config.Bind("7. Collection", "Nearly finished sets: max packs per missing card", 1500, "If only 1-3 cards are missing, the bot buys at most this many packs per missing card – then the set is paused (buy the cards from customers). 0 = no cap.");
            _colPacksBoughtCfg = Config.Bind("7. Collection", "Packs bought per set (internal)", "", "Counter for the cap above. Clear = reset.");
            foreach (var part in (_colPacksBoughtCfg.Value ?? "").Split('|'))
            {
                int eq = part.LastIndexOf('=');
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), out var n)) _setPacksBought[part.Substring(0, eq)] = n;
            }
            _colFills = Config.Bind("7. Collection", "Stock in opener fills", 2f, "Stock = capacity of ALL pack openers x this value (e.g. 11 openers x 448 packs x 2 = 9856 packs), split across the target sets. Adapts automatically when you add openers.");
            _colBudgetPerExtraSet = Config.Bind("7. Collection", "Budget per extra target set ($)", 50000f, "For every this much money above the reserve, one more target set is worked on at the same time. 0 = off.");
            _colPokemonOnly = Config.Bind("7. Collection", "Pokémon sets only", true, "Only sets from Pokémon shops (Pokemon Shop, Pokemon Pocket Shop) – no Tetramon/base game sets.");
            _dayBuyLimit = Config.Bind("7. Collection", "Daily purchase limit ($)", 60000f, "The bot spends at most this much per game day on packs for the collection. Restocking the shop shelves is not limited by it (only the safety reserve). 0 = no limit.");
            _dayBuyLimitPercent = Config.Bind("7. Collection", "Daily limit at least percent of money", 0f, "The daily limit grows with your money: at least this percent of the money above the reserve. 0 = off (fixed value only).");
            _maxStockPacks = Config.Bind("7. Collection", "Max packs in stock overall", 15000, "Upper limit for the pack stock of all target sets together – no matter how much money there is.");
            _colMaxFocusSets = Config.Bind("7. Collection", "Max target sets at once", 10, "Upper limit for the number of target sets at the same time.");
            _colBudgetPerExtraFill = Config.Bind("7. Collection", "Budget per extra opener fill ($)", 150000f, "For every this much money above the reserve, the stock grows by one opener fill. 0 = off.");
            _colMaxFills = Config.Bind("7. Collection", "Max opener fills in stock", 4f, "Upper limit for the stock.");
            _colCasesPerBuy = Config.Bind("7. Collection", "Max boxes per order", 40, "At most this many boxes per set and order.");
            _restockOnlyTcg = Config.Bind("10. Restocking", "Packs and displays only", true, "Only reorder booster packs and booster boxes/displays – no figures, games, sleeves, mats etc.");
            _colMaxBoxesOnFloor = Config.Bind("7. Collection", "Max full boxes in the shop overall", 600, "Above this number of full boxes (shop + warehouse) nothing more is ordered.");
            _colSetWorkerPacks = Config.Bind("7. Collection", "Set opener workers to target sets", true, "Workers with the task 'Fill opener' only take packs of the target sets.");
            _colClaimBoost = Config.Bind("7. Collection", "Claim pack machine boost", true, "Claims the next boost level in the Collection Tracker automatically.");

            _bulkEnabled = Config.Bind("8. Card storage", "Put duplicates into card storage", true, "Puts duplicate cards into the card storage so they end up in the display cases. 1 copy always stays in the album.");
            _bulkMaxValue = Config.Bind("8. Card storage", "Only cards below ($)", 100f, "More valuable duplicates stay in the album.");
            _bulkMaxPerRun = Config.Bind("8. Card storage", "Max cards per run", 2000, "Limit per card storage and run (every 2 minutes).");
            _gradedSell = Config.Bind("8. Card storage", "Sell graded cards first", true, "Graded cards go into the card storage first -> display cases (market price +20 %). Normal duplicates only when no graded cards are waiting.");
            _gradedKeepTen = Config.Bind("8. Card storage", "Grade 10 (PSA 10): only sell duplicates", true, "One of every grade 10 card always stays in the album.");

            _workersAuto = Config.Bind("9. Workers", "Assign workers automatically", true, "Work plan by strengths (main + side task), every morning and when the staff changes.");
            BindWorkerPlanConfig();

            _restockEnabled = Config.Bind("10. Restocking", "Reorder products", true, "Reorders products when shelf + storage are almost empty.");
            _restockMaxPerMinute = Config.Bind("10. Restocking", "Max orders per minute", 3, "Limit so not too many boxes arrive.");
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

            if (_colEnabled.Value && (_colTimer += 1f) >= 60f) { _colTimer = 0f; SafeRun("Collection", CollectionTick); }
            if (_bulkEnabled.Value && (_bulkTimer += 1f) >= 120f) { _bulkTimer = 0f; SafeRun("Card storage", BulkTick); }
            if (_workersAuto.Value && (_workerTimer += 1f) >= 60f) { _workerTimer = 0f; SafeRun("Workers", WorkerTick); }
            if (_restockEnabled.Value && (_restockTimer += 1f) >= 60f) { _restockTimer = 0f; SafeRun("Restocking", RestockTick); }
            HandleUpgrades();
            HandleGrading();
        }

        // ================= Rechnungen =================
        private float _billTimer = 999f;

        private void HandleBills()
        {
            if (!_billsEnabled.Value || (_billTimer += 1f) < 15f) return;
            _billTimer = 0f;
            SafeRun("Bills", () =>
            {
                var screen = S<PhoneManager>()?.m_RentBillScreen;
                if (screen == null) return;
                var parts = new List<FormattableString>();
                void Pay(EBillType type, string name, Action<bool> payFn)
                {
                    var bill = CPlayerData.GetBill(type);
                    if (bill == null || bill.amountToPay <= 0f) return;
                    if (CPlayerData.m_CoinAmountDouble < bill.amountToPay) return;
                    float amount = bill.amountToPay;
                    payFn(false);
                    var after = CPlayerData.GetBill(type);
                    if (after == null || after.amountToPay <= 0f) { parts.Add($"{L(name)} {GameInstance.GetPriceString(amount)}"); NoteBillPaid(amount); }
                }
                Pay(EBillType.Rent, "Rent", screen.OnPressPayRentBill);  // i18n
                Pay(EBillType.Electric, "Electricity", screen.OnPressPayElectricBill);  // i18n
                Pay(EBillType.Employee, "Wages", screen.OnPressPaySalaryBill);  // i18n
                if (parts.Count > 0) NoteF($"Bills paid: {new LList(parts, ", ")}");
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
            SafeRun("Reviews", ReviewTick);
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
                    Logger.LogInfo($"[Reviews] {kv.Value}x missed: {name} (not a pack/display – not ordered)");
                    continue;
                }
                if (RecentlyOrdered(type)) continue;
                int idx = BestRestockIndex(type);
                if (idx < 0) { Logger.LogInfo($"[Reviews] {name} missed, but no license – cannot be ordered"); continue; }

                // Liegt schon im Lager (und ist kein Ziel-Set-Pack, das die Opener verbrauchen)? -> nicht kaufen, nur einräumen lassen
                if (!_focusPackTypes.Contains(type))
                {
                    var stockNow = CountItemsInBoxes(out _);
                    CaseCost(idx, out int perCaseNow);
                    if (stockNow.TryGetValue(type, out var inStock) && inStock >= Mathf.Max(1, perCaseNow / 2))
                    {
                        needShelfHelp = true;
                        Logger.LogInfo($"[Reviews] {name} missed, but it is in storage ({inStock} pcs) – stocking it instead of buying");
                        continue;
                    }
                }
                float cost = CaseCost(idx, out int perCase);
                int cases = Mathf.Clamp(_reviewCases.Value, 1, Mathf.Max(1, (int)(Budget / (cost * 1.1f))));
                // goods customers miss earn money: not limited by the pack daily limit (only the safety budget)
                if (Budget < cost * 1.1f || cases < 1) break;
                if (BuyRestockIndex(idx, type, cases))
                {
                    MarkOrdered(type); MarkSpent(cost * cases);
                    _statSpent += cost * cases;
                    NoteF($"Customers miss {name} ({kv.Value}x) – ordered {cases} boxes (~{GameInstance.GetPriceString(cost * cases)})");
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
                    NoteF($"Customers find {InventoryBase.GetItemData(kv.Key)?.GetName() ?? kv.Key.ToString()} too expensive – markup {cur:0}% -> {next:0}%");
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
            SafeRun("Expansions", ExpansionTick);
        }

        private void ExpansionTick()
        {
            var urm = S<UnlockRoomManager>();
            if (urm == null || LightManager.GetHasDayEnded()) return;
            if (PacksFirstBlocks)
            {
                if (Time.unscaledTime >= _starveLogExp)
                {
                    _starveLogExp = Time.unscaledTime + 600f;
                    Logger.LogInfo($"[Expansions] waiting: pack stock low ({_packHave}/{_packNeed}) – money goes to packs first");
                }
                return;
            }
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
                    NoteF($"Shop expansion {idx + 1} bought ({GameInstance.GetPriceString(cost)}) – more space for shelves/openers");
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
                    NoteF($"Warehouse (Shop B) unlocked ({GameInstance.GetPriceString(price)})");
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
                        NoteF($"Warehouse expansion {widx + 1} bought ({GameInstance.GetPriceString(cost)}) – more room for boxes");
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
                    NoteF($"{WorkerName(_shelfHelper)} back to {TaskName(_shelfHelperOldMain)}");
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
            NoteF($"Customers miss items from storage – {WorkerName(pick)} restocks shelves for 10 min");
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
                if (given.Count > 0) NoteF($"Bonus paid: {string.Join(", ", given)}");
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
                if (!_ctWarned) { _ctWarned = true; Logger.LogWarning("Collection Tracker not found – collection features off."); ModMissing("Collection Tracker", "Collection / pack buying"); }  // i18n-last
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
            _emergencyEnabled = Config.Bind("7. Collection", "Emergency restock when packs run out", true,
                "Checks the pack stock of unfinished sets every 10 s (floor + storage + on the way). If it drops below the set opener fills, a lot is ordered right away – even beyond the daily limit (up to the emergency limit).");
            _emergencyFills = Config.Bind("7. Collection", "Emergency below X opener fills", 1f,
                "1 = as soon as there are fewer packs than all openers hold together.");
            _emergencyExtraLimit = Config.Bind("7. Collection", "Emergency extra limit per day ($)", 100000f,
                "The emergency restock may spend this much per day on top of the normal daily limit.");
            _packsFirst = Config.Bind("7. Collection", "Packs first (licenses/expansions wait when stock is low)", true,
                "While there are fewer packs than one opener fill, the bot buys no expansions and only licenses for sets without any buyable packs – the money goes to packs.");
        }

        private float EmergencyLeft()
        {
            RestoreDayState();
            if (_emergencyDay != CPlayerData.m_CurrentDay) { _emergencyDay = CPlayerData.m_CurrentDay; _emergencySpent = 0f; }
            return Mathf.Max(0f, _emergencyExtraLimit.Value - _emergencySpent);
        }

        // Wie viel darf jetzt ausgegeben werden: Tageslimit, im Notfall zusätzlich das Notfall-Limit
        private float BuyLeft() => EmergencyActive ? DayLeft() + EmergencyLeft() : DayLeft();

        private void AddBuySpent(float v)
        {
            float normal = Mathf.Min(v, DayLeft());
            AddDaySpent(v);
            if (EmergencyActive && v > normal) { EmergencyLeft(); _emergencySpent += v - normal; SaveDayState(); }
        }

        // ================= Pack-Vorrat: Knappheit, Verbrauch, Reichweite =================
        // Vorrat = Packs unfertiger Sets am Boden + im Lager + unterwegs. Knapp = weniger als eine volle Opener-Füllung.
        // Verbrauch pro Minute = (Vorrat vorher + seitdem gekauft − Vorrat jetzt), geglättet.
        private ConfigEntry<bool> _packsFirst;
        private bool _packStarving;
        private int _packHave, _packNeed, _packPrevHave = -1, _packsBoughtSince;
        private float _packPrevAt, _packRatePerMin;
        private float _starveLogLic, _starveLogExp;

        private bool PacksFirstBlocks => _packsFirst != null && _packsFirst.Value && _packStarving;

        private void UpdatePackSupply(int have, int need)
        {
            float now = Time.unscaledTime;
            if (_packPrevHave >= 0 && now > _packPrevAt + 1f)
            {
                float used = _packPrevHave + _packsBoughtSince - have;
                float perMin = Mathf.Max(0f, used) / ((now - _packPrevAt) / 60f);
                _packRatePerMin = _packRatePerMin <= 0f ? perMin : _packRatePerMin * 0.8f + perMin * 0.2f;
            }
            _packPrevHave = have; _packPrevAt = now; _packsBoughtSince = 0;
            _packHave = have; _packNeed = need;
            _packStarving = have < need;
        }

        // Minuten, die der Vorrat beim aktuellen Verbrauch noch reicht (-1 = unbekannt)
        private float PackMinutesLeft => _packRatePerMin > 1f ? _packHave / _packRatePerMin : -1f;

        private string PackSupplyText()
        {
            string s = _packRatePerMin > 1f
                ? F($"Usage ~{_packRatePerMin:N0}/min · lasts ~{PackMinutesLeft:N0} min")
                : T("Floor + storage + on the way");
            return PacksFirstBlocks ? s + T(" · low: packs first") : s;
        }

        private void HandlePackEmergency()
        {
            if (!_emergencyEnabled.Value || !_colEnabled.Value || (_emergencyTimer += 1f) < 10f) return;
            _emergencyTimer = 0f;
            if (_openerPackTypes.Count == 0 || LightManager.GetHasDayEnded()) return;
            SafeRun("Emergency restock", () =>
            {
                int cap = 0;
                var ops = ShelfManager.GetAutoPackOpenerList();
                if (ops != null) foreach (var o in ops) if (o != null) cap += Mathf.Max(0, o.m_MaxPackCount);
                if (cap <= 0) return;
                var stock = CountItemsInBoxes(out _);
                var coming = CountItemsInDelivery();
                int have = _openerPackTypes.Sum(t => (stock.TryGetValue(t, out var a) ? a : 0) + (coming.TryGetValue(t, out var b) ? b : 0));
                int need = Mathf.RoundToInt(cap * Mathf.Max(0.1f, _emergencyFills.Value));
                UpdatePackSupply(have, need);
                if (EmergencyActive || have >= need) return;
                _emergencyUntil = Time.unscaledTime + 30f;
                NoteF($"Emergency restock: only {have} packs left for the openers (target at least {need}) – ordering now");
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
                    Note("Pack machine boost: next level claimed! (faster + more capacity)");
            }

            // 2) Ziel-Sets: unfertig + kaufbar, fast fertige zuerst
            var candidates = new List<KeyValuePair<SetInfo, int>>();
            var sets = GetSets();
            int incomplete = 0, errors = 0;
            var noBox = new List<string>();
            _hardSetTypes.Clear();   // Sets, deren fehlende Karten man kaum über Packs bekommt (pausiert / keine Packs) -> Kunden-Ankauf lohnt mehr
            foreach (var s in sets)
            {
                if (s.Complete || s.Total <= 0 || s.Have >= s.Total) continue;
                if (_colPokemonOnly.Value && s.Shop.IndexOf("pok", StringComparison.OrdinalIgnoreCase) < 0) continue; // nur Pokémon-Sets
                incomplete++;
                int box;
                try { box = GetBiggestBox(s.Type); } catch { errors++; continue; }
                if (box < 0) { noBox.Add(s.Name); _hardSetTypes.Add(s.Type); continue; }
                candidates.Add(new KeyValuePair<SetInfo, int>(s, box));
            }
            Logger.LogInfo($"[Collection] {sets.Count} sets read, {incomplete} unfinished, {candidates.Count} buyable, {errors} errors" +
                           (noBox.Count > 0 ? $" | without buyable packs: {string.Join(", ", noBox.Take(8))}{(noBox.Count > 8 ? " ..." : "")}" : "") +
                           $" | budget: {GameInstance.GetPriceString((float)Budget)}");
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
                capped.Add($"{c.Key.Name} ({miss} missing, {boughtPacks} packs bought)"); _hardSetTypes.Add(c.Key.Type);
                return false;
            }).ToList();
            if (capped.Count > 0 && Time.unscaledTime >= _cappedLogAt)
            {
                _cappedLogAt = Time.unscaledTime + 1800f;
                NoteF($"Collection: nearly finished sets paused (better buy the missing cards from customers): {string.Join(", ", capped.Take(5))}");
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
            // Ziel-Übersicht: alle Pokémon-Karten
            var pokeSets = sets.Where(s => s.Total > 0 && (!_colPokemonOnly.Value || s.Shop.IndexOf("pok", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            _goalTotal = pokeSets.Sum(s => s.Total);
            _goalHave = pokeSets.Sum(s => Mathf.Min(s.Have, s.Total));
            _goalNoPacks = pokeSets.Where(s => s.Have < s.Total && noBox.Contains(s.Name)).Select(s => $"{s.Name} ({s.Total - s.Have})").ToList();
            _goalPaused = capped.ToList();
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
            Logger.LogInfo($"[Collection] Openers: {openerList?.Count ?? 0}, {openerCapacity} packs capacity -> target stock {totalTarget} packs ({targetPerSet} per set)");

            foreach (var f in focus)
            {
                if (boxes >= _colMaxBoxesOnFloor.Value) { NoteF($"Collection: {boxes} full boxes in the shop (limit {_colMaxBoxesOnFloor.Value}) – waiting"); break; }
                float cost = CaseCost(f.Value, out int perCase);
                var setTypes = GetSetPackTypes(f.Key.Type);
                // Lieferung noch unterwegs -> nicht nochmal; im Notfall nur warten, wenn wirklich etwas von diesem Set unterwegs ist
                if (EmergencyActive ? setTypes.Any(t => coming.TryGetValue(t, out var cm) && cm > 0) : setTypes.Any(RecentlyOrdered)) continue;
                int have = setTypes.Sum(t => (stock.TryGetValue(t, out var c) ? c : 0) + (coming.TryGetValue(t, out var d) ? d : 0));
                if (have >= targetPerSet)
                {
                    Logger.LogInfo($"[Collection] {f.Key.Name}: enough stock ({have}/{targetPerSet} packs)");
                    continue;
                }
                // Große Mengen: auf den berechneten Vorrat auffüllen, max. X Kartons pro Bestellung, so viel das Budget hergibt
                int missingCases = Mathf.CeilToInt((targetPerSet - have) / (float)Mathf.Max(1, perCase));
                int cases = Mathf.Min(Mathf.Max(1, _colCasesPerBuy.Value), Mathf.Max(1, missingCases));
                cases = Mathf.Min(cases, (int)(Budget / (cost * 1.1f)));
                cases = Mathf.Min(cases, (int)(BuyLeft() / Mathf.Max(0.01f, cost)));
                if (cases < 1 && BuyLeft() < cost) { NoteF($"Daily limit reached ({GameInstance.GetPriceString(DayLimit)}) – continuing tomorrow"); break; }
                if (cases < 1) { NoteF($"Collection: budget not enough for {f.Key.Name} ({GameInstance.GetPriceString(cost)})"); break; }

                if (PurchaseSet(f.Key.Type, cases, out var msg))
                {
                    foreach (var st in setTypes) MarkOrdered(st);
                    MarkSpent(cost * cases);
                    boxes += cases;
                    _statSpent += cost * cases;
                    AddBuySpent(cost * cases);
                    _statPackCases += cases;
                    _packsBoughtSince += cases * perCase;
                    CountSetPacks(f.Key.Name, cases * perCase);
                    NoteF($"Bought: {cases} boxes {f.Key.Name} ({cases * perCase} packs, ~{GameInstance.GetPriceString(cost * cases)}) – set {f.Key.Have}/{f.Key.Total}");
                }
                else Logger.LogInfo($"Purchase {f.Key.Name} not possible: {msg}");
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
                if (changed) NoteF($"{WorkerName(w)} now fills openers only with target sets: {_focusText}");
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
            int Free(InteractableCardStorageShelf s) => s.GetBoxTotalCardCountMax() - s.GetTotalCardAmount();

            // 1) Graded cards first (PSA 10 only duplicates – one always stays): into the storage with the most room
            if (_gradedSell.Value)
            {
                int need = SellableGradedPoints();
                if (need > 0)
                {
                    var gt = valid.OrderByDescending(Free).First();
                    // Storage full? Cheapest normal duplicates back to the album so the graded cards fit
                    if (Free(gt) < need)
                    {
                        int back = ReturnCheapCardsToAlbum(gt, Mathf.Min(need - Free(gt), 5000), out float backValue);
                        if (back > 0) NoteF($"Card storage full: {back} cheap duplicates (~{GameInstance.GetPriceString(backValue)}) back to the album – room for graded cards");
                    }
                    var glist = gt.GetCompactCardDataAmountList();
                    int g = MoveGradedToStorage(glist, Mathf.Max(0, Free(gt)), out float gValue);
                    if (g > 0)
                    {
                        gt.SetCompactCardDataAmountList(glist);
                        S<InteractionPlayerController>()?.m_CollectionBinderFlipAnimCtrl?.SetCanUpdateSort(true);
                        _statBulk += g;
                        NoteF($"Card storage: {g} graded cards put up for sale (value ~{GameInstance.GetPriceString(gValue)})");
                    }
                }
            }

            // 2) Normal duplicates into ALL storages with room. Storages with graded cards waiting stay free for them
            //    (so the display cases show graded cards first) – unless every storage has graded cards.
            var withRoom = valid.Where(s => Free(s) > 0).ToList();
            var noGraded = withRoom.Where(s => !_gradedSell.Value || !s.GetCompactCardDataAmountList().Any(e => e.gradedCardIndex > 0)).ToList();
            var targets = (noGraded.Count > 0 ? noGraded : withRoom)
                .Select(s => (shelf: s, list: s.GetCompactCardDataAmountList(), budget: Mathf.Min(Free(s), _bulkMaxPerRun.Value)))
                .Where(t => t.budget > 0).ToList();
            // Normal duplicates: spread over several frames (one set per frame) so nothing stutters
            if (targets.Count > 0 && !_bulkRunning) StartCoroutine(BulkRawRoutine(targets));
        }

        private bool _bulkRunning;

        // Fills the targets one after another: when one storage is full (its budget used up), the next one continues
        private System.Collections.IEnumerator BulkRawRoutine(List<(InteractableCardStorageShelf shelf, List<CompactCardDataAmount> list, int budget)> targets)
        {
            _bulkRunning = true;
            int moved = 0, total = 0, usedShelves = 0; float value = 0f;
            List<SetInfo> sets;
            try { sets = GetSets(); } catch { _bulkRunning = false; yield break; }
            float bulkLimit = _gradeEnabled.Value ? Mathf.Min(_bulkMaxValue.Value, _gradeMinValue.Value) : _bulkMaxValue.Value;

            int ti = 0;
            var target = targets[0].shelf; var list = targets[0].list; int budget = targets[0].budget;
            void Flush()
            {
                if (moved <= 0 || target == null) return;
                target.SetCompactCardDataAmountList(list);
                total += moved; usedShelves++; moved = 0;
            }
            bool Next()
            {
                Flush();
                if (++ti >= targets.Count) return false;
                target = targets[ti].shelf; list = targets[ti].list; budget = targets[ti].budget;
                return true;
            }

            foreach (var set in sets)
            {
                if (budget <= 0 && !Next()) break;
                if (target == null) break;
                try
                {
                    foreach (bool dim in set.Type == ECardExpansionType.Ghost ? new[] { false, true } : new[] { false })
                    {
                        var collected = CPlayerData.GetCardCollectedList(set.Type, dim);
                        int count = collected != null ? collected.Count : set.Total;
                        for (int i = 0; i < count; i++)
                        {
                            if (budget <= 0 && !Next()) break;   // this storage is full -> next one
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
                catch (Exception e) { Logger.LogWarning($"[Card storage] {set.Name}: {e.Message}"); }
                yield return null; // nächstes Set im nächsten Bild
            }

            Flush();
            if (total > 0)
            {
                S<InteractionPlayerController>()?.m_CollectionBinderFlipAnimCtrl?.SetCanUpdateSort(true);
                _statBulk += total;
                NoteF($"Card storage: {total} duplicate cards (value ~{GameInstance.GetPriceString(value)}) put in ({usedShelves} storages)");
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
        private string _lastPlanKey, _lastPlanSig;
        private float _planAdaptAt;
        private ConfigEntry<bool> _planAdapt;
        internal (float opener, float shelf, float display) _dashDemand;

        // Work that is left for the workers in the next 2 minutes (items), after what the bot does itself
        private (float opener, float shelf, float display) WorkDemand()
        {
            float window = 120f;
            float opener = 0f, shelf = 0f, display = 0f;
            try
            {
                // openers: free room in openers that are not running, as far as packs are lying around
                int freeRoom = 0;
                var ops = ShelfManager.GetAutoPackOpenerList();
                if (ops != null) foreach (var o in ops) if (o != null && !o.GetIsProcessing()) freeRoom += Mathf.Max(0, o.m_MaxPackCount - o.GetStoredItemList().Count);
                var floor = new Dictionary<EItemType, int>();
                var rm = S<RestockManager>();
                if (rm != null)
                    foreach (var b in rm.m_ItemPackagingBoxList)
                        if (b != null && b.gameObject.activeInHierarchy && !b.m_IsStored && b.m_ItemCompartment != null && b.m_ItemCompartment.GetItemCount() > 0)
                        { var t = b.m_ItemCompartment.GetItemType(); floor[t] = (floor.TryGetValue(t, out var n) ? n : 0) + b.m_ItemCompartment.GetItemCount(); }
                int packs = floor.Where(kv => _openerPackTypes.Contains(kv.Key) && !(_shelfNeed.TryGetValue(kv.Key, out var sn) && sn > 0)).Sum(kv => kv.Value);
                opener = Mathf.Min(freeRoom, packs) - (_openerSelf.Value ? _openerPerSecond.Value * window : 0f);
                // shelves: missing items in labeled slots, as far as the goods are lying around
                foreach (var kv in _shelfNeed) shelf += Mathf.Min(kv.Value, floor.TryGetValue(kv.Key, out var n2) ? n2 : 0);
                shelf -= _stockSelf.Value ? _stockPerSecond.Value * window : 0f;
                // display cases: empty card slots, as far as cards wait in the card storage
                int emptyCards = 0;
                var cs = ShelfManager.GetCardShelfList();
                if (cs != null) foreach (var c in cs) if (c != null) foreach (var comp in c.GetCardCompartmentList()) if (comp != null && !comp.m_ItemNotForSale && comp.m_StoredCardList.Count == 0) emptyCards++;
                int waiting = ShelfManager.GetCardStorageShelfList()?.Where(x => x != null).Sum(x => x.GetTotalCardAmount()) ?? 0;
                display = Mathf.Min(emptyCards, waiting) - (_cardsSelf.Value ? _cardsPerSecond.Value * window : 0f);
            }
            catch { }
            return (Mathf.Max(0f, opener), Mathf.Max(0f, shelf), Mathf.Max(0f, display));
        }
        private ConfigEntry<int> _planCashiers, _planDisplays, _planShelves;

        private void BindWorkerPlanConfig()
        {
            _planCashiers = Config.Bind("9. Workers", "Number of cashiers (0 means as many as registers)", 0, "Main AND side task checkout. Those with the best checkout speed (incl. level).");
            _planDisplays = Config.Bind("9. Workers", "Display fillers (bot fills them itself)", 0, "Workers for display cases. 0 = the bot fills the display cases itself.");
            _planShelves = Config.Bind("9. Workers", "Shelf stockers (bot stocks itself)", 0, "Minimum number of workers for shelves. The rest is split by demand.");
            _planAdapt = Config.Bind("9. Workers", "Adapt work plan to demand during the day", true,
                "Every 2 minutes: where is work left that the bot does not do itself (openers, shelves, display cases)? Workers go there by their strengths; the side task is the next task with work, so nobody stands around.");
            _cashTurbo = Config.Bind("5. Checkout", "Checkout turbo (scan speed x)", 2f, "Cashiers scan this much faster (1 = normal). Works like a second person at every register.");
        }

        private static string TaskName(EWorkerTask t)
        {
            switch (t)
            {
                case EWorkerTask.ManCounter: return "Cashier";  // i18n
                case EWorkerTask.RefillCardOpener: return "Opener";  // i18n
                case EWorkerTask.RestockShelf: return "Shelves";  // i18n
                case EWorkerTask.RestockCardDisplay: return "Displays";  // i18n
                case EWorkerTask.SetPrice: return "Prices";  // i18n
                case EWorkerTask.RefillCleanser: return "Cleaner";  // i18n
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

            // Plan anew on a new day / staff change, and during the day when the demand has shifted (at most every 2 min)
            string key = CPlayerData.m_CurrentDay + "|" + _autoCheckout.Value + "|" + string.Join(",", ws.Select(w => w.m_WorkerIndex).OrderBy(i => i));
            bool fresh = key != _lastPlanKey;
            if (!fresh && (!_planAdapt.Value || Time.unscaledTime < _planAdaptAt)) return;
            var demand = WorkDemand();
            _dashDemand = demand;

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

            // 2) The rest goes where the work is. Demand = what is left after the bot's own share (it fills openers,
            //    shelves and display cases itself). Main task = the task with the most demand that the worker is best at,
            //    side task = the next task with demand - so nobody stands around when the main task runs out.
            var tasks = new List<(EWorkerTask task, float need)>();
            if (hasOpeners) tasks.Add((EWorkerTask.RefillCardOpener, demand.opener));
            tasks.Add((EWorkerTask.RestockShelf, demand.shelf));
            if (hasStorage) tasks.Add((EWorkerTask.RestockCardDisplay, demand.display));
            int nFree = free.Count;
            var count = tasks.ToDictionary(t => t.task, t => 0);
            // fixed minimums from the settings
            if (hasStorage) count[EWorkerTask.RestockCardDisplay] = Mathf.Min(_planDisplays.Value, nFree);
            count[EWorkerTask.RestockShelf] = Mathf.Min(_planShelves.Value, nFree - count.Values.Sum());
            int rest = nFree - count.Values.Sum();
            float total = tasks.Sum(t => t.need);
            if (total <= 0f)
            {
                // nothing left over for the workers: openers (if any) as main task, shelves as backup
                count[hasOpeners ? EWorkerTask.RefillCardOpener : EWorkerTask.RestockShelf] += rest;
            }
            else
            {
                var share = tasks.Select(t => (t.task, exact: rest * t.need / total)).ToList();
                foreach (var sh in share) count[sh.task] += Mathf.FloorToInt(sh.exact);
                int left = nFree - count.Values.Sum();
                foreach (var sh in share.OrderByDescending(x => x.exact - Mathf.Floor(x.exact)))
                { if (left <= 0) break; count[sh.task]++; left--; }
                // every task with demand gets at least one worker (taken from the biggest group)
                foreach (var t in tasks.Where(t => t.need > 0f && count[t.task] == 0).ToList())
                {
                    var big = count.OrderByDescending(kv => kv.Value).First();
                    if (big.Value > 1) { count[big.Key]--; count[t.task]++; }
                }
            }
            // side task: the task with the most demand that is not the main task
            EWorkerTask Side(EWorkerTask main)
            {
                var o = tasks.Where(t => t.task != main).OrderByDescending(t => t.need).Select(t => (EWorkerTask?)t.task).FirstOrDefault();
                return o ?? (main == EWorkerTask.RestockShelf && hasOpeners ? EWorkerTask.RefillCardOpener : EWorkerTask.RestockShelf);
            }
            // fill the tasks with the most demand first, each with the workers who are fastest at it (incl. level)
            foreach (var t in tasks.OrderByDescending(t => t.need))
                foreach (var w in free.OrderBy(w => Restock(w, t.task)).Take(count[t.task]).ToList())
                { plan[w] = (t.task, Side(t.task)); free.Remove(w); }
            foreach (var w in free) plan[w] = (EWorkerTask.RestockShelf, Side(EWorkerTask.RestockShelf));

            // during the day only apply when the split really changed
            string sig = string.Join(",", plan.Values.GroupBy(v => v).OrderBy(g => g.Key.ToString()).Select(g => g.Key + "=" + g.Count()));
            _planAdaptAt = Time.unscaledTime + 120f;
            if (!fresh && sig == _lastPlanSig) return;
            _lastPlanKey = key; _lastPlanSig = sig;

            // Anwenden (nur wo nötig) + speichern, damit es auch am nächsten Tag gilt
            var lines = new List<FormattableString>();
            foreach (var kv in plan.OrderBy(k => k.Value.main))
            {
                var w = kv.Key; var (main, side) = kv.Value;
                bool changed = w.m_PrimaryTask != main || w.m_SecondaryTask != side;
                if (w.m_PrimaryTask != main) w.SetTask(main);
                if (w.m_SecondaryTask != side) w.SetSecondaryTask(side);
                try { w.SetLastTask(main); } catch { }
                lines.Add($"{WorkerName(w)}: {L(TaskName(main))} / {L(TaskName(side))}{(changed ? " *" : "")}");
            }
            _dashPlan = plan.OrderBy(k => k.Value.main).Select(k => (WorkerName(k.Key), TaskName(k.Value.main), TaskName(k.Value.side))).ToList();
            NoteF($"Work plan day {CPlayerData.m_CurrentDay + 1} (work left for workers: openers {demand.opener:N0}, shelves {demand.shelf:N0}, displays {demand.display:N0}): {new LList(lines, " | ")}");
            Logger.LogInfo("[Workers] Work plan (main / side, * = changed):\n  " + string.Join("\n  ", lines.Select(Lang.Source)));
        }

        // ================= Produkte nachbestellen =================
        private void RestockTick()
        {
            if (!CtReady()) return;
            var shelves = ShelfManager.GetShelfList();
            if (shelves == null) return;

            // what is on the shelves and how much room the labeled slots of each goods type have
            var onShelf = new Dictionary<EItemType, int>();
            var capacity = new Dictionary<EItemType, int>();
            foreach (var shelf in shelves)
            {
                if (shelf == null || shelf.m_ItemNotForSale) continue;
                foreach (var comp in shelf.GetItemCompartmentList())
                {
                    if (comp == null || comp.m_ItemNotForSale) continue;
                    var t = comp.GetItemType();
                    if (t == EItemType.None) continue;
                    onShelf[t] = (onShelf.TryGetValue(t, out var c) ? c : 0) + comp.GetItemCount();
                    capacity[t] = (capacity.TryGetValue(t, out var m) ? m : 0) + Mathf.Max(comp.GetItemCount(), comp.GetMaxItemCount());
                }
            }

            var inBoxes = CountItemsInBoxes(out _);
            var inDelivery = CountItemsInDelivery();
            // Box cap: only boxes for the shop shelves count – the many pack boxes for the openers must not block restocking
            int boxes = 0;
            var rmb = S<RestockManager>();
            if (rmb != null)
                foreach (var bx in rmb.m_ItemPackagingBoxList)
                    if (bx != null && bx.gameObject.activeInHierarchy && !bx.m_IsStored && bx.m_ItemCompartment != null && bx.m_ItemCompartment.GetItemCount() > 0
                        && !_openerPackTypes.Contains(bx.m_ItemCompartment.GetItemType())) boxes++;
            int orders = 0;
            // biggest gaps first
            foreach (var kv in onShelf.OrderByDescending(k => (capacity.TryGetValue(k.Key, out var cp) ? cp : 0) - k.Value))
            {
                if (orders >= _restockMaxPerMinute.Value || boxes >= _colMaxBoxesOnFloor.Value) break;
                var type = kv.Key;
                if (_restockOnlyTcg.Value && InventoryBase.GetItemData(type)?.category != EItemCategory.TCG) continue; // keine Figuren/Spiele/Zubehör
                int idx = BestRestockIndex(type);
                if (idx < 0) continue;
                float cost = CaseCost(idx, out int perCase);
                if (RecentlyOrdered(type)) continue;              // Lieferung noch unterwegs
                // Gap = room in all labeled slots of this goods minus what is on them. Before, "enough" was the shelf total –
                // a few half-full slots hid 26 completely empty ones, so nothing was ordered.
                int missing = (capacity.TryGetValue(type, out var capT) ? capT : 0) - kv.Value;
                int available = (inBoxes.TryGetValue(type, out var b) ? b : 0) + (inDelivery.TryGetValue(type, out var dl) ? dl : 0);
                int gap = missing - available;
                if (gap < Mathf.Max(1, perCase / 2)) continue;     // boxes on the floor / on the way cover it
                int cases = Mathf.Clamp(Mathf.CeilToInt(gap / (float)Mathf.Max(1, perCase)), 1, 3);
                // Shelf goods earn money: only the safety budget applies, not the pack daily limit
                // (pack buying used it up within minutes, so the shelves were never restocked)
                cases = Mathf.Min(cases, (int)(Budget / (cost * 1.1f)));
                if (cases < 1) continue;

                if (BuyRestockIndex(idx, type, cases))
                {
                    MarkOrdered(type); MarkSpent(cost * cases);
                    orders++; boxes += cases;
                    _statSpent += cost * cases;
                    NoteF($"Reordered: {InventoryBase.GetItemData(type)?.GetName() ?? type.ToString()} ({cases} boxes, ~{GameInstance.GetPriceString(cost * cases)}) – {missing} free on the shelves");
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
