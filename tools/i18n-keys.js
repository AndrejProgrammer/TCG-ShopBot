// Collects all translatable texts from the C# sources and updates the language files.
//   node tools/i18n-keys.js            -> writes lang/en.txt (all keys) and reports missing keys per language
//   node tools/i18n-keys.js --missing  -> also appends missing keys (empty value) to every lang/<code>.txt
// Translatable: T("..."), F($"..."), Note("..."), NoteF($"..."), L("..."), plus lines marked // i18n (all texts) or // i18n-last (last text)
const fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..');
const BS = String.fromCharCode(92), NL = String.fromCharCode(10);

function templateOf(body, interp) {
  if (!interp) return body;
  let out = '', n = 0, i = 0;
  while (i < body.length) {
    const c = body[i];
    if (c === '{' && body[i + 1] === '{') { out += '{{'; i += 2; continue; }
    if (c === '}' && body[i + 1] === '}') { out += '}}'; i += 2; continue; }
    if (c === '{') {
      let depth = 1, j = i + 1, q = false;
      for (; j < body.length; j++) {
        const d = body[j];
        if (q) { if (d === BS) { j++; continue; } if (d === '"') q = false; continue; }
        if (d === '"') { q = true; continue; }
        if (d === '{') depth++; else if (d === '}') { depth--; if (depth === 0) break; }
      }
      const inner = body.slice(i + 1, j);
      let p = 0, k = 0, cut = -1, qq = false, tern = 0;
      for (; k < inner.length; k++) {
        const d = inner[k];
        if (qq) { if (d === BS) { k++; continue; } if (d === '"') qq = false; continue; }
        if (d === '"') { qq = true; continue; }
        if ('([{'.includes(d)) p++; else if (')]}'.includes(d)) p--;
        else if (p === 0 && d === '?' && inner[k + 1] !== '?' && inner[k - 1] !== '?' && inner[k + 1] !== '.') tern++;
        else if (p === 0 && (d === ':' || d === ',')) { if (d === ':' && tern > 0) { tern--; continue; } cut = k; break; }
      }
      out += '{' + (n++) + (cut < 0 ? '' : inner.slice(cut)) + '}';
      i = j + 1; continue;
    }
    out += c; i++;
  }
  return out;
}

// reads a C# string literal starting at src[i] (at $ or "), returns {body, interp, end}
function readLit(src, i) {
  let interp = false;
  if (src[i] === '$') { interp = true; i++; }
  if (src[i] !== '"') return null;
  let j = i + 1, depth = 0;
  for (; j < src.length; j++) {
    const c = src[j];
    if (interp && c === '{') { if (depth === 0 && src[j + 1] === '{') { j++; continue; } depth++; continue; }
    if (interp && c === '}') { if (depth === 0 && src[j + 1] === '}') { j++; continue; } if (depth > 0) depth--; continue; }
    if (depth > 0 && c === '"') { let k = j + 1; while (k < src.length && src[k] !== '"') { if (src[k] === BS) k++; k++; } j = k; continue; }
    if (c === BS) { j++; continue; }
    if (c === '"') break;
  }
  return { body: src.slice(i + 1, j), interp, end: j + 1 };
}

function unescapeCs(s) { return s.replace(/\\n/g, '\n').replace(/\\"/g, '"').replace(/\\\\/g, BS); }

const keys = new Map();   // key -> first location
function add(k, where) { if (k && /[A-Za-z]/.test(k) && !keys.has(k)) keys.set(k, where); }

for (const f of fs.readdirSync(root).filter(f => f.endsWith('.cs'))) {
  // whole-line comments are ignored
  const src = fs.readFileSync(path.join(root, f), 'utf8').split(NL).map(l => /^\s*\/\//.test(l) ? '' : l).join(NL);
  const re = /\b(T|F|Note|NoteF|L)\(\s*(\$?")/g;
  let m;
  while ((m = re.exec(src))) {
    const lit = readLit(src, m.index + m[0].length - m[2].length);
    if (!lit) continue;
    add(unescapeCs(templateOf(lit.body, lit.interp)), f);
  }
  // lists marked with  // i18n  on the same line: every "..." literal on that line is a key
  for (const line of src.split('\n')) {
    if (!/\/\/\s*i18n/.test(line)) continue;
    // i18n-fs: every interpolated $"..." on the line is a message template (FormattableString shown later with Lang.F)
    if (/\/\/\s*i18n-fs/.test(line)) {
      const codeFs = line.replace(/\/\/\s*i18n-fs.*$/, '');
      for (let i = codeFs.indexOf('$"'); i >= 0; i = codeFs.indexOf('$"', i + 2)) {
        const lit = readLit(codeFs, i);
        if (lit) add(unescapeCs(templateOf(lit.body, true)), f);
      }
      continue;
    }
    const code = line.replace(/\/\/.*$/, '');
    const lits = [...code.matchAll(/"([^"\\]*)"/g)].map(x => x[1]);
    if (/\/\/\s*i18n-last/.test(line)) { if (lits.length) add(lits[lits.length - 1], f); }   // tuple lines: only the last text
    else for (const l of lits) add(l, f);
  }
}

// '=' is written as '\=' so that ' = ' only separates key and value
function esc(s) { return s.replace(/\n/g, '\\n').replace(/=/g, '\\='); }
function unesc(s) { return s.replace(/\\n/g, '\n').replace(/\\=/g, '='); }
const langDir = path.join(root, 'lang');
const en = ['# ShopBot – English source texts (= the keys). Copy this file as <code>.txt and translate the right side.',
  '# Keep {0}, {1:N0} … and <b>/<color> tags. "\\n" = line break.', '@name = English', ''];
for (const k of [...keys.keys()].sort((a, b) => a.localeCompare(b))) en.push(esc(k) + ' = ' + esc(k));
fs.writeFileSync(path.join(langDir, 'en.txt'), en.join('\n') + '\n');
console.log(keys.size + ' keys -> lang/en.txt');

const addMissing = process.argv.includes('--missing');
for (const f of fs.readdirSync(langDir).filter(f => f.endsWith('.txt') && f !== 'en.txt')) {
  const p = path.join(langDir, f), text = fs.readFileSync(p, 'utf8');
  const have = new Set();
  for (const line of text.split(/\r?\n/)) { const e = line.indexOf(' = '); if (e > 0 && line[0] !== '#') have.add(unesc(line.slice(0, e))); }
  const missing = [...keys.keys()].filter(k => !have.has(k));
  const unused = [...have].filter(k => k !== '@name' && !keys.has(k));
  console.log(`${f}: ${missing.length} missing, ${unused.length} unused`);
  if (addMissing && missing.length) fs.appendFileSync(p, '\n# --- new, please translate ---\n' + missing.map(k => esc(k) + ' = ').join('\n') + '\n');
}
