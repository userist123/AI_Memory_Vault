// Verificare non-distructivă de coordonare a proiectului (Secțiunea 14.8):
// Clasifică fiecare constatare în ERROR / WARNING / INFO / UNKNOWN cu ID-urile obiectelor afectate și sursa regulii,
// fără a pretinde conformitate legală certificată pentru instalații sau structură.
import type { Catalog, MaterialsCatalog, Snapshot } from './types';
import { validateFloor, validatePlacement, issueTarget } from './validate';
import { floors } from './levels';
import { buildTechnicalSchedule } from './technical';
import { resolve } from './catalog';

export type CoordinationSeverity = 'ERROR' | 'WARNING' | 'INFO' | 'UNKNOWN';

export interface CoordinationFinding {
  code: string;
  severity: CoordinationSeverity;
  refs: string[];
  message: string;
  rule: string;
}

export function reviewProjectCoordination(snap: Snapshot, cat: Catalog, _mc?: MaterialsCatalog): CoordinationFinding[] {
  const findings: CoordinationFinding[] = [];
  const allFloors = floors(snap);

  // 1. Geometria pereților, golurilor și plasărilor pe fiecare nivel
  for (const fl of allFloors) {
    for (const iss of validateFloor(fl)) {
      const tg = issueTarget(iss, fl, snap.placements);
      findings.push({
        code: iss.code,
        severity: iss.severity === 'ERROR' ? 'ERROR' : 'WARNING',
        refs: tg ? [tg.id] : [],
        message: iss.message,
        rule: 'casa3d.geometry.floor-validator',
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
      });
    }
    const rv = resolve(cat, pl.variantId);
    if (!rv?.offer || pl.size) {
      findings.push({
        code: 'PRICE_UNKNOWN',
        severity: 'UNKNOWN',
        refs: [pl.id],
        message: `Piesa ${pl.id} (${pl.variantId}) nu are preț verificat în catalog sau are dimensiuni personalizate.`,
        rule: 'casa3d.boq.no-fabricated-prices',
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
      rule: 'casa3d.underlay.scale-confirmation',
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
        rule: 'casa3d.library.asset-governance',
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
        rule: 'casa3d.technical.switch-group-check',
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
          rule: 'casa3d.technical.sanitary-service-points',
        });
      }
    }
    findings.push({
      code: 'PRELIMINARY_TECHNICAL_PLANS',
      severity: 'INFO',
      refs: [],
      message: sched.disclaimer,
      rule: 'casa3d.technical.competence-boundary',
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
          rule: 'casa3d.finishes.explicit-selection',
        });
      }
    }
  }

  return findings;
}
