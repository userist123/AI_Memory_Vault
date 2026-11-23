// S8: live emulator mirror (binary WS -> streaming Annex-B feeder -> WebCodecs) and capture thumbnails (REST by id).
import { afterEach, cleanup, createElement, describe, expect, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { accessUnits, splitNals } from "../../../apps/desktop/src/lib/annexb";
import { createLiveFeeder, LIVE_MAX_QUEUE, type DecoderLike } from "../../../apps/desktop/src/lib/mirror";
import { VideoClient, videoUrl, VIDEO_MAX_FRAME } from "../../../apps/desktop/src/lib/video";
import { CAPTURE_MAX_BYTES, fetchCapture } from "../../../apps/desktop/src/lib/api";
import { makeSession } from "../../../apps/desktop/src/lib/session";
import { parseObject } from "../../../apps/desktop/src/lib/events";
import { initialState, LIMITS, reducer, type State } from "../../../apps/desktop/src/lib/store";
import { CaptureStrip } from "../../../apps/desktop/src/components/Captures";
import { MIRROR_FIXTURE_B64 } from "../../../apps/desktop/src/fixtures/mirrorFixture";

const session = makeSession("http://127.0.0.1:17875", "tok_0123456789abcdef")!;
const PID = "prj_0123456789abcdef";

function fixture(): Uint8Array {
  const bin = atob(MIRROR_FIXTURE_B64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

class FakeDecoder implements DecoderLike {
  state = "unconfigured";
  codec = "";
  chunks: Array<{ type: string; timestamp: number; data: Uint8Array }> = [];
  decodeQueueSize = 0;
  constructor(
    public output: (f: { displayWidth: number; displayHeight: number; close(): void }) => void,
    public error: (e: { message: string }) => void,
  ) {}
  configure(cfg: { codec: string }) {
    this.state = "configured";
    this.codec = cfg.codec;
  }
  decode(c: unknown) {
    this.chunks.push(c as { type: string; timestamp: number; data: Uint8Array });
    this.output({ displayWidth: 180, displayHeight: 320, close() {} });
  }
  close() {
    this.state = "closed";
  }
}

function feederSetup(supported = true) {
  const decoders: FakeDecoder[] = [];
  const statuses: Array<[string, string]> = [];
  const canvas = { width: 0, height: 0, getContext: () => ({ drawImage: vi.fn() }) };
  const f = createLiveFeeder({
    createDecoder: (o, e) => {
      const d = new FakeDecoder(o, e);
      decoders.push(d);
      return d;
    },
    makeChunk: (type, timestamp, data) => ({ type, timestamp, data }),
    getCanvas: () => canvas,
    status: (text, state) => statuses.push([text, state]),
    supported: () => Promise.resolve(supported),
  });
  return { f, decoders, statuses, canvas };
}

const flush = () => new Promise((r) => setTimeout(r, 0));

describe("live mirror feeder", () => {
  it("decodes the fixture stream fed in arbitrary chunks, key frame first, codec from the SPS", async () => {
    const stream = fixture();
    const expected = accessUnits(splitNals(stream));
    const { f, decoders, statuses, canvas } = feederSetup();
    let i = 0;
    let n = 1;
    while (i < stream.length) {
      f.push(stream.slice(i, i + n));
      i += n;
      n = (n * 7 + 3) % 977 + 1; // irregular cuts: NALs and start codes straddle chunks
      await flush();
    }
    f.end(); // end of stream: the last access unit is complete
    expect(decoders).toHaveLength(1);
    const d = decoders[0]!;
    expect(d.codec).toMatch(/^avc1\.[0-9a-f]{6}$/);
    expect(d.chunks[0]!.type).toBe("key");
    expect(d.chunks.length).toBe(expected.length);
    expect(d.chunks.map((c) => Array.from(c.data))).toEqual(expected.map((a) => Array.from(a.data)));
    expect(canvas.width).toBe(180);
    expect(statuses.some(([, s]) => s === "decoding")).toBe(true);
  });

  it("drops delta frames before the first key frame and when the decoder queue is full", async () => {
    const stream = fixture();
    const nals = splitNals(stream);
    const sc = new Uint8Array([0, 0, 0, 1]);
    const join = (list: Uint8Array[]) => {
      const out: number[] = [];
      for (const x of list) out.push(...sc, ...x);
      return new Uint8Array(out);
    };
    const deltas = nals.filter((x) => (x[0]! & 0x1f) === 1);
    const params = nals.filter((x) => [7, 8].includes(x[0]! & 0x1f));
    const idr = nals.filter((x) => (x[0]! & 0x1f) === 5);
    const { f, decoders } = feederSetup();
    f.push(join([...params, deltas[0]!, deltas[1]!])); // no IDR yet
    await flush();
    f.push(join([idr[0]!, deltas[2]!]));
    await flush();
    f.end();
    const d = decoders[0]!;
    expect(d.chunks.map((c) => c.type)).toEqual(["key", "delta"]);
    d.decodeQueueSize = LIVE_MAX_QUEUE + 1; // a slow decoder: deltas are dropped until the next key frame
    f.push(join([deltas[3]!, deltas[4]!]));
    f.end();
    expect(d.chunks).toHaveLength(2);
  });

  it("reports unsupported when the codec is not supported and decodes nothing", async () => {
    const { f, decoders, statuses } = feederSetup(false);
    f.push(fixture());
    await flush();
    f.end();
    expect(decoders).toHaveLength(0);
    expect(statuses.at(-1)![1]).toBe("unsupported");
  });

  it("caps its buffer: a stream without start codes never grows memory", () => {
    const { f, statuses } = feederSetup();
    for (let i = 0; i < 40; i++) f.push(new Uint8Array(64 * 1024).fill(7));
    expect(f.buffered()).toBeLessThanOrEqual(2 * 1024 * 1024);
    expect(statuses.length).toBeGreaterThanOrEqual(0);
  });
});

class FakeWS {
  static all: FakeWS[] = [];
  binaryType = "blob";
  onopen: (() => void) | null = null;
  onmessage: ((m: { data: unknown }) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  protocol = "aas.v1";
  sent: unknown[] = [];
  constructor(
    public url: string,
    public protocols: string[],
  ) {
    FakeWS.all.push(this);
  }
  send(x: unknown) {
    this.sent.push(x);
  }
  close() {
    if (this.closed) return;
    this.closed = true;
    this.onclose?.();
  }
}

describe("VideoClient", () => {
  it("connects per project with aas.v1 + aas.token, binary frames only, resets the decoder on every (re)connect", () => {
    vi.useFakeTimers();
    FakeWS.all = [];
    const chunks: number[] = [];
    let resets = 0;
    const c = new VideoClient(session, PID, (b) => chunks.push(b.byteLength), () => resets++, {
      WebSocketImpl: FakeWS as unknown as typeof WebSocket,
    });
    c.start();
    const ws = FakeWS.all[0]!;
    expect(ws.url).toBe(`ws://127.0.0.1:17875/api/v1/device/video?projectId=${PID}`);
    expect(videoUrl(session, PID)).toBe(ws.url);
    expect(ws.protocols).toEqual(["aas.v1", "aas.token.tok_0123456789abcdef"]);
    expect(ws.binaryType).toBe("arraybuffer");
    ws.onopen?.();
    expect(resets).toBe(1);
    const first = new ArrayBuffer(10); // review M1: 4-byte big-endian seq (1 first) + 6 bytes of h264
    new DataView(first).setUint32(0, 1);
    ws.onmessage?.({ data: first });
    ws.onmessage?.({ data: "text frame" }); // not video: ignored
    expect(chunks).toEqual([6]); // the seq header is stripped
    expect(ws.sent).toEqual([]);
    const big = new ArrayBuffer(VIDEO_MAX_FRAME + 1);
    new DataView(big).setUint32(0, 2);
    ws.onmessage?.({ data: big }); // oversize: socket dropped, nothing forwarded
    expect(chunks).toEqual([6]);
    expect(ws.closed).toBe(true);
    vi.advanceTimersByTime(600);
    expect(FakeWS.all).toHaveLength(2);
    FakeWS.all[1]!.onopen?.();
    expect(resets).toBe(2);
    c.stop();
    vi.useRealTimers();
  });

  it("refuses a socket that did not negotiate aas.v1 and never connects for an invalid project id", () => {
    FakeWS.all = [];
    const chunks: number[] = [];
    const c = new VideoClient(session, PID, (b) => chunks.push(b.byteLength), () => {}, {
      WebSocketImpl: FakeWS as unknown as typeof WebSocket,
    });
    c.start();
    const ws = FakeWS.all[0]!;
    ws.protocol = "aas.token.tok_0123456789abcdef";
    ws.onopen?.();
    ws.onmessage?.({ data: new ArrayBuffer(10) });
    expect(ws.closed).toBe(true);
    expect(chunks).toEqual([]);
    c.stop();
    FakeWS.all = [];
    const bad = new VideoClient(session, "../x", () => {}, () => {}, { WebSocketImpl: FakeWS as unknown as typeof WebSocket });
    bad.start();
    expect(FakeWS.all).toHaveLength(0);
  });
});

// ---------------------------------------------------------------- capture thumbnails
const SHA = "a".repeat(64);
const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3]);

async function sha256hex(b: Uint8Array<ArrayBuffer>): Promise<string> {
  const d = await crypto.subtle.digest("SHA-256", b);
  return Array.from(new Uint8Array(d), (x) => x.toString(16).padStart(2, "0")).join("");
}

function mockFetch(body: Uint8Array<ArrayBuffer>, init: { status?: number; type?: string } = {}) {
  const calls: Array<{ url: string; init: RequestInit }> = [];
  const f = vi.fn(async (url: string, i: RequestInit) => {
    calls.push({ url, init: i });
    return new Response(body, { status: init.status ?? 200, headers: { "Content-Type": init.type ?? "image/png" } });
  });
  vi.stubGlobal("fetch", f);
  return calls;
}

describe("fetchCapture", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("fetches by sha256 with the bearer token and returns a PNG blob whose sha256 is the id", async () => {
    const sha = await sha256hex(PNG);
    const calls = mockFetch(PNG);
    const blob = await fetchCapture(session, sha);
    expect(calls[0]!.url).toBe(`http://127.0.0.1:17875/api/v1/captures/${sha}`);
    expect((calls[0]!.init.headers as Record<string, string>).Authorization).toBe("Bearer tok_0123456789abcdef");
    expect(calls[0]!.init.credentials).toBe("omit");
    expect(blob.type).toBe("image/png");
    expect(blob.size).toBe(PNG.length);
  });

  it("refuses a bad id without a request, and a body that is not the PNG with that sha256", async () => {
    const calls = mockFetch(PNG);
    await expect(fetchCapture(session, "../../x")).rejects.toThrow();
    expect(calls).toHaveLength(0);
    await expect(fetchCapture(session, SHA)).rejects.toThrow(/sha256/); // content does not hash to the id
    const sha = await sha256hex(PNG);
    mockFetch(PNG, { type: "text/html" });
    await expect(fetchCapture(session, sha)).rejects.toThrow();
    const notPng = new Uint8Array([60, 104, 116, 109, 108, 62]);
    mockFetch(notPng);
    await expect(fetchCapture(session, await sha256hex(notPng))).rejects.toThrow();
    expect(CAPTURE_MAX_BYTES).toBe(8 * 1024 * 1024);
  });
});

function withCaptures(n: number): State {
  let s = initialState;
  for (let i = 1; i <= n; i++) {
    const frame = ["phone", "tablet", "desktop"][i % 3];
    const r = parseObject({
      seq: i,
      kind: "preview.captured",
      runId: "r1",
      projectId: PID,
      payload: { revision: i, frame, sha256: i.toString(16).padStart(64, "0") },
    });
    if (!r.ok) throw new Error(r.reason);
    s = reducer(s, { type: "event", event: r.event });
  }
  return s;
}

describe("captures in the store and the timeline", () => {
  afterEach(() => cleanup());

  it("keeps the most recent captures (bounded) with frame, viewport and revision; bad ids are ignored", () => {
    const s = withCaptures(LIMITS.captures + 5);
    expect(s.captures).toHaveLength(LIMITS.captures);
    expect(s.captures.at(-1)).toMatchObject({ frame: "desktop", revision: LIMITS.captures + 5 });
    const bad = parseObject({ seq: 999, kind: "preview.captured", payload: { revision: 1, frame: "phone", sha256: "zz" } });
    if (!bad.ok) throw new Error("parse");
    expect(reducer(initialState, { type: "event", event: bad.event }).captures).toHaveLength(0);
  });

  it("renders thumbnails as blob: images with the viewport label, never the API URL", async () => {
    const s = withCaptures(2);
    const load = vi.fn(async () => "blob:http://127.0.0.1/1234");
    const r = render(createElement(CaptureStrip, { captures: s.captures, load }));
    await vi.waitFor(() => expect(r.container.querySelectorAll("img")).toHaveLength(2));
    for (const img of r.container.querySelectorAll("img")) expect(img.getAttribute("src")).toMatch(/^blob:/);
    expect(r.container.textContent).toContain("Tablet 834×1112 · rev 1");
    expect(r.container.textContent).toContain("Desktop 1440×900 · rev 2");
    expect(load).toHaveBeenCalledWith("1".padStart(64, "0"));
  });

  it("a failed load shows a text placeholder, not a broken image", async () => {
    const s = withCaptures(1);
    const r = render(createElement(CaptureStrip, { captures: s.captures, load: async () => Promise.reject(new Error("x")) }));
    await vi.waitFor(() => expect(r.container.textContent).toContain("capture unavailable"));
    expect(r.container.querySelectorAll("img")).toHaveLength(0);
  });
});
