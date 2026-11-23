// S6 review item 5, end to end in the browser: prompt box -> POST /projects + POST /runs, the run's events make it
// "running", the plan card's Approve posts the owner decision, Stop posts /runs/<id>/stop.
import { expect, MockBackend, test } from "../../../apps/desktop/testkit/pw";
import { injectDevSession, waitOpen } from "./helpers";

let be: MockBackend;
test.beforeEach(async () => {
  be = new MockBackend();
  await be.start();
});
test.afterEach(async () => {
  await be.stop();
});

const RUN = "run_00000000000000a1";

test("prompt box starts a run, plan card approves it, Stop stops it", async ({ page }) => {
  await injectDevSession(page);
  await page.goto("/");
  await waitOpen(page);
  const start = page.getByTestId("run-start");
  await expect(start).toBeDisabled();
  await expect(page.getByTestId("run-stop")).toBeDisabled();
  await expect(page.getByTestId("run-project")).toHaveValue("__new__"); // no project yet: a new one is created

  await page.getByTestId("run-prompt").fill("Build a habit tracker");
  await start.click();
  await expect.poll(() => be.runStarts.length).toBe(1);
  expect(be.projectCreates).toEqual([{ name: "Build a habit tracker" }]);
  const created = be.projects[0]?.id;
  expect(be.runStarts[0]).toEqual({ projectId: created, prompt: "Build a habit tracker" });

  // the run's own events (from the server) switch the UI to running; the plan arrives as a server record
  be.approvals = [{ id: "apr_1", kind: "plan", title: "Habit tracker", steps: ["Scaffold", "Build"], runId: RUN, status: "pending" }];
  be.push(
    { kind: "run.started", runId: RUN, payload: { title: "Build a habit tracker" } },
    { kind: "plan.proposed", runId: RUN, payload: { planId: "apr_1" } },
  );
  await expect(page.getByTestId("run-status")).toContainText("running");
  await expect(start).toBeDisabled();
  const card = page.getByTestId("approval-region").getByTestId("plan-card");
  await expect(card).toHaveCount(1);
  await card.getByRole("button", { name: "Approve plan" }).click();
  await expect.poll(() => be.decisions.length).toBe(1);
  expect(be.decisions[0]).toEqual({ url: "/api/v1/plans/apr_1/decide", body: { decision: "approve", note: "" } });

  await page.getByTestId("run-stop").click();
  await expect.poll(() => be.runStops).toEqual([RUN]);
});
