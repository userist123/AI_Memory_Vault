/**
 * W3 flow test results for the owner. Source: the latest qa.result event of suite "flows" (server-shaped, sanitised
 * and redacted summary: "flows rev N: P/T passed" + one "x <file> › <title> [shot <sha12>]: <error>" line per
 * failure). A failure screenshot is a preview.captured image, matched by its sha256 prefix and fetched through the
 * authenticated capture loader. All text is rendered as text (React escapes it); nothing from it becomes a URL.
 */
import type { Capture } from "../lib/store";
import { type CaptureLoad, CaptureThumb } from "./Captures";

export interface FlowFailure {
  label: string;
  error: string;
  shot: string | null;
}
export interface FlowSummary {
  revision: number;
  passed: number;
  total: number;
  failures: FlowFailure[];
}

const HEADER = /^flows rev (\d{1,9}): (\d{1,4})\/(\d{1,4}) passed$/;
const FAIL = /^x (.{1,200}?)(?: \[shot ([0-9a-f]{12})\])?: ?(.*)$/;

export function parseFlowSummary(summary: string | null | undefined): FlowSummary | null {
  if (!summary) return null;
  const lines = summary.split("\n");
  const m = HEADER.exec(lines[0] ?? "");
  if (!m) return null;
  const failures: FlowFailure[] = [];
  for (const ln of lines.slice(1)) {
    const f = FAIL.exec(ln);
    if (f) failures.push({ label: f[1]!, shot: f[2] ?? null, error: (f[3] ?? "").slice(0, 300) });
  }
  return { revision: Number(m[1]), passed: Number(m[2]), total: Number(m[3]), failures };
}

export function FlowTestsPanel(props: {
  qa: { passed: number; failed: number; summary: string } | null;
  captures: Capture[];
  load: CaptureLoad | null;
}) {
  const s = parseFlowSummary(props.qa?.summary);
  if (!s) return null;
  const ok = s.passed === s.total;
  return (
    <section className="flow-tests" data-testid="flow-tests" aria-label="Flow tests"
      style={{ padding: "6px 12px", borderTop: "1px solid var(--line)" }}>
      <span className="eyebrow">Flow tests</span>{" "}
      <span className={`tag${ok ? "" : " accent"}`} data-testid="flow-badge">
        {s.passed}/{s.total} passed · rev {s.revision}
      </span>
      {s.failures.length ? (
        <ul style={{ margin: "6px 0 0", paddingLeft: 16 }}>
          {s.failures.map((f, i) => {
            const cap = f.shot ? props.captures.find((c) => c.sha256.startsWith(f.shot!)) : undefined;
            return (
              <li key={i} data-testid="flow-failure">
                <strong>{f.label}</strong>
                {f.error ? <div className="muted">{f.error}</div> : null}
                {cap ? <CaptureThumb capture={cap} load={props.load} testid="flow-shot" /> : null}
              </li>
            );
          })}
        </ul>
      ) : null}
    </section>
  );
}
