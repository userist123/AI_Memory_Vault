// LiveClient transport (afterSeq resume, prev-based gap detection; P6f client side) and Annex-B parsing.
import { describe, expect, it, vi } from "../../../apps/desktop/testkit/unit";
import { LiveClient } from "../../../apps/desktop/src/lib/live";
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
  frame(seq: number, prev: number) {
    this.onmessage?.({ data: JSON.stringify({ seq, prev, kind: "build.log", payload: { line: `l${seq}` } }) });
  }
}

const session = makeSession("http://127.0.0.1:17875", "tok_0123456789abcdef")!;

describe("session", () => {
  it("accepts only loopback api bases and token-shaped tokens", () => {
    expect(session.wsUrl).toBe("ws://127.0.0.1:17875/api/v1/events");
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
    expect(JSON.parse(ws1.sent[0]!)).toEqual({ afterSeq: 0 });
    ws1.frame(1, 0);
    ws1.frame(2, 1);
    ws1.frame(5, 4); // 3 and 4 were lost: prev != lastSeq
    expect(store.get().lastSeq).toBe(2);
    expect(store.get().gaps[0]).toMatchObject({ from: 3, to: 4 });
    expect(ws1.closed).toBe(true);
    vi.advanceTimersByTime(10);
    const ws2 = FakeWS.all[1]!;
    ws2.onopen?.();
    expect(JSON.parse(ws2.sent[0]!)).toEqual({ afterSeq: 2 });
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
    expect(JSON.parse(ok.sent[0]!)).toEqual({ afterSeq: 0 });
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
