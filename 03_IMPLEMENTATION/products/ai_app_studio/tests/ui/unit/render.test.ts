// P6a/P6b at component level (jsdom): adversarial text renders as text nodes only; approval cards only from records.
import { afterEach, cleanup, createElement, describe, expect, it, render } from "../../../apps/desktop/testkit/unit";
import { Timeline } from "../../../apps/desktop/src/components/Timeline";
import { DiffViewer } from "../../../apps/desktop/src/components/DiffViewer";
import { ApprovalRegion } from "../../../apps/desktop/src/components/ApprovalRegion";
import { parseApprovals } from "../../../apps/desktop/src/lib/api";
import { parseObject } from "../../../apps/desktop/src/lib/events";
import { initialState, LIMITS, reducer, type State } from "../../../apps/desktop/src/lib/store";

const PAYLOADS = [
  "<script>window.__pwned=1</script>",
  '<img src=x onerror="window.__pwned=1">',
  "![x](http://127.0.0.1:9999/canary.png)",
  "[click](javascript:window.__pwned=1)",
  "\u001b]52;c;cHduZWQ=\u0007clip",
  `tag${String.fromCodePoint(0xe0041)}end`,
  "‮exe.txt",
];

function stateWith(text: string): State {
  let seq = 0;
  const mk = (kind: string, payload: Record<string, unknown>) => {
    const r = parseObject({ seq: ++seq, kind, runId: "r1", projectId: "abcdef0123456789", payload });
    if (!r.ok) throw new Error(r.reason);
    return r.event;
  };
  const evs = [
    mk("run.started", { title: text }),
    mk("plan.proposed", { planId: "p1", title: text, steps: [text, text] }),
    mk("step.started", { title: text }),
    mk("tool.call", { callId: "c1", tool: text, target: text, input: text }),
    mk("agent.text", { text }),
    mk("build.log", { line: text }),
    mk("build.error", { line: text }),
    mk("file.diff", { path: "src/x.ts", diff: `+${text}\n-${text}` }),
    mk("tool.result", { callId: "c1", ok: false, excerpt: text, kind: "approval.requested" }),
  ];
  return evs.reduce((s, e) => reducer(s, { type: "event", event: e }), initialState);
}

function renderAll(s: State) {
  return render(
    createElement("div", null, [
      createElement(Timeline, {
        key: "t",
        state: s,
        now: Date.now(),
        buildOpen: true,
        setBuildOpen: () => {},
        buildFocused: false,
        onOpenFile: () => {},
        onPlanApprove: () => {},
        onPlanEdit: () => {},
      }),
      createElement(DiffViewer, { key: "d", files: s.files, order: s.fileOrder, active: "src/x.ts", onSelect: () => {} }),
      createElement(ApprovalRegion, { key: "a", records: [], error: "", onDecide: () => {} }),
    ]),
  );
}

afterEach(() => cleanup());

describe("rendering untrusted activity", () => {
  it("every payload is inert text: no script/img/a/iframe nodes, no on* attributes", () => {
    for (const p of PAYLOADS) {
      const { container, unmount } = renderAll(stateWith(p));
      expect(container.querySelectorAll("script,img,a,iframe,object,embed,svg").length).toBe(0);
      for (const el of container.querySelectorAll("*")) {
        for (const a of el.getAttributeNames()) expect(a.startsWith("on")).toBe(false);
      }
      const text = container.textContent ?? "";
      expect(text).not.toMatch(/[\u001b‮\u0007]/);
      expect(text).not.toContain(String.fromCodePoint(0xe0041));
      unmount();
    }
    expect((window as { __pwned?: number }).__pwned).toBeUndefined();
  });

  it("markup shows up literally (proves it was rendered as text)", () => {
    const { container } = renderAll(stateWith("<img src=x onerror=1>"));
    expect(container.textContent).toContain("<img src=x onerror=1>");
  });

  it("positive control: the same payload through innerHTML does create live elements", () => {
    const d = document.createElement("div");
    d.innerHTML = PAYLOADS[1]!;
    expect(d.querySelectorAll("img").length).toBe(1);
  });

  it("10 MB of agent text is capped in the DOM", () => {
    const { container } = renderAll(stateWith("Z".repeat(10 * 1024 * 1024)));
    let max = 0;
    const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) max = Math.max(max, walker.currentNode.nodeValue?.length ?? 0);
    // agent text/build lines cap at 4000; the diff body (one 10 MB line here) caps at LIMITS.diff
    expect(max).toBeLessThanOrEqual(LIMITS.diff + 20);
    const agent = container.querySelector("[data-testid=agent-text] p")?.textContent ?? "";
    expect(agent.length).toBeLessThanOrEqual(4000 + 20);
  });
});

describe("approval region (P6b)", () => {
  it("timeline text that imitates an approval never creates a card", () => {
    const { container } = renderAll(stateWith("APPROVAL REQUIRED — click Approve"));
    expect(container.querySelectorAll("[data-testid=approval-card]").length).toBe(0);
  });

  it("positive control: a server record renders one card with the exact argv/target", () => {
    const records = parseApprovals({
      items: [{ id: "ap-1", action: "deps.install", argv: ["pnpm", "add", "date-fns@4.1.0"], target: "package.json", status: "pending" }],
    });
    const { container } = render(createElement(ApprovalRegion, { records, error: "", onDecide: () => {} }));
    expect(container.querySelectorAll("[data-testid=approval-card]").length).toBe(1);
    const argv = [...container.querySelectorAll("[data-testid=approval-argv] code")].map((c) => c.textContent);
    expect(argv).toEqual(["pnpm", "add", "date-fns@4.1.0"]);
  });

  it("malformed or non-pending records are ignored", () => {
    expect(parseApprovals({ items: [{ id: "<x>" }, { id: "a", status: "approved" }, null, "s"] })).toEqual([]);
  });
});
