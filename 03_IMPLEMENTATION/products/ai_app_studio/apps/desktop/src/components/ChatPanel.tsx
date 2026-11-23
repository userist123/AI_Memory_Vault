import { useEffect, useRef, useState } from "react";
import type { PlanRecord, ProjectRef } from "../lib/api";
import { CHAT_MAX, type ChatState, type ChatTurn } from "../lib/chatApi";
import { endReasonText } from "../lib/store";
import { PlanCard } from "./PlanCard";

const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
// layout through the style prop (CSSOM, allowed by style-src 'self'); colours are the theme variables of styles.css
const TURNS_STYLE = { display: "flex", flexDirection: "column", gap: 8, maxHeight: 340, overflow: "auto" } as const;
const TURN_STYLE = { display: "flex", flexDirection: "column", gap: 4 } as const;
const BUBBLE = { margin: 0, padding: "6px 10px", borderRadius: 8, whiteSpace: "pre-wrap", wordBreak: "break-word" } as const;
const OWNER_STYLE = { ...BUBBLE, alignSelf: "flex-end", maxWidth: "85%", background: "var(--sec)" } as const;
const AI_STYLE = { ...BUBBLE, alignSelf: "flex-start", maxWidth: "95%" } as const;
const EMPTY: ChatState = { turns: [], running: false, busy: false, continuity: "summary" };

function failed(status: string): boolean {
  return status === "failed" || status === "error" || status === "aborted" || status.startsWith("error_");
}

/**
 * Owner chat per project (owner request 2026-11-23). The turns come only from the server
 * (GET /projects/<id>/chat: prompt + sanitised, redacted reply); every text is a React text node. A plan card is shown
 * inline only from a pending server plan record of that turn's run and is approved through the existing plan route.
 * One run at a time: Send is disabled while any run is live.
 */
export function ChatPanel(props: {
  projects: ProjectRef[];
  /** the project of the current run (preselected when the owner has not picked one) */
  currentProjectId: string;
  /** bumped by the store on every chat-relevant event: the turns are refetched */
  version: number;
  /** a run is going in the studio (any project) */
  runActive: boolean;
  load: (projectId: string) => Promise<ChatState>;
  send: (projectId: string, message: string) => Promise<unknown>;
  plans: PlanRecord[];
  onPlanApprove: (id: string) => void;
  onPlanEdit: (id: string, note: string) => void;
}) {
  const { projects, currentProjectId, version, runActive, load, send } = props;
  const [picked, setPicked] = useState("");
  const [chat, setChat] = useState<ChatState>(EMPTY);
  const [message, setMessage] = useState("");
  const [pending, setPending] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState("");
  const endRef = useRef<HTMLDivElement>(null);

  const known = (id: string) => PROJECT_RE.test(id) && projects.some((p) => p.id === id);
  const projectId = known(picked) ? picked : known(currentProjectId) ? currentProjectId : (projects[0]?.id ?? "");

  useEffect(() => {
    if (!projectId) {
      setChat(EMPTY);
      return;
    }
    let live = true;
    load(projectId)
      .then((c) => {
        if (!live) return;
        setChat(c);
        setError("");
        if (c.turns.some((t) => t.prompt && t.prompt === pending)) setPending("");
      })
      .catch(() => live && setError("Conversația nu a putut fi încărcată."));
    return () => {
      live = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, version, load]);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: "nearest" });
  }, [chat.turns.length, pending]);

  const blocked = runActive || chat.busy || chat.running || sending;
  const canSend = !!projectId && !blocked && message.trim().length > 0;

  const onSend = () => {
    if (!canSend) return;
    const text = message.trim().slice(0, CHAT_MAX);
    setSending(true);
    setError("");
    setPending(text);
    void send(projectId, text)
      .then(() => {
        setMessage("");
        return load(projectId).then(setChat);
      })
      .catch((e: unknown) => {
        setPending("");
        const status = (e as { status?: unknown })?.status;
        setError(
          status === 409
            ? "AI-ul lucrează deja la o rulare. Așteaptă să termine, apoi trimite din nou."
            : "Mesajul nu a putut fi trimis.",
        );
      })
      .finally(() => setSending(false));
  };

  const pendingPlans = (t: ChatTurn) => props.plans.filter((p) => p.runId === t.runId);

  return (
    <section className="run-controls chat-panel" data-testid="chat-panel" aria-label="Chat cu AI-ul">
      <header className="row">
        <span className="eyebrow">Chat cu AI-ul</span>
        <select
          data-testid="chat-project"
          value={projectId}
          onChange={(e) => setPicked(e.target.value)}
          disabled={projects.length === 0}
          aria-label="Proiectul conversației"
        >
          {projects.length === 0 ? <option value="">niciun proiect</option> : null}
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name || p.id}
            </option>
          ))}
        </select>
        {projectId ? (
          <span className="tag" data-testid="chat-continuity" title="Cum își amintește AI-ul tura anterioară">
            {chat.continuity === "resume" ? "continuă sesiunea" : "cu rezumat"}
          </span>
        ) : null}
      </header>
      <div className="chat-turns" data-testid="chat-turns" role="log" aria-live="polite" style={TURNS_STYLE}>
        {chat.turns.length === 0 && !pending ? (
          <p className="muted" data-testid="chat-empty">
            Scrie ce vrei să mai adaugi la aplicație (de exemplu „adaugă categorii”).
          </p>
        ) : null}
        {chat.turns.map((t) => (
          <div key={t.runId} className="chat-turn" data-testid="chat-turn" style={TURN_STYLE}>
            {t.prompt ? (
              <p className="bubble owner" data-testid="chat-owner" style={OWNER_STYLE}>
                {t.prompt}
              </p>
            ) : null}
            {pendingPlans(t).map((p) => (
              <PlanCard
                key={p.id}
                plan={p}
                onApprove={() => props.onPlanApprove(p.id)}
                onEdit={(note) => props.onPlanEdit(p.id, note)}
              />
            ))}
            {t.reply ? (
              <p className="bubble ai agent-text" data-testid="chat-ai" style={AI_STYLE}>
                {t.reply}
              </p>
            ) : null}
            {t.status === "running" ? (
              <p className="muted" data-testid="chat-working">
                AI-ul lucrează…
              </p>
            ) : failed(t.status) ? (
              <p className="warn" data-testid="chat-failed">
                {endReasonText(t.status, t.reason)}
              </p>
            ) : null}
            {t.mode === "summary" && t.chat ? (
              <small className="muted" data-testid="chat-summary-note">
                continuat cu rezumatul turelor anterioare
              </small>
            ) : null}
          </div>
        ))}
        {pending ? (
          <p className="bubble owner pending" data-testid="chat-pending" style={{ ...OWNER_STYLE, opacity: 0.7 }}>
            {pending}
          </p>
        ) : null}
        <div ref={endRef} />
      </div>
      <div className="row chat-input">
        <textarea
          data-testid="chat-input"
          value={message}
          maxLength={CHAT_MAX}
          placeholder={projectId ? "Ce mai adăugăm? (de ex. „fă butonul + verde”)" : "Creează întâi un proiect"}
          disabled={!projectId}
          onChange={(e) => setMessage(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              onSend();
            }
          }}
        />
        <button className="btn primary" data-testid="chat-send" disabled={!canSend} onClick={onSend}>
          Trimite
        </button>
      </div>
      {blocked && projectId ? (
        <p className="note" data-testid="chat-busy">
          Se poate trimite după ce rularea curentă se termină.
        </p>
      ) : null}
      {error ? (
        <p className="warn" role="alert" data-testid="chat-error">
          {error}
        </p>
      ) : null}
    </section>
  );
}
