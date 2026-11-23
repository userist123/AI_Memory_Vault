import { useEffect, useRef, useState } from "react";
import { countsLabel, type PolishState } from "../lib/qaApi";

/**
 * W2 Auto-polish (owner button): the server runs the quality gates, starts one chat turn asking the agent to fix the
 * listed issues (the plan gate applies: the owner approves the plan as usual), reruns the gates; at most two rounds.
 * Shows the step while running and the before/after counts when done.
 */
export function AutoPolishButton(props: {
  projectId: string;
  disabled?: boolean;
  pollMs?: number;
  api: { start: (pid: string) => Promise<PolishState>; status: (pid: string) => Promise<PolishState> };
}) {
  const { projectId, api } = props;
  const pollMs = props.pollMs ?? 2000;
  const [st, setSt] = useState<PolishState | null>(null);
  const [err, setErr] = useState("");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);
  useEffect(() => {
    setSt(null);
    setErr("");
  }, [projectId]);

  const poll = (pid: string) => {
    timer.current = setTimeout(() => {
      void api
        .status(pid)
        .then((s) => {
          setSt(s);
          if (s.state === "running") poll(pid);
        })
        .catch(() => setErr("Auto-polish status unavailable"));
    }, pollMs);
  };
  const start = () => {
    setErr("");
    void api
      .start(projectId)
      .then((s) => {
        setSt(s);
        if (s.state === "running") poll(projectId);
      })
      .catch((e: unknown) => setErr(e instanceof Error ? e.message : "Auto-polish failed"));
  };

  if (!projectId) return null;
  const running = st?.state === "running";
  return (
    <span className="auto-polish" data-testid="auto-polish">
      <button className="btn small" data-testid="auto-polish-start" disabled={running || props.disabled} onClick={start}
        title="Run the quality gates, let the agent fix the issues (you approve its plan), run them again - at most 2 rounds">
        {running ? `Polishing (${st.loop}/${st.maxLoops})…` : "Auto-polish"}
      </button>
      {st && running && st.step ? <span data-testid="auto-polish-step">{st.step}</span> : null}
      {st && !running && st.state !== "idle" ? (
        <span className={st.state === "done" && st.after?.total === 0 ? "ok" : "warn"} role="status" data-testid="auto-polish-result">
          {`Before: ${countsLabel(st.before)} → after: ${countsLabel(st.after)}${st.message ? ` (${st.message})` : ""}`}
        </span>
      ) : null}
      {err ? (
        <span className="warn" role="status" data-testid="auto-polish-error">
          {err}
        </span>
      ) : null}
    </span>
  );
}
