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
        private ConfigEntry<int> _sortPerSecond, _pullPerTick;
        private ConfigEntry<bool> _pullFromStorage;
        private float _sortAcc;
        private int _statSorted, _statTrashed;
        internal float _sortNoRoomAt = -1f;      // last time boxes did not fit on the storage shelves (advisor: warehouse worth it)
        private float _sortFullLogAt;
        private readonly Dictionary<InteractablePackagingBox_Item, float> _emptySince = new Dictionary<InteractablePackagingBox_Item, float>();

        private void BindSortConfig()
        {
            _sortEnabled = Config.Bind("17. Sorting", "Put loose boxes on the storage shelf", true, "Full boxes lying on the shop floor go onto the storage shelf (first slots with a matching label, then free ones).");
            _sortKeepOpenerPacks = Config.Bind("17. Sorting", "Leave packs for openers on the floor", true, "Packs of unfinished sets stay on the floor until one opener fill is reached – the bot fills the openers from them. The rest goes on the shelf.");
            _sortTrashEmpty = Config.Bind("17. Sorting", "Throw away empty boxes", true, "Empty boxes go into the trash bin (only if there is one in the shop).");
            _sortPerSecond = Config.Bind("17. Sorting", "Boxes per second", 4, "How fast the bot sorts.");
            _pullFromStorage = Config.Bind("17. Sorting", "Take boxes from the storage shelf", true, "If packs are missing for free openers or goods for half-empty shelf slots, the bot takes boxes from the storage shelf itself (like clicking a storage slot).");
            _pullPerTick = Config.Bind("17. Sorting", "Boxes from storage per round (openers)", 6, "At most this many boxes every 2 seconds for the openers.");
        }

        private void HandleSorting()
        {
            if (!_sortEnabled.Value && !_sortTrashEmpty.Value) return;
            _sortAcc = Mathf.Min(_sortAcc + Mathf.Max(1, _sortPerSecond.Value), Mathf.Max(1, _sortPerSecond.Value) * 2f);
            SafeRun("Sorting", SortTick);
        }

        // Is a box being carried / moved / opened right now?
        // Boxes delivered in front of the entrance can keep such a flag forever (seen: 176 boxes, all "busy", never stocked).
        // So: a flagged box that has not moved for 15 s is treated as free. A box someone really carries keeps moving.
        private readonly Dictionary<InteractablePackagingBox_Item, (float since, Vector3 pos)> _busySince = new Dictionary<InteractablePackagingBox_Item, (float, Vector3)>();
        private int _busyHold, _busyLerp, _busyToggle, _busyStuck;

        private bool BoxBusy(InteractablePackagingBox_Item b)
        {
            var tb = Traverse.Create(b);
            bool hold = tb.Field("m_IsBeingHold").GetValue<bool>(), lerp = tb.Field("m_IsLerpingToPos").GetValue<bool>(), toggle = tb.Field("m_IsTogglingOpenClose").GetValue<bool>();
            if (!hold && !lerp && !toggle) { _busySince.Remove(b); return false; }
            if (hold) _busyHold++; if (lerp) _busyLerp++; if (toggle) _busyToggle++;
            var p = b.transform.position;
            if (!_busySince.TryGetValue(b, out var s) || (s.pos - p).sqrMagnitude > 0.0004f)
            {
                _busySince[b] = (Time.unscaledTime, p);
                if (_busySince.Count > 2000) foreach (var k in _busySince.Keys.Where(k => k == null).ToList()) _busySince.Remove(k);
                return true;
            }
            // "opening" takes about 1 s in the game – a box stuck in it is freed after 3 s; carried/moving boxes after 15 s
            float wait = (!hold && !lerp && toggle) ? 3f : 15f;
            if (Time.unscaledTime - s.since < wait) return true;
            _busyStuck++;
            // reset the stuck flag in the game too, so the WORKERS can take the box again (not "being held" – that is real)
            if (!hold)
            {
                try { if (toggle) tb.Field("m_IsTogglingOpenClose").SetValue(false); if (lerp) tb.Field("m_IsLerpingToPos").SetValue(false); } catch { }
            }
            _busySince.Remove(b);
            return false;                                           // flag stuck, box lies still -> free
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
                var noRoomTypes = new Dictionary<string, int>();
                foreach (var b in full)
                {
                    if (done >= budget) break;
                    if (keep.Contains(b)) continue;
                    var type = b.m_ItemCompartment.GetItemType();
                    if (type == EItemType.None) continue;
                    if (_stockSelf.Value && shelfTypes.Contains(type)) continue;
                    var comp = FindStoreCompartment(type, b.m_IsBigBox);
                    if (comp == null)
                    {
                        noRoom++;
                        string nm = null;
                        try { nm = InventoryBase.GetItemData(type)?.GetName(); } catch { }
                        nm = (nm ?? type.ToString()) + (b.m_IsBigBox ? T(" (large)") : T(" (small)"));
                        noRoomTypes[nm] = (noRoomTypes.TryGetValue(nm, out var nc) ? nc : 0) + 1;
                        continue;
                    }
                    try
                    {
                        b.DispenseItem(isPlayer: false, comp);
                        if (b.m_IsStored) { done++; _statSorted++; }
                    }
                    catch (Exception e) { Logger.LogWarning("[Sorting]" + e.Message); }
                }
                if (noRoom > 0) _sortNoRoomAt = Time.unscaledTime;
                if (noRoom > 0 && Time.unscaledTime >= _sortFullLogAt)
                {
                    _sortFullLogAt = Time.unscaledTime + 600f;
                    NoteF($"Sorting: {noRoom} boxes no longer fit on the storage shelf – place more storage shelves? {string.Join(", ", noRoomTypes.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Value}× {kv.Key}"))}");
                }
            }

            _sortAcc -= done;
            if (done > 0 && (_statSorted + _statTrashed) % 25 < done)
                NoteF($"Sorted: {_statSorted} boxes into storage, {_statTrashed} empty ones thrown away");
        }

        // ================= Kartons aus dem Lagerregal holen =================
        // Wie das Spiel, wenn du auf ein Lagerfach klickst (InteractableStorageCompartment.OnMouseButtonUp):
        // letzten Karton-Eintrag entnehmen und einen echten Karton daraus erzeugen – hier direkt an der Stelle, wo er gebraucht wird.
        private int _statPulled;

        private float _pullWhyAt;
        private string _pullWhy = "";

        // Takes boxes out of the storage shelves. Looks at EVERY box entry of a compartment (not only the top one) –
        // like the counting does – and takes the matching box out wherever it sits.
        private int PullFromStorage(Func<EItemType, int> priority, int max, Vector3 pos)
        {
            var sm = S<ShelfManager>();
            if (sm == null || max <= 0) return 0;
            if (sm.m_WarehouseShelfList == null || sm.m_WarehouseShelfList.Count == 0) return 0;   // no storage shelf at all -> nothing to take
            var found = new List<(ShelfCompartment comp, System.Collections.IList recs, int index, EItemType type, int prio)>();
            int compartments = 0, records = 0;
            foreach (var ws in sm.m_WarehouseShelfList)
            {
                if (ws == null) continue;
                var comps = Traverse.Create(ws).Field("m_ItemCompartmentList").GetValue<List<ShelfCompartment>>();
                if (comps == null) continue;
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var recs = Traverse.Create(comp).Field("m_StoredBoxRecordList").GetValue() as System.Collections.IList;
                    if (recs == null || recs.Count == 0) continue;
                    compartments++; records += recs.Count;
                    // newest box of each type first (from the top of the stack)
                    var seen = new HashSet<EItemType>();
                    for (int i = recs.Count - 1; i >= 0; i--)
                    {
                        var tr = Traverse.Create(recs[i]);
                        var type = tr.Field("itemType").GetValue<EItemType>();
                        if (type == EItemType.None || tr.Field("amount").GetValue<int>() <= 0 || !seen.Add(type)) continue;
                        int p = priority(type);
                        if (p >= 0) found.Add((comp, recs, i, type, p));
                    }
                }
            }
            int pulled = 0;
            var usedComps = new HashSet<ShelfCompartment>();
            foreach (var f in found.OrderBy(x => x.prio))
            {
                if (pulled >= max) break;
                if (!usedComps.Add(f.comp)) continue;          // indices change after taking a box – one per compartment and round
                try
                {
                    object recObj;
                    if (f.index == f.recs.Count - 1)
                    {
                        if (!f.comp.TryPopLastStoredBoxRecord(out var top)) { Why($"pop failed for {f.type}"); continue; }
                        recObj = top;
                    }
                    else
                    {
                        recObj = f.recs[f.index];
                        f.recs.RemoveAt(f.index);                  // box from the middle of the stack
                    }
                    StoredBoxVisualBatcher.RebuildImmediate(f.comp);
                    var tr = Traverse.Create(recObj);
                    var type = tr.Field("itemType").GetValue<EItemType>();
                    int amount = tr.Field("amount").GetValue<int>();
                    bool big = tr.Field("isBigBox").GetValue<bool>();
                    var p = pos + new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), 0.6f + pulled * 0.35f, UnityEngine.Random.Range(-0.4f, 0.4f));
                    var box = RestockManager.SpawnPackageBoxItemAtTransform(type, amount, big, p, Quaternion.identity);
                    if (box != null) { pulled++; _statPulled++; }
                    else Why($"spawning a box of {type} failed");
                }
                catch (Exception e) { Logger.LogWarning("[Take from storage] " + e.Message); break; }
            }
            if (pulled == 0) Why(found.Count == 0 ? $"no matching box ({compartments} storage compartments, {records} boxes)" : $"{found.Count} matching, none taken");
            if (pulled > 0 && _statPulled % 20 < pulled) NoteF($"Taken from storage: {_statPulled} boxes in total");
            return pulled;
        }

        // Reason why nothing came out of storage – once a minute in the log
        private void Why(string why)
        {
            _pullWhy = why;
            if (Time.unscaledTime < _pullWhyAt) return;
            _pullWhyAt = Time.unscaledTime + 60f;
            Logger.LogInfo("[Take from storage] nothing taken: " + why);
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
