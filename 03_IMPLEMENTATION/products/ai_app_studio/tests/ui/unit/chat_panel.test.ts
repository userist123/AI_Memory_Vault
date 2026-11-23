// Owner chat per project (owner request 2026-11-23): ChatPanel + chatApi parsing + store chatVersion wiring.
import { act, afterEach, cleanup, createElement, describe, expect, fireEvent, it, render, vi } from "../../../apps/desktop/testkit/unit";
import { ChatPanel } from "../../../apps/desktop/src/components/ChatPanel";
import { ApiError, type PlanRecord } from "../../../apps/desktop/src/lib/api";
import { type ChatState, fetchChat, parseChat, sendChat } from "../../../apps/desktop/src/lib/chatApi";
import { parseFrame } from "../../../apps/desktop/src/lib/events";
import { initialState, reducer } from "../../../apps/desktop/src/lib/store";

const PID = "prj_0123456789abcdef";
const PID2 = "prj_fedcba9876543210";
const RUN1 = "run_00000000000000a1";
const RUN2 = "run_00000000000000a2";
const SESSION = { apiBase: "http://127.0.0.1:47875", wsUrl: "ws://127.0.0.1:47875/api/v1/events", token: "t".repeat(40), previewPort: 47880 };

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function turn(over: Record<string, unknown> = {}) {
  return { runId: RUN1, prompt: "fă o aplicație de notițe", reply: "Gata: notițe cu butonul +.", status: "success", reason: "", mode: "new", costUsd: 0.1, planIds: [], chat: true, ...over };
}

function chatState(turns: Record<string, unknown>[], over: Partial<ChatState> = {}): ChatState {
  return { ...parseChat({ turns }), ...over };
}

async function mount(over: Partial<Parameters<typeof ChatPanel>[0]> = {}, state: ChatState = chatState([turn()])) {
  const load = vi.fn(async () => state);
  const send = vi.fn(async () => ({ runId: RUN2 }));
  const onPlanApprove = vi.fn();
  const onPlanEdit = vi.fn();
  let r!: ReturnType<typeof render>;
  await act(async () => {
    r = render(
      createElement(ChatPanel, {
        projects: [
          { id: PID, name: "Notes" },
          { id: PID2, name: "Other" },
        ],
        currentProjectId: PID,
        version: 0,
        runActive: false,
        load,
        send,
        plans: [],
        onPlanApprove,
        onPlanEdit,
        ...over,
      }),
    );
  });
  return { ...r, load, send, onPlanApprove, onPlanEdit };
}

describe("ChatPanel", () => {
  it("shows the project's turns as owner + AI bubbles (text nodes only)", async () => {
    const evil = '<img src=x onerror="alert(1)">‮';
    const { getAllByTestId, container, load } = await mount({}, chatState([turn({ reply: `ok ${evil}` })]));
    expect(load).toHaveBeenCalledWith(PID);
    expect(getAllByTestId("chat-owner")[0]!.textContent).toBe("fă o aplicație de notițe");
    const ai = getAllByTestId("chat-ai")[0]!;
    expect(ai.textContent).toContain("<img");
    expect(ai.textContent).not.toContain("‮"); // bidi control stripped by parseChat
    expect(container.querySelector("img")).toBeNull();
  });

  it("sends a follow-up for the same project and refetches the turns", async () => {
    const { getByTestId, send, load } = await mount();
    const btn = getByTestId("chat-send") as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    fireEvent.change(getByTestId("chat-input"), { target: { value: "  fă butonul + verde " } });
    expect(btn.disabled).toBe(false);
    await act(async () => {
      fireEvent.click(btn);
    });
    expect(send).toHaveBeenCalledWith(PID, "fă butonul + verde");
    expect(load).toHaveBeenCalledTimes(2);
    expect((getByTestId("chat-input") as HTMLTextAreaElement).value).toBe("");
  });

  it("Enter sends, Shift+Enter does not", async () => {
    const { getByTestId, send } = await mount();
    fireEvent.change(getByTestId("chat-input"), { target: { value: "adaugă categorii" } });
    fireEvent.keyDown(getByTestId("chat-input"), { key: "Enter", shiftKey: true });
    expect(send).not.toHaveBeenCalled();
    await act(async () => {
      fireEvent.keyDown(getByTestId("chat-input"), { key: "Enter" });
    });
    expect(send).toHaveBeenCalledWith(PID, "adaugă categorii");
  });

  it("send is disabled while a run is going (studio or server says busy)", async () => {
    for (const [over, st] of [
      [{ runActive: true }, chatState([turn()])],
      [{}, chatState([turn({ status: "running", reply: "" })], { busy: true, running: true })],
    ] as const) {
      const { getByTestId, queryByTestId, unmount } = await mount(over, st);
      fireEvent.change(getByTestId("chat-input"), { target: { value: "x" } });
      expect((getByTestId("chat-send") as HTMLButtonElement).disabled).toBe(true);
      expect(queryByTestId("chat-busy")).not.toBeNull();
      unmount();
    }
  });

  it("a 409 from the server is explained, not swallowed", async () => {
    const { getByTestId, send } = await mount();
    send.mockRejectedValueOnce(new ApiError(409, "another run is in progress"));
    fireEvent.change(getByTestId("chat-input"), { target: { value: "x" } });
    await act(async () => {
      fireEvent.click(getByTestId("chat-send"));
    });
    expect(getByTestId("chat-error").textContent).toContain("lucrează deja");
    expect(() => getByTestId("chat-pending")).toThrow();
  });

  it("refetches on a new store version and when the owner picks another project", async () => {
    const r = await mount();
    expect(r.load).toHaveBeenCalledTimes(1);
    await act(async () => {
      r.rerender(
        createElement(ChatPanel, {
          projects: [{ id: PID, name: "Notes" }, { id: PID2, name: "Other" }],
          currentProjectId: PID, version: 1, runActive: false, load: r.load, send: r.send, plans: [],
          onPlanApprove: r.onPlanApprove, onPlanEdit: r.onPlanEdit,
        }),
      );
    });
    expect(r.load).toHaveBeenCalledTimes(2);
    await act(async () => {
      fireEvent.change(r.getByTestId("chat-project"), { target: { value: PID2 } });
    });
    expect(r.load).toHaveBeenLastCalledWith(PID2);
  });

  it("an inline plan card comes from the server plan record of that turn and approves through the plan route", async () => {
    const plan: PlanRecord = { id: "pln_0000000000000001", title: "Butonul verde", steps: ["schimbă culoarea"], runId: RUN1, contentSha256: "a".repeat(64), unsafe: false };
    const other: PlanRecord = { ...plan, id: "pln_0000000000000002", runId: "run_ffffffffffffffff" };
    const { getAllByTestId, onPlanApprove } = await mount({ plans: [plan, other] }, chatState([turn({ planIds: [plan.id], reply: "" , status: "running"})]));
    const cards = getAllByTestId("plan-card");
    expect(cards).toHaveLength(1);
    fireEvent.click(cards[0]!.querySelector("button.primary")!);
    expect(onPlanApprove).toHaveBeenCalledWith(plan.id);
  });

  it("a failed turn shows the plain Romanian reason; a summary turn says so", async () => {
    const { getByTestId } = await mount({}, chatState([turn({ status: "error_max_budget_usd", mode: "summary" })]));
    expect(getByTestId("chat-failed").textContent).toContain("bugetul");
    expect(getByTestId("chat-summary-note")).toBeTruthy();
  });

  it("no project: input disabled, nothing loaded", async () => {
    const { getByTestId, load } = await mount({ projects: [], currentProjectId: "" });
    expect((getByTestId("chat-input") as HTMLTextAreaElement).disabled).toBe(true);
    expect(load).not.toHaveBeenCalled();
  });
});

describe("chatApi", () => {
  it("parseChat drops malformed turns and bad ids, caps text", () => {
    const st = parseChat({
      turns: [turn(), turn({ runId: "../../x" }), "junk", turn({ runId: RUN2, planIds: ["ok_1", "<bad>"], costUsd: -1, reply: "r".repeat(20000) })],
      running: "yes",
      continuity: "resume",
    });
    expect(st.turns.map((t) => t.runId)).toEqual([RUN1, RUN2]);
    expect(st.turns[1]!.planIds).toEqual(["ok_1"]);
    expect(st.turns[1]!.costUsd).toBeNull();
    expect(st.turns[1]!.reply.length).toBeLessThan(8100);
    expect(st.running).toBe(false);
    expect(st.continuity).toBe("resume");
  });

  it("fetchChat / sendChat use the bearer token, no cookies, and validate ids", async () => {
    const f = vi.spyOn(globalThis, "fetch").mockImplementation(async (_u, init) =>
      new Response(init?.method === "POST" ? JSON.stringify({ runId: RUN2 }) : JSON.stringify({ turns: [turn()] }), { status: init?.method === "POST" ? 202 : 200 }),
    );
    expect((await fetchChat(SESSION, PID)).turns).toHaveLength(1);
    expect(await sendChat(SESSION, PID, "fă butonul + verde")).toEqual({ runId: RUN2 });
    const [url, init] = f.mock.calls[1]!;
    expect(url).toBe(`${SESSION.apiBase}/api/v1/projects/${PID}/chat`);
    expect(init?.credentials).toBe("omit");
    expect((init?.headers as Record<string, string>).Authorization).toBe(`Bearer ${SESSION.token}`);
    expect(JSON.parse(String(init?.body))).toEqual({ message: "fă butonul + verde" });
    await expect(fetchChat(SESSION, "../etc")).rejects.toThrow();
    expect(f).toHaveBeenCalledTimes(2);
  });
});

describe("store wiring", () => {
  it("chat kinds pass the frame allow-list and every chat-relevant event bumps chatVersion (kept across runs)", () => {
    let s = initialState;
    let seq = 0;
    const ev = (kind: string, runId = RUN1) => {
      const p = parseFrame(JSON.stringify({ seq: ++seq, prev: seq - 1, kind, runId, projectId: PID, ts: 1, payload: {} }));
      expect(p.ok).toBe(true);
      if (p.ok) s = reducer(s, { type: "event", event: p.event });
    };
    ev("run.started");
    ev("chat.prompt");
    ev("agent.text");
    ev("run.finished");
    const v = s.chatVersion;
    expect(v).toBe(4);
    ev("run.started", RUN2); // a new run resets the run view, not the chat counter
    expect(s.chatVersion).toBe(v + 1);
    ev("chat.fallback", RUN2);
    expect(s.chatVersion).toBe(v + 2);
  });
});
