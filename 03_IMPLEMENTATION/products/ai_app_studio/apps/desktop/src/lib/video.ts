/**
 * Live emulator mirror transport (S8). Binary WS ws://127.0.0.1:17875/api/v1/device/video?projectId=prj_<16 hex>
 * with the same subprotocols as the activity socket (["aas.v1", "aas.token.<token>"]; the server echoes only
 * "aas.v1"). The server sends whole NAL units (Annex-B, <= 64 KiB per frame), starting with SPS + PPS + an IDR.
 * Nothing is ever sent by the client. Any client-side drop (oversize frame, over the byte budget) closes the socket:
 * the reconnect starts a clean stream again, so the decoder never sees a cut NAL unit.
 */
import { wsProtocols, WS_PROTOCOL, MIN_DELAY_MS, MAX_DELAY_MS } from "./live";
import type { Session } from "./session";

export const VIDEO_MAX_FRAME = 64 * 1024;
/** bytes per second accepted from the server (screenrecord runs at 4 Mbit/s = 0.5 MiB/s) */
export const VIDEO_MAX_BPS = 4 * 1024 * 1024;
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
  private windowStart = 0;
  private windowBytes = 0;

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
    this.ws?.close();
    this.ws = null;
  }

  private connect(): void {
    if (this.stopped) return;
    const WS = this.opts.WebSocketImpl ?? WebSocket;
    const ws = new WS(videoUrl(this.session, this.projectId), wsProtocols(this.session.token));
    ws.binaryType = "arraybuffer";
    this.ws = ws;
    let accepted = false;
    ws.onopen = () => {
      if (ws.protocol !== WS_PROTOCOL) {
        ws.close();
        return;
      }
      accepted = true;
      this.windowStart = Date.now();
      this.windowBytes = 0;
      this.onReset();
    };
    ws.onmessage = (m: MessageEvent) => {
      if (ws !== this.ws || !accepted) return;
      const data: unknown = m.data;
      if (!(data instanceof ArrayBuffer)) return;
      if (data.byteLength > VIDEO_MAX_FRAME || !this.budget(data.byteLength)) {
        ws.close();
        return;
      }
      this.delay = MIN_DELAY_MS;
      this.onChunk(data);
    };
    ws.onclose = () => {
      if (this.ws === ws) this.ws = null;
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
    }
    this.windowBytes += n;
    return this.windowBytes <= VIDEO_MAX_BPS;
  }
}
