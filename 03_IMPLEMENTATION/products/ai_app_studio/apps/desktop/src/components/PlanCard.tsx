import { useState } from "react";
import type { PlanRecord } from "../lib/api";

/**
 * Plan gate card. Rendered only from a server plan record (never from a plan.proposed event). When a field had
 * hidden or escape characters the raw code points are shown as text and Approve is disabled (Edit stays possible:
 * the owner's own note is the only thing it sends).
 */
export function PlanCard(props: {
  plan: PlanRecord;
  onApprove: () => void;
  onEdit: (note: string) => void;
}) {
  const { plan } = props;
  const [editing, setEditing] = useState(false);
  const [note, setNote] = useState("");
  return (
    <section className="plan-card" data-testid="plan-card" aria-label="Plan gate">
      <header>
        <span className="eyebrow">Plan gate</span>
        <span className="tag accent">waiting for you</span>
      </header>
      <h3>{plan.title || "Proposed plan"}</h3>
      <ol>
        {plan.steps.map((s, i) => (
          <li key={i}>{s}</li>
        ))}
      </ol>
      {plan.unsafe ? (
        <p className="warn" role="alert" data-testid="plan-unsafe">
          {plan.contentSha256
            ? "This plan contains hidden or control characters (shown as escapes above). It cannot be approved here."
            : "This plan has no valid content hash from the server. It cannot be approved here."}
        </p>
      ) : null}
      <div className="row">
        {editing ? (
          <>
            <textarea
              value={note}
              maxLength={4000}
              onChange={(e) => setNote(e.target.value)}
              placeholder="What should change in this plan?"
            />
            <button className="btn" onClick={() => setEditing(false)}>
              Cancel
            </button>
            <button className="btn primary" onClick={() => props.onEdit(note)} disabled={!note.trim()}>
              Send edit
            </button>
          </>
        ) : (
          <>
            <button className="btn" onClick={() => setEditing(true)}>
              Edit
            </button>
            <button className="btn primary" onClick={props.onApprove} disabled={plan.unsafe}>
              Approve plan
            </button>
          </>
        )}
      </div>
    </section>
  );
}
