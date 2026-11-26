import { useState } from "react";
import type { ExportOutcome } from "../lib/api";

type Target = "web" | "android" | "desktop";

/**
 * The owner's Export buttons (S9b). Step 1 asks the server, which answers with the exact artifact sha256 to approve;
 * the owner confirms here (owner action in the studio window), the approval is decided, step 2 repeats the call with
 * the approval id. A changed artifact gets another sha256 and a new confirmation. Nothing the agent writes can press
 * these buttons; the agent's own app_export waits for the same approval in the approvals list.
 */
export function ExportControls(props: {
  projectId: string;
  run: (projectId: string, target: Target, approvalId?: string) => Promise<ExportOutcome>;
  approve: (approvalId: string) => Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<{ target: Target; approvalId: string; sha256: string; note?: string } | null>(null);
  const [msg, setMsg] = useState<{ kind: "ok" | "warn"; text: string } | null>(null);

  const show = (target: Target, o: ExportOutcome) => {
    if (o.kind === "approval") setPending({ target, approvalId: o.approvalId, sha256: o.sha256, note: o.note });
    else if (o.kind === "done") setMsg({ kind: "ok", text: `Exported to ${o.path}${o.note ? `. ${o.note}` : ""}` });
    else if (o.kind === "blocked") setMsg({ kind: "warn", text: `Export blocked (secret scan): ${o.findings.join(", ")}` });
    else setMsg({ kind: "warn", text: `Export failed: ${o.message}` });
  };
  const start = (target: Target) => {
    setBusy(true);
    setMsg(null);
    setPending(null);
    void props
      .run(props.projectId, target)
      .then((o) => show(target, o))
      .catch(() => setMsg({ kind: "warn", text: "Export failed." }))
      .finally(() => setBusy(false));
  };
  const confirm = () => {
    if (!pending) return;
    const p = pending;
    setBusy(true);
    setPending(null);
    void props
      .approve(p.approvalId)
      .then(() => props.run(props.projectId, p.target, p.approvalId))
      .then((o) => show(p.target, o))
      .catch(() => setMsg({ kind: "warn", text: "Export failed." }))
      .finally(() => setBusy(false));
  };

  return (
    <div className="export-controls" data-testid="export-controls">
      <button className="btn" data-testid="export-web" disabled={busy || !props.projectId} onClick={() => start("web")}>
        Export web (zip)
      </button>
      <button className="btn" data-testid="export-android" disabled={busy || !props.projectId} onClick={() => start("android")}>
        Export APK
      </button>
      <button className="btn" data-testid="export-desktop" disabled={busy || !props.projectId} onClick={() => start("desktop")}>
        Export desktop (exe)
      </button>
      {pending ? (
        <div className="export-confirm" role="dialog" aria-label="Approve export" data-testid="export-confirm">
          <p>
            Export the {pending.target === "web" ? "web zip" : pending.target === "android" ? "debug APK" : "unsigned Windows exe"} with sha256{" "}
            <code title={pending.sha256}>{pending.sha256.slice(0, 12)}…</code>?
          </p>
          {pending.note ? (
            <p className="warn" data-testid="export-confirm-note">
              {pending.note}
            </p>
          ) : null}
          <button className="btn primary" data-testid="export-approve" onClick={confirm}>
            Approve and export
          </button>
          <button className="btn" data-testid="export-cancel" onClick={() => setPending(null)}>
            Cancel
          </button>
        </div>
      ) : null}
      {msg ? (
        <p className={msg.kind === "ok" ? "ok" : "warn"} role="status" data-testid="export-result">
          {msg.text}
        </p>
      ) : null}
    </div>
  );
}
