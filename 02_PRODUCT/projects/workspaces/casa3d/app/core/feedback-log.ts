// Istoricul deciziilor de pe linkul clientului (UI/UX P1-4): cine, ce și când, lizibil. Aprobările date împreună
// („Aprobați tot”: aceeași persoană, același verdict, același minut, fără text) devin un singur rând cu numărul de elemente
// pe camere; o decizie singulară își arată camera și elementul. Cele mai noi primele.

export interface LogInput { id: string; roomId: string | null; verdict: string; author: string; text: string; createdAt: string; item?: { label: string } | null }
export interface LogEntry { id: string; at: string; author: string; verdict: string; count: number; rooms: { name: string; count: number }[]; label: string | null; text: string }

export function feedbackLog(items: readonly LogInput[], roomName: (id: string | null) => string): LogEntry[] {
  const out: LogEntry[] = [], minute = (s: string) => s.slice(0, 16);
  for (const f of [...items].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id))){
    const room = roomName(f.roomId), last = out[out.length - 1];
    const groupable = !!f.item && !f.text;
    if (groupable && last && last.count > 0 && !last.text && last.author === f.author && last.verdict === f.verdict && minute(last.at) === minute(f.createdAt)){
      last.count++; const r = last.rooms.find(x => x.name === room); if (r) r.count++; else last.rooms.push({ name: room, count: 1 });
      if (last.count > 1) last.label = null; continue; }
    out.push({ id: f.id, at: f.createdAt, author: f.author, verdict: f.verdict, count: f.item ? 1 : 0, rooms: [{ name: room, count: 1 }], label: f.item?.label ?? null, text: f.text });
  }
  return out;
}
