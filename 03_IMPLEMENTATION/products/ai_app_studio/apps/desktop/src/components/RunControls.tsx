import { useEffect, useState, type ReactNode } from "react";
import { TEMPLATES, type ProjectRef, type RunnerProfile, type Template } from "../lib/api";

const NEW = "__new__";

/**
 * The owner's prompt box: Start run (POST /api/v1/runs) on the chosen project (or a new one), Stop (POST
 * /runs/<id>/stop). Both are owner actions in the studio window; nothing an agent writes can press them.
 */
export function RunControls(props: {
  projects: ProjectRef[];
  running: boolean;
  busy: boolean;
  error: string;
  /** a new project (projectId "") with a template other than blank gets the template as third argument */
  onStart: (projectId: string, prompt: string, template?: Template) => void;
  onStop: () => void;
  /** owner setting (P5f): plan approval required before writes/builds; default ON */
  planGate?: boolean;
  onPlanGate?: (enabled: boolean) => void;
  /** owner setting (2026-11-23): "full" loads the owner's Claude setup into the builder; default "full" */
  profile?: RunnerProfile;
  onProfile?: (profile: RunnerProfile) => void;
  /** S9b: owner export buttons for the chosen (existing) project */
  renderExport?: (projectId: string) => ReactNode;
  /** the text the owner typed earlier for this project (kept by the App across project switches) */
  draft?: string;
  onDraft?: (text: string) => void;
}) {
  const { projects, running, busy } = props;
  const [prompt, setPromptState] = useState(props.draft ?? "");
  const setPrompt = (text: string) => {
    setPromptState(text);
    props.onDraft?.(text);
  };
  // "" = not chosen yet (defaults to the newest project), NEW = the owner picked "New project"
  const [project, setProject] = useState("");
  const [template, setTemplate] = useState<Template>("blank");
  useEffect(() => {
    if (project && project !== NEW && !projects.some((p) => p.id === project)) setProject("");
    else if (!project && projects[0]) setProject(projects[0].id);
  }, [projects, project]);
  const chosen = project === NEW ? "" : project;
  const canStart = !running && !busy && prompt.trim().length > 0;
  return (
    <section className="run-controls" data-testid="run-controls" aria-label="Start a run">
      <textarea
        data-testid="run-prompt"
        value={prompt}
        maxLength={20000}
        rows={3}
        placeholder="What should the AI build or change?"
        onChange={(e) => setPrompt(e.target.value)}
      />
      <div className="row">
        <select data-testid="run-project" value={project || NEW} onChange={(e) => setProject(e.target.value)}>
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name || p.id}
            </option>
          ))}
          <option value={NEW}>New project</option>
        </select>
        {chosen === "" ? (
          <select
            data-testid="run-template"
            aria-label="Template for the new project"
            value={template}
            onChange={(e) => setTemplate((TEMPLATES as readonly string[]).includes(e.target.value) ? (e.target.value as Template) : "blank")}
          >
            {TEMPLATES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        ) : null}
        <button className="btn primary" data-testid="run-start" disabled={!canStart} onClick={() =>
            chosen === "" && template !== "blank"
              ? props.onStart(chosen, prompt.trim(), template)
              : props.onStart(chosen, prompt.trim())
          }
        >
          Start run
        </button>
        <button className="btn" data-testid="run-stop" disabled={!running || busy} onClick={props.onStop}>
          Stop
        </button>
      </div>
      {chosen && props.renderExport ? props.renderExport(chosen) : null}
      {props.onPlanGate ? (
        <label className="plan-gate" data-testid="plan-gate-toggle" title="Owner setting: writes, deletes and builds wait for your approval of the AI's plan">
          <input
            type="checkbox"
            checked={props.planGate !== false}
            onChange={(e) => props.onPlanGate?.(e.target.checked)}
          />
          Require plan approval
        </label>
      ) : null}
      {props.onProfile ? (
        <div className="runner-profile" data-testid="runner-profile">
          <label title="Owner setting: which of your own Claude Code setup the AI builder loads">
            <input
              type="checkbox"
              data-testid="runner-profile-toggle"
              checked={props.profile !== "isolated"}
              disabled={running}
              onChange={(e) => props.onProfile?.(e.target.checked ? "full" : "isolated")}
            />
            Use my Claude setup
          </label>
          <p className="note" data-testid="runner-profile-note">
            {props.profile !== "isolated"
              ? "Full: loads your CLAUDE.md, memory, hooks, plugins, skills, agents and allowed MCP servers; no shell, web or file writes outside the studio."
              : "Isolated: the builder sees only the studio tools, none of your Claude setup."}
          </p>
        </div>
      ) : null}
      {props.error ? (
        <p className="warn" role="alert" data-testid="run-error">
          {props.error}
        </p>
      ) : null}
    </section>
  );
}
