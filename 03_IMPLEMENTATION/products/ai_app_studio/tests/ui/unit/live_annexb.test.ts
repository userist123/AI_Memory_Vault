// LiveClient transport (afterSeq resume, prev-based gap detection; P6f client side) and Annex-B parsing.
import { describe, expect, it, vi } from "../../../apps/desktop/testkit/unit";
import { LiveClient, wsUrlFor } from "../../../apps/desktop/src/lib/live";
import { makeSession } from "../../../apps/desktop/src/lib/session";
import { createStore } from "../../../apps/desktop/src/lib/store";
import { accessUnits, codecFromSps, splitNals } from "../../../apps/desktop/src/lib/annexb";
import { MIRROR_FIXTURE_B64 } from "../../../apps/desktop/src/fixtures/mirrorFixture";

class FakeWS {
  static all: FakeWS[] = [];
  sent: string[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((m: { data: unknown }) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  /** negotiated subprotocol; the contract server echoes only "aas.v1" */
  protocol = "aas.v1";
  constructor(
    public url: string,
    public protocols: string[],
  ) {
    FakeWS.all.push(this);
  }
  send(s: string) {
    this.sent.push(s);
  }
  close() {
    if (this.closed) return;
    this.closed = true;
    this.onclose?.();
  }
  frame(seq: number, prev: number | null) {
    const o: Record<string, unknown> = { seq, kind: "build.log", payload: { line: `l${seq}` } };
    if (prev !== null) o.prev = prev;
    this.onmessage?.({ data: JSON.stringify(o) });
  }
}

const session = makeSession("http://127.0.0.1:17875", "tok_0123456789abcdef")!;

describe("session", () => {
  it("accepts only loopback api bases and token-shaped tokens", () => {
    expect(session.wsUrl).toBe("ws://127.0.0.1:17875/api/v1/events");
    expect(wsUrlFor(session, 7)).toBe("ws://127.0.0.1:17875/api/v1/events?afterSeq=7");
    expect(makeSession("http://evil.example:17875", "tok_0123456789abcdef")).toBeNull();
    expect(makeSession("http://127.0.0.1:17875", "short")).toBeNull();
    expect(makeSession("http://127.0.0.1:17875/x", "tok_0123456789abcdef")).toBeNull();
  });
});

describe("LiveClient", () => {
  it("offers aas.v1 + aas.token subprotocols, sends afterSeq and resumes after a prev mismatch", () => {
    vi.useFakeTimers();
    FakeWS.all = [];
    const store = createStore();
    const c = new LiveClient(session, store, { WebSocketImpl: FakeWS as unknown as typeof WebSocket });
    c.start();
    const ws1 = FakeWS.all[0]!;
    expect(ws1.protocols).toEqual(["aas.v1", "aas.token.tok_0123456789abcdef"]);
    ws1.onopen?.();
    expect(ws1.url).toBe("ws://127.0.0.1:17875/api/v1/events?afterSeq=0");
    expect(ws1.sent).toEqual([]); // afterSeq travels in the URL (backend: query param), never as a message
    ws1.frame(1, 0);
    ws1.frame(2, 1);
    ws1.frame(5, 4); // 3 and 4 were lost: prev != lastSeq
    expect(store.get().lastSeq).toBe(2);
    expect(store.get().gaps[0]).toMatchObject({ from: 3, to: 4 });
    expect(ws1.closed).toBe(true);
    vi.advanceTimersByTime(499);
    expect(FakeWS.all).toHaveLength(1); // never an immediate reconnect
    vi.advanceTimersByTime(2);
    const ws2 = FakeWS.all[1]!;
    ws2.onopen?.();
    expect(ws2.url).toBe("ws://127.0.0.1:17875/api/v1/events?afterSeq=2");
    expect(ws2.sent).toEqual([]);
    ws2.frame(3, 2);
    ws2.frame(4, 3);
    ws2.frame(5, 4);
    expect(store.get().build.lines.map((l) => l.text)).toEqual(["l1", "l2", "l3", "l4", "l5"]);
    c.stop();
    vi.useRealTimers();
  });

  it("positive control: with gap detection disabled the missing events stay missing", () => {
    FakeWS.all = [];
    const store = createStore();
    const c = new LiveClient(session, store, { WebSocketImpl: FakeWS as unknown as typeof WebSocket, gapDetection: false });
    c.start();
    const ws = FakeWS.all[0]!;
    ws.onopen?.();
    ws.frame(1, 0);
    ws.frame(5, 4);
    expect(store.get().build.lines.map((l) => l.text)).toEqual(["l1", "l5"]);
    expect(FakeWS.all).toHaveLength(1);
    c.stop();
  });

  it("refuses a socket whose server echoed the token or nothing instead of aas.v1", () => {
    vi.useFakeTimers();
    FakeWS.all = [];
    const store = createStore();
    const c = new LiveClient(session, store, { WebSocketImpl: FakeWS as unknown as typeof WebSocket });
    c.start();
    for (const bad of ["aas.token.tok_0123456789abcdef", ""]) {
      const ws = FakeWS.all.at(-1)!;
      ws.protocol = bad;
      ws.onopen?.();
      expect(ws.sent).toEqual([]); // nothing (not even afterSeq) goes out on a mis-negotiated socket
      expect(ws.closed).toBe(true);
      expect(store.get().protocolError).toBe(bad ? "aas.token.<redacted>" : "");
      expect(store.get().connection).toBe("offline");
      vi.advanceTimersByTime(10_000);
    }
    // positive control: a correct echo opens, sends afterSeq and clears the warning
    const ok = FakeWS.all.at(-1)!;
    expect(FakeWS.all).toHaveLength(3);
    ok.onopen?.();
    expect(ok.url).toContain("?afterSeq=0");
    expect(ok.sent).toEqual([]);
    expect(store.get().protocolError).toBeNull();
    c.stop();
    vi.useRealTimers();
  });

  it("invalid frames are counted, not rendered", () => {
    FakeWS.all = [];
    const store = createStore();
    const c = new LiveClient(session, store, { WebSocketImpl: FakeWS as unknown as typeof WebSocket });
    c.start();
    const ws = FakeWS.all[0]!;
    ws.onopen?.();
    ws.onmessage?.({ data: JSON.stringify({ seq: 1, prev: 0, kind: "approval", payload: {} }) });
    ws.onmessage?.({ data: "not json" });
    expect(store.get().dropped).toBe(2);
    expect(store.get().lastSeq).toBe(0);
    c.stop();
  });
});

describe("LiveClient gap detection (review S4 finding 4)", () => {
  const mk = (opts = {}) => {
    FakeWS.all = [];
    const store = createStore();
    const c = new LiveClient(session, store, { WebSocketImpl: FakeWS as unknown as typeof WebSocket, ...opts });
    c.start();
    FakeWS.all[0]!.onopen?.();
    return { store, c };
  };
  it("the first frame is checked too: prev != afterSeq is a gap", () => {
    vi.useFakeTimers();
    const { store, c } = mk();
    const ws = FakeWS.all[0]!;
    ws.frame(5, 4); // nothing before it was delivered on this socket
    expect(store.get().lastSeq).toBe(0);
    expect(store.get().gaps).toHaveLength(1);
    expect(ws.closed).toBe(true);
    c.stop();
    vi.useRealTimers();
  });
  it("a frame without prev is treated as a gap, including the first one", () => {
    vi.useFakeTimers();
    const { store, c } = mk();
    const ws = FakeWS.all[0]!;
    ws.frame(1, null);
    expect(store.get().lastSeq).toBe(0);
    expect(store.get().gaps).toHaveLength(1);
    expect(ws.closed).toBe(true);
    c.stop();
    vi.useRealTimers();
  });
  it("positive control: a contiguous first frame after a resume is accepted", () => {
    vi.useFakeTimers();
    const { store, c } = mk();
    const ws1 = FakeWS.all[0]!;
    ws1.frame(1, 0);
    ws1.frame(2, 1);
    ws1.close();
    vi.advanceTimersByTime(600);
    const ws2 = FakeWS.all[1]!;
    ws2.onopen?.();
    ws2.frame(3, 2);
    expect(store.get().lastSeq).toBe(3);
    expect(store.get().gaps).toHaveLength(0);
    c.stop();
    vi.useRealTimers();
  });
  it("repeated gaps back off exponentially (>= 0.5 s, capped), never an immediate reconnect", () => {
    vi.useFakeTimers();
    const { c } = mk();
    const waits: number[] = [];
    for (let i = 0; i < 7; i++) {
      const ws = FakeWS.all.at(-1)!;
      ws.onopen?.();
      ws.frame(10, 3); // always a gap
      const n = FakeWS.all.length;
      let t = 0;
      while (FakeWS.all.length === n && t < 20_000) {
        vi.advanceTimersByTime(50);
        t += 50;
      }
      waits.push(t);
    }
    expect(waits[0]!).toBeGreaterThanOrEqual(500);
    for (let i = 1; i < waits.length; i++) expect(waits[i]!).toBeGreaterThanOrEqual(waits[i - 1]!);
    expect(waits[2]!).toBeGreaterThanOrEqual(2000);
    expect(Math.max(...waits)).toBeLessThanOrEqual(8100);
    c.stop();
    vi.useRealTimers();
  });
  it("progress resets the backoff", () => {
    vi.useFakeTimers();
    const { c } = mk();
    for (let i = 0; i < 3; i++) {
      FakeWS.all.at(-1)!.onopen?.();
      FakeWS.all.at(-1)!.frame(10, 3);
      vi.advanceTimersByTime(9000);
    }
    const ws = FakeWS.all.at(-1)!;
    ws.onopen?.();
    ws.frame(1, 0); // a good frame
    ws.frame(9, 3);
    const n = FakeWS.all.length;
    vi.advanceTimersByTime(600);
    expect(FakeWS.all.length).toBe(n + 1);
    c.stop();
    vi.useRealTimers();
  });
  it("a server gap frame {type: gap, after, before} is recorded and does not count as a rejected frame", () => {
    const { store, c } = mk();
    const ws = FakeWS.all[0]!;
    ws.frame(1, 0);
    ws.onmessage?.({ data: JSON.stringify({ type: "gap", after: 1, before: 4 }) });
    ws.frame(4, 1);
    expect(store.get().gaps[0]).toMatchObject({ from: 2, to: 3 });
    expect(store.get().dropped).toBe(0);
    expect(store.get().lastSeq).toBe(4);
    expect(FakeWS.all).toHaveLength(1);
    c.stop();
  });
});

describe("Annex-B", () => {
  const bytes = Uint8Array.from(atob(MIRROR_FIXTURE_B64), (c) => c.charCodeAt(0));
  it("finds SPS/PPS, derives the avc1 codec string and groups 20 access units", () => {
    const nals = splitNals(bytes);
    expect(codecFromSps(nals)).toMatch(/^avc1\.42[0-9a-f]{2}1e$/);
    const aus = accessUnits(nals);
    expect(aus).toHaveLength(20);
    expect(aus[0]!.key).toBe(true);
    expect(aus.filter((a) => a.key).length).toBe(2);
  });
  it("garbage yields no units", () => {
    expect(accessUnits(splitNals(new Uint8Array([1, 2, 3, 0, 0, 1])))).toEqual([]);
  });
});
