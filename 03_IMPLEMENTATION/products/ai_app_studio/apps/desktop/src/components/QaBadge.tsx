import { useEffect, useRef, useState } from "react";
import { countsLabel, SEVERITIES, type QaResult, type QaState } from "../lib/qaApi";

/**
 * W2 QA badge: counts by severity of the latest quality-gate run (axe-core + layout + console on phone/tablet/desktop).
 * After every preview change (``nonce``) it asks the server for an automatic run; the server only runs the gates when
 * the stored result is not for the web build the preview serves now, so the badge's own captures never loop.
 * Issue text is page-derived, sanitised by qaApi and rendered as text nodes only.
 */
const DETAILS = { position: "absolute", top: "100%", right: 0, zIndex: 20, width: 420, maxHeight: 360, overflow: "auto",
  padding: 8, background: "var(--panel, #1e1e1e)", border: "1px solid var(--border, #444)", borderRadius: 6 } as const;

export function QaBadge(props: {
  projectId: string;
  nonce: number;
  api: { load: (pid: string) => Promise<QaState>; run: (pid: string, auto: boolean) => Promise<QaResult | null> };
  /** the latest gate result (null on project switch), for the QA results panel under the preview */
  onResult?: (r: QaResult | null) => void;
}) {
  const { projectId, nonce } = props;
  const apiRef = useRef(props.api);
  apiRef.current = props.api; // an inline api object must not re-trigger the effects below
  const [result, setResult] = useState<QaResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);
  const [err, setErr] = useState("");
  const seq = useRef(0);
  const onResultRef = useRef(props.onResult);
  onResultRef.current = props.onResult;
  useEffect(() => {
    onResultRef.current?.(result);
  }, [result]);
  const inflight = useRef(false);
  const again = useRef(false); // an automatic request arrived while a run was in flight (its own captures bump nonce)

  const go = (auto: boolean) => {
    if (!projectId) return;
    if (inflight.current) {
      if (auto) again.current = true;
      return;
    }
    inflight.current = true;
    const my = seq.current;
    setBusy(true);
    setErr("");
    void apiRef.current
      .run(projectId, auto)
      .then((r) => {
        if (my === seq.current && r) setResult(r);
      })
      .catch((e: unknown) => {
        if (my === seq.current) setErr(e instanceof Error ? e.message : "QA failed");
      })
      .finally(() => {
        inflight.current = false;
        setBusy(false);
        if (again.current && my === seq.current) {
          again.current = false;
          go(true); // the server skips it when the stored result is already for the current build
        }
      });
  };

  useEffect(() => {
    setResult(null);
    again.current = false;
    if (!projectId) return;
    const my = ++seq.current;
    void apiRef.current
      .load(projectId)
      .then((s) => {
        if (my === seq.current) setResult(s.result);
      })
      .catch(() => undefined);
  }, [projectId]);

  useEffect(() => {
    if (nonce > 0) go(true);
  }, [nonce, projectId]);

  if (!projectId) return null;
  const c = result?.counts ?? null;
  const tone = !result ? "" : result.passed ? "ok" : c && (c.critical || c.serious) ? "warn" : "";
  return (
    <span className="qa-badge" data-testid="qa-badge" style={{ position: "relative", display: "inline-flex", gap: 4 }}>
      <button
        className={`tag ${tone}`}
        data-testid="qa-summary"
        title="Quality gates: accessibility (axe-core WCAG 2.2 AA), overflow, overlaps, broken images, 44 px targets, console errors"
        onClick={() => setOpen((o) => !o)}
      >
        {busy ? "QA…" : !result ? "QA -" : result.passed ? `QA ok${c && c.minor ? ` (${c.minor} minor)` : ""}` : `QA ${countsLabel(c)}`}
      </button>
      <button className="btn small" data-testid="qa-run" disabled={busy} onClick={() => go(false)}>
        Run QA
      </button>
      {err ? (
        <span className="warn" role="status" data-testid="qa-error">
          {err}
        </span>
      ) : null}
      {open && result ? (
        <div className="qa-details" role="dialog" aria-label="QA issues" data-testid="qa-details" style={DETAILS}>
          <p>
            {SEVERITIES.map((s) => `${s}: ${c ? c[s] : 0}`).join(" · ")}
          </p>
          <ul>
            {result.issues.map((i, n) => (
              <li key={n} data-testid="qa-issue">
                <strong>{i.severity}</strong> {i.rule} ({i.frames.join(", ")}): {i.help}
                {i.targets.length ? ` - ${i.targets.join("; ")}` : ""}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </span>
  );
}
