/**
 * Owner export client (owner report 2026-11-23 "Nu merge sa o export APK"). Export -> 409 build_required -> owner build
 * (POST/GET /projects/<id>/build) -> 428 approval bound to the artifact sha256 -> approve -> written -> the export
 * folder can be opened (POST /projects/<id>/export/open-folder: the server computes the path). Responses are
 * size-capped and validated; every text shown is sanitised (inspect) and rendered as a React text node only.
 */
import type { Session } from "./session";
import { inspect } from "./sanitize";

const MAX_BODY = 1024 * 1024;
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
const APR_RE = /^apr_[0-9a-f]{16}$/;
const SHA256_RE = /^[0-9a-f]{64}$/;

export type ExportTarget = "web" | "android" | "desktop";

export type PanelOutcome =
  | { kind: "approval"; approvalId: string; sha256: string; approved: boolean }
  | { kind: "done"; path: string; sha256: string; bytes: number }
  | { kind: "blocked"; findings: string[] }
  | { kind: "build_required"; message: string }
  | { kind: "error"; message: string };

export interface BuildState {
  state: "idle" | "building" | "ok" | "failed";
  step: string;
  message: string;
}

async function call(s: Session, path: string, init: RequestInit = {}): Promise<{ status: number; body: Record<string, unknown> }> {
  const res = await fetch(`${s.apiBase}/api/v1${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  const text = await res.text();
  if (text.length > MAX_BODY) return { status: res.status, body: { message: "response too large" } };
  let body: Record<string, unknown> = {};
  try {
    const j = text ? (JSON.parse(text) as unknown) : {};
    if (j && typeof j === "object" && !Array.isArray(j)) body = j as Record<string, unknown>;
  } catch {
    body = {};
  }
  return { status: res.status, body };
}

function message(status: number, body: Record<string, unknown>): string {
  return inspect(body.message ?? `HTTP ${status}`, 300).text || `HTTP ${status}`;
}

export function parseExport(status: number, body: Record<string, unknown>): PanelOutcome {
  const d = (body.details && typeof body.details === "object" ? body.details : {}) as Record<string, unknown>;
  if (status === 428) {
    if (typeof d.approvalId === "string" && APR_RE.test(d.approvalId) && typeof d.sha256 === "string" && SHA256_RE.test(d.sha256))
      return { kind: "approval", approvalId: d.approvalId, sha256: d.sha256, approved: d.status === "approved" };
    return { kind: "error", message: "malformed approval request" };
  }
  if (status === 409 && body.error === "build_required") return { kind: "build_required", message: message(status, body) };
  if (status === 422 && body.error === "export_blocked") {
    const findings = (Array.isArray(d.findings) ? d.findings.slice(0, 20) : []).map((f) => {
      const r = (f && typeof f === "object" ? f : {}) as Record<string, unknown>;
      return `${inspect(r.pattern, 40).text} in ${inspect(r.file, 200).text}`;
    });
    return { kind: "blocked", findings };
  }
  if (status === 200 && typeof body.path === "string" && typeof body.sha256 === "string" && SHA256_RE.test(body.sha256))
    return {
      kind: "done",
      path: inspect(body.path, 400).text,
      sha256: body.sha256,
      bytes: typeof body.bytes === "number" && Number.isFinite(body.bytes) && body.bytes >= 0 ? body.bytes : 0,
    };
  return { kind: "error", message: message(status, body) };
}

export async function exportArtifact(s: Session, projectId: string, target: ExportTarget, approvalId?: string): Promise<PanelOutcome> {
  if (!PROJECT_RE.test(projectId) || (approvalId !== undefined && !APR_RE.test(approvalId)))
    return { kind: "error", message: "invalid id" };
  const r = await call(s, `/projects/${projectId}/export`, {
    method: "POST",
    body: JSON.stringify(approvalId ? { target, approvalId } : { target }),
  });
  return parseExport(r.status, r.body);
}

function parseBuild(status: number, body: Record<string, unknown>): BuildState {
  const st = body.state;
  if ((status === 200 || status === 202) && (st === "idle" || st === "building" || st === "ok" || st === "failed"))
    return { state: st, step: inspect(body.step, 20).text, message: inspect(body.message, 500).text };
  return { state: "failed", step: "", message: message(status, body) };
}

export async function startOwnerBuild(s: Session, projectId: string, target: "web" | "android"): Promise<BuildState> {
  if (!PROJECT_RE.test(projectId)) return { state: "failed", step: "", message: "invalid id" };
  const r = await call(s, `/projects/${projectId}/build`, { method: "POST", body: JSON.stringify({ target }) });
  return parseBuild(r.status, r.body);
}

export async function ownerBuildStatus(s: Session, projectId: string): Promise<BuildState> {
  if (!PROJECT_RE.test(projectId)) return { state: "failed", step: "", message: "invalid id" };
  const r = await call(s, `/projects/${projectId}/build`);
  return parseBuild(r.status, r.body);
}

/** Opens the project's export folder in Explorer (server-side, path computed by the server). Throws on failure. */
export async function openExportFolder(s: Session, projectId: string): Promise<void> {
  if (!PROJECT_RE.test(projectId)) throw new Error("invalid id");
  const r = await call(s, `/projects/${projectId}/export/open-folder`, { method: "POST" });
  if (r.status !== 200) throw new Error(message(r.status, r.body));
}
