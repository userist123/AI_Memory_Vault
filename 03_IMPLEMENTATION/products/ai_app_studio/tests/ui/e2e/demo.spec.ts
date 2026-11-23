// Demo mode replays the fixture run (no backend); writes the owner-facing screenshot docs/slices/S4-demo.png.
import { resolve } from "node:path";
import { expect, test } from "../../../apps/desktop/testkit/pw";

test("demo mode replays a full run", async ({ page }) => {
  const requests: string[] = [];
  page.on("request", (r) => requests.push(r.url()));
  await page.goto("/?demo=1&speed=2");
  await expect(page.getByTestId("connection")).toHaveText("demo");
  await expect(page.getByTestId("plan-card")).toBeVisible();

  // approval card appears from the demo "server record" while the run waits on deps.install
  const card = page.getByTestId("approval-card");
  await expect(card).toBeVisible({ timeout: 30_000 });
  await expect(card.getByTestId("approval-argv").locator("code")).toHaveText(["pnpm", "add", "--offline", "date-fns@4.1.0"]);
  await expect(page.getByTestId("now-doing")).toContainText("deps_install");
  await page.waitForTimeout(300);
  await page.screenshot({ path: resolve(__dirname, "../../../docs/slices/S4-demo.png") });

  await expect(page.getByTestId("tap-marker").first()).toBeAttached({ timeout: 30_000 });
  await expect(page.getByTestId("build-errors")).toContainText("src/App.tsx:9:8");
  await expect(page.getByTestId("run-status")).toContainText("finished", { timeout: 60_000 });
  await expect(page.getByTestId("step")).toHaveCount(4);
  await expect(page.getByTestId("qa")).toContainText("7 passed");

  // mirror: decoder worker either decodes the fixture or reports why it cannot
  await page.getByRole("button", { name: "Emulator" }).click();
  await expect(page.getByTestId("mirror-status")).toHaveAttribute("data-state", /decoding|unsupported/, { timeout: 15_000 });

  // demo never talks to the network beyond its own origin
  expect(requests.filter((u) => !u.startsWith("http://127.0.0.1:4174/"))).toEqual([]);
});
