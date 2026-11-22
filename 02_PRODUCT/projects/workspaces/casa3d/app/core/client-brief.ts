// Chestionarul clientului: brief-ul completat de client pe un link public (fără cont), cu poze de inspirație.
// Aici e doar partea pură: lista întrebărilor, validarea strictă a răspunsurilor, maparea spre brief-ul designerului
// (core/brief.ts) și curățarea pozelor JPEG de metadate. Stocarea și linkul sunt în lib/client-brief.ts.
// Nu se întreabă nimic despre sănătate (alergii etc.): ar fi date din categoria specială a art. 9 GDPR.
import { STYLES, PRIORITIES, DEFAULT_BRIEF, type DesignBrief, type Style, type Priority } from './brief';
import { clean } from './site';

export class ClientBriefError extends Error { status = 400; }
export const UNSURE = 'nu_stiu';
const ROOMS = ['living', 'bucatarie', 'dormitor', 'camera_copil', 'baie', 'hol', 'birou', 'balcon', 'dressing'] as const;
export const BUDGET_RANGES = ['sub_5k', '5_10k', '10_20k', '20_40k', 'peste_40k'] as const;
export type BudgetRange = typeof BUDGET_RANGES[number];

export type QuestionKind = 'enum' | 'multi' | 'int' | 'bool' | 'text';
export interface Question { key: string; section: SectionId; kind: QuestionKind; options?: readonly string[]; min?: number; max?: number; required?: boolean; long?: boolean; ordered?: boolean }
export const SECTIONS = ['home', 'people', 'style', 'rooms', 'budget', 'other'] as const;
export type SectionId = typeof SECTIONS[number];
/** Întrebările, în ordinea din formular. `max` = numărul maxim de opțiuni (multi), valoarea maximă (int) sau lungimea (text).
 *  `required` = obligatoriu doar la trimitere; ciorna poate fi oricât de incompletă. */
export const QUESTIONS = [
  // locuința
  { key: 'propertyType', section: 'home', kind: 'enum', options: ['apartament', 'casa', 'studio'], required: true },
  { key: 'state', section: 'home', kind: 'enum', options: ['nou_gri', 'nou_cheie', 'vechi_renovare', 'locuit_redecorare'] },
  // cine locuiește și obiceiuri
  { key: 'adults', section: 'people', kind: 'int', min: 1, max: 8, required: true },
  { key: 'children', section: 'people', kind: 'int', min: 0, max: 6 },
  { key: 'childAges', section: 'people', kind: 'multi', options: ['0-3', '4-10', '11-17'] },
  { key: 'pets', section: 'people', kind: 'multi', options: ['caine', 'pisica', 'altele'] },
  { key: 'wfh', section: 'people', kind: 'enum', options: ['nu', 'ocazional', 'zilnic'] },
  { key: 'cooking', section: 'people', kind: 'enum', options: ['rar', 'zilnic', 'pasiune'] },
  { key: 'guests', section: 'people', kind: 'enum', options: ['rar', 'des', 'des_cu_cazare'] },
  // stil și culori
  { key: 'styles', section: 'style', kind: 'multi', options: [...Object.keys(STYLES), UNSURE], max: 2, required: true, ordered: true },
  { key: 'mood', section: 'style', kind: 'multi', options: ['luminos', 'cald', 'minimalist', 'elegant', 'colorat', 'natural'], max: 3 },
  { key: 'colorsLike', section: 'style', kind: 'text', max: 120 },
  { key: 'colorsAvoid', section: 'style', kind: 'text', max: 120 },
  { key: 'avoidMaterials', section: 'style', kind: 'multi', options: ['sticla', 'metal', 'negru', 'lucios', 'piele'] },
  // camere și depozitare
  { key: 'rooms', section: 'rooms', kind: 'multi', options: ROOMS, required: true },
  { key: 'priorityRoom', section: 'rooms', kind: 'enum', options: [...ROOMS, UNSURE] },
  { key: 'priorities', section: 'rooms', kind: 'multi', options: Object.keys(PRIORITIES) },
  { key: 'storage', section: 'rooms', kind: 'multi', options: ['haine', 'carti', 'sport', 'jucarii', 'camara'] },
  { key: 'keepItems', section: 'rooms', kind: 'text', max: 400, long: true },
  // buget și termen
  { key: 'budgetRange', section: 'budget', kind: 'enum', options: [...BUDGET_RANGES, UNSURE], required: true },
  { key: 'budgetIncludes', section: 'budget', kind: 'multi', options: ['mobilier', 'finisaje', 'electrocasnice', 'manopera'] },
  { key: 'suppliers', section: 'budget', kind: 'multi', options: ['IKEA', 'Dedeman', 'la_comanda'] },
  { key: 'deadline', section: 'budget', kind: 'enum', options: ['1_luna', '3_luni', '6_luni', 'flexibil'] },
  // altele
  { key: 'accessibility', section: 'other', kind: 'bool' },
  { key: 'dislikes', section: 'other', kind: 'text', max: 400, long: true },
  { key: 'notes', section: 'other', kind: 'text', max: 600, long: true },
] as const satisfies readonly Question[];
export type ClientBriefKey = typeof QUESTIONS[number]['key'];
const BY_KEY = new Map<string, Question>(QUESTIONS.map(q => [q.key, q]));

export interface ClientBrief { v: 1;
  propertyType?: 'apartament' | 'casa' | 'studio'; state?: 'nou_gri' | 'nou_cheie' | 'vechi_renovare' | 'locuit_redecorare';
  adults?: number; children?: number; childAges?: string[]; pets?: string[]; wfh?: 'nu' | 'ocazional' | 'zilnic'; cooking?: 'rar' | 'zilnic' | 'pasiune'; guests?: 'rar' | 'des' | 'des_cu_cazare';
  styles?: string[]; mood?: string[]; colorsLike?: string; colorsAvoid?: string; avoidMaterials?: string[];
  rooms?: string[]; priorityRoom?: string; priorities?: Priority[]; storage?: string[]; keepItems?: string;
  budgetRange?: BudgetRange | typeof UNSURE; budgetIncludes?: string[]; suppliers?: string[]; deadline?: '1_luna' | '3_luni' | '6_luni' | 'flexibil';
  accessibility?: boolean; dislikes?: string; notes?: string }

/** Text liber de la client: fără caractere de control, fără marcaje de direcție, fără < și >; pe un rând dacă nu e câmp lung. */
export const cleanText = (v: string, max: number, long = false) => clean(long ? v : v.replace(/\s+/g, ' '), max * 4).replace(/[<>]/g, '').replace(/\n{3,}/g, '\n\n').trim();

/** Validarea strictă a documentului întreg. Cheile necunoscute, valorile din afara listelor și textele prea lungi sunt
 *  respinse (400), nu corectate tacit. `partial` = ciornă (orice întrebare poate lipsi); altfel se cer cele obligatorii.
 *  O valoare goală (null, "", []) înseamnă „fără răspuns” și nu se păstrează. */
export function checkClientBrief(x: unknown, opts: { partial?: boolean } = {}): ClientBrief {
  const bad = (m: string): never => { throw new ClientBriefError(m); };
  if (!x || typeof x !== 'object' || Array.isArray(x)) bad('Chestionarul este invalid.');
  const o = x as Record<string, unknown>, out: Record<string, unknown> = { v: 1 };
  if (o.v !== undefined && o.v !== 1) bad('Versiune de chestionar necunoscută.');
  for (const k of Object.keys(o)) if (k !== 'v' && !BY_KEY.has(k)) bad(`Câmp necunoscut: ${String(k).slice(0, 40)}.`);
  for (const q of QUESTIONS as readonly Question[]){ const v = o[q.key]; if (v === undefined || v === null) continue;
    const opts2 = q.options as readonly string[] | undefined;
    if (q.kind === 'enum'){ if (typeof v !== 'string' || !opts2!.includes(v)) bad(`Răspuns necunoscut la „${q.key}”.`); out[q.key] = v; }
    else if (q.kind === 'multi'){
      if (!Array.isArray(v) || !v.every(s => typeof s === 'string' && opts2!.includes(s)) || new Set(v).size !== v.length) bad(`Răspuns necunoscut la „${q.key}”.`);
      const a = v as string[]; if (a.length > (q.max ?? opts2!.length)) bad(`Prea multe opțiuni la „${q.key}” (cel mult ${q.max}).`);
      if (a.includes(UNSURE) && a.length > 1) bad(`„Nu știu încă” nu se combină cu alte opțiuni („${q.key}”).`);
      if (a.length) out[q.key] = q.ordered ? [...a] : opts2!.filter(s => a.includes(s)); }   // ordinea din listă; la stiluri ordinea aleasă (primul = preferatul)
    else if (q.kind === 'int'){ if (typeof v !== 'number' || !Number.isInteger(v) || v < q.min! || v > q.max!) bad(`„${q.key}” trebuie să fie un număr între ${q.min} și ${q.max}.`); out[q.key] = v; }
    else if (q.kind === 'bool'){ if (typeof v !== 'boolean') bad(`Răspuns necunoscut la „${q.key}”.`); out[q.key] = v; }
    else { if (typeof v !== 'string') bad(`Răspuns necunoscut la „${q.key}”.`); const s = cleanText(v as string, q.max!, q.long);
      if (s.length > q.max!) bad(`Textul de la „${q.key}” e prea lung (cel mult ${q.max} de caractere).`); if (s) out[q.key] = s; }
  }
  // întrebări condiționate: vârstele copiilor au sens doar cu copii (altfel se golesc); camera prioritară e una dintre camerele bifate
  if (!((out.children as number | undefined) ?? 0)) delete out.childAges;
  if (out.priorityRoom !== undefined && out.priorityRoom !== UNSURE && out.rooms !== undefined && !(out.rooms as string[]).includes(out.priorityRoom as string)) bad('Camera cea mai importantă trebuie să fie una dintre camerele alese.');
  if (!opts.partial){ const missing = (QUESTIONS as readonly Question[]).filter(q => q.required && out[q.key] === undefined).map(q => q.key);
    if (missing.length) throw Object.assign(new ClientBriefError('Mai sunt întrebări obligatorii fără răspuns.'), { missing }); }
  return out as unknown as ClientBrief;
}
/** Întrebările obligatorii rămase fără răspuns (pentru formular). */
export const missingRequired = (b: Partial<ClientBrief>) => (QUESTIONS as readonly Question[]).filter(q => q.required && (b as any)[q.key] === undefined).map(q => q.key);

// ---------- bugetul ----------
/** Intervalele de buget sunt în moneda proiectului, aceeași cu DesignBrief.budget (bugetul total din brief, comparat cu
 *  devizul). Cheile (`sub_5k`, `5_10k`…) sunt doar identificatori: pragurile în mii, pe monedă. La proiectele în lei
 *  pragurile sunt de 5 ori mai mari (25, 50, 100, 200 de mii de lei), ca intervalele să acopere amenajările obișnuite;
 *  nu e un curs valutar și nu se face nicio conversie. Etichetele din formular arată sumele și moneda. */
const BUDGET_K: Record<BudgetRange, [number, number | null]> = { sub_5k: [0, 5], '5_10k': [5, 10], '10_20k': [10, 20], '20_40k': [20, 40], peste_40k: [40, null] };
export const budgetScale = (currency: string) => currency === 'RON' ? 5000 : 1000;
export function budgetBounds(r: BudgetRange, currency = 'RON'): { min: number; max: number | null } { const [a, b] = BUDGET_K[r], k = budgetScale(currency); return { min: a * k, max: b == null ? null : b * k }; }
/** Suma pusă în brief pentru un interval: mijlocul lui; pentru „peste …” pragul de jos (singura sumă pe care clientul a
 *  spus-o); „nu știu încă” nu dă nicio sumă. */
export function budgetAmount(r: ClientBrief['budgetRange'], currency = 'RON'): number | null {
  if (!r || r === UNSURE) return null; const { min, max } = budgetBounds(r, currency); return max == null ? min : (min + max) / 2; }

// ---------- „Preia în brief” ----------
/** Cuvintele puse în `avoid` (filtru pe numele produselor, core/proposer-rules.ts): fixe, alese de noi, niciodată text de la client. */
const AVOID_WORD: Record<string, string> = { sticla: 'sticlă', metal: 'metal', negru: 'negru', lucios: 'lucios', piele: 'piele' };
export const BRIEF_NOTES_MAX = 600;
export interface BriefField { key: keyof DesignBrief; current: unknown; proposed: unknown; preselect: boolean }
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);
/** Ce ar schimba răspunsurile clientului în brief-ul designerului. Întoarce brief-ul propus întreg și lista câmpurilor
 *  care diferă (actual → propus). `preselect` = câmpul poate fi bifat din start: designerul nu a pus acolo altceva decât
 *  valoarea implicită. Ce nu a răspuns clientul (sau „nu știu încă”) nu atinge câmpul. */
export function briefFromClient(client: Partial<ClientBrief>, current: DesignBrief, currency = 'RON'): { proposed: DesignBrief; fields: BriefField[] } {
  const p: Partial<DesignBrief> = {};
  if (client.propertyType) p.propertyType = client.propertyType;
  const style = client.styles?.find(s => s !== UNSURE); if (style && style in STYLES) p.style = style as Style;
  if (client.adults != null) p.occupants = Math.max(1, Math.min(12, client.adults + (client.children ?? 0)));
  if (client.children != null) p.children = client.children > 0;
  if (client.pets?.length) p.pets = true;
  if (client.accessibility != null) p.accessibility = client.accessibility;
  { const add = new Set<string>(client.priorities ?? []); if (client.wfh === 'zilnic') add.add('birou'); if (client.guests === 'des' || client.guests === 'des_cu_cazare') add.add('musafiri');
    if (add.size) p.priorities = (Object.keys(PRIORITIES) as Priority[]).filter(k => add.has(k) || current.priorities.includes(k)); }
  { const b = budgetAmount(client.budgetRange, currency); if (b != null) p.budget = b; }
  if (client.colorsLike) p.colors = client.colorsLike.slice(0, 120);
  if (client.avoidMaterials?.length) p.avoid = client.avoidMaterials.map(m => AVOID_WORD[m]).filter(Boolean).join(', ');
  // doar magazinele din catalog; „la comandă” ajunge în note. Fără niciun magazin din catalog rămân cele actuale (brief-ul cere cel puțin unul)
  { const s = (client.suppliers ?? []).filter((x): x is 'IKEA' | 'Dedeman' => x === 'IKEA' || x === 'Dedeman'); if (s.length) p.suppliers = s; }
  { const parts = [client.dislikes && `Nu îi place: ${client.dislikes}`, client.colorsAvoid && `Culori de evitat: ${client.colorsAvoid}`, client.keepItems && `Păstrează: ${client.keepItems}`,
      client.suppliers?.includes('la_comanda') && 'Acceptă mobilier la comandă', client.notes && `Alte detalii: ${client.notes}`].filter(Boolean) as string[];
    if (parts.length){ const s = parts.map(x => x.replace(/\s+/g, ' ').replace(/[.\s]+$/, '')).join('. ') + '.';
      p.notes = s.length > BRIEF_NOTES_MAX ? s.slice(0, BRIEF_NOTES_MAX - 1).trimEnd() + '…' : s; } }
  const fields: BriefField[] = [];
  for (const key of Object.keys(DEFAULT_BRIEF) as (keyof DesignBrief)[]) if (key in p && !same(p[key], current[key])) fields.push({ key, current: current[key], proposed: p[key], preselect: same(current[key], DEFAULT_BRIEF[key]) });
  return { proposed: { ...current, ...Object.fromEntries(fields.map(f => [f.key, f.proposed])) } as DesignBrief, fields };
}

// ---------- pozele: JPEG reconstruit pe listă albă ----------
export type JpegErrorCode = 'not_jpeg' | 'corrupt' | 'truncated' | 'segment' | 'unsupported' | 'components' | 'precision' | 'dimensions' | 'too_large';
export class JpegError extends Error { status = 400; constructor(public code: JpegErrorCode, msg: string){ super(msg); } }
export const JPEG_LIMITS = { side: 4096, pixels: 16_000_000, scans: 100 };
const M = { SOI: 0xd8, EOI: 0xd9, SOS: 0xda, DQT: 0xdb, DNL: 0xdc, DRI: 0xdd, DHT: 0xc4, APP0: 0xe0, APP14: 0xee, TEM: 0x01 };
/** Reconstruiește un JPEG doar din segmentele necesare afișării: SOI, APP0 „JFIF” fără miniatură, DQT, DHT, un singur
 *  SOF0/SOF1/SOF2, DRI, scanările (SOS + datele lor) și EOI. Tot restul dispare: EXIF (cu GPS), XMP, ICC, MPF, Adobe,
 *  comentarii, miniaturi, octeții de umplere și orice se află după primul EOI. Ce nu e un JPEG obișnuit pe 8 biți, gri sau
 *  color (CMYK, 12 biți, aritmetic, fără pierderi, ierarhic, cu DNL), sau e trunchiat ori prea mare, e refuzat cu JpegError.
 *  Atenție: orientarea din EXIF dispare și ea — browserul o aplică la micșorare (lib/image-resize.ts), înainte de trimitere. */
export function stripJpeg(b: Uint8Array): { data: Uint8Array; width: number; height: number } {
  const bad = (code: JpegErrorCode, msg: string): never => { throw new JpegError(code, msg); };
  const truncated = () => bad('truncated', 'Imaginea este incompletă.');
  if (!(b instanceof Uint8Array) || b.length < 4 || b[0] !== 0xff || b[1] !== M.SOI) bad('not_jpeg', 'Fișierul nu este o imagine JPEG.');
  const out: Uint8Array[] = [Uint8Array.of(0xff, M.SOI)];
  const keep = (m: number, body: Uint8Array) => { out.push(Uint8Array.of(0xff, m), body); };
  let p = 2, width = 0, height = 0, comps = 0, sof = false, jfif = false, scans = 0, eoi = false, adobeTransform = -1;
  while (p < b.length){
    if (b[p] !== 0xff) bad('corrupt', 'Imaginea este deteriorată.');
    while (p < b.length && b[p] === 0xff) p++;            // FF FF …: octeți de umplere înaintea codului
    if (p >= b.length) truncated();
    const m = b[p++]!;
    if (m === M.EOI){ eoi = true; break; }               // tot ce urmează după primul EOI se taie
    if (m === M.TEM || (m >= 0xd0 && m <= 0xd7)) continue; // fără lungime; în afara unei scanări nu au rost
    if (m === 0x00 || m === M.SOI) bad('corrupt', 'Imaginea este deteriorată.');
    if (p + 2 > b.length) truncated();
    const len = (b[p]! << 8) | b[p + 1]!;
    if (len < 2) bad('segment', 'Imaginea are un segment cu lungime invalidă.');
    if (p + len > b.length) bad('segment', 'Imaginea are un segment care depășește fișierul.');
    const body = b.subarray(p, p + len); p += len;
    if (m === 0xc0 || m === 0xc1 || m === 0xc2){
      if (sof) bad('unsupported', 'Imaginea are mai multe cadre; acest tip de JPEG nu este acceptat.');
      if (len < 8) bad('segment', 'Imaginea are un antet invalid.');
      if (body[2] !== 8) bad('precision', 'Sunt acceptate doar imagini JPEG pe 8 biți.');
      height = (body[3]! << 8) | body[4]!; width = (body[5]! << 8) | body[6]!; comps = body[7]!;
      if (comps !== 1 && comps !== 3) bad('components', 'Sunt acceptate doar imagini JPEG gri sau color (nu CMYK).');
      if (len !== 8 + 3 * comps) bad('segment', 'Imaginea are un antet invalid.');
      if (!width || !height) bad('dimensions', 'Imaginea nu își declară dimensiunile.');
      if (width > JPEG_LIMITS.side || height > JPEG_LIMITS.side || width * height > JPEG_LIMITS.pixels) bad('too_large', `Imaginea e prea mare (cel mult ${JPEG_LIMITS.side} px pe latură).`);
      sof = true; keep(m, body); continue; }
    if ((m >= 0xc0 && m <= 0xcf && m !== M.DHT) || m === M.DNL || m === 0xde || m === 0xdf) bad('unsupported', 'Acest tip de JPEG (aritmetic, fără pierderi sau ierarhic) nu este acceptat.');
    if (m === M.DHT || m === M.DQT){ keep(m, body); continue; }
    if (m === M.DRI){ if (len !== 4) bad('segment', 'Imaginea are un segment cu lungime invalidă.'); keep(m, body); continue; }
    if (m === M.APP0){   // doar antetul JFIF simplu: 16 octeți, fără miniatură, o singură dată, înaintea cadrului
      if (!jfif && !sof && len === 16 && body[2] === 0x4a && body[3] === 0x46 && body[4] === 0x49 && body[5] === 0x46 && body[6] === 0 && body[14] === 0 && body[15] === 0){ jfif = true; keep(m, body); }
      continue; }
    if (m === M.APP14 && len >= 14 && body[2] === 0x41 && body[3] === 0x64 && body[4] === 0x6f && body[5] === 0x62 && body[6] === 0x65) adobeTransform = body[13]!;
    if ((m >= 0xe0 && m <= 0xef) || m === 0xfe) continue;   // APPn (EXIF, XMP, ICC, MPF, Adobe…) și comentarii: aruncate
    if (m === M.SOS){
      if (!sof) bad('corrupt', 'Imaginea este deteriorată.');
      const ns = body[2] ?? 0; if (ns < 1 || ns > comps || len !== 6 + 2 * ns) bad('segment', 'Imaginea are un antet de scanare invalid.');
      if (++scans > JPEG_LIMITS.scans) bad('unsupported', 'Imaginea are prea multe scanări.');
      // datele comprimate: până la primul FF urmat (după eventuali FF de umplere) de altceva decât 00 (octet FF din date) sau RST0–7
      let i = p; for (;;){ while (i < b.length && b[i] !== 0xff) i++;
        let j = i + 1; while (j < b.length && b[j] === 0xff) j++;   // umplere înaintea codului
        if (j >= b.length) truncated();
        const n = b[j]!; if (n === 0x00 || (n >= 0xd0 && n <= 0xd7)){ i = j + 1; continue; } break; }   // alt cod: scanarea se termină la primul FF
      keep(m, body); out.push(b.subarray(p, i)); p = i; continue; }
    bad('corrupt', 'Imaginea este deteriorată.');
  }
  if (!eoi) truncated();
  if (!sof || !scans) bad('corrupt', 'Imaginea este deteriorată.');
  // fără marcajul Adobe, o imagine cu 3 componente e citită ca YCbCr: una declarată RGB direct ar ieși cu alte culori
  if (comps === 3 && adobeTransform === 0) bad('unsupported', 'Acest tip de JPEG (RGB fără conversie) nu este acceptat.');
  out.push(Uint8Array.of(0xff, M.EOI));
  const data = new Uint8Array(out.reduce((n, x) => n + x.length, 0)); let o = 0; for (const x of out){ data.set(x, o); o += x.length; }
  return { data, width, height };
}
