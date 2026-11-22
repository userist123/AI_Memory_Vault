// Blocarea pieselor de mobilier (`locked: true`): o piesă blocată nu se mută, nu se rotește, nu se aliniază și nu se șterge
// până nu e deblocată — protecție la lucrul cu selecția multiplă și cu tragerea în plan. Copiile (dublare, repetare în rând)
// pornesc deblocate: blocarea e o alegere pe piesa existentă, nu o proprietate a modelului. Funcții pure.
import type { FurniturePlacement, Snapshot } from './types';

export const isLocked = (snap: Pick<Snapshot, 'placements'>, id: string) => snap.placements.some(p => p.id === id && p.locked === true);
/** Prima piesă blocată din listă (pentru mesaj), sau null. */
export const firstLocked = (snap: Pick<Snapshot, 'placements'>, ids: readonly string[]) => ids.find(id => isLocked(snap, id)) ?? null;
/** Comutarea pe un grup: dacă vreo piesă e deblocată, se blochează toate; altfel se deblochează toate. Întoarce starea nouă. */
export function toggleLock(snap: Snapshot, ids: readonly string[]): { snapshot: Snapshot; locked: boolean; count: number } {
  const n = structuredClone(snap), set = new Set(ids), ps = n.placements.filter(p => set.has(p.id)), lock = ps.some(p => p.locked !== true);
  for (const p of ps){ if (lock) p.locked = true; else delete p.locked; }
  return { snapshot: n, locked: lock, count: ps.length };
}
/** O copie a unei piese pornește deblocată (oglindirea și restul se păstrează). */
export function unlockedCopy<T extends FurniturePlacement>(p: T): T { const c = { ...p }; delete c.locked; return c; }
/** Camera are piese pe care amenajarea automată de la redimensionare nu are voie să le refacă: puse de mână sau blocate. */
export const hasPinnedPieces = (snap: Pick<Snapshot, 'placements'>, roomId: string) => snap.placements.some(p => p.roomId === roomId && (p.source === 'manual' || p.locked === true));
