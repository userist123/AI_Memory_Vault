/**
 * QA results for the owner, in one place under the preview: the W2 quality gates (latest run, reported by QaBadge)
 * and the W3 flow tests (latest qa.result event of suite "flows"). Everything is rendered as text nodes; issue and
 * error text is page-derived and already sanitised by qaApi / the server.
 */
import type { Capture } from "../lib/store";
import { countsLabel, type QaResult, SEVERITIES } from "../lib/qaApi";
import type { CaptureLoad } from "./Captures";
import { FlowTestsPanel, parseFlowSummary } from "./FlowTestsPanel";

const MAX_ISSUES = 5;

export function QaResultsPanel(props: {
  gates: QaResult | null;
  qa: { passed: number; failed: number; summary: string } | null;
  captures: Capture[];
  load: CaptureLoad | null;
}) {
  const { gates } = props;
  const flows = parseFlowSummary(props.qa?.summary);
  if (!gates && !flows) return null;
  const c = gates?.counts ?? null;
  return (
    <section className="qa-results" data-testid="qa-results" aria-label="QA results"
      style={{ borderTop: "1px solid var(--line)" }}>
      <div style={{ padding: "6px 12px" }} data-testid="qa-results-gates">
        <span className="eyebrow">Quality gates</span>{" "}
        {gates ? (
          <>
            <span className={`tag${gates.passed ? "" : " accent"}`} data-testid="qa-results-gates-badge">
              {gates.passed ? "passed" : "failed"} · {countsLabel(c)}
            </span>
            {!gates.passed && gates.issues.length ? (
              <ul style={{ margin: "6px 0 0", paddingLeft: 16 }}>
                {gates.issues.slice(0, MAX_ISSUES).map((i, n) => (
                  <li key={n} data-testid="qa-results-issue">
                    <strong>{i.severity}</strong> {i.rule} ({i.frames.join(", ")}): {i.help}
                  </li>
                ))}
                {gates.issues.length > MAX_ISSUES ? (
                  <li className="muted">+{gates.issues.length - MAX_ISSUES} more (QA badge)</li>
                ) : null}
              </ul>
            ) : null}
            {c ? <span className="muted"> {SEVERITIES.map((s) => `${s} ${c[s]}`).join(" · ")}</span> : null}
          </>
        ) : (
          <span className="muted" data-testid="qa-results-gates-none">not run yet</span>
        )}
      </div>
      {flows ? (
        <FlowTestsPanel qa={props.qa} captures={props.captures} load={props.load} />
      ) : (
        <div style={{ padding: "6px 12px" }} data-testid="qa-results-flows-none">
          <span className="eyebrow">Flow tests</span> <span className="muted">not run yet</span>
        </div>
      )}
    </section>
  );
}
