import type { ApprovalRecord, PlanRecord } from "../lib/api";
import { PlanCard } from "./PlanCard";

/**
 * Fixed region outside the timeline. Cards (approvals and the plan gate) come ONLY from server records
 * (GET /api/v1/approvals); `approval.*` / `plan.*` events merely trigger a refetch. Agent text can never create
 * a card here (P6b). A record whose text had hidden/control characters shows them as escapes and cannot be approved.
 */
export function ApprovalRegion(props: {
  records: ApprovalRecord[];
  plans?: PlanRecord[];
  error: string;
  onDecide: (id: string, d: "approve" | "deny") => void;
  onPlanApprove?: (id: string) => void;
  onPlanEdit?: (id: string, note: string) => void;
}) {
  const plans = props.plans ?? [];
  const count = props.records.length + plans.length;
  return (
    <section className="approvals" data-testid="approval-region" aria-label="Approvals requested by the studio">
      <header>
        <span className="eyebrow">Approvals</span>
        <span className="tag">{count ? `${count} pending` : "none pending"}</span>
      </header>
      {props.error ? <p className="muted">{props.error}</p> : null}
      {plans.map((p) => (
        <PlanCard
          key={p.id}
          plan={p}
          onApprove={() => props.onPlanApprove?.(p.id)}
          onEdit={(note) => props.onPlanEdit?.(p.id, note)}
        />
      ))}
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
          {r.unsafe ? (
            <p className="warn" role="alert" data-testid="approval-unsafe">
              Hidden or control characters were found. They are shown as escapes above (for example \x1b or U+202E)
              and this request cannot be approved here. You can still deny it.
            </p>
          ) : null}
          <div className="row">
            <button className="btn" onClick={() => props.onDecide(r.id, "deny")}>
              Deny
            </button>
            <button className="btn primary" disabled={r.unsafe} onClick={() => props.onDecide(r.id, "approve")}>
              Approve
            </button>
          </div>
        </article>
      ))}
    </section>
  );
}
