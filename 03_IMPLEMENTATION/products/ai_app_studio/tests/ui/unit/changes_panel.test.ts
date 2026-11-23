// WOW W6: "Aprobă ecranul" stores baselines; a finished run re-checks; changed screens are listed with their %,
// shown side by side / overlay / diff; warnings and the acceptance checklist (with W3 pass/fail) are visible.
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { ChangesPanel } from "../../../apps/desktop/src/components/ChangesPanel";
import { checklistFromText, parseChecklist, parseState } from "../../../apps/desktop/src/lib/baselinesApi";

const PID = "prj_0123456789abcdef";
const A = "aa".repeat(32);
const B = "bb".repeat(32);
const C = "cc".repeat(32);
const D = "dd".repeat(32);

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const approved = parseState({ thresholdPct: 0.1, baselines: [{ route: "/", frame: "phone", sha256: A, revision: 3 }], lastCheck: null });
const checked = parseState({
  thresholdPct: 0.1,
  baselines: [{ route: "/", frame: "phone", sha256: A, revision: 3 }, { route: "/", frame: "desktop", sha256: D, revision: 3 }],
  lastCheck: {
    at: "2026-11-23T10:00:00+00:00",
    revision: 4,
    warnings: ["/ (desktop) changed 12.5% although it was not declared as intended: revert or fix it"],
    results: [
      { key: "/@phone", route: "/", frame: "phone", status: "same", changedPct: 0, baselineSha256: A, currentSha256: A },
      { key: "/@desktop", route: "/", frame: "desktop", status: "changed", changedPct: 12.5, baselineSha256: D, currentSha256: B, diffSha256: C },
      { key: "bad", route: "/", frame: "watch", status: "changed" },
    ],
  },
});

function api(over: Record<string, unknown> = {}) {
  return {
    status: vi.fn().mockResolvedValue(parseState({ baselines: [] })),
    approve: vi.fn().mockResolvedValue(approved),
    check: vi.fn().mockResolvedValue(checked),
    image: vi.fn().mockImplementation((_p: string, sha: string) => Promise.resolve(`blob:${sha.slice(0, 2)}`)),
    checklist: vi.fn().mockResolvedValue(parseChecklist({ flows: [{ id: "add-note", title: "Adaug notiță", statements: ["Apare în listă"], result: { status: "fail", message: "lipsă" } }] })),
    saveChecklist: vi.fn().mockResolvedValue([]),
    ...over,
  };
}

describe("ChangesPanel", () => {
  it("approves, re-checks after a finished run and shows what changed", async () => {
    const a = api();
    const { getByTestId, findByTestId, rerender, findAllByTestId } = render(createElement(ChangesPanel, { projectId: PID, api: a, runStatus: "idle" }));
    await findByTestId("changes-empty");
    fireEvent.click(getByTestId("baseline-approve"));
    await findByTestId("changes-approved");
    expect(a.approve).toHaveBeenCalledWith(PID);
    rerender(createElement(ChangesPanel, { projectId: PID, api: a, runStatus: "running" }));
    rerender(createElement(ChangesPanel, { projectId: PID, api: a, runStatus: "finished" }));
    const summary = await findByTestId("changes-summary");
    expect(a.check).toHaveBeenCalledTimes(1);
    expect(summary.textContent).toContain("1 ecran(e) schimbate");
    expect(getByTestId("changes-warnings").textContent).toContain("/ (desktop) changed 12.5%");
    const rows = await findAllByTestId("changes-row");
    expect(rows).toHaveLength(2); // the malformed row is dropped
    const diff = (await findByTestId("changes-diff-img")) as HTMLImageElement;
    expect(diff.src).toBe("blob:cc"); // the changed screen is selected, diff mode first
    fireEvent.click(getByTestId("changes-mode-side"));
    expect(((await findByTestId("changes-baseline-img")) as HTMLImageElement).src).toBe("blob:dd");
    expect(((await findByTestId("changes-current-img")) as HTMLImageElement).src).toBe("blob:bb");
    fireEvent.click(getByTestId("changes-mode-overlay"));
    await findByTestId("changes-opacity");
    expect((await findByTestId("flow-add-note")).getAttribute("data-status")).toBe("fail");
  });

  it("shows server errors as text", async () => {
    const a = api({ approve: vi.fn().mockRejectedValue(new Error("less than 5 GiB free")) });
    const { getByTestId, findByTestId } = render(createElement(ChangesPanel, { projectId: PID, api: a }));
    await findByTestId("changes-empty");
    fireEvent.click(getByTestId("baseline-approve"));
    expect((await findByTestId("changes-error")).textContent).toContain("5 GiB");
  });
});

describe("baselinesApi parsing", () => {
  it("turns the owner's text into flows with slug ids", () => {
    const flows = checklistFromText("# Adaug o notiță\n- Butonul + deschide formularul\n- Notița apare\n\n# Adaug o notiță\nfără liniuță");
    expect(flows.map((f) => f.id)).toEqual(["adaug-o-notita", "adaug-o-notita-2"]);
    expect(flows[0]!.statements).toEqual(["Butonul + deschide formularul", "Notița apare"]);
    expect(flows[1]!.statements).toEqual(["fără liniuță"]);
  });

  it("drops bad shas and sanitises text", () => {
    const st = parseState({ baselines: [{ route: "/‮evil", frame: "phone", sha256: "zz" }, { route: "/", frame: "phone", sha256: A }] });
    expect(st.baselines).toHaveLength(1);
    const cl = parseChecklist({ flows: [{ id: "a", title: "t​", statements: ["x"], result: { status: "hacked" } }] });
    expect(cl[0]!.status).toBe("unknown");
    expect(cl[0]!.title).toBe("tU+200B"); // invisible characters are made visible, never passed through
  });
});
