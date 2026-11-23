// S8 against the mock backend: the live emulator mirror subscribes per project over the authenticated binary WS and
// feeds the WebCodecs worker; preview captures appear as blob: thumbnails (timeline + next to the device frame).
import { createHash } from "node:crypto";
import { expect, MockBackend, test } from "../../../apps/desktop/testkit/pw";
import { MIRROR_FIXTURE_B64 } from "../../../apps/desktop/src/fixtures/mirrorFixture";
import { injectDevSession, waitOpen } from "./helpers";

const PID = "prj_abcdef0123456789";
// a valid 1x1 PNG
const PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "base64",
);
const SHA = createHash("sha256").update(PNG).digest("hex");

let be: MockBackend;
test.beforeEach(async () => {
  be = new MockBackend();
  await be.start();
});
test.afterEach(async () => {
  await be.stop();
});

test("live mirror: per-project binary WS feeds the decoder worker", async ({ page }) => {
  be.videoStream = Buffer.from(MIRROR_FIXTURE_B64, "base64");
  await injectDevSession(page);
  await page.goto("/");
  await waitOpen(page);
  expect(be.videoUpgrades).toEqual([]); // no project yet: no video socket
  be.push(
    { kind: "run.started", projectId: PID, payload: { title: "Mirror run" } },
    { kind: "device.state", projectId: PID, payload: { state: "running" } },
  );
  await expect.poll(() => be.videoUpgrades).toEqual([`?projectId=${PID}`]);
  await expect(page.getByTestId("mirror")).toHaveAttribute("data-source", "live");
  // where this Chromium decodes H.264 the worker must reach "decoding" on the live stream (frames drawn); elsewhere it
  // must say why it cannot
  const h264 = await page.evaluate(async () =>
    typeof VideoDecoder !== "undefined" &&
    Boolean((await VideoDecoder.isConfigSupported({ codec: "avc1.42c01e", optimizeForLatency: true })).supported),
  );
  const status = page.getByTestId("mirror-status");
  if (h264) {
    await expect(status).toHaveAttribute("data-state", "decoding", { timeout: 15_000 });
    await expect(status).toContainText(/live \(\d+ frames\)/, { timeout: 15_000 });
  } else {
    await expect(status).toHaveAttribute("data-state", "unsupported", { timeout: 15_000 });
  }
});

test("capture thumbnails: fetched by sha256 with the token, shown as blob: images with the viewport label", async ({ page }) => {
  be.captures.set(SHA, PNG);
  await injectDevSession(page);
  await page.goto("/");
  await waitOpen(page);
  be.push(
    { kind: "run.started", projectId: PID, payload: { title: "Capture run" } },
    { kind: "preview.captured", projectId: PID, payload: { revision: 3, frame: "phone", sha256: SHA } },
  );
  const strip = page.getByTestId("timeline-captures");
  await expect(strip).toContainText("Phone 390×844 · rev 3");
  const img = strip.locator("img");
  await expect(img).toHaveAttribute("src", /^blob:/);
  await expect.poll(() => img.evaluate((i: HTMLImageElement) => i.naturalWidth)).toBe(1);
  const side = page.getByTestId("frame-capture");
  await expect(side).toContainText("Phone 390×844 · rev 3");
  await expect(side.locator("img")).toHaveAttribute("src", /^blob:/);
  expect(be.captureGets).toContain(SHA);
  // a capture whose bytes do not hash to its id is never shown
  const bad = "b".repeat(64);
  be.captures.set(bad, PNG);
  be.push({ kind: "preview.captured", projectId: PID, payload: { revision: 4, frame: "phone", sha256: bad } });
  await expect(page.getByTestId("frame-capture")).toContainText("capture unavailable");
  await expect(page.getByTestId("frame-capture").locator("img")).toHaveCount(0);
});
