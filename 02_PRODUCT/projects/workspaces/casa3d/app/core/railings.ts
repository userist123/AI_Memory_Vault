// Balustrade (Floor.railings) și tipul spațiului camerei (Room.spaceKind: balcon, terasă). Funcții pure: valorile implicite,
// lungimea pentru deviz, avertismentul de înălțime și ce desenează motorul 3D (sticla refolosește pereții de sticlă).
import type { Floor, Issue, Railing, RailingKind, Room, RoomSpaceKind } from './types';
import { PLAN_DEFAULTS, PLAN_LIMITS } from './plan-limits';

const r2 = (v: number) => Math.round(v * 100) / 100;
export const railingHeight = (r: Pick<Railing, 'height'>): number => r.height ?? PLAN_DEFAULTS.railing.height;
export const railingKind = (r: Pick<Railing, 'kind'>): RailingKind => r.kind ?? 'metal';
export const railingLength = (r: Pick<Railing, 'a' | 'b'>): number => r2(Math.hypot(r.b[0] - r.a[0], r.b[1] - r.a[1]));
/** Tipul spațiului: lipsă = interior (proiectele de până acum). */
export const spaceKindOf = (room: Pick<Room, 'spaceKind'>): RoomSpaceKind => room.spaceKind ?? 'interior';
/** Spațiu deschis spre exterior (balcon deschis, terasă, curte): cere balustradă dacă e la înălțime. */
export const isOpenSpace = (room: Pick<Room, 'spaceKind'>) => { const k = spaceKindOf(room); return k === 'open_balcony' || k === 'terrace' || k === 'outdoor'; };
/** RAILING_LOW: balustrada sub 90 cm (înălțimea minimă uzuală la balcoane și terase; verifică normativul pentru clădirea ta). */
export function railingIssues(fl: Pick<Floor, 'railings'>): Issue[] {
  return (fl.railings ?? []).filter(r => railingHeight(r) < PLAN_LIMITS.railing.minSafeM - 1e-9).map(r => ({ code: 'RAILING_LOW' as const, severity: 'WARNING' as const,
    message: `Balustrada ${r.id} are ${Math.round(railingHeight(r) * 100)} cm: sub ${PLAN_LIMITS.railing.minSafeM * 100} cm nu protejează la cădere (verifică normativul).`,
    key: 'issue.RAILING_LOW', vars: { rail: r.id, h_m: railingHeight(r), min_m: PLAN_LIMITS.railing.minSafeM } }));
}
/** Pasul montanților (m): metal = stâlpi la cel mult 1,2 m; sticla = panouri de cel mult 1,2 m. */
export const RAILING_POST_M = 1.2;
/** Intrarea motorului 3D: balustradele de sticlă devin pereți de sticlă cu înălțimea balustradei (aceeași construcție: panou,
 *  profile, montanți), cele de metal o listă separată (mână curentă, stâlpi, baluștri la 11 cm). */
export function railingVisual(fl: Pick<Floor, 'railings'>): { glass: { id: string; a: [number, number]; b: [number, number]; thickness: number; mullions: number; frameColor: string; h: number; rail: true }[]; metal: { id: string; a: [number, number]; b: [number, number]; h: number; posts: number }[] } {
  const glass: ReturnType<typeof railingVisual>['glass'] = [], metal: ReturnType<typeof railingVisual>['metal'] = [];
  for (const r of fl.railings ?? []){ const L = railingLength(r); if (L < .05) continue; const posts = Math.max(1, Math.ceil(L / RAILING_POST_M - 1e-9)), h = railingHeight(r);
    if (railingKind(r) === 'glass') glass.push({ id: r.id, a: [r.a[0], r.a[1]], b: [r.b[0], r.b[1]], thickness: .05, mullions: posts, frameColor: '#3a3d42', h, rail: true });
    else metal.push({ id: r.id, a: [r.a[0], r.a[1]], b: [r.b[0], r.b[1]], h, posts }); }
  return { glass, metal };
}
