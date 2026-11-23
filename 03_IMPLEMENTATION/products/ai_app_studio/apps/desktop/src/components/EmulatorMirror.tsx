import { useEffect, useRef, useState } from "react";
import type { Session } from "../lib/session";
import type { Tap } from "../lib/store";
import { VideoClient } from "../lib/video";
import { TapLayer } from "./DeviceFrames";

/**
 * Optional in-studio mirror of the Android emulator (the primary view is the real WSLg window).
 * H.264 Annex-B -> WebCodecs VideoDecoder in a Worker -> OffscreenCanvas. `stream` is bytes from
 * /api/v1/device/video or, in demo mode, the bundled fixture.
 */
export function EmulatorMirror(props: {
  stream: ArrayBuffer | null;
  /** S8: live h264 of the project's studio emulator over the authenticated binary WS (null in demo mode) */
  live?: { session: Session; projectId: string } | null;
  taps: Tap[];
  now: number;
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const workerRef = useRef<Worker | null>(null);
  const clientRef = useRef<VideoClient | null>(null);
  const [status, setStatus] = useState({ text: "no stream", state: "idle" });
  const [scale, setScale] = useState(1);

  useEffect(() => {
    const c = canvasRef.current;
    if (!c || typeof Worker === "undefined" || !("transferControlToOffscreen" in c)) {
      setStatus({ text: "mirror unavailable in this runtime", state: "unsupported" });
      return;
    }
    const w = new Worker(new URL("../workers/decoder.worker.ts", import.meta.url), { type: "module" });
    workerRef.current = w;
    w.onmessage = (m: MessageEvent<{ type: string; text?: unknown; state?: unknown }>) => {
      if (m.data?.type === "status") {
        setStatus({ text: String(m.data.text ?? "").slice(0, 200), state: String(m.data.state ?? "") });
      } else if (m.data?.type === "resync") {
        clientRef.current?.resync(); // decoder error: fresh stream after a bounded backoff
      }
    };
    const off = c.transferControlToOffscreen();
    w.postMessage({ type: "init", canvas: off }, [off]);
    return () => {
      w.terminate();
      workerRef.current = null;
    };
  }, []);

  useEffect(() => {
    const w = workerRef.current;
    if (!w || !props.stream) return;
    const copy = props.stream.slice(0);
    w.postMessage({ type: "stream", data: copy, fps: 10, loop: true }, [copy]);
  }, [props.stream]);

  const liveSession = props.live?.session ?? null;
  const liveProject = props.live?.projectId ?? "";
  useEffect(() => {
    const w = workerRef.current;
    if (!w || !liveSession || !liveProject) return;
    w.postMessage({ type: "live" });
    const client = new VideoClient(
      liveSession,
      liveProject,
      (chunk) => w.postMessage({ type: "chunk", data: chunk }, [chunk]),
      () => w.postMessage({ type: "reset" }),
    );
    clientRef.current = client;
    client.start();
    return () => {
      if (clientRef.current === client) clientRef.current = null;
      client.stop();
      w.postMessage({ type: "stop" });
    };
  }, [liveSession, liveProject]);

  useEffect(() => {
    const c = canvasRef.current;
    if (!c) return;
    const fit = () => setScale(c.getBoundingClientRect().width / 1080);
    fit();
    const ro = typeof ResizeObserver !== "undefined" ? new ResizeObserver(fit) : null;
    ro?.observe(c);
    return () => ro?.disconnect();
  }, []);

  return (
    <div className="mirror" data-testid="mirror" data-source={liveProject ? "live" : props.stream ? "fixture" : "none"}>
      <div className="mirror-screen">
        <canvas ref={canvasRef} width={180} height={320} data-testid="mirror-canvas" />
        {/* device.tap coordinates are in device pixels of a 1080-wide screen */}
        <TapLayer taps={props.taps} scale={scale} now={props.now} />
      </div>
      <p className="muted" data-testid="mirror-status" data-state={status.state}>
        Emulator mirror: {status.text}. The real emulator window on the desktop draws every touch itself.
      </p>
    </div>
  );
}
