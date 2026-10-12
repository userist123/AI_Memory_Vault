// Undo/redo pe instantanee complete ale proiectului (simplu și sigur pentru un proiect de câțiva zeci de KB).
export class History<T> {
  private past: string[] = []; private future: string[] = [];
  constructor(private limit = 100){}
  push(state: T){ this.past.push(JSON.stringify(state)); if (this.past.length > this.limit) this.past.shift(); this.future = []; }
  undo(current: T): T | null { const s = this.past.pop(); if (!s) return null; this.future.push(JSON.stringify(current)); return JSON.parse(s); }
  redo(current: T): T | null { const s = this.future.pop(); if (!s) return null; this.past.push(JSON.stringify(current)); return JSON.parse(s); }
  get canUndo(){ return this.past.length > 0; } get canRedo(){ return this.future.length > 0; }
  clear(){ this.past = []; this.future = []; }
  discardLast(){ this.past.pop(); }
}
