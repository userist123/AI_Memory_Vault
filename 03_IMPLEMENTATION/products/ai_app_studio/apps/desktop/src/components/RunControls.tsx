import { useEffect, useState } from "react";
import type { ProjectRef } from "../lib/api";

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
  onStart: (projectId: string, prompt: string) => void;
  onStop: () => void;
}) {
  const { projects, running, busy } = props;
  const [prompt, setPrompt] = useState("");
  // "" = not chosen yet (defaults to the newest project), NEW = the owner picked "New project"
  const [project, setProject] = useState("");
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
        <button className="btn primary" data-testid="run-start" disabled={!canStart} onClick={() => props.onStart(chosen, prompt.trim())}>
          Start run
        </button>
        <button className="btn" data-testid="run-stop" disabled={!running || busy} onClick={props.onStop}>
          Stop
        </button>
      </div>
      {props.error ? (
        <p className="warn" role="alert" data-testid="run-error">
          {props.error}
        </p>
      ) : null}
    </section>
  );
}
