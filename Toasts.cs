using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ShopBot
{
    // Short in-game notifications for important events (set complete, new license, grading package, new phase, …)
    // and the PSA 10 progress. Shown top center while the dashboard is closed, fade out after 6 s.
    public partial class Plugin
    {
        private ConfigEntry<bool> _toastsOn;
        private readonly List<(string tpl, object[] args, float at)> _toasts = new List<(string, object[], float)>();
        private GUIStyle _toastStyle;

        // messages that are worth a notification (English templates, start of the text)
        private static readonly string[] ToastStarts =
        {
            "Set complete", "New license", "Grading package opened", "New game phase", "Shop expansion", "Warehouse expansion",
            "Warehouse (Shop B) unlocked", "New worker hired", "Pack machine boost", "Display cases full",
        };

        private void BindToastConfig()
        {
            _toastsOn = Config.Bind("1. General", "Show notifications", true, "Short notifications at the top of the screen for important events (set complete, new license, grading package opened, new game phase …).");
        }

        private void MaybeToast(string tpl, object[] args)
        {
            if (_toastsOn == null || !_toastsOn.Value || tpl == null) return;
            bool important = ToastStarts.Any(s => tpl.StartsWith(s, StringComparison.Ordinal)) || tpl.Contains("switched off");
            if (!important) return;
            _toasts.Add((tpl, args, Time.unscaledTime));
            if (_toasts.Count > 3) _toasts.RemoveAt(0);
        }

        private void DrawToasts()
        {
            if (_toasts.Count == 0) return;
            float now = Time.unscaledTime;
            _toasts.RemoveAll(t => now - t.at > 6f);
            if (_toasts.Count == 0) return;
            if (_toastStyle == null)
            {
                _toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = true };
                _toastStyle.normal.textColor = Color.white;
            }
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            var old = GUI.matrix; var oldC = GUI.color;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, y = 70f;
            foreach (var t in _toasts)
            {
                float age = now - t.at;
                float a = Mathf.Clamp01(age * 4f) * Mathf.Clamp01((6f - age) * 2f);      // fade in / out
                string text = "<color=#FF9933>ShopBot</color>  " + MsgText(t.tpl, t.args);
                var r = new Rect(sw / 2f - 320f, y - 8f * (1f - Mathf.Clamp01(age * 4f)), 640f, 44f);
                GUI.color = new Color(0f, 0f, 0f, 0.72f * a);
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, GUI.color, 0f, 12f);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Label(new Rect(r.x + 12, r.y, r.width - 24, r.height), text, _toastStyle);
                y += 52f;
            }
            GUI.matrix = old; GUI.color = oldC;
        }

        // ---------- Set completion + PSA 10 progress ----------
        private readonly HashSet<string> _setsDoneSeen = new HashSet<string>();
        private bool _setsDoneInit;
        internal int _psa10Have = -1;

        // called after the collection was read: notes newly completed sets, counts cards that have a PSA 10
        private void TrackCollectionProgress(List<SetInfo> sets)
        {
            foreach (var s in sets)
            {
                bool done = s.Complete || (s.Total > 0 && s.Have >= s.Total);
                if (!done) continue;
                if (_setsDoneSeen.Add(s.Name) && _setsDoneInit) NoteF($"Set complete: {s.Name} ({s.Total} cards)!");
            }
            _setsDoneInit = true;
            try
            {
                var inv = CPlayerData.m_GradedCardInventoryList;
                if (inv != null)
                    _psa10Have = inv.Where(g => g != null && g.amount >= 10).Select(g => $"{(int)g.expansionType}|{g.cardSaveIndex}|{g.isDestiny}").Distinct().Count();
            }
            catch { }
        }
    }
}
