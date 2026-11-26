// Slice C, end to end in the browser: the header picker lists the projects, "Proiect nou" creates one with a template
// and opens it, switching starts timeline, diff and frames from the chosen project, and a running run locks the picker.
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

const A = "prj_00000000000000aa";
const B = "prj_00000000000000bb";
const RUN = "run_00000000000000a1";

test("project picker: list, new project with template, switch resets the views", async ({ page }) => {
  be.projects = [
    { id: A, name: "S8 live check" },
    { id: B, name: "Notite" },
  ];
  await injectDevSession(page);
  await page.goto("/");
  await waitOpen(page);

  const picker = page.getByTestId("project-picker");
  await expect(picker).toHaveValue(A); // the newest project is open
  await expect(picker.locator("option")).toHaveText([/S8 live check · neconstruit încă/, /Notite · neconstruit încă/]);
  await expect(page.getByTestId("frame-empty")).toHaveText("Nicio aplicație construită încă — scrie în chat ce vrei și apasă Start");

  // the open project's run: its diff shows; while it runs the picker is locked
  be.push(
    { kind: "run.started", runId: RUN, projectId: A, payload: { title: "Live run" } },
    { kind: "file.diff", runId: RUN, projectId: A, payload: { path: "src/A.tsx", diff: "+a" } },
  );
  await expect(page.getByTestId("diff-path")).toHaveText("src/A.tsx");
  await expect(picker).toBeDisabled();
  await expect(page.getByTestId("project-delete")).toBeDisabled();
  be.push({ kind: "run.finished", runId: RUN, projectId: A, payload: { status: "ok" } });
  await expect(picker).toBeEnabled();

  // switching: the other project's (empty) state, not the previous run's diff
  await picker.selectOption(B);
  await expect(picker).toHaveValue(B);
  await expect(page.getByTestId("diff-path")).toHaveCount(0);
  await expect(page.getByTestId("run-status")).toContainText("idle");
  await expect(page.getByTestId("run-project")).toHaveValue(B); // the prompt box follows the open project

  // Proiect nou: name + template -> POST /projects, the new project is opened
  await page.getByTestId("project-new").click();
  await page.getByTestId("project-form-name").fill("Jurnal zilnic");
  await page.getByTestId("project-form-template").selectOption("tabs");
  await page.getByTestId("project-form-create").click();
  await expect.poll(() => be.projectCreates).toEqual([{ name: "Jurnal zilnic", template: "tabs" }]);
  const created = be.projects[0]!.id;
  await expect(picker).toHaveValue(created);
  await expect(page.getByTestId("project-form")).toHaveCount(0);
  await expect(page.getByTestId("run-project")).toHaveValue(created);
});
