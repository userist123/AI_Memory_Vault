// Selecția multiplă fără mouse: atingerea lungă pe ecranul tactil și tastele din plan / din lista Structură.
// Totul e pur (fără React, fără DOM): PlanView, Outliner și editorul aplică rezultatele. Grupul are aceeași convenție ca în
// core/arrange.ts: o listă de id-uri de piese, cu piesa principală pe primul loc.
import { toggleMulti } from './arrange';

/** Atingerea lungă: degetul stă cel puțin atât pe loc… */
export const LONG_PRESS_MS = 500;
/** …fără să se miște mai mult de atât (pixeli de ecran); peste prag e tragere sau panoramare. */
export const LONG_PRESS_MOVE_PX = 8;

export interface PressPoint { x: number; y: number }
/** Cât s-a depărtat degetul de locul atingerii, în pixeli de ecran. */
export const pressTravel = (a: PressPoint, b: PressPoint): number => Math.hypot(b.x - a.x, b.y - a.y);

/**
 * E atingere lungă? `downAt` și `now` sunt în milisecunde (același ceas), `movedPx` e cea mai mare depărtare de locul
 * atingerii. Valorile care nu sunt numere finite (ceas lipsă, coordonate lipsă) nu dau niciodată atingere lungă.
 */
export function isLongPress(downAt: number, now: number, movedPx: number, ms = LONG_PRESS_MS, slopPx = LONG_PRESS_MOVE_PX): boolean {
  if (!Number.isFinite(downAt) || !Number.isFinite(now) || !Number.isFinite(movedPx)) return false;
  return now - downAt >= ms && movedPx <= slopPx;
}

export type LongPressAction = 'toggle' | 'marquee' | null;
/** Peste ce se poate porni dreptunghiul de selecție: golul planului, o cameră, o zonă sau planul de fundal. */
export const MARQUEE_OVER: ReadonlySet<string> = new Set(['room', 'floor_zone', 'ceiling_zone', 'underlay']);
/**
 * Ce face atingerea lungă peste un element al planului (`kind` = data-k; null = gol): pe o piesă o adaugă în grup sau o
 * scoate, pe gol (sau peste o cameră / zonă) pornește dreptunghiul de selecție. Pereții, golurile și mânerele își
 * păstrează gestul (meniul contextual al apăsării lungi).
 */
export function longPressAction(kind: string | null | undefined): LongPressAction {
  if (!kind) return 'marquee';
  return kind === 'placement' ? 'toggle' : MARQUEE_OVER.has(kind) ? 'marquee' : null;
}

/**
 * Adaugă / scoate o piesă din grup de la tastatură (Shift+Space în plan, Shift+Enter în Structură). Piesa principală
 * rămâne pe primul loc cât timp nu e ea cea scoasă; singura piesă aleasă nu se scoate (nu rămâi fără selecție dintr-o
 * tastă: Escape renunță). Grupul gol primește piesa ca principală.
 */
export function toggleInGroup(group: readonly string[], id: string): string[] {
  if (group.length === 1 && group[0] === id) return [id];
  return toggleMulti(group, id);
}

export interface GroupKeyIn { key: string; shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean; altKey?: boolean }
/** Shift+Space cu planul în focus: comută în grup obiectul curent. Fără alți modificatori, ca să nu prindă scurtături de sistem. */
export const isGroupToggleKey = (e: GroupKeyIn): boolean => (e.key === ' ' || e.key === 'Spacebar') && !!e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey;
/** Shift+Enter sau Shift+Space pe un rând din Structură: piesa intră în grup sau iese, fără să devină piesa principală. */
export const isListGroupToggleKey = (e: GroupKeyIn): boolean => (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') && !!e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey;

export type KeyCursor = { kind: string; id: string } | null;
/**
 * Shift+Space în plan pe obiectul curent (`cur` = cursorul tastaturii sau, fără el, selecția): grupul nou, sau `null`
 * când obiectul curent nu e o piesă (camerele, pereții, scările nu intră în grup).
 */
export function groupAfterKeyToggle(group: readonly string[], cur: KeyCursor): string[] | null {
  return cur?.kind === 'placement' ? toggleInGroup(group, cur.id) : null;
}
/**
 * Tab cu grupul „prins” (după primul Shift+Space): mută doar cursorul tastaturii, nu selecția, ca grupul să nu se piardă.
 * `pinned` = există cursor; fără el Tab alege direct, ca înainte.
 */
export const tabMovesCursor = (pinned: boolean, groupSize: number): boolean => pinned && groupSize >= 1;
