// Arhivă ZIP minimală (format PKZIP 2.0, deflate din zlib), fără dependențe: suficientă pentru exportul unui proiect.
import zlib from 'node:zlib';

export interface ZipEntry { name: string; data: Buffer | string; date?: Date }
const dosTime = (d: Date) => ((d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1)) & 0xffff;
const dosDate = (d: Date) => (((d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate()) & 0xffff;

export function zip(entries: ZipEntry[]): Buffer {
  const parts: Buffer[] = [], central: Buffer[] = []; let offset = 0;
  for (const e of entries){
    const name = Buffer.from(e.name.replace(/^\/+/, ''), 'utf8'), raw = typeof e.data === 'string' ? Buffer.from(e.data, 'utf8') : e.data;
    const crc = zlib.crc32(raw) >>> 0, packed = zlib.deflateRawSync(raw), store = packed.length >= raw.length, body = store ? raw : packed, d = e.date ?? new Date();
    const head = Buffer.alloc(30); head.writeUInt32LE(0x04034b50, 0); head.writeUInt16LE(20, 4); head.writeUInt16LE(0x0800, 6); head.writeUInt16LE(store ? 0 : 8, 8);
    head.writeUInt16LE(dosTime(d), 10); head.writeUInt16LE(dosDate(d), 12); head.writeUInt32LE(crc, 14); head.writeUInt32LE(body.length, 18); head.writeUInt32LE(raw.length, 22);
    head.writeUInt16LE(name.length, 26); head.writeUInt16LE(0, 28);
    const cd = Buffer.alloc(46); cd.writeUInt32LE(0x02014b50, 0); cd.writeUInt16LE(20, 4); cd.writeUInt16LE(20, 6); cd.writeUInt16LE(0x0800, 8); cd.writeUInt16LE(store ? 0 : 8, 10);
    cd.writeUInt16LE(dosTime(d), 12); cd.writeUInt16LE(dosDate(d), 14); cd.writeUInt32LE(crc, 16); cd.writeUInt32LE(body.length, 20); cd.writeUInt32LE(raw.length, 24);
    cd.writeUInt16LE(name.length, 28); cd.writeUInt32LE(offset, 42);
    parts.push(head, name, body); central.push(cd, name); offset += head.length + name.length + body.length;
  }
  const cdBuf = Buffer.concat(central), end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(cdBuf.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...parts, cdBuf, end]);
}
