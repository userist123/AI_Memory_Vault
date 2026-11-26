// Live owner check fixes (wow2): B1 duplicate keys, B2 run-less events never move the selection.
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, cleanup, createElement, describe, expect, it, render } from "../../../apps/desktop/testkit/unit";
import { RunControls } from "../../../apps/desktop/src/components/RunControls";
import { ChangesPanel } from "../../../apps/desktop/src/components/ChangesPanel";
import { panelKey } from "../../../apps/desktop/src/lib/panelKey";
import { parseObject } from "../../../apps/desktop/src/lib/events";
import { initialState, reducer } from "../../../apps/desktop/src/lib/store";

afterEach(cleanup);
const PID = "abcdef0123456789";

describe("B1 panel keys", () => {
  it("panelKey never collides across kinds", () => {
    expect(panelKey("rc", PID)).not.toBe(panelKey("changes", PID));
    expect(panelKey("rc", "")).toBe("rc:none");
  });
  it("RunControls + ChangesPanel as siblings render exactly one RunControls", () => {
    const never_ = new Promise<never>(() => undefined);
    const api = { status: () => never_, approve: () => never_, check: () => never_, image: () => never_, checklist: async () => [], saveChecklist: async () => [] } as never;
    const noop = () => undefined;
    const { container, rerender } = render(
      createElement("div", null,
        createElement(RunControls, { key: panelKey("rc", PID), projects: [], running: false, busy: false, error: "", onStart: noop, onStop: noop }),
        createElement(ChangesPanel, { key: panelKey("changes", PID), projectId: PID, api }),
      ),
    );
    for (let i = 0; i < 5; i++) {
      rerender(createElement("div", null,
        createElement(RunControls, { key: panelKey("rc", PID), projects: [], running: false, busy: false, error: "", onStart: noop, onStop: noop }),
        createElement(ChangesPanel, { key: panelKey("changes", PID), projectId: PID, api }),
      ));
    }
    expect(container.querySelectorAll('[data-testid="run-controls"]')).toHaveLength(1);
  });
  it("App.tsx uses panelKey for both siblings (no raw project-id keys)", () => {
    const src = readFileSync(resolve(process.cwd(), "src/App.tsx"), "utf8");
    expect(src).toContain('panelKey("rc"');
    expect(src).toContain('panelKey("changes"');
    expect(src).not.toMatch(/<ChangesPanel key=\{selectedProject\}/);
  });
});

function ev(seq: number, kind: string, extra: Record<string, unknown> = {}) {
  const r = parseObject({ seq, prev: seq - 1, kind, ts: 1, payload: {}, ...extra });
  if (!r.ok) throw new Error(r.reason);
  return r.event;
}

describe("B2 store follows projectId only from live-run events", () => {
  it("a run-less tool.call never sets projectId", () => {
    const s = reducer(initialState, { type: "event", event: ev(1, "tool.call", { projectId: PID, payload: { tool: "project.inspect" } }) });
    expect(s.projectId).toBe("");
  });
  it("positive control: an event with a runId sets projectId", () => {
    const s = reducer(initialState, { type: "event", event: ev(1, "run.started", { runId: "run-1", projectId: PID }) });
    expect(s.projectId).toBe(PID);
  });
  it("a run-less event does not override the project of the shown run", () => {
    let s = reducer(initialState, { type: "event", event: ev(1, "run.started", { runId: "run-1", projectId: PID }) });
    s = reducer(s, { type: "event", event: ev(2, "tool.call", { projectId: "fedcba9876543210", payload: { tool: "project.inspect" } }) });
    expect(s.projectId).toBe(PID);
  });
});

// ---------------------------------------------------------------- #4 / #5 switchers disabled with a hint; #6 component family
import { StateSwitcher, STATES_UNSUPPORTED_HINT } from "../../../apps/desktop/src/components/StateSwitcher";
import { ThemeSwitcher, THEME_UNSUPPORTED_HINT, DEFAULT_PREVIEW_THEME } from "../../../apps/desktop/src/components/ThemeSwitcher";
import { fetchCapabilities } from "../../../apps/desktop/src/lib/inspector";
import { vi } from "../../../apps/desktop/testkit/unit";

describe("switchers without support", () => {
  it("state switcher: disabled with the Romanian hint; enabled by default and when supported", () => {
    const onChange = vi.fn();
    const off = render(createElement(StateSwitcher, { value: null, onChange, supported: false }));
    expect(STATES_UNSUPPORTED_HINT).toBe("Aplicația nu are încă stări — cere în chat: «adaugă stările»");
    expect(off.getByTestId("state-hint").textContent).toBe(STATES_UNSUPPORTED_HINT);
    for (const s of ["live", "populated", "loading", "empty", "error"]) expect((off.getByTestId(`state-${s}`) as HTMLButtonElement).disabled).toBe(true);
    cleanup();
    const on = render(createElement(StateSwitcher, { value: null, onChange }));
    expect((on.getByTestId("state-loading") as HTMLButtonElement).disabled).toBe(false);
    expect(on.queryByTestId("state-hint")).toBeNull();
  });
  it("theme switcher: disabled with the Romanian hint", () => {
    const off = render(createElement(ThemeSwitcher, { value: DEFAULT_PREVIEW_THEME, onChange: vi.fn(), supported: false }));
    expect(THEME_UNSUPPORTED_HINT).toBe("Aplicația nu folosește încă sistemul de design — cere în chat: «treci pe sistemul de design»");
    expect(off.getByTestId("theme-hint").textContent).toBe(THEME_UNSUPPORTED_HINT);
    expect((off.getByTestId("theme-dark") as HTMLButtonElement).disabled).toBe(true);
    expect((off.getByTestId("theme-preset") as HTMLSelectElement).disabled).toBe(true);
    cleanup();
    const on = render(createElement(ThemeSwitcher, { value: DEFAULT_PREVIEW_THEME, onChange: vi.fn() }));
    expect((on.getByTestId("theme-dark") as HTMLButtonElement).disabled).toBe(false);
  });
  it("capabilities parse strictly (only literal true counts)", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ states: true, theme: "yes" }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const caps = await fetchCapabilities({ apiBase: "http://127.0.0.1:1", token: "t".repeat(64) } as never, PID_FULL);
    expect(caps).toEqual({ states: true, theme: false });
    vi.unstubAllGlobals();
  });
});
const PID_FULL = "prj_0123456789abcdef";
