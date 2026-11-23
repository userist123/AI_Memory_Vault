// Owner report 2026-11-23 "Nu merge sa o export APK": Export APK builds the APK when the server says build_required,
// shows the approval card with the artifact sha256, writes after approval, shows the path and "Deschide folderul".
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { ExportPanel } from "../../../apps/desktop/src/components/ExportPanel";
import { parseExport } from "../../../apps/desktop/src/lib/exportApi";

const PID = "prj_0123456789abcdef";
const SHA = "ab".repeat(32);
const APR = "apr_00112233445566ff";
const PATH = "C:\\Users\\o\\Downloads\\AIAppStudio\\Notes-0123456789abcdef\\Notes-rev3-abababababab.apk";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function api(over: Record<string, unknown> = {}) {
  return {
    exportArtifact: vi
      .fn()
      .mockResolvedValueOnce({ kind: "build_required", message: "no APK has been built" })
      .mockResolvedValueOnce({ kind: "approval", approvalId: APR, sha256: SHA, approved: false })
      .mockResolvedValueOnce({ kind: "done", path: PATH, sha256: SHA, bytes: 4198858 }),
    approve: vi.fn().mockResolvedValue(undefined),
    startBuild: vi.fn().mockResolvedValue({ state: "building", step: "android", message: "" }),
    buildStatus: vi
      .fn()
      .mockResolvedValueOnce({ state: "building", step: "android", message: "" })
      .mockResolvedValueOnce({ state: "ok", step: "", message: "" }),
    openFolder: vi.fn().mockResolvedValue(undefined),
    ...over,
  };
}

describe("ExportPanel - Export APK", () => {
  it("builds when required, asks approval of the sha256, writes, shows the path and opens the folder", async () => {
    const a = api();
    const { getByTestId, findByTestId } = render(createElement(ExportPanel, { projectId: PID, api: a, pollMs: 1 }));
    fireEvent.click(getByTestId("export-android"));
    const dlg = await findByTestId("export-confirm");
    expect(a.startBuild).toHaveBeenCalledWith(PID, "android");
    expect(a.buildStatus).toHaveBeenCalledTimes(2);
    expect(dlg.textContent).toContain(SHA);
    expect(dlg.textContent).toContain("debug APK");
    fireEvent.click(getByTestId("export-approve"));
    const res = await findByTestId("export-result");
    expect(a.approve).toHaveBeenCalledWith(APR);
    expect(a.exportArtifact).toHaveBeenLastCalledWith(PID, "android", APR);
    expect((getByTestId("export-path") as HTMLInputElement).value).toBe(PATH);
    expect(res.textContent).toContain("4.0 MB");
    expect(res.textContent).toContain("Notes-0123456789abcdef");
    fireEvent.click(getByTestId("export-open-folder"));
    expect(a.openFolder).toHaveBeenCalledWith(PID);
  });

  it("does not decide an approval twice when it was already approved in the Approvals list", async () => {
    const a = api({
      exportArtifact: vi
        .fn()
        .mockResolvedValueOnce({ kind: "approval", approvalId: APR, sha256: SHA, approved: true })
        .mockResolvedValueOnce({ kind: "done", path: PATH, sha256: SHA, bytes: 2048 }),
    });
    const { getByTestId, findByTestId } = render(createElement(ExportPanel, { projectId: PID, api: a, pollMs: 1 }));
    fireEvent.click(getByTestId("export-android"));
    await findByTestId("export-confirm");
    expect(a.startBuild).not.toHaveBeenCalled();
    fireEvent.click(getByTestId("export-approve"));
    await findByTestId("export-result");
    expect(a.approve).not.toHaveBeenCalled();
    expect(a.exportArtifact).toHaveBeenLastCalledWith(PID, "android", APR);
  });

  it("reports a failed build and never asks for approval", async () => {
    const a = api({
      buildStatus: vi.fn().mockResolvedValue({ state: "failed", step: "", message: "gradle exploded" }),
    });
    const { getByTestId, findByTestId, queryByTestId } = render(createElement(ExportPanel, { projectId: PID, api: a, pollMs: 1 }));
    fireEvent.click(getByTestId("export-android"));
    expect((await findByTestId("export-error")).textContent).toContain("gradle exploded");
    expect(queryByTestId("export-confirm")).toBeNull();
    expect(a.exportArtifact).toHaveBeenCalledTimes(1);
  });

  it("shows a copy hint when the folder cannot be opened", async () => {
    const a = api({
      exportArtifact: vi.fn().mockResolvedValueOnce({ kind: "done", path: PATH, sha256: SHA, bytes: 1 }),
      openFolder: vi.fn().mockRejectedValue(new Error("no")),
    });
    const { getByTestId, findByTestId, findByText } = render(createElement(ExportPanel, { projectId: PID, api: a, pollMs: 1 }));
    fireEvent.click(getByTestId("export-android"));
    await findByTestId("export-result");
    fireEvent.click(getByTestId("export-open-folder"));
    expect(await findByText(/copy the path/)).toBeTruthy();
  });
});

describe("parseExport", () => {
  it("maps 409 build_required, 428 with approved status, 200 with bytes", () => {
    expect(parseExport(409, { error: "build_required", message: "no APK" }).kind).toBe("build_required");
    expect(parseExport(428, { error: "approval_required", details: { approvalId: APR, sha256: SHA, status: "approved" } })).toEqual({
      kind: "approval",
      approvalId: APR,
      sha256: SHA,
      approved: true,
    });
    expect(parseExport(200, { path: PATH, sha256: SHA, bytes: 7 })).toEqual({ kind: "done", path: PATH, sha256: SHA, bytes: 7 });
    expect(parseExport(428, { details: { approvalId: "x", sha256: SHA } }).kind).toBe("error");
  });
});
