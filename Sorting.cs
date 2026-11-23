using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Herumliegende Kartons einsortieren:
    //  - volle Kartons ins Lagerregal (wie ein Arbeiter: InteractablePackagingBox_Item.DispenseItem auf ein Lagerfach)
    //  - Packs unfertiger Sets bleiben bis zu einer Opener-Füllung am Boden liegen (der Bot befüllt die Opener daraus)
    //  - leere Kartons in den Mülleimer
    public partial class Plugin
    {
        private ConfigEntry<bool> _sortEnabled, _sortTrashEmpty, _sortKeepOpenerPacks;
        private ConfigEntry<int> _sortPerSecond;
        private float _sortAcc;
        private int _statSorted, _statTrashed;
        private float _sortFullLogAt;
        private readonly Dictionary<InteractablePackagingBox_Item, float> _emptySince = new Dictionary<InteractablePackagingBox_Item, float>();

        private void BindSortConfig()
        {
            _sortEnabled = Config.Bind("17. Einsortieren", "Herumliegende Kartons ins Lagerregal", true, "Volle Kartons, die im Laden auf dem Boden liegen, kommen ins Lagerregal (erst Fächer mit passendem Etikett, dann freie).");
            _sortKeepOpenerPacks = Config.Bind("17. Einsortieren", "Packs für Opener am Boden lassen", true, "Packs unfertiger Sets bleiben liegen, bis eine Opener-Füllung erreicht ist – daraus befüllt der Bot die Opener selbst. Der Rest kommt ins Regal.");
            _sortTrashEmpty = Config.Bind("17. Einsortieren", "Leere Kartons wegwerfen", true, "Leere Kartons kommen in den Mülleimer (nur wenn einer im Laden steht).");
            _sortPerSecond = Config.Bind("17. Einsortieren", "Kartons pro Sekunde", 4, "Wie schnell der Bot einsortiert.");
        }

        private void HandleSorting()
        {
            if (!_sortEnabled.Value && !_sortTrashEmpty.Value) return;
            _sortAcc = Mathf.Min(_sortAcc + Mathf.Max(1, _sortPerSecond.Value), Mathf.Max(1, _sortPerSecond.Value) * 2f);
            SafeRun("Einsortieren", SortTick);
        }

        private static bool BoxBusy(InteractablePackagingBox_Item b)
        {
            var tb = Traverse.Create(b);
            return tb.Field("m_IsBeingHold").GetValue<bool>() || tb.Field("m_IsLerpingToPos").GetValue<bool>()
                   || tb.Field("m_IsTogglingOpenClose").GetValue<bool>();
        }

        private void SortTick()
        {
            int budget = Mathf.FloorToInt(_sortAcc);
            if (budget <= 0) return;
            var rm = S<RestockManager>();
            if (rm == null || rm.m_ItemPackagingBoxList == null) return;
            var ipc = S<InteractionPlayerController>();

            var full = new List<InteractablePackagingBox_Item>();
            var empty = new List<InteractablePackagingBox_Item>();
            foreach (var b in rm.m_ItemPackagingBoxList.ToArray())
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null || b.m_IsStored) continue;
                if (!b.IsValidObject() || !b.CanWorkerTakeBox() || BoxBusy(b)) continue;
                if (b.m_ItemCompartment.GetItemCount() <= 0)
                {
                    // Sicherheit: nur geöffnete Kartons, die schon 5 s leer sind (nie frisch gelieferte)
                    if (!Traverse.Create(b).Field("m_IsBoxOpened").GetValue<bool>()) continue;
                    if (!_emptySince.TryGetValue(b, out var since)) { _emptySince[b] = Time.unscaledTime; continue; }
                    if (Time.unscaledTime - since >= 5f) empty.Add(b);
                }
                else { _emptySince.Remove(b); full.Add(b); }
            }
            if (_emptySince.Count > 500) { foreach (var k in _emptySince.Keys.Where(k => k == null).ToList()) _emptySince.Remove(k); }

            int done = 0;

            // 1) Leere Kartons -> Mülleimer
            if (_sortTrashEmpty.Value && empty.Count > 0)
            {
                var bin = ShelfManager.GetTrashBinList()?.FirstOrDefault(t => t != null && t.IsValidObject());
                if (bin != null)
                    foreach (var b in empty)
                    {
                        if (done >= budget) break;
                        try { bin.DiscardBox(b, isPlayer: false); _emptySince.Remove(b); done++; _statTrashed++; } catch { }
                    }
            }

            // 2) Volle Kartons -> Lagerregal
            if (_sortEnabled.Value && full.Count > 0 && done < budget)
            {
                // Packs für die Opener: bis zur Kapazität aller Opener liegen lassen (Ziel-Sets zuerst behalten)
                var keep = new HashSet<InteractablePackagingBox_Item>();
                if (_sortKeepOpenerPacks.Value && _openerSelf.Value && _openerPackTypes.Count > 0)
                {
                    int cap = 0;
                    var ops = ShelfManager.GetAutoPackOpenerList();
                    if (ops != null) foreach (var o in ops) if (o != null) cap += Mathf.Max(0, o.m_MaxPackCount);
                    int kept = 0;
                    foreach (var b in full.Where(b => _openerPackTypes.Contains(b.m_ItemCompartment.GetItemType()))
                                          .OrderBy(b => _focusPackTypes.Contains(b.m_ItemCompartment.GetItemType()) ? 0 : 1))
                    {
                        if (kept >= cap) break;
                        keep.Add(b); kept += b.m_ItemCompartment.GetItemCount();
                    }
                }

                // Ware, die im Laden ein Regalfach hat, bleibt am Boden – daraus räumt der Bot die Regale selbst ein
                // (Kartons im Lagerregal kann nur ein Arbeiter wieder herausholen)
                var shelfTypes = new HashSet<EItemType>();
                var shelves = ShelfManager.GetShelfList();
                if (shelves != null)
                    foreach (var sh in shelves)
                    {
                        if (sh == null || sh.m_ItemNotForSale || !sh.IsValidObject()) continue;
                        foreach (var comp in sh.GetItemCompartmentList())
                            if (comp != null && !comp.m_ItemNotForSale && comp.GetItemType() != EItemType.None) shelfTypes.Add(comp.GetItemType());
                    }

                int noRoom = 0;
                foreach (var b in full)
                {
                    if (done >= budget) break;
                    if (keep.Contains(b)) continue;
                    var type = b.m_ItemCompartment.GetItemType();
                    if (type == EItemType.None) continue;
                    if (_stockSelf.Value && shelfTypes.Contains(type)) continue;
                    var comp = FindStoreCompartment(type, b.m_IsBigBox);
                    if (comp == null) { noRoom++; continue; }
                    try
                    {
                        b.DispenseItem(isPlayer: false, comp);
                        if (b.m_IsStored) { done++; _statSorted++; }
                    }
                    catch (Exception e) { Logger.LogWarning("[Einsortieren] " + e.Message); }
                }
                if (noRoom > 0 && Time.unscaledTime >= _sortFullLogAt)
                {
                    _sortFullLogAt = Time.unscaledTime + 600f;
                    Note($"Einsortieren: {noRoom} Kartons passen nicht mehr ins Lagerregal – mehr Lagerregale aufstellen?");
                }
            }

            _sortAcc -= done;
            if (done > 0 && (_statSorted + _statTrashed) % 25 < done)
                Note($"Einsortiert: {_statSorted} Kartons ins Lager, {_statTrashed} leere weggeworfen");
        }

        // Wie die Arbeiter: zuerst Fächer mit passendem Etikett/Inhalt, dann freie Fächer
        private static ShelfCompartment FindStoreCompartment(EItemType type, bool big)
        {
            // Spiel: ignoreNoneType = true -> nur Fächer mit genau dieser Ware; false -> auch leere Fächer
            foreach (bool onlyMatching in new[] { true, false })
            {
                List<WarehouseShelf> shelves;
                try { shelves = ShelfManager.GetWarehouseShelfListToStoreItem(type, big, onlyMatching); } catch { continue; }
                if (shelves == null) continue;
                foreach (var ws in shelves)
                {
                    if (ws == null || !ws.IsValidObject()) continue;
                    var c = ws.GetNonFullItemCompartment(type, big, onlyMatching);
                    if (c != null && c.HasEnoughSlot()) return c;
                }
            }
            return null;
        }
    }
}
