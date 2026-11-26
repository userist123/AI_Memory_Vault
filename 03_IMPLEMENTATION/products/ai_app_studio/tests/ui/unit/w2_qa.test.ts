// W2: QA badge (counts by severity, auto-run after a preview change without looping on its own captures) and the
// Auto-polish button (start -> poll -> before/after counts). Page-derived issue text is sanitised and shown as text.
import { act, afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { QaBadge } from "../../../apps/desktop/src/components/QaBadge";
import { AutoPolishButton } from "../../../apps/desktop/src/components/AutoPolishButton";
import { countsLabel, parsePolish, parseResult } from "../../../apps/desktop/src/lib/qaApi";

const PID = "prj_0123456789abcdef";
const counts = (o: Partial<Record<string, number>> = {}) => ({ critical: 0, serious: 0, moderate: 0, minor: 0, total: 0, ...o });

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("qaApi parsing", () => {
  it("validates counts and sanitises issue text", () => {
    const r = parseResult({
      passed: false,
      counts: counts({ serious: 1, total: 1 }),
      issues: [{ severity: "serious", rule: "color-contrast", frames: ["phone"], help: "x‮y\u0007", targets: ["<img onerror=1>"], count: 2 }],
    });
    expect(r?.counts.serious).toBe(1);
    expect(r?.issues[0]?.help).not.toContain("‮");
    expect(parseResult({ passed: true, counts: { critical: "lots" } })).toBeNull();
    expect(parsePolish({ state: "evil" }).state).toBe("idle");
    expect(countsLabel(counts({ critical: 1, moderate: 2, total: 3 }))).toBe("1 critical · 2 moderate");
    expect(countsLabel(counts())).toBe("0 issues");
  });
});

describe("QaBadge", () => {
  it("shows the stored counts, auto-runs on a preview change and renders issues as text", async () => {
    const bad = parseResult({
      passed: false,
      counts: counts({ critical: 1, serious: 2, total: 3 }),
      issues: [{ severity: "critical", rule: "image-alt", frames: ["phone", "tablet"], help: "Images need alt", targets: ["<b>img</b>"], count: 1 }],
    });
    const api = { load: vi.fn().mockResolvedValue({ result: null, stale: true, polish: parsePolish({}) }), run: vi.fn().mockResolvedValue(bad) };
    const { getByTestId, findByText, rerender } = render(createElement(QaBadge, { projectId: PID, nonce: 0, api }));
    expect(api.run).not.toHaveBeenCalled();
    rerender(createElement(QaBadge, { projectId: PID, nonce: 1, api: { ...api } }));
    await findByText("QA 1 critical · 2 serious");
    expect(api.run).toHaveBeenCalledWith(PID, true);
    expect(api.load).toHaveBeenCalledTimes(1); // a new inline api object does not refetch
    fireEvent.click(getByTestId("qa-summary"));
    const li = getByTestId("qa-issue");
    expect(li.textContent).toContain("<b>img</b>"); // text, not markup
    expect(li.querySelector("b")).toBeNull();
    fireEvent.click(getByTestId("qa-run"));
    await findByText("QA 1 critical · 2 serious");
    expect(api.run).toHaveBeenLastCalledWith(PID, false);
  });

  it("coalesces preview changes during a run into one follow-up automatic request", async () => {
    let release: (v: unknown) => void = () => undefined;
    const ok = parseResult({ passed: true, counts: counts(), issues: [] });
    const run = vi.fn().mockImplementationOnce(() => new Promise((r) => (release = r))).mockResolvedValue(ok);
    const api = { load: vi.fn().mockResolvedValue({ result: null, stale: false, polish: parsePolish({}) }), run };
    const { rerender, findByText } = render(createElement(QaBadge, { projectId: PID, nonce: 1, api }));
    rerender(createElement(QaBadge, { projectId: PID, nonce: 2, api }));
    rerender(createElement(QaBadge, { projectId: PID, nonce: 3, api }));
    expect(run).toHaveBeenCalledTimes(1);
    await act(async () => release(ok));
    await findByText("QA ok");
    expect(run).toHaveBeenCalledTimes(2);
  });
});

describe("AutoPolishButton", () => {
  it("starts, polls while running and shows before/after counts", async () => {
    const running = parsePolish({ state: "running", loop: 1, maxLoops: 2, step: "the agent is fixing the issues", before: counts({ serious: 2, total: 2 }) });
    const done = parsePolish({ state: "done", loop: 1, maxLoops: 2, before: counts({ serious: 2, total: 2 }), after: counts(), message: "all gates pass" });
    const api = { start: vi.fn().mockResolvedValue(running), status: vi.fn().mockResolvedValueOnce(running).mockResolvedValue(done) };
    const { getByTestId, findByTestId } = render(createElement(AutoPolishButton, { projectId: PID, api, pollMs: 1 }));
    fireEvent.click(getByTestId("auto-polish-start"));
    await findByTestId("auto-polish-step");
    const res = await findByTestId("auto-polish-result");
    expect(res.textContent).toBe("Before: 2 serious → after: 0 issues (all gates pass)");
    expect(api.start).toHaveBeenCalledWith(PID);
    expect(api.status).toHaveBeenCalledTimes(2);
  });

  it("shows the server's refusal", async () => {
    const api = { start: vi.fn().mockRejectedValue(new Error("a run is in progress: wait for it to finish")), status: vi.fn() };
    const { getByTestId, findByTestId } = render(createElement(AutoPolishButton, { projectId: PID, api, pollMs: 1 }));
    fireEvent.click(getByTestId("auto-polish-start"));
    expect((await findByTestId("auto-polish-error")).textContent).toContain("run is in progress");
  });
});
