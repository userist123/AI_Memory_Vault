// Undo/redo pe instantanee complete ale proiectului (simplu și sigur pentru un proiect de câțiva zeci de KB).
export interface HistoryMark { readonly gen: number; readonly future: string[]; readonly dropped?: string }
export class History<T> {
  private past: string[] = []; private future: string[] = [];
  // bumped on every change to the stacks, so a mark knows whether its push is still the latest change
  private gen = 0;
  constructor(private limit = 100){}
  push(state: T): string | undefined { this.past.push(JSON.stringify(state)); const dropped = this.past.length > this.limit ? this.past.shift() : undefined;
    this.future = []; this.gen++; return dropped; }
  undo(current: T): T | null { const s = this.past.pop(); if (!s) return null; this.future.push(JSON.stringify(current)); this.gen++; return JSON.parse(s); }
  redo(current: T): T | null { const s = this.future.pop(); if (!s) return null; this.past.push(JSON.stringify(current)); this.gen++; return JSON.parse(s); }
  get canUndo(){ return this.past.length > 0; } get canRedo(){ return this.future.length > 0; }
  clear(){ this.past = []; this.future = []; this.gen++; }
  discardLast(){ this.past.pop(); this.gen++; }
  // A drag is a transaction: begin() pushes the start state and keeps the redo stack it clears; rollback() removes exactly
  // that push and restores redo, but only if nothing else touched the history since (undo, redo, another push, clear).
  begin(state: T): HistoryMark { const future = this.future, dropped = this.push(state); return { gen: this.gen, future, dropped }; }
  rollback(mark: HistoryMark): boolean { if (mark.gen !== this.gen) return false;
    this.past.pop(); if (mark.dropped !== undefined) this.past.unshift(mark.dropped); this.future = mark.future; this.gen++; return true; }
}
