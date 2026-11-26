using System.Diagnostics;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ShopBot
{
    // Every shop is different: 0 or 50 pack openers, 10 or 300 shelf slots, small or big openers/shelves (also modded ones).
    // Once per second the bot looks at the shop (one pass over all shelf slots, openers and display cases) and derives:
    //  - what to do: stock shelves, fill openers – or, without openers, open packs by hand (HandOpen.cs)
    //  - how fast: "auto speed" = as fast as the shop needs right now (your slider value is the minimum)
    // A time limit per frame keeps that from causing lag: whatever does not fit in this frame is done in the next round.
    public partial class Plugin
    {
        private ConfigEntry<bool> _autoSpeed;

        // survey of the shop (last second)
        internal int _scOpeners, _scOpenerCap, _scOpenerFree, _scOpenersIdle;
        internal float _scOpenerPacksPerMin;   // what all openers open per minute (from each machine's time per pack)
        internal int _scFloorBoxes, _scDeliveryBoxes;   // full boxes lying in the shop / still to be delivered
        internal int _scShelves, _scSlots, _scSlotsMissing, _scSlotsEmpty;
        internal int _scDisplays, _scDisplaySlots, _scDisplayEmpty;
        internal int _scStorageShelves, _scSortBacklog;
        // effective speeds this round (for the dashboard)
        internal int _rateShelf, _rateOpener, _rateCards, _rateSort;

        private void BindShopScanConfig()
        {
            _autoSpeed = Config.Bind("16. Self-stocking", "Speed adapts to the shop", true,
                "On: the bot measures your shop every second (openers, shelf slots, display cases, loose boxes) and works as fast as needed – the speed sliders are the minimum. A time limit per frame prevents lag. Off: exactly the slider values.");
        }

        private bool AutoSpeed => _autoSpeed == null || _autoSpeed.Value;

        // ---- time limit per frame ----
        private const double FrameBudgetMs = 4.0;
        private readonly Stopwatch _stageWatch = new Stopwatch();
        private void StageStart() => _stageWatch.Restart();
        // the limit counts from the start of the actual work (filling, stocking, sorting) –
        // the survey of hundreds of boxes before it must not eat up the time (0.7.4 test: openers filled only in drips)
        private void ActionStart() => _stageWatch.Restart();
        // true when this frame's share for bot work is used up -> stop, the rest comes next round
        private bool OutOfTime
        {
            get
            {
                if (_stageWatch.Elapsed.TotalMilliseconds < FrameBudgetMs) return false;
                _perfLimitHit = true;
                return true;
            }
        }

        // ---- once a minute a performance line in the log (so lag can be checked from the log) ----
        private bool _perfLimitHit;
        private int _perfLimitFrames, _perfOpenerMoved, _perfShelfMoved;
        private float _perfLogAt = 60f, _perfMinWorst, _perfMinSum; private int _perfMinSecs;

        private void PerfMinute()
        {
            if (_perfLimitHit) { _perfLimitFrames++; _perfLimitHit = false; }
        }

        // called once per second (stage 0)
        private void PerfSecond()
        {
            _perfMinSum += _perfAvgMsPerSec; _perfMinSecs++;
            if (_perfWorstFrameMs > _perfMinWorst) _perfMinWorst = _perfWorstFrameMs;
            if (Time.unscaledTime < _perfLogAt) return;
            _perfLogAt = Time.unscaledTime + 60f;
            float avg = _perfMinSecs > 0 ? _perfMinSum / _perfMinSecs : 0f;
            var slow = string.Join(", ", _featMaxMs.Where(kv => kv.Value >= 5.0).OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value:0} ms"));
            _featMaxMs.Clear();
            Logger.LogInfo($"[Perf] bot {avg:0.0} ms/s, slowest frame {_perfMinWorst:0.0} ms, FPS {1f / Mathf.Max(0.001f, Time.smoothDeltaTime):0} | slowest tasks: {(slow.Length > 0 ? slow : "all under 5 ms")} | last minute: openers +{_perfOpenerMoved} packs, shelves +{_perfShelfMoved} items, time limit hit in {_perfLimitFrames} rounds | speed: openers {_rateOpener}/s, shelves {_rateShelf}/s, sorting {_rateSort}/s | openers {_scOpeners} (use {_scOpenerPacksPerMin:0} packs/min), free room {_scOpenerFree}, idle {_scOpenersIdle} | boxes in shop {_scFloorBoxes} + {_scDeliveryBoxes} on the way (limit {FloorBoxLimit})");
            _perfMinSum = 0f; _perfMinSecs = 0; _perfMinWorst = 0f; _perfLimitFrames = 0; _perfOpenerMoved = 0; _perfShelfMoved = 0;
        }

        // Worker planning: what the bot can surely do per second – the time limit per frame may stop it before a high auto speed,
        // so workers are still planned for big backlogs
        private static float BotCap(int rate) => Mathf.Min(rate, 30);

        private static int Need(int cfg, float auto, int cap) => Mathf.Max(cfg, Mathf.Min(cap, Mathf.CeilToInt(auto)));

        // Stage 1: one pass over the shop
        private void ScanShop()
        {
            // pack openers (any number, any size: m_MaxPackCount per machine)
            int op = 0, cap = 0, free = 0, idle = 0;
            float perMin = 0f;
            var ops = ShelfManager.GetAutoPackOpenerList();
            if (ops != null)
                foreach (var o in ops)
                {
                    if (o == null || !o.gameObject.activeInHierarchy || Fast.BoxedUp(o)) continue;
                    op++;
                    int max = Mathf.Max(0, o.m_MaxPackCount);
                    cap += max;
                    if (o.m_PackOpenTime > 0.01f) perMin += 60f / o.m_PackOpenTime;
                    int stored = o.GetStoredItemList().Count;
                    if (!o.GetIsProcessing()) { free += Mathf.Max(0, max - stored); if (stored == 0) idle++; }
                }
            _scOpeners = op; _scOpenerCap = cap; _scOpenerFree = free; _scOpenersIdle = idle; _scOpenerPacksPerMin = perMin;
            CountFloorBoxes();

            // shelves: all shelf slots once (also used for "shelves before openers")
            int shelves = 0, slots = 0, missing = 0, empty = 0;
            _shelfNeed.Clear();
            var list = ShelfManager.GetShelfList();
            if (list != null)
                foreach (var shelf in list)
                {
                    if (shelf == null || shelf.m_ItemNotForSale || !shelf.IsValidObject()) continue;
                    shelves++;
                    foreach (var comp in shelf.GetItemCompartmentList())
                    {
                        if (comp == null || comp.m_ItemNotForSale || !comp.m_CanPutItem) continue;
                        slots++;
                        int count = comp.GetItemCount();
                        if (count == 0) empty++;
                        var t = comp.GetItemType();
                        if (t == EItemType.None) continue;
                        int miss = comp.GetMaxItemCount() - count;
                        if (miss <= 0) continue;
                        missing += miss;
                        _shelfNeed[t] = (_shelfNeed.TryGetValue(t, out var n) ? n : 0) + miss;
                    }
                }
            _scShelves = shelves; _scSlots = slots; _scSlotsMissing = missing; _scSlotsEmpty = empty;
            TrackShelfNeed();

            // card display cases
            int disp = 0, dslots = 0, dempty = 0;
            var cs = ShelfManager.GetCardShelfList();
            if (cs != null)
                foreach (var c in cs)
                {
                    if (c == null || !c.IsValidObject()) continue;
                    disp++;
                    foreach (var comp in c.GetCardCompartmentList())
                    {
                        if (comp == null || comp.m_ItemNotForSale) continue;
                        dslots++;
                        if (comp.m_StoredCardList.Count == 0) dempty++;
                    }
                }
            _scDisplays = disp; _scDisplaySlots = dslots; _scDisplayEmpty = dempty;
            _scStorageShelves = S<ShelfManager>()?.m_WarehouseShelfList?.Count ?? 0;

            // speeds: fill the openers within ~10 s, the shelves within ~20 s, display cases ~20 s, loose boxes ~20 s
            bool auto = AutoSpeed;
            _rateOpener = auto ? Need(_openerPerSecond.Value, free / 10f, 600) : _openerPerSecond.Value;
            _rateShelf = auto ? Need(_stockPerSecond.Value, missing / 20f, 400) : _stockPerSecond.Value;
            _rateCards = auto ? Need(_cardsPerSecond.Value, dempty / 20f, 60) : _cardsPerSecond.Value;
            _rateSort = auto ? Need(Mathf.Max(1, _sortPerSecond.Value), _scSortBacklog / 20f, 80) : Mathf.Max(1, _sortPerSecond.Value);
        }

        // ---- how many boxes may lie in the shop ----
        // Every box on the floor is a real 3D object (physics, rendering) – 600 of them cost the game a lot of FPS.
        // Boxes on the storage shelf are only records. So on the floor only what the openers use in the next minutes.
        private const float FloorMinutes = 5f;      // packs for the openers on the floor: about 5 minutes of use
        private const float EmergencyMinutes = 10f; // emergency restock: below 10 minutes of use

        // packs the openers use in N minutes (before the first survey: a quarter of a full fill)
        internal int OpenerUse(float minutes) =>
            _scOpenerPacksPerMin > 0f ? Mathf.CeilToInt(_scOpenerPacksPerMin * minutes) : Mathf.CeilToInt(_scOpenerCap / 4f);

        // opener packs that stay on the floor (the rest goes into storage); at least one fill of the biggest opener
        internal int FloorKeepPacks => Mathf.Min(_scOpenerCap, Mathf.Max(OpenerUse(FloorMinutes), 64 * 4));

        // max. full boxes in the shop (floor + on the way): opener boxes for ~5 min + room for shelf goods,
        // never above the setting "Max full boxes in the shop overall"
        internal int FloorBoxLimit
        {
            get
            {
                int cfg = Mathf.Max(20, _colMaxBoxesOnFloor.Value);
                if (!AutoSpeed) return cfg;
                int auto = Mathf.CeilToInt(FloorKeepPacks / 64f) + 40;
                return Mathf.Clamp(auto, 60, cfg);
            }
        }
        internal int BoxesInShop => _scFloorBoxes + _scDeliveryBoxes;

        private void CountFloorBoxes()
        {
            int floor = 0;
            var rm = S<RestockManager>();
            if (rm != null && rm.m_ItemPackagingBoxList != null)
                foreach (var b in rm.m_ItemPackagingBoxList)
                    if (b != null && !b.m_IsStored && b.m_ItemCompartment != null && b.m_ItemCompartment.GetItemCount() > 0 && b.gameObject.activeInHierarchy) floor++;
            int coming = 0;
            try { var cnt = CPlayerData.m_SpawnBoxItemCountWaitingList; if (cnt != null) foreach (var c in cnt) coming += Mathf.Max(0, c); } catch { }
            _scFloorBoxes = floor; _scDeliveryBoxes = coming;
        }

        // What the bot does with packs in this shop (dashboard)
        internal string ShopModeText()
        {
            if (_scOpeners > 0) return F($"Packs: the bot fills {_scOpeners} pack openers ({_scOpenerCap} packs per fill)");
            if (_handOpen.Value) return T("Packs: no pack opener yet – the bot opens packs by hand");
            return T("Packs: no pack opener and opening by hand is off – packs wait");
        }
    }
}
