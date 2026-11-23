// Live behaviour against the mock backend: timeline rows, auto-surfaced build errors, follow-the-AI focus and
// pause on owner input, tap markers, cost pill, gap detection with lossless resume (P6f client side).
import { expect, MockBackend, test, TOKEN } from "../../../apps/desktop/testkit/pw";
import { injectDevSession, waitOpen } from "./helpers";

let be: MockBackend;
test.beforeEach(async () => {
  be = new MockBackend();
  await be.start();
});
test.afterEach(async () => {
  await be.stop();
});

test("timeline, follow the AI, taps, cost and gap resync", async ({ page }) => {
  await injectDevSession(page);
  await page.goto("/");
  await waitOpen(page);

  be.push(
    { kind: "run.started", payload: { title: "Live run", budgetUsd: 1 } },
    { kind: "step.started", payload: { stepId: "s1", title: "Write files" } },
    { kind: "tool.call", payload: { callId: "c1", tool: "code_write_file", target: "src/A.tsx" } },
    { kind: "file.diff", payload: { path: "src/A.tsx", diff: "+a\n+b\n-c" } },
    { kind: "tool.result", payload: { callId: "c1", ok: true } },
    { kind: "cost.update", payload: { inputTokens: 1500, outputTokens: 200, costUsd: 0.05 } },
  );
  await expect(page.getByTestId("diff-path")).toHaveText("src/A.tsx");
  const row = page.getByTestId("tool-row").first();
  await expect(row).toContainText("code_write_file");
  await expect(row).toContainText("+2");
  await expect(row).toContainText("-1");
  await expect(page.getByTestId("cost-pill")).toHaveText("1.5k in · 200 out · $0.05 / $1.00");

  // build log collapsed, error auto-surfaced
  be.push(
    { kind: "build.started" },
    { kind: "build.log", payload: { line: "vite building" } },
    { kind: "build.log", payload: { line: "src/A.tsx(3,1): error TS1005: ';' expected." } },
  );
  await expect(page.getByTestId("build-errors")).toContainText("src/A.tsx:3:1");
  await expect(page.getByTestId("build-log").locator(".build-lines")).toHaveCount(0);

  // follow: device frame of the latest preview event; tap marker drawn there
  be.push({ kind: "preview.revision", payload: { revision: 4, frame: "tablet" } });
  await expect(page.getByRole("button", { name: "Tablet 834×1112" })).toHaveClass(/active/);
  be.push({ kind: "preview.interaction", payload: { frame: "tablet", x: 400, y: 500 } });
  await expect(page.getByTestId("tap-marker")).toHaveCount(1);

  // owner input pauses follow for 10 s
  await page.getByTestId("diff-viewer").click();
  await expect(page.getByTestId("follow-toggle")).toContainText("paused");
  be.push({ kind: "file.diff", payload: { path: "src/B.tsx", diff: "+x" } });
  await expect(page.getByRole("button", { name: "B.tsx" })).toBeVisible();
  await expect(page.getByTestId("diff-path")).toHaveText("src/A.tsx");

  // gap: one frame is stored but lost on the socket -> client notices prev mismatch, resumes with afterSeq
  const lostSeq = be.lastSeq() + 1;
  be.record({ kind: "step.started", payload: { title: "LOST-THEN-REPLAYED" } });
  be.push({ kind: "step.started", payload: { title: "AFTER-GAP" } });
  await expect(page.getByTestId("gap-indicator")).toContainText(`gap ${lostSeq}–${lostSeq}`);
  await expect(page.getByText("LOST-THEN-REPLAYED")).toBeVisible();
  await expect(page.getByText("AFTER-GAP")).toBeVisible();
  expect(be.afterSeqs.at(-1)).toBe(lostSeq - 1);
});

test("contract: stream.dropped warning, truncated tool.result sha256, diff fallback to the newest file", async ({ page }) => {
  await injectDevSession(page);
  await page.goto("/");
  await waitOpen(page);
  const sha = "0123456789abcdef".repeat(4);
  be.push(
    { kind: "run.started", payload: { title: "Contract run" } },
    { kind: "step.started", payload: { title: "Write" } },
    { kind: "tool.call", payload: { callId: "c1", tool: "code_write_file", target: "src/A.tsx" } },
    { kind: "file.diff", payload: { path: "src/A.tsx", diff: "+a" } },
    { kind: "tool.result", payload: { callId: "c1", ok: true, sha256: sha, size: 12 } },
  );
  const shaPill = page.getByTestId("tool-sha256");
  await expect(shaPill).toHaveText("sha256 0123456789ab…");
  await expect(shaPill).toHaveAttribute("title", `sha256 ${sha}`);
  await expect(page.getByTestId("stream-dropped")).toHaveCount(0);

  be.push({ kind: "stream.dropped", payload: { count: 3 } }, { kind: "stream.dropped", payload: { count: 4 } });
  await expect(page.getByTestId("timeline").getByTestId("stream-dropped")).toContainText("7 agent stream item(s) dropped");
  await expect(page.getByTestId("gap-indicator")).toHaveCount(0); // allow-list drops are not transport gaps

  // follow focuses package.json (no diff yet): the viewer falls back to the newest written file
  be.push({ kind: "tool.call", payload: { callId: "c2", tool: "deps_install", target: "package.json" } });
  await expect(page.getByTestId("now-doing")).toContainText("package.json");
  await expect(page.getByTestId("diff-path")).toHaveText("src/A.tsx");
  // positive control: once package.json has a diff it is shown
  be.push({ kind: "file.diff", payload: { path: "package.json", diff: '+"left-pad": "1.3.0"' } });
  await expect(page.getByTestId("diff-path")).toHaveText("package.json");
});

test("contract: a server that does not echo aas.v1 is refused; the correct echo connects", async ({ page }) => {
  be.wsEcho = "token";
  await injectDevSession(page);
  await page.goto("/");
  const pill = page.getByTestId("protocol-error");
  await expect(pill).toContainText("aas.token.<redacted>");
  await expect(pill).not.toContainText(TOKEN);
  await expect(page.getByTestId("connection")).not.toHaveText("open");
  await expect(page.getByTestId("timeline")).not.toContainText("sha256"); // a refused socket delivers nothing
  be.wsEcho = "aas.v1";
  await waitOpen(page);
  await expect(pill).toHaveCount(0);
  expect(be.afterSeqs.length).toBeGreaterThan(0);
});
