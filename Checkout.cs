using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShopBot
{
    // Auto-Kasse: der Bot bedient ALLE Kassen gleichzeitig, an denen weder du noch ein Arbeiter steht.
    // Ablauf wie beim Arbeiter (InteractableCashierCounter.Update für NPCs), nur ohne Worker:
    //   ScanningItem -> Artikel/Karten scannen, TakingCash -> Geld/Karte nehmen, GivingChange -> Kartenzahlung bestätigen
    //   bzw. genau passendes Wechselgeld geben und abschließen (OnPressSpaceBar). So können alle Arbeiter Opener befüllen.
    public partial class Plugin
    {
        private ConfigEntry<bool> _autoCheckout;
        private ConfigEntry<float> _autoCheckoutScansPerSecond;
        private int _statCheckouts;
        private float _checkoutDayMoney; private int _checkoutDay = -1;
        private bool _readyFlag;

        private readonly Dictionary<InteractableCashierCounter, float> _counterNext = new Dictionary<InteractableCashierCounter, float>();

        private static readonly AccessTools.FieldRef<InteractableCashierCounter, bool> F_UsingCard = AccessTools.FieldRefAccess<InteractableCashierCounter, bool>("m_IsUsingCard");
        private static readonly AccessTools.FieldRef<InteractableCashierCounter, bool> F_ChangeReady = AccessTools.FieldRefAccess<InteractableCashierCounter, bool>("m_IsChangeReady");
        private static readonly AccessTools.FieldRef<InteractableCashierCounter, double> F_Total = AccessTools.FieldRefAccess<InteractableCashierCounter, double>("m_TotalScannedItemCost");
        private static readonly AccessTools.FieldRef<InteractableCashierCounter, double> F_Paid = AccessTools.FieldRefAccess<InteractableCashierCounter, double>("m_CustomerPaidAmount");
        private static readonly AccessTools.FieldRef<InteractableCashierCounter, double> F_Change = AccessTools.FieldRefAccess<InteractableCashierCounter, double>("m_CurrentMoneyChangeValue");
        private static readonly AccessTools.FieldRef<InteractableCashierCounter, bool> F_StartGivingChange = AccessTools.FieldRefAccess<InteractableCashierCounter, bool>("m_IsStartGivingChange");

        private void BindCheckoutConfig()
        {
            _autoCheckout = Config.Bind("5. Checkout", "Bot serves all free registers", true,
                "The bot serves every register where neither you nor a worker is standing. No cashiers needed – all workers can fill the pack openers.");
            _autoCheckoutScansPerSecond = Config.Bind("5. Checkout", "Bot checkout items per second", 8f,
                "How fast the bot scans at each register.");
        }

        // Jedes Bild (Update), unabhängig von Fenstern und davon, ob du gerade spielst
        private void HandleAutoCheckout()
        {
            if (!_autoCheckout.Value) return;
            var sm = S<ShelfManager>();
            if (sm == null || sm.m_CashierCounterList == null) return;
            float now = Time.unscaledTime;
            foreach (var c in sm.m_CashierCounterList)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.IsMannedByNPC() || c.IsMannedByPlayer()) continue;     // dort arbeitet jemand anderes
                var cust = c.m_CurrentCustomer;
                if (cust == null) continue;
                if (_counterNext.TryGetValue(c, out var next) && now < next) continue;
                try { _counterNext[c] = now + CheckoutStep(c, cust); }
                catch (Exception e) { _counterNext[c] = now + 2f; Logger.LogWarning("[Checkout]" + e.Message); }
            }
        }

        // Ein Arbeitsschritt; gibt die Wartezeit bis zum nächsten Schritt zurück
        private float CheckoutStep(InteractableCashierCounter c, Customer cust)
        {
            float scanGap = 1f / Mathf.Clamp(_autoCheckoutScansPerSecond.Value, 1f, 60f);
            switch (c.m_CashierCounterState)
            {
                case ECashierCounterState.ScanningItem:
                {
                    var items = cust.GetItemInBagList();
                    if (items != null)
                        foreach (var it in items)
                            if (it != null && it.m_InteractableScanItem != null && it.m_InteractableScanItem.IsNotScanned())
                            { it.m_InteractableScanItem.OnMouseButtonUp(); return scanGap; }
                    var cards = cust.GetCardInBagList();
                    if (cards != null)
                        foreach (var cd in cards)
                            if (cd != null && cd.IsNotScanned()) { cd.OnMouseButtonUp(); return scanGap; }
                    return 0.25f;                                              // alles gescannt – Kunde zahlt gleich
                }
                case ECashierCounterState.TakingCash:
                    if (cust.m_CustomerCash != null) cust.m_CustomerCash.OnMouseButtonUp();
                    return 0.3f;
                case ECashierCounterState.GivingChange:
                {
                    if (!F_StartGivingChange(c)) return 0.2f;
                    double total = F_Total(c);
                    if (F_UsingCard(c))
                    {
                        c.EvaluateCreditCard(total);                            // Kartenzahlung: exakter Betrag, schließt ab
                    }
                    else
                    {
                        // Genau passendes Wechselgeld (wie der Arbeiter, nur in einem Schritt)
                        F_Change(c) = Math.Max(0.0, F_Paid(c) - total);
                        Traverse.Create(c).Method("CheckChangeReady").GetValue();
                        if (!F_ChangeReady(c)) { Logger.LogWarning($"[Checkout] Change does not fit (paid {F_Paid(c):0.00}, total {total:0.00})"); return 1f; }
                        c.OnPressSpaceBar();
                    }
                    CountCheckout((float)total);
                    return 0.4f;
                }
            }
            return 0.25f;
        }

        private void CountCheckout(float value)
        {
            if (_checkoutDay != CPlayerData.m_CurrentDay) { _checkoutDay = CPlayerData.m_CurrentDay; _checkoutDayMoney = 0f; }
            _statCheckouts++;
            ReportCheckout(value);
            _checkoutDayMoney += value;
            if (_statCheckouts % 25 == 0)
                NoteF($"Bot checkout: {_statCheckouts} customers served (today {GameInstance.GetPriceString(_checkoutDayMoney)})");
        }
    }
}
