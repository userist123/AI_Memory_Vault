// JPEG-uri construite din octeți pentru teste (structură validă de segmente; tabelele și datele comprimate sunt de umplutură,
// deci nu sunt imagini decodabile — pentru decodare reală testele folosesc `sharp`, dacă e instalat).
export const cat = (...parts: (Uint8Array | number[])[]) => { const all = parts.map(p => p instanceof Uint8Array ? p : Uint8Array.from(p)); const out = new Uint8Array(all.reduce((n, p) => n + p.length, 0)); let o = 0; for (const p of all){ out.set(p, o); o += p.length; } return out; };
export const ascii = (s: string) => Uint8Array.from([...s].map(c => c.charCodeAt(0)));
/** Segment cu lungime: FF, cod, lungimea (2 + conținut), conținutul. */
export const seg = (m: number, payload: Uint8Array | number[] = []) => { const n = payload.length + 2; return cat([0xff, m, n >> 8, n & 255], payload); };
export const SOI = [0xff, 0xd8], EOI = [0xff, 0xd9];
export const JFIF = seg(0xe0, cat(ascii('JFIF\0'), [1, 1, 0, 0, 1, 0, 1, 0, 0]));
export const exif = (secret: string) => seg(0xe1, cat(ascii('Exif\0\0'), ascii(`II*\0 GPSLatitude ${secret} `)));
export const DQT = seg(0xdb, [0, ...Array(64).fill(1)]);
export const DHT = seg(0xc4, [0, ...Array(16).fill(0)]);
export const sof = (m: number, w: number, h: number, comps = 3, precision = 8) => seg(m, [precision, h >> 8, h & 255, w >> 8, w & 255, comps, ...Array.from({ length: comps }, (_, i) => [i + 1, 0x11, 0]).flat()]);
export const sos = (comps = 3) => seg(0xda, [comps, ...Array.from({ length: comps }, (_, i) => [i + 1, 0]).flat(), 0, 63, 0]);
/** Date comprimate de umplutură: conțin un FF 00 (octet FF din date) și, opțional, un marcaj de repornire RST0. */
export const scanData = (rst = false) => Uint8Array.from([0x12, 0x34, 0xff, 0x00, 0x56, ...(rst ? [0xff, 0xd0, 0x78, 0x9a] : []), 0xbc]);
export const baseline = (o: { w?: number; h?: number; comps?: number; extra?: Uint8Array[]; tail?: Uint8Array | number[]; rst?: boolean } = {}) =>
  cat(SOI, JFIF, ...(o.extra ?? []), DQT, sof(0xc0, o.w ?? 640, o.h ?? 480, o.comps ?? 3), DHT, ...(o.rst ? [seg(0xdd, [0, 4])] : []), sos(o.comps ?? 3), scanData(o.rst), EOI, o.tail ?? []);
/** Unde apare un șir de octeți în altul (−1 dacă lipsește). */
export const indexOfBytes = (hay: Uint8Array, needle: Uint8Array) => { outer: for (let i = 0; i + needle.length <= hay.length; i++){ for (let j = 0; j < needle.length; j++) if (hay[i + j] !== needle[j]) continue outer; return i; } return -1; };
/** Codurile segmentelor dintr-un JPEG produs de stripJpeg (fără octeți de umplere), în ordine. */
export function markers(b: Uint8Array){ const out: number[] = []; let p = 2; out.push(b[1]!);
  while (p < b.length){ if (b[p] !== 0xff) throw new Error('marcaj așteptat la ' + p); const m = b[p + 1]!; out.push(m); if (m === 0xd9) break;
    const len = (b[p + 2]! << 8) | b[p + 3]!; p += 2 + len;
    if (m === 0xda){ for (;;){ while (b[p] !== 0xff) p++; const n = b[p + 1]!; if (n === 0 || (n >= 0xd0 && n <= 0xd7)){ p += 2; continue; } break; } } }
  return out; }
