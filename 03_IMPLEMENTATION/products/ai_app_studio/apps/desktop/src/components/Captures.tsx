import { useEffect, useState } from "react";
import { DEVICES, type Capture } from "../lib/store";

/** Resolves a capture sha256 to a blob: URL (lib/captures.ts: authenticated fetch, sha256-checked, cached). */
export type CaptureLoad = (sha256: string) => Promise<string>;

export function captureLabel(c: Capture): string {
  return `${DEVICES[c.frame].label} · rev ${c.revision}`;
}

export function CaptureThumb(props: { capture: Capture; load: CaptureLoad | null; testid?: string }) {
  const { capture, load } = props;
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let live = true;
    setUrl(null);
    setFailed(false);
    if (!load) return;
    load(capture.sha256)
      .then((u) => {
        if (live && u.startsWith("blob:")) setUrl(u);
        else if (live) setFailed(true);
      })
      .catch(() => live && setFailed(true));
    return () => {
      live = false;
    };
  }, [capture.sha256, load]);
  const label = captureLabel(capture);
  return (
    <figure className={`capture ${capture.frame}`} data-testid={props.testid ?? "capture-thumb"} data-sha256={capture.sha256}>
      {url ? (
        <img src={url} alt={`${label} capture`} referrerPolicy="no-referrer" decoding="async" />
      ) : (
        <div className="capture-empty">{failed || !load ? "capture unavailable" : "loading…"}</div>
      )}
      <figcaption>{label}</figcaption>
    </figure>
  );
}

/** Timeline strip: the most recent preview captures, newest first. */
export function CaptureStrip(props: { captures: Capture[]; load: CaptureLoad | null; max?: number }) {
  const shown = props.captures.slice(-(props.max ?? 6)).reverse();
  if (!shown.length) return null;
  return (
    <section className="captures" data-testid="timeline-captures" aria-label="Preview captures">
      <span className="eyebrow">Preview captures</span>
      <div className="capture-row">
        {shown.map((c) => (
          <CaptureThumb key={c.seq} capture={c} load={props.load} />
        ))}
      </div>
    </section>
  );
}
