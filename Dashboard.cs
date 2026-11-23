using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ShopBot
{
    // ShopBot-Dashboard (F9) – modernes Bedienfeld mit Seitenleiste, Karten, Fortschrittsbalken, Schaltern und Reglern.
    public partial class Plugin
    {
        // ---------- Zustand ----------
        private bool _dashOpen, _dashLockedUi;
        private int _dashPage;
        private Rect _dashRect;
        private bool _dashRectInit;
        private Vector2 _dashScroll;
        private int _histFilter;
        private ConfigEntry<KeyboardShortcut> _dashKey;
        private ConfigEntry<float> _dashScale;
        private float _nextConfigSave;

        private void OnApplicationQuit() { try { Config.Save(); } catch { } }

        // Von anderen Teilen gefüllt
        private List<(string name, int have, int total)> _dashFocus = new List<(string, int, int)>();
        private int _dashSetsTotal, _dashSetsDone, _dashSetsBuyable, _dashStockTarget, _dashStockHave;
        private List<(string worker, string main, string side)> _dashPlan = new List<(string, string, string)>();
        private readonly List<(string time, string msg, int cat)> _history = new List<(string, string, int)>();

        // Zwischenspeicher (nicht jedes Bild neu rechnen)
        private float _dashCacheAt, _dashSetsAt;
        private int _cOpeners, _cCap, _cInside, _cRunning, _cDone, _cStorageCards, _cStorageMax, _cStorageGraded, _cGrading;
        private readonly Dictionary<string, int> _cBonus = new Dictionary<string, int>();
        private readonly List<(string name, string main, string side, int bonus, float checkout, float restock)> _cWorkers = new List<(string, string, string, int, float, float)>();
        private int _cSetsDone, _cSetsTotal;
        private List<SetInfo> _cAllSets = new List<SetInfo>();

        private static readonly string[] Pages = { "Übersicht", "Sammlung", "Laden & Preise", "Kunden", "Karten", "Arbeiter", "Verlauf" };
        private static readonly string[] HistCats = { "Alle", "Einkauf", "Kunden", "Karten", "Arbeiter", "Laden" };

        // ---------- Farben ----------
        private static readonly Color CBg = new Color(0.055f, 0.063f, 0.086f, 0.98f);
        private static readonly Color CSide = new Color(0.075f, 0.086f, 0.118f, 1f);
        private static readonly Color CCard = new Color(0.106f, 0.122f, 0.165f, 1f);
        private static readonly Color CCardHi = new Color(0.14f, 0.16f, 0.215f, 1f);
        private static readonly Color CLine = new Color(0.2f, 0.225f, 0.29f, 1f);
        private static readonly Color CText = new Color(0.92f, 0.94f, 0.97f);
        private static readonly Color CDim = new Color(0.56f, 0.6f, 0.68f);
        private static readonly Color CAccent = new Color(1f, 0.6f, 0.2f);
        private static readonly Color CGreen = new Color(0.27f, 0.82f, 0.48f);
        private static readonly Color CBlue = new Color(0.33f, 0.6f, 1f);
        private static readonly Color CPurple = new Color(0.7f, 0.5f, 1f);
        private static readonly Color CRed = new Color(0.95f, 0.36f, 0.36f);
        private static readonly Color CYellow = new Color(1f, 0.82f, 0.3f);
        private static readonly Color[] CatColors = { new Color(0.6f, 0.62f, 0.7f), new Color(0.33f, 0.6f, 1f), new Color(0.27f, 0.82f, 0.48f), new Color(0.7f, 0.5f, 1f), new Color(1f, 0.6f, 0.2f), new Color(0.6f, 0.62f, 0.7f) };

        // ================= Steuerung =================
        private void HandleDashboardKey()
        {
            if (_dashKey == null)
            {
                _dashKey = Config.Bind("1. Allgemein", "Hotkey Dashboard", new KeyboardShortcut(KeyCode.F9), "Öffnet/schließt das ShopBot-Dashboard.");
                _dashScale = Config.Bind("1. Allgemein", "Dashboard-Größe", 0f, "0 = automatisch passend zur Bildschirmhöhe, sonst z. B. 1.0 / 1.25 / 1.5.");
            }
            // Einstellungen gesammelt speichern statt bei jeder Regler-Bewegung (sonst Ruckeln)
            if (Config.SaveOnConfigSet) Config.SaveOnConfigSet = false;
            if (Time.unscaledTime >= _nextConfigSave) { _nextConfigSave = Time.unscaledTime + 30f; try { Config.Save(); } catch { } }

            if (_dashKey.Value.IsDown()) SetDash(!_dashOpen);
            if (_dashOpen && Input.GetKeyDown(KeyCode.Escape)) SetDash(false);
            if (_dashOpen) RefreshDashCache();
        }

        private void SetDash(bool open)
        {
            if (open == _dashOpen) return;
            _dashOpen = open;
            var ipc = S<InteractionPlayerController>();
            if (open)
            {
                _dashCacheAt = _dashSetsAt = 0f;
                if (ipc != null && !ipc.IsInUIMode())
                {
                    ipc.EnterUIMode();
                    ipc.m_WalkerCtrl?.SetStopMovement(true);
                    _dashLockedUi = true;
                }
            }
            if (!open) { try { Config.Save(); } catch { } }
            if (!open && _dashLockedUi)
            {
                _dashLockedUi = false;
                if (ipc != null)
                {
                    ipc.m_WalkerCtrl?.SetStopMovement(false);
                    ipc.ExitUIMode();
                }
            }
        }

        private static int Category(string msg)
        {
            string m = msg.ToLowerInvariant();
            if (m.Contains("gekauft:") || m.Contains("nachbestellt") || m.Contains("vermissen") || m.Contains("sammlung")) return 1;
            if (m.Contains("ankauf") || m.Contains("tausch") || m.Contains("kunde") || m.StartsWith("gekauft für")) return 2;
            if (m.Contains("grad") || m.Contains("ablage") || m.Contains("opener")) return 3;
            if (m.Contains("arbeiter") || m.Contains("einsatzplan") || m.Contains("bonus") || m.Contains("upgrade") || m.Contains("kasse")) return 4;
            return 5;
        }

        private void RecordHistory(string msg)
        {
            string time = "";
            try
            {
                if (CSingleton<LightManager>.IsSet)
                    time = $"T{CPlayerData.m_CurrentDay + 1} {LightManager.GetTimeHour():00}:{LightManager.GetTimeMinute():00}";
            }
            catch { }
            _history.Add((time, msg, Category(msg)));
            if (_history.Count > 300) _history.RemoveAt(0);
        }

        // Geld formatieren, ohne im Hauptmenü Spiel-Objekte anzulegen
        private static string Money(double v) =>
            CSingleton<CGameManager>.IsSet ? GameInstance.GetPriceString((float)v) : "$" + v.ToString("N0");

        private bool DashGameReady => CSingleton<CGameManager>.IsSet && _readySince >= 0f && S<ShelfManager>() != null;

        private void RefreshDashCache()
        {
            if (!DashGameReady) return;
            float now = Time.unscaledTime;
            if (now - _dashCacheAt >= 1f)
            {
                _dashCacheAt = now;
                try
                {
                    _cOpeners = _cCap = _cInside = _cRunning = _cDone = 0;
                    var ops = ShelfManager.GetAutoPackOpenerList();
                    if (ops != null)
                        foreach (var o in ops)
                        {
                            if (o == null) continue;
                            int stored = o.GetStoredItemList().Count;
                            _cOpeners++; _cCap += o.m_MaxPackCount; _cInside += stored;
                            if (o.GetIsProcessing()) { if (stored == 0) _cDone++; else _cRunning++; }
                        }
                    _cStorageCards = _cStorageMax = _cStorageGraded = 0;
                    var st = ShelfManager.GetCardStorageShelfList();
                    if (st != null)
                        foreach (var s in st)
                        {
                            if (s == null) continue;
                            _cStorageCards += s.GetTotalCardAmount(); _cStorageMax += s.GetBoxTotalCardCountMax();
                            var l = s.GetCompactCardDataAmountList();
                            if (l != null) _cStorageGraded += l.Count(e => e.gradedCardIndex > 0);
                        }
                    _cGrading = CPlayerData.m_GradeCardInProgressList?.Count ?? 0;
                    _cBonus.Clear();
                    _cWorkers.Clear();
                    var ws = WorkerManager.GetWorkerList();
                    if (ws != null)
                        foreach (var w in ws)
                        {
                            if (w == null || !w.IsActive() || w.m_PrimaryTask == EWorkerTask.Fired) continue;
                            string n = WorkerName(w);
                            _cBonus[n] = w.GetBonusBoostedCount();
                            var d = w.GetWorkerData();
                            _cWorkers.Add((n, TaskName(w.m_PrimaryTask), TaskName(w.m_SecondaryTask), w.GetBonusBoostedCount(), d?.checkoutSpeed ?? 0f, d?.restockSpeed ?? 0f));
                        }
                    _cWorkers.Sort((a, b) => string.CompareOrdinal(a.main, b.main));
                }
                catch { }
            }
            if ((_dashPage == 0 || _dashPage == 1) && now - _dashSetsAt >= (_dashPage == 1 ? 5f : 10f))
            {
                _dashSetsAt = now;
                try
                {
                    if (CtReady())
                    {
                        _cAllSets = GetSets();
                        var pk = _cAllSets.Where(x => x.Total > 0 && (x.Shop ?? "").IndexOf("pok", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                        _cSetsTotal = pk.Count;
                        _cSetsDone = pk.Count(x => x.Complete || x.Have >= x.Total);
                    }
                }
                catch { }
            }
        }

        // ================= Stile =================
        private bool _stylesReady;
        private GUIStyle _sWin, _sCard, _sCardHi, _sSide, _sNav, _sNavOn, _sH1, _sH2, _sTxt, _sDim, _sBig, _sSmallBold, _sBtn, _sBtnAccent, _sBtnGhost, _sSlider, _sThumb, _sChip, _sChipOn;
        private Texture2D _tBar, _tKnob, _tWhite;
        private readonly Dictionary<string, Texture2D> _roundCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, GUIStyle> _txtCache = new Dictionary<string, GUIStyle>();

        // Abgerundete Textur (9-Slice) – für Karten, Knöpfe, Balken
        private Texture2D Round(Color c, int r = 8)
        {
            string key = $"{c.r:0.000}|{c.g:0.000}|{c.b:0.000}|{c.a:0.000}|{r}";
            if (_roundCache.TryGetValue(key, out var cached) && cached != null) return cached;
            int size = r * 2 + 2;
            var t = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                    float dy = Mathf.Max(0f, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * a));
                }
            t.Apply();
            _roundCache[key] = t;
            return t;
        }

        private GUIStyle Box(Color c, int r = 8, int pad = 12)
        {
            var s = new GUIStyle { border = new RectOffset(r + 1, r + 1, r + 1, r + 1), padding = new RectOffset(pad, pad, pad - 2, pad - 2), margin = new RectOffset(4, 4, 4, 4) };
            s.normal.background = Round(c, r);
            return s;
        }

        private GUIStyle Txt(int size, Color col, FontStyle fs = FontStyle.Normal, bool wrap = true)
        {
            string key = $"{size}|{col.r:0.00}|{col.g:0.00}|{col.b:0.00}|{(int)fs}|{wrap}";
            if (_txtCache.TryGetValue(key, out var cachedStyle)) return cachedStyle;
            var s = new GUIStyle { fontSize = size, fontStyle = fs, richText = true, wordWrap = wrap, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(2, 2, 2, 2) };
            s.normal.textColor = col;
            _txtCache[key] = s;
            return s;
        }

        private GUIStyle Btn(Color bg, Color hover, Color text, int size = 13)
        {
            var s = new GUIStyle { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, border = new RectOffset(7, 7, 7, 7), padding = new RectOffset(12, 12, 7, 7), margin = new RectOffset(3, 3, 3, 3) };
            s.normal.background = Round(bg, 6); s.normal.textColor = text;
            s.hover.background = Round(hover, 6); s.hover.textColor = text;
            s.active.background = Round(hover, 6); s.active.textColor = text;
            return s;
        }

        private void EnsureStyles()
        {
            if (_stylesReady && _sWin?.normal.background != null) return;
            _roundCache.Clear();
            _txtCache.Clear();
            _tWhite = Texture2D.whiteTexture;
            _tBar = Round(new Color(0.18f, 0.2f, 0.26f, 1f), 4);
            _tKnob = Round(Color.white, 8);

            _sWin = new GUIStyle { border = new RectOffset(13, 13, 13, 13), padding = new RectOffset(0, 0, 0, 0) };
            _sWin.normal.background = Round(CBg, 12);
            _sCard = Box(CCard);
            _sCardHi = Box(CCardHi);
            _sSide = new GUIStyle { padding = new RectOffset(10, 10, 12, 12) };
            _sNav = Btn(new Color(0, 0, 0, 0), CCardHi, CDim, 14); _sNav.alignment = TextAnchor.MiddleLeft; _sNav.fixedHeight = 36; _sNav.padding = new RectOffset(14, 8, 6, 6);
            _sNavOn = Btn(CAccent, CAccent, new Color(0.08f, 0.08f, 0.1f), 14); _sNavOn.alignment = TextAnchor.MiddleLeft; _sNavOn.fixedHeight = 36; _sNavOn.padding = new RectOffset(14, 8, 6, 6);
            _sH1 = Txt(20, Color.white, FontStyle.Bold, false);
            _sH2 = Txt(15, CAccent, FontStyle.Bold, false);
            _sTxt = Txt(13, CText);
            _sDim = Txt(12, CDim);
            _sBig = Txt(22, Color.white, FontStyle.Bold, false);
            _sSmallBold = Txt(12, CText, FontStyle.Bold, false);
            _sBtn = Btn(CCardHi, new Color(0.2f, 0.23f, 0.3f, 1f), CText);
            _sBtnAccent = Btn(CAccent, new Color(1f, 0.7f, 0.35f), new Color(0.08f, 0.08f, 0.1f));
            _sBtnGhost = Btn(new Color(0, 0, 0, 0), CCardHi, CDim);
            _sChip = Btn(CCard, CCardHi, CDim, 12); _sChip.padding = new RectOffset(10, 10, 4, 4);
            _sChipOn = Btn(CAccent, CAccent, new Color(0.08f, 0.08f, 0.1f), 12); _sChipOn.padding = new RectOffset(10, 10, 4, 4);

            _sSlider = new GUIStyle { border = new RectOffset(5, 5, 5, 5), fixedHeight = 8, margin = new RectOffset(4, 4, 10, 10) };
            _sSlider.normal.background = _tBar;
            _sThumb = new GUIStyle { fixedWidth = 18, fixedHeight = 18, border = new RectOffset(9, 9, 9, 9), margin = new RectOffset(0, 0, -5, 0) };
            _sThumb.normal.background = Round(CAccent, 8); _sThumb.hover.background = Round(new Color(1f, 0.72f, 0.4f), 8); _sThumb.active.background = _sThumb.hover.background;
            _stylesReady = true;
        }

        // ================= Zeichnen =================
        private float UiScale()
        {
            float s = _dashScale != null && _dashScale.Value > 0.3f ? _dashScale.Value : Screen.height / 1080f;
            return Mathf.Clamp(s, 0.75f, 2.5f);
        }

        private void DrawDashboard()
        {
            EnsureStyles();
            float scale = UiScale();
            float sw = Screen.width / scale, sh = Screen.height / scale;
            if (!_dashRectInit)
            {
                float w = Mathf.Min(1040f, sw - 40f), h = Mathf.Min(680f, sh - 40f);
                _dashRect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
                _dashRectInit = true;
            }
            _dashRect.width = Mathf.Min(_dashRect.width, sw - 10f);
            _dashRect.height = Mathf.Min(_dashRect.height, sh - 10f);
            _dashRect.x = Mathf.Clamp(_dashRect.x, 0f, sw - _dashRect.width);
            _dashRect.y = Mathf.Clamp(_dashRect.y, 0f, sh - _dashRect.height);

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            _dashRect = GUI.Window(0x5B08, _dashRect, DashWindow, GUIContent.none, _sWin);
            GUI.matrix = old;
        }

        private void DashWindow(int id)
        {
            try
            {
                GUILayout.BeginVertical();
                DrawHeader();
                GUILayout.BeginHorizontal();
                DrawSidebar();
                GUILayout.BeginVertical();
                if (!DashGameReady)
                {
                    GUILayout.Space(30);
                    GUILayout.Label("Warte auf geladenen Laden …", _sH1);
                    GUILayout.Label("Das Dashboard zeigt Daten, sobald dein Spielstand geladen ist und der Bot läuft.", _sDim);
                }
                else
                {
                    _dashScroll = GUILayout.BeginScrollView(_dashScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
                    GUILayout.Space(4);
                    switch (_dashPage)
                    {
                        case 0: PageOverview(); break;
                        case 1: PageCollection(); break;
                        case 2: PageShop(); break;
                        case 3: PageCustomers(); break;
                        case 4: PageCards(); break;
                        case 5: PageWorkers(); break;
                        case 6: PageHistory(); break;
                    }
                    GUILayout.Space(8);
                    GUILayout.EndScrollView();
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            catch (Exception e)
            {
                // Layout-Fehler nie bis ins Spiel durchreichen
                if (!(e is ExitGUIException)) Logger.LogWarning("Dashboard: " + e.Message);
                else throw;
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 54));
        }

        private void DrawHeader()
        {
            var r = GUILayoutUtility.GetRect(10, 54, GUILayout.ExpandWidth(true));
            RR(new Rect(r.x, r.y + 52, r.width, 2), CAccent, 0f);
            GUI.Label(new Rect(r.x + 20, r.y + 8, 160, 36), "SHOP<color=#FF9933>BOT</color>", Txt(22, Color.white, FontStyle.Bold, false));

            // Status-Pille
            bool on = _enabled.Value;
            var pill = new Rect(r.x + 160, r.y + 16, 82, 24);
            RR(pill, on ? new Color(0.08f, 0.22f, 0.14f) : new Color(0.26f, 0.08f, 0.08f), 12f);
            var ps = new GUIStyle(Txt(12, on ? new Color(0.45f, 1f, 0.62f) : new Color(1f, 0.5f, 0.5f), FontStyle.Bold, false)) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(pill, on ? "● AKTIV" : "● PAUSE", ps);

            if (DashGameReady)
            {
                string time = CSingleton<LightManager>.IsSet ? $"{LightManager.GetTimeHour():00}:{LightManager.GetTimeMinute():00}" : "";
                GUI.Label(new Rect(r.x + 262, r.y + 8, 300, 20), $"Tag {CPlayerData.m_CurrentDay + 1}  ·  {time} Uhr", _sDim);
                GUI.Label(new Rect(r.x + 262, r.y + 26, 360, 22), $"<b>{Money(CPlayerData.m_CoinAmountDouble)}</b>  <color=#8A93A6>Budget {Money(Math.Max(0, Budget))}</color>", _sTxt);
            }

            if (GUI.Button(new Rect(r.xMax - 210, r.y + 12, 140, 30), on ? "Bot pausieren" : "Bot starten", on ? _sBtn : _sBtnAccent)) _enabled.Value = !on;
            if (GUI.Button(new Rect(r.xMax - 60, r.y + 12, 40, 30), "×", _sBtn)) SetDash(false);
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(_sSide, GUILayout.Width(190));
            for (int i = 0; i < Pages.Length; i++)
                if (GUILayout.Button(Pages[i], i == _dashPage ? _sNavOn : _sNav, GUILayout.ExpandWidth(true)))
                {
                    _dashPage = i; _dashScroll = Vector2.zero; _dashSetsAt = 0f;
                }
            GUILayout.FlexibleSpace();
            GUILayout.Label("F9  Dashboard\nF8  Bot an/aus\nESC schließen", _sDim);
            GUILayout.Label("Fenster oben ziehen zum Verschieben", _sDim);
            GUILayout.EndVertical();
        }

        // ================= Bausteine =================
        private static void RR(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        private void Stat(string title, string value, string sub = null, Color? accent = null, float width = 0f)
        {
            if (width > 0f) GUILayout.BeginVertical(_sCard, GUILayout.Width(width), GUILayout.MinHeight(86));
            else GUILayout.BeginVertical(_sCard, GUILayout.MinHeight(86), GUILayout.ExpandWidth(true));
            GUILayout.Label(title.ToUpperInvariant(), Txt(11, CDim, FontStyle.Bold, false));
            var big = Txt(22, accent ?? Color.white, FontStyle.Bold, false);
            GUILayout.Label(value, big);
            if (sub != null) GUILayout.Label(sub, _sDim);
            GUILayout.EndVertical();
        }

        private void Bar(float frac, Color col, float h = 10f)
        {
            var r = GUILayoutUtility.GetRect(10, h + 4, GUILayout.ExpandWidth(true));
            r.y += 2; r.height = h;
            RR(r, new Color(0.18f, 0.2f, 0.26f, 1f), r.height / 2f);
            float w = r.width * Mathf.Clamp01(frac);
            if (w >= 2f) RR(new Rect(r.x, r.y, Mathf.Max(w, r.height), r.height), col, r.height / 2f);
        }

        private void ProgressCard(string title, string value, float frac, Color col, string sub = null)
        {
            GUILayout.BeginVertical(_sCard, GUILayout.MinHeight(86), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(title.ToUpperInvariant(), Txt(11, CDim, FontStyle.Bold, false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, Txt(14, Color.white, FontStyle.Bold, false));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            Bar(frac, col);
            if (sub != null) GUILayout.Label(sub, _sDim);
            GUILayout.EndVertical();
        }

        private void Section(string title, string hint = null)
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _sH2);
            GUILayout.FlexibleSpace();
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndHorizontal();
        }

        // Moderner An/Aus-Schalter mit Beschriftung
        private void Switch(ConfigEntry<bool> e, string label, string hint = null)
        {
            if (e == null) return;
            GUILayout.BeginHorizontal(_sCard, GUILayout.MinHeight(44));
            GUILayout.BeginVertical();
            GUILayout.Label(label, _sTxt);
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            var r = GUILayoutUtility.GetRect(48, 26, GUILayout.Width(48), GUILayout.Height(26));
            r.y += 4;
            RR(r, e.Value ? CGreen : new Color(0.28f, 0.3f, 0.36f), 13f);
            var knob = new Rect(e.Value ? r.xMax - 23 : r.x + 3, r.y + 3, 20, 20);
            RR(knob, Color.white, 10f);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) e.Value = !e.Value;
            GUILayout.EndHorizontal();
        }

        // Regler mit Titel, Wertanzeige und Rasterung
        private void Slider(ConfigEntry<float> e, string label, float min, float max, float step, Func<float, string> fmt, string hint = null)
        {
            if (e == null) return;
            GUILayout.BeginVertical(_sCard);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _sTxt);
            GUILayout.FlexibleSpace();
            GUILayout.Label(fmt(e.Value), Txt(14, CAccent, FontStyle.Bold, false));
            GUILayout.EndHorizontal();
            float v = GUILayout.HorizontalSlider(Mathf.Clamp(e.Value, min, max), min, max, _sSlider, _sThumb);
            if (step > 0f) v = Mathf.Round(v / step) * step;
            v = Mathf.Clamp(v, min, max);
            if (Mathf.Abs(v - e.Value) > 0.0001f) e.Value = v;
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndVertical();
        }

        private void SliderInt(ConfigEntry<int> e, string label, int min, int max, string unit, string hint = null)
        {
            if (e == null) return;
            GUILayout.BeginVertical(_sCard);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _sTxt);
            GUILayout.FlexibleSpace();
            GUILayout.Label(e.Value + unit, Txt(14, CAccent, FontStyle.Bold, false));
            GUILayout.EndHorizontal();
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(Mathf.Clamp(e.Value, min, max), min, max, _sSlider, _sThumb));
            if (v != e.Value) e.Value = v;
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndVertical();
        }

        private static string Pct(float v) => $"{v:0}%";
        // Spielwerte: kleiner = schneller (Sekunden pro Handgriff)
        private static string SpeedText(float v) => v <= 0f ? "?" : v < 1f ? "<color=#45D17A>sehr schnell</color>" : v < 1.75f ? "<color=#8BD17A>schnell</color>" : v < 2.5f ? "normal" : v < 3f ? "<color=#E0A070>langsam</color>" : "<color=#E07070>sehr langsam</color>";
        private static string Usd(float v) => "$" + v.ToString("N0");

        private void Two(Action left, Action right)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(_dashRect.width * 0.5f - 120f)); left(); GUILayout.EndVertical();
            GUILayout.BeginVertical(); right(); GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void HistoryRows(int max, int filter)
        {
            int shown = 0;
            for (int i = _history.Count - 1; i >= 0 && shown < max; i--)
            {
                var h = _history[i];
                if (filter > 0 && h.cat != filter) continue;
                shown++;
                GUILayout.BeginHorizontal(shown % 2 == 0 ? _sCard : _sCardHi);
                var dot = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
                dot.y += 5;
                RR(dot, CatColors[Mathf.Clamp(h.cat, 0, CatColors.Length - 1)], 5f);
                GUILayout.Label(h.time, _sDim, GUILayout.Width(86));
                GUILayout.Label(h.msg, _sTxt);
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("Noch keine Einträge.", _sDim);
        }

        // ================= Seiten =================
        private void PageOverview()
        {
            var rep = CPlayerData.m_GameReportDataCollect;
            float income = rep.totalItemEarning + rep.totalCardEarning + rep.totalPlayTableEarning;
            float costs = -(rep.supplyCost + rep.upgradeCost + rep.employeeCost + rep.rentCost + rep.billCost);

            GUILayout.Label("Übersicht", _sH1);
            GUILayout.BeginHorizontal();
            Stat("Geld", Money(CPlayerData.m_CoinAmountDouble), $"zurückgehalten {Money(Reserve)}");
            Stat("Umsatz heute", Money(income), $"{rep.itemAmountSold} Artikel · {rep.cardAmountSold} Karten", CGreen);
            Stat("Kosten heute", Money(costs), "Einkauf, Löhne, Miete, Grading", CRed);
            Stat("Kunden heute", $"{rep.customerVisited}", $"{rep.checkoutCount} an der Kasse · {rep.customerDisatisfied} unzufrieden");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            float spentToday = _daySpentDay == CPlayerData.m_CurrentDay ? _daySpent : 0f;
            float dayLim = DayLimit;
            ProgressCard("Einkauf heute (Bot)", dayLim > 0 ? $"{Money(spentToday)} / {Money(dayLim)}" : Money(spentToday),
                dayLim > 0 ? Mathf.Clamp01(spentToday / dayLim) : 0f, spentToday >= dayLim && dayLim > 0 ? CRed : CAccent,
                "Packs, Displays, Nachbestellungen – Limit unter Sammlung");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ProgressCard("Pokémon-Sets fertig", $"{_cSetsDone} / {_cSetsTotal}", _cSetsTotal > 0 ? (float)_cSetsDone / _cSetsTotal : 0f, CGreen, _dashSetsBuyable > 0 ? $"{_dashSetsBuyable} kaufbar & unfertig" : "Ziel-Sets nach der ersten Runde (1 Min.)");
            ProgressCard("Grading-Plätze", $"{_cGrading} / 8", _cGrading / 8f, CPurple, $"{_statGraded} geschickt · {_statGradedOpened} zurück");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ProgressCard("Pack-Opener", $"{_cInside:N0} / {_cCap:N0} Packs", _cCap > 0 ? (float)_cInside / _cCap : 0f, CAccent, $"{_cOpeners} Automaten · {_cRunning} öffnen · {_cDone} fertig");
            ProgressCard("Pack-Vorrat Ziel-Sets", $"{_dashStockHave:N0} / {_dashStockTarget:N0}", _dashStockTarget > 0 ? (float)_dashStockHave / _dashStockTarget : 0f, CBlue, "Lager + unterwegs");
            GUILayout.EndHorizontal();

            // Warnungen: fehlende Mods, volle Vitrinen
            if (_modWarnings.Count > 0 || _dashVitrinesFull > 0)
            {
                Section("Hinweise");
                foreach (var w in _modWarnings) GUILayout.Label("⚠ " + w, Txt(13, CRed, FontStyle.Bold, false));
                if (_dashVitrinesFull > 0) GUILayout.Label($"⚠ Vitrinen voll – {_dashVitrinesFull:N0} Karten warten in der Kartenablage. Mehr Karten-Vitrinen aufstellen.", Txt(13, CRed, FontStyle.Bold, false));
            }

            // Tagesbericht: heute + letzte Tage
            Section("Tagesbericht", "Bot-Kasse und Bot-Ausgaben");
            void DayRow(string label, DayRecord r)
            {
                GUILayout.BeginHorizontal(_sCard);
                GUILayout.Label(label, _sTxt, GUILayout.Width(110));
                GUILayout.Label($"Einnahmen {Money(r.Income)}", Txt(13, CGreen, FontStyle.Bold, false), GUILayout.Width(200));
                GUILayout.Label($"Ausgaben {Money(r.Spent)}", Txt(13, CRed, FontStyle.Bold, false), GUILayout.Width(200));
                GUILayout.Label($"{r.Customers} Kunden · ~{r.Packs:N0} Packs gekauft", _sDim);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (_today != null) DayRow($"Heute (Tag {_today.Day + 1})", _today);
            for (int i = _dayHistory.Count - 1; i >= 0 && i >= _dayHistory.Count - 3; i--) DayRow($"Tag {_dayHistory[i].Day + 1}", _dayHistory[i]);

            Section("Schnellaktionen");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Preise jetzt prüfen", _sBtn)) { _priceTimer = 999f; Note("Preise werden sofort geprüft"); }
            if (GUILayout.Button("Sammlung jetzt einkaufen", _sBtn)) { _colTimer = 59f; Note("Sammlung wird sofort geprüft"); }
            if (GUILayout.Button("Kartenablage jetzt füllen", _sBtn)) { _bulkTimer = 299f; Note("Kartenablage wird sofort gefüllt"); }
            if (GUILayout.Button("Einsatzplan neu", _sBtn)) { _lastPlanKey = null; _workerTimer = 59f; Note("Einsatzplan wird neu berechnet"); }
            GUILayout.EndHorizontal();

            Section("Letzte Aktionen", "alle im Verlauf");
            HistoryRows(8, 0);

            Section("Diese Sitzung");
            GUILayout.BeginHorizontal();
            Stat("Opener", $"{_statCollected}", $"eingesammelt · {_statStarted} gestartet");
            Stat("Kunden", $"{_statBought + _statSwapped}", $"{_statBought} Ankäufe · {_statSwapped} Tausche · {_statDeclined} abgelehnt");
            Stat("Pack-Kartons", $"{_statPackCases}", $"Ausgaben {Money(_statSpent)}");
            GUILayout.EndHorizontal();
        }

        private void PageCollection()
        {
            GUILayout.Label("Sammlung", _sH1);
            GUILayout.Label("Der Bot kauft Packs der Sets mit den meisten neuen Karten pro Dollar und lässt sie in den Openern öffnen. Fehlende Karten fast fertiger Sets kauft er von Kunden.", _sDim);

            // Ziel: alle Pokémon-Karten
            Section("Ziel: alle Karten", _goalHave >= 0 ? $"{_goalHave:N0} von {_goalTotal:N0} Karten" : "wird nach der ersten Runde berechnet (1 Min.)");
            if (_goalHave >= 0)
            {
                int missing = _goalTotal - _goalHave;
                float perDay = CardsPerDay();
                GUILayout.BeginHorizontal();
                ProgressCard("Karten gesammelt", $"{_goalHave:N0} / {_goalTotal:N0}", _goalTotal > 0 ? (float)_goalHave / _goalTotal : 0f, CGreen, $"{missing:N0} fehlen noch");
                ProgressCard("Tempo", perDay > 0f ? $"{perDay:N0} neue Karten / Tag" : "–", 0f, CAccent,
                             perDay > 0f ? $"noch ca. {Mathf.CeilToInt(missing / perDay):N0} Spieltage" : "nach dem ersten Tageswechsel");
                GUILayout.EndHorizontal();
                GUILayout.Label(_goalNoPacks.Count > 0
                    ? "Ohne kaufbare Packs (nur über Kunden/Tausch oder nach Lizenz): " + string.Join(", ", _goalNoPacks)
                    : "Alle unfertigen Sets haben kaufbare Packs.", _sDim);
                GUILayout.Label(_goalPaused.Count > 0
                    ? "Pausiert (fast fertig, Karten von Kunden kaufen): " + string.Join(", ", _goalPaused)
                    : "Keine Sets pausiert.", _sDim);
            }

            GUILayout.BeginHorizontal();
            ProgressCard("Pokémon-Sets fertig", $"{_cSetsDone} / {_cSetsTotal}", _cSetsTotal > 0 ? (float)_cSetsDone / _cSetsTotal : 0f, CGreen);
            ProgressCard("Pack-Vorrat", $"{_dashStockHave:N0} / {_dashStockTarget:N0}", _dashStockTarget > 0 ? (float)_dashStockHave / _dashStockTarget : 0f, CBlue);
            GUILayout.EndHorizontal();

            Section($"Aktuelle Ziel-Sets ({_dashFocus.Count})", "wird jede Minute neu berechnet");
            if (_dashFocus.Count == 0) GUILayout.Label("Noch keine – erscheint nach der ersten Runde.", _sDim);
            foreach (var f in _dashFocus)
            {
                int miss = f.total - f.have;
                GUILayout.BeginVertical(_sCard);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{f.name}</b>", _sTxt);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{f.have}/{f.total}   <color={(miss <= 3 ? "#45D17A" : "#FF9933")}>fehlen {miss}</color>", _sSmallBold);
                GUILayout.EndHorizontal();
                Bar(f.total > 0 ? (float)f.have / f.total : 0f, miss <= 3 ? CGreen : CAccent, 8f);
                GUILayout.EndVertical();
            }

            Section("Steuerung");
            Switch(_colEnabled, "Sammlung automatisch vervollständigen", "Packs der Ziel-Sets kaufen und öffnen lassen");
            Switch(_colPokemonOnly, "Nur Pokémon-Sets", "keine Tetramon-/Grundspiel-Sets");
            Two(() =>
            {
                Switch(_useAllMoney, "Ganzes Geld nutzen", $"hält nur Rechnungen zurück (jetzt {Money(Reserve)}) – nie ins Minus");
                Slider(_colReserve, "Sicherheitsreserve", 0f, 200000f, 5000f, Usd, "nur wenn \"Ganzes Geld nutzen\" aus ist");
                Slider(_colFills, "Vorrat (Opener-Füllungen)", 0.5f, 6f, 0.5f, v => $"{v:0.0}x", "Grundvorrat = Kapazität aller Opener × Wert");
                SliderInt(_colCasesPerBuy, "Max. Kartons pro Bestellung", 5, 100, "", "pro Set und Bestellung");
            }, () =>
            {
                Slider(_colBudgetPerExtraSet, "Budget pro zusätzlichem Ziel-Set", 0f, 200000f, 5000f, v => v <= 0 ? "aus" : Usd(v), "mehr Geld = mehr Sets gleichzeitig");
                SliderInt(_colMaxFocusSets, "Max. Ziel-Sets gleichzeitig", 1, 20, "");
                Switch(_colEfficiency, "Sets nach Effizienz wählen", "meiste neue Karten pro Dollar (aus = fast fertige zuerst)");
                SliderInt(_colPacksPerMissing, "Fast fertige Sets: max. Packs pro fehlender Karte", 0, 10000, "", "0 = kein Deckel");
                Slider(_colMaxFills, "Max. Vorrat (Füllungen)", 1f, 8f, 0.5f, v => $"{v:0.0}x");
            });

            Section("Grenzen", "damit nicht zu viel bestellt wird");
            Two(() =>
            {
                Slider(_dayBuyLimit, "Einkaufslimit pro Tag", 0f, 300000f, 5000f, v => v <= 0 ? "kein Limit" : Usd(v), $"heute ausgegeben: {Usd(_daySpentDay == CPlayerData.m_CurrentDay ? _daySpent : 0f)}");
                Slider(_dayBuyLimitPercent, "Limit wächst mit: mind. % vom Geld", 0f, 30f, 1f, v => v <= 0 ? "aus" : $"{v:0} %", $"wirksam heute: {(DayLimit > 0 ? Usd(DayLimit) : "kein Limit")}");
            }, () =>
            {
                SliderInt(_maxStockPacks, "Max. Packs im Vorrat gesamt", 0, 50000, " Packs", "0 = keine Obergrenze");
            });

            Section("Alle Pokémon-Sets", "wenigste fehlende zuerst");
            var list = _cAllSets.Where(s => s.Shop != null && s.Shop.IndexOf("pok", StringComparison.OrdinalIgnoreCase) >= 0 && s.Total > 0)
                                .OrderBy(s => s.Have >= s.Total ? 1 : 0).ThenBy(s => s.Total - s.Have).ToList();
            if (list.Count == 0) GUILayout.Label("Wird geladen …", _sDim);
            foreach (var s in list)
            {
                bool done = s.Complete || s.Have >= s.Total;
                GUILayout.BeginHorizontal(_sCard);
                GUILayout.Label(s.Name, _sTxt, GUILayout.Width(250));
                GUILayout.BeginVertical();
                GUILayout.Space(8);
                Bar((float)s.Have / s.Total, done ? CGreen : CBlue, 8f);
                GUILayout.EndVertical();
                GUILayout.Label(done ? "<color=#45D17A>fertig</color>" : $"{s.Have}/{s.Total}", _sSmallBold, GUILayout.Width(90));
                GUILayout.EndHorizontal();
            }
        }

        private void PageShop()
        {
            GUILayout.Label("Laden & Preise", _sH1);
            Two(() =>
            {
                Section("Preise");
                Switch(_priceEnabled, "Preise automatisch setzen", "Marktpreis + Aufschlag");
                Switch(_priceRound, "Preise runden", "ab $1 auf ganze Dollar – schnelles Wechselgeld");
                Slider(_priceItemPercent, "Aufschlag Produkte", 0f, 60f, 1f, Pct);
                Slider(_priceCardPercent, "Aufschlag Einzelkarten & gegradete", 0f, 60f, 1f, Pct);
            }, () =>
            {
                Section("Einkauf & Bewertungen");
                Switch(_restockEnabled, "Packs/Displays nachbestellen");
                Switch(_restockOnlyTcg, "Nur Packs und Displays", "keine Figuren, Spiele, Zubehör");
                Switch(_reviewEnabled, "Auf Kundenbewertungen reagieren", "vermisste Packs bestellen, Preis bei Beschwerden senken");
                Slider(_reviewMinMarkup, "Minimaler Aufschlag bei Beschwerden", 0f, 30f, 1f, Pct);
            });

            Section("Tagesablauf");
            Two(() =>
            {
                Switch(_billsEnabled, "Rechnungen automatisch bezahlen", "Miete, Strom, Löhne – vor allen Einkäufen");
                Switch(_dayEndEnabled, "Tag automatisch beenden", "ab 21 Uhr, wenn der Laden leer ist");
            }, () =>
            {
                Switch(_autoCheckout, "Bot kassiert an allen freien Kassen", $"keine Kassierer nötig – alle Arbeiter befüllen Opener · {_statCheckouts} Kunden");
                Slider(_autoCheckoutScansPerSecond, "Bot-Kasse: Artikel pro Sekunde", 1f, 30f, 1f, v => $"{v:0}/s");
                Switch(_cashierHelp, "Arbeiter an volle Kasse holen", "nur ohne Bot-Kasse");
                Switch(_cashierStayLate, "Kassierer bleiben bis zum letzten Kunden");
            });

            Section("Erweiterungen", $"Laden {CPlayerData.m_UnlockRoomCount}/30 · Lager {(CPlayerData.m_IsWarehouseRoomUnlocked ? CPlayerData.m_UnlockWarehouseRoomCount + "/14" : "nicht frei")}");
            Two(() =>
            {
                Switch(_expEnabled, "Laden-Erweiterungen kaufen", "mehr Fläche, sobald Level und Geld reichen");
                Switch(_expWarehouse, "Lager freischalten & erweitern", "mehr Platz für Kartons");
                Slider(_expMoneyFactor, "Nur kaufen wenn Budget mind. X × Preis", 1f, 20f, 1f, v => $"{v:0}×", "Schutz, damit genug Geld für Packs bleibt");
            }, () =>
            {
                SliderInt(_expMaxShop, "Max. Laden-Erweiterungen", 0, 30, "");
                SliderInt(_expMaxWarehouse, "Max. Lager-Erweiterungen", 0, 14, "");
                GUILayout.Label("Neue Fläche ist leer – Regale, Opener und Vitrinen stellst du selbst auf.", _sDim);
            });

            Section("Selbst einräumen & neue Produkte", $"{_statSelfStocked} Artikel · {_statSelfCards} Karten eingeräumt");
            Two(() =>
            {
                Switch(_stockSelf, "Bot räumt Regale selbst ein", "Ware aus Kartons in passende Regalfächer");
                SliderInt(_stockPerSecond, "Artikel pro Sekunde", 1, 30, "");
                Switch(_cardsSelf, "Bot füllt leere Vitrinen-Plätze", "Karten aus der Kartenablage");
                SliderInt(_cardsPerSecond, "Karten pro Sekunde", 1, 10, "");
            }, () =>
            {
                Switch(_licEnabled, "Neue Produkte freischalten (Lizenzen)", "günstigste zuerst, sobald Level & Geld reichen");
                Switch(_licPokemonOnly, "Nur Pokémon-Produkte");
                Slider(_licMoneyFactor, "Nur kaufen wenn Budget mind. X × Preis", 1f, 20f, 1f, v => $"{v:0}×");
            });

            Section("Herumliegende Kartons einsortieren", $"{_statSorted} ins Lager · {_statTrashed} leere weggeworfen");
            Two(() =>
            {
                Switch(_sortEnabled, "Volle Kartons ins Lagerregal", "Ware mit Laden-Regalfach bleibt liegen (Bot räumt daraus ein)");
                Switch(_sortKeepOpenerPacks, "Packs für Opener am Boden lassen", "bis eine Opener-Füllung, Rest ins Lager");
                Switch(_pullFromStorage, "Kartons aus dem Lagerregal holen", $"für freie Opener und halb leere Regale · {_statPulled} geholt");
            }, () =>
            {
                Switch(_sortTrashEmpty, "Leere Kartons wegwerfen", "braucht einen Mülleimer im Laden");
                SliderInt(_sortPerSecond, "Kartons pro Sekunde", 1, 20, "");
            });

            if (_itemMarkup.Count > 0)
            {
                Section("Gesenkte Aufschläge (wegen Beschwerden)");
                foreach (var kv in _itemMarkup)
                {
                    GUILayout.BeginHorizontal(_sCard);
                    GUILayout.Label(InventoryBase.GetItemData(kv.Key)?.GetName() ?? kv.Key.ToString(), _sTxt);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"+{kv.Value:0}%", _sSmallBold);
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void PageCustomers()
        {
            GUILayout.Label("Kunden-Angebote", _sH1);
            GUILayout.Label("Der Bot bearbeitet Ankauf- und Tauschangebote, wenn du ein paar Sekunden nichts drückst.", _sDim);
            GUILayout.BeginHorizontal();
            Stat("Ankäufe", $"{_statBought}");
            Stat("Tausche", $"{_statSwapped}");
            Stat("Abgelehnt", $"{_statDeclined}", null, CRed);
            GUILayout.EndHorizontal();

            Switch(_tradeEnabled, "Kunden-Angebote bearbeiten");
            Switch(_swapEnabled, "Tauschangebote bearbeiten", "nie die letzte Karte einer Sorte abgeben");
            Two(() =>
            {
                Section("Ankauf");
                Slider(_buyMaxPercent2, "Limit normale Karten", 50f, 120f, 1f, Pct, "% vom Marktpreis – Weiterverkauf +20 %");
                Switch(_buyNewAlwaysAsk, "Neue Karte: immer den Wunschpreis zahlen", "nie mehr als verlangt – nur die Geld-Grenze gilt");
                Slider(_dupStartPct, "Doppelte: Startgebot", 30f, 95f, 5f, Pct, "dann in Schritten bis zum Ankauf-Limit");
                Slider(_dupStartCheapPct, "Doppelte: Startgebot billige Karten", 30f, 95f, 5f, Pct);
                Slider(_dupCheapBelow, "Doppelte: billig heißt unter", 1f, 100f, 1f, Usd);
                Slider(_buyMaxNewPercent, "Neue Karte (Set mit Packs): bis", 100f, 500f, 10f, Pct, "Wunschpreis zahlen bis zu so viel vom Marktpreis");
                Slider(_buyNewHardPercent, "Neue Karte (Set pausiert/ohne Packs): bis", 100f, 1500f, 25f, Pct, "Kunde ist die einzige Quelle");
                Slider(_buyNewMaxMoneyPct, "Neue Karte: höchstens % vom Geld", 1f, 50f, 1f, v => $"{v:0} %");
                Slider(_buyAlwaysBelow, "Kleinkram immer kaufen unter", 0f, 20f, 0.5f, v => "$" + v.ToString("0.0"));
            }, () =>
            {
                Section("Tausch & Timing");
                Slider(_swapTolerance, "Tausch: kleiner Verlust erlaubt", 0f, 10f, 0.5f, v => "$" + v.ToString("0.0"));
                Slider(_idleSeconds, "Fenster erst nach X Sek. ohne Eingabe", 2f, 30f, 1f, v => $"{v:0} s", "Angebote/Pakete öffnen nur, wenn du nichts drückst");
                Slider(_startDelay, "Startpause nach dem Laden", 3f, 60f, 1f, v => $"{v:0} s");
            });
        }

        private void PageCards()
        {
            GUILayout.Label("Karten", _sH1);
            GUILayout.BeginHorizontal();
            ProgressCard("Grading-Plätze", $"{_cGrading} / 8", _cGrading / 8f, CPurple, $"{_statGraded} geschickt · {_statGradedOpened} zurück");
            ProgressCard("Kartenablage", _cStorageCards >= _cStorageMax && _cStorageMax > 0 ? "voll" : $"{_cStorageCards:N0} / {_cStorageMax:N0}", _cStorageMax > 0 ? Mathf.Clamp01((float)_cStorageCards / _cStorageMax) : 0f, CYellow, $"{_cStorageGraded} gegradete warten auf die Vitrinen");
            GUILayout.EndHorizontal();

            Two(() =>
            {
                Section("Graden (PSA Standard)");
                Switch(_gradeEnabled, "Karten automatisch graden", "teuerste zuerst, alle 8 Plätze");
                Switch(_gradeOnlyFull, "Nur volle Einsendungen (52 Karten)");
                Switch(_gradeKeepOne, "1 Stück jeder Karte im Album lassen");
                Switch(_gradePsa10Hunt, "PSA-10-Sammlung (Ziel: alle Karten als PSA 10)", "auch billige Doppelte, bis es eine 10 gibt");
                Slider(_gradeMinValue, "Nur Karten ab Wert", 0f, 500f, 5f, Usd, "Graden kostet ca. $20 pro Karte");
                Switch(_gradedBoxOpen, "Grading-Pakete automatisch öffnen", "Karten kommen ins Album");
            }, () =>
            {
                Section("Verkauf über Kartenablage");
                Switch(_gradedSell, "Gegradete Karten zuerst verkaufen");
                Switch(_gradedKeepTen, "Grade 10: nur Duplikate verkaufen", "eine PSA 10 bleibt immer");
                Switch(_bulkEnabled, "Doppelte normale Karten einlegen", "1 Stück bleibt im Album");
                Slider(_bulkMaxValue, "Doppelte nur unter Wert", 1f, 500f, 1f, Usd);
            });

            Section("Pack-Opener", $"{_statSelfOpener} Packs selbst eingelegt");
            Two(() =>
            {
                Switch(_openerCollect, "Fertige Opener einsammeln");
                Switch(_openerSelf, "Bot befüllt Opener selbst", "Packs unfertiger Sets, Ziel-Sets zuerst");
                SliderInt(_openerPerSecond, "Packs pro Sekunde", 5, 100, "");
            }, () =>
            {
                Switch(_openerStartPartial, "Halbvolle Opener starten");
                Slider(_openerTurbo, "Opener-Turbo", 1f, 5f, 0.5f, v => v <= 1f ? "aus" : $"{v:0.0}×", "Packs werden so viel schneller geöffnet");
            });
        }

        private void PageWorkers()
        {
            GUILayout.Label("Arbeiter", _sH1);
            Section($"Einsatzplan Tag {CPlayerData.m_CurrentDay + 1}", "Stärken + Level pro Aufgabe");
            if (_cWorkers.Count == 0) GUILayout.Label("Keine Arbeiter im Laden (kommen morgens).", _sDim);
            foreach (var p in _cWorkers)
            {
                GUILayout.BeginHorizontal(_sCard, GUILayout.MinHeight(40));
                GUILayout.Label($"<b>{p.name}</b>", _sTxt, GUILayout.Width(190));
                Color rc = p.main == "Kasse" ? CBlue : p.main == "Opener" ? CAccent : p.main == "Vitrinen" ? CPurple : p.main == "Regale" ? CGreen : CDim;
                var pr = GUILayoutUtility.GetRect(96, 24, GUILayout.Width(96), GUILayout.Height(24));
                pr.y += 6;
                RR(pr, new Color(rc.r * 0.35f, rc.g * 0.35f, rc.b * 0.35f, 1f), 12f);
                var ps = new GUIStyle(Txt(12, rc, FontStyle.Bold, false)) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(pr, p.main, ps);
                GUILayout.Space(10);
                GUILayout.Label($"Neben: {p.side}", _sDim, GUILayout.Width(130));
                GUILayout.Label($"Kasse {SpeedText(p.checkout)} · Einräumen {SpeedText(p.restock)}", _sDim, GUILayout.Width(230));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Bonus {p.bonus}/3", p.bonus >= 3 ? Txt(12, CGreen, FontStyle.Bold, false) : _sDim);
                GUILayout.EndHorizontal();
            }

            Section("Steuerung");
            Two(() =>
            {
                Switch(_workersAuto, "Arbeiter nach Stärken einteilen", "jeden Morgen neu");
                Switch(_bonusEnabled, "Täglich 3× Bonus an alle", "+1 Level je Bonus, doppelte Erfahrung");
                Switch(_upEnabled, "Upgrades mit Sammelpunkten", "Arbeitstempo zuerst für Opener & Kasse");
                Switch(_hireEnabled, "Neue Arbeiter automatisch einstellen", _statHired > 0 ? $"diese Sitzung: {_statHired} eingestellt" : "sobald Level & Geld reichen");
                SliderInt(_hireMax, "Max. Arbeiter (0 = alle)", 0, 30, "");
            }, () =>
            {
                SliderInt(_planCashiers, "Kassierer (0 = so viele wie Kassen)", 0, 8, "");
                SliderInt(_planDisplays, "Vitrinen-Befüller", 0, 6, "");
                SliderInt(_planShelves, "Regal-Einräumer", 0, 6, "", "alle übrigen befüllen die Pack-Opener");
                if (GUILayout.Button("Einsatzplan jetzt neu berechnen", _sBtnAccent)) { _lastPlanKey = null; _workerTimer = 59f; Note("Einsatzplan wird neu berechnet"); }
            });
        }

        private void PageHistory()
        {
            GUILayout.Label("Verlauf", _sH1);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < HistCats.Length; i++)
                if (GUILayout.Button(HistCats[i], i == _histFilter ? _sChipOn : _sChip)) _histFilter = i;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            HistoryRows(200, _histFilter);
        }
    }
}
