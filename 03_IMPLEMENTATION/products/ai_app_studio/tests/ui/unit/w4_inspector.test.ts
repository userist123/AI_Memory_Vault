// W4 click-to-edit inspector: strict descriptor schema, origin/source checks of the frame messages, the panel
// (text nodes only, edit + attach calls), the ChatPanel attachment hook and the kit inspector script.
import { act, afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { InspectorOverlay } from "../../../apps/desktop/src/components/InspectorOverlay";
import { InspectorPanel } from "../../../apps/desktop/src/components/InspectorPanel";
import { ChatPanel } from "../../../apps/desktop/src/components/ChatPanel";
import { parseChat } from "../../../apps/desktop/src/lib/chatApi";
import {
  attachmentContext,
  inspectorEdit,
  inspectorPreview,
  parseSelectMessage,
  previewOriginOf,
  projectIdFromOrigin,
  type InspectorSelection,
} from "../../../apps/desktop/src/lib/inspector";
import { describe as describeEl, isPreviewContext } from "../../../kit/template/src/aas-inspector";

const ID16 = "0123456789abcdef";
const PID = `prj_${ID16}`;
const ORIGIN = `http://p-${ID16}.localhost:47880`;
const SRC = `${ORIGIN}/?r=3-1`;

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function desc(over: Record<string, unknown> = {}) {
  return { v: 1, kind: "select", path: "main>h1", tag: "h1", text: "Habits", rect: { x: 16, y: 40, w: 200, h: 32 }, classes: ["title"], ...over };
}

describe("parseSelectMessage", () => {
  it("accepts exactly the v1 select descriptor", () => {
    expect(parseSelectMessage(desc())).toEqual(desc());
  });
  const bad: [string, unknown][] = [
    ["null", null],
    ["string", JSON.stringify(desc())],
    ["array", [desc()]],
    ["extra key", { ...desc(), html: "<b>" }],
    ["missing key", (({ classes: _c, ...rest }) => rest)(desc())],
    ["v as string", desc({ v: "1" })],
    ["v2", desc({ v: 2 })],
    ["other kind", desc({ kind: "eval" })],
    ["oversized text", desc({ text: "x".repeat(201) })],
    ["control char", desc({ text: "a\u0000b" })],
    ["bidi override", desc({ text: "a\u202eb" })],
    ["too many classes", desc({ classes: Array.from({ length: 21 }, (_, i) => `c${i}`) })],
    ["bad class", desc({ classes: ["a b"] })],
    ["class not a string", desc({ classes: [1] })],
    ["long path", desc({ path: "a".repeat(301) })],
    ["non-ascii path", desc({ path: "main>h1\u2028" })],
    ["bad tag", desc({ tag: "H1 onclick" })],
    ["NaN rect", desc({ rect: { x: Number.NaN, y: 0, w: 1, h: 1 } })],
    ["huge rect", desc({ rect: { x: 1e9, y: 0, w: 1, h: 1 } })],
    ["negative size", desc({ rect: { x: 0, y: 0, w: -1, h: 1 } })],
    ["rect extra", desc({ rect: { x: 0, y: 0, w: 1, h: 1, z: 2 } })],
    ["class instance", Object.assign(Object.create({ evil: 1 }) as object, desc())],
  ];
  for (const [name, data] of bad) {
    it(`rejects ${name}`, () => {
      expect(parseSelectMessage(data)).toBeNull();
    });
  }

  it("derives the project only from an exact preview origin", () => {
    expect(projectIdFromOrigin(ORIGIN)).toBe(PID);
    expect(projectIdFromOrigin(`https://p-${ID16}.localhost:47880`)).toBeNull();
    expect(projectIdFromOrigin(`http://p-${ID16}.localhost.evil.com:47880`)).toBeNull();
    expect(projectIdFromOrigin(`http://p-${ID16}.localhost`)).toBeNull();
    expect(previewOriginOf(SRC)).toBe(ORIGIN);
    expect(previewOriginOf("http://127.0.0.1:47875/")).toBeNull();
    expect(previewOriginOf("not a url")).toBeNull();
  });
});

function frame(): HTMLIFrameElement {
  const f = document.createElement("iframe");
  document.body.appendChild(f);
  return f;
}

function post(data: unknown, origin: string, source: Window | null) {
  act(() => {
    window.dispatchEvent(new MessageEvent("message", { data, origin, source }));
  });
}

describe("InspectorOverlay", () => {
  it("arms the frame for its exact origin and accepts only that frame + origin + schema", () => {
    const f = frame();
    const w = f.contentWindow!;
    const armed = vi.spyOn(w, "postMessage").mockImplementation(() => undefined);
    const onSelect = vi.fn();
    const { getByTestId, unmount } = render(
      createElement(InspectorOverlay, { device: "phone", src: SRC, frame: f, active: true, onToggle: vi.fn(), onSelect }),
    );
    expect(armed).toHaveBeenCalledWith({ v: 1, kind: "inspect", on: true }, ORIGIN);
    post(desc(), "http://p-fedcba9876543210.localhost:47880", w); // another project's origin
    post(desc(), "http://evil.example", w); // spoofed origin
    post(desc(), ORIGIN, window); // right origin claimed, wrong source (not the frame)
    post(desc(), ORIGIN, null);
    post({ ...desc(), extra: 1 }, ORIGIN, w); // malformed
    post(desc({ text: "y".repeat(10_000) }), ORIGIN, w); // oversized
    expect(onSelect).not.toHaveBeenCalled();
    expect(getByTestId("inspector-rejected").textContent).toBe("4 respinse");
    post(desc(), ORIGIN, w);
    expect(onSelect).toHaveBeenCalledWith({ projectId: PID, device: "phone", element: desc() });
    unmount();
    expect(armed).toHaveBeenLastCalledWith({ v: 1, kind: "inspect", on: false }, ORIGIN);
  });

  it("listens to nothing while off and renders nothing for a non-preview src", () => {
    const f = frame();
    const onSelect = vi.fn();
    render(createElement(InspectorOverlay, { device: "phone", src: SRC, frame: f, active: false, onToggle: vi.fn(), onSelect }));
    post(desc(), ORIGIN, f.contentWindow);
    expect(onSelect).not.toHaveBeenCalled();
    cleanup();
    const { container } = render(
      createElement(InspectorOverlay, { device: "phone", src: "http://127.0.0.1:1/", frame: f, active: true, onToggle: vi.fn(), onSelect }),
    );
    expect(container.innerHTML).toBe("");
  });
});

const SEL: InspectorSelection = { projectId: PID, device: "phone", element: desc() as InspectorSelection["element"] };
const TOKEN = "c".repeat(64);
const OPTS = { revision: 7, families: { color: ["bg-primary", "bg-accent", "text-primary"], spacing: ["p-2", "p-4", "m-2"] } };

async function mountPanel(sel: InspectorSelection = SEL) {
  const loadOptions = vi.fn(async () => OPTS);
  const edit = vi.fn(async () => ({ path: "src/App.tsx", revision: 8, rebuild: "building" }));
  const preview = vi.fn(async () => ({ path: "src/App.tsx", line: 12, planToken: TOKEN }));
  const capture = vi.fn(async () => "a".repeat(64));
  const onAttach = vi.fn();
  let r!: ReturnType<typeof render>;
  await act(async () => {
    r = render(createElement(InspectorPanel, { selection: sel, loadOptions, preview, edit, capture, onAttach, onClose: vi.fn() }));
  });
  return { ...r, loadOptions, preview, edit, capture, onAttach };
}

describe("InspectorPanel", () => {
  it("renders descriptor fields as text only", async () => {
    const evil = '<img src=x onerror="alert(1)">';
    const { container, getByTestId } = await mountPanel({ ...SEL, element: { ...SEL.element, path: evil, text: evil } });
    expect(container.querySelector("img")).toBeNull();
    expect(getByTestId("inspector-path").textContent).toBe(evil);
  });

  it("saves the text with the options revision and swaps a class within its family", async () => {
    const el = { ...SEL.element, classes: ["btn", "bg-primary", "p-2"] };
    const { getByTestId, edit, preview } = await mountPanel({ ...SEL, element: el });
    fireEvent.change(getByTestId("inspector-text"), { target: { value: "My habits" } });
    await act(async () => {
      fireEvent.click(getByTestId("inspector-save-text"));
    });
    // step 1: only the dry run; the owner sees the target file:line and confirms before anything is written
    expect(preview).toHaveBeenCalledWith(PID, 7, expect.objectContaining({ element: el }), { op: "text", to: "My habits" });
    expect(edit).not.toHaveBeenCalled();
    expect(getByTestId("inspector-target").textContent).toContain("src/App.tsx:12");
    await act(async () => {
      fireEvent.click(getByTestId("inspector-confirm"));
    });
    expect(edit).toHaveBeenCalledWith(PID, 7, expect.objectContaining({ element: el }), { op: "text", to: "My habits" }, TOKEN);
    expect(getByTestId("inspector-status").textContent).toContain("rev 8");
    const sel = getByTestId("inspector-swap-p-2") as HTMLSelectElement;
    expect(Array.from(sel.options).map((o) => o.value)).toEqual(["p-2", "p-4"]); // same prefix only
    expect(() => getByTestId("inspector-swap-btn")).toThrow(); // not in the allowed list
    fireEvent.change(sel, { target: { value: "p-4" } });
    await act(async () => {
      fireEvent.click(getByTestId("inspector-apply-p-2"));
    });
    await act(async () => {
      fireEvent.click(getByTestId("inspector-confirm"));
    });
    expect(edit).toHaveBeenLastCalledWith(PID, 8, expect.anything(), { op: "class", from: "p-2", to: "p-4" }, TOKEN);
  });

  it("shows a clear message for an ambiguous edit", async () => {
    const { getByTestId, preview } = await mountPanel();
    preview.mockRejectedValueOnce(Object.assign(new Error("x"), { status: 409, serverMessage: "inspector_ambiguous: twice" }));
    fireEvent.change(getByTestId("inspector-text"), { target: { value: "Other" } });
    await act(async () => {
      fireEvent.click(getByTestId("inspector-save-text"));
    });
    expect(getByTestId("inspector-error").textContent).toContain("mai multe ori");
  });

  it("cancelling the confirmation writes nothing", async () => {
    const { getByTestId, edit, preview } = await mountPanel();
    fireEvent.change(getByTestId("inspector-text"), { target: { value: "Other" } });
    await act(async () => {
      fireEvent.click(getByTestId("inspector-save-text"));
    });
    expect(preview).toHaveBeenCalledTimes(1);
    fireEvent.click(getByTestId("inspector-cancel"));
    expect(() => getByTestId("inspector-target")).toThrow();
    expect(edit).not.toHaveBeenCalled();
  });

  it("attaches the descriptor + cropped capture to the chat", async () => {
    const { getByTestId, capture, onAttach } = await mountPanel();
    await act(async () => {
      fireEvent.click(getByTestId("inspector-attach"));
    });
    expect(capture).toHaveBeenCalledWith(SEL);
    const a = onAttach.mock.calls[0]![0];
    expect(a.projectId).toBe(PID);
    expect(a.captureSha).toBe("a".repeat(64));
    expect(a.context).toContain('text: "Habits"');
    expect(a.context).toContain("untrusted page data");
  });
});

describe("inspector REST: the confirm is bound to the dry run (planToken)", () => {
  afterEach(() => vi.unstubAllGlobals());
  const session = { apiBase: "http://127.0.0.1:17875", wsUrl: "ws://127.0.0.1:17875/ws", token: "t".repeat(40), previewPort: 17880 };

  function mockJson(body: unknown) {
    const calls: Array<{ url: string; init: RequestInit }> = [];
    vi.stubGlobal("fetch", vi.fn(async (url: string, init: RequestInit) => {
      calls.push({ url, init });
      return new Response(JSON.stringify(body), { status: 200, headers: { "Content-Type": "application/json" } });
    }));
    return calls;
  }

  it("the dry run returns the server's planToken and drops a malformed one", async () => {
    mockJson({ dryRun: true, path: "src/App.tsx", line: 3, op: "text", planToken: TOKEN });
    const el = desc() as InspectorSelection["element"];
    expect(await inspectorPreview(session, PID, 7, el, { op: "text", to: "x" })).toEqual({ path: "src/App.tsx", line: 3, planToken: TOKEN });
    mockJson({ dryRun: true, path: "src/App.tsx", line: 3, op: "text", planToken: "<script>" });
    expect((await inspectorPreview(session, PID, 7, el, { op: "text", to: "x" })).planToken).toBe("");
  });

  it("the confirm sends the planToken with the same body", async () => {
    const calls = mockJson({ path: "src/App.tsx", line: 3, revision: 8, rebuild: "building" });
    const el = desc() as InspectorSelection["element"];
    await inspectorEdit(session, PID, 7, el, { op: "text", to: "x" }, TOKEN);
    const sent = JSON.parse(String(calls[0]!.init.body)) as Record<string, unknown>;
    expect(sent).toEqual({ baseRevision: 7, element: el, op: "text", to: "x", confirm: true, planToken: TOKEN });
  });
});

describe("ChatPanel attachment", () => {
  it("appends the attached element to the next message of its project, then clears it", async () => {
    const send = vi.fn(async () => ({ runId: "run_00000000000000a2" }));
    const onClearAttachment = vi.fn();
    const attachment = { projectId: PID, label: "<h1> „Habits”", context: attachmentContext(SEL, null), captureSha: null };
    let r!: ReturnType<typeof render>;
    await act(async () => {
      r = render(
        createElement(ChatPanel, {
          projects: [{ id: PID, name: "Notes" }],
          currentProjectId: PID,
          version: 0,
          runActive: false,
          load: vi.fn(async () => parseChat({ turns: [] })),
          send,
          plans: [],
          onPlanApprove: vi.fn(),
          onPlanEdit: vi.fn(),
          attachment,
          onClearAttachment,
        }),
      );
    });
    expect(r.getByTestId("chat-attachment").textContent).toContain("Habits");
    fireEvent.change(r.getByTestId("chat-input"), { target: { value: "fă-l mai mare" } });
    await act(async () => {
      fireEvent.click(r.getByTestId("chat-send"));
    });
    expect(send).toHaveBeenCalledWith(PID, `fă-l mai mare\n\n${attachment.context}`);
    expect(onClearAttachment).toHaveBeenCalled();
  });
});

describe("kit aas-inspector", () => {
  it("is inert outside a framed preview origin", () => {
    expect(isPreviewContext(window)).toBe(false); // jsdom: localhost, top level
    const fake = (host: string, framed: boolean) => {
      const w = { location: { hostname: host } } as unknown as Window & { parent: unknown };
      (w as { parent: unknown }).parent = framed ? {} : w;
      return w;
    };
    expect(isPreviewContext(fake(`p-${ID16}.localhost`, true))).toBe(true);
    expect(isPreviewContext(fake(`p-${ID16}.localhost`, false))).toBe(false); // exported app opened top level
    expect(isPreviewContext(fake("localhost", true))).toBe(false); // Capacitor WebView
    expect(isPreviewContext(fake(`p-${ID16}.localhost.evil.com`, true))).toBe(false);
  });

  it("describes an element within the protocol limits", () => {
    const main = document.createElement("main");
    const h = document.createElement("h1");
    h.className = Array.from({ length: 30 }, (_, i) => `c${i}`).join(" ") + " bad\u0000";
    h.textContent = `  ${"long text ".repeat(50)}\n`;
    main.appendChild(document.createElement("h1"));
    main.appendChild(h);
    document.body.appendChild(main);
    const d = describeEl(h)!;
    expect(parseSelectMessage(d)).not.toBeNull();
    expect(d.text.length).toBe(200);
    expect(d.classes.length).toBe(20);
    expect(d.path).toBe("main>h1:nth-of-type(2)");
  });
});

describe("InspectorPanel kit component modifiers (wow2 #6)", () => {
  it("offers the other values of the same component only and sends a class op", async () => {
    const comp = ["ui-btn--primary", "ui-btn--secondary", "ui-btn--ghost", "ui-btn--danger", "ui-toast--neutral", "ui-toast--danger"];
    const loadOptions = vi.fn(async () => ({ ...OPTS, families: { ...OPTS.families, component: comp } }));
    const preview = vi.fn(async () => ({ path: "src/App.tsx", line: 5, planToken: TOKEN }));
    const el = { ...SEL.element, tag: "button", classes: ["ui-btn", "ui-btn--secondary"] };
    let r!: ReturnType<typeof render>;
    await act(async () => {
      r = render(createElement(InspectorPanel, { selection: { ...SEL, element: el }, loadOptions, preview, edit: vi.fn(), capture: vi.fn(), onAttach: vi.fn(), onClose: vi.fn() }));
    });
    const sel = r.getByTestId("inspector-swap-ui-btn--secondary") as HTMLSelectElement;
    expect(Array.from(sel.options).map((o) => o.value)).toEqual(["ui-btn--primary", "ui-btn--secondary", "ui-btn--ghost", "ui-btn--danger"]);
    expect(() => r.getByTestId("inspector-swap-ui-btn")).toThrow(); // the base class is not swappable
    fireEvent.change(sel, { target: { value: "ui-btn--danger" } });
    await act(async () => {
      fireEvent.click(r.getByTestId("inspector-apply-ui-btn--secondary"));
    });
    expect(preview).toHaveBeenCalledWith(PID, 7, expect.anything(), { op: "class", from: "ui-btn--secondary", to: "ui-btn--danger" });
  });
});
