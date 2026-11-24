using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace ShopBot
{
    // Keeps the day's counters across a game restart (same game day):
    //  - purchases counted against the daily limit and the emergency limit
    //  - the daily report (today + last 7 days) – also feeds "new cards per day"
    // Without this a restart on the same day reset "spent today" to $0 and the bot could spend the daily limit twice.
    public partial class Plugin
    {
        private ConfigEntry<string> _dayState, _dayReportState;
        private bool _dayRestored, _reportRestored;
        // hides internal entries in the Configuration Manager (F1); it looks for a class with this name
        private static readonly ConfigurationManagerAttributes Hidden = new ConfigurationManagerAttributes { Browsable = false };
        private float _reportSaveAt;

        private void BindDayStateConfig()
        {
            _dayState = Config.Bind("99. Internal", "Day purchases", "", new ConfigDescription(
                "Internal: purchases of the current game day (day;spent;emergency) so a restart does not reset the daily limit. Do not edit.", null, Hidden));
            _dayReportState = Config.Bind("99. Internal", "Daily report", "", new ConfigDescription(
                "Internal: daily report of the last days (survives a restart). Do not edit.", null, Hidden));
        }

        // Called when the shop is no longer loaded (main menu) – another save may be loaded next
        private void ResetDayStateRestore() { _dayRestored = false; _reportRestored = false; }

        // ---------- Daily limit ----------
        private void RestoreDayState()
        {
            if (_dayRestored || _readySince < 0f) return;
            _dayRestored = true;
            int day = CPlayerData.m_CurrentDay;
            float saved = 0f, savedEmergency = 0f;
            var p = (_dayState.Value ?? "").Split(';');
            if (p.Length >= 3 && int.TryParse(p[0], out var d) && d == day)
            {
                float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out saved);
                float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out savedEmergency);
            }
            // The game saves its own day report (purchase costs today) with the save – safe fallback
            float game = 0f;
            try { game = Mathf.Max(0f, -CPlayerData.m_GameReportDataCollect.supplyCost); } catch { }
            _daySpentDay = day; _daySpent = Mathf.Max(saved, game);
            _emergencyDay = day; _emergencySpent = Mathf.Max(0f, savedEmergency);
            if (_daySpent > 0f)
                Logger.LogInfo($"[Collection] Day {day + 1}: {GameInstance.GetPriceString(_daySpent)} already spent today (restored after restart)");
        }

        private void SaveDayState() =>
            _dayState.Value = string.Format(CultureInfo.InvariantCulture, "{0};{1:0.##};{2:0.##}", _daySpentDay, _daySpent, _emergencySpent);

        // ---------- Daily report ----------
        private static string ReportToString(DayRecord r) =>
            string.Format(CultureInfo.InvariantCulture, "{0},{1:0.##},{2:0.##},{3},{4},{5}", r.Day, r.Income, r.Spent, r.Customers, r.Packs, r.HaveStart);

        private static DayRecord ReportFromString(string s)
        {
            var f = s.Split(',');
            if (f.Length < 6) return null;
            var inv = CultureInfo.InvariantCulture;
            if (!int.TryParse(f[0], NumberStyles.Integer, inv, out int day)) return null;
            var r = new DayRecord { Day = day };
            float.TryParse(f[1], NumberStyles.Float, inv, out r.Income);
            float.TryParse(f[2], NumberStyles.Float, inv, out r.Spent);
            int.TryParse(f[3], NumberStyles.Integer, inv, out r.Customers);
            int.TryParse(f[4], NumberStyles.Integer, inv, out r.Packs);
            int.TryParse(f[5], NumberStyles.Integer, inv, out r.HaveStart);
            return r;
        }

        // Restores yesterday's days and – if it is still the same game day – today's numbers
        private bool RestoreDayReport(int day)
        {
            if (_reportRestored) return false;
            _reportRestored = true;
            DayRecord today = null;
            _dayHistory.Clear();
            foreach (var part in (_dayReportState.Value ?? "").Split('|'))
            {
                var r = part.Length > 0 ? ReportFromString(part) : null;
                if (r == null || r.Day > day) continue;          // other/older save: ignore days "from the future"
                if (r.Day == day) today = r; else _dayHistory.Add(r);
            }
            _dayHistory.Sort((a, b) => a.Day.CompareTo(b.Day));
            while (_dayHistory.Count > 7) _dayHistory.RemoveAt(0);
            if (today == null) return false;
            _today = today;
            _todaySpentStart = _statSpent - today.Spent;
            _todayPacksStart = _statPackCases - today.Packs / 64;
            return true;
        }

        private void SaveDayReport(bool force = false)
        {
            if (!force && Time.unscaledTime < _reportSaveAt) return;
            _reportSaveAt = Time.unscaledTime + 15f;
            var sb = new StringBuilder();
            foreach (var r in _dayHistory) { if (sb.Length > 0) sb.Append('|'); sb.Append(ReportToString(r)); }
            if (_today != null) { if (sb.Length > 0) sb.Append('|'); sb.Append(ReportToString(_today)); }
            _dayReportState.Value = sb.ToString();
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
    }
}
