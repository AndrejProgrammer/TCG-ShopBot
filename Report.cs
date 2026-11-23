using System.Collections.Generic;
using UnityEngine;

namespace ShopBot
{
    // Tagesbericht (Einnahmen Bot-Kasse / Ausgaben Bot pro Tag) und Warnungen über fehlende Mods
    public partial class Plugin
    {
        // ---------------- Fehlende Mods ----------------
        private readonly List<string> _modWarnings = new List<string>();
        private void ModMissing(string mod, string feature)
        {
            string w = $"{mod} nicht gefunden – {feature} ist aus";
            if (!_modWarnings.Contains(w)) _modWarnings.Add(w);
        }

        // ---------------- Tagesbericht ----------------
        private class DayRecord { public int Day; public float Income, Spent; public int Customers, Packs; }
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
                return;
            }
            if (_today != null)
            {
                _dayHistory.Add(_today);
                if (_dayHistory.Count > 7) _dayHistory.RemoveAt(0);
                Logger.LogInfo($"[Tagesbericht] Tag {_today.Day + 1}: Bot-Kasse {GameInstance.GetPriceString(_today.Income)} ({_today.Customers} Kunden), Bot-Ausgaben {GameInstance.GetPriceString(_today.Spent)}");
            }
            _today = new DayRecord { Day = day };
            _todaySpentStart = _statSpent;
            _todayPacksStart = _statPackCases;
        }

        private void ReportCheckout(float value)
        {
            if (_today == null) return;
            _today.Income += value;
            _today.Customers++;
        }
    }
}
