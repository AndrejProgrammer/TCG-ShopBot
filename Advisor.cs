using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Advisor: thinks like a good player. Measures the shop (profit, fixed costs, bottlenecks), explains what holds the shop back,
    // recommends what only the player can do (placing shelves/openers/displays) and lists the next investments.
    // Step 1: measure + show. The grading tier is already chosen by profit (see ChooseGradeTier).
    public partial class Plugin
    {
        internal class Bottleneck { public string Key; public float Severity; public FormattableString Text; }

        internal float _advIncome, _advFixed, _advProfit, _advReserveTarget;
        internal readonly List<Bottleneck> _advBottlenecks = new List<Bottleneck>();
        internal readonly List<FormattableString> _advTips = new List<FormattableString>();
        internal readonly List<(FormattableString what, float cost, FormattableString verdict)> _advInvest = new List<(FormattableString, float, FormattableString)>();
        internal string _gradeTierText = "";
        private float _advTimer = 45f;
        // shelf facts from the shelf status (Store.cs)
        internal int _advShelfSlots, _advShelfEmpty, _advShelfCapacity;
        private int _advOpenerSamples, _advOpenerIdleSum, _advOpenersTotal;

        private void HandleAdvisor()
        {
            // opener idle sampling every second (idle = not running and empty)
            try
            {
                var ops = ShelfManager.GetAutoPackOpenerList();
                if (ops != null)
                {
                    int idle = 0, total = 0;
                    foreach (var o in ops) { if (o == null) continue; total++; if (!o.GetIsProcessing() && o.GetStoredItemList().Count == 0) idle++; }
                    _advOpenerIdleSum += idle; _advOpenerSamples++; _advOpenersTotal = total;
                }
            }
            catch { }
            if ((_advTimer += 1f) < 60f) return;
            _advTimer = 0f;
            SafeRun("Advisor", AdvisorTick);
        }

        // average fixed costs per day (rent + electricity + wages): measured from paid bills
        private float FixedPerDay => Mathf.Max(0f, _billsEstimate);

        private void AdvisorTick()
        {
            _advIncome = _phAvgIncome;
            _advFixed = FixedPerDay;
            _advProfit = _advIncome - _advFixed;
            _advReserveTarget = _advFixed * 3f;              // 3 days of fixed costs
            float openerIdle = _advOpenerSamples > 0 && _advOpenersTotal > 0 ? (float)_advOpenerIdleSum / _advOpenerSamples / _advOpenersTotal : 0f;
            _advOpenerSamples = 0; _advOpenerIdleSum = 0;

            _advBottlenecks.Clear();
            void B(string key, float sev, FormattableString text) { if (sev > 0.05f) _advBottlenecks.Add(new Bottleneck { Key = key, Severity = Mathf.Clamp01(sev), Text = text }); }

            // packs for the openers
            if (_packNeed > 0 && _packHave < _packNeed)
                B("packs", 1f - (float)_packHave / _packNeed, $"Openers are short of packs: {_packHave:N0} of {_packNeed:N0} in stock");   // i18n-fs
            // openers standing idle
            if (_advOpenersTotal > 0 && openerIdle > 0.1f)
                B("openersIdle", openerIdle, $"{openerIdle * 100f:0} % of the pack openers stand empty");   // i18n-fs
            // shelves: goods missing
            if (_advShelfSlots > 0 && _advShelfEmpty > 0)
                B("shelfGoods", (float)_advShelfEmpty / _advShelfSlots, $"{_advShelfEmpty} of {_advShelfSlots} shelf slots are empty");   // i18n-fs
            // shelves: space (sold more than 1.5x the shelf capacity per day -> shelves are the limit)
            var lastDay = _dayHistory.LastOrDefault(d => d.ItemsSold > 0);
            float turnover = lastDay != null && _advShelfCapacity > 0 ? (float)lastDay.ItemsSold / _advShelfCapacity : 0f;
            if (turnover > 1.5f && _advShelfEmpty == 0)
                B("shelfSpace", Mathf.Clamp01((turnover - 1.5f) / 2f + 0.3f), $"Shelves sell out {turnover:0.0}× per day – more shelf space would sell more");   // i18n-fs
            // display cases
            if (_dashVitrinesFull > 0)
                B("displays", Mathf.Clamp01(_dashVitrinesFull / 500f + 0.3f), $"{_dashVitrinesFull:N0} cards wait for a free display slot");   // i18n-fs
            // workers
            if (_dashDemand.opener + _dashDemand.shelf + _dashDemand.display > 0f)
                B("workers", 0.5f, $"Work is left over that the bot and the workers do not manage");   // i18n-fs
            // grading
            if (_cGrading >= 8 && GradeActive)
                B("grading", 0.3f, $"All 8 grading slots are busy – {L(_gradeTierText)}");   // i18n-fs
            // card storage full
            if (_cStorageMax > 0 && _cStorageCards >= _cStorageMax)
                B("storage", 0.4f, $"Card storage is full");   // i18n-fs
            _advBottlenecks.Sort((a, b) => b.Severity.CompareTo(a.Severity));

            // ---- tips: what only the player can do ----
            _advTips.Clear();
            if (_dashVitrinesFull > 0) _advTips.Add($"Place more card display cases – {_dashVitrinesFull:N0} cards are waiting to be sold");   // i18n-fs
            if (turnover > 1.5f && _advShelfEmpty == 0) _advTips.Add($"Place more shelves – the shelves sell out {turnover:0.0}× per day");   // i18n-fs
            if (_packStarving && _advProfit > 0f && _packRatePerMin > 0f)
            {
                float perDay = _packRatePerMin * 60f * 12f;   // rough: openers run about 12 h of game time per day
                _advTips.Add($"The openers use ~{perDay:N0} packs a day – more than the income buys. Fewer openers or a higher daily limit");   // i18n-fs
            }
            else if (!_packStarving && openerIdle < 0.05f && _advOpenersTotal > 0 && _advProfit > 20000f)
                _advTips.Add($"All openers run all the time and packs are in stock – more pack openers would open more cards");   // i18n-fs
            if (_advOpenersTotal == 0 && _advProfit > 5000f)
                _advTips.Add($"No pack openers yet – a pack opener opens packs for the collection automatically");   // i18n-fs
            if (_dashDemand.opener + _dashDemand.shelf + _dashDemand.display <= 0f && (WorkerManager.GetWorkerList()?.Count(w => w != null && w.IsActive()) ?? 0) > 3 && _autoCheckout.Value)
                _advTips.Add($"The bot does most of the work itself – you could hire fewer workers and save wages");   // i18n-fs

            // ---- next investments the bot considers ----
            _advInvest.Clear();
            try
            {
                int idx = CPlayerData.m_UnlockRoomCount;
                if (idx < 30)
                {
                    float cost = CPlayerData.GetUnlockShopRoomCost(idx);
                    bool space = _advBottlenecks.Any(b => b.Key == "shelfSpace");
                    _advInvest.Add(($"Shop expansion {idx + 1}", cost, space ? (FormattableString)$"worth it: shelf space is a bottleneck" : $"wait: space is not the bottleneck (more rent)"));   // i18n-fs
                }
            }
            catch { }
            if (_licPending.Count > 0)
            {
                var l = _licPending[0];
                _advInvest.Add(($"License: {l.name}", l.price, l.needed ? (FormattableString)$"worth it: packs for a set without buyable packs" : $"nice to have"));   // i18n-fs
            }
            if (_gradeTierText.Length > 0) _advInvest.Add(($"Grading tier", 0f, $"{L(_gradeTierText)}"));   // i18n-fs
        }

        // ---------- Grading tier by profit ----------
        // For every PSA tier: cards per day per slot (limit / days) and its cost per day. The fastest tier whose cost (all 8 slots)
        // stays within ~15 % of the profit per day wins. Without a known profit: the cheapest tier (as before).
        private int ChooseGradeTier(object psa, Type tGrading, int waitingCards, out float perCard, out int limit)
        {
            var tiers = Traverse.Create(psa).Field("Tiers").GetValue() as Array;
            var perCardM = AccessTools.Method(tGrading, "PerCard");
            var limitM = AccessTools.Method(tGrading, "Limit");
            var daysM = AccessTools.Method(tGrading, "Days");
            int maxSets = (int)(AccessTools.Field(tGrading, "MaxSets")?.GetValue(null) ?? 8);
            var opts = new List<(int i, float per, int lim, int days, float perDay, float costDay)>();
            for (int i = 0; i < (tiers?.Length ?? 0); i++)
            {
                float c = (float)perCardM.Invoke(null, new object[] { psa, i });
                if (c <= 0f) continue;
                int lim = (int)limitM.Invoke(null, new object[] { i });
                int level = Traverse.Create(tiers.GetValue(i)).Field("Level").GetValue<int>();
                int days = daysM != null ? (int)daysM.Invoke(null, new object[] { level }) : 0;
                if (lim <= 0 || days <= 0) continue;
                opts.Add((i, c, lim, days, lim / (float)days, (10f + c * lim) / days * maxSets));
            }
            if (opts.Count == 0) { perCard = 0f; limit = 0; return 0; }
            var cheapest = opts.OrderBy(o => o.per).First();
            float budgetDay = _advProfit * 0.15f;
            var pick = cheapest;
            if (budgetDay > 0f)
            {
                // only tiers we can fill (enough cards waiting) and afford
                var ok = opts.Where(o => o.costDay <= budgetDay && o.lim <= Mathf.Max(waitingCards, cheapest.lim)).ToList();
                if (ok.Count > 0) pick = ok.OrderByDescending(o => o.perDay).ThenBy(o => o.per).First();
            }
            string name = "";
            try { name = (string)AccessTools.Method(tGrading, "TierName")?.Invoke(null, new object[] { psa, pick.i }); } catch { }
            _gradeTierText = $"PSA {name} ({pick.lim} cards, {pick.days} days, ${pick.per:0.##}/card)";
            perCard = pick.per; limit = pick.lim;
            return pick.i;
        }
    }
}
