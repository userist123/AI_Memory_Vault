// Ce spune numele produsului despre forma lui: culoarea și finisajul („stejar sonoma”, „negru mat”), câte sertare și uși are
// („3 sertare”, codurile magazinelor „3K2F1O” = 3 uși, 2 sertare, 1 oglindă; „V” = vitrină), dacă e rotund, tapițat, cu raft,
// cu oglindă, cu lumini sau cu scaune incluse. Modelul 3D folosește aceste câmpuri, ca piesa randată să fie cea comandată.
// Câmpurile scrise de mână în catalog au întâietate (vezi toEngineCatalog).

export interface DerivedStyle {
  col?: string; col2?: string; wood?: boolean; mat?: 'fabric' | 'velvet' | 'leather';
  drawers?: number; doors?: number; mirrors?: number; glass?: number;
  round?: boolean; shelf?: boolean; uph?: boolean; lights?: boolean; sliding?: boolean; chairs?: number; marble?: boolean;
}

// nuanțele cu nume („stejar sonoma”) și culorile de bază; „alb lucios” e tot alb
const COLORS: [RegExp, string, boolean][] = [
  [/stejar\s+sonoma|sonoma/i, '#c8a97e', true], [/stejar\s+artisan|artisan/i, '#c4a77d', true], [/stejar\s+(alb|deschis)/i, '#d9c9a8', true],
  [/stejar\s+gri/i, '#8d8a85', true], [/stejar\s+(inchis|închis|salbatic\s+inchis|sălbatic\s+închis)/i, '#6b5238', true], [/stejar\s+auriu/i, '#c8a24a', true],
  [/cannolo/i, '#b49b7e', true], [/wenge/i, '#3b2a21', true], [/\bnuc\b/i, '#6b4a36', true], [/natur/i, '#c2a47a', true], [/stejar|furnir|lemn masiv/i, '#b8936a', true],
  [/antracit/i, '#3a3c40', false], [/grafit/i, '#4b4d52', false], [/cappuccino/i, '#8b6b52', false], [/cashmere/i, '#d8cdb8', false],
  [/\bcrem\b/i, '#ece3d0', false], [/\bbej\b/i, '#d8ccb8', false], [/auriu|alam[ăa]/i, '#b08d57', false], [/verde|salvie/i, '#6f7f6a', false],
  [/albastru|bleumarin/i, '#4a5a70', false], [/\broz\b/i, '#d9b2a9', false], [/\bmaro\b/i, '#5a3e2b', false],
  [/\bnegru\b|\bnegre\b|black/i, '#1e1e20', false], [/\bgri\b/i, '#8f9092', false], [/\balb[ăa]?\b|white/i, '#f2f2ef', false],
];

const num = (re: RegExp, s: string) => { const m = s.match(re); return m ? Number(m[1]) : undefined; };

export function deriveStyle(group: string, name: string): DerivedStyle {
  const n = name.normalize('NFC'), out: DerivedStyle = {};
  // culoarea e prima menționată în nume („bej (specs: crem)” e bej); la aceeași poziție câștigă potrivirea mai lungă („stejar gri”)
  // a doua culoare („bej cashmere + stejar artisan”, „negru + stejar”) e a corpului / blatului; lemnul contează oriunde apare
  const hits: { at: number; len: number; col: string; wood: boolean }[] = [];
  const cn = n.replace(/\([^)]*\)/g, ' ');   // notele dintre paranteze („specs: crem”) nu sunt culoarea produsului
  for (const [re, col, wood] of COLORS){ const m = re.exec(cn); if (m && !hits.some(h => m.index < h.at + h.len && h.at < m.index + m[0].length)) hits.push({ at: m.index, len: m[0].length, col, wood }); }
  hits.sort((a, b) => a.at - b.at);
  if (hits[0]){ out.col = hits[0].col; if (hits.some(h => h.wood)) out.wood = true; }
  if (hits[1] && hits[1].col !== hits[0]!.col) out.col2 = hits[1].col;
  // codurile magazinelor de mobilă: 3K = 3 uși (canate), 2F = 2 sertare (fronturi), 1O = o oglindă, 2V = 2 vitrine; ex. „3K2F2V”, „5F”
  const code = n.match(/\b((?:\d[KFOV])+)\b/);
  if (code) for (const [, c, k] of code[1].matchAll(/(\d)([KFOV])/g)){ const v = Number(c);
    if (k === 'K') out.doors = v; else if (k === 'F') out.drawers = v; else if (k === 'O') out.mirrors = v; else out.glass = v; }
  const dr = num(/(\d+)\s*sertar/i, n); if (dr !== undefined) out.drawers = dr; else if (out.drawers === undefined && /cu sertare/i.test(n)) out.drawers = 2; else if (out.drawers === undefined && /cu sertar\b|\+ sertar|si sertar\b|și sertar\b/i.test(n)) out.drawers = 1;
  const ds = num(/(\d+)\s*u[sș]i/i, n); if (ds !== undefined) out.doors = ds; else if (out.doors === undefined && /cu u[sș]i/i.test(n)) out.doors = 2;
  if (out.mirrors === undefined && /oglind/i.test(n)) out.mirrors = 1;
  if (/rotund|oval[aă]|\bØ/i.test(n)) out.round = true;
  if (/\braft\b|\(deschis/i.test(n)) out.shelf = true;
  if (/tapitat|tapițat|catifea|velvet/i.test(n)){ out.uph = true; out.mat = /catifea|velvet/i.test(n) ? 'velvet' : 'fabric'; }
  if (/piele/i.test(n) && !out.mat) out.mat = 'leather';
  if (/lumini|\bLED\b|iluminare/i.test(n)) out.lights = true;
  if (/glisant/i.test(n)) out.sliding = true;
  if (/marm[uo]r/i.test(n)) out.marble = true;
  const ch = num(/(\d+)\s*scaune/i, n); if (group === 'masa') out.chairs = ch ?? (/scaun/i.test(n) ? 4 : 0);
  return out;
}
