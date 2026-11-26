// W1: kit component catalogue (kit/template/src/ui), theme bootstrap, the "Proiect nou" style preset picker and the
// studio preview theme switcher (light / dark / split, preset override through two whitelisted query values).
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { act, afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import {
  Button, Card, Dialog, EmptyState, Input, List, ListItem, Sheet, Skeleton, TabBar, Toast, resolveTheme,
} from "../../../kit/template/src/ui";
import { applyTheme } from "../../../kit/template/src/ui/theme";
import { PRESET_IDS, PRESET_INFO, PresetPicker } from "../../../apps/desktop/src/components/PresetPicker";
import { ThemeSwitcher, ThemedPreview, themedSrc, DEFAULT_PREVIEW_THEME } from "../../../apps/desktop/src/components/ThemeSwitcher";
import { ProjectPicker } from "../../../apps/desktop/src/components/ProjectPicker";
import { createProject } from "../../../apps/desktop/src/lib/api";

const h = createElement;
const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };
const PID = "prj_0123456789abcdef";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe("kit catalogue", () => {
  it("Button: type=button by default, variants, loading blocks clicks and sets aria-busy", () => {
    const onClick = vi.fn();
    const { getByRole, rerender } = render(h(Button, { onClick, variant: "danger" }, "Delete"));
    const b = getByRole("button", { name: "Delete" }) as HTMLButtonElement;
    expect(b.type).toBe("button");
    expect(b.className).toContain("ui-btn--danger");
    fireEvent.click(b);
    expect(onClick).toHaveBeenCalledTimes(1);
    rerender(h(Button, { onClick, loading: true }, "Delete"));
    expect(b.disabled).toBe(true);
    expect(b.getAttribute("aria-busy")).toBe("true");
    fireEvent.click(b);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("Input: label is wired, error marks aria-invalid and is described and announced", () => {
    const { getByLabelText, getByRole } = render(h(Input, { label: "Name", hint: "Your first name", error: "Required" }));
    const input = getByLabelText("Name") as HTMLInputElement;
    expect(input.getAttribute("aria-invalid")).toBe("true");
    const ids = (input.getAttribute("aria-describedby") ?? "").split(" ");
    expect(ids.length).toBe(2);
    expect(ids.map((i) => document.getElementById(i)?.textContent)).toEqual(["Your first name", "Required"]);
    expect(getByRole("alert").textContent).toBe("Required");
  });

  it("Card renders title, body and actions", () => {
    const { getByRole, getByText } = render(h(Card, { title: "Today", subtitle: "3 things", actions: h(Button, null, "Go") }, "Body"));
    expect(getByRole("heading", { name: "Today" })).toBeTruthy();
    expect(getByText("Body")).toBeTruthy();
    expect(getByRole("button", { name: "Go" })).toBeTruthy();
  });

  it("ListItem: a button row when clickable, aria-current for the selected row, plain row otherwise", () => {
    const onClick = vi.fn();
    const { getAllByRole, getByRole } = render(
      h(List, { "aria-label": "Items" },
        h(ListItem, { title: "First", subtitle: "one", onClick, "aria-current": "true" }),
        h(ListItem, { title: "Version", trailing: "1.0" })),
    );
    expect(getByRole("list", { name: "Items" })).toBeTruthy();
    expect(getAllByRole("listitem").length).toBe(2);
    const row = getByRole("button");
    expect(row.getAttribute("aria-current")).toBe("true");
    fireEvent.click(row);
    expect(onClick).toHaveBeenCalled();
  });

  it("TabBar: WAI-ARIA tabs, roving tabIndex, arrow keys wrap and select", () => {
    const onChange = vi.fn();
    const tabs = [{ id: "a", label: "A" }, { id: "b", label: "B" }, { id: "c", label: "C" }];
    const { getAllByRole, getByRole } = render(h(TabBar, { tabs, active: "a", onChange }));
    expect(getByRole("tablist")).toBeTruthy();
    const t = getAllByRole("tab") as [HTMLElement, HTMLElement, HTMLElement];
    expect(t.map((x) => x.getAttribute("aria-selected"))).toEqual(["true", "false", "false"]);
    expect(t.map((x) => x.tabIndex)).toEqual([0, -1, -1]);
    expect(t[0].getAttribute("aria-controls")).toBe("panel-a");
    fireEvent.keyDown(t[0], { key: "ArrowLeft" });
    expect(onChange).toHaveBeenLastCalledWith("c");
    fireEvent.keyDown(t[0], { key: "ArrowRight" });
    expect(onChange).toHaveBeenLastCalledWith("b");
    fireEvent.keyDown(t[0], { key: "End" });
    expect(onChange).toHaveBeenLastCalledWith("c");
  });

  it("Dialog: modal, labelled, focus moves in, Escape and backdrop close, focus returns to the opener", () => {
    const onClose = vi.fn();
    const opener = document.createElement("button");
    document.body.appendChild(opener);
    opener.focus();
    const { getByRole, rerender } = render(h(Dialog, { open: true, title: "Delete?", description: "Cannot be undone.", onClose,
      actions: h(Button, null, "Cancel") }));
    const d = getByRole("dialog", { name: "Delete?" });
    expect(d.getAttribute("aria-modal")).toBe("true");
    expect(document.activeElement?.textContent).toBe("Cancel");
    fireEvent.keyDown(document, { key: "Escape" });
    expect(onClose).toHaveBeenCalledTimes(1);
    fireEvent.mouseDown(d.parentElement!);
    expect(onClose).toHaveBeenCalledTimes(2);
    rerender(h(Dialog, { open: false, title: "Delete?", onClose }));
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });

  it("Dialog: Tab stays inside", () => {
    render(h(Dialog, { open: true, title: "T", onClose: () => undefined, actions: [h(Button, { key: 1 }, "One"), h(Button, { key: 2 }, "Two")] }));
    const [one, two] = ["One", "Two"].map((n) => Array.from(document.querySelectorAll("button")).find((b) => b.textContent === n)!) as [HTMLButtonElement, HTMLButtonElement];
    two.focus();
    fireEvent.keyDown(document, { key: "Tab" });
    expect(document.activeElement).toBe(one);
    fireEvent.keyDown(document, { key: "Tab", shiftKey: true });
    expect(document.activeElement).toBe(two);
  });

  it("Sheet: a labelled modal with a close button; closed renders nothing", () => {
    const onClose = vi.fn();
    const { getByRole, queryByRole, rerender } = render(h(Sheet, { open: true, title: "Edit", onClose }, h(Input, { label: "Name" })));
    expect(getByRole("dialog", { name: "Edit" })).toBeTruthy();
    fireEvent.click(getByRole("button", { name: "Close" }));
    expect(onClose).toHaveBeenCalled();
    rerender(h(Sheet, { open: false, title: "Edit", onClose }));
    expect(queryByRole("dialog")).toBeNull();
  });

  it("Toast: live region always present, auto-dismisses, danger is assertive", () => {
    vi.useFakeTimers();
    const onDismiss = vi.fn();
    const { getByRole, rerender } = render(h(Toast, { open: false, message: "Saved", onDismiss }));
    expect(getByRole("status").getAttribute("aria-live")).toBe("polite");
    rerender(h(Toast, { open: true, message: "Saved", onDismiss, duration: 1000 }));
    expect(getByRole("status").textContent).toContain("Saved");
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(onDismiss).toHaveBeenCalledTimes(1);
    rerender(h(Toast, { open: true, message: "Failed", tone: "danger", onDismiss, duration: 0 }));
    expect(getByRole("alert").getAttribute("aria-live")).toBe("assertive");
  });

  it("Skeleton is decorative; EmptyState error is announced with its action", () => {
    const { container, getByRole } = render(
      h("div", null, h(Skeleton, { lines: 3 }), h(EmptyState, { title: "Could not load", tone: "error", action: h(Button, null, "Try again") })),
    );
    const sk = container.querySelector(".ui-skeleton-group")!;
    expect(sk.getAttribute("aria-hidden")).toBe("true");
    expect(sk.children.length).toBe(3);
    expect(getByRole("alert").textContent).toContain("Could not load");
    expect(getByRole("button", { name: "Try again" })).toBeTruthy();
  });
});

describe("kit theme bootstrap", () => {
  it("uses the project preset and the system theme by default", () => {
    expect(resolveTheme("", false)).toEqual({ preset: "clean", theme: "light", forced: false });
    expect(resolveTheme("?r=3", true)).toEqual({ preset: "clean", theme: "dark", forced: false });
  });

  it("accepts only whitelisted aas-preset / aas-theme values", () => {
    expect(resolveTheme("?aas-theme=dark&aas-preset=warm", false)).toEqual({ preset: "warm", theme: "dark", forced: true });
    expect(resolveTheme("?aas-theme=%3Cscript%3E&aas-preset=__proto__", false)).toEqual({ preset: "clean", theme: "light", forced: false });
    expect(resolveTheme("?aas-preset=constructor", true).preset).toBe("clean");
  });

  it("applyTheme sets data-preset and data-theme on <html>", () => {
    applyTheme({ preset: "playful", theme: "dark" });
    expect(document.documentElement.dataset.preset).toBe("playful");
    expect(document.documentElement.dataset.theme).toBe("dark");
  });
});

describe("PresetPicker", () => {
  it("offers the six presets as a radio group and reports the choice", () => {
    const onChange = vi.fn();
    const { getAllByRole, getByTestId } = render(h(PresetPicker, { value: "clean", onChange }));
    expect(getAllByRole("radio").map((r) => (r as HTMLInputElement).value)).toEqual([...PRESET_IDS]);
    expect((getByTestId("preset-clean") as HTMLInputElement).checked).toBe(true);
    fireEvent.click(getByTestId("preset-warm"));
    expect(onChange).toHaveBeenCalledWith("warm");
  });

  it("swatches are the kit tokens of each preset (no drift)", () => {
    // vitest runs with root apps/desktop (vite.config.ts)
    const css = readFileSync(resolve(process.cwd(), "../../kit/template/src/ui/globals.css"), "utf8");
    const block = (sel: string) => {
      const m = css.match(new RegExp(`${sel.replace(/[[\]"]/g, (c) => `\\${c}`)}\\s*\\{([^}]*)\\}`));
      return Object.fromEntries([...(m?.[1] ?? "").matchAll(/(--[a-z0-9-]+):\s*([^;]+);/g)].map((x) => [x[1], (x[2] ?? "").trim()]));
    };
    for (const id of PRESET_IDS) {
      const light = block(`[data-preset="${id}"]`);
      const t = id === "dark-pro" ? { ...light, ...block(`[data-preset="${id}"][data-theme="dark"]`) } : light;
      expect(PRESET_INFO[id].swatch, id).toEqual([t["--color-bg"], t["--color-surface-2"], t["--color-primary"], t["--color-fg"]]);
    }
  });

  it("Proiect nou passes a non-default preset; the default keeps the old call", async () => {
    const onCreate = vi.fn().mockResolvedValue(undefined);
    const props = { projects: [], selected: "", disabled: false, onSelect: vi.fn(), onCreate, requestDelete: vi.fn(),
      approve: vi.fn(), deny: vi.fn(), onDeleted: vi.fn() };
    const { getByTestId } = render(h(ProjectPicker, props));
    fireEvent.click(getByTestId("project-new"));
    expect(getByTestId("preset-picker")).toBeTruthy();
    fireEvent.change(getByTestId("project-form-name"), { target: { value: "Jurnal" } });
    fireEvent.change(getByTestId("project-form-template"), { target: { value: "tabs" } });
    fireEvent.click(getByTestId("preset-dark-pro"));
    fireEvent.click(getByTestId("project-form-create"));
    expect(onCreate).toHaveBeenCalledWith("Jurnal", "tabs", "dark-pro");
  });

  it("createProject sends <template>:<preset> only for a known non-default preset", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockImplementation(async () => new Response(JSON.stringify({ id: PID }), { status: 201 }));
    await createProject(SESSION, "n", "tabs", "warm");
    await createProject(SESSION, "n", "blank", "playful");
    await createProject(SESSION, "n", "list-detail", "clean");
    await createProject(SESSION, "n", "tabs", "evil" as never);
    const bodies = f.mock.calls.map((c) => JSON.parse(String(c[1]?.body)));
    expect(bodies).toEqual([
      { name: "n", template: "tabs:warm" },
      { name: "n", template: "blank:playful" },
      { name: "n", template: "list-detail" },
      { name: "n", template: "tabs" },
    ]);
  });
});

describe("ThemeSwitcher", () => {
  const SRC = "http://p-0123456789abcdef.localhost:47880/?r=3-1";

  it("themedSrc adds only the whitelisted values and keeps the rest", () => {
    expect(themedSrc(null, DEFAULT_PREVIEW_THEME)).toBeNull();
    expect(themedSrc(SRC, DEFAULT_PREVIEW_THEME)).toBe(SRC);
    expect(themedSrc(SRC, { mode: "dark", preset: "warm" })).toBe(`${SRC}&aas-theme=dark&aas-preset=warm`);
    expect(themedSrc(SRC, { mode: "split", preset: "project" }, "light")).toBe(`${SRC}&aas-theme=light`);
    expect(themedSrc(`${SRC}&aas-theme=dark`, { mode: "light", preset: "x" as never })).toBe(`${SRC}&aas-theme=light`);
  });

  it("one click switches the mode; the preset select falls back to the project's", () => {
    const onChange = vi.fn();
    const { getByTestId } = render(h(ThemeSwitcher, { value: DEFAULT_PREVIEW_THEME, onChange }));
    expect(getByTestId("theme-auto").getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(getByTestId("theme-dark"));
    expect(onChange).toHaveBeenLastCalledWith({ mode: "dark", preset: "project" });
    fireEvent.change(getByTestId("theme-preset"), { target: { value: "minimal" } });
    expect(onChange).toHaveBeenLastCalledWith({ mode: "auto", preset: "minimal" });
  });

  it("split shows the same build light and dark side by side, in the same sandbox", () => {
    const { getByTestId } = render(h(ThemedPreview, { theme: { mode: "split", preset: "project" }, device: "phone", src: SRC,
      demoRevision: null, taps: [], now: 0 }));
    const frames = Array.from(getByTestId("theme-split").querySelectorAll("iframe"));
    expect(frames.map((f) => f.getAttribute("src"))).toEqual([`${SRC}&aas-theme=light`, `${SRC}&aas-theme=dark`]);
    expect(frames.every((f) => f.getAttribute("sandbox") === "allow-scripts allow-same-origin")).toBe(true);
  });
});
