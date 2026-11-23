// S8 review fixes (UI side): WS frames carry a seq, a gap or a decoder error makes the client resync (reconnect with
// bounded backoff -> the server primes SPS/PPS + GOP), and the first second after a (re)connect allows the prime.
import { afterEach, describe, expect, it, vi } from "../../../apps/desktop/testkit/unit";
import { createLiveFeeder, type DecoderLike } from "../../../apps/desktop/src/lib/mirror";
import {
  VideoClient,
  VIDEO_MAX_BPS,
  VIDEO_MAX_FRAME,
  VIDEO_PRIME_MAX,
  VIDEO_SEQ_HEADER,
} from "../../../apps/desktop/src/lib/video";
import { makeSession } from "../../../apps/desktop/src/lib/session";
import { MIN_DELAY_MS, MAX_DELAY_MS } from "../../../apps/desktop/src/lib/live";
import { MIRROR_FIXTURE_B64 } from "../../../apps/desktop/src/fixtures/mirrorFixture";

const session = makeSession("http://127.0.0.1:17875", "tok_0123456789abcdef")!;
const PID = "prj_0123456789abcdef";

class FakeWS {
  static all: FakeWS[] = [];
  binaryType = "blob";
  onopen: (() => void) | null = null;
  onmessage: ((m: { data: unknown }) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  protocol = "aas.v1";
  constructor(
    public url: string,
    public protocols: string[],
  ) {
    FakeWS.all.push(this);
  }
  send() {}
  close() {
    if (this.closed) return;
    this.closed = true;
    this.onclose?.();
  }
}

function frame(seq: number, body: number[] | number): ArrayBuffer {
  const len = typeof body === "number" ? body : body.length;
  const b = new Uint8Array(VIDEO_SEQ_HEADER + len);
  new DataView(b.buffer).setUint32(0, seq);
  if (typeof body !== "number") b.set(body, VIDEO_SEQ_HEADER);
  return b.buffer;
}

function client() {
  FakeWS.all = [];
  const chunks: number[][] = [];
  let resets = 0;
  const c = new VideoClient(session, PID, (b) => chunks.push(Array.from(new Uint8Array(b))), () => resets++, {
    WebSocketImpl: FakeWS as unknown as typeof WebSocket,
  });
  c.start();
  const open = (i: number) => {
    const ws = FakeWS.all[i]!;
    ws.onopen?.();
    return ws;
  };
  return { c, chunks, resets: () => resets, open };
}

afterEach(() => {
  vi.useRealTimers();
});

describe("VideoClient seq (review M1)", () => {
  it("strips the 4-byte seq header and forwards contiguous frames", () => {
    vi.useFakeTimers();
    const { c, chunks, open } = client();
    const ws = open(0);
    ws.onmessage?.({ data: frame(1, [0, 0, 0, 1, 0x67]) });
    ws.onmessage?.({ data: frame(2, [0x42]) });
    expect(chunks).toEqual([[0, 0, 0, 1, 0x67], [0x42]]);
    expect(ws.closed).toBe(false);
    ws.onmessage?.({ data: frame(3, VIDEO_MAX_FRAME - VIDEO_SEQ_HEADER) }); // exactly the cap, header included
    expect(chunks).toHaveLength(3);
    c.stop();
  });

  it("a seq gap drops the frame and resyncs: reconnect, the server primes again, reset on open", () => {
    vi.useFakeTimers();
    const { c, chunks, resets, open } = client();
    const ws = open(0);
    ws.onmessage?.({ data: frame(1, [1]) });
    ws.onmessage?.({ data: frame(3, [3]) }); // frame 2 lost (server-side drop)
    expect(chunks).toEqual([[1]]);
    expect(ws.closed).toBe(true);
    expect(c.gaps).toBe(1);
    vi.advanceTimersByTime(MIN_DELAY_MS);
    expect(FakeWS.all).toHaveLength(2);
    const ws2 = open(1);
    expect(resets()).toBe(2);
    ws2.onmessage?.({ data: frame(1, [9]) }); // a new socket starts at seq 1 again
    expect(chunks).toEqual([[1], [9]]);
    c.stop();
  });

  it("the first frame after open must be seq 1; a frame without a full header closes the socket", () => {
    vi.useFakeTimers();
    const { c, chunks, open } = client();
    const ws = open(0);
    ws.onmessage?.({ data: frame(2, [2]) });
    expect(chunks).toEqual([]);
    expect(ws.closed).toBe(true);
    vi.advanceTimersByTime(MIN_DELAY_MS);
    const ws2 = open(1);
    ws2.onmessage?.({ data: new Uint8Array([0, 0, 1]).buffer });
    expect(chunks).toEqual([]);
    expect(ws2.closed).toBe(true);
    c.stop();
  });
});

describe("VideoClient.resync (decoder error, review M1 c)", () => {
  it("reconnects with a bounded, growing backoff; never an immediate reconnect", () => {
    vi.useFakeTimers();
    const { c, open } = client();
    let ws = open(0);
    const waits: number[] = [];
    for (let k = 0; k < 6; k++) {
      c.resync();
      expect(ws.closed).toBe(true);
      const n = FakeWS.all.length;
      let waited = 0;
      while (FakeWS.all.length === n && waited < 60_000) {
        vi.advanceTimersByTime(100);
        waited += 100;
      }
      waits.push(waited);
      ws = open(FakeWS.all.length - 1);
      ws.onmessage?.({ data: frame(1, [1]) }); // frames flow, but the decoder keeps failing
    }
    expect(waits[0]).toBe(MIN_DELAY_MS);
    for (let k = 1; k < waits.length; k++) expect(waits[k]!).toBeGreaterThanOrEqual(waits[k - 1]!);
    expect(Math.max(...waits)).toBe(MAX_DELAY_MS);
    c.stop();
  });

  it("a resync while one is pending is ignored; after a quiet period the backoff starts again", () => {
    vi.useFakeTimers();
    const { c, open } = client();
    open(0);
    c.resync();
    c.resync();
    vi.advanceTimersByTime(MIN_DELAY_MS);
    expect(FakeWS.all).toHaveLength(2);
    open(1);
    vi.advanceTimersByTime(60_000); // quiet
    c.resync();
    vi.advanceTimersByTime(MIN_DELAY_MS);
    expect(FakeWS.all).toHaveLength(3);
    c.stop();
    c.resync(); // stopped: nothing
    vi.advanceTimersByTime(MAX_DELAY_MS);
    expect(FakeWS.all).toHaveLength(3);
  });
});

describe("VideoClient budget (review L2)", () => {
  it("the first second after open allows the late-viewer prime on top of the live budget", () => {
    vi.useFakeTimers();
    const { c, chunks, open } = client();
    const ws = open(0);
    const body = VIDEO_MAX_FRAME - VIDEO_SEQ_HEADER;
    const n = Math.floor((VIDEO_MAX_BPS + VIDEO_PRIME_MAX) / VIDEO_MAX_FRAME);
    for (let i = 1; i <= n; i++) ws.onmessage?.({ data: frame(i, body) });
    expect(chunks).toHaveLength(n);
    expect(ws.closed).toBe(false);
    vi.advanceTimersByTime(1000); // later windows: the live budget only
    const live = Math.floor(VIDEO_MAX_BPS / VIDEO_MAX_FRAME);
    for (let i = n + 1; i <= n + live; i++) ws.onmessage?.({ data: frame(i, body) });
    expect(ws.closed).toBe(false);
    for (let i = n + live + 1; i <= n + live + 4; i++) ws.onmessage?.({ data: frame(i, body) });
    expect(ws.closed).toBe(true); // over the live budget
    expect(VIDEO_PRIME_MAX).toBeGreaterThanOrEqual(2 * 1024 * 1024);
    c.stop();
  });
});

// ---------------------------------------------------------------- feeder: decoder error -> resync -> live again
function fixture(): Uint8Array {
  const bin = atob(MIRROR_FIXTURE_B64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

class FakeDecoder implements DecoderLike {
  state = "unconfigured";
  chunks = 0;
  failOnDecode = false;
  constructor(
    public output: (f: { displayWidth: number; displayHeight: number; close(): void }) => void,
    public error: (e: { message: string }) => void,
  ) {}
  configure() {
    this.state = "configured";
  }
  decode() {
    if (this.failOnDecode) throw new Error("boom");
    this.chunks++;
    this.output({ displayWidth: 180, displayHeight: 320, close() {} });
  }
  close() {
    this.state = "closed";
  }
}

function feeder() {
  const decoders: FakeDecoder[] = [];
  const statuses: Array<[string, string]> = [];
  let resyncs = 0;
  const f = createLiveFeeder({
    createDecoder: (o, e) => {
      const d = new FakeDecoder(o, e);
      decoders.push(d);
      return d;
    },
    makeChunk: (type, timestamp, data) => ({ type, timestamp, data }),
    getCanvas: () => ({ width: 0, height: 0, getContext: () => ({ drawImage: vi.fn() }) }),
    status: (text, state) => statuses.push([text, state]),
    supported: () => Promise.resolve(true),
    resync: () => resyncs++,
  });
  return { f, decoders, statuses, resyncs: () => resyncs };
}

const flush = () => new Promise((r) => setTimeout(r, 0));

describe("live feeder recovery (review M1 c)", () => {
  it("decoder error callback -> resync requested -> reconnect stream -> a new decoder is live again", async () => {
    const { f, decoders, statuses, resyncs } = feeder();
    f.push(fixture());
    await flush();
    f.end();
    expect(decoders).toHaveLength(1);
    decoders[0]!.error({ message: "decode failed" });
    expect(resyncs()).toBe(1);
    expect(statuses.at(-1)![1]).toBe("error");
    f.reset(); // the VideoClient reconnected: the server primes SPS/PPS + GOP
    f.push(fixture());
    await flush();
    f.end();
    expect(decoders).toHaveLength(2);
    expect(decoders[1]!.chunks).toBeGreaterThan(0);
    expect(statuses.at(-1)![1]).toBe("decoding");
  });

  it("a decode() that throws also requests a resync and recovers", async () => {
    const { f, decoders, resyncs } = feeder();
    f.push(fixture().subarray(0, 2000));
    await flush();
    decoders[0]!.failOnDecode = true;
    f.push(fixture().subarray(2000));
    f.end();
    expect(resyncs()).toBe(1);
    f.reset();
    f.push(fixture());
    await flush();
    f.end();
    expect(decoders.at(-1)!.chunks).toBeGreaterThan(0);
  });
});
