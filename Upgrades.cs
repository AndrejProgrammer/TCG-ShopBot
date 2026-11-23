using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Kauft Upgrades aus der "Upgrades"-Mod (Sammelpunkte) in sinnvoller Reihenfolge: die nützlichsten zuerst.
    public partial class Plugin
    {
        private ConfigEntry<bool> _upEnabled;
        private ConfigEntry<int> _upPointReserve;
        private float _upTimer;
        private bool _upWarned;

        // Reihenfolge = Priorität. Arbeiter-Upgrades gelten pro Arbeiter.
        private static readonly (string Cat, string Id, string Name)[] UpgradePriority =
        {
            ("Workers",   "workers_stay_late",   "Arbeiter bleiben bis zum letzten Kunden"), // 15.000 – abends Kasse abarbeiten
            ("Workers",   "working_speed",       "Arbeitstempo"),          // Opener schneller befüllt, Kasse schneller
            ("Customers", "IncreasedCustomers",  "Mehr Kunden"),
            ("Customers", "IncreaseWalletSize",  "Kunden mit mehr Geld"),
            ("Shop",      "AutoOpenShop",        "Laden automatisch öffnen"),
            ("Shop",      "AutoLights",          "Licht automatisch"),
            ("Customers", "Scalpers",            "Kunden kaufen mehr"),
            ("Shop",      "NoSmellyCustomers",   "Keine stinkenden Kunden"),
            ("Workers",   "wage_reduction",      "Weniger Lohn"),
            ("Player",    "FasterInteraction",   "Schneller aufheben/abstellen"),
            ("Player",    "PackCarryCapacity",   "Mehr Packs tragen"),
            ("Player",    "FasterPackOpening",   "Schneller selbst öffnen"),
        };

        private void BindUpgradeConfig()
        {
            _upEnabled = Config.Bind("11. Upgrades", "Upgrades automatisch kaufen", true,
                "Kauft Upgrades mit Sammelpunkten: Arbeitstempo, mehr Kunden, Kunden mit mehr Geld, ... (nützlichste zuerst). Glückspacks/God-Pack werden nie gekauft.");
            _upPointReserve = Config.Bind("11. Upgrades", "Sammelpunkte-Reserve", 0, "So viele Sammelpunkte bleiben immer übrig.");
        }

        private void HandleUpgrades()
        {
            if (!_upEnabled.Value || (_upTimer += 1f) < 30f) return;
            _upTimer = 0f;
            SafeRun("Upgrades", UpgradeTick);
        }

        private void UpgradeTick()
        {
            var store = AccessTools.TypeByName("Upgrades.UpStore");
            var itemType = AccessTools.TypeByName("Upgrades.UpStore+Item") ?? store?.GetNestedType("Item", System.Reflection.BindingFlags.NonPublic);
            var catType = AccessTools.TypeByName("Upgrades.UpStore+Category") ?? store?.GetNestedType("Category", System.Reflection.BindingFlags.NonPublic);
            var workersType = AccessTools.TypeByName("Upgrades.UpWorkers");
            if (store == null || itemType == null || catType == null)
            {
                if (!_upWarned) { _upWarned = true; Logger.LogWarning("Upgrades-Mod nicht gefunden – Auto-Upgrades aus."); }
                return;
            }

            int balance = (int)(AccessTools.Property(store, "Balance")?.GetValue(null, null) ?? 0);
            var canBuy = AccessTools.Method(store, "CanBuy");
            var buy = AccessTools.Method(store, "Buy");

            // eingestellte Arbeiter
            var workerIdx = new List<int>();
            if (AccessTools.Method(workersType, "GetHiredWorkers")?.Invoke(null, null) is IEnumerable hired)
                foreach (var w in hired) workerIdx.Add(Traverse.Create(w).Field("Index").GetValue<int>());

            // Wichtigste Rollen zuerst verbessern: Opener-Befüller, dann Kassierer, dann der Rest
            int RoleRank(int idx)
            {
                var wk = WorkerManager.GetWorkerList()?.FirstOrDefault(x => x != null && x.m_WorkerIndex == idx);
                if (wk == null) return 9;
                switch (wk.m_PrimaryTask)
                {
                    case EWorkerTask.RefillCardOpener: return 0;
                    case EWorkerTask.ManCounter: return 1;
                    case EWorkerTask.RestockShelf: return 2;
                    default: return 3;
                }
            }
            workerIdx = workerIdx.OrderBy(RoleRank).ToList();

            foreach (var p in UpgradePriority)
            {
                var targets = p.Cat == "Workers" && p.Id != "workers_stay_late" ? workerIdx : new List<int> { -1 };

                // Pro Arbeiter: gleichmäßig verteilen – wer die niedrigste Stufe hat, kommt zuerst (bei Gleichstand wichtigste Rolle)
                var items = new List<(object item, int worker, int level, int order)>();
                for (int i = 0; i < targets.Count; i++)
                {
                    var it = Activator.CreateInstance(itemType, true);
                    var tt = Traverse.Create(it);
                    tt.Field("Cat").SetValue(Enum.Parse(catType, p.Cat));
                    tt.Field("Id").SetValue(p.Id);
                    tt.Field("Worker").SetValue(targets[i]);
                    items.Add((it, targets[i], tt.Property("Level").GetValue<int>(), i));
                }

                foreach (var entry in items.OrderBy(x => x.level).ThenBy(x => x.order))
                {
                    var item = entry.item;
                    int worker = entry.worker;
                    var t = Traverse.Create(item);

                    if (t.Property("Maxed").GetValue<bool>()) continue;
                    int cost = t.Property("Cost").GetValue<int>();
                    if (cost == int.MaxValue || balance - cost < _upPointReserve.Value) continue;
                    var args = new object[] { item, null };
                    if (!(bool)canBuy.Invoke(null, args)) continue;

                    if ((bool)buy.Invoke(null, new[] { item }))
                    {
                        int lvl = t.Property("Level").GetValue<int>();
                        int max = t.Property("MaxLevel").GetValue<int>();
                        string who = worker >= 0 ? $" ({WorkerNameByIndex(worker)})" : "";
                        Note($"Upgrade gekauft: {p.Name}{who} Stufe {lvl}/{max} für {cost:N0} Sammelpunkte");
                        return; // max. 1 Kauf pro Durchgang
                    }
                }
            }
        }

        private static string WorkerNameByIndex(int index)
        {
            var list = WorkerManager.GetWorkerList();
            if (list != null)
                foreach (var w in list)
                    if (w != null && w.m_WorkerIndex == index) return WorkerName(w);
            return "Arbeiter " + index;
        }
    }
}
