// Mirror decoder lifecycle (review S4 finding 5): an error stops the timer and drops the decoder; frame size is capped.
import { describe, expect, it, vi } from "../../../apps/desktop/testkit/unit";
import { createMirrorPlayer, dimsOk, MAX_DIM, type DecoderLike } from "../../../apps/desktop/src/lib/mirror";

class FakeDecoder implements DecoderLike {
  state = "unconfigured";
  decoded = 0;
  constructor(
    public output: (f: { displayWidth: number; displayHeight: number; close(): void }) => void,
    public error: (e: { message: string }) => void,
  ) {}
  configure() {
    this.state = "configured";
  }
  decode() {
    this.decoded++;
  }
  close() {
    this.state = "closed";
  }
}

function setup() {
  const decoders: FakeDecoder[] = [];
  const statuses: Array<[string, string]> = [];
  const canvas = { width: 0, height: 0, getContext: () => ({ drawImage: vi.fn() }) };
  const p = createMirrorPlayer({
    createDecoder: (output, error) => {
      const d = new FakeDecoder(output, error);
      decoders.push(d);
      return d;
    },
    makeChunk: (type, timestamp, data) => ({ type, timestamp, data }),
    getCanvas: () => canvas,
    status: (text, state) => statuses.push([text, state]),
  });
  return { p, decoders, statuses, canvas };
}
const AUS = [
  { key: true, data: new Uint8Array([1]) },
  { key: false, data: new Uint8Array([2]) },
];

describe("mirror player", () => {
  it("dimsOk caps frame dimensions", () => {
    expect(MAX_DIM).toBe(4096);
    expect(dimsOk(1080, 2400)).toBe(true);
    expect(dimsOk(4096, 4096)).toBe(true);
    expect(dimsOk(4097, 100)).toBe(false);
    expect(dimsOk(100, 5000)).toBe(false);
    expect(dimsOk(0, 100)).toBe(false);
    expect(dimsOk(Number.NaN, 100)).toBe(false);
  });

  it("positive control: ticks decode access units and loop", () => {
    vi.useFakeTimers();
    const { p, decoders } = setup();
    p.start("avc1.42001e", AUS, 25, true);
    vi.advanceTimersByTime(200);
    expect(decoders.reduce((n, d) => n + d.decoded, 0)).toBeGreaterThan(2);
    expect(decoders.length).toBeGreaterThan(1); // restarted at the loop point
    p.stop();
    vi.useRealTimers();
  });

  it("a decoder error stops the interval and drops the decoder", () => {
    vi.useFakeTimers();
    const { p, decoders, statuses } = setup();
    p.start("avc1.42001e", AUS, 25, true);
    vi.advanceTimersByTime(100);
    const d = decoders[0]!;
    const before = d.decoded;
    d.error({ message: "boom" });
    expect(statuses.at(-1)).toEqual(["decoder error: boom", "error"]);
    expect(d.state).toBe("closed");
    expect(vi.getTimerCount()).toBe(0);
    vi.advanceTimersByTime(1000);
    expect(d.decoded).toBe(before); // no further decode on a dead decoder
    expect(decoders).toHaveLength(1);
    vi.useRealTimers();
  });

  it("a frame larger than 4096x4096 is refused: no canvas resize, playback stopped", () => {
    vi.useFakeTimers();
    const { p, decoders, statuses, canvas } = setup();
    p.start("avc1.42001e", AUS, 25, true);
    const close = vi.fn();
    decoders[0]!.output({ displayWidth: 8192, displayHeight: 8192, close });
    expect(close).toHaveBeenCalled();
    expect(canvas.width).toBe(0);
    expect(statuses.at(-1)![1]).toBe("error");
    expect(vi.getTimerCount()).toBe(0);
    expect(decoders[0]!.state).toBe("closed");
    vi.useRealTimers();
  });

  it("a frame within the cap resizes the canvas", () => {
    vi.useFakeTimers();
    const { p, decoders, canvas } = setup();
    p.start("avc1.42001e", AUS, 25, true);
    decoders[0]!.output({ displayWidth: 540, displayHeight: 1170, close: () => {} });
    expect(canvas.width).toBe(540);
    expect(canvas.height).toBe(1170);
    p.stop();
    vi.useRealTimers();
  });
});
