using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    [BepInPlugin("ratta.shopbot", "ShopBot", "0.6.1")]
    public partial class Plugin : BaseUnityPlugin
    {
        // ---- Konfiguration ----
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _toggleKey;
        private ConfigEntry<bool> _showOverlay;
        private ConfigEntry<float> _idleSeconds;

        private ConfigEntry<bool> _openerCollect;
        private ConfigEntry<float> _openerShowSeconds;
        private ConfigEntry<bool> _openerStartPartial;
        private ConfigEntry<float> _openerPartialIdleSeconds;

        private ConfigEntry<bool> _tradeEnabled;
        private ConfigEntry<float> _buyStartPercent;
        private ConfigEntry<float> _buyMaxPercent;
        private ConfigEntry<float> _buyStepPercent;
        private ConfigEntry<bool> _swapEnabled;
        private ConfigEntry<float> _swapMinGainPercent;
        private ConfigEntry<float> _stepDelay;
        private ConfigEntry<float> _buyMaxPercent2, _buyMaxNewPercent, _buyNewHardPercent, _buyNewMaxMoneyPct, _buyAlwaysBelow, _swapTolerance;
        private ConfigEntry<bool> _buyNewAlwaysAsk;
        private ConfigEntry<float> _dupStartPct, _dupStartCheapPct, _dupCheapBelow;

        // Wie viel ist uns eine Karte beim Ankauf/Tausch höchstens wert (in $)?
        //  doppelt: Marktpreis x Limit (Weiterverkauf +20 % = Gewinn)
        //  neu:     Marktpreis x 200 % – bzw. 500 %, wenn ihr Set pausiert ist / keine Packs hat (Kunde = einzige Quelle)
        //  neu:     nie mehr als X % vom Geld
        private float CardWorth(CardData card, float market, out bool isNew, out bool hard)
        {
            isNew = card != null && card.cardGrade == 0 && CPlayerData.GetCardAmount(card) == 0;
            hard = isNew && _hardSetTypes.Contains(card.expansionType);
            if (!isNew) return market * _buyMaxPercent2.Value / 100f;
            float worth = market * (hard ? _buyNewHardPercent.Value : _buyMaxNewPercent.Value) / 100f;
            float money = (float)Math.Max(0.0, CPlayerData.m_CoinAmountDouble - Reserve);
            return Mathf.Min(worth, money * _buyNewMaxMoneyPct.Value / 100f);
        }

        private ConfigEntry<bool> _dayEndEnabled;
        private ConfigEntry<float> _dayEndDelay;

        private ConfigEntry<bool> _cashierHelp;
        private ConfigEntry<int> _cashierQueueThreshold;
        private ConfigEntry<float> _cashierReleaseSeconds;

        private ConfigEntry<bool> _priceEnabled;
        private ConfigEntry<float> _priceItemPercent;
        private ConfigEntry<float> _priceCardPercent;
        private ConfigEntry<float> _priceInterval;
        private ConfigEntry<bool> _priceRound;
        private ConfigEntry<bool> _cashierStayLate;

        // ---- Zustand ----
        private bool _busy;
        private float _tick;
        private readonly Dictionary<InteractableAutoPackOpener, KeyValuePair<int, float>> _openerIdle =
            new Dictionary<InteractableAutoPackOpener, KeyValuePair<int, float>>();

        private int _statCollected, _statStarted, _statBought, _statSwapped, _statDeclined, _statPackCases, _statBulk, _statGraded, _statGradedOpened;
        private ConfigEntry<bool> _gradedBoxOpen;
        private float _statSpent;
        private string _lastAction = "-";

        private void Awake()
        {
            _enabled = Config.Bind("1. General", "Bot active", true, "Turn the bot on/off.");
            _toggleKey = Config.Bind("1. General", "Hotkey on/off", new KeyboardShortcut(KeyCode.F8), "Key to turn the bot on/off.");
            _showOverlay = Config.Bind("1. General", "Show status", true, "Small status line at the top left.");
            _startDelay = Config.Bind("1. General", "Start delay after loading (seconds)", 10f, "The bot waits this long after loading the save before it starts.");
            _idleSeconds = Config.Bind("1. General", "Windows only if I press nothing for X sec", 8f,
                "Customer offers, opener results and end of day open windows and briefly lock the controls. This only happens if you have not used a key/mouse for this long.");

            _openerCollect = Config.Bind("2. Pack opener", "Collect finished openers", true, "Takes the cards out of finished pack openers.");
            _openerShowSeconds = Config.Bind("2. Pack opener", "Show result (seconds)", 2.5f, "How long the card overview stays open.");
            _openerStartPartial = Config.Bind("2. Pack opener", "Start half-full openers", true, "Starts openers that have packs but have not been filled further for a while.");
            _gradedBoxOpen = Config.Bind("2. Pack opener", "Open grading packages automatically", true, "Opens graded card packages – the cards go into the album and are sold afterwards (PSA 10 only duplicates).");
            _openerPartialIdleSeconds = Config.Bind("2. Pack opener", "Wait time half-full (seconds)", 45f, "The pack count must not change for this long before the opener is started.");

            _tradeEnabled = Config.Bind("3. Customer offers", "Handle offers", true, "Serve customers who want to sell/swap cards automatically.");
            _buyStartPercent = Config.Bind("3. Customer offers", "Buy start offer (% market price)", 50f, "First offer when haggling.");
            _buyMaxPercent = Config.Bind("3. Customer offers", "Buy maximum (% market price)", 70f, "The bot never pays more.");
            _buyStepPercent = Config.Bind("3. Customer offers", "Buy step (% market price)", 5f, "Raised by this much per round.");
            _swapEnabled = Config.Bind("3. Customer offers", "Handle swaps", true, "Accept swap offers when we gain.");
            _swapMinGainPercent = Config.Bind("3. Customer offers", "Swap minimum gain (%)", 0f, "The card we get must be worth this many % more than the one we give.");
            _stepDelay = Config.Bind("3. Customer offers", "Pause between steps (seconds)", 0.8f, "So you can see what happens.");
            _buyMaxPercent2 = Config.Bind("3. Customer offers", "Buy limit normal cards (% market price)", 95f, "The bot pays at most this much. Reselling at +20 % stays profitable.");
            _buyMaxNewPercent = Config.Bind("3. Customer offers", "New card: up to X percent market price (set with packs)", 200f, "If the card is missing from the collection and its set still has buyable packs: pay the asking price up to this percent of the market price.");
            _buyNewHardPercent = Config.Bind("3. Customer offers", "New card: up to X percent market price (set paused or without packs)", 500f, "If the card is missing and its set is nearly finished (paused) or has no buyable packs: the customer is the only realistic source – up to this percent of the market price.");
            _buyNewMaxMoneyPct = Config.Bind("3. Customer offers", "New card: at most X percent of money", 10f, "Safety limit per card.");
            _buyNewAlwaysAsk = Config.Bind("3. Customer offers", "New card: always pay the asking price", true, "If the card is missing from the collection, the bot pays exactly the customer's asking price (never more) – regardless of the market price. Only the percent-of-money limit applies. Off: limits in percent of market price.");
            _dupStartPct = Config.Bind("3. Customer offers", "Duplicates: start offer (percent market price)", 60f, "First offer for cards we already have. Then the bot raises in steps up to the buy limit (as many steps as the customer has refusals left).");
            _dupStartCheapPct = Config.Bind("3. Customer offers", "Duplicates: start offer cheap cards (percent market price)", 50f, "Start offer for cheap duplicates (below the value below).");
            _dupCheapBelow = Config.Bind("3. Customer offers", "Duplicates: cheap means below ($)", 10f, "From this market price on the normal start offer applies.");
            _buyAlwaysBelow = Config.Bind("3. Customer offers", "Always buy cards below this value ($)", 2f, "Small stuff: just pay the asking price, no haggling.");
            _swapTolerance = Config.Bind("3. Customer offers", "Swap: small loss allowed ($)", 1f, "Also accept a swap if we get at most this much less (saves refusals on small stuff).");

            _dayEndEnabled = Config.Bind("4. End of day", "End day automatically", true, "At 9 PM, when no customer is left in the shop, start the next day.");
            _dayEndDelay = Config.Bind("4. End of day", "Wait before end of day (seconds)", 8f, "Short pause so openers are still emptied.");

            _cashierHelp = Config.Bind("5. Checkout", "Call a worker to the register", true, "When customers wait at an unstaffed register, an extra worker is set to checkout.");
            _cashierQueueThreshold = Config.Bind("5. Checkout", "From this many waiting customers", 2, "Threshold for checkout help.");
            _cashierReleaseSeconds = Config.Bind("5. Checkout", "Return worker after (seconds)", 30f, "The register must be empty this long before the worker gets the old task back.");

            _priceEnabled = Config.Bind("6. Prices", "Set prices automatically", true, "Set products and cards in the shop to market price + markup.");
            _priceItemPercent = Config.Bind("6. Prices", "Markup products (%)", 20f, "Packs, boxes and other products.");
            _priceCardPercent = Config.Bind("6. Prices", "Markup single cards + graded (%)", 20f, "Cards in the card display cases.");
            _priceInterval = Config.Bind("6. Prices", "Check interval (seconds)", 20f, "How often prices are checked.");
            _priceRound = Config.Bind("6. Prices", "Round prices", true, "Round to whole dollars from $1 (quick change at the register), below that to 10 cents.");
            _cashierStayLate = Config.Bind("5. Checkout", "Cashiers stay until the last customer", true, "Workers at the register only leave in the evening when all customers have paid.");

            BindCollectionConfig();
            BindUpgradeConfig();
            BindGradingConfig();
            BindStoreConfig();
            BindSortConfig();
            BindCheckoutConfig();
            BindEmergencyConfig();
            BindDayStateConfig();
            BindDashboardConfig();
            MigrateConfig();
            InitLanguage();
            Logger.LogInfo($"ShopBot {Info.Metadata.Version} loaded. F8 = on/off, F9 = dashboard. Language: {Lang.Names[Lang.Current]}");
        }

        private void Update()
        {
            HandleDashboardKey();
            if (_toggleKey.Value.IsDown())
            {
                _enabled.Value = !_enabled.Value;
                _lastAction = _enabled.Value ? T("Bot turned on") : T("Bot turned off");
                Logger.LogInfo(_lastAction);
            }
            // Spieler-Eingaben merken: Fenster-Aktionen nur, wenn du gerade nichts machst
            if (Input.anyKey || Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f)
                _lastInput = Time.unscaledTime;

            // Auto-Kasse läuft jedes Bild an allen freien Kassen (auch während Fenster offen sind oder du spielst)
            if (_enabled.Value && _readyFlag && !LoadingNextDay())
            {
                try { HandleAutoCheckout(); } catch (Exception e) { Logger.LogWarning("[Checkout]" + e.Message); }
            }

            if (!_enabled.Value || _busy) return;

            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;

            // Erst arbeiten, wenn der Laden fertig geladen ist (nie im Hauptmenü / Ladebildschirm)
            _readyFlag = GameReady();
            if (!_readyFlag) return;

            try
            {
                // Hintergrund-Aufgaben (brauchen keine freie Spielersteuerung)
                if (_priceEnabled.Value) HandlePrices();
                if (_cashierHelp.Value && !_autoCheckout.Value) HandleCashiers();   // Bot-Kasse ersetzt die Kassen-Hilfe
                HandleEconomy();

                if (!CanAct()) return;

                bool idle = Time.unscaledTime - _lastInput >= _idleSeconds.Value;

                // Tagesende hat Vorrang (sonst halten Tausch-Kunden/Opener den Tag ewig offen)
                if (_dayEndEnabled.Value)
                {
                    HandleDayEnd(idle);
                    if (_busy) return;
                }

                if (!idle)
                {
                    // Während du spielst: fertige Opener blitzschnell leeren (Fenster nur 1 Frame), damit Arbeiter nachfüllen können
                    if (_openerCollect.Value || _openerStartPartial.Value) HandleOpeners(collect: _openerCollect.Value, quick: true);
                    // Grading-Pakete nicht ewig liegen lassen, nur weil du gerade spielst
                    if (_gradedBoxOpen.Value && GradedBoxWaitingLong()) TryOpenGradedBox();
                    return;
                }
                // Ab hier öffnen sich Fenster -> nur wenn du gerade nichts drückst
                if (_tradeEnabled.Value && TryStartTrade()) return;
                if (_gradedBoxOpen.Value && TryOpenGradedBox()) return;
                if (_openerCollect.Value || _openerStartPartial.Value) HandleOpeners(collect: _openerCollect.Value);
            }
            catch (Exception e)
            {
                Logger.LogError("Error in bot tick:" + e);
            }
        }

        // WICHTIG: CSingleton<T>.Instance legt eine LEERE Kopie an, wenn es die echte noch nicht gibt
        // (z. B. im Hauptmenü). Das hat Tasten-Anzeigen kaputt gemacht. Darum nur lesen, nie anlegen.
        private static T S<T>() where T : Component => CSingleton<T>.IsSet ? CSingleton<T>.Instance : null;

        private float _lastInput;
        private float _readySince = -1f;
        private float StartDelay => _startDelay != null ? Mathf.Clamp(_startDelay.Value, 3f, 120f) : 10f;
        private ConfigEntry<float> _startDelay;

        private bool GameReady()
        {
            bool inLevel = false;
            try
            {
                var gm = S<CGameManager>();
                inLevel = gm != null && gm.m_IsGameLevel
                          && S<ShelfManager>() != null
                          && S<InteractionPlayerController>() != null
                          && S<CustomerManager>() != null
                          && !ScreenActive(S<LoadingScreen>());
            }
            catch { inLevel = false; }

            if (!inLevel)
            {
                if (_readySince >= 0f) { _readySince = -1f; _lastAction = T("Waiting for a loaded shop..."); ResetDayStateRestore(); }
                return false;
            }
            if (_readySince < 0f) { _readySince = Time.unscaledTime; _lastAction = F($"Shop loaded – bot starts in {StartDelay:0} s"); }
            return Time.unscaledTime - _readySince >= StartDelay;
        }

        // Nur handeln, wenn der Spieler gerade kein Menü / keinen Modus offen hat.
        private bool CanAct()
        {
            var ipc = S<InteractionPlayerController>();
            var cm = S<CustomerManager>();
            if (ipc == null || cm == null) return false;
            if (ipc.IsInUIMode()) return false;
            if (cm.m_IsPlayerTrading) return false;

            var t = Traverse.Create(ipc);
            foreach (var f in new[] { "m_IsCashCounterMode", "m_IsPhoneScreenMode", "m_IsWorkerInteractMode",
                                      "m_IsPlayTableGameMode", "m_IsDecoScreenMode", "m_IsExitingViewCardAlbumMode" })
            {
                var fld = t.Field(f);
                if (fld.FieldExists() && fld.GetValue<bool>()) return false;
            }

            if (ScreenActive(S<SettingScreen>())) return false;
            if (ScreenActive(S<PauseScreen>())) return false;
            if (ScreenActive(S<LoadingScreen>())) return false;
            if (ipc.m_ShowCardObtainedPage != null && ipc.m_ShowCardObtainedPage.IsScreenOpened()) return false;
            return true;
        }

        private static bool ScreenActive(Component screen)
        {
            if (screen == null) return false;
            var grp = Traverse.Create(screen).Field("m_ScreenGrp");
            if (!grp.FieldExists()) return false;
            var go = grp.GetValue<GameObject>();
            return go != null && go.activeSelf;
        }

        // ================= Pack-Opener =================
        private bool HandleOpeners(bool collect, bool quick = false)
        {
            var list = ShelfManager.GetAutoPackOpenerList();
            if (list == null) return false;

            foreach (var o in list.ToArray())
            {
                if (o == null || !o.gameObject.activeInHierarchy) continue;
                var boxed = Traverse.Create(o).Field("m_IsBoxedUp");
                if (boxed.FieldExists() && boxed.GetValue<bool>()) continue;

                int stored = o.GetStoredItemList().Count;
                bool processing = o.GetIsProcessing();

                // Fertig -> einsammeln
                if (collect && processing && stored == 0 && o.GetCompactCardDataAmountList().Count > 0)
                {
                    _openerIdle.Remove(o);
                    if (quick) CollectOpenerQuick(o);
                    else StartCoroutine(CollectOpener(o));
                    return true;
                }

                // Halbvoll und keiner füllt nach -> starten
                if (_openerStartPartial.Value && !processing && stored > 0)
                {
                    if (!_openerIdle.TryGetValue(o, out var st) || st.Key != stored)
                    {
                        _openerIdle[o] = new KeyValuePair<int, float>(stored, Time.time);
                    }
                    else if (Time.time - st.Value >= _openerPartialIdleSeconds.Value)
                    {
                        o.OnMouseButtonUp(); // spieleigener "Einschalten"-Klick
                        _openerIdle.Remove(o);
                        _statStarted++;
                        NoteF($"Opener started ({stored} packs)");
                        return true;
                    }
                }
                else
                {
                    _openerIdle.Remove(o);
                }
            }
            return false;
        }

        // ================= Tagesende =================
        private float _dayEndSince = -1f;

        private float _dayEndLogAt;

        // Wird jede Sekunde aufgerufen (wenn kein Menü offen ist).
        // Ende: 21 Uhr + kurze Pause. Wartet auf leeren Laden und darauf, dass du nichts drückst –
        // aber nie ewig: nach 90 Sek. auch mit Kunden, nach 120 Sek. auch wenn du gerade spielst.
        private void HandleDayEnd(bool idle)
        {
            var cm = S<CustomerManager>();
            var report = S<EndOfDayReportScreen>();
            if (!LightManager.GetHasDayEnded()) { _dayEndSince = -1f; return; }
            if (ReportOpen(report)) return;
            if (_dayEndSince < 0f) { _dayEndSince = Time.unscaledTime; return; }

            float waited = Time.unscaledTime - _dayEndSince;
            bool customers = cm != null && cm.HasCustomerInShop();
            // Steht noch ein Kassierer an der Kasse, lassen wir die letzten Kunden fertig bezahlen (max. 5 Min.)
            bool cashierWorking = false;
            var sm = S<ShelfManager>();
            // (auch die Bot-Kasse zählt: dort wird ebenfalls weiter kassiert)
            if (sm != null) foreach (var c in sm.m_CashierCounterList)
                    if (c != null && (c.IsMannedByNPC() || (_autoCheckout.Value && !c.IsMannedByPlayer())) && c.GetCustomerInQueueCount() > 0) cashierWorking = true;
            float maxCustomerWait = cashierWorking ? 300f : 90f;
            bool ready = waited >= _dayEndDelay.Value
                         && (!customers || waited >= maxCustomerWait)
                         && (idle || waited >= maxCustomerWait + 30f);
            if (!ready)
            {
                if (Time.unscaledTime >= _dayEndLogAt)
                {
                    _dayEndLogAt = Time.unscaledTime + 20f;
                    NoteF($"End of day: waiting {waited:0}s{(customers ? L(" – customers still in the shop") : L(""))}{(!idle ? L(" – you are active") : L(""))}");
                }
                return;
            }
            _dayEndSince = -1f;
            StartCoroutine(DoDayEnd());
        }

        private static bool ReportOpen(EndOfDayReportScreen r) =>
            r != null && Traverse.Create(r).Field("m_IsActive").GetValue<bool>();

        private IEnumerator DoDayEnd()
        {
            _busy = true;
            NoteF($"Day {CPlayerData.m_CurrentDay + 1} ended – starting next day");
            S<InteractionPlayerController>().ShowGoNextDayScreen(); // wie Enter drücken
            yield return new WaitForSecondsRealtime(4f);                         // Bericht kurz zeigen
            var report = S<EndOfDayReportScreen>();
            if (ReportOpen(report)) report.OnPressGoNextDay();
            yield return new WaitForSecondsRealtime(10f);                        // Ladebildschirm abwarten
            _busy = false;
        }

        // ================= Kasse =================
        private class Borrowed { public EWorkerTask OldSecondary; public float EmptySince = -1f; public InteractableCashierCounter Counter; }
        private readonly Dictionary<Worker, Borrowed> _borrowed = new Dictionary<Worker, Borrowed>();

        private void HandleCashiers()
        {
            var sm = S<ShelfManager>();
            if (sm == null) return;

            // Kassen-Turbo: Scan-Tempo der Kassierer (Spielwert 1.5) erhöhen
            if (_cashTurbo != null)
            {
                float speed = 1.5f * Mathf.Clamp(_cashTurbo.Value, 1f, 5f);
                foreach (var counter in sm.m_CashierCounterList)
                {
                    if (counter == null) continue;
                    var f = Traverse.Create(counter).Field("m_NPCScanItemSpeed");
                    if (f.FieldExists() && Mathf.Abs(f.GetValue<float>() - speed) > 0.01f) f.SetValue(speed);
                }
            }

            // Kassierer bleiben abends, bis alle Kunden bezahlt haben (Spiel-Flag "geht pünktlich" für Kassierer aus)
            if (_cashierStayLate.Value)
            {
                var all = WorkerManager.GetWorkerList();
                if (all != null)
                    foreach (var w in all)
                    {
                        if (w == null || !w.IsActive()) continue;
                        if (w.m_PrimaryTask != EWorkerTask.ManCounter && w.m_SecondaryTask != EWorkerTask.ManCounter && w.m_WorkerTask != EWorkerTask.ManCounter) continue;
                        var f = Traverse.Create(w).Field("m_InstantStopCounterWhenDayEnd");
                        if (f.FieldExists() && f.GetValue<bool>()) f.SetValue(false);
                    }
            }

            // Ausgeliehene Arbeiter zurückgeben, wenn ihre Kasse eine Weile leer ist
            foreach (var kv in new List<KeyValuePair<Worker, Borrowed>>(_borrowed))
            {
                var w = kv.Key; var b = kv.Value;
                if (w == null || !w.IsActive()) { _borrowed.Remove(w); continue; }
                bool empty = b.Counter == null || b.Counter.GetCustomerInQueueCount() == 0;
                if (!empty) { b.EmptySince = -1f; continue; }
                if (b.EmptySince < 0f) { b.EmptySince = Time.time; continue; }
                if (Time.time - b.EmptySince >= _cashierReleaseSeconds.Value)
                {
                    w.SetSecondaryTask(b.OldSecondary);
                    _borrowed.Remove(w);
                    NoteF($"Worker {WorkerName(w)} back to the old task");
                }
            }

            foreach (var counter in sm.m_CashierCounterList)
            {
                if (counter == null || !counter.gameObject.activeInHierarchy) continue;
                if (counter.IsMannedByNPC() || counter.IsMannedByPlayer()) continue;
                if (counter.GetCustomerInQueueCount() < _cashierQueueThreshold.Value) continue;

                // Schon jemand unterwegs zu dieser Kasse?
                bool someoneComing = false;
                foreach (var b in _borrowed.Values) if (b.Counter == counter) someoneComing = true;
                if (someoneComing) continue;

                var helper = PickCashierHelper();
                if (helper == null) return;
                _borrowed[helper] = new Borrowed { OldSecondary = helper.m_SecondaryTask, Counter = counter };
                helper.SetSecondaryTask(EWorkerTask.ManCounter);
                NoteF($"{counter.GetCustomerInQueueCount()} customers waiting – {WorkerName(helper)} helps at the register");
                return;
            }
        }

        // Schnellster Kassierer, der gerade nicht schon an der Kasse steht und keine Opener befüllt
        private Worker PickCashierHelper()
        {
            Worker best = null; float bestSpeed = float.MaxValue;
            var list = WorkerManager.GetWorkerList();
            if (list == null) return null;
            foreach (var w in list)
            {
                if (w == null || !w.IsActive() || _borrowed.ContainsKey(w)) continue;
                if (w.m_PrimaryTask == EWorkerTask.ManCounter || w.m_SecondaryTask == EWorkerTask.ManCounter) continue;
                if (w.m_PrimaryTask == EWorkerTask.RefillCardOpener) continue; // Opener sind wichtiger
                if (w.m_PrimaryTask == EWorkerTask.Fired || w.m_PrimaryTask == EWorkerTask.GoBackHome) continue;
                float speed = w.GetWorkerData() != null ? w.GetWorkerData().checkoutSpeed : 99f; // kleiner = schneller
                if (speed < bestSpeed) { bestSpeed = speed; best = w; }
            }
            return best;
        }

        private static string WorkerName(Worker w)
        {
            try { return w.GetWorkerData().GetName(); } catch { return "#" + w.m_WorkerIndex; }
        }

        // ================= Preise =================
        private float _priceTimer = 999f;

        private void HandlePrices()
        {
            _priceTimer += 1f;
            if (_priceTimer < _priceInterval.Value) return;
            _priceTimer = 0f;

            int changedItems = 0, changedCards = 0;
            float itemMul = 1f + _priceItemPercent.Value / 100f;
            float cardMul = 1f + _priceCardPercent.Value / 100f;

            // Produkte in Regalen
            var seen = new HashSet<EItemType>();
            var shelves = ShelfManager.GetShelfList();
            if (shelves != null)
            {
                foreach (var shelf in shelves)
                {
                    if (shelf == null || shelf.m_ItemNotForSale) continue;
                    foreach (var comp in shelf.GetItemCompartmentList())
                    {
                        if (comp == null) continue;
                        var type = comp.GetItemType();
                        if (type == EItemType.None || !seen.Add(type)) continue;
                        float market = CPlayerData.GetItemMarketPrice(type);
                        if (market <= 0f) continue;
                        float mul = _itemMarkup.TryGetValue(type, out var pct) ? 1f + pct / 100f : itemMul; // ggf. wegen Beschwerden gesenkt
                        float target = RoundPrice(market * mul);
                        if (Mathf.Abs(CPlayerData.GetItemPrice(type, preventZero: false) - target) > 0.009f)
                        {
                            CPlayerData.SetItemPrice(type, target);
                            changedItems++;
                        }
                    }
                }
            }

            // Einzelkarten + gegradete Karten in Vitrinen
            var cardShelves = ShelfManager.GetCardShelfList();
            if (cardShelves != null)
            {
                foreach (var cs in cardShelves)
                {
                    if (cs == null) continue;
                    foreach (var comp in cs.GetCardCompartmentList())
                    {
                        if (comp == null || comp.m_ItemNotForSale || comp.m_StoredCardList.Count == 0) continue;
                        var card3d = comp.m_StoredCardList[0];
                        if (card3d == null) continue;
                        var data = card3d.m_Card3dUI.m_CardUI.GetCardData();
                        if (data == null) continue;
                        float market = CPlayerData.GetCardMarketPrice(data);
                        if (market <= 0f) continue;
                        float target = RoundPrice(market * cardMul);
                        if (Mathf.Abs(CPlayerData.GetCardPrice(data) - target) <= 0.009f)
                        {
                            // Preis stimmt schon – nur Schild als "gesetzt" markieren, nicht neu melden
                            if (!comp.HasSetPrice()) foreach (var tag in comp.m_InteractablePriceTagList) tag.SetPriceChecked(true);
                        }
                        else
                        {
                            CPlayerData.SetCardPrice(data, target);
                            foreach (var tag in comp.m_InteractablePriceTagList) tag.SetPriceChecked(true);
                            comp.SetPriceTagItemPriceText(data);
                            changedCards++;
                        }
                    }
                }
            }

            if (changedItems + changedCards > 0)
                NoteF($"Prices adjusted: {changedItems} products, {changedCards} cards (+{_priceItemPercent.Value:0}% / +{_priceCardPercent.Value:0}%)");
        }

        // Runden: ab 1 auf ganze Beträge (schnelles Wechselgeld), darunter auf 10 Cent (sonst wären Billigkarten 10x zu teuer)
        private float RoundPrice(float p)
        {
            float rate = GameInstance.GetCurrencyConversionRate();
            if (rate <= 0f) rate = 1f;
            float shown = p * rate;
            if (_priceRound.Value)
            {
                if (shown >= 1f) shown = Mathf.Round(shown);                                             // ganze Dollar/Euro
                else shown = Mathf.Max(0.1f, Mathf.Round(shown * 10f) / 10f);                            // 10 Cent
            }
            float result = shown / rate;
            float div = GameInstance.GetCurrencyRoundDivideAmount();
            if (div <= 0f) div = 100f;
            return Mathf.Round(result * div) / div;
        }

        // ================= Grading-Pakete öffnen =================
        // Fertig gegradete Karten kommen als Paket. Öffnen wie im Spiel (CardGrade bekommt alles mit: Zertifikate),
        // dann wie "Rechtsklick = überspringen" beenden. Karten bleiben im Album statt in der Hand.
        private bool TryOpenGradedBox()
        {
            var boxes = RestockManager.GetCardPackagingBoxList();
            if (boxes == null || boxes.Count == 0) return false;
            var ipc = S<InteractionPlayerController>();
            var seq = FindGradedSequence();
            if (ipc == null || seq == null) { GradedWhy($"{boxes.Count} packages, but game parts missing (ipc {ipc != null}, seq {seq != null})"); return false; }
            // Das Spiel holt sich das Fenster beim Öffnen per CSingleton.Instance – das klappt nur, wenn es aktiv in der Szene ist
            if (!seq.gameObject.activeInHierarchy && !CSingleton<GradedCardOpeningSequence>.IsSet) { GradedWhy("opening window is inactive"); return false; }
            var ti = Traverse.Create(ipc);
            if (ti.Field("m_IsHoldBoxMode").GetValue<bool>() || ti.Field("m_IsHoldCardMode").GetValue<bool>()) { GradedWhy("you are holding something"); return false; }
            // Das Spiel setzt m_IsScreenActive nach dem Öffnen nie zurück -> echte Sichtbarkeit des Fensters prüfen
            if (seq.m_ScreenGrp != null && seq.m_ScreenGrp.activeSelf) { GradedWhy("opening window is still open"); return false; }

            int active = 0, opening = 0, held = 0, emptyList = 0;
            InteractablePackagingBox_Card box = null;
            foreach (var b in boxes)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                active++;
                var tb = Traverse.Create(b);
                if (tb.Field("m_IsOpeningBox").GetValue<bool>()) { opening++; continue; }
                if (tb.Field("m_IsBeingHold").GetValue<bool>()) { held++; continue; }
                if ((tb.Field("m_StoredCardDataList").GetValue<List<CardData>>()?.Count ?? 0) <= 0) { emptyList++; continue; }
                box = b; break;
            }
            if (box == null)
            {
                if (active > 0) GradedWhy($"{active} packages here, none suitable (opening {opening}, carried {held}, without cards {emptyList})");
                return false;
            }
            StartCoroutine(OpenGradedBox(box, seq));
            return true;
        }

        // Das Öffnungsfenster meldet sich erst beim ersten Öffnen als Singleton an (CSingleton lädt "faul").
        // Darum: angemeldete Instanz nehmen, sonst das vorhandene Objekt in der Szene suchen (nie ein neues anlegen).
        private GradedCardOpeningSequence _gradedSeq;
        private GradedCardOpeningSequence FindGradedSequence()
        {
            if (_gradedSeq != null) return _gradedSeq;
            var s = S<GradedCardOpeningSequence>();
            if (s == null)
            {
                try { s = UnityEngine.Object.FindObjectOfType<GradedCardOpeningSequence>(); } catch { }
            }
            if (s == null)
            {
                try
                {
                    foreach (var x in Resources.FindObjectsOfTypeAll<GradedCardOpeningSequence>())
                        if (x != null && x.gameObject.scene.IsValid()) { s = x; break; }
                }
                catch { }
            }
            if (s != null) Logger.LogInfo("[Grading package] Opening window found:" + s.gameObject.name);
            _gradedSeq = s;
            return s;
        }

        private float _gradedWhyAt;
        private void GradedWhy(string why)
        {
            if (Time.unscaledTime < _gradedWhyAt) return;
            _gradedWhyAt = Time.unscaledTime + 60f;
            Logger.LogInfo("[Grading package] not opened:" + why);
        }

        private float _gradedBoxSeenAt = -1f;

        // Liegt ein Grading-Paket schon länger als 60 s, wird es auch geöffnet, während du spielst (Fenster ~2 s)
        private bool GradedBoxWaitingLong()
        {
            var boxes = RestockManager.GetCardPackagingBoxList();
            bool any = boxes != null && boxes.Any(b => b != null && b.gameObject.activeInHierarchy);
            if (!any) { _gradedBoxSeenAt = -1f; return false; }
            if (_gradedBoxSeenAt < 0f) _gradedBoxSeenAt = Time.unscaledTime;
            return Time.unscaledTime - _gradedBoxSeenAt >= 60f;
        }

        private static void SkipCardGradeRevealRest()
        {
            var reveal = AccessTools.TypeByName("CardGradeOverhaul.Reveal");
            if (reveal == null) return;
            try { AccessTools.TypeByName("CardGradeOverhaul.Summary")?.GetMethod("Hide", AccessTools.all)?.Invoke(null, null); } catch { }
            try { reveal.GetMethod("RestToAlbum", AccessTools.all)?.Invoke(null, null); } catch { }
            try { reveal.GetField("Multi", AccessTools.all)?.SetValue(null, false); } catch { }
        }

        private IEnumerator OpenGradedBox(InteractablePackagingBox_Card box, GradedCardOpeningSequence seq)
        {
            _busy = true;
            var ts = Traverse.Create(seq);
            bool oldHold = ts.Field("m_HoldCardAtEnd").GetValue<bool>();
            int count = Traverse.Create(box).Field("m_StoredCardDataList").GetValue<List<CardData>>()?.Count ?? 0;
            try
            {
                ts.Field("m_HoldCardAtEnd").SetValue(false);   // Karten ins Album, nicht in die Hand
                box.OnPressOpenBox();                            // spieleigenes Öffnen (fügt die Karten hinzu)
            }
            catch (Exception e) { Logger.LogError("Open grading package:" + e); ts.Field("m_HoldCardAtEnd").SetValue(oldHold); _busy = false; yield break; }

            yield return new WaitForSecondsRealtime(1.2f);
            try
            {
                // CardGrade Overhaul zeigt große Pakete in mehreren Runden. Alle Karten sind beim Öffnen schon im Album –
                // die restlichen Runden verwerfen (wie "Rest überspringen"), sonst bleibt das Fenster offen.
                SkipCardGradeRevealRest();
                // wie Rechtsklick "Überspringen"
                if (ts.Field("m_StateIndex").GetValue<int>() < 5)
                {
                    ts.Field("m_StateIndex").SetValue(5);
                    seq.m_HighValueCardIcon?.SetActive(false);
                    ts.Field("m_IsGetHighValueCard").SetValue(false);
                    seq.m_CardOpeningSequenceUI?.HideGradedCardValue();
                    if (ts.Method("CardRevealEnd").GetValue() is IEnumerator end) seq.StartCoroutine(end);
                }
            }
            catch (Exception e) { Logger.LogError("Finish grading package:" + e); }

            yield return new WaitForSecondsRealtime(0.6f);
            // Hängt das Fenster noch (z. B. nächste Runde der Mod gestartet)? Noch einmal schließen.
            try
            {
                if (ts.Field("m_IsScreenActive").GetValue<bool>() && seq.m_ScreenGrp != null && seq.m_ScreenGrp.activeSelf)
                {
                    SkipCardGradeRevealRest();
                    ts.Field("m_StateIndex").SetValue(5);
                    if (ts.Method("CardRevealEnd").GetValue() is IEnumerator end2) seq.StartCoroutine(end2);
                }
            }
            catch (Exception e) { Logger.LogWarning("Close grading package again:" + e.Message); }
            yield return new WaitForSecondsRealtime(0.4f);
            ts.Field("m_HoldCardAtEnd").SetValue(oldHold);
            _statGradedOpened += count;
            _gradedBoxSeenAt = -1f;
            NoteF($"Grading package opened: {count} graded cards to the album");
            _bulkTimer = 290f; // gleich danach in die Kartenablage (Verkauf)
            yield return new WaitForSecondsRealtime(0.5f);
            _busy = false;
        }

        // Einsammeln ohne sichtbares Fenster: öffnen + im selben Frame schließen (gibt XP, Karten ins Album)
        private void CollectOpenerQuick(InteractableAutoPackOpener o)
        {
            var ipc = S<InteractionPlayerController>();
            if (ipc == null || ipc.IsInUIMode()) return;
            int packs = o.GetPackOpenedCount();
            o.OnMouseButtonUp();
            var page = ipc.m_ShowCardObtainedPage;
            if (page != null && page.IsScreenOpened()) page.CloseScreen();
            _statCollected++;
            NoteF($"Opener collected ({packs} packs)");
        }

        private IEnumerator CollectOpener(InteractableAutoPackOpener o)
        {
            _busy = true;
            int packs = o.GetPackOpenedCount();
            o.OnMouseButtonUp(); // spieleigener "Einsammeln"-Klick -> Karten ins Album
            _statCollected++;
            NoteF($"Opener collected ({packs} packs)");

            yield return new WaitForSecondsRealtime(Mathf.Max(0.3f, _openerShowSeconds.Value));
            var page = S<InteractionPlayerController>()?.m_ShowCardObtainedPage;
            if (page != null && page.IsScreenOpened()) page.CloseScreen(); // gibt auch die Shop-XP
            yield return new WaitForSecondsRealtime(0.3f);
            _busy = false;
        }

        // ================= Kunden-Angebote =================
        private bool TryStartTrade()
        {
            var cm = S<CustomerManager>();
            var customers = Traverse.Create(cm).Field("m_CustomerList").GetValue<List<Customer>>();
            if (customers == null) return false;

            foreach (var c in customers)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.m_CurrentState != ECustomerState.WaitingToTradeCard) continue;
                StartCoroutine(DoTrade(c));
                return true;
            }
            return false;
        }

        private IEnumerator DoTrade(Customer c)
        {
            _busy = true;
            var cm = S<CustomerManager>();
            var s = cm.m_CustomerTradeCardScreen;
            bool opened = false;
            try { c.OnMousePress(); opened = true; }
            catch (Exception e) { Logger.LogError("Could not open offer:" + e); }

            if (opened)
            {
                yield return new WaitForSecondsRealtime(_stepDelay.Value);
                var t = Traverse.Create(s);
                bool isSwap = t.Field("m_IsTrading").GetValue<bool>();
                var cardL = t.Field("m_CardData_L").GetValue<CardData>();

                if (isSwap)
                    yield return HandleSwap(s, t, cardL);
                else
                    yield return HandleBuy(s, t, cardL);

                // Sicherheitsnetz: Fenster nie offen lassen
                yield return new WaitForSecondsRealtime(0.3f);
                if (s.IsScreenOpened())
                {
                    if (t.Field("m_HasAccepted").GetValue<bool>()) s.OnPressAccept();
                    else s.OnPressDecline();
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
            _busy = false;
        }

        private IEnumerator HandleSwap(CustomerTradeCardScreen s, Traverse t, CardData get)
        {
            var give = t.Field("m_CardData_R").GetValue<CardData>();
            float pGet = CPlayerData.GetCardMarketPrice(get);
            float pGive = give != null ? CPlayerData.GetCardMarketPrice(give) : float.MaxValue;
            // Nur Duplikate abgeben: die letzte normale Karte einer Sorte bleibt immer in der Sammlung
            bool own = give != null && (give.cardGrade == 0 ? CPlayerData.GetCardAmount(give) > 1 : CPlayerData.HasGradedCardInAlbum(give));
            // Bekommen wir eine Karte, die uns noch fehlt, ist der Tausch auch bei etwas weniger Wert gut (neu für die Sammlung)
            // Neue Karte: abgeben darf man (nur Doppelte!) bis zu dem Wert, den wir auch beim Ankauf zahlen würden (CardWorth)
            float worthGet = CardWorth(get, pGet, out bool isNewForUs, out bool hardGet);
            bool good = _swapEnabled.Value && own && (isNewForUs
                ? pGive <= Mathf.Max(worthGet, pGet) + _swapTolerance.Value
                : (pGet > pGive * (1f + _swapMinGainPercent.Value / 100f) || pGive - pGet <= _swapTolerance.Value));

            if (good)
            {
                s.OnPressAccept();
                _statSwapped++;
                NoteF($"Swap accepted: got {Price(pGet)} / gave {Price(pGive)}{(isNewForUs ? L(" (new card for the collection!)") : L(""))}");
                yield return new WaitForSecondsRealtime(_stepDelay.Value);
                if (s.IsScreenOpened() && t.Field("m_HasAccepted").GetValue<bool>()) s.OnPressAccept(); // "Fertig"
            }
            else
            {
                s.OnPressDecline();
                _statDeclined++;
                NoteF($"Swap declined: got {Price(pGet)} / gave {Price(pGive)}{(own ? L("") : L(" (would be our last copy)"))}");
            }
        }

        private IEnumerator HandleBuy(CustomerTradeCardScreen s, Traverse t, CardData card)
        {
            // Annahme-Formel des Spiels (CustomerTradeCardScreen.OnPressAccept):
            //  - Chance ~ Gebot/Wunschpreis, mit Abzügen je weiter darunter; unter Marktpreis -5..15; je Ablehnung -5..15,
            //    und -10..25 je Ablehnung zusätzlich, wenn das Gebot nicht höher ist als das letzte. Gebot >= Wunschpreis = 100 %.
            //  - Lehnt der Kunde ab, senkt er manchmal seinen Wunschpreis Richtung Gebot (Feilschen).
            //  - m_MaxDeclineCount = wie oft er noch ablehnt, bevor er geht (0-4, auslesbar).
            // Strategie:
            //  - NEUE Karte: genau den Wunschpreis zahlen (nie mehr), nur Geld-Grenze.
            //  - DOPPELTE: bei 60 % Marktpreis starten (billige Karten 50 %), in Schritten bis 95 % steigern –
            //    so viele Schritte, wie der Kunde Ablehnungen übrig hat; jedes Gebot höher als das letzte;
            //    senkt er seinen Wunschpreis unter unser nächstes Gebot -> Wunschpreis zahlen.
            bool isNew = false, hard = false;
            float lastOffer = -1f;
            for (int round = 0; round < 7; round++)
            {
                if (!s.IsScreenOpened()) { _statDeclined++; NoteF($"Customer left (last offer {Price(lastOffer)})"); yield break; }

                float ask = t.Field("m_SellCardAskPrice").GetValue<float>();
                float market = t.Field("m_SellCardMarketPrice").GetValue<float>();
                if (market <= 0f) market = CPlayerData.GetCardMarketPrice(card);
                float cap = CardWorth(card, market, out isNew, out hard);
                float askPay = Mathf.Ceil(ask * 100f - 0.001f) / 100f;           // genau der Wunschpreis (auf den Cent aufgerundet)
                int declinesLeft = t.Field("m_MaxDeclineCount").GetValue<int>();
                float moneyCap = (float)Math.Max(0.0, CPlayerData.m_CoinAmountDouble - Reserve) * _buyNewMaxMoneyPct.Value / 100f;

                float offer;
                if (isNew && _buyNewAlwaysAsk.Value)
                {
                    if (askPay > moneyCap)
                    {
                        s.OnPressDecline(); _statDeclined++;
                        NoteF($"Purchase declined: new card, but {Price(ask)} is more than {_buyNewMaxMoneyPct.Value:0} % of the money");
                        yield break;
                    }
                    offer = askPay;                                               // neue Karte -> Wunschpreis
                }
                else if (market < _buyAlwaysBelow.Value)
                    offer = askPay;                                               // Kleinkram: kein Feilschen
                else
                {
                    // geplantes Gebot dieser Runde: Start (oder gleich Maximum, wenn der Kunde keine Ablehnung mehr übrig hat), dann Schritte
                    float planned = lastOffer < 0f
                        ? (declinesLeft <= 0 ? cap : Mathf.Min(DupStart(market), cap))
                        : NextDupOffer(lastOffer, cap, declinesLeft);
                    if (askPay <= planned)
                        offer = askPay;                                           // Wunschpreis liegt schon darunter -> den zahlen
                    else if (cap < ask * 0.5f)
                    {
                        s.OnPressDecline(); _statDeclined++;                      // aussichtslos: Wunschpreis mehr als doppelt so hoch wie unser Maximum
                        NoteF($"Purchase declined: wants {Price(ask)}, market {Price(market)}, worth at most {Price(cap)} to us");
                        yield break;
                    }
                    else offer = planned;
                }

                if (lastOffer >= 0f && offer <= lastOffer + 0.005f)
                {
                    s.OnPressDecline(); _statDeclined++;
                    NoteF($"Purchase declined: wants {Price(ask)}, market {Price(market)}, our maximum {Price(cap)} reached");
                    yield break;
                }
                if (CPlayerData.m_CoinAmountDouble < offer)
                {
                    s.OnPressDecline(); _statDeclined++;
                    Note("Purchase cancelled: not enough money");
                    yield break;
                }

                SetPrice(s, offer);
                lastOffer = offer;
                yield return new WaitForSecondsRealtime(_stepDelay.Value * 0.5f);
                s.OnPressAccept();
                yield return new WaitForSecondsRealtime(_stepDelay.Value);

                if (t.Field("m_HasAccepted").GetValue<bool>())
                {
                    _statBought++;
                    _statSpent += offer;
                    NoteF($"Bought for {Price(offer)} (market {Price(market)}, {offer / Mathf.Max(0.01f, market) * 100f:0}%, round {round + 1}){(isNew ? L(" – new card for the collection!") : L(""))}");
                    if (s.IsScreenOpened()) s.OnPressAccept(); // "Fertig"
                    yield break;
                }
            }
            if (s.IsScreenOpened()) { s.OnPressDecline(); _statDeclined++; Note("Purchase declined (customer too expensive)"); }
        }

        // Startgebot für Doppelte: 60 % Marktpreis, billige Karten 50 %
        private float DupStart(float market) =>
            market * (market < _dupCheapBelow.Value ? _dupStartCheapPct.Value : _dupStartPct.Value) / 100f;

        // Nächstes Gebot: gleichmäßig zum Maximum, so dass beim letzten möglichen Versuch genau das Maximum geboten wird
        private static float NextDupOffer(float last, float cap, int declinesLeft) =>
            Mathf.Ceil((last + (cap - last) / Mathf.Max(1, declinesLeft + 1)) * 100f) / 100f;

        private static void SetPrice(CustomerTradeCardScreen s, float price)
        {
            float shown = Mathf.Ceil(price * GameInstance.GetCurrencyConversionRate() * 100f - 0.001f) / 100f;   // nie unter dem Wunschpreis runden
            s.OnInputTextUpdated(shown.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private static string Price(float p) =>
            p == float.MaxValue ? "?" : GameInstance.GetPriceString(p);

        // ================= Messages (history + log) =================
        // UI texts: T("English") / F($"English {x}"). Messages: Note("...") / NoteF($"... {x}").
        // The log file always gets English, the dashboard shows the chosen language (also for old entries).
        private static string T(string s) => Lang.T(s);
        private static string F(FormattableString fs) => Lang.F(fs);
        private static LStr L(string s) => new LStr(s);

        private string _lastTpl = "-"; private object[] _lastArgs;

        private void Note(string msg) => NoteRaw(msg, null);
        private void NoteF(FormattableString fs) => NoteRaw(fs.Format, fs.GetArguments());

        private void NoteRaw(string tpl, object[] args)
        {
            _lastTpl = tpl; _lastArgs = args; _lastAction = null;
            Logger.LogInfo(Lang.Source(tpl, args));
            RecordHistory(tpl, args);
        }

        // Message in the current language
        private static string MsgText(string tpl, object[] args)
        {
            string t = Lang.T(tpl);
            if (args == null) return t;
            try { return string.Format(t, args); }
            catch (FormatException) { try { return string.Format(tpl, args); } catch { return tpl; } }
        }

        // ================= Anzeige =================
        private GUIStyle _style;

        private void OnGUI()
        {
            if (_dashOpen) { DrawDashboard(); return; }
            if (!_showOverlay.Value) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                _style.normal.textColor = Color.white;
            }
            // Compact line – details in the dashboard (F9)
            string state = _enabled.Value ? "<color=#5f5>" + T("ON") + "</color>" : "<color=#f55>" + T("OFF") + "</color>";
            string text = $"<b>ShopBot</b> {state}  <color=#aaa>{T("F8 on/off · F9 dashboard")}</color>\n{_lastAction ?? MsgText(_lastTpl, _lastArgs)}";
            GUI.Label(new Rect(10, 40, 1400, 50), text, _style);
        }
    }
}
