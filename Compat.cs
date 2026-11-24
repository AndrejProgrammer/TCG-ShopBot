using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Protection against game/mod updates.
    //  1) At start: check that the game fields/methods each feature needs still exist. Missing -> only that feature is off.
    //  2) While running: a feature that fails 10 times in a row is switched off for this session (instead of flooding the log).
    // Switched-off features are shown on the dashboard overview and noted in the history.
    public partial class Plugin
    {
        private readonly Dictionary<string, int> _featErr = new Dictionary<string, int>();
        internal readonly Dictionary<string, string> _featOff = new Dictionary<string, string>();   // feature -> reason (English)
        private bool _compatChecked;

        internal bool FeatureOff(string feature) => _featOff.ContainsKey(feature);

        // Runs a feature; counts errors in a row and switches the feature off after 10
        private bool Guard(string feature, Action a)
        {
            if (FeatureOff(feature)) return false;
            try { a(); if (_featErr.ContainsKey(feature)) _featErr[feature] = 0; return true; }
            catch (Exception e) { FeatureFailed(feature, e); return false; }
        }

        private bool GuardB(string feature, Func<bool> f)
        {
            if (FeatureOff(feature)) return false;
            try { bool r = f(); if (_featErr.ContainsKey(feature)) _featErr[feature] = 0; return r; }
            catch (Exception e) { FeatureFailed(feature, e); return false; }
        }

        private void FeatureFailed(string feature, Exception e)
        {
            _featErr.TryGetValue(feature, out var n);
            _featErr[feature] = ++n;
            if (n <= 3) Logger.LogError($"[{feature}] {e}");
            if (n >= 10 && !FeatureOff(feature)) DisableFeature(feature, $"keeps failing: {e.GetType().Name}: {e.Message}");
        }

        private void DisableFeature(string feature, string why)
        {
            _featOff[feature] = why;
            Logger.LogWarning($"[Compat] {feature} switched off: {why}");
            NoteF($"{L(feature)} switched off – does not work with this game version ({why})");
        }

        // Features and the game members they need. Feature names = the names used in SafeRun/Guard.
        private static IEnumerable<(string feature, Type type, string member)> CompatMembers()
        {
            var box = typeof(InteractablePackagingBox_Item);
            Type itemComp = null;
            try { itemComp = AccessTools.Field(box, "m_ItemCompartment")?.FieldType; } catch { }
            foreach (var f in new[] { "Stocking", "Fill openers", "Sorting" })
            {
                foreach (var m in new[] { "m_IsBeingHold", "m_IsLerpingToPos", "m_IsTogglingOpenClose", "m_IsBoxOpened", "m_ItemCompartment" }) yield return (f, box, m);
                yield return (f, itemComp, "m_StoredItemList");
            }
            yield return ("Fill openers", typeof(InteractableAutoPackOpener), "m_IsBoxedUp");
            yield return ("Sorting", typeof(WarehouseShelf), "m_ItemCompartmentList");
            yield return ("Sorting", typeof(ShelfCompartment), "m_StoredBoxRecordList");
            foreach (var m in new[] { "m_IsUsingCard", "m_IsChangeReady", "m_TotalScannedItemCost", "m_CustomerPaidAmount", "m_CurrentMoneyChangeValue", "m_IsStartGivingChange", "CheckChangeReady" })
                yield return ("Checkout", typeof(InteractableCashierCounter), m);
            foreach (var m in new[] { "m_IsTrading", "m_CardData_L", "m_CardData_R", "m_HasAccepted", "m_SellCardAskPrice", "m_SellCardMarketPrice", "m_MaxDeclineCount" })
                yield return ("Customer offers", typeof(CustomerTradeCardScreen), m);
            yield return ("Customer offers", typeof(CustomerManager), "m_CustomerList");
            foreach (var m in new[] { "m_HoldCardAtEnd", "m_StateIndex", "m_IsGetHighValueCard", "m_IsScreenActive", "m_ScreenGrp", "CardRevealEnd" })
                yield return ("Grading packages", typeof(GradedCardOpeningSequence), m);
            yield return ("Grading packages", typeof(InteractablePackagingBox_Card), "m_StoredCardDataList");
            yield return ("Day end", typeof(EndOfDayReportScreen), "m_IsLoadingNextDay");
            yield return ("Day end", typeof(EndOfDayReportScreen), "m_IsActive");
        }

        // once, when the shop is loaded
        private void CompatCheck()
        {
            if (_compatChecked) return;
            _compatChecked = true;
            var missing = new Dictionary<string, List<string>>();
            int checkedCount = 0;
            foreach (var (feature, type, member) in CompatMembers())
            {
                checkedCount++;
                bool ok = type != null && (AccessTools.Field(type, member) != null || AccessTools.Method(type, member) != null || AccessTools.Property(type, member) != null);
                if (ok) continue;
                if (!missing.TryGetValue(feature, out var l)) missing[feature] = l = new List<string>();
                l.Add($"{type?.Name ?? "?"}.{member}");
            }
            string game = Application.version;
            if (missing.Count == 0)
            {
                Logger.LogInfo($"[Compat] game {game}: all {checkedCount} game members found – all features active");
                return;
            }
            foreach (var kv in missing) DisableFeature(kv.Key, "game changed: " + string.Join(", ", kv.Value) + " missing");
            Logger.LogWarning($"[Compat] game {game}: {missing.Count} features switched off: {string.Join(", ", missing.Keys)}");
        }

        // Feature names for the dashboard/history (translated)
        private static readonly string[] FeatureNames = { "Stocking", "Fill openers", "Sorting", "Checkout", "Customer offers", "Grading packages", "Day end", "Prices", "Cashier help", "Openers", "Bills", "Bonus", "Card storage", "Collection", "Displays", "Emergency restock", "Expansions", "Grading", "Hiring", "Licenses", "Opener turbo", "Restocking", "Reviews", "Upgrades", "Workers", "Game phase" };  // i18n
    }
}
