// Integration W2+W3: one QA results panel with the quality gates and the flow tests together.
import { afterEach, cleanup, createElement, describe, expect, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { QaResultsPanel } from "../../../apps/desktop/src/components/QaResultsPanel";
import type { QaResult } from "../../../apps/desktop/src/lib/qaApi";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const FLOWS = "flows rev 7: 1/2 passed\nx todo.spec.ts › todo › add: expected 1 got 0";
const FAILED: QaResult = {
  passed: false,
  counts: { critical: 0, serious: 1, moderate: 0, minor: 2, total: 3 },
  issues: [{ severity: "serious", rule: "color-contrast", frames: ["phone"], help: "<img src=x onerror=alert(1)>",
    targets: ["#a"], count: 1 }],
  site: "abc",
};

describe("QaResultsPanel", () => {
  it("shows the gates and the flow tests together", () => {
    const { getByTestId, getAllByTestId, container } = render(createElement(QaResultsPanel, {
      gates: FAILED, qa: { passed: 1, failed: 1, summary: FLOWS }, captures: [], load: null }));
    const panel = getByTestId("qa-results");
    expect(getByTestId("qa-results-gates-badge").textContent).toContain("failed");
    expect(getByTestId("qa-results-gates-badge").textContent).toContain("1 serious");
    expect(getAllByTestId("qa-results-issue")).toHaveLength(1);
    expect(panel.contains(getByTestId("flow-tests"))).toBe(true);
    expect(getByTestId("flow-badge").textContent).toContain("1/2 passed");
    expect(container.querySelector("img[onerror]")).toBeNull(); // issue text stays text
  });

  it("shows one side as not run when only the other has results", () => {
    const a = render(createElement(QaResultsPanel, { gates: { ...FAILED, passed: true, issues: [] }, qa: null,
      captures: [], load: null }));
    expect(a.getByTestId("qa-results-gates-badge").textContent).toContain("passed");
    expect(a.getByTestId("qa-results-flows-none")).toBeTruthy();
    cleanup();
    const b = render(createElement(QaResultsPanel, { gates: null, qa: { passed: 1, failed: 1, summary: FLOWS },
      captures: [], load: null }));
    expect(b.getByTestId("qa-results-gates-none")).toBeTruthy();
    expect(b.getByTestId("flow-tests")).toBeTruthy();
  });

  it("stays hidden without any QA result", () => {
    const { queryByTestId } = render(createElement(QaResultsPanel, {
      gates: null, qa: { passed: 1, failed: 0, summary: "axe: ok" }, captures: [], load: null }));
    expect(queryByTestId("qa-results")).toBeNull();
  });
});
