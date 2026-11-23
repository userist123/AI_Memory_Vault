import { useState } from "react";
import type { BuildLine, State, Step } from "../lib/store";
import { CaptureStrip, type CaptureLoad } from "./Captures";

export function elapsed(from: number | null, to: number | null, now: number): string {
  if (from === null) return "";
  const ms = Math.max(0, (to ?? now) - from);
  if (ms < 1000) return `${ms} ms`;
  const s = Math.floor(ms / 1000);
  return s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${String(s % 60).padStart(2, "0")}s`;
}

function Pills({ added, removed }: { added: number; removed: number }) {
  if (!added && !removed) return null;
  return (
    <span className="pills">
      <span className="pill add">+{added}</span>
      <span className="pill del">-{removed}</span>
    </span>
  );
}

function StepRow(props: { step: Step; current: boolean; now: number; onOpen: (path: string) => void }) {
  const { step, current, now } = props;
  const [open, setOpen] = useState<boolean | null>(null);
  const expanded = open ?? current;
  const added = step.tools.reduce((n, t) => n + t.added, 0);
  const removed = step.tools.reduce((n, t) => n + t.removed, 0);
  const failed = step.tools.some((t) => t.ok === false);
  const live = step.tools.find((t) => t.endedAt === null);
  return (
    <li className={`step${current ? " current" : ""}${failed ? " failed" : ""}`} data-testid="step">
      <button className="step-head" onClick={() => setOpen(!expanded)} aria-expanded={expanded}>
        <span className="chev">{expanded ? "▾" : "▸"}</span>
        <span className="step-title">{step.title}</span>
        <Pills added={added} removed={removed} />
        <span className="time">{elapsed(step.startedAt, step.endedAt, now)}</span>
      </button>
      {current && live && (
        <div className="now-doing" data-testid="now-doing">
          <span className="pulse" aria-hidden="true" />
          <span className="tool">{live.tool}</span>
          <span className="target">{live.target}</span>
        </div>
      )}
      {expanded && (
        <ul className="tools">
          {step.tools.map((t) => (
            <li key={t.callId} className={t.ok === false ? "err" : ""} data-testid="tool-row">
              <span className="tool">{t.tool}</span>
              {t.target ? (
                <button className="target link-inert" onClick={() => props.onOpen(t.target)} title={t.target}>
                  {t.target}
                </button>
              ) : null}
              <Pills added={t.added} removed={t.removed} />
              {t.sha256 ? (
                <span className="sha" data-testid="tool-sha256" title={`sha256 ${t.sha256}`}>
                  sha256 {t.sha256.slice(0, 12)}…
                </span>
              ) : null}
              <span className="time">
                {t.ok === null ? "running " : t.ok ? "" : "failed "}
                {elapsed(t.startedAt, t.endedAt, now)}
              </span>
              {t.note ? <pre className="note">{t.note}</pre> : null}
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}

export function BuildLog(props: {
  lines: BuildLine[];
  errors: BuildLine[];
  status: string;
  open: boolean;
  setOpen: (v: boolean) => void;
  focused: boolean;
  onOpenLocation: (loc: string) => void;
}) {
  const { lines, errors, status } = props;
  if (!lines.length && !status) return null;
  return (
    <section className={`build${props.focused ? " focused" : ""}`} data-testid="build-log">
      <button className="step-head" onClick={() => props.setOpen(!props.open)} aria-expanded={props.open}>
        <span className="chev">{props.open ? "▾" : "▸"}</span>
        <span className="step-title">Build log</span>
        <span className="tag">{status || "…"}</span>
        <span className="time">{lines.length} lines</span>
      </button>
      {errors.length > 0 && (
        <ul className="build-errors" data-testid="build-errors">
          {errors.slice(-5).map((l) => (
            <li key={l.seq}>
              {l.location ? (
                <button className="loc link-inert" onClick={() => props.onOpenLocation(l.location)}>
                  {l.location}
                </button>
              ) : null}
              <span>{l.text}</span>
            </li>
          ))}
        </ul>
      )}
      {props.open && (
        <pre className="build-lines">
          {lines.map((l) => (
            <div key={l.seq} className={l.error ? "err" : ""}>
              {l.text}
            </div>
          ))}
        </pre>
      )}
    </section>
  );
}

export function Timeline(props: {
  state: State;
  now: number;
  buildOpen: boolean;
  setBuildOpen: (v: boolean) => void;
  buildFocused: boolean;
  onOpenFile: (path: string) => void;
  /** S8: capture thumbnails (null in demo mode / without a session: text placeholders) */
  captureLoad?: CaptureLoad | null;
}) {
  const { state, now } = props;
  const running = state.runStatus === "running";
  return (
    <div className="timeline" data-testid="timeline">
      {state.streamDropped > 0 && (
        <div className="warn" role="status" data-testid="stream-dropped">
          {state.streamDropped} agent stream item(s) dropped by the server allow-list (not shown here).
        </div>
      )}
      {state.steps.length === 0 && (
        <p className="empty">{state.runStatus === "idle" ? "No run yet. Start one from Claude Code or the runner." : "Starting…"}</p>
      )}
      <ol className="steps">
        {state.steps.map((s, i) => (
          <StepRow
            key={s.id + i}
            step={s}
            current={running && i === state.steps.length - 1}
            now={now}
            onOpen={props.onOpenFile}
          />
        ))}
      </ol>
      <CaptureStrip captures={state.captures} load={props.captureLoad ?? null} />
      {state.agentText ? (
        <div className="agent-text" data-testid="agent-text">
          <span className="eyebrow">Agent says</span>
          <p>{state.agentText}</p>
        </div>
      ) : null}
      <BuildLog
        lines={state.build.lines}
        errors={state.build.errors}
        status={state.build.status}
        open={props.buildOpen}
        setOpen={props.setBuildOpen}
        focused={props.buildFocused}
        onOpenLocation={(loc) => props.onOpenFile(loc.replace(/:\d+(?::\d+)?$/, ""))}
      />
      {state.qa && (
        <div className={`qa ${state.qa.failed ? "bad" : "good"}`} data-testid="qa">
          QA {state.qa.passed} passed, {state.qa.failed} failed{state.qa.summary ? ` — ${state.qa.summary}` : ""}
        </div>
      )}
    </div>
  );
}
