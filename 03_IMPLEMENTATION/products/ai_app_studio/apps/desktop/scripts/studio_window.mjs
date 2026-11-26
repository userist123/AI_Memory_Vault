// Opens the studio UI (vite dev / vite preview) in a Chromium app window with the owner session injected.
//
// The owner token is read from the FIRST LINE OF STDIN and lives only in this process and in the page's
// window.__AAS_DEV_SESSION__ (top window only, never preview frames). It is never put on a command line, in the
// environment, in a file or in a URL. Used by scripts/run_studio.ps1 (dev mode) and by the S6 end-to-end test.
//
//   node scripts/studio_window.mjs --url http://127.0.0.1:4174 --api http://127.0.0.1:17875 --preview-port 17880
//        [--shot out.png] [--wait-selector "[data-testid=tool-row]"] [--min 1] [--headless] [--width 1440 --height 900]
// With --shot the script saves a screenshot after the selector appears (and exits); without it the window stays open
// until it is closed.
import { chromium } from "@playwright/test";
import { createInterface } from "node:readline";

const args = new Map();
for (let i = 2; i < process.argv.length; i++) {
  const a = process.argv[i];
  if (!a.startsWith("--")) continue;
  const next = process.argv[i + 1];
  if (next === undefined || next.startsWith("--")) args.set(a, "1");
  else {
    args.set(a, next);
    i++;
  }
}

const url = args.get("--url");
const api = args.get("--api");
const previewPort = Number(args.get("--preview-port") ?? 17880);
if (!url || !api || !/^http:\/\/127\.0\.0\.1:\d{2,5}$/.test(api)) {
  console.error("usage: studio_window.mjs --url <ui url> --api http://127.0.0.1:<port> [--preview-port N] [--shot file]");
  process.exit(2);
}

const rl = createInterface({ input: process.stdin });
const token = await new Promise((resolve) => {
  rl.once("line", (l) => resolve(l.trim()));
  rl.once("close", () => resolve(""));
});
rl.close();
if (token.length < 32) {
  console.error("the owner token (>= 32 characters) must arrive on the first stdin line");
  process.exit(2);
}

const width = Number(args.get("--width") ?? 1440);
const height = Number(args.get("--height") ?? 900);
// headless uses Playwright's chrome-headless-shell; the headed window uses the full Chromium build
const browser = await chromium.launch({ headless: args.has("--headless"), args: [`--window-size=${width},${height}`] });
const context = await browser.newContext({ viewport: args.has("--headless") ? { width, height } : null });
await context.addInitScript(
  (s) => {
    if (window === window.top) window.__AAS_DEV_SESSION__ = s;
  },
  { apiBase: api, token, previewPort },
);
const page = await context.newPage();
await page.goto(url);

if (args.has("--shot")) {
  const sel = args.get("--wait-selector");
  if (sel) {
    const min = Number(args.get("--min") ?? 1);
    await page.waitForFunction(([s, n]) => document.querySelectorAll(s).length >= n, [sel, min], { timeout: 120_000 });
  }
  const wt = args.get("--wait-text"); // "<css selector>=<substring>"
  if (wt && wt.includes("=")) {
    const i = wt.lastIndexOf("=");
    await page.waitForFunction(([s, t]) => (document.querySelector(s)?.textContent ?? "").toLowerCase().includes(t),
      [wt.slice(0, i), wt.slice(i + 1).toLowerCase()], { timeout: 120_000 });
  }
  await page.waitForTimeout(Number(args.get("--settle-ms") ?? 1500));
  await page.screenshot({ path: args.get("--shot"), fullPage: false });
  await browser.close();
} else {
  console.log("studio window open; close it to stop");
  await new Promise((resolve) => browser.on("disconnected", resolve));
}
