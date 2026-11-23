/**
 * W2 quality gates client (backend apps/backend/qa_routes.py). Same transport rules as exportApi.ts: Bearer token, no
 * cookies, no redirects, size-capped bodies. Every page-derived text (selectors, help, console) is sanitised on read
 * and rendered as a React text node only; counts are validated numbers.
 */
import type { Session } from "./session";
import { clean, cleanList, num } from "./sanitize";

const MAX_BODY = 512 * 1024;
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
export const SEVERITIES = ["critical", "serious", "moderate", "minor"] as const;
export type Severity = (typeof SEVERITIES)[number];
export type Counts = Record<Severity, number> & { total: number };

export interface QaIssue {
  severity: Severity;
  rule: string;
  frames: string[];
  help: string;
  targets: string[];
  count: number;
}

export interface QaResult {
  passed: boolean;
  counts: Counts;
  issues: QaIssue[];
  site: string;
}

export interface QaState {
  result: QaResult | null;
  stale: boolean;
  polish: PolishState;
}

export interface PolishState {
  state: "idle" | "running" | "done" | "stopped" | "failed";
  loop: number;
  maxLoops: number;
  before: Counts | null;
  after: Counts | null;
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
  if (res.status >= 400) throw new Error(clean(body.message, 300) || `HTTP ${res.status}`);
  return { status: res.status, body };
}

export function parseCounts(v: unknown): Counts | null {
  if (!v || typeof v !== "object") return null;
  const o = v as Record<string, unknown>;
  const c = { total: 0 } as Counts;
  for (const s of SEVERITIES) {
    const n = num(o[s]);
    if (n === null) return null;
    c[s] = Math.floor(n);
  }
  c.total = Math.floor(num(o.total) ?? SEVERITIES.reduce((a, s) => a + c[s], 0));
  return c;
}

export function parseResult(v: unknown): QaResult | null {
  if (!v || typeof v !== "object") return null;
  const o = v as Record<string, unknown>;
  const counts = parseCounts(o.counts);
  if (!counts) return null;
  const issues: QaIssue[] = (Array.isArray(o.issues) ? o.issues.slice(0, 40) : []).flatMap((i) => {
    if (!i || typeof i !== "object") return [];
    const r = i as Record<string, unknown>;
    const sev = SEVERITIES.find((s) => s === r.severity) ?? "minor";
    return [{ severity: sev, rule: clean(r.rule, 60), frames: cleanList(r.frames, 3, 10), help: clean(r.help, 200),
              targets: cleanList(r.targets, 3, 160), count: Math.floor(num(r.count) ?? 1) }];
  });
  return { passed: o.passed === true, counts, issues, site: clean(o.site, 40) };
}

export function parsePolish(v: unknown): PolishState {
  const o = (v && typeof v === "object" ? v : {}) as Record<string, unknown>;
  const states = ["idle", "running", "done", "stopped", "failed"] as const;
  return {
    state: states.find((s) => s === o.state) ?? "idle",
    loop: Math.floor(num(o.loop) ?? 0),
    maxLoops: Math.floor(num(o.maxLoops) ?? 2),
    before: parseCounts(o.before),
    after: parseCounts(o.after),
    step: clean(o.step, 200),
    message: clean(o.message, 300),
  };
}

function pid(projectId: string): string {
  if (!PROJECT_RE.test(projectId)) throw new Error("invalid project id");
  return projectId;
}

export async function fetchQa(s: Session, projectId: string): Promise<QaState> {
  const { body } = await call(s, `/projects/${pid(projectId)}/qa`);
  return { result: parseResult(body.result), stale: body.stale === true, polish: parsePolish(body.polish) };
}

export async function runQa(s: Session, projectId: string, auto: boolean): Promise<QaResult | null> {
  const { body } = await call(s, `/projects/${pid(projectId)}/qa`, { method: "POST", body: JSON.stringify({ auto }) });
  return parseResult(body.result);
}

export async function startPolish(s: Session, projectId: string): Promise<PolishState> {
  const { body } = await call(s, `/projects/${pid(projectId)}/qa/polish`, { method: "POST", body: "{}" });
  return parsePolish(body);
}

export async function polishStatus(s: Session, projectId: string): Promise<PolishState> {
  const { body } = await call(s, `/projects/${pid(projectId)}/qa/polish`);
  return parsePolish(body);
}

export function countsLabel(c: Counts | null): string {
  if (!c) return "-";
  return SEVERITIES.filter((s) => c[s] > 0).map((s) => `${c[s]} ${s}`).join(" · ") || "0 issues";
}
