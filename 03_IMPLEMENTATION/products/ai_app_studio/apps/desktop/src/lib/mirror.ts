/**
 * Emulator mirror playback core (runs inside the decoder worker; dependency-injected so it is unit-testable).
 * Lifecycle rules: a decoder error or an oversize frame stops the timer and drops the decoder; frames above
 * MAX_DIM x MAX_DIM never resize the canvas.
 */
import type { AccessUnit } from "./annexb";

export const MAX_DIM = 4096;

export function dimsOk(w: number, h: number): boolean {
  return Number.isFinite(w) && Number.isFinite(h) && w >= 1 && h >= 1 && w <= MAX_DIM && h <= MAX_DIM;
}

export type MirrorState = "idle" | "decoding" | "unsupported" | "error";

export interface FrameLike {
  displayWidth: number;
  displayHeight: number;
  close(): void;
}
export interface DecoderLike {
  state: string;
  configure(cfg: { codec: string; optimizeForLatency: boolean }): void;
  decode(chunk: unknown): void;
  close(): void;
}
export interface CanvasLike {
  width: number;
  height: number;
  getContext(kind: "2d"): { drawImage(frame: never, x: number, y: number): void } | null;
}

export interface PlayerEnv {
  createDecoder(output: (f: FrameLike) => void, error: (e: { message: string }) => void): DecoderLike;
  makeChunk(type: "key" | "delta", timestamp: number, data: Uint8Array): unknown;
  getCanvas(): CanvasLike | null;
  status(text: string, state: MirrorState, frames: number): void;
}

export function createMirrorPlayer(env: PlayerEnv) {
  let decoder: DecoderLike | null = null;
  let timer: ReturnType<typeof setInterval> | undefined;
  let frames = 0;
  const report = (text: string, state: MirrorState) => env.status(text, state, frames);

  function stop() {
    if (timer) clearInterval(timer);
    timer = undefined;
    const d = decoder;
    decoder = null;
    if (d && d.state !== "closed") {
      try {
        d.close();
      } catch {
        // already torn down
      }
    }
  }

  function fail(text: string) {
    stop();
    report(text, "error");
  }

  function onFrame(frame: FrameLike) {
    if (!dimsOk(frame.displayWidth, frame.displayHeight)) {
      frame.close();
      fail(`frame ${frame.displayWidth}x${frame.displayHeight} exceeds ${MAX_DIM}x${MAX_DIM}`);
      return;
    }
    const canvas = env.getCanvas();
    if (canvas) {
      if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
        canvas.width = frame.displayWidth;
        canvas.height = frame.displayHeight;
      }
      canvas.getContext("2d")?.drawImage(frame as never, 0, 0);
    }
    frame.close();
    frames++;
    if (frames % 10 === 1) report(`decoding (${frames} frames)`, "decoding");
  }

  function create(codec: string): DecoderLike {
    const d = env.createDecoder(onFrame, (e) => {
      if (decoder === d) fail(`decoder error: ${e.message}`);
    });
    d.configure({ codec, optimizeForLatency: true });
    return d;
  }

  function start(codec: string, aus: AccessUnit[], fps: number, loop: boolean) {
    stop();
    frames = 0;
    decoder = create(codec);
    let i = 0;
    const step = 1_000_000 / Math.max(1, Math.min(60, fps));
    timer = setInterval(() => {
      if (!decoder) return;
      try {
        if (i >= aus.length) {
          if (!loop) return stop();
          i = 0;
          decoder.close();
          decoder = create(codec);
        }
        const au = aus[i]!;
        decoder.decode(env.makeChunk(au.key ? "key" : "delta", i * step, au.data));
        i++;
      } catch (e) {
        fail(`decoder error: ${e instanceof Error ? e.message : "decode failed"}`);
      }
    }, step / 1000);
    report(`decoding ${codec}`, "decoding");
  }

  return { start, stop, report };
}
