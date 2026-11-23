import type { ApprovalRecord } from "../lib/api";

/**
 * Fixed region outside the timeline. Cards come ONLY from server approval records (GET /api/v1/approvals);
 * `approval.requested` events merely trigger a refetch. Agent text can never create a card here (P6b).
 */
export function ApprovalRegion(props: {
  records: ApprovalRecord[];
  error: string;
  onDecide: (id: string, d: "approve" | "deny") => void;
}) {
  return (
    <section className="approvals" data-testid="approval-region" aria-label="Approvals requested by the studio">
      <header>
        <span className="eyebrow">Approvals</span>
        <span className="tag">{props.records.length ? `${props.records.length} pending` : "none pending"}</span>
      </header>
      {props.error ? <p className="muted">{props.error}</p> : null}
      {props.records.map((r) => (
        <article key={r.id} className="approval-card" data-testid="approval-card">
          <div className="approval-action">{r.action || "action"}</div>
          {r.argv.length > 0 && (
            <div className="argv" data-testid="approval-argv">
              {r.argv.map((a, i) => (
                <code key={i}>{a}</code>
              ))}
            </div>
          )}
          {r.target ? (
            <div className="approval-target">
              <span className="muted">target</span> <code data-testid="approval-target">{r.target}</code>
            </div>
          ) : null}
          {r.reason ? <p className="muted">{r.reason}</p> : null}
          <div className="row">
            <button className="btn" onClick={() => props.onDecide(r.id, "deny")}>
              Deny
            </button>
            <button className="btn primary" onClick={() => props.onDecide(r.id, "approve")}>
              Approve
            </button>
          </div>
        </article>
      ))}
    </section>
  );
}
