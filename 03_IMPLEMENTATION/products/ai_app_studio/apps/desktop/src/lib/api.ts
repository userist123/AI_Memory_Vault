/** REST client for http://127.0.0.1:17875/api/v1 (Bearer token). Responses are size-capped and validated. */
import type { Session } from "./session";
import { clean, cleanList, SHORT_CAP } from "./sanitize";

const MAX_BODY = 1024 * 1024;

async function request(s: Session, path: string, init?: RequestInit): Promise<unknown> {
  const res = await fetch(`${s.apiBase}/api/v1${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  const text = await res.text();
  if (text.length > MAX_BODY) throw new Error("response too large");
  return text ? (JSON.parse(text) as unknown) : null;
}

/** A pending approval, as stored by the server (services.py approvals). Rendered only from these records. */
export interface ApprovalRecord {
  id: string;
  action: string;
  argv: string[];
  target: string;
  reason: string;
  runId: string;
}

const ID_RE = /^[A-Za-z0-9_.:-]{1,64}$/;

export function parseApprovals(body: unknown): ApprovalRecord[] {
  const list = Array.isArray(body)
    ? body
    : body && typeof body === "object" && Array.isArray((body as { items?: unknown }).items)
      ? (body as { items: unknown[] }).items
      : [];
  const out: ApprovalRecord[] = [];
  for (const item of list.slice(0, 50)) {
    if (!item || typeof item !== "object") continue;
    const r = item as Record<string, unknown>;
    if (typeof r.id !== "string" || !ID_RE.test(r.id)) continue;
    if (r.status !== undefined && r.status !== "pending") continue;
    out.push({
      id: r.id,
      action: clean(r.action ?? r.kind, SHORT_CAP),
      argv: cleanList(r.argv),
      target: clean(r.target, 500),
      reason: clean(r.reason, 1000),
      runId: typeof r.runId === "string" && ID_RE.test(r.runId) ? r.runId : "",
    });
  }
  return out;
}

export async function fetchApprovals(s: Session): Promise<ApprovalRecord[]> {
  return parseApprovals(await request(s, "/approvals"));
}

export async function decideApproval(s: Session, id: string, decision: "approve" | "deny"): Promise<void> {
  if (!ID_RE.test(id)) return;
  await request(s, `/approvals/${encodeURIComponent(id)}/decide`, {
    method: "POST",
    body: JSON.stringify({ decision }),
  });
}

export async function decidePlan(s: Session, planId: string, decision: "approve" | "edit", note = ""): Promise<void> {
  if (!ID_RE.test(planId)) return;
  await request(s, `/plans/${encodeURIComponent(planId)}/decide`, {
    method: "POST",
    body: JSON.stringify({ decision, note: note.slice(0, 4000) }),
  });
}
