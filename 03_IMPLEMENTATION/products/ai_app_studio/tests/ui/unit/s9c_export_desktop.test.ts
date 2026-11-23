// S9c: the owner's "Export desktop (exe)" button runs the same two-step export with target "desktop"; the
// confirmation names the unsigned exe and its sha256.
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { ExportControls } from "../../../apps/desktop/src/components/ExportControls";

const PID = "prj_0123456789abcdef";
const SHA = "cd".repeat(32);
const APR = "apr_00112233445566ff";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("Export desktop", () => {
  it("asks with target desktop, confirms the unsigned exe sha256, then approves and repeats", async () => {
    const run = vi
      .fn()
      .mockResolvedValueOnce({ kind: "approval", approvalId: APR, sha256: SHA })
      .mockResolvedValueOnce({ kind: "done", path: "C:/x/app.exe" });
    const approve = vi.fn().mockResolvedValue(undefined);
    const { getByTestId, findByTestId } = render(createElement(ExportControls, { projectId: PID, run, approve }));
    fireEvent.click(getByTestId("export-desktop"));
    const dlg = await findByTestId("export-confirm");
    expect(dlg.textContent).toContain("unsigned Windows exe");
    expect(dlg.textContent).toContain(SHA.slice(0, 12));
    expect(run).toHaveBeenCalledWith(PID, "desktop");
    fireEvent.click(getByTestId("export-approve"));
    expect((await findByTestId("export-result")).textContent).toContain("app.exe");
    expect(approve).toHaveBeenCalledWith(APR);
    expect(run).toHaveBeenLastCalledWith(PID, "desktop", APR);
  });
});
