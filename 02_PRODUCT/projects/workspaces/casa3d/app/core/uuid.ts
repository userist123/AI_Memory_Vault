// Identificator aleator (UUID v4) care merge și în browser pe http simplu: `crypto.randomUUID()` există doar în context securizat
// (https sau localhost), deci pe telefonul din aceeași rețea (http://192.168.x.y) lipsește. `crypto.getRandomValues()` există
// în orice context, așa că din el se construiește același format (RFC 4122, versiunea 4).
export function randomId(): string {
  const c = globalThis.crypto;
  if (typeof c?.randomUUID === 'function') return c.randomUUID();
  const b = new Uint8Array(16); c.getRandomValues(b);
  b[6] = (b[6]! & 0x0f) | 0x40; b[8] = (b[8]! & 0x3f) | 0x80;   // versiunea 4 și varianta RFC 4122
  const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}
