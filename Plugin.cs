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
    [BepInPlugin("ratta.shopbot", "ShopBot", "0.4.2")]
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
        private ConfigEntry<float> _buyMaxPercent2, _buyMaxNewPercent, _buyAlwaysBelow, _swapTolerance;

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
            _enabled = Config.Bind("1. Allgemein", "Bot aktiv", true, "Bot ein-/ausschalten.");
            _toggleKey = Config.Bind("1. Allgemein", "Hotkey An/Aus", new KeyboardShortcut(KeyCode.F8), "Taste zum Ein-/Ausschalten.");
            _showOverlay = Config.Bind("1. Allgemein", "Status anzeigen", true, "Kleine Statuszeile oben links.");
            _startDelay = Config.Bind("1. Allgemein", "Startpause nach dem Laden (Sekunden)", 10f, "So lange wartet der Bot nach dem Laden des Spielstands, bevor er loslegt.");
            _idleSeconds = Config.Bind("1. Allgemein", "Fenster nur wenn ich X Sek. nichts drücke", 8f,
                "Kunden-Angebote, Opener-Ergebnis und Tagesende öffnen Fenster und sperren kurz die Steuerung. Das passiert nur, wenn du so lange keine Taste/Maus benutzt hast.");

            _openerCollect = Config.Bind("2. Pack-Opener", "Fertige Opener einsammeln", true, "Holt die Karten aus fertigen Pack-Openern.");
            _openerShowSeconds = Config.Bind("2. Pack-Opener", "Ergebnis anzeigen (Sekunden)", 2.5f, "Wie lange die Karten-Übersicht offen bleibt.");
            _openerStartPartial = Config.Bind("2. Pack-Opener", "Halbvolle Opener starten", true, "Startet Opener, die Packs haben, aber eine Weile nicht weiter befüllt wurden.");
            _gradedBoxOpen = Config.Bind("2. Pack-Opener", "Grading-Pakete automatisch öffnen", true, "Fertig gegradete Karten (Pakete) öffnen – Karten kommen ins Album und werden danach verkauft (PSA 10 nur Duplikate).");
            _openerPartialIdleSeconds = Config.Bind("2. Pack-Opener", "Wartezeit halbvoll (Sekunden)", 45f, "So lange darf sich die Pack-Anzahl nicht ändern, bevor gestartet wird.");

            _tradeEnabled = Config.Bind("3. Kunden-Angebote", "Angebote bearbeiten", true, "Kunden, die Karten verkaufen/tauschen wollen, automatisch bedienen.");
            _buyStartPercent = Config.Bind("3. Kunden-Angebote", "Ankauf Startgebot (% Marktpreis)", 50f, "Erstes Gebot beim Verhandeln.");
            _buyMaxPercent = Config.Bind("3. Kunden-Angebote", "Ankauf Maximum (% Marktpreis)", 70f, "Mehr zahlt der Bot nie.");
            _buyStepPercent = Config.Bind("3. Kunden-Angebote", "Ankauf Schritt (% Marktpreis)", 5f, "Um so viel wird pro Runde erhöht.");
            _swapEnabled = Config.Bind("3. Kunden-Angebote", "Tausch bearbeiten", true, "Tauschangebote annehmen, wenn wir gewinnen.");
            _swapMinGainPercent = Config.Bind("3. Kunden-Angebote", "Tausch Mindestgewinn (%)", 0f, "Erhaltene Karte muss so viel % mehr wert sein als die abgegebene.");
            _stepDelay = Config.Bind("3. Kunden-Angebote", "Pause zwischen Schritten (Sekunden)", 0.8f, "Damit man sieht, was passiert.");
            _buyMaxPercent2 = Config.Bind("3. Kunden-Angebote", "Ankauf Limit normale Karten (% Marktpreis)", 95f, "Höchstens so viel zahlt der Bot. Weiterverkauf mit +20 % bleibt Gewinn.");
            _buyMaxNewPercent = Config.Bind("3. Kunden-Angebote", "Ankauf Limit neue Karten (% Marktpreis)", 125f, "Für Karten, die in der Sammlung noch fehlen, zahlt der Bot mehr.");
            _buyAlwaysBelow = Config.Bind("3. Kunden-Angebote", "Karten unter diesem Wert immer kaufen ($)", 2f, "Kleinkram: einfach den Wunschpreis zahlen, kein Verhandeln.");
            _swapTolerance = Config.Bind("3. Kunden-Angebote", "Tausch: kleiner Verlust erlaubt ($)", 1f, "Tausch auch annehmen, wenn wir höchstens so viel weniger bekommen (spart Ablehnungen bei Kleinkram).");

            _dayEndEnabled = Config.Bind("4. Tagesende", "Tag automatisch beenden", true, "Um 21 Uhr, wenn kein Kunde mehr im Laden ist, den nächsten Tag starten.");
            _dayEndDelay = Config.Bind("4. Tagesende", "Wartezeit vor Tagesende (Sekunden)", 8f, "Kurze Pause, damit Opener noch geleert werden.");

            _cashierHelp = Config.Bind("5. Kasse", "Arbeiter an Kasse holen", true, "Wenn Kunden an einer unbesetzten Kasse warten, wird ein Arbeiter zusätzlich auf Kasse gesetzt.");
            _cashierQueueThreshold = Config.Bind("5. Kasse", "Ab so vielen wartenden Kunden", 2, "Schwelle für die Kassen-Hilfe.");
            _cashierReleaseSeconds = Config.Bind("5. Kasse", "Arbeiter zurückgeben nach (Sekunden)", 30f, "So lange muss die Kasse leer sein, bevor der Arbeiter seine alte Aufgabe zurückbekommt.");

            _priceEnabled = Config.Bind("6. Preise", "Preise automatisch setzen", true, "Produkte und Karten im Laden auf Marktpreis + Aufschlag setzen.");
            _priceItemPercent = Config.Bind("6. Preise", "Aufschlag Produkte (%)", 20f, "Packs, Boxen und andere Produkte.");
            _priceCardPercent = Config.Bind("6. Preise", "Aufschlag Einzelkarten + gegradete (%)", 20f, "Karten in den Karten-Vitrinen.");
            _priceInterval = Config.Bind("6. Preise", "Prüfintervall (Sekunden)", 20f, "Wie oft Preise geprüft werden.");
            _priceRound = Config.Bind("6. Preise", "Preise runden", true, "Ab $1 auf ganze Dollar runden (schnelles Wechselgeld an der Kasse), darunter auf 10 Cent.");
            _cashierStayLate = Config.Bind("5. Kasse", "Kassierer bleiben bis zum letzten Kunden", true, "Arbeiter an der Kasse gehen abends erst, wenn alle Kunden bezahlt haben.");

            BindCollectionConfig();
            BindUpgradeConfig();
            BindGradingConfig();
            BindStoreConfig();
            BindSortConfig();
            BindCheckoutConfig();
            BindEmergencyConfig();
            Logger.LogInfo("ShopBot geladen. F8 = An/Aus.");
        }

        private void Update()
        {
            HandleDashboardKey();
            if (_toggleKey.Value.IsDown())
            {
                _enabled.Value = !_enabled.Value;
                _lastAction = _enabled.Value ? "Bot eingeschaltet" : "Bot ausgeschaltet";
                Logger.LogInfo(_lastAction);
            }
            // Spieler-Eingaben merken: Fenster-Aktionen nur, wenn du gerade nichts machst
            if (Input.anyKey || Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f)
                _lastInput = Time.unscaledTime;

            // Auto-Kasse läuft jedes Bild an allen freien Kassen (auch während Fenster offen sind oder du spielst)
            if (_enabled.Value && _readyFlag && !LoadingNextDay())
            {
                try { HandleAutoCheckout(); } catch (Exception e) { Logger.LogWarning("[Kasse] " + e.Message); }
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
                Logger.LogError("Fehler im Bot-Takt: " + e);
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
                if (_readySince >= 0f) { _readySince = -1f; _lastAction = "Warte auf geladenen Laden..."; }
                return false;
            }
            if (_readySince < 0f) { _readySince = Time.unscaledTime; _lastAction = $"Laden geladen – Bot startet in {StartDelay:0} Sek."; }
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
                        Note($"Opener gestartet ({stored} Packs)");
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
                    Note($"Tagesende: warte {waited:0}s" + (customers ? " – noch Kunden im Laden" : "") + (!idle ? " – du bist aktiv" : ""));
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
            Note($"Tag {CPlayerData.m_CurrentDay + 1} beendet – starte nächsten Tag");
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
                    Note($"Arbeiter {WorkerName(w)} zurück an alte Aufgabe");
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
                Note($"{counter.GetCustomerInQueueCount()} Kunden warten – {WorkerName(helper)} hilft an der Kasse");
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
                Note($"Preise angepasst: {changedItems} Produkte, {changedCards} Karten (+{_priceItemPercent.Value:0}% / +{_priceCardPercent.Value:0}%)");
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
            if (ipc == null || seq == null) { GradedWhy($"{boxes.Count} Pakete, aber Spielteile fehlen (ipc {ipc != null}, seq {seq != null})"); return false; }
            // Das Spiel holt sich das Fenster beim Öffnen per CSingleton.Instance – das klappt nur, wenn es aktiv in der Szene ist
            if (!seq.gameObject.activeInHierarchy && !CSingleton<GradedCardOpeningSequence>.IsSet) { GradedWhy("Öffnungsfenster ist inaktiv"); return false; }
            var ti = Traverse.Create(ipc);
            if (ti.Field("m_IsHoldBoxMode").GetValue<bool>() || ti.Field("m_IsHoldCardMode").GetValue<bool>()) { GradedWhy("du hältst gerade etwas in der Hand"); return false; }
            // Das Spiel setzt m_IsScreenActive nach dem Öffnen nie zurück -> echte Sichtbarkeit des Fensters prüfen
            if (seq.m_ScreenGrp != null && seq.m_ScreenGrp.activeSelf) { GradedWhy("Öffnungsfenster ist noch offen"); return false; }

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
                if (active > 0) GradedWhy($"{active} Pakete da, keins passend (öffnet gerade {opening}, getragen {held}, ohne Karten {emptyList})");
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
            if (s != null) Logger.LogInfo("[Grading-Paket] Öffnungsfenster gefunden: " + s.gameObject.name);
            _gradedSeq = s;
            return s;
        }

        private float _gradedWhyAt;
        private void GradedWhy(string why)
        {
            if (Time.unscaledTime < _gradedWhyAt) return;
            _gradedWhyAt = Time.unscaledTime + 60f;
            Logger.LogInfo("[Grading-Paket] nicht geöffnet: " + why);
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
            catch (Exception e) { Logger.LogError("Grading-Paket öffnen: " + e); ts.Field("m_HoldCardAtEnd").SetValue(oldHold); _busy = false; yield break; }

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
            catch (Exception e) { Logger.LogError("Grading-Paket beenden: " + e); }

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
            catch (Exception e) { Logger.LogWarning("Grading-Paket nachschließen: " + e.Message); }
            yield return new WaitForSecondsRealtime(0.4f);
            ts.Field("m_HoldCardAtEnd").SetValue(oldHold);
            _statGradedOpened += count;
            _gradedBoxSeenAt = -1f;
            Note($"Grading-Paket geöffnet: {count} gegradete Karten ins Album");
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
            Note($"Opener eingesammelt ({packs} Packs)");
        }

        private IEnumerator CollectOpener(InteractableAutoPackOpener o)
        {
            _busy = true;
            int packs = o.GetPackOpenedCount();
            o.OnMouseButtonUp(); // spieleigener "Einsammeln"-Klick -> Karten ins Album
            _statCollected++;
            Note($"Opener eingesammelt ({packs} Packs)");

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
            catch (Exception e) { Logger.LogError("Konnte Angebot nicht öffnen: " + e); }

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
            bool isNewForUs = get != null && get.cardGrade == 0 && CPlayerData.GetCardAmount(get) == 0;
            float needRatio = isNewForUs ? 0.8f : 1f + _swapMinGainPercent.Value / 100f;
            bool good = _swapEnabled.Value && own && (pGet > pGive * needRatio || pGive - pGet <= _swapTolerance.Value);

            if (good)
            {
                s.OnPressAccept();
                _statSwapped++;
                Note($"Tausch angenommen: bekommen {Price(pGet)} / abgegeben {Price(pGive)}" + (isNewForUs ? " (neue Karte für die Sammlung!)" : ""));
                yield return new WaitForSecondsRealtime(_stepDelay.Value);
                if (s.IsScreenOpened() && t.Field("m_HasAccepted").GetValue<bool>()) s.OnPressAccept(); // "Fertig"
            }
            else
            {
                s.OnPressDecline();
                _statDeclined++;
                Note($"Tausch abgelehnt: bekommen {Price(pGet)} / abgegeben {Price(pGive)}" + (own ? "" : " (wäre unsere letzte Karte)"));
            }
        }

        private IEnumerator HandleBuy(CustomerTradeCardScreen s, Traverse t, CardData card)
        {
            // Strategie (aus der Annahme-Formel des Spiels):
            //  - Gebot >= Wunschpreis des Kunden  -> 100 % Annahme
            //  - Gebot deutlich darunter          -> kaum Chance, und jede Ablehnung macht ihn ungeduldiger
            // Darum: Wunschpreis zahlen, wenn er unter unserem Limit liegt; sonst nur 1-2 knappe Gebote nahe am Limit.
            // Limit: normale Karten x % vom Marktpreis (Weiterverkauf +20 % = Gewinn), neue Karten für die Sammlung mehr.
            bool isNew = card != null && card.cardGrade == 0 && CPlayerData.GetCardAmount(card) == 0;
            float lastOffer = -1f;
            for (int round = 0; round < 4; round++)
            {
                if (!s.IsScreenOpened()) { _statDeclined++; Note($"Kunde hat abgelehnt und ist gegangen (letztes Gebot {Price(lastOffer)})"); yield break; }

                float ask = t.Field("m_SellCardAskPrice").GetValue<float>();
                float market = t.Field("m_SellCardMarketPrice").GetValue<float>();
                if (market <= 0f) market = CPlayerData.GetCardMarketPrice(card);
                float capPct = isNew ? _buyMaxNewPercent.Value : _buyMaxPercent2.Value;
                float cap = market * capPct / 100f;
                if (market < _buyAlwaysBelow.Value) cap = Mathf.Max(cap, ask + 0.01f); // Kleinkram unter $X: einfach Wunschpreis zahlen
                int declinesLeft = t.Field("m_MaxDeclineCount").GetValue<int>();

                float offer;
                if (ask <= cap)
                    offer = ask + 0.01f;                                     // Wunschpreis -> sicher
                else if (round == 0 && declinesLeft > 0)
                    offer = Mathf.Max(cap * 0.95f, Mathf.Min(cap, ask * 0.9f)); // 1. Versuch knapp unter Limit
                else
                    offer = cap;                                             // dann unser Maximum

                if (lastOffer >= 0f && offer <= lastOffer + 0.005f)
                {
                    s.OnPressDecline();
                    _statDeclined++;
                    Note($"Ankauf abgelehnt: will {Price(ask)}, Markt {Price(market)}, unser Limit {Price(cap)} ({capPct:0}%)");
                    yield break;
                }
                if (CPlayerData.m_CoinAmountDouble < offer)
                {
                    s.OnPressDecline();
                    _statDeclined++;
                    Note("Ankauf abgebrochen: nicht genug Geld");
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
                    Note($"Gekauft für {Price(offer)} (Markt {Price(market)}, {offer / Mathf.Max(0.01f, market) * 100f:0}%)" + (isNew ? " – neue Karte für die Sammlung!" : ""));
                    if (s.IsScreenOpened()) s.OnPressAccept(); // "Fertig"
                    yield break;
                }
            }
            if (s.IsScreenOpened()) { s.OnPressDecline(); _statDeclined++; Note("Ankauf abgelehnt (Kunde zu teuer)"); }
        }

        private static void SetPrice(CustomerTradeCardScreen s, float price)
        {
            float shown = price * GameInstance.GetCurrencyConversionRate();
            s.OnInputTextUpdated(shown.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private static string Price(float p) =>
            p == float.MaxValue ? "?" : GameInstance.GetPriceString(p);

        private void Note(string msg)
        {
            _lastAction = msg;
            Logger.LogInfo(msg);
            RecordHistory(msg);
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
            // Kompakte Zeile – Details im Dashboard (F9)
            string state = _enabled.Value ? "<color=#5f5>AN</color>" : "<color=#f55>AUS</color>";
            string text = $"<b>ShopBot</b> {state}  <color=#aaa>F8 An/Aus · F9 Dashboard</color>\n{_lastAction}";
            GUI.Label(new Rect(10, 40, 1400, 50), text, _style);
        }
    }
}
