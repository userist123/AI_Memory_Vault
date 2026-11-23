/**
 * Live emulator mirror transport (S8). Binary WS ws://127.0.0.1:17875/api/v1/device/video?projectId=prj_<16 hex>
 * with the same subprotocols as the activity socket (["aas.v1", "aas.token.<token>"]; the server echoes only
 * "aas.v1"). Every server frame is a 4-byte big-endian seq (1, 2, ... per socket) + whole NAL units (Annex-B), at
 * most 64 KiB in all, starting with SPS + PPS + an IDR. Nothing is ever sent by the client.
 * - Any client-side drop (oversize or headerless frame, over the byte budget) closes the socket: the reconnect starts
 *   a clean stream again, so the decoder never sees a cut NAL unit.
 * - A seq gap (the server dropped whole NALs for this viewer) and a decoder error (`resync()`, from the worker) both
 *   resync: the socket is replaced after a bounded, doubling backoff (review M1) and the server primes the new one
 *   with the cached SPS/PPS + GOP.
 * - Budget: VIDEO_MAX_BPS per second, plus VIDEO_PRIME_MAX in the first second after open for that prime (review L2).
 * - The socket is per project and outlives runs; between runs it stays open and idle (review L3).
 */
import { wsProtocols, WS_PROTOCOL, MIN_DELAY_MS, MAX_DELAY_MS } from "./live";
import type { Session } from "./session";

export const VIDEO_MAX_FRAME = 64 * 1024;
export const VIDEO_SEQ_HEADER = 4;
/** bytes per second accepted from the server: the hub's 2 MiB/s rate + 2 MiB burst, plus frame/start-code overhead */
export const VIDEO_MAX_BPS = 4 * 1024 * 1024 + 64 * 1024;
/** extra bytes in the first second after open: the hub's late-viewer prime (PRIME_MAX 2 MiB) + overhead */
export const VIDEO_PRIME_MAX = 2 * 1024 * 1024 + 64 * 1024;
/** a resync this long after the previous one starts the backoff again at MIN_DELAY_MS */
export const RESYNC_QUIET_MS = 30_000;
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;

export function videoUrl(session: Session, projectId: string): string {
  return `${session.wsUrl.replace(/\/api\/v1\/events$/, "")}/api/v1/device/video?projectId=${encodeURIComponent(projectId)}`;
}

export interface VideoClientOptions {
  WebSocketImpl?: typeof WebSocket;
}

export class VideoClient {
  private ws: WebSocket | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private stopped = false;
  private delay = MIN_DELAY_MS;
  private resyncDelay = MIN_DELAY_MS;
  private lastResync = -Infinity;
  private resyncPending = false;
  private windowStart = 0;
  private windowBytes = 0;
  private primeWindow = false;
  private nextSeq = 1;
  /** seq gaps seen (each one resynced) */
  gaps = 0;

  constructor(
    private readonly session: Session,
    private readonly projectId: string,
    private readonly onChunk: (data: ArrayBuffer) => void,
    private readonly onReset: () => void,
    private readonly opts: VideoClientOptions = {},
  ) {}

  start(): void {
    if (!PROJECT_RE.test(this.projectId)) return;
    this.stopped = false;
    this.connect();
  }

  stop(): void {
    this.stopped = true;
    if (this.timer) clearTimeout(this.timer);
    this.timer = undefined;
    const ws = this.ws;
    this.ws = null;
    ws?.close();
  }

  /** Drop the socket and reconnect after a bounded, doubling backoff; the server primes SPS/PPS + GOP again. */
  resync(): void {
    if (this.stopped || this.resyncPending) return;
    const now = Date.now();
    if (now - this.lastResync >= RESYNC_QUIET_MS) this.resyncDelay = MIN_DELAY_MS;
    this.lastResync = now;
    this.resyncPending = true;
    if (this.timer) clearTimeout(this.timer);
    const ws = this.ws;
    this.ws = null; // its close handler no longer schedules anything
    ws?.close();
    this.timer = setTimeout(() => this.connect(), this.resyncDelay);
    this.resyncDelay = Math.min(Math.max(this.resyncDelay * 2, MIN_DELAY_MS), MAX_DELAY_MS);
  }

  private connect(): void {
    this.timer = undefined;
    this.resyncPending = false;
    if (this.stopped) return;
    const WS = this.opts.WebSocketImpl ?? WebSocket;
    const ws = new WS(videoUrl(this.session, this.projectId), wsProtocols(this.session.token));
    ws.binaryType = "arraybuffer";
    this.ws = ws;
    let accepted = false;
    ws.onopen = () => {
      if (ws !== this.ws) return;
      if (ws.protocol !== WS_PROTOCOL) {
        ws.close();
        return;
      }
      accepted = true;
      this.windowStart = Date.now();
      this.windowBytes = 0;
      this.primeWindow = true;
      this.nextSeq = 1;
      this.onReset();
    };
    ws.onmessage = (m: MessageEvent) => {
      if (ws !== this.ws || !accepted) return;
      const data: unknown = m.data;
      if (!(data instanceof ArrayBuffer)) return;
      if (data.byteLength <= VIDEO_SEQ_HEADER || data.byteLength > VIDEO_MAX_FRAME || !this.budget(data.byteLength)) {
        ws.close();
        return;
      }
      const seq = new DataView(data).getUint32(0);
      if (seq !== this.nextSeq) {
        this.gaps++;
        this.resync();
        return;
      }
      this.nextSeq = (this.nextSeq + 1) >>> 0;
      this.delay = MIN_DELAY_MS;
      this.onChunk(data.slice(VIDEO_SEQ_HEADER));
    };
    ws.onclose = () => {
      if (ws !== this.ws) return; // replaced by resync() or stop()
      this.ws = null;
      if (this.stopped) return;
      this.timer = setTimeout(() => this.connect(), this.delay);
      this.delay = Math.min(Math.max(this.delay * 2, MIN_DELAY_MS), MAX_DELAY_MS);
    };
    ws.onerror = () => ws.close();
  }

  private budget(n: number): boolean {
    const now = Date.now();
    if (now - this.windowStart >= 1000) {
      this.windowStart = now;
      this.windowBytes = 0;
      this.primeWindow = false;
    }
    this.windowBytes += n;
    return this.windowBytes <= VIDEO_MAX_BPS + (this.primeWindow ? VIDEO_PRIME_MAX : 0);
  }
}
