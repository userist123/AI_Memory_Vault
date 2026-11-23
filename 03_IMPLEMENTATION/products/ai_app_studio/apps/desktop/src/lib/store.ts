/** Activity state: a pure reducer over validated frames plus a tiny external store for React. */
import { type ActivityEvent, P, previewLabel } from "./events";
import { clean, inspect, TEXT_CAP } from "./sanitize";

export type DeviceKind = "phone" | "tablet" | "desktop";
export const DEVICES: Record<DeviceKind, { w: number; h: number; label: string }> = {
  phone: { w: 390, h: 844, label: "Phone 390×844" },
  tablet: { w: 834, h: 1112, label: "Tablet 834×1112" },
  desktop: { w: 1440, h: 900, label: "Desktop 1440×900" },
};
export type TapTarget = DeviceKind | "mirror";

export interface ToolRow {
  callId: string;
  tool: string;
  target: string;
  startedAt: number;
  endedAt: number | null;
  ok: boolean | null;
  added: number;
  removed: number;
  note: string;
  /** tool.result `sha256` (DESIGN 5.2: body kept only as size+sha256), lowercase 64-hex or "" */
  sha256: string;
}
export interface Step {
  id: string;
  title: string;
  startedAt: number;
  endedAt: number | null;
  tools: ToolRow[];
}
export interface FileView {
  path: string;
  revision: number | null;
  diff: string;
  typing: string;
  added: number;
  removed: number;
  updatedAt: number;
}
export interface BuildLine {
  seq: number;
  text: string;
  error: boolean;
  location: string;
}
export interface Tap {
  id: number;
  target: TapTarget;
  x: number;
  y: number;
  at: number;
}
export interface Gap {
  from: number;
  to: number;
  reason: string;
}
export type Panel = "diff" | "build" | "device" | "mirror";
export interface Focus {
  panel: Panel;
  file: string | null;
  device: DeviceKind | null;
  seq: number;
}

export interface State {
  lastSeq: number;
  runId: string;
  projectId: string;
  runStatus: "idle" | "running" | "finished" | "failed";
  runTitle: string;
  startedAt: number | null;
  endedAt: number | null;
  steps: Step[];
  files: Record<string, FileView>;
  fileOrder: string[];
  build: { lines: BuildLine[]; errors: BuildLine[]; status: string };
  revision: number;
  previewProjectId: string;
  /** bumped by every preview.captured / preview.revision: the iframes reload on it (a file.diff already raises revision) */
  previewNonce: number;
  taps: Tap[];
  cost: { inputTokens: number; outputTokens: number; costUsd: number; budgetUsd: number | null };
  gaps: Gap[];
  dropped: number;
  /** sum of `stream.dropped {count}`: agent stream items the server allow-list dropped (DESIGN 5.2) */
  streamDropped: number;
  /** subprotocol the server negotiated when it was not `aas.v1` ("" = none); cleared on a good open */
  protocolError: string | null;
  qa: { passed: number; failed: number; summary: string } | null;
  approvalsVersion: number;
  agentText: string;
  focus: Focus | null;
  connection: "offline" | "connecting" | "open" | "demo";
}

export const initialState: State = {
  lastSeq: 0,
  runId: "",
  projectId: "",
  runStatus: "idle",
  runTitle: "",
  startedAt: null,
  endedAt: null,
  steps: [],
  files: {},
  fileOrder: [],
  build: { lines: [], errors: [], status: "" },
  revision: 0,
  previewProjectId: "",
  previewNonce: 0,
  taps: [],
  cost: { inputTokens: 0, outputTokens: 0, costUsd: 0, budgetUsd: null },
  gaps: [],
  dropped: 0,
  streamDropped: 0,
  protocolError: null,
  qa: null,
  approvalsVersion: 0,
  agentText: "",
  focus: null,
  connection: "offline",
};

export const LIMITS = { steps: 400, toolsPerStep: 200, buildLines: 2000, files: 100, taps: 8, gaps: 50, diff: 16000 };

export type Action =
  | { type: "event"; event: ActivityEvent }
  | { type: "gap"; gap: Gap }
  | { type: "dropped" }
  | { type: "connection"; value: State["connection"] }
  | { type: "protocol"; value: string }
  | { type: "reset" };

const BUILD_LOC = /((?:[\w.@-]+[\\/])*[\w.@-]+\.(?:tsx?|jsx?|mjs|cjs|css|html|json|kt|kts|java|gradle|xml))[:(](\d+)(?:[:,](\d+))?/;
const BUILD_ERR = /\berror\b|\bfailed\b|\bexception\b|✘|ERR!/i;

function parseBuildLine(seq: number, text: string, forceError: boolean): BuildLine {
  const m = BUILD_LOC.exec(text);
  const location = m ? `${m[1]}:${m[2]}${m[3] ? `:${m[3]}` : ""}` : "";
  return { seq, text, error: forceError || BUILD_ERR.test(text), location };
}

const SHA256_RE = /^[0-9a-fA-F]{64}$/;
function sha256Of(v: unknown): string {
  return typeof v === "string" && SHA256_RE.test(v) ? v.toLowerCase() : "";
}

/** Diff viewer target: the selected file, or the newest written file when the selection has no diff yet. */
export function pickShownFile(active: string | null, files: Record<string, FileView>, order: string[]): string | null {
  if (active && files[active]) return active;
  return order[order.length - 1] ?? null;
}

function asDevice(v: string): DeviceKind | null {
  return v === "phone" || v === "tablet" || v === "desktop" ? v : null;
}

function lastStep(steps: Step[], ts: number): [Step[], Step] {
  if (steps.length > 0) return [steps, steps[steps.length - 1]!];
  const implicit: Step = { id: "implicit", title: "Working", startedAt: ts, endedAt: null, tools: [] };
  return [[implicit], implicit];
}

function replaceLast(steps: Step[], s: Step): Step[] {
  const copy = steps.slice();
  copy[copy.length - 1] = s;
  return copy;
}

function countDiff(diff: string): [number, number] {
  let a = 0;
  let r = 0;
  for (const line of diff.split("\n")) {
    if (line.startsWith("+++") || line.startsWith("---")) continue;
    if (line.startsWith("+")) a++;
    else if (line.startsWith("-")) r++;
  }
  return [a, r];
}

function upsertFile(state: State, path: string, patch: Partial<FileView>, ts: number): State {
  const prev = state.files[path];
  const next: FileView = {
    path,
    revision: null,
    diff: "",
    typing: "",
    added: 0,
    removed: 0,
    ...prev,
    ...patch,
    updatedAt: ts,
  };
  let order = state.fileOrder.filter((p) => p !== path);
  order.push(path);
  const files = { ...state.files, [path]: next };
  if (order.length > LIMITS.files) {
    const evicted = order.slice(0, order.length - LIMITS.files);
    order = order.slice(order.length - LIMITS.files);
    for (const p of evicted) delete files[p];
  }
  return { ...state, files, fileOrder: order };
}

function applyEvent(s0: State, e: ActivityEvent): State {
  if (e.seq <= s0.lastSeq) return s0; // duplicate / replay overlap
  let s: State = { ...s0, lastSeq: e.seq };
  if (e.runId && e.runId !== s.runId && e.kind === "run.started") {
    // a new run: keep connection + approvals counter, clear the rest
    s = {
      ...initialState,
      connection: s.connection,
      approvalsVersion: s.approvalsVersion,
      lastSeq: e.seq,
      gaps: s.gaps,
      dropped: s.dropped,
      protocolError: s.protocolError,
    };
  }
  if (e.runId) s.runId = e.runId;
  if (e.projectId) s.projectId = e.projectId;
  const ts = e.ts;
  const focus = (panel: Panel, file: string | null = null, device: DeviceKind | null = null) => {
    s.focus = { panel, file, device, seq: e.seq };
  };

  switch (e.kind) {
    case "run.started": {
      s.runStatus = "running";
      s.runTitle = P.text(e, "title", 200);
      s.startedAt = ts;
      s.endedAt = null;
      const bud = P.num(e, "budgetUsd");
      if (bud !== null) s.cost = { ...s.cost, budgetUsd: bud };
      {
        const label = previewLabel(e.projectId);
        if (label) s.previewProjectId = label;
      }
      break;
    }
    case "run.finished": {
      const status = P.short(e, "status");
      s.runStatus = status === "failed" || status === "error" ? "failed" : "finished";
      s.endedAt = ts;
      if (s.steps.length) {
        const last = s.steps[s.steps.length - 1]!;
        if (last.endedAt === null) s.steps = replaceLast(s.steps, { ...last, endedAt: ts });
      }
      break;
    }
    case "plan.proposed":
    case "plan.decided": {
      // never rendered from the event: the plan card comes only from the server record (GET /api/v1/approvals,
      // kind "plan"); the event is just a signal to refetch
      s.approvalsVersion = s.approvalsVersion + 1;
      break;
    }
    case "step.started": {
      let steps = s.steps.slice();
      const prev = steps[steps.length - 1];
      if (prev && prev.endedAt === null) steps[steps.length - 1] = { ...prev, endedAt: ts };
      steps.push({
        id: P.id(e, "stepId") || `s${e.seq}`,
        title: P.text(e, "title", 300) || "Step",
        startedAt: ts,
        endedAt: null,
        tools: [],
      });
      if (steps.length > LIMITS.steps) steps = steps.slice(steps.length - LIMITS.steps);
      s.steps = steps;
      break;
    }
    case "tool.call": {
      const [steps, step] = lastStep(s.steps, ts);
      const row: ToolRow = {
        callId: P.id(e, "callId") || `c${e.seq}`,
        tool: P.short(e, "tool") || "tool",
        target: inspect(e.payload.target, 500).text,
        startedAt: ts,
        endedAt: null,
        ok: null,
        added: 0,
        removed: 0,
        note: inspect(e.payload.input, 2000, false).text,
        sha256: "",
      };
      const tools = step.tools.concat(row).slice(-LIMITS.toolsPerStep);
      s.steps = replaceLast(steps, { ...step, tools });
      if (row.target && /\.[A-Za-z0-9]{1,6}$/.test(row.target)) focus("diff", row.target);
      break;
    }
    case "tool.result": {
      const callId = P.id(e, "callId");
      s.steps = s.steps.map((st) => {
        const idx = st.tools.findIndex((t) => t.callId === callId);
        if (idx < 0) return st;
        const tools = st.tools.slice();
        const t = tools[idx]!;
        tools[idx] = {
          ...t,
          endedAt: ts,
          ok: e.payload.ok === false || e.payload.isError === true ? false : true,
          note: P.text(e, "excerpt", 300) || t.note,
          sha256: sha256Of(e.payload.sha256) || t.sha256,
        };
        return { ...st, tools };
      });
      break;
    }
    case "file.diff": {
      const path = P.text(e, "path", 500);
      if (!path) break;
      const diff = clean(e.payload.diff, LIMITS.diff);
      const [ca, cr] = countDiff(diff);
      const added = P.num(e, "added") ?? ca;
      const removed = P.num(e, "removed") ?? cr;
      s = upsertFile(s, path, { diff, typing: "", added, removed, revision: P.num(e, "revision") }, ts);
      // attach +N/-M to the newest tool row targeting this path (or the newest row)
      if (s.steps.length) {
        const last = s.steps[s.steps.length - 1]!;
        let idx = -1;
        for (let i = last.tools.length - 1; i >= 0; i--) {
          if (last.tools[i]!.target === path) {
            idx = i;
            break;
          }
        }
        if (idx < 0) idx = last.tools.length - 1;
        if (idx >= 0) {
          const tools = last.tools.slice();
          tools[idx] = { ...tools[idx]!, added, removed };
          s.steps = replaceLast(s.steps, { ...last, tools });
        }
      }
      const rev = P.num(e, "revision");
      if (rev !== null && rev > s.revision) s.revision = rev;
      focus("diff", path);
      break;
    }
    case "agent.typing": {
      const path = P.text(e, "path", 500);
      if (!path) break;
      const prev = s.files[path]?.typing ?? "";
      const chunk = clean(e.payload.text, TEXT_CAP);
      const typing = (prev + chunk).slice(-LIMITS.diff);
      s = upsertFile(s, path, { typing }, ts);
      focus("diff", path);
      break;
    }
    case "agent.text": {
      s.agentText = P.text(e, "text");
      break;
    }
    case "build.started": {
      s.build = { lines: [], errors: [], status: "running" };
      focus("build");
      break;
    }
    case "build.log":
    case "build.error": {
      const text = P.text(e, "line", 2000) || P.text(e, "text", 2000);
      const line = parseBuildLine(e.seq, text, e.kind === "build.error");
      const lines = s.build.lines.concat(line).slice(-LIMITS.buildLines);
      const errors = line.error ? s.build.errors.concat(line).slice(-200) : s.build.errors;
      s.build = { ...s.build, lines, errors, status: s.build.status || "running" };
      focus("build");
      break;
    }
    case "build.finished": {
      s.build = { ...s.build, status: P.short(e, "status") || "finished" };
      focus("build");
      break;
    }
    case "preview.captured":
    case "preview.revision": {
      s.previewNonce = s.previewNonce + 1;
      const rev = P.num(e, "revision");
      if (rev !== null && rev > s.revision) s.revision = rev;
      else if (rev === null) s.revision = s.revision + 1;
      {
        const label = previewLabel(e.projectId);
        if (label) s.previewProjectId = label;
      }
      focus("device", null, asDevice(P.short(e, "frame")) ?? "phone");
      break;
    }
    case "preview.interaction":
    case "device.tap":
    case "device.input": {
      const x = P.num(e, "x");
      const y = P.num(e, "y");
      const frame = asDevice(P.short(e, "frame"));
      const target: TapTarget = e.kind === "preview.interaction" ? (frame ?? "phone") : (frame ?? "mirror");
      if (x !== null && y !== null) {
        const wall = Date.now();
        const at = Math.abs(wall - ts) < 10_000 ? wall : ts; // replayed old taps do not animate again
        s.taps = s.taps.concat({ id: e.seq, target, x, y, at }).slice(-LIMITS.taps);
      }
      if (target === "mirror") focus("mirror");
      else focus("device", null, target);
      break;
    }
    case "device.state":
    case "device.log": {
      focus("mirror");
      break;
    }
    case "qa.result": {
      s.qa = {
        passed: P.num(e, "passed") ?? 0,
        failed: P.num(e, "failed") ?? 0,
        summary: P.text(e, "summary", 500),
      };
      break;
    }
    case "approval.requested":
    case "approval.decided": {
      // never rendered from the event: only a signal to refetch the server records
      s.approvalsVersion = s.approvalsVersion + 1;
      break;
    }
    case "cost.update":
    case "usage": {
      s.cost = {
        inputTokens: P.num(e, "inputTokens") ?? s.cost.inputTokens,
        outputTokens: P.num(e, "outputTokens") ?? s.cost.outputTokens,
        costUsd: P.num(e, "costUsd") ?? s.cost.costUsd,
        budgetUsd: P.num(e, "budgetUsd") ?? s.cost.budgetUsd,
      };
      break;
    }
    case "stream.dropped": {
      // not a transport gap: the server's stream-json allow-list dropped `count` items (DESIGN 5.2)
      const n = P.num(e, "count");
      const add = n !== null && Number.isSafeInteger(n) && n > 0 ? Math.min(n, 1_000_000) : 1;
      s.streamDropped = Math.min(s.streamDropped + add, Number.MAX_SAFE_INTEGER);
      break;
    }
    case "gap": {
      const from = P.num(e, "from") ?? s0.lastSeq + 1;
      const to = P.num(e, "to") ?? e.seq;
      s.gaps = s.gaps.concat({ from, to, reason: P.short(e, "reason") || e.kind }).slice(-LIMITS.gaps);
      break;
    }
  }
  return s;
}

export function reducer(state: State, action: Action): State {
  switch (action.type) {
    case "event":
      return applyEvent(state, action.event);
    case "gap":
      return { ...state, gaps: state.gaps.concat(action.gap).slice(-LIMITS.gaps) };
    case "dropped":
      return { ...state, dropped: state.dropped + 1 };
    case "connection":
      return action.value === "open"
        ? { ...state, connection: action.value, protocolError: null }
        : { ...state, connection: action.value };
    case "protocol":
      // never keep a token-bearing value (a broken server echoing aas.token.<t>) in state or on screen
      return { ...state, protocolError: action.value.startsWith("aas.token.") ? "aas.token.<redacted>" : clean(action.value, 64) };
    case "reset":
      return { ...initialState, connection: state.connection };
  }
}

export interface Store {
  get(): State;
  dispatch(a: Action): void;
  subscribe(fn: () => void): () => void;
}

export function createStore(init: State = initialState): Store {
  let state = init;
  const subs = new Set<() => void>();
  return {
    get: () => state,
    dispatch(a) {
      const next = reducer(state, a);
      if (next !== state) {
        state = next;
        for (const f of subs) f();
      }
    },
    subscribe(fn) {
      subs.add(fn);
      return () => subs.delete(fn);
    },
  };
}
