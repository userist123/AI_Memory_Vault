/**
 * Emulator mirror playback core (runs inside the decoder worker; dependency-injected so it is unit-testable).
 * Lifecycle rules: a decoder error or an oversize frame stops the timer and drops the decoder; frames above
 * MAX_DIM x MAX_DIM never resize the canvas.
 * S9 (B9): every decoder is configured with DECODER_CONFIG_BASE (prefer-software); the live feeder passes only NAL
 * types 1/5/7/8, refuses a stream whose SPS is outside the bounds (annexb.parseSps), drops access units above
 * MAX_AU_BYTES and decodes nothing before it has seen SPS + PPS and then an IDR.
 */
import { DECODER_CONFIG_BASE, MAX_AU_BYTES, NAL_ALLOW, parseSps, type AccessUnit } from "./annexb";

export const MAX_DIM = 4096;

export function dimsOk(w: number, h: number): boolean {
  return Number.isFinite(w) && Number.isFinite(h) && w >= 1 && h >= 1 && w <= MAX_DIM && h <= MAX_DIM;
}

export type MirrorState = "idle" | "decoding" | "unsupported" | "error";

export interface FrameLike {
  displayWidth: number;
  displayHeight: number;
  close(): void;
}
export interface DecoderLike {
  state: string;
  configure(cfg: { codec: string; optimizeForLatency: boolean; hardwareAcceleration?: "prefer-software" }): void;
  decode(chunk: unknown): void;
  close(): void;
}
export interface CanvasLike {
  width: number;
  height: number;
  getContext(kind: "2d"): { drawImage(frame: never, x: number, y: number): void } | null;
}

export interface PlayerEnv {
  createDecoder(output: (f: FrameLike) => void, error: (e: { message: string }) => void): DecoderLike;
  makeChunk(type: "key" | "delta", timestamp: number, data: Uint8Array): unknown;
  getCanvas(): CanvasLike | null;
  status(text: string, state: MirrorState, frames: number): void;
}

export function createMirrorPlayer(env: PlayerEnv) {
  let decoder: DecoderLike | null = null;
  let timer: ReturnType<typeof setInterval> | undefined;
  let frames = 0;
  const report = (text: string, state: MirrorState) => env.status(text, state, frames);

  function stop() {
    if (timer) clearInterval(timer);
    timer = undefined;
    const d = decoder;
    decoder = null;
    if (d && d.state !== "closed") {
      try {
        d.close();
      } catch {
        // already torn down
      }
    }
  }

  function fail(text: string) {
    stop();
    report(text, "error");
  }

  function onFrame(frame: FrameLike) {
    if (!dimsOk(frame.displayWidth, frame.displayHeight)) {
      frame.close();
      fail(`frame ${frame.displayWidth}x${frame.displayHeight} exceeds ${MAX_DIM}x${MAX_DIM}`);
      return;
    }
    const canvas = env.getCanvas();
    if (canvas) {
      if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
        canvas.width = frame.displayWidth;
        canvas.height = frame.displayHeight;
      }
      canvas.getContext("2d")?.drawImage(frame as never, 0, 0);
    }
    frame.close();
    frames++;
    if (frames % 10 === 1) report(`decoding (${frames} frames)`, "decoding");
  }

  function create(codec: string): DecoderLike {
    const d = env.createDecoder(onFrame, (e) => {
      if (decoder === d) fail(`decoder error: ${e.message}`);
    });
    d.configure({ codec, ...DECODER_CONFIG_BASE });
    return d;
  }

  function start(codec: string, aus: AccessUnit[], fps: number, loop: boolean) {
    stop();
    frames = 0;
    decoder = create(codec);
    let i = 0;
    const step = 1_000_000 / Math.max(1, Math.min(60, fps));
    timer = setInterval(() => {
      if (!decoder) return;
      try {
        if (i >= aus.length) {
          if (!loop) return stop();
          i = 0;
          decoder.close();
          decoder = create(codec);
        }
        const au = aus[i]!;
        decoder.decode(env.makeChunk(au.key ? "key" : "delta", i * step, au.data));
        i++;
      } catch (e) {
        fail(`decoder error: ${e instanceof Error ? e.message : "decode failed"}`);
      }
    }, step / 1000);
    report(`decoding ${codec}`, "decoding");
  }

  return { start, stop, report };
}

// ---------------------------------------------------------------- live stream (S8)
/** Live mirror limits: decoder queue above this drops delta frames until the next key frame; bounded buffers. */
export const LIVE_MAX_QUEUE = 30;
export const LIVE_MAX_BUFFER = 2 * 1024 * 1024;
const LIVE_MAX_PENDING = 120;
const LIVE_STEP_US = 40_000;

export interface LiveDecoderLike extends DecoderLike {
  decodeQueueSize?: number;
}

export interface LiveEnv extends PlayerEnv {
  /** VideoDecoder.isConfigSupported in the worker; tests inject it */
  supported(codec: string): Promise<boolean>;
  /** decoder error: ask the transport for a fresh stream (the worker posts {type: "resync"} to the VideoClient) */
  resync?(): void;
}

function concat(a: Uint8Array, b: Uint8Array): Uint8Array {
  const out = new Uint8Array(a.length + b.length);
  out.set(a, 0);
  out.set(b, a.length);
  return out;
}

function findStart(buf: Uint8Array, from: number): number {
  for (let i = from; i + 2 < buf.length; i++) {
    if (buf[i] === 0 && buf[i + 1] === 0 && buf[i + 2] === 1) return i;
  }
  return -1;
}

/**
 * Streaming Annex-B -> access units -> VideoDecoder. Chunks may cut NAL units and start codes anywhere. The codec
 * string comes from the SPS; nothing is decoded before the support check passed and a key frame arrived. A full
 * decoder queue drops delta frames until the next key frame (video is dropped first, the UI never stalls).
 */
interface LiveAu extends AccessUnit {
  /** a key frame emitted after SPS + PPS were seen (only such a frame may start the decoding) */
  ready: boolean;
}

export function createLiveFeeder(env: LiveEnv) {
  let buf: Uint8Array = new Uint8Array(0);
  let synced = false;
  let decoder: LiveDecoderLike | null = null;
  let codec = "";
  let probing = false;
  let dead = false;
  let needKey = true;
  let ts = 0;
  let frames = 0;
  let cur: Uint8Array[] = [];
  let curVcl = false;
  let curKey = false;
  let pending: LiveAu[] = [];
  let curBytes = 0;
  let haveSps = false; // since the last reset: a bounded SPS ...
  let havePps = false; // ... and a PPS were seen; only then can an IDR start the decoding
  const report = (text: string, state: MirrorState) => env.status(text, state, frames);

  function closeDecoder() {
    const d = decoder;
    decoder = null;
    if (d && d.state !== "closed") {
      try {
        d.close();
      } catch {
        // already closed
      }
    }
  }

  function onFrame(frame: FrameLike) {
    if (!dimsOk(frame.displayWidth, frame.displayHeight)) {
      frame.close();
      closeDecoder();
      dead = true;
      report(`frame ${frame.displayWidth}x${frame.displayHeight} exceeds ${MAX_DIM}x${MAX_DIM}`, "error");
      return;
    }
    const canvas = env.getCanvas();
    if (canvas) {
      if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
        canvas.width = frame.displayWidth;
        canvas.height = frame.displayHeight;
      }
      canvas.getContext("2d")?.drawImage(frame as never, 0, 0);
    }
    frame.close();
    frames++;
    if (frames % 25 === 1) report(`live (${frames} frames)`, "decoding");
  }

  function decode(au: LiveAu) {
    if (!decoder) return;
    if (au.key && au.ready) needKey = false;
    else if (needKey) return;
    if ((decoder.decodeQueueSize ?? 0) > LIVE_MAX_QUEUE) {
      needKey = true;
      return;
    }
    try {
      decoder.decode(env.makeChunk(au.key ? "key" : "delta", ts, au.data));
      ts += LIVE_STEP_US;
    } catch (e) {
      decoderFailed(e instanceof Error ? e.message : "decode failed");
    }
  }

  /** The decoder is gone: ask the transport to resync (reconnect with backoff; the server primes SPS/PPS + GOP), and
   * the SPS at the start of the new stream configures a new decoder (review M1 c). */
  function decoderFailed(message: string) {
    closeDecoder();
    needKey = true;
    codec = "";
    pending = [];
    report(`decoder error: ${message}`, "error");
    env.resync?.();
  }

  function startDecoder() {
    closeDecoder();
    const d = env.createDecoder(onFrame, (e) => {
      if (decoder === d) decoderFailed(e.message);
    }) as LiveDecoderLike;
    d.configure({ codec, ...DECODER_CONFIG_BASE });
    decoder = d;
    needKey = true;
    report(`live ${codec}`, "decoding");
    const queued = pending;
    pending = [];
    for (const au of queued) decode(au);
  }

  function onCodec(c: string) {
    if (dead || c === codec) return;
    codec = c;
    closeDecoder();
    probing = true;
    env
      .supported(c)
      .catch(() => false)
      .then((ok) => {
        if (c !== codec || dead) return;
        probing = false;
        if (!ok) {
          dead = true;
          pending = [];
          report(`codec ${c} not supported here`, "unsupported");
          return;
        }
        startDecoder();
      });
  }

  function emitAu() {
    if (cur.length && curVcl && curBytes > MAX_AU_BYTES) {
      needKey = true; // oversize access unit: dropped, the decoder waits for the next key frame
      pending = [];
    } else if (cur.length && curVcl) {
      const size = cur.reduce((n, x) => n + 4 + x.length, 0);
      const data = new Uint8Array(size);
      let o = 0;
      for (const n of cur) {
        data.set([0, 0, 0, 1], o);
        data.set(n, o + 4);
        o += 4 + n.length;
      }
      const au: LiveAu = { data, key: curKey, ready: curKey && haveSps && havePps };
      if (decoder) decode(au);
      else if (!dead && (probing || codec === "")) {
        if (au.key) pending = [au];
        else if (pending.length && pending.length < LIVE_MAX_PENDING) pending.push(au);
      }
    }
    cur = [];
    curVcl = false;
    curKey = false;
    curBytes = 0;
  }

  function onNal(n: Uint8Array) {
    const t = (n[0] ?? 0) & 0x1f;
    const vcl = t === 1 || t === 5;
    const firstSlice = vcl && ((n[1] ?? 0) & 0x80) !== 0;
    if (curVcl && ((vcl && firstSlice) || t === 7 || t === 8 || t === 9 || t === 6)) emitAu();
    if (((n[0] ?? 0) & 0x80) !== 0 || !NAL_ALLOW.has(t)) return; // S9: types 1/5/7/8 only (AUD/SEI/... dropped)
    if (t === 7) {
      const info = parseSps(n);
      if (!info?.ok) {
        dead = true; // a stream outside the bounds is never configured (reset by stop / a new mirror session)
        pending = [];
        closeDecoder();
        report(info ? `stream outside the decoder bounds (profile ${info.profile}, ${info.width}x${info.height})`
          : "malformed SPS", "unsupported");
        return;
      }
      haveSps = true;
      const c = codecFromSpsNal(n);
      if (c) onCodec(c);
    }
    if (t === 8 && haveSps) havePps = true;
    if (curBytes <= MAX_AU_BYTES) cur.push(n); // past the cap the unit is only counted, emitAu drops it
    curBytes += 4 + n.length;
    if (vcl) curVcl = true;
    if (t === 5) curKey = true;
  }

  function trimZeros(n: Uint8Array): Uint8Array {
    let e = n.length;
    while (e > 0 && n[e - 1] === 0) e--;
    return n.subarray(0, e);
  }

  function push(chunk: Uint8Array) {
    if (dead) return;
    buf = buf.length ? concat(buf, chunk) : chunk.slice();
    if (!synced) {
      const j = findStart(buf, 0);
      if (j < 0) {
        buf = buf.slice(Math.max(0, buf.length - 2));
        return;
      }
      buf = buf.slice(j);
      synced = true;
    }
    let begin = 3;
    let i = 3;
    for (;;) {
      const j = findStart(buf, i);
      if (j < 0) break;
      const nal = trimZeros(buf.subarray(begin, j));
      if (nal.length) onNal(nal.slice());
      begin = i = j + 3;
    }
    if (begin > 3) buf = buf.slice(begin - 3);
    if (buf.length > LIVE_MAX_BUFFER) reset(); // a NAL above 2 MiB: resync at the next start code
  }

  /** End of stream (tests, socket closed): the buffered NAL and access unit are complete. */
  function end() {
    if (dead) return;
    if (synced && buf.length > 3) {
      const nal = trimZeros(buf.subarray(3));
      if (nal.length) onNal(nal.slice());
    }
    buf = new Uint8Array(0);
    synced = false;
    emitAu();
  }

  /** Socket (re)connected: the server starts again with SPS + PPS + an IDR. */
  function reset() {
    buf = new Uint8Array(0);
    synced = false;
    cur = [];
    curVcl = false;
    curKey = false;
    curBytes = 0;
    pending = [];
    needKey = true;
    haveSps = havePps = false;
  }

  function stop() {
    reset();
    closeDecoder();
    codec = "";
    probing = false;
    dead = false;
    frames = 0;
  }

  return { push, end, reset, stop, buffered: () => buf.length };
}

function codecFromSpsNal(sps: Uint8Array): string | null {
  if (sps.length < 4) return null;
  const hex = (b: number) => b.toString(16).padStart(2, "0");
  return `avc1.${hex(sps[1]!)}${hex(sps[2]!)}${hex(sps[3]!)}`;
}
