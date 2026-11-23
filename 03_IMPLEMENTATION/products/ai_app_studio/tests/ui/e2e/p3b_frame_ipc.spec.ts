// THREAT_MODEL P3b (web build): the preview frame cannot reach the studio's IPC: window.__TAURI__ /
// __TAURI_INTERNALS__ absent in the frame, parent internals unreachable, fetch to ipc.localhost fails, forged
// postMessage changes nothing; P3c top navigation blocked; a credentialed fetch to the studio API never leaves
// the frame. DESIGN 4 sandbox "allow-scripts allow-same-origin" is acceptable only because the preview origin
// (p-<id>.localhost:<previewPort>) differs from the app origin, so "same origin" is the project's, not the studio's.
// [+] the main window's invoke('get_session') returns a session (stub of the Tauri main webview), the frame can
// fetch its own origin, a same-origin frame CAN read window.parent.document, and the same probe served without the
// preview CSP/sandbox DOES reach the API.
import { expect, MockBackend, PORTS, PROJECT, test } from "../../../apps/desktop/testkit/pw";
import { injectTauriStub, waitOpen } from "./helpers";

let be: MockBackend;
test.beforeEach(async () => {
  be = new MockBackend();
  await be.start();
});
test.afterEach(async () => {
  await be.stop();
});

test("preview frame is isolated from the studio IPC", async ({ page }) => {
  await injectTauriStub(page);
  await page.goto("/");
  await waitOpen(page); // positive control: session obtained through invoke('get_session')
  expect(await page.evaluate(() => typeof (window as unknown as { __TAURI_INTERNALS__?: unknown }).__TAURI_INTERNALS__)).toBe("object");
  const uiUrl = page.url();
  // P3c: any later navigation of the top window is recorded and fails the test with the target URL
  const topNavs: string[] = [];
  page.on("framenavigated", (f) => {
    if (f === page.mainFrame() && f.url() !== uiUrl) topNavs.push(f.url());
  });

  be.push({ kind: "run.started", payload: { title: "frames" } }, { kind: "preview.revision", payload: { revision: 1, frame: "phone" } });
  const iframe = page.getByTestId("frame-phone");
  await expect(iframe).toHaveAttribute("src", `http://p-${PROJECT}.localhost:${PORTS.preview}/?r=1`);
  await expect(iframe).toHaveAttribute("referrerpolicy", "no-referrer");
  // allow-same-origin is only tolerable because the frame origin is not the app origin
  expect(new URL(`http://p-${PROJECT}.localhost:${PORTS.preview}/`).origin).not.toBe(new URL(uiUrl).origin);

  const frame = await (async () => {
    for (let i = 0; i < 100; i++) {
      expect(topNavs, "top window navigated by the preview frame").toEqual([]);
      const f = page.frames().find((x) => x.url().startsWith(`http://p-${PROJECT}.localhost`));
      if (f) return f;
      await page.waitForTimeout(100);
    }
    throw new Error("preview frame did not load");
  })();
  await expect
    .poll(async () =>
      topNavs.length
        ? `top window navigated to ${topNavs[0]}`
        : frame.evaluate(() => (window as unknown as { __probe?: { done: boolean } }).__probe?.done ?? false).catch(() => false),
    )
    .toBe(true);
  const probe = await frame.evaluate(() => (window as unknown as { __probe: Record<string, unknown> }).__probe);

  expect(probe.tauriGlobal).toBe("undefined");
  expect(probe.tauriInternals).toBe("undefined");
  expect(String(probe.parentInternals)).toMatch(/^blocked:/);
  expect(String(probe.parentDocument)).toMatch(/^blocked:/);
  expect(String(probe.ipcFetch)).toMatch(/^failed:/);
  expect(String(probe.apiFetch)).toMatch(/^failed:/);
  expect(await frame.evaluate(() => {
    try {
      return String(!!window.parent.document.body);
    } catch (e) {
      return `blocked:${(e as Error).name}`;
    }
  })).toMatch(/^blocked:/);
  expect(probe.ownFetch).toBe("own-origin-ok"); // positive control inside the frame
  // forged postMessage did nothing; top navigation (P3c) did not happen
  await page.waitForTimeout(500);
  expect(topNavs, "top window navigated by the preview frame").toEqual([]);
  expect(page.url()).toBe(uiUrl);
  expect(be.canaryHits).toEqual([]);
  await expect(page.getByTestId("approval-card")).toHaveCount(0);
  expect(be.apiHits.filter((h) => h.includes("from=frame"))).toEqual([]);
  // exact sandbox (checked after the behaviour so a mutation shows the behavioural failure first)
  await expect(iframe).toHaveAttribute("sandbox", "allow-scripts allow-same-origin");

  // frames reload on every revision
  be.push({ kind: "preview.revision", payload: { revision: 2, frame: "phone" } });
  await expect(iframe).toHaveAttribute("src", `http://p-${PROJECT}.localhost:${PORTS.preview}/?r=2`);
});

test("positive controls: same-origin frame reads the parent; probe without CSP/sandbox reaches the API", async ({ page }) => {
  await injectTauriStub(page);
  await page.goto("/");
  await waitOpen(page);
  // a frame that shares the app origin (srcdoc inherits it) with the same sandbox tokens CAN read the parent
  await page.evaluate(() => {
    const f = document.createElement("iframe");
    f.setAttribute("sandbox", "allow-scripts allow-same-origin");
    f.setAttribute("data-testid", "same-origin-control");
    f.srcdoc = "<p>control</p>";
    document.body.appendChild(f);
  });
  await page.getByTestId("same-origin-control").waitFor({ state: "attached" });
  await expect.poll(() => page.frames().length).toBeGreaterThan(1);
  const control = page.frames().find((f) => f !== page.mainFrame() && f.url() === "about:srcdoc")!;
  expect(await control.evaluate(() => {
    try {
      return String(!!window.parent.document.body);
    } catch (e) {
      return `blocked:${(e as Error).name}`;
    }
  })).toBe("true");

  // the identical probe served WITHOUT the preview CSP and not sandboxed does send the credentialed request
  const p2 = await page.context().newPage();
  await p2.goto(`http://p-${PROJECT}.localhost:${PORTS.preview}/nocsp`);
  await expect.poll(() => p2.evaluate(() => (window as unknown as { __probe?: { done: boolean } }).__probe?.done ?? false)).toBe(true);
  expect(await p2.evaluate(() => (window as unknown as { __probe: Record<string, unknown> }).__probe.apiFetch)).toBe("sent");
  await expect.poll(() => be.apiHits.filter((h) => h.includes("from=frame")).length).toBeGreaterThan(0);
  await p2.close();
});
