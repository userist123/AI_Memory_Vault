import { useEffect, useRef, useState } from "react";
import { parseSelectMessage, previewOriginOf, projectIdFromOrigin, type InspectorSelection } from "../lib/inspector";
import type { DeviceKind } from "../lib/store";

const ARM = (on: boolean) => ({ v: 1, kind: "inspect", on }) as const;

/**
 * W4 "Inspectează" toggle of a device frame. While on, the preview's kit inspector (kit/template/src/aas-inspector.ts)
 * is armed with {v:1, kind:"inspect", on:true}, sent only to the frame's exact preview origin. A selection is accepted
 * only when the message comes from that frame's window AND from exactly that origin AND passes the strict schema
 * (parseSelectMessage); everything else is counted and dropped. The iframe sandbox and CSP are unchanged: the
 * messages are data only.
 */
export function InspectorOverlay(props: {
  device: DeviceKind;
  src: string | null;
  frame: HTMLIFrameElement | null;
  active: boolean;
  onToggle: (on: boolean) => void;
  onSelect: (sel: InspectorSelection) => void;
}) {
  const { device, src, frame, active, onToggle } = props;
  const origin = previewOriginOf(src);
  const projectId = origin ? projectIdFromOrigin(origin) : null;
  const [rejected, setRejected] = useState(0);
  const onSelect = useRef(props.onSelect);
  onSelect.current = props.onSelect;

  useEffect(() => {
    if (!active || !origin || !projectId || !frame) return;
    const target = () => frame.contentWindow;
    const arm = (on: boolean) => {
      try {
        target()?.postMessage(ARM(on), origin);
      } catch {
        /* the frame is navigating: the load handler arms it again */
      }
    };
    const onLoad = () => arm(true);
    const onMessage = (e: MessageEvent) => {
      const w = target();
      if (!w || e.source !== w) return; // not this frame (another frame, a popup, the studio itself)
      if (e.origin !== origin) {
        setRejected((n) => n + 1);
        return;
      }
      const element = parseSelectMessage(e.data);
      if (!element) {
        setRejected((n) => n + 1);
        return;
      }
      onSelect.current({ projectId, device, element });
    };
    window.addEventListener("message", onMessage);
    frame.addEventListener("load", onLoad);
    arm(true);
    return () => {
      window.removeEventListener("message", onMessage);
      frame.removeEventListener("load", onLoad);
      arm(false);
    };
  }, [active, origin, projectId, frame, device]);

  if (!origin) return null;
  return (
    <div className="row inspector-bar" data-testid={`inspector-bar-${device}`}>
      <button
        className={`btn${active ? " primary" : ""}`}
        data-testid="inspector-toggle"
        aria-pressed={active}
        onClick={() => onToggle(!active)}
        title="Apasă pe un element din previzualizare ca să-l editezi sau să-l trimiți în chat"
      >
        {active ? "Inspectare pornită" : "Inspectează"}
      </button>
      {active ? (
        <span className="muted" data-testid="inspector-hint">
          Apasă pe un element din cadru.
        </span>
      ) : null}
      {rejected > 0 ? (
        <span className="tag" data-testid="inspector-rejected" title="Mesaje din cadru respinse (origine sau format greșit)">
          {rejected} respinse
        </span>
      ) : null}
    </div>
  );
}
