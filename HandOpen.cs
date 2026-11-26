using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Early game without pack openers: the bot opens collection packs itself, the same way a pack opener machine does
    // (InteractableAutoPackOpener.OpenPack: ItemTypeToCollectionPackType + CardOpeningSequence.GetPackContentCardDataList),
    // and puts the cards into the album. Safety:
    //  - only while there is NO pack opener in the shop (as soon as you place one, the machines do it)
    //  - only packs of unfinished sets the bot bought for the collection (never goods for the shelves)
    //  - slowly (default 20 packs per minute, like a player), can be switched off
    public partial class Plugin
    {
        private ConfigEntry<bool> _handOpen;
        private ConfigEntry<int> _handOpenPerMinute;
        private float _handAcc;
        private int _statHandOpened;

        private void BindHandOpenConfig()
        {
            _handOpen = Config.Bind("2. Pack opener", "Open packs by hand while there is no pack opener", true,
                "Early game: as long as there is no pack opener in the shop, the bot opens packs of unfinished sets itself (like a pack opener machine) and puts the cards into the album.");
            _handOpenPerMinute = Config.Bind("2. Pack opener", "Packs opened by hand per minute", 20, "Speed of opening by hand (about the pace of a player).");
        }

        private void HandleHandOpen()
        {
            if (!_handOpen.Value || !ColActive || _openerPackTypes.Count == 0) return;
            if ((ShelfManager.GetAutoPackOpenerList()?.Count(o => o != null) ?? 0) > 0) { _handAcc = 0f; return; }   // machines do it
            _handAcc = Mathf.Min(_handAcc + Mathf.Max(1, _handOpenPerMinute.Value) / 60f, 10f);
            if (_handAcc < 1f) return;
            Guard("Open packs by hand", HandOpenTick);
        }

        private void HandOpenTick()
        {
            if (LightManager.GetHasDayEnded()) return;
            var rm = S<RestockManager>();
            // the singleton may only register on first use (like the grading sequence) -> also look in the scene
            var seq = S<CardOpeningSequence>() ?? UnityEngine.Object.FindAnyObjectByType<CardOpeningSequence>();
            if (rm == null || seq == null) return;
            int budget = Mathf.FloorToInt(_handAcc);
            int opened = 0, cards = 0;
            foreach (var b in rm.m_ItemPackagingBoxList.ToArray())
            {
                if (opened >= budget) break;
                if (b == null || !b.gameObject.activeInHierarchy || b.m_ItemCompartment == null || b.m_IsStored) continue;
                var type = b.m_ItemCompartment.GetItemType();
                if (!_openerPackTypes.Contains(type) || BoxBusy(b)) continue;
                if (RealItemCount(b) <= 0)
                {
                    if (!Fast.Opened(b)) { try { b.SetOpenCloseBox(true, false); } catch { } }
                    continue;
                }
                var packType = InventoryBase.ItemTypeToCollectionPackType(type);
                while (opened < budget && RealItemCount(b) > 0)
                {
                    var item = b.m_ItemCompartment.GetFirstItem();
                    if (item == null) break;
                    // the same call the pack opener machine uses – card odds, rarities and god packs stay the game's own
                    var list = seq.GetPackContentCardDataList(packType, false);
                    if (list == null || list.Count == 0) return;           // game refused: stop, try again later
                    foreach (var cd in list) { if (cd != null) { CPlayerData.AddCard(cd, 1); cards++; } }
                    b.m_ItemCompartment.RemoveItem(item);
                    item.DisableItem();
                    opened++;
                }
            }
            _handAcc -= opened;
            if (opened > 0)
            {
                _statHandOpened += opened;
                S<InteractionPlayerController>()?.m_CollectionBinderFlipAnimCtrl?.SetCanUpdateSort(true);
                if (_statHandOpened % 20 < opened) NoteF($"Opened by hand (no pack opener yet): {_statHandOpened} packs in total");
            }
        }
    }
}
