// Lista de debitare a bucătăriei pe module: piesele fiecărui corp (carcasă PAL 18, spate HDF 3, fronturi), feroneria, metrii de
// cant și observațiile pentru atelier. Cotele sunt repere comerciale uzuale, NU un standard — toate sunt parametri
// (KitchenCutOptions) și se verifică cu atelierul înainte de debitare.
// Construcție: laterale pe toată înălțimea, fund între laterale; corpul de jos are sus două traverse de 100 mm (față + spate) în
// loc de capac, corpul suspendat și cel înalt au capac între laterale; spatele HDF e aplicat pe spate și e cu 2 mm retras pe
// fiecare latură (lățime − 4 × înălțime − 4), ca să nu se vadă din lateral; raftul e retras 20 mm de la față și are 2 mm joc pe
// lățime; fronturile au 1,5 mm joc pe fiecare latură (lățime − 3, înălțime − 3), două uși peste 600 mm lățime.
// Cant: carcasa doar pe muchia din față (L1), fronturile din PAL pe tot conturul; frontul din MDF vopsit nu se cantuiește.
// Convenția cotelor: finale (cu cant inclus), L = latura pe fibră; la HDF și MDF vopsit fibra nu contează (piesa se poate roti).
import { BOARD_MM, BACK_MM, hingesFor, type CutPart, type Hardware } from './joinery';
import type { KitchenModule, ModuleType } from './kitchen-modules';
import type { KitchenSpec } from './types';

export interface KitchenCutOptions {
  carcassH: number; depth: number; plinth: number; upperH: number; upperD: number; tallH: number;
  edgeCarcass: number; edgeFront: number; frontMaterial: 'pal' | 'mdf'; kerf: number; trim: number; sheetL: number; sheetW: number; minPartMm: number }
export const KITCHEN_CUT_DEFAULT: KitchenCutOptions = { carcassH: 720, depth: 560, plinth: 100, upperH: 720, upperD: 320, tallH: 2100,
  edgeCarcass: 0.4, edgeFront: 2, frontMaterial: 'pal', kerf: 4, trim: 10, sheetL: 2800, sheetW: 2070, minPartMm: 50 };
export const EDGE_MM = [0.4, 1, 2] as const;
/** Intervalele admise (mm). Nume românești pentru mesajele de eroare. */
export const KITCHEN_CUT_RANGE: Record<Exclude<keyof KitchenCutOptions, 'edgeCarcass' | 'edgeFront' | 'frontMaterial'>, [number, number, string]> = {
  carcassH: [600, 900, 'Înălțimea corpului de jos'], depth: [450, 650, 'Adâncimea corpului de jos'], plinth: [0, 200, 'Soclul'], upperH: [300, 1000, 'Înălțimea corpului suspendat'],
  upperD: [250, 400, 'Adâncimea corpului suspendat'], tallH: [1800, 2400, 'Înălțimea corpului înalt'], kerf: [0, 8, 'Lățimea tăieturii'], trim: [0, 30, 'Tunderea marginilor'],
  sheetL: [1000, 4200, 'Lungimea plăcii'], sheetW: [600, 2200, 'Lățimea plăcii'], minPartMm: [0, 300, 'Latura minimă a piesei'] };
export class KitchenCutError extends Error {}

/** Validare strictă a parametrilor salvați (parțiali: doar ce diferă de implicit). Chei necunoscute sau valori în afara intervalului → eroare. */
export function checkKitchenCut(x: unknown): Partial<KitchenCutOptions> {
  if (!x || typeof x !== 'object' || Array.isArray(x)) throw new KitchenCutError('Parametrii de debitare sunt invalizi.');
  const out: Partial<KitchenCutOptions> = {}, nf = (v: number) => String(v).replace('.', ',');
  for (const [k, v] of Object.entries(x as Record<string, unknown>)){
    if (v === undefined) continue;
    if (k === 'frontMaterial'){ if (v !== 'pal' && v !== 'mdf') throw new KitchenCutError('Materialul fronturilor: PAL sau MDF vopsit.'); out.frontMaterial = v; continue; }
    if (k === 'edgeCarcass' || k === 'edgeFront'){ if (!(EDGE_MM as readonly unknown[]).includes(v)) throw new KitchenCutError('Grosimea cantului: 0,4, 1 sau 2 mm.'); out[k] = v as number; continue; }
    const r = Object.hasOwn(KITCHEN_CUT_RANGE, k) ? KITCHEN_CUT_RANGE[k as keyof typeof KITCHEN_CUT_RANGE] : undefined;
    if (!r) throw new KitchenCutError(`Parametru de debitare necunoscut: ${k.replace(/[^\w]/g, '').slice(0, 30)}.`);
    if (typeof v !== 'number' || !Number.isFinite(v) || v < r[0] || v > r[1]) throw new KitchenCutError(`${r[2]}: între ${nf(r[0])} și ${nf(r[1])} mm.`);
    (out as Record<string, number>)[k] = k === 'kerf' ? Math.round(v * 10) / 10 : Math.round(v);
  }
  const o = { ...KITCHEN_CUT_DEFAULT, ...out };
  if (o.sheetW > o.sheetL) throw new KitchenCutError('Lungimea plăcii este latura mare (pe fibră).');
  return out;
}
/** Parametrii compleți: implicit + ce a schimbat utilizatorul. */
export const kitchenCutOptions = (cut?: Partial<KitchenCutOptions> | null): KitchenCutOptions => ({ ...KITCHEN_CUT_DEFAULT, ...(cut || {}) });
/** Doar ce diferă de implicit (ce se salvează pe KitchenSpec.cut); `undefined` dacă nu diferă nimic. */
export function kitchenCutDiff(o: KitchenCutOptions): Partial<KitchenCutOptions> | undefined {
  const d = Object.fromEntries(Object.entries(o).filter(([k, v]) => v !== (KITCHEN_CUT_DEFAULT as any)[k])) as Partial<KitchenCutOptions>;
  return Object.keys(d).length ? d : undefined;
}

export type KitchenMaterial = 'pal' | 'hdf' | 'front';
export type PartKind = 'side' | 'bottom' | 'top' | 'rail' | 'shelf' | 'fixedShelf' | 'back' | 'door' | 'drawerFront' | 'applianceFront' | 'blind' | 'filler' | 'plinth';
export const PART_NAME: Record<PartKind, string> = { side: 'Lateral', bottom: 'Fund', top: 'Capac', rail: 'Traversă', shelf: 'Raft', fixedShelf: 'Poliță fixă', back: 'Spate HDF', door: 'Ușă',
  drawerFront: 'Front sertar', applianceFront: 'Front mașină de vase', blind: 'Panou fix (front orb)', filler: 'Bandă de completare', plinth: 'Soclu' };
export const MODULE_NAME: Record<ModuleType, string> = { base: 'Corp', drawers: 'Sertare', sink: 'Chiuvetă', dishwasher: 'Mașină de vase', hob: 'Plită', oven: 'Cuptor', tall: 'Coloană', fridge: 'Frigider', corner: 'Colț', filler: 'Completare' };
/** O piesă din lista bucătăriei: ca la dulap, plus materialul „front”, corpul din care face parte și felul piesei (pentru traducere). */
export interface KitchenCutPart extends Omit<CutPart, 'material'> { material: KitchenMaterial; module: string; group: string; kind: PartKind }
/** Un corp din listă: J<n> = corpul de jos / înalt de pe poziția n (de la stânga), S<n> = suspendatul de deasupra lui, SOC = soclul. */
export interface KitchenCutGroup { id: string; row: 'base' | 'upper' | 'plinth'; type?: ModuleType; wCm?: number; label: string }
export type HardwareKey = 'hinge' | 'runner' | 'drawerBox' | 'leg' | 'shelfPin' | 'hanger' | 'handle' | 'gola' | 'push' | 'plinthClip' | 'confirmat';
export interface KitchenHardware extends Hardware { key: HardwareKey }
export type NoteCode = 'sinkNoBack' | 'dishwasher' | 'hood' | 'cornerBlind' | 'oven' | 'fridge' | 'tooNarrow' | 'mdf' | 'plinth' | 'verify';
export interface KitchenCutNote { code: NoteCode; group?: string; text: string }
export interface KitchenCutResult { groups: KitchenCutGroup[]; parts: KitchenCutPart[]; hardware: KitchenHardware[]; notes: KitchenCutNote[];
  edgeM: { mm: number; m: number }[]; areas: Record<KitchenMaterial, number>; small: number[]; options: KitchenCutOptions }

const OVEN_NICHE = 595, CORNER_DOOR = 450, RAIL = 100, SHELF_BACK = 20, MIN_CARCASS = 200, PLINTH_MAX = 2700;
const cm = (w: number) => String(Math.round(w * 10) / 10).replace('.', ',');
const NOTE: Record<NoteCode, string> = {
  sinkNoBack: 'corp de chiuvetă fără raft și fără spate HDF (acces la instalații)', dishwasher: 'mașină de vase: doar front, fără carcasă; frontul se verifică după fișa aparatului',
  hood: 'deasupra plitei nu e corp suspendat: loc pentru hotă', cornerBlind: 'colț orb: ușă de 45 cm + panou fix; de verificat cu atelierul (feronerie de colț)',
  oven: 'corp de cuptor: nișă fără front și fără spate (ventilație); cotele nișei după fișa aparatului', fridge: 'frigider încorporabil: fără spate (ventilație); împărțirea fronturilor după fișa aparatului',
  tooNarrow: 'modul prea îngust pentru un corp (sub 20 cm): nu are piese în listă', mdf: 'fronturi din MDF vopsit: fără cant, muchiile se rotunjesc la vopsire',
  plinth: 'soclul se ajustează la montaj (lungime și colțuri)', verify: 'construcție și cote uzuale, de verificat cu atelierul înainte de debitare' };

/** Lista de debitare pentru modulele bucătăriei (lățimi în cm), după alegerile din KitchenSpec (suspendate, mânere, fronturi). */
export function kitchenCutList(modules: KitchenModule[], kitchen: KitchenSpec, opt: Partial<KitchenCutOptions> | undefined = kitchen.cut): KitchenCutResult {
  const o = kitchenCutOptions(opt), t = BOARD_MM, R = Math.round, mdf = o.frontMaterial === 'mdf', upper = kitchen.upper ?? 'open', handle = kitchen.handle ?? 'bar';
  const FE: CutPart['edges'] = mdf ? [0, 0, 0, 0] : [1, 1, 1, 1], CE: CutPart['edges'] = [1, 0, 0, 0];
  const groups: KitchenCutGroup[] = [], raw: KitchenCutPart[] = [], notes: KitchenCutNote[] = [];
  const hw: Record<HardwareKey, number> = { hinge: 0, runner: 0, drawerBox: 0, leg: 0, shelfPin: 0, hanger: 0, handle: 0, gola: 0, push: 0, plinthClip: 0, confirmat: 0 };
  let g!: KitchenCutGroup;
  const group = (x: KitchenCutGroup) => { groups.push(g = x); };
  const note = (code: NoteCode, withGroup = true) => notes.push({ code, ...(withGroup ? { group: g.id } : {}), text: withGroup ? `${g.label}: ${NOTE[code]}` : NOTE[code] });
  const add = (kind: PartKind, l: number, w: number, qty: number, material: KitchenMaterial, edges: CutPart['edges'] = CE, grain = true) => {
    if (qty > 0 && l > 0 && w > 0) raw.push({ name: PART_NAME[kind], kind, l: R(l), w: R(w), qty, edges: material === 'hdf' ? [0, 0, 0, 0] : edges, material, grain: material === 'hdf' ? false : grain, module: g.label, group: g.id }); };
  const front = (kind: PartKind, l: number, w: number, qty: number) => add(kind, l, w, qty, 'front', FE, !mdf);
  const grip = (n: number, wMm: number) => { if (handle === 'profile') hw.gola += n * wMm; else if (handle === 'none') hw.push += n; else hw.handle += n; };
  /** Carcasa: laterale pe toată înălțimea, fund între ele, sus traverse (corp jos) sau capac; rafturi mobile; spate HDF aplicat. */
  const carcass = (W: number, H: number, D: number, c: { top: 'rails' | 'panel'; shelves?: number; fixed?: number; back?: boolean }) => {
    add('side', H, D, 2, 'pal'); add('bottom', W - 2 * t, D, 1, 'pal');
    if (c.top === 'rails') add('rail', W - 2 * t, RAIL, 2, 'pal'); else add('top', W - 2 * t, D, 1, 'pal');
    if (c.fixed) add('fixedShelf', W - 2 * t, D, c.fixed, 'pal');
    if (c.shelves){ add('shelf', W - 2 * t - 2, D - SHELF_BACK, c.shelves, 'pal'); hw.shelfPin += c.shelves * 4; }
    if (c.back !== false) add('back', H - 4, W - 4, 1, 'hdf');
    hw.confirmat += 8 + (c.fixed ?? 0) * 4 + (c.top === 'rails' ? 4 : 0);
  };
  const doors = (W: number, H: number) => { const k = W > 600 ? 2 : 1, dw = R(W / k - 3); front('door', H - 3, dw, k); hw.hinge += k * hingesFor(H - 3); grip(k, dw); };
  const drawers = (W: number, H: number, n: number) => { front('drawerFront', W - 3, H / n - 3, n); hw.runner += n; hw.drawerBox += n; grip(n, W - 3); };
  const blindDoors = (W: number, H: number) => { if (W - CORNER_DOOR - 3 < 50){ doors(W, H); return; }
    front('door', H - 3, CORNER_DOOR - 3, 1); front('blind', H - 3, W - CORNER_DOOR - 3, 1); hw.hinge += hingesFor(H - 3); grip(1, CORNER_DOOR - 3); };

  modules.forEach((m, i) => {
    const W = R(m.w * 10), H = o.carcassH, D = o.depth, TH = o.tallH;
    group({ id: `J${i + 1}`, row: 'base', type: m.type, wCm: m.w, label: `J${i + 1} ${MODULE_NAME[m.type]} ${cm(m.w)}` });
    if (m.type === 'filler') front('filler', H - 3, W, 1);   // aliniată cu fronturile vecine (același joc pe înălțime)
    else if (m.type === 'dishwasher'){ front('applianceFront', H - 3, W - 3, 1); grip(1, W - 3); note('dishwasher'); }
    else if (W < MIN_CARCASS) note('tooNarrow');
    else { hw.leg += 4;
      if (m.type === 'base'){ carcass(W, H, D, { top: 'rails', shelves: 1 }); doors(W, H); }
      else if (m.type === 'drawers'){ carcass(W, H, D, { top: 'rails' }); drawers(W, H, 3); }
      else if (m.type === 'sink'){ carcass(W, H, D, { top: 'rails', back: false }); doors(W, H); note('sinkNoBack'); }
      else if (m.type === 'hob'){ carcass(W, H, D, { top: 'rails' }); drawers(W, H, 2); }
      else if (m.type === 'oven'){ carcass(W, H, D, { top: 'rails', fixed: 1, back: false }); if (H - OVEN_NICHE - 3 >= 60) drawers(W, H - OVEN_NICHE, 1); note('oven'); }
      else if (m.type === 'corner'){ carcass(W, H, D, { top: 'rails', shelves: 1 }); blindDoors(W, H); note('cornerBlind'); }
      else { // corp înalt (coloană sau frigider): două fronturi suprapuse, cel de jos aliniat cu fronturile corpurilor de jos
        const k = W > 600 ? 2 : 1, dw = R(W / k - 3);
        if (m.type === 'tall') carcass(W, TH, D, { top: 'panel', shelves: 4 }); else { carcass(W, TH, D, { top: 'panel', fixed: 1, back: false }); note('fridge'); }
        front('door', H - 3, dw, k); front('door', TH - H - 3, dw, k); hw.hinge += k * (hingesFor(H - 3) + hingesFor(TH - H - 3)); grip(2 * k, dw); }
    }
  });
  if (upper !== 'none') modules.forEach((m, i) => {
    if (m.type === 'tall' || m.type === 'fridge') return;
    const W = R(m.w * 10), H = o.upperH, D = o.upperD;
    group({ id: `S${i + 1}`, row: 'upper', type: m.type, wCm: m.w, label: `S${i + 1} Suspendat ${cm(m.w)}` });
    if (m.type === 'hob'){ note('hood'); return; }
    if (m.type === 'filler'){ if (upper === 'closed') front('filler', H - 3, W, 1); else add('filler', H, W, 1, 'pal', [1, 1, 1, 1]); return; }
    if (W < MIN_CARCASS){ note('tooNarrow'); return; }
    carcass(W, H, D, { top: 'panel', shelves: 1 }); hw.hanger += 2;
    if (upper === 'closed'){ if (m.type === 'corner') blindDoors(W, H); else doors(W, H); }
  });
  const total = modules.reduce((a, m) => a + R(m.w * 10), 0);
  if (o.plinth > 0 && total > 0){ const max = Math.max(300, Math.min(PLINTH_MAX, o.sheetL - 2 * o.trim)), n = Math.ceil(total / max), len = R(total / n);
    group({ id: 'SOC', row: 'plinth', label: 'Soclu' }); add('plinth', len, o.plinth, n, 'pal'); hw.plinthClip += n * Math.max(2, Math.ceil(len / 600)); note('plinth'); }
  if (mdf && raw.some(p => p.material === 'front')) notes.push({ code: 'mdf', text: NOTE.mdf });
  notes.push({ code: 'verify', text: NOTE.verify });

  // piesele identice din același corp se adună
  const merged = new Map<string, KitchenCutPart>();
  for (const p of raw){ const k = `${p.group}|${p.kind}|${p.l}|${p.w}|${p.edges.join('')}|${p.material}`, c = merged.get(k); if (c) c.qty += p.qty; else merged.set(k, { ...p }); }
  const parts = [...merged.values()], area = (m: KitchenMaterial) => Math.round(parts.filter(p => p.material === m).reduce((a, p) => a + p.l * p.w * p.qty, 0) / 1e4) / 100;
  const edge = new Map<number, number>();
  for (const p of parts){ const mm = p.material === 'front' ? o.edgeFront : o.edgeCarcass, len = p.qty * ((p.edges[0] + p.edges[1]) * p.l + (p.edges[2] + p.edges[3]) * p.w); if (len > 0) edge.set(mm, (edge.get(mm) ?? 0) + len); }
  const H = (key: HardwareKey, name: string, qty: number, unit: string): KitchenHardware[] => qty > 0 ? [{ key, name, qty, unit }] : [];
  const hardware = [...H('hinge', 'Balamale aplicate cu amortizor', hw.hinge, 'buc'), ...H('runner', 'Glisiere sertar (pereche)', hw.runner, 'set'), ...H('drawerBox', 'Cutie sertar (laterale + spate + fund)', hw.drawerBox, 'buc'),
    ...H('leg', 'Picioare reglabile', hw.leg, 'buc'), ...H('plinthClip', 'Clipsuri soclu', hw.plinthClip, 'buc'), ...H('shelfPin', 'Suporți raft', hw.shelfPin, 'buc'), ...H('hanger', 'Suporți de prindere corp suspendat', hw.hanger, 'buc'),
    ...H('handle', handle === 'knob' ? 'Butoni' : 'Mânere', hw.handle, 'buc'), ...H('gola', 'Profil mâner (gola)', Math.round(hw.gola / 100) / 10, 'm'), ...H('push', 'Sistem de deschidere prin apăsare', hw.push, 'buc'),
    ...H('confirmat', 'Confirmate / excentrice', hw.confirmat, 'buc')];
  return { groups, parts, hardware, notes, edgeM: [...edge].sort((a, b) => a[0] - b[0]).map(([mm, len]) => ({ mm, m: Math.round(len / 10) / 100 })),
    areas: { pal: area('pal'), front: area('front'), hdf: area('hdf') }, small: parts.flatMap((p, i) => Math.min(p.l, p.w) < o.minPartMm ? [i + 1] : []), options: o };
}

const FINISH_RO: Record<string, string> = { matt: 'mat', gloss: 'lucios', wood: 'furnir / decor lemn' };
/** Materialul unei piese, așa cum îl citește atelierul (decorul fronturilor = culoarea și finisajul alese în bucătărie). */
export function kitchenMaterialLabel(m: KitchenMaterial, kitchen: KitchenSpec, o: KitchenCutOptions): string {
  if (m === 'hdf') return `HDF ${BACK_MM} mm`; if (m === 'pal') return `PAL ${BOARD_MM} mm carcasă`;
  const decor = [kitchen.frontColor, FINISH_RO[kitchen.frontFinish ?? '']].filter(Boolean).join(' ');
  return `${o.frontMaterial === 'mdf' ? `Front MDF ${BOARD_MM} mm vopsit` : `Front PAL ${BOARD_MM} mm`}${decor ? ' ' + decor : ''}`;
}
export const KITCHEN_CSV_HEAD = ['Nr', 'Denumire', 'Material/decor', 'L (mm)', 'l (mm)', 'Buc', 'Fibră (1/0)', 'Cant L1', 'Cant L2', 'Cant l1', 'Cant l2', 'Observații'];
export const KITCHEN_CSV_CONVENTION = 'Cote finale, cu cant inclus; L = latura pe fibră; Fibră 1 = nu se rotește';
/** O celulă CSV: ghilimele la separator / ghilimele / rând nou; textul care ar porni o formulă în Excel (= + - @) primește un apostrof. */
export const csvCell = (v: string | number): string => { let s = String(v); if (typeof v === 'string' && /^[=+\-@\t\r]/.test(s)) s = `'${s}`; return /[";\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
/** CSV pentru atelier: BOM UTF-8, separator „;”, convenția cotelor declarată în antet, apoi o linie pe piesă (Observații = corpul). */
export function kitchenCutCsv(res: Pick<KitchenCutResult, 'parts' | 'options'>, kitchen: KitchenSpec, projectName = ''): string {
  const o = res.options, mm = (v: number) => String(v).replace('.', ','), name = projectName.replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, 120);
  const pre = [`Lista de debitare bucătărie${name ? ' — ' + name : ''}`, KITCHEN_CSV_CONVENTION,
    `Cant carcasă ABS ${mm(o.edgeCarcass)} mm; cant fronturi ${o.frontMaterial === 'mdf' ? 'fără (MDF vopsit)' : `ABS ${mm(o.edgeFront)} mm`}; în coloanele Cant e grosimea cantului pe latura respectivă (gol = fără cant)`,
    `Placă ${o.sheetL}×${o.sheetW} mm; construcție și cote de verificat cu atelierul înainte de debitare`].map(l => [l]);
  const rows = res.parts.map((p, i) => { const e = p.material === 'front' ? o.edgeFront : o.edgeCarcass;
    return [i + 1, p.name, kitchenMaterialLabel(p.material, kitchen, o), p.l, p.w, p.qty, p.grain ? 1 : 0, ...p.edges.map(x => x ? mm(e) : ''), p.module]; });
  return '﻿' + [...pre, KITCHEN_CSV_HEAD, ...rows].map(r => r.map(csvCell).join(';')).join('\r\n') + '\r\n';
}
