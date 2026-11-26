using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Fast access to private game fields.
    // Traverse.Create(x).Field("…") looks the field up by name EVERY time and allocates – with 900+ boxes,
    // several times per second, that cost a lot of frame time and garbage (lag spikes).
    // These accessors are built once and are as fast as a normal field access.
    // If a field is missing (game update), the accessor is null and the helper returns a safe default
    // (Compat.cs switches the feature off in that case anyway).
    internal static class Fast
    {
        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.Field(typeof(T), name) != null ? AccessTools.FieldRefAccess<T, F>(name) : null; }
            catch { return null; }
        }

        private static AccessTools.FieldRef<object, F> RefObj<F>(Type t, string name)
        {
            try { return t != null && AccessTools.Field(t, name) != null ? AccessTools.FieldRefAccess<F>(t, name) : null; }
            catch { return null; }
        }

        // ---- boxes ----
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Item, bool> BoxHold = Ref<InteractablePackagingBox_Item, bool>("m_IsBeingHold");
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Item, bool> BoxLerp = Ref<InteractablePackagingBox_Item, bool>("m_IsLerpingToPos");
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Item, bool> BoxToggle = Ref<InteractablePackagingBox_Item, bool>("m_IsTogglingOpenClose");
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Item, bool> BoxOpened = Ref<InteractablePackagingBox_Item, bool>("m_IsBoxOpened");
        private static readonly AccessTools.FieldRef<object, List<Item>> CompStored =
            RefObj<List<Item>>(AccessTools.Field(typeof(InteractablePackagingBox_Item), "m_ItemCompartment")?.FieldType, "m_StoredItemList");

        public static bool Hold(InteractablePackagingBox_Item b) => BoxHold != null && BoxHold(b);
        public static bool Lerp(InteractablePackagingBox_Item b) => BoxLerp != null && BoxLerp(b);
        public static bool Toggle(InteractablePackagingBox_Item b) => BoxToggle != null && BoxToggle(b);
        public static bool Opened(InteractablePackagingBox_Item b) => BoxOpened != null && BoxOpened(b);
        public static void SetLerp(InteractablePackagingBox_Item b, bool v) { if (BoxLerp != null) BoxLerp(b) = v; }
        public static void SetToggle(InteractablePackagingBox_Item b, bool v) { if (BoxToggle != null) BoxToggle(b) = v; }

        // real item objects in the box (GetItemCount also counts items that are not created yet)
        public static int RealItems(InteractablePackagingBox_Item b)
        {
            if (CompStored == null || b == null || b.m_ItemCompartment == null) return 0;
            try { return CompStored(b.m_ItemCompartment)?.Count ?? 0; } catch { return 0; }
        }

        // ---- card packages (grading) ----
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Card, bool> CardOpening = Ref<InteractablePackagingBox_Card, bool>("m_IsOpeningBox");
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Card, bool> CardHold = Ref<InteractablePackagingBox_Card, bool>("m_IsBeingHold");
        private static readonly AccessTools.FieldRef<InteractablePackagingBox_Card, List<CardData>> CardList = Ref<InteractablePackagingBox_Card, List<CardData>>("m_StoredCardDataList");
        public static bool Opening(InteractablePackagingBox_Card b) => CardOpening != null && CardOpening(b);
        public static bool Hold(InteractablePackagingBox_Card b) => CardHold != null && CardHold(b);
        public static int CardCount(InteractablePackagingBox_Card b) => CardList != null ? (CardList(b)?.Count ?? 0) : 0;

        // ---- openers ----
        private static readonly AccessTools.FieldRef<InteractableAutoPackOpener, bool> OpenerBoxed = Ref<InteractableAutoPackOpener, bool>("m_IsBoxedUp");
        public static bool BoxedUp(InteractableAutoPackOpener o) => OpenerBoxed != null && OpenerBoxed(o);

        // ---- end of day screen (checked every frame) ----
        private static readonly AccessTools.FieldRef<EndOfDayReportScreen, bool> EodLoading = Ref<EndOfDayReportScreen, bool>("m_IsLoadingNextDay");
        private static readonly AccessTools.FieldRef<EndOfDayReportScreen, bool> EodActive = Ref<EndOfDayReportScreen, bool>("m_IsActive");
        public static bool EodLoadingNext(EndOfDayReportScreen r) => EodLoading != null && EodLoading(r);
        public static bool EodIsActive(EndOfDayReportScreen r) => EodActive != null && EodActive(r);

        // ---- player modes (CanAct, every second) ----
        private static readonly AccessTools.FieldRef<InteractionPlayerController, bool>[] PlayerModes =
        {
            Ref<InteractionPlayerController, bool>("m_IsCashCounterMode"),
            Ref<InteractionPlayerController, bool>("m_IsPhoneScreenMode"),
            Ref<InteractionPlayerController, bool>("m_IsWorkerInteractMode"),
            Ref<InteractionPlayerController, bool>("m_IsPlayTableGameMode"),
            Ref<InteractionPlayerController, bool>("m_IsDecoScreenMode"),
            Ref<InteractionPlayerController, bool>("m_IsExitingViewCardAlbumMode"),
        };
        public static bool AnyPlayerMode(InteractionPlayerController ipc)
        {
            foreach (var f in PlayerModes) if (f != null && f(ipc)) return true;
            return false;
        }

        // ---- storage shelf (box records) ----
        private static readonly AccessTools.FieldRef<WarehouseShelf, List<ShelfCompartment>> WsComps = Ref<WarehouseShelf, List<ShelfCompartment>>("m_ItemCompartmentList");
        private static readonly System.Reflection.FieldInfo CompRecs = AccessTools.Field(typeof(ShelfCompartment), "m_StoredBoxRecordList");
        public static List<ShelfCompartment> Compartments(WarehouseShelf ws) => WsComps != null ? WsComps(ws) : null;
        public static System.Collections.IList Records(ShelfCompartment c) => CompRecs?.GetValue(c) as System.Collections.IList;

        // record fields (the record may be a struct -> cached FieldInfo, looked up once per type)
        private static Type _recType;
        private static System.Reflection.FieldInfo _recItem, _recAmount, _recBig;
        private static void RecFields(object r)
        {
            var t = r.GetType();
            if (t == _recType) return;
            _recType = t;
            _recItem = AccessTools.Field(t, "itemType"); _recAmount = AccessTools.Field(t, "amount"); _recBig = AccessTools.Field(t, "isBigBox");
        }
        public static EItemType RecType(object r) { if (r == null) return EItemType.None; RecFields(r); return _recItem != null ? (EItemType)_recItem.GetValue(r) : EItemType.None; }
        public static int RecAmount(object r) { if (r == null) return 0; RecFields(r); return _recAmount != null ? (int)_recAmount.GetValue(r) : 0; }
        public static bool RecBig(object r) { if (r == null) return false; RecFields(r); return _recBig != null && (bool)_recBig.GetValue(r); }

        // ---- types of other mods ----
        // AccessTools.TypeByName searches ALL types of ALL loaded assemblies – with 35 mods that took ~200 ms per call,
        // and Grading/Upgrades called it every minute (stutter). Looked up once, also "not found" is remembered.
        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
        public static Type TypeByName(string name)
        {
            if (Types.TryGetValue(name, out var t)) return t;
            try { t = AccessTools.TypeByName(name); } catch { t = null; }
            Types[name] = t;
            return t;
        }

        // m_ScreenGrp on different screen types: one accessor per type, cached
        private static readonly Dictionary<Type, AccessTools.FieldRef<object, GameObject>> ScreenGrp = new Dictionary<Type, AccessTools.FieldRef<object, GameObject>>();
        public static GameObject ScreenGroup(Component screen)
        {
            var t = screen.GetType();
            if (!ScreenGrp.TryGetValue(t, out var f)) ScreenGrp[t] = f = RefObj<GameObject>(t, "m_ScreenGrp");
            return f != null ? f(screen) : null;
        }
    }

    // Measures how much time the bot needs (shown in the settings)
    public partial class Plugin
    {
        private readonly Stopwatch _perfWatch = new Stopwatch();
        private double _perfFrameMs;          // bot time in the current frame
        private double _perfSecMs, _perfSecMax;   // sum / worst frame in the current second
        private float _perfSecStart;
        internal float _perfAvgMsPerSec, _perfWorstFrameMs;   // last full second (for the dashboard)

        private void PerfBegin() => _perfWatch.Restart();
        private void PerfEnd()
        {
            _perfWatch.Stop();
            _perfFrameMs += _perfWatch.Elapsed.TotalMilliseconds;
        }
        // once per frame, at the end of Update
        private void PerfFrame()
        {
            _perfSecMs += _perfFrameMs;
            if (_perfFrameMs > _perfSecMax) _perfSecMax = _perfFrameMs;
            _perfFrameMs = 0;
            if (Time.unscaledTime - _perfSecStart >= 1f)
            {
                _perfAvgMsPerSec = (float)_perfSecMs;
                _perfWorstFrameMs = (float)_perfSecMax;
                _perfSecMs = 0; _perfSecMax = 0;
                _perfSecStart = Time.unscaledTime;
            }
        }
    }
}
