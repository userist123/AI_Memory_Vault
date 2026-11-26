// Owner bug 2026-11-23 "the screen in the phone is black": the studio opened on the Emulator tab (a black canvas)
// because Follow-the-AI replayed the last run's device events; empty frames must say what to do, never stay black.
import { afterEach, cleanup, createElement, describe, expect, it, render } from "../../../apps/desktop/testkit/unit";
import { DeviceFrame } from "../../../apps/desktop/src/components/DeviceFrames";
import { EmulatorMirror } from "../../../apps/desktop/src/components/EmulatorMirror";
import { parseObject } from "../../../apps/desktop/src/lib/events";
import { initialState, reducer, type State } from "../../../apps/desktop/src/lib/store";

let seq = 0;
function ev(kind: string, payload: Record<string, unknown> = {}) {
  seq += 1;
  const r = parseObject({ seq, prev: seq - 1, kind, runId: "run-1", projectId: "abcdef0123456789", ts: Date.now(), payload });
  if (!r.ok) throw new Error(r.reason);
  return r.event;
}
const play = (...events: ReturnType<typeof ev>[]): State =>
  events.reduce((s, e) => reducer(s, { type: "event", event: e }), initialState);

afterEach(() => cleanup());

describe("finished run shows the built app, not the emulator", () => {
  it("a run that ended on device events focuses the phone preview of its project", () => {
    const s = play(
      ev("run.started", { title: "t" }),
      ev("preview.captured", { frame: "phone", revision: 2 }),
      ev("device.state", { state: "booted" }),
      ev("device.tap", { x: 10, y: 10 }),
      ev("run.finished", { status: "ok" }),
    );
    expect(s.focus?.panel).toBe("device");
    expect(s.focus?.device).toBe("phone");
  });
  it("positive control: while the run is live the mirror is followed", () => {
    const s = play(ev("run.started", { title: "t" }), ev("preview.captured", { frame: "phone" }), ev("device.state", {}));
    expect(s.focus?.panel).toBe("mirror");
  });
  it("a run without any preview keeps its last focus", () => {
    const s = play(ev("run.started", { title: "t" }), ev("device.state", {}), ev("run.finished", { status: "ok" }));
    expect(s.focus?.panel).not.toBe("device");
  });
});

describe("empty states are explicit", () => {
  it("phone frame without a build says how to start", () => {
    const { getByTestId } = render(createElement(DeviceFrame, { device: "phone", src: null, demoRevision: null, taps: [], now: 0 }));
    const el = getByTestId("frame-empty");
    expect(el.textContent).toContain("Nicio aplicație construită încă");
    expect(el.textContent).toContain("Start");
  });
  it("emulator mirror without live video shows a placeholder, not a bare black canvas", () => {
    const { getByTestId } = render(createElement(EmulatorMirror, { stream: null, live: null, taps: [], now: 0 }));
    const el = getByTestId("mirror-placeholder");
    expect(el.textContent).toContain("Emulatorul Android nu rulează");
    expect(el.textContent).toContain("Phone");
  });
});

describe("a run that did not succeed says so", () => {
  it("aborted (init_missing) is failed with a Romanian reason, not 'finished in 1s'", () => {
    const s = play(ev("run.started", { title: "t" }), ev("run.finished", { status: "aborted", reason: "init_missing" }));
    expect(s.runStatus).toBe("failed");
    expect(s.endReason).toContain("Claude");
  });
  it("budget and turn limits are failures with their own text", () => {
    expect(play(ev("run.started", {}), ev("run.finished", { status: "error_max_budget_usd" })).endReason).toContain("buget");
    expect(play(ev("run.started", {}), ev("run.finished", { status: "error_max_turns" })).runStatus).toBe("failed");
  });
  it("positive control: success is finished without a reason", () => {
    const s = play(ev("run.started", {}), ev("run.finished", { status: "success" }));
    expect([s.runStatus, s.endReason]).toEqual(["finished", ""]);
  });
});
