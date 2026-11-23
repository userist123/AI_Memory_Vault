/** REST client for http://127.0.0.1:17875/api/v1 (Bearer token). Responses are size-capped and validated. */
import type { Session } from "./session";
import { clean, inspect, inspectList, SHORT_CAP } from "./sanitize";

const MAX_BODY = 1024 * 1024;
const MAX_ERROR_BODY = 16 * 1024;

/** A non-2xx API reply. `serverMessage` is the server's `message` field, sanitised and capped ("" if none). */
export class ApiError extends Error {
  readonly status: number;
  readonly serverMessage: string;
  constructor(status: number, serverMessage: string) {
    super(serverMessage ? `${serverMessage} (HTTP ${status})` : `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.serverMessage = serverMessage;
  }
}

async function apiError(res: Response): Promise<ApiError> {
  let msg = "";
  try {
    const text = (await res.text()).slice(0, MAX_ERROR_BODY);
    const body = JSON.parse(text) as unknown;
    if (body && typeof body === "object") msg = clean((body as { message?: unknown }).message, SHORT_CAP).trim();
  } catch {
    msg = "";
  }
  return new ApiError(res.status, msg);
}

/** Sanitised, capped text of any thrown value, for an error line in the UI (React text node only). */
export function errorText(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error) return clean(e.message, SHORT_CAP).trim() || "unknown error";
  return "unknown error";
}

/**
 * A project title the backend accepts (^[A-Za-z0-9][A-Za-z0-9 _.-]{0,63}$) from the first words of a prompt:
 * accents folded, every other character dropped, spaces collapsed, at most 40 chars; "App" when nothing is left.
 */
export function projectTitle(prompt: string): string {
  const folded = (typeof prompt === "string" ? prompt : "").slice(0, 400).normalize("NFKD").replace(/[\u0300-\u036f]/g, "");
  const t = folded.replace(/[^A-Za-z0-9 _.\-]+/g, " ").replace(/\s+/g, " ").replace(/^[^A-Za-z0-9]+/, "").slice(0, 40).trim();
  return t || "App";
}

async function request(s: Session, path: string, init?: RequestInit): Promise<unknown> {
  const res = await fetch(`${s.apiBase}/api/v1${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  if (!res.ok) throw await apiError(res);
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
  /** sha256 of the plan content stored at submit; the decision is bound to it (S7). Missing/invalid -> unsafe. */
  contentSha256: string;
  unsafe: boolean;
}

export interface ServerRecords {
  approvals: ApprovalRecord[];
  plans: PlanRecord[];
}

const ID_RE = /^[A-Za-z0-9_.:-]{1,64}$/;
const SHA256_RE = /^[0-9a-f]{64}$/;

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
      const contentSha256 = typeof r.contentSha256 === "string" && SHA256_RE.test(r.contentSha256) ? r.contentSha256 : "";
      out.plans.push({
        id: r.id,
        title: title.text,
        steps: steps.items,
        runId,
        contentSha256,
        unsafe: title.unsafe || steps.unsafe || !contentSha256,
      });
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

export async function decidePlan(
  s: Session,
  planId: string,
  contentSha256: string,
  decision: "approve" | "edit",
  note = "",
): Promise<void> {
  if (!ID_RE.test(planId) || !SHA256_RE.test(contentSha256)) return;
  await request(s, `/plans/${encodeURIComponent(planId)}/decide`, {
    method: "POST",
    body: JSON.stringify({ decision, note: note.slice(0, 4000), contentSha256 }),
  });
}

// ---------------------------------------------------------------- owner settings
/** The plan gate (P5f): ON = writes, deletes and builds need an approved plan. Owner-only route, default ON. */
export async function getPlanGate(s: Session): Promise<boolean> {
  const body = await request(s, "/settings/plan-gate");
  // fail closed: anything but an explicit false is shown as ON
  return !(body && typeof body === "object" && (body as { enabled?: unknown }).enabled === false);
}

export async function setPlanGate(s: Session, enabled: boolean): Promise<boolean> {
  const body = await request(s, "/settings/plan-gate", { method: "PUT", body: JSON.stringify({ enabled }) });
  return !(body && typeof body === "object" && (body as { enabled?: unknown }).enabled === false);
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

export const TEMPLATES = ["blank", "tabs", "list-detail"] as const;
export type Template = (typeof TEMPLATES)[number];

export async function createProject(s: Session, name: string, template: Template = "blank"): Promise<string> {
  const t: Template = TEMPLATES.includes(template) ? template : "blank";
  const body = await request(s, "/projects", { method: "POST", body: JSON.stringify(t === "blank" ? { name: name.slice(0, 64) } : { name: name.slice(0, 64), template: t }) });
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

// ---------------------------------------------------------------- capture thumbnails (S8)
export const CAPTURE_MAX_BYTES = 8 * 1024 * 1024; // the server refuses bigger captures (capture.MAX_PNG_BYTES)
const PNG_MAGIC = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

/**
 * GET /api/v1/captures/<sha256> with the bearer token. The id is checked before any request; the body must be a PNG
 * (content type and magic), at most CAPTURE_MAX_BYTES, and hash to the id (content-addressed). The caller turns the
 * Blob into a blob: URL (the studio CSP allows img-src 'self' blob: only, never the API origin).
 */
export async function fetchCapture(s: Session, sha256: string): Promise<Blob> {
  if (!SHA256_RE.test(sha256)) throw new Error("invalid capture id");
  const res = await fetch(`${s.apiBase}/api/v1/captures/${sha256}`, {
    headers: { Authorization: `Bearer ${s.token}` },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  if ((res.headers.get("Content-Type") ?? "").split(";")[0]!.trim() !== "image/png") throw new Error("not a PNG");
  const declared = Number(res.headers.get("Content-Length") ?? "0");
  if (declared > CAPTURE_MAX_BYTES) throw new Error("capture too large");
  const buf = new Uint8Array(await res.arrayBuffer());
  if (buf.length > CAPTURE_MAX_BYTES) throw new Error("capture too large");
  if (buf.length < 8 || PNG_MAGIC.some((b, i) => buf[i] !== b)) throw new Error("not a PNG");
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", buf));
  const hex = Array.from(digest, (x) => x.toString(16).padStart(2, "0")).join("");
  if (hex !== sha256) throw new Error("capture sha256 does not match its id");
  return new Blob([buf], { type: "image/png" });
}

// ---------------------------------------------------------------- export (S9b, owner, two steps)
const APR_RE = /^apr_[0-9a-f]{16}$/;

export type ExportOutcome =
  | { kind: "approval"; approvalId: string; sha256: string }
  | { kind: "done"; path: string; sha256: string }
  | { kind: "blocked"; findings: string[] }
  | { kind: "error"; message: string };

/** POST /projects/<id>/export. 428 = the owner must approve this exact artifact (sha256) first; 422 export_blocked =
 * the final secret scan found key patterns (names only, never values). Every field is validated (fail closed). */
export async function exportProject(
  s: Session,
  projectId: string,
  target: "web" | "android" | "desktop",
  approvalId?: string,
): Promise<ExportOutcome> {
  if (!PROJECT_RE.test(projectId) || (approvalId !== undefined && !APR_RE.test(approvalId)))
    return { kind: "error", message: "invalid id" };
  const res = await fetch(`${s.apiBase}/api/v1/projects/${projectId}/export`, {
    method: "POST",
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    body: JSON.stringify(approvalId ? { target, approvalId } : { target }),
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  const text = await res.text();
  if (text.length > MAX_BODY) return { kind: "error", message: "response too large" };
  let body: Record<string, unknown> = {};
  try {
    const j = text ? (JSON.parse(text) as unknown) : {};
    if (j && typeof j === "object") body = j as Record<string, unknown>;
  } catch {
    return { kind: "error", message: `HTTP ${res.status}` };
  }
  const d = (body.details && typeof body.details === "object" ? body.details : {}) as Record<string, unknown>;
  if (res.status === 428) {
    if (typeof d.approvalId === "string" && APR_RE.test(d.approvalId) && typeof d.sha256 === "string" && SHA256_RE.test(d.sha256))
      return { kind: "approval", approvalId: d.approvalId, sha256: d.sha256 };
    return { kind: "error", message: "malformed approval request" };
  }
  if (res.status === 422 && body.error === "export_blocked") {
    const findings = (Array.isArray(d.findings) ? d.findings.slice(0, 20) : []).map((f) => {
      const r = (f && typeof f === "object" ? f : {}) as Record<string, unknown>;
      return `${inspect(r.pattern, 40).text} in ${inspect(r.file, 200).text}`;
    });
    return { kind: "blocked", findings };
  }
  if (res.ok && typeof body.path === "string" && typeof body.sha256 === "string" && SHA256_RE.test(body.sha256))
    return { kind: "done", path: inspect(body.path, 400).text, sha256: body.sha256 };
  return { kind: "error", message: inspect(body.message ?? `HTTP ${res.status}`, 200).text };
}
