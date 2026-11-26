/**
 * Activity frame contract (backend WS /api/v1/events):
 *   {seq, prev, kind, runId, projectId, ts, payload}
 * Kinds are set by server code only. Anything outside the allow-list is dropped here; payload fields are
 * read through explicit typed accessors (never spread into props) and sanitised on read.
 */
import { clean, cleanList, num, SHORT_CAP, TEXT_CAP } from "./sanitize";

/** Contract kinds plus the DESIGN 5.1 names that map onto them. */
export const KINDS = [
  "run.started",
  "run.finished",
  "plan.proposed",
  "plan.decided",
  "step.started",
  "tool.call",
  "tool.result",
  "file.diff",
  "agent.typing",
  "agent.text",
  "build.started",
  "build.log",
  "build.error",
  "build.finished",
  "preview.captured",
  "preview.revision",
  "preview.interaction",
  "qa.result",
  "approval.requested",
  "approval.decided",
  "device.tap",
  "device.input",
  "device.state",
  "device.log",
  "cost.update",
  "usage",
  "gap",
  "stream.dropped",
  // owner chat per project (aas_runner/chat.py): only a signal to refetch GET /projects/<id>/chat
  "chat.prompt",
  "chat.fallback",
] as const;
export type Kind = (typeof KINDS)[number];
const KIND_SET: ReadonlySet<string> = new Set(KINDS);

/**
 * Frames larger than this are discarded before JSON.parse. The backend caps the payload JSON at 8 KiB
 * (activity.py MAX_PAYLOAD_JSON) and the envelope is a few hundred bytes, so 64 KiB leaves wide headroom.
 */
export const MAX_FRAME_CHARS = 64 * 1024;

export interface ActivityEvent {
  seq: number;
  prev: number | null;
  kind: Kind;
  runId: string;
  projectId: string;
  ts: number;
  /** untrusted; read only through the accessors below */
  payload: Readonly<Record<string, unknown>>;
}

const ID_RE = /^[A-Za-z0-9_.:-]{1,64}$/;

function safeId(v: unknown): string {
  return typeof v === "string" && ID_RE.test(v) ? v : "";
}

function parseTs(v: unknown): number {
  if (typeof v === "number" && Number.isFinite(v)) return v < 1e12 ? v * 1000 : v;
  if (typeof v === "string") {
    const t = Date.parse(v);
    if (Number.isFinite(t)) return t;
  }
  return Date.now();
}

export type ParseResult = { ok: true; event: ActivityEvent } | { ok: false; reason: string };

export function parseFrame(raw: unknown): ParseResult {
  if (typeof raw !== "string") return { ok: false, reason: "binary frame" };
  if (raw.length > MAX_FRAME_CHARS) return { ok: false, reason: "oversize frame" };
  let obj: unknown;
  try {
    obj = JSON.parse(raw);
  } catch {
    return { ok: false, reason: "invalid json" };
  }
  return parseObject(obj);
}

export function parseObject(obj: unknown): ParseResult {
  if (!obj || typeof obj !== "object" || Array.isArray(obj)) return { ok: false, reason: "not an object" };
  const o = obj as Record<string, unknown>;
  const seq = o.seq;
  if (typeof seq !== "number" || !Number.isSafeInteger(seq) || seq < 1) return { ok: false, reason: "bad seq" };
  const kind = o.kind;
  if (typeof kind !== "string" || !KIND_SET.has(kind)) return { ok: false, reason: "unknown kind" };
  const prev = typeof o.prev === "number" && Number.isSafeInteger(o.prev) && o.prev >= 0 ? o.prev : null;
  const payload =
    o.payload && typeof o.payload === "object" && !Array.isArray(o.payload)
      ? (o.payload as Record<string, unknown>)
      : {};
  return {
    ok: true,
    event: {
      seq,
      prev,
      kind: kind as Kind,
      runId: safeId(o.runId),
      projectId: safeId(o.projectId),
      ts: parseTs(o.ts),
      payload,
    },
  };
}

// ---- typed payload accessors (all sanitised) ----
export const P = {
  text: (e: ActivityEvent, key: string, cap = TEXT_CAP) => clean(e.payload[key], cap),
  short: (e: ActivityEvent, key: string) => clean(e.payload[key], SHORT_CAP),
  num: (e: ActivityEvent, key: string) => num(e.payload[key]),
  bool: (e: ActivityEvent, key: string) => e.payload[key] === true,
  list: (e: ActivityEvent, key: string) => cleanList(e.payload[key]),
  id: (e: ActivityEvent, key: string) => safeId(e.payload[key]),
};

/** Project id that may appear in the preview host label p-<id>.localhost. */
const PROJECT_HOST_RE = /^[a-z0-9]{8,32}$/;
export function isPreviewProjectId(id: string): boolean {
  return PROJECT_HOST_RE.test(id);
}

/** The backend names projects prj_<16 lowercase hex>; the preview host label is the 16 hex digits. "" = not valid. */
const BACKEND_PROJECT_RE = /^prj_([0-9a-f]{16})$/;
export function previewLabel(id: string): string {
  const m = BACKEND_PROJECT_RE.exec(id);
  if (m) return m[1]!;
  return isPreviewProjectId(id) ? id : "";
}

/** Server-side gap marker: {type: "gap", after, before} = rows after+1 .. before-1 are missing from the table. */
export function parseGapFrame(raw: unknown): { after: number; before: number } | null {
  if (typeof raw !== "string" || raw.length > MAX_FRAME_CHARS || !raw.includes('"gap"')) return null;
  let o: unknown;
  try {
    o = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!o || typeof o !== "object" || Array.isArray(o)) return null;
  const g = o as Record<string, unknown>;
  if (g.type !== "gap" || "kind" in g) return null;
  const { after, before } = g;
  if (typeof after !== "number" || typeof before !== "number") return null;
  if (!Number.isSafeInteger(after) || !Number.isSafeInteger(before) || after < 0 || before <= after) return null;
  return { after, before };
}
