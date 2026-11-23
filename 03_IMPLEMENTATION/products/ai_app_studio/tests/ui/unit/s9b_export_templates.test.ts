// S9b: template choice for a new project (blank / tabs / list-detail) and the owner's Export button
// (two steps: 428 -> the owner confirms the exact artifact sha256 -> approve -> repeat with approvalId).
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { RunControls } from "../../../apps/desktop/src/components/RunControls";
import { ExportControls } from "../../../apps/desktop/src/components/ExportControls";
import { createProject, exportProject } from "../../../apps/desktop/src/lib/api";

const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };
const PID = "prj_0123456789abcdef";
const SHA = "ab".repeat(32);
const APR = "apr_00112233445566ff";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function reply(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("templates", () => {
  it("a new project offers the three templates and passes the choice", () => {
    const onStart = vi.fn();
    const { getByTestId } = render(
      createElement(RunControls, { projects: [], running: false, busy: false, error: "", onStart, onStop: vi.fn() }),
    );
    const sel = getByTestId("run-template") as HTMLSelectElement;
    expect([...sel.options].map((o) => o.value)).toEqual(["blank", "tabs", "list-detail"]);
    fireEvent.change(sel, { target: { value: "tabs" } });
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "notes app" } });
    fireEvent.click(getByTestId("run-start"));
    expect(onStart).toHaveBeenCalledWith("", "notes app", "tabs");
  });

  it("no template choice for an existing project", () => {
    const { queryByTestId } = render(
      createElement(RunControls, {
        projects: [{ id: PID, name: "x" }], running: false, busy: false, error: "", onStart: vi.fn(), onStop: vi.fn(),
      }),
    );
    expect(queryByTestId("run-template")).toBeNull();
  });

  it("createProject sends only a known template", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockImplementation(async () => reply(201, { id: PID }));
    await createProject(SESSION, "n", "list-detail");
    expect(JSON.parse(String(f.mock.calls[0]?.[1]?.body))).toEqual({ name: "n", template: "list-detail" });
    await createProject(SESSION, "n", "evil" as never);
    expect(JSON.parse(String(f.mock.calls[1]?.[1]?.body))).toEqual({ name: "n" }); // blank = the server default: not sent
  });
});

describe("exportProject (REST)", () => {
  it("428 -> approval needed with the artifact sha256; 200 -> path", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(reply(428, { error: "approval_required", details: { approvalId: APR, sha256: SHA, target: "web" } }))
      .mockResolvedValueOnce(reply(200, { status: "exported", path: "C:\\Users\\o\\Downloads\\AIAppStudio\\x\\a.zip", sha256: SHA }));
    expect(await exportProject(SESSION, PID, "web")).toEqual({ kind: "approval", approvalId: APR, sha256: SHA });
    expect(await exportProject(SESSION, PID, "web", APR)).toEqual({
      kind: "done", path: "C:\\Users\\o\\Downloads\\AIAppStudio\\x\\a.zip", sha256: SHA,
    });
  });

  it("export_blocked shows the patterns, never trusts malformed ids", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(reply(422, { error: "export_blocked", details: { findings: [{ pattern: "anthropic-key", file: "assets/a.js" }] } }))
      .mockResolvedValueOnce(reply(428, { error: "approval_required", details: { approvalId: "apr_x", sha256: SHA } }));
    expect(await exportProject(SESSION, PID, "web")).toEqual({ kind: "blocked", findings: ["anthropic-key in assets/a.js"] });
    expect((await exportProject(SESSION, PID, "web")).kind).toBe("error");
  });
});

describe("ExportControls", () => {
  it("asks the owner to confirm the exact sha256, approves, repeats with the approval id", async () => {
    const run = vi.fn()
      .mockResolvedValueOnce({ kind: "approval", approvalId: APR, sha256: SHA })
      .mockResolvedValueOnce({ kind: "done", path: "D:\\out\\a.zip", sha256: SHA });
    const approve = vi.fn().mockResolvedValue(undefined);
    const { getByTestId, findByTestId } = render(createElement(ExportControls, { projectId: PID, run, approve }));
    fireEvent.click(getByTestId("export-web"));
    const confirm = await findByTestId("export-confirm");
    expect(confirm.textContent).toContain(SHA.slice(0, 12));
    fireEvent.click(getByTestId("export-approve"));
    expect((await findByTestId("export-result")).textContent).toContain("D:\\out\\a.zip");
    expect(approve).toHaveBeenCalledWith(APR);
    expect(run.mock.calls).toEqual([[PID, "web"], [PID, "web", APR]]);
  });

  it("cancel does not approve", async () => {
    const run = vi.fn().mockResolvedValueOnce({ kind: "approval", approvalId: APR, sha256: SHA });
    const approve = vi.fn();
    const { getByTestId, findByTestId, queryByTestId } = render(createElement(ExportControls, { projectId: PID, run, approve }));
    fireEvent.click(getByTestId("export-android"));
    await findByTestId("export-confirm");
    fireEvent.click(getByTestId("export-cancel"));
    expect(queryByTestId("export-confirm")).toBeNull();
    expect(approve).not.toHaveBeenCalled();
    expect(run).toHaveBeenCalledWith(PID, "android");
  });
});
