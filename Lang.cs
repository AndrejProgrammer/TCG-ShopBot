using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ShopBot
{
    // Translations. English is the source language: every UI text in the code is English and is looked up here.
    // Language files: lang/<code>.txt, embedded in the DLL. Files in BepInEx/plugins/ShopBot/lang/ override them
    // or add new languages (first line "@name = Language name").
    // Format: "English text = Translation" per line, "#" = comment, "\n" = line break.
    // Placeholders {0}, {1:N0} … must stay in the translation (order may change).
    public static class Lang
    {
        public static readonly List<string> Codes = new List<string> { "en", "de" };   // more come from lang/*.txt
        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["en"] = "English", ["de"] = "Deutsch"
        };

        private static readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>();
        private static Dictionary<string, string> _cur;
        public static string Current { get; private set; } = "en";

        // While true, T()/F() return English (for the log file – bug reports stay readable)
        [ThreadStatic] public static bool ForceSource;

        public static void Load(string pluginDir, Action<string> warn)
        {
            _tables.Clear();
            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!res.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || res.IndexOf(".lang.", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string code = Path.GetFileNameWithoutExtension(res.Substring(res.LastIndexOf(".lang.", StringComparison.OrdinalIgnoreCase) + 6));
                using (var s = asm.GetManifestResourceStream(res))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    Parse(code, r.ReadToEnd(), warn);
            }
            try
            {
                string dir = Path.Combine(pluginDir ?? "", "lang");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.txt"))
                        Parse(Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), File.ReadAllText(f, Encoding.UTF8), warn);
            }
            catch (Exception e) { warn?.Invoke("Language files: " + e.Message); }
        }

        private static void Parse(string code, string text, Action<string> warn)
        {
            if (!_tables.TryGetValue(code, out var t)) _tables[code] = t = new Dictionary<string, string>();
            if (!Codes.Contains(code)) Codes.Add(code);
            if (!Names.ContainsKey(code)) Names[code] = code;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                if (eq <= 0) continue;
                string key = Unescape(line.Substring(0, eq)), val = Unescape(line.Substring(eq + 3));
                if (key == "@name") { Names[code] = val; continue; }
                if (val.Length > 0) t[key] = val;
            }
        }

        private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\=", "=");

        public static void Set(string code)
        {
            if (string.IsNullOrEmpty(code) || !Codes.Contains(code)) code = "en";
            Current = code;
            _tables.TryGetValue(code, out _cur);
        }

        public static string T(string s)
        {
            if (s == null || ForceSource || _cur == null) return s;
            return _cur.TryGetValue(s, out var v) ? v : s;
        }

        public static string F(FormattableString fs)
        {
            if (fs == null) return "";
            var args = fs.GetArguments();
            string tpl = T(fs.Format);
            try { return string.Format(tpl, args); }
            catch (FormatException) { return string.Format(fs.Format, args); }   // broken translation -> English
        }

        // English rendering of a message (log file)
        public static string Source(FormattableString fs)
        {
            bool old = ForceSource; ForceSource = true;
            try { return string.Format(fs.Format, fs.GetArguments()); }
            finally { ForceSource = old; }
        }

        public static string Source(string tpl, object[] args)
        {
            if (args == null) return tpl;
            bool old = ForceSource; ForceSource = true;
            try { return string.Format(tpl, args); } catch (FormatException) { return tpl; }
            finally { ForceSource = old; }
        }

        // All keys of the source language (for translators)
        public static IEnumerable<string> Keys(string code) =>
            _tables.TryGetValue(code, out var t) ? (IEnumerable<string>)t.Keys : Array.Empty<string>();
    }

    // A list of message parts, joined and translated when shown
    public sealed class LList : IFormattable
    {
        private readonly List<FormattableString> _items; private readonly string _sep;
        public LList(List<FormattableString> items, string sep) { _items = items; _sep = sep; }
        public override string ToString() { var sb = new StringBuilder(); for (int i = 0; i < _items.Count; i++) { if (i > 0) sb.Append(_sep); sb.Append(Lang.F(_items[i])); } return sb.ToString(); }
        public string ToString(string format, IFormatProvider p) => ToString();
    }

    // A text part that is translated only when it is shown (e.g. inside a history message)
    public readonly struct LStr : IFormattable
    {
        private readonly string _s;
        public LStr(string s) { _s = s; }
        public override string ToString() => Lang.T(_s);
        public string ToString(string format, IFormatProvider p) => Lang.T(_s);
    }
}
