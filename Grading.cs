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
    // Karten graden über CardGrade Overhaul: teuerste zuerst, nur volle Einsendungen, PSA Standard (günstigste Stufe).
    public partial class Plugin
    {
        private ConfigEntry<bool> _gradeEnabled;
        private ConfigEntry<float> _gradeMinValue;
        private ConfigEntry<bool> _gradeKeepOne;
        private ConfigEntry<bool> _gradeOnlyFull, _gradePsa10Hunt;
        private float _gradeTimer = 50f;
        private bool _gradeWarned;
        private float _gradeWaitLogAt;

        private void BindGradingConfig()
        {
            _gradeEnabled = Config.Bind("12. Grading", "Grade cards automatically", true,
                "Sends cards to PSA (Standard, cheapest tier). Most expensive cards first, all 8 submission slots are used.");
            _gradeMinValue = Config.Bind("12. Grading", "Only cards from value ($)", 50f,
                "Grading costs about $20 per card – cheaper cards are not worth it.");
            _gradeKeepOne = Config.Bind("12. Grading", "Keep 1 copy of every card in the album", true,
                "So the collection in the tracker does not shrink. Off = grade single copies too.");
            _gradeOnlyFull = Config.Bind("12. Grading", "Only send full submissions", true,
                "Only send when the submission is full (PSA Standard: 52 cards).");
            _gradePsa10Hunt = Config.Bind("12. Grading", "PSA 10 collection (all cards as PSA 10)", false,
                "Later goal: also grade cheap duplicates as long as there is no PSA 10 of that card yet. Valuable cards still come first. One PSA 10 of every card always stays.");
        }

        private void HandleGrading()
        {
            if (!GradeActive || (_gradeTimer += 1f) < 60f) return;
            _gradeTimer = 0f;
            SafeRun("Grading", GradingTick);
        }

        private void GradingTick()
        {
            var tGrading = AccessTools.TypeByName("CardGradeOverhaul.Grading");
            var tCompanies = AccessTools.TypeByName("CardGradeOverhaul.Companies");
            var tCompany = AccessTools.TypeByName("CardGradeOverhaul.Company");
            var tWant = AccessTools.TypeByName("CardGradeOverhaul.Want");
            if (tGrading == null || tCompanies == null || tCompany == null || tWant == null)
            {
                if (!_gradeWarned) { _gradeWarned = true; Logger.LogWarning("CardGrade Overhaul not found – auto grading off."); ModMissing("CardGrade Overhaul", "Grading"); }  // i18n-last
                return;
            }

            // Freie Einsende-Plätze?
            int maxSets = (int)(AccessTools.Field(tGrading, "MaxSets")?.GetValue(null) ?? 8);
            int running = CPlayerData.m_GradeCardInProgressList?.Count ?? 0;
            if (running >= maxSets) return;

            // PSA, günstigste Stufe
            var psa = AccessTools.Method(tCompanies, "Get").Invoke(null, new[] { Enum.Parse(tCompany, "Psa") });
            var tiers = Traverse.Create(psa).Field("Tiers").GetValue() as Array;
            var perCardM = AccessTools.Method(tGrading, "PerCard");
            int tier = 0; float perCard = float.MaxValue;
            for (int i = 0; i < (tiers?.Length ?? 1); i++)
            {
                float c = (float)perCardM.Invoke(null, new object[] { psa, i });
                if (c > 0f && c < perCard) { perCard = c; tier = i; }
            }
            int limit = (int)AccessTools.Method(tGrading, "Limit").Invoke(null, new object[] { tier });

            // Album einlesen (wie die Grading-Seite)
            AccessTools.Method(tGrading, "BuildCollection").Invoke(null, null);
            var collection = AccessTools.Field(tGrading, "Collection").GetValue(null) as IList;
            if (collection == null) return;

            // PSA-10-Sammlung: Karten, von denen es noch keine 10 gibt
            var haveTen = new HashSet<string>();
            if (Psa10Active && CPlayerData.m_GradedCardInventoryList != null)
                foreach (var g in CPlayerData.m_GradedCardInventoryList)
                    if (g != null && g.amount >= 10) haveTen.Add($"{(int)g.expansionType}|{g.cardSaveIndex}|{g.isDestiny}");

            var entries = new List<(object o, float value, long key, int amount, int total, int exp, int index, bool dim, int copy)>();
            foreach (var o in collection)
            {
                var t = Traverse.Create(o);
                float value = t.Field("Value").GetValue<float>();
                if (value < _gradeMinValue.Value)
                {
                    // billige Karte: nur für die PSA-10-Sammlung und nur, solange es davon noch keine 10 gibt (wertvolle zuerst, da nach Wert sortiert)
                    if (!Psa10Active) continue;
                    string k10 = $"{(int)t.Field("Exp").GetValue<ECardExpansionType>()}|{t.Field("Index").GetValue<int>()}|{t.Field("Dim").GetValue<bool>()}";
                    if (haveTen.Contains(k10)) continue;
                }
                var data = t.Field("Data").GetValue<CardData>();
                if (data != null && data.cardGrade > 0) continue;
                entries.Add((o, value, t.Field("Key").GetValue<long>(), t.Field("Amount").GetValue<int>(), t.Field("Total").GetValue<int>(),
                             (int)t.Field("Exp").GetValue<ECardExpansionType>(), t.Field("Index").GetValue<int>(), t.Field("Dim").GetValue<bool>(), t.Field("Copy").GetValue<int>()));
            }

            // Teuerste zuerst
            entries.Sort((a, b) => b.value.CompareTo(a.value));
            var takenPerKey = new Dictionary<long, int>();
            var wants = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tWant));
            float sumValue = 0f;
            foreach (var e in entries)
            {
                if (wants.Count >= limit) break;
                takenPerKey.TryGetValue(e.key, out int taken);
                int allowed = (_gradeKeepOne.Value ? e.total - 1 : e.total) - taken;
                int n = Mathf.Min(e.amount, allowed, limit - wants.Count);
                for (int k = 0; k < n; k++)
                {
                    object w = Activator.CreateInstance(tWant);
                    AccessTools.Field(tWant, "Exp").SetValue(w, e.exp);
                    AccessTools.Field(tWant, "Index").SetValue(w, e.index);
                    AccessTools.Field(tWant, "Dim").SetValue(w, e.dim);
                    AccessTools.Field(tWant, "Copy").SetValue(w, e.copy);
                    wants.Add(w);
                    sumValue += e.value;
                }
                if (n > 0) takenPerKey[e.key] = taken + n;
            }

            if (wants.Count == 0) return;
            if (_gradeOnlyFull.Value && wants.Count < limit)
            {
                if (Time.unscaledTime >= _gradeWaitLogAt)
                {
                    _gradeWaitLogAt = Time.unscaledTime + 600f;
                    Logger.LogInfo($"[Grading] Waiting for a full submission: {wants.Count}/{limit} cards from {GameInstance.GetPriceString(_gradeMinValue.Value)} available");
                }
                return;
            }

            float fee = 10f + perCard * wants.Count;
            if (Budget < fee) { NoteF($"Grading: budget not enough ({GameInstance.GetPriceString(fee)})"); return; }

            var send = AccessTools.Method(tGrading, "SendFor");
            var args = new object[] { psa, tier, wants, null, null };
            int sent = (int)send.Invoke(null, args);
            if (sent > 0)
            {
                _statSpent += fee;
                _statGraded += sent;
                NoteF($"Grading: {sent} cards sent to PSA (value ~{GameInstance.GetPriceString(sumValue)}, fee {GameInstance.GetPriceString(fee)}) – slot {running + 1}/{maxSets}");
            }
            else Logger.LogInfo($"[Grading] Not sent: {args[3]}");
        }
    }
}
