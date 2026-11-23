import { useLayoutEffect, useRef, useState } from "react";
import { DEVICES, type DeviceKind, type Tap } from "../lib/store";

/** Sandbox per DESIGN 4: scripts + the project's own origin (never the studio's); nothing else. */
export const FRAME_SANDBOX = "allow-scripts allow-same-origin";

function useFitScale(w: number, h: number) {
  const ref = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.4);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const fit = () => {
      const r = el.getBoundingClientRect();
      const s = Math.min((r.width - 16) / w, (r.height - 16) / h, 1);
      setScale(s > 0 ? s : 0.1);
    };
    fit();
    const ro = typeof ResizeObserver !== "undefined" ? new ResizeObserver(fit) : null;
    ro?.observe(el);
    return () => ro?.disconnect();
  }, [w, h]);
  return [ref, scale] as const;
}

export function TapLayer(props: { taps: Tap[]; scale: number; now: number }) {
  return (
    <div className="tap-layer" aria-hidden="true">
      {props.taps
        .filter((t) => props.now - t.at < 1600)
        .map((t) => (
          <span
            key={t.id}
            className="tap"
            data-testid="tap-marker"
            style={{ left: t.x * props.scale, top: t.y * props.scale }}
          />
        ))}
    </div>
  );
}

export function DeviceFrame(props: {
  device: DeviceKind;
  src: string | null;
  /** demo only: revision of the built-in stand-in screen (no iframe, no preview server) */
  demoRevision: number | null;
  taps: Tap[];
  now: number;
}) {
  const d = DEVICES[props.device];
  const [ref, scale] = useFitScale(d.w, d.h);
  return (
    <div className="frame-stage" ref={ref}>
      <div className={`device ${props.device}`} style={{ width: d.w * scale, height: d.h * scale }}>
        {props.src ? (
          <iframe
            key={props.src}
            title={`${props.device} preview`}
            data-testid={`frame-${props.device}`}
            src={props.src}
            sandbox={FRAME_SANDBOX}
            referrerPolicy="no-referrer"
            allow=""
            width={d.w}
            height={d.h}
            style={{ transform: `scale(${scale})` }}
          />
        ) : props.demoRevision ? (
          <div
            className="demo-screen"
            data-testid={`frame-${props.device}`}
            style={{ width: d.w, height: d.h, transform: `scale(${scale})` }}
          >
            <DemoScreen revision={props.demoRevision} />
          </div>
        ) : (
          <div className="frame-empty" data-testid="frame-empty" role="status">
            <strong>Nicio aplicație construită încă</strong>
            <span>Scrie ce aplicație vrei în caseta din stânga și apasă Start run. Aprobă planul, iar aplicația apare aici.</span>
          </div>
        )}
        <TapLayer taps={props.taps} scale={scale} now={props.now} />
      </div>
    </div>
  );
}

/** Stand-in for the preview page in demo mode (static, app-styled). */
function DemoScreen({ revision }: { revision: number }) {
  const items = revision >= 3 ? ["Drink water", "Read 20 pages", "Walk 6000 steps"] : [];
  return (
    <div className="demo-page">
      <header>
        <h1>Habits</h1>
        <small>Today · revision {revision}</small>
      </header>
      {items.length ? (
        <ul>
          {items.map((t, i) => (
            <li key={t}>
              <span className={`box${i === 1 ? " on" : ""}`} />
              {t}
            </li>
          ))}
        </ul>
      ) : (
        <p className="e">No habits yet. Add your first one.</p>
      )}
      <button tabIndex={-1}>Add habit</button>
    </div>
  );
}
