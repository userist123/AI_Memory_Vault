// Review #5: when the store screenshots could not be re-shot, the server leaves the stale ones out and says so; the
// owner sees the note in the approval preview and in the export result.
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { ExportControls } from "../../../apps/desktop/src/components/ExportControls";
import { exportProject } from "../../../apps/desktop/src/lib/api";

const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };
const PID = "prj_0123456789abcdef";
const SHA = "ab".repeat(32);
const APR = "apr_00112233445566ff";
const NOTE = "Capturi pentru magazin omise: nu s-au putut reface";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function reply(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

describe("store screenshots omitted note", () => {
  it("exportProject carries the note from the 428 details and from the result", async () => {
    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(reply(428, { error: "approval_required", details: { approvalId: APR, sha256: SHA, storeShotsNote: NOTE } }))
      .mockResolvedValueOnce(reply(200, { status: "exported", path: "D:\out\a.apk", sha256: SHA, storeScreenshotsNote: NOTE }));
    expect(await exportProject(SESSION, PID, "android")).toEqual({ kind: "approval", approvalId: APR, sha256: SHA, note: NOTE });
    expect(await exportProject(SESSION, PID, "android", APR)).toEqual({ kind: "done", path: "D:\out\a.apk", sha256: SHA, note: NOTE });
  });

  it("the approval preview and the result show the note", async () => {
    const run = vi.fn()
      .mockResolvedValueOnce({ kind: "approval", approvalId: APR, sha256: SHA, note: NOTE })
      .mockResolvedValueOnce({ kind: "done", path: "D:\out\a.apk", sha256: SHA, note: NOTE });
    const approve = vi.fn().mockResolvedValue(undefined);
    const { getByTestId, findByTestId } = render(createElement(ExportControls, { projectId: PID, run, approve }));
    fireEvent.click(getByTestId("export-android"));
    expect((await findByTestId("export-confirm")).textContent).toContain(NOTE);
    fireEvent.click(getByTestId("export-approve"));
    const res = (await findByTestId("export-result")).textContent ?? "";
    expect(res).toContain("D:\out\a.apk");
    expect(res).toContain(NOTE);
  });
});
