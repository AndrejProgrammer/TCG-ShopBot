using System.Collections.Generic;
using UnityEngine;

namespace ShopBot
{
    // Daily report (bot checkout income / bot spending per day) and warnings about missing mods
    public partial class Plugin
    {
        // ---------------- Fehlende Mods ----------------
        private readonly List<(string mod, string feature)> _modWarnings = new List<(string, string)>();
        private void ModMissing(string mod, string feature)
        {
            if (!_modWarnings.Contains((mod, feature))) _modWarnings.Add((mod, feature));
        }
        private string ModWarningText((string mod, string feature) w) => F($"{w.mod} not found – {L(w.feature)} is off");

        // ---------------- Tagesbericht ----------------
        private class DayRecord { public int Day; public float Income, Spent, ShopIncome; public int Customers, Packs, HaveStart = -1; }
        private readonly List<DayRecord> _dayHistory = new List<DayRecord>();
        private DayRecord _today;
        private float _todaySpentStart; private int _todayPacksStart;

        private void DayReportTick()
        {
            int day = CPlayerData.m_CurrentDay;
            if (_today != null && _today.Day == day)
            {
                _today.Spent = _statSpent - _todaySpentStart;
                _today.Packs = (_statPackCases - _todayPacksStart) * 64;
                if (_today.HaveStart < 0 && _goalHave >= 0) _today.HaveStart = _goalHave;
                // all shop sales today (the game keeps this day report in the save)
                try
                {
                    var rep = CPlayerData.m_GameReportDataCollect;
                    _today.ShopIncome = Mathf.Max(_today.ShopIncome, rep.totalItemEarning + rep.totalCardEarning + rep.totalPlayTableEarning);
                }
                catch { }
                SaveDayReport();
                return;
            }
            if (_today != null)
            {
                _dayHistory.Add(_today);
                if (_dayHistory.Count > 7) _dayHistory.RemoveAt(0);
                Logger.LogInfo($"[Daily report] Day {_today.Day + 1}: bot checkout {GameInstance.GetPriceString(_today.Income)} ({_today.Customers} customers), bot spending {GameInstance.GetPriceString(_today.Spent)}");
            }
            // after a restart on the same day: continue with the saved numbers
            if (_today == null && RestoreDayReport(day)) return;
            _today = new DayRecord { Day = day, HaveStart = _goalHave };
            _todaySpentStart = _statSpent;
            _todayPacksStart = _statPackCases;
            SaveDayReport(true);
        }

        // Neue Karten pro Tag (aus den gespeicherten Tagen) -> geschätzte Tage bis alle Karten da sind; -1 = noch unbekannt
        private float CardsPerDay()
        {
            if (_goalHave < 0) return -1f;
            DayRecord first = null;
            foreach (var r in _dayHistory) if (r.HaveStart >= 0) { first = r; break; }
            if (first == null || _today == null || _today.Day <= first.Day) return -1f;
            return (_goalHave - first.HaveStart) / (float)(_today.Day - first.Day);
        }

        private void ReportCheckout(float value)
        {
            if (_today == null) return;
            _today.Income += value;
            _today.Customers++;
        }
    }
}
