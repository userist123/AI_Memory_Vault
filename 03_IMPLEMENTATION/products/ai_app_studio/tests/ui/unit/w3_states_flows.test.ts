// W3: preview state switcher (URL flag only, no postMessage) and the flow test results panel (qa.result suite flows).
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { StateSwitcher, withPreviewState } from "../../../apps/desktop/src/components/StateSwitcher";
import { FlowTestsPanel, parseFlowSummary } from "../../../apps/desktop/src/components/FlowTestsPanel";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

const SRC = "http://p-0123456789abcdef.localhost:17880/?r=3-1";
const SHA = "ab12cd34ef56" + "0".repeat(52);

describe("withPreviewState", () => {
  it("sets, replaces and clears the aas-state flag and keeps the origin", () => {
    const loading = withPreviewState(SRC, "loading")!;
    expect(new URL(loading).origin).toBe("http://p-0123456789abcdef.localhost:17880");
    expect(new URL(loading).searchParams.get("aas-state")).toBe("loading");
    expect(new URL(loading).searchParams.get("r")).toBe("3-1");
    const empty = withPreviewState(loading, "empty")!;
    expect(new URL(empty).searchParams.getAll("aas-state")).toEqual(["empty"]);
    expect(new URL(withPreviewState(empty, null)!).searchParams.has("aas-state")).toBe(false);
    expect(withPreviewState(null, "error")).toBeNull();
    expect(withPreviewState("javascript:alert(1)", "error")).toBe("javascript:alert(1)");
    expect(withPreviewState(SRC, "bogus" as never)).not.toContain("bogus");
  });
});

describe("StateSwitcher", () => {
  it("offers Live + the four states and reports the owner's choice", () => {
    const onChange = vi.fn();
    const { getByTestId } = render(createElement(StateSwitcher, { value: "empty", onChange }));
    expect(getByTestId("state-empty").getAttribute("aria-pressed")).toBe("true");
    expect(getByTestId("state-live").getAttribute("aria-pressed")).toBe("false");
    for (const s of ["populated", "loading", "error"]) {
      fireEvent.click(getByTestId(`state-${s}`));
      expect(onChange).toHaveBeenLastCalledWith(s);
    }
    fireEvent.click(getByTestId("state-live"));
    expect(onChange).toHaveBeenLastCalledWith(null);
  });
});

// the kit hook (kit/template/src/aas-states.ts) is exercised in a real web build + Chromium (W3 live run)
describe("FlowTestsPanel", () => {
  const summary = `flows rev 7: 2/3 passed\nx todo.spec.ts › todo › dark mode [shot ${SHA.slice(0, 12)}]: AssertionError: <img src=x onerror=alert(1)>`;

  it("parses only the flows summary shape", () => {
    expect(parseFlowSummary("a11y: 3 issues")).toBeNull();
    const s = parseFlowSummary(summary)!;
    expect(s).toMatchObject({ revision: 7, passed: 2, total: 3 });
    expect(s.failures).toEqual([{ label: "todo.spec.ts › todo › dark mode", shot: SHA.slice(0, 12),
      error: "AssertionError: <img src=x onerror=alert(1)>" }]);
  });

  it("renders the badge, the failure as text and the matching screenshot", () => {
    const load = vi.fn(async () => "blob:x");
    const { getByTestId, getAllByTestId, container } = render(createElement(FlowTestsPanel, {
      qa: { passed: 2, failed: 1, summary },
      captures: [{ seq: 4, sha256: SHA, frame: "phone", revision: 7 }],
      load,
    }));
    expect(getByTestId("flow-badge").textContent).toContain("2/3 passed");
    expect(getAllByTestId("flow-failure")).toHaveLength(1);
    expect(container.querySelector("img[onerror]")).toBeNull(); // page text is shown as text, never as markup
    expect(getByTestId("flow-shot").getAttribute("data-sha256")).toBe(SHA);
    expect(load).toHaveBeenCalledWith(SHA);
  });

  it("stays hidden for other QA suites", () => {
    const { queryByTestId } = render(createElement(FlowTestsPanel, {
      qa: { passed: 1, failed: 0, summary: "axe: ok" }, captures: [], load: null }));
    expect(queryByTestId("flow-tests")).toBeNull();
  });
});
