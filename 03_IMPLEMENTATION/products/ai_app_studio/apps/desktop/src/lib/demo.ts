/**
 * Demo mode (`?demo=1`): replays a recorded-style fixture run through the same parser + reducer as the live
 * stream, so the owner can watch what a run looks like without a backend. Approval cards still come from
 * "server records" (a local stand-in for GET /api/v1/approvals), never from the events.
 */
import type { ApprovalRecord, PlanRecord } from "./api";
import { parseObject } from "./events";
import type { Store } from "./store";

export const DEMO_PROJECT = "a1b2c3d4e5f60718";
const RUN = "run-demo-1";

type Raw = { kind: string; payload?: Record<string, unknown>; delay: number };

const APP_V1 = `import { useState } from "react";

export function App() {
  const [habits, setHabits] = useState<string[]>([]);
  return (
    <main className="app">
      <h1>Habits</h1>
      <HabitList items={habits} />
    </main>
  );
}`;

const DIFF_V1 = `--- a/src/App.tsx
+++ b/src/App.tsx
@@ -1,5 +1,11 @@
-export function App() {
-  return <main>Hello</main>;
+import { useState } from "react";
+
+export function App() {
+  const [habits, setHabits] = useState<string[]>([]);
+  return (
+    <main className="app">
+      <h1>Habits</h1>
+      <HabitList items={habits} />
+    </main>
+  );
 }`;

const DIFF_V2 = `--- a/src/HabitList.tsx
+++ b/src/HabitList.tsx
@@ -0,0 +1,14 @@
+export function HabitList({ items }: { items: string[] }) {
+  if (items.length === 0) {
+    return <p className="empty">No habits yet. Add your first one.</p>;
+  }
+  return (
+    <ul className="habits">
+      {items.map((h) => (
+        <li key={h}>
+          <input type="checkbox" aria-label={h} /> {h}
+        </li>
+      ))}
+    </ul>
+  );
+}`;

const SCRIPT: Raw[] = [
  { kind: "run.started", payload: { title: "Habit tracker — phone first", budgetUsd: 2 }, delay: 200 },
  {
    kind: "plan.proposed",
    payload: {
      planId: "plan-1",
      title: "Build a habit tracker with a daily checklist",
      steps: [
        "Scaffold App.tsx with habit state",
        "Add HabitList component",
        "Install date-fns for streaks (needs approval)",
        "Build, preview on phone/tablet/desktop, run QA taps",
      ],
    },
    delay: 900,
  },
  { kind: "plan.decided", payload: { planId: "plan-1", decision: "approved" }, delay: 1600 },
  { kind: "cost.update", payload: { inputTokens: 4210, outputTokens: 380, costUsd: 0.03 }, delay: 300 },
  { kind: "step.started", payload: { stepId: "st1", title: "Scaffold App.tsx" }, delay: 400 },
  { kind: "tool.call", payload: { callId: "c1", tool: "code_write_file", target: "src/App.tsx" }, delay: 400 },
  ...APP_V1.split("\n").map(
    (line): Raw => ({ kind: "agent.typing", payload: { path: "src/App.tsx", text: line + "\n" }, delay: 110 }),
  ),
  { kind: "file.diff", payload: { path: "src/App.tsx", diff: DIFF_V1, revision: 1 }, delay: 300 },
  { kind: "tool.result", payload: { callId: "c1", ok: true, size: 1184, sha256: "9f2c4e61b0a7d38e5c1f6a2b9d07e4c3a8b15f6e2d9c0a7b4e3f1d8c6b5a2e90" }, delay: 150 },
  { kind: "preview.revision", payload: { revision: 1, frame: "phone" }, delay: 500 },
  { kind: "step.started", payload: { stepId: "st2", title: "Add HabitList component" }, delay: 600 },
  { kind: "tool.call", payload: { callId: "c2", tool: "code_write_file", target: "src/HabitList.tsx" }, delay: 400 },
  { kind: "file.diff", payload: { path: "src/HabitList.tsx", diff: DIFF_V2, revision: 2 }, delay: 900 },
  { kind: "tool.result", payload: { callId: "c2", ok: true }, delay: 150 },
  { kind: "cost.update", payload: { inputTokens: 9120, outputTokens: 1460, costUsd: 0.09 }, delay: 300 },
  { kind: "step.started", payload: { stepId: "st3", title: "Install date-fns" }, delay: 500 },
  { kind: "tool.call", payload: { callId: "c3", tool: "deps_install", target: "package.json" }, delay: 300 },
  { kind: "approval.requested", payload: { approvalId: "ap-1" }, delay: 300 },
  { kind: "approval.decided", payload: { approvalId: "ap-1", decision: "approve" }, delay: 3500 },
  { kind: "tool.result", payload: { callId: "c3", ok: true }, delay: 300 },
  { kind: "step.started", payload: { stepId: "st4", title: "Build and preview" }, delay: 400 },
  { kind: "build.started", payload: {}, delay: 300 },
  { kind: "build.log", payload: { line: "vite v8.3.8 building for production..." }, delay: 300 },
  { kind: "build.log", payload: { line: "transforming (42) src/HabitList.tsx" }, delay: 300 },
  {
    kind: "build.error",
    payload: { line: "src/App.tsx(9,8): error TS2304: Cannot find name 'HabitList'." },
    delay: 500,
  },
  { kind: "tool.call", payload: { callId: "c4", tool: "code_write_file", target: "src/App.tsx" }, delay: 600 },
  {
    kind: "file.diff",
    payload: {
      path: "src/App.tsx",
      diff: '--- a/src/App.tsx\n+++ b/src/App.tsx\n@@ -1,2 +1,3 @@\n import { useState } from "react";\n+import { HabitList } from "./HabitList";\n',
      revision: 3,
    },
    delay: 700,
  },
  { kind: "tool.result", payload: { callId: "c4", ok: true }, delay: 150 },
  { kind: "build.log", payload: { line: "✓ 31 modules transformed." }, delay: 400 },
  { kind: "build.log", payload: { line: "dist/index.html  0.41 kB │ gzip: 0.28 kB" }, delay: 200 },
  { kind: "build.finished", payload: { status: "ok" }, delay: 300 },
  { kind: "preview.revision", payload: { revision: 3, frame: "phone" }, delay: 500 },
  { kind: "preview.interaction", payload: { frame: "phone", x: 195, y: 300 }, delay: 900 },
  { kind: "preview.interaction", payload: { frame: "phone", x: 120, y: 420 }, delay: 900 },
  { kind: "device.state", payload: { state: "booted" }, delay: 600 },
  { kind: "device.tap", payload: { x: 540, y: 980 }, delay: 900 },
  { kind: "device.tap", payload: { x: 300, y: 1400 }, delay: 900 },
  { kind: "preview.revision", payload: { revision: 3, frame: "tablet" }, delay: 800 },
  { kind: "preview.revision", payload: { revision: 3, frame: "desktop" }, delay: 800 },
  { kind: "qa.result", payload: { passed: 7, failed: 0, summary: "checklist toggles on all three frames" }, delay: 600 },
  { kind: "cost.update", payload: { inputTokens: 15840, outputTokens: 2930, costUsd: 0.17 }, delay: 300 },
  {
    kind: "agent.text",
    payload: { text: "The habit list renders on phone, tablet and desktop. Next: streak counter." },
    delay: 400,
  },
  { kind: "run.finished", payload: { status: "ok" }, delay: 600 },
];

export const DEMO_APPROVAL: ApprovalRecord = {
  id: "ap-1",
  action: "deps.install",
  argv: ["pnpm", "add", "--offline", "date-fns@4.1.0"],
  target: "kit/web/package.json",
  reason: "Needed for streak dates. Comes from the curated dependency kit.",
  runId: RUN,
  unsafe: false,
};

export const DEMO_PLAN: PlanRecord = {
  id: "plan-1",
  title: "Build a habit tracker with a daily checklist",
  steps: [
    "Scaffold App.tsx with habit state",
    "Add HabitList component",
    "Install date-fns for streaks (needs approval)",
    "Build, preview on phone/tablet/desktop, run QA taps",
  ],
  runId: RUN,
  unsafe: false,
};

export interface DemoHandle {
  stop(): void;
}

export function runDemo(
  store: Store,
  onApprovals: (r: ApprovalRecord[]) => void,
  speed = 1,
  onPlans: (r: PlanRecord[]) => void = () => {},
): DemoHandle {
  let seq = store.get().lastSeq;
  let i = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let stopped = false;
  store.dispatch({ type: "connection", value: "demo" });
  const next = () => {
    if (stopped || i >= SCRIPT.length) return;
    const raw = SCRIPT[i++]!;
    timer = setTimeout(() => {
      const prev = seq;
      seq += 1;
      const r = parseObject({
        seq,
        prev,
        kind: raw.kind,
        runId: RUN,
        projectId: DEMO_PROJECT,
        ts: Date.now(),
        payload: raw.payload ?? {},
      });
      if (r.ok) {
        store.dispatch({ type: "event", event: r.event });
        // the demo "server": a record exists between requested and decided
        if (raw.kind === "plan.proposed") onPlans([DEMO_PLAN]);
        if (raw.kind === "plan.decided") onPlans([]);
        if (raw.kind === "approval.requested") onApprovals([DEMO_APPROVAL]);
        if (raw.kind === "approval.decided") onApprovals([]);
      }
      next();
    }, raw.delay / speed);
  };
  next();
  return {
    stop() {
      stopped = true;
      if (timer) clearTimeout(timer);
    },
  };
}
