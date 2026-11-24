using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShopBot
{
    // Game phase detection (step 1: measure + show only – the bot's behaviour does not change yet).
    // Signals: shop level, expansions, pack openers, money, collection progress, average shop income per day.
    //   Start     – little money, no openers yet: prices, checkout, restocking, bills
    //   Build-up  – first openers / medium income: money first into expansions and licenses
    //   Collect   – many openers, high income: packs first, emergency restock
    //   Endgame   – collection over 90 %: missing cards from customers, PSA 10 hunt
    // A new phase only counts after it was detected for 5 minutes in a row (no flickering).
    public partial class Plugin
    {
        internal enum GamePhase { Unknown, Start, Buildup, Collect, Endgame }

        private GamePhase _phase = GamePhase.Unknown, _phaseCandidate = GamePhase.Unknown;
        private float _phaseCandidateSince, _phaseTimer = 50f;
        internal string _phaseReason = "";
        internal int _phOpeners, _phLevel, _phExpansions;
        internal float _phAvgIncome, _phCollection = -1f;

        internal static string PhaseName(GamePhase p) =>
            p == GamePhase.Start ? "Start" : p == GamePhase.Buildup ? "Build-up" : p == GamePhase.Collect ? "Collect" : p == GamePhase.Endgame ? "Endgame" : "…";   // i18n

        // What the bot will focus on in this phase (shown in the dashboard; not active yet)
        internal static string PhasePlan(GamePhase p) =>
            p == GamePhase.Start ? "Focus: prices, checkout, restocking and bills – no collection packs, no grading yet" :      // i18n
            p == GamePhase.Buildup ? "Focus: money first into expansions and licenses, packs in moderation" :                   // i18n
            p == GamePhase.Collect ? "Focus: packs first, emergency restock, licenses for missing sets" :                       // i18n
            p == GamePhase.Endgame ? "Focus: missing cards from customers, PSA 10 hunt, full grading" : "";                     // i18n

        // ================= Step 2: settings follow the phase =================
        private BepInEx.Configuration.ConfigEntry<bool> _phaseAuto;

        private void BindPhaseConfig()
        {
            _phaseAuto = Config.Bind("7. Collection", "Adapt settings to the game phase", true,
                "On: the bot sets collection buying, grading, emergency restock, \"packs first\", the daily purchase limit and the license/expansion safety factor to fit the detected game phase (Start, Build-up, Collect, Endgame). Off: your own values below are used.");
        }

        // auto is active once a phase is detected
        internal bool PhaseAuto => _phaseAuto != null && _phaseAuto.Value && _phase != GamePhase.Unknown;

        // finished days with a known shop income (the daily limit needs at least one – before that: learning, your own limit applies)
        private int IncomeDays => _dayHistory.Count(d => d.ShopIncome > 0f);

        internal bool ColActive => PhaseAuto && _phase == GamePhase.Start ? false : _colEnabled.Value;
        internal bool GradeActive => PhaseAuto && _phase == GamePhase.Start ? false : _gradeEnabled.Value;
        internal bool Psa10Active => (PhaseAuto && _phase == GamePhase.Endgame) || _gradePsa10Hunt.Value;
        internal bool PacksFirstActive => PhaseAuto ? (_phase == GamePhase.Collect || _phase == GamePhase.Endgame) : (_packsFirst != null && _packsFirst.Value);
        internal bool EmergencyOn => PhaseAuto ? (_phase == GamePhase.Collect || _phase == GamePhase.Endgame) : _emergencyEnabled.Value;
        internal float ExpFactor => PhaseAuto && _phase == GamePhase.Buildup ? 2f : PhaseAuto && _phase == GamePhase.Start ? 3f : Mathf.Max(1f, _expMoneyFactor.Value);
        internal float LicFactor => PhaseAuto && _phase == GamePhase.Buildup ? Mathf.Min(2f, Mathf.Max(1f, _licMoneyFactor.Value)) : Mathf.Max(1f, _licMoneyFactor.Value);

        // Daily purchase limit from the average shop income (share depends on the phase); null = use your own limit
        internal float? AutoDayLimit
        {
            get
            {
                if (!PhaseAuto || IncomeDays < 1 || _phAvgIncome <= 0f) return null;
                float share = _phase == GamePhase.Collect ? 0.7f : _phase == GamePhase.Start ? 0.1f : 0.3f;
                return Mathf.Max(5000f, _phAvgIncome * share);
            }
        }

        // Short summary for the dashboard: what auto sets right now
        internal string PhaseAutoText()
        {
            if (!PhaseAuto) return _phaseAuto != null && !_phaseAuto.Value ? T("Auto is off – your own settings apply") : "";
            var lim = AutoDayLimit;
            return F($"Auto: collection {OnOff(ColActive)} · grading {OnOff(GradeActive)} · emergency {OnOff(EmergencyOn)} · packs first {OnOff(PacksFirstActive)} · daily limit {(lim.HasValue ? Money(lim.Value) : T("your own (learning first day)"))}");
        }
        private static string OnOff(bool b) => b ? T("on") : T("off");

        private void HandleGamePhase()
        {
            if ((_phaseTimer += 1f) < 60f) return;
            _phaseTimer = 0f;
            SafeRun("Game phase", DetectPhase);
        }

        // Average shop income per day (all sales) – last 3 finished days, otherwise today so far
        private float AvgShopIncome()
        {
            var days = _dayHistory.Where(d => d.ShopIncome > 0f).Skip(Math.Max(0, _dayHistory.Count(d => d.ShopIncome > 0f) - 3)).ToList();
            if (days.Count > 0) return days.Average(d => d.ShopIncome);
            return _today != null ? _today.ShopIncome : 0f;
        }

        private void DetectPhase()
        {
            _phLevel = CPlayerData.m_ShopLevel + 1;
            _phExpansions = CPlayerData.m_UnlockRoomCount;
            _phOpeners = ShelfManager.GetAutoPackOpenerList()?.Count(o => o != null) ?? 0;
            _phAvgIncome = AvgShopIncome();
            _phCollection = _goalTotal > 0 && _goalHave >= 0 ? (float)_goalHave / _goalTotal : -1f;
            double money = CPlayerData.m_CoinAmountDouble;

            GamePhase cand;
            if (_phCollection >= 0.9f) cand = GamePhase.Endgame;
            else if (_phOpeners >= 8 && (_phAvgIncome >= 50000f || money >= 200000)) cand = GamePhase.Collect;
            else if (_phOpeners >= 1 || _phLevel >= 15 || money >= 20000 || _phAvgIncome >= 10000f) cand = GamePhase.Buildup;
            else cand = GamePhase.Start;

            _phaseReason = F($"Level {_phLevel} · {_phExpansions} expansions · {_phOpeners} openers · ~{Money(_phAvgIncome)} income/day · collection {(_phCollection >= 0 ? (_phCollection * 100f).ToString("0") + " %" : "?")}");

            float now = Time.unscaledTime;
            if (cand != _phaseCandidate) { _phaseCandidate = cand; _phaseCandidateSince = now; }
            bool first = _phase == GamePhase.Unknown;
            if (cand != _phase && (first || now - _phaseCandidateSince >= 300f))
            {
                var old = _phase;
                _phase = cand;
                if (first) Logger.LogInfo($"[Phase] detected: {PhaseName(cand)} ({Lang.Source(_phaseReasonFs())})");
                else NoteF($"New game phase: {L(PhaseName(cand))} (before: {L(PhaseName(old))})");
            }
        }

        // English version of the reason for the log
        private FormattableString _phaseReasonFs() =>
            $"Level {_phLevel} · {_phExpansions} expansions · {_phOpeners} openers · ~{Money(_phAvgIncome)} income/day · collection {(_phCollection >= 0 ? (_phCollection * 100f).ToString("0") + " %" : "?")}";
    }
}
