/**
 * Owner chat per project (backend apps/backend/chat_routes.py). Same transport rules as api.ts: Bearer token, no
 * cookies, no redirects, size-capped bodies; every text field is sanitised on read and rendered as a React text node.
 */
import { ApiError } from "./api";
import type { Session } from "./session";
import { clean, SHORT_CAP } from "./sanitize";

const MAX_BODY = 1024 * 1024;
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
const RUN_RE = /^run_[0-9a-f]{16}$/;
const PLAN_RE = /^[A-Za-z0-9_.:-]{1,64}$/;
export const CHAT_MAX = 4000; // = the server's chat message cap (chat_routes.MAX_MESSAGE)
export const REPLY_CAP = 8000;

export interface ChatTurn {
  runId: string;
  prompt: string;
  reply: string;
  status: string;
  reason: string;
  mode: string;
  costUsd: number | null;
  planIds: string[];
  chat: boolean;
}

export interface ChatState {
  turns: ChatTurn[];
  running: boolean;
  busy: boolean;
  continuity: "resume" | "summary";
}

async function call(s: Session, path: string, init?: RequestInit): Promise<unknown> {
  const res = await fetch(`${s.apiBase}/api/v1${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  const text = await res.text();
  if (text.length > MAX_BODY) throw new Error("response too large");
  if (!res.ok) {
    let msg = "";
    try {
      const body = JSON.parse(text.slice(0, 16 * 1024)) as unknown;
      if (body && typeof body === "object") msg = clean((body as { message?: unknown }).message, SHORT_CAP).trim();
    } catch {
      msg = "";
    }
    throw new ApiError(res.status, msg);
  }
  return text ? (JSON.parse(text) as unknown) : null;
}

export function parseChat(body: unknown): ChatState {
  const b = body && typeof body === "object" ? (body as Record<string, unknown>) : {};
  const turns: ChatTurn[] = [];
  for (const t of Array.isArray(b.turns) ? b.turns.slice(-50) : []) {
    if (!t || typeof t !== "object") continue;
    const r = t as Record<string, unknown>;
    if (typeof r.runId !== "string" || !RUN_RE.test(r.runId)) continue;
    const cost = typeof r.costUsd === "number" && Number.isFinite(r.costUsd) && r.costUsd >= 0 ? r.costUsd : null;
    turns.push({
      runId: r.runId,
      prompt: clean(r.prompt, CHAT_MAX),
      reply: clean(r.reply, REPLY_CAP),
      status: clean(r.status, 40),
      reason: clean(r.reason, 40),
      mode: clean(r.mode, 16),
      costUsd: cost,
      planIds: (Array.isArray(r.planIds) ? r.planIds : [])
        .filter((p): p is string => typeof p === "string" && PLAN_RE.test(p))
        .slice(0, 20),
      chat: r.chat === true,
    });
  }
  return {
    turns,
    running: b.running === true,
    busy: b.busy === true,
    continuity: b.continuity === "resume" ? "resume" : "summary",
  };
}

export async function fetchChat(s: Session, projectId: string): Promise<ChatState> {
  if (!PROJECT_RE.test(projectId)) throw new Error("bad project id");
  return parseChat(await call(s, `/projects/${projectId}/chat`));
}

export async function sendChat(s: Session, projectId: string, message: string): Promise<{ runId: string }> {
  if (!PROJECT_RE.test(projectId)) throw new Error("bad project id");
  const body = await call(s, `/projects/${projectId}/chat`, {
    method: "POST",
    body: JSON.stringify({ message: message.slice(0, CHAT_MAX) }),
  });
  const runId = body && typeof body === "object" ? (body as { runId?: unknown }).runId : undefined;
  if (typeof runId !== "string" || !RUN_RE.test(runId)) throw new Error("bad run id");
  return { runId };
}
