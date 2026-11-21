// Generează components/theme-pro.gen.css din app/globals.css: aceleași reguli, prefixate cu .ws.theme-pro, cu doar
// declarațiile de culoare, iar culorile deschise trecute în varianta întunecată (fundaluri gri închis, text deschis, roșul
// mărcii → albastrul de selecție). Rulează: node scripts/gen-theme-pro.mjs (după orice schimbare de culori în globals.css).
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const css = readFileSync(join(root, 'app/globals.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

// ---------- culori ----------
const toHsl = (r, g, b) => { r /= 255; g /= 255; b /= 255; const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2; let h = 0, s = 0;
  if (mx !== mn){ const d = mx - mn; s = l > .5 ? d / (2 - mx - mn) : d / (mx + mn); h = mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4; h /= 6; } return [h, s, l]; };
const fromHsl = (h, s, l) => { const f = n => { const k = (n + h * 12) % 12, a = s * Math.min(l, 1 - l); return Math.round(255 * (l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)))); }; return [f(0), f(8), f(4)]; };
const hex = ([r, g, b]) => '#' + [r, g, b].map(x => x.toString(16).padStart(2, '0')).join('');
const BRAND = { c63c29: '#2470cf', a82f1f: '#1d66c0', b5341f: '#2c76cc', '83291f': '#2c76cc', f7e3dc: '#1f3a5c', e8a17e: '#7fb3f5', ff8c78: '#7fb3f5' };
/** kind: text | bg | line | any */
function dark(r, g, b, kind){
  const key = hex([r, g, b]).slice(1); if (BRAND[key]) return BRAND[key];
  if (kind === 'shadow') return '#000000';
  const [h, s0, l] = toHsl(r, g, b), s = Math.max(r, g, b) - Math.min(r, g, b) < 34 ? 0 : s0;   // aproape gri: gri curat
  if (kind === 'text' && l > .9) return '#ffffff';                     // text pe buton colorat
  if (kind === 'text' && l > .6) return hex(fromHsl(h, s * .2, Math.max(l, .78)));   // text deschis de pe zonele deja închise (bara de sus): rămâne deschis
  if (kind === 'bg' && l < .3) return hex(fromHsl(h, s * .15, Math.max(.1, l)));     // fundaluri închise (cipuri, butoane): rămân închise
  if (kind !== 'text' && l < .35) return hex(fromHsl(h, s * .15, Math.max(.1, l)));   // zonele deja închise (bara de sus, 3D) rămân închise
  let nl = l > .5 ? .115 + (1 - l) * .34 : .93 - l * .6;
  if (kind === 'line' && l > .5) nl = Math.max(nl, .24);
  const ns = s < .3 ? s * .12 : s * .75;
  return hex(fromHsl(h, ns, Math.min(.96, Math.max(.06, nl))));
}
const kindOf = prop => /^--(ink|lead|graphite|fg)/.test(prop) || prop === 'color' || prop === 'fill' || prop === 'caret-color' || prop === '-webkit-text-fill-color' ? 'text'
  : /^--(paper|surface|bg)/.test(prop) || prop.startsWith('background') ? 'bg' : prop === 'box-shadow' || prop === 'text-shadow' ? 'shadow' : /^--(line|rule)/.test(prop) || prop.startsWith('border') || prop.startsWith('outline') || prop === 'stroke' ? 'line' : 'any';
const COLOR_RE = /#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b|rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)/g;
function mapValue(v, kind){
  return v.replace(COLOR_RE, (m, hx, r, g, b, a) => {
    if (hx){ const full = hx.length === 3 ? hx.split('').map(c => c + c).join('') : hx; const n = parseInt(full, 16); return dark(n >> 16, (n >> 8) & 255, n & 255, kind); }
    const d = dark(+r, +g, +b, kind), n = parseInt(d.slice(1), 16); return a !== undefined ? `rgba(${n >> 16},${(n >> 8) & 255},${n & 255},${a})` : d; });
}
const HAS_COLOR = new RegExp(COLOR_RE.source);
// planul (desenul pe hârtie) și scena 3D își păstrează culorile: sunt „monitorul”, nu interfața
const KEEP = /plan|svg|\.canvas|viewer|minimap|\.wall\b|\.room\b|dim-|ruler/i;
const COLOR_PROPS = /^(color|background(-color|-image)?|border(-(top|right|bottom|left))?(-color)?|outline(-color)?|box-shadow|text-shadow|fill|stroke|caret-color|accent-color|text-decoration-color|column-rule-color|-webkit-text-fill-color|--[\w-]+|color-scheme)$/;

// ---------- parcurgere simplă a CSS-ului (reguli și @media/@supports imbricate) ----------
function walk(src){ const out = []; let i = 0;
  while (i < src.length){ const open = src.indexOf('{', i); if (open < 0) break; const head = src.slice(i, open).trim();
    let depth = 1, j = open + 1; while (j < src.length && depth){ if (src[j] === '{') depth++; else if (src[j] === '}') depth--; j++; }
    const body = src.slice(open + 1, j - 1); i = j;
    if (/^@(media|supports|container)/.test(head)){ const inner = walk(body); if (inner.length) out.push(`${head}{\n${inner.join('\n')}\n}`); continue; }
    if (head.startsWith('@')) continue;
    const sels = head.split(',').map(s => s.trim()).filter(Boolean).filter(s => !/^(html|body|:root|\*)/.test(s) && !KEEP.test(s))
      .map(s => s.startsWith('.ws.') ? s.replace(/^\.ws\./, '.ws.theme-pro.') : s.startsWith('.ws ') || s === '.ws' ? s.replace(/^\.ws/, '.ws.theme-pro') : `.ws.theme-pro ${s}`);
    if (!sels.length) continue;
    const decls = body.split(';').map(d => d.trim()).filter(Boolean).map(d => { const k = d.indexOf(':'); return k < 0 ? null : [d.slice(0, k).trim(), d.slice(k + 1).trim()]; })
      .filter(d => d && COLOR_PROPS.test(d[0]) && (d[0] === 'color-scheme' || HAS_COLOR.test(d[1]) || /\b(white|black)\b/.test(d[1])))
      .map(([p, v]) => { COLOR_RE.lastIndex = 0; if (p === 'color-scheme') return `${p}:dark`; const vv = mapValue(v.replace(/\bwhite\b/g, '#ffffff').replace(/\bblack\b/g, '#000000'), kindOf(p)); return `${p}:${vv}`; });
    COLOR_RE.lastIndex = 0;
    // „cip inversat” (fundal = culoarea textului, ex. filtrul ales): în tema închisă devine gri ridicat cu text alb
    if (/background(-color)?:\s*var\(--(ink|graphite)\)/.test(body)) decls.push('background:#4a4a4a', 'border-color:#5c5c5c', 'color:#ffffff');
    if (decls.length) out.push(`${sels.join(',')}{${decls.join(';')}}`);
  }
  return out; }

const rules = walk(css);
const target = join(root, 'components/theme-pro.gen.css'), text = `/* GENERAT de scripts/gen-theme-pro.mjs din app/globals.css — nu edita manual; ajustările merg în components/theme-pro.css. */\n${rules.join('\n')}\n`;
if (process.argv.includes('--check')){ const cur = readFileSync(target, 'utf8'); if (cur !== text){ console.error('theme-pro.gen.css nu e la zi: rulează node scripts/gen-theme-pro.mjs'); process.exit(1); } console.log('la zi'); }
else { writeFileSync(target, text); console.log(`${rules.length} reguli`); }