/// <reference lib="webworker" />
/**
 * Emulator mirror decoder. Input: H.264 Annex-B bytes (from the binary WS /api/v1/device/video, or the demo
 * fixture). Output: frames drawn on a transferred OffscreenCanvas. Nothing from the stream reaches the DOM.
 */
import { accessUnits, codecFromSps, splitNals } from "../lib/annexb";

type InMsg =
  | { type: "init"; canvas: OffscreenCanvas }
  | { type: "stream"; data: ArrayBuffer; fps: number; loop: boolean }
  | { type: "stop" };

const ctx = self as unknown as DedicatedWorkerGlobalScope;
let canvas: OffscreenCanvas | null = null;
let decoder: VideoDecoder | null = null;
let timer: ReturnType<typeof setInterval> | undefined;
let frames = 0;
const MAX_STREAM = 8 * 1024 * 1024;

function status(text: string, state: "idle" | "decoding" | "unsupported" | "error") {
  ctx.postMessage({ type: "status", text, state, frames });
}

function draw(frame: VideoFrame) {
  if (canvas) {
    if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
      canvas.width = frame.displayWidth;
      canvas.height = frame.displayHeight;
    }
    canvas.getContext("2d")?.drawImage(frame, 0, 0);
  }
  frame.close();
  frames++;
  if (frames % 10 === 1) status(`decoding (${frames} frames)`, "decoding");
}

async function play(data: ArrayBuffer, fps: number, loop: boolean) {
  stop();
  if (data.byteLength > MAX_STREAM) return status("stream too large", "error");
  if (typeof VideoDecoder === "undefined") return status("WebCodecs VideoDecoder unavailable", "unsupported");
  const nals = splitNals(new Uint8Array(data));
  const codec = codecFromSps(nals);
  if (!codec) return status("no SPS in stream", "error");
  const support = await VideoDecoder.isConfigSupported({ codec, optimizeForLatency: true }).catch(() => null);
  if (!support?.supported) return status(`codec ${codec} not supported here`, "unsupported");
  const aus = accessUnits(nals);
  if (!aus.length) return status("no frames in stream", "error");
  const start = () => {
    decoder = new VideoDecoder({ output: draw, error: (e) => status(`decoder error: ${e.message}`, "error") });
    decoder.configure({ codec, optimizeForLatency: true });
  };
  start();
  let i = 0;
  const step = 1_000_000 / Math.max(1, Math.min(60, fps));
  timer = setInterval(() => {
    if (!decoder) return;
    if (i >= aus.length) {
      if (!loop) return stop();
      i = 0;
      decoder.close();
      start();
    }
    const au = aus[i]!;
    decoder.decode(new EncodedVideoChunk({ type: au.key ? "key" : "delta", timestamp: i * step, data: au.data }));
    i++;
  }, step / 1000);
  status(`decoding ${codec}`, "decoding");
}

function stop() {
  if (timer) clearInterval(timer);
  timer = undefined;
  if (decoder && decoder.state !== "closed") decoder.close();
  decoder = null;
}

ctx.onmessage = (m: MessageEvent<InMsg>) => {
  const msg = m.data;
  if (msg.type === "init") {
    canvas = msg.canvas;
    status("waiting for stream", "idle");
  } else if (msg.type === "stream") {
    void play(msg.data, msg.fps, msg.loop);
  } else if (msg.type === "stop") {
    stop();
    status("stopped", "idle");
  }
};
