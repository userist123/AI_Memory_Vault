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

// ---------------------------------------------------------------- S9 (B9): decoder input hardening
/** The only NAL unit types handed to the decoder: non-IDR slice, IDR slice, SPS, PPS. */
export const NAL_ALLOW: ReadonlySet<number> = new Set([1, 5, 7, 8]);
/** An access unit above this is dropped (the server caps one NAL at 1 MiB, see core/aas_device/video_relay.py). */
export const MAX_AU_BYTES = 1024 * 1024;
export const SPS_MAX_DIM = 4096;
/** Baseline (incl. constrained), Main, High - what Android screenrecord / the fixture produce. */
export const SPS_PROFILES: ReadonlySet<number> = new Set([66, 77, 100]);
/** WebCodecs config shared by every decoder: software decoding keeps H.264 parsing out of the GPU process. */
export const DECODER_CONFIG_BASE = { optimizeForLatency: true, hardwareAcceleration: "prefer-software" } as const;

const HIGH_FAMILY = new Set([100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135]);

/** RBSP of a NAL payload: emulation prevention bytes (00 00 03) removed. */
function rbsp(p: Uint8Array): Uint8Array {
  const out = new Uint8Array(p.length);
  let o = 0;
  let zeros = 0;
  for (let i = 0; i < p.length; i++) {
    const b = p[i]!;
    if (zeros >= 2 && b === 3) {
      zeros = 0;
      continue;
    }
    zeros = b === 0 ? zeros + 1 : 0;
    out[o++] = b;
  }
  return out.subarray(0, o);
}

class Bits {
  private pos = 0;
  constructor(private readonly b: Uint8Array) {}
  u(n: number): number {
    let v = 0;
    for (let i = 0; i < n; i++) {
      const byte = this.pos >> 3;
      if (byte >= this.b.length) throw new Error("eof");
      v = v * 2 + ((this.b[byte]! >> (7 - (this.pos & 7))) & 1);
      this.pos++;
    }
    return v;
  }
  ue(): number {
    let zeros = 0;
    while (this.u(1) === 0) {
      if (++zeros > 31) throw new Error("exp-golomb too long");
    }
    return 2 ** zeros - 1 + this.u(zeros);
  }
  se(): number {
    const k = this.ue();
    return k % 2 ? (k + 1) / 2 : -(k / 2);
  }
}

export interface SpsInfo {
  profile: number;
  chromaFormat: number;
  bitDepthLuma: number;
  bitDepthChroma: number;
  width: number;
  height: number;
  /** inside the bounds above: profile 66/77/100, 8-bit 4:2:0, no scaling matrix, <= 4096 x 4096 */
  ok: boolean;
}

/** Bounded SPS parse (up to frame_mbs_only_flag). null = malformed or truncated. */
export function parseSps(nal: Uint8Array): SpsInfo | null {
  if (nal.length < 5 || nalType(nal) !== 7 || (nal[0]! & 0x80) !== 0) return null;
  try {
    const b = new Bits(rbsp(nal.subarray(1)));
    const profile = b.u(8);
    b.u(8); // constraint flags
    b.u(8); // level_idc
    if (b.ue() > 31) return null; // seq_parameter_set_id
    let chromaFormat = 1;
    let bitDepthLuma = 8;
    let bitDepthChroma = 8;
    if (HIGH_FAMILY.has(profile)) {
      chromaFormat = b.ue();
      if (chromaFormat > 3) return null;
      if (chromaFormat === 3) b.u(1);
      bitDepthLuma = 8 + b.ue();
      bitDepthChroma = 8 + b.ue();
      b.u(1); // qpprime_y_zero_transform_bypass_flag
      if (b.u(1) === 1) {
        // scaling matrices are never produced by screenrecord: not parsed, refused
        return { profile, chromaFormat, bitDepthLuma, bitDepthChroma, width: 0, height: 0, ok: false };
      }
    }
    if (b.ue() > 12) return null; // log2_max_frame_num_minus4
    const poc = b.ue();
    if (poc === 0) {
      if (b.ue() > 12) return null;
    } else if (poc === 1) {
      b.u(1);
      b.se();
      b.se();
      const n = b.ue();
      if (n > 255) return null;
      for (let i = 0; i < n; i++) b.se();
    } else if (poc !== 2) return null;
    if (b.ue() > 16) return null; // max_num_ref_frames
    b.u(1); // gaps_in_frame_num_value_allowed_flag
    const wMbs = b.ue() + 1;
    const hMap = b.ue() + 1;
    const frameMbsOnly = b.u(1);
    const width = wMbs * 16;
    const height = (2 - frameMbsOnly) * hMap * 16;
    const ok = SPS_PROFILES.has(profile) && chromaFormat === 1 && bitDepthLuma === 8 && bitDepthChroma === 8 &&
      width <= SPS_MAX_DIM && height <= SPS_MAX_DIM;
    return { profile, chromaFormat, bitDepthLuma, bitDepthChroma, width, height, ok };
  } catch {
    return null;
  }
}

/** Fixture / file input: only allowed NAL types, and null when the stream's first SPS is outside the bounds. */
export function sanitizeNals(nals: Uint8Array[]): Uint8Array[] | null {
  const kept = nals.filter((n) => (n[0]! & 0x80) === 0 && NAL_ALLOW.has(nalType(n)));
  const first = kept.find((n) => nalType(n) === 7);
  if (!first || !parseSps(first)?.ok) return null;
  return kept;
}
