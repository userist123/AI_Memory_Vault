// S9a B9: the mirror decoder runs in software ("prefer-software", parsing stays in the sandboxed renderer), takes only
// NAL types 1/5/7/8, accepts only bounded SPS (8-bit 4:2:0, <= 4096 px, profile 66/77/100), caps the access-unit
// size and decodes nothing before SPS + PPS + IDR.
import { describe, expect, it, vi } from "../../../apps/desktop/testkit/unit";
import { createLiveFeeder, createMirrorPlayer, type DecoderLike } from "../../../apps/desktop/src/lib/mirror";
import {
  DECODER_CONFIG_BASE,
  MAX_AU_BYTES,
  NAL_ALLOW,
  nalType,
  parseSps,
  splitNals,
} from "../../../apps/desktop/src/lib/annexb";
import { MIRROR_FIXTURE_B64 } from "../../../apps/desktop/src/fixtures/mirrorFixture";

function fixture(): Uint8Array {
  const bin = atob(MIRROR_FIXTURE_B64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

/** Minimal exp-Golomb SPS writer (fields up to frame_mbs_only_flag, then a stop bit). */
function sps(o: { profile: number; chroma?: number; depthL?: number; depthC?: number; wMbs: number; hMbs: number;
  frameMbsOnly?: number; scaling?: number }): Uint8Array {
  const bits: number[] = [];
  const u = (v: number, n: number) => {
    for (let i = n - 1; i >= 0; i--) bits.push((v >> i) & 1);
  };
  const ue = (v: number) => {
    const x = v + 1;
    const n = Math.floor(Math.log2(x));
    u(0, n);
    u(x, n + 1);
  };
  ue(0); // seq_parameter_set_id
  if ([100, 110, 122, 244, 44, 83, 86, 118, 128].includes(o.profile)) {
    ue(o.chroma ?? 1);
    if ((o.chroma ?? 1) === 3) u(0, 1);
    ue(o.depthL ?? 0);
    ue(o.depthC ?? 0);
    u(0, 1); // qpprime_y_zero_transform_bypass_flag
    u(o.scaling ?? 0, 1);
  }
  ue(0); // log2_max_frame_num_minus4
  ue(0); // pic_order_cnt_type
  ue(0); // log2_max_pic_order_cnt_lsb_minus4
  ue(1); // max_num_ref_frames
  u(0, 1); // gaps
  ue(o.wMbs - 1);
  ue(o.hMbs - 1);
  u(o.frameMbsOnly ?? 1, 1);
  u(1, 1); // rbsp stop bit (enough for the bounds check)
  while (bits.length % 8) bits.push(0);
  const body: number[] = [];
  for (let i = 0; i < bits.length; i += 8) body.push(parseInt(bits.slice(i, i + 8).join(""), 2));
  return new Uint8Array([0x67, o.profile, 0, 30, ...body]);
}

const PPS = new Uint8Array([0x68, 0xce, 0x38, 0x80]);
const idr = (n = 16) => {
  const a = new Uint8Array(n + 2).fill(0x11);
  a[0] = 0x65;
  a[1] = 0x88;
  return a;
};
const delta = () => new Uint8Array([0x41, 0x9a, 0x22, 0x22]);
const sei = () => new Uint8Array([0x06, 0x05, 0x01, 0xff, 0x80]);
const aud = () => new Uint8Array([0x09, 0xf0]);

function annexb(...nals: Uint8Array[]): Uint8Array {
  const out = new Uint8Array(nals.reduce((k, n) => k + 4 + n.length, 0));
  let o = 0;
  for (const n of nals) {
    out.set([0, 0, 0, 1], o);
    out.set(n, o + 4);
    o += 4 + n.length;
  }
  return out;
}

class FakeDecoder implements DecoderLike {
  state = "unconfigured";
  configs: unknown[] = [];
  chunks: Array<{ type: string; data: Uint8Array }> = [];
  constructor(public output: (f: { displayWidth: number; displayHeight: number; close(): void }) => void) {}
  configure(cfg: unknown) {
    this.configs.push(cfg);
    this.state = "configured";
  }
  decode(c: unknown) {
    this.chunks.push(c as { type: string; data: Uint8Array });
    this.output({ displayWidth: 180, displayHeight: 320, close() {} });
  }
  close() {
    this.state = "closed";
  }
}

function feeder() {
  const decoders: FakeDecoder[] = [];
  const statuses: Array<[string, string]> = [];
  const supportedCalls: string[] = [];
  const f = createLiveFeeder({
    createDecoder: (o) => {
      const d = new FakeDecoder(o);
      decoders.push(d);
      return d;
    },
    makeChunk: (type, _ts, data) => ({ type, data }),
    getCanvas: () => ({ width: 0, height: 0, getContext: () => ({ drawImage: vi.fn() }) }),
    status: (text, state) => statuses.push([text, state]),
    supported: (c) => {
      supportedCalls.push(c);
      return Promise.resolve(true);
    },
  });
  return { f, decoders, statuses, supportedCalls };
}

const flush = () => new Promise((r) => setTimeout(r, 0));
const GOOD = sps({ profile: 66, wMbs: 34, hMbs: 74 }); // 544 x 1184

describe("SPS bounds", () => {
  it("parses the real fixture SPS and the synthetic ones", () => {
    const real = splitNals(fixture()).find((n) => nalType(n) === 7)!;
    expect(parseSps(real)).toMatchObject({ profile: 66, chromaFormat: 1, bitDepthLuma: 8, width: 192, height: 320 });
    expect(parseSps(GOOD)).toMatchObject({ profile: 66, width: 544, height: 1184, ok: true });
  });
  it("refuses other profiles, 10-bit, 4:4:4 / 4:2:2, scaling matrices, oversize and field coding", () => {
    expect(parseSps(sps({ profile: 88, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 110, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 100, depthL: 2, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 100, depthC: 2, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 100, chroma: 3, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 100, chroma: 2, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 100, scaling: 1, wMbs: 34, hMbs: 74 }))!.ok).toBe(false);
    expect(parseSps(sps({ profile: 66, wMbs: 257, hMbs: 10 }))!.ok).toBe(false); // 4112 px wide
    expect(parseSps(sps({ profile: 66, wMbs: 10, hMbs: 129, frameMbsOnly: 0 }))!.ok).toBe(false); // field, 4128 high
    expect(parseSps(sps({ profile: 100, wMbs: 256, hMbs: 256 }))!.ok).toBe(true); // 4096 x 4096 high profile 8-bit
    expect(parseSps(sps({ profile: 77, wMbs: 34, hMbs: 74 }))!.ok).toBe(true);
    expect(parseSps(new Uint8Array([0x67, 66]))).toBeNull(); // truncated
    expect(parseSps(new Uint8Array([0x67, 66, 0, 30, 0, 0, 0, 0]))).toBeNull(); // exp-Golomb runs off the end
  });
});

describe("live feeder hardening", () => {
  it("configures the decoder with prefer-software", async () => {
    const { f, decoders } = feeder();
    f.push(annexb(GOOD, PPS, idr()));
    await flush();
    f.end();
    expect(DECODER_CONFIG_BASE).toEqual({ optimizeForLatency: true, hardwareAcceleration: "prefer-software" });
    expect(decoders[0]!.configs[0]).toMatchObject({ hardwareAcceleration: "prefer-software", optimizeForLatency: true });
  });

  it("drops a stream whose SPS is outside the bounds (nothing configured, nothing decoded)", async () => {
    const { f, decoders, statuses, supportedCalls } = feeder();
    f.push(annexb(sps({ profile: 100, depthL: 2, wMbs: 34, hMbs: 74 }), PPS, idr(), delta()));
    await flush();
    f.end();
    expect(decoders).toHaveLength(0);
    expect(supportedCalls).toHaveLength(0);
    expect(statuses.at(-1)![1]).toBe("unsupported");
  });

  it("passes only NAL types 1, 5, 7, 8 to the decoder", async () => {
    expect([...NAL_ALLOW].sort()).toEqual([1, 5, 7, 8]);
    const { f, decoders } = feeder();
    f.push(annexb(aud(), GOOD, PPS, sei(), idr(), new Uint8Array([0x0c, 0xff, 0xff]), aud(), delta(),
      new Uint8Array([0x14, 0x01, 0x02])));
    await flush();
    f.end();
    const all = decoders[0]!.chunks.flatMap((c) => splitNals(c.data).map(nalType));
    expect(all).toEqual([7, 8, 5, 1]);
  });

  it("decodes nothing before SPS + PPS + IDR", async () => {
    const { f, decoders } = feeder();
    f.push(annexb(GOOD, idr(), delta())); // no PPS yet
    await flush();
    f.push(annexb(delta()));
    expect(decoders[0]?.chunks.length ?? 0).toBe(0);
    f.push(annexb(PPS, delta(), delta())); // PPS but no IDR after it
    expect(decoders[0]!.chunks.length).toBe(0);
    f.push(annexb(idr(), delta()));
    f.end();
    expect(decoders[0]!.chunks.map((c) => c.type)).toEqual(["key", "delta"]);
  });

  it("refuses an access unit above MAX_AU_BYTES and waits for the next key frame", async () => {
    const { f, decoders } = feeder();
    f.push(annexb(GOOD, PPS, idr()));
    await flush();
    f.push(annexb(idr(MAX_AU_BYTES + 10)));
    f.push(annexb(delta()));
    f.push(annexb(idr(), delta()));
    f.end();
    const sizes = decoders[0]!.chunks.map((c) => c.data.length);
    expect(Math.max(...sizes)).toBeLessThan(MAX_AU_BYTES);
    expect(decoders[0]!.chunks.map((c) => c.type)).toEqual(["key", "key", "delta"]);
  });

  it("the real fixture still plays (positive control)", async () => {
    const { f, decoders } = feeder();
    f.push(fixture());
    await flush();
    f.end();
    expect(decoders).toHaveLength(1);
    // while the codec probe is pending only the newest GOP is kept (S8 behaviour): the fixture has keyint=10
    expect(decoders[0]!.chunks.length).toBe(10);
    expect(decoders[0]!.chunks[0]!.type).toBe("key");
    expect(decoders[0]!.chunks.slice(1).every((c) => c.type === "delta")).toBe(true);
  });
});

describe("fixture player", () => {
  it("configures prefer-software too", () => {
    vi.useFakeTimers();
    const d: FakeDecoder[] = [];
    const p = createMirrorPlayer({
      createDecoder: (o) => {
        const x = new FakeDecoder(o);
        d.push(x);
        return x;
      },
      makeChunk: (type, _ts, data) => ({ type, data }),
      getCanvas: () => null,
      status: () => {},
    });
    p.start("avc1.42c01e", [{ data: annexb(GOOD, PPS, idr()), key: true }], 25, false);
    expect(d[0]!.configs[0]).toMatchObject({ codec: "avc1.42c01e", hardwareAcceleration: "prefer-software" });
    p.stop();
    vi.useRealTimers();
  });
});
