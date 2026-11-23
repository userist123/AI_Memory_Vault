/// <reference lib="webworker" />
/**
 * Emulator mirror decoder. Input: H.264 Annex-B bytes (from the binary WS /api/v1/device/video, or the demo
 * fixture). Output: frames drawn on a transferred OffscreenCanvas. Nothing from the stream reaches the DOM.
 * Lifecycle (error -> stop timer and drop decoder, frame size cap) lives in lib/mirror.ts.
 */
import { accessUnits, codecFromSps, splitNals } from "../lib/annexb";
import { createMirrorPlayer, type CanvasLike, type DecoderLike, type FrameLike } from "../lib/mirror";

type InMsg =
  | { type: "init"; canvas: OffscreenCanvas }
  | { type: "stream"; data: ArrayBuffer; fps: number; loop: boolean }
  | { type: "stop" };

const ctx = self as unknown as DedicatedWorkerGlobalScope;
let canvas: OffscreenCanvas | null = null;
const MAX_STREAM = 8 * 1024 * 1024;

const player = createMirrorPlayer({
  createDecoder: (output, error) =>
    new VideoDecoder({
      output: (f) => output(f as unknown as FrameLike),
      error: (e) => error({ message: e.message }),
    }) as unknown as DecoderLike,
  makeChunk: (type, timestamp, data) => new EncodedVideoChunk({ type, timestamp, data }),
  getCanvas: () => canvas as unknown as CanvasLike | null,
  status: (text, state, frames) => ctx.postMessage({ type: "status", text, state, frames }),
});

async function play(data: ArrayBuffer, fps: number, loop: boolean) {
  player.stop();
  if (data.byteLength > MAX_STREAM) return player.report("stream too large", "error");
  if (typeof VideoDecoder === "undefined") return player.report("WebCodecs VideoDecoder unavailable", "unsupported");
  const nals = splitNals(new Uint8Array(data));
  const codec = codecFromSps(nals);
  if (!codec) return player.report("no SPS in stream", "error");
  const support = await VideoDecoder.isConfigSupported({ codec, optimizeForLatency: true }).catch(() => null);
  if (!support?.supported) return player.report(`codec ${codec} not supported here`, "unsupported");
  const aus = accessUnits(nals);
  if (!aus.length) return player.report("no frames in stream", "error");
  player.start(codec, aus, fps, loop);
}

ctx.onmessage = (m: MessageEvent<InMsg>) => {
  const msg = m.data;
  if (msg.type === "init") {
    canvas = msg.canvas;
    player.report("waiting for stream", "idle");
  } else if (msg.type === "stream") {
    void play(msg.data, msg.fps, msg.loop);
  } else if (msg.type === "stop") {
    player.stop();
    player.report("stopped", "idle");
  }
};
