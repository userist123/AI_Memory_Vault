// Verificare non-distructivă de coordonare a proiectului (Secțiunea 14.8):
// Clasifică fiecare constatare în ERROR / WARNING / INFO / UNKNOWN cu ID-urile obiectelor afectate și sursa regulii,
// fără a pretinde conformitate legală certificată pentru instalații sau structură.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { validateFloor, validatePlacement, issueTarget } from './validate';
import { roomCeilingDropCm } from './finishes';
import { floors } from './levels';
import { buildTechnicalSchedule } from './technical';
import { resolve } from './catalog';
import { detectClashes, solidsOf, CLASH_RULES, CLEARANCE_RULE, type ClashOptions } from './clash';
import { mepIssues } from './mep';

export type CoordinationSeverity = 'ERROR' | 'WARNING' | 'INFO' | 'UNKNOWN';

/** id: stabil între rulări pentru același cod, aceeași regulă și aceleași elemente (findingId), ca starea unei constatări
 *  (acceptată, rezolvată, fals pozitiv) să poată fi legată de ea în faza de issue-uri (docs/bim/FAZA0_INVENTAR.md). */
export interface CoordinationFinding {
  id: string;
  code: string;
  severity: CoordinationSeverity;
  refs: string[];
  message: string;
  rule: string;
  /** același text în limba interfeței (lib/locales): cheie + variabile; variabila `a`/`b`/`el` = id de element, rezolvat la nume de panou */
  key?: string;
  vars?: Record<string, string | number>;
}

export function reviewProjectCoordination(snap: Snapshot, cat: Catalog, _mc?: MaterialsCatalog): CoordinationFinding[] {
  const findings: Omit<CoordinationFinding, 'id'>[] = [];
  const allFloors = floors(snap);

  // 1. Geometria pereților, golurilor și plasărilor pe fiecare nivel
  for (const fl of allFloors) {
    for (const iss of validateFloor(fl, id => roomCeilingDropCm(snap, id))) {
      const tg = issueTarget(iss, fl, snap.placements);
      findings.push({
        code: iss.code,
        severity: iss.severity === 'ERROR' ? 'ERROR' : 'WARNING',
        refs: tg ? [tg.id] : [],
        message: iss.message,
        rule: 'casa3d.geometry.floor-validator',
        ...(iss.key ? { key: iss.key, vars: iss.vars ?? {} } : {}),
      });
    }
  }
  for (const pl of snap.placements) {
    for (const iss of validatePlacement(snap, cat, pl)) {
      findings.push({
        code: iss.code,
        severity: iss.severity === 'ERROR' ? 'ERROR' : 'WARNING',
        refs: iss.with ? [pl.id, iss.with] : [pl.id],
        message: iss.message,
        rule: 'casa3d.geometry.placement-validator',
        ...(iss.key ? { key: iss.key, vars: iss.vars ?? {} } : {}),
      });
    }
    const rv = resolve(cat, pl.variantId);
    if (!rv?.offer || pl.size) {
      findings.push({
        code: 'PRICE_UNKNOWN',
        severity: 'UNKNOWN',
        refs: [pl.id],
        message: `Piesa ${pl.id} (${pl.variantId}) nu are preț verificat în catalog sau are dimensiuni personalizate.`,
        rule: 'casa3d.boq.no-fabricated-prices', key: 'coord.f.priceUnknown', vars: { el: pl.id },
      });
    }
  }

  // 2. Scara releveului neconfirmată
  if (snap.underlay && snap.underlay.confirmed === false) {
    findings.push({
      code: 'UNDERLAY_SCALE_UNCONFIRMED',
      severity: 'WARNING',
      refs: [],
      message: 'Imaginea de releveu (underlay) este încărcată, dar scara nu a fost confirmată din două puncte cunoscute.',
      rule: 'casa3d.underlay.scale-confirmation', key: 'coord.f.underlayScale', vars: {},
    });
  }

  // 3. Biblioteca utilizatorului / modele GLB importate fără dimensiuni, sursă sau licență
  for (const item of snap.userLibrary ?? []) {
    if (!item.measurable || !item.source || !item.license) {
      findings.push({
        code: 'ASSET_PROVENANCE_OR_SCALE_UNKNOWN',
        severity: 'UNKNOWN',
        refs: [item.id],
        message: `Componenta "${item.name}" (${item.id}) nu are scară/sursă/licență confirmată și este exclusă din calculul automat BOQ.`,
        rule: 'casa3d.library.asset-governance', key: 'coord.f.asset', vars: { name: item.name },
      });
    }
  }

  // 4. Coordonare instalații conceptuale (electrice, iluminat, sanitare)
  if (snap.tech && snap.tech.length > 0) {
    const sched = buildTechnicalSchedule(snap);
    for (const swId of sched.unlinkedSwitches) {
      findings.push({
        code: 'TECH_SWITCH_UNLINKED',
        severity: 'WARNING',
        refs: [swId],
        message: `Întrerupătorul ${swId} nu este asociat niciunui grup de iluminat.`,
        rule: 'casa3d.technical.switch-group-check', key: 'coord.f.switchUnlinked', vars: { el: swId },
      });
    }
    const sanitaryGroups = new Set(['lavoar', 'wc', 'dus', 'cada', 'bideu']);
    for (const pl of snap.placements.filter(p => sanitaryGroups.has(p.group))) {
      const roomPts = snap.tech.filter(t => t.roomId === pl.roomId);
      const hasWater = roomPts.some(t => t.kind === 'water_cold' || t.kind === 'water_hot');
      const hasDrain = roomPts.some(t => t.kind === 'drain');
      if (!hasWater || !hasDrain) {
        findings.push({
          code: 'TECH_SANITARY_UNCONNECTED',
          severity: 'WARNING',
          refs: [pl.id, pl.roomId],
          message: `Obiectul sanitar ${pl.group} (${pl.id}) din camera ${pl.roomId} nu are racorduri complete de apă și scurgere pe stratul tehnic.`,
          rule: 'casa3d.technical.sanitary-service-points', key: 'coord.f.sanitary', vars: { el: pl.id, room: pl.roomId },
        });
      }
    }
    findings.push({
      code: 'PRELIMINARY_TECHNICAL_PLANS',
      severity: 'INFO',
      refs: [],
      message: sched.disclaimer,
      rule: 'casa3d.technical.competence-boundary', key: 'coord.f.preliminary', vars: {},
    });
  }

  // 5. Camere fără finisaje explicit alese
  for (const fl of allFloors) {
    for (const r of fl.rooms) {
      if (!snap.finishes?.[r.id]) {
        findings.push({
          code: 'ROOM_DEFAULT_FINISHES',
          severity: 'INFO',
          refs: [r.id],
          message: `Camera "${r.name}" folosește finisajele implicite orientative (nu au fost alese încă finisaje dedicate).`,
          rule: 'casa3d.finishes.explicit-selection', key: 'coord.f.defaultFinishes', vars: { room: r.id },
        });
      }
    }
  }

  return withIds(findings);
}

/** FNV-1a pe 32 de biți: determinist, fără dependențe, același în browser și pe server. */
function fnv1a(s: string): string {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; }
  return h.toString(16).padStart(8, '0');
}
/** Id-ul unei constatări: din regulă, cod și elementele implicate (ordinea lor nu contează). Mesajul intră doar când constatarea
 *  nu are elemente (altfel două constatări diferite fără țintă, de pe niveluri diferite, ar primi același id). */
export function findingId(code: string, rule: string, refs: string[], message = ''): string {
  const who = refs.length ? [...refs].sort().join(',') : 'msg:' + message.trim().replace(/\s+/g, ' ');
  return 'cf-' + fnv1a(`${rule}|${code}|${who}`);
}
function withIds(raw: Omit<CoordinationFinding, 'id'>[]): CoordinationFinding[] {
  const seen = new Set<string>(), out: CoordinationFinding[] = [];
  for (const f of raw) { const id = findingId(f.code, f.rule, f.refs, f.message); if (seen.has(id)) continue; seen.add(id); out.push({ id, ...f }); }
  return out;
}

/** Regulile pe care reviewProjectCoordination le poate rula. Cele tehnice rulează doar dacă proiectul are strat tehnic. */
export const COORDINATION_RULES = ['casa3d.geometry.floor-validator', 'casa3d.geometry.placement-validator', 'casa3d.boq.no-fabricated-prices',
  'casa3d.underlay.scale-confirmation', 'casa3d.library.asset-governance', 'casa3d.finishes.explicit-selection'] as const;
export const TECHNICAL_RULES = ['casa3d.technical.switch-group-check', 'casa3d.technical.sanitary-service-points', 'casa3d.technical.competence-boundary'] as const;

export interface CoordinationRun {
  revision: number | 'draft';
  findings: CoordinationFinding[];
  counts: Record<CoordinationSeverity, number>;
  rulesChecked: string[];
  rulesNotChecked: string[];
  /** fraza de raport: ce s-a verificat, la ce revizie, și limita ei (ghidul BIM §3.7: „zero” doar cu domeniul testat) */
  statement: string;
}

/** Rularea cu domeniul ei: revizia verificată, regulile rulate și cele nerulate, numărul pe severitate. */
export function runCoordination(snap: Snapshot, cat: Catalog, opts: { revision?: number; mc?: MaterialsCatalog; clash?: ClashOptions } = {}): CoordinationRun {
  const findings = reviewProjectCoordination(snap, cat, opts.mc);
  const hasTech = !!snap.tech && snap.tech.length > 0, hasClearance = !!opts.clash?.clearances?.length;
  // interferențele 3D (core/clash.ts) și proprietățile declarate ale traseelor (core/mep.ts), cu id-urile lor stabile
  const seen = new Set(findings.map(f => f.id));
  const push = (f: CoordinationFinding) => { if (!seen.has(f.id)) { seen.add(f.id); findings.push(f); } };
  for (const c of detectClashes(snap, cat, opts.clash))
    push({ id: c.id, code: c.type === 'hard' ? 'CLASH_HARD' : c.type === 'penetration' ? 'WALL_PENETRATION' : 'CLASH_CLEARANCE', severity: c.severity, refs: [...c.refs], message: c.message, rule: c.rule,
      key: `coord.f.${c.type}`, vars: { a: c.refs[0], b: c.refs[1], mm: Math.round((c.depthM ?? c.distanceM ?? 0) * 1000), tol: Math.round(c.toleranceM * 1000) } });
  for (const id of solidsOf(snap, cat).skipped.map(x => x.id))
    push({ id: findingId('CLASH_SIZE_UNKNOWN', 'casa3d.clash.inputs', [id]), code: 'CLASH_SIZE_UNKNOWN', severity: 'UNKNOWN', refs: [id], rule: 'casa3d.clash.inputs',
      message: `Piesa ${id} nu are dimensiuni cunoscute și nu a intrat în verificarea interferențelor.`, key: 'coord.f.sizeUnknown', vars: { el: id } });
  for (const fl of floors(snap)) for (const m of mepIssues(fl)) push({ id: findingId(m.code, 'casa3d.mep.declared-properties', m.refs), code: m.code, severity: m.severity, refs: m.refs, message: m.message, rule: 'casa3d.mep.declared-properties', key: m.key, vars: m.vars });
  const rulesChecked: string[] = [...COORDINATION_RULES, ...(hasTech ? TECHNICAL_RULES : []), ...CLASH_RULES, 'casa3d.clash.inputs', 'casa3d.mep.declared-properties', ...(hasClearance ? [CLEARANCE_RULE] : [])];
  const rulesNotChecked: string[] = [...(hasTech ? [] : TECHNICAL_RULES), ...(hasClearance ? [] : [CLEARANCE_RULE])];
  const counts: Record<CoordinationSeverity, number> = { ERROR: 0, WARNING: 0, INFO: 0, UNKNOWN: 0 };
  for (const f of findings) counts[f.severity]++;
  const revision = opts.revision ?? 'draft';
  const where = revision === 'draft' ? 'proiectul curent (nesalvat ca revizie)' : `revizia ${revision}`;
  const statement = `${counts.ERROR} erori, ${counts.WARNING} avertismente, ${counts.UNKNOWN} necunoscute pentru ${rulesChecked.length} reguli la ${where}`
    + (rulesNotChecked.length ? `; nerulate: ${rulesNotChecked.join(', ')}` : '')
    + '. Verificare asistivă: nu înseamnă conformitate cu normativele și nu înlocuiește verificarea unui specialist autorizat.';
  return { revision, findings, counts, rulesChecked, rulesNotChecked, statement };
}