/**
 * Live activity client. WS ws://127.0.0.1:17875/api/v1/events with subprotocols ["aas.v1", "aas.token.<token>"];
 * the server must echo exactly `aas.v1` (never the token). A socket that negotiated anything else is closed
 * before any byte is sent on it.
 * On open the client sends {afterSeq: lastSeq}; every frame carries `prev` (the seq last sent on this socket).
 * When prev != lastSeq the client records a gap and reconnects with afterSeq=lastSeq (lossless replay from DB).
 */
import { parseFrame } from "./events";
import type { Session } from "./session";
import type { Store } from "./store";

/** Application protocol the server echoes back; the token subprotocol is offered but never echoed. */
export const WS_PROTOCOL = "aas.v1";

export function wsProtocols(token: string): string[] {
  return [WS_PROTOCOL, `aas.token.${token}`];
}

export interface LiveClientOptions {
  /** test hook: disable gap detection (THREAT_MODEL P6f positive control) */
  gapDetection?: boolean;
  WebSocketImpl?: typeof WebSocket;
}

export class LiveClient {
  private ws: WebSocket | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private stopped = false;
  private delay = 500;
  /** first frame on a socket may carry prev=0/null (nothing sent yet on it) */
  private fresh = true;

  constructor(
    private readonly session: Session,
    private readonly store: Store,
    private readonly opts: LiveClientOptions = {},
  ) {}

  start(): void {
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
    this.store.dispatch({ type: "connection", value: "connecting" });
    const ws = new WS(this.session.wsUrl, wsProtocols(this.session.token));
    this.ws = ws;
    this.fresh = true;
    ws.onopen = () => {
      if (ws.protocol !== WS_PROTOCOL) {
        // wrong or missing negotiation (e.g. a server echoing the token): refuse the socket, retry later
        this.store.dispatch({ type: "protocol", value: ws.protocol || "" });
        ws.close();
        return;
      }
      this.delay = 500;
      this.store.dispatch({ type: "connection", value: "open" });
      const afterSeq = this.store.get().lastSeq;
      ws.send(JSON.stringify({ afterSeq }));
    };
    ws.onmessage = (m: MessageEvent) => this.onFrame(ws, m.data);
    ws.onclose = () => {
      if (this.ws === ws) this.ws = null;
      this.store.dispatch({ type: "connection", value: "offline" });
      if (this.stopped) return;
      this.timer = setTimeout(() => this.connect(), this.delay);
      this.delay = Math.min(Math.max(this.delay * 2, 500), 8000);
    };
    ws.onerror = () => ws.close();
  }

  private onFrame(ws: WebSocket, data: unknown): void {
    if (ws !== this.ws) return;
    const r = parseFrame(data);
    if (!r.ok) {
      this.store.dispatch({ type: "dropped" });
      return;
    }
    const ev = r.event;
    const lastSeq = this.store.get().lastSeq;
    const lenient = this.fresh && (ev.prev === null || ev.prev === 0);
    this.fresh = false;
    if (this.opts.gapDetection !== false && !lenient && ev.prev !== null && ev.prev !== lastSeq && ev.seq > lastSeq) {
      this.store.dispatch({ type: "gap", gap: { from: lastSeq + 1, to: ev.seq - 1, reason: "resync" } });
      // reconnect immediately; the server replays afterSeq=lastSeq from the DB
      this.delay = 0;
      ws.close();
      return;
    }
    this.store.dispatch({ type: "event", event: ev });
  }
}
