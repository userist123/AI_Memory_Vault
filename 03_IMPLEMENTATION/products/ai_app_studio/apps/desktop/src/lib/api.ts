/** REST client for http://127.0.0.1:17875/api/v1 (Bearer token). Responses are size-capped and validated. */
import type { Session } from "./session";
import { inspect, inspectList, SHORT_CAP } from "./sanitize";

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

/**
 * A pending approval, as stored by the server. Rendered only from these records. Every text field is shown through
 * inspect(): if sanitising would change it, the raw code points are shown as escapes and `unsafe` is set, which
 * disables Approve (fail closed).
 */
export interface ApprovalRecord {
  id: string;
  action: string;
  argv: string[];
  target: string;
  reason: string;
  runId: string;
  unsafe: boolean;
}

/** A pending plan-gate record (GET /api/v1/approvals, kind "plan"). Same fail-closed rules as approvals. */
export interface PlanRecord {
  id: string;
  title: string;
  steps: string[];
  runId: string;
  unsafe: boolean;
}

export interface ServerRecords {
  approvals: ApprovalRecord[];
  plans: PlanRecord[];
}

const ID_RE = /^[A-Za-z0-9_.:-]{1,64}$/;

export function parseRecords(body: unknown): ServerRecords {
  const list = Array.isArray(body)
    ? body
    : body && typeof body === "object" && Array.isArray((body as { items?: unknown }).items)
      ? (body as { items: unknown[] }).items
      : [];
  const out: ServerRecords = { approvals: [], plans: [] };
  for (const item of list.slice(0, 50)) {
    if (!item || typeof item !== "object") continue;
    const r = item as Record<string, unknown>;
    if (typeof r.id !== "string" || !ID_RE.test(r.id)) continue;
    if (r.status !== undefined && r.status !== "pending") continue;
    const runId = typeof r.runId === "string" && ID_RE.test(r.runId) ? r.runId : "";
    if (r.kind === "plan") {
      const title = inspect(r.title, 300);
      const steps = inspectList(r.steps, 20, 300);
      out.plans.push({ id: r.id, title: title.text, steps: steps.items, runId, unsafe: title.unsafe || steps.unsafe });
      continue;
    }
    const action = inspect(r.action ?? r.kind, SHORT_CAP);
    const argv = inspectList(r.argv);
    const target = inspect(r.target, 500);
    const reason = inspect(r.reason, 1000, false);
    out.approvals.push({
      id: r.id,
      action: action.text,
      argv: argv.items,
      target: target.text,
      reason: reason.text,
      runId,
      unsafe: action.unsafe || argv.unsafe || target.unsafe || reason.unsafe,
    });
  }
  return out;
}

export function parseApprovals(body: unknown): ApprovalRecord[] {
  return parseRecords(body).approvals;
}

export async function fetchApprovals(s: Session): Promise<ServerRecords> {
  return parseRecords(await request(s, "/approvals"));
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

// ---------------------------------------------------------------- runs (owner actions from the prompt box)
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
const RUN_RE = /^run_[0-9a-f]{16}$/;

export interface ProjectRef {
  id: string;
  name: string;
}

export async function listProjects(s: Session): Promise<ProjectRef[]> {
  const body = await request(s, "/projects");
  const out: ProjectRef[] = [];
  for (const p of Array.isArray(body) ? body.slice(0, 200) : []) {
    if (!p || typeof p !== "object") continue;
    const r = p as Record<string, unknown>;
    if (typeof r.id !== "string" || !PROJECT_RE.test(r.id)) continue;
    out.push({ id: r.id, name: inspect(r.name, 64).text });
  }
  return out;
}

export async function createProject(s: Session, name: string): Promise<string> {
  const body = await request(s, "/projects", { method: "POST", body: JSON.stringify({ name: name.slice(0, 64) }) });
  const id = body && typeof body === "object" ? (body as { id?: unknown }).id : undefined;
  if (typeof id !== "string" || !PROJECT_RE.test(id)) throw new Error("bad project id");
  return id;
}

export async function startRun(s: Session, projectId: string, prompt: string): Promise<{ runId: string }> {
  if (!PROJECT_RE.test(projectId)) throw new Error("bad project id");
  const body = await request(s, "/runs", { method: "POST", body: JSON.stringify({ projectId, prompt: prompt.slice(0, 20000) }) });
  const runId = body && typeof body === "object" ? (body as { runId?: unknown }).runId : undefined;
  if (typeof runId !== "string" || !RUN_RE.test(runId)) throw new Error("bad run id");
  return { runId };
}

export async function stopRun(s: Session, runId: string): Promise<void> {
  if (!RUN_RE.test(runId)) return;
  await request(s, `/runs/${runId}/stop`, { method: "POST", body: "{}" });
}
