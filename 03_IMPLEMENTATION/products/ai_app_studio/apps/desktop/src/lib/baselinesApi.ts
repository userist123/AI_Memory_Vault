/**
 * Visual baselines + acceptance checklist client (WOW W6; server: apps/backend/baseline_routes.py). Responses are
 * size-capped and validated here; every text shown is sanitised (inspect) and rendered as a React text node only.
 * Images are fetched with the owner token, checked (PNG magic, size, sha256 = id) and handed out as blob: URLs.
 */
import type { Session } from "./session";
import { inspect } from "./sanitize";

const MAX_BODY = 1024 * 1024;
const MAX_PNG = 8 * 1024 * 1024;
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
const SHA256_RE = /^[0-9a-f]{64}$/;
const FRAMES = ["phone", "tablet", "desktop"] as const;
const PNG_MAGIC = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

export type Frame = (typeof FRAMES)[number];
export interface Baseline { route: string; frame: Frame; sha256: string; revision: number }
export interface CheckRow {
  key: string;
  route: string;
  frame: Frame;
  status: "same" | "changed" | "baseline_missing";
  changedPct: number | null;
  baselineSha256: string;
  currentSha256: string;
  diffSha256: string;
}
export interface BaselineState {
  thresholdPct: number;
  baselines: Baseline[];
  check: { at: string; revision: number; rows: CheckRow[]; warnings: string[] } | null;
}
export type FlowStatus = "pass" | "fail" | "skip" | "unknown";
export interface Flow { id: string; title: string; statements: string[]; status: FlowStatus; message: string }

const obj = (v: unknown): Record<string, unknown> => (v && typeof v === "object" && !Array.isArray(v) ? (v as Record<string, unknown>) : {});
const sha = (v: unknown): string => (typeof v === "string" && SHA256_RE.test(v) ? v : "");
const frame = (v: unknown): Frame | null => (FRAMES as readonly string[]).includes(v as string) ? (v as Frame) : null;
const num = (v: unknown): number | null => (typeof v === "number" && Number.isFinite(v) ? v : null);

export function parseState(body: unknown): BaselineState {
  const b = obj(body);
  const baselines: Baseline[] = [];
  for (const raw of Array.isArray(b.baselines) ? b.baselines.slice(0, 64) : []) {
    const r = obj(raw);
    const f = frame(r.frame);
    if (f && sha(r.sha256)) baselines.push({ route: inspect(r.route, 200).text, frame: f, sha256: sha(r.sha256), revision: num(r.revision) ?? 0 });
  }
  const lc = obj(b.lastCheck);
  let check: BaselineState["check"] = null;
  if (Array.isArray(lc.results)) {
    const rows: CheckRow[] = [];
    for (const raw of lc.results.slice(0, 64)) {
      const r = obj(raw);
      const f = frame(r.frame);
      const st = r.status === "same" || r.status === "changed" || r.status === "baseline_missing" ? r.status : null;
      if (!f || !st) continue;
      rows.push({
        key: inspect(r.key, 220).text,
        route: inspect(r.route, 200).text,
        frame: f,
        status: st,
        changedPct: num(r.changedPct),
        baselineSha256: sha(r.baselineSha256),
        currentSha256: sha(r.currentSha256),
        diffSha256: sha(r.diffSha256),
      });
    }
    const warnings = (Array.isArray(lc.warnings) ? lc.warnings.slice(0, 32) : []).map((w) => inspect(w, 400).text);
    check = { at: inspect(lc.at, 40).text, revision: num(lc.revision) ?? 0, rows, warnings };
  }
  return { thresholdPct: num(b.thresholdPct) ?? 0.1, baselines, check };
}

export function parseChecklist(body: unknown): Flow[] {
  const flows = obj(body).flows;
  return (Array.isArray(flows) ? flows.slice(0, 20) : []).map((raw) => {
    const f = obj(raw);
    const res = obj(f.result);
    const st = res.status === "pass" || res.status === "fail" || res.status === "skip" ? res.status : "unknown";
    return {
      id: inspect(f.id, 40).text,
      title: inspect(f.title, 120).text,
      statements: (Array.isArray(f.statements) ? f.statements.slice(0, 30) : []).map((s) => inspect(s, 300).text),
      status: st,
      message: inspect(res.message, 300).text,
    };
  });
}

/** "# Title" starts a flow, "- text" (or any other line) adds a statement; the flow id is the title's slug. */
export function checklistFromText(text: string): { id: string; title: string; statements: string[] }[] {
  const out: { id: string; title: string; statements: string[] }[] = [];
  const used = new Set<string>();
  for (const line of text.split(/\r?\n/)) {
    const t = line.trim();
    if (!t) continue;
    if (t.startsWith("#")) {
      const title = t.replace(/^#+\s*/, "").slice(0, 120);
      if (!title) continue;
      let base = title.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 32) || "flow";
      if (!/^[a-z0-9]/.test(base)) base = `f${base}`;
      let id = base;
      for (let i = 2; used.has(id); i++) id = `${base}-${i}`;
      used.add(id);
      out.push({ id, title, statements: [] });
    } else {
      if (!out.length) out.push({ id: "general", title: "General", statements: [] }), used.add("general");
      out[out.length - 1]!.statements.push(t.replace(/^[-*]\s*/, "").slice(0, 300));
    }
  }
  return out.slice(0, 20).map((f) => ({ ...f, statements: f.statements.filter(Boolean).slice(0, 30) }));
}

export function checklistToText(flows: Flow[]): string {
  return flows.map((f) => [`# ${f.title}`, ...f.statements.map((s) => `- ${s}`)].join("\n")).join("\n\n");
}

async function call(s: Session, path: string, init: RequestInit = {}): Promise<unknown> {
  const res = await fetch(`${s.apiBase}/api/v1${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  const text = await res.text();
  if (text.length > MAX_BODY) throw new Error("response too large");
  let body: unknown = {};
  try {
    body = text ? JSON.parse(text) : {};
  } catch {
    body = {};
  }
  if (!res.ok) throw new Error(inspect(obj(body).message ?? `HTTP ${res.status}`, 300).text || `HTTP ${res.status}`);
  return body;
}

export interface ChangesApi {
  status: (projectId: string) => Promise<BaselineState>;
  approve: (projectId: string) => Promise<BaselineState>;
  check: (projectId: string) => Promise<BaselineState>;
  image: (projectId: string, sha256: string) => Promise<string>;
  checklist: (projectId: string) => Promise<Flow[]>;
  saveChecklist: (projectId: string, text: string) => Promise<Flow[]>;
}

export function changesApi(s: Session): ChangesApi {
  const guard = (pid: string) => {
    if (!PROJECT_RE.test(pid)) throw new Error("invalid project id");
    return pid;
  };
  return {
    status: async (p) => parseState(await call(s, `/projects/${guard(p)}/baselines`)),
    approve: async (p) => parseState(await call(s, `/projects/${guard(p)}/baselines/approve`, { method: "POST", body: "{}" })),
    check: async (p) => parseState(await call(s, `/projects/${guard(p)}/baselines/check`, { method: "POST", body: "{}" })),
    checklist: async (p) => parseChecklist(await call(s, `/projects/${guard(p)}/checklist`)),
    saveChecklist: async (p, text) =>
      parseChecklist(await call(s, `/projects/${guard(p)}/checklist`, { method: "PUT", body: JSON.stringify({ flows: checklistFromText(text) }) })),
    image: async (p, id) => {
      if (!SHA256_RE.test(id)) throw new Error("invalid image id");
      const res = await fetch(`${s.apiBase}/api/v1/projects/${guard(p)}/baselines/images/${id}`, {
        headers: { Authorization: `Bearer ${s.token}` },
        credentials: "omit",
        redirect: "error",
        referrerPolicy: "no-referrer",
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      if ((res.headers.get("Content-Type") ?? "").split(";")[0]!.trim() !== "image/png") throw new Error("not a PNG");
      const buf = new Uint8Array(await res.arrayBuffer());
      if (buf.length > MAX_PNG || buf.length < 8 || PNG_MAGIC.some((b, i) => buf[i] !== b)) throw new Error("not a PNG");
      const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", buf));
      if (Array.from(digest, (x) => x.toString(16).padStart(2, "0")).join("") !== id) throw new Error("image sha256 mismatch");
      return URL.createObjectURL(new Blob([buf], { type: "image/png" }));
    },
  };
}
