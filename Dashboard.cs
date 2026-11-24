using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ShopBot
{
    // ShopBot dashboard (F9) – sidebar, cards, animated bars and switches, smooth scrolling, language picker.
    // All texts are English and go through T()/F() (see Lang.cs).
    public partial class Plugin
    {
        // ---------- State ----------
        private bool _dashOpen, _dashClosing, _dashLockedUi;
        private int _dashPage, _histFilter;
        private Rect _dashRect;
        private bool _dashRectInit;
        private Vector2 _dashScroll;
        private float _scrollTarget, _contentH, _viewH;
        private ConfigEntry<KeyboardShortcut> _dashKey;
        private ConfigEntry<float> _dashScale;
        private ConfigEntry<string> _language;
        private float _nextConfigSave;

        // Animation
        private float _dt, _dashAnim, _pageAnim = 1f, _navY = -1f, _navTargetY, _pulse;
        private readonly Dictionary<int, float> _anim = new Dictionary<int, float>();
        private bool _langPopup;
        private Rect _langBtnRect;

        private void OnApplicationQuit() { try { Config.Save(); } catch { } }

        // Filled by the other parts
        private List<(string name, int have, int total)> _dashFocus = new List<(string, int, int)>();
        private int _dashSetsTotal, _dashSetsDone, _dashSetsBuyable, _dashStockTarget, _dashStockHave;
        private List<(string worker, string main, string side)> _dashPlan = new List<(string, string, string)>();
        private readonly List<(int day, int minute, string tpl, object[] args, int cat)> _history = new List<(int, int, string, object[], int)>();

        // Cache (not recalculated every frame)
        private float _dashCacheAt, _dashSetsAt;
        private int _cOpeners, _cCap, _cInside, _cRunning, _cDone, _cStorageCards, _cStorageMax, _cStorageGraded, _cGrading;
        private readonly List<(string name, string main, string side, int bonus, float checkout, float restock)> _cWorkers = new List<(string, string, string, int, float, float)>();
        private int _cSetsDone, _cSetsTotal;
        private List<SetInfo> _cAllSets = new List<SetInfo>();

        private static readonly string[] Pages = { "Overview", "Collection", "Shop & prices", "Customers", "Cards", "Workers", "History", "Settings" };  // i18n
        private static readonly string[] HistCats = { "All", "Purchases", "Customers", "Cards", "Workers", "Shop" };  // i18n

        // ---------- Colors ----------
        private static readonly Color CBg = new Color(0.047f, 0.055f, 0.075f, 0.985f);
        private static readonly Color CSide = new Color(0.067f, 0.078f, 0.106f, 1f);
        private static readonly Color CCard = new Color(0.098f, 0.114f, 0.153f, 1f);
        private static readonly Color CCardHi = new Color(0.125f, 0.145f, 0.196f, 1f);
        private static readonly Color CLine = new Color(0.19f, 0.215f, 0.28f, 1f);
        private static readonly Color CText = new Color(0.92f, 0.94f, 0.97f);
        private static readonly Color CDim = new Color(0.55f, 0.59f, 0.67f);
        private static readonly Color CAccent = new Color(1f, 0.6f, 0.2f);
        private static readonly Color CAccent2 = new Color(1f, 0.38f, 0.42f);
        private static readonly Color CGreen = new Color(0.27f, 0.82f, 0.48f);
        private static readonly Color CBlue = new Color(0.33f, 0.6f, 1f);
        private static readonly Color CPurple = new Color(0.7f, 0.5f, 1f);
        private static readonly Color CRed = new Color(0.95f, 0.36f, 0.36f);
        private static readonly Color CYellow = new Color(1f, 0.82f, 0.3f);
        private static readonly Color CTrack = new Color(0.17f, 0.19f, 0.25f, 1f);
        private static readonly Color[] CatColors = { new Color(0.6f, 0.62f, 0.7f), CBlue, CGreen, CPurple, CAccent, new Color(0.6f, 0.62f, 0.7f) };
        private static readonly Color[] PageColors = { CAccent, CGreen, CBlue, CYellow, CPurple, CAccent2, new Color(0.6f, 0.65f, 0.75f), new Color(0.45f, 0.8f, 0.85f) };

        // ================= Config + language =================
        private void BindDashboardConfig()
        {
            _dashKey = Config.Bind("1. General", "Dashboard hotkey", new KeyboardShortcut(KeyCode.F9), "Opens/closes the ShopBot dashboard.");
            _dashScale = Config.Bind("1. General", "Dashboard size", 0f, "0 = automatic to fit the screen height, otherwise e.g. 1.0 / 1.25 / 1.5.");
            _language = Config.Bind("1. General", "Language", "en",
                "Dashboard language: en = English, de = Deutsch. More: put a file <code>.txt into BepInEx/plugins/ShopBot/lang/. The log file always stays English.");
        }

        private void InitLanguage()
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(Info.Location); } catch { }
            Lang.Load(dir, w => Logger.LogWarning(w));
            Lang.Set(_language.Value);
            _language.SettingChanged += (s, e) => { Lang.Set(_language.Value); _txtCache.Clear(); };
        }

        // ================= Control =================
        private void HandleDashboardKey()
        {
            // Save settings in batches instead of on every slider move (otherwise stutter)
            if (Config.SaveOnConfigSet) Config.SaveOnConfigSet = false;
            if (Time.unscaledTime >= _nextConfigSave) { _nextConfigSave = Time.unscaledTime + 30f; try { Config.Save(); } catch { } }

            if (_dashKey.Value.IsDown()) SetDash(!_dashOpen || _dashClosing);
            if (_dashOpen && !_dashClosing && Input.GetKeyDown(KeyCode.Escape)) { if (_langPopup) _langPopup = false; else SetDash(false); }
            if (_dashOpen) { AnimateDash(); RefreshDashCache(); }
            // page changed or dashboard reopened: forget old animation values
            if (_anim.Count > 4000) _anim.Clear();
        }

        private void AnimateDash()
        {
            _dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _pulse += _dt;
            if (_dashClosing)
            {
                _dashAnim = Mathf.MoveTowards(_dashAnim, 0f, _dt * 8f);
                if (_dashAnim <= 0f) FinishClose();
            }
            else _dashAnim = Mathf.MoveTowards(_dashAnim, 1f, _dt * 6f);
            _pageAnim = Mathf.MoveTowards(_pageAnim, 1f, _dt * 5f);
            _navY = _navY < 0f ? _navTargetY : Mathf.Lerp(_navY, _navTargetY, 1f - Mathf.Exp(-_dt * 16f));
            // Smooth scrolling
            float max = Mathf.Max(0f, _contentH - _viewH);
            _scrollTarget = Mathf.Clamp(_scrollTarget, 0f, max);
            _dashScroll.y = Mathf.Lerp(_dashScroll.y, _scrollTarget, 1f - Mathf.Exp(-_dt * 14f));
            if (Mathf.Abs(_dashScroll.y - _scrollTarget) < 0.5f) _dashScroll.y = _scrollTarget;
        }

        private void SetDash(bool open)
        {
            if (open)
            {
                _dashClosing = false;
                if (_dashOpen) return;
                _dashOpen = true; _dashAnim = 0f; _pageAnim = 0f; _navY = -1f;
                _dashCacheAt = _dashSetsAt = 0f;
                var ipc = S<InteractionPlayerController>();
                if (ipc != null && !ipc.IsInUIMode())
                {
                    ipc.EnterUIMode();
                    ipc.m_WalkerCtrl?.SetStopMovement(true);
                    _dashLockedUi = true;
                }
            }
            else if (_dashOpen) { _dashClosing = true; _langPopup = false; }
        }

        private void FinishClose()
        {
            _dashOpen = false; _dashClosing = false;
            try { Config.Save(); } catch { }
            if (_dashLockedUi)
            {
                _dashLockedUi = false;
                var ipc = S<InteractionPlayerController>();
                if (ipc != null)
                {
                    ipc.m_WalkerCtrl?.SetStopMovement(false);
                    ipc.ExitUIMode();
                }
            }
        }

        private void GoPage(int i)
        {
            if (i == _dashPage) return;
            _dashPage = i; _dashScroll = Vector2.zero; _scrollTarget = 0f; _dashSetsAt = 0f; _pageAnim = 0f;
        }

        // History category from the English message template
        private static int Category(string tpl)
        {
            string m = (tpl ?? "").ToLowerInvariant();
            if (m.StartsWith("bought for") || m.Contains("purchase") || m.Contains("swap") || m.Contains("customer left")) return 2;
            if (m.StartsWith("bought:") || m.Contains("reordered") || m.Contains("miss") || m.Contains("collection") || m.Contains("emergency") || m.Contains("daily limit") || m.Contains("license")) return 1;
            if (m.Contains("grad") || m.Contains("card storage") || m.Contains("opener") || m.Contains("display")) return 3;
            if (m.Contains("worker") || m.Contains("work plan") || m.Contains("bonus") || m.Contains("upgrade") || m.Contains("checkout") || m.Contains("register")) return 4;
            return 5;
        }

        private void RecordHistory(string tpl, object[] args)
        {
            int day = -1, minute = 0;
            try
            {
                if (CSingleton<LightManager>.IsSet) { day = CPlayerData.m_CurrentDay + 1; minute = LightManager.GetTimeHour() * 60 + LightManager.GetTimeMinute(); }
            }
            catch { }
            _history.Add((day, minute, tpl, args, Category(tpl)));
            if (_history.Count > 300) _history.RemoveAt(0);
        }

        // Format money without creating game objects in the main menu
        private static string Money(double v) =>
            CSingleton<CGameManager>.IsSet ? GameInstance.GetPriceString((float)v) : "$" + v.ToString("N0");

        private bool DashGameReady => CSingleton<CGameManager>.IsSet && _readySince >= 0f && S<ShelfManager>() != null;

        private void RefreshDashCache()
        {
            if (!DashGameReady) return;
            RestoreDayState();
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
                    _cWorkers.Clear();
                    var ws = WorkerManager.GetWorkerList();
                    if (ws != null)
                        foreach (var w in ws)
                        {
                            if (w == null || !w.IsActive() || w.m_PrimaryTask == EWorkerTask.Fired) continue;
                            var d = w.GetWorkerData();
                            _cWorkers.Add((WorkerName(w), TaskName(w.m_PrimaryTask), TaskName(w.m_SecondaryTask), w.GetBonusBoostedCount(), d?.checkoutSpeed ?? 0f, d?.restockSpeed ?? 0f));
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

        // ================= Styles =================
        private bool _stylesReady;
        private GUIStyle _sWin, _sCard, _sCardHi, _sCardFlat, _sH1, _sH2, _sTxt, _sDim, _sSmallBold, _sSlider, _sThumb;
        private readonly Dictionary<string, Texture2D> _roundCache = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, GUIStyle> _txtCache = new Dictionary<string, GUIStyle>();
        private Texture2D _tGradient;

        // Rounded texture (9-slice) with an optional 1 px outline that is lighter at the top (subtle "glass" edge)
        private Texture2D Round(Color c, int r = 8, Color? outline = null)
        {
            string key = $"{c.r:0.000}|{c.g:0.000}|{c.b:0.000}|{c.a:0.000}|{r}|{(outline.HasValue ? outline.Value.ToString() : "-")}";
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
                    Color px = c;
                    if (outline.HasValue)
                    {
                        // distance to the edge of the rounded rect
                        float edge = Mathf.Min(Mathf.Min(x + 0.5f, size - x - 0.5f), Mathf.Min(y + 0.5f, size - y - 0.5f));
                        float rim = (dx > 0f && dy > 0f) ? r - d : edge;
                        float o = Mathf.Clamp01(1.5f - rim);
                        var oc = outline.Value;
                        float top = y > size / 2 ? 1f : 0.45f;          // texture y goes up: top edge brighter
                        px = Color.Lerp(c, new Color(oc.r, oc.g, oc.b, c.a), o * oc.a * top);
                    }
                    t.SetPixel(x, y, new Color(px.r, px.g, px.b, px.a * a));
                }
            t.Apply();
            _roundCache[key] = t;
            return t;
        }

        private GUIStyle Box(Color c, int r = 10, int pad = 12, Color? outline = null)
        {
            var s = new GUIStyle { border = new RectOffset(r + 1, r + 1, r + 1, r + 1), padding = new RectOffset(pad + 2, pad + 2, pad, pad), margin = new RectOffset(5, 5, 5, 5) };
            s.normal.background = Round(c, r, outline);
            return s;
        }

        private GUIStyle Txt(int size, Color col, FontStyle fs = FontStyle.Normal, bool wrap = true, TextAnchor align = TextAnchor.MiddleLeft)
        {
            string key = $"{size}|{col.r:0.00}|{col.g:0.00}|{col.b:0.00}|{col.a:0.00}|{(int)fs}|{wrap}|{(int)align}";
            if (_txtCache.TryGetValue(key, out var cachedStyle)) return cachedStyle;
            var s = new GUIStyle { fontSize = size, fontStyle = fs, richText = true, wordWrap = wrap, alignment = align, padding = new RectOffset(2, 2, 2, 2) };
            s.normal.textColor = col;
            _txtCache[key] = s;
            return s;
        }

        private void EnsureStyles()
        {
            if (_stylesReady && _sWin?.normal.background != null) return;
            _roundCache.Clear();
            _txtCache.Clear();
            _sWin = new GUIStyle { border = new RectOffset(15, 15, 15, 15), padding = new RectOffset(0, 0, 0, 0) };
            _sWin.normal.background = Round(CBg, 14, new Color(0.32f, 0.35f, 0.44f, 0.9f));
            var rim = new Color(0.25f, 0.28f, 0.36f, 1f);
            _sCard = Box(CCard, 10, 12, rim);
            _sCardHi = Box(CCardHi, 10, 12, rim);
            _sCardFlat = Box(CCard, 8, 10);
            _sH1 = Txt(22, Color.white, FontStyle.Bold, false);
            _sH2 = Txt(15, CText, FontStyle.Bold, false);
            _sTxt = Txt(13, CText);
            _sDim = Txt(12, CDim);
            _sSmallBold = Txt(12, CText, FontStyle.Bold, false);
            _sSlider = new GUIStyle { border = new RectOffset(5, 5, 5, 5), fixedHeight = 8, margin = new RectOffset(4, 4, 10, 10) };
            _sSlider.normal.background = Round(CTrack, 4);
            _sThumb = new GUIStyle { fixedWidth = 18, fixedHeight = 18, border = new RectOffset(9, 9, 9, 9), margin = new RectOffset(0, 0, -5, 0) };
            _sThumb.normal.background = Round(CAccent, 8); _sThumb.hover.background = Round(new Color(1f, 0.72f, 0.4f), 8); _sThumb.active.background = _sThumb.hover.background;
            // horizontal accent gradient (header line, active nav)
            _tGradient = new Texture2D(64, 1, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < 64; x++) _tGradient.SetPixel(x, 0, Color.Lerp(CAccent, CAccent2, x / 63f));
            _tGradient.Apply();
            _stylesReady = true;
        }

        // ================= Animation helpers =================
        private static float Ease(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

        // Animated value per control: moves toward target (only on Repaint, once per frame)
        private float Anim(int id, float target, float speed = 10f, float start = float.NaN)
        {
            if (!_anim.TryGetValue(id, out var v)) { v = float.IsNaN(start) ? target : start; _anim[id] = v; return v; }
            if (Event.current.type == EventType.Repaint) { v = Mathf.Lerp(v, target, 1f - Mathf.Exp(-_dt * speed)); if (Mathf.Abs(v - target) < 0.001f) v = target; _anim[id] = v; }
            return v;
        }
        private float Hover(int id, Rect r) => Anim(id, r.Contains(Event.current.mousePosition) && !_langPopup ? 1f : 0f, 14f);

        // ================= Drawing =================
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
                float w = Mathf.Min(1100f, sw - 40f), h = Mathf.Min(720f, sh - 40f);
                _dashRect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
                _dashRectInit = true;
            }
            _dashRect.width = Mathf.Min(_dashRect.width, sw - 10f);
            _dashRect.height = Mathf.Min(_dashRect.height, sh - 10f);
            _dashRect.x = Mathf.Clamp(_dashRect.x, 0f, sw - _dashRect.width);
            _dashRect.y = Mathf.Clamp(_dashRect.y, 0f, sh - _dashRect.height);

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            float e = Ease(_dashAnim);
            var oldM = GUI.matrix; var oldC = GUI.color;
            // dim the game behind the window
            GUI.color = new Color(0f, 0f, 0f, 0.35f * e);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            // grow in from 96 % around the window center
            float z = scale * (0.96f + 0.04f * e);
            var c = new Vector2((_dashRect.x + _dashRect.width / 2f) * scale, (_dashRect.y + _dashRect.height / 2f) * scale);
            GUI.matrix = Matrix4x4.TRS(new Vector3(c.x, c.y + 12f * (1f - e), 0f), Quaternion.identity, new Vector3(z, z, 1f)) *
                         Matrix4x4.TRS(new Vector3(-(_dashRect.x + _dashRect.width / 2f), -(_dashRect.y + _dashRect.height / 2f), 0f), Quaternion.identity, Vector3.one);
            GUI.color = new Color(1f, 1f, 1f, e);
            _dashRect = GUI.Window(0x5B08, _dashRect, DashWindow, GUIContent.none, _sWin);
            GUI.matrix = oldM; GUI.color = oldC;
        }

        private void DashWindow(int id)
        {
            float alpha = GUI.color.a;
            try
            {
                LanguagePopupInput();
                GUILayout.BeginVertical();
                DrawHeader();
                GUILayout.BeginHorizontal();
                DrawSidebar();
                GUILayout.BeginVertical();
                if (!DashGameReady)
                {
                    GUILayout.Space(30);
                    GUILayout.Label(T("Waiting for a loaded shop …"), _sH1);
                    GUILayout.Label(T("The dashboard shows data as soon as your save is loaded and the bot is running. Settings work already."), _sDim);
                    GUILayout.Space(10);
                    if (_dashPage == 7) PageSettings();
                    else if (BigButton(T("Open settings"), CAccent)) GoPage(7);
                }
                else
                {
                    // smooth mouse wheel
                    var ev = Event.current;
                    if (ev.type == EventType.ScrollWheel && !_langPopup) { _scrollTarget += ev.delta.y * 32f; ev.Use(); }
                    var before = _dashScroll;
                    _dashScroll = GUILayout.BeginScrollView(_dashScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
                    if (Mathf.Abs(_dashScroll.y - before.y) > 0.01f) _scrollTarget = _dashScroll.y;  // dragged the scrollbar
                    // page fade + slide in
                    float pe = Ease(_pageAnim);
                    GUI.color = new Color(1f, 1f, 1f, alpha * pe);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(14f * (1f - pe));
                    GUILayout.BeginVertical();
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
                        case 7: PageSettings(); break;
                    }
                    GUILayout.Space(10);
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    var end = GUILayoutUtility.GetRect(1, 1);
                    if (ev.type == EventType.Repaint) _contentH = end.yMax;
                    GUILayout.EndScrollView();
                    if (ev.type == EventType.Repaint) _viewH = GUILayoutUtility.GetLastRect().height;
                }
                GUILayout.EndVertical();
                GUILayout.Space(10);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                DrawLanguagePopup();
            }
            catch (Exception e)
            {
                // Never let layout errors reach the game
                if (!(e is ExitGUIException)) Logger.LogWarning("Dashboard: " + e.Message);
                else throw;
            }
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DragWindow(new Rect(0, 0, 10000, 60));
        }

        private void DrawHeader()
        {
            var r = GUILayoutUtility.GetRect(10, 62, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(new Rect(r.x + 14, r.y + 60, r.width - 28, 2), _tGradient);

            // Logo
            var logo = new Rect(r.x + 18, r.y + 15, 32, 32);
            GUI.DrawTexture(logo, _tGradient, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 9f);
            GUI.Label(logo, "S", Txt(18, new Color(0.08f, 0.08f, 0.1f), FontStyle.Bold, false, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(r.x + 58, r.y + 13, 150, 36), "SHOP<color=#FF9933>BOT</color>", Txt(22, Color.white, FontStyle.Bold, false));

            // Status pill with pulsing dot
            bool on = _enabled.Value;
            var pill = new Rect(r.x + 182, r.y + 20, 96, 24);
            RR(pill, on ? new Color(0.08f, 0.22f, 0.14f) : new Color(0.26f, 0.08f, 0.08f), 12f);
            float p = on ? 0.55f + 0.45f * Mathf.Sin(_pulse * 3.2f) : 1f;
            var dotC = on ? new Color(0.45f, 1f, 0.62f, p) : new Color(1f, 0.5f, 0.5f);
            RR(new Rect(pill.x + 11, pill.y + 8, 8, 8), dotC, 4f);
            GUI.Label(new Rect(pill.x + 22, pill.y, pill.width - 26, pill.height), on ? T("ACTIVE") : T("PAUSED"),
                Txt(11, on ? new Color(0.45f, 1f, 0.62f) : new Color(1f, 0.5f, 0.5f), FontStyle.Bold, false, TextAnchor.MiddleCenter));

            if (DashGameReady)
            {
                string time = CSingleton<LightManager>.IsSet ? $"{LightManager.GetTimeHour():00}:{LightManager.GetTimeMinute():00}" : "";
                GUI.Label(new Rect(r.x + 296, r.y + 12, 320, 20), F($"Day {CPlayerData.m_CurrentDay + 1}  ·  {time}"), _sDim);
                GUI.Label(new Rect(r.x + 296, r.y + 30, 380, 22), $"<b>{Money(CPlayerData.m_CoinAmountDouble)}</b>  <color=#8A93A6>{F($"Budget {Money(Math.Max(0, Budget))}")}</color>", _sTxt);
            }

            // Language, pause, close
            _langBtnRect = new Rect(r.xMax - 300, r.y + 16, 78, 32);
            if (FlatButton(_langBtnRect, Lang.Current.ToUpperInvariant() + "  ▼", CCardHi, CText, 0x7F000051)) _langPopup = !_langPopup;
            var pr = new Rect(r.xMax - 212, r.y + 16, 150, 32);
            if (FlatButton(pr, on ? T("Pause bot") : T("Start bot"), on ? CCardHi : CAccent, on ? CText : new Color(0.08f, 0.08f, 0.1f), 0x7F000052)) _enabled.Value = !on;
            if (FlatButton(new Rect(r.xMax - 54, r.y + 16, 36, 32), "×", CCardHi, CText, 0x7F000053)) SetDash(false);
        }

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(GUILayout.Width(200));
            GUILayout.Space(10);
            // active indicator (slides between items)
            for (int i = 0; i < Pages.Length; i++)
            {
                var r = GUILayoutUtility.GetRect(180, 40, GUILayout.ExpandWidth(true));
                r.x += 10; r.width -= 14;
                if (i == _dashPage && Event.current.type == EventType.Repaint) _navTargetY = r.y;
                float h = Hover(0x7F001000 + i, r);
                if (h > 0.01f && i != _dashPage) RR(r, new Color(1f, 1f, 1f, 0.05f * h), 9f);
                if (i == _dashPage && _navY >= 0f)
                {
                    var a = new Rect(r.x, _navY, r.width, r.height);
                    RR(a, new Color(1f, 0.6f, 0.2f, 0.16f), 9f);
                    RR(new Rect(a.x, a.y + 9, 3, a.height - 18), CAccent, 1.5f);
                }
                var ic = new Rect(r.x + 12, r.y + 13, 14, 14);
                RR(ic, i == _dashPage ? PageColors[i] : new Color(PageColors[i].r, PageColors[i].g, PageColors[i].b, 0.45f + 0.3f * h), 4f);
                GUI.Label(new Rect(r.x + 36, r.y, r.width - 40, r.height), T(Pages[i]),
                    Txt(14, i == _dashPage ? Color.white : Color.Lerp(CDim, CText, h), i == _dashPage ? FontStyle.Bold : FontStyle.Normal, false));
                if (!_langPopup && GUI.Button(r, GUIContent.none, GUIStyle.none)) GoPage(i);
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(_sCardFlat);
            GUILayout.Label($"<b>F9</b>  {T("Dashboard")}\n<b>F8</b>  {T("Bot on/off")}\n<b>Esc</b>  {T("Close")}", _sDim);
            GUILayout.EndVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);   // keep clear of the rounded window edge
            GUILayout.Label($"v{Info.Metadata.Version}  ·  {T("Drag the top bar to move")}", Txt(11, CDim));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.EndVertical();
        }

        // ---------- Language popup ----------
        private Rect LangPopupRect() => new Rect(_langBtnRect.x - 60, _langBtnRect.yMax + 6, 170, 12 + 34 * Lang.Codes.Count);

        // Runs first in the window: clicks for the open popup must not reach the controls below it
        private void LanguagePopupInput()
        {
            if (!_langPopup) return;
            var ev = Event.current;
            if (ev.type != EventType.MouseDown) return;
            var pr = LangPopupRect();
            if (pr.Contains(ev.mousePosition))
            {
                int i = Mathf.FloorToInt((ev.mousePosition.y - pr.y - 6) / 34f);
                if (i >= 0 && i < Lang.Codes.Count) _language.Value = Lang.Codes[i];
            }
            _langPopup = false;
            ev.Use();
        }

        private void DrawLanguagePopup()
        {
            if (!_langPopup) return;
            var pr = LangPopupRect();
            RR(new Rect(pr.x + 2, pr.y + 4, pr.width, pr.height), new Color(0f, 0f, 0f, 0.35f), 10f);
            GUI.Box(pr, GUIContent.none, _sCardHi);
            for (int i = 0; i < Lang.Codes.Count; i++)
            {
                var r = new Rect(pr.x + 6, pr.y + 6 + 34 * i, pr.width - 12, 32);
                string code = Lang.Codes[i];
                bool cur = code == Lang.Current;
                if (r.Contains(Event.current.mousePosition) || cur) RR(r, cur ? new Color(1f, 0.6f, 0.2f, 0.2f) : new Color(1f, 1f, 1f, 0.06f), 7f);
                GUI.Label(new Rect(r.x + 10, r.y, 40, r.height), code.ToUpperInvariant(), Txt(12, cur ? CAccent : CDim, FontStyle.Bold, false));
                GUI.Label(new Rect(r.x + 48, r.y, r.width - 50, r.height), Lang.Names.TryGetValue(code, out var n) ? n : code, Txt(13, cur ? Color.white : CText, cur ? FontStyle.Bold : FontStyle.Normal, false));
            }
        }

        // ================= Building blocks =================
        private static void RR(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

        private static Color Mul(Color c, float f) => new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);

        // Button at a fixed position with animated hover
        private bool FlatButton(Rect r, string text, Color bg, Color fg, int id)
        {
            float h = Hover(id, r);
            RR(r, Color.Lerp(bg, Mul(bg, bg.r > 0.5f ? 1.12f : 1.45f), h), 8f);
            GUI.Label(r, text, Txt(13, fg, FontStyle.Bold, false, TextAnchor.MiddleCenter));
            return !_langPopup && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // Layout button with animated hover
        private bool LButton(string text, Color bg, Color fg, params GUILayoutOption[] opts)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var st = Txt(13, fg, FontStyle.Bold, false, TextAnchor.MiddleCenter);
            var size = st.CalcSize(new GUIContent(text));
            var list = new List<GUILayoutOption> { GUILayout.Height(34), GUILayout.MinWidth(size.x + 28) };
            list.AddRange(opts);
            var r = GUILayoutUtility.GetRect(new GUIContent(text), st, list.ToArray());
            r.x += 3; r.width -= 6; r.y += 3; r.height -= 6;
            float h = Hover(id, r);
            RR(r, Color.Lerp(bg, Mul(bg, bg.r > 0.5f ? 1.12f : 1.45f), h), 8f);
            GUI.Label(r, text, st);
            return !_langPopup && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
        private bool Btn(string text) => LButton(text, CCardHi, CText);
        private bool BigButton(string text, Color bg) => LButton(text, bg, new Color(0.08f, 0.08f, 0.1f), GUILayout.Height(40));

        private void Stat(string title, string value, string sub = null, Color? accent = null)
        {
            GUILayout.BeginVertical(_sCard, GUILayout.MinHeight(92), GUILayout.ExpandWidth(true));
            GUILayout.Label(title.ToUpperInvariant(), Txt(11, CDim, FontStyle.Bold, false));
            GUILayout.Label(value, Txt(22, accent ?? Color.white, FontStyle.Bold, false));
            if (sub != null) GUILayout.Label(sub, _sDim);
            GUILayout.EndVertical();
        }

        // Progress bar that glides to its value
        private void Bar(float frac, Color col, float h = 10f)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var r = GUILayoutUtility.GetRect(10, h + 4, GUILayout.ExpandWidth(true));
            r.y += 2; r.height = h;
            float f = Anim(id, Mathf.Clamp01(frac), 6f, 0f);
            RR(r, CTrack, r.height / 2f);
            float w = r.width * f;
            if (w >= 2f)
            {
                var fr = new Rect(r.x, r.y, Mathf.Max(w, r.height), r.height);
                RR(fr, col, r.height / 2f);
                RR(new Rect(fr.x + 2, fr.y + 1, fr.width - 4, fr.height * 0.4f), new Color(1f, 1f, 1f, 0.12f), r.height / 4f);   // gloss
            }
        }

        private void ProgressCard(string title, string value, float frac, Color col, string sub = null)
        {
            GUILayout.BeginVertical(_sCard, GUILayout.MinHeight(92), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(title.ToUpperInvariant(), Txt(11, CDim, FontStyle.Bold, false));
            GUILayout.FlexibleSpace();
            GUILayout.Label(value, Txt(14, Color.white, FontStyle.Bold, false));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            Bar(frac, col);
            if (sub != null) GUILayout.Label(sub, _sDim);
            GUILayout.EndVertical();
        }

        private void PageTitle(string title, string sub = null)
        {
            GUILayout.Label(title, _sH1);
            if (sub != null) GUILayout.Label(sub, _sDim);
            GUILayout.Space(4);
        }

        private void Section(string title, string hint = null)
        {
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            var bar = GUILayoutUtility.GetRect(4, 18, GUILayout.Width(4), GUILayout.Height(18));
            bar.y += 3;
            RR(bar, CAccent, 2f);
            GUILayout.Space(6);
            GUILayout.Label(title, _sH2);
            GUILayout.FlexibleSpace();
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // Toggle switch with a gliding knob
        private void Switch(ConfigEntry<bool> e, string label, string hint = null)
        {
            if (e == null) return;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            GUILayout.BeginHorizontal(_sCard, GUILayout.MinHeight(46));
            GUILayout.BeginVertical();
            GUILayout.Label(label, _sTxt);
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            var r = GUILayoutUtility.GetRect(50, 26, GUILayout.Width(50), GUILayout.Height(26));
            r.y += 4;
            float k = Anim(id, e.Value ? 1f : 0f, 16f);
            float h = Hover(id ^ 0x40000000, r);
            RR(r, Color.Lerp(new Color(0.27f, 0.29f, 0.35f), CGreen, k), 13f);
            if (h > 0.01f) RR(r, new Color(1f, 1f, 1f, 0.07f * h), 13f);
            RR(new Rect(Mathf.Lerp(r.x + 3, r.xMax - 23, k), r.y + 3, 20, 20), Color.white, 10f);
            if (!_langPopup && GUI.Button(r, GUIContent.none, GUIStyle.none)) e.Value = !e.Value;
            GUILayout.EndHorizontal();
        }

        // Slider with title, value and step
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
            if (!_langPopup && Mathf.Abs(v - e.Value) > 0.0001f) e.Value = v;
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
            if (!_langPopup && v != e.Value) e.Value = v;
            if (hint != null) GUILayout.Label(hint, _sDim);
            GUILayout.EndVertical();
        }

        // Row of chips (like tabs); returns the clicked index or -1
        private int Chips(IList<string> labels, int current)
        {
            int clicked = -1;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Count; i++)
            {
                bool on = i == current;
                if (LButton(labels[i], on ? CAccent : CCard, on ? new Color(0.08f, 0.08f, 0.1f) : CDim)) clicked = i;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return clicked;
        }

        // Pack stock is known after the first collection round (about 1 min after loading)
        private string StockValue() => _dashStockTarget > 0 ? $"{_dashStockHave:N0} / {_dashStockTarget:N0}" : "…";
        private string StockSub() => _dashStockTarget > 0 ? PackSupplyText() : T("calculated after the first round (1 min)");

        private static string Pct(float v) => $"{v:0}%";
        // Game values: smaller = faster (seconds per action)
        private static string SpeedText(float v) => v <= 0f ? "?" : v < 1f ? $"<color=#45D17A>{T("very fast")}</color>" : v < 1.75f ? $"<color=#8BD17A>{T("fast")}</color>" : v < 2.5f ? T("normal") : v < 3f ? $"<color=#E0A070>{T("slow")}</color>" : $"<color=#E07070>{T("very slow")}</color>";
        private static string Usd(float v) => "$" + v.ToString("N0");
        private static string Off(float v, Func<float, string> f) => v <= 0 ? T("off") : f(v);

        private void Two(Action left, Action right)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(_dashRect.width * 0.5f - 125f)); left(); GUILayout.EndVertical();
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
                GUILayout.BeginHorizontal(shown % 2 == 0 ? _sCardFlat : _sCard);
                var dot = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
                dot.y += 5;
                RR(dot, CatColors[Mathf.Clamp(h.cat, 0, CatColors.Length - 1)], 5f);
                GUILayout.Label(h.day < 0 ? "" : F($"D{h.day} {h.minute / 60:00}:{h.minute % 60:00}"), _sDim, GUILayout.Width(92));
                GUILayout.Label(MsgText(h.tpl, h.args), _sTxt);
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label(T("No entries yet."), _sDim);
        }

        // ================= Pages =================
        private void PageOverview()
        {
            var rep = CPlayerData.m_GameReportDataCollect;
            float income = rep.totalItemEarning + rep.totalCardEarning + rep.totalPlayTableEarning;
            float costs = -(rep.supplyCost + rep.upgradeCost + rep.employeeCost + rep.rentCost + rep.billCost);

            PageTitle(T("Overview"));
            GUILayout.BeginHorizontal();
            Stat(T("Money"), Money(CPlayerData.m_CoinAmountDouble), F($"held back {Money(Reserve)}"));
            Stat(T("Revenue today"), Money(income), F($"{rep.itemAmountSold} items · {rep.cardAmountSold} cards"), CGreen);
            Stat(T("Costs today"), Money(costs), T("Purchases, wages, rent, grading"), CRed);
            Stat(T("Customers today"), $"{rep.customerVisited}", F($"{rep.checkoutCount} at the register · {rep.customerDisatisfied} unhappy"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            float spentToday = _daySpentDay == CPlayerData.m_CurrentDay ? _daySpent : 0f;
            float dayLim = DayLimit;
            ProgressCard(T("Purchases today (bot)"), dayLim > 0 ? $"{Money(spentToday)} / {Money(dayLim)}" : Money(spentToday),
                dayLim > 0 ? Mathf.Clamp01(spentToday / dayLim) : 0f, spentToday >= dayLim && dayLim > 0 ? CRed : CAccent,
                T("Packs for the collection – shelf restocking is not limited"));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ProgressCard(T("Pokémon sets done"), $"{_cSetsDone} / {_cSetsTotal}", _cSetsTotal > 0 ? (float)_cSetsDone / _cSetsTotal : 0f, CGreen, _dashSetsBuyable > 0 ? F($"{_dashSetsBuyable} buyable & unfinished") : T("Target sets after the first round (1 min)"));
            ProgressCard(T("Grading slots"), $"{_cGrading} / 8", _cGrading / 8f, CPurple, F($"{_statGraded} sent · {_statGradedOpened} back"));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ProgressCard(T("Pack openers"), F($"{_cInside:N0} / {_cCap:N0} packs"), _cCap > 0 ? (float)_cInside / _cCap : 0f, CAccent, F($"{_cOpeners} machines · {_cRunning} opening · {_cDone} done"));
            ProgressCard(T("Pack stock target sets"), StockValue(), _dashStockTarget > 0 ? (float)_dashStockHave / _dashStockTarget : 0f, CBlue, StockSub());
            GUILayout.EndHorizontal();

            // Warnings: missing mods, full display cases
            if (_modWarnings.Count > 0 || _dashVitrinesFull > 0)
            {
                Section(T("Notes"));
                foreach (var w in _modWarnings) GUILayout.Label("⚠ " + ModWarningText(w), Txt(13, CRed, FontStyle.Bold, false));
                if (_dashVitrinesFull > 0) GUILayout.Label("⚠ " + F($"Display cases full – {_dashVitrinesFull:N0} cards are waiting in the card storage. Place more card display cases."), Txt(13, CRed, FontStyle.Bold, false));
            }

            // Daily report: today + last days
            Section(T("Daily report"), T("Bot checkout and bot spending"));
            void DayRow(string label, DayRecord r)
            {
                GUILayout.BeginHorizontal(_sCard);
                GUILayout.Label(label, _sTxt, GUILayout.Width(120));
                GUILayout.Label(F($"Income {Money(r.Income)}"), Txt(13, CGreen, FontStyle.Bold, false), GUILayout.Width(200));
                GUILayout.Label(F($"Spending {Money(r.Spent)}"), Txt(13, CRed, FontStyle.Bold, false), GUILayout.Width(200));
                GUILayout.Label(F($"{r.Customers} customers · ~{r.Packs:N0} packs bought"), _sDim);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (_today != null) DayRow(F($"Today (day {_today.Day + 1})"), _today);
            for (int i = _dayHistory.Count - 1; i >= 0 && i >= _dayHistory.Count - 3; i--) DayRow(F($"Day {_dayHistory[i].Day + 1}"), _dayHistory[i]);

            Section(T("Quick actions"));
            GUILayout.BeginHorizontal();
            if (Btn(T("Check prices now"))) { _priceTimer = 999f; Note("Prices are checked right away"); }
            if (Btn(T("Buy collection now"))) { _colTimer = 59f; Note("Collection is checked right away"); }
            if (Btn(T("Fill card storage now"))) { _bulkTimer = 299f; Note("Card storage is filled right away"); }
            if (Btn(T("New work plan"))) { _lastPlanKey = null; _workerTimer = 59f; Note("Work plan is recalculated"); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Section(T("Latest actions"), T("all in History"));
            HistoryRows(8, 0);

            Section(T("This session"));
            GUILayout.BeginHorizontal();
            Stat(T("Openers"), $"{_statCollected}", F($"collected · {_statStarted} started"));
            Stat(T("Customers"), $"{_statBought + _statSwapped}", F($"{_statBought} purchases · {_statSwapped} swaps · {_statDeclined} declined"));
            Stat(T("Pack boxes"), $"{_statPackCases}", F($"Spending {Money(_statSpent)}"));
            GUILayout.EndHorizontal();
        }

        private void PageCollection()
        {
            PageTitle(T("Collection"), T("The bot buys packs of the sets with the most new cards per dollar and lets the openers open them. Missing cards of nearly finished sets are bought from customers."));

            // Goal: all Pokémon cards
            Section(T("Goal: all cards"), _goalHave >= 0 ? F($"{_goalHave:N0} of {_goalTotal:N0} cards") : T("calculated after the first round (1 min)"));
            if (_goalHave >= 0)
            {
                int missing = _goalTotal - _goalHave;
                float perDay = CardsPerDay();
                GUILayout.BeginHorizontal();
                ProgressCard(T("Cards collected"), $"{_goalHave:N0} / {_goalTotal:N0}", _goalTotal > 0 ? (float)_goalHave / _goalTotal : 0f, CGreen, F($"{missing:N0} still missing"));
                ProgressCard(T("Pace"), perDay > 0f ? F($"{perDay:N0} new cards / day") : "–", 0f, CAccent,
                             perDay > 0f ? F($"about {Mathf.CeilToInt(missing / perDay):N0} game days left") : T("after the first day change"));
                GUILayout.EndHorizontal();
                GUILayout.Label(_goalNoPacks.Count > 0
                    ? T("Without buyable packs (only via customers/swaps or after a license): ") + string.Join(", ", _goalNoPacks)
                    : T("All unfinished sets have buyable packs."), _sDim);
                GUILayout.Label(_goalPaused.Count > 0
                    ? T("Paused (nearly finished, buy cards from customers): ") + string.Join(", ", _goalPaused)
                    : T("No sets paused."), _sDim);
            }

            GUILayout.BeginHorizontal();
            ProgressCard(T("Pokémon sets done"), $"{_cSetsDone} / {_cSetsTotal}", _cSetsTotal > 0 ? (float)_cSetsDone / _cSetsTotal : 0f, CGreen);
            ProgressCard(T("Pack stock"), StockValue(), _dashStockTarget > 0 ? (float)_dashStockHave / _dashStockTarget : 0f, CBlue, StockSub());
            GUILayout.EndHorizontal();

            Section(F($"Current target sets ({_dashFocus.Count})"), T("recalculated every minute"));
            if (_dashFocus.Count == 0) GUILayout.Label(T("None yet – they appear after the first round."), _sDim);
            foreach (var f in _dashFocus)
            {
                int miss = f.total - f.have;
                GUILayout.BeginVertical(_sCard);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{f.name}</b>", _sTxt);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{f.have}/{f.total}   <color={(miss <= 3 ? "#45D17A" : "#FF9933")}>{F($"{miss} missing")}</color>", _sSmallBold);
                GUILayout.EndHorizontal();
                Bar(f.total > 0 ? (float)f.have / f.total : 0f, miss <= 3 ? CGreen : CAccent, 8f);
                GUILayout.EndVertical();
            }

            Section(T("Control"));
            Switch(_colEnabled, T("Complete the collection automatically"), T("Buy packs of the target sets and let them be opened"));
            Switch(_colPokemonOnly, T("Pokémon sets only"), T("no Tetramon/base game sets"));
            Two(() =>
            {
                Switch(_useAllMoney, T("Use all money"), F($"only holds back bills (now {Money(Reserve)}) – never negative"));
                Slider(_colReserve, T("Safety reserve"), 0f, 200000f, 5000f, Usd, T("only if \"Use all money\" is off"));
                Slider(_colFills, T("Stock (opener fills)"), 0.5f, 6f, 0.5f, v => $"{v:0.0}x", T("Base stock = capacity of all openers × value"));
                SliderInt(_colCasesPerBuy, T("Max boxes per order"), 5, 100, "", T("per set and order"));
                Switch(_packsFirst, T("Packs first"), T("when stock is low: no expansions, only licenses for sets without packs"));
                Switch(_emergencyEnabled, T("Emergency restock"), T("orders right away when the openers run out of packs"));
            }, () =>
            {
                Slider(_colBudgetPerExtraSet, T("Budget per extra target set"), 0f, 200000f, 5000f, v => Off(v, Usd), T("more money = more sets at once"));
                SliderInt(_colMaxFocusSets, T("Max target sets at once"), 1, 20, "");
                Switch(_colEfficiency, T("Choose sets by efficiency"), T("most new cards per dollar (off = nearly finished first)"));
                SliderInt(_colPacksPerMissing, T("Nearly finished sets: max packs per missing card"), 0, 10000, "", T("0 = no cap"));
                Slider(_colMaxFills, T("Max stock (fills)"), 1f, 8f, 0.5f, v => $"{v:0.0}x");
                Slider(_emergencyExtraLimit, T("Emergency extra limit per day"), 0f, 500000f, 10000f, v => Off(v, Usd));
            });

            Section(T("Limits"), T("so not too much is ordered"));
            Two(() =>
            {
                Slider(_dayBuyLimit, T("Purchase limit per day"), 0f, 300000f, 5000f, v => v <= 0 ? T("no limit") : Usd(v), F($"spent today: {Usd(_daySpentDay == CPlayerData.m_CurrentDay ? _daySpent : 0f)}"));
                Slider(_dayBuyLimitPercent, T("Limit grows with: at least % of money"), 0f, 30f, 1f, v => Off(v, x => $"{x:0} %"), F($"effective today: {(DayLimit > 0 ? Usd(DayLimit) : T("no limit"))}"));
            }, () =>
            {
                SliderInt(_maxStockPacks, T("Max packs in stock overall"), 0, 50000, " " + T("packs"), T("0 = no upper limit"));
            });

            Section(T("All Pokémon sets"), T("fewest missing first"));
            var list = _cAllSets.Where(s => s.Shop != null && s.Shop.IndexOf("pok", StringComparison.OrdinalIgnoreCase) >= 0 && s.Total > 0)
                                .OrderBy(s => s.Have >= s.Total ? 1 : 0).ThenBy(s => s.Total - s.Have).ToList();
            if (list.Count == 0) GUILayout.Label(T("Loading …"), _sDim);
            foreach (var s in list)
            {
                bool done = s.Complete || s.Have >= s.Total;
                GUILayout.BeginHorizontal(_sCardFlat);
                GUILayout.Label(s.Name, _sTxt, GUILayout.Width(260));
                GUILayout.BeginVertical();
                GUILayout.Space(8);
                Bar((float)s.Have / s.Total, done ? CGreen : CBlue, 8f);
                GUILayout.EndVertical();
                GUILayout.Label(done ? $"<color=#45D17A>{T("done")}</color>" : $"{s.Have}/{s.Total}", _sSmallBold, GUILayout.Width(90));
                GUILayout.EndHorizontal();
            }
        }

        private void PageShop()
        {
            PageTitle(T("Shop & prices"));
            Two(() =>
            {
                Section(T("Prices"));
                Switch(_priceEnabled, T("Set prices automatically"), T("Market price + markup"));
                Switch(_priceRound, T("Round prices"), T("from $1 to whole dollars – quick change"));
                Slider(_priceItemPercent, T("Markup products"), 0f, 60f, 1f, Pct);
                Slider(_priceCardPercent, T("Markup single cards & graded"), 0f, 60f, 1f, Pct);
            }, () =>
            {
                Section(T("Purchasing & reviews"));
                Switch(_restockEnabled, T("Reorder packs/displays"));
                Switch(_restockOnlyTcg, T("Packs and displays only"), T("no figures, games, accessories"));
                Switch(_reviewEnabled, T("React to customer reviews"), T("order missed packs, lower the price on complaints"));
                Slider(_reviewMinMarkup, T("Minimum markup on complaints"), 0f, 30f, 1f, Pct);
            });

            Section(T("Daily routine"));
            Two(() =>
            {
                Switch(_billsEnabled, T("Pay bills automatically"), T("rent, electricity, wages – before any purchases"));
                Switch(_dayEndEnabled, T("End day automatically"), T("from 9 PM, when the shop is empty"));
            }, () =>
            {
                Switch(_autoCheckout, T("Bot serves all free registers"), F($"no cashiers needed – all workers fill openers · {_statCheckouts} customers"));
                Slider(_autoCheckoutScansPerSecond, T("Bot checkout: items per second"), 1f, 30f, 1f, v => $"{v:0}/s");
                Switch(_cashierHelp, T("Call a worker to a full register"), T("only without bot checkout"));
                Switch(_cashierStayLate, T("Cashiers stay until the last customer"));
            });

            Section(T("Expansions"), F($"Shop {CPlayerData.m_UnlockRoomCount}/30 · Warehouse {(CPlayerData.m_IsWarehouseRoomUnlocked ? CPlayerData.m_UnlockWarehouseRoomCount + "/14" : T("locked"))}"));
            Two(() =>
            {
                Switch(_expEnabled, T("Buy shop expansions"), T("more space as soon as level and money allow"));
                Switch(_expWarehouse, T("Unlock & expand the warehouse"), T("more room for boxes"));
                Slider(_expMoneyFactor, T("Only buy if budget is at least X × price"), 1f, 20f, 1f, v => $"{v:0}×", T("safety so enough money stays for packs"));
            }, () =>
            {
                SliderInt(_expMaxShop, T("Max shop expansions"), 0, 30, "");
                SliderInt(_expMaxWarehouse, T("Max warehouse expansions"), 0, 14, "");
                GUILayout.Label(T("New space is empty – you place shelves, openers and display cases yourself."), _sDim);
            });

            Section(T("Self-stocking & new products"), F($"{_statSelfStocked} items · {_statSelfCards} cards stocked"));
            if (_dashShelfStatus.Length > 0) GUILayout.Label(_dashShelfStatus, _sDim);
            Two(() =>
            {
                Switch(_stockSelf, T("Bot stocks shelves itself"), T("goods from boxes into matching shelf slots"));
                Switch(_stockEmptySlots, T("Fill empty shelves without a label"), T("booster boxes first, then packs"));
                Switch(_relabelEmpty, T("Relabel empty slots whose goods are gone"), T("gets goods that are here – boxes first, then packs of the new sets"));
                SliderInt(_stockPerSecond, T("Items per second"), 1, 30, "");
                Switch(_cardsSelf, T("Bot fills empty display slots"), T("cards from the card storage"));
                SliderInt(_cardsPerSecond, T("Cards per second"), 1, 10, "");
            }, () =>
            {
                Switch(_licEnabled, T("Unlock new products (licenses)"), T("packs of missing sets first, as soon as level & money allow"));
                Switch(_licPokemonOnly, T("Pokémon products only"));
                Slider(_licMoneyFactor, T("Only buy if budget is at least X × price"), 1f, 20f, 1f, v => $"{v:0}×");
            });

            Section(T("Sort loose boxes"), F($"{_statSorted} into storage · {_statTrashed} empty thrown away"));
            Two(() =>
            {
                Switch(_sortEnabled, T("Full boxes onto the storage shelf"), T("goods with a shop shelf slot stay on the floor (the bot stocks from them)"));
                Switch(_sortKeepOpenerPacks, T("Leave packs for openers on the floor"), T("up to one opener fill, the rest into storage"));
                Switch(_pullFromStorage, T("Take boxes from the storage shelf"), F($"for free openers and half-empty shelves · {_statPulled} taken"));
            }, () =>
            {
                Switch(_sortTrashEmpty, T("Throw away empty boxes"), T("needs a trash bin in the shop"));
                SliderInt(_sortPerSecond, T("Boxes per second"), 1, 20, "");
            });

            if (_itemMarkup.Count > 0)
            {
                Section(T("Lowered markups (because of complaints)"));
                foreach (var kv in _itemMarkup)
                {
                    GUILayout.BeginHorizontal(_sCardFlat);
                    GUILayout.Label(InventoryBase.GetItemData(kv.Key)?.GetName() ?? kv.Key.ToString(), _sTxt);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"+{kv.Value:0}%", _sSmallBold);
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void PageCustomers()
        {
            PageTitle(T("Customer offers"), T("The bot handles purchase and swap offers when you have not pressed anything for a few seconds."));
            GUILayout.BeginHorizontal();
            Stat(T("Purchases"), $"{_statBought}");
            Stat(T("Swaps"), $"{_statSwapped}");
            Stat(T("Declined"), $"{_statDeclined}", null, CRed);
            GUILayout.EndHorizontal();

            Switch(_tradeEnabled, T("Handle customer offers"));
            Switch(_swapEnabled, T("Handle swap offers"), T("never give away the last copy of a card"));
            Two(() =>
            {
                Section(T("Buying"));
                Slider(_buyMaxPercent2, T("Limit normal cards"), 50f, 120f, 1f, Pct, T("% of market price – reselling +20 %"));
                Switch(_buyNewAlwaysAsk, T("New card: always pay the asking price"), T("never more than asked – only the money limit applies"));
                Slider(_dupStartPct, T("Duplicates: start offer"), 30f, 95f, 5f, Pct, T("then in steps up to the buy limit"));
                Slider(_dupStartCheapPct, T("Duplicates: start offer cheap cards"), 30f, 95f, 5f, Pct);
                Slider(_dupCheapBelow, T("Duplicates: cheap means below"), 1f, 100f, 1f, Usd);
                Slider(_buyMaxNewPercent, T("New card (set with packs): up to"), 100f, 500f, 10f, Pct, T("pay the asking price up to this much of the market price"));
                Slider(_buyNewHardPercent, T("New card (set paused/without packs): up to"), 100f, 1500f, 25f, Pct, T("the customer is the only source"));
                Slider(_buyNewMaxMoneyPct, T("New card: at most % of money"), 1f, 50f, 1f, v => $"{v:0} %");
                Slider(_buyAlwaysBelow, T("Always buy small stuff below"), 0f, 20f, 0.5f, v => "$" + v.ToString("0.0"));
            }, () =>
            {
                Section(T("Swaps"));
                Slider(_swapTolerance, T("Swap: small loss allowed"), 0f, 10f, 0.5f, v => "$" + v.ToString("0.0"));
                GUILayout.Label(T("Timing (idle seconds, start delay) is on the Settings page."), _sDim);
            });
        }

        private void PageCards()
        {
            PageTitle(T("Cards"));
            GUILayout.BeginHorizontal();
            ProgressCard(T("Grading slots"), $"{_cGrading} / 8", _cGrading / 8f, CPurple, F($"{_statGraded} sent · {_statGradedOpened} back"));
            ProgressCard(T("Card storage"), _cStorageCards >= _cStorageMax && _cStorageMax > 0 ? T("full") : $"{_cStorageCards:N0} / {_cStorageMax:N0}", _cStorageMax > 0 ? Mathf.Clamp01((float)_cStorageCards / _cStorageMax) : 0f, CYellow, F($"{_cStorageGraded} graded cards waiting for the display cases"));
            GUILayout.EndHorizontal();

            Two(() =>
            {
                Section(T("Grading (PSA Standard)"));
                Switch(_gradeEnabled, T("Grade cards automatically"), T("most expensive first, all 8 slots"));
                Switch(_gradeOnlyFull, T("Only full submissions (52 cards)"));
                Switch(_gradeKeepOne, T("Keep 1 copy of every card in the album"));
                Switch(_gradePsa10Hunt, T("PSA 10 collection (goal: all cards as PSA 10)"), T("cheap duplicates too, until there is a 10"));
                Slider(_gradeMinValue, T("Only cards from value"), 0f, 500f, 5f, Usd, T("grading costs about $20 per card"));
                Switch(_gradedBoxOpen, T("Open grading packages automatically"), T("the cards go into the album"));
            }, () =>
            {
                Section(T("Selling via card storage"));
                Switch(_gradedSell, T("Sell graded cards first"));
                Switch(_gradedKeepTen, T("Grade 10: only sell duplicates"), T("one PSA 10 always stays"));
                Switch(_bulkEnabled, T("Put in normal duplicates"), T("1 copy stays in the album"));
                Slider(_bulkMaxValue, T("Duplicates only below value"), 1f, 500f, 1f, Usd);
            });

            Section(T("Pack openers"), F($"{_statSelfOpener} packs put in by the bot"));
            Two(() =>
            {
                Switch(_openerCollect, T("Collect finished openers"));
                Switch(_openerSelf, T("Bot fills openers itself"), T("packs of unfinished sets, target sets first"));
                Switch(_openerSpread, T("Spread evenly when stock is low"), T("every free opener gets a portion and starts right away"));
                SliderInt(_openerPerSecond, T("Packs per second"), 5, 100, "");
            }, () =>
            {
                Switch(_openerStartPartial, T("Start half-full openers"));
                SliderInt(_openerMinBatch, T("Minimum portion per opener"), 16, 400, " " + T("packs"));
                Slider(_openerTurbo, T("Opener turbo"), 1f, 5f, 0.5f, v => v <= 1f ? T("off") : $"{v:0.0}×", T("packs are opened this much faster"));
            });
        }

        private void PageWorkers()
        {
            PageTitle(T("Workers"));
            Section(F($"Work plan day {CPlayerData.m_CurrentDay + 1}"), T("strengths + level per task"));
            if (_cWorkers.Count == 0) GUILayout.Label(T("No workers in the shop (they come in the morning)."), _sDim);
            foreach (var p in _cWorkers)
            {
                GUILayout.BeginHorizontal(_sCard, GUILayout.MinHeight(42));
                GUILayout.Label($"<b>{p.name}</b>", _sTxt, GUILayout.Width(190));
                Color rc = p.main == "Cashier" ? CBlue : p.main == "Opener" ? CAccent : p.main == "Displays" ? CPurple : p.main == "Shelves" ? CGreen : CDim;
                var pr = GUILayoutUtility.GetRect(104, 24, GUILayout.Width(104), GUILayout.Height(24));
                pr.y += 6;
                RR(pr, new Color(rc.r * 0.3f, rc.g * 0.3f, rc.b * 0.3f, 1f), 12f);
                GUI.Label(pr, T(p.main), Txt(12, rc, FontStyle.Bold, false, TextAnchor.MiddleCenter));
                GUILayout.Space(10);
                GUILayout.Label(F($"Side: {L(p.side)}"), _sDim, GUILayout.Width(140));
                GUILayout.Label(F($"Checkout {SpeedText(p.checkout)} · stocking {SpeedText(p.restock)}"), _sDim, GUILayout.Width(250));
                GUILayout.FlexibleSpace();
                GUILayout.Label(F($"Bonus {p.bonus}/3"), p.bonus >= 3 ? Txt(12, CGreen, FontStyle.Bold, false) : _sDim);
                GUILayout.EndHorizontal();
            }

            Section(T("Control"));
            Two(() =>
            {
                Switch(_workersAuto, T("Assign workers by strengths"), T("new every morning"));
                Switch(_bonusEnabled, T("3× bonus for everyone daily"), T("+1 level per bonus, double experience"));
                Switch(_upEnabled, T("Upgrades with collection points"), T("working speed first for openers & checkout"));
                Switch(_hireEnabled, T("Hire new workers automatically"), _statHired > 0 ? F($"this session: {_statHired} hired") : T("as soon as level & money allow"));
                SliderInt(_hireMax, T("Max workers (0 = all)"), 0, 30, "");
            }, () =>
            {
                SliderInt(_planCashiers, T("Cashiers (0 = as many as registers)"), 0, 8, "");
                SliderInt(_planDisplays, T("Display fillers"), 0, 6, "");
                SliderInt(_planShelves, T("Shelf stockers"), 0, 6, "", T("all others fill the pack openers"));
                if (BigButton(T("Recalculate work plan now"), CAccent)) { _lastPlanKey = null; _workerTimer = 59f; Note("Work plan is recalculated"); }
            });
        }

        private void PageHistory()
        {
            PageTitle(T("History"));
            int c = Chips(HistCats.Select(T).ToList(), _histFilter);
            if (c >= 0) _histFilter = c;
            GUILayout.Space(6);
            HistoryRows(200, _histFilter);
        }

        private void PageSettings()
        {
            PageTitle(T("Settings"));
            Section(T("Language"), T("English is the default"));
            var names = Lang.Codes.Select(code => Lang.Names.TryGetValue(code, out var n) ? n : code).ToList();
            int pick = Chips(names, Lang.Codes.IndexOf(Lang.Current));
            if (pick >= 0) _language.Value = Lang.Codes[pick];
            GUILayout.Label(T("Missing your language? Copy lang/en.txt from the GitHub page, translate it and put it into BepInEx/plugins/ShopBot/lang/ as <code>.txt (e.g. it.txt). The log file always stays English."), _sDim);

            Section(T("Display"));
            Two(() =>
            {
                Slider(_dashScale, T("Dashboard size"), 0f, 2.5f, 0.05f, v => v <= 0.3f ? T("auto") : $"{v:0.00}×", T("auto = fits your screen height"));
                Switch(_showOverlay, T("Status line top left"), T("small line with the last action"));
            }, () =>
            {
                GUILayout.BeginVertical(_sCard);
                GUILayout.Label(T("Hotkeys"), _sSmallBold);
                GUILayout.Label(F($"{_dashKey.Value} = dashboard · {_toggleKey.Value} = bot on/off"), _sDim);
                GUILayout.Label(T("Change them in the config file or with the Configuration Manager (F1)."), _sDim);
                GUILayout.EndVertical();
            });

            Section(T("Timing"));
            Two(() =>
            {
                Slider(_idleSeconds, T("Windows only after X sec without input"), 2f, 30f, 1f, v => $"{v:0} s", T("offers/packages only open when you are not pressing anything"));
            }, () =>
            {
                Slider(_startDelay, T("Start delay after loading"), 3f, 60f, 1f, v => $"{v:0} s");
            });

            Section(T("About"));
            GUILayout.BeginVertical(_sCard);
            GUILayout.Label(F($"ShopBot {Info.Metadata.Version} · MIT license"), _sSmallBold);
            GUILayout.Label(T("Bug reports and ideas: GitHub page of the mod (Issues). Please attach BepInEx/LogOutput.log."), _sDim);
            GUILayout.EndVertical();
        }
    }
}
