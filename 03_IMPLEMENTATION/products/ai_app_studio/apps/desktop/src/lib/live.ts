/**
 * Live activity client. WS ws://127.0.0.1:17875/api/v1/events with subprotocols ["aas.v1", "aas.token.<token>"];
 * the server must echo exactly `aas.v1` (never the token). A socket that negotiated anything else is closed
 * before any byte is sent on it.
 * afterSeq is a QUERY parameter of the WS URL (backend: `events(ws, afterSeq: int = 0)`); nothing is sent after open.
 * Every frame carries `prev` (the seq last sent on this socket; for the first frame that is afterSeq itself).
 * When prev != lastSeq (also for the first frame) or prev is missing, the client records a gap and reconnects with
 * afterSeq=lastSeq (lossless replay from the DB), with a backoff that never drops below 0.5 s.
 * A server frame {type: "gap", after, before} (row missing in the table) is recorded without a reconnect.
 */
import { parseFrame, parseGapFrame } from "./events";
import type { Session } from "./session";
import type { Store } from "./store";

/** Application protocol the server echoes back; the token subprotocol is offered but never echoed. */
export const WS_PROTOCOL = "aas.v1";

export function wsProtocols(token: string): string[] {
  return [WS_PROTOCOL, `aas.token.${token}`];
}

export const MIN_DELAY_MS = 500;
export const MAX_DELAY_MS = 8000;

/** ws url with the resume point: ws://127.0.0.1:17875/api/v1/events?afterSeq=N */
export function wsUrlFor(session: Session, afterSeq: number): string {
  const n = Number.isSafeInteger(afterSeq) && afterSeq > 0 ? afterSeq : 0;
  return `${session.wsUrl}?afterSeq=${n}`;
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
  private delay = MIN_DELAY_MS;

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
    const ws = new WS(wsUrlFor(this.session, this.store.get().lastSeq), wsProtocols(this.session.token));
    this.ws = ws;
    ws.onopen = () => {
      if (ws.protocol !== WS_PROTOCOL) {
        // wrong or missing negotiation (e.g. a server echoing the token): refuse the socket, retry later
        this.store.dispatch({ type: "protocol", value: ws.protocol || "" });
        ws.close();
        return;
      }
      // the backoff is reset only by an accepted frame (see onFrame), so a server that opens and then
      // immediately produces a gap again cannot make the client spin
      this.store.dispatch({ type: "connection", value: "open" });
    };
    ws.onmessage = (m: MessageEvent) => this.onFrame(ws, m.data);
    ws.onclose = () => {
      if (this.ws === ws) this.ws = null;
      this.store.dispatch({ type: "connection", value: "offline" });
      if (this.stopped) return;
      this.timer = setTimeout(() => this.connect(), this.delay);
      this.delay = Math.min(Math.max(this.delay * 2, MIN_DELAY_MS), MAX_DELAY_MS);
    };
    ws.onerror = () => ws.close();
  }

  private onFrame(ws: WebSocket, data: unknown): void {
    if (ws !== this.ws) return;
    const g = parseGapFrame(data);
    if (g) {
      this.store.dispatch({ type: "gap", gap: { from: g.after + 1, to: g.before - 1, reason: "missing rows" } });
      return;
    }
    const r = parseFrame(data);
    if (!r.ok) {
      this.store.dispatch({ type: "dropped" });
      return;
    }
    const ev = r.event;
    const lastSeq = this.store.get().lastSeq;
    // every frame, the first one included: the server sets prev to the seq it sent before (afterSeq for the first).
    // A missing prev or prev != lastSeq means events were lost between us and the server.
    if (this.opts.gapDetection !== false && ev.seq > lastSeq && (ev.prev === null || ev.prev !== lastSeq)) {
      this.store.dispatch({ type: "gap", gap: { from: lastSeq + 1, to: Math.max(lastSeq + 1, ev.seq - 1), reason: "resync" } });
      // reconnect (the close handler waits this.delay >= MIN_DELAY_MS, doubling while no frame gets through)
      ws.close();
      return;
    }
    this.delay = MIN_DELAY_MS;
    this.store.dispatch({ type: "event", event: ev });
  }
}
