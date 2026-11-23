/** H.264 Annex-B helpers for the emulator mirror (WebCodecs VideoDecoder, no description => Annex-B input). */

export interface AccessUnit {
  data: Uint8Array;
  key: boolean;
}

/** Split an Annex-B byte stream into NAL units (start codes removed). */
export function splitNals(buf: Uint8Array): Uint8Array[] {
  const starts: Array<[number, number]> = []; // [startCodeIndex, payloadIndex]
  for (let i = 0; i + 2 < buf.length; i++) {
    if (buf[i] === 0 && buf[i + 1] === 0) {
      if (buf[i + 2] === 1) {
        starts.push([i, i + 3]);
        i += 2;
      } else if (buf[i + 2] === 0 && i + 3 < buf.length && buf[i + 3] === 1) {
        starts.push([i, i + 4]);
        i += 3;
      }
    }
  }
  const out: Uint8Array[] = [];
  for (let k = 0; k < starts.length; k++) {
    const begin = starts[k]![1];
    const end = k + 1 < starts.length ? starts[k + 1]![0] : buf.length;
    if (end > begin) out.push(buf.subarray(begin, end));
  }
  return out;
}

export function nalType(nal: Uint8Array): number {
  return (nal[0] ?? 0) & 0x1f;
}

/** codec string `avc1.PPCCLL` from the first SPS (profile_idc, constraint flags, level_idc). */
export function codecFromSps(nals: Uint8Array[]): string | null {
  const sps = nals.find((n) => nalType(n) === 7);
  if (!sps || sps.length < 4) return null;
  const hex = (b: number) => b.toString(16).padStart(2, "0");
  return `avc1.${hex(sps[1]!)}${hex(sps[2]!)}${hex(sps[3]!)}`;
}

function withStartCodes(nals: Uint8Array[]): Uint8Array {
  const size = nals.reduce((n, x) => n + 4 + x.length, 0);
  const out = new Uint8Array(size);
  let o = 0;
  for (const n of nals) {
    out.set([0, 0, 0, 1], o);
    out.set(n, o + 4);
    o += 4 + n.length;
  }
  return out;
}

/** Group NAL units into access units (one decoded frame each). */
export function accessUnits(nals: Uint8Array[]): AccessUnit[] {
  const aus: AccessUnit[] = [];
  let cur: Uint8Array[] = [];
  let hasVcl = false;
  let key = false;
  const flush = () => {
    if (cur.length && hasVcl) aus.push({ data: withStartCodes(cur), key });
    cur = [];
    hasVcl = false;
    key = false;
  };
  for (const n of nals) {
    const t = nalType(n);
    const vcl = t === 1 || t === 5;
    const firstSlice = vcl && ((n[1] ?? 0) & 0x80) !== 0; // first_mb_in_slice == 0
    if (hasVcl && ((vcl && firstSlice) || t === 7 || t === 8 || t === 9 || t === 6)) flush();
    cur.push(n);
    if (vcl) hasVcl = true;
    if (t === 5) key = true;
  }
  flush();
  return aus;
}
