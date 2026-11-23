// Slice C: the owner's project picker in the header (switch, "Proiect nou" with a template, owner-only delete through
// the existing two-step approval), and the api helpers it uses.
import { act, afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { ProjectPicker, buildTimeText } from "../../../apps/desktop/src/components/ProjectPicker";
import { DeviceFrame } from "../../../apps/desktop/src/components/DeviceFrames";
import { deleteProject, listProjects } from "../../../apps/desktop/src/lib/api";

const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };
const A = "prj_00000000000000aa";
const B = "prj_00000000000000bb";
const PROJECTS = [
  { id: A, name: "S8 live check", lastBuildAt: "2026-11-20T09:30:00+00:00" },
  { id: B, name: "Notite", lastBuildAt: null },
];

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function picker(over: Partial<Parameters<typeof ProjectPicker>[0]> = {}) {
  const props = {
    projects: PROJECTS,
    selected: A,
    disabled: false,
    onSelect: vi.fn(),
    onCreate: vi.fn().mockResolvedValue(undefined),
    requestDelete: vi.fn(),
    approve: vi.fn().mockResolvedValue(undefined),
    deny: vi.fn().mockResolvedValue(undefined),
    onDeleted: vi.fn(),
    ...over,
  };
  return { ...render(createElement(ProjectPicker, props)), props };
}

const flush = () => act(async () => {
  await new Promise((r) => setTimeout(r, 0));
});

describe("ProjectPicker", () => {
  it("lists every project with its last build time, Romanian labels, and the selected one chosen", () => {
    const { getByTestId } = picker();
    const sel = getByTestId("project-picker") as HTMLSelectElement;
    expect(sel.value).toBe(A);
    const opts = Array.from(sel.options).map((o) => o.textContent ?? "");
    expect(opts[0]).toContain("S8 live check");
    expect(opts[0]).toContain(`construit ${buildTimeText("2026-11-20T09:30:00+00:00")}`);
    expect(opts[1]).toContain("Notite");
    expect(opts[1]).toContain("neconstruit încă");
    expect(getByTestId("project-new").textContent).toBe("Proiect nou");
    expect(getByTestId("project-delete").textContent).toBe("Șterge");
  });

  it("switching calls onSelect with the chosen id", () => {
    const { getByTestId, props } = picker();
    fireEvent.change(getByTestId("project-picker"), { target: { value: B } });
    expect(props.onSelect).toHaveBeenCalledWith(B);
  });

  it("switching and deleting are disabled while a run is going", () => {
    const { getByTestId } = picker({ disabled: true });
    expect((getByTestId("project-picker") as HTMLSelectElement).disabled).toBe(true);
    expect((getByTestId("project-delete") as HTMLButtonElement).disabled).toBe(true);
  });

  it("Proiect nou opens a small form: name + template (blank / tabs / list-detail), then creates", async () => {
    const { getByTestId, queryByTestId, props } = picker();
    expect(queryByTestId("project-form")).toBeNull();
    fireEvent.click(getByTestId("project-new"));
    const tpl = getByTestId("project-form-template") as HTMLSelectElement;
    expect(Array.from(tpl.options).map((o) => o.value)).toEqual(["blank", "tabs", "list-detail"]);
    const create = getByTestId("project-form-create") as HTMLButtonElement;
    expect(create.disabled).toBe(true); // no name yet
    fireEvent.change(getByTestId("project-form-name"), { target: { value: "!!" } });
    expect(create.disabled).toBe(true); // the backend's name rule
    fireEvent.change(getByTestId("project-form-name"), { target: { value: "Jurnal zilnic" } });
    fireEvent.change(tpl, { target: { value: "tabs" } });
    fireEvent.click(create);
    expect(props.onCreate).toHaveBeenCalledWith("Jurnal zilnic", "tabs");
    await flush();
    expect(queryByTestId("project-form")).toBeNull();
  });

  it("a failed create keeps the form and shows the error", async () => {
    const { getByTestId } = picker({ onCreate: vi.fn().mockRejectedValue(new Error("a project with this name already exists")) });
    fireEvent.click(getByTestId("project-new"));
    fireEvent.change(getByTestId("project-form-name"), { target: { value: "Notite" } });
    fireEvent.click(getByTestId("project-form-create"));
    await flush();
    expect(getByTestId("project-form")).toBeTruthy();
    expect(getByTestId("project-error").textContent).toContain("already exists");
  });

  it("delete: the server asks for approval, the owner confirms, approve + repeat with the id", async () => {
    const requestDelete = vi
      .fn()
      .mockResolvedValueOnce({ kind: "approval", approvalId: "apr_0123456789abcdef" })
      .mockResolvedValueOnce({ kind: "done" });
    const { getByTestId, queryByTestId, props } = picker({ requestDelete });
    fireEvent.click(getByTestId("project-delete"));
    await flush();
    expect(requestDelete).toHaveBeenNthCalledWith(1, A);
    const dlg = getByTestId("project-delete-confirm");
    expect(dlg.textContent).toContain("S8 live check");
    expect(props.approve).not.toHaveBeenCalled(); // nothing deleted before the owner confirms
    fireEvent.click(getByTestId("project-delete-approve"));
    await flush();
    expect(props.approve).toHaveBeenCalledWith("apr_0123456789abcdef");
    expect(requestDelete).toHaveBeenNthCalledWith(2, A, "apr_0123456789abcdef");
    expect(props.onDeleted).toHaveBeenCalledWith(A);
    expect(queryByTestId("project-delete-confirm")).toBeNull();
  });

  it("delete cancelled: the pending approval is denied and nothing is deleted", async () => {
    const requestDelete = vi.fn().mockResolvedValue({ kind: "approval", approvalId: "apr_0123456789abcdef" });
    const { getByTestId, props } = picker({ requestDelete });
    fireEvent.click(getByTestId("project-delete"));
    await flush();
    fireEvent.click(getByTestId("project-delete-cancel"));
    await flush();
    expect(props.deny).toHaveBeenCalledWith("apr_0123456789abcdef");
    expect(props.approve).not.toHaveBeenCalled();
    expect(requestDelete).toHaveBeenCalledTimes(1);
    expect(props.onDeleted).not.toHaveBeenCalled();
  });

  it("delete refused by the server shows the reason", async () => {
    const requestDelete = vi.fn().mockResolvedValue({ kind: "error", message: "a run of this project is in progress" });
    const { getByTestId } = picker({ requestDelete });
    fireEvent.click(getByTestId("project-delete"));
    await flush();
    expect(getByTestId("project-error").textContent).toContain("in progress");
  });

  it("with no project at all: only Proiect nou, no delete", () => {
    const { getByTestId, queryByTestId } = picker({ projects: [], selected: "" });
    expect(queryByTestId("project-delete")).toBeNull();
    expect((getByTestId("project-picker") as HTMLSelectElement).disabled).toBe(true);
    expect(getByTestId("project-new")).toBeTruthy();
  });
});

describe("empty frame", () => {
  it("says what to do when the project has no build", () => {
    const { getByTestId } = render(createElement(DeviceFrame, { device: "phone", src: null, demoRevision: null, taps: [], now: 0 }));
    expect(getByTestId("frame-empty").textContent).toBe("Nicio aplicație construită încă — scrie în chat ce vrei și apasă Start");
  });
});

describe("api: projects", () => {
  it("listProjects keeps a valid lastBuildAt and drops a malformed one", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(
        JSON.stringify([
          { id: A, name: "One", lastBuildAt: "2026-11-20T09:30:00+00:00" },
          { id: B, name: "Two", lastBuildAt: "<script>" },
        ]),
        { status: 200 },
      ),
    );
    expect(await listProjects(SESSION)).toEqual([
      { id: A, name: "One", lastBuildAt: "2026-11-20T09:30:00+00:00" },
      { id: B, name: "Two", lastBuildAt: null },
    ]);
  });

  it("deleteProject: 428 -> approval id; repeat sends the id in the DELETE body; 200 -> done", async () => {
    const f = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response(JSON.stringify({ error: "approval_required", message: "m", details: { approvalId: "apr_0123456789abcdef" } }), { status: 428 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ deleted: A }), { status: 200 }));
    expect(await deleteProject(SESSION, A)).toEqual({ kind: "approval", approvalId: "apr_0123456789abcdef" });
    expect(await deleteProject(SESSION, A, "apr_0123456789abcdef")).toEqual({ kind: "done" });
    const [url, init] = f.mock.calls[1]!;
    expect(url).toBe(`${SESSION.apiBase}/api/v1/projects/${A}`);
    expect(init?.method).toBe("DELETE");
    expect(JSON.parse(String(init?.body))).toEqual({ approvalId: "apr_0123456789abcdef" });
    expect((init?.headers as Record<string, string>).Authorization).toBe(`Bearer ${SESSION.token}`);
  });

  it("deleteProject refuses a bad id without calling the server, and reports a server refusal", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify({ error: "run_in_progress", message: "a run is in progress" }), { status: 409 }));
    expect((await deleteProject(SESSION, "../x")).kind).toBe("error");
    expect(f).not.toHaveBeenCalled();
    expect(await deleteProject(SESSION, A)).toEqual({ kind: "error", message: "a run is in progress" });
  });
});
