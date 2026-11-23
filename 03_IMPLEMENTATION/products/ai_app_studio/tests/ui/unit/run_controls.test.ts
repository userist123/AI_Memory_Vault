// S6 review item 5: prompt box + Start run (owner action -> POST /api/v1/runs) and Stop (POST /runs/<id>/stop).
import { afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { RunControls } from "../../../apps/desktop/src/components/RunControls";
import { getRunnerProfile, setRunnerProfile, startRun, stopRun } from "../../../apps/desktop/src/lib/api";

const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function controls(over: Partial<Parameters<typeof RunControls>[0]> = {}) {
  const onStart = vi.fn();
  const onStop = vi.fn();
  const r = render(
    createElement(RunControls, {
      projects: [{ id: "prj_0123456789abcdef", name: "Habit tracker" }],
      running: false,
      busy: false,
      error: "",
      onStart,
      onStop,
      ...over,
    }),
  );
  return { ...r, onStart, onStop };
}

describe("RunControls", () => {
  it("Start is disabled until the owner typed a prompt, then sends the prompt and the chosen project", () => {
    const { getByTestId, onStart } = controls();
    const start = getByTestId("run-start") as HTMLButtonElement;
    expect(start.disabled).toBe(true);
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "   " } });
    expect(start.disabled).toBe(true);
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "Build a habit tracker" } });
    expect(start.disabled).toBe(false);
    fireEvent.click(start);
    expect(onStart).toHaveBeenCalledWith("prj_0123456789abcdef", "Build a habit tracker");
  });

  it("offers a new project when there is none", () => {
    const { getByTestId, onStart } = controls({ projects: [] });
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "x" } });
    fireEvent.click(getByTestId("run-start"));
    expect(onStart).toHaveBeenCalledWith("", "x");
  });

  it("the owner can pick New project even when projects exist", () => {
    const { getByTestId, onStart } = controls();
    fireEvent.change(getByTestId("run-project"), { target: { value: "__new__" } });
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "fresh" } });
    fireEvent.click(getByTestId("run-start"));
    expect(onStart).toHaveBeenCalledWith("", "fresh");
  });

  it("while a run is going Start is disabled and Stop calls onStop", () => {
    const { getByTestId, onStop } = controls({ running: true });
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "x" } });
    expect((getByTestId("run-start") as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(getByTestId("run-stop"));
    expect(onStop).toHaveBeenCalledTimes(1);
  });

  it("Stop is disabled when nothing runs", () => {
    const { getByTestId } = controls();
    expect((getByTestId("run-stop") as HTMLButtonElement).disabled).toBe(true);
  });

  it("the prompt box is length-bounded", () => {
    const { getByTestId } = controls();
    expect((getByTestId("run-prompt") as HTMLTextAreaElement).maxLength).toBe(20000);
  });
});

describe("api: runs", () => {
  it("startRun posts projectId + prompt only, with the bearer token", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({ runId: "run_0123456789abcdef", status: "running" }), { status: 202 }));
    const r = await startRun(SESSION, "prj_0123456789abcdef", "hello");
    expect(r.runId).toBe("run_0123456789abcdef");
    const [url, init] = f.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://127.0.0.1:47875/api/v1/runs");
    expect(init.method).toBe("POST");
    expect(JSON.parse(String(init.body))).toEqual({ projectId: "prj_0123456789abcdef", prompt: "hello" });
    expect((init.headers as Record<string, string>).Authorization).toBe(`Bearer ${SESSION.token}`);
  });

  it("startRun refuses a malformed project id and a malformed run id in the answer", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({ runId: "../x" }), { status: 202 }));
    await expect(startRun(SESSION, "../evil", "x")).rejects.toThrow();
    expect(f).not.toHaveBeenCalled();
    await expect(startRun(SESSION, "prj_0123456789abcdef", "x")).rejects.toThrow();
  });

  it("stopRun posts to /runs/<id>/stop and ignores malformed ids", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("{}", { status: 200 }));
    await stopRun(SESSION, "run_0123456789abcdef");
    expect((f.mock.calls[0] as [string])[0]).toBe("http://127.0.0.1:47875/api/v1/runs/run_0123456789abcdef/stop");
    await stopRun(SESSION, "run_../x");
    expect(f).toHaveBeenCalledTimes(1);
  });
});

describe("runner profile (owner decision 2026-11-23)", () => {
  it("is shown as full by default with a note of what it loads, and the owner can switch to isolated", () => {
    const onProfile = vi.fn();
    const { getByTestId } = controls({ profile: "full", onProfile });
    const box = getByTestId("runner-profile-toggle") as HTMLInputElement;
    expect(box.checked).toBe(true);
    expect(getByTestId("runner-profile-note").textContent).toMatch(/memory, hooks, plugins, skills/);
    fireEvent.click(box);
    expect(onProfile).toHaveBeenCalledWith("isolated");
  });

  it("isolated is unchecked, explains itself, and cannot change while a run is going", () => {
    const onProfile = vi.fn();
    const { getByTestId } = controls({ profile: "isolated", onProfile, running: true });
    const box = getByTestId("runner-profile-toggle") as HTMLInputElement;
    expect(box.checked).toBe(false);
    expect(box.disabled).toBe(true);
    expect(getByTestId("runner-profile-note").textContent).toMatch(/only the studio tools/);
  });

  it("the api sends the exact profile string and shows anything but an explicit isolated as full", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({ profile: "isolated" }), { status: 200 }));
    expect(await setRunnerProfile(SESSION, "isolated")).toBe("isolated");
    const [url, init] = f.mock.calls[0] as [string, RequestInit];
    expect(url).toBe("http://127.0.0.1:47875/api/v1/settings/runner-profile");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(String(init.body))).toEqual({ profile: "isolated" });
    f.mockResolvedValue(new Response(JSON.stringify({ profile: "weird" }), { status: 200 }));
    expect(await getRunnerProfile(SESSION)).toBe("full");
  });
});

describe("RunControls prompt draft per project", () => {
  it("starts from the draft the App kept for this project and reports every edit", () => {
    const onDraft = vi.fn();
    const { getByTestId, unmount } = controls({ draft: "Add a dark mode", onDraft });
    expect((getByTestId("run-prompt") as HTMLTextAreaElement).value).toBe("Add a dark mode");
    fireEvent.change(getByTestId("run-prompt"), { target: { value: "Add a dark mode toggle" } });
    expect(onDraft).toHaveBeenLastCalledWith("Add a dark mode toggle");
    unmount();
    // another project (the App remounts RunControls per project) gets its own text, not the previous one
    const other = controls({ draft: "", onDraft });
    expect((other.getByTestId("run-prompt") as HTMLTextAreaElement).value).toBe("");
  });
});
